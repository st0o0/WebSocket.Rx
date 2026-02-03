using System.Buffers;
using System.Net.WebSockets;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Text;
using System.Threading.Channels;
using Microsoft.IO;
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
    protected bool IsDisposing;
    protected DateTime LastMessageReceived = DateTime.UtcNow;

    protected readonly Subject<ReceivedMessage> MessageReceivedSource = new();
    protected readonly Subject<Connected> ConnectionHappenedSource = new();
    protected readonly Subject<Disconnected> DisconnectionHappenedSource = new();

    protected ChannelWriter<Payload> SendWriter => SendChannel.Writer;

    protected Channel<Payload> SendChannel =
        Channel.CreateUnbounded<Payload>(new UnboundedChannelOptions { SingleReader = true });

    protected Task? SendLoopTask;
    protected Task? ReceiveLoopTask;
    protected Task? HeartbeatTask;

    public Uri Url { get; set; } = url ?? throw new ArgumentNullException(nameof(url));
    public TimeSpan ConnectTimeout { get; set; } = TimeSpan.FromSeconds(10);
    public TimeSpan InactivityTimeout { get; set; } = TimeSpan.FromSeconds(30);
    public bool IsReconnectionEnabled { get; set; } = true;
    public string? Name { get; set; }
    public bool IsStarted { get; internal set; }
    public bool IsRunning { get; internal set; }
    public bool SenderRunning => SendLoopTask!.Status is TaskStatus.Running or TaskStatus.WaitingForActivation;
    public bool IsInsideLock => ConnectionLock.IsLocked;
    public bool IsTextMessageConversionEnabled { get; set; }
    public Encoding MessageEncoding { get; set; } = Encoding.UTF8;
    public ClientWebSocket NativeClient { get; private set; } = new();

    public IObservable<ReceivedMessage> MessageReceived => MessageReceivedSource.AsObservable();
    public IObservable<Connected> ConnectionHappened => ConnectionHappenedSource.AsObservable();
    public IObservable<Disconnected> DisconnectionHappened => DisconnectionHappenedSource.AsObservable();

    #region Start/Stop

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await StartOrFailAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            DisconnectionHappenedSource.OnNext(Disconnected.Create(DisconnectReason.Error, ex));
        }
    }

    public async Task StartOrFailAsync(CancellationToken cancellationToken = default)
    {
        using (await ConnectionLock.LockAsync())
        {
            if (IsStarted)
            {
                return;
            }

            await ConnectInternalAsync(ConnectReason.Initial, true, cancellationToken);
        }
    }

    public async Task<bool> StopAsync(WebSocketCloseStatus status, string statusDescription,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await StopOrFailAsync(status, statusDescription, cancellationToken);
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> StopOrFailAsync(WebSocketCloseStatus status, string statusDescription,
        CancellationToken cancellationToken = default)
    {
        using (await ConnectionLock.LockAsync())
        {
            if (!IsStarted)
            {
                return false;
            }

            IsStarted = false;
            IsRunning = false;

            ReconnectCts?.Cancel();
            MainCts?.Cancel();

            if (NativeClient.State is WebSocketState.Open)
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

            DisconnectionHappenedSource.OnNext(Disconnected.Create(DisconnectReason.ClientInitiated));

            await CleanupAsync();
            return true;
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
                await ReconnectInternalAsync(DisconnectReason.ClientInitiated, false, cancellationToken);
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
            await ReconnectInternalAsync(DisconnectReason.ClientInitiated, true, cancellationToken);
        }
    }

    private async Task ReconnectInternalAsync(DisconnectReason reason, bool throwOnError,
        CancellationToken cancellationToken = default)
    {
        if (IsDisposing || !IsStarted)
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
            DisconnectionHappenedSource.OnNext(Disconnected.Create(reason));

            MainCts?.Cancel();

            NativeClient?.Try(x => x.Abort());
            NativeClient?.Dispose();

            await Task.WhenAll(
                SendLoopTask ?? Task.CompletedTask,
                ReceiveLoopTask ?? Task.CompletedTask,
                HeartbeatTask ?? Task.CompletedTask
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
        MainCts = new CancellationTokenSource();
        NativeClient = new ClientWebSocket();

        try
        {
            using var timeoutCts = new CancellationTokenSource(ConnectTimeout);
            using var linkedCts = CancellationTokenSource
                .CreateLinkedTokenSource(MainCts.Token, timeoutCts.Token, cancellationToken);

            await NativeClient.ConnectAsync(Url, linkedCts.Token);

            IsStarted = true;
            IsRunning = true;
            LastMessageReceived = DateTime.UtcNow;

            SendLoopTask = Task.Run(() => SendLoopAsync(MainCts.Token), CancellationToken.None);
            ReceiveLoopTask = Task.Run(() => ReceiveLoopAsync(MainCts.Token), CancellationToken.None);
            HeartbeatTask = Task.Run(() => HeartbeatMonitorAsync(MainCts.Token), CancellationToken.None);

            ConnectionHappenedSource.OnNext(Connected.Create(reason));
        }
        catch (Exception)
        {
            NativeClient?.Dispose();
            MainCts?.Cancel();
            MainCts?.Dispose();

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
        if (!IsReconnectionEnabled || IsDisposing || !IsStarted)
        {
            return;
        }

        ReconnectCts = new CancellationTokenSource();

        try
        {
            await Task.Delay(InactivityTimeout, ReconnectCts.Token);

            using (await ConnectionLock.LockAsync())
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

    protected async Task HeartbeatMonitorAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));

        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                var timeSinceLastMessage = DateTime.UtcNow - LastMessageReceived;

                if (timeSinceLastMessage <= InactivityTimeout) continue;

                if (NativeClient?.State == WebSocketState.Open)
                {
                    using (await ConnectionLock.LockAsync())
                    {
                        await ReconnectInternalAsync(DisconnectReason.Timeout, throwOnError: false,
                            cancellationToken: cancellationToken);
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
            await foreach (var (data, messageType) in SendChannel.Reader.ReadAllAsync(cancellationToken))
            {
                if (NativeClient?.State == WebSocketState.Open)
                {
                    await NativeClient.SendAsync(
                        data,
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
            DisconnectionHappenedSource.OnNext(Disconnected.Create(DisconnectReason.Error, ex));
            _ = ScheduleReconnectAsync(DisconnectReason.Error);
        }
    }

    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(1024 * 16);

        try
        {
            while (NativeClient?.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
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

                LastMessageReceived = DateTime.UtcNow;

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    DisconnectionHappenedSource.OnNext(Disconnected.Create(DisconnectReason.ServerInitiated));
                    _ = ScheduleReconnectAsync(DisconnectReason.ServerInitiated);
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
            DisconnectionHappenedSource.OnNext(Disconnected.Create(DisconnectReason.ConnectionLost, ex));
            _ = ScheduleReconnectAsync(DisconnectReason.ConnectionLost);
        }
        catch (Exception ex)
        {
            DisconnectionHappenedSource.OnNext(Disconnected.Create(DisconnectReason.Error, ex));
            _ = ScheduleReconnectAsync(DisconnectReason.Error);
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

        var encoding = MessageEncoding;
        var bytes = encoding.GetBytes(message);
        await SendInstantAsync(bytes, cancellationToken);
    }

    public async Task SendInstantAsync(byte[] message, CancellationToken cancellationToken = default)
    {
        if (NativeClient.State != WebSocketState.Open || message.Length == 0)
        {
            return;
        }

        using var connectedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, MainCts.Token);

        await NativeClient
            .SendAsync(message, WebSocketMessageType.Binary, endOfMessage: true, connectedCts.Token)
            .ConfigureAwait(false);
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

    public virtual bool TrySendAsBinary(string message)
    {
        return !string.IsNullOrEmpty(message) && TrySendAsBinary(MessageEncoding.GetBytes(message));
    }

    public virtual bool TrySendAsBinary(byte[] message)
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

    #region Cleanup

    protected async Task CleanupAsync()
    {
        var tasks = new[]
        {
            SendLoopTask ?? Task.CompletedTask,
            ReceiveLoopTask ?? Task.CompletedTask,
            HeartbeatTask ?? Task.CompletedTask
        };

        await Task.WhenAll(tasks);

        NativeClient.Dispose();
        MainCts?.Dispose();
    }

    public virtual void Dispose()
    {
        if (IsDisposing)
        {
            return;
        }

        IsDisposing = true;

        _ = StopAsync(WebSocketCloseStatus.NormalClosure, "Client disposing");

        ReconnectCts?.Cancel();
        ReconnectCts?.Dispose();

        MessageReceivedSource.OnCompleted();
        ConnectionHappenedSource.OnCompleted();
        DisconnectionHappenedSource.OnCompleted();

        MessageReceivedSource.Dispose();
        ConnectionHappenedSource.Dispose();
        DisconnectionHappenedSource.Dispose();
        GC.SuppressFinalize(this);
    }

    #endregion
}