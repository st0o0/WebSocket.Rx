using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using R3;

namespace WebSocket.Rx.Tests;

public class ReactiveWebSocketServerTests : IAsyncLifetime
{
    private readonly ITestOutputHelper _output;
    private ReactiveWebSocketServer _server = null!;
    private string _serverUrl = string.Empty;
    private string _webSocketUrl = string.Empty;

    private const int DefaultTimeoutMs = 10000;
    private const int LongTimeoutMs = 30000;

    public ReactiveWebSocketServerTests(ITestOutputHelper output)
    {
        _output = output;
    }

    public async ValueTask InitializeAsync()
    {
        const int maxRetries = 10;
        for (var i = 0; i < maxRetries; i++)
        {
            try
            {
                var port = GetAvailablePort();
                _serverUrl = $"http://127.0.0.1:{port}/";
                _webSocketUrl = _serverUrl.Replace("http://", "ws://");
                _server = new ReactiveWebSocketServer(_serverUrl);
                await _server.StartAsync();
                _output.WriteLine($"Server started on {_serverUrl}");
                return;
            }
            catch (Exception ex) when (ex is HttpListenerException or SocketException && i < maxRetries - 1)
            {
                _output.WriteLine($"Failed to start server on attempt {i + 1}: {ex.Message}. Retrying...");
                await Task.Delay(200);
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await (_server?.DisposeAsync() ?? ValueTask.CompletedTask);
    }

    #region Helper Methods

    private static int GetAvailablePort()
    {
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        return ((IPEndPoint)socket.LocalEndPoint!).Port;
    }

    private async Task<ClientWebSocket> ConnectClientAsync(CancellationToken ct = default)
    {
        var client = new ClientWebSocket();
        await client.ConnectAsync(new Uri(_webSocketUrl), ct);
        return client;
    }

    private static async Task<string> ReceiveTextAsync(ClientWebSocket client, CancellationToken ct = default)
    {
        var buffer = new byte[1024 * 64];
        var result = await client.ReceiveAsync(buffer, ct);
        return Encoding.UTF8.GetString(buffer, 0, result.Count);
    }

    private static async Task SendTextAsync(ClientWebSocket client, string message, CancellationToken ct = default)
    {
        var bytes = Encoding.UTF8.GetBytes(message);
        await client.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, ct);
    }

    private async Task<T> WaitForEventAsync<T>(
        Observable<T> observable,
        Func<T, bool>? predicate = null,
        int? timeoutMs = null)
    {
        var timeout = timeoutMs ?? DefaultTimeoutMs;
        var tcs = new TaskCompletionSource<T>();
        using var cts = new CancellationTokenSource(timeout);
        using var registration = cts.Token.Register(() =>
        {
            var msg = $"Event {typeof(T).Name} not received within {timeout}ms";
            _output.WriteLine($"[TIMEOUT] {msg}");
            tcs.TrySetException(new TimeoutException(msg));
        });

        using var subscription = observable.Subscribe(value =>
        {
            if (predicate == null || predicate(value))
            {
                tcs.TrySetResult(value);
            }
        });

        return await tcs.Task;
    }

    private async Task WaitForConditionAsync(
        Func<bool> condition,
        TimeSpan? timeout = null,
        string? errorMessage = null)
    {
        timeout ??= TimeSpan.FromMilliseconds(DefaultTimeoutMs);
        var endTime = DateTime.UtcNow.Add(timeout.Value);

        while (!condition() && DateTime.UtcNow < endTime)
        {
            await Task.Delay(10);
        }

        if (!condition())
        {
            var msg = errorMessage ?? $"Condition was not met within {timeout.Value.TotalSeconds}s";
            _output.WriteLine($"[TIMEOUT] {msg}");
            throw new TimeoutException(msg);
        }
    }

    #endregion

    #region Connection Tests

    [Fact(Timeout = DefaultTimeoutMs)]
    public async Task Should_Accept_Client_Connection()
    {
        // Arrange & Act
        _output.WriteLine("Waiting for client connection event...");
        var connectedTask = WaitForEventAsync(_server.ClientConnected);
        using var client = await ConnectClientAsync();
        var connected = await connectedTask;

        // Assert
        _output.WriteLine($"Client connected: {connected.Metadata.Id}");
        Assert.NotNull(connected);
        Assert.NotNull(connected.Metadata);
        Assert.Equal(ConnectReason.Initial, connected.Event.Reason);
        Assert.Equal(1, _server.ClientCount);
    }

    [Fact(Timeout = DefaultTimeoutMs)]
    public async Task Should_Handle_Multiple_Clients()
    {
        // Arrange
        var connectedClients = new List<ClientConnected>();
        using var subscription = _server.ClientConnected.Subscribe(connectedClients.Add);

        // Act
        _output.WriteLine("Connecting 3 clients...");
        using var client1 = await ConnectClientAsync();
        using var client2 = await ConnectClientAsync();
        using var client3 = await ConnectClientAsync();

        await WaitForConditionAsync(
            () => _server.ClientCount == 3,
            errorMessage: $"Expected 3 clients to be connected, but found {_server.ClientCount}");

        // Assert
        _output.WriteLine("All 3 clients connected successfully.");
        Assert.Equal(3, _server.ClientCount);
        Assert.Equal(3, connectedClients.Count);
        Assert.Equal(3, _server.ConnectedClients.Count);
    }

    [Fact(Timeout = DefaultTimeoutMs)]
    public async Task Should_Detect_Client_Disconnect()
    {
        // Arrange
        var disconnectedTask = WaitForEventAsync(_server.ClientDisconnected);
        var client = await ConnectClientAsync();

        // Act
        await client.CloseAsync(WebSocketCloseStatus.NormalClosure, "Test", CancellationToken.None);
        client.Dispose();

        // Assert
        var disconnected = await disconnectedTask;
        Assert.NotNull(disconnected);
        Assert.Equal(DisconnectReason.ClientInitiated, disconnected.Event.Reason);

        await WaitForConditionAsync(
            () => _server.ClientCount == 0,
            errorMessage: "Client count should be 0 after disconnect");
    }

    [Fact(Timeout = DefaultTimeoutMs)]
    public async Task Should_Reject_Non_WebSocket_Requests()
    {
        // Arrange & Act
        using var httpClient = new HttpClient();
        var response = await httpClient.GetAsync(_serverUrl);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact(Timeout = DefaultTimeoutMs)]
    public async Task Should_Handle_Unexpected_Client_Disconnect()
    {
        // Arrange
        var disconnectedTask = WaitForEventAsync(_server.ClientDisconnected);
        var client = await ConnectClientAsync();

        // Act
        client.Dispose();

        // Assert
        var disconnected = await disconnectedTask;
        Assert.NotNull(disconnected);

        await WaitForConditionAsync(
            () => _server.ClientCount == 0,
            errorMessage: "Client count should be 0 after unexpected disconnect");
    }

    [Fact(Timeout = DefaultTimeoutMs)]
    public async Task Should_Track_Connected_Clients()
    {
        // Arrange & Act
        using var client1 = await ConnectClientAsync();
        using var client2 = await ConnectClientAsync();

        await WaitForConditionAsync(() => _server.ClientCount == 2);

        // Assert
        var connectedClients = _server.ConnectedClients;
        Assert.Equal(2, connectedClients.Count);
        Assert.All(connectedClients.Values, metadata => Assert.NotNull(metadata));
    }

    #endregion

    #region Message Tests

    [Fact(Timeout = DefaultTimeoutMs)]
    public async Task Should_Receive_Text_Message_From_Client()
    {
        // Arrange
        using var client = await ConnectClientAsync();
        var messageTask = WaitForEventAsync(_server.Messages);

        // Act
        await SendTextAsync(client, "Hello Server");

        // Assert
        var receivedMessage = await messageTask;
        Assert.NotNull(receivedMessage);
        Assert.Equal("Hello Server", receivedMessage.Message.Text);
        Assert.NotNull(receivedMessage.Metadata);
    }

    [Fact(Timeout = DefaultTimeoutMs)]
    public async Task Should_Receive_Binary_Message_From_Client()
    {
        // Arrange
        using var client = await ConnectClientAsync();
        var messageTask = WaitForEventAsync(_server.Messages);
        var binaryData = new byte[] { 1, 2, 3, 4, 5 };

        // Act
        await client.SendAsync(
            new ArraySegment<byte>(binaryData),
            WebSocketMessageType.Binary,
            true,
            CancellationToken.None);

        // Assert
        var receivedMessage = await messageTask;
        Assert.NotNull(receivedMessage);
        Assert.NotNull(receivedMessage.Message.Binary);
        Assert.Equal(binaryData, receivedMessage.Message.Binary);
    }

    [Fact(Timeout = DefaultTimeoutMs)]
    public async Task Should_Receive_Multiple_Messages_From_Same_Client()
    {
        // Arrange
        var messages = new List<ServerReceivedMessage>();
        using var subscription = _server.Messages.Subscribe(messages.Add);
        using var client = await ConnectClientAsync();

        // Act
        await SendTextAsync(client, "Message 1");
        await SendTextAsync(client, "Message 2");
        await SendTextAsync(client, "Message 3");

        await WaitForConditionAsync(
            () => messages.Count == 3,
            errorMessage: "Expected 3 messages to be received");

        // Assert
        Assert.Equal(3, messages.Count);
        Assert.Equal("Message 1", messages[0].Message.Text);
        Assert.Equal("Message 2", messages[1].Message.Text);
        Assert.Equal("Message 3", messages[2].Message.Text);
    }

    [Fact(Timeout = DefaultTimeoutMs + 5000)]
    public async Task Should_Handle_Large_Messages()
    {
        // Arrange
        using var client = await ConnectClientAsync();
        var messageTask = WaitForEventAsync(_server.Messages, timeoutMs: 10000);
        var largeMessage = new string('A', 1024 * 64);

        // Act
        await SendTextAsync(client, largeMessage);

        // Assert
        var received = await messageTask;
        Assert.NotNull(received.Message.Text);
        Assert.Equal(1024 * 64, received.Message.Text.Length);
        Assert.Equal(largeMessage, received.Message.Text);
    }

    [Fact(Timeout = DefaultTimeoutMs)]
    public async Task Should_Handle_Empty_Messages()
    {
        // Arrange
        using var client = await ConnectClientAsync();
        var messageTask = WaitForEventAsync(_server.Messages);

        // Act
        await SendTextAsync(client, "");

        // Assert
        var received = await messageTask;
        Assert.NotNull(received);
        Assert.Equal(string.Empty, received.Message.Text);
    }

    [Fact(Timeout = DefaultTimeoutMs)]
    public async Task Should_Maintain_Message_Order()
    {
        // Arrange
        var messages = new List<ServerReceivedMessage>();
        using var subscription = _server.Messages.Subscribe(messages.Add);
        using var client = await ConnectClientAsync();

        // Act
        for (var i = 0; i < 20; i++)
        {
            await SendTextAsync(client, $"Order {i}");
        }

        await WaitForConditionAsync(
            () => messages.Count == 20,
            errorMessage: "Expected 20 messages in order");

        // Assert
        for (var i = 0; i < 20; i++)
        {
            Assert.Equal($"Order {i}", messages[i].Message.Text);
        }
    }

    #endregion

    #region Send Tests

    [Fact(Timeout = DefaultTimeoutMs)]
    public async Task Should_Send_Text_To_Specific_Client()
    {
        // Arrange
        var connectedTask = WaitForEventAsync(_server.ClientConnected);
        using var client = await ConnectClientAsync();
        var connected = await connectedTask;
        var clientId = connected.Metadata.Id;

        // Act
        var sent = await _server.SendAsTextAsync(clientId, "Hello Client");

        // Assert
        Assert.True(sent);
        var received = await ReceiveTextAsync(client);
        Assert.Equal("Hello Client", received);
    }

    [Fact(Timeout = DefaultTimeoutMs)]
    public async Task Should_Send_Binary_To_Specific_Client()
    {
        // Arrange
        var connectedTask = WaitForEventAsync(_server.ClientConnected);
        using var client = await ConnectClientAsync();
        var connected = await connectedTask;
        var clientId = connected.Metadata.Id;
        var binaryData = new byte[] { 10, 20, 30, 40, 50 };

        // Act
        var sent = await _server.SendAsBinaryAsync(clientId, binaryData);

        // Assert
        Assert.True(sent);
        var buffer = new byte[1024];
        var result = await client.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
        Assert.Equal(binaryData, buffer.Take(result.Count).ToArray());
    }

    [Fact(Timeout = DefaultTimeoutMs)]
    public async Task Should_Return_False_When_Sending_To_Non_Existent_Client()
    {
        // Arrange
        var nonExistentId = Guid.NewGuid();

        // Act
        var sent = await _server.SendAsTextAsync(nonExistentId, "Test");

        // Assert
        Assert.False(sent);
    }

    [Fact(Timeout = DefaultTimeoutMs)]
    public async Task Should_Send_Multiple_Messages_To_Same_Client()
    {
        // Arrange
        var connectedTask = WaitForEventAsync(_server.ClientConnected);
        using var client = await ConnectClientAsync();
        var connected = await connectedTask;
        var clientId = connected.Metadata.Id;

        // Act
        await _server.SendAsTextAsync(clientId, "Message 1");
        await _server.SendAsTextAsync(clientId, "Message 2");
        await _server.SendAsTextAsync(clientId, "Message 3");

        // Assert
        var msg1 = await ReceiveTextAsync(client);
        var msg2 = await ReceiveTextAsync(client);
        var msg3 = await ReceiveTextAsync(client);

        Assert.Equal("Message 1", msg1);
        Assert.Equal("Message 2", msg2);
        Assert.Equal("Message 3", msg3);
    }

    [Fact(Timeout = DefaultTimeoutMs)]
    public async Task TrySendAsText_Should_Return_True_For_Existing_Client()
    {
        // Arrange
        var connectedTask = WaitForEventAsync(_server.ClientConnected);
        using var client = await ConnectClientAsync();
        var connected = await connectedTask;
        var clientId = connected.Metadata.Id;

        // Act
        var result = _server.TrySendAsText(clientId, "Test");

        // Assert
        Assert.True(result);
        var received = await ReceiveTextAsync(client);
        Assert.Equal("Test", received);
    }

    [Fact]
    public void TrySendAsText_Should_Return_False_For_Non_Existent_Client()
    {
        // Arrange
        var nonExistentId = Guid.NewGuid();

        // Act
        var result = _server.TrySendAsText(nonExistentId, "Test");

        // Assert
        Assert.False(result);
    }

    [Fact(Timeout = DefaultTimeoutMs)]
    public async Task TrySendAsBinary_Should_Return_True_For_Existing_Client()
    {
        // Arrange
        var connectedTask = WaitForEventAsync(_server.ClientConnected);
        using var client = await ConnectClientAsync();
        var connected = await connectedTask;
        var clientId = connected.Metadata.Id;
        var data = new byte[] { 1, 2, 3 };

        // Act
        var result = _server.TrySendAsBinary(clientId, data);

        // Assert
        Assert.True(result);
        var buffer = new byte[1024];
        var receiveResult = await client.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
        Assert.Equal(data, buffer.Take(receiveResult.Count).ToArray());
    }

    [Fact(Timeout = DefaultTimeoutMs)]
    public async Task SendInstantAsync_Should_Send_Message_Immediately()
    {
        // Arrange
        var connectedTask = WaitForEventAsync(_server.ClientConnected);
        using var client = await ConnectClientAsync();
        var connected = await connectedTask;
        var clientId = connected.Metadata.Id;

        // Act
        var sent = await _server.SendInstantAsync(clientId, "Instant");

        // Assert
        Assert.True(sent);
        var received = await ReceiveTextAsync(client);
        Assert.Equal("Instant", received);
    }

    #endregion

    #region Integration Tests

    [Fact(Timeout = DefaultTimeoutMs)]
    public async Task EchoServer_Should_Reply_To_All_Messages()
    {
        // Arrange
        using var messageSubscription = _server.Messages.Subscribe(msg =>
        {
            _server.TrySendAsText(msg.Metadata.Id, $"Echo: {msg.Message.Text}");
        });

        using var client = await ConnectClientAsync();

        // Act & Assert
        await SendTextAsync(client, "Test 1");
        var echo1 = await ReceiveTextAsync(client);
        Assert.Equal("Echo: Test 1", echo1);

        await SendTextAsync(client, "Test 2");
        var echo2 = await ReceiveTextAsync(client);
        Assert.Equal("Echo: Test 2", echo2);
    }

    [Fact(Timeout = DefaultTimeoutMs)]
    public async Task ChatRoom_Should_Broadcast_Messages_To_Other_Clients()
    {
        // Arrange
        using var messageSubscription = _server.Messages.Subscribe(msg =>
        {
            var otherClients = _server.ConnectedClients.Keys.Where(id => id != msg.Metadata.Id);
            foreach (var clientId in otherClients)
            {
                _server.TrySendAsText(clientId, $"[{msg.Metadata.Id}]: {msg.Message.Text}");
            }
        });

        using var client1 = await ConnectClientAsync();
        using var client2 = await ConnectClientAsync();
        using var client3 = await ConnectClientAsync();

        await WaitForConditionAsync(() => _server.ClientCount == 3);

        // Act
        await SendTextAsync(client1, "Hello everyone!");

        // Assert
        var received2 = await ReceiveTextAsync(client2);
        var received3 = await ReceiveTextAsync(client3);

        Assert.Contains("Hello everyone!", received2);
        Assert.Contains("Hello everyone!", received3);
    }

    [Fact(Timeout = 30000)]
    public async Task Should_Handle_Concurrent_Messages_From_Multiple_Clients()
    {
        // Arrange
        const int clientCount = 5;
        const int messagesPerClient = 20;
        const int expectedMessageCount = clientCount * messagesPerClient;

        var messages = new List<ServerReceivedMessage>();
        using var subscription = _server.Messages.Subscribe(m => messages.Add(m));

        var clients = new List<ClientWebSocket>();
        for (var i = 0; i < clientCount; i++)
        {
            clients.Add(await ConnectClientAsync());
        }

        await WaitForConditionAsync(() => _server.ClientCount == clientCount);

        // Act
        var sendTasks = new List<Task>();
        for (var i = 0; i < clientCount; i++)
        {
            var client = clients[i];
            var clientId = i;
            sendTasks.Add(Task.Run(async () =>
            {
                for (var j = 0; j < messagesPerClient; j++)
                {
                    await SendTextAsync(client, $"Client {clientId} Message {j}");
                }
            }));
        }

        await Task.WhenAll(sendTasks);

        await WaitForConditionAsync(
            () => messages.Count == expectedMessageCount,
            errorMessage:
            $"Expected {expectedMessageCount} messages from concurrent clients, but got {messages.Count}");

        // Assert
        Assert.Equal(expectedMessageCount, messages.Count);
        var messageList = messages.ToList();
        for (var i = 0; i < clientCount; i++)
        {
            for (var j = 0; j < messagesPerClient; j++)
            {
                var text = $"Client {i} Message {j}";
                Assert.Contains(messageList, m => m.Message.Text == text);
            }
        }

        foreach (var client in clients)
        {
            client.Dispose();
        }
    }

    #region Broadcast Tests

    [Fact(Timeout = DefaultTimeoutMs)]
    public async Task BroadcastInstantAsync_WithMultipleClients_ShouldSendToAll()
    {
        // Arrange
        using var client1 = await ConnectClientAsync();
        using var client2 = await ConnectClientAsync();
        using var client3 = await ConnectClientAsync();

        await WaitForConditionAsync(() => _server.ClientCount == 3);

        const string message = "Test broadcast message";

        // Act
        var result = await _server.BroadcastInstantAsync(message, CancellationToken.None);

        // Assert
        Assert.True(result);

        var received1 = await ReceiveTextAsync(client1);
        var received2 = await ReceiveTextAsync(client2);
        var received3 = await ReceiveTextAsync(client3);

        Assert.Equal(message, received1);
        Assert.Equal(message, received2);
        Assert.Equal(message, received3);
    }

    [Fact(Timeout = DefaultTimeoutMs)]
    public async Task BroadcastInstantAsync_WithNoClients_ShouldReturnTrue()
    {
        // Arrange
        Assert.Equal(0, _server.ClientCount);
        const string message = "Test message";

        // Act
        var result = await _server.BroadcastInstantAsync(message, CancellationToken.None);

        // Assert
        Assert.True(result);
    }

    [Fact(Timeout = DefaultTimeoutMs)]
    public async Task BroadcastInstantAsync_WithSingleClient_ShouldSendCorrectly()
    {
        // Arrange
        using var client = await ConnectClientAsync();
        await WaitForConditionAsync(() => _server.ClientCount == 1);

        const string message = "Solo message";

        // Act
        var result = await _server.BroadcastInstantAsync(message, CancellationToken.None);

        // Assert
        Assert.True(result);

        var received = await ReceiveTextAsync(client);
        Assert.Equal(message, received);
    }

    [Fact(Timeout = DefaultTimeoutMs)]
    public async Task BroadcastInstantAsync_ShouldRunInParallel()
    {
        // Arrange
        using var client1 = await ConnectClientAsync();
        using var client2 = await ConnectClientAsync();
        using var client3 = await ConnectClientAsync();

        await WaitForConditionAsync(() => _server.ClientCount == 3);

        const string message = "Parallel test";
        var startTime = DateTime.UtcNow;

        // Act
        var result = await _server.BroadcastInstantAsync(message, CancellationToken.None);

        // Assert
        var elapsed = DateTime.UtcNow - startTime;

        Assert.True(result);

        Assert.True(elapsed < TimeSpan.FromSeconds(1),
            $"Broadcast took {elapsed.TotalMilliseconds}ms - should be fast with parallel execution");

        var received1 = await ReceiveTextAsync(client1);
        var received2 = await ReceiveTextAsync(client2);
        var received3 = await ReceiveTextAsync(client3);

        Assert.Equal(message, received1);
        Assert.Equal(message, received2);
        Assert.Equal(message, received3);
    }

    [Fact(Timeout = DefaultTimeoutMs)]
    public async Task BroadcastInstantAsync_ByteArray_ShouldSendToAll()
    {
        // Arrange
        using var client1 = await ConnectClientAsync();
        using var client2 = await ConnectClientAsync();

        await WaitForConditionAsync(() => _server.ClientCount == 2);

        var message = new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05 };

        // Act
        var result = await _server.BroadcastInstantAsync(message, CancellationToken.None);

        // Assert
        Assert.True(result);

        var buffer1 = new byte[1024];
        var buffer2 = new byte[1024];

        var result1 = await client1.ReceiveAsync(new ArraySegment<byte>(buffer1), CancellationToken.None);
        var result2 = await client2.ReceiveAsync(new ArraySegment<byte>(buffer2), CancellationToken.None);

        var received1 = buffer1.Take(result1.Count).ToArray();
        var received2 = buffer2.Take(result2.Count).ToArray();

        Assert.Equal(message, received1);
        Assert.Equal(message, received2);
    }

