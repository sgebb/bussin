/**
 * Smoke tests for bussin-mcp.
 *
 * Drives the built server over stdio exactly as an MCP client does, against the
 * in-process simulator, so no Azure namespace or sign-in is needed.
 *
 *   npm test
 */

import { spawn } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import path from 'node:path';

const ROOT = path.dirname(path.dirname(fileURLToPath(import.meta.url)));
const RPC_TIMEOUT_MS = 25_000;

let failures = 0;
function check(label, cond, detail = '') {
    console.log(`${cond ? 'PASS' : 'FAIL'}  ${label}${detail ? '\n        ' + detail : ''}`);
    if (!cond) failures++;
}

/** Start a server and return an RPC client bound to it. */
function startServer(args) {
    const proc = spawn(process.execPath, ['dist/index.js', ...args], {
        cwd: ROOT, stdio: ['pipe', 'pipe', 'pipe']
    });

    let buf = '', stderr = '', nonJson = [];
    const waiters = new Map();

    proc.stdout.on('data', chunk => {
        buf += chunk;
        const lines = buf.split('\n');
        buf = lines.pop() ?? '';
        for (const line of lines) {
            if (!line.trim()) continue;
            let msg;
            try { msg = JSON.parse(line); } catch { nonJson.push(line); continue; }
            const resolve = waiters.get(msg.id);
            if (resolve) { waiters.delete(msg.id); resolve(msg); }
        }
    });
    proc.stderr.on('data', d => { stderr += d; });

    let id = 0;
    const rpc = (method, params) => new Promise((resolve, reject) => {
        const myId = ++id;
        waiters.set(myId, resolve);
        setTimeout(() => {
            if (waiters.delete(myId)) reject(new Error(`timeout waiting for ${method}`));
        }, RPC_TIMEOUT_MS);
        proc.stdin.write(JSON.stringify({ jsonrpc: '2.0', id: myId, method, params }) + '\n');
    });

    return {
        rpc,
        get stderr() { return stderr; },
        get nonJson() { return nonJson; },
        async handshake() {
            await rpc('initialize', {
                protocolVersion: '2024-11-05', capabilities: {}, clientInfo: { name: 'smoke', version: '1' }
            });
            proc.stdin.write(JSON.stringify({ jsonrpc: '2.0', method: 'notifications/initialized' }) + '\n');
        },
        async call(name, args = {}) {
            const r = await rpc('tools/call', { name, arguments: args });
            return {
                isError: !!r.result?.isError,
                notFound: !!r.error,
                text: r.result?.content?.[0]?.text ?? JSON.stringify(r.error ?? r)
            };
        },
        async toolNames() {
            const r = await rpc('tools/list', {});
            return (r.result?.tools ?? []).map(t => t.name).sort();
        },
        stop() { proc.kill(); }
    };
}

// --- Capability gating ------------------------------------------------------
console.log('\n=== capability gating ===');
for (const [label, args, wantSend, wantDestructive] of [
    ['read-only (default)', ['--demo'], false, false],
    ['--allow-send', ['--demo', '--allow-send'], true, false],
    ['--allow-send --allow-destructive', ['--demo', '--allow-send', '--allow-destructive'], true, true]
]) {
    const s = startServer(args);
    await s.handshake();
    const names = await s.toolNames();
    const has = n => names.includes(n);

    check(`${label}: stdout is pure JSON-RPC`, s.nonJson.length === 0,
        s.nonJson[0]?.slice(0, 100) ?? '');
    check(`${label}: read tools present`,
        ['list_namespaces', 'list_queues', 'list_topics', 'list_subscriptions',
            'peek_messages', 'search_messages'].every(has), names.join(', '));
    check(`${label}: send tools ${wantSend ? 'present' : 'absent'}`,
        (has('send_message') && has('resubmit_dead_letter')) === wantSend);
    check(`${label}: destructive tools ${wantDestructive ? 'present' : 'absent'}`,
        (has('purge_entity') && has('delete_messages') && has('dead_letter_messages')) === wantDestructive);
    check(`${label}: banner on stderr`, /bussin-mcp ready/.test(s.stderr),
        s.stderr.trim().split('\n').pop() ?? '');
    s.stop();
}

