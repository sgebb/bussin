import { ServiceBusConnection } from './connection.js';
import { MessageSender } from './messageSender.js';
import { ManagementClient } from './managementClient.js';
import type { MessageProperties } from './types.js';

/**
 * Send a message to a queue
 */
export async function sendQueueMessage(
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
export async function sendTopicMessage(
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
        console.log(`[ServiceBusAPI] Auth success for ${entityPath}`);

        const sender = new MessageSender(connection, entityPath);
        await sender.open();
        console.log(`[ServiceBusAPI] Sender opened for ${entityPath}`);
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
export async function sendQueueMessageBatch(
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
export async function sendTopicMessageBatch(
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
            // Check if it's already a wrapped object from C# { body, properties }
            // or a raw message body (string/object)
            let rawBody: any;
            let rawProps: MessageProperties = {};

            if (msg && typeof msg === 'object' && ('body' in msg || 'properties' in msg)) {
                rawBody = msg.body;
                rawProps = msg.properties || {};
            } else {
                rawBody = msg;
            }

            const messageProps: MessageProperties = { ...rawProps };
            let bodyToSend: string;

            if (typeof rawBody === 'string') {
                bodyToSend = rawBody;
            } else if (rawBody !== null && rawBody !== undefined) {
                bodyToSend = JSON.stringify(rawBody);
                if (!messageProps.content_type) {
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
    } catch (err: any) {
        connection.close();
        // Extract error message properly from various error types
        let errorMsg = 'Unknown error';
        if (err) {
            if (typeof err === 'string') {
                errorMsg = err;
            } else if (err.message) {
                errorMsg = err.message;
            } else if (err.description) {
                errorMsg = err.description;
            } else if (err.condition) {
                errorMsg = `${err.condition}: ${err.description || ''}`;
            } else {
                try {
                    errorMsg = JSON.stringify(err);
                } catch {
                    errorMsg = String(err);
                }
            }
        }
        console.error(`[sendMessageBatch] Failed to send to ${entityPath}:`, err);
        throw new Error(`Batch send failed: ${errorMsg}`);
    }
}

/**
 * Cancel scheduled messages from a queue by sequence numbers
 */
export async function cancelScheduledQueueMessages(
    namespace: string,
    queueName: string,
    token: string,
    sequenceNumbers: number[]
): Promise<void> {
    return await cancelScheduledMessages(namespace, queueName, token, sequenceNumbers);
}

/**
 * Cancel scheduled messages from a topic by sequence numbers
 */
export async function cancelScheduledTopicMessages(
    namespace: string,
    topicName: string,
    token: string,
    sequenceNumbers: number[]
): Promise<void> {
    return await cancelScheduledMessages(namespace, topicName, token, sequenceNumbers);
}

// Internal implementation
async function cancelScheduledMessages(
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

        await managementClient.cancelScheduledMessages(sequenceNumbers);

        managementClient.close();
        connection.close();
    } catch (err) {
        connection.close();
        throw new Error(`Cancel scheduled messages failed: ${(err as Error).message}`);
    }
}
