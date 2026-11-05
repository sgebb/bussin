import { describe, it, expect, beforeEach } from 'vitest';
import { GlobalMockBroker } from '../src/mockBroker.js';
import * as ServiceBusAPI from '../serviceBusApi.js';

describe('settle by sequence number with gaps in the selection', () => {
    beforeEach(() => {
        (globalThis as any).__BUSSIN_SIMULATOR_ACTIVE__ = true;
        GlobalMockBroker.reset();
        GlobalMockBroker.createQueue('q');
    });

    const peek = async (dlq = false) =>
        (await ServiceBusAPI.peekQueueMessages('ns', 'q', 'token', 100, 0, dlq)).map(m => m.sequenceNumber);

    it('moving 1st and 3rd to the DLQ leaves the 2nd and 4th untouched, in order', async () => {
        await ServiceBusAPI.sendQueueMessageBatch('ns', 'q', 'token', [1, 2, 3, 4].map(id => ({ body: { id } })));
        const [a, b, c, d] = await peek();

        const result = await ServiceBusAPI.deadLetterQueueMessagesBySequence('ns', 'q', 'token', [a!, c!], 'why', 'what');

        expect(result.settled.sort()).toEqual([a, c].sort());
        expect(await peek()).toEqual([b, d]);
        expect(await peek(true)).toEqual([a, c]);
    });

    it('deleting 1st and 3rd from the DLQ leaves the 2nd and 4th untouched, in order', async () => {
        await ServiceBusAPI.sendQueueMessageBatch('ns', 'q', 'token', [1, 2, 3, 4].map(id => ({ body: { id } })));
        const [a, b, c, d] = await peek();
        await ServiceBusAPI.deadLetterQueueMessagesBySequence('ns', 'q', 'token', [a!, b!, c!, d!], 'why', 'what');
        const dlq = await peek(true);
        expect(dlq).toEqual([a, b, c, d]);

        const result = await ServiceBusAPI.deleteQueueMessagesBySequence('ns', 'q', 'token', [dlq[0]!, dlq[2]!], true);

        expect(result.settled.sort()).toEqual([dlq[0], dlq[2]].sort());
        expect(await peek(true)).toEqual([dlq[1], dlq[3]]);
    });

    it('releasing several messages keeps their order', async () => {
        await ServiceBusAPI.sendQueueMessageBatch('ns', 'q', 'token', [1, 2, 3, 4, 5].map(id => ({ body: { id } })));
        const [a, b, c, d, e] = await peek();

        await ServiceBusAPI.deleteQueueMessagesBySequence('ns', 'q', 'token', [a!, e!]);

        expect(await peek()).toEqual([b, c, d]);
    });
});
