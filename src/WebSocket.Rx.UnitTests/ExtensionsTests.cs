using System.Net;
using System.Net.WebSockets;
using System.Text;
using R3;

namespace WebSocket.Rx.UnitTests;

public class ExtensionsTests
{
    private const int DefaultTimeoutMs = 10000;

    [Fact(Timeout = DefaultTimeoutMs)]
    public void ToPayload_ShouldEncodeText()
    {
        using var payload = "Hello".AsMemory().ToPayload(Encoding.UTF8, WebSocketMessageType.Text);

        Assert.Equal(WebSocketMessageType.Text, payload.Type);
        Assert.Equal(Encoding.UTF8.GetBytes("Hello"), payload.Data.ToArray());
    }

    [Fact(Timeout = DefaultTimeoutMs)]
    public async Task ClientSendInstant_ShouldUseSendAsync()
    {
        var client = new TestReactiveWebSocketClient
        {
            SendTextHandler = (_, _) => Task.FromResult(true),
            SendBinaryHandler = (_, _) => Task.FromResult(false)
        };

        var subject = new Subject<Message>();
        var resultsTask = CollectResultsAsync(client.SendInstant(subject), 2);

        subject.OnNext(Message.Create("text"));
        subject.OnNext(Message.Create(new byte[] { 1, 2 }));

        var results = await resultsTask;

        Assert.Equal(new[] { true, false }, results);
        Assert.Single(client.TextSendCalls);
        Assert.Single(client.BinarySendCalls);
        Assert.Equal("text", client.TextSendCalls[0].Message.ToString());
    }

    [Fact(Timeout = DefaultTimeoutMs)]
    public async Task ClientSend_ShouldUseSendAsync()
    {
        var client = new TestReactiveWebSocketClient
        {
            SendTextHandler = (_, _) => Task.FromResult(false),
            SendBinaryHandler = (_, _) => Task.FromResult(true)
        };

        var subject = new Subject<Message>();
        var resultsTask = CollectResultsAsync(client.Send(subject), 2);

        subject.OnNext(Message.Create("text"));
        subject.OnNext(Message.Create(new byte[] { 9 }));

        var results = await resultsTask;

        Assert.Equal(new[] { false, true }, results);
        Assert.Single(client.TextSendCalls);
        Assert.Single(client.BinarySendCalls);
    }

    [Fact(Timeout = DefaultTimeoutMs)]
    public async Task ClientTrySend_ShouldUseTrySend()
    {
        var client = new TestReactiveWebSocketClient
        {
            TrySendTextHandler = (_, _) => true,
            TrySendBinaryHandler = (_, _) => false
        };

        var subject = new Subject<Message>();
        var resultsTask = CollectResultsAsync(client.TrySend(subject), 2);

        subject.OnNext(Message.Create("text"));
        subject.OnNext(Message.Create(new byte[] { 7, 8 }));

        var results = await resultsTask;

        Assert.Equal(new[] { true, false }, results);
        Assert.Single(client.TextTrySendCalls);
        Assert.Single(client.BinaryTrySendCalls);
    }

    [Fact(Timeout = DefaultTimeoutMs)]
    public async Task ServerSendInstant_ShouldUseSendAsync()
    {
        var server = new TestReactiveWebSocketServer
        {
            SendTextHandler = (_, _, _) => Task.FromResult(true),
            SendBinaryHandler = (_, _, _) => Task.FromResult(false)
        };

        var clientId = Guid.NewGuid();
        var subject = new Subject<ServerMessage>();
        var resultsTask = CollectResultsAsync(server.SendInstant(subject), 2);

        subject.OnNext(new ServerMessage(new Metadata(clientId), Message.Create("hi")));
        subject.OnNext(new ServerMessage(new Metadata(clientId), Message.Create(new byte[] { 1 })));

        var results = await resultsTask;

        Assert.Equal(new[] { true, false }, results);
        Assert.Single(server.TextSendCalls);
        Assert.Single(server.BinarySendCalls);
        Assert.Equal(clientId, server.TextSendCalls[0].ClientId);
    }

