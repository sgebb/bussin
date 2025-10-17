/**
 * Service Bus Management Client
 * Handles management operations via $management node (peek, etc.)
 */

import rhea from 'rhea';
import type { ServiceBusConnection } from './connection.js';
import type { Sender, Receiver } from 'rhea';

/**
 * Management Client - for management operations like true peek
 */
export class ManagementClient {
    private readonly connection: ServiceBusConnection;
    private readonly entityPath: string;
    private readonly managementAddress: string;
    private sender: Sender | null = null;
    private receiver: Receiver | null = null;
    private replyTo: string | null = null;

    constructor(connection: ServiceBusConnection, entityPath: string) {
        this.connection = connection;
        this.entityPath = entityPath;
        this.managementAddress = `${entityPath}/$management`;
    }

    /**
     * Open management sender/receiver
     */
    async open(): Promise<void> {
        if (!this.connection.connection) {
            throw new Error('Connection not established');
        }

        return new Promise((resolve, reject) => {
            // Create unique reply-to address
            this.replyTo = `mgmt-reply-${Date.now()}-${Math.random().toString(36).substr(2, 9)}`;
            
            this.sender = this.connection.connection!.open_sender({
                target: { address: this.managementAddress }
            });

            this.receiver = this.connection.connection!.open_receiver({
                source: { address: this.managementAddress },
                target: { address: this.replyTo }
            });

            let senderOpen = false;
            let receiverOpen = false;

            const checkBothOpen = () => {
                if (senderOpen && receiverOpen) {
                    resolve();
                }
            };

            this.sender.on('sender_open', () => {
                senderOpen = true;
                checkBothOpen();
            });

            this.receiver.on('receiver_open', () => {
                receiverOpen = true;
                checkBothOpen();
            });

            this.sender.on('sender_error', (context: any) => {
                reject(new Error(context.sender.error ? context.sender.error.toString() : 'Sender error'));
            });

            this.receiver.on('receiver_error', (context: any) => {
                reject(new Error(context.receiver.error ? context.receiver.error.toString() : 'Receiver error'));
            });

            setTimeout(() => reject(new Error('Management client open timeout')), 10000);
        });
    }

    /**
     * Peek messages without locking (true peek)
     */
    async peekMessages(fromSequenceNumber: number = 0, messageCount: number = 1): Promise<any[]> {
        if (!this.sender || !this.receiver || !this.replyTo) {
            throw new Error('Management client not opened');
        }

        return new Promise((resolve, reject) => {
            const replyTo = this.replyTo!;
            
            // Set up receiver for response
            const responseHandler = (context: any) => {
                const statusCode = context.message.application_properties?.statusCode;
                const statusDescription = context.message.application_properties?.statusDescription;

                // 204 = No Content (no messages found), 200 = Success
                if (statusCode === 200 || statusCode === 204) {
                    const body = context.message.body;
                    let messages: any[] = [];
                    
                    // Status 204 means no messages
                    if (statusCode === 204) {
                        resolve([]);
                        this.receiver!.removeListener('message', responseHandler);
                        return;
                    }
                    
                    // Body is a map with 'messages' key containing array of {message: Buffer}
                    if (body && body.messages) {
                        const msgArray = Array.isArray(body.messages) ? body.messages : [body.messages];
                        
                        for (const item of msgArray) {
                            if (item.message) {
                                // The message is an encoded AMQP message buffer
                                try {
                                    const decodedMessage = decodeEncodedMessage(item.message);
                                    messages.push(decodedMessage);
                                } catch (err) {
                                    console.error('Failed to decode message:', err);
                                }
                            }
                        }
                    }
                    
                    resolve(messages);
                } else {
                    reject(new Error(`Peek failed: ${statusCode} - ${statusDescription}`));
                }

                this.receiver!.removeListener('message', responseHandler);
            };

            this.receiver!.on('message', responseHandler);

            // Build request body with proper AMQP types
            const messageBody: Record<string, any> = {};
            messageBody['from-sequence-number'] = rhea.types.wrap_long(fromSequenceNumber);
            messageBody['message-count'] = rhea.types.wrap_int(messageCount);
            
            const request = {
                body: messageBody,
                reply_to: replyTo,
                application_properties: {
                    operation: 'com.microsoft:peek-message'
                },
                message_id: `peek-${Date.now()}`
            };

            this.sender!.send(request);

            // Timeout
            setTimeout(() => {
                this.receiver!.removeListener('message', responseHandler);
                reject(new Error('Peek request timeout'));
            }, 10000);
        });
    }

    /**
     * Delete messages by sequence numbers (batch operation)
     * Note: AMQP management API supports this via receive-by-sequence-number
     */
    async deleteMessagesBySequenceNumbers(sequenceNumbers: number[]): Promise<void> {
        if (!this.sender || !this.receiver || !this.replyTo) {
            throw new Error('Management client not opened');
        }

        return new Promise((resolve, reject) => {
            const replyTo = this.replyTo!;
            
            const responseHandler = (context: any) => {
                const statusCode = context.message.application_properties?.statusCode;
                const statusDescription = context.message.application_properties?.statusDescription;

                if (statusCode === 200 || statusCode === 204) {
                    resolve();
                } else {
                    reject(new Error(`Delete failed: ${statusCode} - ${statusDescription}`));
                }

                this.receiver!.removeListener('message', responseHandler);
            };

            this.receiver!.on('message', responseHandler);

            // Build sequence number array as buffers
            const seqNumBuffers = sequenceNumbers.map(seq => {
                const buffer = (globalThis as any).Buffer.alloc(8);
                buffer.writeBigInt64BE(BigInt(seq));
                return buffer;
            });

            const messageBody: Record<string, any> = {};
            messageBody['sequence-numbers'] = rhea.types.wrap_array(seqNumBuffers, 0x81, undefined);
            messageBody['receiver-settle-mode'] = rhea.types.wrap_uint(0); // receive-and-delete
            
            const request = {
                body: messageBody,
                reply_to: replyTo,
                application_properties: {
                    operation: 'com.microsoft:receive-by-sequence-number'
                },
                message_id: `delete-${Date.now()}`
            };

            this.sender!.send(request);

            setTimeout(() => {
                this.receiver!.removeListener('message', responseHandler);
                reject(new Error('Delete request timeout'));
            }, 10000);
        });
    }

    /**
     * Close management client
     */
    close(): void {
        if (this.sender) {
            this.sender.close();
        }
        if (this.receiver) {
            this.receiver.close();
        }
    }
}

/**
 * Decode an encoded AMQP message buffer
 */
function decodeEncodedMessage(messageBuffer: any): any {
    // Convert Buffer object to actual Uint8Array
    let bytes: Uint8Array;
    if (messageBuffer.type === 'Buffer' && Array.isArray(messageBuffer.data)) {
        bytes = new Uint8Array(messageBuffer.data);
    } else if (messageBuffer instanceof Uint8Array) {
        bytes = messageBuffer;
    } else {
        throw new Error('Invalid message buffer format');
    }
    
    // Use rhea's message decoder (cast to any due to Node.js Buffer vs browser Uint8Array mismatch)
    const decoded = rhea.message.decode(bytes as any);
    return decoded;
}