    [Fact(Timeout = DefaultTimeoutMs)]
    public async Task BroadcastInstantAsync_AfterClientDisconnects_ShouldContinueWithOthers()
    {
        // Arrange
        using var client1 = await ConnectClientAsync();
        using var client2 = await ConnectClientAsync();
        using var client3 = await ConnectClientAsync();

        await WaitForConditionAsync(() => _server.ClientCount == 3);

        await client2.CloseAsync(WebSocketCloseStatus.NormalClosure, "Disconnecting", CancellationToken.None);

        const string message = "Message after disconnect";

        // Act
        _ = await _server.BroadcastInstantAsync(message, CancellationToken.None);

        // Assert
        var received1 = await ReceiveTextAsync(client1);
        var received3 = await ReceiveTextAsync(client3);

        Assert.Equal(message, received1);
        Assert.Equal(message, received3);
    }

    [Fact(Timeout = DefaultTimeoutMs)]
    public async Task BroadcastInstantAsync_WithSimultaneousBroadcasts_ShouldHandleCorrectly()
    {
        // Arrange
        using var client1 = await ConnectClientAsync();
        using var client2 = await ConnectClientAsync();

        await WaitForConditionAsync(() => _server.ClientCount == 2);

        const string message1 = "Broadcast 1";
        const string message2 = "Broadcast 2";
        const string message3 = "Broadcast 3";

        // Act
        var task1 = _server.BroadcastInstantAsync(message1, CancellationToken.None);
        var task2 = _server.BroadcastInstantAsync(message2, CancellationToken.None);
        var task3 = _server.BroadcastInstantAsync(message3, CancellationToken.None);

        var results = await Task.WhenAll(task1, task2, task3);

        // Assert
        Assert.All(results, Assert.True);

        var messages1 = new List<string>();
        var messages2 = new List<string>();

        for (var i = 0; i < 3; i++)
        {
            messages1.Add(await ReceiveTextAsync(client1));
            messages2.Add(await ReceiveTextAsync(client2));
        }

        Assert.Equal(3, messages1.Count);
        Assert.Equal(3, messages2.Count);

        Assert.Contains(message1, messages1);
        Assert.Contains(message2, messages1);
        Assert.Contains(message3, messages1);

        Assert.Contains(message1, messages2);
        Assert.Contains(message2, messages2);
        Assert.Contains(message3, messages2);
    }

