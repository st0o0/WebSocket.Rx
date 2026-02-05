using System.Buffers;
using System.Net.WebSockets;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using System.Threading.Channels;
using Microsoft.IO;
using R3;
using WebSocket.Rx.Internal;

namespace WebSocket.Rx;

public class ReactiveWebSocketClient(Uri url, RecyclableMemoryStreamManager? memoryStreamManager = null)
    : IReactiveWebSocketClient
{
    protected RecyclableMemoryStreamManager MemoryStreamManager
    {
        get => field ??= new RecyclableMemoryStreamManager();
    } = memoryStreamManager;

    protected CancellationTokenSource? MainCts;
    protected CancellationTokenSource? ReconnectCts;
    protected readonly AsyncLock ConnectionLock = new();

    protected bool IsReconnecting;

    protected readonly Subject<ReceivedMessage> MessageReceivedSource = new();
    protected readonly Subject<Connected> ConnectionHappenedSource = new();
    protected readonly Subject<Disconnected> DisconnectionHappenedSource = new();

    protected ChannelWriter<Payload> SendWriter => SendChannel.Writer;

    protected Channel<Payload> SendChannel =
        Channel.CreateUnbounded<Payload>(new UnboundedChannelOptions { SingleReader = true });

    protected Task? SendLoopTask;
    protected Task? ReceiveLoopTask;

    public Uri Url { get; set; } = url ?? throw new ArgumentNullException(nameof(url));
    public TimeSpan ConnectTimeout { get; set; } = TimeSpan.FromSeconds(10);
    public TimeSpan KeepAliveInterval { get; set; } = TimeSpan.FromSeconds(30);
    public TimeSpan KeepAliveTimeout { get; set; } = TimeSpan.FromSeconds(10);
    public bool IsReconnectionEnabled { get; set; } = true;
    public bool IsStarted { get; internal set; }
    public bool IsRunning { get; internal set; }
    public bool IsDisposed { get; internal set; }
    public bool SenderRunning => SendLoopTask!.Status is TaskStatus.Running or TaskStatus.WaitingForActivation;
    public bool IsInsideLock => ConnectionLock.IsLocked;
    public bool IsTextMessageConversionEnabled { get; set; }
    public Encoding MessageEncoding { get; set; } = Encoding.UTF8;
    public ClientWebSocket NativeClient { get; private set; } = new();

    public Observable<ReceivedMessage> MessageReceived => MessageReceivedSource.AsObservable();
    public Observable<Connected> ConnectionHappened => ConnectionHappenedSource.AsObservable();
    public Observable<Disconnected> DisconnectionHappened => DisconnectionHappenedSource.AsObservable();

    #region Start/Stop

    public async Task StartAsync()
    {
        try
        {
            await StartOrFailAsync();
        }
        catch (Exception ex)
        {
            DisconnectionHappenedSource.OnNext(Disconnected.Create(DisconnectReason.Error, ex));
        }
    }

    public async Task StartOrFailAsync()
    {
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
        catch
        {
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
                    using var closeCts =
                        CancellationTokenSource.CreateLinkedTokenSource(timeoutCts.Token);
                    await NativeClient.CloseAsync(status, statusDescription, closeCts.Token);
                }
                catch (Exception)
                {
                    NativeClient.Try(x => x.Abort());
                }
            }

            DisconnectionHappenedSource.OnNext(Disconnected.Create(DisconnectReason.ClientInitiated));

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
            _ = ex;
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

            ConnectionHappenedSource.OnNext(Connected.Create(reason));
        }
        catch (Exception)
        {
            NativeClient.Dispose();
            MainCts?.Cancel();

            if (throwOnError)
            {
                throw;
            }

            if (IsReconnectionEnabled && reason == ConnectReason.Reconnect)
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
        catch (OperationCanceledException ex)
        {
            _ = ex;
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
        catch (ChannelClosedException ex)
        {
            _ = ex;
        }
        catch (OperationCanceledException ex)
        {
            _ = ex;
        }
        catch (Exception)
        {
            if (!IsDisposed && IsReconnectionEnabled)
            {
                _ = ScheduleReconnectAsync();
            }
        }
    }

    protected virtual async Task SendAsync(byte[] data, WebSocketMessageType type, bool endOfMessage,
        CancellationToken cancellationToken = default)
    {
        if (NativeClient.State == WebSocketState.Open)
        {
            await NativeClient.SendAsync(
                data,
                type,
                endOfMessage,
                cancellationToken
            );
        }
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
                    DisconnectionHappenedSource.OnNext(Disconnected.Create(DisconnectReason.ServerInitiated));
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
        catch (OperationCanceledException ex)
        {
            _ = ex;
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
            DisconnectionHappenedSource.OnNext(Disconnected.Create(reason, ex));
            _ = ScheduleReconnectAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            if (!IsDisposed && IsReconnectionEnabled)
            {
                DisconnectionHappenedSource.OnNext(Disconnected.Create(DisconnectReason.Error, ex));
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

    public async Task SendInstantAsync(string message, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(message))
        {
            return;
        }

        await SendAsync(MessageEncoding.GetBytes(message), WebSocketMessageType.Binary, true, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task SendInstantAsync(byte[] message, CancellationToken cancellationToken = default)
    {
        if (message.Length == 0)
        {
            return;
        }

        using var connectedCts =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken,
                MainCts?.Token ?? CancellationToken.None);
        await SendAsync(message, WebSocketMessageType.Binary, true, connectedCts.Token);
    }

    public async Task SendAsBinaryAsync(string message, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(message))
        {
            return;
        }

        await SendAsBinaryAsync(MessageEncoding.GetBytes(message), cancellationToken);
    }

    public async Task SendAsBinaryAsync(byte[] message, CancellationToken cancellationToken = default)
    {
        if (!IsRunning || message.Length == 0)
        {
            return;
        }

        await SendWriter.WriteAsync(new Payload(message, WebSocketMessageType.Binary), cancellationToken);
    }

    public async Task SendAsTextAsync(string message, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(message))
        {
            return;
        }

        await SendAsTextAsync(MessageEncoding.GetBytes(message), cancellationToken);
    }

    public async Task SendAsTextAsync(byte[] message, CancellationToken cancellationToken = default)
    {
        if (!IsRunning || message.Length == 0)
        {
            return;
        }

        await SendWriter.WriteAsync(new Payload(message, WebSocketMessageType.Text), cancellationToken);
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

    #region IDisposable

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (IsDisposed)
        {
            return;
        }

        if (disposing)
        {
            _ = StopAsync(WebSocketCloseStatus.NormalClosure, "Disposing");
            CleanupManagedResources();
            CompleteSubjects();
        }


        IsDisposed = true;
    }

    ~ReactiveWebSocketClient()
    {
        Dispose(false);
    }

    private void CleanupManagedResources()
    {
        ReconnectCts?.Cancel();
        MainCts?.Cancel();

        ReconnectCts?.Dispose();
        MainCts?.Dispose();

        NativeClient.Dispose();
        SendChannel.Writer.Complete();
    }

    private void CompleteSubjects()
    {
        try
        {
            MessageReceivedSource.OnCompleted();
            ConnectionHappenedSource.OnCompleted();
            DisconnectionHappenedSource.OnCompleted();
        }
        catch (Exception)
        {
            // no op
        }

        MessageReceivedSource.Dispose();
        ConnectionHappenedSource.Dispose();
        DisconnectionHappenedSource.Dispose();
    }

    #endregion
}