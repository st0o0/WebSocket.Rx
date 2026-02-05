using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using R3;

namespace WebSocket.Rx.Tests;

[CollectionDefinition("ReactiveWebSocketServerTests", DisableParallelization = true)]
public class CollectionDefinition;

[Collection("ReactiveWebSocketServerTests")]
public class ReactiveWebSocketServerTests : IAsyncLifetime
{
    private ReactiveWebSocketServer _server = null!;
    private string _serverUrl = string.Empty;
    private string _webSocketUrl;

    public async Task InitializeAsync()
    {
        var port = GetAvailablePort();
        _serverUrl = $"http://127.0.0.1:{port}/";
        _webSocketUrl = _serverUrl.Replace("http://", "ws://");

        _server = new ReactiveWebSocketServer(_serverUrl);
        await _server.StartAsync();
    }

    public async Task DisposeAsync()
    {
        _server.Dispose();
        await Task.CompletedTask;
    }

    private static int GetAvailablePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
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

    private static async Task<T> WaitAsync<T>(TaskCompletionSource<T> tcs, int timeoutMs = 5000)
    {
        using var cts = new CancellationTokenSource(timeoutMs);
        return await tcs.Task.WaitAsync(cts.Token);
    }

    private static async Task WaitForConditionAsync(Func<bool> condition, TimeSpan? timeout = null)
    {
        timeout ??= TimeSpan.FromSeconds(5);
        var endTime = DateTime.UtcNow.Add(timeout.Value);
        while (!condition() && DateTime.UtcNow < endTime)
        {
            await Task.Delay(10);
        }

        if (!condition())
        {
            throw new TimeoutException("Condition was not met within timeout");
        }
    }

    #endregion

    #region Connection Tests

    [Fact(Timeout = 10000)]
    public async Task Should_Accept_Client_Connection()
    {
        // Arrange
        var connectedTcs = new TaskCompletionSource<ClientConnected>();
        using var subscription = _server.ClientConnected.Subscribe(connectedTcs.SetResult);

        // Act
        using var client = await ConnectClientAsync();

        // Assert
        var connected = await WaitAsync(connectedTcs);
        Assert.NotNull(connected);
        Assert.NotNull(connected.Metadata);
        Assert.Equal(ConnectReason.Initial, connected.Event.Reason);
        Assert.Equal(1, _server.ClientCount);
    }

    [Fact(Timeout = 10000)]
    public async Task Should_Handle_Multiple_Clients()
    {
        // Arrange
        var connectedClients = new List<ClientConnected>();
        using var subscription = _server.ClientConnected.Subscribe(connectedClients.Add);

        // Act
        using var client1 = await ConnectClientAsync();
        using var client2 = await ConnectClientAsync();
        using var client3 = await ConnectClientAsync();

        await WaitForConditionAsync(() => _server.ClientCount == 3);

        // Assert
        Assert.Equal(3, _server.ClientCount);
        Assert.Equal(3, connectedClients.Count);
        Assert.Equal(3, _server.ConnectedClients.Count);
    }

    [Fact(Timeout = 10000000)]
    public async Task Should_Detect_Client_Disconnect()
    {
        // Arrange
        var disconnectedTcs = new TaskCompletionSource<ClientDisconnected>();
        using var subscription = _server.ClientDisconnected.Subscribe(disconnectedTcs.SetResult);

        var client = await ConnectClientAsync();

        // Act
        await client.CloseAsync(WebSocketCloseStatus.NormalClosure, "Test", CancellationToken.None);
        client.Dispose();

        // Assert
        var disconnected = await WaitAsync(disconnectedTcs);
        Assert.NotNull(disconnected);
        Assert.Equal(DisconnectReason.ClientInitiated, disconnected.Event.Reason);

        await WaitForConditionAsync(() => _server.ClientCount == 0);
        Assert.Equal(0, _server.ClientCount);
    }

