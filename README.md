# Service Bus Explorer

A zero-backend, browser-based tool for exploring and managing Azure Service Bus namespaces, queues, topics, and subscriptions.

[![Deploy to GitHub Pages](https://github.com/sgebb/slimsbe/actions/workflows/deploy.yml/badge.svg)](https://github.com/sgebb/slimsbe/actions/workflows/deploy.yml)

## 🚀 Live Demo

**[https://sgebb.github.io/slimsbe/](https://sgebb.github.io/slimsbe/)**

## ✨ Features

- 🔐 **Azure AD Authentication** - Secure login with your Microsoft account
- 📋 **Browse Service Bus Resources** - View namespaces, queues, topics, and subscriptions
- 👀 **Peek Messages** - Non-destructive message viewing
- 📤 **Send Messages** - Send messages to queues and topics
- 🗑️ **Delete Messages** - Remove specific messages by sequence number
- 🔄 **Resend Messages** - Duplicate and resend messages
- 💀 **Dead Letter Queue Support** - View and resubmit DLQ messages
- 🧹 **Purge Operations** - Clear all messages from queues/subscriptions
- 💾 **Client-Side Only** - No backend, all operations run in your browser
- 🔒 **Secure** - Your credentials never leave your browser

## 🏗️ Architecture

This is a **100% client-side application** built with:

- **Blazor WebAssembly** - .NET running in the browser via WebAssembly
- **MSAL.js** - Microsoft Authentication Library for Azure AD
- **Azure Management API** - For discovering Service Bus resources
- **AMQP over WebSockets** - Direct Service Bus message operations via [rhea](https://github.com/amqp/rhea)

### Why Client-Side Only?

- ✅ **No server costs** - Hosted as static files
- ✅ **No backend to secure** - Your tokens stay in your browser
- ✅ **Transparent & auditable** - All code is open source
- ✅ **Works offline** - Can be installed as PWA
- ✅ **Fast** - No server round-trips

## 🔒 Security & Privacy

- **Your credentials never leave your browser** - Authentication tokens are stored in browser memory only
- **No telemetry** - We don't track anything
- **Open source** - Audit the code yourself
- **Build verification** - Each deployment includes a commit hash proving it matches the source code

## 🛠️ Build Verification

Every deployment to GitHub Pages includes build metadata:

1. Open the deployed app
2. Open browser DevTools → Console
3. Look for: `Build Info: Commit <hash> built at <timestamp>`
4. Verify the commit hash matches the latest commit on GitHub

You can also check `buildinfo.json` at the root of the deployed site.

## 🚀 Getting Started

### Prerequisites

- .NET 9.0 SDK or later
- Azure subscription with Service Bus access
- Modern browser with WebAssembly support

### Running Locally

```bash
# Clone the repository
git clone https://github.com/sgebb/slimsbe.git
cd slimsbe/ServiceBusExplorer.Blazor

# Run the app
dotnet run

# Open browser to https://localhost:5001
```

### Azure AD App Registration

The app uses a pre-configured Azure AD app registration. If you want to use your own:

1. Create an Azure AD app registration
2. Add redirect URIs:
   - `https://localhost:5001/authentication/login-callback`
   - `https://sgebb.github.io/slimsbe/authentication/login-callback`
3. Request API permissions:
   - `https://management.azure.com/user_impersonation`
   - `https://servicebus.azure.net/user_impersonation`
4. Update `wwwroot/appsettings.json` with your Client ID

## 📦 Project Structure

```
ServiceBusExplorer.Blazor/
├── Models/                    # Data models
├── Services/                  # Business logic
│   ├── AuthenticationService.cs
│   ├── AzureResourceService.cs
│   ├── ServiceBusJsInteropService.cs
│   └── ServiceBusOperationsService.cs
├── Pages/                     # Razor pages
│   ├── Home.razor
│   ├── Explorer.razor
│   └── Diagnostics.razor
├── wwwroot/                   # Static assets
│   ├── js/servicebus-api.js  # AMQP client
│   └── index.html
└── Program.cs                 # App configuration
```

## 🤝 Contributing

Contributions are welcome! Please:

1. Fork the repository
2. Create a feature branch
3. Make your changes
4. Submit a pull request

## 📝 License

MIT License - See [LICENSE](LICENSE) file for details

## 🙏 Acknowledgments

- Built with [Blazor WebAssembly](https://dotnet.microsoft.com/apps/aspnet/web-apps/blazor)
- AMQP client based on [rhea](https://github.com/amqp/rhea)
- Authentication via [MSAL.js](https://github.com/AzureAD/microsoft-authentication-library-for-js)

## ⚠️ Disclaimer

This is a community tool and is not officially supported by Microsoft. Use at your own risk.

---

**Made with ❤️ for the Azure Service Bus community**
