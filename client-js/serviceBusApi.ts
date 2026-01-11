/**
 * Service Bus Client API
 * High-level API for Azure Service Bus operations
 */

import { ServiceBusConnection } from './src/connection.js';
import { ManagementClient } from './src/managementClient.js';
import { MessageReceiver } from './src/messageReceiver.js';
import { MessageSender } from './src/messageSender.js';
import { parseServiceBusMessage } from './src/messageParser.js';
import { types } from 'rhea'; // Import types for wrapping symbols
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
    messageBody: string | object | Uint8Array | ArrayBuffer | null | undefined,
    properties: MessageProperties = {}
): Promise<void> {
    const connection = new ServiceBusConnection(namespace, token);

    try {
        await connection.connect();
        await connection.authenticateCBS(entityPath);

        const sender = new MessageSender(connection, entityPath);
        await sender.open();
        const messageProps: MessageProperties = { ...properties };
        let bodyToSend: string;

        if (typeof messageBody === 'string') {
            bodyToSend = messageBody;
        } else if (messageBody instanceof Uint8Array) {
            bodyToSend = new TextDecoder().decode(messageBody);
        } else if (messageBody instanceof ArrayBuffer) {
            bodyToSend = new TextDecoder().decode(new Uint8Array(messageBody));
        } else if (messageBody !== null && messageBody !== undefined) {
            bodyToSend = JSON.stringify(messageBody);
            if (!messageProps.content_type && !messageProps.contentType) {
                messageProps.content_type = 'application/json; charset=utf-8';
            }
        } else {
            bodyToSend = '';
        }

        await sender.send(bodyToSend, messageProps);

        sender.close();
        connection.close();
    } catch (err) {
        connection.close();
        throw new Error(`Send failed: ${(err as Error).message}`);
    }
}

/**
 * Send multiple messages to a queue in batch (single connection)
 */
async function sendQueueMessageBatch(
    namespace: string,
    queueName: string,
    token: string,
    messages: { body: string | object | null | undefined; properties?: MessageProperties }[]
): Promise<void> {
    return await sendMessageBatch(namespace, queueName, token, messages);
}

/**
 * Send multiple messages to a topic in batch (single connection)
 */
async function sendTopicMessageBatch(
    namespace: string,
    topicName: string,
    token: string,
    messages: { body: string | object | null | undefined; properties?: MessageProperties }[]
): Promise<void> {
    return await sendMessageBatch(namespace, topicName, token, messages);
}

