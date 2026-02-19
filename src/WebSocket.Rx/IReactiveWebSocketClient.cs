using System.Net.WebSockets;
using System.Text;
using R3;

namespace WebSocket.Rx;

public interface IReactiveWebSocketClient : IDisposable, IAsyncDisposable
{
    Uri Url { get; set; }
    Observable<Message> MessageReceived { get; }
    Observable<Connected> ConnectionHappened { get; }
    Observable<Disconnected> DisconnectionHappened { get; }
    Observable<ErrorOccurred> ErrorOccurred { get; }
    TimeSpan ConnectTimeout { get; set; }
    TimeSpan KeepAliveInterval { get; set; }
    TimeSpan KeepAliveTimeout { get; set; }
    bool IsReconnectionEnabled { get; set; }
    bool IsStarted { get; }
    bool IsRunning { get; }
    bool SenderRunning { get; }
    bool IsTextMessageConversionEnabled { get; set; }
    Encoding MessageEncoding { get; set; }
    ClientWebSocket NativeClient { get; }

    Task StartAsync(CancellationToken cancellationToken = default);

    Task StartOrFailAsync(CancellationToken cancellationToken = default);

    Task<bool> StopAsync(WebSocketCloseStatus status, string statusDescription,
        CancellationToken cancellationToken = default);

    Task<bool> StopOrFailAsync(WebSocketCloseStatus status, string statusDescription,
        CancellationToken cancellationToken = default);

    Task ReconnectAsync(CancellationToken cancellationToken = default);

    Task ReconnectOrFailAsync(CancellationToken cancellationToken = default);

    Task<bool> SendInstantAsync(ReadOnlyMemory<char> message, WebSocketMessageType type,
        CancellationToken cancellationToken = default);

    Task<bool> SendInstantAsync(ReadOnlyMemory<byte> message, WebSocketMessageType type,
        CancellationToken cancellationToken = default);

    Task<bool> SendAsync(ReadOnlyMemory<byte> message, WebSocketMessageType type,
        CancellationToken cancellationToken = default);

    Task<bool> SendAsync(ReadOnlyMemory<char> message, WebSocketMessageType type,
        CancellationToken cancellationToken = default);

    bool TrySend(ReadOnlyMemory<byte> message, WebSocketMessageType type);

    bool TrySend(ReadOnlyMemory<char> message, WebSocketMessageType type);

    void StreamFakeMessage(Message message);
}