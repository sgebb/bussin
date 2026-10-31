import { ServiceBusConnection } from './connection.js';
import { ManagementClient } from './managementClient.js';
import { MessageReceiver } from './messageReceiver.js';
import { parseServiceBusMessage } from './messageParser.js';
import { formatAmqpError } from './types.js';
import { settleBySequence } from './peekOperations.js';
import type { SettleBySequenceResult } from './peekOperations.js';
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
    fromDeadLetter: boolean = false,
    requiresSession: boolean = false
): Promise<PurgeController> {
    const entityPath = fromDeadLetter ? `${queueName}/$DeadLetterQueue` : queueName;
    return await purgeEntity(namespace, entityPath, token, onProgress, requiresSession);
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
    fromDeadLetter: boolean = false,
    requiresSession: boolean = false
): Promise<PurgeController> {
    const subscriptionPath = `${topicName}/subscriptions/${subscriptionName}`;
    const entityPath = fromDeadLetter ? `${subscriptionPath}/$DeadLetterQueue` : subscriptionPath;
    return await purgeEntity(namespace, entityPath, token, onProgress, requiresSession);
}

// Batch delete accepts at most 4000 messages per request on Premium and 500 on Standard. The smaller size is
// tried before falling back to receivers, and is kept for the rest of the purge.
const BATCH_DELETE_SIZES = [4000, 500];
const PARALLEL_RECEIVERS = 4;
const RECEIVER_BATCH_SIZE = 2000;
const MAX_THROTTLE_RETRIES = 8;

function isThrottled(err: unknown): boolean {
    return /server-busy|serverbusy|throttl|\b503\b/i.test((err as Error)?.message ?? String(err));
}

function isBatchTooLarge(err: unknown): boolean {
    return /argument-out-of-range/i.test((err as Error)?.message ?? String(err));
}

