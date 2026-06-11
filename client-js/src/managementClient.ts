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
    async peekMessages(fromSequenceNumber: number = 0, messageCount: number = 1, sessionId?: string): Promise<any[]> {
        if (!this.sender || !this.receiver || !this.replyTo) {
            throw new Error('Management client not opened');
        }

        return new Promise((resolve, reject) => {
            const replyTo = this.replyTo!;

            const messageId = `peek-${Date.now()}-${Math.random().toString(36).substr(2, 9)}`;

            const responseHandler = (context: any) => {
                // Ensure this response is actually for our specific request
                if (context.message.correlation_id !== messageId) {
                    return;
                }

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
            if (sessionId) {
                messageBody['session-id'] = sessionId;
            }

            const request = {
                body: messageBody,
                reply_to: replyTo,
                application_properties: {
                    operation: 'com.microsoft:peek-message'
                },
                message_id: messageId
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
            const messageId = `delete-${Date.now()}-${Math.random().toString(36).substr(2, 9)}`;

            const responseHandler = (context: any) => {
                if (context.message.correlation_id !== messageId) return;

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
                message_id: messageId
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
            const messageId = `lock-${Date.now()}-${Math.random().toString(36).substr(2, 9)}`;

            const responseHandler = (context: any) => {
                if (context.message.correlation_id !== messageId) return;

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
                message_id: messageId
            };

            this.sender!.send(request);

            setTimeout(() => {
                this.receiver!.removeListener('message', responseHandler);
                reject(new Error('Lock by sequence request timeout'));
            }, 10000);
        });
    }

    /**
     * Cancel scheduled messages by sequence numbers
     */
    async cancelScheduledMessages(sequenceNumbers: number[]): Promise<void> {
        if (!this.sender || !this.receiver || !this.replyTo) {
            throw new Error('Management client not opened');
        }

        return new Promise((resolve, reject) => {
            const replyTo = this.replyTo!;
            const messageId = `cancel-${Date.now()}-${Math.random().toString(36).substr(2, 9)}`;

            const responseHandler = (context: any) => {
                if (context.message.correlation_id !== messageId) return;

                const statusCode = context.message.application_properties?.statusCode;
                const statusDescription = context.message.application_properties?.statusDescription;

                if (statusCode === 200 || statusCode === 204) {
                    resolve();
                } else {
                    reject(new Error(`Cancel scheduled messages failed: ${statusCode} - ${statusDescription}`));
                }

                this.receiver!.removeListener('message', responseHandler);
            };

            this.receiver!.on('message', responseHandler);

            // Build sequence number array as 8-byte big-endian buffers, wrapped as AMQP long array (0x81)
            const seqNumBuffers = sequenceNumbers.map(seq => longToBytes(seq));
            const wrappedSeqNums = rhea.types.wrap_array(seqNumBuffers, 0x81, undefined);

            const messageBody: Record<string, any> = {};
            messageBody['sequence-numbers'] = wrappedSeqNums;

            const request = {
                body: messageBody,
                reply_to: replyTo,
                application_properties: {
                    operation: 'com.microsoft:cancel-scheduled-message'
                },
                message_id: messageId
            };

            this.sender!.send(request);

            setTimeout(() => {
                this.receiver!.removeListener('message', responseHandler);
                reject(new Error('Cancel scheduled messages request timeout'));
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
            const messageId = `disposition-${Date.now()}-${Math.random().toString(36).substr(2, 9)}`;

            const responseHandler = (context: any) => {
                if (context.message.correlation_id !== messageId) return;

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
                message_id: messageId
            };

            this.sender!.send(request);

            setTimeout(() => {
                this.receiver!.removeListener('message', responseHandler);
                reject(new Error('Update disposition request timeout'));
            }, 10000);
        });
    }

    /**
     * Purge messages using batch delete API (what Azure Portal uses)
     * This is the FASTEST way to purge - server-side batch deletion
     * @param messageCount - Number of messages to delete in this batch (max 4000 for Premium, 500 for Standard)
     * @param beforeEnqueueTime - Optional: only delete messages enqueued before this time
     * @returns Number of messages actually deleted
     */
    async purgeMessages(messageCount: number = 4000, beforeEnqueueTime?: Date): Promise<number> {
        if (!this.sender || !this.receiver || !this.replyTo) {
            throw new Error('Management client not opened');
        }

        return new Promise((resolve, reject) => {
            const replyTo = this.replyTo!;
            const messageId = `purge-${Date.now()}-${Math.random().toString(36).substr(2, 9)}`;

            const responseHandler = (context: any) => {
                if (context.message.correlation_id !== messageId) return;

                const statusCode = context.message.application_properties?.statusCode;
                const statusDescription = context.message.application_properties?.statusDescription;

                if (statusCode === 200) {
                    // Response contains the count of deleted messages
                    const body = context.message.body;
                    const deletedCount = body?.['message-count'] || body?.messageCount || 0;
                    resolve(deletedCount);
                } else if (statusCode === 204) {
                    // No messages to delete
                    resolve(0);
                } else {
                    reject(new Error(`Purge failed: ${statusCode} - ${statusDescription}`));
                }

                this.receiver!.removeListener('message', responseHandler);
            };

            this.receiver!.on('message', responseHandler);

            const messageBody: Record<string, any> = {};
            messageBody['message-count'] = rhea.types.wrap_int(messageCount);

            if (beforeEnqueueTime) {
                messageBody['before-enqueue-time'] = rhea.types.wrap_timestamp(beforeEnqueueTime.getTime());
            }

            const request = {
                body: messageBody,
                reply_to: replyTo,
                application_properties: {
                    operation: 'com.microsoft:purge-messages'
                },
                message_id: messageId
            };

            this.sender!.send(request);

            setTimeout(() => {
                this.receiver!.removeListener('message', responseHandler);
                reject(new Error('Purge request timeout'));
            }, 30000); // Longer timeout for large batch operations
        });
    }

    /**
     * Enumerate rules for a subscription
     */
    async enumerateRules(skip: number = 0, top: number = 100): Promise<any[]> {
        if (!this.sender || !this.receiver || !this.replyTo) {
            throw new Error('Management client not opened');
        }

        return new Promise((resolve, reject) => {
            const replyTo = this.replyTo!;
            const messageId = `rules-${Date.now()}-${Math.random().toString(36).substr(2, 9)}`;

            const responseHandler = (context: any) => {
                if (context.message.correlation_id !== messageId) return;

                const statusCode = context.message.application_properties?.statusCode;
                const statusDescription = context.message.application_properties?.statusDescription;

                if (statusCode === 200 || statusCode === 204) {
                    const body = context.message.body;
                    let rulesList: any[] = [];
                    
                    if (statusCode === 204 || !body || !body.rules) {
                        resolve([]);
                        this.receiver!.removeListener('message', responseHandler);
                        return;
                    }
                    
                    const rawRules = Array.isArray(body.rules) ? body.rules : [body.rules];
                    for (const rule of rawRules) {
                        if (!rule) continue;
                        const ruleDesc = rule['rule-description'] !== undefined ? rule['rule-description'] : (rule.get ? rule.get('rule-description') : null);
                        const ruleValue = ruleDesc ? ruleDesc.value : null;
                        if (ruleValue) {
                            let filterVal: any = null;
                            let actionVal: any = null;
                            if (Array.isArray(ruleValue)) {
                                filterVal = ruleValue[0];
                                actionVal = ruleValue[1];
                            } else {
                                // Handle map-based rule description
                                filterVal = ruleValue['sql-filter'] !== undefined ? ruleValue['sql-filter'] :
                                            (ruleValue['correlation-filter'] !== undefined ? ruleValue['correlation-filter'] :
                                            (ruleValue['true-filter'] !== undefined ? ruleValue['true-filter'] :
                                            (ruleValue['false-filter'] !== undefined ? ruleValue['false-filter'] :
                                            (ruleValue.get ? (ruleValue.get('sql-filter') || ruleValue.get('correlation-filter') || ruleValue.get('true-filter') || ruleValue.get('false-filter')) : null))));
                                            
                                actionVal = ruleValue['sql-rule-action'] !== undefined ? ruleValue['sql-rule-action'] :
                                            (ruleValue.get ? ruleValue.get('sql-rule-action') : null);
                            }

                            const filter = parseFilter(filterVal);
                            const actionExpression = parseAction(actionVal);
                            const name = rule['rule-name'] !== undefined ? rule['rule-name'] : 
                                         (rule.get ? rule.get('rule-name') : (Array.isArray(ruleValue) ? (ruleValue[2] || '') : ''));
                            
                            rulesList.push({
                                name,
                                filterType: filter.filterType,
                                sqlExpression: filter.sqlExpression,
                                correlationId: filter.correlationId,
                                messageId: filter.messageId,
                                to: filter.to,
                                replyTo: filter.replyTo,
                                label: filter.label,
                                sessionId: filter.sessionId,
                                replyToSessionId: filter.replyToSessionId,
                                contentType: filter.contentType,
                                properties: filter.properties,
                                actionExpression
                            });
                        }
                    }
                    
                    resolve(rulesList);
                } else {
                    reject(new Error(`Enumerate rules failed: ${statusCode} - ${statusDescription}`));
                }

                this.receiver!.removeListener('message', responseHandler);
            };

            this.receiver!.on('message', responseHandler);

            const messageBody: Record<string, any> = {};
            messageBody['skip'] = rhea.types.wrap_int(skip);
            messageBody['top'] = rhea.types.wrap_int(top);

            const request = {
                body: messageBody,
                reply_to: replyTo,
                application_properties: {
                    operation: 'com.microsoft:enumerate-rules'
                },
                message_id: messageId
            };

            this.sender!.send(request);

            setTimeout(() => {
                this.receiver!.removeListener('message', responseHandler);
                reject(new Error('Enumerate rules request timeout'));
            }, 10000);
        });
    }

    /**
     * Add a rule to a subscription
     */
    async addRule(
        ruleName: string, 
        filterType: 'Sql' | 'Correlation', 
        expressionOrFilter: any, 
        actionExpression?: string
    ): Promise<void> {
        if (!this.sender || !this.receiver || !this.replyTo) {
            throw new Error('Management client not opened');
        }

        return new Promise((resolve, reject) => {
            const replyTo = this.replyTo!;
            const messageId = `addrule-${Date.now()}-${Math.random().toString(36).substr(2, 9)}`;

            const responseHandler = (context: any) => {
                if (context.message.correlation_id !== messageId) return;

                const statusCode = context.message.application_properties?.statusCode;
                const statusDescription = context.message.application_properties?.statusDescription;

                if (statusCode === 200 || statusCode === 204) {
                    resolve();
                } else {
                    reject(new Error(`Add rule failed: ${statusCode} - ${statusDescription}`));
                }

                this.receiver!.removeListener('message', responseHandler);
            };

            this.receiver!.on('message', responseHandler);

            // Wrap rule description as a map according to the Service Bus AMQP request-response management protocol
            const ruleDescription: Record<string, any> = {};

            if (filterType === 'Sql') {
                ruleDescription['sql-filter'] = {
                    'expression': expressionOrFilter
                };
            } else {
                const f = expressionOrFilter || {};
                const correlationFilter: Record<string, any> = {};
                if (f.correlationId !== undefined && f.correlationId !== null) correlationFilter['correlation-id'] = f.correlationId;
                if (f.messageId !== undefined && f.messageId !== null) correlationFilter['message-id'] = f.messageId;
                if (f.to !== undefined && f.to !== null) correlationFilter['to'] = f.to;
                if (f.replyTo !== undefined && f.replyTo !== null) correlationFilter['reply-to'] = f.replyTo;
                if (f.label !== undefined && f.label !== null) correlationFilter['label'] = f.label;
                if (f.sessionId !== undefined && f.sessionId !== null) correlationFilter['session-id'] = f.sessionId;
                if (f.replyToSessionId !== undefined && f.replyToSessionId !== null) correlationFilter['reply-to-session-id'] = f.replyToSessionId;
                if (f.contentType !== undefined && f.contentType !== null) correlationFilter['content-type'] = f.contentType;
                if (f.properties !== undefined && f.properties !== null) correlationFilter['properties'] = f.properties;
                
                ruleDescription['correlation-filter'] = correlationFilter;
            }

            if (actionExpression) {
                ruleDescription['sql-rule-action'] = {
                    'expression': actionExpression
                };
            }

            const messageBody: Record<string, any> = {};
            messageBody['rule-name'] = ruleName;
            messageBody['rule-description'] = ruleDescription;

            const request = {
                body: messageBody,
                reply_to: replyTo,
                application_properties: {
                    operation: 'com.microsoft:add-rule'
                },
                message_id: messageId
            };

            this.sender!.send(request);

            setTimeout(() => {
                this.receiver!.removeListener('message', responseHandler);
                reject(new Error('Add rule request timeout'));
            }, 10000);
        });
    }

    /**
     * Remove a rule from a subscription
     */
    async removeRule(ruleName: string): Promise<void> {
        if (!this.sender || !this.receiver || !this.replyTo) {
            throw new Error('Management client not opened');
        }

        return new Promise((resolve, reject) => {
            const replyTo = this.replyTo!;
            const messageId = `remerule-${Date.now()}-${Math.random().toString(36).substr(2, 9)}`;

            const responseHandler = (context: any) => {
                if (context.message.correlation_id !== messageId) return;

                const statusCode = context.message.application_properties?.statusCode;
                const statusDescription = context.message.application_properties?.statusDescription;

                if (statusCode === 200 || statusCode === 204) {
                    resolve();
                } else {
                    reject(new Error(`Remove rule failed: ${statusCode} - ${statusDescription}`));
                }

                this.receiver!.removeListener('message', responseHandler);
            };

            this.receiver!.on('message', responseHandler);

            const messageBody: Record<string, any> = {};
            messageBody['rule-name'] = ruleName;

            const request = {
                body: messageBody,
                reply_to: replyTo,
                application_properties: {
                    operation: 'com.microsoft:remove-rule'
                },
                message_id: messageId
            };

            this.sender!.send(request);

            setTimeout(() => {
                this.receiver!.removeListener('message', responseHandler);
                reject(new Error('Remove rule request timeout'));
            }, 10000);
        });
    }

    /**
     * Get active message sessions
     */
    async getMessageSessions(lastUpdatedTime?: Date, skip: number = 0, top: number = 100): Promise<string[]> {
        if (!this.sender || !this.receiver || !this.replyTo) {
            throw new Error('Management client not opened');
        }

        return new Promise((resolve, reject) => {
            const replyTo = this.replyTo!;
            const messageId = `sessions-${Date.now()}-${Math.random().toString(36).substr(2, 9)}`;

            const responseHandler = (context: any) => {
                if (context.message.correlation_id !== messageId) return;

                const statusCode = context.message.application_properties?.statusCode;
                const statusDescription = context.message.application_properties?.statusDescription;

                if (statusCode === 200 || statusCode === 204) {
                    const body = context.message.body;
                    let sessionIds: string[] = [];
                    if (body && body['sessions-ids']) {
                        sessionIds = Array.isArray(body['sessions-ids']) ? body['sessions-ids'] : [body['sessions-ids']];
                    }
                    resolve(sessionIds);
                } else {
                    reject(new Error(`Get message sessions failed: ${statusCode} - ${statusDescription}`));
                }

                this.receiver!.removeListener('message', responseHandler);
            };

            this.receiver!.on('message', responseHandler);

            const messageBody: Record<string, any> = {};
            const filterTime = lastUpdatedTime || new Date(0);
            messageBody['last-updated-time'] = rhea.types.wrap_timestamp(filterTime.getTime());
            messageBody['skip'] = rhea.types.wrap_int(skip);
            messageBody['top'] = rhea.types.wrap_int(top);

            const request = {
                body: messageBody,
                reply_to: replyTo,
                application_properties: {
                    operation: 'com.microsoft:get-message-sessions'
                },
                message_id: messageId
            };

            this.sender!.send(request);

            setTimeout(() => {
                this.receiver!.removeListener('message', responseHandler);
                reject(new Error('Get message sessions request timeout'));
            }, 10000);
        });
    }

    /**
     * Get session state
     */
    async getSessionState(sessionId: string): Promise<string> {
        if (!this.sender || !this.receiver || !this.replyTo) {
            throw new Error('Management client not opened');
        }

        return new Promise((resolve, reject) => {
            const replyTo = this.replyTo!;
            const messageId = `getstate-${Date.now()}-${Math.random().toString(36).substr(2, 9)}`;

            const responseHandler = (context: any) => {
                if (context.message.correlation_id !== messageId) return;

                const statusCode = context.message.application_properties?.statusCode;
                const statusDescription = context.message.application_properties?.statusDescription;

                if (statusCode === 200 || statusCode === 204) {
                    const body = context.message.body;
                    let stateVal = '';
                    if (body && body['session-state'] !== undefined && body['session-state'] !== null) {
                        const rawState = body['session-state'];
                        if (typeof rawState === 'string') {
                            stateVal = rawState;
                        } else if (rawState && typeof rawState === 'object') {
                            let data: Uint8Array;
                            if (rawState instanceof Uint8Array) {
                                data = rawState;
                            } else if (rawState.data && Array.isArray(rawState.data)) {
                                data = new Uint8Array(rawState.data);
                            } else {
                                data = new Uint8Array(Object.values(rawState));
                            }
                            stateVal = new TextDecoder().decode(data);
                        }
                    }
                    resolve(stateVal);
                } else {
                    reject(new Error(`Get session state failed: ${statusCode} - ${statusDescription}`));
                }

                this.receiver!.removeListener('message', responseHandler);
            };

            this.receiver!.on('message', responseHandler);

            const messageBody: Record<string, any> = {};
            messageBody['session-id'] = sessionId;

            const request = {
                body: messageBody,
                reply_to: replyTo,
                application_properties: {
                    operation: 'com.microsoft:get-session-state'
                },
                message_id: messageId
            };

            this.sender!.send(request);

            setTimeout(() => {
                this.receiver!.removeListener('message', responseHandler);
                reject(new Error('Get session state request timeout'));
            }, 10000);
        });
    }

    /**
     * Set session state
     */
    async setSessionState(sessionId: string, state: string | null): Promise<void> {
        if (!this.sender || !this.receiver || !this.replyTo) {
            throw new Error('Management client not opened');
        }

        return new Promise((resolve, reject) => {
            const replyTo = this.replyTo!;
            const messageId = `setstate-${Date.now()}-${Math.random().toString(36).substr(2, 9)}`;

            const responseHandler = (context: any) => {
                if (context.message.correlation_id !== messageId) return;

                const statusCode = context.message.application_properties?.statusCode;
                const statusDescription = context.message.application_properties?.statusDescription;

                if (statusCode === 200 || statusCode === 204) {
                    resolve();
                } else {
                    reject(new Error(`Set session state failed: ${statusCode} - ${statusDescription}`));
                }

                this.receiver!.removeListener('message', responseHandler);
            };

            this.receiver!.on('message', responseHandler);

            const messageBody: Record<string, any> = {};
            messageBody['session-id'] = sessionId;
            if (state !== null && state !== undefined) {
                const encodedState = new TextEncoder().encode(state);
                messageBody['session-state'] = rhea.types.wrap_binary(encodedState);
            } else {
                messageBody['session-state'] = null;
            }

            const request = {
                body: messageBody,
                reply_to: replyTo,
                application_properties: {
                    operation: 'com.microsoft:set-session-state'
                },
                message_id: messageId
            };

            this.sender!.send(request);

            setTimeout(() => {
                this.receiver!.removeListener('message', responseHandler);
                reject(new Error('Set session state request timeout'));
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
    (bytes as any).copy = function (target: Uint8Array, offset: number) {
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
 * Decode an encoded AMQP message buffer.
 * In real environments rhea.message.encode returns a binary buffer and
 * rhea.message.decode converts it back to a message object.
 * In mock/test environments both are identity functions, so plain objects
 * pass through unchanged — no environment-sniffing needed here.
 */
function decodeEncodedMessage(messageBuffer: any): any {
    // Handle JSON-serialized Node.js Buffer representation ({ type: 'Buffer', data: [...] })
    if (messageBuffer?.type === 'Buffer' && Array.isArray(messageBuffer.data)) {
        messageBuffer = new Uint8Array(messageBuffer.data);
    }

    return rhea.message.decode(messageBuffer as any);
}

function getDescriptorValue(descriptor: any): string | number | null {
    if (descriptor === null || descriptor === undefined) return null;
    if (typeof descriptor === 'number' || typeof descriptor === 'string') return descriptor;
    if (typeof descriptor === 'object') {
        if (descriptor.name !== undefined) return descriptor.name;
        if (descriptor.value !== undefined) return descriptor.value;
        if (typeof descriptor.toString === 'function') {
            const str = descriptor.toString();
            const num = Number(str);
            if (!isNaN(num)) return num;
            return str;
        }
    }
    return String(descriptor);
}

function getSqlExpression(value: any): string {
    if (!value) return '';
    if (typeof value === 'string') return value;
    if (Array.isArray(value)) return typeof value[0] === 'string' ? value[0] : (value[0]?.toString() || '');
    if (typeof value === 'object') {
        const exp = value['expression'] !== undefined ? value['expression'] : (value.get ? value.get('expression') : '');
        if (exp) return exp.toString();
    }
    return String(value);
}

function getActionExpression(value: any): string | undefined {
    if (!value) return undefined;
    if (typeof value === 'string') return value;
    if (Array.isArray(value)) return typeof value[0] === 'string' ? value[0] : (value[0]?.toString() || undefined);
    if (typeof value === 'object') {
        const exp = value['expression'] !== undefined ? value['expression'] : (value.get ? value.get('expression') : undefined);
        if (exp) return exp.toString();
    }
    return String(value);
}

function parseFilter(filter: any): any {
    if (!filter) return { filterType: 'True', sqlExpression: '1=1' };
    
    const descriptor = filter.descriptor;
    const value = filter.value;
    const descVal = getDescriptorValue(descriptor);
    
    if (descVal === 0x13700000006 || descVal === 0x1370000006 || descVal === 'com.microsoft:sql-filter:list') {
        return {
            filterType: 'Sql',
            sqlExpression: getSqlExpression(value)
        };
    }
    
    if (descVal === 0x13700000009 || descVal === 0x1370000009 || descVal === 'com.microsoft:correlation-filter:list') {
        return {
            filterType: 'Correlation',
            correlationId: value ? value[0] : null,
            messageId: value ? value[1] : null,
            to: value ? value[2] : null,
            replyTo: value ? value[3] : null,
            label: value ? value[4] : null,
            sessionId: value ? value[5] : null,
            replyToSessionId: value ? value[6] : null,
            contentType: value ? value[7] : null,
            properties: value ? value[8] : null
        };
    }
    
    if (descVal === 0x13700000007 || descVal === 0x1370000007 || descVal === 'com.microsoft:true-filter:list') {
        return {
            filterType: 'True',
            sqlExpression: '1=1'
        };
    }
    
    if (descVal === 0x13700000008 || descVal === 0x1370000008 || descVal === 'com.microsoft:false-filter:list') {
        return {
            filterType: 'False',
            sqlExpression: '1=0'
        };
    }
    
    return { filterType: 'Sql', sqlExpression: typeof filter === 'string' ? filter : getSqlExpression(filter) };
}

function parseAction(action: any): string | undefined {
    if (!action) return undefined;
    const descriptor = action.descriptor;
    const value = action.value;
    const descVal = getDescriptorValue(descriptor);
    if (descVal === 0x13700000006 || descVal === 0x1370000006 || descVal === 'com.microsoft:sql-rule-action:list') {
        return getActionExpression(value);
    }
    return undefined;
}
