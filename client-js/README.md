# client-js

TypeScript AMQP client library for Azure Service Bus browser access.

## Overview

This is the low-level AMQP implementation that powers the Blazor WebAssembly app. It uses [rhea](https://github.com/amqp/rhea) for AMQP 1.0 protocol support over WebSockets.

## Building

```bash
npm install
npm run build
```

Output: `../src/wwwroot/js/servicebus-api.js` (minified, bundled)

## Architecture

- **serviceBusApi.ts** - High-level API exported to `window.ServiceBusAPI`
- **src/connection.ts** - AMQP connection management
- **src/messageReceiver.ts** - Message receiving (peek/destructive modes)
- **src/messageSender.ts** - Message sending
- **src/managementClient.ts** - Management operations (peek, etc.)
- **src/messageParser.ts** - AMQP message to JSON parsing

## Usage

The built library is loaded in the Blazor app and called via JS interop. See `../src/Services/ServiceBusJsInteropService.cs` for usage examples.

## Development

```bash
npm run dev
```

Opens http://localhost:5174/ with a test page (see index.html).