    [Fact(Timeout = DefaultTimeoutMs + 5000)]
    public async Task BroadcastInstantAsync_WithManySimultaneousBroadcasts_ShouldNotDeadlock()
    {
        // Arrange
        var clients = new List<ClientWebSocket>();
        for (var i = 0; i < 5; i++)
        {
            clients.Add(await ConnectClientAsync());
        }

        await WaitForConditionAsync(() => _server.ClientCount == 5);

        try
        {
            // Act
            var broadcastTasks = Enumerable.Range(0, 20)
                .Select(i => _server.BroadcastInstantAsync($"Message {i}", CancellationToken.None))
                .ToList();

            var timeout = Task.Delay(TimeSpan.FromSeconds(10));
            var allTasks = Task.WhenAll(broadcastTasks);

            var completedTask = await Task.WhenAny(allTasks, timeout);

            // Assert
            Assert.Same(allTasks, completedTask);
            var results = await allTasks;
            Assert.All(results, Assert.True);
        }
        finally
        {
            foreach (var client in clients)
            {
                client.Dispose();
            }
        }
    }

    [Fact(Timeout = DefaultTimeoutMs)]
    public async Task BroadcastAsBinaryAsync_WithMultipleClients_ShouldSendToAll()
    {
        // Arrange
        using var client1 = await ConnectClientAsync();
        using var client2 = await ConnectClientAsync();

        await WaitForConditionAsync(() => _server.ClientCount == 2);

        var message = new byte[] { 0x01, 0x02, 0x03 };

        // Act
        var result = await _server.BroadcastAsBinaryAsync(message, CancellationToken.None);

        // Assert
        Assert.True(result);

        var buffer1 = new byte[1024];
        var buffer2 = new byte[1024];

        var result1 = await client1.ReceiveAsync(new ArraySegment<byte>(buffer1), CancellationToken.None);
        var result2 = await client2.ReceiveAsync(new ArraySegment<byte>(buffer2), CancellationToken.None);

        Assert.Equal(WebSocketMessageType.Binary, result1.MessageType);
        Assert.Equal(WebSocketMessageType.Binary, result2.MessageType);

        var received1 = buffer1.Take(result1.Count).ToArray();
        var received2 = buffer2.Take(result2.Count).ToArray();

        Assert.Equal(message, received1);
        Assert.Equal(message, received2);
    }

