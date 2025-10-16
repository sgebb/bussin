/**
 * Service Bus Client API
 * High-level API for Azure Service Bus operations
 * All functions handle their own connection lifecycle
 */

import { ServiceBusConnection } from './src/connection.js';
import { ManagementClient } from './src/managementClient.js';
import { MessageReceiver } from './src/messageReceiver.js';
import { MessageSender } from './src/messageSender.js';
import { parseServiceBusMessage } from './src/messageParser.js';
import type { 
    ServiceBusMessage, 
    MessageProperties, 
    PurgeController, 
    MonitorController,
    ProgressCallback,
    MessageCallback,
    ErrorCallback,
    BatchOperationResult,
    LockedMessage,
    DeadLetterOptions
} from './src/types.js';

// ============================================================================
// QUEUE OPERATIONS
// ============================================================================

/**
 * Peek messages from a queue (read-only, no side effects)
 * @param fromDeadLetter - If true, peeks from the dead letter queue
 */
async function peekQueueMessages(
    namespace: string, 
    queueName: string, 
    token: string, 
    count: number = 10, 
    fromSequence: number = 0,
    fromDeadLetter: boolean = false
): Promise<ServiceBusMessage[]> {
    const entityPath = fromDeadLetter ? `${queueName}/$DeadLetterQueue` : queueName;
    return await peekMessages(namespace, entityPath, token, count, fromSequence);
}

/**
 * Peek messages from a subscription (read-only, no side effects)
 * @param fromDeadLetter - If true, peeks from the dead letter queue
 */
async function peekSubscriptionMessages(
    namespace: string, 
    topicName: string, 
    subscriptionName: string, 
    token: string, 
    count: number = 10, 
    fromSequence: number = 0,
    fromDeadLetter: boolean = false
): Promise<ServiceBusMessage[]> {
    const subscriptionPath = `${topicName}/subscriptions/${subscriptionName}`;
    const entityPath = fromDeadLetter ? `${subscriptionPath}/$DeadLetterQueue` : subscriptionPath;
    return await peekMessages(namespace, entityPath, token, count, fromSequence);
}

// Internal implementation
async function peekMessages(
    namespace: string, 
    entityPath: string, 
    token: string, 
    count: number = 10, 
    fromSequence: number = 0
): Promise<ServiceBusMessage[]> {
    const connection = new ServiceBusConnection(namespace, token);
    
    try {
        await connection.connect();
        await connection.authenticateCBS(entityPath);
        
        const managementClient = new ManagementClient(connection, entityPath);
        await managementClient.open();
        
        const messages = await managementClient.peekMessages(fromSequence, count);
        
        managementClient.close();
        connection.close();
        
        // Parse messages
        return messages.map(msg => parseServiceBusMessage(msg));
    } catch (err) {
        connection.close();
        throw new Error(`Peek failed: ${(err as Error).message}`);
    }
}

/**
 * Send a message to a queue
 */
async function sendQueueMessage(
    namespace: string, 
    queueName: string, 
    token: string, 
    messageBody: string | object | Uint8Array | ArrayBuffer, 
    properties: MessageProperties = {}
): Promise<void> {
    return await sendMessage(namespace, queueName, token, messageBody, properties);
}

/**
 * Send a message to a topic
 */
async function sendTopicMessage(
    namespace: string, 
    topicName: string, 
    token: string, 
    messageBody: string | object | Uint8Array | ArrayBuffer, 
    properties: MessageProperties = {}
): Promise<void> {
    return await sendMessage(namespace, topicName, token, messageBody, properties);
}

// Internal implementation
async function sendMessage(
    namespace: string, 
    entityPath: string, 
    token: string, 
    messageBody: string | object | Uint8Array | ArrayBuffer, 
    properties: MessageProperties = {}
): Promise<void> {
    const connection = new ServiceBusConnection(namespace, token);
    
    try {
        await connection.connect();
        await connection.authenticateCBS(entityPath);
        
        const sender = new MessageSender(connection, entityPath);
        await sender.open();
        await sender.send(messageBody, properties);
        
        sender.close();
        connection.close();
    } catch (err) {
        connection.close();
        throw new Error(`Send failed: ${(err as Error).message}`);
    }
}

