using System.Net.WebSockets;

namespace WebSocket.Rx;

public record Disconnected(
    DisconnectReason Reason,
    WebSocketCloseStatus? CloseStatus = null,
    string? CloseStatusDescription = null,
    string? SubProtocol = null,
    WebSocketException? Exception = null)
{
    internal bool IsClosingCanceled { get; private set; }
    internal bool IsReconnectionCanceled { get; private set; }

    public void CancelClosing() => IsClosingCanceled = true;
    public void CancelReconnection() => IsReconnectionCanceled = true;
}