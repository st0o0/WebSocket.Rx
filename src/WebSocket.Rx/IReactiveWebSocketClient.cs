using System.Net.WebSockets;
using System.Text;

namespace WebSocket.Rx;

public interface IReactiveWebSocketClient : IDisposable
{
    Uri Url { get; set; }
    IObservable<ReceivedMessage> MessageReceived { get; }
    IObservable<Connected> ConnectionHappened { get; }
    IObservable<Disconnected> DisconnectionHappened { get; }
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