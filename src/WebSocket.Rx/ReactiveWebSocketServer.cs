using System.Buffers;
using System.Collections.Concurrent;
using System.Net;
using System.Net.WebSockets;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Text;
using System.Threading.Channels;
using WebSocket.Rx.Internal;

namespace WebSocket.Rx;

public class ReactiveWebSocketServer : IReactiveWebSocketServer
{
    #region Fields & State (wie Client)

    private readonly HttpListener _listener;
    private Task? _serverLoopTask;
    private readonly ConcurrentDictionary<Guid, ServerWebSocketAdapter> _clients = new();
    private readonly ConcurrentDictionary<Guid, Channel<Payload>> _sendChannels = new();

    private readonly Subject<ClientConnected> _clientConnectedSource = new();
    private readonly Subject<ClientDisconnected> _clientDisconnectedSource = new();
    private readonly Subject<ServerReceivedMessage> _messageReceivedSource = new();

    private CancellationTokenSource? _mainCts;
    private readonly AsyncLock _serverLock = new();

    #endregion

    #region Properties (Client-Style)

    public bool IsDisposing { get; private set; }

    public TimeSpan InactivityTimeout { get; set; } = TimeSpan.FromSeconds(30);
    public TimeSpan ConnectTimeout { get; set; } = TimeSpan.FromSeconds(10);
    public bool IsReconnectionEnabled { get; set; } = true;
    public Encoding MessageEncoding { get; set; } = Encoding.UTF8;
    public bool IsTextMessageConversionEnabled { get; set; } = true;
    public int ClientCount => _clients.Count;

    public IReadOnlyDictionary<Guid, Metadata> ConnectedClients
        => _clients.ToDictionary(x => x.Key, x => x.Value.Metadata);

    public IObservable<ClientConnected> ClientConnected => _clientConnectedSource.AsObservable();
    public IObservable<ClientDisconnected> ClientDisconnected => _clientDisconnectedSource.AsObservable();
    public IObservable<ServerReceivedMessage> Messages => _messageReceivedSource.AsObservable();

    #endregion

    public ReactiveWebSocketServer(string prefix = "http://localhost:8080/")
    {
        _listener = new HttpListener
        {
            Prefixes = { prefix },
            TimeoutManager =
            {
                IdleConnection = InactivityTimeout,
                RequestQueue = TimeSpan.FromSeconds(30),
                EntityBody = TimeSpan.FromMinutes(2)
            },
            AuthenticationSchemes = AuthenticationSchemes.Anonymous,
            UnsafeConnectionNtlmAuthentication = false
        };
    }

    #region Start/Stop

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        using var disposable = await _serverLock.LockAsync();
        _mainCts = new CancellationTokenSource();

        _listener.Start();

