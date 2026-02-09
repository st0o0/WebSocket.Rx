using System.Text;
using R3;
using WebSocket.Rx.Tests.Internal;

namespace WebSocket.Rx.Tests;

public class ReactiveWebSocketClientSendingTests(ITestOutputHelper output) : ReactiveWebSocketClientTestBase(output)
{
    [Fact(Timeout = DefaultTimeoutMs)]
    public async Task Send_String_WhenConnected_ShouldSendMessage()
    {
        // Arrange
        Client = new ReactiveWebSocketClient(new Uri(Server.WebSocketUrl));
        await Client.StartOrFailAsync(TestContext.Current.CancellationToken);
        await Task.Delay(50, TestContext.Current.CancellationToken);

        var receivedMessage = "";
        Server.OnMessageReceived += msg => receivedMessage = msg;

        // Act
        var result = Client.TrySendAsText("Hello World");
        await Task.Delay(50, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result);
        Assert.Equal("Hello World", receivedMessage);
    }

    [Fact(Timeout = DefaultTimeoutMs)]
    public async Task Send_ByteArray_WhenConnected_ShouldSendMessage()
    {
        // Arrange
        Client = new ReactiveWebSocketClient(new Uri(Server.WebSocketUrl));
        await Client.StartOrFailAsync(TestContext.Current.CancellationToken);
        await Task.Delay(50, TestContext.Current.CancellationToken);

        var receivedBytes = Array.Empty<byte>();
        Server.OnBytesReceived += bytes => receivedBytes = bytes;

        var testData = new byte[] { 1, 2, 3, 4, 5 };

        // Act
        var result = Client.TrySendAsBinary(testData);
        await Task.Delay(50, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result);
        Assert.Equal(testData, receivedBytes);
    }

    [Fact(Timeout = DefaultTimeoutMs)]
    public void Send_WhenNotRunning_ShouldReturnFalse()
    {
        // Arrange
        Client = new ReactiveWebSocketClient(new Uri(Server.WebSocketUrl));

        // Act
        var result = Client.TrySendAsText("test");

        // Assert
        Assert.False(result);
    }

    [Fact(Timeout = DefaultTimeoutMs)]
    public async Task Send_EmptyString_ShouldReturnFalse()
    {
        // Arrange
        Client = new ReactiveWebSocketClient(new Uri(Server.WebSocketUrl));
        await Client.StartOrFailAsync(TestContext.Current.CancellationToken);

        // Act
        var result = Client.TrySendAsText("");

        // Assert
        Assert.False(result);
    }

    [Fact(Timeout = DefaultTimeoutMs)]
    public async Task SendInstant_String_WhenConnected_ShouldSendImmediately()
    {
        // Arrange
        Client = new ReactiveWebSocketClient(new Uri(Server.WebSocketUrl));
        await Client.StartOrFailAsync(TestContext.Current.CancellationToken);
        await Task.Delay(50, TestContext.Current.CancellationToken);

        var receivedMessage = "";
        Server.OnBytesReceived += msg => receivedMessage = Encoding.UTF8.GetString(msg);

        // Act
        await Client.SendInstantAsync("Instant", TestContext.Current.CancellationToken);
        await Task.Delay(50, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("Instant", receivedMessage);
    }

    [Fact(Timeout = DefaultTimeoutMs)]
    public async Task SendInstantAsync_ByteArray_WhenConnected_ShouldSendMessage()
    {
        // Arrange
        Client = new ReactiveWebSocketClient(new Uri(Server.WebSocketUrl));
        await Client.StartOrFailAsync(TestContext.Current.CancellationToken);

        var tcs = new TaskCompletionSource<byte[]>();
        Server.OnBytesReceived += bytes => tcs.TrySetResult(bytes);

        var testData = new byte[] { 5, 4, 3, 2, 1 };

        // Act
        var result = await Client.SendInstantAsync(testData, TestContext.Current.CancellationToken);
        var receivedBytes = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result);
        Assert.Equal(testData, receivedBytes);
    }

    [Fact(Timeout = DefaultTimeoutMs)]
    public async Task SendAsBinaryAsync_String_WhenConnected_ShouldSendMessageAsBinary()
    {
        // Arrange
        Client = new ReactiveWebSocketClient(new Uri(Server.WebSocketUrl));
        await Client.StartOrFailAsync(TestContext.Current.CancellationToken);

        var tcs = new TaskCompletionSource<byte[]>();
        Server.OnBytesReceived += bytes => tcs.TrySetResult(bytes);

        // Act
        var result = await Client.SendAsBinaryAsync("BinaryString", TestContext.Current.CancellationToken);
        var receivedBytes = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result);
        Assert.Equal(Client.MessageEncoding.GetBytes("BinaryString"), receivedBytes);
    }

    [Fact(Timeout = DefaultTimeoutMs)]
    public async Task SendAsTextAsync_String_WhenConnected_ShouldSendMessageAsText()
    {
        // Arrange
        Client = new ReactiveWebSocketClient(new Uri(Server.WebSocketUrl));
        await Client.StartOrFailAsync(TestContext.Current.CancellationToken);

        var tcs = new TaskCompletionSource<string>();
        Server.OnMessageReceived += msg => tcs.TrySetResult(msg);

        // Act
        var result = await Client.SendAsTextAsync("Hello Text", TestContext.Current.CancellationToken);
        var receivedText = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result);
        Assert.Equal("Hello Text", receivedText);
    }

    [Fact(Timeout = DefaultTimeoutMs)]
    public async Task SendInstant_Observable_ShouldSendMessages()
    {
        // Arrange
        Client = new ReactiveWebSocketClient(new Uri(Server.WebSocketUrl));
        await Client.StartOrFailAsync(TestContext.Current.CancellationToken);

        var receivedMessages = new List<string>();
        var tcs = new TaskCompletionSource<bool>();
        Server.OnBytesReceived += bytes =>
        {
            receivedMessages.Add(Encoding.UTF8.GetString(bytes));
            if (receivedMessages.Count == 2) tcs.TrySetResult(true);
        };

        var messages = Observable.Return("Msg1").Concat(Observable.Return("Msg2"));

        // Act
        using var subscription = Client.SendInstant(messages).Subscribe();
        await tcs.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);

        // Assert
        Assert.Contains("Msg1", receivedMessages);
        Assert.Contains("Msg2", receivedMessages);
    }

    [Fact(Timeout = DefaultTimeoutMs)]
    public async Task ConcurrentSends_ShouldAllBeProcessed()
    {
        // Arrange
        Client = new ReactiveWebSocketClient(new Uri(Server.WebSocketUrl));
        await Client.StartOrFailAsync(TestContext.Current.CancellationToken);
        await Task.Delay(50, TestContext.Current.CancellationToken);

        var receivedCount = 0;
        Server.OnBytesReceived += _ => Interlocked.Increment(ref receivedCount);

        // Act
        var tasks = Enumerable.Range(0, 50)
            .Select(i => Task.Run(() => Client.TrySendAsBinary($"Message {i}")))
            .ToArray();

        await Task.WhenAll(tasks);
        await Task.Delay(50, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(50, receivedCount);
    }
}