    [Fact(Timeout = DefaultTimeoutMs)]
    public async Task ServerSend_ShouldUseSendAsync()
    {
        var server = new TestReactiveWebSocketServer
        {
            SendTextHandler = (_, _, _) => Task.FromResult(false),
            SendBinaryHandler = (_, _, _) => Task.FromResult(true)
        };

        var clientId = Guid.NewGuid();
        var subject = new Subject<ServerMessage>();
        var resultsTask = CollectResultsAsync(server.Send(subject), 2);

        subject.OnNext(new ServerMessage(new Metadata(clientId), Message.Create("hi")));
        subject.OnNext(new ServerMessage(new Metadata(clientId), Message.Create(new byte[] { 2 })));

        var results = await resultsTask;

        Assert.Equal(new[] { false, true }, results);
        Assert.Single(server.TextSendCalls);
        Assert.Single(server.BinarySendCalls);
    }

    [Fact(Timeout = DefaultTimeoutMs)]
    public async Task ServerTrySend_ShouldUseTrySend()
    {
        var server = new TestReactiveWebSocketServer
        {
            TrySendTextHandler = (_, _, _) => true,
            TrySendBinaryHandler = (_, _, _) => false
        };

        var clientId = Guid.NewGuid();
        var subject = new Subject<ServerMessage>();
        var resultsTask = CollectResultsAsync(server.TrySend(subject), 2);

        subject.OnNext(new ServerMessage(new Metadata(clientId), Message.Create("hi")));
        subject.OnNext(new ServerMessage(new Metadata(clientId), Message.Create(new byte[] { 3 })));

        var results = await resultsTask;

        Assert.Equal(new[] { true, false }, results);
        Assert.Single(server.TextTrySendCalls);
        Assert.Single(server.BinaryTrySendCalls);
    }

    [Fact(Timeout = DefaultTimeoutMs)]
    public async Task ServerBroadcastInstant_ShouldUseBroadcastInstantAsync()
    {
        var server = new TestReactiveWebSocketServer
        {
            BroadcastInstantTextHandler = (_, _) => Task.FromResult(true),
            BroadcastInstantBinaryHandler = (_, _) => Task.FromResult(false)
        };

        var subject = new Subject<ServerMessage>();
        var resultsTask = CollectResultsAsync(server.BroadcastInstant(subject), 2);

        subject.OnNext(new ServerMessage(new Metadata(Guid.NewGuid()), Message.Create("hi")));
        subject.OnNext(new ServerMessage(new Metadata(Guid.NewGuid()), Message.Create(new byte[] { 4 })));

        var results = await resultsTask;

        Assert.Equal(new[] { true, false }, results);
        Assert.Single(server.TextBroadcastInstantCalls);
        Assert.Single(server.BinaryBroadcastInstantCalls);
    }

    [Fact(Timeout = DefaultTimeoutMs)]
    public async Task ServerBroadcastAsync_ShouldUseBroadcastAsync()
    {
        var server = new TestReactiveWebSocketServer
        {
            BroadcastTextHandler = (_, _) => Task.FromResult(false),
            BroadcastBinaryHandler = (_, _) => Task.FromResult(true)
        };

        var subject = new Subject<ServerMessage>();
        var resultsTask = CollectResultsAsync(server.BroadcastAsync(subject), 2);

        subject.OnNext(new ServerMessage(new Metadata(Guid.NewGuid()), Message.Create("hi")));
        subject.OnNext(new ServerMessage(new Metadata(Guid.NewGuid()), Message.Create(new byte[] { 5 })));

        var results = await resultsTask;

        Assert.Equal(new[] { false, true }, results);
        Assert.Single(server.TextBroadcastCalls);
        Assert.Single(server.BinaryBroadcastCalls);
    }

    [Fact(Timeout = DefaultTimeoutMs)]
    public async Task ServerTryBroadcast_ShouldUseTryBroadcast()
    {
        var server = new TestReactiveWebSocketServer
        {
            TryBroadcastTextHandler = (_, _) => true,
            TryBroadcastBinaryHandler = (_, _) => false
        };

        var subject = new Subject<ServerMessage>();
        var resultsTask = CollectResultsAsync(server.TryBroadcast(subject), 2);

        subject.OnNext(new ServerMessage(new Metadata(Guid.NewGuid()), Message.Create("hi")));
        subject.OnNext(new ServerMessage(new Metadata(Guid.NewGuid()), Message.Create(new byte[] { 6 })));

        var results = await resultsTask;

        Assert.Equal(new[] { true, false }, results);
        Assert.Single(server.TextTryBroadcastCalls);
        Assert.Single(server.BinaryTryBroadcastCalls);
    }