// --- Tool behaviour against the simulator -----------------------------------
console.log('\n=== tool behaviour (simulator) ===');
{
    const s = startServer(['--demo', '--allow-send', '--allow-destructive']);
    await s.handshake();

    // The server instructions tell a model to call list_queues first, so demo mode has to
    // answer it from the simulator rather than erroring on the documented first step.
    let r = await s.call('list_queues');
    const queues = r.isError ? null : JSON.parse(r.text);
    check('list_queues works in demo mode', !r.isError && queues.length === 3,
        queues ? queues.map(q => `${q.name}:${q.totalMessageCount}`).join(', ') : r.text.slice(0, 90));
    check('list_queues reports live simulator counts',
        !!queues && queues.find(q => q.name === 'orders')?.counts.activeMessageCount === 2);

    r = await s.call('list_namespaces');
    const nsResult = r.isError ? null : JSON.parse(r.text);
    check('list_namespaces works in demo mode',
        !!nsResult && nsResult.namespaces.some(n => n.name === 'bussin-demo-prod'), r.text.slice(0, 90));

    // nameFilter keeps large tenants from flooding the caller's context.
    r = await s.call('list_namespaces', { nameFilter: '*-dev' });
    const filtered = r.isError ? null : JSON.parse(r.text);
    check('nameFilter glob narrows the result',
        !!filtered && filtered.matched === 1 && filtered.namespaces[0].name === 'bussin-demo-dev',
        filtered ? filtered.namespaces.map(n => n.name).join(', ') : r.text.slice(0, 90));

    r = await s.call('list_namespaces', { nameFilter: 'demo' });
    check('nameFilter substring matches both',
        !r.isError && JSON.parse(r.text).matched === 2);

    r = await s.call('list_namespaces', { nameFilter: 'nothing-like-this' });
    const empty = r.isError ? null : JSON.parse(r.text);
    check('nameFilter with no match explains itself',
        !!empty && empty.matched === 0 && /Call again without nameFilter/.test(empty.hint),
        empty?.hint ?? r.text.slice(0, 90));

    r = await s.call('peek_messages', { queue: 'orders', count: 10 });
    const peek = JSON.parse(r.text);
    check('peek_messages returns the seeded messages', peek.returned === 2,
        `ids=${peek.messages.map(m => m.messageId).join(',')}`);
    check('peeked message carries body and sequenceNumber',
        !!peek.messages[0].body && peek.messages[0].sequenceNumber !== undefined);

    r = await s.call('search_messages', { queue: 'orders', bodyFilter: 'Alice', maxMessages: 100 });
    const search = JSON.parse(r.text);
    check('search_messages matches on body substring', search.matched === 1,
        `scanned=${search.scanned} id=${search.messages?.[0]?.messageId}`);

    r = await s.call('search_messages', { queue: 'orders' });
    check('search without any filter is rejected', r.isError && /at least one of/i.test(r.text));

    r = await s.call('send_message', { queue: 'orders', body: '{"orderId":9999}', messageId: 'sent-1' });
    check('send_message succeeds', !r.isError && JSON.parse(r.text).sent === true);

    r = await s.call('peek_messages', { queue: 'orders', count: 20 });
    check('the sent message appears on the queue', JSON.parse(r.text).returned === 3);

    r = await s.call('purge_entity', { queue: 'orders', confirm: 'wrong-name' });
    check('purge refuses a mismatched confirm', r.isError && /Refusing to purge/.test(r.text));

    r = await s.call('peek_messages', { queue: 'orders', count: 20 });
    check('queue is untouched after a refused purge', JSON.parse(r.text).returned === 3);

    r = await s.call('purge_entity', { queue: 'notifications', confirm: 'notifications' });
    check('purge with the correct confirm runs', !r.isError && JSON.parse(r.text).deletedCount === 2);

    s.stop();
}

// --- Guards on a server without --allow-destructive -------------------------
console.log('\n=== destructive guards ===');
{
    const s = startServer(['--demo', '--allow-send']);
    await s.handshake();

    let r = await s.call('resubmit_dead_letter',
        { queue: 'orders', sequenceNumbers: [1], removeOriginals: true });
    check('resubmit(removeOriginals) is blocked without --allow-destructive',
        r.isError && /allow-destructive/.test(r.text));

    // An unregistered tool comes back as an isError result, not a JSON-RPC error.
    r = await s.call('purge_entity', { queue: 'orders', confirm: 'orders' });
    check('purge_entity is not even exposed without --allow-destructive',
        (r.isError || r.notFound) && /not found/i.test(r.text), r.text.slice(0, 100));

    s.stop();
}

console.log(`\n${failures === 0 ? '*** ALL CHECKS PASSED ***' : `*** ${failures} CHECK(S) FAILED ***`}`);
process.exit(failures === 0 ? 0 : 1);
