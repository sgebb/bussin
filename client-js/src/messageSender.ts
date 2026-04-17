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

    /**
     * Send multiple messages in batch (single connection, multiple sends)
     */
    async sendBatch(messages: { body: string | null | undefined; properties: MessageProperties }[]): Promise<void> {
        if (!this.sender) {
            throw new Error('Sender not opened. Call open() first.');
        }

        for (const msg of messages) {
            const amqpMessage = this.createAmqpMessage(msg.body, msg.properties);
            await this.dispatchMessage(amqpMessage);
        }
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

        // Add bussin.dev origin marker to application properties
        // Using x-bussin- prefix to avoid conflicts with other applications
        const existingAppProps = properties.application_properties || {};
        message.application_properties = {
            ...existingAppProps,
            'x-bussin-sent-via': 'bussin.dev',
            'x-bussin-sent-at': new Date().toISOString()
        };

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

    private extractErrorMessage(error: any): string | null {
        if (!error) return null;

        if (typeof error === 'string') {
            return error;
        }

        // AMQP errors have condition and description
        if (error.condition) {
            return `${error.condition}${error.description ? ': ' + error.description : ''}`;
        }

        if (error.message) {
            return error.message;
        }

        if (error.description) {
            return error.description;
        }

        // Try to stringify
        try {
            const str = JSON.stringify(error);
            if (str !== '{}') return str;
        } catch { }

        return String(error);
    }

    private async dispatchMessage(message: any): Promise<void> {
        const maxAttempts = 3;
        const baseTimeout = 10000; // Increased to 10 seconds

        for (let attempt = 1; attempt <= maxAttempts; attempt++) {
            try {
                await this.dispatchMessageOnce(message, baseTimeout);
                return; // Success
            } catch (err: any) {
                const isTimeout = err.message?.includes('sender not ready');
                const isLastAttempt = attempt === maxAttempts;

                if (isTimeout && !isLastAttempt) {
                    console.warn(`[MessageSender] Send attempt ${attempt} timed out for ${this.entityPath}, retrying...`);
                    // Small delay before retry
                    await new Promise(r => setTimeout(r, 500));
                    continue;
                }

                throw err;
            }
        }
    }

    private async dispatchMessageOnce(message: any, timeout: number): Promise<void> {
        await new Promise<void>((resolve, reject) => {
            let settled = false;

            const sendHandler = () => {
                if (settled) return;
                settled = true;
                try {
                    this.sender!.send(message);
                    resolve();
                } catch (err) {
                    reject(err);
                }
            };

            const errorHandler = (context: any) => {
                if (settled) return;
                settled = true;
                const error = context?.sender?.error;
                const errorMsg = this.extractErrorMessage(error) || 'Sender error during dispatch';
                console.error(`[MessageSender] Sender error for ${this.entityPath}:`, error);
                reject(new Error(errorMsg));
            };

            const closeHandler = (context: any) => {
                if (settled) return;
                settled = true;
                const error = context?.sender?.error;
                const errorMsg = this.extractErrorMessage(error) || 'Sender closed unexpectedly';
                console.error(`[MessageSender] Sender closed for ${this.entityPath}:`, error);
                reject(new Error(`Sender closed: ${errorMsg}`));
            };

            // Check if already sendable
            if (this.sender!.sendable()) {
                sendHandler();
                return;
            }

            console.log(`[MessageSender] Waiting for sendable state on ${this.entityPath}...`);

            this.sender!.once('sendable', sendHandler);
            this.sender!.once('sender_error', errorHandler);
            this.sender!.once('sender_close', closeHandler);

            setTimeout(() => {
                if (settled) return;
                settled = true;
                this.sender!.removeListener('sendable', sendHandler);
                this.sender!.removeListener('sender_error', errorHandler);
                this.sender!.removeListener('sender_close', closeHandler);
                console.error(`[MessageSender] Send timeout for ${this.entityPath} - sender credit: ${(this.sender as any)?.credit}, sendable: ${this.sender?.sendable()}`);
                reject(new Error(`Send timeout - sender not ready (entity: ${this.entityPath})`));
            }, timeout);
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
