/**
 * State-changing tools, registered only when the operator opts in.
 *
 * Two tiers, because the risks differ in kind:
 *   --allow-send        puts new messages on the broker (recoverable, but visible to consumers)
 *   --allow-destructive removes or dead-letters existing messages (not recoverable)
 *
 * Tools that are not registered are invisible to the model, which is the point: an
 * unavailable tool cannot be talked into running.
 */

import { z } from 'zod';
import type { McpServer } from '@modelcontextprotocol/sdk/server/mcp.js';
import { getClient, summarize } from '../client.js';
import { type Ctx, resolveNamespace, json, guard, withTimeout } from '../context.js';

const nsArg = z.string().optional().describe('Service Bus namespace name. Defaults to the server namespace.');

export function registerSendTools(server: McpServer, ctx: Ctx): void {
    server.registerTool('send_message', {
        title: 'Send a message',
        description:
            'Send a message to a queue or topic. This is a real send: consumers will pick it up. ' +
            'Requires the server to be started with --allow-send.',
        inputSchema: {
            namespace: nsArg,
            queue: z.string().optional().describe('Target queue. Omit when sending to a topic.'),
            topic: z.string().optional().describe('Target topic. Omit when sending to a queue.'),
            body: z.string().describe('Message body. JSON should be passed as a JSON string.'),
            contentType: z.string().optional().describe('Content type, e.g. application/json.'),
            messageId: z.string().optional().describe('Message id. Azure generates one if omitted.'),
            correlationId: z.string().optional().describe('Correlation id.'),
            subject: z.string().optional().describe('Subject (label).'),
            applicationProperties: z.record(z.string(), z.unknown()).optional()
                .describe('Custom application properties.')
        }
    }, guard(async (a) => {
        const ns = resolveNamespace(ctx, a.namespace);
        if (!a.queue && !a.topic) throw new Error('Provide either queue or topic.');
        if (a.queue && a.topic) throw new Error('Provide queue or topic, not both.');

        const token = await ctx.tokens.getDataPlaneToken();
        const client = await getClient();

        const properties: Record<string, unknown> = { ...(a.applicationProperties ?? {}) };
        if (a.messageId) properties['message_id'] = a.messageId;
        if (a.correlationId) properties['correlation_id'] = a.correlationId;
        if (a.subject) properties['subject'] = a.subject;
        if (a.contentType) properties['content_type'] = a.contentType;

        if (a.queue) {
            await client.sendQueueMessage(ns, a.queue, token, a.body, properties);
        } else {
            await client.sendTopicMessage(ns, a.topic!, token, a.body, properties);
        }

        return json({ sent: true, namespace: ns, entity: a.queue ?? a.topic, messageId: a.messageId ?? null });
    }));

    server.registerTool('resubmit_dead_letter', {
        title: 'Resubmit dead-lettered messages',
        description:
            'Copy messages out of a dead-letter queue and send them back to the originating queue or topic. ' +
            'By default this leaves the dead-lettered originals in place, so it is additive and safe to retry; ' +
            'set removeOriginals to also delete them, which additionally requires --allow-destructive. ' +
            'Requires --allow-send.',
        inputSchema: {
            namespace: nsArg,
            queue: z.string().describe('Queue whose dead-letter queue should be drained back.'),
            sequenceNumbers: z.array(z.number().int()).min(1).max(100)
                .describe('Sequence numbers of the dead-lettered messages, from peek_messages or search_messages.'),
            removeOriginals: z.boolean().default(false)
                .describe('Delete the dead-lettered originals after a successful resend. Needs --allow-destructive.')
        }
    }, guard(async (a) => {
        const ns = resolveNamespace(ctx, a.namespace);
        if (a.removeOriginals && !ctx.config.allowDestructive) {
            throw new Error(
                'removeOriginals needs --allow-destructive. Resubmit without it, then delete the originals ' +
                'deliberately once you have confirmed the replay worked.'
            );
        }

        const token = await ctx.tokens.getDataPlaneToken();
        const client = await getClient();

        const originals = await client.peekQueueMessagesBySequence(ns, a.queue, token, a.sequenceNumbers, true);
        if (originals.length === 0) {
            throw new Error('No dead-lettered messages found for those sequence numbers.');
        }

        const resent: Array<Record<string, unknown>> = [];
        for (const m of originals) {
            const properties: Record<string, unknown> = { ...m.applicationProperties };
            if (m.messageId) properties['message_id'] = m.messageId;
            if (m.correlationId) properties['correlation_id'] = m.correlationId;
            if (m.subject) properties['subject'] = m.subject;
            if (m.contentType) properties['content_type'] = m.contentType;
            await client.sendQueueMessage(ns, a.queue, token, m.body, properties);
            resent.push({ sequenceNumber: m.sequenceNumber, messageId: m.messageId });
        }

        let removed = 0;
        if (a.removeOriginals) {
            const seqs = originals.map(m => m.sequenceNumber).filter((s): s is number => typeof s === 'number');
            await client.deleteQueueMessagesBySequence(ns, a.queue, token, seqs, true);
            removed = seqs.length;
        }

        return json({
            namespace: ns,
            queue: a.queue,
            resentCount: resent.length,
            resent,
            originalsRemoved: removed,
            note: a.removeOriginals
                ? 'Originals were deleted from the dead-letter queue.'
                : 'Originals remain in the dead-letter queue; delete them once you have verified the replay.'
        });
    }));
}

