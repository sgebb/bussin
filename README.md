# 🚌 Bussin: The Modern Azure Service Bus Explorer

**Bussin** is a high-performance, client-side Azure Service Bus explorer. It runs entirely in your browser as a Progressive Web App (PWA), offering a secure and private way to manage your messaging infrastructure without any backend proxy or intermediate servers.

[![Use Bussin](https://img.shields.io/badge/Use%20Bussin-bussin.dev-blueviolet?style=for-the-badge)](https://bussin.dev/)

---

## 🚀 Key Features

### Background Operations & Purge
Execute massive, connection-safe bulk actions in the background without locking your UI.
![Background Purge](src/wwwroot/blog/gif/backgroud-purge.gif)

### Massive Batch Transmissions
Stream tens of thousands of payloads instantly utilizing concurrent AMQP pipelining.
![15k Batch Send](src/wwwroot/blog/gif/batch-send-15k.gif)

### Bulletproof Bulk Management
Resubmit or delete precise combinations of active and dead-letter messages with zero data-loss through automated dynamic depth-locking.
![Bulk Move & Resubmit](src/wwwroot/blog/gif/bulk-operations-movetodlq-resubmit.gif)

### Seamless Organization
Instantly navigate across multiple Entra ID environments using intuitive folder nesting and active searching.
![Namespace Organization](src/wwwroot/blog/gif/namespace-filter-and-folder-organize.gif)

### Deep Content Inspection
Search deep into queues visually using advanced property and body pattern matching.
![Peek & Deep Search](src/wwwroot/blog/gif/peak-filter-deepsearch.gif)

---

- **Privacy-First Architecture**: Your data never leaves your browser. Bussin communicates directly with Azure APIs.
- **Zero Installation**: Access it via [bussin.dev](https://bussin.dev/) or install it as a PWA for offline-enabled access.
- **Entra ID (Azure AD) Integration**: Secure authentication using your existing Azure identity and RBAC roles.

## 🛠 Why Bussin?

If you are looking for an **Azure Service Bus Explorer alternative** that is cross-platform and web-native, Bussin is built for you:

- **No Backend**: Unlike other web-based explorers, Bussin has no backend. Your Service Bus connection strings or tokens are never sent to a third-party server.
- **Modern UI**: A clean, responsive interface built with Blazor WebAssembly and Bootstrap 5.
- **Dev-Focused**: Designed for developers who need to quickly debug queues and topics without the bloat of traditional desktop clients.

## 🔒 Permissions & Security

Bussin respects your Azure RBAC configuration. It uses two-step delegated consent:
1. **Azure Management API**: To list namespaces and entities.
2. **Azure Service Bus**: For data-plane operations (Peek, Send, etc.).

*Required Roles:* Azure Service Bus Data Owner, Receiver, or Sender.

## 🏗 Development

Requirements:
- .NET 10 SDK
- Node.js 20+

```bash
# Build client-side AMQP library
cd client-js && npm ci && npm run build

# Build Blazor WASM app
cd ../src && dotnet publish -c Release
```

## 📜 License

Business Source License 1.1 (BSL) - See [LICENSE](LICENSE) file for details.
- **Standard functionality** is always free via [bussin.dev](https://bussin.dev/).
- **Commercial redistribution** or rebranding is strictly prohibited.

---

*Disclaimer: Bussin is a community tool and is not officially supported by Microsoft. Use at your own risk.*
