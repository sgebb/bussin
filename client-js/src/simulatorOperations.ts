import { GlobalMockBroker } from './mockBroker.js';

/**
 * Activate or deactivate the in-process mock broker.
 * Must be called before any ServiceBus API calls in demo mode.
 */
export function enableSimulator(enabled: boolean): void {
    (globalThis as any).__BUSSIN_SIMULATOR_ACTIVE__ = enabled;
    if (enabled) {
        console.log('[ServiceBusAPI] Simulator ENABLED - AMQP calls will use MockBroker');
    }
}

/**
 * Seed a queue with initial messages (for demo/test setup).
 * @param messages - array of { body, messageId? } objects
 */
export function seedMockData(
    namespace: string,
    queueName: string,
    messages: { body: any; messageId?: string }[]
): void {
    GlobalMockBroker.createQueue(queueName);
    for (const m of messages) {
        const body = typeof m.body === 'string' ? m.body : JSON.stringify(m.body);
        GlobalMockBroker.pushMessage(queueName, {
            body: new TextEncoder().encode(body),
            message_id: m.messageId ?? `seed-${Date.now()}-${Math.random().toString(36).substr(2, 5)}`,
            application_properties: {},
            content_type: 'application/json; charset=utf-8'
        });
    }
    console.log(`[ServiceBusAPI] Seeded ${messages.length} messages into ${queueName}`);
}

/**
 * Create (or reset) a topic in the mock broker topology.
 */
export function seedTopic(namespace: string, topicName: string): void {
    GlobalMockBroker.createTopic(topicName);
    console.log(`[ServiceBusAPI] Created topic: ${topicName}`);
}

/**
 * Create a subscription under a topic in the mock broker topology.
 */
export function seedSubscription(namespace: string, topicName: string, subscriptionName: string): void {
    GlobalMockBroker.createSubscription(topicName, subscriptionName);
    console.log(`[ServiceBusAPI] Created subscription: ${topicName}/subscriptions/${subscriptionName}`);
}

/**
 * Seed a topic with messages (broadcast to all subs).
 */
export function seedTopicData(
    namespace: string,
    topicName: string,
    messages: { body: any; messageId?: string; properties?: any }[]
): void {
    for (const m of messages) {
        const body = typeof m.body === 'string' ? m.body : JSON.stringify(m.body);
        GlobalMockBroker.pushMessage(topicName, {
            body: new TextEncoder().encode(body),
            message_id: m.messageId ?? `seed-${Date.now()}-${Math.random().toString(36).substr(2, 5)}`,
            application_properties: {},
            content_type: 'application/json; charset=utf-8',
            ...m.properties
        });
    }
}

/**
 * Seed messages directly into a subscription's queue.
 */
export function seedSubscriptionData(
    namespace: string,
    topicName: string,
    subscriptionName: string,
    messages: { body: any; messageId?: string; properties?: any }[]
): void {
    const subPath = `${topicName}/subscriptions/${subscriptionName}`;
    for (const m of messages) {
        const body = typeof m.body === 'string' ? m.body : JSON.stringify(m.body);
        GlobalMockBroker.pushMessage(subPath, {
            body: new TextEncoder().encode(body),
            message_id: m.messageId ?? `seed-sub-${Date.now()}-${Math.random().toString(36).substr(2, 5)}`,
            application_properties: {},
            content_type: 'application/json; charset=utf-8',
            ...m.properties
        });
    }
}

/**
 * Return the current message count for a queue (synchronous).
 * Used by IJSInProcessRuntime.Invoke from Blazor.
 */
export function getQueueMessageCount(namespace: string, queueName: string, fromDeadLetter: boolean = false): number {
    const path = fromDeadLetter ? `${queueName}/$deadletterqueue` : queueName;
    return GlobalMockBroker.getMessages(path).length;
}

/**
 * Return the current message count for a subscription (synchronous).
 * Used by IJSInProcessRuntime.Invoke from Blazor.
 */
export function getSubscriptionMessageCount(namespace: string, topicName: string, subscriptionName: string, fromDeadLetter: boolean = false): number {
    const basePath = `${topicName}/subscriptions/${subscriptionName}`;
    const path = fromDeadLetter ? `${basePath}/$deadletterqueue` : basePath;
    return GlobalMockBroker.getMessages(path).length;
}
