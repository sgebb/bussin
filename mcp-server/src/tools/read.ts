/**
 * Read-only tools. Always registered: none of these change broker state.
 */

import { z } from 'zod';
import type { McpServer } from '@modelcontextprotocol/sdk/server/mcp.js';
import { getClient, summarize } from '../client.js';
import { listNamespaces, listQueues, listTopics, listSubscriptionsForTopic } from '../arm.js';
import {
    demoNamespaceList, demoQueueList, demoTopicList, demoSubscriptionList, type SimulatorClient
} from '../demo.js';
import { type Ctx, resolveNamespace, json, guard, withTimeout, matchesNameFilter } from '../context.js';

const nsArg = z.string().optional().describe('Service Bus namespace name. Defaults to the server namespace.');

/** The client doubles as the simulator control surface when --demo is active. */
async function simulator(): Promise<SimulatorClient> {
    return (await getClient()) as unknown as SimulatorClient;
}

export function registerReadTools(server: McpServer, ctx: Ctx): void {
    server.registerTool('list_namespaces', {
        title: 'List Service Bus namespaces',
        description:
            'List the Azure Service Bus namespaces the signed-in identity can see, across all enabled ' +
            'subscriptions. Large tenants can hold dozens, so pass nameFilter whenever you already know ' +
            'roughly what you are looking for. Requires Entra ID sign-in (not available with a ' +
            'connection string).',
        inputSchema: {
            nameFilter: z.string().optional().describe(
                'Case-insensitive substring, or a glob when it contains "*". ' +
                'Examples: "payments" matches any name containing it; "billing-*" matches names ' +
                'starting with it; "*-prod" matches names ending with it.'
            ),
            subscriptionId: z.string().optional().describe('Limit to one subscription.')
        }
    }, guard(async ({ nameFilter, subscriptionId }) => {
        const all = ctx.config.demo
            ? demoNamespaceList()
            : await listNamespaces(ctx.tokens, subscriptionId);

        const matched = nameFilter ? all.filter(n => matchesNameFilter(n.name, nameFilter)) : all;

        if (nameFilter && matched.length === 0) {
            return json({
                nameFilter,
                totalNamespaces: all.length,
                matched: 0,
                namespaces: [],
                hint: `No namespace matched "${nameFilter}". Call again without nameFilter to see what exists.`
            });
        }

        // Tell the caller when an unfiltered call returned a lot, so the next one can be narrower.
        const crowded = !nameFilter && all.length > 20;
        return json({
            ...(nameFilter ? { nameFilter } : {}),
            matched: matched.length,
            ...(crowded ? { hint: 'This tenant has many namespaces. Pass nameFilter to narrow the next call.' } : {}),
            namespaces: matched
        });
    }));

    server.registerTool('list_queues', {
        title: 'List queues',
        description:
            'List queues in a namespace with active, dead-letter and scheduled message counts. ' +
            'Use this before peeking so you know which queues have a backlog.',
        inputSchema: { namespace: nsArg }
    }, guard(async ({ namespace }) => ctx.config.demo
        ? json(demoQueueList(await simulator(), resolveNamespace(ctx, namespace)))
        : json(await listQueues(ctx.tokens, resolveNamespace(ctx, namespace)))));

    server.registerTool('list_topics', {
        title: 'List topics',
        description:
            'List topics in a namespace with their message counts. Messages sit on a topic\'s ' +
            'subscriptions rather than the topic itself, so follow up with list_subscriptions to find ' +
            'where a backlog actually is.',
        inputSchema: { namespace: nsArg }
    }, guard(async ({ namespace }) => ctx.config.demo
        ? json(demoTopicList(resolveNamespace(ctx, namespace)))
        : json(await listTopics(ctx.tokens, resolveNamespace(ctx, namespace)))));

    server.registerTool('list_subscriptions', {
        title: 'List topic subscriptions',
        description:
            'List the subscriptions under a topic, with active and dead-letter counts. Use this to pick ' +
            'the subscription to pass to peek_messages or search_messages.',
        inputSchema: { namespace: nsArg, topic: z.string().describe('Topic name.') }
    }, guard(async ({ namespace, topic }) => ctx.config.demo
        ? json(demoSubscriptionList(await simulator(), resolveNamespace(ctx, namespace), topic))
        : json(await listSubscriptionsForTopic(ctx.tokens, resolveNamespace(ctx, namespace), topic))));

    server.registerTool('peek_messages', {
        title: 'Peek messages',
        description:
            'Peek messages from a queue, or from a topic subscription when topic and subscription are given. ' +
            'Peek is non-destructive: it does not lock, consume, or increment delivery count. ' +
            'Set fromDeadLetter to read the dead-letter queue instead of the main entity. ' +
            'This reads from the head of the entity outwards, so it is the right tool for "what is in ' +
            'this queue"; to find particular messages in a backlog, use search_messages instead of ' +
            'paging through peek. Needs the Azure Service Bus Data Receiver role (or Data Owner).',
        inputSchema: {
            namespace: nsArg,
            queue: z.string().optional().describe('Queue name. Omit when using topic + subscription.'),
            topic: z.string().optional().describe('Topic name, when reading a subscription.'),
            subscription: z.string().optional().describe('Subscription name, requires topic.'),
            count: z.number().int().min(1).max(200).default(10).describe('How many messages to return.'),
            fromSequence: z.number().int().min(0).default(0)
                .describe('Start at this sequence number. 0 starts from the oldest available message.'),
            fromDeadLetter: z.boolean().default(false).describe('Read the dead-letter queue.')
        }
    }, guard(async ({ namespace, queue, topic, subscription, count, fromSequence, fromDeadLetter }) => {
        const ns = resolveNamespace(ctx, namespace);
        const token = await ctx.tokens.getDataPlaneToken();
        const client = await getClient();

        let messages;
        if (queue) {
            messages = await client.peekQueueMessages(ns, queue, token, count, fromSequence, fromDeadLetter);
        } else if (topic && subscription) {
            messages = await client.peekSubscriptionMessages(
                ns, topic, subscription, token, count, fromSequence, fromDeadLetter
            );
        } else {
            throw new Error('Provide either queue, or both topic and subscription.');
        }

        return json({
            namespace: ns,
            entity: queue ?? `${topic}/subscriptions/${subscription}`,
            deadLetter: fromDeadLetter,
            returned: messages.length,
            messages: messages.map(m => summarize(m))
        });
    }));

    server.registerTool('search_messages', {
        title: 'Search messages',
        description:
            'Scan a queue or subscription for messages matching a body substring, message id, or subject. ' +
            'Non-destructive. Scanning is bounded by maxMessages and by a server-side timeout, so on a deep ' +
            'backlog prefer a narrow filter. Returns matching messages with their sequence numbers. ' +
            'Prefer this over repeated peek_messages calls whenever you know something about the messages ' +
            'you want. The sequence numbers it returns are what delete_messages, resubmit_dead_letter and ' +
            'dead_letter_messages take as input. Needs the Data Receiver role (or Data Owner).',
        inputSchema: {
            namespace: nsArg,
            queue: z.string().optional().describe('Queue name. Omit when using topic + subscription.'),
            topic: z.string().optional().describe('Topic name, when searching a subscription.'),
            subscription: z.string().optional().describe('Subscription name, requires topic.'),
            bodyFilter: z.string().default('').describe('Case-insensitive substring to find in the body.'),
            messageIdFilter: z.string().default('').describe('Exact message id to match.'),
            subjectFilter: z.string().default('').describe('Subject (label) to match.'),
            fromDeadLetter: z.boolean().default(false).describe('Search the dead-letter queue.'),
            maxMessages: z.number().int().min(1).max(10000).default(1000).describe('Cap on messages scanned.'),
            maxMatches: z.number().int().min(1).max(100).default(25).describe('Stop after this many matches.')
        }
    }, guard(async (a) => {
        const ns = resolveNamespace(ctx, a.namespace);
        if (!a.bodyFilter && !a.messageIdFilter && !a.subjectFilter) {
            throw new Error('Give at least one of bodyFilter, messageIdFilter or subjectFilter.');
        }
        const token = await ctx.tokens.getDataPlaneToken();
        const client = await getClient();

        let controller;
        if (a.queue) {
            controller = await client.searchQueueMessages(
                ns, a.queue, token, a.fromDeadLetter,
                a.bodyFilter, a.messageIdFilter, a.subjectFilter, a.maxMessages, a.maxMatches);
        } else if (a.topic && a.subscription) {
            controller = await client.searchSubscriptionMessages(
                ns, a.topic, a.subscription, token, a.fromDeadLetter,
                a.bodyFilter, a.messageIdFilter, a.subjectFilter, a.maxMessages, a.maxMatches);
        } else {
            throw new Error('Provide either queue, or both topic and subscription.');
        }

        const result = await withTimeout(
            controller.promise, ctx.config.searchTimeoutMs, () => controller.stop(), 'Search'
        );

        // Search yields sequence numbers only; fetch bodies for the matches.
        let messages: Record<string, unknown>[] = [];
        if (result.matchingSequenceNumbers.length > 0) {
            const seqs = result.matchingSequenceNumbers.slice(0, a.maxMatches);
            const fetched = a.queue
                ? await client.peekQueueMessagesBySequence(ns, a.queue, token, seqs, a.fromDeadLetter)
                : await client.peekSubscriptionMessagesBySequence(
                    ns, a.topic!, a.subscription!, token, seqs, a.fromDeadLetter);
            messages = fetched.map(m => summarize(m));
        }

        return json({
            namespace: ns,
            entity: a.queue ?? `${a.topic}/subscriptions/${a.subscription}`,
            deadLetter: a.fromDeadLetter,
            scanned: result.scannedCount,
            matched: result.matchCount,
            messages
        });
    }));
}
