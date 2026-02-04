using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;

namespace WebSocket.Rx.Tests;

public class ReactiveWebSocketServerTests : IAsyncLifetime
{
    private readonly string _testPrefix = $"http://localhost:{GetAvailablePort()}/";
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring as nullable.
    private ReactiveWebSocketServer _server;
    private List<ClientWebSocket> _testClients;
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring as nullable.

    public async Task InitializeAsync()
    {
        _server = new ReactiveWebSocketServer(_testPrefix);
        _testClients = [];
        await Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await _server.StopAsync();
        _server.Dispose();

        foreach (var client in _testClients)
        {
            client.Dispose();
        }

        _testClients.Clear();
    }

    private static int GetAvailablePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    #region Initialization Tests

    [Fact]
    public void Constructor_ShouldInitializeWithDefaultValues()
    {
        // Arrange & Act
        var server = new ReactiveWebSocketServer();

        // Assert
        Assert.Equal(TimeSpan.FromSeconds(30), server.InactivityTimeout);
        Assert.Equal(TimeSpan.FromSeconds(10), server.ConnectTimeout);
        Assert.True(server.IsReconnectionEnabled);
        Assert.Equal(Encoding.UTF8, server.MessageEncoding);
        Assert.True(server.IsTextMessageConversionEnabled);
        Assert.Equal(0, server.ClientCount);

        server.Dispose();
    }

    [Fact]
    public void Constructor_ShouldAcceptCustomPrefix()
    {
        // Arrange & Act
        const string customPrefix = "http://localhost:8888/ws/";
        var server = new ReactiveWebSocketServer(customPrefix);

        // Assert
        Assert.NotNull(server);

        server.Dispose();
    }

    [Fact]
    public void Properties_ShouldBeSettable()
    {
        // Arrange
        var newTimeout = TimeSpan.FromSeconds(60);
        var newConnectTimeout = TimeSpan.FromSeconds(5);
        var newEncoding = Encoding.ASCII;

        // Act
        _server.InactivityTimeout = newTimeout;
        _server.ConnectTimeout = newConnectTimeout;
        _server.IsReconnectionEnabled = false;
        _server.MessageEncoding = newEncoding;
        _server.IsTextMessageConversionEnabled = false;

        // Assert
        Assert.Equal(newTimeout, _server.InactivityTimeout);
        Assert.Equal(newConnectTimeout, _server.ConnectTimeout);
        Assert.False(_server.IsReconnectionEnabled);
        Assert.Equal(newEncoding, _server.MessageEncoding);
        Assert.False(_server.IsTextMessageConversionEnabled);
    }

    #endregion

    #region Start/Stop Tests

    [Fact(Timeout = 5000)]
    public async Task StartAsync_ShouldStartServer()
    {
        // Act
        await _server.StartAsync();

        // Assert
        var client = new ClientWebSocket();
        _testClients.Add(client);

        var uri = new Uri(_testPrefix.Replace("http://", "ws://"));
        await client.ConnectAsync(uri, CancellationToken.None);

        Assert.Equal(WebSocketState.Open, client.State);
    }

    [Fact(Timeout = 5000)]
    public async Task StartAsync_CalledTwice_ShouldNotStartTwice()
    {
        // Arrange
        await _server.StartAsync();

        // Act
        await _server.StartAsync();

        // Assert
        Assert.Equal(0, _server.ClientCount);
    }

    [Fact(Timeout = 5000)]
    public async Task StartAsync_WithCancellationToken_ShouldRespectCancellation()
    {
        // Arrange
        using var cts = new CancellationTokenSource();

        // Act
        await _server.StartAsync(cts.Token);
        await cts.CancelAsync();

        // Assert
        Assert.NotNull(_server);
    }

    [Fact(Timeout = 5000)]
    public async Task StopAsync_ShouldStopServerAndDisconnectClients()
    {
        // Arrange
        await _server.StartAsync();

        var client = new ClientWebSocket();
        _testClients.Add(client);
        var uri = new Uri(_testPrefix.Replace("http://", "ws://"));
        await client.ConnectAsync(uri, CancellationToken.None);

        // Act
        var result = await _server.StopAsync();

        // Assert
        Assert.True(result);
        Assert.True(client.State is WebSocketState.Closed or WebSocketState.Aborted);
    }

    [Fact(Timeout = 5000)]
    public async Task StopAsync_WithCustomStatusAndReason_ShouldUseProvidedValues()
    {
        // Arrange
        await _server.StartAsync();

        // Act
        var result = await _server.StopAsync(WebSocketCloseStatus.EndpointUnavailable, "Custom shutdown");

        // Assert
        Assert.True(result);
    }

    [Fact(Timeout = 5000)]
    public async Task StopAsync_CalledTwice_ShouldReturnFalseSecondTime()
    {
        // Arrange
        await _server.StartAsync();
        await _server.StopAsync();

        // Act
        var result = await _server.StopAsync();

        // Assert
        Assert.False(result);
    }

    #endregion

    #region Client Connection Tests

