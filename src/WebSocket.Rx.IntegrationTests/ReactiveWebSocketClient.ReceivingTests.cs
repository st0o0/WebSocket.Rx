using R3;
using WebSocket.Rx.IntegrationTests.Internal;

namespace WebSocket.Rx.IntegrationTests;

public class ReactiveWebSocketClientReceivingTests(ITestOutputHelper output) : ReactiveWebSocketClientTestBase(output)
{
    [Fact(Timeout = DefaultTimeoutMs)]
    public async Task MessageReceived_WhenServerSendsMessage_ShouldReceive()
    {
        // Arrange
        Client = new ReactiveWebSocketClient(new Uri(Server.WebSocketUrl));
        Client.IsTextMessageConversionEnabled = true;

        var messageTask = WaitForEventAsync(Client.MessageReceived);

        await Client.StartOrFailAsync(TestContext.Current.CancellationToken);
        await WaitForConditionAsync(() => Server.ClientCount > 0);

        // Act
        await Server.SendToAllAsync("Server Message");
        var received = await messageTask;

        // Assert
        Assert.Equal("Server Message", received.Text.ToString());
    }

    [Fact(Timeout = DefaultTimeoutMs)]
    public async Task MessageReceived_BinaryMessage_ShouldReceiveBinary()
    {
        // Arrange
        Client = new ReactiveWebSocketClient(new Uri(Server.WebSocketUrl));
        Client.IsTextMessageConversionEnabled = false;

        var messageTask = WaitForEventAsync(Client.MessageReceived);

        await Client.StartOrFailAsync(TestContext.Current.CancellationToken);
        await WaitForConditionAsync(() => Server.ClientCount > 0);

        var testData = new byte[] { 1, 2, 3 };

        // Act
        await Server.SendBinaryToAllAsync(testData);
        var received = await messageTask;

        // Assert
        Assert.Equal(testData, received.Binary.ToArray());
    }

    [Fact(Timeout = DefaultTimeoutMs)]
    public async Task StreamFakeMessage_ShouldTriggerObservable()
    {
        // Arrange
        Client = new ReactiveWebSocketClient(new Uri(Server.WebSocketUrl));

        var messageTask = WaitForEventAsync(Client.MessageReceived);
        var fakeMessage = Message.Create("Fake".AsMemory());

        // Act
        Client.StreamFakeMessage(fakeMessage);
        var received = await messageTask;

        // Assert
        Assert.Equal("Fake", received.Text.ToString());
    }
}
