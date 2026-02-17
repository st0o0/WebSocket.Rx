using System.Net.WebSockets;
using System.Text;
using WebSocket.Rx.IntegrationTests.Internal;

namespace WebSocket.Rx.IntegrationTests;

public class ReactiveWebSocketClientPropertyTests(ITestOutputHelper output) : ReactiveWebSocketClientTestBase(output)
{
    [Fact(Timeout = DefaultTimeoutMs)]
    public void Properties_DefaultValues_ShouldBeCorrect()
    {
        // Arrange & Act
        Client = new ReactiveWebSocketClient(new Uri(Server.WebSocketUrl));

        // Assert
        Assert.Equal(TimeSpan.FromSeconds(10), Client.ConnectTimeout);
        Assert.True(Client.IsReconnectionEnabled);
        Assert.True(Client.IsTextMessageConversionEnabled);
        Assert.Equal(Encoding.UTF8, Client.MessageEncoding);
    }

    [Fact(Timeout = DefaultTimeoutMs)]
    public async Task NativeClient_WhenConnected_ShouldNotBeNull()
    {
        // Arrange
        Client = new ReactiveWebSocketClient(new Uri(Server.WebSocketUrl));

        // Act
        await Client.StartOrFailAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(Client.NativeClient);
        Assert.Equal(WebSocketState.Open, Client.NativeClient.State);
    }

    [Fact(Timeout = DefaultTimeoutMs)]
    public async Task SenderRunning_WhenConnected_ShouldBeTrue()
    {
        // Arrange
        Client = new ReactiveWebSocketClient(new Uri(Server.WebSocketUrl));

        // Act
        await Client.StartOrFailAsync(TestContext.Current.CancellationToken);
        await Task.Delay(50, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(Client.SenderRunning);
    }
}