/**
 * Send a scheduled message to a queue
 * @param scheduledEnqueueTime - When the message should become visible
 */
async function sendScheduledQueueMessage(
    namespace: string,
    queueName: string,
    token: string,
    messageBody: string | object | Uint8Array | ArrayBuffer,
    scheduledEnqueueTime: Date,
    properties: MessageProperties = {}
): Promise<void> {
    // Add scheduled enqueue time to message annotations
    const messageProps = {
        ...properties,
        message_annotations: {
            ...properties.message_annotations,
            'x-opt-scheduled-enqueue-time': scheduledEnqueueTime
        }
    };
    return await sendMessage(namespace, queueName, token, messageBody, messageProps);
}

/**
 * Send a scheduled message to a topic
 * @param scheduledEnqueueTime - When the message should become visible
 */
async function sendScheduledTopicMessage(
    namespace: string,
    topicName: string,
    token: string,
    messageBody: string | object | Uint8Array | ArrayBuffer,
    scheduledEnqueueTime: Date,
    properties: MessageProperties = {}
): Promise<void> {
    // Add scheduled enqueue time to message annotations
    const messageProps = {
        ...properties,
        message_annotations: {
            ...properties.message_annotations,
            'x-opt-scheduled-enqueue-time': scheduledEnqueueTime
        }
    };
    return await sendMessage(namespace, topicName, token, messageBody, messageProps);
}

// ============================================================================
// PEEK-LOCK OPERATIONS
// ============================================================================

// Store active message handles by lock token (necessary for settlement)
// This is minimal state - just the AMQP handles that can't serialize
const messageHandles = new Map<string, {
    delivery: any;
    receiver: any;
    connection: any;
}>();

/**
 * Receive and lock a message from a queue (peek-lock mode)
 * Message stays in queue but locked to you until completed/abandoned/expired
 * @param fromDeadLetter - If true, locks from the dead letter queue
 * @param count - Number of messages to lock (default 1, max 100)
 */
async function receiveAndLockQueueMessage(
    namespace: string,
    queueName: string,
    token: string,
    timeoutSeconds: number = 5,
    fromDeadLetter: boolean = false,
    count: number = 1
): Promise<LockedMessage[]> {
    const entityPath = fromDeadLetter ? `${queueName}/$DeadLetterQueue` : queueName;
    return await receiveAndLockMessages(namespace, entityPath, token, timeoutSeconds, count);
}

/**
 * Receive and lock a message from a subscription (peek-lock mode)
 * @param fromDeadLetter - If true, locks from the dead letter queue
 * @param count - Number of messages to lock (default 1, max 100)
 */
async function receiveAndLockSubscriptionMessage(
    namespace: string,
    topicName: string,
    subscriptionName: string,
    token: string,
    timeoutSeconds: number = 5,
    fromDeadLetter: boolean = false,
    count: number = 1
): Promise<LockedMessage[]> {
    const subscriptionPath = `${topicName}/subscriptions/${subscriptionName}`;
    const entityPath = fromDeadLetter ? `${subscriptionPath}/$DeadLetterQueue` : subscriptionPath;
    return await receiveAndLockMessages(namespace, entityPath, token, timeoutSeconds, count);
}

