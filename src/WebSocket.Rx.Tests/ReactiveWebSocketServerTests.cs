using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using R3;
using Xunit.Abstractions;

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

    public async Task InitializeAsync()
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

    public async Task DisposeAsync()
    {
        await (_server?.DisposeAsync() ?? ValueTask.CompletedTask);
    }

    private static int GetAvailablePort()
    {
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        return ((IPEndPoint)socket.LocalEndPoint!).Port;
    }

    #region Helper Methods

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
        for (int i = 0; i < clientCount; i++)
        {
            clients.Add(await ConnectClientAsync());
        }

        await WaitForConditionAsync(() => _server.ClientCount == clientCount);

        // Act
        var sendTasks = new List<Task>();
        for (int i = 0; i < clientCount; i++)
        {
            var client = clients[i];
            var clientId = i;
            sendTasks.Add(Task.Run(async () =>
            {
                for (int j = 0; j < messagesPerClient; j++)
                {
                    await SendTextAsync(client, $"Client {clientId} Message {j}");
                }
            }));
        }

        await Task.WhenAll(sendTasks);

        await WaitForConditionAsync(
            () => messages.Count == expectedMessageCount,
            errorMessage: $"Expected {expectedMessageCount} messages from concurrent clients, but got {messages.Count}");

        // Assert
        Assert.Equal(expectedMessageCount, messages.Count);
        var messageList = messages.ToList();
        for (int i = 0; i < clientCount; i++)
        {
            for (int j = 0; j < messagesPerClient; j++)
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