    [Fact(Timeout = 10000)]
    public async Task Should_Reject_Non_WebSocket_Requests()
    {
        // Arrange & Act
        using var httpClient = new HttpClient();
        var response = await httpClient.GetAsync(_serverUrl);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact(Timeout = 10000)]
    public async Task Should_Handle_Unexpected_Client_Disconnect()
    {
        // Arrange
        var disconnectedTcs = new TaskCompletionSource<ClientDisconnected>();
        using var subscription = _server.ClientDisconnected.Subscribe(disconnectedTcs.SetResult);

        var client = await ConnectClientAsync();

        // Act
        client.Dispose();

        // Assert
        var disconnected = await WaitAsync(disconnectedTcs);
        Assert.NotNull(disconnected);

        await WaitForConditionAsync(() => _server.ClientCount == 0);
        Assert.Equal(0, _server.ClientCount);
    }

    #endregion

    #region Message Tests

    [Fact(Timeout = 10000)]
    public async Task Should_Receive_Text_Message_From_Client()
    {
        // Arrange
        var messageTcs = new TaskCompletionSource<ServerReceivedMessage>();
        using var subscription = _server.Messages.Subscribe(messageTcs.SetResult);

        using var client = await ConnectClientAsync();
        await Task.Delay(100);

        // Act
        await SendTextAsync(client, "Hello Server");

        // Assert
        var receivedMessage = await WaitAsync(messageTcs);
        Assert.NotNull(receivedMessage);
        Assert.Equal("Hello Server", receivedMessage.Message.Text);
        Assert.NotNull(receivedMessage.Metadata);
    }

    [Fact(Timeout = 10000)]
    public async Task Should_Receive_Binary_Message_From_Client()
    {
        // Arrange
        var messageTcs = new TaskCompletionSource<ServerReceivedMessage>();
        using var subscription = _server.Messages.Subscribe(messageTcs.SetResult);

        using var client = await ConnectClientAsync();
        await Task.Delay(100);

        var binaryData = new byte[] { 1, 2, 3, 4, 5 };

        // Act
        await client.SendAsync(new ArraySegment<byte>(binaryData), WebSocketMessageType.Binary, true,
            CancellationToken.None);

        // Assert
        var receivedMessage = await WaitAsync(messageTcs);
        Assert.NotNull(receivedMessage);
        Assert.NotNull(receivedMessage.Message.Binary);
        Assert.Equal(binaryData, receivedMessage.Message.Binary);
    }

    [Fact(Timeout = 10000)]
    public async Task Should_Receive_Multiple_Messages_From_Same_Client()
    {
        // Arrange
        var messages = new List<ServerReceivedMessage>();
        using var subscription = _server.Messages.Subscribe(messages.Add);

        using var client = await ConnectClientAsync();
        await Task.Delay(100);

        // Act
        await SendTextAsync(client, "Message 1");
        await SendTextAsync(client, "Message 2");
        await SendTextAsync(client, "Message 3");

        await WaitForConditionAsync(() => messages.Count == 3);

        // Assert
        Assert.Equal(3, messages.Count);
        Assert.Equal("Message 1", messages[0].Message.Text);
        Assert.Equal("Message 2", messages[1].Message.Text);
        Assert.Equal("Message 3", messages[2].Message.Text);
    }

    [Fact(Timeout = 10000)]
    public async Task Should_Handle_Large_Messages()
    {
        // Arrange
        var messageTcs = new TaskCompletionSource<ServerReceivedMessage>();
        using var subscription = _server.Messages.Subscribe(messageTcs.SetResult);

        using var client = await ConnectClientAsync();
        await Task.Delay(100);

        var largeMessage = new string('A', 1024 * 64);

        // Act
        await SendTextAsync(client, largeMessage);

        // Assert
        var received = await WaitAsync(messageTcs, 10000);
        Assert.NotNull(received.Message.Text);
        Assert.Equal(1024 * 64, received.Message.Text.Length);
    }

    #endregion

    #region Send Tests

