using System.Net.WebSockets;
using R3;
using WebSocket.Rx.IntegrationTests.Internal;

namespace WebSocket.Rx.IntegrationTests;

public class ReactiveWebSocketClientStressTests(ITestOutputHelper output) : ReactiveWebSocketClientTestBase(output)
{
    [Fact(Timeout = DefaultTimeoutMs)]
    public async Task LargeMessage_ShouldSendAndReceiveCorrectly()
    {
        // Arrange
        Client = new ReactiveWebSocketClient(new Uri(Server.WebSocketUrl));
        Client.IsTextMessageConversionEnabled = true;

        var largeMessage = new string('A', 1024 * 1024);
        var messageReceivedTask = WaitForEventAsync(Client.MessageReceived, msg => msg.Text.ToString() == largeMessage);

        await Client.StartOrFailAsync(TestContext.Current.CancellationToken);
        await WaitForConditionAsync(() => Server.ClientCount > 0);

        // Act
        await Server.SendToAllAsync(largeMessage);
        var received = await messageReceivedTask;

        // Assert
        Assert.Equal(largeMessage, received.Text.ToString());
    }

    [Fact(Timeout = DefaultTimeoutMs)]
    public async Task RapidConnectDisconnect_ShouldHandleGracefully()
    {
        // Arrange
        Client = new ReactiveWebSocketClient(new Uri(Server.WebSocketUrl));

        // Act
        for (var i = 0; i < 5; i++)
        {
            await Client.StartOrFailAsync(TestContext.Current.CancellationToken);
            await Client.StopAsync(WebSocketCloseStatus.NormalClosure, "Rapid test",
                TestContext.Current.CancellationToken);
            await WaitUntilAsync(Client.DisconnectionHappened, () => !Client.IsRunning);
        }

        // Assert
        Assert.False(Client.IsRunning);
    }

    [Fact(Timeout = DefaultTimeoutMs)]
    public async Task InactivityTimeout_OnConnectionLost_ShouldReconnectQuickly()
    {
        // Arrange
        var server2 = new WebSocketTestServer();
        await server2.StartAsync();

        Client = new ReactiveWebSocketClient(new Uri(server2.WebSocketUrl));
        Client.KeepAliveInterval = TimeSpan.FromMilliseconds(50);
        Client.IsReconnectionEnabled = true;

        var reconnectionTask = WaitForEventAsync(Client.ConnectionHappened, c => c.Reason == ConnectReason.Reconnected);

        await Client.StartOrFailAsync(TestContext.Current.CancellationToken);

        // Act — dispose the server to force TCP connection closed
        await server2.DisposeAsync();

        // Restart a server on the same URL so the client can reconnect
        var server3 = new WebSocketTestServer(server2.Port);
        await server3.StartAsync();

        try
        {
            // Assert
            var result = await reconnectionTask;
            Assert.Equal(ConnectReason.Reconnected, result.Reason);
        }
        finally
        {
            await server3.DisposeAsync();
        }
    }

    [Fact(Timeout = DefaultTimeoutMs)]
    public async Task MultipleReconnects_InParallel_ShouldNotCauseConcurrencyIssues()
    {
        // Arrange
        Client = new ReactiveWebSocketClient(new Uri(Server.WebSocketUrl));
        await Client.StartOrFailAsync(TestContext.Current.CancellationToken);

        // Act
        var tasks = new Task[10];
        for (var i = 0; i < 10; i++)
        {
            tasks[i] = Task.Run(async () => await Client.ReconnectAsync(TestContext.Current.CancellationToken),
                TestContext.Current.CancellationToken);
        }

        await Task.WhenAll(tasks);

        // Assert — parallel reconnects must not deadlock or corrupt state
        Assert.True(Client.IsStarted);

        // Individual reconnects may fail under load; verify the client is still functional
        await Client.ReconnectOrFailAsync(TestContext.Current.CancellationToken);
        Assert.True(Client.IsRunning);
    }
}