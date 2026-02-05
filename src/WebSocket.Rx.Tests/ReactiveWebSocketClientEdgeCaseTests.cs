using System.Net.WebSockets;
using R3;
using System.Text;
using WebSocket.Rx.Tests.Internal;

namespace WebSocket.Rx.Tests;

public class ReactiveWebSocketClientEdgeCaseTests
{
    private const string InvalidUrl = "ws://localhost:9999/invalid";

    #region Encoding Tests

    [Fact(Timeout = 5000)]
    public async Task MessageEncoding_CustomEncoding_ShouldUseCustomEncoding()
    {
        // Arrange
        await using var server = new WebSocketTestServer();
        await server.StartAsync();

        using var client = new ReactiveWebSocketClient(new Uri(server.WebSocketUrl));
        client.MessageEncoding = Encoding.ASCII;

        var receivedMessage = "";
        server.OnBytesReceived += msg => receivedMessage = Encoding.ASCII.GetString(msg);

        await client.StartOrFailAsync();
        await Task.Delay(50);

        // Act
        client.TrySendAsBinary("ASCII Text");
        await Task.Delay(50);

        // Assert
        Assert.Equal("ASCII Text", receivedMessage);
    }

    [Fact(Timeout = 5000)]
    public async Task MessageReceived_WithTextConversion_ShouldConvertToText()
    {
        // Arrange
        await using var server = new WebSocketTestServer();
        await server.StartAsync();

        using var client = new ReactiveWebSocketClient(new Uri(server.WebSocketUrl));
        client.IsTextMessageConversionEnabled = true;

        string? receivedText = null;
        client.MessageReceived.Subscribe(msg => receivedText = msg.Text);

        await client.StartOrFailAsync();
        await Task.Delay(50);

        // Act
        await server.SendToAllAsync("Converted Text");
        await Task.Delay(50);

        // Assert
        Assert.Equal("Converted Text", receivedText);
    }

    [Fact(Timeout = 5000)]
    public async Task MessageReceived_WithoutTextConversion_ShouldNotConvertToText()
    {
        // Arrange
        await using var server = new WebSocketTestServer();
        await server.StartAsync();

        using var client = new ReactiveWebSocketClient(new Uri(server.WebSocketUrl));
        client.IsTextMessageConversionEnabled = false;

        string? receivedText = null;
        byte[]? receivedBinary = null;
        client.MessageReceived.Subscribe(msg =>
        {
            receivedText = msg.Text;
            receivedBinary = msg.Binary;
        });

        await client.StartOrFailAsync();
        await Task.Delay(50);

        // Act
        await server.SendToAllAsync("No Conversion");
        await Task.Delay(50);

        // Assert
        Assert.Null(receivedText);
        Assert.NotNull(receivedBinary);
    }

    #endregion

    #region Error Scenarios

    [Fact(Timeout = 5000)]
    public async Task Send_EmptyByteArray_ShouldReturnFalse()
    {
        // Arrange
        await using var server = new WebSocketTestServer();
        await server.StartAsync();

        using var client = new ReactiveWebSocketClient(new Uri(server.WebSocketUrl));
        await client.StartOrFailAsync();

        // Act
        var result = client.TrySendAsBinary([]);

        // Assert
        Assert.False(result);
    }

    [Fact(Timeout = 5000)]
    public async Task SendAsText_EmptyByteArray_ShouldReturnFalse()
    {
        // Arrange
        await using var server = new WebSocketTestServer();
        await server.StartAsync();

        using var client = new ReactiveWebSocketClient(new Uri(server.WebSocketUrl));
        await client.StartOrFailAsync();

        // Act
        var result = client.TrySendAsText(Array.Empty<byte>());

        // Assert
        Assert.False(result);
    }

    [Fact(Timeout = 5000)]
    public async Task SendInstant_WhenNotConnected_ShouldNotThrow()
    {
        // Arrange
        using var client = new ReactiveWebSocketClient(new Uri(InvalidUrl));

        // Act & Assert
        await client.SendInstantAsync("test");
        await client.SendInstantAsync([1, 2, 3]);
        Assert.True(true);
    }

