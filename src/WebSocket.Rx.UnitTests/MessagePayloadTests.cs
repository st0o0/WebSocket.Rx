using System.Buffers;
using System.Net;
using System.Net.WebSockets;

namespace WebSocket.Rx.UnitTests;

public class MessagePayloadTests
{
    private const int DefaultTimeoutMs = 5000;

    [Fact(Timeout = DefaultTimeoutMs)]
    public void Message_CreateText_ShouldSetTextProperties()
    {
        var message = Message.Create("Hello");

        Assert.True(message.IsText);
        Assert.False(message.IsBinary);
        Assert.Equal(WebSocketMessageType.Text, message.Type);
        Assert.Equal("Hello", message.Text.ToString());
        Assert.True(message.Binary.IsEmpty);
        Assert.Equal("Hello", message.ToString());
    }

    [Fact(Timeout = DefaultTimeoutMs)]
    public void Message_CreateBinary_ShouldSetBinaryProperties()
    {
        var data = new byte[] { 1, 2, 3 };

        var message = Message.Create(data);

        Assert.True(message.IsBinary);
        Assert.False(message.IsText);
        Assert.Equal(WebSocketMessageType.Binary, message.Type);
        Assert.Equal(data, message.Binary.ToArray());
        Assert.True(message.Text.IsEmpty);
        Assert.Equal("Type binary, length: 3", message.ToString());
    }

    [Fact(Timeout = DefaultTimeoutMs)]
    public void Message_CreateEmptyText_ShouldNotBeText()
    {
        var message = Message.Create(ReadOnlyMemory<char>.Empty);

        Assert.False(message.IsText);
        Assert.False(message.IsBinary);
    }

    [Fact(Timeout = DefaultTimeoutMs)]
    public void Payload_Constructors_ShouldSetDataAndType()
    {
        var data = new byte[] { 4, 5, 6 };
        using var payload = new Payload(data, WebSocketMessageType.Binary);

        Assert.Equal(data, payload.Data.ToArray());
        Assert.Equal(WebSocketMessageType.Binary, payload.Type);

        var rented = ArrayPool<byte>.Shared.Rent(8);
        rented[0] = 9;
        rented[1] = 8;

        using var rentedPayload = new Payload(rented, 2, WebSocketMessageType.Text);

        Assert.Equal(2, rentedPayload.Data.Length);
        Assert.Equal(new byte[] { 9, 8 }, rentedPayload.Data.ToArray());
        Assert.Equal(WebSocketMessageType.Text, rentedPayload.Type);
    }

    [Fact(Timeout = DefaultTimeoutMs)]
    public void Metadata_DefaultsToNullForOptionalValues()
    {
        var metadata = new Metadata(Guid.NewGuid());

        Assert.Null(metadata.Address);
        Assert.Null(metadata.Port);
    }

    [Fact(Timeout = DefaultTimeoutMs)]
    public void Records_WithSameValues_ShouldBeEqual()
    {
        var metadata = new Metadata(Guid.NewGuid(), IPAddress.Loopback, 1234);
        var message = Message.Create("Hi");
        var serverMessage1 = new ServerMessage(metadata, message);
        var serverMessage2 = new ServerMessage(metadata, message);

        var exception = new InvalidOperationException("Test");
        var error1 = new ErrorOccurred(ErrorSource.Send, exception);
        var error2 = new ErrorOccurred(ErrorSource.Send, exception);

        Assert.Equal(serverMessage1, serverMessage2);
        Assert.Equal(error1, error2);
    }
}
