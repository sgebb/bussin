/**
 * Service Bus Connection Management
 * Handles AMQP connection and CBS authentication
 */

import rhea from 'rhea';
import type { Connection } from 'rhea';
import type { CBSAuthResult } from './types.js';
import { formatAmqpError } from './types.js';

import { GlobalMockBroker } from './mockBroker.js';

const CONNECT_TIMEOUT_MS = 15000;

/**
 * Service Bus Connection with CBS authentication
 */
export class ServiceBusConnection {
    public readonly namespace: string;
    public readonly hostname: string;
    public connection: Connection | null = null;
    public token: string;
    private useMock: boolean = false;
    private readonly customWebSocketUrl: string | null = null;

    constructor(namespace: string, token: string) {
        this.token = token;
        
        if (namespace.startsWith('ws://') || namespace.startsWith('wss://')) {
            this.customWebSocketUrl = namespace;
            try {
                const url = new URL(namespace);
                this.hostname = url.hostname;
                this.namespace = url.hostname;
            } catch {
                this.hostname = namespace;
                this.namespace = namespace;
            }
        } else if (namespace.includes(':')) {
            this.hostname = namespace.split(':')[0];
            this.namespace = this.hostname;
            this.customWebSocketUrl = `ws://${namespace}/$servicebus/websocket`;
        } else {
            this.useMock = (globalThis as any).__BUSSIN_SIMULATOR_ACTIVE__ === true;
            let cleanNamespace = namespace;
            if (cleanNamespace.endsWith('.servicebus.windows.net')) {
                cleanNamespace = cleanNamespace.substring(0, cleanNamespace.length - '.servicebus.windows.net'.length);
            }
            this.hostname = this.useMock 
                ? `${cleanNamespace}.bussin.internal` 
                : `${cleanNamespace}.servicebus.windows.net`;
            this.namespace = cleanNamespace;
        }
    }

    /**
     * Connect to Service Bus via AMQP over WebSocket. Rejects if the connection fails, drops or has not
     * opened within CONNECT_TIMEOUT_MS. Safe to call again after a drop: each call opens a new connection.
     */
    async connect(): Promise<void> {
        return new Promise((resolve, reject) => {
            let settled = false;
            const settle = (error?: Error) => {
                if (settled) return;
                settled = true;
                clearTimeout(timeout);
                if (error) {
                    this.close();
                    reject(error);
                } else {
                    resolve();
                }
            };
            const timeout = setTimeout(() => settle(new Error(`Connecting to ${this.hostname} timed out`)), CONNECT_TIMEOUT_MS);

            const wsUrl = this.customWebSocketUrl || `wss://${this.hostname}:443/$servicebus/websocket`;
            const isSecure = wsUrl.startsWith('wss://');
            let port = 443;
            try {
                const urlObj = new URL(wsUrl);
                port = urlObj.port ? parseInt(urlObj.port) : (isSecure ? 443 : 80);
            } catch { }
            
            if ((globalThis as any).__BUSSIN_SIMULATOR_ACTIVE__) {
                console.log(`[ServiceBusConnection] Initializing Mock Connection for ${this.namespace}`);
                this.connection = GlobalMockBroker.connect({ container_id: `bussin-${this.namespace}` }) as any as Connection;
                settle();
                return;
            } else {
                // Use rhea's websocket_connect helper with browser's native WebSocket
                const wsFactory = rhea.websocket_connect(WebSocket as any);
                
                this.connection = rhea.connect({
                    connection_details: wsFactory(wsUrl, ['AMQPWSB10'], {}) as any,
                    host: this.hostname,
                    hostname: this.hostname,
                    port: port,
                    transport: isSecure ? 'ssl' : undefined,
                    reconnect: false
                } as any);
            }

            this.connection!.on('connection_open', () => {
                console.log(`[ServiceBusConnection] Connection OPEN for ${this.namespace}`);
                settle();
            });

            this.connection.on('connection_error', (context: any) => {
                console.error(`[ServiceBusConnection] Connection ERROR:`, context.error);
                settle(new Error(context.error ? formatAmqpError(context.error) : 'Connection error'));
            });

            // Only matters before the connection opened; settle ignores later calls
            this.connection.on('disconnected', (context: any) => {
                settle(new Error(context.error ? formatAmqpError(context.error) : `Disconnected from ${this.hostname}`));
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

            // The $cbs links are closed however authentication ends
            let settled = false;
            const settle = (outcome: () => void) => {
                if (settled) return;
                settled = true;
                clearTimeout(authTimeout);
                cbsReceiver.close();
                cbsSender.close();
                outcome();
            };

            const authTimeout = setTimeout(() => {
                settle(() => reject(new Error('CBS authentication timeout')));
            }, 5000);

            cbsReceiver.on('message', (context: any) => {
                const msg = context.message;
                const statusCode = msg.application_properties?.['status-code'];
                const statusDesc = msg.application_properties?.['status-description'];

                console.log(`[ServiceBusConnection] CBS Message Received: ${statusCode} - ${statusDesc}`);

                settle(() => statusCode === 200 || statusCode === 202
                    ? resolve({ statusCode, statusDesc })
                    : reject(new Error(`CBS auth failed: ${statusCode} - ${statusDesc}`)));
            });

            const linkError = (error: any) =>
                settle(() => reject(new Error(`CBS authentication failed: ${error ? formatAmqpError(error) : 'link error'}`)));
            cbsSender.on('sender_error', (context: any) => linkError(context.sender.error));
            cbsReceiver.on('receiver_error', (context: any) => linkError(context.receiver.error));
            
            cbsSender.on('sender_open', () => {
                console.log(`[ServiceBusConnection] CBS Sender OPEN - Sending token for ${entityPath}`);
                const isSasToken = this.token.startsWith('SharedAccessSignature ');
                const tokenMessage = {
                    application_properties: {
                        'operation': 'put-token',
                        'type': isSasToken ? 'servicebus.windows.net:sastoken' : 'jwt',
                        'name': `sb://${this.hostname}/${entityPath}`
                    },
                    body: this.token
                };
                
                cbsSender.send(tokenMessage);
            });

            cbsReceiver.on('receiver_open', () => {
                console.log(`[ServiceBusConnection] CBS Receiver OPEN`);
            });
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