// Internal implementation - lock multiple messages with SINGLE connection
async function receiveAndLockMessages(
    namespace: string,
    entityPath: string,
    token: string,
    timeoutSeconds: number = 5,
    count: number = 1
): Promise<LockedMessage[]> {
    const connection = new ServiceBusConnection(namespace, token);
    
    try {
        await connection.connect();
        await connection.authenticateCBS(entityPath);
        
        return new Promise((resolve, reject) => {
            const lockedMsgs: LockedMessage[] = [];
            let messagesReceived = 0;
            let timedOut = false;
            let noMoreMessagesTimer: NodeJS.Timeout | null = null;
            
            // Use peek-lock mode (rcv_settle_mode: 1, autoaccept: false)
            const receiver = connection.connection!.open_receiver({
                source: { address: entityPath },
                credit_window: 0,  // Manual credit control
                autoaccept: false,
                rcv_settle_mode: 1  // Peek-lock mode
            });

            receiver.on('message', (context: any) => {
                if (timedOut) return;
                
                // Clear the "no more messages" timer since we got one
                if (noMoreMessagesTimer) {
                    clearTimeout(noMoreMessagesTimer);
                    noMoreMessagesTimer = null;
                }
                
                // Parse the message
                const parsedMessage = parseServiceBusMessage(context.message) as LockedMessage;
                
                // Generate lock token from delivery tag
                const lockToken = context.delivery.tag.toString('hex');
                parsedMessage.lockToken = lockToken;
                
                // Store handle in Map (can't serialize, so keep server-side)
                messageHandles.set(lockToken, {
                    delivery: context.delivery,
                    receiver: receiver,
                    connection: connection
                });
                
                lockedMsgs.push(parsedMessage);
                messagesReceived++;
                
                // If we got all requested messages, resolve immediately
                if (messagesReceived >= count) {
                    timedOut = true;
                    resolve(lockedMsgs);
                    return;
                }
                
                // Start a short timer - if no message arrives in 500ms, assume queue is empty
                noMoreMessagesTimer = setTimeout(() => {
                    timedOut = true;
                    resolve(lockedMsgs);
                }, 500);
            });

            receiver.on('receiver_error', (context: any) => {
                if (!timedOut) {
                    if (noMoreMessagesTimer) clearTimeout(noMoreMessagesTimer);
                    receiver.close();
                    connection.close();
                    reject(new Error(context.receiver.error ? context.receiver.error.toString() : 'Receiver error'));
                }
            });
            
            // Issue credit to receive up to 'count' messages
            receiver.add_credit(count);
            
            // Overall timeout - return whatever we got (safety net)
            setTimeout(() => {
                if (!timedOut) {
                    timedOut = true;
                    if (noMoreMessagesTimer) clearTimeout(noMoreMessagesTimer);
                    resolve(lockedMsgs);
                }
            }, timeoutSeconds * 1000);
        });
    } catch (err) {
        connection.close();
        throw new Error(`Receive and lock failed: ${(err as Error).message}`);
    }
}

/**
 * Complete (delete) locked messages by lock tokens
 * @param lockTokens - Array of lock tokens from receiveAndLock
 */
async function complete(lockTokens: string[]): Promise<BatchOperationResult> {
    const result: BatchOperationResult = {
        successCount: 0,
        failureCount: 0,
        errors: []
    };
    
    for (const lockToken of lockTokens) {
        try {
            const handle = messageHandles.get(lockToken);
            if (!handle) {
                throw new Error('Message not found or lock expired');
            }
            
            // Accept the delivery (complete/delete)
            handle.delivery.accept();
            
            // Cleanup
            handle.receiver.close();
            handle.connection.close();
            messageHandles.delete(lockToken);
            
            result.successCount++;
        } catch (err) {
            result.failureCount++;
            result.errors.push({
                messageId: lockToken,
                error: (err as Error).message
            });
        }
    }
    
    return result;
}

/**
 * Abandon locked messages by lock tokens - releases locks and returns messages to queue
 * @param lockTokens - Array of lock tokens from receiveAndLock
 */
