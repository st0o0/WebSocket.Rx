using System.Buffers;
using System.Net.WebSockets;
using System.Text;
using System.Threading.Channels;
using Microsoft.IO;
using R3;
using WebSocket.Rx.Internal;

namespace WebSocket.Rx;

public class ReactiveWebSocketClient : IReactiveWebSocketClient
{
    private int _disposed;
    private readonly SemaphoreSlim _disposeLock = new(1, 1);

    protected readonly RecyclableMemoryStreamManager MemoryStreamManager;
    protected CancellationTokenSource? MainCts;
    protected CancellationTokenSource? ReconnectCts;
    internal readonly AsyncLock ConnectionLock = new();

    protected bool IsReconnecting;

    protected readonly Subject<ReceivedMessage> MessageReceivedSource = new();
    protected readonly Subject<Connected> ConnectionHappenedSource = new();
    protected readonly Subject<Disconnected> DisconnectionHappenedSource = new();
    protected readonly Subject<ErrorOccurred> ErrorOccurredSource = new();

    protected ChannelWriter<Payload> SendWriter => SendChannel.Writer;

    protected Channel<Payload> SendChannel =
        Channel.CreateUnbounded<Payload>(new UnboundedChannelOptions { SingleReader = true });

    protected Task? SendLoopTask;
    protected Task? ReceiveLoopTask;

    public ReactiveWebSocketClient(Uri url, RecyclableMemoryStreamManager? memoryStreamManager = null)
    {
        Url = url ?? throw new ArgumentNullException(nameof(url));
        MemoryStreamManager = memoryStreamManager ?? new RecyclableMemoryStreamManager();
    }

    public Uri Url { get; set; }
    public TimeSpan ConnectTimeout { get; set; } = TimeSpan.FromSeconds(10);
    public TimeSpan KeepAliveInterval { get; set; } = TimeSpan.FromSeconds(30);
    public TimeSpan KeepAliveTimeout { get; set; } = TimeSpan.FromSeconds(10);
    public bool IsReconnectionEnabled { get; set; } = true;
    public bool IsStarted { get; internal set; }
    public bool IsRunning { get; internal set; }
    public bool IsDisposed => _disposed != 0;
    public bool SenderRunning => SendLoopTask?.Status is TaskStatus.Running or TaskStatus.WaitingForActivation;
    public bool IsInsideLock => ConnectionLock.IsLocked;
    public bool IsTextMessageConversionEnabled { get; set; } = true;
    public Encoding MessageEncoding { get; set; } = Encoding.UTF8;
    public ClientWebSocket NativeClient { get; private set; } = new();

    public Observable<ReceivedMessage> MessageReceived => MessageReceivedSource.AsObservable();
    public Observable<Connected> ConnectionHappened => ConnectionHappenedSource.AsObservable();
    public Observable<Disconnected> DisconnectionHappened => DisconnectionHappenedSource.AsObservable();
    public Observable<ErrorOccurred> ErrorOccurred => ErrorOccurredSource.AsObservable();

    #region Start/Stop

    public async Task StartAsync()
    {
        try
        {
            await StartOrFailAsync();
        }
        catch (Exception ex)
        {
            ErrorOccurredSource.OnNext(new ErrorOccurred(ErrorSource.Connection, ex));
        }
    }

    public async Task StartOrFailAsync()
    {
        ThrowIfDisposed();

        using (await ConnectionLock.LockAsync())
        {
            if (IsStarted)
            {
                return;
            }

            await ConnectInternalAsync(ConnectReason.Initial, true);
        }
    }

    public async Task<bool> StopAsync(WebSocketCloseStatus status, string statusDescription)
    {
        try
        {
            return await StopOrFailAsync(status, statusDescription);
        }
        catch (Exception ex)
        {
            ErrorOccurredSource.OnNext(new ErrorOccurred(ErrorSource.Disconnection, ex));
            return false;
        }
    }

    public async Task<bool> StopOrFailAsync(WebSocketCloseStatus status, string statusDescription)
    {
        if (IsDisposed)
        {
            return false;
        }

        using (await ConnectionLock.LockAsync().ConfigureAwait(false))
        {
            if (!IsStarted || IsDisposed)
            {
                return false;
            }

            IsStarted = false;
            IsRunning = false;

            ReconnectCts?.Cancel();
            MainCts?.Cancel();

            if (NativeClient.State is WebSocketState.Open or WebSocketState.CloseReceived)
            {
                try
                {
                    using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                    using var closeCts = CancellationTokenSource.CreateLinkedTokenSource(timeoutCts.Token);
                    await NativeClient.CloseAsync(status, statusDescription, closeCts.Token);
                }
                catch (Exception ex)
                {
                    ErrorOccurredSource.OnNext(new ErrorOccurred(ErrorSource.Disconnection, ex));
                    NativeClient.Try(x => x.Abort());
                }
            }

            DisconnectionHappenedSource.OnNext(new Disconnected(DisconnectReason.Shutdown));

            await CleanupAsync();

            return true;
        }
    }

