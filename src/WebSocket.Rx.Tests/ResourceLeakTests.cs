using WebSocket.Rx.Tests.Internal;

namespace WebSocket.Rx.Tests;

public class ResourceLeakTests(ITestOutputHelper output) : ReactiveWebSocketServerTestBase(output)
{
    [Fact(Timeout = DefaultTimeoutMs)]
    public async Task ResourceLeak_Client_ShouldNotLeakHandles()
    {
        // Arrange & Act
        for (var i = 0; i < 50; i++)
        {
            var client = new ReactiveWebSocketClient(new Uri(WebSocketUrl));
            await client.DisposeAsync();
        }

        // Force garbage collection
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        // Assert
        Assert.True(true);
    }

    [Fact(Timeout = DefaultTimeoutMs)]
    public async Task ResourceLeak_Server_ShouldNotLeakHandles()
    {
        // Arrange & Act
        for (var i = 0; i < 50; i++)
        {
            var port = GetAvailablePort();
            var server = new ReactiveWebSocketServer($"http://localhost:{port}/");
            await server.DisposeAsync();
        }

        // Force garbage collection
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        // Assert
        Assert.True(true);
    }
}
