using System.Net.WebSockets;

namespace WebSocket.Rx;

public class ReceivedMessage
{
    private ReceivedMessage(byte[]? binary, string? text, WebSocketMessageType messageType)
    {
        Binary = binary;
        Text = text;
        MessageType = messageType;
    }

    public string? Text { get; }

    public byte[]? Binary { get; }

    public WebSocketMessageType MessageType { get; }

    public override string ToString()
    {
        if (MessageType == WebSocketMessageType.Text)
        {
            return Text ?? string.Empty;
        }

        return $"Type binary, length: {Binary?.Length}";
    }

    public static ReceivedMessage Empty() => new(null, null, WebSocketMessageType.Close);

    public static ReceivedMessage TextMessage(string? data)
        => new(null, data, WebSocketMessageType.Text);

    public static ReceivedMessage BinaryMessage(byte[]? data)
        => new(data, null, WebSocketMessageType.Binary);
}