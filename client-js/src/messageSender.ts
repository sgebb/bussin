/**
 * Service Bus Message Sender
 * Handles sending messages to queues/topics
 */

import type { ServiceBusConnection } from './connection.js';
import type { MessageProperties } from './types.js';
import type { Sender } from 'rhea';

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
    async send(body: string | object | Uint8Array | ArrayBuffer, properties: MessageProperties = {}): Promise<void> {
        if (!this.sender) {
            throw new Error('Sender not opened. Call open() first.');
        }

        // Generate unique message ID if not provided
        const messageId = properties.message_id || properties.messageId || `msg-${Date.now()}-${Math.random().toString(36).substr(2, 9)}`;
        
        // Determine content type and encode body
        let encodedBody: Uint8Array;
        let contentType = properties.content_type || properties.contentType;
        
        if (typeof body === 'string') {
            // String body - encode as UTF-8 bytes (like portal does)
            const encoder = new TextEncoder();
            encodedBody = encoder.encode(body);
            contentType = contentType || 'text/plain';
        } else if (body instanceof Uint8Array) {
            // Already binary
            encodedBody = body;
            contentType = contentType || 'application/octet-stream';
        } else if (body instanceof ArrayBuffer) {
            encodedBody = new Uint8Array(body);
            contentType = contentType || 'application/octet-stream';
        } else {
            // Object - serialize to JSON and encode as bytes
            const encoder = new TextEncoder();
            encodedBody = encoder.encode(JSON.stringify(body));
            contentType = contentType || 'application/json';
        }
        
        const message: any = {
            body: encodedBody,
            content_type: contentType,
            message_id: messageId,
            creation_time: new Date(),
            ...properties
        };
        
        // Remove duplicate properties
        delete message.contentType;
        delete message.messageId;

        return new Promise((resolve, reject) => {
            // Wait for sender to be sendable
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
            } else {
                this.sender!.once('sendable', sendHandler);
                // Timeout
                setTimeout(() => {
                    this.sender!.removeListener('sendable', sendHandler);
                    reject(new Error('Send timeout - sender not ready'));
                }, 5000);
            }
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
