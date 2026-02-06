using System.Buffers;
using System.Collections.Concurrent;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Threading.Channels;
using R3;
using WebSocket.Rx.Internal;

namespace WebSocket.Rx;

public class ReactiveWebSocketServer : IReactiveWebSocketServer
{
    private sealed record Client(
        ServerWebSocketAdapter Socket,
        CompositeDisposable Disposables)
    {
        public async ValueTask DisposeAsync()
        {
            await Socket.DisposeAsync().ConfigureAwait(false);
            Disposables.Dispose();
        }
    };

    #region Fields & State

    private readonly HttpListener _listener;
    private Task? _serverLoopTask;
    private readonly ConcurrentDictionary<Guid, Task> _connectionTasks = new();
    private readonly ConcurrentDictionary<Guid, Client> _clients = new();

    private readonly Subject<ClientConnected> _clientConnectedSource = new();
    private readonly Subject<ClientDisconnected> _clientDisconnectedSource = new();
    private readonly Subject<ServerReceivedMessage> _messageReceivedSource = new();

    private CancellationTokenSource? _mainCts;
    private readonly AsyncLock _serverLock = new();
    private int _disposed;
    private readonly SemaphoreSlim _disposeLock = new(1, 1);

    #endregion

    #region Properties

    public bool IsDisposed => _disposed != 0;
    public bool IsRunning { get; private set; }
    public bool IsStarted { get; private set; }
    public bool IsListing => _listener.IsListening;
    public TimeSpan IdleConnection { get; set; } = TimeSpan.FromSeconds(30);
    public TimeSpan ConnectTimeout { get; set; } = TimeSpan.FromSeconds(10);
    public Encoding MessageEncoding { get; set; } = Encoding.UTF8;
    public bool IsTextMessageConversionEnabled { get; set; } = true;
    public int ClientCount => _clients.Count;

    public IReadOnlyDictionary<Guid, Metadata> ConnectedClients
        => _clients.ToDictionary(x => x.Key, x => x.Value.Socket.Metadata);

    public Observable<ClientConnected> ClientConnected => _clientConnectedSource.AsObservable();
    public Observable<ClientDisconnected> ClientDisconnected => _clientDisconnectedSource.AsObservable();
    public Observable<ServerReceivedMessage> Messages => _messageReceivedSource.AsObservable();

    #endregion

    public ReactiveWebSocketServer(string prefix = "http://localhost:8080/")
    {
        _listener = new HttpListener
        {
            Prefixes = { prefix },
            TimeoutManager =
            {
                IdleConnection = IdleConnection,
            },
            AuthenticationSchemes = AuthenticationSchemes.Anonymous,
            UnsafeConnectionNtlmAuthentication = false
        };
    }

    #region Start/Stop

    public async Task StartAsync()
    {
        ThrowIfDisposed();

        using (await _serverLock.LockAsync().ConfigureAwait(false))
        {
            if (IsDisposed)
            {
                return;
            }

            _mainCts = new CancellationTokenSource();

            _listener.Start();
            IsRunning = true;
        }

        _serverLoopTask = Task.Run(() => ServerLoopAsync(_mainCts.Token), CancellationToken.None);
    }

    public async Task<bool> StopAsync(WebSocketCloseStatus status, string statusDescription)
    {
        Dictionary<Guid, Client> clientsToStop;
        using (await _serverLock.LockAsync().ConfigureAwait(false))
        {
            if (IsDisposed || !IsRunning)
            {
                return false;
            }

            _mainCts?.Try(x => x.Cancel());

            try
            {
                _listener.Stop();
            }
            catch (ObjectDisposedException)
            {
                // noop
            }
            catch (HttpListenerException)
            {
                // noop
            }

            clientsToStop = _clients.ToDictionary();
            IsRunning = false;
        }

        await Task.WhenAll(clientsToStop.Values
            .Select(client => client.Socket.StopAsync(status, statusDescription))
            .ToList()).ConfigureAwait(false);

        _listener.Close();
        _listener.Try(x => x.Abort());

        foreach (var clientId in clientsToStop.Keys)
        {
            _clients.TryRemove(clientId, out _);
        }

        await (_serverLoopTask ?? Task.CompletedTask).ConfigureAwait(false);

        return true;
    }

