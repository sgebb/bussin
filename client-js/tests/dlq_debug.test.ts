import { describe, it, expect, beforeEach } from 'vitest';
import { MockBroker } from '../src/mockBroker';
import * as ServiceBusAPIFunctions from '../serviceBusApi';

describe('Dead Letter Queue Debug', () => {
    let broker: MockBroker;

    beforeEach(() => {
        broker = new MockBroker();
        broker.reset();
        // @ts-ignore
        globalThis.GlobalMockBroker = broker;
        // @ts-ignore
        globalThis.__BUSSIN_SIMULATOR_ACTIVE__ = true;
        broker.createQueue('test-queue-1');
    });

    it('should move message to DLQ correctly', async () => {
        // 1. Push a message
        broker.pushMessage('test-queue-1', { 
            body: 'hello world', 
            _sequenceNumber: 1,
            application_properties: {} 
        });

        const activeMsgs = broker.getMessages('test-queue-1');
        expect(activeMsgs.length).toBe(1);

        // 2. Dead letter it
        console.log('--- Move to DLQ ---');
        await ServiceBusAPIFunctions.deadLetterQueueMessagesBySequence('test-ns', 'test-queue-1', 'fake-token', [1]);

        // 3. Verify it's gone from active queue
        expect(activeMsgs.length).toBe(0);

        // 4. Verify it's in DLQ
        console.log('--- Peek DLQ ---');
        const dlqMsgs = await ServiceBusAPIFunctions.peekQueueMessages('test-ns', 'test-queue-1', 'fake-token', 10, 0, true);
        
        expect(dlqMsgs.length).toBe(1);
        expect(dlqMsgs[0].body).toBe('hello world');
    });
});
