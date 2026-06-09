import { EventEmitter } from 'events';
import rhea from 'rhea';

let _nextId = 1;

// Truly global memory store
if (!(globalThis as any).__BUSSIN_AUDIT_LOG__) {
    (globalThis as any).__BUSSIN_AUDIT_LOG__ = [];
    (globalThis as any).__BUSSIN_QUEUES__ = new Map<string, any[]>();
    (globalThis as any).__BUSSIN_TOPOLOGY__ = new Map<string, any>();
    (globalThis as any).__BUSSIN_SEQ_COUNTERS__ = new Map<string, number>();
}

const _globalAuditLog: string[] = (globalThis as any).__BUSSIN_AUDIT_LOG__;
const _globalQueues: Map<string, any[]> = (globalThis as any).__BUSSIN_QUEUES__;
const _globalTopology: Map<string, { type: 'queue' | 'topic' | 'subscription', subscriptions?: string[] }> = (globalThis as any).__BUSSIN_TOPOLOGY__;
const _globalSeqCounters: Map<string, number> = (globalThis as any).__BUSSIN_SEQ_COUNTERS__;

/**
 * Build a full AMQP message object from a stored mock message.
 * Copies all relevant AMQP sections so that the message survives
 * the mock encode/decode round-trip (where encode is an identity function).
 */
function buildAmqpMessage(m: any, extraAnnotations?: Record<string, any>): any {
    return {
        body: m.body,
        message_id: m.message_id,
        content_type: m.content_type,
        correlation_id: m.correlation_id,
        group_id: m.group_id,
        subject: m.subject,
        reply_to: m.reply_to,
        to: m.to,
        ttl: m.ttl,
        absolute_expiry_time: m.absolute_expiry_time,
        creation_time: m.creation_time,
        application_properties: m.application_properties,
        message_annotations: {
            ...(m.message_annotations || {}),
            ...(extraAnnotations || {}),
        },
    };
}

export class MockBroker extends EventEmitter {
    public get auditLog() { return _globalAuditLog; }
    public queues = _globalQueues;

    constructor() { super(); }

    public reset() {
        _globalQueues.clear();
        _globalTopology.clear();
        _globalSeqCounters.clear();
        _globalAuditLog.length = 0;
        this.removeAllListeners();
    }

    public normalizePath(path: string): string {
        return (path || '').toLowerCase().replace(/^\/+/, '').replace(/\/+$/, '');
    }

    public getMessages(name: string) {
        const cleanName = this.normalizePath(name);
        if (!_globalQueues.has(cleanName)) {
            console.log(`[MockBroker] Initializing empty queue for ${cleanName}`);
            _globalQueues.set(cleanName, []);
        }
        return _globalQueues.get(cleanName)!;
    }

    public createQueue(name: string) {
        const cleanName = this.normalizePath(name);
        _globalTopology.set(cleanName, { type: 'queue' });
        this.getMessages(cleanName);
    }

    public createTopic(name: string) {
        const cleanName = this.normalizePath(name);
        _globalTopology.set(cleanName, { type: 'topic', subscriptions: [] });
    }

    public createSubscription(topicName: string, subName: string) {
        const cleanTopic = this.normalizePath(topicName);
        const subPath = `${cleanTopic}/subscriptions/${this.normalizePath(subName)}`;
        _globalTopology.set(subPath, { type: 'subscription' });

        const topic = _globalTopology.get(cleanTopic);
        if (topic) {
            topic.subscriptions = topic.subscriptions || [];
            if (!topic.subscriptions.includes(subPath)) topic.subscriptions.push(subPath);
        }
        this.getMessages(subPath);
    }

    public log(action: string) {
        _globalAuditLog.push(action);
        console.log(`[Broker LOG] ${action}`);
    }

