using System.Net.WebSockets;
using WebSocket.Rx.IntegrationTests.Internal;

namespace WebSocket.Rx.IntegrationTests;

public class IntegrationDisposeTests(ITestOutputHelper output) : ReactiveWebSocketServerTestBase(output)
{
    [Fact(Timeout = DefaultTimeoutMs)]
    public async Task Integration_ServerAndClient_BothDispose_ShouldCleanupProperly()
    {
        // Arrange
        var client = new ReactiveWebSocketClient(new Uri(WebSocketUrl));
        var connectionTask = WaitForEventAsync(Server.ClientConnected);
        await client.StartAsync(TestContext.Current.CancellationToken);
        await connectionTask;

        // Act
        await client.DisposeAsync();
        await Server.DisposeAsync();

        // Assert
        Assert.True(client.IsDisposed);
        Assert.True(Server.IsDisposed);
    }

    [Fact(Timeout = DefaultTimeoutMs)]
    public async Task Integration_MultipleClientsAndServer_AllDispose_ShouldCleanupProperly()
    {
        // Arrange
        var clients = new List<ReactiveWebSocketClient>();
        for (var i = 0; i < 5; i++)
        {
            var client = new ReactiveWebSocketClient(new Uri(WebSocketUrl));
            var connectionTask = WaitForEventAsync(Server.ClientConnected);
            await client.StartAsync(TestContext.Current.CancellationToken);
            await connectionTask;
            clients.Add(client);
        }

        Assert.Equal(5, Server.ClientCount);

        // Act
        foreach (var client in clients)
        {
            await client.DisposeAsync();
        }

        await Server.DisposeAsync();

        // Assert
        foreach (var client in clients)
        {
            Assert.True(client.IsDisposed);
        }

        Assert.True(Server.IsDisposed);
        Assert.Equal(0, Server.ClientCount);
    }

    [Fact(Timeout = DefaultTimeoutMs)]
    public async Task Integration_DisposeUnderLoad_ShouldHandleGracefully()
    {
        // Arrange
        var client = new ReactiveWebSocketClient(new Uri(WebSocketUrl));
        var connectionTask = WaitForEventAsync(Server.ClientConnected);
        await client.StartAsync(TestContext.Current.CancellationToken);
        await connectionTask;

        var sendTask = Task.Run(async () =>
        {
            for (var i = 0; i < 100; i++)
            {
                try
                {
                    await client.SendAsync($"Message {i}".AsMemory(), WebSocketMessageType.Text);
                    await Task.Delay(10, TestContext.Current.CancellationToken);
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }, TestContext.Current.CancellationToken);

        await Task.Delay(50, TestContext.Current.CancellationToken);

        // Act
        await client.DisposeAsync();
        await Server.DisposeAsync();

        // Assert
        Assert.True(client.IsDisposed);
        Assert.True(Server.IsDisposed);

        await sendTask;
    }

    [Fact(Timeout = DefaultTimeoutMs)]
    public async Task Integration_ConcurrentDispose_ShouldBeThreadSafe()
    {
        // Act
        var disposeTasks = Enumerable.Range(0, 10)
            .Select(_ => Task.Run(async () => await Server.DisposeAsync()))
            .ToArray();

        await Task.WhenAll(disposeTasks);

        // Assert
        Assert.True(Server.IsDisposed);
    }
}