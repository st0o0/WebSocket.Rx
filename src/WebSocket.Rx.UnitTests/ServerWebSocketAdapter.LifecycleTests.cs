using System.Net;
using WebSocket.Rx.UnitTests.Internal;

namespace WebSocket.Rx.UnitTests;

public class ServerWebSocketAdapterLifecycleTests(ITestOutputHelper output) : ServerWebSocketAdapterTestBase(output)
{
    [Fact(Timeout = DefaultTimeoutMs)]
    public void Constructor_ShouldInitializeAdapter()
    {
        // Act
        Adapter = new ReactiveWebSocketServer.ServerWebSocketAdapter(MockWebSocket,
            new Metadata(Guid.Empty, IPAddress.Any, 0));

        // Assert
        Assert.Equal(MockWebSocket, Adapter.NativeServerSocket);
        Assert.True(Adapter.IsStarted);
        Assert.True(Adapter.IsRunning);
    }

    [Fact(Timeout = DefaultTimeoutMs)]
    public void Constructor_WithNullSocket_ShouldThrowArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new ReactiveWebSocketServer.ServerWebSocketAdapter(null!, new Metadata(Guid.Empty, IPAddress.Any, 0)));
    }

    [Fact(Timeout = DefaultTimeoutMs)]
    public void Dispose_CalledTwice_ShouldNotThrow()
    {
        // Arrange
        Adapter = new ReactiveWebSocketServer.ServerWebSocketAdapter(MockWebSocket,
            new Metadata(Guid.Empty, IPAddress.Any, 0));
        Adapter.Dispose();

        // Act
        var exception = Record.Exception(() => Adapter.Dispose());

        // Assert
        Assert.Null(exception);
    }

    [Fact(Timeout = DefaultTimeoutMs)]
    public async Task DisposeAsync_ShouldMarkAsDisposed()
    {
        // Arrange
        Adapter = new ReactiveWebSocketServer.ServerWebSocketAdapter(MockWebSocket,
            new Metadata(Guid.Empty, IPAddress.Any, 0));

        // Act
        await Adapter.DisposeAsync();

        // Assert
        Assert.True(Adapter.IsDisposed);
    }

    [Fact(Timeout = DefaultTimeoutMs)]
    public async Task Dispose_MultipleTimes_ShouldBeIdempotent()
    {
        // Arrange
        Adapter = new ReactiveWebSocketServer.ServerWebSocketAdapter(MockWebSocket,
            new Metadata(Guid.Empty, IPAddress.Any, 0));

        // Act
        await Adapter.DisposeAsync();
        await Adapter.DisposeAsync();
        Adapter.Dispose();

        // Assert
        Assert.True(Adapter.IsDisposed);
    }
}
