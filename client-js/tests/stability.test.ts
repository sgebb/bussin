import { describe, it, expect, vi, beforeEach } from 'vitest';
import { GlobalMockBroker } from '../src/mockBroker.js';
import * as ServiceBusAPI from '../serviceBusApi.js';

// Mock the 'rhea' library to redirect to our MockBroker
vi.mock('rhea', () => {
    const mockRhea = {
        connect: (opts: any) => GlobalMockBroker.connect(opts),
        websocket_connect: (ws: any) => {
            return (url: string, protocols: string[]) => {
                return { url, protocols }; // dummy
            };
        },
        message: {
            data_section: (data: any) => data,
            decode: (data: any) => data
        },
        types: {
            wrap_long: (v: any) => v,
            wrap_int: (v: any) => v,
            wrap_uint: (v: any) => v,
            wrap_array: (v: any) => v,
            wrap_timestamp: (v: any) => v
        }
    };

    return {
        ...mockRhea,
        default: mockRhea,
        message: mockRhea.message
    };
});

// Since connection.ts uses WebSocket, we need a global mock for it in Node
(globalThis as any).WebSocket = class {
    constructor() {}
};

describe('Stability Tests (Mock Broker)', () => {
    beforeEach(() => {
        (globalThis as any).__BUSSIN_SIMULATOR_ACTIVE__ = true;
        GlobalMockBroker.reset();
        GlobalMockBroker.createQueue('test-queue');
    });

    it('should verify Batch Send preserves all messages', async () => {
        const messages = [
            { body: { id: 1 } },
            { body: { id: 2 } },
            { body: { id: 3 } }
        ];

        await ServiceBusAPI.sendQueueMessageBatch('demo-ns', 'test-queue', 'token', messages);

        const stored = GlobalMockBroker.getMessages('test-queue');
        expect(stored.length).toBe(3);
        // Verify we at least saw the SEND actions
        expect(GlobalMockBroker.auditLog.some(log => log.startsWith('SEND (to: test-queue'))).toBe(true);
    });

    it('should verify Move-to-DLQ safety (Lock -> Reject)', async () => {
        // 1. Setup a message in the queue
        await ServiceBusAPI.sendQueueMessage('demo-ns', 'test-queue', 'token', { text: 'fail-me' });
        
        // 2. Receive and Lock
        const [locked] = await ServiceBusAPI.receiveAndLockQueueMessage('demo-ns', 'test-queue', 'token');
        expect(locked).toBeDefined();

        // 3. Move to DLQ (DeadLetter)
        await ServiceBusAPI.deadLetter([locked.lockToken!], { 
            deadLetterReason: 'Testing', 
            deadLetterErrorDescription: 'Stability test' 
        });

        // 4. Verify results
        const mainQueue = GlobalMockBroker.getMessages('test-queue');
        const dlq = GlobalMockBroker.getMessages('test-queue/$DeadLetterQueue');

        expect(mainQueue.length).toBe(0);
        expect(dlq.length).toBe(1);
        
        // Decode buffer for assertion
        const bodyStr = new TextDecoder().decode(dlq[0].body);
        expect(bodyStr).toContain('fail-me');

        // 5. Audit sequence
        const settlementIndex = GlobalMockBroker.auditLog.findIndex(a => a.includes('SETTLEMENT_REJECTED'));
        expect(settlementIndex).toBeGreaterThan(-1);
    });
});
