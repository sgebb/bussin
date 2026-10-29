#!/usr/bin/env node
// One command: start the Service Bus emulator, probe what Bussin needs from it, write report.md.
//
//   node tools/emulator-probe/run.mjs --accept-eula   start containers (you accept the emulator + SQL Edge EULAs) and probe
//   node tools/emulator-probe/run.mjs --no-start      probe an emulator that's already running
//   node tools/emulator-probe/run.mjs --down          stop and remove the probe containers
//   options: --tag <emulator image tag> (default latest)
import { execFileSync, spawnSync } from 'node:child_process';
import crypto from 'node:crypto';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { startBridge } from './bridge.mjs';
import { Skip, amqpChecks, managementChecks, sasToken, websocketEndpointChecks } from './probes.mjs';

const here = path.dirname(fileURLToPath(import.meta.url));
const clientJs = path.resolve(here, '../../client-js');
const args = process.argv.slice(2);
const flag = (name) => args.includes(name);
const option = (name) => { const i = args.indexOf(name); return i >= 0 ? args[i + 1] : undefined; };
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

const engine = ['docker', 'podman'].find((cmd) => spawnSync(cmd, ['--version'], { stdio: 'ignore', shell: true }).status === 0);
const compose = (...composeArgs) => {
    if (!engine) throw new Error('Neither docker nor podman is on PATH.');
    const env = { ...process.env, EMULATOR_TAG: option('--tag') ?? 'latest' };
    if (flag('--accept-eula')) env.ACCEPT_EULA = 'Y';
    const r = spawnSync(engine, ['compose', '-f', path.join(here, 'docker-compose.yml'), ...composeArgs], { stdio: 'inherit', env, cwd: here, shell: true });
    if (r.status !== 0) throw new Error(`${engine} compose ${composeArgs.join(' ')} failed (exit ${r.status})`);
};

function ensureSqlPassword() {
    const envFile = path.join(here, '.env');
    if (fs.existsSync(envFile) && /SQL_PASSWORD=/.test(fs.readFileSync(envFile, 'utf8'))) return;
    // SQL Edge requires upper, lower, digit and symbol.
    const password = `Pb1!${crypto.randomBytes(12).toString('base64url')}`;
    fs.appendFileSync(envFile, `SQL_PASSWORD=${password}\n`);
}

async function waitFor(what, test, timeoutMs) {
    const start = Date.now();
    while (Date.now() - start < timeoutMs) {
        try { if (await test()) return; } catch { /* not ready yet */ }
        await sleep(2000);
    }
    throw new Error(`Timed out after ${timeoutMs / 1000}s waiting for ${what}. Check: ${engine} logs bussin-probe-servicebus-emulator`);
}

async function runGroup(title, checks, timeoutMs = 30_000) {
    const rows = [];
    for (const [name, run] of checks) {
        let status, detail;
        try {
            detail = await Promise.race([run(), sleep(timeoutMs).then(() => { throw new Error(`timed out after ${timeoutMs / 1000}s`); })]);
            status = 'PASS';
        } catch (err) {
            status = err instanceof Skip ? 'SKIP' : 'FAIL';
            detail = err.message;
        }
        rows.push({ name, status, detail: String(detail ?? '').replace(/\s+/g, ' ').replace(/\|/g, '\\|') });
        process.stdout.write(`  ${status.padEnd(4)} ${name}${status === 'PASS' ? '' : `: ${rows.at(-1).detail}`}\n`);
    }
    return { title, rows };
}

