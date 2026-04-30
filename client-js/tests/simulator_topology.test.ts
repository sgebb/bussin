import { describe, it, expect, vi, beforeEach } from 'vitest';
import { GlobalMockBroker } from '../src/mockBroker.js';
import * as ServiceBusAPI from '../serviceBusApi.js';

// Setup global mocks for Node environment
(globalThis as any).WebSocket = class { constructor() {} };

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
            decode: (data: any) => data,
            encode: (msg: any) => msg
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
        message: mockRhea.message,
        types: mockRhea.types
    };
});

describe('Simulator Topology & Fidelity Tests', () => {
    const ns = 'demo-ns';
    const topic = 'orders-topic';
    const sub1 = 'inventory-sub';
    const sub2 = 'billing-sub';

    beforeEach(() => {
        // Clear all global state before each test
        (globalThis as any).__BUSSIN_SIMULATOR_ACTIVE__ = true;
        GlobalMockBroker.reset();
        
        // Setup topology
        GlobalMockBroker.createTopic(topic);
        GlobalMockBroker.createSubscription(topic, sub1);
        GlobalMockBroker.createSubscription(topic, sub2);
    });

    it('should fan-out a single topic message to all subscriptions', async () => {
        const payload = { orderId: 100, item: 'Nexus-6' };
        const properties = { 'Priority': 'High', 'Source': 'TestRunner' };

        // Send to Topic
        await ServiceBusAPI.sendTopicMessage(ns, topic, 'token', payload, properties);

        // Verify Fan-out
        const sub1Messages = GlobalMockBroker.getMessages(`${topic}/subscriptions/${sub1}`);
        const sub2Messages = GlobalMockBroker.getMessages(`${topic}/subscriptions/${sub2}`);

        expect(sub1Messages.length).toBe(1);
        expect(sub2Messages.length).toBe(1);

        // Verify Property Preservation in Sub 1
        const msg1 = sub1Messages[0];
        expect(msg1.application_properties.Priority).toBe('High');
        expect(msg1.application_properties.Source).toBe('TestRunner');

        // Verify Property Preservation in Sub 2
        const msg2 = sub2Messages[0];
        expect(msg2.application_properties.Priority).toBe('High');
    });

    it('should isolate DLQ actions between subscriptions', async () => {
        // 1. Send message to topic
        await ServiceBusAPI.sendTopicMessage(ns, topic, 'token', { text: 'test-isolation' });

        // 2. Receive and DLQ from Subscription 1
        const sub1Path = `${topic}/subscriptions/${sub1}`;
        const [locked] = await ServiceBusAPI.receiveAndLockSubscriptionMessage(ns, topic, sub1, 'token');
        
        await ServiceBusAPI.deadLetter([locked.lockToken!], { 
            deadLetterReason: 'TestingIsolation' 
        });

        // 3. Verify Results
        const sub1Active = GlobalMockBroker.getMessages(sub1Path);
        const sub1DLQ = GlobalMockBroker.getMessages(`${sub1Path}/$DeadLetterQueue`);
        const sub2Active = GlobalMockBroker.getMessages(`${topic}/subscriptions/${sub2}`);

        // Sub 1 should be moved to DLQ
        expect(sub1Active.length).toBe(0);
        expect(sub1DLQ.length).toBe(1);

        // Sub 2 should remain Active (Isolation!)
        expect(sub2Active.length).toBe(1);
    });

    it('should preserve custom properties through a Resubmit workflow', async () => {
        // 1. Put message in DLQ
        const dlqPath = `${topic}/subscriptions/${sub1}/$DeadLetterQueue`;
        const payload = { data: 'resubmit-me' };
        const props = { 'OriginalTry': 1, 'RetryCount': 0 };
        
        // Hack: manually push to DLQ for setup
        GlobalMockBroker.pushMessage(dlqPath, {
            body: JSON.stringify(payload),
            message_id: 'msg-dlq',
            application_properties: props
        });

        // 2. Resubmit Workflow (Peek from DLQ -> Send to Topic -> Complete from DLQ)
        const [dlqMsg] = await ServiceBusAPI.peekSubscriptionMessages(ns, topic, sub1, 'token', 1, 0, true);
        
        // Update properties for resubmit
        const resubmitProps = { ...dlqMsg.applicationProperties, 'RetryCount': 1 };
        await ServiceBusAPI.sendTopicMessage(ns, topic, 'token', dlqMsg.body, resubmitProps);

        // Complete the DLQ message
        const [locked] = await ServiceBusAPI.receiveAndLockSubscriptionMessage(ns, topic, sub1, 'token', 5, true);
        await ServiceBusAPI.complete([locked.lockToken!]);

        // 3. Verify final state
        const sub1Active = GlobalMockBroker.getMessages(`${topic}/subscriptions/${sub1}`);
        const sub1DLQFinal = GlobalMockBroker.getMessages(dlqPath);

        expect(sub1DLQFinal.length).toBe(0); // Completed
        expect(sub1Active.length).toBe(1);   // Resubmitted
        
        const finalMsg = sub1Active[0];
        expect(finalMsg.application_properties.RetryCount).toBe(1);
        expect(finalMsg.application_properties.OriginalTry).toBe(1);
    });
});