    [Fact(Timeout = DefaultTimeoutMs)]
    public async Task BroadcastAsBinaryAsync_String_ShouldSendToAll()
    {
        // Arrange
        using var client1 = await ConnectClientAsync();
        using var client2 = await ConnectClientAsync();

        await WaitForConditionAsync(() => _server.ClientCount == 2);

        const string message = "Binary message as string";

        // Act
        var result = await _server.BroadcastAsBinaryAsync(message, CancellationToken.None);

        // Assert
        Assert.True(result);

        var buffer1 = new byte[1024];
        var buffer2 = new byte[1024];

        var result1 = await client1.ReceiveAsync(new ArraySegment<byte>(buffer1), CancellationToken.None);
        var result2 = await client2.ReceiveAsync(new ArraySegment<byte>(buffer2), CancellationToken.None);

        Assert.Equal(WebSocketMessageType.Binary, result1.MessageType);
        Assert.Equal(WebSocketMessageType.Binary, result2.MessageType);
    }

    [Fact(Timeout = DefaultTimeoutMs)]
    public async Task BroadcastAsTextAsync_WithMultipleClients_ShouldSendToAll()
    {
        // Arrange
        using var client1 = await ConnectClientAsync();
        using var client2 = await ConnectClientAsync();

        await WaitForConditionAsync(() => _server.ClientCount == 2);

        const string message = "Text message";

        // Act
        var result = await _server.BroadcastAsTextAsync(message, CancellationToken.None);

        // Assert
        Assert.True(result);

        var received1 = await ReceiveTextAsync(client1);
        var received2 = await ReceiveTextAsync(client2);

        Assert.Equal(message, received1);
        Assert.Equal(message, received2);
    }

