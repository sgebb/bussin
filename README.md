# bussin

A zero-backend, installable tool for interacting with Azure Service Bus. Runs entirely in your browser with no server required.

[![Deploy to GitHub Pages](https://github.com/sgebb/slimsbe/actions/workflows/deploy.yml/badge.svg)](https://github.com/sgebb/slimsbe/actions/workflows/deploy.yml)

## 🚀 Live Demo

**[https://sgebb.github.io/slimsbe/](https://sgebb.github.io/slimsbe/)**

Can be installed as a Progressive Web App (PWA) for offline access.

## Features

### Message Operations
- **Peek Messages** - Non-destructive message viewing with batch loading
- **Send Messages** - Send messages to queues and topics with custom properties
- **Send Scheduled Messages** - Schedule messages for future delivery
- **Delete Messages** - Lock and delete specific messages by sequence number
- **Batch Operations** - Select and process multiple messages at once
- **Dead Letter Queue Support** - View, resubmit, and manage DLQ messages
- **Purge Operations** - Clear all messages from queues or subscriptions

### Resource Management
- **Browse Resources** - View all Service Bus namespaces in your Azure subscriptions
- **Entity Explorer** - Navigate queues, topics, and subscriptions
- **Recent Namespaces** - Quick access to frequently used namespaces

### User Experience
- **Dark Mode** - Toggle between light and dark themes
- **Installable PWA** - Install as a desktop or mobile app
- **Client-Side Only** - No backend server, all operations run in your browser
- **Secure** - Your credentials never leave your browser

## 🏗️ Architecture

This is a **100% client-side application** built with:

- **Blazor WebAssembly** - .NET running in the browser via WebAssembly
- **MSAL.js** - Microsoft Authentication Library for Azure AD
- **Azure Management API** - For discovering Service Bus resources
- **AMQP over WebSockets** - Direct Service Bus message operations via [rhea](https://github.com/amqp/rhea)

### Why Client-Side Only?

- **No server costs** - Hosted as static files on GitHub Pages
- **No backend to secure** - Your tokens stay in your browser
- **Transparent & auditable** - All code is open source
- **Works offline** - Can be installed as PWA
- **Fast** - Direct connection to Service Bus, no server round-trips

## 🔒 Security & Privacy

- **Your credentials never leave your browser** - Authentication tokens are stored in browser memory only
- **No telemetry** - We don't track anything
- **Open source** - Audit the code yourself
- **Build verification** - Each deployment includes a commit hash proving it matches the source code

## Build Verification

Every deployment to GitHub Pages includes build metadata:

1. Open the deployed app
2. Open browser DevTools → Console
3. Look for: `Build Info: Commit <hash> built at <timestamp>`
4. Verify the commit hash matches the latest commit on GitHub

You can also check `buildinfo.json` at the root of the deployed site.

## 🛠️ Getting Started

### Prerequisites

- .NET 10.0 SDK or later
- Azure subscription with Service Bus access
- Modern browser with WebAssembly support

### Running Locally

```bash
# Clone the repository
git clone https://github.com/sgebb/slimsbe.git
cd slimsbe/src

# Run the app
dotnet run

# Open browser to https://localhost:63079 (or the port shown in console)
```

### Azure AD App Registration

The app uses a pre-configured Azure AD app registration. If you want to use your own:

1. Create an Azure AD app registration in the Azure Portal
2. Set it as a Single Page Application (SPA)
3. Add redirect URIs:
   - `https://localhost:63079/authentication/login-callback`
   - `https://sgebb.github.io/slimsbe/authentication/login-callback`
4. Request API permissions:
   - `https://management.azure.com/user_impersonation`
   - `https://servicebus.azure.net/user_impersonation`
5. Update `wwwroot/appsettings.json` with your Client ID and Tenant ID

## Project Structure

```
src/
├── Models/                    # Data models
├── Services/                  # Business logic
│   ├── AuthenticationService.cs
│   ├── AzureResourceService.cs
│   ├── ServiceBusJsInteropService.cs
│   └── ServiceBusOperationsService.cs
├── Pages/                     # Razor pages
│   ├── Home.razor            # Namespace browser
│   ├── Explorer.razor        # Message operations
│   └── Diagnostics.razor     # Debug info
├── Components/               # Reusable components
│   └── MessageList.razor     # Message display
├── wwwroot/                  # Static assets
│   ├── js/
│   │   ├── servicebus-api.js # Compiled AMQP client
│   │   └── storage.js        # LocalStorage helpers
│   ├── references/           # TypeScript source
│   │   └── serviceBusApi.ts  # AMQP client source
│   ├── service-worker.js     # PWA service worker
│   ├── manifest.json         # PWA manifest
│   └── index.html
└── Program.cs                # App configuration
```

## 🤝 Contributing

Contributions are welcome! Please:

1. Fork the repository
2. Create a feature branch
3. Make your changes
4. Submit a pull request

## License

MIT License - See [LICENSE](LICENSE) file for details

## Tech Stack

- [Blazor WebAssembly](https://dotnet.microsoft.com/apps/aspnet/web-apps/blazor) - .NET 10 running in the browser
- [rhea](https://github.com/amqp/rhea) - AMQP 1.0 client for JavaScript
- [MSAL.js](https://github.com/AzureAD/microsoft-authentication-library-for-js) - Microsoft Authentication Library
- [Bootstrap 5](https://getbootstrap.com/) - UI framework

## ⚠️ Disclaimer

This is a community tool and is not officially supported by Microsoft. Use at your own risk.