using System.Text;

namespace WebSocket.Rx;

public interface IReactiveWebSocketServer : IDisposable
{
    public TimeSpan InactivityTimeout { get; set; }

    public TimeSpan ConnectTimeout { get; set; }

    public bool IsReconnectionEnabled { get; set; }

    public Encoding MessageEncoding { get; set; }

    public bool IsTextMessageConversionEnabled { get; set; }

    public int ClientCount { get; }

    public IReadOnlyDictionary<string, ReactiveWebSocketClient> ConnectedClients { get; }

    public IObservable<Temp.ClientConnected> ClientConnected { get; }

    public IObservable<Temp.ClientDisconnected> ClientDisconnected { get; }

    public IObservable<ReceivedMessage> Messages { get; }
}