/**
 * Service Bus Message Sender
 * Handles sending messages to queues/topics
 */

import type { ServiceBusConnection } from './connection.js';
import type { MessageProperties } from './types.js';
import type { Sender } from 'rhea';
import { message as rheaMessage } from 'rhea';

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

        const chunkSize = 500;
        for (let i = 0; i < messages.length; i += chunkSize) {
            const chunk = messages.slice(i, i + chunkSize);
            const promises = chunk.map(msg => {
                const amqpMessage = this.createAmqpMessage(msg.body, msg.properties);
                return this.dispatchMessage(amqpMessage);
            });
            await Promise.all(promises);
            if (i + chunkSize < messages.length) {
                await new Promise(r => setTimeout(r, 50));
            }
        }
    }

    private createAmqpMessage(body: string | null | undefined, properties: MessageProperties): any {
        const textBody = this.normalizeBodyToString(body);
        const encoder = new TextEncoder();
        const encodedBody = encoder.encode(textBody);
        
        // AMQP standard properties
        const message: any = {
            body: rheaMessage.data_section(encodedBody),
            message_id: properties.message_id || `msg-${Date.now()}-${Math.random().toString(36).substr(2, 9)}`,
            content_type: properties.content_type || 'text/plain; charset=utf-8',
            creation_time: new Date(),
        };

        // Map standard AMQP fields
        if (properties.correlation_id) message.correlation_id = properties.correlation_id;
        if (properties.subject) message.subject = properties.subject;
        if (properties.reply_to) message.reply_to = properties.reply_to;
        if (properties.to) message.to = properties.to;
        if (properties.time_to_live) message.ttl = properties.time_to_live;
        if (properties.group_id) message.group_id = properties.group_id;
        if (properties.session_id) message.group_id = properties.session_id; // Mapping session_id to AMQP group_id

        // Handle message_annotations (broker-specific metadata)
        if (properties.message_annotations) {
            const annotations: Record<string, any> = { ...properties.message_annotations };
            const scheduledTime = annotations['x-opt-scheduled-enqueue-time'];
            if (scheduledTime) {
                // Ensure it's a Date object if provided as string or number
                if (typeof scheduledTime === 'string' || typeof scheduledTime === 'number') {
                    annotations['x-opt-scheduled-enqueue-time'] = new Date(scheduledTime);
                }
            }
            message.message_annotations = annotations;
        }

        // Handle application_properties (custom metadata)
        const appProps: Record<string, any> = { 
            ...(properties.application_properties || {}),
            'x-bussin-sent-via': 'bussin.dev',
            'x-bussin-sent-at': new Date().toISOString()
        };

        // Harvest any other custom properties provided at top level that aren't AMQP reserved fields
        const reservedFields = ['message_id', 'correlation_id', 'subject', 'content_type', 'reply_to', 'to', 'time_to_live', 'group_id', 'session_id', 'message_annotations', 'application_properties'];
        for (const key of Object.keys(properties)) {
            if (!reservedFields.includes(key)) {
                appProps[key] = properties[key];
            }
        }

        message.application_properties = appProps;

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
        const baseTimeout = 10000;

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
                try {
                    const delivery = this.sender!.send(message);
                    
                    // We must wait for the broker to acknowledge the message,
                    // otherwise closing the connection drops it from the local buffer.
                    this.sender!.on('accepted', (context: any) => {
                        if (context.delivery === delivery && !settled) {
                            settled = true;
                            resolve();
                        }
                    });
                    
                    this.sender!.on('rejected', (context: any) => {
                        if (context.delivery === delivery && !settled) {
                            settled = true;
                            const error = context?.delivery?.remote_state?.error;
                            const errorMsg = this.extractErrorMessage(error) || 'Message rejected by broker';
                            reject(new Error(errorMsg));
                        }
                    });
                    
                    this.sender!.on('released', (context: any) => {
                        if (context.delivery === delivery && !settled) {
                            settled = true;
                            reject(new Error('Message released by broker'));
                        }
                    });

                } catch (err) {
                    if (!settled) {
                        settled = true;
                        reject(err);
                    }
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
            } else {
                this.sender!.once('sendable', sendHandler);
            }

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
