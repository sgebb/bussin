import rhea from 'rhea';
import { ServiceBusConnection } from './connection.js';
import { ManagementClient } from './managementClient.js';
import { parseServiceBusMessage } from './messageParser.js';
import type {
    ServiceBusMessage,
    LockedMessage,
    DeadLetterOptions,
    BatchOperationResult
} from './types.js';
import { formatAmqpError } from './types.js';

// Store active message handles by lock token (necessary for settlement)
// This is minimal state - just the AMQP handles that can't serialize
const messageHandles = new Map<string, {
    delivery?: any;
    receiver?: any;
    connection?: any;
    isManagementLock?: boolean;
    namespace?: string;
    entityPath?: string;
    token?: string;
}>();

/**
 * Peek messages from a queue (read-only, no side effects)
 * @param fromDeadLetter - If true, peeks from the dead letter queue
 */
export async function peekQueueMessages(
    namespace: string,
    queueName: string,
    token: string,
    count: number = 10,
    fromSequence: number = 0,
    fromDeadLetter: boolean = false,
    sessionId?: string
): Promise<ServiceBusMessage[]> {
    const entityPath = fromDeadLetter ? `${queueName}/$DeadLetterQueue` : queueName;
    return await peekMessages(namespace, entityPath, token, count, fromSequence, sessionId);
}

/**
 * Peek messages from a subscription (read-only, no side effects)
 * @param fromDeadLetter - If true, peeks from the dead letter queue
 */
export async function peekSubscriptionMessages(
    namespace: string,
    topicName: string,
    subscriptionName: string,
    token: string,
    count: number = 10,
    fromSequence: number = 0,
    fromDeadLetter: boolean = false,
    sessionId?: string
): Promise<ServiceBusMessage[]> {
    const subscriptionPath = `${topicName}/subscriptions/${subscriptionName}`;
    const entityPath = fromDeadLetter ? `${subscriptionPath}/$DeadLetterQueue` : subscriptionPath;
    return await peekMessages(namespace, entityPath, token, count, fromSequence, sessionId);
}

