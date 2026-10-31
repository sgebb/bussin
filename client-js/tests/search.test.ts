import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest';
import { GlobalMockBroker } from '../src/mockBroker.js';
import { ManagementClient } from '../src/managementClient.js';
import * as ServiceBusAPI from '../serviceBusApi.js';

describe('search', () => {
    beforeEach(async () => {
        (globalThis as any).__BUSSIN_SIMULATOR_ACTIVE__ = true;
        GlobalMockBroker.reset();
        GlobalMockBroker.createQueue('q');
        await ServiceBusAPI.sendQueueMessageBatch('ns', 'q', 'token',
            ['apple', 'banana', 'apple pie'].map(body => ({ body })));
    });

    afterEach(() => {
        vi.restoreAllMocks();
    });

    it('finds the messages whose body matches', async () => {
        const controller = await ServiceBusAPI.searchQueueMessages('ns', 'q', 'token', false, 'apple', '', '', 100, 50);
        const result = await controller.promise;

        expect(result.scannedCount).toBe(3);
        expect(result.matchCount).toBe(2);
    });

    it('fails with the cause when peeking keeps failing, instead of reporting a partial result as complete', async () => {
        vi.spyOn(ManagementClient.prototype, 'peekMessages').mockRejectedValue(new Error('ExpiredToken'));

        const controller = await ServiceBusAPI.searchQueueMessages('ns', 'q', 'token', false, 'apple', '', '', 100, 50);

        await expect(controller.promise).rejects.toThrow('Search failed after scanning 0 messages: ExpiredToken');
    });
});
