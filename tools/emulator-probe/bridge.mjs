// Stand-in for the emulator's upcoming AMQP-over-WebSocket listener (installer PR #154):
// accepts ws://localhost:<port>/$servicebus/websocket with the AMQPWSB10 subprotocol and
// pipes the bytes to the emulator's plain AMQP TCP port. Lets Bussin's real client code run
// against today's emulator image.
import { createRequire } from 'node:module';
import net from 'node:net';

const require = createRequire(new URL('../../client-js/package.json', import.meta.url));
const { WebSocketServer } = require('ws');

export function startBridge({ port = 15673, amqpHost = 'localhost', amqpPort = 5672, log = () => {} } = {}) {
    const upgrades = [];
    const wss = new WebSocketServer({
        port,
        handleProtocols: (protocols) => (protocols.has('AMQPWSB10') ? 'AMQPWSB10' : false)
    });

    wss.on('connection', (ws, req) => {
        upgrades.push({ path: req.url, origin: req.headers.origin ?? null, protocol: ws.protocol });
        const tcp = net.connect(amqpPort, amqpHost);
        tcp.on('data', (chunk) => ws.readyState === ws.OPEN && ws.send(chunk));
        tcp.on('close', () => ws.close());
        tcp.on('error', (err) => { log(`bridge tcp error: ${err.message}`); ws.close(); });
        ws.on('message', (data) => tcp.write(data));
        ws.on('close', () => tcp.end());
    });

    return new Promise((resolve, reject) => {
        wss.once('listening', () => resolve({ url: `ws://localhost:${port}/$servicebus/websocket`, upgrades, close: () => wss.close() }));
        wss.once('error', reject);
    });
}
