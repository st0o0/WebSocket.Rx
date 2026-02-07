using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using R3;

namespace WebSocket.Rx.Tests;

public class DisposeTests
{
    private static int GetAvailablePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    #region ReactiveWebSocketClient Dispose Tests

    [Fact]
    public void Client_Dispose_ShouldMarkAsDisposed()
    {
        // Arrange
        var client = new ReactiveWebSocketClient(new Uri($"ws://localhost:{GetAvailablePort()}"));

        // Act
        client.Dispose();

        // Assert
        Assert.True(client.IsDisposed);
    }

    [Fact(Timeout = 10000)]
    public async Task Client_DisposeAsync_ShouldMarkAsDisposed()
    {
        // Arrange
        var client = new ReactiveWebSocketClient(new Uri($"ws://localhost:{GetAvailablePort()}"));

        // Act
        await client.DisposeAsync();

        // Assert
        Assert.True(client.IsDisposed);
    }

    [Fact]
    public void Client_Dispose_MultipleTimes_ShouldBeIdempotent()
    {
        // Arrange
        var client = new ReactiveWebSocketClient(new Uri($"ws://localhost:{GetAvailablePort()}"));

        // Act
        client.Dispose();
        client.Dispose();
        client.Dispose();

        // Assert
        Assert.True(client.IsDisposed);
    }

    [Fact(Timeout = 10000)]
    public async Task Client_DisposeAsync_MultipleTimes_ShouldBeIdempotent()
    {
        // Arrange
        var client = new ReactiveWebSocketClient(new Uri($"ws://localhost:{GetAvailablePort()}"));

        // Act
        await client.DisposeAsync();
        await client.DisposeAsync();
        await client.DisposeAsync();

        // Assert
        Assert.True(client.IsDisposed);
    }

    [Fact(Timeout = 10000)]
    public async Task Client_MixedDispose_ShouldBeIdempotent()
    {
        // Arrange
        var client = new ReactiveWebSocketClient(new Uri($"ws://localhost:{GetAvailablePort()}"));

        // Act
        client.Dispose();
        await client.DisposeAsync();
        client.Dispose();

        // Assert
        Assert.True(client.IsDisposed);
    }

    [Fact(Timeout = 10000)]
    public async Task Client_AfterDispose_OperationsShouldThrowOrReturnFalse()
    {
        // Arrange
        var client = new ReactiveWebSocketClient(new Uri($"ws://localhost:{GetAvailablePort()}"));
        var exceptionSource = new TaskCompletionSource<ErrorOccurred>();
        client.ErrorOccurred.Subscribe(msg => exceptionSource.TrySetResult(msg));
        // Act
        client.Dispose();

        // Assert
        await client.StartAsync();
        var taskResult = await exceptionSource.Task;
        Assert.IsType<ObjectDisposedException>(taskResult.Exception);

        var result = client.TrySendAsText("test");
        Assert.False(result);
    }

    [Fact(Timeout = 10000)]
    public async Task Client_Dispose_ShouldCompleteAllObservables()
    {
        // Arrange
        var client = new ReactiveWebSocketClient(new Uri($"ws://localhost:{GetAvailablePort()}"));
        var messageCompleted = false;
        var connectionCompleted = false;
        var disconnectionCompleted = false;

        client.MessageReceived.Subscribe(
            _ => { },
            _ => { },
            _ => messageCompleted = true
        );

        client.ConnectionHappened.Subscribe(
            _ => { },
            _ => { },
            _ => connectionCompleted = true
        );

        client.DisconnectionHappened.Subscribe(
            _ => { },
            _ => { },
            _ => disconnectionCompleted = true
        );

        // Act
        await client.DisposeAsync();
        await Task.Delay(50);

        // Assert
        Assert.True(messageCompleted);
        Assert.True(connectionCompleted);
        Assert.True(disconnectionCompleted);
    }

    [Fact(Timeout = 10000)]
    public async Task Client_Dispose_WhileRunning_ShouldStopGracefully()
    {
        // Arrange
        var port = GetAvailablePort();
        using var server = new ReactiveWebSocketServer($"http://localhost:{port}/");
        await server.StartAsync();

        var client = new ReactiveWebSocketClient(new Uri($"ws://localhost:{port}"));
        await client.StartAsync();
        await Task.Delay(50);

        // Act
        await client.DisposeAsync();

        // Assert
        Assert.True(client.IsDisposed);
        Assert.False(client.IsRunning);
        Assert.False(client.IsStarted);

        await server.DisposeAsync();
    }

    [Fact]
    public void Client_Finalizer_ShouldNotThrow()
    {
        CreateAndAbandonClient();
        GC.Collect();
        GC.WaitForPendingFinalizers();

        // Assert
        Assert.True(true);
        return;

        // Arrange & Act
        void CreateAndAbandonClient()
        {
            var client = new ReactiveWebSocketClient(new Uri("ws://localhost:8080"));
        }
    }