    public pushMessage(address: string, message: any) {
        const cleanAddress = this.normalizePath(address);
        console.log(`[Broker] Pushing message to ${cleanAddress}`);
        const config = _globalTopology.get(cleanAddress);

        if (config?.type === 'topic' && config.subscriptions) {
            config.subscriptions.forEach(sub => {
                // Deep-ish clone to preserve all AMQP sections
                const clone = {
                    ...message,
                    application_properties: { ...(message.application_properties || {}) },
                    message_annotations: { ...(message.message_annotations || {}) }
                };
                this.pushMessage(sub, clone);
            });
        } else {
            const queue = this.getMessages(cleanAddress);
            if (!message._sequenceNumber) {
                const nextSeq = (_globalSeqCounters.get(cleanAddress) ?? 0) + 1;
                _globalSeqCounters.set(cleanAddress, nextSeq);
                message._sequenceNumber = nextSeq;
            }

            // Populate message_annotations for parser compatibility
            if (!message.message_annotations) message.message_annotations = {};
            if (!message.message_annotations['x-opt-sequence-number']) {
                message.message_annotations['x-opt-sequence-number'] = message._sequenceNumber;
            }
            if (!message.message_annotations['x-opt-enqueued-time']) {
                message.message_annotations['x-opt-enqueued-time'] = new Date();
            }

            // Ensure broker properties are at top level for simulator simplicity
            if (!message.message_id && message.properties?.message_id) message.message_id = message.properties.message_id;
            if (!message.subject && message.properties?.subject) message.subject = message.properties.subject;

            if (!message.application_properties) message.application_properties = {};
            queue.push(message);
            console.log(`[MockBroker] Stored in ${cleanAddress}, queue size: ${queue.length}`);
        }

        const isSystem = cleanAddress.includes('$') || cleanAddress.includes('reply') || cleanAddress.includes('management') || cleanAddress.includes('cbs');
        if (isSystem) {
            queueMicrotask(() => {
                this.emit('activity', cleanAddress);
            });
        } else {
            setTimeout(() => this.emit('activity', cleanAddress), 5);
        }
    }

    public connect(options: any) { return new MockConnection(this, options); }
}

class MockConnection extends EventEmitter {
    public id = _nextId++;
    public receivers = new Set<MockReceiver>();
    public session: any = { connection: this, isOpen: () => true, isClosed: () => false };
    private activityHandler: (addr: string) => void;

    constructor(private broker: MockBroker, public options: any) {
        super();
        this.activityHandler = (addr: string) => {
            const cleanAddr = this.broker.normalizePath(addr);
            for (const r of this.receivers) {
                const cleanRAddr = this.broker.normalizePath(r.address);
                const isMgmtMatch = (cleanAddr.includes('management') || cleanAddr.includes('cbs')) && (cleanRAddr.includes('management') || cleanRAddr.includes('cbs'));
                const isMatch = cleanRAddr === cleanAddr || cleanAddr.endsWith(cleanRAddr) || cleanRAddr.endsWith(cleanAddr) || isMgmtMatch;

                if (isMatch) {
                    r.deliverMessages(cleanAddr);
                }
            }
        };
        this.broker.on('activity', this.activityHandler);
        setTimeout(() => this.emit('connection_open', { connection: this }), 1);
    }

    public isOpen() { return true; }
    public isClosed() { return false; }

    public close() {
        this.broker.removeListener('activity', this.activityHandler);
        this.receivers.forEach(r => r.close());
        this.emit('disconnected', { connection: this });
    }

    public open_sender(opts: any) {
        const addr = typeof opts === 'string' ? opts : opts.target?.address;
        return new MockSender(this.broker, addr, this);
    }

    public open_receiver(opts: any) {
        let addr: string;
        if (typeof opts === 'string') {
            addr = opts;
        } else if (opts.source?.dynamic) {
            addr = `mgmt-reply-${Date.now()}-${Math.random().toString(36).substr(2, 5)}`;
        } else {
            // When target.address is present it is the reply-to / listening address
            // (ManagementClient pattern: source=mgmt node, target=reply-to address)
            addr = opts.target?.address || opts.source?.address;
        }

        const r = new MockReceiver(this.broker, addr, this);
        this.receivers.add(r);
        return r;
    }

