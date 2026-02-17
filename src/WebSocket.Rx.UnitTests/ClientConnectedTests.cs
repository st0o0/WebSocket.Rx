using System.Net;

namespace WebSocket.Rx.UnitTests;

public class ClientConnectedTests
{
    private const int DefaultTimeoutMs = 5000;

    [Fact(Timeout = DefaultTimeoutMs)]
    public void Constructor_ShouldSetName()
    {
        // Arrange
        var id = Guid.NewGuid();

        // Act
        var connected =
            new ClientConnected(new Metadata(id, IPAddress.Any, 0), new Connected(ConnectReason.Initialized));

        // Assert
        Assert.Equal(id, connected.Metadata.Id);
    }

    [Fact(Timeout = DefaultTimeoutMs)]
    public void Equality_WithSameName_ShouldBeEqual()
    {
        // Arrange
        var @event = new Connected(ConnectReason.Initialized);
        var metadata = new Metadata(Guid.Empty, IPAddress.Any, 0);
        var connected1 = new ClientConnected(metadata, @event);
        var connected2 = new ClientConnected(metadata, @event);

        // Act & Assert
        Assert.Equal(connected1, connected2);
    }

    [Fact(Timeout = DefaultTimeoutMs)]
    public void Equality_WithDifferentName_ShouldNotBeEqual()
    {
        // Arrange
        var connected1 = new ClientConnected(new Metadata(Guid.Empty, IPAddress.Any, 0),
            new Connected(ConnectReason.Initialized));
        var connected2 = new ClientConnected(new Metadata(Guid.Empty, IPAddress.Any, 1),
            new Connected(ConnectReason.Initialized));

        // Act & Assert
        Assert.NotEqual(connected1, connected2);
    }
}