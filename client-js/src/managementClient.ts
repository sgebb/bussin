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
     * Receive and delete messages by sequence numbers (batch operation)
     * Uses receive-by-sequence-number with receive-and-delete mode
     */
    async receiveAndDeleteBySequenceNumbers(sequenceNumbers: number[]): Promise<void> {
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

            // Build sequence number array as 8-byte big-endian buffers, wrapped as AMQP long array (0x81)
            const seqNumBuffers = sequenceNumbers.map(seq => longToBytes(seq));
            const wrappedSeqNums = rhea.types.wrap_array(seqNumBuffers, 0x81, undefined);

            const messageBody: Record<string, any> = {};
            messageBody['sequence-numbers'] = wrappedSeqNums;
            messageBody['receiver-settle-mode'] = rhea.types.wrap_uint(0); // 0 = receive-and-delete
            
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
     * Lock messages by sequence numbers (peek-lock mode)
     * Returns lock tokens that can be used to complete/abandon/dead-letter
     */
    async lockBySequenceNumbers(sequenceNumbers: number[]): Promise<{ sequenceNumber: number; lockToken: string }[]> {
        if (!this.sender || !this.receiver || !this.replyTo) {
            throw new Error('Management client not opened');
        }

        return new Promise((resolve, reject) => {
            const replyTo = this.replyTo!;
            
            const responseHandler = (context: any) => {
                const statusCode = context.message.application_properties?.statusCode;
                const statusDescription = context.message.application_properties?.statusDescription;

                if (statusCode === 200) {
                    // Response body contains messages with lock tokens
                    const body = context.message.body;
                    const results: { sequenceNumber: number; lockToken: string }[] = [];
                    
                    if (body && body.messages) {
                        const msgArray = Array.isArray(body.messages) ? body.messages : [body.messages];
                        for (let i = 0; i < msgArray.length; i++) {
                            const item = msgArray[i];
                            // Lock token is in the 'lock-token' field as a UUID
                            if (item['lock-token']) {
                                results.push({
                                    sequenceNumber: sequenceNumbers[i],
                                    lockToken: uuidFromBuffer(item['lock-token'])
                                });
                            }
                        }
                    }
                    resolve(results);
                } else if (statusCode === 204) {
                    resolve([]); // No messages found
                } else {
                    reject(new Error(`Lock by sequence failed: ${statusCode} - ${statusDescription}`));
                }

                this.receiver!.removeListener('message', responseHandler);
            };

            this.receiver!.on('message', responseHandler);

            // Build sequence number array as 8-byte big-endian buffers, wrapped as AMQP long array (0x81)
            const seqNumBuffers = sequenceNumbers.map(seq => longToBytes(seq));
            const wrappedSeqNums = rhea.types.wrap_array(seqNumBuffers, 0x81, undefined);

            const messageBody: Record<string, any> = {};
            messageBody['sequence-numbers'] = wrappedSeqNums;
            messageBody['receiver-settle-mode'] = rhea.types.wrap_uint(1); // 1 = peek-lock
            
            const request = {
                body: messageBody,
                reply_to: replyTo,
                application_properties: {
                    operation: 'com.microsoft:receive-by-sequence-number'
                },
                message_id: `lock-${Date.now()}`
            };

            this.sender!.send(request);

            setTimeout(() => {
                this.receiver!.removeListener('message', responseHandler);
                reject(new Error('Lock by sequence request timeout'));
            }, 10000);
        });
    }

    /**
     * Update disposition of locked messages (complete, abandon, dead-letter)
     */
    async updateDisposition(lockTokens: string[], disposition: 'completed' | 'abandoned' | 'suspended', deadLetterReason?: string, deadLetterDescription?: string): Promise<void> {
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
                    reject(new Error(`Update disposition failed: ${statusCode} - ${statusDescription}`));
                }

                this.receiver!.removeListener('message', responseHandler);
            };

            this.receiver!.on('message', responseHandler);

            // Convert string UUIDs to binary format
            const lockTokenBuffers = lockTokens.map(uuid => uuidToBuffer(uuid));

            const messageBody: Record<string, any> = {};
            messageBody['lock-tokens'] = lockTokenBuffers;
            messageBody['disposition-status'] = disposition; // 'completed', 'abandoned', or 'suspended' (dead-letter)
            
            if (disposition === 'suspended' && deadLetterReason) {
                messageBody['deadletter-reason'] = deadLetterReason;
                messageBody['deadletter-description'] = deadLetterDescription || '';
            }
            
            const request = {
                body: messageBody,
                reply_to: replyTo,
                application_properties: {
                    operation: 'com.microsoft:update-disposition'
                },
                message_id: `disposition-${Date.now()}`
            };

            this.sender!.send(request);

            setTimeout(() => {
                this.receiver!.removeListener('message', responseHandler);
                reject(new Error('Update disposition request timeout'));
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
 * Convert a number to 8-byte big-endian buffer (for AMQP long)
 * Returns a Buffer-like object with copy method for rhea compatibility
 */
function longToBytes(num: number): Uint8Array & { copy: (target: Uint8Array, offset: number) => void } {
    const bytes = new Uint8Array(8);
    // Handle as BigInt for proper 64-bit encoding
    const bigNum = BigInt(num);
    for (let i = 7; i >= 0; i--) {
        bytes[i] = Number(bigNum >> BigInt((7 - i) * 8) & BigInt(0xff));
    }
    // Add copy method for rhea compatibility (it expects Node Buffer interface)
    (bytes as any).copy = function(target: Uint8Array, offset: number) {
        for (let i = 0; i < this.length; i++) {
            target[offset + i] = this[i];
        }
    };
    return bytes as Uint8Array & { copy: (target: Uint8Array, offset: number) => void };
}

/**
 * Convert a UUID buffer (16 bytes) to a string UUID
 */
function uuidFromBuffer(buffer: Uint8Array | { type: string; data: number[] }): string {
    let bytes: Uint8Array;
    if (buffer && (buffer as any).type === 'Buffer' && Array.isArray((buffer as any).data)) {
        bytes = new Uint8Array((buffer as any).data);
    } else if (buffer instanceof Uint8Array) {
        bytes = buffer;
    } else {
        throw new Error('Invalid UUID buffer format');
    }
    
    const hex = Array.from(bytes).map(b => b.toString(16).padStart(2, '0')).join('');
    return `${hex.slice(0, 8)}-${hex.slice(8, 12)}-${hex.slice(12, 16)}-${hex.slice(16, 20)}-${hex.slice(20, 32)}`;
}

/**
 * Convert a string UUID to a 16-byte Uint8Array
 */
function uuidToBuffer(uuid: string): Uint8Array {
    const hex = uuid.replace(/-/g, '');
    const bytes = new Uint8Array(16);
    for (let i = 0; i < 16; i++) {
        bytes[i] = parseInt(hex.slice(i * 2, i * 2 + 2), 16);
    }
    return bytes;
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
