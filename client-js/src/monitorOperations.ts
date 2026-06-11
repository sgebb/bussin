import { ServiceBusConnection } from './connection.js';
import { ManagementClient } from './managementClient.js';
import { parseServiceBusMessage } from './messageParser.js';
import type { MessageCallback, ErrorCallback, MonitorController } from './types.js';

/**
 * Start monitoring messages from a queue (non-destructive, continuous)
 */
export async function monitorQueue(
    namespace: string,
    queueName: string,
    token: string,
    onMessage: MessageCallback,
    onError?: ErrorCallback,
    dotnetRef?: any
): Promise<MonitorController> {
    return await startMonitoring(namespace, queueName, token, onMessage, onError, dotnetRef);
}

/**
 * Start monitoring messages from a subscription (non-destructive, continuous)
 */
export async function monitorSubscription(
    namespace: string,
    topicName: string,
    subscriptionName: string,
    token: string,
    onMessage: MessageCallback,
    onError?: ErrorCallback,
    dotnetRef?: any
): Promise<MonitorController> {
    const entityPath = `${topicName}/subscriptions/${subscriptionName}`;
    return await startMonitoring(namespace, entityPath, token, onMessage, onError, dotnetRef);
}

// Internal implementation - non-destructive monitoring using Management API
async function startMonitoring(
    namespace: string,
    entityPath: string,
    token: string,
    onMessage: MessageCallback,
    onError?: ErrorCallback,
    dotnetRef?: any
): Promise<MonitorController> {
    const connection = new ServiceBusConnection(namespace, token);
    let isRunning = true;
    let lastSequenceNumber = 0;
    let pollInterval: NodeJS.Timeout | null = null;
    let managementClient: ManagementClient | null = null;

    // Track token refresh timing
    let lastAuthTime = Date.now();
    const tokenRefreshIntervalMs = 20 * 60 * 1000; // Refresh every 20 minutes

    const setupManagementLink = async () => {
        await connection.connect();
        await connection.authenticateCBS(entityPath);
        managementClient = new ManagementClient(connection, entityPath);
        await managementClient.open();
    };

    const pollForMessages = async () => {
        if (!isRunning) return;

        try {
            // Check if token needs refresh
            if (dotnetRef && Date.now() - lastAuthTime > tokenRefreshIntervalMs) {
                console.log('[ServiceBusAPI] Refreshing CBS token for monitor...');
                try {
                    const newToken = await dotnetRef.invokeMethodAsync('GetFreshToken');
                    if (newToken) {
                        connection.token = newToken;
                        await connection.authenticateCBS(entityPath);
                        lastAuthTime = Date.now();
                        console.log('[ServiceBusAPI] CBS token refreshed successfully.');
                    }
                } catch (refreshErr) {
                    console.error('[ServiceBusAPI] Failed to refresh monitor token:', refreshErr);
                }
            }

            // Ensure connection and managementClient are open
            if (!connection.connection || !managementClient) {
                await setupManagementLink();
            }

            // Peek messages starting from the last sequence number + 1
            // Fetch up to 50 messages to handle higher throughput
            const messages = await managementClient!.peekMessages(lastSequenceNumber + 1, 50);

            // Process any new messages
            for (const message of messages) {
                if (message.message_annotations?.['x-opt-sequence-number']) {
                    const seqNum = message.message_annotations['x-opt-sequence-number'];
                    if (seqNum > lastSequenceNumber) {
                        lastSequenceNumber = seqNum;
                        onMessage(parseServiceBusMessage(message));
                    }
                }
            }

            // Schedule next poll if still running (faster polling for better responsiveness)
            if (isRunning) {
                pollInterval = setTimeout(pollForMessages, messages.length > 0 ? 200 : 2000);
            }

        } catch (err) {
            console.error('[ServiceBusAPI] Monitor poll error, will reconnect:', err);
            // Clean up management link so it will reconnect on next poll
            if (managementClient) {
                try { managementClient.close(); } catch {}
                managementClient = null;
            }
            try { connection.close(); } catch {}

            if (onError && isRunning) {
                onError(err as Error);
            }
            // Retry connection after error
            if (isRunning) {
                pollInterval = setTimeout(pollForMessages, 5000); // Wait 5 seconds before retry
            }
        }
    };

    // Start monitoring
    try {
        await setupManagementLink();

        // Get initial sequence number to start from (try to get the latest message)
        const initialMessages = await managementClient!.peekMessages(0, 100);
        if (initialMessages.length > 0) {
            // Find the highest sequence number
            for (const msg of initialMessages) {
                if (msg.message_annotations?.['x-opt-sequence-number']) {
                    const seqNum = msg.message_annotations['x-opt-sequence-number'];
                    if (seqNum > lastSequenceNumber) {
                        lastSequenceNumber = seqNum;
                    }
                }
            }
        }

        // Start polling loop
        pollInterval = setTimeout(pollForMessages, 100);

    } catch (err) {
        if (managementClient) {
            try { managementClient.close(); } catch {}
        }
        connection.close();
        if (onError) {
            onError(err as Error);
        }
        throw new Error(`Monitor failed: ${(err as Error).message}`);
    }

    return {
        stop: () => {
            isRunning = false;
            if (pollInterval) {
                clearTimeout(pollInterval);
            }
            if (managementClient) {
                try { managementClient.close(); } catch {}
            }
            connection.close();
        }
    };
}
