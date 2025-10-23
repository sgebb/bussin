/**
 * Type definitions for Azure Service Bus operations
 */

import type { Message as RheaMessage, Connection, Sender, Receiver } from 'rhea';

/**
 * Parsed Service Bus message
 */
export interface ServiceBusMessage {
    messageId: string | undefined;
    body: string;
    contentType: string | undefined;
    deliveryCount: number;
    enqueuedTime: Date | undefined;
    sequenceNumber: number | undefined;
    lockedUntil: Date | undefined;
    applicationProperties: Record<string, any>;
    properties: Record<string, any>;
    ttl: number | undefined;
    expiryTime: Date | undefined;
    creationTime: Date | undefined;
    originalBody?: any; // Preserve original binary body for resend operations
    originalContentType?: string; // Preserve original content type
}

/**
 * Connection options
 */
export interface ConnectionOptions {
    namespace: string;
    token: string;
}

/**
 * Message receiver options
 */
export interface ReceiverOptions {
    peekMode?: boolean;
    maxMessages?: number | null;
    autoClose?: boolean;
}

/**
 * Message properties for sending
 */
export interface MessageProperties {
    message_id?: string;
    messageId?: string;
    content_type?: string;
    contentType?: string;
    message_annotations?: Record<string, any>; // For scheduled messages
    original_body?: any; // Preserve original binary body for resend operations
    original_content_type?: string; // Preserve original content type for resend operations
    [key: string]: any;
}

/**
 * Purge controller
 */
export interface PurgeController {
    promise: Promise<number>;
    stop: () => number;
    getCount: () => number;
}

/**
 * Monitor controller
 */
export interface MonitorController {
    stop: () => void;
}

/**
 * CBS authentication result
 */
export interface CBSAuthResult {
    statusCode: number;
    statusDesc: string;
}

/**
 * Progress callback for purge operations
 */
export type ProgressCallback = (deletedCount: number) => void;

/**
 * Message callback for monitoring
 */
export type MessageCallback = (message: ServiceBusMessage) => void;

/**
 * Error callback
 */
export type ErrorCallback = (error: Error) => void;

/**
 * Batch operation result
 */
export interface BatchOperationResult {
    successCount: number;
    failureCount: number;
    errors: Array<{ messageId: string; error: string }>;
}

/**
 * Locked message with lock token for settlement
 * The lock token is used to settle the message (complete/abandon/deadletter).
 * The actual AMQP handle is stored server-side and looked up by lock token.
 */
export interface LockedMessage extends ServiceBusMessage {
    lockToken: string;
}

/**
 * Dead letter options
 */
export interface DeadLetterOptions {
    deadLetterReason?: string;
    deadLetterErrorDescription?: string;
}
