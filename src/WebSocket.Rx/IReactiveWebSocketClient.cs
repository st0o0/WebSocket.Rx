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

    Task StartAsync();

    Task StartOrFailAsync();

    Task<bool> StopAsync(WebSocketCloseStatus status, string statusDescription);

    Task<bool> StopOrFailAsync(WebSocketCloseStatus status, string statusDescription);

    Task ReconnectAsync(CancellationToken cancellationToken = default);

    Task ReconnectOrFailAsync(CancellationToken cancellationToken = default);

    Task SendInstantAsync(string message, CancellationToken cancellationToken = default);

    Task SendInstantAsync(byte[] message, CancellationToken cancellationToken = default);

    Task SendAsBinaryAsync(byte[] message, CancellationToken cancellationToken = default);

    Task SendAsBinaryAsync(string message, CancellationToken cancellationToken = default);

    Task SendAsTextAsync(byte[] message, CancellationToken cancellationToken = default);

    Task SendAsTextAsync(string message, CancellationToken cancellationToken = default);

    bool TrySendAsBinary(string message);

    bool TrySendAsBinary(byte[] message);

    bool TrySendAsText(byte[] message);

    bool TrySendAsText(string message);

    void StreamFakeMessage(ReceivedMessage message);
}