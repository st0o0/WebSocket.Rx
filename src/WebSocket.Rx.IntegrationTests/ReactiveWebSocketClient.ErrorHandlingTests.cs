using R3;
using WebSocket.Rx.IntegrationTests.Internal;

namespace WebSocket.Rx.IntegrationTests;

public class ReactiveWebSocketClientErrorHandlingTests(ITestOutputHelper output)
    : ReactiveWebSocketClientTestBase(output)
{
    private const string InvalidUrl = "ws://localhost:9999/invalid";

    [Fact(Timeout = DefaultTimeoutMs)]
    public async Task Send_EmptyByteArray_ShouldReturnFalse()
    {
        // Arrange
        Client = new ReactiveWebSocketClient(new Uri(Server.WebSocketUrl));
        await Client.StartOrFailAsync(TestContext.Current.CancellationToken);

        // Act
        var result = Client.TrySendAsBinary([]);

        // Assert
        Assert.False(result);
    }

    [Fact(Timeout = DefaultTimeoutMs)]
    public async Task SendAsText_EmptyByteArray_ShouldReturnFalse()
    {
        // Arrange
        Client = new ReactiveWebSocketClient(new Uri(Server.WebSocketUrl));
        await Client.StartOrFailAsync(TestContext.Current.CancellationToken);

        // Act
        var result = Client.TrySendAsText([]);

        // Assert
        Assert.False(result);
    }

    [Fact(Timeout = DefaultTimeoutMs)]
    public async Task SendInstant_WhenNotConnected_ShouldNotThrow()
    {
        // Arrange
        Client = new ReactiveWebSocketClient(new Uri(InvalidUrl));

        // Act & Assert
        await Client.SendInstantAsync("test", TestContext.Current.CancellationToken);
        await Client.SendInstantAsync([1, 2, 3], TestContext.Current.CancellationToken);
        Assert.True(true);
    }

    [Fact(Timeout = DefaultTimeoutMs)]
    public async Task ConnectTimeout_WhenServerNotResponding_ShouldTimeout()
    {
        // Arrange
        Client = new ReactiveWebSocketClient(new Uri(InvalidUrl));
        Client.ConnectTimeout = TimeSpan.FromMilliseconds(50);

        var disconnected = false;
        Client.ErrorOccurred.Subscribe(d =>
        {
            if (d.Source == ErrorSource.Connection)
            {
                disconnected = true;
            }
        });

        // Act
        await Client.StartAsync(TestContext.Current.CancellationToken);
        await Task.Delay(50, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(disconnected);
    }

    [Fact(Timeout = DefaultTimeoutMs)]
    public async Task DisconnectionHappened_WithException_ShouldIncludeException()
    {
        // Arrange
        Client = new ReactiveWebSocketClient(new Uri(InvalidUrl));
        Client.ConnectTimeout = TimeSpan.FromMilliseconds(50);

        Exception? capturedException = null;
        Client.ErrorOccurred.Subscribe(d => capturedException = d.Exception);

        // Act
        await Client.StartAsync(TestContext.Current.CancellationToken);
        await Task.Delay(50, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(capturedException);
    }

    [Fact(Timeout = DefaultTimeoutMs)]
    public void Send_NullString_ShouldReturnFalse()
    {
        // Arrange
        Client = new ReactiveWebSocketClient(new Uri(InvalidUrl));

        // Act
        var result = Client.TrySendAsText((string)null!);

        // Assert
        Assert.False(result);
    }

    [Fact(Timeout = 5000)]
    public async Task SendInstant_NullString_ShouldNotThrow()
    {
        // Arrange
        Client = new ReactiveWebSocketClient(new Uri(Server.WebSocketUrl));
        await Client.StartOrFailAsync(TestContext.Current.CancellationToken);

        // Act & Assert
        await Client.SendInstantAsync((string)null!, TestContext.Current.CancellationToken);
        Assert.True(true);
    }
}
