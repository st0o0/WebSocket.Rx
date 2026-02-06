namespace WebSocket.Rx;

public record Disconnected(DisconnectReason Reason, Exception? Exception = null);