    #endregion

    #region Send Methods

    public async Task<bool> SendInstantAsync(Guid clientId, string message,
        CancellationToken cancellationToken = default)
    {
        if (!_clients.TryGetValue(clientId, out var client)) return false;
        await client.Socket.SendInstantAsync(message, cancellationToken);
        return true;
    }

    public async Task<bool> SendInstantAsync(Guid clientId, byte[] message,
        CancellationToken cancellationToken = default)
    {
        if (!_clients.TryGetValue(clientId, out var client)) return false;
        await client.Socket.SendInstantAsync(message, cancellationToken);
        return true;
    }

    public async Task<bool> SendAsBinaryAsync(Guid clientId, byte[] message,
        CancellationToken cancellationToken = default)
    {
        if (!_clients.TryGetValue(clientId, out var client)) return false;
        await client.Socket.SendAsBinaryAsync(message, cancellationToken);
        return true;
    }

    public async Task<bool> SendAsBinaryAsync(Guid clientId, string message,
        CancellationToken cancellationToken = default)
    {
        if (!_clients.TryGetValue(clientId, out var client)) return false;
        await client.Socket.SendAsBinaryAsync(message, cancellationToken);
        return true;
    }

    public async Task<bool> SendAsTextAsync(Guid clientId, byte[] message,
        CancellationToken cancellationToken = default)
    {
        if (!_clients.TryGetValue(clientId, out var client)) return false;
        await client.Socket.SendAsTextAsync(message, cancellationToken);
        return true;
    }

    public async Task<bool> SendAsTextAsync(Guid clientId, string message,
        CancellationToken cancellationToken = default)
    {
        if (!_clients.TryGetValue(clientId, out var client)) return false;
        await client.Socket.SendAsTextAsync(message, cancellationToken);
        return true;
    }

    public bool TrySendAsBinary(Guid clientId, string message)
    {
        if (!_clients.TryGetValue(clientId, out var client)) return false;
        client.Socket.TrySendAsBinary(message);
        return true;
    }

    public bool TrySendAsBinary(Guid clientId, byte[] data)
    {
        if (!_clients.TryGetValue(clientId, out var client)) return false;
        client.Socket.TrySendAsBinary(data);
        return true;
    }

    public bool TrySendAsText(Guid clientId, string message)
    {
        if (!_clients.TryGetValue(clientId, out var client)) return false;
        client.Socket.TrySendAsText(message);
        return true;
    }

    public bool TrySendAsText(Guid clientId, byte[] data)
    {
        if (!_clients.TryGetValue(clientId, out var client)) return false;
        client.Socket.TrySendAsText(data);
        return true;
    }

    #endregion

    #region Client Handling

