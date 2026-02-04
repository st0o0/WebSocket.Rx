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

public record Disconnected
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

public record Connected
{
    public ConnectReason Reason { get; init; }
    public DateTime Timestamp { get; init; }

    public static Connected Create(ConnectReason reason) => new()
    {
        Reason = reason,
        Timestamp = DateTime.UtcNow
    };
}