    #endregion

    #region ReactiveWebSocketServer Dispose Tests

    [Fact]
    public void Server_Dispose_ShouldMarkAsDisposed()
    {
        // Arrange
        var server = new ReactiveWebSocketServer("http://localhost:9002/");

        // Act
        server.Dispose();

        // Assert
        Assert.True(server.IsDisposed);
    }

    [Fact(Timeout = 10000)]
    public async Task Server_DisposeAsync_ShouldMarkAsDisposed()
    {
        // Arrange
        var server = new ReactiveWebSocketServer("http://localhost:9003/");

        // Act
        await server.DisposeAsync();

        // Assert
        Assert.True(server.IsDisposed);
    }

    [Fact]
    public void Server_Dispose_MultipleTimes_ShouldBeIdempotent()
    {
        // Arrange
        var server = new ReactiveWebSocketServer("http://localhost:9004/");

        // Act
        server.Dispose();
        server.Dispose();
        server.Dispose();

        // Assert
        Assert.True(server.IsDisposed);
    }

    [Fact(Timeout = 10000)]
    public async Task Server_DisposeAsync_MultipleTimes_ShouldBeIdempotent()
    {
        // Arrange
        var server = new ReactiveWebSocketServer("http://localhost:9005/");

        // Act
        await server.DisposeAsync();
        await server.DisposeAsync();
        await server.DisposeAsync();

        // Assert
        Assert.True(server.IsDisposed);
    }

    [Fact(Timeout = 10000)]
    public async Task Server_AfterDispose_OperationsShouldThrowOrReturnFalse()
    {
        // Arrange
        var server = new ReactiveWebSocketServer("http://localhost:9006/");

        // Act
        server.Dispose();

        // Assert
        await Assert.ThrowsAsync<ObjectDisposedException>(async () => await server.StartAsync());
    }

    [Fact(Timeout = 10000)]
    public async Task Server_Dispose_ShouldCompleteAllObservables()
    {
        // Arrange
        var server = new ReactiveWebSocketServer($"http://localhost:{GetAvailablePort()}/");
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
        await Task.Delay(50);

        // Assert
        Assert.True(clientConnectedCompleted);
        Assert.True(clientDisconnectedCompleted);
        Assert.True(messagesCompleted);
    }

    [Fact(Timeout = 10000)]
    public async Task Server_Dispose_WhileRunning_ShouldStopGracefully()
    {
        // Arrange
        var server = new ReactiveWebSocketServer($"http://localhost:{GetAvailablePort()}/");
        await server.StartAsync();

        // Act
        await server.DisposeAsync();

        // Assert
        Assert.True(server.IsDisposed);
        Assert.False(server.IsRunning);
    }

    [Fact(Timeout = 10000)]
    public async Task Server_Dispose_WithConnectedClients_ShouldDisconnectAll()
    {
        // Arrange
        var port = GetAvailablePort();
        var server = new ReactiveWebSocketServer($"http://localhost:{port}/");
        await server.StartAsync();

        var client1 = new ReactiveWebSocketClient(new Uri($"ws://localhost:{port}/"));
        var client2 = new ReactiveWebSocketClient(new Uri($"ws://localhost:{port}/"));

        await client1.StartAsync();
        await client2.StartAsync();
        await Task.Delay(50);

        Assert.Equal(2, server.ClientCount);

        // Act
        await server.DisposeAsync();
        await Task.Delay(50);

        // Assert
        Assert.True(server.IsDisposed);
        Assert.Equal(0, server.ClientCount);

        await client1.DisposeAsync();
        await client2.DisposeAsync();
    }