    protected async Task CleanupAsync()
    {
        if (IsDisposed)
        {
            return;
        }

        var tasks = new[]
        {
            SendLoopTask ?? Task.CompletedTask,
            ReceiveLoopTask ?? Task.CompletedTask
        };


        await Task.WhenAll(tasks).Try(async x => await x.WaitAsync(TimeSpan.FromSeconds(10)));

        if (!IsDisposed)
        {
            NativeClient.Try(x => x.Abort());
        }
    }

    #endregion

    #region Reconnection

    public async Task ReconnectAsync(CancellationToken cancellationToken = default)
    {
        if (!IsStarted)
        {
            return;
        }

        try
        {
            using (await ConnectionLock.LockAsync())
            {
                await ReconnectInternalAsync(false, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            ErrorOccurredSource.OnNext(new ErrorOccurred(ErrorSource.Reconnection, ex));
        }
    }

    public async Task ReconnectOrFailAsync(CancellationToken cancellationToken = default)
    {
        if (!IsStarted)
        {
            throw new InvalidOperationException("Client not started");
        }

        using (await ConnectionLock.LockAsync())
        {
            await ReconnectInternalAsync(true, cancellationToken);
        }
    }

    private async Task ReconnectInternalAsync(bool throwOnError, CancellationToken cancellationToken = default)
    {
        if (IsDisposed || !IsStarted)
        {
            return;
        }

        if (IsReconnecting)
        {
            return;
        }

        IsReconnecting = true;
        IsRunning = false;

        try
        {
            MainCts?.Cancel();

            NativeClient.Try(x => x.Abort());
            NativeClient.Dispose();

            await Task.WhenAll(
                SendLoopTask ?? Task.CompletedTask,
                ReceiveLoopTask ?? Task.CompletedTask
            );

            await ConnectInternalAsync(ConnectReason.Reconnect, throwOnError, cancellationToken);
        }
        catch (Exception ex)
        {
            ErrorOccurredSource.OnNext(new ErrorOccurred(ErrorSource.Reconnection, ex));
            if (throwOnError) throw;
        }
        finally
        {
            IsReconnecting = false;
        }
    }

    private async Task ConnectInternalAsync(ConnectReason reason, bool throwOnError,
        CancellationToken cancellationToken = default)
    {
        MainCts?.Try(x => x.Dispose());
        MainCts = new CancellationTokenSource();
        NativeClient = new ClientWebSocket
        {
            Options =
            {
                KeepAliveInterval = KeepAliveInterval,
                KeepAliveTimeout = KeepAliveTimeout
            }
        };

        try
        {
            using var timeoutCts = new CancellationTokenSource(ConnectTimeout);
            using var linkedCts = CancellationTokenSource
                .CreateLinkedTokenSource(MainCts.Token, timeoutCts.Token, cancellationToken);

            await NativeClient.ConnectAsync(Url, linkedCts.Token);

            IsStarted = true;
            IsRunning = true;

            SendLoopTask = Task.Run(() => SendLoopAsync(MainCts.Token), CancellationToken.None);
            ReceiveLoopTask = Task.Run(() => ReceiveLoopAsync(MainCts.Token), CancellationToken.None);

            ConnectionHappenedSource.OnNext(new Connected(reason));
        }
        catch (Exception ex)
        {
            NativeClient.Dispose();
            MainCts?.Cancel();

            if (throwOnError)
            {
                throw;
            }

            ErrorOccurredSource.OnNext(new ErrorOccurred(ErrorSource.Connection, ex));

            if (IsReconnectionEnabled)
            {
                _ = ScheduleReconnectAsync().ConfigureAwait(false);
            }
        }
    }

    private async Task ScheduleReconnectAsync()
    {
        if (!IsReconnectionEnabled || IsDisposed || !IsStarted)
        {
            return;
        }

        ReconnectCts = new CancellationTokenSource();

        try
        {
            using (await ConnectionLock.LockAsync())
            {
                await ReconnectInternalAsync(throwOnError: false);
            }
        }
        catch (OperationCanceledException)
        {
            // noop
        }
        catch (Exception ex)
        {
            ErrorOccurredSource.OnNext(new ErrorOccurred(ErrorSource.Reconnection, ex));
        }
    }

    #endregion

    #region Send/Receive Loops

    protected async Task SendLoopAsync(CancellationToken ct)
    {
        try
        {
            await foreach (var payload in SendChannel.Reader.ReadAllAsync(ct))
            {
                if (IsDisposed)
                {
                    break;
                }

                await SendAsync(payload.Data, payload.Type, true, ct);
            }
        }
        catch (ChannelClosedException)
        {
            // noop
        }
        catch (OperationCanceledException)
        {
            // noop
        }
        catch (Exception ex)
        {
            ErrorOccurredSource.OnNext(new ErrorOccurred(ErrorSource.SendLoop, ex));

            if (!IsDisposed && IsReconnectionEnabled)
            {
                _ = ScheduleReconnectAsync();
            }
        }
    }

    protected virtual async Task<bool> SendAsync(byte[] data, WebSocketMessageType type, bool endOfMessage,
        CancellationToken cancellationToken = default)
    {
        if (NativeClient.State is not WebSocketState.Open) return false;

        await NativeClient.SendAsync(
            data,
            type,
            endOfMessage,
            cancellationToken
        );
        return true;
    }

    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(1024 * 16);

        try
        {
            while (!IsDisposed && NativeClient.State is WebSocketState.Open &&
                   !cancellationToken.IsCancellationRequested)
            {
                await using var ms = MemoryStreamManager.GetStream();
                WebSocketReceiveResult result;

                do
                {
                    result = await NativeClient
                        .ReceiveAsync(buffer, cancellationToken)
                        .ConfigureAwait(false);

                    ms.Write(buffer, 0, result.Count);
                } while (!result.EndOfMessage);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    await NativeClient
                        .CloseAsync(result.CloseStatus ?? WebSocketCloseStatus.NormalClosure,
                            result.CloseStatusDescription ?? "", CancellationToken.None);

                    var @event = new Disconnected(DisconnectReason.ServerInitiated, NativeClient.CloseStatus,
                        NativeClient.CloseStatusDescription, NativeClient.SubProtocol);
                    DisconnectionHappenedSource.OnNext(@event);
                    if (!@event.IsClosingCanceled && !IsReconnectionEnabled) return;
                    _ = ScheduleReconnectAsync().ConfigureAwait(false);
                    return;
                }

                var messageBytes = ms.GetBuffer().AsMemory(0, (int)ms.Length);
                var message = ReceivedMessage.BinaryMessage(messageBytes.ToArray());

                if (IsTextMessageConversionEnabled && result.MessageType == WebSocketMessageType.Text)
                {
                    var text = MessageEncoding.GetString(messageBytes.Span);
                    message = ReceivedMessage.TextMessage(text);
                }

                MessageReceivedSource.OnNext(message);
            }
        }
        catch (OperationCanceledException)
        {
            // noop
        }
        catch (WebSocketException ex)
        {
            var reason = ex.NativeErrorCode switch
            {
                // KeepAlive  
                10060 or 110 => DisconnectReason.Timeout,

                // Connection lost
                10054 or 104 => DisconnectReason.ConnectionLost,

                // Aborted/Cancelled
                10053 or 995 => DisconnectReason.ClientInitiated,

                _ => DisconnectReason.ConnectionLost
            };

            var @event = new Disconnected(reason, Exception: ex);
            DisconnectionHappenedSource.OnNext(@event);
            if (@event.IsReconnectionCanceled) return;
            _ = ScheduleReconnectAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            ErrorOccurredSource.OnNext(new ErrorOccurred(ErrorSource.ReceiveLoop, ex));

            if (!IsDisposed && IsReconnectionEnabled)
            {
                var @event = new Disconnected(DisconnectReason.ConnectionLost);
                DisconnectionHappenedSource.OnNext(@event);
                if (@event.IsReconnectionCanceled) return;
                _ = ScheduleReconnectAsync().ConfigureAwait(false);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    #endregion

    #region Send Methods

    public async Task<bool> SendInstantAsync(string message, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(message))
        {
            return false;
        }

        try
        {
            using var connectedCts =
                CancellationTokenSource.CreateLinkedTokenSource(cancellationToken,
                    MainCts?.Token ?? CancellationToken.None);
            return await SendAsync(MessageEncoding.GetBytes(message), WebSocketMessageType.Binary, true,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            ErrorOccurredSource.OnNext(new ErrorOccurred(ErrorSource.Send, ex));
            return false;
        }
    }

    public async Task<bool> SendInstantAsync(byte[] message, CancellationToken cancellationToken = default)
    {
        if (message.Length == 0)
        {
            return false;
        }

        try
        {
            using var connectedCts =
                CancellationTokenSource.CreateLinkedTokenSource(cancellationToken,
                    MainCts?.Token ?? CancellationToken.None);
            return await SendAsync(message, WebSocketMessageType.Binary, true, connectedCts.Token)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            ErrorOccurredSource.OnNext(new ErrorOccurred(ErrorSource.Send, ex));
            return false;
        }
    }

    public async Task<bool> SendAsBinaryAsync(string message, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(message))
        {
            return false;
        }

        return await SendAsBinaryAsync(MessageEncoding.GetBytes(message), cancellationToken);
    }

    public async Task<bool> SendAsBinaryAsync(byte[] message, CancellationToken cancellationToken = default)
    {
        if (!IsRunning || message.Length == 0)
        {
            return false;
        }

        await SendWriter.WriteAsync(new Payload(message, WebSocketMessageType.Binary), cancellationToken);
        return true;
    }

    public async Task<bool> SendAsTextAsync(string message, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(message))
        {
            return false;
        }

        return await SendAsTextAsync(MessageEncoding.GetBytes(message), cancellationToken);
    }

    public async Task<bool> SendAsTextAsync(byte[] message, CancellationToken cancellationToken = default)
    {
        if (!IsRunning || message.Length == 0)
        {
            return false;
        }

        await SendWriter.WriteAsync(new Payload(message, WebSocketMessageType.Text), cancellationToken);
        return true;
    }

    public bool TrySendAsBinary(string message)
    {
        return !string.IsNullOrEmpty(message) && TrySendAsBinary(MessageEncoding.GetBytes(message));
    }

    public bool TrySendAsBinary(byte[] message)
    {
        if (!IsRunning || message.Length == 0)
        {
            return false;
        }

        return SendWriter.TryWrite(new Payload(message, WebSocketMessageType.Binary));
    }

    public bool TrySendAsText(string message)
    {
        return !string.IsNullOrEmpty(message) && TrySendAsText(MessageEncoding.GetBytes(message));
    }

    public bool TrySendAsText(byte[] message)
    {
        if (!IsRunning || message.Length == 0)
        {
            return false;
        }

        return SendWriter.TryWrite(new Payload(message, WebSocketMessageType.Text));
    }

    public void StreamFakeMessage(ReceivedMessage message)
    {
        MessageReceivedSource.OnNext(message);
    }

    #endregion

    #region Dispose

    protected void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    public async ValueTask DisposeAsync()
    {
        await DisposeAsyncCore().ConfigureAwait(false);
        Dispose(false);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (Interlocked.CompareExchange(ref _disposed, 1, 0) != 0)
        {
            return;
        }

        if (disposing)
        {
            // Synchronous cleanup - fire and forget async cleanup
            _ = DisposeAsyncCore();
        }
    }

    protected virtual async ValueTask DisposeAsyncCore()
    {
        if (Interlocked.CompareExchange(ref _disposed, 1, 0) != 0)
        {
            return;
        }

        await _disposeLock.WaitAsync().ConfigureAwait(false);
        try
        {
            await StopAsync(WebSocketCloseStatus.NormalClosure, "Disposing").ConfigureAwait(false);

            ReconnectCts?.Cancel();
            MainCts?.Cancel();

            var tasks = new List<Task>();
            if (SendLoopTask != null) tasks.Add(SendLoopTask);
            if (ReceiveLoopTask != null) tasks.Add(ReceiveLoopTask);

            if (tasks.Count > 0)
            {
                try
                {
                    await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                }
                catch (TimeoutException)
                {
                    // noop
                }
                catch (Exception ex)
                {
                    ErrorOccurredSource.OnNext(new ErrorOccurred(ErrorSource.Dispose, ex));
                }
            }

            SendChannel.Writer.Complete();

            ReconnectCts?.Dispose();
            MainCts?.Dispose();

            NativeClient.Dispose();

            CompleteSubjects();
            IsRunning = false;
            IsStarted = false;
        }
        finally
        {
            if (!_disposeLock.CurrentCount.Equals(0))
            {
                _disposeLock.Release();
            }

            _disposeLock.Dispose();
        }
    }

    private void CompleteSubjects()
    {
        try
        {
            MessageReceivedSource.OnCompleted();
            ConnectionHappenedSource.OnCompleted();
            DisconnectionHappenedSource.OnCompleted();
            ErrorOccurredSource.OnCompleted();
        }
        catch (Exception)
        {
            // noop
        }

        MessageReceivedSource.Dispose();
        ConnectionHappenedSource.Dispose();
        DisconnectionHappenedSource.Dispose();
        ErrorOccurredSource.Dispose();
    }

    ~ReactiveWebSocketClient()
    {
        Dispose(false);
    }

    #endregion
}