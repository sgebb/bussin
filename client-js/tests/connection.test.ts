import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { EventEmitter } from 'events';

// A stand-in for rhea's connection, driven by each test
class FakeLink extends EventEmitter {
    closed = false;
    close() { this.closed = true; }
    send() {}
}

class FakeConnection extends EventEmitter {
    links: FakeLink[] = [];
    closed = false;
    open_sender() { const l = new FakeLink(); this.links.push(l); return l; }
    open_receiver() { const l = new FakeLink(); this.links.push(l); return l; }
    close() { this.closed = true; }
}

let current: FakeConnection;

vi.mock('rhea', () => {
    const rhea = {
        connect: () => (current = new FakeConnection()),
        websocket_connect: () => () => ({})
    };
    return { ...rhea, default: rhea };
});

const { ServiceBusConnection } = await import('../src/connection.js');

describe('ServiceBusConnection', () => {
    beforeEach(() => {
        (globalThis as any).__BUSSIN_SIMULATOR_ACTIVE__ = false;
        (globalThis as any).WebSocket = class {};
    });

    afterEach(() => {
        vi.useRealTimers();
    });

    it('rejects when the connection drops before it opens, also on a reconnect', async () => {
        const conn = new ServiceBusConnection('ns', 'token');

        const first = conn.connect();
        current.emit('connection_open', {});
        await first;

        const auth = conn.authenticateCBS('queue');
        const [, cbsReceiver] = current.links;
        cbsReceiver.emit('message', { message: { application_properties: { 'status-code': 200 } } });
        await auth;

        // Reconnecting after a drop used to hang forever once the first connection had authenticated
        const second = conn.connect();
        current.emit('disconnected', { error: { condition: 'amqp:connection:forced', description: 'gone' } });
        await expect(second).rejects.toThrow('amqp:connection:forced: gone');
        expect(current.closed).toBe(true);
    });

    it('times out a connection that never opens', async () => {
        vi.useFakeTimers();
        const conn = new ServiceBusConnection('ns', 'token');

        const pending = conn.connect();
        const assertion = expect(pending).rejects.toThrow('timed out');
        await vi.advanceTimersByTimeAsync(15000);
        await assertion;
        expect(current.closed).toBe(true);
    });

    it('closes the $cbs links when authentication times out', async () => {
        vi.useFakeTimers();
        const conn = new ServiceBusConnection('ns', 'token');
        const connected = conn.connect();
        current.emit('connection_open', {});
        await connected;

        const auth = conn.authenticateCBS('queue');
        const assertion = expect(auth).rejects.toThrow('CBS authentication timeout');
        await vi.advanceTimersByTimeAsync(5000);
        await assertion;
        expect(current.links.every(l => l.closed)).toBe(true);
    });
});
