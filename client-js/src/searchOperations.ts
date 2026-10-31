import { ServiceBusConnection } from './connection.js';
import { ManagementClient } from './managementClient.js';
import { parseServiceBusMessage } from './messageParser.js';
import type { ServiceBusMessage } from './types.js';

// Consecutive failed batches before a search gives up
const MAX_CONSECUTIVE_FAILURES = 3;

export interface SearchController {
    promise: Promise<SearchResult>;
    stop: () => void;
    getProgress: () => { scanned: number; matches: number };
}

export interface SearchResult {
    scannedCount: number;
    matchCount: number;
    matchingSequenceNumbers: number[];
}

export type SearchProgressCallback = (scanned: number, matches: number, newMatches: number[]) => void;

/**
 * Search for messages in a queue matching filters
 */
export async function searchQueueMessages(
    namespace: string,
    queueName: string,
    token: string,
    fromDeadLetter: boolean,
    bodyFilter: string,
    messageIdFilter: string,
    subjectFilter: string,
    maxMessages: number,
    maxMatches: number,
    onProgress: SearchProgressCallback | null = null
): Promise<SearchController> {
    const entityPath = fromDeadLetter ? `${queueName}/$DeadLetterQueue` : queueName;
    return await searchMessages(namespace, entityPath, token, bodyFilter, messageIdFilter, subjectFilter, maxMessages, maxMatches, onProgress);
}

/**
 * Search for messages in a subscription matching filters
 */
export async function searchSubscriptionMessages(
    namespace: string,
    topicName: string,
    subscriptionName: string,
    token: string,
    fromDeadLetter: boolean,
    bodyFilter: string,
    messageIdFilter: string,
    subjectFilter: string,
    maxMessages: number,
    maxMatches: number,
    onProgress: SearchProgressCallback | null = null
): Promise<SearchController> {
    const subscriptionPath = `${topicName}/subscriptions/${subscriptionName}`;
    const entityPath = fromDeadLetter ? `${subscriptionPath}/$DeadLetterQueue` : subscriptionPath;
    return await searchMessages(namespace, entityPath, token, bodyFilter, messageIdFilter, subjectFilter, maxMessages, maxMatches, onProgress);
}

/**
 * Internal search implementation - scans messages and filters client-side
 */
async function searchMessages(
    namespace: string,
    entityPath: string,
    token: string,
    bodyFilter: string,
    messageIdFilter: string,
    subjectFilter: string,
    maxMessages: number,
    maxMatches: number,
    onProgress: SearchProgressCallback | null = null
): Promise<SearchController> {
    const connection = new ServiceBusConnection(namespace, token);
    let isRunning = true;
    let scannedCount = 0;
    let matchingSequenceNumbers: number[] = [];

    try {
        await connection.connect();
        await connection.authenticateCBS(entityPath);

        const runSearch = async (): Promise<SearchResult> => {
            const managementClient = new ManagementClient(connection, entityPath);
            try {
                await managementClient.open();

                const batchSize = 100;
                let currentSequence = 0;
                let consecutiveEmptyBatches = 0;
                const maxEmptyBatches = 3;
                let consecutiveFailures = 0;

                while (isRunning && scannedCount < maxMessages && matchingSequenceNumbers.length < maxMatches) {
                    let messages: any[];
                    try {
                        messages = await managementClient.peekMessages(currentSequence, batchSize);
                        consecutiveFailures = 0;
                    } catch (batchErr) {
                        // Retry the same batch a few times; a persistent failure (expired token,
                        // dropped link) ends the search with its cause rather than skipping messages.
                        if (++consecutiveFailures >= MAX_CONSECUTIVE_FAILURES) throw batchErr;
                        console.warn('[Search] Batch failed, retrying:', batchErr);
                        continue;
                    }

                    if (messages.length === 0) {
                        consecutiveEmptyBatches++;
                        if (consecutiveEmptyBatches >= maxEmptyBatches) {
                            break; // No more messages
                        }
                        continue;
                    }

                    consecutiveEmptyBatches = 0;
                    const newMatches: number[] = [];

                    for (const rawMsg of messages) {
                        if (!isRunning) break;

                        const msg = parseServiceBusMessage(rawMsg);
                        const seqNum = msg.sequenceNumber;

                        if (seqNum !== undefined && seqNum > currentSequence) {
                            currentSequence = seqNum;
                        }

                        scannedCount++;

                        // Check filters (all must match if specified)
                        let matches = true;

                        if (bodyFilter && matches) {
                            const body = typeof msg.body === 'string' ? msg.body : JSON.stringify(msg.body);
                            matches = body?.toLowerCase().includes(bodyFilter.toLowerCase()) ?? false;
                        }

                        if (messageIdFilter && matches) {
                            matches = msg.messageId?.toLowerCase().includes(messageIdFilter.toLowerCase()) ?? false;
                        }

                        if (subjectFilter && matches) {
                            matches = msg.subject?.toLowerCase().includes(subjectFilter.toLowerCase()) ?? false;
                        }

                        if (matches && seqNum !== undefined) {
                            matchingSequenceNumbers.push(seqNum);
                            newMatches.push(seqNum);
                        }
                    }

                    // Update sequence for next batch
                    currentSequence++;

                    // Report progress
                    if (onProgress) {
                        onProgress(scannedCount, matchingSequenceNumbers.length, newMatches);
                    }
                }

                return {
                    scannedCount,
                    matchCount: matchingSequenceNumbers.length,
                    matchingSequenceNumbers
                };
            } catch (err) {
                throw new Error(`Search failed after scanning ${scannedCount} messages: ${(err as Error).message}`);
            } finally {
                managementClient.close();
                connection.close();
            }
        };

        return {
            promise: runSearch(),
            stop: () => {
                isRunning = false;
            },
            getProgress: () => ({
                scanned: scannedCount,
                matches: matchingSequenceNumbers.length
            })
        };

    } catch (err) {
        connection.close();
        throw new Error(`Failed to start search: ${(err as Error).message}`);
    }
}
