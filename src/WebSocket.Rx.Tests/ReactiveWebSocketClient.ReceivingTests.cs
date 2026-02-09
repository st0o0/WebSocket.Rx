using R3;
using WebSocket.Rx.Tests.Internal;

namespace WebSocket.Rx.Tests;

public class ReactiveWebSocketClientReceivingTests(ITestOutputHelper output) : ReactiveWebSocketClientTestBase(output)
{
    [Fact(Timeout = DefaultTimeoutMs)]
    public async Task MessageReceived_WhenServerSendsMessage_ShouldReceive()
    {
        // Arrange
        Client = new ReactiveWebSocketClient(new Uri(Server.WebSocketUrl));
        Client.IsTextMessageConversionEnabled = true;

        var receivedMessage = "";
        Client.MessageReceived.Subscribe(msg => receivedMessage = msg.Text ?? "");

        await Client.StartOrFailAsync(TestContext.Current.CancellationToken);
        await Task.Delay(50, TestContext.Current.CancellationToken);

        // Act
        await Server.SendToAllAsync("Server Message");
        await Task.Delay(50, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("Server Message", receivedMessage);
    }

    [Fact(Timeout = DefaultTimeoutMs)]
    public async Task MessageReceived_BinaryMessage_ShouldReceiveBinary()
    {
        // Arrange
        Client = new ReactiveWebSocketClient(new Uri(Server.WebSocketUrl));
        Client.IsTextMessageConversionEnabled = false;

        byte[]? receivedBytes = null;
        Client.MessageReceived.Subscribe(msg => receivedBytes = msg.Binary);

        await Client.StartOrFailAsync(TestContext.Current.CancellationToken);
        await Task.Delay(50, TestContext.Current.CancellationToken);

        var testData = new byte[] { 1, 2, 3 };

        // Act
        await Server.SendBinaryToAllAsync(testData);
        await Task.Delay(50, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(receivedBytes);
        Assert.Equal(testData, receivedBytes);
    }

    [Fact(Timeout = DefaultTimeoutMs)]
    public async Task StreamFakeMessage_ShouldTriggerObservable()
    {
        // Arrange
        Client = new ReactiveWebSocketClient(new Uri(Server.WebSocketUrl));
        var received = false;
        Client.MessageReceived.Subscribe(_ => received = true);

        var fakeMessage = ReceivedMessage.TextMessage("Fake");

        // Act
        Client.StreamFakeMessage(fakeMessage);
        await Task.Delay(50, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(received);
    }
}
