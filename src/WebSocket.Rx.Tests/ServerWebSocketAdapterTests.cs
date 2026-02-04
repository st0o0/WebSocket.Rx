using System.Net;
using System.Net.WebSockets;
using NSubstitute;

namespace WebSocket.Rx.Tests;

public class ServerWebSocketAdapterTests : IAsyncLifetime
{
    private System.Net.WebSockets.WebSocket? _mockWebSocket;
    private ReactiveWebSocketServer.ServerWebSocketAdapter? _adapter;

    public async Task InitializeAsync()
    {
        _mockWebSocket = Substitute.For<System.Net.WebSockets.WebSocket>();
        _mockWebSocket.State.Returns(WebSocketState.Open);
        await Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        _adapter?.Dispose();
        await Task.CompletedTask;
    }

    [Fact]
    public void Constructor_ShouldInitializeAdapter()
    {
        // Act
        _adapter = new ReactiveWebSocketServer.ServerWebSocketAdapter(_mockWebSocket,
            new Metadata(Guid.Empty, IPAddress.Any, 0));

        // Assert
        Assert.Equal(_mockWebSocket, _adapter.NativeServerSocket);
        Assert.True(_adapter.IsStarted);
        Assert.True(_adapter.IsRunning);
    }

    [Fact]
    public void Constructor_WithNullSocket_ShouldThrowArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new ReactiveWebSocketServer.ServerWebSocketAdapter(null, new Metadata(Guid.Empty, IPAddress.Any, 0)));
    }

    // [Fact]
    // public void Send_WithByteArray_ShouldQueueMessage()
    // {
    //     // Arrange
    //     _adapter = new ReactiveWebSocketServer.ServerWebSocketAdapter(_mockWebSocket);
    //     var testData = new byte[] { 1, 2, 3, 4 };
    //
    //     // Act
    //     var result = _adapter.Send(testData);
    //
    //     // Assert
    //     Assert.True(result);
    // }
    //
    // [Fact]
    // public void Send_WithString_ShouldQueueEncodedMessage()
    // {
    //     // Arrange
    //     _adapter = new ReactiveWebSocketServer.ServerWebSocketAdapter(_mockWebSocket);
    //     var testMessage = "Test message";
    //
    //     // Act
    //     var result = _adapter.Send(testMessage);
    //
    //     // Assert
    //     Assert.True(result);
    // }
    //
    // [Fact]
    // public void Send_WithEmptyByteArray_ShouldReturnFalse()
    // {
    //     // Arrange
    //     _adapter = new ReactiveWebSocketServer.ServerWebSocketAdapter(_mockWebSocket);
    //
    //     // Act
    //     var result = _adapter.Send([]);
    //
    //     // Assert
    //     Assert.False(result);
    // }
    //
    // [Fact]
    // public void Send_WithEmptyString_ShouldReturnFalse()
    // {
    //     // Arrange
    //     _adapter = new ReactiveWebSocketServer.ServerWebSocketAdapter(_mockWebSocket);
    //
    //     // Act
    //     var result = _adapter.Send(string.Empty);
    //
    //     // Assert
    //     Assert.False(result);
    // }
    //
    // [Fact]
    // public void Send_WithNullString_ShouldReturnFalse()
    // {
    //     // Arrange
    //     _adapter = new ReactiveWebSocketServer.ServerWebSocketAdapter(_mockWebSocket);
    //
    //     // Act
    //     var result = _adapter.Send((string)null);
    //
    //     // Assert
    //     Assert.False(result);
    // }
    //
    // [Fact]
    // public void SendAsText_WithValidMessage_ShouldQueueTextMessage()
    // {
    //     // Arrange
    //     _adapter = new ReactiveWebSocketServer.ServerWebSocketAdapter(_mockWebSocket);
    //     var testMessage = "Text message";
    //
    //     // Act
    //     var result = _adapter.SendAsText(testMessage);
    //
    //     // Assert
    //     Assert.True(result);
    // }
    //
    // [Fact]
    // public void SendAsText_WithEmptyString_ShouldReturnFalse()
    // {
    //     // Arrange
    //     _adapter = new ReactiveWebSocketServer.ServerWebSocketAdapter(_mockWebSocket);
    //
    //     // Act
    //     var result = _adapter.SendAsText(string.Empty);
    //
    //     // Assert
    //     Assert.False(result);
    // }
    //
    // [Fact]
    // public async Task SendInstant_WithByteArray_ShouldSendImmediately()
    // {
    //     // Arrange
    //     _mockWebSocket.SendAsync(
    //             Arg.Any<ArraySegment<byte>>(),
    //             Arg.Any<WebSocketMessageType>(),
    //             Arg.Any<bool>(),
    //             Arg.Any<CancellationToken>())
    //         .Returns(Task.CompletedTask);
    //
    //     _adapter = new ReactiveWebSocketServer.ServerWebSocketAdapter(_mockWebSocket);
    //     var testData = new byte[] { 1, 2, 3 };
    //
    //     // Act
    //     await _adapter.SendInstant(testData);
    //
    //     // Assert
    //     await _mockWebSocket.Received(1).SendAsync(
    //         Arg.Any<ArraySegment<byte>>(),
    //         WebSocketMessageType.Binary,
    //         true,
    //         Arg.Any<CancellationToken>());
    // }
    //
    // [Fact]
    // public async Task SendInstant_WithString_ShouldSendEncodedMessage()
    // {
    //     // Arrange
    //     _mockWebSocket.SendAsync(
    //             Arg.Any<ArraySegment<byte>>(),
    //             Arg.Any<WebSocketMessageType>(),
    //             Arg.Any<bool>(),
    //             Arg.Any<CancellationToken>())
    //         .Returns(Task.CompletedTask);
    //
    //     _adapter = new ReactiveWebSocketServer.ServerWebSocketAdapter(_mockWebSocket);
    //     var testMessage = "Instant message";
    //
    //     // Act
    //     await _adapter.SendInstant(testMessage);
    //
    //     // Assert
    //     await _mockWebSocket.Received(1).SendAsync(
    //         Arg.Any<ArraySegment<byte>>(),
    //         WebSocketMessageType.Binary,
    //         true,
    //         Arg.Any<CancellationToken>());
    // }
    //
    // [Fact]
    // public async Task SendInstant_WithClosedSocket_ShouldNotSend()
    // {
    //     // Arrange
    //     _mockWebSocket.State.Returns(WebSocketState.Closed);
    //     _adapter = new ReactiveWebSocketServer.ServerWebSocketAdapter(_mockWebSocket);
    //     var testData = new byte[] { 1, 2, 3 };
    //
    //     // Act
    //     await _adapter.SendInstant(testData);
    //
    //     // Assert
    //     await _mockWebSocket.DidNotReceive().SendAsync(
    //         Arg.Any<ArraySegment<byte>>(),
    //         Arg.Any<WebSocketMessageType>(),
    //         Arg.Any<bool>(),
    //         Arg.Any<CancellationToken>());
    // }
    //
    // [Fact]
    // public async Task SendInstant_WithEmptyByteArray_ShouldNotSend()
    // {
    //     // Arrange
    //     _adapter = new ReactiveWebSocketServer.ServerWebSocketAdapter(_mockWebSocket);
    //
    //     // Act
    //     await _adapter.SendInstant([]);
    //
    //     // Assert
    //     await _mockWebSocket.DidNotReceive().SendAsync(
    //         Arg.Any<ArraySegment<byte>>(),
    //         Arg.Any<WebSocketMessageType>(),
    //         Arg.Any<bool>(),
    //         Arg.Any<CancellationToken>());
    // }
    //
    // [Fact]
    // public async Task SendInstant_WithNullOrEmptyString_ShouldNotSend()
    // {
    //     // Arrange
    //     _adapter = new ReactiveWebSocketServer.ServerWebSocketAdapter(_mockWebSocket);
    //
    //     // Act
    //     await _adapter.SendInstant(string.Empty);
    //
    //     // Assert
    //     await _mockWebSocket.DidNotReceive().SendAsync(
    //         Arg.Any<ArraySegment<byte>>(),
    //         Arg.Any<WebSocketMessageType>(),
    //         Arg.Any<bool>(),
    //         Arg.Any<CancellationToken>());
    // }

    [Fact]
    public async Task StopAsync_ShouldCloseWebSocketGracefully()
    {
        // Arrange
        _mockWebSocket.CloseAsync(
                Arg.Any<WebSocketCloseStatus>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        _adapter = new ReactiveWebSocketServer.ServerWebSocketAdapter(_mockWebSocket,
            new Metadata(Guid.Empty, IPAddress.Any, 0));

        // Act
        var result = await _adapter.StopAsync(
            WebSocketCloseStatus.NormalClosure,
            "Test stop");

        // Assert
        Assert.True(result);
        await _mockWebSocket.Received(1).CloseAsync(
            WebSocketCloseStatus.NormalClosure,
            "Test stop",
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StopAsync_WhenCloseAsyncThrows_ShouldAbortConnection()
    {
        // Arrange
        _mockWebSocket.CloseAsync(
                Arg.Any<WebSocketCloseStatus>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(x => throw new WebSocketException());

        _adapter = new ReactiveWebSocketServer.ServerWebSocketAdapter(_mockWebSocket,
            new Metadata(Guid.Empty, IPAddress.Any, 0));

        // Act
        var result = await _adapter.StopAsync(
            WebSocketCloseStatus.NormalClosure,
            "Test");

        // Assert
        Assert.True(result);
        _mockWebSocket.Received(1).Abort();
    }

    [Fact]
    public void Dispose_ShouldCleanupResources()
    {
        // Arrange
        _adapter = new ReactiveWebSocketServer.ServerWebSocketAdapter(_mockWebSocket,
            new Metadata(Guid.Empty, IPAddress.Any, 0));

        // Act
        _adapter.Dispose();

        // Assert
        Assert.True(_adapter.IsDisposed);
    }

    [Fact]
    public void Dispose_CalledTwice_ShouldNotThrow()
    {
        // Arrange
        _adapter = new ReactiveWebSocketServer.ServerWebSocketAdapter(_mockWebSocket,
            new Metadata(Guid.Empty, IPAddress.Any, 0));
        _adapter.Dispose();

        // Act
        var exception = Record.Exception(() => _adapter.Dispose());

        // Assert
        Assert.Null(exception);
    }
}