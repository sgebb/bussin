/**
 * Runtime configuration: capability gating and connection defaults.
 *
 * Bussin's MCP server is read-only by default. Anything that changes broker
 * state has to be switched on deliberately, because the caller is a language
 * model and a mistaken `purge_queue` against production is unrecoverable.
 */

export interface Config {
    /** Allow tools that put messages onto the broker (send, resubmit). */
    allowSend: boolean;
    /** Allow tools that remove or dead-letter messages (purge, delete, dead-letter). */
    allowDestructive: boolean;
    /** Optional namespace used when a tool call omits one. */
    defaultNamespace: string | null;
    /** Entra tenant to authenticate against, if not the credential's default. */
    tenantId: string | null;
    /** Connection string, for namespaces not reachable via the signed-in identity. */
    connectionString: string | null;
    /** Upper bound (ms) on any single long-running scan. */
    searchTimeoutMs: number;
    /** Run against the in-process simulator instead of Azure. */
    demo: boolean;
}

function flag(argv: string[], name: string, envName: string): boolean {
    if (argv.includes(`--${name}`)) return true;
    const env = process.env[envName];
    return env === '1' || env === 'true';
}

function value(argv: string[], name: string, envName: string): string | null {
    const i = argv.indexOf(`--${name}`);
    if (i !== -1 && argv[i + 1] && !argv[i + 1].startsWith('--')) return argv[i + 1];
    const inline = argv.find(a => a.startsWith(`--${name}=`));
    if (inline) return inline.slice(name.length + 3);
    return process.env[envName] ?? null;
}

export function loadConfig(argv: string[] = process.argv.slice(2)): Config {
    const timeout = value(argv, 'search-timeout-ms', 'BUSSIN_SEARCH_TIMEOUT_MS');
    return {
        allowSend: flag(argv, 'allow-send', 'BUSSIN_ALLOW_SEND'),
        allowDestructive: flag(argv, 'allow-destructive', 'BUSSIN_ALLOW_DESTRUCTIVE'),
        defaultNamespace: value(argv, 'namespace', 'BUSSIN_NAMESPACE'),
        tenantId: value(argv, 'tenant', 'AZURE_TENANT_ID'),
        connectionString: value(argv, 'connection-string', 'BUSSIN_CONNECTION_STRING'),
        searchTimeoutMs: timeout ? Number(timeout) : 30_000,
        demo: flag(argv, 'demo', 'BUSSIN_DEMO')
    };
}
