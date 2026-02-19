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
    Observable<ServerMessage> Messages { get; }
    Observable<ServerErrorOccurred> ErrorOccurred { get; }

    Task StartAsync(CancellationToken cancellationToken = default);

    Task<bool> StopAsync(WebSocketCloseStatus status, string statusDescription,
        CancellationToken cancellationToken = default);

    Task<bool> SendInstantAsync(Guid clientId, ReadOnlyMemory<char> message, WebSocketMessageType type,
        CancellationToken cancellationToken = default);

    Task<bool> SendInstantAsync(Guid clientId, ReadOnlyMemory<byte> message, WebSocketMessageType type,
        CancellationToken cancellationToken = default);

    Task<bool> SendAsync(Guid clientId, ReadOnlyMemory<char> message, WebSocketMessageType type,
        CancellationToken cancellationToken = default);

    Task<bool> SendAsync(Guid clientId, ReadOnlyMemory<byte> message, WebSocketMessageType type,
        CancellationToken cancellationToken = default);
    
    bool TrySend(Guid clientId, ReadOnlyMemory<char> message, WebSocketMessageType type);

    bool TrySend(Guid clientId, ReadOnlyMemory<byte> message, WebSocketMessageType type);

    Task<bool> BroadcastInstantAsync(ReadOnlyMemory<char> message, WebSocketMessageType type,
        CancellationToken cancellationToken = default);

    Task<bool> BroadcastInstantAsync(ReadOnlyMemory<byte> message, WebSocketMessageType type,
        CancellationToken cancellationToken = default);

    Task<bool> BroadcastAsync(ReadOnlyMemory<char> message, WebSocketMessageType type,
        CancellationToken cancellationToken = default);

    Task<bool> BroadcastAsync(ReadOnlyMemory<byte> message, WebSocketMessageType type,
        CancellationToken cancellationToken = default);

    bool TryBroadcast(ReadOnlyMemory<char> message, WebSocketMessageType type);

    bool TryBroadcast(ReadOnlyMemory<byte> message, WebSocketMessageType type);
}