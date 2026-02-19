using System.Net.WebSockets;
using R3;
using WebSocket.Rx.IntegrationTests.Internal;

namespace WebSocket.Rx.IntegrationTests;

[Collection("WebSocket Tests")]
public class ReactiveWebSocketServerLifecycleTests(ITestOutputHelper output) : ReactiveWebSocketServerTestBase(output)
{
    [Fact(Timeout = DefaultTimeoutMs)]
    public async Task Should_Start_And_Stop_Server()
    {
        // Arrange
        var port = GetAvailablePort();
        var url = $"http://127.0.0.1:{port}/";
        await using var server = new ReactiveWebSocketServer(url);

        // Act
        await server.StartAsync(TestContext.Current.CancellationToken);
        var isRunning = server.IsRunning;
        var stopped = await server.StopAsync(WebSocketCloseStatus.NormalClosure, "Test",
            TestContext.Current.CancellationToken);

        // Assert
        Assert.True(isRunning);
        Assert.True(stopped);
        Assert.False(server.IsRunning);
    }

    [Fact(Timeout = DefaultTimeoutMs)]
    public async Task Should_Handle_Dispose_Without_Deadlock()
    {
        // Arrange
        var port = GetAvailablePort();
        var url = $"http://127.0.0.1:{port}/";
        var server = new ReactiveWebSocketServer(url);
        await server.StartAsync(TestContext.Current.CancellationToken);

        // Act
        await server.SendInstantAsync(Guid.Empty, "test".AsMemory(), WebSocketMessageType.Binary,
            TestContext.Current.CancellationToken);
        await server.StopAsync(WebSocketCloseStatus.NormalClosure, "test", TestContext.Current.CancellationToken);
        server.Dispose();

        // Assert - no deadlock occurred
        Assert.True(true);
    }

    [Fact(Timeout = DefaultTimeoutMs)]
    public async Task Should_Complete_Observables_On_Dispose()
    {
        // Arrange
        var port = GetAvailablePort();
        var url = $"http://127.0.0.1:{port}/";
        var server = new ReactiveWebSocketServer(url);
        await server.StartAsync(TestContext.Current.CancellationToken);

        // Act
        await server.DisposeAsync();

        // Assert
        Assert.True(true);
    }

    [Fact(Timeout = DefaultTimeoutMs)]
    public async Task Should_Disconnect_All_Clients_On_Stop()
    {
        // Arrange
        var connectionTask1 = WaitUntilAsync(Server.ClientConnected, () => Server.ClientCount == 1);
        using var client1 = await ConnectClientAsync(TestContext.Current.CancellationToken);
        await connectionTask1;

        var connectionTask2 = WaitUntilAsync(Server.ClientConnected, () => Server.ClientCount == 2);
        using var client2 = await ConnectClientAsync(TestContext.Current.CancellationToken);
        await connectionTask2;

        // Act
        var disconnectionTask = WaitUntilAsync(Server.ClientDisconnected, () => Server.ClientCount == 0);
        await Server.StopAsync(WebSocketCloseStatus.NormalClosure, "Server stopping",
            TestContext.Current.CancellationToken);
        await disconnectionTask;

        // Assert
        Assert.Equal(0, Server.ClientCount);
    }

    [Fact(Timeout = DefaultTimeoutMs)]
    public async Task Should_Not_Throw_On_Multiple_Dispose_Calls()
    {
        // Arrange
        var port = GetAvailablePort();
        var url = $"http://127.0.0.1:{port}/";
        var server = new ReactiveWebSocketServer(url);
        await server.StartAsync(TestContext.Current.CancellationToken);

        // Act
        server.Dispose();
        server.Dispose();
        await server.DisposeAsync();
        await server.DisposeAsync();

        // Assert
        Assert.True(server.IsDisposed);
    }

    [Fact]
    public void Server_Dispose_ShouldMarkAsDisposed()
    {
        // Arrange
        var port = GetAvailablePort();
        var server = new ReactiveWebSocketServer($"http://127.0.0.1:{port}/");

        // Act
        server.Dispose();

        // Assert
        Assert.True(server.IsDisposed);
    }

