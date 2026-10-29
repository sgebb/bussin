// Probes for what Bussin needs from the Service Bus emulator. Each check returns a detail
// string on success and throws on failure; `skip(reason)` marks it as not applicable.
import crypto from 'node:crypto';
import http from 'node:http';

const HOST = 'localhost';
const KEY_NAME = 'RootManageSharedAccessKey';
const KEY = 'SAS_KEY_VALUE'; // the emulator's fixed key
const MGMT = `http://${HOST}:5300`;
const API = 'api-version=2021-05';
const BROWSER_ORIGIN = 'https://app.bussin.dev';

export class Skip extends Error {}
const skip = (reason) => { throw new Skip(reason); };
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

/** Same scheme as Bussin's ServiceBusConnectionStringHelper.GenerateSasToken. */
export function sasToken(entityPath, key = KEY) {
    const resource = `sb://${HOST}/${entityPath.replace(/^\//, '')}`;
    const expiry = Math.floor(Date.now() / 1000) + 3600;
    const toSign = `${encodeURIComponent(resource)}\n${expiry}`;
    const sig = crypto.createHmac('sha256', key).update(toSign, 'utf8').digest('base64');
    return `SharedAccessSignature sr=${encodeURIComponent(resource)}&sig=${encodeURIComponent(sig)}&se=${expiry}&skn=${KEY_NAME}`;
}

const corsHeaders = (res) => ['access-control-allow-origin', 'access-control-allow-headers', 'access-control-allow-methods']
    .map((h) => `${h}=${res.headers.get(h) ?? '(none)'}`).join(', ');

const titles = (xml) => [...xml.matchAll(/<title[^>]*>([^<]+)<\/title>/g)].map((m) => m[1]).filter((t) => !/^(Queues|Topics|Subscriptions)$/i.test(t));

async function mgmtGet(path, { auth = true, origin } = {}) {
    const headers = {};
    if (auth) headers.Authorization = sasToken('');
    if (origin) headers.Origin = origin;
    const res = await fetch(`${MGMT}/${path}${path.includes('?') ? '&' : '?'}${API}`, { headers });
    return { res, body: await res.text() };
}

/** Management HTTP API on 5300: can a browser list entities and counts? */
export const managementChecks = [
    ['Health endpoint responds', async () => {
        const res = await fetch(`${MGMT}/health`);
        if (!res.ok) throw new Error(`HTTP ${res.status}`);
        return `HTTP ${res.status}`;
    }],
    ['List queues (ATOM API, SAS auth)', async () => {
        const { res, body } = await mgmtGet('$Resources/queues');
        if (!res.ok) throw new Error(`HTTP ${res.status}: ${body.slice(0, 200)}`);
        return `queues: ${titles(body).join(', ') || '(none parsed)'}`;
    }],
    ['List topics (ATOM API)', async () => {
        const { res, body } = await mgmtGet('$Resources/topics');
        if (!res.ok) throw new Error(`HTTP ${res.status}: ${body.slice(0, 200)}`);
        return `topics: ${titles(body).join(', ') || '(none parsed)'}`;
    }],
    ['List subscriptions of topic.1', async () => {
        const { res, body } = await mgmtGet('topic.1/Subscriptions');
        if (!res.ok) throw new Error(`HTTP ${res.status}: ${body.slice(0, 200)}`);
        return `subscriptions: ${titles(body).join(', ') || '(none parsed)'}`;
    }],
    ['Queue runtime counts (queue.1)', async () => {
        const { res, body } = await mgmtGet('queue.1?enrich=false');
        if (!res.ok) throw new Error(`HTTP ${res.status}: ${body.slice(0, 200)}`);
        const count = (name) => body.match(new RegExp(`${name}>(\\d+)<`))?.[1] ?? '?';
        return `active=${count('ActiveMessageCount')}, deadletter=${count('DeadLetterMessageCount')}, scheduled=${count('ScheduledMessageCount')}`;
    }],
    ['Listing works without Authorization header', async () => {
        const { res } = await mgmtGet('$Resources/queues', { auth: false });
        if (!res.ok) throw new Error(`HTTP ${res.status} (auth required, so browsers need a CORS preflight)`);
        return `HTTP ${res.status} (no auth needed: a browser could do a simple GET)`;
    }],
    [`CORS on GET with Origin ${BROWSER_ORIGIN}`, async () => {
        const { res } = await mgmtGet('$Resources/queues', { origin: BROWSER_ORIGIN });
        const acao = res.headers.get('access-control-allow-origin');
        if (!acao) throw new Error(`no Access-Control-Allow-Origin (HTTP ${res.status}): browser JS can't read the response`);
        return corsHeaders(res);
    }],
    ['CORS preflight (OPTIONS, Authorization header)', async () => {
        const res = await fetch(`${MGMT}/$Resources/queues?${API}`, {
            method: 'OPTIONS',
            headers: { Origin: BROWSER_ORIGIN, 'Access-Control-Request-Method': 'GET', 'Access-Control-Request-Headers': 'authorization' }
        });
        const ok = res.ok && res.headers.get('access-control-allow-origin') && /authorization/i.test(res.headers.get('access-control-allow-headers') ?? '');
        if (!ok) throw new Error(`HTTP ${res.status}; ${corsHeaders(res)}`);
        return `HTTP ${res.status}; ${corsHeaders(res)}`;
    }]
];

