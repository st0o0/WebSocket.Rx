using System.Buffers;
using System.Collections.Concurrent;
using System.Net;
using System.Net.WebSockets;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Security.Authentication.ExtendedProtection;
using System.Text;
using System.Threading.Channels;
using WebSocket.Rx.Internal;

namespace WebSocket.Rx;

public class Temp
{
    public class ReactiveWebSocketServer : IReactiveWebSocketServer
    {
        #region Fields & State (wie Client)

        private readonly HttpListener _listener;
        private readonly ConcurrentDictionary<string, ReactiveWebSocketClient> _clients = new();
        private readonly ConcurrentDictionary<string, Channel<Payload>> _sendChannels = new();

        private readonly Subject<ClientConnected> _clientConnected = new();
        private readonly Subject<ClientDisconnected> _clientDisconnected = new();
        private readonly Subject<ReceivedMessage> _messageReceived = new();

        private CancellationTokenSource? _cts;
        private readonly AsyncLock _serverLock = new();
        private bool _isDisposing;

        #endregion

        #region Properties (Client-Style)

        public TimeSpan InactivityTimeout { get; set; } = TimeSpan.FromSeconds(30);


        public TimeSpan ConnectTimeout { get; set; } = TimeSpan.FromSeconds(10);

        public bool IsReconnectionEnabled { get; set; } = true;

        public Encoding MessageEncoding { get; set; } = Encoding.UTF8;

        public bool IsTextMessageConversionEnabled { get; set; } = true;

        public int ClientCount => _clients.Count;

        public IReadOnlyDictionary<string, ReactiveWebSocketClient> ConnectedClients => _clients;

        public IObservable<ClientConnected> ClientConnected => _clientConnected.AsObservable();
        public IObservable<ClientDisconnected> ClientDisconnected => _clientDisconnected.AsObservable();
        public IObservable<ReceivedMessage> Messages => _messageReceived.AsObservable();

        #endregion

        public ReactiveWebSocketServer(string prefix = "http://localhost:8080/")
        {
            _listener = new HttpListener
            {
                TimeoutManager = { IdleConnection = InactivityTimeout },
                Prefixes = { prefix }
            };
        }

        #region Start/Stop (Client-Style mit Lock)

        public async Task StartAsync(CancellationToken cancellationToken = default)
        {
            using (await _serverLock.LockAsync())
            {
                if (_cts != null) return;

                _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                _listener.Start();
                Console.WriteLine($"Reactive Server gestartet: {_listener.Prefixes.First()}");

                _ = Task.Run(ServerLoopAsync, _cts.Token);
            }
        }