// Internal implementation
async function peekMessages(
    namespace: string,
    entityPath: string,
    token: string,
    count: number = 10,
    fromSequence: number = 0,
    sessionId?: string
): Promise<ServiceBusMessage[]> {
    const connection = new ServiceBusConnection(namespace, token);

    try {
        await connection.connect();
        await connection.authenticateCBS(entityPath);

        const managementClient = new ManagementClient(connection, entityPath);
        await managementClient.open();

        const messages = await managementClient.peekMessages(fromSequence, count, sessionId);

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
 * Receive and lock a message from a queue (peek-lock mode)
 * Message stays in queue but locked to you until completed/abandoned/expired
 * @param fromDeadLetter - If true, locks from the dead letter queue
 * @param count - Number of messages to lock (default 1, max 100)
 */
export async function receiveAndLockQueueMessage(
    namespace: string,
    queueName: string,
    token: string,
    timeoutSeconds: number = 5,
    fromDeadLetter: boolean = false,
    count: number = 1,
    sessionId?: string
): Promise<LockedMessage[]> {
    const entityPath = fromDeadLetter ? `${queueName}/$DeadLetterQueue` : queueName;
    return await receiveAndLockMessages(namespace, entityPath, token, timeoutSeconds, count, sessionId);
}

/**
 * Receive and lock a message from a subscription (peek-lock mode)
 * @param fromDeadLetter - If true, locks from the dead letter queue
 * @param count - Number of messages to lock (default 1, max 100)
 */
export async function receiveAndLockSubscriptionMessage(
    namespace: string,
    topicName: string,
    subscriptionName: string,
    token: string,
    timeoutSeconds: number = 5,
    fromDeadLetter: boolean = false,
    count: number = 1,
    sessionId?: string
): Promise<LockedMessage[]> {
    const subscriptionPath = `${topicName}/subscriptions/${subscriptionName}`;
    const entityPath = fromDeadLetter ? `${subscriptionPath}/$DeadLetterQueue` : subscriptionPath;
    return await receiveAndLockMessages(namespace, entityPath, token, timeoutSeconds, count, sessionId);
}

// Internal implementation - lock multiple messages with SINGLE connection
async function receiveAndLockMessages(
    namespace: string,
    entityPath: string,
    token: string,
    timeoutSeconds: number = 5,
    count: number = 1,
    sessionId?: string
): Promise<LockedMessage[]> {
    // First attempt: try without session filter (works for standard entities).
    // If the entity requires sessions, the broker will reject the link,
    // and we retry with a "next available session" filter.
    try {
        return await _receiveAndLockMessagesInternal(namespace, entityPath, token, timeoutSeconds, count, undefined);
    } catch (err) {
        const errorMsg = (err as Error).message || '';
        // If the entity requires sessions, retry with session filter
        if (errorMsg.includes('session') || errorMsg.includes('Session')) {
            console.log(`[ServiceBusAPI] Entity ${entityPath} requires sessions, retrying with session filter (sessionId: ${sessionId || 'next-available'})`);
            return await _receiveAndLockMessagesInternal(namespace, entityPath, token, timeoutSeconds, count, sessionId || null);
        }
        throw err;
    }
}

// Core receiver implementation
async function _receiveAndLockMessagesInternal(
    namespace: string,
    entityPath: string,
    token: string,
    timeoutSeconds: number = 5,
    count: number = 1,
    sessionId?: string | null
): Promise<LockedMessage[]> {
    const connection = new ServiceBusConnection(namespace, token);

    try {
        await connection.connect();
        await connection.authenticateCBS(entityPath);
        console.log(`[ServiceBusAPI] Auth success for receive: ${entityPath}`);

        return new Promise((resolve, reject) => {
            const lockedMsgs: LockedMessage[] = [];
            let messagesReceived = 0;
            let timedOut = false;
            let noMoreMessagesTimer: NodeJS.Timeout | null = null;

            console.log(`[ServiceBusAPI] Opening receiver for ${entityPath} (sessionId: ${sessionId === undefined ? 'none' : sessionId === null ? 'next-available' : sessionId})`);
            const source: any = { address: entityPath };
            // Only apply session filter when explicitly requested (non-undefined)
            if (sessionId !== undefined) {
                const filterMap: Record<string, any> = {};
                filterMap['com.microsoft:session-filter'] = sessionId;
                source.filter = filterMap;
            }

            // Use peek-lock mode (rcv_settle_mode: 1, autoaccept: false)
            const receiver = connection.connection!.open_receiver({
                source: source,
                credit_window: 0,  // Manual credit control
                autoaccept: false,
                rcv_settle_mode: 1  // Peek-lock mode
            });

            receiver.on('message', (context: any) => {
                if (timedOut) return;
                console.log(`[ServiceBusAPI] Message event received on ${entityPath}`);

                try {
                    // Clear the "no more messages" timer since we got one
                    if (noMoreMessagesTimer) {
                        clearTimeout(noMoreMessagesTimer);
                        noMoreMessagesTimer = null;
                    }

                    // Parse the message
                    const parsedMessage = parseServiceBusMessage(context.message) as LockedMessage;

                    // Generate unique lock token using random prefix and delivery tag to prevent collisions
                    const uniqueId = Math.random().toString(36).substring(2, 10);
                    const lockTokenArray = Array.from(context.delivery.tag as Iterable<number>);
                    const lockToken = `${uniqueId}-${lockTokenArray.map(b => b.toString(16).padStart(2, '0')).join('')}`;
                    console.log(`[ServiceBusAPI] Unique lock token generated: ${lockToken}`);
                    parsedMessage.lockToken = lockToken;

                    // Store handle in Map (can't serialize, so keep server-side)
                    messageHandles.set(lockToken, {
                        delivery: context.delivery,
                        receiver: receiver,
                        connection: connection
                    });

                    lockedMsgs.push(parsedMessage);
                    messagesReceived++;
                    console.log(`[ServiceBusAPI] Message processed: ${messagesReceived}/${count}`);

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
                } catch (procErr) {
                    console.error(`[ServiceBusAPI] Error processing message on ${entityPath}:`, procErr);
                }
            });

            receiver.on('receiver_error', (context: any) => {
                if (!timedOut) {
                    if (noMoreMessagesTimer) clearTimeout(noMoreMessagesTimer);
                    receiver.close();
                    connection.close();
                    reject(new Error(context.receiver.error ? formatAmqpError(context.receiver.error) : 'Receiver error'));
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
export async function complete(lockTokens: string[] | string): Promise<BatchOperationResult> {
    const tokens = Array.isArray(lockTokens) ? lockTokens : [lockTokens];
    console.log(`[ServiceBusAPI] complete called for ${tokens.length} tokens`);
    const result: BatchOperationResult = {
        successCount: 0,
        failureCount: 0,
        errors: []
    };

    const receiversToClose = new Set<any>();
    const connectionsToClose = new Set<any>();

    for (const lockToken of tokens) {
        try {
            const handle = messageHandles.get(lockToken);
            if (!handle) {
                console.error(`[ServiceBusAPI] Lock token ${lockToken} not found in messageHandles Map (size: ${messageHandles.size})`);
                throw new Error('Message not found or lock expired');
            }

            if (handle.isManagementLock) {
                const connection = new ServiceBusConnection(handle.namespace, handle.token);
                await connection.connect();
                await connection.authenticateCBS(handle.entityPath);
                const managementClient = new ManagementClient(connection, handle.entityPath);
                await managementClient.open();
                await managementClient.updateDisposition([lockToken], 'completed');
                await managementClient.close();
                await connection.close();
                messageHandles.delete(lockToken);
                result.successCount++;
                continue;
            }

            // Accept the delivery (complete/delete)
            handle.delivery.accept();

            // Cleanup
            if (!handle.isManagementLock) {
                receiversToClose.add(handle.receiver);
                connectionsToClose.add(handle.connection);
            }
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

    // Close standard handles that were settled
    for (const receiver of receiversToClose) {
        try { receiver.close(); } catch {}
    }
    for (const connection of connectionsToClose) {
        try { connection.close(); } catch {}
    }

    return result;
}

/**
 * Abandon locked messages by lock tokens - releases locks and returns messages to queue
 * @param lockTokens - Array of lock tokens from receiveAndLock
 */
export async function abandon(lockTokens: string[] | string): Promise<BatchOperationResult> {
    const tokens = Array.isArray(lockTokens) ? lockTokens : [lockTokens];
    console.log(`[ServiceBusAPI] abandon called for ${tokens.length} tokens`);
    const result: BatchOperationResult = {
        successCount: 0,
        failureCount: 0,
        errors: []
    };

    const receiversToClose = new Set<any>();
    const connectionsToClose = new Set<any>();

    for (const lockToken of tokens) {
        try {
            const handle = messageHandles.get(lockToken);
            if (!handle) {
                throw new Error('Message not found or lock expired');
            }

            if (handle.isManagementLock) {
                const connection = new ServiceBusConnection(handle.namespace, handle.token);
                await connection.connect();
                await connection.authenticateCBS(handle.entityPath);
                const managementClient = new ManagementClient(connection, handle.entityPath);
                await managementClient.open();
                await managementClient.updateDisposition([lockToken], 'abandoned');
                await managementClient.close();
                await connection.close();
                messageHandles.delete(lockToken);
                result.successCount++;
                continue;
            }

            // Release the message (abandon)
            handle.delivery.release();

            // Cleanup
            if (!handle.isManagementLock) {
                receiversToClose.add(handle.receiver);
                connectionsToClose.add(handle.connection);
            }
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

    // Close standard handles that were settled
    for (const receiver of receiversToClose) {
        try { receiver.close(); } catch {}
    }
    for (const connection of connectionsToClose) {
        try { connection.close(); } catch {}
    }

    return result;
}

/**
 * Dead letter locked messages by lock tokens - moves messages to DLQ
 * @param lockTokens - Array of lock tokens from receiveAndLock
 */
export async function deadLetter(
    lockTokens: string[] | string,
    options: DeadLetterOptions = {}
): Promise<BatchOperationResult> {
    const tokens = Array.isArray(lockTokens) ? lockTokens : [lockTokens];
    console.log(`[ServiceBusAPI] deadLetter called for ${tokens.length} tokens`);
    const result: BatchOperationResult = {
        successCount: 0,
        failureCount: 0,
        errors: []
    };

    const receiversToClose = new Set<any>();
    const connectionsToClose = new Set<any>();

    for (const lockToken of tokens) {
        try {
            const handle = messageHandles.get(lockToken);
            if (!handle) {
                throw new Error('Message not found or lock expired');
            }

            if (handle.isManagementLock) {
                const connection = new ServiceBusConnection(handle.namespace!, handle.token!);
                await connection.connect();
                await connection.authenticateCBS(handle.entityPath!);
                const managementClient = new ManagementClient(connection, handle.entityPath!);
                await managementClient.open();
                await managementClient.updateDisposition([lockToken], 'suspended', options.deadLetterReason, options.deadLetterErrorDescription);
                await managementClient.close();
                await connection.close();
                messageHandles.delete(lockToken);
                result.successCount++;
                continue;
            }

            // Reject the delivery to move to DLQ with error info
            handle.delivery.reject({
                condition: 'com.microsoft:dead-letter',
                description: options.deadLetterErrorDescription || 'Manual dead letter from browser',
                info: {
                    'DeadLetterReason': options.deadLetterReason || 'Manual dead letter',
                    'DeadLetterErrorDescription': options.deadLetterErrorDescription || 'Manual dead letter from browser'
                }
            });

            // Cleanup
            if (!handle.isManagementLock) {
                receiversToClose.add(handle.receiver);
                connectionsToClose.add(handle.connection);
            }
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

    // Close standard handles that were settled
    for (const receiver of receiversToClose) {
        try { receiver.close(); } catch {}
    }
    for (const connection of connectionsToClose) {
        try { connection.close(); } catch {}
    }

    return result;
}

/**
 * Peek specific messages by sequence numbers
 */
export async function peekQueueMessagesBySequence(
    namespace: string,
    queueName: string,
    token: string,
    sequenceNumbers: number[],
    fromDeadLetter: boolean = false
): Promise<ServiceBusMessage[]> {
    const entityPath = fromDeadLetter ? `${queueName}/$DeadLetterQueue` : queueName;
    return await peekMessagesBySequence(namespace, entityPath, token, sequenceNumbers);
}

/**
 * Peek specific messages by sequence numbers from subscription
 */
export async function peekSubscriptionMessagesBySequence(
    namespace: string,
    topicName: string,
    subscriptionName: string,
    token: string,
    sequenceNumbers: number[],
    fromDeadLetter: boolean = false
): Promise<ServiceBusMessage[]> {
    const subscriptionPath = `${topicName}/subscriptions/${subscriptionName}`;
    const entityPath = fromDeadLetter ? `${subscriptionPath}/$DeadLetterQueue` : subscriptionPath;
    return await peekMessagesBySequence(namespace, entityPath, token, sequenceNumbers);
}

/**
 * Internal implementation - peek messages by specific sequence numbers
 */
async function peekMessagesBySequence(
    namespace: string,
    entityPath: string,
    token: string,
    sequenceNumbers: number[]
): Promise<ServiceBusMessage[]> {
    const connection = new ServiceBusConnection(namespace, token);

    try {
        await connection.connect();
        await connection.authenticateCBS(entityPath);

        const managementClient = new ManagementClient(connection, entityPath);
        await managementClient.open();

        const messages: ServiceBusMessage[] = [];

        // Fetch messages concurrently in chunks to dramatically improve speed
        const chunkSize = 50;
        for (let i = 0; i < sequenceNumbers.length; i += chunkSize) {
            const chunk = sequenceNumbers.slice(i, i + chunkSize);

            const promises = chunk.map(async (seqNum) => {
                try {
                    const peeked = await managementClient.peekMessages(seqNum, 1);
                    if (peeked.length > 0) {
                        const msg = parseServiceBusMessage(peeked[0]);
                        if (msg.sequenceNumber === seqNum) {
                            return msg;
                        }
                    }
                } catch {
                    // Message may have been deleted, skip
                }
                return null;
            });

            const results = await Promise.all(promises);
            for (const msg of results) {
                if (msg) {
                    messages.push(msg);
                }
            }
        }

        managementClient.close();
        connection.close();

        return messages;
    } catch (err) {
        connection.close();
        throw new Error(`Peek by sequence failed: ${(err as Error).message}`);
    }
}

/**
 * Get active message sessions
 */
export async function getMessageSessions(
    namespace: string,
    entityPath: string,
    token: string,
    lastUpdatedTime?: string,
    skip: number = 0,
    top: number = 100
): Promise<string[]> {
    const connection = new ServiceBusConnection(namespace, token);
    try {
        await connection.connect();
        await connection.authenticateCBS(entityPath);

        const managementClient = new ManagementClient(connection, entityPath);
        await managementClient.open();

        const filterTime = lastUpdatedTime ? new Date(lastUpdatedTime) : undefined;
        const sessions = await managementClient.getMessageSessions(filterTime, skip, top);

        managementClient.close();
        connection.close();

        return sessions;
    } catch (err) {
        connection.close();
        throw new Error(`Get message sessions failed: ${(err as Error).message}`);
    }
}