    [Fact(Timeout = 10000)]
    public async Task Should_Send_Text_To_Specific_Client()
    {
        // Arrange
        var clientIdTcs = new TaskCompletionSource<Guid>();
        using var subscription = _server.ClientConnected.Subscribe(c => clientIdTcs.SetResult(c.Metadata.Id));

        using var client = await ConnectClientAsync();
        var clientId = await WaitAsync(clientIdTcs);

        // Act
        var sent = await _server.SendAsTextAsync(clientId, "Hello Client");

        // Assert
        Assert.True(sent);
        var received = await ReceiveTextAsync(client);
        Assert.Equal("Hello Client", received);
    }

    [Fact(Timeout = 10000)]
    public async Task Should_Send_Binary_To_Specific_Client()
    {
        // Arrange
        var clientIdTcs = new TaskCompletionSource<Guid>();
        using var subscription = _server.ClientConnected.Subscribe(c => clientIdTcs.SetResult(c.Metadata.Id));

        using var client = await ConnectClientAsync();
        var clientId = await WaitAsync(clientIdTcs);

        var binaryData = new byte[] { 10, 20, 30, 40, 50 };

        // Act
        var sent = await _server.SendAsBinaryAsync(clientId, binaryData);

        // Assert
        Assert.True(sent);
        var buffer = new byte[1024];
        var result = await client.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
        Assert.Equal(binaryData, buffer.Take(result.Count).ToArray());
    }

    [Fact(Timeout = 10000)]
    public async Task Should_Return_False_When_Sending_To_Non_Existent_Client()
    {
        // Arrange
        var nonExistentId = Guid.NewGuid();

        // Act
        var sent = await _server.SendAsTextAsync(nonExistentId, "Test");

        // Assert
        Assert.False(sent);
    }

