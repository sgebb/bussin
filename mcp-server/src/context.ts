import type { Config } from './config.js';
import type { TokenProvider } from './auth.js';

export interface Ctx {
    config: Config;
    tokens: TokenProvider;
}

/** Resolve the namespace for a call, falling back to the server default. */
export function resolveNamespace(ctx: Ctx, provided?: string): string {
    const ns = provided ?? ctx.config.defaultNamespace;
    if (!ns) {
        throw new Error(
            'No namespace given. Pass `namespace`, or start the server with --namespace <name>.'
        );
    }
    return ns.replace(/\.servicebus\.windows\.net$/i, '');
}

/**
 * Match a name against a user-supplied filter.
 *
 * Plain text is a case-insensitive substring match, which is what people reach for first.
 * A `*` turns it into an anchored glob, so "ulfendringer-*" and "*-prod" both work. Tenants
 * with dozens of namespaces are common, and an unfiltered listing floods the caller's context
 * with entries it has no use for.
 */
export function matchesNameFilter(name: string, filter: string): boolean {
    const needle = filter.trim().toLowerCase();
    if (!needle) return true;

    const haystack = name.toLowerCase();
    if (!needle.includes('*')) return haystack.includes(needle);

    const pattern = needle
        .split('*')
        .map(part => part.replace(/[.*+?^${}()|[\]\\]/g, '\\$&'))
        .join('.*');
    return new RegExp(`^${pattern}$`).test(haystack);
}

/** MCP tool results are content arrays; everything here returns JSON text. */
export function json(data: unknown) {
    return { content: [{ type: 'text' as const, text: JSON.stringify(data, null, 2) }] };
}

export function fail(message: string) {
    return { content: [{ type: 'text' as const, text: message }], isError: true };
}

/** Wrap a handler so thrown errors reach the model as readable text, not a transport fault. */
export function guard<A>(fn: (args: A) => Promise<{ content: Array<{ type: 'text'; text: string }>; isError?: boolean }>) {
    return async (args: A) => {
        try {
            return await fn(args);
        } catch (err) {
            const msg = err instanceof Error ? err.message : String(err);
            return fail(`Operation failed: ${msg}`);
        }
    };
}

/** Reject a long scan rather than letting an MCP call hang indefinitely. */
export function withTimeout<T>(promise: Promise<T>, ms: number, onTimeout: () => void, label: string): Promise<T> {
    return new Promise<T>((resolve, reject) => {
        const timer = setTimeout(() => {
            onTimeout();
            reject(new Error(`${label} exceeded ${ms}ms and was stopped. Narrow the filter or lower maxMessages.`));
        }, ms);
        promise.then(
            v => { clearTimeout(timer); resolve(v); },
            e => { clearTimeout(timer); reject(e); }
        );
    });
}