/**
 * Bussin's own client code (client-js Node build) over a WebSocket endpoint.
 * `ns` is the ws:// URL, exactly what Bussin passes for a custom WebSocket endpoint.
 */
export function amqpChecks(api, ns, runId) {
    const q = 'queue.1';
    const tq = sasToken(q);
    const id = (n) => `probe-${runId}-${n}`;
    const peekAll = (opts = {}) => api.peekQueueMessages(ns, q, tq, 100, 0, !!opts.dlq);
    const find = (msgs, n) => msgs.find((m) => m.messageId === id(n));
    const state = {};

    return [
        ['CBS put-token (SAS) + purge queue and DLQ (receive-and-delete)', async () => {
            const main = await api.purgeQueueDirect(ns, q, tq, false);
            const dlq = await api.purgeQueueDirect(ns, q, tq, true);
            return `deleted ${main.deletedCount} active, ${dlq.deletedCount} dead-lettered left over from earlier runs`;
        }],
        ['Send to queue', async () => {
            await api.sendQueueMessage(ns, q, tq, { probe: 1 }, { message_id: id(1) });
            return 'sent';
        }],
        ['Peek ($management peek-message from-sequence-number)', async () => {
            const m = find(await peekAll(), 1);
            if (!m) throw new Error('sent message not found by peek');
            state.seq1 = m.sequenceNumber;
            return `seq=${m.sequenceNumber}, body=${m.body}`;
        }],
        ['Peek from a later sequence number skips earlier ones', async () => {
            await api.sendQueueMessage(ns, q, tq, 'second', { message_id: id(2) });
            const msgs = await api.peekQueueMessages(ns, q, tq, 10, state.seq1 + 1);
            if (find(msgs, 1)) throw new Error('from-sequence-number ignored');
            if (!find(msgs, 2)) throw new Error('later message missing');
            return 'ok';
        }],
        ['Peek by sequence number list', async () => {
            const msgs = await api.peekQueueMessagesBySequence(ns, q, tq, [state.seq1]);
            if (!find(msgs, 1)) throw new Error(`got ${msgs.length} messages`);
            return 'ok';
        }],
        ['Receive (peek-lock) + abandon', async () => {
            const [locked] = await api.receiveAndLockQueueMessage(ns, q, tq, 5, false, 1);
            if (!locked) throw new Error('nothing received');
            const r = await api.abandon([locked.lockToken]);
            if (r.failureCount) throw new Error(JSON.stringify(r.errors));
            const after = find(await peekAll(), 1);
            return `deliveryCount after abandon=${after?.deliveryCount}`;
        }],
        ['Receive + dead-letter with reason, then peek DLQ', async () => {
            const [locked] = await api.receiveAndLockQueueMessage(ns, q, tq, 5, false, 1);
            const r = await api.deadLetter([locked.lockToken], { deadLetterReason: 'probe', deadLetterErrorDescription: 'probe run' });
            if (r.failureCount) throw new Error(JSON.stringify(r.errors));
            const d = find(await peekAll({ dlq: true }), 1);
            if (!d) throw new Error('not found in $DeadLetterQueue');
            return `DeadLetterReason=${d.applicationProperties?.DeadLetterReason}`;
        }],
        ['Receive + complete', async () => {
            const [locked] = await api.receiveAndLockQueueMessage(ns, q, tq, 5, false, 1);
            const r = await api.complete([locked.lockToken]);
            if (r.failureCount) throw new Error(JSON.stringify(r.errors));
            if ((await peekAll()).some((m) => m.messageId === locked.messageId)) throw new Error('still in queue after complete');
            return `completed ${locked.messageId}`;
        }],
        ['Dead-letter by sequence number (defer + receive-by-sequence)', async () => {
            await api.sendQueueMessage(ns, q, tq, 'to dlq', { message_id: id(3) });
            const m = find(await peekAll(), 3);
            await api.deadLetterQueueMessagesBySequence(ns, q, tq, [m.sequenceNumber], 'probe', 'by sequence');
            if (!find(await peekAll({ dlq: true }), 3)) throw new Error('not in DLQ');
            return 'ok';
        }],
        ['Delete by sequence number', async () => {
            await api.sendQueueMessage(ns, q, tq, 'to delete', { message_id: id(4) });
            const m = find(await peekAll(), 4);
            await api.deleteQueueMessagesBySequence(ns, q, tq, [m.sequenceNumber]);
            if (find(await peekAll(), 4)) throw new Error('still there');
            return 'ok';
        }],
        ['Schedule message, peek shows it, cancel it', async () => {
            const at = new Date(Date.now() + 3600_000);
            await api.sendQueueMessage(ns, q, tq, 'later', { message_id: id(5), message_annotations: { 'x-opt-scheduled-enqueue-time': at } });
            const m = find(await peekAll(), 5);
            if (!m) throw new Error('scheduled message not visible to peek');
            await api.cancelScheduledQueueMessages(ns, q, tq, [m.sequenceNumber]);
            if (find(await peekAll(), 5)) throw new Error('still there after cancel');
            return `state=${m.state} (2 = scheduled)`;
        }],
        ['Topic fan-out honours correlation rule', async () => {
            const t = 'topic.1';
            await api.sendTopicMessage(ns, t, sasToken(t), { json: true }, { message_id: id(6) });
            await api.sendTopicMessage(ns, t, sasToken(t), 'plain text', { message_id: id(7), content_type: 'text/plain' });
            await sleep(500);
            const sub = (s) => api.peekSubscriptionMessages(ns, t, s, sasToken(`${t}/subscriptions/${s}`), 100, 0);
            const json = await sub('subscription.json');
            const all = await sub('subscription.all');
            if (!find(json, 6) || find(json, 7)) throw new Error(`subscription.json has: ${json.map((m) => m.messageId).join(', ')}`);
            if (!find(all, 6) || !find(all, 7)) throw new Error(`subscription.all has: ${all.map((m) => m.messageId).join(', ')}`);
            return 'json-only rule filtered correctly';
        }],
        ['Enumerate rules ($management enumerate-rules)', async () => {
            const rules = await api.enumerateRules(ns, 'topic.1', 'subscription.json', sasToken('topic.1/subscriptions/subscription.json'));
            return rules.map((r) => `${r.name}:${r.filterType}`).join(', ') || '(no rules)';
        }],
        ['Add + remove SQL rule', async () => {
            const tok = sasToken('topic.1/subscriptions/subscription.all');
            await api.addRule(ns, 'topic.1', 'subscription.all', tok, 'probe-sql', 'Sql', '1=1');
            const rules = await api.enumerateRules(ns, 'topic.1', 'subscription.all', tok);
            await api.removeRule(ns, 'topic.1', 'subscription.all', tok, 'probe-sql');
            if (!rules.some((r) => r.name === 'probe-sql')) throw new Error('added rule not listed');
            return 'ok';
        }],
        ['Sessions: list sessions, peek and receive by session', async () => {
            const sq = 'queue.sessions';
            const ts = sasToken(sq);
            await api.sendQueueMessage(ns, sq, ts, 'session msg', { message_id: id(8), session_id: `s-${runId}` });
            const sessions = await api.getMessageSessions(ns, sq, ts);
            const peeked = await api.peekQueueMessages(ns, sq, ts, 10, 0, false, `s-${runId}`);
            const [locked] = await api.receiveAndLockQueueMessage(ns, sq, ts, 5, false, 1, `s-${runId}`);
            if (locked) await api.complete([locked.lockToken]);
            if (!find(peeked, 8)) throw new Error(`peek by session found nothing; sessions=${sessions.join(', ')}`);
            return `sessions listed: ${sessions.length}, received: ${!!locked}`;
        }],
        ['Watch (monitorEntity) sees a new message', async () => {
            const seen = [];
            const monitor = await api.monitorEntity(ns, q, tq, (m) => seen.push(m), () => {}, async () => tq);
            try {
                await api.sendQueueMessage(ns, q, tq, 'watched', { message_id: id(9) });
                await sleep(2500);
            } finally {
                monitor.stop();
            }
            if (!find(seen, 9)) throw new Error(`saw ${seen.length} messages, not the new one`);
            return `saw ${seen.length} new message(s)`;
        }],
        ['(info) Token signed with the wrong key', async () => {
            try {
                await api.peekQueueMessages(ns, q, sasToken(q, 'wrong-key'), 1, 0);
            } catch (err) {
                return `rejected, so SAS is validated: ${err.message.slice(0, 120)}`;
            }
            return 'accepted, so SAS is not validated (fine for a local emulator)';
        }]
    ];
}

