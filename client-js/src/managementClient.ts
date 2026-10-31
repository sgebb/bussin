/**
 * Service Bus Management Client
 * Handles management operations via $management node (peek, etc.)
 */

import rhea from 'rhea';
import type { ServiceBusConnection } from './connection.js';
import type { Sender, Receiver } from 'rhea';
import { formatAmqpError } from './types.js';

interface ManagementResponse {
    statusCode: number;
    errorCondition?: string;
    body: any;
}

/**
 * Management Client - for management operations like true peek
 */
export class ManagementClient {
    private readonly connection: ServiceBusConnection;
    private readonly managementAddress: string;
    private sender: Sender | null = null;
    private receiver: Receiver | null = null;
    private replyTo: string | null = null;
    private readonly amqpSession?: any;

    constructor(connection: ServiceBusConnection, entityPath: string, amqpSession?: any) {
        this.connection = connection;
        this.managementAddress = `${entityPath}/$management`;
        this.amqpSession = amqpSession;
    }

    /**
     * Open management sender/receiver. On failure the links are closed again.
     */
    async open(): Promise<void> {
        if (!this.connection.connection) {
            throw new Error('Connection not established');
        }

        return new Promise((resolve, reject) => {
            // Create unique reply-to address
            this.replyTo = `mgmt-reply-${Date.now()}-${Math.random().toString(36).substr(2, 9)}`;

            const sessionOrConn = this.amqpSession || this.connection.connection!;

            const sender: Sender = sessionOrConn.open_sender({
                target: { address: this.managementAddress }
            });
            this.sender = sender;

            const receiver: Receiver = sessionOrConn.open_receiver({
                source: { address: this.managementAddress },
                target: { address: this.replyTo }
            });
            this.receiver = receiver;

            let senderOpen = false;
            let receiverOpen = false;
            let settled = false;

            const fail = (error: Error) => {
                if (settled) return;
                settled = true;
                clearTimeout(timeout);
                this.close();
                reject(error);
            };

            const checkBothOpen = () => {
                if (senderOpen && receiverOpen && !settled) {
                    settled = true;
                    clearTimeout(timeout);
                    resolve();
                }
            };

            sender.on('sender_open', () => {
                senderOpen = true;
                checkBothOpen();
            });

            receiver.on('receiver_open', () => {
                receiverOpen = true;
                checkBothOpen();
            });

            sender.on('sender_error', (context: any) => {
                fail(new Error(context.sender.error ? formatAmqpError(context.sender.error) : 'Sender error'));
            });

            receiver.on('receiver_error', (context: any) => {
                fail(new Error(context.receiver.error ? formatAmqpError(context.receiver.error) : 'Receiver error'));
            });

            const timeout = setTimeout(() => fail(new Error('Management client open timeout')), 10000);
        });
    }

    /**
     * Sends one request to the $management node and waits for its response.
     * Resolves with any status in okStatuses; anything else rejects with "<label> failed: ...".
     */
    private request(
        operation: string,
        body: Record<string, any>,
        label: string,
        okStatuses: number[] = [200, 204],
        timeoutMs: number = 10000
    ): Promise<ManagementResponse> {
        if (!this.sender || !this.receiver || !this.replyTo) {
            return Promise.reject(new Error('Management client not opened'));
        }
        const receiver = this.receiver;

        return new Promise((resolve, reject) => {
            const messageId = `${operation}-${Date.now()}-${Math.random().toString(36).substr(2, 9)}`;

            const responseHandler = (context: any) => {
                if (context.message.correlation_id !== messageId) return;
                clearTimeout(timeout);
                receiver.removeListener('message', responseHandler);

                const props = context.message.application_properties ?? {};
                if (okStatuses.includes(props.statusCode)) {
                    resolve({ statusCode: props.statusCode, errorCondition: props.errorCondition, body: context.message.body });
                } else {
                    const condition = props.errorCondition ? ` (${props.errorCondition})` : '';
                    reject(new Error(`${label} failed: ${props.statusCode} - ${props.statusDescription}${condition}`));
                }
            };

            receiver.on('message', responseHandler);

            this.sender!.send({
                body,
                reply_to: this.replyTo!,
                application_properties: { operation },
                message_id: messageId
            });

            const timeout = setTimeout(() => {
                receiver.removeListener('message', responseHandler);
                reject(new Error(`${label} request timeout`));
            }, timeoutMs);
        });
    }