// Internal implementation
async function purgeEntity(
    namespace: string,
    entityPath: string,
    token: string,
    onProgress: ProgressCallback | null = null,
    requiresSession: boolean = false
): Promise<PurgeController> {
    const connection = new ServiceBusConnection(namespace, token);
    let isRunning = true;
    let deletedCount = 0;
    // Messages that arrive while the purge runs are left alone, so a busy producer can't keep it going forever
    const purgeStartedAt = new Date();

    try {
        await connection.connect();
        await connection.authenticateCBS(entityPath);
    } catch (err) {
        connection.close();
        throw new Error(`Failed to start purge: ${(err as Error).message}`);
    }

    const report = (count: number) => {
        deletedCount += count;
        if (onProgress && count > 0) {
            onProgress(deletedCount);
        }
    };

    const sleepWhileRunning = async (ms: number) => {
        const until = Date.now() + ms;
        while (isRunning && Date.now() < until) {
            await new Promise(r => setTimeout(r, Math.min(250, until - Date.now())));
        }
    };

    // A busy namespace (shared Standard tier especially) answers with ServerBusy: back off and retry
    // instead of failing. Returns 0 if the purge is stopped while waiting.
    const retryThrottled = async (op: () => Promise<number>): Promise<number> => {
        for (let failures = 0; ;) {
            const countBefore = deletedCount;
            try {
                return await op();
            } catch (err) {
                if (!isThrottled(err)) throw err;
                if (deletedCount > countBefore) failures = 0;
                if (++failures > MAX_THROTTLE_RETRIES) {
                    throw new Error(`Namespace kept throttling after ${MAX_THROTTLE_RETRIES} retries: ${(err as Error).message}`);
                }
                const delay = Math.min(1000 * 2 ** (failures - 1), 30000);
                console.log(`[Purge] Namespace is throttling, retrying in ${delay}ms (${(err as Error).message})`);
                await sleepWhileRunning(delay);
                if (!isRunning) return 0;
            }
        }
    };

    // Server-side batch delete (what the Azure Portal uses). Returns false if the entity rejects it,
    // for example a partitioned entity, which batch delete does not support.
    const purgeWithBatchDelete = async (): Promise<boolean> => {
        const managementClient = new ManagementClient(connection, entityPath);
        await managementClient.open();
        try {
            let sizeIndex = 0;
            while (isRunning) {
                const batchSize = BATCH_DELETE_SIZES[sizeIndex];
                let count: number;
                try {
                    count = await retryThrottled(() => managementClient.batchDeleteMessages(batchSize, purgeStartedAt));
                } catch (err) {
                    if (isThrottled(err)) throw err;
                    // Standard namespaces reject batches above 500 with argument-out-of-range
                    if (isBatchTooLarge(err) && sizeIndex + 1 < BATCH_DELETE_SIZES.length) {
                        sizeIndex++;
                        console.log(`[Purge] This namespace allows smaller batches, deleting ${BATCH_DELETE_SIZES[sizeIndex]} at a time`);
                        continue;
                    }
                    console.log(`[Purge] Batch delete is not available here (${(err as Error).message})`);
                    return false;
                }
                report(count);
                console.log(`[Purge] Batch delete: ${count} messages (total: ${deletedCount})`);
                // The service can delete fewer than asked while more remain, so only an empty batch means done
                if (count === 0) break;
            }
            return true;
        } finally {
            managementClient.close();
        }
    };

    // Lock the next available session and receive-and-delete until it goes quiet. Resolves 0 if no session is available.
    const drainNextSession = (sessionConn: ServiceBusConnection) => new Promise<number>((resolve, reject) => {
        let messagesInSession = 0;
        let sessionTimeout: ReturnType<typeof setTimeout> | null = null;
        let done = false;

        const rx = sessionConn.connection!.open_receiver({
            source: {
                address: entityPath,
                filter: {
                    'com.microsoft:session-filter': null // next available
                }
            },
            credit_window: 0,
            autoaccept: true,
            rcv_settle_mode: 0 // Receive and Delete
        });

        const closeAndResolve = () => {
            if (done) return;
            done = true;
            if (sessionTimeout) clearTimeout(sessionTimeout);
            rx.close();
            resolve(messagesInSession);
        };

        rx.on('message', () => {
            if (sessionTimeout) clearTimeout(sessionTimeout);
            messagesInSession++;
            report(1);
            rx.add_credit(1);
            sessionTimeout = setTimeout(closeAndResolve, 500);
        });

        rx.on('receiver_error', (context) => {
            if (sessionTimeout) clearTimeout(sessionTimeout);
            done = true;
            rx.close();
            const err = context.receiver.error ? formatAmqpError(context.receiver.error) : '';
            if (err.includes('timeout') || err.includes('Timeout') || err.includes('no-session') || err.includes('No session available')) {
                resolve(0);
            } else {
                reject(new Error(`Session receiver error: ${err}`));
            }
        });

        rx.add_credit(1);
        sessionTimeout = setTimeout(closeAndResolve, 2000);
    });

    const purgeSessions = async () => {
        const sessionConn = new ServiceBusConnection(namespace, token);
        try {
            await sessionConn.connect();
            await sessionConn.authenticateCBS(entityPath);

            // Stop after 3 consecutive attempts yielding 0 messages
            let consecutiveEmptySessions = 0;
            while (isRunning && consecutiveEmptySessions < 3) {
                const sessionDeleted = await retryThrottled(() => drainNextSession(sessionConn));
                if (sessionDeleted > 0) {
                    consecutiveEmptySessions = 0;
                    console.log(`[Purge] Drained session: deleted ${sessionDeleted} messages (total: ${deletedCount})`);
                } else {
                    consecutiveEmptySessions++;
                }
            }
        } finally {
            sessionConn.close();
        }
    };

    // Receive-and-delete on one connection until 3 consecutive batches come back empty
    const drainWithReceiver = (conn: ServiceBusConnection, aborted: () => boolean) => new Promise<void>((resolve, reject) => {
        const receiver = new MessageReceiver(conn, entityPath, {
            peekMode: false,  // Receive and delete mode
            maxMessages: null,  // Continuous
            autoClose: false
        });
        let pending = 0;
        let consecutiveEmptyBatches = 0;
        let hasReceivedAnyMessages = false;
        let batchTimeout: ReturnType<typeof setTimeout> | null = null;

        const processBatch = () => {
            if (pending > 0) {
                report(pending);
                pending = 0;
                consecutiveEmptyBatches = 0;
            } else {
                consecutiveEmptyBatches++;
            }

            if (!isRunning || aborted() || consecutiveEmptyBatches >= 3) {
                receiver.close();
                resolve();
                return;
            }

            receiver.add_credit(RECEIVER_BATCH_SIZE);
            // Wait longer for the first messages and once batches start coming back empty
            const timeout = !hasReceivedAnyMessages ? 1000 : (consecutiveEmptyBatches > 0 ? 1500 : 300);
            batchTimeout = setTimeout(processBatch, timeout);
        };

        receiver.receive(
            () => {
                if (batchTimeout) clearTimeout(batchTimeout);
                pending++;
                hasReceivedAnyMessages = true;

                if (pending >= RECEIVER_BATCH_SIZE) {
                    report(pending);
                    pending = 0;
                    consecutiveEmptyBatches = 0;
                    if (isRunning) {
                        receiver.add_credit(RECEIVER_BATCH_SIZE);
                    }
                } else {
                    batchTimeout = setTimeout(processBatch, 50);
                }
            },
            (error: Error) => {
                if (batchTimeout) clearTimeout(batchTimeout);
                receiver.close();
                report(pending); // received in receive-and-delete mode, so already gone
                pending = 0;
                reject(new Error(`Purge failed: ${error.message}`));
            }
        );

        receiver.add_credit(RECEIVER_BATCH_SIZE);
        batchTimeout = setTimeout(processBatch, 1000);
    });

    // Parallel receivers, each on its own connection for true parallelism. Returns the number deleted.
    const drainWithReceivers = async (): Promise<number> => {
        const countBefore = deletedCount;
        const connections: ServiceBusConnection[] = [];
        let aborted = false;
        try {
            for (let i = 0; i < PARALLEL_RECEIVERS; i++) {
                const conn = new ServiceBusConnection(namespace, token);
                connections.push(conn);
                await conn.connect();
                await conn.authenticateCBS(entityPath);
            }
            await Promise.all(connections.map(conn => drainWithReceiver(conn, () => aborted)));
            return deletedCount - countBefore;
        } finally {
            aborted = true;
            connections.forEach(conn => conn.close());
        }
    };

    /** Whether the entity still holds a message that was there when the purge started. */
    const hasMessagesLeft = async (): Promise<boolean> => {
        const managementClient = new ManagementClient(connection, entityPath);
        await managementClient.open();
        try {
            const [first] = (await managementClient.peekMessages(0, 1)).map(parseServiceBusMessage);
            return !!first && (!first.enqueuedTime || new Date(first.enqueuedTime) < purgeStartedAt);
        } finally {
            managementClient.close();
        }
    };

    // Receivers conclude the entity is empty after a few seconds without messages, which a slow or busy
    // namespace also causes. Keep draining until a peek confirms nothing from before the purge is left.
    const purgeWithReceivers = async () => {
        while (isRunning) {
            const deleted = await retryThrottled(drainWithReceivers);
            if (!isRunning || !(await hasMessagesLeft())) return;
            if (deleted === 0) {
                throw new Error(`Deleted ${deletedCount} messages, but the rest can't be received: they are scheduled, deferred or locked by another receiver`);
            }
            console.log(`[Purge] Messages remain after ${deletedCount} deleted, draining again...`);
        }
    };

    const run = async (): Promise<number> => {
        try {
            if (await purgeWithBatchDelete()) {
                return deletedCount;
            }
            if (requiresSession) {
                console.log('[Purge] Falling back to session-by-session receive-and-delete...');
                await purgeSessions();
            } else {
                console.log(`[Purge] Falling back to ${PARALLEL_RECEIVERS} parallel receivers...`);
                await purgeWithReceivers();
            }
            console.log(`[Purge] Complete. Total deleted: ${deletedCount}`);
            return deletedCount;
        } finally {
            connection.close();
        }
    };

    return {
        promise: run(),
        stop: () => {
            isRunning = false;
            return deletedCount;
        },
        getCount: () => deletedCount
    };
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
 * Delete messages from a queue by sequence numbers
 * @param fromDeadLetter - If true, deletes from the dead letter queue
 */
export async function deleteQueueMessagesBySequence(
    namespace: string,
    queueName: string,
    token: string,
    sequenceNumbers: number[],
    fromDeadLetter: boolean = false,
    sessionId?: string
): Promise<SettleBySequenceResult> {
    const entityPath = fromDeadLetter ? `${queueName}/$DeadLetterQueue` : queueName;
    return await settleBySequence(namespace, entityPath, token, sequenceNumbers, 'complete', {}, sessionId);
}

/**
 * Delete messages from a subscription by sequence numbers
 * @param fromDeadLetter - If true, deletes from the dead letter queue
 */
export async function deleteSubscriptionMessagesBySequence(
    namespace: string,
    topicName: string,
    subscriptionName: string,
    token: string,
    sequenceNumbers: number[],
    fromDeadLetter: boolean = false,
    sessionId?: string
): Promise<SettleBySequenceResult> {
    const subscriptionPath = `${topicName}/subscriptions/${subscriptionName}`;
    const entityPath = fromDeadLetter ? `${subscriptionPath}/$DeadLetterQueue` : subscriptionPath;
    return await settleBySequence(namespace, entityPath, token, sequenceNumbers, 'complete', {}, sessionId);
}

/**
 * Dead letter messages from a queue by sequence numbers
 */
export async function deadLetterQueueMessagesBySequence(
    namespace: string,
    queueName: string,
    token: string,
    sequenceNumbers: number[],
    reason: string = 'Manual dead letter',
    description: string = 'Moved by user',
    sessionId?: string
): Promise<SettleBySequenceResult> {
    return await settleBySequence(namespace, queueName, token, sequenceNumbers, 'deadLetter',
        { deadLetterReason: reason, deadLetterErrorDescription: description }, sessionId);
}

/**
 * Dead letter messages from a subscription by sequence numbers
 */
export async function deadLetterSubscriptionMessagesBySequence(
    namespace: string,
    topicName: string,
    subscriptionName: string,
    token: string,
    sequenceNumbers: number[],
    reason: string = 'Manual dead letter',
    description: string = 'Moved by user',
    sessionId?: string
): Promise<SettleBySequenceResult> {
    const subscriptionPath = `${topicName}/subscriptions/${subscriptionName}`;
    return await settleBySequence(namespace, subscriptionPath, token, sequenceNumbers, 'deadLetter',
        { deadLetterReason: reason, deadLetterErrorDescription: description }, sessionId);
}
