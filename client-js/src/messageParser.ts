/**
 * Message Parsing Utilities
 * Handles parsing and decoding of AMQP messages
 */

import type { ServiceBusMessage } from './types.js';

interface AMQPMessage {
    message_id?: string;
    body: any;
    content_type?: string;
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
    // Extract the original binary body before decoding for display
    let originalBody: any = undefined;
    let originalContentType: string | undefined = undefined;

    // Handle AMQP message body - preserve original format
    if (amqpMessage.body) {
        originalBody = amqpMessage.body;
        originalContentType = amqpMessage.content_type;
    }

    const decodedBody = decodeMessageBody(amqpMessage.body);
    
    // Safely parse TTL - ensure it's a valid number and within range
    let ttl: number | undefined = undefined;
    if (amqpMessage.ttl !== undefined && amqpMessage.ttl !== null) {
        const ttlValue = Number(amqpMessage.ttl);
        if (!isNaN(ttlValue) && isFinite(ttlValue)) {
            ttl = ttlValue;
        }
    }
    
    return {
        messageId: amqpMessage.message_id,
        body: decodedBody,
        contentType: amqpMessage.content_type,
        deliveryCount: amqpMessage.delivery_count || 0,
        enqueuedTime: amqpMessage.message_annotations?.['x-opt-enqueued-time'],
        sequenceNumber: amqpMessage.message_annotations?.['x-opt-sequence-number'],
        lockedUntil: amqpMessage.message_annotations?.['x-opt-locked-until'],
        applicationProperties: amqpMessage.application_properties || {},
        properties: amqpMessage.properties || {},
        ttl: ttl,
        expiryTime: amqpMessage.absolute_expiry_time,
        creationTime: amqpMessage.creation_time,
        originalBody: originalBody,
        originalContentType: originalContentType
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