    public handleSystemMessage(msg: any, target: string) {
        const correlationId = msg.message_id || msg.correlation_id || msg.application_properties?.['message-id'];
        const replyTo = msg.reply_to || (target === '$cbs' ? '$cbs' : null);
        if (!replyTo) return;

        let response: any = {
            application_properties: {
                statusCode: 200,
                'status-code': 200,
                'status-description': 'OK'
            },
            correlation_id: correlationId
        };

        if (target === '$cbs') {
            response.application_properties.statusCode = 202;
            response.application_properties['status-code'] = 202;
            response.application_properties['status-description'] = 'Accepted';
        } else if (target.includes('$management')) {
            const operation = msg.application_properties?.operation || msg.application_properties?.['operation'];
            // Entity path comes from the target address: strip /$management suffix
            const entityPath = this.broker.normalizePath(
                target.replace(/\$management$/i, '').replace(/\/$/, '')
            );

            if (operation === 'com.microsoft:peek-message') {
                // Return up to messageCount messages from the entity starting at fromSequenceNumber
                const fromSeq = Number(msg.body?.['from-sequence-number'] ?? 0);
                const msgCount = Number(msg.body?.['message-count'] ?? 10);
                const allMessages = this.broker.getMessages(entityPath);
                const eligible = allMessages.filter(m => (m._sequenceNumber ?? 0) >= fromSeq);
                const slice = eligible.slice(0, msgCount);

                if (slice.length === 0) {
                    response.application_properties.statusCode = 204;
                    response.application_properties['status-code'] = 204;
                    response.application_properties['status-description'] = 'No Content';
                    response.body = { messages: [] };
                } else {
                    const encodedMessages = slice.map(m => {
                        const amqpMsg = buildAmqpMessage(m, {
                            'x-opt-sequence-number': m._sequenceNumber,
                        });
                        return { message: rhea.message.encode(amqpMsg) };
                    });
                    response.body = { messages: encodedMessages };
                }

            } else if (operation === 'com.microsoft:receive-by-sequence-number') {
                // Receive (and optionally delete) messages by sequence numbers
                const settleMode = Number(msg.body?.['receiver-settle-mode'] ?? 0);
                const rawSeqNums = msg.body?.['sequence-numbers'];
                const seqNums: number[] = (Array.isArray(rawSeqNums) ? rawSeqNums : (rawSeqNums?.value ?? [])).map((sn: any) => {
                    // If it's a Uint8Array (8-byte long), convert back to number
                    if (sn instanceof Uint8Array && sn.length === 8) {
                        let val = BigInt(0);
                        for (let i = 0; i < 8; i++) val = (val << BigInt(8)) + BigInt(sn[i]);
                        return Number(val);
                    }
                    return Number(sn);
                });
                const queue = this.broker.getMessages(entityPath);

                if (settleMode === 0) {
                    // Receive-and-delete
                    seqNums.forEach(seq => {
                        const idx = queue.findIndex(m => m._sequenceNumber === seq);
                        if (idx !== -1) queue.splice(idx, 1);
                    });
                    response.application_properties.statusCode = 200;
                } else {
                    // Peek-lock (mode 1): return messages with lock tokens
                    const locked = seqNums.map(seq => {
                        const m = queue.find(msg => msg._sequenceNumber === seq);
                        if (!m) return null;
                        // Generate a fake 16-byte lock token UUID
                        const lockToken = Array.from({ length: 16 }, () =>
                            Math.floor(Math.random() * 256)
                        );
                        m._lockToken = lockToken;

                        const amqpMsg = buildAmqpMessage(m, {
                            'x-opt-sequence-number': m._sequenceNumber,
                            'x-opt-lock-token': new Uint8Array(lockToken),
                        });
                        return {
                            message: rhea.message.encode(amqpMsg),
                            'lock-token': new Uint8Array(lockToken)
                        };
                    }).filter(Boolean);
                    response.body = { messages: locked };
                    response.application_properties.statusCode = 200;
                    response.application_properties['status-code'] = 200;
                }

            } else if (operation === 'com.microsoft:cancel-scheduled-message') {
                // Cancel scheduled messages by sequence numbers
                const rawSeqNums = msg.body?.['sequence-numbers'];
                const seqNums: number[] = (Array.isArray(rawSeqNums) ? rawSeqNums : (rawSeqNums?.value ?? [])).map((sn: any) => {
                    if (sn instanceof Uint8Array && sn.length === 8) {
                        let val = BigInt(0);
                        for (let i = 0; i < 8; i++) val = (val << BigInt(8)) + BigInt(sn[i]);
                        return Number(val);
                    }
                    return Number(sn);
                });
                const queue = this.broker.getMessages(entityPath);

                seqNums.forEach(seq => {
                    const idx = queue.findIndex(m => m._sequenceNumber === seq);
                    if (idx !== -1) {
                        queue.splice(idx, 1);
                        this.broker.log(`CANCEL_SCHEDULED (${entityPath}) via management`);
                    }
                });
                response.application_properties.statusCode = 200;
                response.application_properties['status-code'] = 200;

            } else if (operation === 'com.microsoft:update-disposition') {
                // Update disposition: completed, abandoned, or suspended (dead-letter)
                const disposition = msg.body?.['disposition-status'];
                const lockTokenBuffers: Uint8Array[] = msg.body?.['lock-tokens'] ?? [];
                console.log(`[MockBroker] UPDATE DISPOSITION. Disposition: ${disposition}. Body keys: ${Object.keys(msg.body ?? {})}. Lock token buffers: ${lockTokenBuffers.length}`);

                const queue = this.broker.getMessages(entityPath);
                console.log(`[MockBroker] Queue size: ${queue.length}.`);

                lockTokenBuffers.forEach((ltBuf: any) => {
                    const ltArray = ltBuf instanceof Uint8Array ? Array.from(ltBuf) : (ltBuf.data ?? []);
                    const idx = queue.findIndex(m =>
                        m._lockToken && JSON.stringify(Array.from(m._lockToken)) === JSON.stringify(ltArray)
                    );
                    if (idx !== -1) {
                        if (disposition === 'completed') {
                            queue.splice(idx, 1);
                            this.broker.log(`SETTLEMENT_ACCEPT (${entityPath}) via management`);
                        } else if (disposition === 'suspended') {
                            const [removed] = queue.splice(idx, 1);
                            removed._lockToken = undefined; // Clear the lock token when moving
                            const dlqPath = `${entityPath}/$deadletterqueue`;
                            console.log(`[MockBroker] Moving message ${removed._sequenceNumber} to DLQ path: ${dlqPath}`);
                            this.broker.pushMessage(dlqPath, removed);
                            this.broker.log(`SETTLEMENT_REJECTED (${entityPath}) via management`);
                        } else if (disposition === 'abandoned') {
                            queue[idx]._lockToken = undefined;
                            this.broker.log(`SETTLEMENT_ABANDONED (${entityPath}) via management`);
                        }
                    }
                });

                response.application_properties.statusCode = 200;
                response.application_properties['status-code'] = 200;
            } else if (operation === 'com.microsoft:purge-messages') {
                // Purge all (or N) messages from the entity
                const rawCount = msg.body?.['message-count'];
                const msgCount = rawCount !== undefined ? Number(rawCount.value ?? rawCount) : Infinity;
                const queue = this.broker.getMessages(entityPath);
                const deleted = Math.min(msgCount, queue.length);
                queue.splice(0, deleted);
                response.application_properties.statusCode = 200;
                response.application_properties['status-code'] = 200;
                response.body = { 'message-count': deleted };

            } else {
                // Unknown operation: return 501
                response.application_properties.statusCode = 501;
                response.application_properties['status-code'] = 501;
                response.application_properties['status-description'] = 'Not Implemented';
            }
        }

        this.broker.pushMessage(replyTo, response);
    }
}

