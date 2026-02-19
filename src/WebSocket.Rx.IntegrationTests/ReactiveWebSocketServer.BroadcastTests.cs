using System.Net.WebSockets;
using WebSocket.Rx.IntegrationTests.Internal;

namespace WebSocket.Rx.IntegrationTests;

public class ReactiveWebSocketServerBroadcastTests(ITestOutputHelper output) : ReactiveWebSocketServerTestBase(output)
{
    [Fact(Timeout = DefaultTimeoutMs)]
    public async Task BroadcastInstantAsync_WithMultipleClients_ShouldSendToAll()
    {
        // Arrange
        using var client1 = await ConnectClientAsync(TestContext.Current.CancellationToken);
        var connectionTask = WaitForEventAsync(Server.ClientConnected);
        using var client2 = await ConnectClientAsync(TestContext.Current.CancellationToken);
        await connectionTask;

        var receiveTask1 = ReceiveTextAsync(client1, TestContext.Current.CancellationToken);
        var receiveTask2 = ReceiveTextAsync(client2, TestContext.Current.CancellationToken);

        // Act
        await Server.BroadcastInstantAsync("Broadcast Message".AsMemory(), WebSocketMessageType.Binary,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("Broadcast Message", await receiveTask1);
        Assert.Equal("Broadcast Message", await receiveTask2);
    }

    [Fact(Timeout = DefaultTimeoutMs)]
    public async Task BroadcastInstantAsync_WithNoClients_ShouldReturnTrue()
    {
        // Act
        var result = await Server.BroadcastInstantAsync("test".AsMemory(), WebSocketMessageType.Binary,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result);
    }

    [Fact(Timeout = DefaultTimeoutMs)]
    public async Task BroadcastInstantAsync_WithSingleClient_ShouldSendCorrectly()
    {
        // Arrange
        var connectionTask = WaitUntilAsync(Server.ClientConnected, () => Server.ClientCount == 1);
        using var client = await ConnectClientAsync(TestContext.Current.CancellationToken);
        await connectionTask;

        // Act
        await Server.BroadcastInstantAsync("Single Broadcast".AsMemory(), WebSocketMessageType.Binary,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("Single Broadcast", await ReceiveTextAsync(client, TestContext.Current.CancellationToken));
    }


    [Fact(Timeout = DefaultTimeoutMs)]
    public async Task BroadcastInstantAsync_ByteArray_ShouldSendToAll()
    {
        // Arrange
        using var client1 = await ConnectClientAsync(TestContext.Current.CancellationToken);
        var connectionTask = WaitForEventAsync(Server.ClientConnected);
        using var client2 = await ConnectClientAsync(TestContext.Current.CancellationToken);
        await connectionTask;
        var binaryData = new byte[] { 10, 20, 30 };

        // Act
        await Server.BroadcastInstantAsync(binaryData, WebSocketMessageType.Binary,
            TestContext.Current.CancellationToken);

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
        var connectionTask = WaitForEventAsync(Server.ClientConnected);
        using var client2 = await ConnectClientAsync(TestContext.Current.CancellationToken);
        await connectionTask;

        // Act
        await Server.BroadcastAsync("Binary Broadcast".AsMemory(), WebSocketMessageType.Binary,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("Binary Broadcast", await ReceiveTextAsync(client1, TestContext.Current.CancellationToken));
        Assert.Equal("Binary Broadcast", await ReceiveTextAsync(client2, TestContext.Current.CancellationToken));
    }

    [Fact(Timeout = DefaultTimeoutMs)]
    public async Task BroadcastAsTextAsync_WithMultipleClients_ShouldSendToAll()
    {
        // Arrange
        using var client1 = await ConnectClientAsync(TestContext.Current.CancellationToken);
        var connectionTask = WaitForEventAsync(Server.ClientConnected);
        using var client2 = await ConnectClientAsync(TestContext.Current.CancellationToken);
        await connectionTask;

        // Act
        await Server.BroadcastAsync("Text Broadcast".AsMemory(), WebSocketMessageType.Text,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("Text Broadcast", await ReceiveTextAsync(client1, TestContext.Current.CancellationToken));
        Assert.Equal("Text Broadcast", await ReceiveTextAsync(client2, TestContext.Current.CancellationToken));
    }
}