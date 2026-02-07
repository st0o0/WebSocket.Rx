namespace WebSocket.Rx;

public enum ErrorSource
{
    Undefined = 0,
    Connection,
    Reconnection,
    Disconnection,
    Send,
    SendLoop,
    ReceiveLoop,
    Dispose,
}