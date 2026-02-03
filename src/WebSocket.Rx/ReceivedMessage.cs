using System.Net.WebSockets;

namespace WebSocket.Rx;

public class ReceivedMessage
{
    private ReceivedMessage(MemoryStream? memoryStream, byte[]? binary, string? text, WebSocketMessageType messageType)
    {
        Stream = memoryStream;
        Binary = binary;
        Text = text;
        MessageType = messageType;
    }

    public string? Text { get; }

    public byte[]? Binary => Stream is null ? field : Stream.ToArray();

    public MemoryStream? Stream { get; }

    public WebSocketMessageType MessageType { get; }

    public override string ToString()
    {
        if (MessageType == WebSocketMessageType.Text)
        {
            return Text ?? string.Empty;
        }

        return $"Type binary, length: {Binary?.Length}";
    }

    public static ReceivedMessage Empty()
    {
        return new ReceivedMessage(null, null, null, WebSocketMessageType.Close);
    }

    public static ReceivedMessage TextMessage(string? data)
    {
        return new ReceivedMessage(null, null, data, WebSocketMessageType.Text);
    }

    public static ReceivedMessage BinaryMessage(byte[]? data)
    {
        return new ReceivedMessage(null, data, null, WebSocketMessageType.Binary);
    }

    public static ReceivedMessage BinaryStreamMessage(MemoryStream? memoryStream)
    {
        return new ReceivedMessage(memoryStream, null, null, WebSocketMessageType.Binary);
    }
}