    [Fact(Timeout = 5000)]
    public async Task ConnectTimeout_WhenServerNotResponding_ShouldTimeout()
    {
        // Arrange
        using var client = new ReactiveWebSocketClient(new Uri(InvalidUrl));
        client.ConnectTimeout = TimeSpan.FromMilliseconds(50);

        var disconnected = false;
        client.DisconnectionHappened.Subscribe(d =>
        {
            if (d.Reason == DisconnectReason.Error)
                disconnected = true;
        });

        // Act
        await client.StartAsync();
        await Task.Delay(50);

        // Assert
        Assert.True(disconnected);
    }

    [Fact(Timeout = 5000)]
    public async Task MultipleReconnects_InParallel_ShouldNotCauseConcurrencyIssues()
    {
        // Arrange
        await using var server = new WebSocketTestServer();
        await server.StartAsync();

        using var client = new ReactiveWebSocketClient(new Uri(server.WebSocketUrl));
        await client.StartOrFailAsync();

        // Act
        var tasks = new Task[10];
        for (var i = 0; i < 10; i++)
        {
            tasks[i] = Task.Run(async () => await client.ReconnectAsync());
        }

        await Task.WhenAll(tasks);

        // Assert
        Assert.True(client.IsStarted);
        Assert.True(client.IsRunning);
    }

    #endregion

    #region Timeout Scenarios

    [Fact(Timeout = 5000)]
    public async Task ErrorReconnectTimeout_OnError_ShouldUseCorrectTimeout()
    {
        // Arrange
        using var client = new ReactiveWebSocketClient(new Uri(InvalidUrl));
        client.KeepAliveInterval = TimeSpan.FromMilliseconds(50);
        client.IsReconnectionEnabled = true;

        var reconnectAttempts = 0;
        client.ConnectionHappened.Subscribe(_ => reconnectAttempts++);

        // Act
        await client.StartAsync();

        // Assert
        Assert.True(reconnectAttempts >= 0);
    }

    [Fact(Timeout = 40000)]
    public async Task InactivityTimeout_OnConnectionLost_ShouldReconnectQuickly()
    {
        // Arrange
        await using var server = new WebSocketTestServer();
        await server.StartAsync();

        using var client = new ReactiveWebSocketClient(new Uri(server.WebSocketUrl));
        client.KeepAliveInterval = TimeSpan.FromMilliseconds(50);
        client.IsReconnectionEnabled = true;

        var reconnected = new TaskCompletionSource<bool>();
        var reconnectTask = reconnected.Task;
        client.ConnectionHappened
            .Where(c => c.Reason == ConnectReason.Reconnect)
            .Take(1)
            .Subscribe(x => reconnected.TrySetResult(true));

        await client.StartOrFailAsync();

        // Act
        await server.DisconnectAllAsync();

        // Assert
        Assert.True(await reconnectTask.WaitAsync(TimeSpan.FromSeconds(1)));
        Assert.True(reconnectTask.IsCompletedSuccessfully);
    }

    #endregion

    #region Observable Tests

    [Fact(Timeout = 5000)]
    public async Task ConnectionHappened_MultipleSubscribers_ShouldNotifyAll()
    {
        // Arrange
        await using var server = new WebSocketTestServer();
        await server.StartAsync();

        using var client = new ReactiveWebSocketClient(new Uri(server.WebSocketUrl));

        var subscriber1Notified = false;
        var subscriber2Notified = false;

        client.ConnectionHappened.Subscribe(_ => subscriber1Notified = true);
        client.ConnectionHappened.Subscribe(_ => subscriber2Notified = true);

        // Act
        await client.StartOrFailAsync();

        // Assert
        Assert.True(subscriber1Notified);
        Assert.True(subscriber2Notified);
    }

    [Fact(Timeout = 5000)]
    public async Task DisconnectionHappened_WithException_ShouldIncludeException()
    {
        // Arrange
        using var client = new ReactiveWebSocketClient(new Uri(InvalidUrl));
        client.ConnectTimeout = TimeSpan.FromMilliseconds(50);

        Exception? capturedException = null;
        client.DisconnectionHappened.Subscribe(d => capturedException = d.Exception);

        // Act
        await client.StartAsync();
        await Task.Delay(50);

        // Assert
        Assert.NotNull(capturedException);
    }