        _serverLoopTask = Task.Run(() => ServerLoopAsync(_mainCts.Token), _mainCts.Token);
    }

    private async Task ServerLoopAsync(CancellationToken cancellationToken = default)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var ctx = await _listener.GetContextAsync();
                if (ctx.Request.IsWebSocketRequest)
                {
                    _ = HandleWebSocketAsync(ctx, ctx.GetMetadata());
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

    public async Task<bool> StopAsync(WebSocketCloseStatus status = WebSocketCloseStatus.NormalClosure,
        string reason = "Server stopping")
    {
        if (IsDisposing)
        {
            return false;
        }

        using var async = await _serverLock.LockAsync();
        IsDisposing = true;

        _mainCts?.Cancel();

        var disconnectTasks = _clients.Values.Select(client => client.StopAsync(status, reason));
        await Task.WhenAll(disconnectTasks).ConfigureAwait(false);

        _listener.Stop();
        _listener.Close();

        _clientConnectedSource.OnCompleted();
        _clientDisconnectedSource.OnCompleted();
        _messageReceivedSource.OnCompleted();

        return true;
    }

    #endregion

    #region Send Methods

    public bool SendToClient(Guid clientId, byte[] data) => SendToClient(clientId, data, WebSocketMessageType.Binary);

    public bool SendToClient(Guid clientId, string text)
        => SendToClient(clientId, MessageEncoding.GetBytes(text), WebSocketMessageType.Binary);

    public bool SendToClient(Guid clientId, byte[] data, WebSocketMessageType type)
    {
        return _sendChannels.TryGetValue(clientId, out var channel) && channel.Writer.TryWrite(new Payload(data, type));
    }

    public async Task<bool> SendToClientInstantAsync(Guid clientId, byte[] data,
        CancellationToken cancellationToken = default)
    {
        if (!_clients.TryGetValue(clientId, out var client) || client.NativeClient.State is not WebSocketState.Open)
        {
            return false;
        }

        await client.NativeClient.SendAsync(data, WebSocketMessageType.Binary, true, cancellationToken);
        return true;
    }

    public bool Broadcast(byte[] data) => _clients.Keys.AsParallel().All(id => SendToClient(id, data));

    public async Task<bool> BroadcastInstantAsync(byte[] data, CancellationToken cancellationToken = default)
    {
        var tasks = _clients.Values
            .Where(c => c.NativeClient.State == WebSocketState.Open)
            .Select(c =>
                c.NativeClient.SendAsync(data, WebSocketMessageType.Binary, true, cancellationToken));

        await Task.WhenAll(tasks);
        return true;
    }

    #endregion

    #region Client Handling

    private async Task HandleWebSocketAsync(HttpListenerContext context, Metadata metadata)
    {
        System.Net.WebSockets.WebSocket? socket = null;
        ServerWebSocketAdapter? client;

        try
        {
            using var timeoutCts = new CancellationTokenSource(ConnectTimeout);

            var webSocketCtx = await context
                .AcceptWebSocketAsync(null, InactivityTimeout)
                .ConfigureAwait(false);

            client = new ServerWebSocketAdapter(webSocketCtx.WebSocket, metadata)
            {
                IsTextMessageConversionEnabled = IsTextMessageConversionEnabled,
                MessageEncoding = MessageEncoding,
                KeepAliveInterval = InactivityTimeout
            };

            var sendChannel = Channel.CreateUnbounded<Payload>(new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = false
            });

            _sendChannels[metadata.Id] = sendChannel;
            _clients[metadata.Id] = client;

            _clientConnectedSource.OnNext(new ClientConnected(metadata, Connected.Create(ConnectReason.Initial)));

            _ = client.MessageReceived.Subscribe(
                msg => _messageReceivedSource.OnNext(new ServerReceivedMessage(client.Metadata, msg)),
                ex => HandleClientError(metadata.Id, ex),
                () => HandleClientDisconnected(metadata.Id)
            );
        }
        catch (Exception ex)
        {
            HandleClientError(metadata.Id, ex);
            socket?.Dispose();
        }
    }

    private void HandleClientError(Guid id, Exception ex)
    {
        if (_clients.TryRemove(id, out var client) && _sendChannels.Remove(id, out _))
        {
            _clientDisconnectedSource.OnNext(new ClientDisconnected(client.Metadata,
                Disconnected.Create(DisconnectReason.Error, ex)));
        }
    }

    private void HandleClientDisconnected(Guid id)
    {
        if (_clients.TryRemove(id, out var client) && _sendChannels.Remove(id, out _))
        {
            _clientDisconnectedSource.OnNext(new ClientDisconnected(client.Metadata,
                Disconnected.Create(DisconnectReason.ClientInitiated)));
        }
    }

    #endregion

    public void Dispose()
    {
        if (IsDisposing) return;
        _ = StopAsync();

        _clientConnectedSource.Dispose();
        _clientDisconnectedSource.Dispose();
        _messageReceivedSource.Dispose();
        _mainCts?.Dispose();
        GC.SuppressFinalize(this);
    }

    public sealed class ServerWebSocketAdapter : ReactiveWebSocketClient
    {
        private readonly CancellationTokenSource _adapterCts = new();
        public System.Net.WebSockets.WebSocket NativeServerSocket { get; }
        public Metadata Metadata { get; }


        public ServerWebSocketAdapter(System.Net.WebSockets.WebSocket serverSocket, Metadata metadata)
            : base(new Uri("ws://server-adapter"))
        {
            Metadata = metadata;
            NativeServerSocket = serverSocket ?? throw new ArgumentNullException(nameof(serverSocket));

            IsStarted = true;
            IsRunning = true;
            ConnectionHappenedSource.OnNext(Connected.Create(ConnectReason.Initial));

            SendChannel = Channel.CreateUnbounded<Payload>(new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = false
            });

            MainCts = _adapterCts;
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
                        DisconnectionHappenedSource.OnNext(Disconnected.Create(DisconnectReason.ServerInitiated));
                        return;
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
            catch (WebSocketException ex)
            {
                DisconnectionHappenedSource.OnNext(Disconnected.Create(DisconnectReason.ConnectionLost, ex));
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

        public async Task<bool> StopAsync(WebSocketCloseStatus status, string statusDescription)
        {
            await _adapterCts.CancelAsync();

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
            return true;
        }

        protected override void Dispose(bool disposing)
        {
            if (IsDisposed) return;
            IsDisposed = true;

            _adapterCts.Try(x => x.Cancel());

            _ = StopAsync(WebSocketCloseStatus.NormalClosure, "Adapter disposed");

            ReconnectCts?.Try(x => x.Cancel());
            ReconnectCts?.Try(x => x.Dispose());

            NativeServerSocket.Dispose();

            MessageReceivedSource.OnCompleted();
            ConnectionHappenedSource.OnCompleted();
            DisconnectionHappenedSource.OnCompleted();

            MessageReceivedSource.Dispose();
            ConnectionHappenedSource.Dispose();
            DisconnectionHappenedSource.Dispose();
        }
    }
}

public record ClientConnected(Metadata Metadata, Connected Event);

public record ClientDisconnected(Metadata Metadata, Disconnected Event);