namespace WebSocket.Rx;

public enum DisconnectReason
{
    Undefined = 0,
    ConnectionLost = 1,
    Timeout = 2,
    Error = 3,
    ClientInitiated = 4,
    ServerInitiated = 5,
    Shutdown = 6
}

public enum ConnectReason
{
    Undefined = 0,
    Initial = 1,
    Reconnect = 2
}

public record Disconnected(DisconnectReason Reason, Exception? Exception = null)
{
    public static Disconnected Create(DisconnectReason reason, Exception? exception = null) => new(reason, exception);
}

public record Connected(ConnectReason Reason)
{
    public static Connected Create(ConnectReason reason) => new(reason);
}