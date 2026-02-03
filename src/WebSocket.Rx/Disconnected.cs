namespace WebSocket.Rx;

public enum DisconnectReason
{
    ConnectionLost = 0,
    Timeout = 1,
    Error = 2,
    ClientInitiated = 3,
    ServerInitiated = 4,
    Shutdown = 5
}

public enum ConnectReason
{
    Initial = 0,
    Reconnect = 1
}

public sealed class Disconnected
{
    public DisconnectReason Reason { get; init; }
    public DateTime Timestamp { get; init; }
    public Exception? Exception { get; init; }

    public static Disconnected Create(DisconnectReason reason, Exception? exception = null) => new()
    {
        Reason = reason,
        Exception = exception,
        Timestamp = DateTime.UtcNow
    };
}

public sealed class Connected
{
    public ConnectReason Reason { get; init; }
    public DateTime Timestamp { get; init; }

    public static Connected Create(ConnectReason reason) => new()
    {
        Reason = reason,
        Timestamp = DateTime.UtcNow
    };
}