    /**
     * Peek messages without locking (true peek)
     */
    async peekMessages(fromSequenceNumber: number = 0, messageCount: number = 1, sessionId?: string): Promise<any[]> {
        const messageBody: Record<string, any> = {
            'from-sequence-number': rhea.types.wrap_long(fromSequenceNumber),
            'message-count': rhea.types.wrap_int(messageCount)
        };
        if (sessionId) {
            messageBody['session-id'] = sessionId;
        }

        // 204 = No Content (no messages found)
        const { statusCode, body } = await this.request('com.microsoft:peek-message', messageBody, 'Peek');
        if (statusCode === 204 || !body?.messages) return [];

        // Body is a map with 'messages' key containing array of {message: Buffer}
        const messages: any[] = [];
        const msgArray = Array.isArray(body.messages) ? body.messages : [body.messages];
        for (const item of msgArray) {
            if (item.message) {
                // The message is an encoded AMQP message buffer
                try {
                    messages.push(decodeEncodedMessage(item.message));
                } catch (err) {
                    console.error('Failed to decode message:', err);
                }
            }
        }
        return messages;
    }

    /**
     * Cancel scheduled messages by sequence numbers
     */
    async cancelScheduledMessages(sequenceNumbers: number[]): Promise<void> {
        // Sequence numbers as 8-byte big-endian buffers, wrapped as AMQP long array (0x81)
        const seqNumBuffers = sequenceNumbers.map(seq => longToBytes(seq));
        await this.request('com.microsoft:cancel-scheduled-message', {
            'sequence-numbers': rhea.types.wrap_array(seqNumBuffers, 0x81, undefined)
        }, 'Cancel scheduled messages');
    }

    /**
     * Batch delete: deletes up to messageCount messages enqueued before enqueuedBefore, on the server,
     * without receiving them. This is the operation the Azure SDKs call DeleteMessagesAsync.
     * The service may delete fewer than requested even when more are eligible.
     * @param messageCount - At most 4000 on Premium, 500 on Standard
     * @returns Number of messages actually deleted
     */
    async batchDeleteMessages(messageCount: number, enqueuedBefore: Date): Promise<number> {
        // 204 means nothing was eligible; 404 message-not-found still carries the count it deleted
        const { statusCode, errorCondition, body } = await this.request('com.microsoft:batch-delete-messages', {
            'message-count': rhea.types.wrap_int(messageCount),
            'enqueued-time-utc': rhea.types.wrap_timestamp(enqueuedBefore.getTime())
        }, 'Batch delete', [200, 204, 404], 30000);

        if (statusCode === 404 && errorCondition !== 'com.microsoft:message-not-found') {
            throw new Error(`Batch delete failed: 404 (${errorCondition})`);
        }
        return statusCode === 204 ? 0 : Number(body?.['message-count'] ?? 0);
    }

    /**
     * Enumerate rules for a subscription
     */
    async enumerateRules(skip: number = 0, top: number = 100): Promise<any[]> {
        const { statusCode, body } = await this.request('com.microsoft:enumerate-rules', {
            'skip': rhea.types.wrap_int(skip),
            'top': rhea.types.wrap_int(top)
        }, 'Enumerate rules');
        if (statusCode === 204 || !body || !body.rules) return [];

        const rulesList: any[] = [];
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
        return rulesList;
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

        await this.request('com.microsoft:add-rule', {
            'rule-name': ruleName,
            'rule-description': ruleDescription
        }, 'Add rule');
    }

    /**
     * Remove a rule from a subscription
     */
    async removeRule(ruleName: string): Promise<void> {
        await this.request('com.microsoft:remove-rule', { 'rule-name': ruleName }, 'Remove rule');
    }

    /**
     * Get active message sessions
     */
    async getMessageSessions(lastUpdatedTime?: Date, skip: number = 0, top: number = 100): Promise<string[]> {
        const filterTime = lastUpdatedTime || new Date(0);
        const { body } = await this.request('com.microsoft:get-message-sessions', {
            'last-updated-time': rhea.types.wrap_timestamp(filterTime.getTime()),
            'skip': rhea.types.wrap_int(skip),
            'top': rhea.types.wrap_int(top)
        }, 'Get message sessions');

        const ids = body?.['sessions-ids'];
        return ids ? (Array.isArray(ids) ? ids : [ids]) : [];
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
