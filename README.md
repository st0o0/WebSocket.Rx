# WebSocket.Rx

<div align="center">

<img alt="WebSocket.Rx logo" height="128" src="https://raw.githubusercontent.com/st0o0/WebSocket.Rx/refs/heads/main/docs/logo/logo.png" width="128"/>

**A powerful .NET library for reactive WebSocket communication using R3 (Reactive Extensions)**

[![NuGet](https://img.shields.io/nuget/v/WebSocket.Rx.svg?style=flat-square)](https://www.nuget.org/packages/WebSocket.Rx/)
[![License](https://img.shields.io/github/license/st0o0/WebSocket.Rx?style=flat-square)](LICENSE)
[![Downloads](https://img.shields.io/nuget/dt/WebSocket.Rx.svg?style=flat-square)](https://www.nuget.org/packages/WebSocket.Rx/)
[![Dotnet](https://img.shields.io/badge/dotnet-10.0-5027d5?style=flat-square)](https://dotnet.microsoft.com)
</div>

---

## 📋 Table of Contents

- [Features](#-features)
- [Installation](#-installation)
- [Quick Start](#-quick-start)
- [Core Concepts](#-core-concepts)
- [Advanced Usage](#-advanced-usage)
- [API Reference](#-api-reference)
- [Inspiration](#-inspiration)
- [Contributing](#-contributing)
- [License](#-license)

## ✨ Features

### Client Features
- 🔄 **Automatic Reconnection** - Built-in reconnection logic with configurable strategies
- 📡 **Reactive Streams** - Observable sequences for messages, connections, and disconnections
- 🧵 **Thread-Safe** - Safe concurrent message sending and receiving
- 📦 **Message Queuing** - Automatic buffering with channel-based send queue
- ⚡ **High Performance** - Built on `System.Threading.Channels` and `ArrayPool<byte>`
- 🎯 **Type-Safe** - Strong typing with text/binary message support
- 🔒 **Proper Resource Management** - Full `IAsyncDisposable` support with graceful shutdown

### Server Features
- 👥 **Multi-Client Support** - Handle multiple WebSocket connections simultaneously
- 📊 **Client Tracking** - Built-in client metadata and connection management
- 🔔 **Event Streams** - Observables for client connect/disconnect events
- 🎯 **Selective Messaging** - Send to specific clients or broadcast to all
- 🛡️ **Robust Cleanup** - Automatic client cleanup on disconnect

## 📦 Installation

```bash
dotnet add package WebSocket.Rx
```

**Requirements:** .NET 10.0 or higher

## 🚀 Quick Start

### Client Example

```csharp
using WebSocket.Rx;
using R3;

// Create and configure client
await using var client = new ReactiveWebSocketClient(new Uri("wss://echo.websocket.org"))
{
    IsReconnectionEnabled = true,
    KeepAliveInterval = TimeSpan.FromSeconds(30),
    IsTextMessageConversionEnabled = true
};

// Subscribe to messages
client.MessageReceived
    .Subscribe(msg => Console.WriteLine($"Received: {msg.Text}"));

// Subscribe to connection events
client.ConnectionHappened
    .Subscribe(info => Console.WriteLine($"Connected: {info.Reason}"));

client.DisconnectionHappened
    .Subscribe(info => Console.WriteLine($"Disconnected: {info.Reason}"));

// Connect and send messages
await client.StartAsync();
await client.SendAsTextAsync("Hello WebSocket!");
```

### Server Example

```csharp
using WebSocket.Rx;
using R3;

// Create and start server
await using var server = new ReactiveWebSocketServer("http://localhost:8080/")
{
    IsTextMessageConversionEnabled = true
};

// Subscribe to client events
server.ClientConnected
    .Subscribe(client => Console.WriteLine($"Client connected: {client.Metadata.Id}"));

server.Messages
    .Subscribe(msg => 
    {
        Console.WriteLine($"From {msg.Metadata.Id}: {msg.Message.Text}");
        // Echo back to sender
        server.SendAsTextAsync(msg.Metadata.Id, $"Echo: {msg.Message.Text}");
    });

await server.StartAsync();
Console.WriteLine($"Server running with {server.ClientCount} clients");
```

## 🎓 Core Concepts

### Observable Streams

WebSocket.Rx is built around reactive streams using [R3](https://github.com/Cysharp/R3):

```csharp
// Filter and transform messages
client.MessageReceived
    .Where(msg => msg.MessageType == MessageType.Text)
    .Select(msg => msg.Text.ToUpper())
    .Subscribe(text => Console.WriteLine(text));

// Debounce reconnection events
client.ConnectionHappened
    .Throttle(TimeSpan.FromSeconds(1))
    .Subscribe(info => Console.WriteLine("Stable connection established"));
```

### Message Types

```csharp
// Send text message (queued)
await client.SendAsTextAsync("Hello");

// Send binary message (queued)
await client.SendAsBinaryAsync(new byte[] { 0x01, 0x02 });

// Send instant (bypasses queue)
await client.SendInstantAsync("Urgent message");

// Try send (non-blocking)
bool sent = client.TrySendAsText("Optional message");
```

### Connection Lifecycle

```csharp
// Start connection
await client.StartAsync();

// Reconnect manually
await client.ReconnectAsync();

// Stop gracefully
await client.StopAsync(WebSocketCloseStatus.NormalClosure, "Goodbye");

// Dispose (automatic cleanup)
await client.DisposeAsync();
```

## 🔧 Advanced Usage

### Custom Configuration

```csharp
var client = new ReactiveWebSocketClient(new Uri("wss://example.com"))
{
    // Connection settings
    ConnectTimeout = TimeSpan.FromSeconds(10),
    KeepAliveInterval = TimeSpan.FromSeconds(30),
    KeepAliveTimeout = TimeSpan.FromSeconds(10),
    
    // Reconnection
    IsReconnectionEnabled = true,
    
    // Message handling
    IsTextMessageConversionEnabled = true,
    MessageEncoding = Encoding.UTF8
};
```

### Server Broadcasting

```csharp
// Broadcast to all clients
foreach (var clientId in server.ConnectedClients.Keys)
{
    await server.SendAsTextAsync(clientId, "Broadcast message");
}

// Send to specific clients
var targetClients = server.ConnectedClients
    .Where(c => c.Value.CustomData?.Contains("premium") == true)
    .Select(c => c.Key);

foreach (var clientId in targetClients)
{
    await server.SendAsTextAsync(clientId, "Premium feature alert!");
}
```

### Error Handling

```csharp
client.DisconnectionHappened
    .Subscribe(info => 
    {
        Console.WriteLine($"Disconnect reason: {info.Reason}");
        if (info.Exception != null)
        {
            Console.WriteLine($"Error: {info.Exception.Message}");
        }
    });
```

### Combining Observables

```csharp
// Wait for connection before sending
client.ConnectionHappened
    .Take(1)
    .Subscribe(_ => client.SendAsTextAsync("Connected!"));

// Process messages in batches
client.MessageReceived
    .Buffer(TimeSpan.FromSeconds(1))
    .Where(batch => batch.Count > 0)
    .Subscribe(batch => Console.WriteLine($"Processed {batch.Count} messages"));
```

## 📚 API Reference

### ReactiveWebSocketClient

| Property | Type | Description |
|----------|------|-------------|
| `Url` | `Uri` | WebSocket server URL |
| `IsStarted` | `bool` | Client started state |
| `IsRunning` | `bool` | Client running state |
| `IsReconnectionEnabled` | `bool` | Enable auto-reconnect |
| `MessageReceived` | `Observable<ReceivedMessage>` | Message stream |
| `ConnectionHappened` | `Observable<Connected>` | Connection stream |
| `DisconnectionHappened` | `Observable<Disconnected>` | Disconnection stream |

**Key Methods:**
- `Task StartAsync()` - Start the client
- `Task StopAsync(status, description)` - Stop gracefully
- `Task ReconnectAsync()` - Manual reconnect
- `Task SendAsTextAsync(message)` - Send text (queued)
- `Task SendAsBinaryAsync(data)` - Send binary (queued)
- `ValueTask DisposeAsync()` - Clean up resources

### ReactiveWebSocketServer

| Property | Type | Description |
|----------|------|-------------|
| `IsRunning` | `bool` | Server running state |
| `ClientCount` | `int` | Number of connected clients |
| `ConnectedClients` | `IReadOnlyDictionary<Guid, Metadata>` | Client metadata |
| `ClientConnected` | `Observable<ClientConnected>` | Client connect stream |
| `ClientDisconnected` | `Observable<ClientDisconnected>` | Client disconnect stream |
| `Messages` | `Observable<ServerReceivedMessage>` | Server message stream |

**Key Methods:**
- `Task StartAsync()` - Start the server
- `Task<bool> StopAsync(status, description)` - Stop server
- `Task<bool> SendAsTextAsync(clientId, message)` - Send to client
- `ValueTask DisposeAsync()` - Clean up resources

## 💡 Inspiration

This library is inspired by and builds upon the excellent work of:

### [Websocket.Client](https://github.com/Marfusios/websocket-client) by Marfusios

WebSocket.Rx takes inspiration from Websocket.Client's elegant reactive approach to WebSocket communication. Key influences include:

- **Reactive-First Design** - Using observables for all events and messages
- **Automatic Reconnection** - Built-in reconnection logic for robust connections
- **Clean API** - Intuitive and easy-to-use interface

#### What's Different?

While honoring the spirit of Websocket.Client, WebSocket.Rx offers:

- ✅ **R3 Integration** - Built on the modern [R3](https://github.com/Cysharp/R3) reactive library (successor to Rx.NET)
- ✅ **Server Support** - Full-featured WebSocket server implementation
- ✅ **Modern .NET** - Built for .NET 10+ with latest performance optimizations
- ✅ **IAsyncDisposable** - Proper async resource cleanup
- ✅ **Channel-Based Queuing** - High-performance message queue using `System.Threading.Channels`
- ✅ **Enhanced Memory Management** - Uses `ArrayPool<byte>` and `RecyclableMemoryStream`

Both libraries share the same core philosophy: **WebSocket communication should be simple, reactive, and reliable.**

## 🤝 Contributing

Contributions are welcome! This library grows with the community's needs.

### How to Contribute

1. **Fork** the repository
2. **Create** a feature branch: `git checkout -b feature/amazing-feature`
3. **Write** tests for your changes
4. **Ensure** all tests pass: `dotnet test`
5. **Submit** a Pull Request

### Guidelines

- ✅ Follow existing code style and conventions
- ✅ Include unit tests for new features
- ✅ Update documentation for API changes
- ✅ Keep PRs focused and atomic
- ✅ Write meaningful commit messages

### Development Setup

```bash
git clone https://github.com/st0o0/WebSocket.Rx.git
cd WebSocket.Rx
dotnet restore
dotnet build
dotnet test
```

## 📄 License

This project is licensed under the **MIT License** - see the [LICENSE](LICENSE) file for details.

---

<div align="center">

**Built with ❤️ for the .NET community**

[Report Bug](https://github.com/st0o0/WebSocket.Rx/issues) · [Request Feature](https://github.com/st0o0/WebSocket.Rx/issues) · [Documentation](https://github.com/st0o0/WebSocket.Rx/wiki)

</div>