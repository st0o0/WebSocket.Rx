using System.Net.WebSockets;
using R3;
using WebSocket.Rx.IntegrationTests.Internal;

namespace WebSocket.Rx.IntegrationTests;

public class ReactiveWebSocketClientLifecycleTests(ITestOutputHelper output) : ReactiveWebSocketClientTestBase(output)
{
    [Fact(Timeout = DefaultTimeoutMs)]
    public void Constructor_WithValidUri_ShouldSetProperties()
    {
        // Arrange & Act
        var uri = new Uri(Server.WebSocketUrl);
        Client = new ReactiveWebSocketClient(uri);

        // Assert
        Assert.Equal(uri, Client.Url);
        Assert.False(Client.IsStarted);
        Assert.False(Client.IsRunning);
        Assert.NotNull(Client.MessageReceived);
        Assert.NotNull(Client.ConnectionHappened);
        Assert.NotNull(Client.DisconnectionHappened);
    }

    [Fact(Timeout = DefaultTimeoutMs)]
    public void Constructor_WithNullUri_ShouldThrowArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new ReactiveWebSocketClient(null!));
    }

    [Fact(Timeout = DefaultTimeoutMs)]
    public async Task StartOrFail_WhenNotStarted_ShouldConnect()
    {
        // Arrange
        Client = new ReactiveWebSocketClient(new Uri(Server.WebSocketUrl));
        var connected = false;
        Client.ConnectionHappened.Subscribe(_ => connected = true);

        // Act
        await Client.StartOrFailAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.True(Client.IsStarted);
        Assert.True(Client.IsRunning);
        await WaitUntilAsync(Client.ConnectionHappened, () => Client.IsRunning);
        Assert.True(connected);
    }

    [Fact(Timeout = DefaultTimeoutMs)]
    public async Task StartOrFail_WhenAlreadyStarted_ShouldNotConnectAgain()
    {
        // Arrange
        Client = new ReactiveWebSocketClient(new Uri(Server.WebSocketUrl));
        await Client.StartOrFailAsync(TestContext.Current.CancellationToken);
        var connectionCount = 0;
        Client.ConnectionHappened.Subscribe(_ => connectionCount++);

        // Act
        await Client.StartOrFailAsync(TestContext.Current.CancellationToken);
        await Task.Delay(200, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(0, connectionCount);
    }

    [Fact(Timeout = DefaultTimeoutMs)]
    public async Task Start_WithConnectionError_ShouldNotThrow()
    {
        // Arrange
        Client = new ReactiveWebSocketClient(new Uri("ws://127.0.0.1:1/invalid"));
        Client.ConnectTimeout = TimeSpan.FromSeconds(1);
        var error = false;
        Client.ErrorOccurred.Subscribe(d => error = true);

        // Act
        await Client.StartAsync(TestContext.Current.CancellationToken);

        // Assert
        await WaitForConditionAsync(() => error, TimeSpan.FromSeconds(5), "ErrorOccurred was not observed in time");
        Assert.True(error);
    }

    [Fact(Timeout = DefaultTimeoutMs)]
    public async Task StartOrFail_WithInvalidUri_ShouldThrow()
    {
        // Arrange
        Client = new ReactiveWebSocketClient(new Uri("ws://localhost:9999/invalid"));
        Client.ConnectTimeout = TimeSpan.FromMilliseconds(500);

        // Act & Assert
        await Assert.ThrowsAnyAsync<Exception>(() => Client.StartOrFailAsync(TestContext.Current.CancellationToken));
    }

    [Fact(Timeout = DefaultTimeoutMs)]
    public async Task Stop_WhenRunning_ShouldDisconnect()
    {
        // Arrange
        Client = new ReactiveWebSocketClient(new Uri(Server.WebSocketUrl));
        await Client.StartOrFailAsync(TestContext.Current.CancellationToken);
        var disconnected = new TaskCompletionSource<bool>();
        var disconnectedTask = disconnected.Task;
        Client.DisconnectionHappened
            .Where(x => x.Reason is DisconnectReason.ClientInitiated)
            .Take(1)
            .Subscribe(x => disconnected.SetResult(true));

        // Act
        var result = await Client.StopAsync(WebSocketCloseStatus.NormalClosure, "Test stop",
            TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result);
        Assert.False(Client.IsStarted);
        Assert.False(Client.IsRunning);
        Assert.True(await disconnectedTask.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
        Assert.True(disconnectedTask.IsCompletedSuccessfully);
    }

    [Fact(Timeout = DefaultTimeoutMs)]
    public async Task Stop_WhenNotStarted_ShouldReturnFalse()
    {
        // Arrange
        Client = new ReactiveWebSocketClient(new Uri(Server.WebSocketUrl));

        // Act
        var result = await Client.StopAsync(WebSocketCloseStatus.NormalClosure, "Test",
            TestContext.Current.CancellationToken);

        // Assert
        Assert.False(result);
    }

    [Fact(Timeout = DefaultTimeoutMs)]
    public async Task StopOrFail_WhenRunning_ShouldStopSuccessfully()
    {
        // Arrange
        Client = new ReactiveWebSocketClient(new Uri(Server.WebSocketUrl));
        await Client.StartOrFailAsync(TestContext.Current.CancellationToken);

        // Act
        var result = await Client.StopOrFailAsync(WebSocketCloseStatus.NormalClosure, "Test",
            TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result);
        Assert.False(Client.IsStarted);
    }

    [Fact(Timeout = DefaultTimeoutMs)]
    public void Dispose_ShouldMarkAsDisposed()
    {
        // Arrange
        Client = new ReactiveWebSocketClient(new Uri(Server.WebSocketUrl));

        // Act
        Client.Dispose();

        // Assert
        Assert.True(Client.IsDisposed);
    }

    [Fact(Timeout = DefaultTimeoutMs)]
    public async Task DisposeAsync_ShouldMarkAsDisposed()
    {
        // Arrange
        Client = new ReactiveWebSocketClient(new Uri(Server.WebSocketUrl));

        // Act
        await Client.DisposeAsync();

        // Assert
        Assert.True(Client.IsDisposed);
    }

    [Fact(Timeout = DefaultTimeoutMs)]
    public void Dispose_MultipleTimes_ShouldBeIdempotent()
    {
        // Arrange
        Client = new ReactiveWebSocketClient(new Uri(Server.WebSocketUrl));

        // Act
        Client.Dispose();
        Client.Dispose();
        Client.Dispose();

        // Assert
        Assert.True(Client.IsDisposed);
    }

    [Fact(Timeout = DefaultTimeoutMs)]
    public async Task DisposeAsync_MultipleTimes_ShouldBeIdempotent()
    {
        // Arrange
        Client = new ReactiveWebSocketClient(new Uri(Server.WebSocketUrl));

        // Act
        await Client.DisposeAsync();
        await Client.DisposeAsync();
        await Client.DisposeAsync();

        // Assert
        Assert.True(Client.IsDisposed);
    }

    [Fact(Timeout = DefaultTimeoutMs)]
    public async Task AfterDispose_OperationsShouldThrowOrReturnFalse()
    {
        // Arrange
        Client = new ReactiveWebSocketClient(new Uri(Server.WebSocketUrl));
        var exceptionSource = new TaskCompletionSource<ErrorOccurred>();
        Client.ErrorOccurred.Subscribe(msg => exceptionSource.TrySetResult(msg));

        // Act
        Client.Dispose();

        // Assert
        await Client.StartAsync(TestContext.Current.CancellationToken);
        var taskResult =
            await exceptionSource.Task.WaitAsync(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);
        Assert.IsType<ObjectDisposedException>(taskResult.Exception);

        var result = Client.TrySendAsText("test");
        Assert.False(result);
    }

    [Fact(Timeout = DefaultTimeoutMs)]
    public async Task Dispose_ShouldCompleteAllObservables()
    {
        // Arrange
        Client = new ReactiveWebSocketClient(new Uri(Server.WebSocketUrl));
        var messageCompleted = false;
        var connectionCompleted = false;
        var disconnectionCompleted = false;

        Client.MessageReceived.Subscribe(
            _ => { },
            _ => { },
            _ => messageCompleted = true
        );

        Client.ConnectionHappened.Subscribe(
            _ => { },
            _ => { },
            _ => connectionCompleted = true
        );

        Client.DisconnectionHappened.Subscribe(
            _ => { },
            _ => { },
            _ => disconnectionCompleted = true
        );

        // Act
        await Client.DisposeAsync();
        await Task.Delay(50, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(messageCompleted);
        Assert.True(connectionCompleted);
        Assert.True(disconnectionCompleted);
    }

    [Fact(Timeout = DefaultTimeoutMs)]
    public async Task Dispose_WhileRunning_ShouldStopGracefully()
    {
        // Arrange
        Client = new ReactiveWebSocketClient(new Uri(Server.WebSocketUrl));
        await Client.StartAsync(TestContext.Current.CancellationToken);
        await Task.Delay(50, TestContext.Current.CancellationToken);

        // Act
        await Client.DisposeAsync();

        // Assert
        Assert.True(Client.IsDisposed);
        Assert.False(Client.IsRunning);
        Assert.False(Client.IsStarted);
    }

    [Fact(Timeout = DefaultTimeoutMs)]
    public void Finalizer_ShouldNotThrow()
    {
        CreateAndAbandonClient();
        GC.Collect();
        GC.WaitForPendingFinalizers();

        // Assert
        Assert.True(true);
        return;

        // Arrange & Act
        void CreateAndAbandonClient()
        {
            var _ = new ReactiveWebSocketClient(new Uri("ws://localhost:8080"));
        }
    }
}