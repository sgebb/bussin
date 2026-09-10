/**
 * bussin-mcp: Model Context Protocol server for Azure Service Bus.
 *
 * Exposes the same AMQP client the Bussin web app uses (bussin-client-js) as MCP tools,
 * so an MCP-aware assistant can inspect queues, topics and dead-letter queues.
 *
 * Read-only unless started with --allow-send and/or --allow-destructive.
 */

// ---------------------------------------------------------------------------
// stdout belongs to the JSON-RPC transport. The Service Bus client logs freely
// with console.log; a single stray line corrupts the protocol framing and the
// client drops the connection. Redirect all console output to stderr BEFORE
// anything else is imported or run.
// ---------------------------------------------------------------------------
const stderrWrite = (...args: unknown[]): void => {
    process.stderr.write(
        args.map(a => (typeof a === 'string' ? a : JSON.stringify(a))).join(' ') + '\n'
    );
};
console.log = stderrWrite;
console.info = stderrWrite;
console.warn = stderrWrite;
console.debug = stderrWrite;
console.error = stderrWrite;

import { McpServer } from '@modelcontextprotocol/sdk/server/mcp.js';
import { StdioServerTransport } from '@modelcontextprotocol/sdk/server/stdio.js';
import { loadConfig } from './config.js';
import { TokenProvider } from './auth.js';
import { registerReadTools } from './tools/read.js';
import { registerSendTools, registerDestructiveTools } from './tools/write.js';
import type { Ctx } from './context.js';
import { seedDemoData, DEMO_NAMESPACE, type SimulatorClient } from './demo.js';

const HELP = `
bussin-mcp: Azure Service Bus tools for MCP clients

Usage: bussin-mcp [options]

Options:
  --namespace <name>        Default Service Bus namespace for tool calls
  --demo                    Run against the built-in simulator; no Azure, no sign-in
  --allow-send              Enable send_message and resubmit_dead_letter
  --allow-destructive       Enable delete_messages, dead_letter_messages, purge_entity
  --connection-string <cs>  Use a SAS connection string instead of Entra ID
  --tenant <id>             Entra tenant to authenticate against
  --search-timeout-ms <n>   Bound on a single scan or purge (default 30000)
  --help                    Show this message

Authentication defaults to Entra ID via DefaultAzureCredential: run 'az login',
or set AZURE_TENANT_ID / AZURE_CLIENT_ID / AZURE_CLIENT_SECRET.

Every option has an environment-variable equivalent (BUSSIN_NAMESPACE,
BUSSIN_ALLOW_SEND, BUSSIN_ALLOW_DESTRUCTIVE, BUSSIN_CONNECTION_STRING, ...).

The server is read-only unless you opt in. https://bussin.dev
`;

/** Build the demo topology in the in-process broker. Mirrors app.bussin.dev/demo. */
async function startSimulator(): Promise<void> {
    const client = (await import('bussin-client-js')) as unknown as SimulatorClient;
    seedDemoData(client);
}

async function main(): Promise<void> {
    if (process.argv.includes('--help') || process.argv.includes('-h')) {
        process.stdout.write(HELP);
        return;
    }

    const config = loadConfig();
    const ctx: Ctx = { config, tokens: new TokenProvider(config) };

    if (config.demo) {
        // Same in-process broker the web app uses for app.bussin.dev/demo: no Azure, no sign-in.
        await startSimulator();
        if (!config.defaultNamespace) config.defaultNamespace = DEMO_NAMESPACE;
    }

    const server = new McpServer(
        { name: 'bussin-mcp', version: '0.1.0' },
        {
            instructions:
                'Azure Service Bus tools backed by Bussin (https://bussin.dev). Peek and search are ' +
                'non-destructive and always available. Before acting on specific messages, use list_queues ' +
                'to find the backlog, then peek_messages or search_messages to obtain sequence numbers. ' +
                'Write and destructive tools appear only when the operator has enabled them; if a tool you ' +
                'want is missing, say so rather than trying to work around it.'
        }
    );

    registerReadTools(server, ctx);
    if (config.allowSend) registerSendTools(server, ctx);
    if (config.allowDestructive) registerDestructiveTools(server, ctx);

    // Goes to stderr, so it is visible in client logs without touching the protocol stream.
    const mode = [
        'read',
        config.allowSend ? 'send' : null,
        config.allowDestructive ? 'destructive' : null
    ].filter(Boolean).join('+');
    console.error(
        `bussin-mcp ready | capabilities: ${mode}; auth: ` +
        `${ctx.tokens.usesConnectionString ? 'connection string' : 'Entra ID'}` +
        `${config.defaultNamespace ? `; namespace: ${config.defaultNamespace}` : ''}` +
        `${config.demo ? ' (SIMULATOR)' : ''}`
    );

    await server.connect(new StdioServerTransport());
}

main().catch(err => {
    console.error('bussin-mcp failed to start:', err instanceof Error ? err.message : err);
    process.exit(1);
});
