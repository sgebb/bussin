/**
 * Typed boundary over `bussin-client-js`.
 *
 * The browser library ships as an untyped ESM bundle (its sources are type-checked
 * by the app, not emitted as declarations), so we declare exactly the surface this
 * server uses and load it once, lazily.
 */

export interface ServiceBusMessage {
    messageId: string | undefined;
    body: string;
    contentType: string | undefined;
    correlationId: string | undefined;
    sessionId: string | undefined;
    subject: string | undefined;
    deliveryCount: number;
    enqueuedTime: string | undefined;
    sequenceNumber: number | undefined;
    applicationProperties: Record<string, unknown>;
}

export interface SearchResult {
    scannedCount: number;
    matchCount: number;
    matchingSequenceNumbers: number[];
}

export interface SearchController {
    promise: Promise<SearchResult>;
    stop: () => void;
    getProgress: () => { scanned: number; matches: number };
}

export interface PurgeController {
    promise: Promise<number>;
    stop: () => number;
    getCount: () => number;
}

export interface MessageProperties {
    message_id?: string;
    correlation_id?: string;
    subject?: string;
    content_type?: string;
    [key: string]: unknown;
}

interface BussinClient {
    peekQueueMessages(
        ns: string, queue: string, token: string,
        count?: number, fromSequence?: number, fromDeadLetter?: boolean, sessionId?: string
    ): Promise<ServiceBusMessage[]>;

    peekSubscriptionMessages(
        ns: string, topic: string, subscription: string, token: string,
        count?: number, fromSequence?: number, fromDeadLetter?: boolean, sessionId?: string
    ): Promise<ServiceBusMessage[]>;

    peekQueueMessagesBySequence(
        ns: string, queue: string, token: string, sequenceNumbers: number[], fromDeadLetter?: boolean
    ): Promise<ServiceBusMessage[]>;

    peekSubscriptionMessagesBySequence(
        ns: string, topic: string, subscription: string, token: string,
        sequenceNumbers: number[], fromDeadLetter?: boolean
    ): Promise<ServiceBusMessage[]>;

    searchQueueMessages(
        ns: string, queue: string, token: string, fromDeadLetter: boolean,
        bodyFilter: string, messageIdFilter: string, subjectFilter: string,
        maxMessages: number, maxMatches: number
    ): Promise<SearchController>;

    searchSubscriptionMessages(
        ns: string, topic: string, subscription: string, token: string, fromDeadLetter: boolean,
        bodyFilter: string, messageIdFilter: string, subjectFilter: string,
        maxMessages: number, maxMatches: number
    ): Promise<SearchController>;

    sendQueueMessage(
        ns: string, queue: string, token: string,
        body: string | object, properties?: MessageProperties
    ): Promise<void>;

    sendTopicMessage(
        ns: string, topic: string, token: string,
        body: string | object, properties?: MessageProperties
    ): Promise<void>;

    purgeQueue(
        ns: string, queue: string, token: string,
        onProgress?: null, fromDeadLetter?: boolean, requiresSession?: boolean
    ): Promise<PurgeController>;

    purgeSubscription(
        ns: string, topic: string, subscription: string, token: string,
        onProgress?: null, fromDeadLetter?: boolean, requiresSession?: boolean
    ): Promise<PurgeController>;

    deleteQueueMessagesBySequence(
        ns: string, queue: string, token: string, sequenceNumbers: number[], fromDeadLetter?: boolean
    ): Promise<void>;

    deadLetterQueueMessagesBySequence(
        ns: string, queue: string, token: string, sequenceNumbers: number[],
        reason?: string, description?: string
    ): Promise<void>;
}

let cached: BussinClient | null = null;

export async function getClient(): Promise<BussinClient> {
    if (!cached) {
        cached = (await import('bussin-client-js')) as unknown as BussinClient;
    }
    return cached;
}

/**
 * Trim a peeked message down to what is useful to a model, and cap the body.
 * Whole payloads can be megabytes; a model does not need them and the context does
 * not survive them.
 */
export function summarize(m: ServiceBusMessage, maxBodyChars = 4000): Record<string, unknown> {
    const body = typeof m.body === 'string' ? m.body : JSON.stringify(m.body);
    const truncated = body != null && body.length > maxBodyChars;
    return {
        sequenceNumber: m.sequenceNumber,
        messageId: m.messageId,
        subject: m.subject,
        correlationId: m.correlationId,
        sessionId: m.sessionId,
        enqueuedTime: m.enqueuedTime,
        deliveryCount: m.deliveryCount,
        contentType: m.contentType,
        applicationProperties: m.applicationProperties,
        body: truncated ? body.slice(0, maxBodyChars) : body,
        ...(truncated ? { bodyTruncated: true, bodyTotalChars: body.length } : {})
    };
}
