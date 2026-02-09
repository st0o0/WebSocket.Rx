using System.Net.WebSockets;
using WebSocket.Rx.Tests.Internal;

namespace WebSocket.Rx.Tests;

public class ReactiveWebSocketServerBroadcastTests(ITestOutputHelper output) : ReactiveWebSocketServerTestBase(output)
{
    [Fact(Timeout = DefaultTimeoutMs)]
    public async Task BroadcastInstantAsync_WithMultipleClients_ShouldSendToAll()
    {
        // Arrange
        using var client1 = await ConnectClientAsync(TestContext.Current.CancellationToken);
        using var client2 = await ConnectClientAsync(TestContext.Current.CancellationToken);
        await WaitForConditionAsync(() => Server.ClientCount == 2);

        var receiveTask1 = ReceiveTextAsync(client1, TestContext.Current.CancellationToken);
        var receiveTask2 = ReceiveTextAsync(client2, TestContext.Current.CancellationToken);

        // Act
        await Server.BroadcastInstantAsync("Broadcast Message", TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("Broadcast Message", await receiveTask1);
        Assert.Equal("Broadcast Message", await receiveTask2);
    }

    [Fact(Timeout = DefaultTimeoutMs)]
    public async Task BroadcastInstantAsync_WithNoClients_ShouldReturnTrue()
    {
        // Act
        var result = await Server.BroadcastInstantAsync("test", TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result);
    }

    [Fact(Timeout = DefaultTimeoutMs)]
    public async Task BroadcastInstantAsync_WithSingleClient_ShouldSendCorrectly()
    {
        // Arrange
        using var client = await ConnectClientAsync(TestContext.Current.CancellationToken);
        await WaitForConditionAsync(() => Server.ClientCount == 1);

        // Act
        await Server.BroadcastInstantAsync("Single Broadcast", TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("Single Broadcast", await ReceiveTextAsync(client, TestContext.Current.CancellationToken));
    }

    [Fact(Timeout = DefaultTimeoutMs)]
    public async Task BroadcastInstantAsync_ShouldRunInParallel()
    {
        // Arrange
        var clients = new List<ClientWebSocket>();
        for (var i = 0; i < 5; i++)
        {
            clients.Add(await ConnectClientAsync(TestContext.Current.CancellationToken));
        }

        await WaitForConditionAsync(() => Server.ClientCount == 5);

        // Act
        await Server.BroadcastInstantAsync("Parallel Broadcast", TestContext.Current.CancellationToken);

        // Assert
        var receiveTasks = clients.Select(c => ReceiveTextAsync(c, TestContext.Current.CancellationToken)).ToList();
        var results = await Task.WhenAll(receiveTasks);
        Assert.All(results, r => Assert.Equal("Parallel Broadcast", r));

        foreach (var c in clients) c.Dispose();
    }

    [Fact(Timeout = DefaultTimeoutMs)]
    public async Task BroadcastInstantAsync_ByteArray_ShouldSendToAll()
    {
        // Arrange
        using var client1 = await ConnectClientAsync(TestContext.Current.CancellationToken);
        using var client2 = await ConnectClientAsync(TestContext.Current.CancellationToken);
        await WaitForConditionAsync(() => Server.ClientCount == 2);
        var binaryData = new byte[] { 10, 20, 30 };

        // Act
        await Server.BroadcastInstantAsync(binaryData, TestContext.Current.CancellationToken);

        // Assert
        var buffer1 = new byte[1024];
        var result1 = await client1.ReceiveAsync(buffer1, TestContext.Current.CancellationToken);
        Assert.Equal(binaryData, buffer1.Take(result1.Count));

        var buffer2 = new byte[1024];
        var result2 = await client2.ReceiveAsync(buffer2, TestContext.Current.CancellationToken);
        Assert.Equal(binaryData, buffer2.Take(result2.Count));
    }

    [Fact(Timeout = DefaultTimeoutMs)]
    public async Task BroadcastAsBinaryAsync_WithMultipleClients_ShouldSendToAll()
    {
        // Arrange
        using var client1 = await ConnectClientAsync(TestContext.Current.CancellationToken);
        using var client2 = await ConnectClientAsync(TestContext.Current.CancellationToken);
        await WaitForConditionAsync(() => Server.ClientCount == 2);

        // Act
        await Server.BroadcastAsBinaryAsync("Binary Broadcast", TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("Binary Broadcast", await ReceiveTextAsync(client1, TestContext.Current.CancellationToken));
        Assert.Equal("Binary Broadcast", await ReceiveTextAsync(client2, TestContext.Current.CancellationToken));
    }

    [Fact(Timeout = DefaultTimeoutMs)]
    public async Task BroadcastAsTextAsync_WithMultipleClients_ShouldSendToAll()
    {
        // Arrange
        using var client1 = await ConnectClientAsync(TestContext.Current.CancellationToken);
        using var client2 = await ConnectClientAsync(TestContext.Current.CancellationToken);
        await WaitForConditionAsync(() => Server.ClientCount == 2);

        // Act
        await Server.BroadcastAsTextAsync("Text Broadcast", TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("Text Broadcast", await ReceiveTextAsync(client1, TestContext.Current.CancellationToken));
        Assert.Equal("Text Broadcast", await ReceiveTextAsync(client2, TestContext.Current.CancellationToken));
    }

    [Fact(Timeout = DefaultTimeoutMs)]
    public async Task BroadcastInstantAsync_ToManyClients_ShouldComplete()
    {
        // Arrange
        var clients = new List<ClientWebSocket>();
        const int clientCount = 50;
        for (var i = 0; i < clientCount; i++)
        {
            clients.Add(await ConnectClientAsync(TestContext.Current.CancellationToken));
        }

        await WaitForConditionAsync(() => Server.ClientCount == clientCount);

        // Act
        var result = await Server.BroadcastInstantAsync("Mass Broadcast", TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result);
        var receiveTasks = clients.Select(c => ReceiveTextAsync(c, TestContext.Current.CancellationToken));
        var results = await Task.WhenAll(receiveTasks);
        Assert.All(results, r => Assert.Equal("Mass Broadcast", r));

        foreach (var c in clients) c.Dispose();
    }
}