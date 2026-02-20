using System.Buffers;
using System.Net.WebSockets;

namespace WebSocket.Rx;

public readonly struct Payload : IDisposable
{
    private readonly byte[]? _rentedBuffer;
    
    public ReadOnlyMemory<byte> Data { get; }
    public WebSocketMessageType Type { get; }
    
    public Payload(ReadOnlyMemory<byte> data, WebSocketMessageType messageType)
    {
        Data = data;
        Type = messageType;
        _rentedBuffer = null;
    }

    public Payload(byte[] rentedBuffer, int length, WebSocketMessageType messageType)
    {
        Data = rentedBuffer.AsMemory(0, length);
        Type = messageType;
        _rentedBuffer = rentedBuffer;
    }

    public void Dispose()
    {
        if (_rentedBuffer is not null)
        {
            ArrayPool<byte>.Shared.Return(_rentedBuffer);
        }
    }
}