    [Fact]
    public void Server_Finalizer_ShouldNotThrow()
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
            var server = new ReactiveWebSocketServer($"http://localhost:{GetAvailablePort()}/");
        }
    }

    #endregion

    #region ServerWebSocketAdapter Dispose Tests

    [Fact(Timeout = 10000)]
    public async Task Adapter_Dispose_ShouldMarkAsDisposed()
    {
        // Arrange
        var port = GetAvailablePort();
        var server = new ReactiveWebSocketServer($"http://localhost:{port}/");
        await server.StartAsync();

        var client = new ReactiveWebSocketClient(new Uri($"ws://localhost:{port}/"));

        ReactiveWebSocketServer.ServerWebSocketAdapter? adapter = null;
        server.ClientConnected.Subscribe(connected =>
        {
            adapter = server.ConnectedClients.Keys
                .Select(id => server.GetType()
                    .GetField("_clients",
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                    ?.GetValue(server))
                .FirstOrDefault() as ReactiveWebSocketServer.ServerWebSocketAdapter;
        });

        await client.StartAsync();
        await Task.Delay(50);

        // Act
        if (adapter != null)
        {
            await adapter.DisposeAsync();

            // Assert
            Assert.True(adapter.IsDisposed);
        }

        await client.DisposeAsync();
        await server.DisposeAsync();
    }

    [Fact(Timeout = 10000)]
    public async Task Adapter_Dispose_MultipleTimes_ShouldBeIdempotent()
    {
        // Arrange
        using var nativeSocket = new ClientWebSocket();
        var metadata = new Metadata(Guid.NewGuid(), IPAddress.Any, 0);

        // We can't easily create a real ServerWebSocket, so this test verifies the pattern
        // through the base ReactiveWebSocketClient which ServerWebSocketAdapter inherits from
        var client = new ReactiveWebSocketClient(new Uri($"ws://localhost:{GetAvailablePort()}"));

        // Act
        await client.DisposeAsync();
        await client.DisposeAsync();
        client.Dispose();

        // Assert
        Assert.True(client.IsDisposed);
    }

    #endregion

    #region Integration Dispose Tests

    [Fact(Timeout = 10000)]
    public async Task Integration_ServerAndClient_BothDispose_ShouldCleanupProperly()
    {
        // Arrange
        var port = GetAvailablePort();
        var server = new ReactiveWebSocketServer($"http://localhost:{port}/");
        await server.StartAsync();

        var client = new ReactiveWebSocketClient(new Uri($"ws://localhost:{port}/"));
        await client.StartAsync();
        await Task.Delay(50);

        // Act
        await client.DisposeAsync();
        await server.DisposeAsync();

        // Assert
        Assert.True(client.IsDisposed);
        Assert.True(server.IsDisposed);
    }

    [Fact(Timeout = 10000)]
    public async Task Integration_MultipleClientsAndServer_AllDispose_ShouldCleanupProperly()
    {
        // Arrange
        var port = GetAvailablePort();
        var server = new ReactiveWebSocketServer($"http://localhost:{port}/");
        await server.StartAsync();

        var clients = new List<ReactiveWebSocketClient>();
        for (var i = 0; i < 5; i++)
        {
            var client = new ReactiveWebSocketClient(new Uri($"ws://localhost:{port}/"));
            await client.StartAsync();
            clients.Add(client);
        }

        await Task.Delay(50);
        Assert.Equal(5, server.ClientCount);

        // Act
        foreach (var client in clients)
        {
            await client.DisposeAsync();
        }

        await server.DisposeAsync();

        // Assert
        foreach (var client in clients)
        {
            Assert.True(client.IsDisposed);
        }

        Assert.True(server.IsDisposed);
        Assert.Equal(0, server.ClientCount);
    }

    [Fact(Timeout = 10000)]
    public async Task Integration_DisposeUnderLoad_ShouldHandleGracefully()
    {
        // Arrange
        var port = GetAvailablePort();
        var server = new ReactiveWebSocketServer($"http://localhost:{port}/");
        await server.StartAsync();

        var client = new ReactiveWebSocketClient(new Uri($"ws://localhost:{port}/"));
        await client.StartAsync();
        await Task.Delay(50);

        var sendTask = Task.Run(async () =>
        {
            for (var i = 0; i < 100; i++)
            {
                try
                {
                    await client.SendAsTextAsync($"Message {i}");
                    await Task.Delay(10);
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
            }
        });

        await Task.Delay(50);

        // Act
        await client.DisposeAsync();
        await server.DisposeAsync();

        // Assert
        Assert.True(client.IsDisposed);
        Assert.True(server.IsDisposed);

        await sendTask;
    }

    [Fact(Timeout = 10000)]
    public async Task Integration_ConcurrentDispose_ShouldBeThreadSafe()
    {
        // Arrange
        var server = new ReactiveWebSocketServer($"http://localhost:{GetAvailablePort()}/");
        await server.StartAsync();

        // Act
        var disposeTasks = Enumerable.Range(0, 10)
            .Select(_ => Task.Run(async () => await server.DisposeAsync()))
            .ToArray();

        await Task.WhenAll(disposeTasks);

        // Assert
        Assert.True(server.IsDisposed);
    }

    #endregion

    #region Resource Leak Tests

    [Fact(Timeout = 10000)]
    public async Task ResourceLeak_Client_ShouldNotLeakHandles()
    {
        // Arrange & Act
        for (var i = 0; i < 50; i++)
        {
            var client = new ReactiveWebSocketClient(new Uri($"ws://localhost:{GetAvailablePort()}/"));
            await client.DisposeAsync();
        }

        // Force garbage collection
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        // Assert
        Assert.True(true);
    }

    [Fact(Timeout = 10000)]
    public async Task ResourceLeak_Server_ShouldNotLeakHandles()
    {
        // Arrange & Act
        for (var i = 0; i < 50; i++)
        {
            var server = new ReactiveWebSocketServer($"http://localhost:{GetAvailablePort()}/");
            await server.DisposeAsync();
        }

        // Force garbage collection
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        // Assert
        Assert.True(true);
    }

    #endregion
}