    private async Task ServerLoopAsync(CancellationToken cancellationToken = default)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var ctx = await _listener.GetContextAsync();
                if (ctx.Request.IsWebSocketRequest)
                {
                    var metadata = ctx.GetMetadata();
                    var task = HandleWebSocketAsync(ctx, metadata);
                    _connectionTasks[metadata.Id] = task;

                    _ = task.ContinueWith(_ => _connectionTasks.TryRemove(metadata.Id, out var _),
                        CancellationToken.None);
                }
                else
                {
                    ctx.Response.StatusCode = 400;
                    ctx.Response.Close();
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException)
            {
                break;
            }
        }
    }

    private async Task HandleWebSocketAsync(HttpListenerContext context, Metadata metadata)
    {
        ServerWebSocketAdapter? socket = null;

        try
        {
            var webSocketCtx = await context
                .AcceptWebSocketAsync(null, IdleConnection)
                .ConfigureAwait(false);

            socket = new ServerWebSocketAdapter(webSocketCtx.WebSocket, metadata)
            {
                IsTextMessageConversionEnabled = IsTextMessageConversionEnabled,
                MessageEncoding = MessageEncoding,
                KeepAliveInterval = IdleConnection
            };

            var disposables = new CompositeDisposable
            {
                socket.MessageReceived
                    .Select(x => new ServerReceivedMessage(metadata, x))
                    .Subscribe(msg =>
                    {
                        lock (_messageReceivedSource)
                        {
                            _messageReceivedSource.OnNext(msg);
                        }
                    }),
                socket.DisconnectionHappened
                    .Select(x => new ClientDisconnected(metadata, x))
                    .Subscribe(disconnected =>
                    {
                        lock (_clientDisconnectedSource)
                        {
                            _clientDisconnectedSource.OnNext(disconnected);
                        }
                    }),
                socket.DisconnectionHappened
                    .Subscribe(_ => _clients.TryRemove(metadata.Id, out var _))
            };
            socket.Start();

            _clients[metadata.Id] = new Client(socket, disposables);

            if (socket.NativeServerSocket.State != WebSocketState.Open)
            {
                _clients.TryRemove(metadata.Id, out _);
            }

            _clientConnectedSource.OnNext(new ClientConnected(metadata, Connected.Create(ConnectReason.Initial)));
        }
        catch (Exception)
        {
            if (socket != null)
            {
                await socket.DisposeAsync().ConfigureAwait(false);
            }

            _clients.TryRemove(metadata.Id, out _);
        }
    }

    #endregion

    #region Dispose

    protected void ThrowIfDisposed()
    {
        if (IsDisposed)
        {
            throw new ObjectDisposedException(GetType().FullName);
        }
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
            // Fire and forget async cleanup
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
            _mainCts?.Try(x => x.Cancel());

            if (IsRunning)
            {
                await StopAsync(WebSocketCloseStatus.InternalServerError, "Server disposed").ConfigureAwait(false);
            }

            var remainingTasks = _connectionTasks.Values.ToArray();
            if (remainingTasks.Length > 0)
            {
                await Task.WhenAll(remainingTasks).WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
            }

            var clientsToDispose = _clients.Values.ToArray();
            foreach (var client in clientsToDispose)
            {
                try
                {
                    await client.DisposeAsync().ConfigureAwait(false);
                }
                catch (Exception)
                {
                    // noop
                }
            }

            CompleteSubjects();

            _mainCts?.Dispose();
            IsRunning = false;
        }
        finally
        {
            if (_disposeLock.CurrentCount == 0)
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
            _clientConnectedSource.OnCompleted();
            _clientDisconnectedSource.OnCompleted();
            _messageReceivedSource.OnCompleted();
        }
        catch (Exception)
        {
            // Subjects already completed or disposed
        }

        _clientConnectedSource.Dispose();
        _clientDisconnectedSource.Dispose();
        _messageReceivedSource.Dispose();
    }

    ~ReactiveWebSocketServer()
    {
        Dispose(false);
    }

    #endregion

    #region ServerWebSocketAdapter

    public sealed class ServerWebSocketAdapter : ReactiveWebSocketClient
    {
        private readonly CancellationTokenSource _adapterCts = new();
        private int _adapterDisposed;
        private readonly SemaphoreSlim _adapterDisposeLock = new(1, 1);

        public System.Net.WebSockets.WebSocket NativeServerSocket { get; }
        public Metadata Metadata { get; }

        public ServerWebSocketAdapter(System.Net.WebSockets.WebSocket serverSocket, Metadata metadata)
            : base(new Uri("ws://server-adapter"))
        {
            Metadata = metadata;
            NativeServerSocket = serverSocket ?? throw new ArgumentNullException(nameof(serverSocket));

            IsStarted = true;
            IsRunning = true;

            SendChannel = Channel.CreateUnbounded<Payload>(new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = false
            });

            MainCts = _adapterCts;
        }

        public void Start()
        {
            SendLoopTask = Task.Run(() => SendLoopAsync(_adapterCts.Token), CancellationToken.None);
            ReceiveLoopTask = Task.Run(() => ReceiveLoopAdapterAsync(_adapterCts.Token), CancellationToken.None);
        }

        protected override async Task SendAsync(byte[] data, WebSocketMessageType type, bool endOfMessage,
            CancellationToken cancellationToken = default)
        {
            if (NativeServerSocket.State == WebSocketState.Open)
            {
                await NativeServerSocket
                    .SendAsync(data, type, endOfMessage: true, cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        private async Task ReceiveLoopAdapterAsync(CancellationToken cancellationToken)
        {
            var buffer = ArrayPool<byte>.Shared.Rent(1024 * 16);

            try
            {
                while (NativeServerSocket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
                {
                    await using var ms = MemoryStreamManager.GetStream();
                    WebSocketReceiveResult result;

                    do
                    {
                        result = await NativeServerSocket
                            .ReceiveAsync(buffer, cancellationToken)
                            .ConfigureAwait(false);

                        ms.Write(buffer, 0, result.Count);
                    } while (!result.EndOfMessage);

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        await NativeServerSocket
                            .CloseAsync(result.CloseStatus ?? WebSocketCloseStatus.NormalClosure,
                                result.CloseStatusDescription ?? "", CancellationToken.None)
                            .ConfigureAwait(false);
                        DisconnectionHappenedSource.OnNext(Disconnected.Create(DisconnectReason.ClientInitiated));
                        break;
                    }

                    var messageBytes = ms.GetBuffer().AsMemory(0, (int)ms.Length);
                    var message = IsTextMessageConversionEnabled && result.MessageType == WebSocketMessageType.Text
                        ? ReceivedMessage.TextMessage(MessageEncoding.GetString(messageBytes.Span))
                        : ReceivedMessage.BinaryMessage(messageBytes.ToArray());

                    MessageReceivedSource.OnNext(message);
                }
            }
            catch (OperationCanceledException ex)
            {
                _ = ex;
            }
            catch (WebSocketException ex) when (ex.InnerException is not HttpListenerException)
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
            }
            catch (WebSocketException ex) when (ex.InnerException is HttpListenerException)
            {
                // noop
            }
            catch (Exception ex)
            {
                DisconnectionHappenedSource.OnNext(Disconnected.Create(DisconnectReason.Error, ex));
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }

        public new async Task<bool> StopAsync(WebSocketCloseStatus status, string statusDescription)
        {
            if (IsDisposed)
            {
                return false;
            }

            _adapterCts.Try(x => x.Cancel());

            if (NativeServerSocket.State == WebSocketState.Open)
            {
                try
                {
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                    await NativeServerSocket.CloseAsync(status, statusDescription, cts.Token);
                }
                catch
                {
                    NativeServerSocket.Try(x => x.Abort());
                }
            }

            DisconnectionHappenedSource.OnNext(Disconnected.Create(DisconnectReason.ServerInitiated));

            IsStarted = false;
            IsRunning = false;
            return true;
        }

        protected override void Dispose(bool disposing)
        {
            if (Interlocked.CompareExchange(ref _adapterDisposed, 1, 0) != 0)
            {
                return;
            }

            if (disposing)
            {
                // Fire and forget async cleanup
                _ = DisposeAdapterAsyncCore();
            }
        }

        protected override async ValueTask DisposeAsyncCore()
        {
            if (Interlocked.CompareExchange(ref _adapterDisposed, 1, 0) != 0)
            {
                return;
            }

            await DisposeAdapterAsyncCore().ConfigureAwait(false);
        }

        private async ValueTask DisposeAdapterAsyncCore()
        {
            await _adapterDisposeLock.WaitAsync().ConfigureAwait(false);
            try
            {
                // Stop the adapter
                await StopAsync(WebSocketCloseStatus.NormalClosure, "Adapter disposed").ConfigureAwait(false);

                // Cancel operations
                MainCts?.Try(x => x.Cancel());

                // Wait for tasks
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
                        // Continue cleanup
                    }
                }

                // Dispose resources
                MainCts?.Try(x => x.Dispose());
                _adapterCts.Dispose();

                // Complete subjects
                MessageReceivedSource.OnCompleted();
                ConnectionHappenedSource.OnCompleted();
                DisconnectionHappenedSource.OnCompleted();

                MessageReceivedSource.Dispose();
                ConnectionHappenedSource.Dispose();
                DisconnectionHappenedSource.Dispose();

                // Dispose semaphore
                _adapterDisposeLock.Dispose();
            }
            finally
            {
                if (_adapterDisposeLock.CurrentCount == 0)
                {
                    _adapterDisposeLock.Release();
                }
            }
        }
    }

    #endregion
}