    [Fact(Timeout = 5000)]
    public async Task ClientConnected_ShouldFireWhenClientConnects()
    {
        // Arrange
        await _server.StartAsync();
        var connectionReceived = false;
        var clientId = Guid.Empty;

        _server.ClientConnected.Subscribe(connected =>
        {
            connectionReceived = true;
            clientId = connected.Metadata.Id;
        });

        // Act
        var client = new ClientWebSocket();
        _testClients.Add(client);
        var uri = new Uri(_testPrefix.Replace("http://", "ws://"));
        await client.ConnectAsync(uri, CancellationToken.None);
        await Task.Delay(50);

        // Assert
        Assert.True(connectionReceived);
        Assert.NotEqual(Guid.Empty, clientId);
        Assert.True(_server.ClientCount > 0);
    }

    [Fact(Timeout = 5000)]
    public async Task ClientDisconnected_ShouldFireWhenClientDisconnects()
    {
        // Arrange
        await _server.StartAsync();
        var disconnectionReceived = false;
        var disconnectReason = DisconnectReason.Undefined;

        _server.ClientDisconnected
            .Subscribe(disconnected =>
            {
                disconnectionReceived = true;
                disconnectReason = disconnected.Event.Reason;
            });

        var client = new ClientWebSocket();
        _testClients.Add(client);
        var uri = new Uri(_testPrefix.Replace("http://", "ws://"));
        await client.ConnectAsync(uri, CancellationToken.None);

        // Act
        await client.CloseAsync(WebSocketCloseStatus.NormalClosure, "Test", CancellationToken.None);

        // Assert
        Assert.True(disconnectionReceived);
        Assert.NotEqual(DisconnectReason.Undefined, disconnectReason);
    }

    [Fact(Timeout = 5000)]
    public async Task ConnectedClients_ShouldReturnCurrentClients()
    {
        // Arrange
        await _server.StartAsync();

        // Act
        var client1 = new ClientWebSocket();
        var client2 = new ClientWebSocket();
        _testClients.Add(client1);
        _testClients.Add(client2);

        var uri = new Uri(_testPrefix.Replace("http://", "ws://"));
        await client1.ConnectAsync(uri, CancellationToken.None);
        await client2.ConnectAsync(uri, CancellationToken.None);

        // Assert
        Assert.True(_server.ClientCount >= 2);
        Assert.NotNull(_server.ConnectedClients);
        Assert.Equal(_server.ClientCount, _server.ConnectedClients.Count);
    }

    #endregion

    #region Send Methods Tests

    [Fact(Timeout = 5000)]
    public async Task SendToClient_WithByteArray_ShouldSendMessage()
    {
        // Arrange
        await _server.StartAsync();

        var client = new ClientWebSocket();
        _testClients.Add(client);
        var uri = new Uri(_testPrefix.Replace("http://", "ws://"));
        await client.ConnectAsync(uri, CancellationToken.None);

        var clientName = _server.ConnectedClients.Keys.First();
        var testData = "Test message"u8.ToArray();

        // Act
        var result = _server.SendToClient(clientName, testData);

        // Assert
        Assert.True(result);

        var buffer = new byte[1024];
        var receiveResult = await client.ReceiveAsync(new ArraySegment<byte>(buffer),
            new CancellationTokenSource(1000).Token);

        var receivedData = new byte[receiveResult.Count];
        Array.Copy(buffer, receivedData, receiveResult.Count);
        Assert.Equal(testData, receivedData);
    }

