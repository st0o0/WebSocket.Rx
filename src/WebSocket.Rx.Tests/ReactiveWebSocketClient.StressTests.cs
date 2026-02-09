using System.Net.WebSockets;
using R3;
using WebSocket.Rx.Tests.Internal;

namespace WebSocket.Rx.Tests;

public class ReactiveWebSocketClientStressTests(ITestOutputHelper output) : ReactiveWebSocketClientTestBase(output)
{
    [Fact(Timeout = DefaultTimeoutMs)]
    public async Task LargeMessage_ShouldSendAndReceiveCorrectly()
    {
        // Arrange
        Client = new ReactiveWebSocketClient(new Uri(Server.WebSocketUrl));
        Client.IsTextMessageConversionEnabled = true;

        string? receivedMessage = null;
        Client.MessageReceived.Subscribe(msg => receivedMessage = msg.Text);

        await Client.StartOrFailAsync(TestContext.Current.CancellationToken);
        await Task.Delay(50, TestContext.Current.CancellationToken);

        // Act
        var largeMessage = new string('A', 1024 * 1024);
        await Server.SendToAllAsync(largeMessage);
        await Task.Delay(50, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(largeMessage, receivedMessage);
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
            await Task.Delay(50, TestContext.Current.CancellationToken);
            await Client.StopAsync(WebSocketCloseStatus.NormalClosure, "Rapid test", TestContext.Current.CancellationToken);
            await Task.Delay(50, TestContext.Current.CancellationToken);
        }

        // Assert
        Assert.False(Client.IsRunning);
    }

    [Fact(Timeout = DefaultTimeoutMs)]
    public async Task InactivityTimeout_OnConnectionLost_ShouldReconnectQuickly()
    {
        // Arrange
        Client = new ReactiveWebSocketClient(new Uri(Server.WebSocketUrl));
        Client.KeepAliveInterval = TimeSpan.FromMilliseconds(50);
        Client.IsReconnectionEnabled = true;

        var reconnected = new TaskCompletionSource<bool>();
        var reconnectTask = reconnected.Task;
        Client.ConnectionHappened
            .Where(c => c.Reason == ConnectReason.Reconnect)
            .Take(1)
            .Subscribe(_ => reconnected.TrySetResult(true));

        await Client.StartOrFailAsync(TestContext.Current.CancellationToken);

        // Act
        await Server.DisconnectAllAsync();

        // Assert
        Assert.True(await reconnectTask.WaitAsync(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken));
        Assert.True(reconnectTask.IsCompletedSuccessfully);
    }

    [Fact(Timeout = 5000)]
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

        // Assert
        Assert.True(Client.IsStarted);
        Assert.True(Client.IsRunning);
    }
}
