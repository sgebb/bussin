import { describe, it, expect, beforeEach } from 'vitest';
import { GlobalMockBroker } from '../src/mockBroker.js';
import * as ServiceBusAPI from '../serviceBusApi.js';

describe('settle by sequence number (lock and match)', () => {
    beforeEach(() => {
        (globalThis as any).__BUSSIN_SIMULATOR_ACTIVE__ = true;
        GlobalMockBroker.reset();
        GlobalMockBroker.createQueue('q');
    });

    const seed = async (count: number) => {
        await ServiceBusAPI.sendQueueMessageBatch('ns', 'q', 'token',
            Array.from({ length: count }, (_, i) => ({ body: { id: i } })));
        const peeked = await ServiceBusAPI.peekQueueMessages('ns', 'q', 'token', count);
        return peeked.map(m => m.sequenceNumber!);
    };

    const remaining = async () =>
        (await ServiceBusAPI.peekQueueMessages('ns', 'q', 'token', 100)).map(m => m.sequenceNumber).sort((a, b) => a! - b!);

    it('deletes only the requested messages and releases the ones ahead of them', async () => {
        const seqs = await seed(6);

        const result = await ServiceBusAPI.deleteQueueMessagesBySequence('ns', 'q', 'token', [seqs[1], seqs[3]]);

        expect(result.settled.sort()).toEqual([seqs[1], seqs[3]].sort());
        expect(result.notFound).toEqual([]);
        expect(await remaining()).toEqual([seqs[0], seqs[2], seqs[4], seqs[5]]);
    });

    it('dead-letters the requested messages', async () => {
        const seqs = await seed(4);

        const result = await ServiceBusAPI.deadLetterQueueMessagesBySequence('ns', 'q', 'token', [seqs[2]], 'why', 'what');

        expect(result.settled).toEqual([seqs[2]]);
        expect(await remaining()).toEqual([seqs[0], seqs[1], seqs[3]]);
        const dlq = await ServiceBusAPI.peekQueueMessages('ns', 'q', 'token', 10, 0, true);
        expect(dlq.map(m => m.sequenceNumber)).toEqual([seqs[2]]);
    });

    it('reports sequence numbers that are not in the entity', async () => {
        const seqs = await seed(2);

        const result = await ServiceBusAPI.deleteQueueMessagesBySequence('ns', 'q', 'token', [seqs[0], 999_999]);

        expect(result.settled).toEqual([seqs[0]]);
        expect(result.notFound).toEqual([999_999]);
        expect(await remaining()).toEqual([seqs[1]]);
    });
});
