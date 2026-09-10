/**
 * The contents of --demo mode.
 *
 * Deliberately mirrors the web app's demo environment (Services/Demo/DemoAzureResourceService.cs
 * and DemoServiceBusJsInteropService.cs) so that app.bussin.dev/demo and `bussin-mcp --demo`
 * show the same namespaces, entities and messages. Both run the same in-process broker from
 * client-js; only the seed data used to differ.
 *
 * Counts are read back from the simulator rather than hard-coded, so they stay correct after
 * a send or a purge performed through the MCP tools.
 */

export const DEMO_NAMESPACE = 'bussin-demo-prod';

interface DemoMessage {
    body: unknown;
    messageId?: string;
    properties?: Record<string, unknown>;
}

interface DemoTopic {
    name: string;
    subscriptions: string[];
    /** Broadcast to every subscription on the topic. */
    messages?: DemoMessage[];
    /** Delivered to one named subscription only. */
    subscriptionMessages?: Record<string, DemoMessage[]>;
}

interface DemoNamespace {
    name: string;
    resourceGroup: string;
    subscriptionId: string;
    location: string;
    queues: Array<{ name: string; messages: DemoMessage[] }>;
    topics: DemoTopic[];
}

export const DEMO_NAMESPACES: DemoNamespace[] = [
    {
        name: 'bussin-demo-prod',
        resourceGroup: 'bussin-demo-rg',
        subscriptionId: '00000000-0000-0000-0000-000000000000',
        location: 'West Europe',
        queues: [
            {
                name: 'orders',
                messages: [
                    {
                        body: { orderId: 'ORD-101', customer: 'Alice', total: 42.50 },
                        messageId: 'msg-ord-1',
                        properties: { subject: 'New Order', content_type: 'application/json' }
                    },
                    {
                        body: { orderId: 'ORD-102', customer: 'Bob', total: 12.99 },
                        messageId: 'msg-ord-2',
                        properties: { subject: 'New Order' }
                    }
                ]
            },
            {
                name: 'notifications',
                messages: [
                    { body: 'Welcome to Bussin!', messageId: 'msg-notif-1' },
                    { body: 'Your order was shipped', messageId: 'msg-notif-2' }
                ]
            },
            // Present in the app's demo but never seeded, so it shows as an empty queue.
            { name: 'audit-log', messages: [] }
        ],
        topics: [
            {
                name: 'order-events',
                subscriptions: ['inventory-processor', 'email-sender'],
                messages: [
                    { body: { event: 'OrderPlaced', id: 'ORD-101' }, messageId: 'evt-1' },
                    { body: { event: 'PaymentSuccessful', id: 'ORD-101' }, messageId: 'evt-2' }
                ]
            },
            { name: 'user-updates', subscriptions: ['cache-invalidator'] }
        ]
    },
    {
        name: 'bussin-demo-dev',
        resourceGroup: 'bussin-demo-rg',
        subscriptionId: '00000000-0000-0000-0000-000000000000',
        location: 'West Europe',
        queues: [
            {
                name: 'test-queue-1',
                messages: [
                    { body: 'Test message 1', messageId: 'test-1' },
                    { body: 'Test message 2', messageId: 'test-2' },
                    { body: 'Test message 3', messageId: 'test-3' }
                ]
            },
            { name: 'test-queue-2', messages: [] }
        ],
        topics: [
            {
                name: 'dev-events',
                subscriptions: ['sub-1'],
                subscriptionMessages: {
                    'sub-1': [{ body: 'Secret event for sub-1', messageId: 'sub-evt-1' }]
                }
            }
        ]
    }
];

