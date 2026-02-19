using System.Net.WebSockets;
using System.Text;
using R3;
using WebSocket.Rx.IntegrationTests.Internal;

namespace WebSocket.Rx.IntegrationTests;

public class ReactiveWebSocketClientEncodingTests(ITestOutputHelper output) : ReactiveWebSocketClientTestBase(output)
{
    [Fact(Timeout = DefaultTimeoutMs)]
    public async Task MessageEncoding_CustomEncoding_ShouldUseCustomEncoding()
    {
        // Arrange
        Client = new ReactiveWebSocketClient(new Uri(Server.WebSocketUrl));
        Client.MessageEncoding = Encoding.ASCII;

        var receivedMessage = "";
        var messageReceivedSource = new Subject<byte[]>();
        Server.OnBytesReceived += msg => messageReceivedSource.OnNext(msg);

        await Client.StartOrFailAsync(TestContext.Current.CancellationToken);
        await WaitUntilAsync(Client.ConnectionHappened, () => Client.IsRunning);

        // Act
        var receiveTask = WaitForEventAsync(messageReceivedSource, msg =>
        {
            receivedMessage = Encoding.ASCII.GetString(msg);
            return receivedMessage == "ASCII Text";
        });
        Client.TrySend("ASCII Text".AsMemory(), WebSocketMessageType.Binary);
        await receiveTask;

        // Assert
        Assert.Equal("ASCII Text", receivedMessage);
    }

    [Fact(Timeout = DefaultTimeoutMs)]
    public async Task MessageReceived_WithTextConversion_ShouldConvertToText()
    {
        // Arrange
        Client = new ReactiveWebSocketClient(new Uri(Server.WebSocketUrl));
        Client.IsTextMessageConversionEnabled = true;

        string? receivedText = null;
        Client.MessageReceived.Subscribe(msg => receivedText = msg.Text.ToString());

        var messageTask = WaitForEventAsync(Client.MessageReceived, msg => msg.Text.ToString() == "Converted Text");

        await Client.StartOrFailAsync(TestContext.Current.CancellationToken);

        // Act
        await Server.SendToAllAsync("Converted Text");
        var received = await messageTask;

        // Assert
        Assert.Equal("Converted Text", received.Text.ToString());
    }

    [Fact(Timeout = DefaultTimeoutMs)]
    public async Task MessageReceived_WithoutTextConversion_ShouldNotConvertToText()
    {
        // Arrange
        Client = new ReactiveWebSocketClient(new Uri(Server.WebSocketUrl));
        Client.IsTextMessageConversionEnabled = false;

        char[]? receivedText = null;
        byte[]? receivedBinary = null;
        Client.MessageReceived.Subscribe(msg =>
        {
            receivedText = msg.Text.ToArray();
            receivedBinary = msg.Binary.ToArray();
        });
        var connectTask = WaitForEventAsync(Client.ConnectionHappened);
        await Client.StartOrFailAsync(TestContext.Current.CancellationToken);
        await connectTask;

        // Act
        var receiveTask = WaitUntilAsync(Client.MessageReceived, () => receivedBinary != null);
        await Server.SendBinaryToAllAsync("No Conversion"u8.ToArray());
        await receiveTask;

        // Assert
        Assert.NotNull(receivedText);
        Assert.Empty(receivedText);
        Assert.NotNull(receivedBinary);
        Assert.NotEmpty(receivedBinary);
    }
}