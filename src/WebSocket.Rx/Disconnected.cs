using System.Net.WebSockets;

namespace WebSocket.Rx;

public record Disconnected(
    DisconnectReason Reason,
    WebSocketCloseStatus? CloseStatus = null,
    string? CloseStatusDescription = null,
    string? SubProtocol = null,
    WebSocketException? Exception = null)
{
    public void CancelClosing() => IsClosingCanceled = true;
    public void CancelReconnection() => IsReconnectionCanceled = true;

    public bool IsClosingCanceled { get; private set; }
    public bool IsReconnectionCanceled { get; private set; }
}