using System.Net.WebSockets;
using R3;
using System.Text;
using WebSocket.Rx.Tests.Internal;

namespace WebSocket.Rx.Tests;

public class ReactiveWebSocketClientTests : IAsyncLifetime
{
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring as nullable.
    private WebSocketTestServer _server;
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring as nullable.
    private ReactiveWebSocketClient? _client;

    public async Task InitializeAsync()
    {
        _server = new WebSocketTestServer();
        await _server.StartAsync();
    }

    public async Task DisposeAsync()
    {
        _client?.Dispose();
        await _server.DisposeAsync();
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_WithValidUri_ShouldSetProperties()
    {
        // Arrange & Act
        var uri = new Uri(_server.WebSocketUrl);
        _client = new ReactiveWebSocketClient(uri);

        // Assert
        Assert.Equal(uri, _client.Url);
        Assert.False(_client.IsStarted);
        Assert.False(_client.IsRunning);
        Assert.NotNull(_client.MessageReceived);
        Assert.NotNull(_client.ConnectionHappened);
        Assert.NotNull(_client.DisconnectionHappened);
    }

    [Fact]
    public void Constructor_WithNullUri_ShouldThrowArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new ReactiveWebSocketClient(null!));
    }

    #endregion

    #region Start/Stop Tests

    [Fact(Timeout = 5000)]
    public async Task StartOrFail_WhenNotStarted_ShouldConnect()
    {
        // Arrange
        _client = new ReactiveWebSocketClient(new Uri(_server.WebSocketUrl));
        var connected = false;
        _client.ConnectionHappened.Subscribe(c => connected = true);

        // Act
        await _client.StartOrFailAsync();

        // Assert
        Assert.True(_client.IsStarted);
        Assert.True(_client.IsRunning);
        await Task.Delay(50);
        Assert.True(connected);
    }

    [Fact(Timeout = 5000)]
    public async Task StartOrFail_WhenAlreadyStarted_ShouldNotConnectAgain()
    {
        // Arrange
        _client = new ReactiveWebSocketClient(new Uri(_server.WebSocketUrl));
        await _client.StartOrFailAsync();
        var connectionCount = 0;
        _client.ConnectionHappened.Subscribe(_ => connectionCount++);

        // Act
        await _client.StartOrFailAsync();
        await Task.Delay(50);

        // Assert
        Assert.Equal(0, connectionCount);
    }

    [Fact(Timeout = 5000)]
    public async Task Start_WithConnectionError_ShouldNotThrow()
    {
        // Arrange
        _client = new ReactiveWebSocketClient(new Uri("ws://localhost:9999/invalid"));
        var disconnected = false;
        _client.DisconnectionHappened.Subscribe(d => disconnected = true);

        // Act
        await _client.StartAsync();
        await Task.Delay(50);

        // Assert
        Assert.True(disconnected);
    }

    [Fact(Timeout = 5000)]
    public async Task StartOrFail_WithInvalidUri_ShouldThrow()
    {
        // Arrange
        _client = new ReactiveWebSocketClient(new Uri("ws://localhost:9999/invalid"));
        _client.ConnectTimeout = TimeSpan.FromMilliseconds(500);

        // Act & Assert
        await Assert.ThrowsAnyAsync<Exception>(() => _client.StartOrFailAsync());
    }

    [Fact(Timeout = 5000)]
    public async Task Stop_WhenRunning_ShouldDisconnect()
    {
        // Arrange
        _client = new ReactiveWebSocketClient(new Uri(_server.WebSocketUrl));
        await _client.StartOrFailAsync();
        var disconnected = new TaskCompletionSource<bool>();
        var disconnectedTask = disconnected.Task;
        _client.DisconnectionHappened
            .Where(x => x.Reason is DisconnectReason.ClientInitiated)
            .Take(1)
            .Subscribe(x => disconnected.SetResult(true));

        // Act
        var result = await _client.StopAsync(WebSocketCloseStatus.NormalClosure, "Test stop");
        await Task.Delay(50);

        // Assert
        Assert.True(result);
        Assert.False(_client.IsStarted);
        Assert.False(_client.IsRunning);
        Assert.True(await disconnectedTask.WaitAsync(TimeSpan.FromSeconds(1)));
        Assert.True(disconnectedTask.IsCompletedSuccessfully);
    }

    [Fact(Timeout = 5000)]
    public async Task Stop_WhenNotStarted_ShouldReturnFalse()
    {
        // Arrange
        _client = new ReactiveWebSocketClient(new Uri(_server.WebSocketUrl));

        // Act
        var result = await _client.StopAsync(WebSocketCloseStatus.NormalClosure, "Test");

        // Assert
        Assert.False(result);
    }

    [Fact(Timeout = 5000)]
    public async Task StopOrFail_WhenRunning_ShouldStopSuccessfully()
    {
        // Arrange
        _client = new ReactiveWebSocketClient(new Uri(_server.WebSocketUrl));
        await _client.StartOrFailAsync();

        // Act
        var result = await _client.StopOrFailAsync(WebSocketCloseStatus.NormalClosure, "Test");

        // Assert
        Assert.True(result);
        Assert.False(_client.IsStarted);
    }

    #endregion

    #region Send Tests

    [Fact(Timeout = 5000)]
    public async Task Send_String_WhenConnected_ShouldSendMessage()
    {
        // Arrange
        _client = new ReactiveWebSocketClient(new Uri(_server.WebSocketUrl));
        await _client.StartOrFailAsync();
        await Task.Delay(50);

        var receivedMessage = "";
        _server.OnMessageReceived += msg => receivedMessage = msg;

        // Act
        var result = _client.TrySendAsText("Hello World");
        await Task.Delay(50);

        // Assert
        Assert.True(result);
        Assert.Equal("Hello World", receivedMessage);
    }


    [Fact(Timeout = 5000)]
    public async Task Send_ByteArray_WhenConnected_ShouldSendMessage()
    {
        // Arrange
        _client = new ReactiveWebSocketClient(new Uri(_server.WebSocketUrl));
        await _client.StartOrFailAsync();
        await Task.Delay(50);

        var receivedBytes = Array.Empty<byte>();
        _server.OnBytesReceived += bytes => receivedBytes = bytes;

        var testData = new byte[] { 1, 2, 3, 4, 5 };

        // Act
        var result = _client.TrySendAsBinary(testData);
        await Task.Delay(50);

        // Assert
        Assert.True(result);
        Assert.Equal(testData, receivedBytes);
    }

    [Fact]
    public void Send_WhenNotRunning_ShouldReturnFalse()
    {
        // Arrange
        _client = new ReactiveWebSocketClient(new Uri(_server.WebSocketUrl));

        // Act
        var result = _client.TrySendAsText("test");

        // Assert
        Assert.False(result);
    }

    [Fact(Timeout = 5000)]
    public async Task Send_EmptyString_ShouldReturnFalse()
    {
        // Arrange
        _client = new ReactiveWebSocketClient(new Uri(_server.WebSocketUrl));
        await _client.StartOrFailAsync();

        // Act
        var result = _client.TrySendAsText("");

        // Assert
        Assert.False(result);
    }

    [Fact(Timeout = 5000)]
    public async Task Send_ByteArray2_WhenConnected_ShouldSendMessage()
    {
        // Arrange
        _client = new ReactiveWebSocketClient(new Uri(_server.WebSocketUrl));
        await _client.StartOrFailAsync();
        await Task.Delay(50);

        var receivedBytes = Array.Empty<byte>();
        _server.OnBytesReceived += bytes => receivedBytes = bytes;

        var testData = new byte[] { 10, 20, 30, 40, 50 };

        // Act
        var result = _client.TrySendAsBinary(testData);
        await Task.Delay(50);

        // Assert
        Assert.True(result);
        Assert.Equal(new byte[] { 10, 20, 30, 40, 50 }, receivedBytes);
    }

    [Fact(Timeout = 5000)]
    public async Task SendAsText_ByteArray_WhenConnected_ShouldSendAsText()
    {
        // Arrange
        _client = new ReactiveWebSocketClient(new Uri(_server.WebSocketUrl));
        await _client.StartOrFailAsync();
        await Task.Delay(50);

        var receivedText = "";
        _server.OnMessageReceived += msg => receivedText = msg;

        var testData = "TextMessage"u8.ToArray();

        // Act
        var result = _client.TrySendAsText(testData);
        await Task.Delay(50);

        // Assert
        Assert.True(result);
        Assert.Equal("TextMessage", receivedText);
    }

    [Fact(Timeout = 5000)]
    public async Task SendInstant_String_WhenConnected_ShouldSendImmediately()
    {
        // Arrange
        _client = new ReactiveWebSocketClient(new Uri(_server.WebSocketUrl));
        await _client.StartOrFailAsync();
        await Task.Delay(50);

        var receivedMessage = "";
        _server.OnBytesReceived += msg => receivedMessage = Encoding.UTF8.GetString(msg);

        // Act
        await _client.SendInstantAsync("Instant");
        await Task.Delay(50);

        // Assert
        Assert.Equal("Instant", receivedMessage);
    }

    [Fact(Timeout = 5000)]
    public async Task SendInstant_EmptyString_ShouldNotSend()
    {
        // Arrange
        _client = new ReactiveWebSocketClient(new Uri(_server.WebSocketUrl));
        await _client.StartOrFailAsync();

        // Act & Assert
        await _client.SendInstantAsync("");
    }

    #endregion

    #region Receive Tests

    [Fact(Timeout = 5000)]
    public async Task MessageReceived_WhenServerSendsMessage_ShouldReceive()
    {
        // Arrange
        _client = new ReactiveWebSocketClient(new Uri(_server.WebSocketUrl));
        _client.IsTextMessageConversionEnabled = true;

        var receivedMessage = "";
        _client.MessageReceived.Subscribe(msg => receivedMessage = msg.Text ?? "");

        await _client.StartOrFailAsync();
        await Task.Delay(50);

        // Act
        await _server.SendToAllAsync("Server Message");
        await Task.Delay(50);

        // Assert
        Assert.Equal("Server Message", receivedMessage);
    }

    [Fact(Timeout = 5000)]
    public async Task MessageReceived_BinaryMessage_ShouldReceiveBinary()
    {
        // Arrange
        _client = new ReactiveWebSocketClient(new Uri(_server.WebSocketUrl));
        _client.IsTextMessageConversionEnabled = false;

        byte[]? receivedBytes = null;
        _client.MessageReceived.Subscribe(msg => receivedBytes = msg.Binary);

        await _client.StartOrFailAsync();
        await Task.Delay(50);

        var testData = new byte[] { 1, 2, 3 };

        // Act
        await _server.SendBinaryToAllAsync(testData);
        await Task.Delay(50);

        // Assert
        Assert.NotNull(receivedBytes);
        Assert.Equal(testData, receivedBytes);
    }

    [Fact(Timeout = 5000)]
    public async Task StreamFakeMessage_ShouldTriggerObservable()
    {
        // Arrange
        _client = new ReactiveWebSocketClient(new Uri(_server.WebSocketUrl));
        var received = false;
        _client.MessageReceived.Subscribe(_ => received = true);

        var fakeMessage = ReceivedMessage.TextMessage("Fake");

        // Act
        _client.StreamFakeMessage(fakeMessage);
        await Task.Delay(50);

        // Assert
        Assert.True(received);
    }

    #endregion

    #region Reconnection Tests

    [Fact(Timeout = 5000)]
    public async Task Reconnect_WhenStarted_ShouldReconnect()
    {
        // Arrange
        _client = new ReactiveWebSocketClient(new Uri(_server.WebSocketUrl));
        await _client.StartOrFailAsync();

        var reconnected = false;
        _client.ConnectionHappened
            .Where(c => c.Reason == ConnectReason.Reconnect)
            .Subscribe(_ => reconnected = true);

        // Act
        await _client.ReconnectAsync();
        await Task.Delay(50);

        // Assert
        Assert.True(reconnected);
        Assert.True(_client.IsRunning);
    }

    [Fact(Timeout = 5000)]
    public async Task Reconnect_WhenNotStarted_ShouldDoNothing()
    {
        // Arrange
        _client = new ReactiveWebSocketClient(new Uri(_server.WebSocketUrl));
        var connectionCount = 0;
        _client.ConnectionHappened.Subscribe(_ => connectionCount++);

        // Act
        await _client.ReconnectAsync();
        await Task.Delay(50);

        // Assert
        Assert.Equal(0, connectionCount);
    }

    [Fact(Timeout = 5000)]
    public async Task ReconnectOrFail_WhenNotStarted_ShouldThrow()
    {
        // Arrange
        _client = new ReactiveWebSocketClient(new Uri(_server.WebSocketUrl));

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() => _client.ReconnectOrFailAsync());
    }

    [Fact(Timeout = 5000)]
    public async Task ReconnectOrFail_WhenStarted_ShouldReconnect()
    {
        // Arrange
        _client = new ReactiveWebSocketClient(new Uri(_server.WebSocketUrl));
        await _client.StartOrFailAsync();

        var reconnected = false;
        _client.ConnectionHappened
            .Where(c => c.Reason == ConnectReason.Reconnect)
            .Subscribe(_ => reconnected = true);

        // Act
        await _client.ReconnectOrFailAsync();
        await Task.Delay(50);

        // Assert
        Assert.True(reconnected);
    }

    [Fact(Timeout = 5000)]
    public async Task AutoReconnect_OnConnectionLost_ShouldReconnect()
    {
        // Arrange
        _client = new ReactiveWebSocketClient(new Uri(_server.WebSocketUrl));
        _client.IsReconnectionEnabled = true;
        _client.KeepAliveInterval = TimeSpan.FromMilliseconds(25);

        var reconnected = false;
        _client.ConnectionHappened
            .Where(c => c.Reason == ConnectReason.Reconnect)
            .Subscribe(_ => reconnected = true);

        await _client.StartOrFailAsync();
        await Task.Delay(50);

        // Act
        await _server.DisconnectAllAsync();
        await Task.Delay(50);

        // Assert
        Assert.True(reconnected);
    }

    [Fact(Timeout = 5000)]
    public async Task AutoReconnect_WhenDisabled_ShouldNotReconnect()
    {
        // Arrange
        _client = new ReactiveWebSocketClient(new Uri(_server.WebSocketUrl));
        _client.IsReconnectionEnabled = false;

        var reconnectCount = 0;
        _client.ConnectionHappened
            .Where(c => c.Reason == ConnectReason.Reconnect)
            .Subscribe(_ => reconnectCount++);

        await _client.StartOrFailAsync();
        await Task.Delay(50);

        // Act
        await _server.DisconnectAllAsync();
        await Task.Delay(50);

        // Assert
        Assert.Equal(0, reconnectCount);
    }

    #endregion

    #region Property Tests

    [Fact]
    public void Properties_DefaultValues_ShouldBeCorrect()
    {
        // Arrange & Act
        _client = new ReactiveWebSocketClient(new Uri(_server.WebSocketUrl));

        // Assert
        Assert.Equal(TimeSpan.FromSeconds(10), _client.ConnectTimeout);
        Assert.True(_client.IsReconnectionEnabled);
        Assert.False(_client.IsTextMessageConversionEnabled);
        Assert.Equal(Encoding.UTF8, _client.MessageEncoding);
    }

    [Fact(Timeout = 5000)]
    public async Task NativeClient_WhenConnected_ShouldNotBeNull()
    {
        // Arrange
        _client = new ReactiveWebSocketClient(new Uri(_server.WebSocketUrl));

        // Act
        await _client.StartOrFailAsync();

        // Assert
        Assert.NotNull(_client.NativeClient);
        Assert.Equal(WebSocketState.Open, _client.NativeClient.State);
    }

    [Fact(Timeout = 5000)]
    public async Task SenderRunning_WhenConnected_ShouldBeTrue()
    {
        // Arrange
        _client = new ReactiveWebSocketClient(new Uri(_server.WebSocketUrl));

        // Act
        await _client.StartOrFailAsync();
        await Task.Delay(50);

        // Assert
        Assert.True(_client.SenderRunning);
    }

    #endregion


    #region Integration Tests

    [Fact(Timeout = 50000)]
    public async Task FullWorkflow_ConnectSendReceiveDisconnect_ShouldWork()
    {
        // Arrange
        var connectionTcs = new TaskCompletionSource<bool>();
        _client = new ReactiveWebSocketClient(new Uri(_server.WebSocketUrl));
        _client.IsTextMessageConversionEnabled = true;

        var receivedMessages = new List<string>();
        _client.MessageReceived.Subscribe(msg =>
        {
            receivedMessages.Add(msg.Text ?? "");
            connectionTcs.SetResult(true);
        });

        // Act - Connect
        await _client.StartOrFailAsync();

        // Act - Send
        _client.TrySendAsText("Message 1");
        _client.TrySendAsText("Message 2");

        // Act
        await _server.SendToAllAsync("Server Response");
        await connectionTcs.Task;
        await _client.StopOrFailAsync(WebSocketCloseStatus.NormalClosure, "Done");

        // Assert
        Assert.Contains("Server Response", receivedMessages);
        Assert.False(_client.IsRunning);
    }

    [Fact(Timeout = 5000)]
    public async Task ConcurrentSends_ShouldAllBeProcessed()
    {
        // Arrange
        _client = new ReactiveWebSocketClient(new Uri(_server.WebSocketUrl));
        await _client.StartOrFailAsync();
        await Task.Delay(50);

        var receivedCount = 0;
        _server.OnBytesReceived += _ => Interlocked.Increment(ref receivedCount);

        // Act
        var tasks = Enumerable.Range(0, 50)
            .Select(i => Task.Run(() => _client.TrySendAsBinary($"Message {i}")))
            .ToArray();

        await Task.WhenAll(tasks);
        await Task.Delay(50);

        // Assert
        Assert.Equal(50, receivedCount);
    }

    #endregion
}