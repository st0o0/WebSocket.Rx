using System.Net;

namespace WebSocket.Rx.Tests;

public class ClientConnectedTests
{
    [Fact]
    public void Constructor_ShouldSetName()
    {
        // Arrange
        var id = Guid.NewGuid();

        // Act
        var connected =
            new ClientConnected(new Metadata(id, IPAddress.Any, 0), Connected.Create(ConnectReason.Initial));

        // Assert
        Assert.Equal(id, connected.Metadata.Id);
    }

    [Fact]
    public void Equality_WithSameName_ShouldBeEqual()
    {
        // Arrange
        var @event = Connected.Create(ConnectReason.Initial);
        var metadata = new Metadata(Guid.Empty, IPAddress.Any, 0);
        var connected1 = new ClientConnected(metadata, @event);
        var connected2 = new ClientConnected(metadata, @event);

        // Act & Assert
        Assert.Equal(connected1, connected2);
    }

    [Fact]
    public void Equality_WithDifferentName_ShouldNotBeEqual()
    {
        // Arrange
        var connected1 = new ClientConnected(new Metadata(Guid.Empty, IPAddress.Any, 0),
            Connected.Create(ConnectReason.Initial));
        var connected2 = new ClientConnected(new Metadata(Guid.Empty, IPAddress.Any, 1),
            Connected.Create(ConnectReason.Initial));

        // Act & Assert
        Assert.NotEqual(connected1, connected2);
    }
}