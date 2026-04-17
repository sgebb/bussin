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
    const annotations = amqpMessage.message_annotations || {};
    
    // Extract enqueued time - could be Date or timestamp
    let enqueuedTime = annotations['x-opt-enqueued-time'];
    if (enqueuedTime && typeof enqueuedTime === 'number') {
        enqueuedTime = new Date(enqueuedTime);
    }
    
    // Extract scheduled enqueue time
    let scheduledEnqueueTime = annotations['x-opt-scheduled-enqueue-time'];
    if (scheduledEnqueueTime instanceof Date) {
        scheduledEnqueueTime = scheduledEnqueueTime.getTime();
    }
    
    // Extract partition key
    const partitionKey = annotations['x-opt-partition-key'] ?? amqpMessage.group_id ?? props.group_id;
    
    return {
        messageId: amqpMessage.message_id ?? props.message_id,
        body: decodedBody,
        contentType: amqpMessage.content_type ?? props.content_type,
        correlationId: amqpMessage.correlation_id ?? props.correlation_id,
        sessionId: amqpMessage.group_id ?? props.group_id,  // AMQP group_id maps to Service Bus session ID
        subject: amqpMessage.subject ?? props.subject,
        replyTo: amqpMessage.reply_to ?? props.reply_to,
        to: amqpMessage.to ?? props.to,
        deliveryCount: amqpMessage.delivery_count ?? amqpMessage.header?.delivery_count ?? 0,
        enqueuedTime: enqueuedTime,
        sequenceNumber: annotations['x-opt-sequence-number'],
        lockedUntil: annotations['x-opt-locked-until'],
        scheduledEnqueueTime: scheduledEnqueueTime,
        partitionKey: partitionKey,
        applicationProperties: amqpMessage.application_properties || {},
        properties: props,
        ttl: ttl,
        expiryTime: amqpMessage.absolute_expiry_time ?? props.absolute_expiry_time,
        creationTime: amqpMessage.creation_time ?? props.creation_time
    };
}

/**
 * Decode AMQP message body to text/bytes
 * Does NOT parse JSON - that's the consumer's responsibility
 */
export function decodeMessageBody(body: any): string {
    // If it's already a string, return as-is
    if (typeof body === 'string') {
        return body;
    }
    
    // Unwrap AMQP data section (typecode 117 = binary data)
    if (body && typeof body === 'object' && body.typecode === 117) {
        body = body.content;
    }
    
    // Handle Uint8Array (from decoded messages) - decode to text
    if (body instanceof Uint8Array) {
        const decoder = new TextDecoder('utf-8');
        return decoder.decode(body);
    }
    
    // Extract byte array from Buffer object (from received messages) - decode to text
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
