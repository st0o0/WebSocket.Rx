using R3;
using WebSocket.Rx.Tests.Internal;

namespace WebSocket.Rx.Tests;

public class ReactiveWebSocketClientReconnectionTests(ITestOutputHelper output)
    : ReactiveWebSocketClientTestBase(output)
{
    [Fact(Timeout = DefaultTimeoutMs)]
    public async Task Reconnect_WhenStarted_ShouldReconnect()
    {
        // Arrange
        Client = new ReactiveWebSocketClient(new Uri(Server.WebSocketUrl));
        await Client.StartOrFailAsync(TestContext.Current.CancellationToken);

        var reconnected = false;
        Client.ConnectionHappened
            .Where(c => c.Reason == ConnectReason.Reconnected)
            .Subscribe(_ => reconnected = true);

        // Act
        await Client.ReconnectAsync(TestContext.Current.CancellationToken);
        await Task.Delay(50, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(reconnected);
        Assert.True(Client.IsRunning);
    }

    [Fact(Timeout = DefaultTimeoutMs)]
    public async Task Reconnect_WhenNotStarted_ShouldDoNothing()
    {
        // Arrange
        Client = new ReactiveWebSocketClient(new Uri(Server.WebSocketUrl));
        var connectionCount = 0;
        Client.ConnectionHappened.Subscribe(_ => connectionCount++);

        // Act
        await Client.ReconnectAsync(TestContext.Current.CancellationToken);
        await Task.Delay(50, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(0, connectionCount);
    }

    [Fact(Timeout = DefaultTimeoutMs)]
    public async Task ReconnectOrFail_WhenNotStarted_ShouldThrow()
    {
        // Arrange
        Client = new ReactiveWebSocketClient(new Uri(Server.WebSocketUrl));

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Client.ReconnectOrFailAsync(TestContext.Current.CancellationToken));
    }

    [Fact(Timeout = DefaultTimeoutMs)]
    public async Task ReconnectOrFail_WhenStarted_ShouldReconnect()
    {
        // Arrange
        Client = new ReactiveWebSocketClient(new Uri(Server.WebSocketUrl));
        await Client.StartOrFailAsync(TestContext.Current.CancellationToken);

        var reconnected = false;
        Client.ConnectionHappened
            .Where(c => c.Reason == ConnectReason.Reconnected)
            .Subscribe(_ => reconnected = true);

        // Act
        await Client.ReconnectOrFailAsync(TestContext.Current.CancellationToken);
        await Task.Delay(50, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(reconnected);
    }

    [Fact(Timeout = DefaultTimeoutMs)]
    public async Task AutoReconnect_OnConnectionLost_ShouldReconnect()
    {
        // Arrange
        Client = new ReactiveWebSocketClient(new Uri(Server.WebSocketUrl));
        Client.IsReconnectionEnabled = true;
        Client.KeepAliveInterval = TimeSpan.FromMilliseconds(25);

        var reconnected = false;
        Client.ConnectionHappened
            .Where(c => c.Reason == ConnectReason.Reconnected)
            .Subscribe(_ => reconnected = true);

        await Client.StartOrFailAsync(TestContext.Current.CancellationToken);
        await Task.Delay(50, TestContext.Current.CancellationToken);

        // Act
        await Server.DisconnectAllAsync();
        await Task.Delay(50, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(reconnected);
    }

    [Fact(Timeout = DefaultTimeoutMs)]
    public async Task AutoReconnect_WhenDisabled_ShouldNotReconnect()
    {
        // Arrange
        Client = new ReactiveWebSocketClient(new Uri(Server.WebSocketUrl));
        Client.IsReconnectionEnabled = false;

        var reconnectCount = 0;
        Client.ConnectionHappened
            .Where(c => c.Reason == ConnectReason.Reconnected)
            .Subscribe(_ => reconnectCount++);

        await Client.StartOrFailAsync(TestContext.Current.CancellationToken);
        await Task.Delay(50, TestContext.Current.CancellationToken);

        // Act
        await Server.DisconnectAllAsync();
        await Task.Delay(50, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(0, reconnectCount);
    }
}
