namespace WebSocket.Rx.Tests;

public class ConnectedTests
{
    private const int DefaultTimeoutMs = 5000;

    [Fact(Timeout = DefaultTimeoutMs)]
    public void Equality_WithSameValues_ShouldBeEqual()
    {
        // Arrange
        const ConnectReason @enum = ConnectReason.Initial;
        var connected1 = new Connected(@enum);
        var connected2 = new Connected(@enum);
        
        // Act & Assert
        Assert.Equal(connected1, connected2);
    }
}