using WebSocket.Rx.Tests.Internal;

namespace WebSocket.Rx.Tests;

public class ReactiveWebSocketServerSendingTests(ITestOutputHelper output) : ReactiveWebSocketServerTestBase(output)
{
    [Fact(Timeout = DefaultTimeoutMs)]
    public async Task Should_Send_Text_To_Specific_Client()
    {
        // Arrange
        using var client = await ConnectClientAsync(TestContext.Current.CancellationToken);
        await WaitForConditionAsync(() => Server.ClientCount == 1);
        var clientId = Server.ConnectedClients.Keys.First();

        // Act
        await Server.SendInstantAsync(clientId, "Hello Client", TestContext.Current.CancellationToken);

        // Assert
        var received = await ReceiveTextAsync(client, TestContext.Current.CancellationToken);
        Assert.Equal("Hello Client", received);
    }

    [Fact(Timeout = DefaultTimeoutMs)]
    public async Task Should_Send_Binary_To_Specific_Client()
    {
        // Arrange
        using var client = await ConnectClientAsync(TestContext.Current.CancellationToken);
        await WaitForConditionAsync(() => Server.ClientCount == 1);
        var clientId = Server.ConnectedClients.Keys.First();
        var binaryData = new byte[] { 5, 4, 3, 2, 1 };

        // Act
        await Server.SendInstantAsync(clientId, binaryData, TestContext.Current.CancellationToken);

        // Assert
        var buffer = new byte[1024];
        var result = await client.ReceiveAsync(buffer, CancellationToken.None);
        Assert.Equal(binaryData, buffer.Take(result.Count));
    }

    [Fact(Timeout = DefaultTimeoutMs)]
    public async Task Should_Return_False_When_Sending_To_Non_Existent_Client()
    {
        // Act
        var result = await Server.SendInstantAsync(Guid.NewGuid(), "test", TestContext.Current.CancellationToken);

        // Assert
        Assert.False(result);
    }

    [Fact(Timeout = DefaultTimeoutMs)]
    public async Task Should_Send_Multiple_Messages_To_Same_Client()
    {
        // Arrange
        using var client = await ConnectClientAsync(TestContext.Current.CancellationToken);
        await WaitForConditionAsync(() => Server.ClientCount == 1);
        var clientId = Server.ConnectedClients.Keys.First();

        // Act
        await Server.SendInstantAsync(clientId, "Msg 1", TestContext.Current.CancellationToken);
        await Server.SendInstantAsync(clientId, "Msg 2", TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("Msg 1", await ReceiveTextAsync(client, TestContext.Current.CancellationToken));
        Assert.Equal("Msg 2", await ReceiveTextAsync(client, TestContext.Current.CancellationToken));
    }

    [Fact(Timeout = DefaultTimeoutMs)]
    public async Task TrySendAsText_Should_Return_True_For_Existing_Client()
    {
        // Arrange
        using var client = await ConnectClientAsync(TestContext.Current.CancellationToken);
        await WaitForConditionAsync(() => Server.ClientCount == 1);
        var clientId = Server.ConnectedClients.Keys.First();

        // Act
        var result = Server.TrySendAsText(clientId, "test");

        // Assert
        Assert.True(result);
        Assert.Equal("test", await ReceiveTextAsync(client, TestContext.Current.CancellationToken));
    }

    [Fact]
    public void TrySendAsText_Should_Return_False_For_Non_Existent_Client()
    {
        // Act
        var result = Server.TrySendAsText(Guid.NewGuid(), "test");

        // Assert
        Assert.False(result);
    }

    [Fact(Timeout = DefaultTimeoutMs)]
    public async Task TrySendAsBinary_Should_Return_True_For_Existing_Client()
    {
        // Arrange
        using var client = await ConnectClientAsync(TestContext.Current.CancellationToken);
        await WaitForConditionAsync(() => Server.ClientCount == 1);
        var clientId = Server.ConnectedClients.Keys.First();

        // Act
        var result = Server.TrySendAsBinary(clientId, "test");

        // Assert
        Assert.True(result);
        Assert.Equal("test", await ReceiveTextAsync(client, TestContext.Current.CancellationToken));
    }

    [Fact(Timeout = DefaultTimeoutMs)]
    public async Task SendInstantAsync_Should_Send_Message_Immediately()
    {
        // Arrange
        using var client = await ConnectClientAsync(TestContext.Current.CancellationToken);
        await WaitForConditionAsync(() => Server.ClientCount == 1);
        var clientId = Server.ConnectedClients.Keys.First();

        // Act
        await Server.SendInstantAsync(clientId, "Instant message", TestContext.Current.CancellationToken);

        // Assert
        var received = await ReceiveTextAsync(client, TestContext.Current.CancellationToken);
        Assert.Equal("Instant message", received);
    }

    [Fact(Timeout = DefaultTimeoutMs)]
    public async Task Should_Handle_Client_Send_After_Disconnect()
    {
        // Arrange
        var client = await ConnectClientAsync(TestContext.Current.CancellationToken);
        await WaitForConditionAsync(() => Server.ClientCount == 1);
        var clientId = Server.ConnectedClients.Keys.First();
        client.Dispose();
        await WaitForConditionAsync(() => Server.ClientCount == 0);

        // Act
        var result = await Server.SendInstantAsync(clientId, "test", TestContext.Current.CancellationToken);

        // Assert
        Assert.False(result);
    }
}
