using WebSocket.Rx.IntegrationTests.Internal;

namespace WebSocket.Rx.IntegrationTests;

public class ResourceLeakTests(ITestOutputHelper output) : ReactiveWebSocketServerTestBase(output)
{
    [Fact(Timeout = 15000)]
    public async Task ResourceLeak_Client_ShouldNotLeakHandles()
    {
        // Arrange & Act
        var ct = TestContext.Current.CancellationToken;
        for (var i = 0; i < 50; i++)
        {
            var client = new ReactiveWebSocketClient(new Uri(WebSocketUrl));
            await client.DisposeAsync();
        }

        // Assert
        var exception = Record.Exception(() =>
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        });
        Assert.Null(exception);
    }

    [Fact(Timeout = 15000)]
    public async Task ResourceLeak_Server_ShouldNotLeakHandles()
    {
        // Arrange & Act
        var ct = TestContext.Current.CancellationToken;
        for (var i = 0; i < 50; i++)
        {
            var port = GetAvailablePort();
            var server = new ReactiveWebSocketServer($"http://localhost:{port}/");
            await server.DisposeAsync();
        }

        // Assert
        var exception = Record.Exception(() =>
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        });
        Assert.Null(exception);
    }
}
