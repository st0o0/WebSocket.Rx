using System.Net.WebSockets;
using System.Text;
using R3;

namespace WebSocket.Rx;

public interface IReactiveWebSocketClient : IDisposable, IAsyncDisposable
{
    Uri Url { get; set; }
    Observable<ReceivedMessage> MessageReceived { get; }
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

    Task<bool> StopAsync(WebSocketCloseStatus status, string statusDescription, CancellationToken cancellationToken = default);

    Task<bool> StopOrFailAsync(WebSocketCloseStatus status, string statusDescription, CancellationToken cancellationToken = default);

    Task ReconnectAsync(CancellationToken cancellationToken = default);

    Task ReconnectOrFailAsync(CancellationToken cancellationToken = default);

    Task<bool> SendInstantAsync(byte[] message, CancellationToken cancellationToken = default);

    Task<bool> SendInstantAsync(string message, CancellationToken cancellationToken = default);

    Task<bool> SendAsBinaryAsync(byte[] message, CancellationToken cancellationToken = default);

    Task<bool> SendAsBinaryAsync(string message, CancellationToken cancellationToken = default);

    Task<bool> SendAsTextAsync(byte[] message, CancellationToken cancellationToken = default);

    Task<bool> SendAsTextAsync(string message, CancellationToken cancellationToken = default);

    bool TrySendAsBinary(string message);

    bool TrySendAsBinary(byte[] message);

    bool TrySendAsText(byte[] message);

    bool TrySendAsText(string message);

    void StreamFakeMessage(ReceivedMessage message);
}