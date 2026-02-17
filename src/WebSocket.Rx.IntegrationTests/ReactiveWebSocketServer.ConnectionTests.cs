using System.Net.WebSockets;
using WebSocket.Rx.IntegrationTests.Internal;

namespace WebSocket.Rx.IntegrationTests;

public class ReactiveWebSocketServerConnectionTests(ITestOutputHelper output) : ReactiveWebSocketServerTestBase(output)
{
    [Fact(Timeout = DefaultTimeoutMs)]
    public async Task Should_Accept_Client_Connection()
    {
        // Arrange & Act
        var connectionTask = WaitUntilAsync(Server.ClientConnected, () => Server.ClientCount == 1);
        using var client = await ConnectClientAsync(TestContext.Current.CancellationToken);
        await connectionTask;

        // Assert
        Assert.Equal(1, Server.ClientCount);
    }

    [Fact(Timeout = DefaultTimeoutMs)]
    public async Task Should_Handle_Multiple_Clients()
    {
        // Arrange & Act
        var connectionTask1 = WaitUntilAsync(Server.ClientConnected, () => Server.ClientCount >= 1);
        using var client1 = await ConnectClientAsync(TestContext.Current.CancellationToken);
        await connectionTask1;
        
        var connectionTask2 = WaitUntilAsync(Server.ClientConnected, () => Server.ClientCount >= 2);
        using var client2 = await ConnectClientAsync(TestContext.Current.CancellationToken);
        await connectionTask2;

        // Assert
        Assert.Equal(2, Server.ClientCount);
    }

    [Fact(Timeout = DefaultTimeoutMs)]
    public async Task Should_Detect_Client_Disconnect()
    {
        // Arrange
        var connectionTask = WaitUntilAsync(Server.ClientConnected, () => Server.ClientCount == 1);
        using (var client = await ConnectClientAsync(TestContext.Current.CancellationToken))
        {
            await connectionTask;
        }

        // Act
        await WaitUntilAsync(Server.ClientDisconnected, () => Server.ClientCount == 0);

        // Assert
        Assert.Equal(0, Server.ClientCount);
    }

    [Fact(Timeout = DefaultTimeoutMs)]
    public async Task Should_Reject_Non_WebSocket_Requests()
    {
        // Arrange
        using var client = new HttpClient();

        // Act & Assert
        await Assert.ThrowsAnyAsync<Exception>(() =>
            client.GetStringAsync(ServerUrl, TestContext.Current.CancellationToken));

        // Note: No event will be fired for non-websocket requests as they are rejected before connection
        await Task.Delay(100, TestContext.Current.CancellationToken);
        Assert.Equal(0, Server.ClientCount);
    }

    [Fact(Timeout = DefaultTimeoutMs)]
    public async Task Should_Handle_Unexpected_Client_Disconnect()
    {
        // Arrange
        var connectionTask = WaitUntilAsync(Server.ClientConnected, () => Server.ClientCount == 1);
        var client = await ConnectClientAsync(TestContext.Current.CancellationToken);
        await connectionTask;

        // Act
        client.Abort();

        // Assert
        await WaitUntilAsync(Server.ClientDisconnected, () => Server.ClientCount == 0);
        Assert.True(true);
    }

    [Fact(Timeout = DefaultTimeoutMs)]
    public async Task Should_Track_Connected_Clients()
    {
        // Arrange & Act
        var connectionTask1 = WaitUntilAsync(Server.ClientConnected, () => Server.ClientCount >= 1);
        using var client1 = await ConnectClientAsync(TestContext.Current.CancellationToken);
        await connectionTask1;
        
        var connectionTask2 = WaitUntilAsync(Server.ClientConnected, () => Server.ClientCount >= 2);
        using var client2 = await ConnectClientAsync(TestContext.Current.CancellationToken);
        await connectionTask2;

        // Assert
        var connectedClients = Server.ConnectedClients;
        Assert.Equal(2, connectedClients.Count);
        Assert.All(connectedClients.Values, Assert.NotNull);
    }

    [Fact(Timeout = DefaultTimeoutMs)]
    public async Task Should_Update_ClientCount_Correctly()
    {
        // Arrange
        Assert.Equal(0, Server.ClientCount);

        // Act
        var connectionTask1 = WaitUntilAsync(Server.ClientConnected, () => Server.ClientCount == 1);
        using var client1 = await ConnectClientAsync(TestContext.Current.CancellationToken);
        await connectionTask1;

        var connectionTask2 = WaitUntilAsync(Server.ClientConnected, () => Server.ClientCount == 2);
        using var client2 = await ConnectClientAsync(TestContext.Current.CancellationToken);
        await connectionTask2;

        client1.Dispose();
        await WaitUntilAsync(Server.ClientDisconnected, () => Server.ClientCount == 1);

        client2.Dispose();
        await WaitUntilAsync(Server.ClientDisconnected, () => Server.ClientCount == 0);

        // Assert
        Assert.Equal(0, Server.ClientCount);
    }

    [Fact(Timeout = DefaultTimeoutMs)]
    public async Task Should_Handle_Rapid_Connect_Disconnect()
    {
        // Act
        for (var i = 0; i < 10; i++)
        {
            var connectionTask = WaitUntilAsync(Server.ClientConnected, () => Server.ClientCount == 1);
            using var client = await ConnectClientAsync(TestContext.Current.CancellationToken);
            await connectionTask;
        }

        // Assert
        await WaitUntilAsync(Server.ClientDisconnected, () => Server.ClientCount == 0);
        Assert.Equal(0, Server.ClientCount);
    }

    [Fact(Timeout = DefaultTimeoutMs)]
    public async Task Should_Handle_10_Concurrent_Clients()
    {
        // Arrange
        var clients = new List<ClientWebSocket>();
        try
        {
            // Act
            for (var i = 0; i < 10; i++)
            {
                var connectionTask = WaitUntilAsync(Server.ClientConnected, () => Server.ClientCount >= i + 1);
                clients.Add(await ConnectClientAsync(TestContext.Current.CancellationToken));
                await connectionTask;
            }

            // Assert
            Assert.Equal(10, Server.ClientCount);
        }
        finally
        {
            foreach (var c in clients)
            {
                c.Dispose();
            }
        }
    }
}
        