async function abandon(lockTokens: string[]): Promise<BatchOperationResult> {
    const result: BatchOperationResult = {
        successCount: 0,
        failureCount: 0,
        errors: []
    };
    
    for (const lockToken of lockTokens) {
        try {
            const handle = messageHandles.get(lockToken);
            if (!handle) {
                throw new Error('Message not found or lock expired');
            }
            
            // Release the message (abandon)
            handle.delivery.release();
            
            // Cleanup
            handle.receiver.close();
            handle.connection.close();
            messageHandles.delete(lockToken);
            
            result.successCount++;
        } catch (err) {
            result.failureCount++;
            result.errors.push({
                messageId: lockToken,
                error: (err as Error).message
            });
        }
    }
    
    return result;
}

/**
 * Dead letter locked messages by lock tokens - moves messages to DLQ
 * @param lockTokens - Array of lock tokens from receiveAndLock
 */
async function deadLetter(
    lockTokens: string[],
    options: DeadLetterOptions = {}
): Promise<BatchOperationResult> {
    const result: BatchOperationResult = {
        successCount: 0,
        failureCount: 0,
        errors: []
    };
    
    for (const lockToken of lockTokens) {
        try {
            const handle = messageHandles.get(lockToken);
            if (!handle) {
                throw new Error('Message not found or lock expired');
            }
            
            // Reject the delivery to move to DLQ with error info
            handle.delivery.reject({
                condition: 'com.microsoft:dead-letter',
                description: options.deadLetterErrorDescription || 'Manual dead letter from browser',
                info: {
                    'DeadLetterReason': options.deadLetterReason || 'Manual dead letter',
                    'DeadLetterErrorDescription': options.deadLetterErrorDescription || ''
                }
            });
            
            // Cleanup
            handle.receiver.close();
            handle.connection.close();
            messageHandles.delete(lockToken);
            
            result.successCount++;
        } catch (err) {
            result.failureCount++;
            result.errors.push({
                messageId: lockToken,
                error: (err as Error).message
            });
        }
    }
    
    return result;
}

/**
 * Purge all messages from a queue (receive and delete in loop)
 * @param fromDeadLetter - If true, purges the dead letter queue
 */
async function purgeQueue(
    namespace: string, 
    queueName: string, 
    token: string, 
    onProgress: ProgressCallback | null = null,
    fromDeadLetter: boolean = false
): Promise<PurgeController> {
    const entityPath = fromDeadLetter ? `${queueName}/$DeadLetterQueue` : queueName;
    return await purgeEntity(namespace, entityPath, token, onProgress);
}

/**
 * Purge all messages from a subscription (receive and delete in loop)
 * @param fromDeadLetter - If true, purges the dead letter queue
 */
async function purgeSubscription(
    namespace: string, 
    topicName: string, 
    subscriptionName: string, 
    token: string, 
    onProgress: ProgressCallback | null = null,
    fromDeadLetter: boolean = false
): Promise<PurgeController> {
    const subscriptionPath = `${topicName}/subscriptions/${subscriptionName}`;
    const entityPath = fromDeadLetter ? `${subscriptionPath}/$DeadLetterQueue` : subscriptionPath;
    return await purgeEntity(namespace, entityPath, token, onProgress);
}

