using System.Net.WebSockets;
using R3;
using WebSocket.Rx.IntegrationTests.Internal;

namespace WebSocket.Rx.IntegrationTests;

public class ReactiveWebSocketClientReconnectionTests(ITestOutputHelper output)
    : ReactiveWebSocketClientTestBase(output)
{
    [Fact(Timeout = DefaultTimeoutMs)]
    public async Task Reconnect_WhenStarted_ShouldReconnect()
    {
        // Arrange
        Client = new ReactiveWebSocketClient(new Uri(Server.WebSocketUrl));
        await Client.StartOrFailAsync(TestContext.Current.CancellationToken);

        var reconnectionTask = WaitForEventAsync(Client.ConnectionHappened, c => c.Reason == ConnectReason.Reconnected);

        // Act
        await Client.ReconnectAsync(TestContext.Current.CancellationToken);
        await reconnectionTask;

        // Assert
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

        var reconnectionTask = WaitForEventAsync(Client.ConnectionHappened, c => c.Reason == ConnectReason.Reconnected);

        // Act
        await Client.ReconnectOrFailAsync(TestContext.Current.CancellationToken);
        await reconnectionTask;
        Assert.True(true);
    }

    [Fact(Timeout = DefaultTimeoutMs)]
    public async Task AutoReconnect_OnConnectionLost_ShouldReconnect()
    {
        // Arrange
        Client = new ReactiveWebSocketClient(new Uri(Server.WebSocketUrl));
        Client.IsReconnectionEnabled = true;

        var reconnectionTask = WaitForEventAsync(Client.ConnectionHappened, c => c.Reason == ConnectReason.Reconnected);

        await Client.StartOrFailAsync(TestContext.Current.CancellationToken);

        // Act — dispose the server to force TCP connection closed, then restart for reconnection
        var port = Server.Port;
        await Server.DisposeAsync();

        var server2 = new WebSocketTestServer(port);
        await server2.StartAsync();

        try
        {
            await reconnectionTask;
            Assert.True(true);
        }
        finally
        {
            await server2.DisposeAsync();
        }
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

        // Act — stop the client (simulates disconnection) and wait for it to settle
        await Client.StopAsync(WebSocketCloseStatus.NormalClosure, "Test disconnect",
            TestContext.Current.CancellationToken);
        await Task.Delay(200, TestContext.Current.CancellationToken);

        // Assert — no reconnection should have occurred
        Assert.Equal(0, reconnectCount);
    }
}
