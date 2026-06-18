/**
 * Message Parsing Utilities
 * Handles parsing and decoding of AMQP messages
 */

import type { ServiceBusMessage } from './types.js';

interface AMQPMessage {
    message_id?: string;
    body: any;
    content_type?: string;
    correlation_id?: string;
    group_id?: string;  // AMQP session ID
    subject?: string;
    reply_to?: string;
    to?: string;
    delivery_count?: number;
    message_annotations?: Record<string, any>;
    application_properties?: Record<string, any>;
    properties?: Record<string, any>;
    ttl?: number;
    absolute_expiry_time?: Date;
    creation_time?: Date;
}

/**
 * Parse Service Bus message from AMQP format
 */
export function parseServiceBusMessage(amqpMessage: any): ServiceBusMessage {
    const decodedBody = decodeMessageBody(amqpMessage.body);

    // AMQP properties can be at top level or in a 'properties' section
    const props = amqpMessage.properties || {};

    // Safely parse TTL - ensure it's a valid number and within range
    let ttl: number | undefined = undefined;
    const ttlSource = amqpMessage.ttl ?? amqpMessage.header?.ttl;
    if (ttlSource !== undefined && ttlSource !== null) {
        const ttlValue = Number(ttlSource);
        if (!isNaN(ttlValue) && isFinite(ttlValue)) {
            ttl = ttlValue;
        }
    }

    // Get message annotations
    const annotations = amqpMessage.message_annotations || amqpMessage.messageAnnotations || amqpMessage.delivery_annotations || amqpMessage.deliveryAnnotations || {};

    const getAnnotationValue = (key: string): any => {
        if (!annotations) return undefined;
        if (annotations[key] !== undefined) return annotations[key];
        
        // Handle Symbol keys
        const symbols = Object.getOwnPropertySymbols(annotations);
        for (const sym of symbols) {
            const symStr = sym.toString();
            if (symStr === `Symbol(${key})` || sym.description === key || symStr.includes(key)) {
                return annotations[sym];
            }
        }
        
        // Check standard keys or Map keys as string
        for (const k of Object.keys(annotations)) {
            if (k === key || k.toString() === key) {
                return annotations[k];
            }
        }
        
        // If annotations is a Map
        try {
            if (typeof annotations.get === 'function') {
                const val = annotations.get(key);
                if (val !== undefined) return val;
                for (const [k, v] of annotations.entries()) {
                    if (k === key || (k && k.toString() === key)) {
                        return v;
                    }
                }
            }
        } catch {}

        return undefined;
    };

    // Helper to robustly decode AMQP long types (number, bigint, high/low object, buffer)
    const decodeAmqpLong = (val: any): number | undefined => {
        if (val === undefined || val === null) return undefined;
        if (typeof val === 'number') return val;
        if (typeof val === 'bigint') return Number(val);
        if (typeof val === 'object') {
            if ('high' in val && 'low' in val) {
                return val.high * 4294967296 + (val.low >>> 0);
            }
            if (val instanceof Uint8Array || val.type === 'Buffer' || Array.isArray(val.data)) {
                const buf = val.data ? new Uint8Array(val.data) : val;
                let num = BigInt(0);
                for (let i = 0; i < buf.length; i++) {
                    num = (num << BigInt(8)) + BigInt(buf[i]);
                }
                return Number(num);
            }
        }
        const parsed = Number(val);
        return isNaN(parsed) ? undefined : parsed;
    };

    // Helper to robustly decode AMQP timestamp/datetime types to ISO string
    const parseAmqpDate = (val: any): string | undefined => {
        if (val === undefined || val === null) return undefined;
        
        let d: Date | undefined;
        if (val instanceof Date) {
            d = val;
        } else {
            // If it's a number, bigint, or long object/buffer, decode to timestamp
            const ms = decodeAmqpLong(val);
            if (ms !== undefined && !isNaN(ms)) {
                d = new Date(ms);
            } else if (typeof val === 'string') {
                d = new Date(val);
            }
        }
        
        if (d && !isNaN(d.getTime())) {
            try {
                const year = d.getUTCFullYear();
                if (year > 9999) {
                    return "9999-12-31T23:59:59.999Z";
                }
                if (year < 1) {
                    return "0001-01-01T00:00:00.000Z";
                }
                return d.toISOString();
            } catch {
                return undefined;
            }
        }
        
        return undefined;
    };

    // Extract enqueued time
    const enqueuedTime = parseAmqpDate(getAnnotationValue('x-opt-enqueued-time'));

    // Extract scheduled enqueue time
    let scheduledEnqueueTime = getAnnotationValue('x-opt-scheduled-enqueue-time');
    if (scheduledEnqueueTime instanceof Date) {
        scheduledEnqueueTime = scheduledEnqueueTime.getTime();
    } else {
        scheduledEnqueueTime = decodeAmqpLong(scheduledEnqueueTime);
    }

    // Extract sequence number
    const sequenceNumber = decodeAmqpLong(getAnnotationValue('x-opt-sequence-number') ?? amqpMessage._sequenceNumber);

    // Extract partition key
    const partitionKey = getAnnotationValue('x-opt-partition-key') ?? amqpMessage.group_id ?? props.group_id;

    return {
        messageId: amqpMessage.message_id ?? props.message_id,
        body: decodedBody,
        contentType: amqpMessage.content_type ?? props.content_type,
        correlationId: amqpMessage.correlation_id ?? props.correlation_id,
        sessionId: amqpMessage.group_id ?? props.group_id,
        subject: amqpMessage.subject ?? props.subject,
        replyTo: amqpMessage.reply_to ?? props.reply_to,
        to: amqpMessage.to ?? props.to,
        deliveryCount: amqpMessage.delivery_count ?? amqpMessage.header?.delivery_count ?? 0,
        enqueuedTime: enqueuedTime,
        sequenceNumber: sequenceNumber,
        lockedUntil: parseAmqpDate(getAnnotationValue('x-opt-locked-until')),
        scheduledEnqueueTime: scheduledEnqueueTime,
        partitionKey: partitionKey,
        state: getAnnotationValue('x-opt-state'),
        applicationProperties: amqpMessage.application_properties || {},
        messageAnnotations: annotations,
        properties: props,
        ttl: ttl,
        expiryTime: parseAmqpDate(amqpMessage.absolute_expiry_time ?? props.absolute_expiry_time),
        creationTime: parseAmqpDate(amqpMessage.creation_time ?? props.creation_time)
    };
}

