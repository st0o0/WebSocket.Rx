using System.Net.WebSockets;

namespace WebSocket.Rx;

public record Message
{
    private Message(ReadOnlyMemory<byte>? binary, ReadOnlyMemory<char>? text, WebSocketMessageType type)
    {
        Binary = binary ?? new ReadOnlyMemory<byte>([]);
        Text = text ?? new ReadOnlyMemory<char>([]);
        Type = type;
    }

    public bool IsText => !Text.IsEmpty && Type is WebSocketMessageType.Text;

    public bool IsBinary => !Binary.IsEmpty && Type is WebSocketMessageType.Binary;

    public ReadOnlyMemory<char> Text { get; }

    public ReadOnlyMemory<byte> Binary { get; }

    public WebSocketMessageType Type { get; }

    public override string ToString()
    {
        return Type is WebSocketMessageType.Text ? Text.ToString() : $"Type binary, length: {Binary.Length}";
    }

    public static Message Create(string message)
        => Create(message.AsMemory());

    public static Message Create(ReadOnlyMemory<char> message)
        => new(null, message, WebSocketMessageType.Text);

    public static Message Create(ReadOnlyMemory<byte> message)
        => new(message, null, WebSocketMessageType.Binary);
}