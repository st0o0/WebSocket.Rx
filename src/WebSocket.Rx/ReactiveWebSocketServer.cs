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
    private readonly Subject<ServerMessage> _messageReceivedSource = new();
    private readonly Subject<ServerErrorOccurred> _errorOccurredSource = new();

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
    public Observable<ServerMessage> Messages => _messageReceivedSource.AsObservable();
    public Observable<ServerErrorOccurred> ErrorOccurred => _errorOccurredSource.AsObservable();

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

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        using (await _serverLock.LockAsync(cancellationToken).ConfigureAwait(false))
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

    public async Task<bool> StopAsync(WebSocketCloseStatus status, string statusDescription,
        CancellationToken cancellationToken = default)
    {
        Dictionary<Guid, Client> clientsToStop;
        using (await _serverLock.LockAsync(cancellationToken).ConfigureAwait(false))
        {
            if (!IsRunning)
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
            .Select(client => client.Socket.StopAsync(status, statusDescription, cancellationToken))
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
                    .Select(x => new ServerMessage(metadata, x))
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

            lock (_clientConnectedSource)
            {
                _clientConnectedSource.OnNext(new ClientConnected(metadata, new Connected(ConnectReason.Initialized)));
            }
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

    #region Send Methods

    #region Send Client Methods

    public async Task<bool> SendInstantAsync(Guid clientId, ReadOnlyMemory<char> message, WebSocketMessageType type,
        CancellationToken cancellationToken = default)
    {
        if (!_clients.TryGetValue(clientId, out var client)) return false;
        await client.Socket.SendInstantAsync(message, type, cancellationToken);
        return true;
    }

    public async Task<bool> SendInstantAsync(Guid clientId, ReadOnlyMemory<byte> message, WebSocketMessageType type,
        CancellationToken cancellationToken = default)
    {
        if (!_clients.TryGetValue(clientId, out var client)) return false;
        await client.Socket.SendInstantAsync(message, type, cancellationToken);
        return true;
    }

    public async Task<bool> SendAsync(Guid clientId, ReadOnlyMemory<char> message, WebSocketMessageType type,
        CancellationToken cancellationToken = default)
    {
        if (!_clients.TryGetValue(clientId, out var client)) return false;
        await client.Socket.SendAsync(message, type, cancellationToken);
        return true;
    }

    public async Task<bool> SendAsync(Guid clientId, ReadOnlyMemory<byte> message, WebSocketMessageType type,
        CancellationToken cancellationToken = default)
    {
        if (!_clients.TryGetValue(clientId, out var client)) return false;
        await client.Socket.SendAsync(message, type, cancellationToken);
        return true;
    }

    public bool TrySend(Guid clientId, ReadOnlyMemory<char> message, WebSocketMessageType type)
    {
        if (!_clients.TryGetValue(clientId, out var client)) return false;
        client.Socket.TrySend(message, type);
        return true;
    }

    public bool TrySend(Guid clientId, ReadOnlyMemory<byte> data, WebSocketMessageType type)
    {
        if (!_clients.TryGetValue(clientId, out var client)) return false;
        client.Socket.TrySend(data, type);
        return true;
    }

    #endregion

    #region Broadcast Methods

    public async Task<bool> BroadcastInstantAsync(ReadOnlyMemory<char> message, WebSocketMessageType type,
        CancellationToken cancellationToken = default)
    {
        var sockets = _clients.Values.Select(x => x.Socket).ToArray();
        return await sockets.Async((client, ct) => client.SendInstantAsync(message, type, ct), x => x,
            cancellationToken);
    }

    public async Task<bool> BroadcastInstantAsync(ReadOnlyMemory<byte> message, WebSocketMessageType type,
        CancellationToken cancellationToken = default)
    {
        var sockets = _clients.Values.Select(x => x.Socket).ToArray();
        return await sockets.Async((client, ct) => client.SendInstantAsync(message, type, ct), x => x,
            cancellationToken);
    }

    public async Task<bool> BroadcastAsync(ReadOnlyMemory<char> message, WebSocketMessageType type,
        CancellationToken cancellationToken = default)
    {
        var sockets = _clients.Values.Select(x => x.Socket).ToArray();
        return await sockets.Async((client, ct) => client.SendAsync(message, type, ct), x => x, cancellationToken);
    }

    public async Task<bool> BroadcastAsync(ReadOnlyMemory<byte> message, WebSocketMessageType type,
        CancellationToken cancellationToken = default)
    {
        var sockets = _clients.Values.Select(x => x.Socket).ToArray();
        return await sockets.Async((client, ct) => client.SendAsync(message, type, ct), x => x, cancellationToken);
    }

    public bool TryBroadcast(ReadOnlyMemory<char> message, WebSocketMessageType type)
    {
        var sockets = _clients.Values.Select(x => x.Socket).ToArray();
        return sockets.Select(x => x.TrySend(message, type)).All(x => x);
    }

    public bool TryBroadcast(ReadOnlyMemory<byte> message, WebSocketMessageType type)
    {
        var sockets = _clients.Values.Select(x => x.Socket).ToArray();
        return sockets.Select(x => x.TrySend(message, type)).All(x => x);
    }

    #endregion

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
            // noop
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

        protected override async Task<bool> SendAsync(ReadOnlyMemory<byte> data, WebSocketMessageType type,
            bool endOfMessage,
            CancellationToken cancellationToken = default)
        {
            if (NativeServerSocket.State is not WebSocketState.Open) return false;
            await NativeServerSocket
                .SendAsync(data, type, endOfMessage: true, cancellationToken)
                .ConfigureAwait(false);
            return true;
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

                        DisconnectionHappenedSource.OnNext(new Disconnected(DisconnectReason.ClientInitiated,
                            NativeServerSocket.CloseStatus, NativeServerSocket.CloseStatusDescription,
                            NativeServerSocket.SubProtocol));
                        break;
                    }

                    var messageBytes = ms.GetBuffer().AsMemory(0, (int)ms.Length);
                    var message = IsTextMessageConversionEnabled && result.MessageType == WebSocketMessageType.Text
                        ? Message.Create(MessageEncoding.GetString(messageBytes.Span).AsMemory())
                        : Message.Create(messageBytes);

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
                    10060 or 110 => DisconnectReason.Undefined,

                    // Connection lost
                    10054 or 104 => DisconnectReason.Undefined,

                    // Aborted/Cancelled
                    10053 or 995 => DisconnectReason.ClientInitiated,

                    _ => DisconnectReason.Undefined
                };
                DisconnectionHappenedSource.OnNext(new Disconnected(reason, Exception: ex));
            }
            catch (WebSocketException ex) when (ex.InnerException is HttpListenerException)
            {
                // noop
            }
            catch (Exception ex)
            {
                ErrorOccurredSource.OnNext(new ServerErrorOccurred(Metadata, ErrorSource.ReceiveLoop, ex));
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }

        public new async Task<bool> StopAsync(WebSocketCloseStatus status, string statusDescription,
            CancellationToken cancellationToken = default)
        {
            if (!IsStarted)
            {
                return false;
            }

            _adapterCts.Try(x => x.Cancel());

            if (NativeServerSocket.State == WebSocketState.Open)
            {
                try
                {
                    await NativeServerSocket.CloseAsync(status, statusDescription, cancellationToken);
                }
                catch
                {
                    NativeServerSocket.Try(x => x.Abort());
                }
            }

            DisconnectionHappenedSource.OnNext(new Disconnected(DisconnectReason.ServerInitiated));

            IsStarted = false;
            IsRunning = false;
            return true;
        }

        protected override void Dispose(bool disposing)
        {
            if (Interlocked.CompareExchange(ref DisposedValue, 1, 0) != 0)
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
            if (Interlocked.CompareExchange(ref DisposedValue, 1, 0) != 0)
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
                        // noop
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
            }
            finally
            {
                if (_adapterDisposeLock.CurrentCount == 0)
                {
                    _adapterDisposeLock.Release();
                }

                _adapterDisposeLock.Dispose();
            }
        }
    }

    #endregion
}