        private async Task ServerLoopAsync()
        {
            while (!_cts!.Token.IsCancellationRequested)
            {
                try
                {
                    var context = await _listener.GetContextAsync();
                    if (context.Request.IsWebSocketRequest)
                    {
                        _ = HandleWebSocketAsync(context);
                    }
                    else
                    {
                        context.Response.StatusCode = 400;
                        context.Response.Close();
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
            using (await _serverLock.LockAsync())
            {
                if (_isDisposing) return false;
                _isDisposing = true;

                _cts?.Cancel();

                var disconnectTasks = _clients.Values.Select(client => client.StopAsync(status, reason));
                await Task.WhenAll(disconnectTasks).ConfigureAwait(false);

                _listener.Stop();
                _listener.Close();

                _clientConnected.OnCompleted();
                _clientDisconnected.OnCompleted();
                _messageReceived.OnCompleted();

                return true;
            }
        }

        #endregion

        #region Send Methods (Channel-based wie Client)

        public bool SendToClient(string clientName, byte[] data)
            => SendToClient(clientName, data, WebSocketMessageType.Binary);

        public bool SendToClient(string clientName, string text)
            => SendToClient(clientName, MessageEncoding.GetBytes(text), WebSocketMessageType.Binary);

        public bool SendToClient(string clientName, byte[] data, WebSocketMessageType type)
        {
            if (_sendChannels.TryGetValue(clientName, out var channel))
            {
                return channel.Writer.TryWrite(new Payload(data, type));
            }

            return false;
        }

        public async Task<bool> SendToClientInstantAsync(string clientName, byte[] data,
            CancellationToken cancellationToken = default)
        {
            if (!_clients.TryGetValue(clientName, out var client) || client.NativeClient?.State != WebSocketState.Open)
            {
                return false;
            }

            await client.NativeClient.SendAsync(data, WebSocketMessageType.Binary, true, cancellationToken);
            return true;
        }

        public bool Broadcast(byte[] data) => _clients.Keys.AsParallel()
            .All(id => SendToClient(id, data));

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

        #region Client Handling (mit allen Client-Features)

        private async Task HandleWebSocketAsync(HttpListenerContext context)
        {
            System.Net.WebSockets.WebSocket? socket = null;
            ReactiveWebSocketClient? client = null;

            try
            {
                using var timeoutCts = new CancellationTokenSource(ConnectTimeout);
                var t = await context.AcceptWebSocketAsync(null, TimeSpan.FromSeconds(30));
                socket = t.WebSocket;

                client = new ReactiveWebSocketClient(context.Request.Url!)
                {
                    Name = $"Client-{Guid.NewGuid():N}[8..12]",
                    IsTextMessageConversionEnabled = IsTextMessageConversionEnabled,
                    MessageEncoding = MessageEncoding,
                    InactivityTimeout = InactivityTimeout
                };

                var sendChannel = Channel.CreateUnbounded<Payload>(new UnboundedChannelOptions
                {
                    SingleReader = true,
                    SingleWriter = false
                });

                _sendChannels[client.Name] = sendChannel;
                _clients[client.Name] = client;

                _clientConnected.OnNext(new ClientConnected(client.Name));

                var messageSub = client.MessageReceived.Subscribe(
                    msg => _messageReceived.OnNext(msg),
                    ex => HandleClientError(client.Name, ex),
                    () => HandleClientDisconnected(client.Name)
                );
            }
            catch (Exception ex)
            {
                HandleClientError(string.Empty, ex);
                socket?.Dispose();
            }
        }

        private void HandleClientError(string clientName, Exception ex)
        {
            _clients.TryRemove(clientName, out _);
            _sendChannels.TryRemove(clientName, out _);
            _clientDisconnected.OnNext(new ClientDisconnected(clientName, DisconnectReason.Error, ex));
        }

        private void HandleClientDisconnected(string clientName)
        {
            _clients.TryRemove(clientName, out _);
            _sendChannels.TryRemove(clientName, out _);
            _clientDisconnected.OnNext(new ClientDisconnected(clientName, DisconnectReason.ClientInitiated));
        }

        #endregion

        public void Dispose()
        {
            if (_isDisposing) return;
            StopAsync().GetAwaiter().GetResult();

            _clientConnected.Dispose();
            _clientDisconnected.Dispose();
            _messageReceived.Dispose();
            _cts?.Dispose();
            GC.SuppressFinalize(this);
        }
    }

    public sealed class ServerWebSocketAdapter : ReactiveWebSocketClient
    {
        private readonly CancellationTokenSource _adapterCts = new();
        private Channel<Payload>? _sendChannel;

        public System.Net.WebSockets.WebSocket NativeServerSocket { get; }


        public ServerWebSocketAdapter(System.Net.WebSockets.WebSocket serverSocket) : base(
            new Uri("ws://server-adapter"))
        {
            NativeServerSocket = serverSocket ?? throw new ArgumentNullException(nameof(serverSocket));

            IsStarted = true;
            IsRunning = true;
            Name = $"ServerClient-{Guid.NewGuid():N[8..12]}";
            ConnectionHappenedSource.OnNext(Connected.Create(ConnectReason.Initial));

            SendChannel = Channel.CreateUnbounded<Payload>(new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = false
            });

            MainCts = _adapterCts;
            SendLoopTask = Task.Run(() => SendLoopAdapterAsync(_adapterCts.Token), CancellationToken.None);
            ReceiveLoopTask = Task.Run(() => ReceiveLoopAdapterAsync(_adapterCts.Token), CancellationToken.None);
            HeartbeatTask = Task.Run(() => HeartbeatMonitorAdapterAsync(_adapterCts.Token), CancellationToken.None);
        }

        private async Task SendLoopAdapterAsync(CancellationToken cancellationToken)
        {
            try
            {
                await foreach (var (data, messageType) in SendChannel.Reader.ReadAllAsync(cancellationToken))
                {
                    if (NativeServerSocket.State == WebSocketState.Open)
                    {
                        await NativeServerSocket
                            .SendAsync(data, messageType, endOfMessage: true, cancellationToken)
                            .ConfigureAwait(false);
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

                    LastMessageReceived = DateTime.UtcNow;

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

        private async Task HeartbeatMonitorAdapterAsync(CancellationToken cancellationToken)
        {
            try
            {
                while (!cancellationToken.IsCancellationRequested && NativeServerSocket.State == WebSocketState.Open)
                {
                    await Task.Delay(InactivityTimeout, cancellationToken).ConfigureAwait(false);

                    var timeSinceLastMessage = DateTime.UtcNow - LastMessageReceived;

                    if (timeSinceLastMessage <= InactivityTimeout) continue;
                    DisconnectionHappenedSource.OnNext(Disconnected.Create(DisconnectReason.Timeout));

                    await StopAsync(WebSocketCloseStatus.PolicyViolation, "Connection timeout due to inactivity");
                    return;
                }
            }
            catch (OperationCanceledException ex)
            {
                _ = ex;
            }
            catch (Exception ex)
            {
                DisconnectionHappenedSource.OnNext(Disconnected.Create(DisconnectReason.Error, ex));
            }
        }

        public new bool Send(byte[] message)
        {
            if (!IsRunning || message.Length == 0)
            {
                return false;
            }

            return _sendChannel!.Writer.TryWrite(new Payload(message, WebSocketMessageType.Binary));
        }

        public new bool Send(string message)
        {
            if (string.IsNullOrEmpty(message))
            {
                return false;
            }

            var bytes = MessageEncoding.GetBytes(message);
            return Send(bytes);
        }

        public new bool SendAsText(string message)
        {
            if (!IsRunning || message.Length == 0)
            {
                return false;
            }

            return _sendChannel!.Writer.TryWrite(new Payload(MessageEncoding.GetBytes(message),
                WebSocketMessageType.Text));
        }

        public new async Task SendInstant(byte[] message)
        {
            if (NativeServerSocket.State != WebSocketState.Open || message.Length == 0)
            {
                return;
            }

            await NativeServerSocket
                .SendAsync(message, WebSocketMessageType.Binary, endOfMessage: true, _adapterCts.Token)
                .ConfigureAwait(false);
        }

        public new async Task SendInstant(string message)
        {
            if (string.IsNullOrEmpty(message))
            {
                return;
            }

            var bytes = MessageEncoding.GetBytes(message);
            await SendInstant(bytes);
        }

        internal ChannelWriter<Payload> SendChannelWriter => _sendChannel!.Writer;

        public new async Task<bool> StopAsync(WebSocketCloseStatus status, string statusDescription)
        {
            _adapterCts.Cancel();

            if (NativeServerSocket.State == WebSocketState.Open)
            {
                try
                {
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                    await NativeServerSocket.CloseAsync(status, statusDescription, cts.Token);
                }
                catch
                {
                    NativeServerSocket.Abort();
                }
            }

            DisconnectionHappenedSource.OnNext(Disconnected.Create(DisconnectReason.ClientInitiated));
            await CleanupAsync();

            return true;
        }

        public override void Dispose()
        {
            if (IsDisposing) return;
            IsDisposing = true;

            _adapterCts.Cancel();
            _ = StopAsync(WebSocketCloseStatus.NormalClosure, "Adapter disposed");

            NativeServerSocket.Dispose();
            base.Dispose();
        }
    }

    public record ClientConnected(string Name);

    public record ClientDisconnected(string Name, DisconnectReason Reason, Exception? Error = null);
}

public class ReactiveWebSocketServer : IDisposable
{
    private readonly HttpListener _listener;
    private readonly ConcurrentDictionary<Guid, WebSocketClient> _clients = new();
    private readonly Subject<ReceivedMessage> _messageReceived = new();
    private readonly Subject<WebSocketClient> _clientConnected = new();
    private CancellationTokenSource? _cts;

    public IObservable<ReceivedMessage> Messages => _messageReceived.AsObservable();

    public IObservable<WebSocketClient> Clients => _clientConnected.AsObservable();

    public IReadOnlyDictionary<Guid, WebSocketClient> ConnectedClients => _clients;

    public Guid[] GetClientIds() => _clients.Keys.ToArray();

    public int ClientCount => _clients.Count;

    public ReactiveWebSocketServer(string prefix = "http://localhost:8080/")
    {
        _listener = new HttpListener();
        _listener.Prefixes.Add(prefix);
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _listener.Start();


        while (!_cts.Token.IsCancellationRequested)
        {
            try
            {
                var context = await _listener.GetContextAsync();
                if (context.Request.IsWebSocketRequest)
                {
                    _ = HandleWebSocketAsync(context);
                }
                else
                {
                    context.Response.StatusCode = 400;
                    context.Response.Close();
                }
            }
            catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException)
            {
                break;
            }
        }
    }

    public async Task<bool> BroadcastBinaryAsync(byte[] data, CancellationToken cancellationToken = default)
    {
        var tasks = _clients.Values.Select(c => c.SendAsync(data, cancellationToken));
        var results = await Task.WhenAll(tasks);
        return results.All(x => x);
    }

    public async Task<bool> BroadcastBinaryAsync(ArraySegment<byte> data, CancellationToken cancellationToken = default)
    {
        var tasks = _clients.Values.Select(c => c.SendAsync(data, cancellationToken));
        var results = await Task.WhenAll(tasks);
        return results.All(x => x);
    }

    public async Task<bool> BroadcastTextAsync(string message, CancellationToken cancellationToken = default)
    {
        var tasks = _clients.Values.Select(c => c.SendTextAsync(message, cancellationToken));
        var results = await Task.WhenAll(tasks);
        return results.All(x => x);
    }

    public async Task<bool> SendToClientBinaryAsync(Guid clientId, byte[] data)
    {
        if (_clients.TryGetValue(clientId, out var client))
        {
            return await client.SendAsync(data);
        }

        return false;
    }

    private async Task HandleWebSocketAsync(HttpListenerContext context)
    {
        WebSocketContext wsContext;
        try
        {
            wsContext = await context.AcceptWebSocketAsync(null);
        }
        catch
        {
            context.Response.StatusCode = 500;
            context.Response.Close();
            return;
        }

        var client = new WebSocketClient(wsContext.WebSocket);
        _clients[client.Id] = client;
        _clientConnected.OnNext(client);

        client.Messages.Subscribe(
            message => _messageReceived.OnNext(message),
            _ => { }, // TODO:
            () => { } // TODO:
        );

        await client.ReceiveLoopAsync(_cts!.Token);
    }

    public void Dispose()
    {
        foreach (var client in _clients.Values)
        {
            _ = client.CloseAsync();
        }

        _clientConnected.OnCompleted();
        _messageReceived.OnCompleted();
        _cts?.Cancel();
        _listener.Stop();
        _listener.Close();
        _clientConnected.Dispose();
        _messageReceived.Dispose();
        GC.SuppressFinalize(this);
    }

    public sealed class WebSocketClient(System.Net.WebSockets.WebSocket socket)
    {
        public Guid Id { get; } = Guid.NewGuid();
        private readonly Subject<ReceivedMessage> _messageReceived = new();

        public IObservable<ReceivedMessage> Messages => _messageReceived.AsObservable();

        public async Task ReceiveLoopAsync(CancellationToken cancellationToken)
        {
            var buffer = ArrayPool<byte>.Shared.Rent(64 * 1024);

            try
            {
                while (socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
                {
                    var result = await socket
                        .ReceiveAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)
                        .ConfigureAwait(false);

                    var content = buffer.AsSpan(0, result.Count).ToArray();

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        const WebSocketCloseStatus state = WebSocketCloseStatus.NormalClosure;
                        await socket
                            .CloseOutputAsync(state, "Closing", cancellationToken)
                            .ConfigureAwait(false);
                        break;
                    }

                    var message = result.MessageType switch
                    {
                        WebSocketMessageType.Text => ReceivedMessage.TextMessage(
                            Encoding.UTF8.GetString(buffer.AsSpan(0, result.Count))),
                        WebSocketMessageType.Binary => ReceivedMessage.BinaryMessage(buffer.AsMemory(0, result.Count)
                            .ToArray()),
                        _ => ReceivedMessage.Empty()
                    };

                    _messageReceived.OnNext(message);
                }
            }
            catch (Exception ex)
            {
                _messageReceived.OnError(ex);
            }
        }

        public async Task<bool> SendAsync(byte[] data, CancellationToken cancellationToken = default)
        {
            if (socket.State != WebSocketState.Open || data.Length == 0) return false;

            await using var writeStream =
                WebSocketStream.CreateWritableMessageStream(socket, WebSocketMessageType.Binary);

            try
            {
                await writeStream.WriteAsync(data, cancellationToken);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public async Task<bool> SendTextAsync(string message, CancellationToken cancellationToken = default)
        {
            if (socket.State != WebSocketState.Open || string.IsNullOrEmpty(message)) return false;

            var bytes = Encoding.UTF8.GetBytes(message);
            return await SendAsync(bytes, cancellationToken);
        }

        public async Task<bool> SendStreamAsync(Stream stream, CancellationToken cancellationToken = default)
        {
            if (socket.State != WebSocketState.Open) return false;

            await using var writeStream =
                WebSocketStream.CreateWritableMessageStream(socket, WebSocketMessageType.Binary);
            var buffer = new byte[4096];

            try
            {
                int bytesRead;
                while ((bytesRead = await stream.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    await writeStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        public Task<bool> SendAsync(ArraySegment<byte> data, CancellationToken ct = default)
            => SendAsync(data.ToArray(), ct);

        public Task<bool> SendAsync(ReadOnlyMemory<byte> data, CancellationToken ct = default)
            => SendAsync(data.ToArray(), ct);

        public async Task CloseAsync()
        {
            try
            {
                if (socket.State == WebSocketState.Open)
                    await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Server closing",
                        CancellationToken.None);
            }
            catch
            {
                socket.Abort();
            }
        }
    }
}