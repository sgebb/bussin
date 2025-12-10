/**
 * Service Bus Message Sender
 * Handles sending messages to queues/topics
 */

import type { ServiceBusConnection } from './connection.js';
import type { MessageProperties } from './types.js';
import type { Sender } from 'rhea';
import { message as rheaMessage } from 'rhea';
import { Buffer } from 'buffer';

/**
 * Message Sender - for sending messages
 */
export class MessageSender {
    private readonly connection: ServiceBusConnection;
    private readonly entityPath: string;
    private sender: Sender | null = null;

    constructor(connection: ServiceBusConnection, entityPath: string) {
        this.connection = connection;
        this.entityPath = entityPath;
    }

    /**
     * Open the sender
     */
    async open(): Promise<void> {
        if (!this.connection.connection) {
            throw new Error('Connection not established');
        }

        return new Promise((resolve, reject) => {
            this.sender = this.connection.connection!.open_sender({
                target: { address: this.entityPath }
            });

            this.sender.on('sender_open', () => {
                resolve();
            });

            this.sender.on('sender_error', (context: any) => {
                reject(new Error(context.sender.error ? context.sender.error.toString() : 'Sender error'));
            });

            setTimeout(() => reject(new Error('Sender open timeout')), 5000);
        });
    }

    /**
     * Send a message
     */
    async send(body: string | null | undefined, properties: MessageProperties = {}): Promise<void> {
        if (!this.sender) {
            throw new Error('Sender not opened. Call open() first.');
        }

        const message = this.createAmqpMessage(body, properties);
        await this.dispatchMessage(message);
    }

    private createAmqpMessage(body: string | null | undefined, properties: MessageProperties): any {
        const messageId = properties.message_id || properties.messageId || `msg-${Date.now()}-${Math.random().toString(36).substr(2, 9)}`;
        const textBody = this.normalizeBodyToString(body);
        const encodedBody = Buffer.from(textBody, 'utf8');
        const contentType = properties.content_type || properties.contentType || 'text/plain; charset=utf-8';

        const message: any = {
            body: rheaMessage.data_section(encodedBody),
            content_type: contentType,
            message_id: messageId,
            creation_time: new Date(),
            ...properties
        };

        // Map session_id to AMQP group_id
        if (properties.session_id) {
            message.group_id = properties.session_id;
        }

        // Map other broker properties
        if (properties.correlation_id) {
            message.correlation_id = properties.correlation_id;
        }
        if (properties.subject) {
            message.subject = properties.subject;
        }
        if (properties.reply_to) {
            message.reply_to = properties.reply_to;
        }
        if (properties.to) {
            message.to = properties.to;
        }
        if (properties.time_to_live) {
            message.ttl = properties.time_to_live;
        }

        // Handle message_annotations - convert scheduled enqueue time to Date
        if (properties.message_annotations) {
            const annotations: Record<string, any> = { ...properties.message_annotations };
            const scheduledTime = annotations['x-opt-scheduled-enqueue-time'];
            if (scheduledTime) {
                // Convert to Date object if it's a string or already a Date-like value
                if (typeof scheduledTime === 'string') {
                    annotations['x-opt-scheduled-enqueue-time'] = new Date(scheduledTime);
                } else if (typeof scheduledTime === 'number') {
                    // If it's already a timestamp in ms, convert to Date
                    annotations['x-opt-scheduled-enqueue-time'] = new Date(scheduledTime);
                }
            }
            message.message_annotations = annotations;
        }

        // Clean up camelCase duplicates
        delete message.contentType;
        delete message.messageId;

        return message;
    }

    private normalizeBodyToString(body: string | null | undefined): string {
        if (body === undefined || body === null) {
            return '';
        }

        if (typeof body !== 'string') {
            return String(body);
        }

        return body;
    }

    private async dispatchMessage(message: any): Promise<void> {
        await new Promise<void>((resolve, reject) => {
            const sendHandler = () => {
                try {
                    this.sender!.send(message);
                    resolve();
                } catch (err) {
                    reject(err);
                }
            };

            if (this.sender!.sendable()) {
                sendHandler();
                return;
            }

            this.sender!.once('sendable', sendHandler);

            setTimeout(() => {
                this.sender!.removeListener('sendable', sendHandler);
                reject(new Error('Send timeout - sender not ready'));
            }, 5000);
        });
    }

    /**
     * Close the sender
     */
    close(): void {
        if (this.sender) {
            this.sender.close();
        }
    }
}