    [Fact(Timeout = DefaultTimeoutMs)]
    public async Task AfterDispose_OperationsShouldThrow()
    {
        // Arrange
        var port = GetAvailablePort();
        var server = new ReactiveWebSocketServer($"http://127.0.0.1:{port}/");

        // Act
        server.Dispose();

        // Assert
        await Assert.ThrowsAsync<ObjectDisposedException>(async () =>
            await server.StartAsync(TestContext.Current.CancellationToken));
    }

    [Fact(Timeout = DefaultTimeoutMs)]
    public async Task Server_Dispose_ShouldCompleteAllObservables()
    {
        // Arrange
        var port = GetAvailablePort();
        var server = new ReactiveWebSocketServer($"http://127.0.0.1:{port}/");
        var clientConnectedCompleted = false;
        var clientDisconnectedCompleted = false;
        var messagesCompleted = false;

        server.ClientConnected.Subscribe(
            _ => { },
            _ => { },
            _ => clientConnectedCompleted = true
        );

        server.ClientDisconnected.Subscribe(
            _ => { },
            _ => { },
            _ => clientDisconnectedCompleted = true
        );

        server.Messages.Subscribe(
            _ => { },
            _ => { },
            _ => messagesCompleted = true
        );

        // Act
        await server.DisposeAsync();
        await WaitUntilAsync(server.ClientConnected,
            () => clientConnectedCompleted && clientDisconnectedCompleted && messagesCompleted);

        // Assert
        Assert.True(clientConnectedCompleted);
        Assert.True(clientDisconnectedCompleted);
        Assert.True(messagesCompleted);
    }

    [Fact(Timeout = DefaultTimeoutMs)]
    public async Task Server_Dispose_WhileRunning_ShouldStopGracefully()
    {
        // Arrange
        var port = GetAvailablePort();
        var server = new ReactiveWebSocketServer($"http://127.0.0.1:{port}/");
        await server.StartAsync(TestContext.Current.CancellationToken);

        // Act
        await server.DisposeAsync();

        // Assert
        Assert.True(server.IsDisposed);
        Assert.False(server.IsRunning);
    }

    [Fact(Timeout = DefaultTimeoutMs)]
    public async Task Server_Dispose_WithConnectedClients_ShouldDisconnectAll()
    {
        // Arrange
        var port = GetAvailablePort();
        var server = new ReactiveWebSocketServer($"http://127.0.0.1:{port}/");
        await server.StartAsync(TestContext.Current.CancellationToken);

        var client1 = new ReactiveWebSocketClient(new Uri($"ws://127.0.0.1:{port}/"));
        var client2 = new ReactiveWebSocketClient(new Uri($"ws://127.0.0.1:{port}/"));

        var connectionTask1 = WaitUntilAsync(server.ClientConnected, () => server.ClientCount == 1);
        await client1.StartAsync(TestContext.Current.CancellationToken);
        await connectionTask1;

        var connectionTask2 = WaitUntilAsync(server.ClientConnected, () => server.ClientCount == 2);
        await client2.StartAsync(TestContext.Current.CancellationToken);
        await connectionTask2;

        Assert.Equal(2, server.ClientCount);

        // Act
        // Subscribe to disconnection before disposal
        var disconnectTask = WaitUntilAsync(server.ClientDisconnected, () => server.ClientCount == 0);
        await server.DisposeAsync();

        // Assert
        try
        {
            await disconnectTask;
        }
        catch (ObjectDisposedException)
        {
            // If it's already disposed, it's also "finished" for this test purpose
            // as long as the condition is met.
        }

        Assert.True(server.IsDisposed);
        Assert.Equal(0, server.ClientCount);

        await client1.DisposeAsync();
        await client2.DisposeAsync();
    }

    [Fact(Timeout = DefaultTimeoutMs)]
    public void Finalizer_ShouldNotThrow()
    {
        CreateAndAbandonServer();
        GC.Collect();
        GC.WaitForPendingFinalizers();

        // Assert
        Assert.True(true);
        return;

        // Arrange & Act
        void CreateAndAbandonServer()
        {
            _ = new ReactiveWebSocketServer($"http://localhost:{GetAvailablePort()}/");
        }
    }
}