// Internal batch send implementation
async function sendMessageBatch(
    namespace: string,
    entityPath: string,
    token: string,
    messages: { body: string | object | null | undefined; properties?: MessageProperties }[]
): Promise<void> {
    const connection = new ServiceBusConnection(namespace, token);

    try {
        await connection.connect();
        await connection.authenticateCBS(entityPath);

        const sender = new MessageSender(connection, entityPath);
        await sender.open();

        const preparedMessages = messages.map(msg => {
            const messageProps: MessageProperties = { ...msg.properties };
            let bodyToSend: string;

            if (typeof msg.body === 'string') {
                bodyToSend = msg.body;
            } else if (msg.body !== null && msg.body !== undefined) {
                bodyToSend = JSON.stringify(msg.body);
                if (!messageProps.content_type && !messageProps.contentType) {
                    messageProps.content_type = 'application/json; charset=utf-8';
                }
            } else {
                bodyToSend = '';
            }

            return { body: bodyToSend, properties: messageProps };
        });

        await sender.sendBatch(preparedMessages);

        sender.close();
        connection.close();
    } catch (err) {
        connection.close();
        throw new Error(`Batch send failed: ${(err as Error).message}`);
    }
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

// Internal implementation - OPTIMIZED VERSION
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

        // Strategy 1: Try Management API Batch Delete (FASTEST - what Azure Portal uses)
        // This can delete up to 4000 messages per call server-side
        const useBatchDeleteAPI = true; // Can be made configurable

        const purgePromise = new Promise<number>(async (resolve, reject) => {
            try {
                if (useBatchDeleteAPI) {
                    console.log('[Purge] Attempting FAST batch delete API (portal method)...');

                    const managementClient = new ManagementClient(connection, entityPath);
                    await managementClient.open();

                    // Delete in batches of 4000 (Premium tier limit) or 500 (Standard tier)
                    // Start with 4000 and if it fails, we'll catch and fall back
                    const batchSize = 4000;
                    let totalDeleted = 0;
                    let lastBatchCount = 0;

                    do {
                        if (!isRunning) break;

                        try {
                            lastBatchCount = await managementClient.purgeMessages(batchSize);
                            totalDeleted += lastBatchCount;
                            deletedCount = totalDeleted;

                            if (onProgress && lastBatchCount > 0) {
                                onProgress(totalDeleted);
                            }

                            console.log(`[Purge] Batch delete: ${lastBatchCount} messages (total: ${totalDeleted})`);

                            // If we got less than batch size, we're done
                            if (lastBatchCount < batchSize) {
                                break;
                            }
                        } catch (batchErr: any) {
                            // If batch delete is not supported or fails, fall back to parallel receivers
                            console.log(`[Purge] Batch delete failed (${batchErr.message}), falling back to parallel receivers...`);
                            managementClient.close();

                            // Fall through to Strategy 2
                            throw new Error('FALLBACK_TO_PARALLEL');
                        }
                    } while (isRunning && lastBatchCount > 0);

                    managementClient.close();
                    connection.close();
                    resolve(totalDeleted);
                    return;
                }
            } catch (err: any) {
                // If batch delete not supported or failed, fall back to parallel receivers
                if (err.message === 'FALLBACK_TO_PARALLEL' || !useBatchDeleteAPI) {
                    console.log('[Purge] Using parallel receiver strategy...');
                    // Fall through to Strategy 2
                } else {
                    throw err;
                }
            }

            // Strategy 2: Parallel Receivers (FAST - 4-5x faster than single receiver)
            const parallelReceivers = 4; // Use 4 parallel receivers for 4x speed
            const receivers: MessageReceiver[] = [];
            const receiverPromises: Promise<number>[] = [];
            const receiverCounts: number[] = new Array(parallelReceivers).fill(0);

            console.log(`[Purge] Starting ${parallelReceivers} parallel receivers...`);

            for (let i = 0; i < parallelReceivers; i++) {
                // Each receiver needs its own connection for true parallelism
                const conn = new ServiceBusConnection(namespace, token);
                await conn.connect();
                await conn.authenticateCBS(entityPath);

                const receiver = new MessageReceiver(conn, entityPath, {
                    peekMode: false,  // Receive and delete mode
                    maxMessages: null,  // Continuous
                    autoClose: false
                });

                receivers.push(receiver);

                const receiverPromise = new Promise<number>((resolveReceiver, rejectReceiver) => {
                    const batchSize = 2000; // 2000 messages per receiver batch
                    let batchMessages: any[] = [];
                    let consecutiveEmptyBatches = 0;
                    const maxEmptyBatches = 3;
                    let batchTimeout: NodeJS.Timeout | null = null;
                    let hasReceivedAnyMessages = false;

                    const processBatch = () => {
                        if (batchMessages.length > 0) {
                            receiverCounts[i] += batchMessages.length;
                            deletedCount = receiverCounts.reduce((sum, count) => sum + count, 0);

                            if (onProgress) {
                                onProgress(deletedCount);
                            }

                            console.log(`[Purge-R${i}] Deleted ${batchMessages.length} messages (receiver total: ${receiverCounts[i]}, global: ${deletedCount})`);
                            batchMessages = [];
                            consecutiveEmptyBatches = 0;
                        } else {
                            consecutiveEmptyBatches++;
                        }

                        if (!isRunning || consecutiveEmptyBatches >= maxEmptyBatches) {
                            receiver.close();
                            conn.close();
                            resolveReceiver(receiverCounts[i]);
                        } else {
                            // Request next batch
                            receiver.add_credit(batchSize);

                            // Set timeout - shorter if we've seen messages
                            const timeout = !hasReceivedAnyMessages ? 1000 : (consecutiveEmptyBatches > 0 ? 1500 : 300);
                            batchTimeout = setTimeout(() => {
                                processBatch();
                            }, timeout);
                        }
                    };

                    receiver.receive(
                        (message: any) => {
                            if (batchTimeout) {
                                clearTimeout(batchTimeout);
                                batchTimeout = null;
                            }

                            batchMessages.push(message);
                            hasReceivedAnyMessages = true;

                            // If we hit batch size, process immediately
                            if (batchMessages.length >= batchSize) {
                                receiverCounts[i] += batchMessages.length;
                                deletedCount = receiverCounts.reduce((sum, count) => sum + count, 0);

                                if (onProgress) {
                                    onProgress(deletedCount);
                                }

                                batchMessages = [];
                                consecutiveEmptyBatches = 0;

                                if (isRunning) {
                                    receiver.add_credit(batchSize);
                                }
                            } else {
                                // Set short timeout for partial batch
                                if (batchTimeout) clearTimeout(batchTimeout);
                                batchTimeout = setTimeout(() => {
                                    processBatch();
                                }, 50);
                            }
                        },
                        (error: any) => {
                            if (batchTimeout) {
                                clearTimeout(batchTimeout);
                            }
                            receiver.close();
                            conn.close();

                            let errorMsg = 'Unknown error';
                            if (error) {
                                if (typeof error === 'string') {
                                    errorMsg = error;
                                } else if (error.message) {
                                    errorMsg = error.message;
                                } else if (error.description) {
                                    errorMsg = error.description;
                                } else if (error.condition) {
                                    errorMsg = error.condition;
                                } else if (error.toString && error.toString() !== '[object Object]') {
                                    errorMsg = error.toString();
                                }
                            }

                            rejectReceiver(new Error(`Purge failed: ${errorMsg}`));
                        }
                    );

                    // Start receiving first batch
                    receiver.add_credit(batchSize);

                    // Set initial timeout
                    batchTimeout = setTimeout(() => {
                        processBatch();
                    }, 1000);
                });

                receiverPromises.push(receiverPromise);
            }

            // Wait for all receivers to complete
            try {
                await Promise.all(receiverPromises);
                const totalDeleted = receiverCounts.reduce((sum, count) => sum + count, 0);
                console.log(`[Purge] All receivers complete. Total deleted: ${totalDeleted}`);
                resolve(totalDeleted);
            } catch (err) {
                // Clean up any remaining receivers
                receivers.forEach((r, idx) => {
                    try {
                        r.close();
                    } catch { }
                });
                reject(err);
            } finally {
                connection.close();
            }
        });

        return {
            promise: purgePromise,
            stop: () => {
                isRunning = false;
                return deletedCount;
            },
            getCount: () => deletedCount
        };
    } catch (err) {
        connection.close();
        throw new Error(`Failed to start purge: ${(err as Error).message}`);
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

// Internal implementation - non-destructive monitoring using Management API
async function startMonitoring(
    namespace: string,
    entityPath: string,
    token: string,
    onMessage: MessageCallback,
    onError?: ErrorCallback
): Promise<MonitorController> {
    const connection = new ServiceBusConnection(namespace, token);
    let isRunning = true;
    let lastSequenceNumber = 0;
    let pollInterval: NodeJS.Timeout | null = null;

    const pollForMessages = async () => {
        if (!isRunning) return;

        try {
            await connection.connect();
            await connection.authenticateCBS(entityPath);

            const managementClient = new ManagementClient(connection, entityPath);
            await managementClient.open();

            // Peek messages starting from the last sequence number + 1
            const messages = await managementClient.peekMessages(lastSequenceNumber + 1, 10);

            managementClient.close();
            connection.close();

            // Process any new messages
            for (const message of messages) {
                if (message.message_annotations?.['x-opt-sequence-number']) {
                    const seqNum = message.message_annotations['x-opt-sequence-number'];
                    if (seqNum > lastSequenceNumber) {
                        lastSequenceNumber = seqNum;
                        onMessage(parseServiceBusMessage(message));
                    }
                }
            }

            // Schedule next poll if still running (faster polling for better responsiveness)
            if (isRunning) {
                pollInterval = setTimeout(pollForMessages, messages.length > 0 ? 500 : 2000); // Faster when messages are flowing
            }

        } catch (err) {
            connection.close();
            if (onError && isRunning) {
                onError(err as Error);
            }
            // Retry connection after error
            if (isRunning) {
                pollInterval = setTimeout(pollForMessages, 5000); // Wait 5 seconds before retry
            }
        }
    };

    // Start monitoring
    try {
        await connection.connect();
        await connection.authenticateCBS(entityPath);

        const managementClient = new ManagementClient(connection, entityPath);
        await managementClient.open();

        // Get initial sequence number to start from (try to get the latest message)
        const initialMessages = await managementClient.peekMessages(0, 100); // Get more messages to find the latest
        if (initialMessages.length > 0) {
            // Find the highest sequence number
            for (const msg of initialMessages) {
                if (msg.message_annotations?.['x-opt-sequence-number']) {
                    const seqNum = msg.message_annotations['x-opt-sequence-number'];
                    if (seqNum > lastSequenceNumber) {
                        lastSequenceNumber = seqNum;
                    }
                }
            }
        }

        managementClient.close();
        connection.close();

        // Start polling
        pollInterval = setTimeout(pollForMessages, 100);

    } catch (err) {
        connection.close();
        if (onError) {
            onError(err as Error);
        }
        throw new Error(`Monitor failed: ${(err as Error).message}`);
    }

    return {
        stop: () => {
            isRunning = false;
            if (pollInterval) {
                clearTimeout(pollInterval);
            }
            connection.close();
        }
    };
}

// ============================================================================
// DELETE BY SEQUENCE NUMBER (Management API)
// ============================================================================

/**
 * Delete messages from a queue by sequence numbers (direct, no lock needed)
 * @param fromDeadLetter - If true, deletes from the dead letter queue
 */
async function deleteQueueMessagesBySequence(
    namespace: string,
    queueName: string,
    token: string,
    sequenceNumbers: number[],
    fromDeadLetter: boolean = false
): Promise<void> {
    const entityPath = fromDeadLetter ? `${queueName}/$DeadLetterQueue` : queueName;
    return await deleteMessagesBySequence(namespace, entityPath, token, sequenceNumbers);
}

/**
 * Delete messages from a subscription by sequence numbers (direct, no lock needed)
 * @param fromDeadLetter - If true, deletes from the dead letter queue
 */
async function deleteSubscriptionMessagesBySequence(
    namespace: string,
    topicName: string,
    subscriptionName: string,
    token: string,
    sequenceNumbers: number[],
    fromDeadLetter: boolean = false
): Promise<void> {
    const subscriptionPath = `${topicName}/subscriptions/${subscriptionName}`;
    const entityPath = fromDeadLetter ? `${subscriptionPath}/$DeadLetterQueue` : subscriptionPath;
    return await deleteMessagesBySequence(namespace, entityPath, token, sequenceNumbers);
}

// Internal implementation
async function deleteMessagesBySequence(
    namespace: string,
    entityPath: string,
    token: string,
    sequenceNumbers: number[]
): Promise<void> {
    const connection = new ServiceBusConnection(namespace, token);

    try {
        await connection.connect();
        await connection.authenticateCBS(entityPath);

        const managementClient = new ManagementClient(connection, entityPath);
        await managementClient.open();

        await managementClient.receiveAndDeleteBySequenceNumbers(sequenceNumbers);

        managementClient.close();
        connection.close();
    } catch (err) {
        connection.close();
        throw new Error(`Delete by sequence failed: ${(err as Error).message}`);
    }
}

// ============================================================================
// DEAD LETTER BY SEQUENCE NUMBER (Management API)
// ============================================================================

/**
 * Dead letter messages from a queue by sequence numbers (direct, no FIFO lock needed)
 */
async function deadLetterQueueMessagesBySequence(
    namespace: string,
    queueName: string,
    token: string,
    sequenceNumbers: number[],
    reason: string = 'Manual dead letter',
    description: string = 'Moved by user'
): Promise<void> {
    return await deadLetterMessagesBySequence(namespace, queueName, token, sequenceNumbers, reason, description);
}

/**
 * Dead letter messages from a subscription by sequence numbers (direct, no FIFO lock needed)
 */
async function deadLetterSubscriptionMessagesBySequence(
    namespace: string,
    topicName: string,
    subscriptionName: string,
    token: string,
    sequenceNumbers: number[],
    reason: string = 'Manual dead letter',
    description: string = 'Moved by user'
): Promise<void> {
    const subscriptionPath = `${topicName}/subscriptions/${subscriptionName}`;
    return await deadLetterMessagesBySequence(namespace, subscriptionPath, token, sequenceNumbers, reason, description);
}

// Internal implementation
async function deadLetterMessagesBySequence(
    namespace: string,
    entityPath: string,
    token: string,
    sequenceNumbers: number[],
    reason: string,
    description: string
): Promise<void> {
    const connection = new ServiceBusConnection(namespace, token);

    try {
        await connection.connect();
        await connection.authenticateCBS(entityPath);

        const managementClient = new ManagementClient(connection, entityPath);
        await managementClient.open();

        // Lock by sequence number (returns lock tokens for exactly those messages)
        const locked = await managementClient.lockBySequenceNumbers(sequenceNumbers);

        if (locked.length > 0) {
            // Dead letter using update-disposition
            const lockTokens = locked.map(l => l.lockToken);
            await managementClient.updateDisposition(lockTokens, 'suspended', reason, description);
        }

        managementClient.close();
        connection.close();
    } catch (err) {
        connection.close();
        throw new Error(`Dead letter by sequence failed: ${(err as Error).message}`);
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
    sendQueueMessageBatch,
    sendTopicMessageBatch,

    // Destructive operations
    purgeQueue,
    purgeSubscription,
    deleteQueueMessagesBySequence,
    deleteSubscriptionMessagesBySequence,
    deadLetterQueueMessagesBySequence,
    deadLetterSubscriptionMessagesBySequence,

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
    sendQueueMessageBatch,
    sendTopicMessageBatch,

    // Destructive operations
    purgeQueue,
    purgeSubscription,
    deleteQueueMessagesBySequence,
    deleteSubscriptionMessagesBySequence,
    deadLetterQueueMessagesBySequence,
    deadLetterSubscriptionMessagesBySequence,

    // Monitor operations
    monitorQueue,
    monitorSubscription
};
