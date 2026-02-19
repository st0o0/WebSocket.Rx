using System.Net;
using System.Net.WebSockets;
using NSubstitute;
using WebSocket.Rx.UnitTests.Internal;

namespace WebSocket.Rx.UnitTests;

public class ServerWebSocketAdapterMessageTests(ITestOutputHelper output) : ServerWebSocketAdapterTestBase(output)
{
    [Fact(Timeout = DefaultTimeoutMs)]
    public void Send_WithByteArray_ShouldQueueMessage()
    {
        // Arrange
        Adapter = new ReactiveWebSocketServer.ServerWebSocketAdapter(MockWebSocket,
            new Metadata(Guid.Empty, IPAddress.Any, 0));
        var testData = new byte[] { 1, 2, 3, 4 };

        // Act
        var result = Adapter.TrySend(testData.AsMemory(), WebSocketMessageType.Binary);

        // Assert
        Assert.True(result);
    }

    [Fact(Timeout = DefaultTimeoutMs)]
    public void Send_WithString_ShouldQueueEncodedMessage()
    {
        // Arrange
        Adapter = new ReactiveWebSocketServer.ServerWebSocketAdapter(MockWebSocket,
            new Metadata(Guid.Empty, IPAddress.Any, 0));
        const string testMessage = "Test message";

        // Act
        var result = Adapter.TrySend(testMessage.AsMemory(), WebSocketMessageType.Binary);

        // Assert
        Assert.True(result);
    }

    [Fact(Timeout = DefaultTimeoutMs)]
    public void Send_WithEmptyByteArray_ShouldReturnFalse()
    {
        // Arrange
        Adapter = new ReactiveWebSocketServer.ServerWebSocketAdapter(MockWebSocket,
            new Metadata(Guid.Empty, IPAddress.Any, 0));

        // Act
        var result = Adapter.TrySend(new ReadOnlyMemory<char>([]), WebSocketMessageType.Binary);

        // Assert
        Assert.False(result);
    }

    [Fact(Timeout = DefaultTimeoutMs)]
    public void SendAsText_WithValidMessage_ShouldQueueTextMessage()
    {
        // Arrange
        Adapter = new ReactiveWebSocketServer.ServerWebSocketAdapter(MockWebSocket,
            new Metadata(Guid.Empty, IPAddress.Any, 0));
        const string testMessage = "Text message";

        // Act
        var result = Adapter.TrySend(testMessage.AsMemory(), WebSocketMessageType.Text);

        // Assert
        Assert.True(result);
    }

    [Fact(Timeout = DefaultTimeoutMs)]
    public async Task SendInstant_WithByteArray_ShouldSendImmediately()
    {
        // Arrange
        MockWebSocket.SendAsync(
                Arg.Any<ReadOnlyMemory<byte>>(),
                Arg.Any<WebSocketMessageType>(),
                Arg.Any<bool>(),
                Arg.Any<CancellationToken>())
            .Returns(ValueTask.CompletedTask);

        Adapter = new ReactiveWebSocketServer.ServerWebSocketAdapter(MockWebSocket,
            new Metadata(Guid.Empty, IPAddress.Any, 0));
        var testData = new byte[] { 1, 2, 3 };

        // Act
        await Adapter.SendInstantAsync(testData.AsMemory(), WebSocketMessageType.Binary,
            TestContext.Current.CancellationToken);

        // Assert
        await MockWebSocket.Received(1).SendAsync(
            Arg.Any<ReadOnlyMemory<byte>>(),
            WebSocketMessageType.Binary,
            true,
            Arg.Any<CancellationToken>());
    }

    [Fact(Timeout = DefaultTimeoutMs)]
    public async Task SendInstant_WithString_ShouldSendEncodedMessage()
    {
        // Arrange
        MockWebSocket.SendAsync(
                Arg.Any<ReadOnlyMemory<byte>>(),
                Arg.Any<WebSocketMessageType>(),
                Arg.Any<bool>(),
                Arg.Any<CancellationToken>())
            .Returns(ValueTask.CompletedTask);

        Adapter = new ReactiveWebSocketServer.ServerWebSocketAdapter(MockWebSocket,
            new Metadata(Guid.Empty, IPAddress.Any, 0));
        const string testMessage = "Instant message";

        // Act
        await Adapter.SendInstantAsync(testMessage.AsMemory(), WebSocketMessageType.Binary,
            TestContext.Current.CancellationToken);

        // Assert
        await MockWebSocket.Received(1).SendAsync(
            Arg.Any<ReadOnlyMemory<byte>>(),
            WebSocketMessageType.Binary,
            true,
            Arg.Any<CancellationToken>());
    }

    [Fact(Timeout = DefaultTimeoutMs)]
    public async Task SendInstant_WithClosedSocket_ShouldNotSend()
    {
        // Arrange
        MockWebSocket.State.Returns(WebSocketState.Closed);
        Adapter = new ReactiveWebSocketServer.ServerWebSocketAdapter(MockWebSocket,
            new Metadata(Guid.Empty, IPAddress.Any, 0));
        var testData = new byte[] { 1, 2, 3 };

        // Act
        await Adapter.SendInstantAsync(testData, WebSocketMessageType.Binary, TestContext.Current.CancellationToken);

        // Assert
        await MockWebSocket.DidNotReceive().SendAsync(
            Arg.Any<ReadOnlyMemory<byte>>(),
            Arg.Any<WebSocketMessageType>(),
            Arg.Any<bool>(),
            Arg.Any<CancellationToken>());
    }

    [Fact(Timeout = DefaultTimeoutMs)]
    public async Task StopAsync_ShouldCloseWebSocketGracefully()
    {
        // Arrange
        MockWebSocket.CloseAsync(
                Arg.Any<WebSocketCloseStatus>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        Adapter = new ReactiveWebSocketServer.ServerWebSocketAdapter(MockWebSocket,
            new Metadata(Guid.Empty, IPAddress.Any, 0));

        // Act
        var result = await Adapter.StopAsync(
            WebSocketCloseStatus.NormalClosure,
            "Test stop",
            TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result);
        await MockWebSocket.Received(1).CloseAsync(
            WebSocketCloseStatus.NormalClosure,
            "Test stop",
            Arg.Any<CancellationToken>());
    }

    [Fact(Timeout = DefaultTimeoutMs)]
    public async Task StopAsync_WhenCloseAsyncThrows_ShouldAbortConnection()
    {
        // Arrange
        MockWebSocket.CloseAsync(
                Arg.Any<WebSocketCloseStatus>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(_ => throw new WebSocketException());

        Adapter = new ReactiveWebSocketServer.ServerWebSocketAdapter(MockWebSocket,
            new Metadata(Guid.Empty, IPAddress.Any, 0));

        // Act
        var result = await Adapter.StopAsync(
            WebSocketCloseStatus.NormalClosure,
            "Test",
            TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result);
        MockWebSocket.Received(1).Abort();
    }
}