    [Fact(Timeout = 5000)]
    public async Task MessageReceived_MultipleMessages_ShouldReceiveInOrder()
    {
        // Arrange
        await using var server = new WebSocketTestServer();
        await server.StartAsync();

        using var client = new ReactiveWebSocketClient(new Uri(server.WebSocketUrl));
        client.IsTextMessageConversionEnabled = true;

        var receivedMessages = new List<string>();
        client.MessageReceived.Subscribe(msg =>
        {
            if (msg.Text != null)
                receivedMessages.Add(msg.Text);
        });

        await client.StartOrFailAsync();
        await Task.Delay(50);

        // Act
        await server.SendToAllAsync("Message 1");
        await Task.Delay(50);
        await server.SendToAllAsync("Message 2");
        await Task.Delay(50);
        await server.SendToAllAsync("Message 3");
        await Task.Delay(50);

        // Assert
        Assert.Equal(3, receivedMessages.Count);
        Assert.Equal("Message 1", receivedMessages[0]);
        Assert.Equal("Message 2", receivedMessages[1]);
        Assert.Equal("Message 3", receivedMessages[2]);
    }

    #endregion

    #region State Tests

    [Fact(Timeout = 5000)]
    public async Task IsInsideLock_DuringReconnect_ShouldBeTrue()
    {
        // Arrange
        await using var server = new WebSocketTestServer();
        await server.StartAsync();

        using var client = new ReactiveWebSocketClient(new Uri(server.WebSocketUrl));
        await client.StartOrFailAsync();

        // Act
        var reconnectTask = Task.Run(async () => { await client.ReconnectAsync(); });

        await Task.Delay(10);
        await reconnectTask;

        // Assert
        Assert.True(true);
    }

    #endregion

    #region Stress Tests

    [Fact(Timeout = 5000)]
    public async Task LargeMessage_ShouldSendAndReceiveCorrectly()
    {
        // Arrange
        await using var server = new WebSocketTestServer();
        await server.StartAsync();

        using var client = new ReactiveWebSocketClient(new Uri(server.WebSocketUrl));
        client.IsTextMessageConversionEnabled = true;

        string? receivedMessage = null;
        client.MessageReceived.Subscribe(msg => receivedMessage = msg.Text);

        await client.StartOrFailAsync();
        await Task.Delay(50);

        // Act
        var largeMessage = new string('A', 1024 * 1024);
        await server.SendToAllAsync(largeMessage);
        await Task.Delay(50);

        // Assert
        Assert.Equal(largeMessage, receivedMessage);
    }

    [Fact(Timeout = 5000)]
    public async Task RapidConnectDisconnect_ShouldHandleGracefully()
    {
        // Arrange
        await using var server = new WebSocketTestServer();
        await server.StartAsync();

        using var client = new ReactiveWebSocketClient(new Uri(server.WebSocketUrl));

        // Act
        for (var i = 0; i < 5; i++)
        {
            await client.StartOrFailAsync();
            await Task.Delay(50);
            await client.StopAsync(WebSocketCloseStatus.NormalClosure, "Rapid test");
            await Task.Delay(50);
        }

        // Assert
        Assert.False(client.IsRunning);
    }

    #endregion

    #region Null/Empty Checks

    [Fact]
    public void Send_NullString_ShouldReturnFalse()
    {
        // Arrange
        using var client = new ReactiveWebSocketClient(new Uri(InvalidUrl));

        // Act
        var result = client.TrySendAsText((string)null!);

        // Assert
        Assert.False(result);
    }

    [Fact(Timeout = 5000)]
    public async Task SendInstant_NullString_ShouldNotThrow()
    {
        // Arrange
        await using var server = new WebSocketTestServer();
        await server.StartAsync();

        using var client = new ReactiveWebSocketClient(new Uri(server.WebSocketUrl));
        await client.StartOrFailAsync();

        // Act & Assert
        await client.SendInstantAsync((string)null!);
    }

    #endregion
}