    [Fact(Timeout = 10000)]
    public async Task Should_Send_Multiple_Messages_To_Same_Client()
    {
        // Arrange
        var clientIdTcs = new TaskCompletionSource<Guid>();
        using var subscription = _server.ClientConnected.Subscribe(c => clientIdTcs.SetResult(c.Metadata.Id));

        using var client = await ConnectClientAsync();
        var clientId = await WaitAsync(clientIdTcs);

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

    #endregion

    #region Broadcast Tests

    #endregion

    #region Lifecycle Tests

    [Fact(Timeout = 10000)]
    public async Task Should_Disconnect_All_Clients_On_Stop()
    {
        // Arrange
        var disconnectedClients = new List<ClientDisconnected>();
        using var t = _server.ClientDisconnected
            .Where(x => x.Event.Reason is DisconnectReason.ServerInitiated)
            .Subscribe(disconnectedClients.Add);

        using var client1 = await ConnectClientAsync();
        using var client2 = await ConnectClientAsync();
        await WaitForConditionAsync(() => _server.ClientCount == 2);

        // Act
        await _server.StopAsync(WebSocketCloseStatus.NormalClosure, "Test");
        // Assert

        Assert.Equal(2, disconnectedClients.Count);
        Assert.Equal(0, _server.ClientCount);
    }

    [Fact(Timeout = 10000)]
    public async Task Should_Be_Disposable()
    {
        // Arrange
        var port = GetAvailablePort();
        var server = new ReactiveWebSocketServer($"http://localhost:{port}/");
        await server.StartAsync();

        var connectedTcs = new TaskCompletionSource<ClientConnected>();
        using var sub = server.ClientConnected.Subscribe(connectedTcs.SetResult);

        using var client = new ClientWebSocket();
        await client.ConnectAsync(new Uri($"ws://localhost:{port}/"), CancellationToken.None);

        await WaitAsync(connectedTcs);

        // Act
        if (server is IAsyncDisposable asyncDisposable)
        {
            await asyncDisposable.DisposeAsync();
        }
        else
        {
            server.Dispose();
        }

        // Assert
        Assert.True(server.IsDisposed);
        Assert.False(server.IsRunning);
        Assert.Equal(0, server.ClientCount);
    }

    #endregion

    #region Integration Tests

    [Fact(Timeout = 1000000)]
    public async Task EchoServer_Should_Reply_To_All_Messages()
    {
        // Arrange
        using var messageSubscription = _server.Messages.Subscribe(msg =>
        {
            Assert.True(_server.TrySendAsText(msg.Metadata.Id, $"Echo: {msg.Message.Text}"));
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

    [Fact(Timeout = 10000)]
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

    [Fact(Timeout = 10000)]
    public async Task Should_Handle_Concurrent_Messages_From_Multiple_Clients()
    {
        // Arrange
        var messages = new List<ServerReceivedMessage>();
        using var subscription = _server.Messages.Subscribe(messages.Add);

        using var client1 = await ConnectClientAsync();
        using var client2 = await ConnectClientAsync();
        using var client3 = await ConnectClientAsync();

        await WaitForConditionAsync(() => _server.ClientCount == 3);

        // Act
        var sendTasks = new[]
        {
            SendTextAsync(client1, "From Client 1"),
            SendTextAsync(client2, "From Client 2"),
            SendTextAsync(client3, "From Client 3"),
        };

        await Task.WhenAll(sendTasks);

        await WaitForConditionAsync(() => messages.Count == 3);

        // Assert
        Assert.Equal(3, messages.Count);
        Assert.Contains(messages, m => m.Message.Text == "From Client 1");
        Assert.Contains(messages, m => m.Message.Text == "From Client 2");
        Assert.Contains(messages, m => m.Message.Text == "From Client 3");
    }

    #endregion

    #region Performance & Stress Tests

    [Fact(Timeout = 10000)]
    public async Task Should_Handle_Rapid_Connect_Disconnect()
    {
        // Arrange & Act
        for (var i = 0; i < 10; i++)
        {
            var client = await ConnectClientAsync();
            await client.CloseAsync(WebSocketCloseStatus.NormalClosure, "Test", CancellationToken.None);
            client.Dispose();
        }

        // Assert
        await WaitForConditionAsync(() => _server.ClientCount == 0);
        Assert.Equal(0, _server.ClientCount);
    }

    [Fact(Timeout = 10000)]
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

        await WaitForConditionAsync(() => messageCount == 100, TimeSpan.FromSeconds(10));

        // Assert
        Assert.Equal(100, messageCount);
    }

    [Fact(Timeout = 10000)]
    public async Task Should_Handle_10_Concurrent_Clients()
    {
        // Arrange
        var clients = new List<ClientWebSocket>();

        try
        {
            // Act
            for (var i = 0; i < 10; i++)
            {
                var client = await ConnectClientAsync();
                clients.Add(client);
            }

            await WaitForConditionAsync(() => _server.ClientCount == 10);

            // Assert
            Assert.Equal(10, _server.ClientCount);
            Assert.Equal(10, _server.ConnectedClients.Count);
        }
        finally
        {
            // Cleanup
            foreach (var client in clients)
            {
                try
                {
                    await client.CloseAsync(WebSocketCloseStatus.NormalClosure, "Test done",
                        CancellationToken.None);
                    client.Dispose();
                }
                catch
                {
                    // Ignore cleanup errors
                }
            }
        }
    }

    [Fact(Timeout = 10000)]
    public async Task Should_Handle_Empty_Messages()
    {
        // Arrange
        var messageTcs = new TaskCompletionSource<ServerReceivedMessage>();
        using var subscription = _server.Messages.Subscribe(messageTcs.SetResult);

        using var client = await ConnectClientAsync();

        // Act
        await SendTextAsync(client, "");

        // Assert
        var received = await WaitAsync(messageTcs);
        Assert.NotNull(received);
        Assert.Equal(string.Empty, received.Message.Text);
    }

    [Fact(Timeout = 10000)]
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

        await WaitForConditionAsync(() => messages.Count == 20);

        // Assert
        for (var i = 0; i < 20; i++)
        {
            Assert.Equal($"Order {i}", messages[i].Message.Text);
        }
    }

    [Fact(Timeout = 10000)]
    public async Task NoDeadlock()
    {
        var server = new ReactiveWebSocketServer();
        await server.StartAsync();

        await server.SendInstantAsync(Guid.Empty, "test");
        await server.StopAsync(WebSocketCloseStatus.NormalClosure, "test");

        server.Dispose();
        Assert.True(true);
    }

    #endregion
}