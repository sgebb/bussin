# bussin-mcp

**Azure Service Bus tools for Claude, ChatGPT, Cursor, and any other MCP-aware client.**

A [Model Context Protocol](https://modelcontextprotocol.io) server that lets an AI assistant
inspect your Azure Service Bus queues, topics, subscriptions and dead-letter queues. Peek
messages, search payloads and replay failures, using the same AMQP client that powers the
[Bussin](https://bussin.dev) web explorer.

Read-only by default. Nothing writes to your broker unless you say so.

```bash
npx -y bussin-mcp --demo
```

## Install

```bash
npm install -g bussin-mcp
```

Or run it straight from npx, which is what the client config below does.

## Quick start

Try it with no Azure account at all. `--demo` runs against an in-process simulator with
seeded queues:

```bash
npx -y bussin-mcp --demo
```

### Claude Code

```bash
claude mcp add bussin -- npx -y bussin-mcp --namespace my-namespace
```

### Claude Desktop / Cursor

Add to your MCP config (`claude_desktop_config.json`, or `.cursor/mcp.json`):

```json
{
  "mcpServers": {
    "bussin": {
      "command": "npx",
      "args": ["-y", "bussin-mcp", "--namespace", "my-namespace"]
    }
  }
}
```

## Authentication

Entra ID is the default and the recommended path. The server uses Azure's
`DefaultAzureCredential`, so it picks up whatever you already have:

```bash
az login
```

That also works with managed identity, or a service principal via `AZURE_TENANT_ID`,
`AZURE_CLIENT_ID` and `AZURE_CLIENT_SECRET`.

You need two role assignments on the namespace (or its resource group):

| Role | Why |
| --- | --- |
| **Azure Service Bus Data Receiver** | peek and search messages |
| **Azure Service Bus Data Sender** | only if you enable `--allow-send` |
| **Reader** | list namespaces, queues and topics with their message counts |

A SAS connection string is supported for namespaces your signed-in identity cannot see:

```bash
npx -y bussin-mcp --connection-string "Endpoint=sb://...;SharedAccessKeyName=...;SharedAccessKey=..."
```

Connection strings authenticate the data plane only. Entity discovery (`list_queues` and
friends) goes through Azure Resource Manager and needs Entra ID.

## Safety model

The caller is a language model, so anything irreversible is opt-in. Tools you have not
enabled are **not registered at all**: they are invisible to the model rather than merely
refused, so there is nothing to talk it into.

| Flag | Unlocks |
| --- | --- |
| *(none)* | `list_namespaces`, `list_queues`, `list_topics`, `list_subscriptions`, `peek_messages`, `search_messages` |
| `--allow-send` | `send_message`, `resubmit_dead_letter` |
| `--allow-destructive` | `delete_messages`, `dead_letter_messages`, `purge_entity` |

Additional guards:

- **Peek is genuinely non-destructive**: no lock, no consumption, no delivery-count bump.
- **`purge_entity` requires confirmation**: the `confirm` argument must exactly equal the
  entity name being purged.
- **`resubmit_dead_letter` is additive by default**: it copies messages back to the source
  queue and leaves the dead-lettered originals in place. Removing them additionally requires
  `--allow-destructive`.
- **Scans are bounded**: searches and purges stop at `--search-timeout-ms` (default 30s)
  rather than hanging the call.
- **Message bodies are truncated** at 4000 characters per message, so a fat payload cannot
  blow out the model's context.

## Options

| Option | Environment variable | Description |
| --- | --- | --- |
| `--namespace <name>` | `BUSSIN_NAMESPACE` | Default namespace for tool calls |
| `--demo` | `BUSSIN_DEMO` | Run against the built-in simulator; no Azure, no sign-in |
| `--allow-send` | `BUSSIN_ALLOW_SEND` | Enable send and resubmit |
| `--allow-destructive` | `BUSSIN_ALLOW_DESTRUCTIVE` | Enable delete, dead-letter and purge |
| `--connection-string <cs>` | `BUSSIN_CONNECTION_STRING` | SAS connection string instead of Entra ID |
| `--tenant <id>` | `AZURE_TENANT_ID` | Entra tenant to authenticate against |
| `--search-timeout-ms <n>` | `BUSSIN_SEARCH_TIMEOUT_MS` | Bound on a single scan or purge (default 30000) |

## Tools

### Always available

- **`list_namespaces`**: every Service Bus namespace the identity can see, across subscriptions
- **`list_queues`**: queues with active, dead-letter and scheduled counts
- **`list_topics`**: topics with message counts
- **`list_subscriptions`**: subscriptions under a topic, with counts
- **`peek_messages`**: read messages from a queue, subscription, or dead-letter queue
- **`search_messages`**: scan for a body substring, message id, or subject

### With `--allow-send`

- **`send_message`**: send to a queue or topic
- **`resubmit_dead_letter`**: replay dead-lettered messages back to their source queue

### With `--allow-destructive`

- **`delete_messages`**: permanently delete specific messages by sequence number
- **`dead_letter_messages`**: move active messages to the dead-letter queue
- **`purge_entity`**: delete every message from a queue, subscription, or DLQ

## Example prompts

> Which queues in my-namespace have dead-lettered messages?

> Show me the last 5 dead-lettered messages on the orders queue and tell me what they have in common.

> Search the payments subscription for anything mentioning "timeout" in the last batch.

> These three DLQ messages failed on a bug we've now fixed, so resubmit them.

## Related

- [Bussin](https://bussin.dev): the browser-native Azure Service Bus explorer this is built from
- [app.bussin.dev](https://app.bussin.dev): the web app, no install required
- [github.com/sgebb/bussin](https://github.com/sgebb/bussin): source

## Development

```bash
npm install
npm run build     # builds the Node bundle of bussin-client-js, then bundles with esbuild
npm test          # drives the server over stdio against the simulator
npm run typecheck # tsc --noEmit
```

`bussin-client-js` lives in this repo and is inlined at build time, so the published
package is self-contained and has no `file:` dependency.

## Releasing

Publishing is driven by a tag, so it is always deliberate:

1. Bump `version` in `package.json`
2. Add the release to [the changelog](https://bussin.dev/changelog/)
3. Tag and push:

```bash
git tag mcp-v0.1.0 && git push origin mcp-v0.1.0
```

The workflow refuses to publish if the tag and `package.json` versions disagree, or if
that version is already on npm. It publishes with `--provenance`, which links the tarball
to the workflow run and commit that produced it.

**One-time setup:** create an npm automation token with publish rights, add it as the
`NPM_TOKEN` secret under a repository environment named `npm-publish`.

To validate without publishing, run the workflow manually with `dry_run` enabled.

## License

Business Source License 1.1 (BUSL-1.1) — see [LICENSE](LICENSE).

Free for personal, educational, and internal business use. Commercial
redistribution, rebranding, or hosting this software as a service by third
parties is prohibited. Converts to Apache 2.0 on 2029-01-01.
