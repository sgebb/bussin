/**
 * Entity discovery via Azure Resource Manager.
 *
 * Deliberately plain `fetch` rather than @azure/arm-servicebus: we need four read-only
 * endpoints, and the management SDK would dominate the install size of the package.
 */

import type { TokenProvider } from './auth.js';

const ARM = 'https://management.azure.com';
const SB_API = '2021-11-01';

interface ArmList<T> { value: T[]; nextLink?: string }

async function armGet<T>(tokens: TokenProvider, path: string): Promise<T[]> {
    const token = await tokens.getArmToken();
    const out: T[] = [];
    let url = path.startsWith('http') ? path : `${ARM}${path}`;

    while (url) {
        const res = await fetch(url, { headers: { Authorization: `Bearer ${token}` } });
        if (!res.ok) {
            const body = await res.text().catch(() => '');
            throw new Error(`ARM request failed (${res.status} ${res.statusText}): ${body.slice(0, 400)}`);
        }
        const page = (await res.json()) as ArmList<T>;
        out.push(...(page.value ?? []));
        url = page.nextLink ?? '';
    }
    return out;
}

export interface NamespaceInfo {
    name: string;
    resourceGroup: string;
    subscriptionId: string;
    location: string;
    sku: string;
    id: string;
}

interface RawNamespace { id: string; name: string; location: string; sku?: { name?: string } }
interface RawSubscription { subscriptionId: string; displayName: string; state: string }

export async function listSubscriptions(tokens: TokenProvider): Promise<RawSubscription[]> {
    const subs = await armGet<RawSubscription>(tokens, '/subscriptions?api-version=2020-01-01');
    return subs.filter(s => s.state === 'Enabled');
}

export async function listNamespaces(tokens: TokenProvider, subscriptionId?: string): Promise<NamespaceInfo[]> {
    const subs = subscriptionId
        ? [subscriptionId]
        : (await listSubscriptions(tokens)).map(s => s.subscriptionId);

    const perSub = await Promise.all(subs.map(async sub => {
        try {
            const raw = await armGet<RawNamespace>(
                tokens, `/subscriptions/${sub}/providers/Microsoft.ServiceBus/namespaces?api-version=${SB_API}`
            );
            return raw.map(n => ({
                name: n.name,
                // .../resourceGroups/<rg>/providers/...
                resourceGroup: n.id.split('/resourceGroups/')[1]?.split('/')[0] ?? '',
                subscriptionId: sub,
                location: n.location,
                sku: n.sku?.name ?? 'unknown',
                id: n.id
            }));
        } catch {
            // A subscription the identity cannot read Service Bus in is not an error --
            // it just contributes nothing to the list.
            return [];
        }
    }));

    return perSub.flat();
}

export interface CountDetails {
    activeMessageCount: number;
    deadLetterMessageCount: number;
    scheduledMessageCount: number;
    transferMessageCount: number;
    transferDeadLetterMessageCount: number;
}

export interface EntityInfo {
    name: string;
    status: string;
    totalMessageCount: number;
    counts: CountDetails;
}

interface RawEntity {
    name: string;
    properties?: {
        status?: string;
        messageCount?: number;
        countDetails?: Partial<Record<string, number>>;
    };
}

function toEntity(e: RawEntity): EntityInfo {
    const c = e.properties?.countDetails ?? {};
    return {
        name: e.name,
        status: e.properties?.status ?? 'Unknown',
        totalMessageCount: e.properties?.messageCount ?? 0,
        counts: {
            activeMessageCount: c['activeMessageCount'] ?? 0,
            deadLetterMessageCount: c['deadLetterMessageCount'] ?? 0,
            scheduledMessageCount: c['scheduledMessageCount'] ?? 0,
            transferMessageCount: c['transferMessageCount'] ?? 0,
            transferDeadLetterMessageCount: c['transferDeadLetterMessageCount'] ?? 0
        }
    };
}

/** Resolve a namespace name to its ARM resource id, erroring clearly if ambiguous. */
async function resolveNamespaceId(tokens: TokenProvider, namespace: string): Promise<string> {
    const short = namespace.replace(/\.servicebus\.windows\.net$/i, '');
    const all = await listNamespaces(tokens);
    const matches = all.filter(n => n.name.toLowerCase() === short.toLowerCase());

    if (matches.length === 0) {
        const known = all.map(n => n.name).join(', ') || '(none visible to this identity)';
        throw new Error(`Namespace "${short}" not found. Visible namespaces: ${known}`);
    }
    if (matches.length > 1) {
        const where = matches.map(m => `${m.subscriptionId}/${m.resourceGroup}`).join(', ');
        throw new Error(`Namespace "${short}" exists in more than one place: ${where}`);
    }
    return matches[0]!.id;
}

export async function listQueues(tokens: TokenProvider, namespace: string): Promise<EntityInfo[]> {
    const id = await resolveNamespaceId(tokens, namespace);
    const raw = await armGet<RawEntity>(tokens, `${ARM}${id}/queues?api-version=${SB_API}`);
    return raw.map(toEntity);
}

export async function listTopics(tokens: TokenProvider, namespace: string): Promise<EntityInfo[]> {
    const id = await resolveNamespaceId(tokens, namespace);
    const raw = await armGet<RawEntity>(tokens, `${ARM}${id}/topics?api-version=${SB_API}`);
    return raw.map(toEntity);
}

export async function listSubscriptionsForTopic(
    tokens: TokenProvider, namespace: string, topic: string
): Promise<EntityInfo[]> {
    const id = await resolveNamespaceId(tokens, namespace);
    const raw = await armGet<RawEntity>(
        tokens, `${ARM}${id}/topics/${encodeURIComponent(topic)}/subscriptions?api-version=${SB_API}`
    );
    return raw.map(toEntity);
}