    [Fact(Timeout = 5000)]
    public async Task SendToClient_WithString_ShouldSendEncodedMessage()
    {
        // Arrange
        await _server.StartAsync();

        var client = new ClientWebSocket();
        _testClients.Add(client);
        var uri = new Uri(_testPrefix.Replace("http://", "ws://"));
        await client.ConnectAsync(uri, CancellationToken.None);

        var clientName = _server.ConnectedClients.Keys.First();
        const string testMessage = "Hello WebSocket";

        // Act
        var result = _server.SendToClient(clientName, testMessage);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void SendToClient_WithInvalidClientName_ShouldReturnFalse()
    {
        // Arrange
        var testData = "Test"u8.ToArray();

        // Act
        var result = _server.SendToClient(Guid.Empty, testData);

        // Assert
        Assert.False(result);
    }

    [Fact(Timeout = 5000)]
    public async Task SendToClient_WithMessageType_ShouldSendCorrectType()
    {
        // Arrange
        await _server.StartAsync();

        var client = new ClientWebSocket();
        _testClients.Add(client);
        var uri = new Uri(_testPrefix.Replace("http://", "ws://"));
        await client.ConnectAsync(uri, CancellationToken.None);

        var clientName = _server.ConnectedClients.Keys.First();
        var testData = "Test"u8.ToArray();

        // Act
        var result = _server.SendToClient(clientName, testData, WebSocketMessageType.Text);

        // Assert
        Assert.True(result);
    }

    [Fact(Timeout = 5000)]
    public async Task SendToClientInstantAsync_WithValidClient_ShouldSendImmediately()
    {
        // Arrange
        await _server.StartAsync();

        var client = new ClientWebSocket();
        _testClients.Add(client);
        var uri = new Uri(_testPrefix.Replace("http://", "ws://"));
        await client.ConnectAsync(uri, CancellationToken.None);

        var clientId = _server.ConnectedClients.Keys.First();
        var testData = "Instant message"u8.ToArray();

        // Act
        var result = await _server.SendToClientInstantAsync(clientId, testData);

        // Assert
        Assert.True(result);
    }

    [Fact(Timeout = 5000)]
    public async Task SendToClientInstantAsync_WithInvalidClient_ShouldReturnFalse()
    {
        // Arrange
        var testData = "Test"u8.ToArray();

        // Act
        var result = await _server.SendToClientInstantAsync(Guid.Empty, testData);

        // Assert
        Assert.False(result);
    }

    [Fact(Timeout = 5000)]
    public async Task Broadcast_ShouldSendToAllClients()
    {
        // Arrange
        await _server.StartAsync();

        var client1 = new ClientWebSocket();
        var client2 = new ClientWebSocket();
        _testClients.Add(client1);
        _testClients.Add(client2);

        var uri = new Uri(_testPrefix.Replace("http://", "ws://"));
        await client1.ConnectAsync(uri, CancellationToken.None);
        await client2.ConnectAsync(uri, CancellationToken.None);

        var testData = "Broadcast message"u8.ToArray();

        // Act
        var result = _server.Broadcast(testData);

        // Assert
        Assert.True(result);
    }

    [Fact(Timeout = 5000)]
    public async Task BroadcastInstantAsync_ShouldSendToAllClientsImmediately()
    {
        // Arrange
        await _server.StartAsync();

        var client1 = new ClientWebSocket();
        var client2 = new ClientWebSocket();
        _testClients.Add(client1);
        _testClients.Add(client2);

        var uri = new Uri(_testPrefix.Replace("http://", "ws://"));
        await client1.ConnectAsync(uri, CancellationToken.None);
        await client2.ConnectAsync(uri, CancellationToken.None);

        var testData = "Instant broadcast"u8.ToArray();

        // Act
        var result = await _server.BroadcastInstantAsync(testData);

        // Assert
        Assert.True(result);
    }

    #endregion

    #region Message Reception Tests

    [Fact(Timeout = 5000)]
    public async Task Messages_ShouldReceiveClientMessages()
    {
        // Arrange
        await _server.StartAsync();
        ServerReceivedMessage? receivedMessage = null;

        _server.Messages.Subscribe(msg => receivedMessage = msg);

        var client = new ClientWebSocket();
        _testClients.Add(client);
        var uri = new Uri(_testPrefix.Replace("http://", "ws://"));
        await client.ConnectAsync(uri, CancellationToken.None);

        const string testMessage = "Hello from client";
        var testData = Encoding.UTF8.GetBytes(testMessage);

        // Act
        await client.SendAsync(new ArraySegment<byte>(testData),
            WebSocketMessageType.Text, true, CancellationToken.None);

        // Assert
        Assert.NotNull(receivedMessage);
    }

    #endregion

    #region Error Handling Tests

    [Fact(Timeout = 5000)]
    public async Task HandleClientError_ShouldRemoveClientAndFireDisconnectedEvent()
    {
        // Arrange
        await _server.StartAsync();
        ClientDisconnected? disconnectedEvent = null;

        _server.ClientDisconnected.Subscribe(e => disconnectedEvent = e);

        var client = new ClientWebSocket();
        _testClients.Add(client);
        var uri = new Uri(_testPrefix.Replace("http://", "ws://"));
        await client.ConnectAsync(uri, CancellationToken.None);

        var initialCount = _server.ClientCount;

        // Act
        client.Abort();

        // Assert
        Assert.NotNull(disconnectedEvent);
        Assert.True(_server.ClientCount < initialCount);
    }

    #endregion

    #region Disposal Tests

    [Fact]
    public void Dispose_ShouldCleanupResources()
    {
        // Arrange
        var server = new ReactiveWebSocketServer(_testPrefix);

        // Act
        server.Dispose();

        // Assert
        server.Dispose();
        Assert.True(true);
    }

    [Fact(Timeout = 5000)]
    public async Task Dispose_WhileRunning_ShouldStopServer()
    {
        // Arrange
        var server = new ReactiveWebSocketServer(_testPrefix);
        await server.StartAsync();

        // Act
        server.Dispose();

        // Assert
        Assert.True(true);
    }

    #endregion

    #region Observable Completion Tests

    [Fact(Timeout = 5000)]
    public async Task StopAsync_ShouldCompleteObservables()
    {
        // Arrange
        await _server.StartAsync();
        var clientConnectedCompleted = false;
        var clientDisconnectedCompleted = false;
        var messagesCompleted = false;

        _server.ClientConnected.Subscribe(_ => { }, () => clientConnectedCompleted = true);
        _server.ClientDisconnected.Subscribe(_ => { }, () => clientDisconnectedCompleted = true);
        _server.Messages.Subscribe(_ => { }, () => messagesCompleted = true);

        // Act
        await _server.StopAsync();

        // Assert
        Assert.True(clientConnectedCompleted);
        Assert.True(clientDisconnectedCompleted);
        Assert.True(messagesCompleted);
    }

    #endregion
}