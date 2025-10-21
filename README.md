# bussin

A zero-backend, installable tool for interacting with Azure Service Bus. Runs entirely in your browser with no server required.

## 🚀 Use the app

**[https://bussin.dev/](https://bussin.dev/)**

Can be installed as a Progressive Web App (PWA) for offline access.

## 📋 Prerequisites

To use this application, you need:

1. **Azure AD Authentication** - You must consent to the app's permissions when first signing in
2. **Azure RBAC Roles** - You need appropriate roles on the Service Bus namespaces you want to manage:
   - **Azure Service Bus Data Receiver** - To read/peek messages
   - **Azure Service Bus Data Sender** - To send messages  
   - **Azure Service Bus Data Owner** - For full access (delete, purge, dead-letter operations)

### Authentication Flow

The app requires consent for two separate Azure AD resources:

1. **Azure Management API** - Granted at initial login to browse your Service Bus resources
2. **Azure Service Bus API** - Requested via popup after login for message operations

This two-step consent is required because Azure AD doesn't allow requesting multiple resources in a single OAuth flow. When you first sign in, you'll grant Management API access. After login, the home page will show a consent warning with a "Grant Permission" button that opens a popup for Service Bus access.

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

## 📋 Roadmap & TODO

### 🚀 Ready to Start

- [ ] **General code improvements** - Refactor to be more modular, separate business logic into services, improve component structure
- [ ] **UI: Better light/dark mode color schemes** - More consistent and polished color palette across themes
- [ ] **UI: Introduce component framework** - Evaluate Blazorize/MudBlazor for better component consistency and easier theme management

### 🐛 Bugs & Issues

- [ ] **Message scheduling verification** - Verify scheduling actually works, add UI to show number of scheduled messages
- [] **Faster purge** - Multiple processes running purge at the same time?

### 🔮 Future / Blocked

- [ ] **Upgrade to .NET 10** - Migrate from current .NET version to .NET 10 (when released/stable)
- [ ] **Verified publisher in Entra ID** - Complete Microsoft verification process for the app registration
- [ ] **Consider ads/donate button** - Evaluate monetization options (depends on hosting model, may require Azure Static Web Apps, consider tax implications for foreign income in Norway)

## 🛠️ Development

### Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- [Node.js 20+](https://nodejs.org/) and npm

### Building

This repository contains two components:

1. **client-js** - TypeScript AMQP client library (builds to `src/wwwroot/js/servicebus-api.js`)
2. **src** - Blazor WebAssembly application

**Build everything:**
```bash
# Windows PowerShell
.\build.ps1

# Linux/macOS
./build.sh
```

**Build specific components:**
```bash
# Windows PowerShell
.\build.ps1 -Target js          # Build only client-js
.\build.ps1 -Target dotnet      # Build only .NET app

# Linux/macOS
./build.sh js                   # Build only client-js
./build.sh dotnet               # Build only .NET app
```

### Running Locally

```bash
# Build client-js first
cd client-js
npm install
npm run build

# Run .NET app
cd ../src
dotnet run
```

The app will be available at `https://localhost:5001`

## 🤝 Contributing

Contributions are welcome! Please:

1. Fork the repository
2. Create a feature branch
3. Make your changes
4. Submit a pull request

## License

MIT License - See [LICENSE](LICENSE) file for details

## Tech Stack

- [Blazor WebAssembly](https://dotnet.microsoft.com/apps/aspnet/web-apps/blazor) - .NET running in the browser
- [rhea](https://github.com/amqp/rhea) - AMQP 1.0 client for JavaScript
- [MSAL.js](https://github.com/AzureAD/microsoft-authentication-library-for-js) - Microsoft Authentication Library
- [Bootstrap 5](https://getbootstrap.com/) - UI framework

## ⚠️ Disclaimer

This is a community tool and is not officially supported by Microsoft. Use at your own risk.
