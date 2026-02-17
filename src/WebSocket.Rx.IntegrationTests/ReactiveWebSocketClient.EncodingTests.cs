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
        Client.TrySendAsBinary("ASCII Text");
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
        Client.MessageReceived.Subscribe(msg => receivedText = msg.Text);

        var messageTask = WaitForEventAsync(Client.MessageReceived, msg => msg.Text == "Converted Text");

        await Client.StartOrFailAsync(TestContext.Current.CancellationToken);

        // Act
        await Server.SendToAllAsync("Converted Text");
        var received = await messageTask;

        // Assert
        Assert.Equal("Converted Text", received.Text);
    }

    [Fact(Timeout = DefaultTimeoutMs)]
    public async Task MessageReceived_WithoutTextConversion_ShouldNotConvertToText()
    {
        // Arrange
        Client = new ReactiveWebSocketClient(new Uri(Server.WebSocketUrl));
        Client.IsTextMessageConversionEnabled = false;

        string? receivedText = null;
        byte[]? receivedBinary = null;
        Client.MessageReceived.Subscribe(msg =>
        {
            receivedText = msg.Text;
            receivedBinary = msg.Binary;
        });
        var connectTask = WaitForEventAsync(Client.ConnectionHappened);
        await Client.StartOrFailAsync(TestContext.Current.CancellationToken);
        await connectTask;

        // Act
        var receiveTask = WaitUntilAsync(Client.MessageReceived, () => receivedBinary != null);
        await Server.SendBinaryToAllAsync("No Conversion"u8.ToArray());
        await receiveTask;

        // Assert
        Assert.Null(receivedText);
        Assert.NotNull(receivedBinary);
    }
}