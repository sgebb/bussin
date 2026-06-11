/**
 * Service Bus Client API
 * High-level API for Azure Service Bus operations (Modular Entrypoint)
 */

import {
    peekQueueMessages,
    peekSubscriptionMessages,
    peekQueueMessagesBySequence,
    peekSubscriptionMessagesBySequence,
    receiveAndLockQueueMessage,
    receiveAndLockSubscriptionMessage,
    complete,
    abandon,
    deadLetter,
    getMessageSessions,
    getSessionState,
    setSessionState
} from './src/peekOperations.js';

import {
    sendQueueMessage,
    sendTopicMessage,
    sendQueueMessageBatch,
    sendTopicMessageBatch,
    cancelScheduledQueueMessages,
    cancelScheduledTopicMessages
} from './src/sendOperations.js';

import {
    purgeQueue,
    purgeSubscription,
    deleteQueueMessagesBySequence,
    deleteSubscriptionMessagesBySequence,
    deadLetterQueueMessagesBySequence,
    deadLetterSubscriptionMessagesBySequence,
    purgeQueueDirect,
    purgeSubscriptionDirect
} from './src/purgeOperations.js';

import {
    searchQueueMessages,
    searchSubscriptionMessages
} from './src/searchOperations.js';

import {
    monitorQueue,
    monitorSubscription
} from './src/monitorOperations.js';

import {
    enableSimulator,
    seedMockData,
    seedTopic,
    seedSubscription,
    seedTopicData,
    seedSubscriptionData,
    getQueueMessageCount,
    getSubscriptionMessageCount
} from './src/simulatorOperations.js';

import {
    enumerateRules,
    addRule,
    removeRule
} from './src/ruleOperations.js';

// Export for browser JS usage
if (typeof window !== 'undefined') {
    (window as any).ServiceBusAPI = {
        // Read operations
        peekQueueMessages,
        peekSubscriptionMessages,
        peekQueueMessagesBySequence,
        peekSubscriptionMessagesBySequence,
        receiveAndLockQueueMessage,
        receiveAndLockSubscriptionMessage,
        getMessageSessions,
        getSessionState,
        setSessionState,

        // Settlement operations (stateless - take LockedMessage[])
        complete,
        abandon,
        deadLetter,

        // Send operations
        sendQueueMessage,
        sendTopicMessage,
        sendQueueMessageBatch,
        sendTopicMessageBatch,

        // Destructive operations
        purgeQueue,
        purgeSubscription,
        deleteQueueMessagesBySequence,
        deleteSubscriptionMessagesBySequence,
        cancelScheduledQueueMessages,
        cancelScheduledTopicMessages,
        deadLetterQueueMessagesBySequence,
        deadLetterSubscriptionMessagesBySequence,
        purgeQueueDirect,
        purgeSubscriptionDirect,

        // Monitor operations
        monitorQueue,
        monitorSubscription,

        // Search operations
        searchQueueMessages,
        searchSubscriptionMessages,

        // Simulator control (demo / local-dev mode)
        enableSimulator,
        seedMockData,
        seedTopic,
        seedTopicData,
        seedSubscription,
        seedSubscriptionData,
        getQueueMessageCount,
        getSubscriptionMessageCount,

        // Rule operations
        enumerateRules,
        addRule,
        removeRule
    };
}

export {
    // Read operations
    peekQueueMessages,
    peekSubscriptionMessages,
    peekQueueMessagesBySequence,
    peekSubscriptionMessagesBySequence,
    receiveAndLockQueueMessage,
    receiveAndLockSubscriptionMessage,
    getMessageSessions,
    getSessionState,
    setSessionState,

    // Settlement operations (stateless - take LockedMessage[])
    complete,
    abandon,
    deadLetter,

    // Send operations
    sendQueueMessage,
    sendTopicMessage,
    sendQueueMessageBatch,
    sendTopicMessageBatch,

    // Destructive operations
    purgeQueue,
    purgeSubscription,
    deleteQueueMessagesBySequence,
    deleteSubscriptionMessagesBySequence,
    cancelScheduledQueueMessages,
    cancelScheduledTopicMessages,
    deadLetterQueueMessagesBySequence,
    deadLetterSubscriptionMessagesBySequence,
    purgeQueueDirect,
    purgeSubscriptionDirect,

    // Monitor operations
    monitorQueue,
    monitorSubscription,

    // Search operations
    searchQueueMessages,
    searchSubscriptionMessages,

    // Simulator control (demo / local-dev mode)
    enableSimulator,
    seedMockData,
    seedTopic,
    seedTopicData,
    seedSubscription,
    seedSubscriptionData,
    getQueueMessageCount,
    getSubscriptionMessageCount,

    // Rule operations
    enumerateRules,
    addRule,
    removeRule
};
