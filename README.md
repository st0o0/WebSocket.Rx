# WebSocket.Rx

WebSocket.Rx is a powerful .NET NuGet package for reactive WebSocket communication using Reactive Extensions (Rx.NET).


[![NuGet](https://img.shields.io/nuget/v/WebSocket.Rx.svg)](https://www.nuget.org/packages/WebSocket.Rx/)
[![Build Status](https://img.shields.io/github/actions/workflow/status/st0o0/WebSocket.Rx/build-and-release.yml?branch=main)](https://github.com/st0o0/WebSocket.Rx/actions)
[![License](https://img.shields.io/github/license/st0o0/WebSocket.Rx)](LICENSE)
[![Downloads](https://img.shields.io/nuget/dt/WebSocket.Rx.svg)](https://www.nuget.org/packages/WebSocket.Rx/)

<img alt="WebSocket.Rx logo" height="128" src="https://raw.githubusercontent.com/st0o0/WebSocket.Rx/refs/heads/main/docs/logo/logo.png" width="128"/>


## Installation

```bash
dotnet add package WebSocket.Rx
```

Requires: `System.Reactive` and a WebSocket implementation like `Websocket.Client`.

## Features

- **Reactive Streams**: Observable sequences for `MessageReceived`, `ReconnectionHappened`, `StateChanged`
- **Automatic Reconnecting**: Configurable with backoff strategy
- **Thread-Safe Processing**: `ObserveOn` for UI or scheduler integration
- **Send Queue**: Asynchronous messaging with automatic buffering

## Usage

```csharp
using WebSocket.Rx;
using System.Reactive.Linq;

var client = new WebSocketRxClient("wss://echo.websocket.org")
{
    ReconnectTimeout = TimeSpan.FromSeconds(5)
};

client.MessageReceived
    .Where(msg => msg.Text.Contains("ping"))
    .Subscribe(msg => client.Send("pong"));

client.StateChanged.Where(state => state == WebSocketState.Open)
    .Subscribe(_ => client.Send("Connected!"));

await client.ConnectAsync();
```

## Configuration

```csharp
var options = new WebSocketRxOptions
{
    LoggerFactory = LoggerFactory.Create(builder => builder.AddConsole()),
    AutomaticReconnect = true,
    ReconnectTimeout = TimeSpan.FromSeconds(3)
};
```

## Contributing

Contributions are welcome! This library grows with the community's needs.

### How to Contribute

1. Fork the repository
2. Create a feature branch: `git checkout -b feature/amazing-feature`
3. Write tests for your changes
4. Ensure all tests pass: `dotnet test`
5. Submit a Pull Request

### Contribution Guidelines

* Follow existing code style and conventions
* Include unit tests for new features
* Update documentation for API changes
* Keep changes focused and atomic

## License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

---

**Built with ❤️ for the .NET**

For questions, support, or feature requests, please [open an issue](https://github.com/st0o0/WebSocket.Rx/issues).