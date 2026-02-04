using System.Net.WebSockets;

namespace WebSocket.Rx;

public sealed record Payload(byte[] Data, WebSocketMessageType Type);