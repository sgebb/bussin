/**
 * Service Bus Message Receiver
 * Handles receiving messages from queues/subscriptions
 */

import type { ServiceBusConnection } from './connection.js';
import type { ReceiverOptions, MessageCallback, ErrorCallback } from './types.js';
import type { Receiver, Message } from 'rhea';

/**
 * Message Receiver - for listening to messages (peek-lock mode)
 */
export class MessageReceiver {
    private readonly connection: ServiceBusConnection;
    private readonly queueName: string;
    private readonly options: Required<ReceiverOptions>;
    private receiver: Receiver | null = null;
    private messageCount: number = 0;

    constructor(connection: ServiceBusConnection, queueName: string, options: ReceiverOptions = {}) {
        this.connection = connection;
        this.queueName = queueName;
        this.options = {
            peekMode: options.peekMode || false,
            maxMessages: options.maxMessages ?? null,
            autoClose: options.autoClose !== false
        };
    }

    /**
     * Start receiving messages
     */
    receive(onMessage: MessageCallback | ((message: Message) => void), onError?: ErrorCallback): void {
        if (!this.connection.connection) {
            throw new Error('Connection not established');
        }

        // rcv_settle_mode: 0 = first (receive and delete), 1 = second (peek/browse)
        const settleMode = this.options.peekMode ? 1 : 0;
        
        // For peek mode monitoring, use manual credit (0) to avoid automatic re-delivery
        // For receive-and-delete (purge), use credit_window for continuous flow
        const creditWindow = this.options.peekMode ? 0 : (this.options.maxMessages || 10);
        
        this.receiver = this.connection.connection.open_receiver({
            source: { address: this.queueName },
            credit_window: creditWindow,
            autoaccept: !this.options.peekMode,
            rcv_settle_mode: settleMode
        });

        this.receiver.on('message', (context: any) => {
            this.messageCount++;
            
            if (onMessage) {
                onMessage(context.message);
            }

            // Auto-close if max messages reached
            if (this.options.maxMessages && this.messageCount >= this.options.maxMessages) {
                if (this.options.autoClose) {
                    this.close();
                    this.connection.close();
                }
            }
            
            // For peek mode with manual credit, add one more credit after each message
            // This prevents re-reading the same messages while still allowing monitoring
            if (this.options.peekMode && creditWindow === 0) {
                this.receiver?.add_credit(1);
            }
        });

        this.receiver.on('receiver_error', (context: any) => {
            if (onError) {
                const error = context.receiver.error;
                onError(new Error(error ? error.toString() : 'Receiver error'));
            }
        });
        
        // For peek mode with manual credit, issue initial credit
        if (this.options.peekMode && creditWindow === 0) {
            this.receiver.add_credit(1);
        }
    }

    /**
     * Add credit to request more messages (for batch operations)
     */
    add_credit(credit: number): void {
        if (this.receiver) {
            this.receiver.add_credit(credit);
        }
    }

    /**
     * Close the receiver
     */
    close(): void {
        if (this.receiver) {
            this.receiver.close();
        }
    }
}
