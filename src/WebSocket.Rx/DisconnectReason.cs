namespace WebSocket.Rx;

public enum DisconnectReason
{
    Undefined = 0,
    ConnectionLost = 1,
    Timeout = 2,
    ClientInitiated = 4,
    ServerInitiated = 5,
    Shutdown = 6
}