async function main() {
    if (flag('--down')) { compose('down', '--volumes'); return; }

    if (!flag('--no-start')) {
        if (!flag('--accept-eula')) {
            console.error('Starting the emulator means accepting the Service Bus emulator and Azure SQL Edge license terms:\n' +
                '  https://github.com/Azure/azure-service-bus-emulator-installer/blob/main/EMULATOR_EULA.txt\n' +
                '  https://go.microsoft.com/fwlink/?linkid=2139274\n' +
                'Re-run with --accept-eula if you accept them, or with --no-start against an emulator you already run.');
            process.exit(2);
        }
        if (!engine) throw new Error('Neither docker nor podman is on PATH.');
        ensureSqlPassword();
        compose('up', '-d');
    }

    console.log('Building the client-js Node bundle (Bussin\'s own AMQP code)...');
    execFileSync('npm', ['run', 'build:node', '--silent'], { cwd: clientJs, stdio: 'ignore', shell: true });
    const api = await import(pathToFileURL(path.join(clientJs, 'dist/node/serviceBusApi.js')).href);

    console.log('Waiting for the emulator (SQL Edge first start can take a minute)...');
    await waitFor('health endpoint on :5300', async () => (await fetch('http://localhost:5300/health')).ok, flag('--no-start') ? 10_000 : 240_000);

    const bridge = await startBridge({ port: 15673 });
    const runId = Date.now().toString(36);

    // Bussin's client logs every step; keep that out of the report but on disk for debugging.
    const debugLog = fs.createWriteStream(path.join(here, 'probe-debug.log'));
    const realLog = console.log;
    console.log = (...m) => debugLog.write(`${m.map(String).join(' ')}\n`);
    const out = (s) => process.stdout.write(`${s}\n`);

    const groups = [];
    try {
        out('\nManagement HTTP API (:5300)');
        groups.push(await runGroup('Management HTTP API (port 5300)', managementChecks));

        out('\nBussin client over WebSocket (local bridge -> :5672)');
        // Health can report OK before entities from Config.json are provisioned.
        await waitFor('first AMQP operation', async () => { await api.peekQueueMessages(bridge.url, 'queue.1', sasToken('queue.1'), 1); return true; }, 60_000)
            .catch((err) => out(`  (warning) ${err.message}`));
        groups.push(await runGroup('Bussin client over WebSocket (local bridge to the AMQP port)', amqpChecks(api, bridge.url, runId)));

        out('\nEmulator WebSocket listener (:5673, PR #154)');
        const wsGroup = await runGroup('Emulator WebSocket listener (port 5673, PR #154)', websocketEndpointChecks(5673));
        groups.push(wsGroup);
        if (wsGroup.rows.some((r) => r.status === 'PASS')) {
            out('\nBussin client over the emulator\'s own WebSocket listener (:5673)');
            groups.push(await runGroup('Bussin client over the emulator\'s own WebSocket listener', amqpChecks(api, 'ws://localhost:5673/$servicebus/websocket', `${runId}d`)));
        }
    } finally {
        console.log = realLog;
        debugLog.end();
        bridge.close();
    }

    const image = (() => {
        try { return execFileSync(engine, ['inspect', '--format', '{{.Config.Image}} {{.Image}}', 'bussin-probe-servicebus-emulator'], { encoding: 'utf8', shell: true }).trim(); }
        catch { return '(unknown: not started by this script)'; }
    })();
    const origins = [...new Set(bridge.upgrades.map((u) => `${u.origin ?? '(none)'} / ${u.protocol}`))].join('; ');

    const report = [
        '# Service Bus emulator probe for Bussin',
        '',
        `- Run: ${new Date().toISOString()}`,
        `- Emulator container: ${image}`,
        `- Bridge saw upgrades with Origin / subprotocol: ${origins || '(none)'}`,
        '',
        ...groups.flatMap((g) => [
            `## ${g.title}`,
            '',
            '| Check | Result | Detail |',
            '| --- | --- | --- |',
            ...g.rows.map((r) => `| ${r.name} | ${r.status} | ${r.detail} |`),
            ''
        ])
    ].join('\n');
    fs.writeFileSync(path.join(here, 'report.md'), report);

    const all = groups.flatMap((g) => g.rows);
    const count = (s) => all.filter((r) => r.status === s).length;
    out(`\n${count('PASS')} passed, ${count('FAIL')} failed, ${count('SKIP')} skipped. Report: ${path.join(here, 'report.md')}`);
    out(`Stop the emulator with: node ${path.relative(process.cwd(), fileURLToPath(import.meta.url))} --down`);
    process.exit(0);
}

main().catch((err) => { console.error(err.message); process.exit(1); });
