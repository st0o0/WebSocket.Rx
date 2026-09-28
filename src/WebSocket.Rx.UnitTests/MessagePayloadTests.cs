using System.Buffers;
using System.Net;
using System.Net.WebSockets;

namespace WebSocket.Rx.UnitTests;

public class MessagePayloadTests
{
    [Fact]
    public void Message_CreateText_ShouldSetTextProperties()
    {
        var message = Message.Create("Hello");

        Assert.Multiple(() =>
        {
            Assert.True(message.IsText);
            Assert.False(message.IsBinary);
            Assert.Equal(WebSocketMessageType.Text, message.Type);
            Assert.Equal("Hello", message.Text.ToString());
            Assert.True(message.Binary.IsEmpty);
            Assert.Equal("Hello", message.ToString());
        });
    }

    [Fact]
    public void Message_CreateBinary_ShouldSetBinaryProperties()
    {
        var data = new byte[] { 1, 2, 3 };

        var message = Message.Create(data);

        Assert.Multiple(() =>
        {
            Assert.True(message.IsBinary);
            Assert.False(message.IsText);
            Assert.Equal(WebSocketMessageType.Binary, message.Type);
            Assert.Equal(data, message.Binary.ToArray());
            Assert.True(message.Text.IsEmpty);
            Assert.Equal("Type binary, length: 3", message.ToString());
        });
    }

    [Fact]
    public void Message_CreateEmptyText_ShouldNotBeText()
    {
        var message = Message.Create(ReadOnlyMemory<char>.Empty);

        Assert.Multiple(() =>
        {
            Assert.False(message.IsText);
            Assert.False(message.IsBinary);
        });
    }

    [Fact]
    public void Payload_Constructors_ShouldSetDataAndType()
    {
        var data = new byte[] { 4, 5, 6 };
        using var payload = new Payload(data, WebSocketMessageType.Binary);

        Assert.Multiple(() =>
        {
            Assert.Equal(data, payload.Data.ToArray());
            Assert.Equal(WebSocketMessageType.Binary, payload.Type);
        });

        var rented = ArrayPool<byte>.Shared.Rent(8);
        rented[0] = 9;
        rented[1] = 8;

        using var rentedPayload = new Payload(rented, 2, WebSocketMessageType.Text);

        Assert.Multiple(() =>
        {
            Assert.Equal(2, rentedPayload.Data.Length);
            Assert.Equal(new byte[] { 9, 8 }, rentedPayload.Data.ToArray());
            Assert.Equal(WebSocketMessageType.Text, rentedPayload.Type);
        });
    }

    [Fact]
    public void Message_CreateEmptyBinary_ShouldNotBeBinary()
    {
        var message = Message.Create(ReadOnlyMemory<byte>.Empty);

        Assert.Multiple(() =>
        {
            Assert.False(message.IsBinary);
            Assert.False(message.IsText);
        });
    }

    [Fact]
    public void Payload_Dispose_IsIdempotent()
    {
        var data = new byte[] { 1, 2, 3 };
        var payload = new Payload(data, WebSocketMessageType.Binary);

        var exception = Record.Exception(() =>
        {
            payload.Dispose();
            payload.Dispose();
        });

        Assert.Null(exception);
    }

    [Fact]
    public void Payload_RentedBuffer_Dispose_IsIdempotent()
    {
        var rented = ArrayPool<byte>.Shared.Rent(16);
        rented[0] = 42;
        var payload = new Payload(rented, 1, WebSocketMessageType.Binary);

        var exception = Record.Exception(() =>
        {
            payload.Dispose();
            payload.Dispose();
        });

        Assert.Null(exception);
    }

    [Fact]
    public void Metadata_DefaultsToNullForOptionalValues()
    {
        var id = Guid.NewGuid();
        var metadata = new Metadata(id);

        Assert.Multiple(() =>
        {
            Assert.Equal(id, metadata.Id);
            Assert.Null(metadata.Address);
            Assert.Null(metadata.Port);
        });
    }

    [Fact]
    public void Metadata_WithAllProperties_SetsValues()
    {
        var id = Guid.NewGuid();
        var metadata = new Metadata(id, IPAddress.Loopback, 8080);

        Assert.Multiple(() =>
        {
            Assert.Equal(id, metadata.Id);
            Assert.NotNull(metadata.Address);
            Assert.Equal(IPAddress.Loopback, metadata.Address);
            Assert.NotNull(metadata.Port);
            Assert.Equal(8080, metadata.Port);
        });
    }

    [Fact]
    public void Metadata_Equality_WithSameValues()
    {
        var id = Guid.NewGuid();
        var metadata1 = new Metadata(id, IPAddress.Loopback, 1234);
        var metadata2 = new Metadata(id, IPAddress.Loopback, 1234);

        Assert.Multiple(() =>
        {
            Assert.Equal(metadata1, metadata2);
            Assert.True(metadata1 == metadata2);
        });
    }

    [Fact]
    public void Metadata_Equality_WithDifferentId_ShouldNotBeEqual()
    {
        var metadata1 = new Metadata(Guid.NewGuid(), IPAddress.Loopback, 1234);
        var metadata2 = new Metadata(Guid.NewGuid(), IPAddress.Loopback, 1234);

        Assert.NotEqual(metadata1, metadata2);
    }

    [Fact]
    public void ServerMessage_SetsProperties()
    {
        var metadata = new Metadata(Guid.NewGuid(), IPAddress.Loopback, 1234);
        var message = Message.Create("Hello");

        var serverMessage = new ServerMessage(metadata, message);

        Assert.Multiple(() =>
        {
            Assert.Equal(metadata, serverMessage.Metadata);
            Assert.Equal(message, serverMessage.Message);
            Assert.True(serverMessage.Message.IsText);
            Assert.Equal("Hello", serverMessage.Message.Text.ToString());
        });
    }

    [Fact]
    public void ServerMessage_Equality_WithSameValues()
    {
        var metadata = new Metadata(Guid.NewGuid(), IPAddress.Loopback, 1234);
        var message = Message.Create("Hi");
        var serverMessage1 = new ServerMessage(metadata, message);
        var serverMessage2 = new ServerMessage(metadata, message);

        Assert.Multiple(() =>
        {
            Assert.Equal(serverMessage1, serverMessage2);
            Assert.True(serverMessage1 == serverMessage2);
        });
    }

    [Fact]
    public void ServerMessage_Equality_WithDifferentMessage_ShouldNotBeEqual()
    {
        var metadata = new Metadata(Guid.NewGuid());
        var serverMessage1 = new ServerMessage(metadata, Message.Create("A"));
        var serverMessage2 = new ServerMessage(metadata, Message.Create("B"));

        Assert.NotEqual(serverMessage1, serverMessage2);
    }
}