/**
 * Decode AMQP message body to text/bytes
 * Does NOT parse JSON - that's the consumer's responsibility
 */
export function decodeMessageBody(body: any): string {
    if (body === undefined || body === null) return '';

    // If it's already a string, return as-is
    if (typeof body === 'string') {
        return body;
    }

    // Unwrap AMQP data section (typecode 117 = binary data)
    if (body && typeof body === 'object' && body.typecode === 117) {
        body = body.content;
    }

    // Handle Node Buffer/Vite polyfill Buffer
    const isBuffer = typeof Buffer !== 'undefined' && Buffer.isBuffer(body);

    // Handle Uint8Array or Buffer - decode to text
    if (isBuffer || body instanceof Uint8Array) {
        const decoder = new TextDecoder('utf-8');
        return decoder.decode(body);
    }

    // Extract byte array from Buffer JSON representation (if it was already stringified/parsed)
    if (body && body.type === 'Buffer' && Array.isArray(body.data)) {
        const bytes = new Uint8Array(body.data);
        const decoder = new TextDecoder('utf-8');
        return decoder.decode(bytes);
    }

    // AMQP value type - recursively unwrap
    if (body && typeof body === 'object' && body.typecode !== undefined) {
        if (body.value !== undefined) {
            return decodeMessageBody(body.value);
        }
        if (body.content !== undefined) {
            return decodeMessageBody(body.content);
        }
    }

    // If it's already a plain object or other type, return as-is
    return body;
}

/**
 * Decode JWT token payload (for debugging)
 */
export function decodeToken(token: string): {
    audience: string;
    roles: string[];
    expiry: Date;
    issuer: string;
} | null {
    try {
        const parts = token.split('.');
        if (parts.length === 3) {
            const payload = JSON.parse(atob(parts[1]));
            return {
                audience: payload.aud,
                roles: payload.roles || [],
                expiry: new Date(payload.exp * 1000),
                issuer: payload.iss
            };
        }
    } catch (e) {
        return null;
    }
    return null;
}
