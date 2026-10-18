import { describe, it, expect, vi, beforeEach } from 'vitest';
import { GlobalMockBroker } from '../src/mockBroker.js';
import * as ServiceBusAPI from '../serviceBusApi.js';

(globalThis as any).WebSocket = class { constructor() {} };

vi.mock('rhea', () => {
    const mockRhea = {
        connect: (opts: any) => GlobalMockBroker.connect(opts),
        websocket_connect: () => (url: string, protocols: string[]) => ({ url, protocols }),
        message: {
            data_section: (data: any) => data,
            decode: (data: any) => data,
            encode: (msg: any) => msg
        },
        types: {
            wrap_long: (v: any) => v,
            wrap_int: (v: any) => v,
            wrap_uint: (v: any) => v,
            wrap_array: (v: any) => v,
            wrap_timestamp: (v: any) => v,
            wrap_described: (v: any) => v,
            wrap_binary: (v: any) => v
        }
    };
    return { ...mockRhea, default: mockRhea };
});

const ns = 'demo-ns';
const queue = 'watched-queue';
const push = (body: string) => GlobalMockBroker.pushMessage(queue, {
    body: new TextEncoder().encode(body),
    message_id: body,
    application_properties: {}
});
const wait = (ms: number) => new Promise(resolve => setTimeout(resolve, ms));

describe('monitorEntity', () => {
    beforeEach(() => {
        (globalThis as any).__BUSSIN_SIMULATOR_ACTIVE__ = true;
        GlobalMockBroker.reset();
        GlobalMockBroker.createQueue(queue);
    });

    it('skips the existing backlog, even past 100 messages with gaps, and reports only new arrivals', async () => {
        for (let i = 1; i <= 300; i++) push(`old-${i}`);
        // Simulate a consumer having completed a run of messages: sequence numbers now have a gap.
        const messages = GlobalMockBroker.getMessages(queue);
        messages.splice(150, 100);

        const seen: any[] = [];
        const monitor = await ServiceBusAPI.monitorEntity(ns, queue, 'token', m => seen.push(m), () => {}, async () => 'token');
        try {
            await wait(1300);
            expect(seen).toHaveLength(0);

            push('new-1');
            push('new-2');
            await wait(1300);
            expect(seen.map(m => m.messageId)).toEqual(['new-1', 'new-2']);
        } finally {
            monitor.stop();
        }
    });

    it('reports the first message when started on an empty entity', async () => {
        const seen: any[] = [];
        const monitor = await ServiceBusAPI.monitorEntity(ns, queue, 'token', m => seen.push(m), () => {}, async () => 'token');
        try {
            push('first');
            await wait(1300);
            expect(seen.map(m => m.messageId)).toEqual(['first']);
        } finally {
            monitor.stop();
        }
    });
});
