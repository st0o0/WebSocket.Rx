using System.Net;
using System.Net.WebSockets;

namespace WebSocket.Rx.UnitTests;

public class ClientDisconnectedTests
{
    private const int DefaultTimeoutMs = 5000;

    [Fact(Timeout = DefaultTimeoutMs)]
    public void Constructor_WithoutError_ShouldSetProperties()
    {
        // Arrange
        var id = Guid.NewGuid();
        const DisconnectReason reason = DisconnectReason.ClientInitiated;

        // Act
        var disconnected = new ClientDisconnected(new Metadata(id, IPAddress.Any, 0), new Disconnected(reason));

        // Assert
        Assert.Equal(id, disconnected.Metadata.Id);
        Assert.Equal(reason, disconnected.Event.Reason);
        Assert.Null(disconnected.Event.Exception);
    }


    [Fact(Timeout = DefaultTimeoutMs)]
    public void Equality_WithSameValues_ShouldBeEqual()
    {
        // Arrange
        var error = new WebSocketException("Test");
        var @event = new Disconnected(DisconnectReason.Undefined, WebSocketCloseStatus.Empty, string.Empty,
            string.Empty, Exception: error);
        var metadata = new Metadata(Guid.Empty, IPAddress.Any, 0);
        var disconnected1 = new ClientDisconnected(metadata, @event);
        var disconnected2 = new ClientDisconnected(metadata, @event);

        // Act & Assert
        Assert.Equal(disconnected1, disconnected2);
    }
}