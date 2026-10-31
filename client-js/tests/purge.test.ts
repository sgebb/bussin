import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest';
import { GlobalMockBroker } from '../src/mockBroker.js';
import { ManagementClient } from '../src/managementClient.js';
import * as ServiceBusAPI from '../serviceBusApi.js';

describe('purge', () => {
    beforeEach(async () => {
        (globalThis as any).__BUSSIN_SIMULATOR_ACTIVE__ = true;
        GlobalMockBroker.reset();
        GlobalMockBroker.createQueue('q');
        await ServiceBusAPI.sendQueueMessageBatch('ns', 'q', 'token',
            Array.from({ length: 5 }, (_, i) => ({ body: `m${i}` })));
    });

    afterEach(() => {
        vi.restoreAllMocks();
    });

    it('empties the queue with batch delete', async () => {
        const controller = await ServiceBusAPI.purgeQueue('ns', 'q', 'token');

        expect(await controller.promise).toBe(5);
        expect(ServiceBusAPI.getQueueMessageCount('ns', 'q')).toBe(0);
    });

    it('falls back to receivers after one refused batch delete, without retrying a smaller batch', async () => {
        const batchDelete = vi.spyOn(ManagementClient.prototype, 'batchDeleteMessages').mockRejectedValue(
            new Error("Purge failed: 401 - Unauthorized access. '' claim(s) are required (amqp:unauthorized-access)"));

        const controller = await ServiceBusAPI.purgeQueue('ns', 'q', 'token');

        expect(await controller.promise).toBe(5);
        expect(batchDelete).toHaveBeenCalledTimes(1);
        expect(ServiceBusAPI.getQueueMessageCount('ns', 'q')).toBe(0);
    });

    it('switches to 500 per batch when the namespace rejects 4000, as Standard does', async () => {
        const batchDelete = vi.spyOn(ManagementClient.prototype, 'batchDeleteMessages')
            .mockRejectedValueOnce(new Error('Batch delete failed: 400 - Invalid Message Count : requested 4000. ' +
                'Message count should be between 1 and 500. (com.microsoft:argument-out-of-range)'))
            .mockResolvedValueOnce(500)
            .mockResolvedValueOnce(0);

        const controller = await ServiceBusAPI.purgeQueue('ns', 'q', 'token');

        expect(await controller.promise).toBe(500);
        expect(batchDelete.mock.calls.map(([size]) => size)).toEqual([4000, 500, 500]);
    });

    it('keeps batch deleting until a batch deletes nothing, since the service can return short batches early', async () => {
        const batchDelete = vi.spyOn(ManagementClient.prototype, 'batchDeleteMessages')
            .mockResolvedValueOnce(3)
            .mockResolvedValueOnce(1)
            .mockResolvedValueOnce(0);

        const controller = await ServiceBusAPI.purgeQueue('ns', 'q', 'token');

        expect(await controller.promise).toBe(4);
        expect(batchDelete).toHaveBeenCalledTimes(3);
    });

    it('fails with the reason when receivers stop but older messages remain', async () => {
        vi.spyOn(ManagementClient.prototype, 'batchDeleteMessages').mockRejectedValue(new Error('Batch delete failed: 400'));
        // A message from before the purge that receivers never get, like a scheduled one
        vi.spyOn(ManagementClient.prototype, 'peekMessages').mockResolvedValue([{ body: 'scheduled' }]);

        const controller = await ServiceBusAPI.purgeQueue('ns', 'q', 'token');

        await expect(controller.promise).rejects.toThrow("Deleted 5 messages, but the rest can't be received");
    }, 20000);
});
