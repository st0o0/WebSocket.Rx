using System.Buffers;
using System.Net.WebSockets;
using System.Text;

namespace WebSocket.Rx;

public interface IReactiveWebSocketClient : IDisposable
{
    bool Send(string message);

    bool Send(byte[] message);

    bool Send(ArraySegment<byte> message);

    bool Send(ReadOnlySequence<byte> message);

    Task SendInstant(string message);

    Task SendInstant(byte[] message);

    bool SendAsText(byte[] message);

    bool SendAsText(ArraySegment<byte> message);

    bool SendAsText(ReadOnlySequence<byte> message);

    void StreamFakeMessage(ReceivedMessage message);

    Uri Url { get; set; }

    IObservable<ReceivedMessage> MessageReceived { get; }

    IObservable<Connected> ConnectionHappened { get; }

    IObservable<Disconnected> DisconnectionHappened { get; }

    TimeSpan ConnectTimeout { get; set; }

    TimeSpan InactivityTimeout { get; set; }

    bool IsReconnectionEnabled { get; set; }

    string? Name { get; set; }

    bool IsStarted { get; }

    bool IsRunning { get; }

    bool SenderRunning { get; }

    bool IsTextMessageConversionEnabled { get; set; }

    Encoding MessageEncoding { get; set; }

    ClientWebSocket? NativeClient { get; }

    Task Start();

    Task StartOrFail();

    Task<bool> StopAsync(WebSocketCloseStatus status, string statusDescription);

    Task<bool> StopOrFail(WebSocketCloseStatus status, string statusDescription);

    Task Reconnect();

    Task ReconnectOrFail();
}