// Internal implementation
async function purgeEntity(
    namespace: string, 
    entityPath: string, 
    token: string, 
    onProgress: ProgressCallback | null = null
): Promise<PurgeController> {
    const connection = new ServiceBusConnection(namespace, token);
    let isRunning = true;
    let deletedCount = 0;
    
    try {
        await connection.connect();
        await connection.authenticateCBS(entityPath);
        
        const receiver = new MessageReceiver(connection, entityPath, {
            peekMode: false,  // Receive and delete
            maxMessages: null,  // Continuous
            autoClose: false
        });
        
        const purgePromise = new Promise<number>((resolve, reject) => {
            let noMessageTimeout: NodeJS.Timeout | null = null;
            
            receiver.receive(
                (message: any) => {
                    deletedCount++;
                    if (onProgress) {
                        onProgress(deletedCount);
                    }
                    
                    // Reset timeout - we got a message
                    if (noMessageTimeout) {
                        clearTimeout(noMessageTimeout);
                    }
                    
                    // Set timeout to detect when queue is empty
                    noMessageTimeout = setTimeout(() => {
                        if (isRunning) {
                            receiver.close();
                            connection.close();
                            resolve(deletedCount);
                        }
                    }, 2000);  // 2 seconds of no messages = queue empty
                },
                (error: Error) => {
                    receiver.close();
                    connection.close();
                    reject(new Error(`Purge failed: ${error.message}`));
                }
            );
        });
        
        return {
            promise: purgePromise,
            stop: () => {
                isRunning = false;
                receiver.close();
                connection.close();
                return deletedCount;
            },
            getCount: () => deletedCount
        };
    } catch (err) {
        connection.close();
        throw new Error(`Purge failed: ${(err as Error).message}`);
    }
}

/**
 * Start monitoring messages from a queue (non-destructive, continuous)
 */
async function monitorQueue(
    namespace: string, 
    queueName: string, 
    token: string, 
    onMessage: MessageCallback, 
    onError?: ErrorCallback
): Promise<MonitorController> {
    return await startMonitoring(namespace, queueName, token, onMessage, onError);
}

/**
 * Start monitoring messages from a subscription (non-destructive, continuous)
 */
async function monitorSubscription(
    namespace: string, 
    topicName: string, 
    subscriptionName: string, 
    token: string, 
    onMessage: MessageCallback, 
    onError?: ErrorCallback
): Promise<MonitorController> {
    const entityPath = `${topicName}/subscriptions/${subscriptionName}`;
    return await startMonitoring(namespace, entityPath, token, onMessage, onError);
}

// Internal implementation
async function startMonitoring(
    namespace: string, 
    entityPath: string, 
    token: string, 
    onMessage: MessageCallback, 
    onError?: ErrorCallback
): Promise<MonitorController> {
    const connection = new ServiceBusConnection(namespace, token);
    
    try {
        await connection.connect();
        await connection.authenticateCBS(entityPath);
        
        const receiver = new MessageReceiver(connection, entityPath, {
            peekMode: true,  // Non-destructive
            maxMessages: null,  // Continuous
            autoClose: false
        });
        
        receiver.receive(
            (message: any) => onMessage(parseServiceBusMessage(message)),
            onError
        );
        
        return {
            stop: () => {
                receiver.close();
                connection.close();
            }
        };
    } catch (err) {
        connection.close();
        throw new Error(`Monitor failed: ${(err as Error).message}`);
    }
}

// ============================================================================
// EXPORTS
// ============================================================================

// Export for browser JS usage
(window as any).ServiceBusAPI = {
    // Read operations
    peekQueueMessages,
    peekSubscriptionMessages,
    receiveAndLockQueueMessage,
    receiveAndLockSubscriptionMessage,
    
    // Settlement operations (stateless - take LockedMessage[])
    complete,
    abandon,
    deadLetter,
    
    // Send operations
    sendQueueMessage,
    sendTopicMessage,
    sendScheduledQueueMessage,
    sendScheduledTopicMessage,
    
    // Destructive operations
    purgeQueue,
    purgeSubscription,
    
    // Monitor operations
    monitorQueue,
    monitorSubscription
};

export {
    // Read operations
    peekQueueMessages,
    peekSubscriptionMessages,
    receiveAndLockQueueMessage,
    receiveAndLockSubscriptionMessage,
    
    // Settlement operations (stateless - take LockedMessage[])
    complete,
    abandon,
    deadLetter,
    
    // Send operations
    sendQueueMessage,
    sendTopicMessage,
    sendScheduledQueueMessage,
    sendScheduledTopicMessage,
    
    // Destructive operations
    purgeQueue,
    purgeSubscription,
    
    // Monitor operations
    monitorQueue,
    monitorSubscription
};
