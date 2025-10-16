/**
 * Service Bus Connection Management
 * Handles AMQP connection and CBS authentication
 */

import rhea from 'rhea';
import type { Connection } from 'rhea';
import type { CBSAuthResult } from './types.js';

/**
 * Service Bus Connection with CBS authentication
 */
export class ServiceBusConnection {
    public readonly namespace: string;
    public readonly hostname: string;
    public connection: Connection | null = null;
    private readonly token: string;
    private cbsAuthenticated: boolean = false;

    constructor(namespace: string, token: string) {
        this.namespace = namespace;
        this.token = token;
        this.hostname = `${namespace}.servicebus.windows.net`;
    }

    /**
     * Connect to Service Bus via AMQP over WebSocket
     */
    async connect(): Promise<void> {
        return new Promise((resolve, reject) => {
            const wsUrl = `wss://${this.hostname}:443/$servicebus/websocket`;
            
            // Use rhea's websocket_connect helper with browser's native WebSocket
            // The browser's WebSocket constructor
            const wsFactory = rhea.websocket_connect(WebSocket as any);
            
            this.connection = rhea.connect({
                connection_details: wsFactory(wsUrl, ['AMQPWSB10'], {}) as any,
                host: this.hostname,
                hostname: this.hostname,
                port: 443,
                transport: 'ssl',
                reconnect: false
            });

            this.connection.on('connection_open', () => {
                resolve();
            });

            this.connection.on('connection_error', (context: any) => {
                reject(new Error(context.error ? context.error.toString() : 'Connection error'));
            });

            this.connection.on('disconnected', (context: any) => {
                if (context.error && !this.cbsAuthenticated) {
                    reject(new Error(context.error.toString()));
                }
            });
        });
    }

    /**
     * Perform CBS (Claims-Based Security) authentication
     */
    async authenticateCBS(entityPath: string): Promise<CBSAuthResult> {
        if (!this.connection) {
            throw new Error('Connection not established');
        }

        return new Promise((resolve, reject) => {
            const cbsSender = this.connection!.open_sender('$cbs');
            const cbsReceiver = this.connection!.open_receiver('$cbs');
            
            cbsReceiver.on('message', (context: any) => {
                const statusCode = context.message.application_properties['status-code'];
                const statusDesc = context.message.application_properties['status-description'];
                
                cbsReceiver.close();
                cbsSender.close();
                
                if (statusCode === 200 || statusCode === 202) {
                    this.cbsAuthenticated = true;
                    resolve({ statusCode, statusDesc });
                } else {
                    reject(new Error(`CBS auth failed: ${statusCode} - ${statusDesc}`));
                }
            });
            
            cbsSender.on('sender_open', () => {
                const tokenMessage = {
                    application_properties: {
                        'operation': 'put-token',
                        'type': 'jwt',
                        'name': `sb://${this.hostname}/${entityPath}`
                    },
                    body: this.token
                };
                
                cbsSender.send(tokenMessage);
            });

            setTimeout(() => reject(new Error('CBS authentication timeout')), 10000);
        });
    }

    /**
     * Close the connection
     */
    close(): void {
        if (this.connection) {
            this.connection.close();
        }
    }
}
