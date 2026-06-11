import { ServiceBusConnection } from './connection.js';
import { ManagementClient } from './managementClient.js';
import { MessageReceiver } from './messageReceiver.js';
import type { ProgressCallback, PurgeController } from './types.js';

/**
 * Purge all messages from a queue (receive and delete in loop)
 * @param fromDeadLetter - If true, purges the dead letter queue
 */
export async function purgeQueue(
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
export async function purgeSubscription(
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

        const purgePromise = new Promise<number>(async (resolve, reject) => {
            try {
                {
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
                if (err.message === 'FALLBACK_TO_PARALLEL') {
                    console.log('[Purge] Using parallel receiver strategy...');
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
 * Direct version of purgeQueue for C# interop that returns the result directly
 */
export async function purgeQueueDirect(
    namespace: string,
    queueName: string,
    token: string,
    fromDeadLetter: boolean = false
): Promise<any> {
    const controller = await purgeQueue(namespace, queueName, token, null, fromDeadLetter);
    const count = await controller.promise;
    return { deletedCount: count };
}

/**
 * Direct version of purgeSubscription for C# interop that returns the result directly
 */
export async function purgeSubscriptionDirect(
    namespace: string,
    topicName: string,
    subscriptionName: string,
    token: string,
    fromDeadLetter: boolean = false
): Promise<any> {
    const controller = await purgeSubscription(namespace, topicName, subscriptionName, token, null, fromDeadLetter);
    const count = await controller.promise;
    return { deletedCount: count };
}

/**
 * Delete messages from a queue by sequence numbers (direct, no lock needed)
 * @param fromDeadLetter - If true, deletes from the dead letter queue
 */
export async function deleteQueueMessagesBySequence(
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
export async function deleteSubscriptionMessagesBySequence(
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

/**
 * Dead letter messages from a queue by sequence numbers (direct, no FIFO lock needed)
 */
export async function deadLetterQueueMessagesBySequence(
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
export async function deadLetterSubscriptionMessagesBySequence(
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
