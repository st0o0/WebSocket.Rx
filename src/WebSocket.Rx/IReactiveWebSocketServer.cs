using System.Net.WebSockets;
using System.Text;

namespace WebSocket.Rx;

public interface IReactiveWebSocketServer : IDisposable
{
    TimeSpan IdleConnection { get; set; }
    TimeSpan ConnectTimeout { get; set; }
    bool IsReconnectionEnabled { get; set; }
    Encoding MessageEncoding { get; set; }
    bool IsTextMessageConversionEnabled { get; set; }
    int ClientCount { get; }
    IReadOnlyDictionary<Guid, Metadata> ConnectedClients { get; }
    IObservable<ClientConnected> ClientConnected { get; }
    IObservable<ClientDisconnected> ClientDisconnected { get; }
    IObservable<ServerReceivedMessage> Messages { get; }

    Task StartAsync();

    Task<bool> StopAsync(WebSocketCloseStatus status, string statusDescription);

    Task<bool> SendInstantAsync(Guid clientId, string message, CancellationToken cancellationToken = default);

    Task<bool> SendInstantAsync(Guid clientId, byte[] message, CancellationToken cancellationToken = default);

    Task<bool> SendAsBinaryAsync(Guid clientId, byte[] message, CancellationToken cancellationToken = default);

    Task<bool> SendAsBinaryAsync(Guid clientId, string message, CancellationToken cancellationToken = default);

    Task<bool> SendAsTextAsync(Guid clientId, byte[] message, CancellationToken cancellationToken = default);

    Task<bool> SendAsTextAsync(Guid clientId, string message, CancellationToken cancellationToken = default);

    bool TrySendAsBinary(Guid clientId, string message);

    bool TrySendAsBinary(Guid clientId, byte[] data);

    bool TrySendAsText(Guid clientId, string message);

    bool TrySendAsText(Guid clientId, byte[] data);
}