    [Fact(Timeout = DefaultTimeoutMs)]
    public async Task BroadcastAsTextAsync_ByteArray_ShouldSendToAll()
    {
        // Arrange
        using var client1 = await ConnectClientAsync();
        using var client2 = await ConnectClientAsync();

        await WaitForConditionAsync(() => _server.ClientCount == 2);

        var messageBytes = "Text from bytes"u8.ToArray();

        // Act
        var result = await _server.BroadcastAsTextAsync(messageBytes, CancellationToken.None);

        // Assert
        Assert.True(result);

        var received1 = await ReceiveTextAsync(client1);
        var received2 = await ReceiveTextAsync(client2);

        Assert.Equal("Text from bytes", received1);
        Assert.Equal("Text from bytes", received2);
    }

    [Fact(Timeout = DefaultTimeoutMs)]
    public async Task TryBroadcastAsBinary_WithMultipleClients_ShouldSendToAll()
    {
        // Arrange
        using var client1 = await ConnectClientAsync();
        using var client2 = await ConnectClientAsync();

        await WaitForConditionAsync(() => _server.ClientCount == 2);

        var message = new byte[] { 0x01, 0x02, 0x03 };

        // Act
        var result = _server.TryBroadcastAsBinary(message);

        // Assert
        Assert.True(result);

        var buffer1 = new byte[1024];
        var buffer2 = new byte[1024];

        var result1 = await client1.ReceiveAsync(new ArraySegment<byte>(buffer1), CancellationToken.None);
        var result2 = await client2.ReceiveAsync(new ArraySegment<byte>(buffer2), CancellationToken.None);

        var received1 = buffer1.Take(result1.Count).ToArray();
        var received2 = buffer2.Take(result2.Count).ToArray();

        Assert.Equal(message, received1);
        Assert.Equal(message, received2);
    }

