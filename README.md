# bussin

Bussin is a client-side Azure Service Bus explorer. It runs entirely in your browser (no backend) and can be installed as a PWA.

## Use

https://bussin.dev/

## How it works

- **UI**: Blazor WebAssembly
- **Auth**: MSAL (Entra)
- **Resource discovery**: Azure Management API (ARM) to enumerate subscriptions/namespaces/entities
- **Message/data-plane operations**: direct AMQP-over-WebSockets from the browser (via the bundled `client-js` library)

## Permissions and access

Bussin uses your Entra (Azure AD) identity. What you can do in the app is limited by what you're allowed to do in Azure.

- **Azure AD delegated permission: Azure Management API**
  - **Used for**: listing subscriptions, finding Service Bus namespaces, and reading entity metadata.
- **Azure AD delegated permission: Azure Service Bus**
  - **Used for**: acquiring tokens for Service Bus data-plane operations (send/receive/peek/dead-letter/purge, etc.).

The app uses a two-step consent flow (ARM first, then Service Bus) because these are separate resources.

You also need Azure RBAC roles on the namespaces you want to use:

- **Azure Service Bus Data Receiver**
  - **Used for**: peeking/receiving messages.
- **Azure Service Bus Data Sender**
  - **Used for**: sending messages.
- **Azure Service Bus Data Owner**
  - **Used for**: full message operations (including destructive actions like delete/purge/dead-letter management).

## Build verification

Deployments include a `buildinfo.json` file in the published site and log the commit/build time in the browser console.

## Development (optional)

Requirements:

- .NET 10 SDK
- Node.js 20+

Build steps (same as CI):

```bash
cd client-js
npm ci
npm run build

dotnet publish src/Bussin.csproj -c Release
```

## License

MIT License - See [LICENSE](LICENSE) file for details

## Tech Stack

- [Blazor WebAssembly](https://dotnet.microsoft.com/apps/aspnet/web-apps/blazor) - .NET running in the browser
- [rhea](https://github.com/amqp/rhea) - AMQP 1.0 client for JavaScript
- [MSAL.js](https://github.com/AzureAD/microsoft-authentication-library-for-js) - Microsoft Authentication Library
- [Bootstrap 5](https://getbootstrap.com/) - UI framework

## Disclaimer

This is a community tool and is not officially supported by Microsoft. Use at your own risk.