/** Raw WebSocket upgrade against the emulator's own listener (PR #154): is it there, and which Origins are accepted? */
export function upgrade(port, origin) {
    return new Promise((resolve) => {
        const headers = {
            Connection: 'Upgrade',
            Upgrade: 'websocket',
            'Sec-WebSocket-Version': '13',
            'Sec-WebSocket-Key': crypto.randomBytes(16).toString('base64'),
            'Sec-WebSocket-Protocol': 'AMQPWSB10'
        };
        if (origin) headers.Origin = origin;
        const req = http.request({ host: HOST, port, path: '/$servicebus/websocket', headers, timeout: 5000 });
        req.on('upgrade', (res, socket) => { socket.destroy(); resolve({ status: 101, protocol: res.headers['sec-websocket-protocol'] }); });
        req.on('response', (res) => { res.resume(); resolve({ status: res.statusCode }); });
        req.on('timeout', () => { req.destroy(); resolve({ error: 'timeout' }); });
        req.on('error', (err) => resolve({ error: err.code ?? err.message }));
        req.end();
    });
}

export function websocketEndpointChecks(port) {
    const check = (origin) => async () => {
        const r = await upgrade(port, origin);
        if (r.error === 'ECONNREFUSED') skip(`nothing listening on ${port} (image without PR #154's listener)`);
        if (r.error) throw new Error(r.error);
        if (r.status !== 101) throw new Error(`HTTP ${r.status} instead of 101 Switching Protocols`);
        if (r.protocol !== 'AMQPWSB10') throw new Error(`upgraded, but subprotocol=${r.protocol}`);
        return '101, AMQPWSB10';
    };
    return [
        ['Upgrade without Origin (Node/.NET clients)', check(undefined)],
        [`Upgrade with Origin ${BROWSER_ORIGIN} (Bussin in a browser)`, check(BROWSER_ORIGIN)],
        ['Upgrade with Origin http://localhost:63080 (Bussin dev server)', check('http://localhost:63080')],
        ['Upgrade with an unrelated Origin', check('https://example.com')]
    ];
}