export function registerDestructiveTools(server: McpServer, ctx: Ctx): void {
    server.registerTool('delete_messages', {
        title: 'Delete specific messages',
        description:
            'Permanently delete messages by sequence number from a queue or its dead-letter queue. ' +
            'This cannot be undone. Peek or search first to confirm the sequence numbers. ' +
            'Requires --allow-destructive.',
        inputSchema: {
            namespace: nsArg,
            queue: z.string().describe('Queue name.'),
            sequenceNumbers: z.array(z.number().int()).min(1).max(100)
                .describe('Sequence numbers to delete.'),
            fromDeadLetter: z.boolean().default(false).describe('Delete from the dead-letter queue.')
        }
    }, guard(async (a) => {
        const ns = resolveNamespace(ctx, a.namespace);
        const token = await ctx.tokens.getDataPlaneToken();
        const client = await getClient();

        // Report what is actually being destroyed, so the transcript records it.
        const doomed = await client.peekQueueMessagesBySequence(
            ns, a.queue, token, a.sequenceNumbers, a.fromDeadLetter
        );
        await client.deleteQueueMessagesBySequence(ns, a.queue, token, a.sequenceNumbers, a.fromDeadLetter);

        return json({
            namespace: ns,
            queue: a.queue,
            deadLetter: a.fromDeadLetter,
            requested: a.sequenceNumbers.length,
            deleted: doomed.map(m => summarize(m, 200))
        });
    }));

    server.registerTool('dead_letter_messages', {
        title: 'Dead-letter messages',
        description:
            'Move active messages to the dead-letter queue by sequence number, with a reason. ' +
            'Requires --allow-destructive.',
        inputSchema: {
            namespace: nsArg,
            queue: z.string().describe('Queue name.'),
            sequenceNumbers: z.array(z.number().int()).min(1).max(100).describe('Sequence numbers to move.'),
            reason: z.string().default('Manual dead letter').describe('Dead-letter reason.'),
            description: z.string().default('Moved via bussin-mcp').describe('Dead-letter error description.')
        }
    }, guard(async (a) => {
        const ns = resolveNamespace(ctx, a.namespace);
        const token = await ctx.tokens.getDataPlaneToken();
        const client = await getClient();
        await client.deadLetterQueueMessagesBySequence(
            ns, a.queue, token, a.sequenceNumbers, a.reason, a.description
        );
        return json({
            namespace: ns, queue: a.queue, movedToDeadLetter: a.sequenceNumbers.length, reason: a.reason
        });
    }));

    server.registerTool('purge_entity', {
        title: 'Purge a queue or subscription',
        description:
            'Delete EVERY message from a queue, a subscription, or their dead-letter queue. This is ' +
            'irreversible and unbounded. The confirm argument must be the exact entity name, as a deliberate ' +
            'second step. Requires --allow-destructive.',
        inputSchema: {
            namespace: nsArg,
            queue: z.string().optional().describe('Queue to purge. Omit when purging a subscription.'),
            topic: z.string().optional().describe('Topic, when purging a subscription.'),
            subscription: z.string().optional().describe('Subscription to purge, requires topic.'),
            fromDeadLetter: z.boolean().default(false).describe('Purge the dead-letter queue instead.'),
            confirm: z.string().describe('Must exactly equal the queue or subscription name being purged.')
        }
    }, guard(async (a) => {
        const ns = resolveNamespace(ctx, a.namespace);
        const target = a.queue ?? a.subscription;
        if (!target) throw new Error('Provide either queue, or both topic and subscription.');
        if (a.confirm !== target) {
            throw new Error(
                `Refusing to purge: confirm was "${a.confirm}" but the target is "${target}". ` +
                'Pass the exact entity name to confirm.'
            );
        }

        const token = await ctx.tokens.getDataPlaneToken();
        const client = await getClient();

        const controller = a.queue
            ? await client.purgeQueue(ns, a.queue, token, null, a.fromDeadLetter)
            : await client.purgeSubscription(ns, a.topic!, a.subscription!, token, null, a.fromDeadLetter);

        const deleted = await withTimeout(
            controller.promise, ctx.config.searchTimeoutMs, () => controller.stop(), 'Purge'
        );

        return json({
            namespace: ns,
            entity: a.queue ?? `${a.topic}/subscriptions/${a.subscription}`,
            deadLetter: a.fromDeadLetter,
            deletedCount: deleted
        });
    }));
}
