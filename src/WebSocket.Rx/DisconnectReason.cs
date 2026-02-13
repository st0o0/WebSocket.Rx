namespace WebSocket.Rx;

public enum DisconnectReason
{
    Undefined = 0,
    ClientInitiated = 1,
    ServerInitiated = 2,
    TimedOut = 3,
    Dropped = 4,
    Closed = 5
}