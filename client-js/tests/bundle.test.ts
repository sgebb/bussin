import { describe, it, expect } from 'vitest';
import * as fs from 'fs';
import * as path from 'path';
import * as vm from 'vm';

describe('ServiceBusAPI Bundle Verification', () => {
    it('should load the compiled bundle and attach all expected methods to window.ServiceBusAPI', () => {
        const bundlePath = path.resolve(__dirname, '../../src/wwwroot/js/servicebus-api.js');
        expect(fs.existsSync(bundlePath)).toBe(true);

        const bundleContent = fs.readFileSync(bundlePath, 'utf8');

        // Create a mock window context with necessary environment stubs
        const mockWindow: any = {
            navigator: {
                userAgent: 'node'
            },
            WebSocket: class {
                constructor() {}
            }
        };
        mockWindow.window = mockWindow;
        mockWindow.globalThis = mockWindow;
        mockWindow.self = mockWindow;

        const context = vm.createContext({
            window: mockWindow,
            globalThis: mockWindow,
            global: mockWindow,
            self: mockWindow,
            console: console
        });

        // Run the bundle in the VM context
        vm.runInContext(bundleContent, context);

        expect(mockWindow.ServiceBusAPI).toBeDefined();
        
        const expectedMethods = [
            // Read operations
            'peekQueueMessages',
            'peekSubscriptionMessages',
            'peekQueueMessagesBySequence',
            'peekSubscriptionMessagesBySequence',
            'receiveAndLockQueueMessage',
            'receiveAndLockSubscriptionMessage',

            // Settlement operations
            'complete',
            'abandon',
            'deadLetter',

            // Send operations
            'sendQueueMessage',
            'sendTopicMessage',
            'sendQueueMessageBatch',
            'sendTopicMessageBatch',

            // Destructive operations
            'purgeQueue',
            'purgeSubscription',
            'deleteQueueMessagesBySequence',
            'deleteSubscriptionMessagesBySequence',
            'cancelScheduledQueueMessages',
            'cancelScheduledTopicMessages',
            'deadLetterQueueMessagesBySequence',
            'deadLetterSubscriptionMessagesBySequence',
            'purgeQueueDirect',
            'purgeSubscriptionDirect',

            // Monitor operations
            'monitorQueue',
            'monitorSubscription',

            // Search operations
            'searchQueueMessages',
            'searchSubscriptionMessages',

            // Simulator control
            'enableSimulator',
            'seedMockData',
            'seedTopic',
            'seedTopicData',
            'seedSubscription',
            'seedSubscriptionData',
            'getQueueMessageCount',
            'getSubscriptionMessageCount',

            // Rule operations
            'enumerateRules',
            'addRule',
            'removeRule'
        ];

        for (const method of expectedMethods) {
            expect(mockWindow.ServiceBusAPI[method]).toBeDefined();
            expect(typeof mockWindow.ServiceBusAPI[method]).toBe('function');
        }
    });
});
