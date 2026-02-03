using System.Buffers;
using System.Net.WebSockets;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Text;
using System.Threading.Channels;
using WebSocket.Rx.Internal;

namespace WebSocket.Rx;

public class ReactiveWebSocketClient : IReactiveWebSocketClient
{
    private CancellationTokenSource? _mainCts;
    private CancellationTokenSource? _reconnectCts;
    private readonly AsyncLock _connectionLock = new();

    private bool _isReconnecting;
    private bool _isDisposing;
    private DateTime _lastMessageReceived = DateTime.UtcNow;

    private readonly Subject<ReceivedMessage> _messageReceived = new();
    private readonly Subject<Connected> _connectionHappened = new();
    private readonly Subject<Disconnected> _disconnectionHappened = new();

    private readonly Channel<Payload> _sendChannel;
    private Task? _sendLoopTask;
    private Task? _receiveLoopTask;
    private Task? _heartbeatTask;

    public Uri Url { get; set; }
    public TimeSpan ConnectTimeout { get; set; } = TimeSpan.FromSeconds(10);

    public TimeSpan InactivityTimeout { get; set; } = TimeSpan.FromSeconds(30);
    public bool IsReconnectionEnabled { get; set; } = true;
    public string? Name { get; set; }
    public bool IsStarted { get; private set; }
    public bool IsRunning { get; private set; }
    public bool SenderRunning => _sendLoopTask!.Status is TaskStatus.Running or TaskStatus.WaitingForActivation;
    public bool IsInsideLock => _connectionLock.IsLocked;
    public bool IsTextMessageConversionEnabled { get; set; }
    public Encoding MessageEncoding { get; set; } = Encoding.UTF8;
    public ClientWebSocket? NativeClient { get; private set; }

    public IObservable<ReceivedMessage> MessageReceived => _messageReceived.AsObservable();
    public IObservable<Connected> ConnectionHappened => _connectionHappened.AsObservable();
    public IObservable<Disconnected> DisconnectionHappened => _disconnectionHappened.AsObservable();

    public ReactiveWebSocketClient(Uri url)
    {
        Url = url ?? throw new ArgumentNullException(nameof(url));
        _sendChannel = Channel.CreateUnbounded<Payload>(
            new UnboundedChannelOptions { SingleReader = true }
        );
    }

    #region Start/Stop

    public async Task Start()
    {
        try
        {
            await StartOrFail();
        }
        catch (Exception ex)
        {
            _disconnectionHappened.OnNext(Disconnected.Create(DisconnectReason.Error, ex));
        }
    }

    public async Task StartOrFail()
    {
        using (await _connectionLock.LockAsync())
        {
            if (IsStarted)
            {
                return;
            }

            await ConnectInternalAsync(ConnectReason.Initial, throwOnError: true);
        }
    }

