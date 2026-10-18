import { ServiceBusConnection } from './connection.js';
import { ManagementClient } from './managementClient.js';
import { parseServiceBusMessage } from './messageParser.js';
import type { MessageCallback, ErrorCallback, MonitorController } from './types.js';

// Each empty peek is one request, so the idle interval sets the request rate. It also sets how
// long a fresh message must survive in the entity before we see it: a consumer that completes it
// faster than this wins the race, and the watch never shows it.
const IDLE_POLL_MS = 1000;
const BUSY_POLL_MS = 200;
const ERROR_RETRY_MS = 5000;
const PEEK_BATCH = 50;
const TOKEN_REFRESH_MS = 20 * 60 * 1000;

const sequenceOf = (message: any): number | undefined => message.message_annotations?.['x-opt-sequence-number'];

/**
 * Highest sequence number currently in the entity, or -1 if it is empty.
 * Peek-from-N returns the first message at or after N, so "is there a message at or after N" is
 * monotonic in N: gallop upwards from the oldest message until a probe comes back empty, then
 * binary search the gap. Costs O(log n) peeks regardless of backlog size.
 */
async function findLastSequenceNumber(client: ManagementClient): Promise<number> {
    const peekOne = async (from: number) => {
        const [message] = await client.peekMessages(from, 1);
        return message ? sequenceOf(message) : undefined;
    };

    const first = await peekOne(0);
    if (first === undefined) return -1;

    let lo = first; // a message exists at lo
    let hi: number;  // no message at or after hi
    for (let step = 1; ; step *= 2) {
        const found = await peekOne(lo + step);
        if (found === undefined) { hi = lo + step; break; }
        lo = found;
    }
    while (hi - lo > 1) {
        const mid = Math.floor((lo + hi) / 2);
        const found = await peekOne(mid);
        if (found === undefined || found >= hi) hi = mid;
        else lo = found;
    }
    return lo;
}

/**
 * Watch an entity for messages that arrive after the call, by polling peek from a cursor.
 * Non-destructive, and best-effort: a message that a consumer completes between two polls is never seen.
 * @param entityPath queue name, `topic/subscriptions/sub`, either with an optional `/$DeadLetterQueue` suffix
 * @param getFreshToken called every 20 minutes to re-authenticate the long-lived connection
 */
export async function monitorEntity(
    namespace: string,
    entityPath: string,
    token: string,
    onMessage: MessageCallback,
    onError: ErrorCallback,
    getFreshToken: () => Promise<string>
): Promise<MonitorController> {
    const connection = new ServiceBusConnection(namespace, token);
    let isRunning = true;
    let lastSequenceNumber = -1;
    let pollTimer: ReturnType<typeof setTimeout> | null = null;
    let managementClient: ManagementClient | null = null;
    let lastAuthTime = Date.now();

    const openManagementLink = async (): Promise<ManagementClient> => {
        await connection.connect();
        await connection.authenticateCBS(entityPath);
        const client = new ManagementClient(connection, entityPath);
        await client.open();
        lastAuthTime = Date.now();
        return client;
    };

    const closeAll = () => {
        if (managementClient) {
            try { managementClient.close(); } catch {}
            managementClient = null;
        }
        try { connection.close(); } catch {}
    };

    const poll = async () => {
        if (!isRunning) return;
        let delay = IDLE_POLL_MS;
        try {
            if (Date.now() - lastAuthTime > TOKEN_REFRESH_MS) {
                connection.token = await getFreshToken();
                if (managementClient) {
                    await connection.authenticateCBS(entityPath);
                    lastAuthTime = Date.now();
                }
            }
            managementClient ??= await openManagementLink();

            const messages = await managementClient.peekMessages(lastSequenceNumber + 1, PEEK_BATCH);
            for (const message of messages) {
                const seq = sequenceOf(message);
                if (seq !== undefined && seq > lastSequenceNumber) {
                    lastSequenceNumber = seq;
                    onMessage(parseServiceBusMessage(message));
                }
            }
            if (messages.length > 0) delay = BUSY_POLL_MS;
        } catch (err) {
            console.error('[ServiceBusAPI] Watch poll failed, reconnecting:', err);
            closeAll();
            if (isRunning) onError(err as Error);
            delay = ERROR_RETRY_MS;
        }
        if (isRunning) pollTimer = setTimeout(poll, delay);
    };

    try {
        managementClient = await openManagementLink();
        lastSequenceNumber = await findLastSequenceNumber(managementClient);
    } catch (err) {
        closeAll();
        throw new Error(`Watch failed to start: ${(err as Error).message}`);
    }
    pollTimer = setTimeout(poll, IDLE_POLL_MS);

    return {
        stop: () => {
            isRunning = false;
            if (pollTimer) clearTimeout(pollTimer);
            closeAll();
        }
    };
}
