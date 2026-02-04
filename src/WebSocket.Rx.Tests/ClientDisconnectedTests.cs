using System.Net;

namespace WebSocket.Rx.Tests;

public class ClientDisconnectedTests
{
    [Fact]
    public void Constructor_WithoutError_ShouldSetProperties()
    {
        // Arrange
        var id = Guid.NewGuid();
        const DisconnectReason reason = DisconnectReason.ClientInitiated;

        // Act
        var disconnected = new ClientDisconnected(new Metadata(id, IPAddress.Any, 0), Disconnected.Create(reason));

        // Assert
        Assert.Equal(id, disconnected.Metadata.Id);
        Assert.Equal(reason, disconnected.Event.Reason);
        Assert.Null(disconnected.Event.Exception);
    }

    [Fact]
    public void Constructor_WithError_ShouldSetAllProperties()
    {
        // Arrange
        const DisconnectReason reason = DisconnectReason.Error;
        var error = new Exception("Test error");

        // Act
        var disconnected = new ClientDisconnected(new Metadata(Guid.Empty, IPAddress.Any, 0),
            Disconnected.Create(reason, error));

        // Assert
        Assert.Equal(Guid.Empty, disconnected.Metadata.Id);
        Assert.Equal(reason, disconnected.Event.Reason);
        Assert.Equal(error, disconnected.Event.Exception);
    }

    [Fact]
    public void Equality_WithSameValues_ShouldBeEqual()
    {
        // Arrange
        var error = new Exception("Test");
        var @event = Disconnected.Create(DisconnectReason.Error, error);
        var metadata = new Metadata(Guid.Empty, IPAddress.Any, 0);
        var disconnected1 = new ClientDisconnected(metadata, @event);
        var disconnected2 = new ClientDisconnected(metadata, @event);

        // Act & Assert
        Assert.Equal(disconnected1, disconnected2);
    }
}