    [Fact(Timeout = DefaultTimeoutMs)]
    public async Task TryBroadcastAsText_WithMultipleClients_ShouldSendToAll()
    {
        // Arrange
        using var client1 = await ConnectClientAsync();
        using var client2 = await ConnectClientAsync();

        await WaitForConditionAsync(() => _server.ClientCount == 2);

        const string message = "Text message";

        // Act
        var result = _server.TryBroadcastAsText(message);

        // Assert
        Assert.True(result);

        var received1 = await ReceiveTextAsync(client1);
        var received2 = await ReceiveTextAsync(client2);

        Assert.Equal(message, received1);
        Assert.Equal(message, received2);
    }

    [Fact]
    public void TryBroadcastAsText_WithNoClients_ShouldReturnTrue()
    {
        // Arrange
        Assert.Equal(0, _server.ClientCount);
        const string message = "Text message";

        // Act
        var result = _server.TryBroadcastAsText(message);

        // Assert
        Assert.True(result);
    }

    [Fact(Timeout = DefaultTimeoutMs + 5000)]
    public async Task BroadcastInstantAsync_WithLargeMessage_ShouldHandleCorrectly()
    {
        // Arrange
        using var client = await ConnectClientAsync();
        await WaitForConditionAsync(() => _server.ClientCount == 1);

        var message = new string('X', 64 * 1024); // 64KB message

        // Act
        var result = await _server.BroadcastInstantAsync(message, CancellationToken.None);

        // Assert
        Assert.True(result);

        var buffer = new byte[128 * 1024];

        var receiveResult = await client.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);

