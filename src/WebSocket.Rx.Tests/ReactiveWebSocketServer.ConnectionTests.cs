using System.Net.WebSockets;
using WebSocket.Rx.Tests.Internal;

namespace WebSocket.Rx.Tests;

public class ReactiveWebSocketServerConnectionTests(ITestOutputHelper output) : ReactiveWebSocketServerTestBase(output)
{
    [Fact(Timeout = DefaultTimeoutMs)]
    public async Task Should_Accept_Client_Connection()
    {
        // Arrange & Act
        using var client = await ConnectClientAsync(TestContext.Current.CancellationToken);

        await WaitForConditionAsync(() => Server.ClientCount == 1,
            errorMessage: "Server should have 1 connected client");

        // Assert
        Assert.Equal(1, Server.ClientCount);
    }

    [Fact(Timeout = DefaultTimeoutMs)]
    public async Task Should_Handle_Multiple_Clients()
    {
        // Arrange & Act
        using var client1 = await ConnectClientAsync(TestContext.Current.CancellationToken);
        using var client2 = await ConnectClientAsync(TestContext.Current.CancellationToken);

        await WaitForConditionAsync(() => Server.ClientCount == 2);

        // Assert
        Assert.Equal(2, Server.ClientCount);
    }

    [Fact(Timeout = DefaultTimeoutMs)]
    public async Task Should_Detect_Client_Disconnect()
    {
        // Arrange
        using (var client = await ConnectClientAsync(TestContext.Current.CancellationToken))
        {
            await WaitForConditionAsync(() => Server.ClientCount == 1);
        }

        // Act
        await WaitForConditionAsync(() => Server.ClientCount == 0);

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

        await WaitForConditionAsync(() => Server.ClientCount == 0);
    }

    [Fact(Timeout = DefaultTimeoutMs)]
    public async Task Should_Handle_Unexpected_Client_Disconnect()
    {
        // Arrange
        var client = await ConnectClientAsync(TestContext.Current.CancellationToken);
        await WaitForConditionAsync(() => Server.ClientCount == 1);

        // Act
        client.Abort();

        // Assert
        await WaitForConditionAsync(
            () => Server.ClientCount == 0,
            errorMessage: "Client count should be 0 after unexpected disconnect");
        Assert.True(true);
    }

    [Fact(Timeout = DefaultTimeoutMs)]
    public async Task Should_Track_Connected_Clients()
    {
        // Arrange & Act
        using var client1 = await ConnectClientAsync(TestContext.Current.CancellationToken);
        using var client2 = await ConnectClientAsync(TestContext.Current.CancellationToken);

        await WaitForConditionAsync(() => Server.ClientCount == 2);

        // Assert
        var connectedClients = Server.ConnectedClients;
        Assert.Equal(2, connectedClients.Count);
        Assert.All(connectedClients.Values, metadata => Assert.NotNull(metadata));
    }

    [Fact(Timeout = DefaultTimeoutMs)]
    public async Task Should_Update_ClientCount_Correctly()
    {
        // Arrange
        Assert.Equal(0, Server.ClientCount);

        // Act
        using var client1 = await ConnectClientAsync(TestContext.Current.CancellationToken);
        await WaitForConditionAsync(() => Server.ClientCount == 1);

        using var client2 = await ConnectClientAsync(TestContext.Current.CancellationToken);
        await WaitForConditionAsync(() => Server.ClientCount == 2);

        client1.Dispose();
        await WaitForConditionAsync(() => Server.ClientCount == 1);

        client2.Dispose();
        await WaitForConditionAsync(() => Server.ClientCount == 0);

        // Assert
        Assert.Equal(0, Server.ClientCount);
    }

    [Fact(Timeout = DefaultTimeoutMs)]
    public async Task Should_Handle_Rapid_Connect_Disconnect()
    {
        // Act
        for (var i = 0; i < 10; i++)
        {
            using var client = await ConnectClientAsync(TestContext.Current.CancellationToken);
            await WaitForConditionAsync(() => Server.ClientCount == 1);
        }

        // Assert
        await WaitForConditionAsync(() => Server.ClientCount == 0);
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
                clients.Add(await ConnectClientAsync(TestContext.Current.CancellationToken));
            }

            await WaitForConditionAsync(() => Server.ClientCount == 10);

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