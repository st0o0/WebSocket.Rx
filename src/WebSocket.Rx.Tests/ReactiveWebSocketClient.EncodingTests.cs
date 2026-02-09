using System.Text;
using R3;
using WebSocket.Rx.Tests.Internal;

namespace WebSocket.Rx.Tests;

public class ReactiveWebSocketClientEncodingTests(ITestOutputHelper output) : ReactiveWebSocketClientTestBase(output)
{
    [Fact(Timeout = DefaultTimeoutMs)]
    public async Task MessageEncoding_CustomEncoding_ShouldUseCustomEncoding()
    {
        // Arrange
        Client = new ReactiveWebSocketClient(new Uri(Server.WebSocketUrl));
        Client.MessageEncoding = Encoding.ASCII;

        var receivedMessage = "";
        Server.OnBytesReceived += msg => receivedMessage = Encoding.ASCII.GetString(msg);

        await Client.StartOrFailAsync(TestContext.Current.CancellationToken);
        await Task.Delay(50, TestContext.Current.CancellationToken);

        // Act
        Client.TrySendAsBinary("ASCII Text");
        await Task.Delay(50, TestContext.Current.CancellationToken);

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

        await Client.StartOrFailAsync(TestContext.Current.CancellationToken);
        await Task.Delay(50, TestContext.Current.CancellationToken);

        // Act
        await Server.SendToAllAsync("Converted Text");
        await Task.Delay(50, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("Converted Text", receivedText);
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

        await Client.StartOrFailAsync(TestContext.Current.CancellationToken);
        await Task.Delay(50, TestContext.Current.CancellationToken);

        // Act
        await Server.SendToAllAsync("No Conversion");
        await Task.Delay(50, TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(receivedText);
        Assert.NotNull(receivedBinary);
    }
}