        var received = Encoding.UTF8.GetString(buffer, 0, receiveResult.Count);
        Assert.Equal(message.Length, received.Length);
    }

    [Fact(Timeout = DefaultTimeoutMs)]
    public async Task BroadcastInstantAsync_WithSpecialCharacters_ShouldSendCorrectly()
    {
        // Arrange
        using var client = await ConnectClientAsync();
        await WaitForConditionAsync(() => _server.ClientCount == 1);

        const string message = "Special: \n\r\t \"quotes\" 'apostrophes' 日本語 emoji: 🚀";

        // Act
        var result = await _server.BroadcastInstantAsync(message, CancellationToken.None);

        // Assert
        Assert.True(result);

        var received = await ReceiveTextAsync(client);
        Assert.Equal(message, received);
    }

    [Fact(Timeout = DefaultTimeoutMs + 5000)]
    public async Task BroadcastInstantAsync_ToManyClients_ShouldComplete()
    {
        // Arrange
        var clients = new List<ClientWebSocket>();
        const int clientCount = 20;

        try
        {
            for (var i = 0; i < clientCount; i++)
            {
                clients.Add(await ConnectClientAsync());
            }

            await WaitForConditionAsync(() => _server.ClientCount == clientCount);

            const string message = "Stress test message";

            // Act
            var startTime = DateTime.UtcNow;
            var result = await _server.BroadcastInstantAsync(message, CancellationToken.None);
            var elapsed = DateTime.UtcNow - startTime;

            // Assert
            Assert.True(result);

            Assert.True(elapsed < TimeSpan.FromSeconds(5),
                $"Broadcast to {clientCount} clients took {elapsed.TotalSeconds}s");

            // Verify some clients received the message
            var received1 = await ReceiveTextAsync(clients[0]);
            var received2 = await ReceiveTextAsync(clients[clientCount / 2]);
            var received3 = await ReceiveTextAsync(clients[clientCount - 1]);

            Assert.Equal(message, received1);
            Assert.Equal(message, received2);
            Assert.Equal(message, received3);
        }
        finally
        {
            foreach (var client in clients)
            {
                client.Dispose();
            }
        }
    }

    [Fact(Timeout = DefaultTimeoutMs)]
    public async Task BroadcastInstantAsync_MixedClientStates_ShouldHandleGracefully()
    {
        // Arrange
        using var client1 = await ConnectClientAsync();
        using var client2 = await ConnectClientAsync();
        using var client3 = await ConnectClientAsync();

        await WaitForConditionAsync(() => _server.ClientCount == 3);

        // Disconnect one client mid-test
        await client2.CloseAsync(WebSocketCloseStatus.NormalClosure, "Bye", CancellationToken.None);
        await Task.Delay(100);

        const string message = "Message to mixed clients";

        // Act
        var result = await _server.BroadcastInstantAsync(message, CancellationToken.None);

        // Assert
        // The two connected clients should receive the message
        var received1 = await ReceiveTextAsync(client1);
        var received3 = await ReceiveTextAsync(client3);

        Assert.Equal(message, received1);
        Assert.Equal(message, received3);
    }

    [Fact(Timeout = LongTimeoutMs)]
    public async Task BroadcastInstantAsync_RepeatedCalls_ShouldNotLeakMemory()
    {
        // Arrange
        var clients = new List<ClientWebSocket>();
        for (var i = 0; i < 10; i++)
        {
            clients.Add(await ConnectClientAsync());
        }

        await WaitForConditionAsync(() => _server.ClientCount == 10);

        try
        {
            // Act
            for (var i = 0; i < 100; i++)
            {
                await _server.BroadcastInstantAsync($"Message {i}", CancellationToken.None);
            }

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            // Assert
            Assert.True(true);

            // Verify server is still responsive
            const string finalMessage = "Final message";
            var result = await _server.BroadcastInstantAsync(finalMessage, CancellationToken.None);
            Assert.True(result);
        }
        finally
        {
            foreach (var client in clients)
            {
                client.Dispose();
            }
        }
    }

    #endregion

    #endregion

    #region Performance & Stress Tests

    [Fact(Timeout = LongTimeoutMs)]
    public async Task Should_Handle_Rapid_Connect_Disconnect()
    {
        // Arrange & Act
        for (var i = 0; i < 10; i++)
        {
            using var client = await ConnectClientAsync();
            await client.CloseAsync(WebSocketCloseStatus.NormalClosure, "Test", CancellationToken.None);
        }

        // Assert
        await WaitForConditionAsync(
            () => _server.ClientCount == 0,
            TimeSpan.FromSeconds(15),
            "All clients should disconnect");
        Assert.True(true);
    }

    [Fact(Timeout = DefaultTimeoutMs + 5000)]
    public async Task Should_Handle_Many_Small_Messages()
    {
        // Arrange
        var messageCount = 0;
        using var subscription = _server.Messages.Subscribe(_ => Interlocked.Increment(ref messageCount));
        using var client = await ConnectClientAsync();

        // Act
        for (var i = 0; i < 100; i++)
        {
            await SendTextAsync(client, $"Message {i}");
        }

        await WaitForConditionAsync(
            () => messageCount == 100,
            TimeSpan.FromSeconds(10),
            "Expected 100 messages to be received");

        // Assert
        Assert.Equal(100, messageCount);
    }

    [Fact(Timeout = LongTimeoutMs)]
    public async Task Should_Handle_10_Concurrent_Clients()
    {
        // Arrange
        const int targetCount = 10;
        var clients = new List<ClientWebSocket>();

        try
        {
            // Act
            var connectTasks = Enumerable.Range(0, targetCount)
                .Select(_ => ConnectClientAsync());

            var connectedClients = await Task.WhenAll(connectTasks);
            clients.AddRange(connectedClients);

            await WaitForConditionAsync(
                () => _server.ClientCount == targetCount,
                TimeSpan.FromSeconds(15),
                $"Expected {targetCount} concurrent clients");

            // Assert
            Assert.Equal(targetCount, _server.ClientCount);
            Assert.Equal(targetCount, _server.ConnectedClients.Count);
        }
        finally
        {
            var closeTasks = clients.Select(c =>
                c.CloseAsync(WebSocketCloseStatus.NormalClosure, "Test done", CancellationToken.None));
            await Task.WhenAll(closeTasks);
            foreach (var c in clients)
            {
                c.Dispose();
            }
        }
    }

    #endregion

    #region Server Lifecycle Tests

    [Fact(Timeout = DefaultTimeoutMs)]
    public async Task Should_Start_And_Stop_Server()
    {
        // Arrange
        var port = GetAvailablePort();
        var url = $"http://127.0.0.1:{port}/";
        await using var server = new ReactiveWebSocketServer(url);

        // Act
        await server.StartAsync();
        var isRunning = server.IsRunning;
        var stopped = await server.StopAsync(WebSocketCloseStatus.NormalClosure, "Test");

        // Assert
        Assert.True(isRunning);
        Assert.True(stopped);
        Assert.False(server.IsRunning);
    }

    [Fact(Timeout = DefaultTimeoutMs)]
    public async Task Should_Handle_Dispose_Without_Deadlock()
    {
        // Arrange
        var port = GetAvailablePort();
        var url = $"http://127.0.0.1:{port}/";
        var server = new ReactiveWebSocketServer(url);
        await server.StartAsync();

        // Act
        await server.SendInstantAsync(Guid.Empty, "test");
        await server.StopAsync(WebSocketCloseStatus.NormalClosure, "test");
        server.Dispose();

        // Assert - no deadlock occurred
        Assert.True(true);
    }

    [Fact(Timeout = DefaultTimeoutMs)]
    public async Task Should_Complete_Observables_On_Dispose()
    {
        // Arrange
        var port = GetAvailablePort();
        var url = $"http://127.0.0.1:{port}/";
        var server = new ReactiveWebSocketServer(url);
        await server.StartAsync();

        var clientConnectedCompleted = false;
        var clientDisconnectedCompleted = false;
        var messagesCompleted = false;

        using var sub1 = server.ClientConnected.Subscribe(_ => { }, _ => { }, _ => clientConnectedCompleted = true);
        using var sub2 =
            server.ClientDisconnected.Subscribe(_ => { }, _ => { }, _ => clientDisconnectedCompleted = true);
        using var sub3 = server.Messages.Subscribe(_ => { }, _ => { }, _ => messagesCompleted = true);

        // Act
        await server.DisposeAsync();

        // Assert
        Assert.True(clientConnectedCompleted);
        Assert.True(clientDisconnectedCompleted);
        Assert.True(messagesCompleted);
    }

    [Fact(Timeout = DefaultTimeoutMs)]
    public async Task Should_Disconnect_All_Clients_On_Stop()
    {
        // Arrange
        using var client1 = await ConnectClientAsync();
        using var client2 = await ConnectClientAsync();
        await WaitForConditionAsync(() => _server.ClientCount == 2);

        // Act
        await _server.StopAsync(WebSocketCloseStatus.NormalClosure, "Server stopping");

        // Assert
        await WaitForConditionAsync(
            () => _server.ClientCount == 0,
            errorMessage: "All clients should be disconnected after server stop");

        Assert.Equal(0, _server.ClientCount);
    }

    #endregion

    #region Properties Tests

    [Fact]
    public void Should_Have_Default_Properties()
    {
        // Arrange & Act
        var port = GetAvailablePort();
        var url = $"http://127.0.0.1:{port}/";
        using var server = new ReactiveWebSocketServer(url);

        // Assert
        Assert.Equal(TimeSpan.FromSeconds(30), server.IdleConnection);
        Assert.Equal(TimeSpan.FromSeconds(10), server.ConnectTimeout);
        Assert.Equal(Encoding.UTF8, server.MessageEncoding);
        Assert.True(server.IsTextMessageConversionEnabled);
        Assert.Equal(0, server.ClientCount);
        Assert.Empty(server.ConnectedClients);
    }

    [Fact]
    public async Task Should_Update_ClientCount_Correctly()
    {
        // Arrange
        Assert.Equal(0, _server.ClientCount);

        // Act - Add clients
        using var client1 = await ConnectClientAsync();
        await WaitForConditionAsync(() => _server.ClientCount == 1);
        Assert.Equal(1, _server.ClientCount);

        using var client2 = await ConnectClientAsync();
        await WaitForConditionAsync(() => _server.ClientCount == 2);
        Assert.Equal(2, _server.ClientCount);

        // Act - Remove client
        await client1.CloseAsync(WebSocketCloseStatus.NormalClosure, "Done", CancellationToken.None);
        client1.Dispose();

        await WaitForConditionAsync(() => _server.ClientCount == 1);
        Assert.Equal(1, _server.ClientCount);
    }

    #endregion

    #region Error Handling Tests

    [Fact(Timeout = DefaultTimeoutMs)]
    public async Task Should_Handle_Client_Send_After_Disconnect()
    {
        // Arrange
        var connectedTask = WaitForEventAsync(_server.ClientConnected);
        using var client = await ConnectClientAsync();
        var connected = await connectedTask;
        var clientId = connected.Metadata.Id;

        // Act
        await client.CloseAsync(WebSocketCloseStatus.NormalClosure, "Done", CancellationToken.None);
        await WaitForConditionAsync(() => _server.ClientCount == 0);

        var sent = await _server.SendAsTextAsync(clientId, "Test");

        // Assert
        Assert.False(sent);
    }

    [Fact(Timeout = DefaultTimeoutMs)]
    public async Task Should_Not_Throw_On_Multiple_Dispose_Calls()
    {
        // Arrange
        var port = GetAvailablePort();
        var url = $"http://127.0.0.1:{port}/";
        var server = new ReactiveWebSocketServer(url);
        await server.StartAsync();

        // Act & Assert - should not throw
        await server.DisposeAsync();
        await server.DisposeAsync();
        server.Dispose();

        Assert.True(server.IsDisposed);
    }

    #endregion
}