    private static async Task<IReadOnlyList<bool>> CollectResultsAsync(Observable<bool> observable, int expectedCount)
    {
        var results = new List<bool>();
        var sync = new object();
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        using var subscription = observable.Subscribe(value =>
        {
            lock (sync)
            {
                results.Add(value);
                if (results.Count == expectedCount)
                {
                    tcs.TrySetResult(true);
                }
            }
        });

        await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        return results;
    }

    private sealed class TestReactiveWebSocketClient : IReactiveWebSocketClient
    {
        private readonly Subject<Message> _messageReceivedSource = new();
        private readonly Subject<Connected> _connectedSource = new();
        private readonly Subject<Disconnected> _disconnectedSource = new();
        private readonly Subject<ErrorOccurred> _errorSource = new();

        public List<(ReadOnlyMemory<char> Message, WebSocketMessageType Type)> TextSendCalls { get; } = [];
        public List<(ReadOnlyMemory<byte> Message, WebSocketMessageType Type)> BinarySendCalls { get; } = [];
        public List<(ReadOnlyMemory<char> Message, WebSocketMessageType Type)> TextTrySendCalls { get; } = [];
        public List<(ReadOnlyMemory<byte> Message, WebSocketMessageType Type)> BinaryTrySendCalls { get; } = [];

        public Func<ReadOnlyMemory<char>, WebSocketMessageType, Task<bool>> SendTextHandler { get; set; }
            = (_, _) => Task.FromResult(true);

        public Func<ReadOnlyMemory<byte>, WebSocketMessageType, Task<bool>> SendBinaryHandler { get; set; }
            = (_, _) => Task.FromResult(true);

        public Func<ReadOnlyMemory<char>, WebSocketMessageType, bool> TrySendTextHandler { get; set; }
            = (_, _) => true;

        public Func<ReadOnlyMemory<byte>, WebSocketMessageType, bool> TrySendBinaryHandler { get; set; }
            = (_, _) => true;

        public Uri Url { get; set; } = new("wss://localhost");
        public Observable<Message> MessageReceived => _messageReceivedSource;
        public Observable<Connected> ConnectionHappened => _connectedSource;
        public Observable<Disconnected> DisconnectionHappened => _disconnectedSource;
        public Observable<ErrorOccurred> ErrorOccurred => _errorSource;
        public TimeSpan ConnectTimeout { get; set; }
        public TimeSpan KeepAliveInterval { get; set; }
        public TimeSpan KeepAliveTimeout { get; set; }
        public bool IsReconnectionEnabled { get; set; }
        public bool IsStarted { get; }
        public bool IsRunning { get; }
        public bool SenderRunning { get; }
        public bool IsTextMessageConversionEnabled { get; set; }
        public Encoding MessageEncoding { get; set; } = Encoding.UTF8;
        public ClientWebSocket NativeClient { get; } = new();