    public async Task<bool> Stop(WebSocketCloseStatus status, string statusDescription)
    {
        try
        {
            return await StopOrFail(status, statusDescription);
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> StopOrFail(WebSocketCloseStatus status, string statusDescription)
    {
        using (await _connectionLock.LockAsync())
        {
            if (!IsStarted)
            {
                return false;
            }

            IsStarted = false;
            IsRunning = false;

            _reconnectCts?.Cancel();
            _mainCts?.Cancel();

            if (NativeClient?.State == WebSocketState.Open)
            {
                try
                {
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                    await NativeClient.CloseAsync(status, statusDescription, cts.Token);
                }
                catch (OperationCanceledException)
                {
                    NativeClient.Try(x => x.Abort());
                }
                catch (WebSocketException)
                {
                    NativeClient.Try(x => x.Abort());
                }
            }

            _disconnectionHappened.OnNext(Disconnected.Create(DisconnectReason.ClientInitiated));

            await CleanupAsync();
            return true;
        }
    }

    #endregion

    #region Reconnection

    /// <summary>
    /// Force reconnection. Tries indefinitely on errors.
    /// </summary>
    public async Task Reconnect()
    {
        if (!IsStarted)
        {
            return;
        }

        try
        {
            using (await _connectionLock.LockAsync())
            {
                await ReconnectInternalAsync(DisconnectReason.ClientInitiated, throwOnError: false);
            }
        }
        catch (Exception ex)
        {
            _ = ex;
        }
    }

    /// <summary>
    /// Force reconnection. Throws on connection error.
    /// </summary>
    public async Task ReconnectOrFail()
    {
        if (!IsStarted)
        {
            throw new InvalidOperationException("Client not started");
        }

        using (await _connectionLock.LockAsync())
        {
            await ReconnectInternalAsync(DisconnectReason.ClientInitiated, throwOnError: true);
        }
    }

    private async Task ReconnectInternalAsync(DisconnectReason reason, bool throwOnError)
    {
        if (_isDisposing || !IsStarted)
        {
            return;
        }

        if (_isReconnecting)
        {
            return;
        }

        _isReconnecting = true;
        IsRunning = false;

        try
        {
            _disconnectionHappened.OnNext(Disconnected.Create(reason));

            _mainCts?.Cancel();

            NativeClient?.Try(x => x.Abort());
            NativeClient?.Dispose();

            await Task.WhenAll(
                _sendLoopTask ?? Task.CompletedTask,
                _receiveLoopTask ?? Task.CompletedTask,
                _heartbeatTask ?? Task.CompletedTask
            );

            await ConnectInternalAsync(ConnectReason.Reconnect, throwOnError);
        }
        finally
        {
            _isReconnecting = false;
        }
    }

    private async Task ConnectInternalAsync(ConnectReason reason, bool throwOnError)
    {
        _mainCts = new CancellationTokenSource();
        NativeClient = new ClientWebSocket();

        try
        {
            using var timeoutCts = new CancellationTokenSource(ConnectTimeout);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                _mainCts.Token,
                timeoutCts.Token
            );

            await NativeClient.ConnectAsync(Url, linkedCts.Token);

            IsStarted = true;
            IsRunning = true;
            _lastMessageReceived = DateTime.UtcNow;

            _sendLoopTask = Task.Run(async () => await SendLoopAsync(_mainCts.Token), CancellationToken.None);
            _receiveLoopTask = Task.Run(async () => await ReceiveLoopAsync(_mainCts.Token), CancellationToken.None);
            _heartbeatTask = Task.Run(async () => await HeartbeatMonitorAsync(_mainCts.Token), CancellationToken.None);

            _connectionHappened.OnNext(Connected.Create(reason));
        }
        catch (Exception)
        {
            NativeClient?.Dispose();
            _mainCts?.Cancel();
            _mainCts?.Dispose();

            if (throwOnError)
            {
                throw;
            }

            if (IsReconnectionEnabled && reason == ConnectReason.Reconnect)
            {
                _ = ScheduleReconnectAsync(DisconnectReason.Error);
            }
        }
    }

    private async Task ScheduleReconnectAsync(DisconnectReason reason)
    {
        if (!IsReconnectionEnabled || _isDisposing || !IsStarted)
        {
            return;
        }

        _reconnectCts = new CancellationTokenSource();

        try
        {
            await Task.Delay(InactivityTimeout, _reconnectCts.Token);

            using (await _connectionLock.LockAsync())
            {
                await ReconnectInternalAsync(reason, throwOnError: false);
            }
        }
        catch (OperationCanceledException ex)
        {
            _ = ex;
        }
    }

    #endregion

    #region Heartbeat Monitor

    private async Task HeartbeatMonitorAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));

        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                var timeSinceLastMessage = DateTime.UtcNow - _lastMessageReceived;

                if (timeSinceLastMessage <= InactivityTimeout) continue;

                if (NativeClient?.State == WebSocketState.Open)
                {
                    using (await _connectionLock.LockAsync())
                    {
                        await ReconnectInternalAsync(DisconnectReason.Timeout, throwOnError: false);
                    }
                }

                break;
            }
        }
        catch (OperationCanceledException ex)
        {
            _ = ex;
        }
    }

    #endregion

    #region Send/Receive Loops

    private async Task SendLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var (data, messageType) in _sendChannel.Reader.ReadAllAsync(cancellationToken))
            {
                if (NativeClient?.State == WebSocketState.Open)
                {
                    await NativeClient.SendAsync(
                        new ArraySegment<byte>(data),
                        messageType,
                        endOfMessage: true,
                        cancellationToken
                    );
                }
            }
        }
        catch (OperationCanceledException ex)
        {
            _ = ex;
        }
        catch (Exception ex)
        {
            _disconnectionHappened.OnNext(Disconnected.Create(DisconnectReason.Error, ex));
            _ = ScheduleReconnectAsync(DisconnectReason.Error);
        }
    }

    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        var buffer = new byte[1024 * 16];

        try
        {
            while (NativeClient?.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
            {
                using var ms = new MemoryStream();
                WebSocketReceiveResult result;

                do
                {
                    result = await NativeClient.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);
                    ms.Write(buffer, 0, result.Count);
                } while (!result.EndOfMessage);

                _lastMessageReceived = DateTime.UtcNow;

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    _disconnectionHappened.OnNext(Disconnected.Create(DisconnectReason.ServerInitiated));
                    _ = ScheduleReconnectAsync(DisconnectReason.ServerInitiated);
                    return;
                }

                var messageBytes = ms.ToArray();
                var message = ReceivedMessage.BinaryMessage(messageBytes);

                if (IsTextMessageConversionEnabled && result.MessageType == WebSocketMessageType.Text)
                {
                    var encoding = MessageEncoding ?? Encoding.UTF8;
                    var text = encoding.GetString(messageBytes);
                    message = ReceivedMessage.TextMessage(text);
                }

                _messageReceived.OnNext(message);
            }
        }
        catch (OperationCanceledException ex)
        {
            _ = ex;
        }
        catch (WebSocketException ex)
        {
            _disconnectionHappened.OnNext(Disconnected.Create(DisconnectReason.ConnectionLost, ex));
            _ = ScheduleReconnectAsync(DisconnectReason.ConnectionLost);
        }
        catch (Exception ex)
        {
            _disconnectionHappened.OnNext(Disconnected.Create(DisconnectReason.Error, ex));
            _ = ScheduleReconnectAsync(DisconnectReason.Error);
        }
    }

    #endregion

    #region Send Methods

    public bool Send(string message)
    {
        if (string.IsNullOrEmpty(message))
        {
            return false;
        }

        var encoding = MessageEncoding;
        var bytes = encoding.GetBytes(message);
        return Send(bytes);
    }

    public bool Send(byte[] message)
    {
        if (!IsRunning || message.Length == 0)
        {
            return false;
        }

        return _sendChannel.Writer.TryWrite(new Payload(message, WebSocketMessageType.Binary));
    }

    public bool Send(ArraySegment<byte> message)
    {
        if (!IsRunning || message.Count == 0)
        {
            return false;
        }

        var bytes = message.ToArray();
        return _sendChannel.Writer.TryWrite(new Payload(bytes, WebSocketMessageType.Binary));
    }

    public bool Send(ReadOnlySequence<byte> message)
    {
        if (!IsRunning || message.IsEmpty)
        {
            return false;
        }

        var bytes = message.ToArray();
        return _sendChannel.Writer.TryWrite(new Payload(bytes, WebSocketMessageType.Binary));
    }

    public async Task SendInstant(string message)
    {
        if (string.IsNullOrEmpty(message))
        {
            return;
        }

        var encoding = MessageEncoding;
        var bytes = encoding.GetBytes(message);
        await SendInstant(bytes);
    }

    public async Task SendInstant(byte[] message)
    {
        if (NativeClient?.State != WebSocketState.Open || message.Length == 0)
        {
            return;
        }

        await NativeClient.SendAsync(
            new ArraySegment<byte>(message),
            WebSocketMessageType.Binary,
            endOfMessage: true,
            _mainCts?.Token ?? CancellationToken.None
        );
    }

    public bool SendAsText(string message)
    {
        if (!IsRunning || message.Length == 0)
        {
            return false;
        }

        return _sendChannel.Writer.TryWrite(new Payload(MessageEncoding.GetBytes(message), WebSocketMessageType.Text));
    }

    public bool SendAsText(byte[] message)
    {
        if (!IsRunning || message.Length == 0)
        {
            return false;
        }

        return _sendChannel.Writer.TryWrite(new Payload(message, WebSocketMessageType.Text));
    }

    public bool SendAsText(ArraySegment<byte> message)
    {
        if (!IsRunning || message.Count == 0)
        {
            return false;
        }

        var bytes = message.ToArray();
        return _sendChannel.Writer.TryWrite(new Payload(bytes, WebSocketMessageType.Text));
    }

    public bool SendAsText(ReadOnlySequence<byte> message)
    {
        if (!IsRunning || message.IsEmpty)
        {
            return false;
        }

        var bytes = message.ToArray();
        return _sendChannel.Writer.TryWrite(new Payload(bytes, WebSocketMessageType.Text));
    }

    public void StreamFakeMessage(ReceivedMessage message)
    {
        _messageReceived.OnNext(message);
    }

    #endregion

    #region Cleanup

    private async Task CleanupAsync()
    {
        var tasks = new[]
        {
            _sendLoopTask ?? Task.CompletedTask,
            _receiveLoopTask ?? Task.CompletedTask,
            _heartbeatTask ?? Task.CompletedTask
        };

        await Task.WhenAll(tasks);

        NativeClient?.Dispose();
        _mainCts?.Dispose();
    }

    public void Dispose()
    {
        if (_isDisposing)
        {
            return;
        }

        _isDisposing = true;

        _ = Stop(WebSocketCloseStatus.NormalClosure, "Client disposing");

        _reconnectCts?.Cancel();
        _reconnectCts?.Dispose();

        _messageReceived.OnCompleted();
        _connectionHappened.OnCompleted();
        _disconnectionHappened.OnCompleted();

        _messageReceived.Dispose();
        _connectionHappened.Dispose();
        _disconnectionHappened.Dispose();
        GC.SuppressFinalize(this);
    }

    #endregion

    private sealed record Payload(byte[] Content, WebSocketMessageType Type);
}