class MockSender extends EventEmitter {
    public id = _nextId++;
    public target = { address: '' };
    constructor(private broker: MockBroker, public address: string, public connection: MockConnection) {
        super();
        this.target.address = address;
        setTimeout(() => this.emit('sender_open', { sender: this }), 1);
    }
    public isOpen() { return true; }
    public isClosed() { return false; }
    public sendable() { return true; }
    public send(msg: any) {
        this.broker.log(`SEND (to: ${this.address})`);

        if (this.address.includes('$management') || this.address === '$cbs') {
            this.connection.handleSystemMessage(msg, this.address);
        } else {
            this.broker.pushMessage(this.address, msg);
        }
        const d = new EventEmitter();
        setTimeout(() => this.emit('accepted', { delivery: d }), 1);
        return d;
    }
    public close() {
        this.emit('sender_close', { sender: this });
    }
}

class MockReceiver extends EventEmitter {
    public id = _nextId++;
    public source = { address: '' };
    public credit = 0;
    constructor(private broker: MockBroker, public address: string, public connection: MockConnection) {
        super();
        this.source.address = address;
        const cleanAddr = this.broker.normalizePath(address);
        if (cleanAddr.includes('$') || cleanAddr.includes('reply')) this.credit = 999;
        setTimeout(() => this.emit('receiver_open', { receiver: this }), 1);
    }
    public isOpen() { return true; }
    public isClosed() { return false; }
    public add_credit(n: number) {
        this.credit += n;
        queueMicrotask(() => this.deliverMessages(this.address));
    }
    public deliverMessages(targetAddress: string) {
        const queueToCheck = this.broker.normalizePath(targetAddress || this.address);
        const queue = this.broker.getMessages(queueToCheck);

        while (queue.length > 0 && this.credit > 0) {
            const m = queue.shift();
            console.log(`[Receiver ${this.id}] Delivering from ${queueToCheck} (listening on ${this.address})`);
            const d = {
                tag: new Uint8Array([Math.floor(Math.random() * 255)]),
                settled: true,
                accept: () => { this.broker.log(`SETTLEMENT_ACCEPT (${this.address})`); },
                release: () => {
                    this.broker.log(`SETTLEMENT_RELEASE (${this.address})`);
                    const q = this.broker.getMessages(this.address);
                    q.unshift(m); // Put back at front of queue
                },
                modify: (options: any) => {
                    this.broker.log(`SETTLEMENT_MODIFY (${this.address})`);
                    const q = this.broker.getMessages(this.address);
                    if (options?.undeliverable_here) {
                        this.broker.pushMessage(`${this.address}/$deadletterqueue`, m);
                    } else {
                        q.unshift(m); // Put back at front
                    }
                },
                reject: (err: any) => {
                    this.broker.log(`SETTLEMENT_REJECTED (${this.address})`);
                    if (err?.condition === 'com.microsoft:dead-letter') {
                        this.broker.pushMessage(`${this.address}/$deadletterqueue`, m);
                    }
                }
            };
            this.emit('message', { message: m, delivery: d, receiver: this, connection: this.connection, session: this.connection.session });
            const isSystem = queueToCheck.includes('$') || queueToCheck.includes('reply');
            if (!isSystem) this.credit--;
        }
    }
    public close() {
        this.credit = 0;
        this.connection.receivers.delete(this);
        this.emit('receiver_close', { receiver: this });
    }
}

export const GlobalMockBroker = new MockBroker();
(globalThis as any).GlobalMockBroker = GlobalMockBroker;
