using System.Text;

namespace WebSocket.Rx;

public interface IReactiveWebSocketServer : IDisposable
{
    public TimeSpan IdleConnection { get; set; }

    public TimeSpan ConnectTimeout { get; set; }

    public bool IsReconnectionEnabled { get; set; }

    public Encoding MessageEncoding { get; set; }

    public bool IsTextMessageConversionEnabled { get; set; }

    public int ClientCount { get; }

    public IReadOnlyDictionary<Guid, Metadata> ConnectedClients { get; }

    public IObservable<ClientConnected> ClientConnected { get; }

    public IObservable<ClientDisconnected> ClientDisconnected { get; }

    public IObservable<ServerReceivedMessage> Messages { get; }
}