/** The subset of the client surface the simulator seeding and counting needs. */
export interface SimulatorClient {
    enableSimulator(on: boolean): void;
    seedMockData(ns: string, queue: string, msgs: DemoMessage[]): void;
    seedTopic(ns: string, topic: string): void;
    seedSubscription(ns: string, topic: string, subscription: string): void;
    seedTopicData(ns: string, topic: string, msgs: DemoMessage[]): void;
    seedSubscriptionData(ns: string, topic: string, subscription: string, msgs: DemoMessage[]): void;
    getQueueMessageCount(ns: string, queue: string, fromDeadLetter?: boolean): number;
    getSubscriptionMessageCount(ns: string, topic: string, subscription: string, fromDeadLetter?: boolean): number;
}

/** Build the whole demo topology in the in-process broker. */
export function seedDemoData(client: SimulatorClient): void {
    client.enableSimulator(true);

    for (const ns of DEMO_NAMESPACES) {
        for (const queue of ns.queues) {
            if (queue.messages.length > 0) client.seedMockData(ns.name, queue.name, queue.messages);
        }
        for (const topic of ns.topics) {
            client.seedTopic(ns.name, topic.name);
            for (const sub of topic.subscriptions) {
                client.seedSubscription(ns.name, topic.name, sub);
            }
            if (topic.messages?.length) {
                client.seedTopicData(ns.name, topic.name, topic.messages);
            }
            for (const [sub, msgs] of Object.entries(topic.subscriptionMessages ?? {})) {
                client.seedSubscriptionData(ns.name, topic.name, sub, msgs);
            }
        }
    }
}

function findNamespace(name: string): DemoNamespace {
    const short = name.replace(/\.servicebus\.windows\.net$/i, '').toLowerCase();
    const match = DEMO_NAMESPACES.find(n => n.name.toLowerCase() === short);
    if (!match) {
        throw new Error(
            `Namespace "${name}" does not exist in demo mode. Available: ` +
            DEMO_NAMESPACES.map(n => n.name).join(', ')
        );
    }
    return match;
}

function counts(active: number, deadLetter: number) {
    return {
        activeMessageCount: active,
        deadLetterMessageCount: deadLetter,
        scheduledMessageCount: 0,
        transferMessageCount: 0,
        transferDeadLetterMessageCount: 0
    };
}

export function demoNamespaceList() {
    return DEMO_NAMESPACES.map(n => ({
        name: n.name,
        resourceGroup: n.resourceGroup,
        subscriptionId: n.subscriptionId,
        location: n.location,
        sku: 'Standard',
        id: `/subscriptions/${n.subscriptionId}/resourceGroups/${n.resourceGroup}` +
            `/providers/Microsoft.ServiceBus/namespaces/${n.name}`
    }));
}

export function demoQueueList(client: SimulatorClient, namespace: string) {
    const ns = findNamespace(namespace);
    return ns.queues.map(q => {
        const active = client.getQueueMessageCount(ns.name, q.name);
        const dead = client.getQueueMessageCount(ns.name, q.name, true);
        return { name: q.name, status: 'Active', totalMessageCount: active + dead, counts: counts(active, dead) };
    });
}

export function demoTopicList(namespace: string) {
    const ns = findNamespace(namespace);
    return ns.topics.map(t => ({
        name: t.name,
        status: 'Active',
        subscriptionCount: t.subscriptions.length,
        totalMessageCount: 0,
        counts: counts(0, 0)
    }));
}

export function demoSubscriptionList(client: SimulatorClient, namespace: string, topicName: string) {
    const ns = findNamespace(namespace);
    const topic = ns.topics.find(t => t.name.toLowerCase() === topicName.toLowerCase());
    if (!topic) {
        throw new Error(
            `Topic "${topicName}" does not exist on ${ns.name} in demo mode. Available: ` +
            (ns.topics.map(t => t.name).join(', ') || '(none)')
        );
    }
    return topic.subscriptions.map(sub => {
        const active = client.getSubscriptionMessageCount(ns.name, topic.name, sub);
        const dead = client.getSubscriptionMessageCount(ns.name, topic.name, sub, true);
        return { name: sub, status: 'Active', totalMessageCount: active + dead, counts: counts(active, dead) };
    });
}