        public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task StartOrFailAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<bool> StopAsync(WebSocketCloseStatus status, string statusDescription,
            CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task<bool> StopOrFailAsync(WebSocketCloseStatus status, string statusDescription,
            CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task ReconnectAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ReconnectOrFailAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<bool> SendInstantAsync(ReadOnlyMemory<char> message, WebSocketMessageType type,
            CancellationToken cancellationToken = default) => SendAsync(message, type, cancellationToken);
        public Task<bool> SendInstantAsync(ReadOnlyMemory<byte> message, WebSocketMessageType type,
            CancellationToken cancellationToken = default) => SendAsync(message, type, cancellationToken);
        public Task<bool> SendAsync(ReadOnlyMemory<byte> message, WebSocketMessageType type,
            CancellationToken cancellationToken = default)
        {
            BinarySendCalls.Add((message, type));
            return SendBinaryHandler(message, type);
        }
        public Task<bool> SendAsync(ReadOnlyMemory<char> message, WebSocketMessageType type,
            CancellationToken cancellationToken = default)
        {
            TextSendCalls.Add((message, type));
            return SendTextHandler(message, type);
        }
        public bool TrySend(ReadOnlyMemory<byte> message, WebSocketMessageType type)
        {
            BinaryTrySendCalls.Add((message, type));
            return TrySendBinaryHandler(message, type);
        }
        public bool TrySend(ReadOnlyMemory<char> message, WebSocketMessageType type)
        {
            TextTrySendCalls.Add((message, type));
            return TrySendTextHandler(message, type);
        }
        public void StreamFakeMessage(Message message) => _messageReceivedSource.OnNext(message);
        public void Dispose() { }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class TestReactiveWebSocketServer : IReactiveWebSocketServer
    {
        private readonly Subject<ClientConnected> _clientConnectedSource = new();
        private readonly Subject<ClientDisconnected> _clientDisconnectedSource = new();
        private readonly Subject<ServerMessage> _messageSource = new();
        private readonly Subject<ServerErrorOccurred> _errorSource = new();
        private readonly Dictionary<Guid, Metadata> _connectedClients = new();

        public List<(Guid ClientId, ReadOnlyMemory<char> Message, WebSocketMessageType Type)> TextSendCalls { get; } = [];
        public List<(Guid ClientId, ReadOnlyMemory<byte> Message, WebSocketMessageType Type)> BinarySendCalls { get; } = [];
        public List<(Guid ClientId, ReadOnlyMemory<char> Message, WebSocketMessageType Type)> TextTrySendCalls { get; } = [];
        public List<(Guid ClientId, ReadOnlyMemory<byte> Message, WebSocketMessageType Type)> BinaryTrySendCalls { get; } = [];
        public List<(ReadOnlyMemory<char> Message, WebSocketMessageType Type)> TextBroadcastInstantCalls { get; } = [];
        public List<(ReadOnlyMemory<byte> Message, WebSocketMessageType Type)> BinaryBroadcastInstantCalls { get; } = [];
        public List<(ReadOnlyMemory<char> Message, WebSocketMessageType Type)> TextBroadcastCalls { get; } = [];
        public List<(ReadOnlyMemory<byte> Message, WebSocketMessageType Type)> BinaryBroadcastCalls { get; } = [];
        public List<(ReadOnlyMemory<char> Message, WebSocketMessageType Type)> TextTryBroadcastCalls { get; } = [];
        public List<(ReadOnlyMemory<byte> Message, WebSocketMessageType Type)> BinaryTryBroadcastCalls { get; } = [];

        public Func<Guid, ReadOnlyMemory<char>, WebSocketMessageType, Task<bool>> SendTextHandler { get; set; }
            = (_, _, _) => Task.FromResult(true);

        public Func<Guid, ReadOnlyMemory<byte>, WebSocketMessageType, Task<bool>> SendBinaryHandler { get; set; }
            = (_, _, _) => Task.FromResult(true);

        public Func<Guid, ReadOnlyMemory<char>, WebSocketMessageType, bool> TrySendTextHandler { get; set; }
            = (_, _, _) => true;

        public Func<Guid, ReadOnlyMemory<byte>, WebSocketMessageType, bool> TrySendBinaryHandler { get; set; }
            = (_, _, _) => true;

        public Func<ReadOnlyMemory<char>, WebSocketMessageType, Task<bool>> BroadcastInstantTextHandler { get; set; }
            = (_, _) => Task.FromResult(true);

        public Func<ReadOnlyMemory<byte>, WebSocketMessageType, Task<bool>> BroadcastInstantBinaryHandler { get; set; }
            = (_, _) => Task.FromResult(true);

        public Func<ReadOnlyMemory<char>, WebSocketMessageType, Task<bool>> BroadcastTextHandler { get; set; }
            = (_, _) => Task.FromResult(true);

        public Func<ReadOnlyMemory<byte>, WebSocketMessageType, Task<bool>> BroadcastBinaryHandler { get; set; }
            = (_, _) => Task.FromResult(true);

        public Func<ReadOnlyMemory<char>, WebSocketMessageType, bool> TryBroadcastTextHandler { get; set; }
            = (_, _) => true;

        public Func<ReadOnlyMemory<byte>, WebSocketMessageType, bool> TryBroadcastBinaryHandler { get; set; }
            = (_, _) => true;

        public TimeSpan IdleConnection { get; set; }
        public TimeSpan ConnectTimeout { get; set; }
        public Encoding MessageEncoding { get; set; } = Encoding.UTF8;
        public bool IsTextMessageConversionEnabled { get; set; }
        public int ClientCount => _connectedClients.Count;
        public IReadOnlyDictionary<Guid, Metadata> ConnectedClients => _connectedClients;
        public Observable<ClientConnected> ClientConnected => _clientConnectedSource;
        public Observable<ClientDisconnected> ClientDisconnected => _clientDisconnectedSource;
        public Observable<ServerMessage> Messages => _messageSource;
        public Observable<ServerErrorOccurred> ErrorOccurred => _errorSource;

        public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<bool> StopAsync(WebSocketCloseStatus status, string statusDescription,
            CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task<bool> SendInstantAsync(Guid clientId, ReadOnlyMemory<char> message, WebSocketMessageType type,
            CancellationToken cancellationToken = default) => SendAsync(clientId, message, type, cancellationToken);
        public Task<bool> SendInstantAsync(Guid clientId, ReadOnlyMemory<byte> message, WebSocketMessageType type,
            CancellationToken cancellationToken = default) => SendAsync(clientId, message, type, cancellationToken);
        public Task<bool> SendAsync(Guid clientId, ReadOnlyMemory<char> message, WebSocketMessageType type,
            CancellationToken cancellationToken = default)
        {
            TextSendCalls.Add((clientId, message, type));
            return SendTextHandler(clientId, message, type);
        }
        public Task<bool> SendAsync(Guid clientId, ReadOnlyMemory<byte> message, WebSocketMessageType type,
            CancellationToken cancellationToken = default)
        {
            BinarySendCalls.Add((clientId, message, type));
            return SendBinaryHandler(clientId, message, type);
        }
        public bool TrySend(Guid clientId, ReadOnlyMemory<char> message, WebSocketMessageType type)
        {
            TextTrySendCalls.Add((clientId, message, type));
            return TrySendTextHandler(clientId, message, type);
        }
        public bool TrySend(Guid clientId, ReadOnlyMemory<byte> message, WebSocketMessageType type)
        {
            BinaryTrySendCalls.Add((clientId, message, type));
            return TrySendBinaryHandler(clientId, message, type);
        }
        public Task<bool> BroadcastInstantAsync(ReadOnlyMemory<char> message, WebSocketMessageType type,
            CancellationToken cancellationToken = default)
        {
            TextBroadcastInstantCalls.Add((message, type));
            return BroadcastInstantTextHandler(message, type);
        }
        public Task<bool> BroadcastInstantAsync(ReadOnlyMemory<byte> message, WebSocketMessageType type,
            CancellationToken cancellationToken = default)
        {
            BinaryBroadcastInstantCalls.Add((message, type));
            return BroadcastInstantBinaryHandler(message, type);
        }
        public Task<bool> BroadcastAsync(ReadOnlyMemory<char> message, WebSocketMessageType type,
            CancellationToken cancellationToken = default)
        {
            TextBroadcastCalls.Add((message, type));
            return BroadcastTextHandler(message, type);
        }
        public Task<bool> BroadcastAsync(ReadOnlyMemory<byte> message, WebSocketMessageType type,
            CancellationToken cancellationToken = default)
        {
            BinaryBroadcastCalls.Add((message, type));
            return BroadcastBinaryHandler(message, type);
        }
        public bool TryBroadcast(ReadOnlyMemory<char> message, WebSocketMessageType type)
        {
            TextTryBroadcastCalls.Add((message, type));
            return TryBroadcastTextHandler(message, type);
        }
        public bool TryBroadcast(ReadOnlyMemory<byte> message, WebSocketMessageType type)
        {
            BinaryTryBroadcastCalls.Add((message, type));
            return TryBroadcastBinaryHandler(message, type);
        }
        public void Dispose() { }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
