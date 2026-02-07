using System.Net.WebSockets;
using System.Text;
using R3;

namespace WebSocket.Rx;

public interface IReactiveWebSocketServer : IDisposable, IAsyncDisposable
{
    TimeSpan IdleConnection { get; set; }
    TimeSpan ConnectTimeout { get; set; }
    Encoding MessageEncoding { get; set; }
    bool IsTextMessageConversionEnabled { get; set; }
    int ClientCount { get; }
    IReadOnlyDictionary<Guid, Metadata> ConnectedClients { get; }
    Observable<ClientConnected> ClientConnected { get; }
    Observable<ClientDisconnected> ClientDisconnected { get; }
    Observable<ServerReceivedMessage> Messages { get; }
    Observable<ServerErrorOccurred> ErrorOccurred { get; }

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
    
    Observable<bool> SendInstant(Guid clientId, Observable<byte[]> messages);

    Observable<bool> SendInstant(Guid clientId, Observable<string> messages);

    Observable<bool> SendAsBinary(Guid clientId, Observable<byte[]> messages);

    Observable<bool> SendAsBinary(Guid clientId, Observable<string> messages);

    Observable<bool> SendAsText(Guid clientId, Observable<byte[]> messages);

    Observable<bool> SendAsText(Guid clientId, Observable<string> messages);

    Observable<bool> TrySendAsBinary(Guid clientId, Observable<string> messages);

    Observable<bool> TrySendAsBinary(Guid clientId, Observable<byte[]> messages);

    Observable<bool> TrySendAsText(Guid clientId, Observable<string> messages);

    Observable<bool> TrySendAsText(Guid clientId, Observable<byte[]> messages);
}