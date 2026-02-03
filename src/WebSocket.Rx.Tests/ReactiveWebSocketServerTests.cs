using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using Xunit.Abstractions;

namespace WebSocket.Rx.Tests;

public class ReactiveWebSocketServerTests : IAsyncLifetime
{
    private Uri _uri;
    private ReactiveWebSocketServer _server;
    private ClientWebSocket _testClient;
    private CancellationTokenSource _cts = new(TimeSpan.FromSeconds(10));
    private ITestOutputHelper _output;

    public ReactiveWebSocketServerTests(ITestOutputHelper output)
    {
        _output = output;
    }

    public Task InitializeAsync()
    {
        var url = $"localhost:{GetAvailablePort()}/";
        _uri = new Uri("ws://" + url);
        _server = new ReactiveWebSocketServer("http://" + url);
        _testClient = new ClientWebSocket();
        _output.WriteLine("Test setup completed");
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _testClient.Dispose();
        _server.Dispose();
        _cts.Dispose();
        return Task.CompletedTask;
    }

    private async Task<ReactiveWebSocketServer.WebSocketClient> ConnectTestClientAsync()
    {
        await _testClient.ConnectAsync(_uri, _cts.Token);

        var serverClient = _server.ConnectedClients.Values.FirstOrDefault();
        Assert.NotNull(serverClient);
        return serverClient;
    }

    [Fact(Timeout = 10000)]
    public async Task Server_StartsAndAcceptsConnections()
    {
        // Arrange
        var serverTask = Task.Run(() => _server.StartAsync(_cts.Token));

        // Act
        var client = await ConnectTestClientAsync();

        // Assert
        Assert.Equal(1, _server.ClientCount);
        Assert.NotNull(client);
        Assert.Equal(WebSocketState.Open, _testClient.State);
    }

    [Theory(Timeout = 10000)]
    [InlineData("Hello World")]
    [InlineData("😊 Test with Emoji")]
    [InlineData("")]
    public async Task BroadcastTextAsync_SendText_Success(string message)
    {
        var serverTask = Task.Run(() => _server.StartAsync(_cts.Token));
        await ConnectTestClientAsync();

        var success = await _server.BroadcastTextAsync(message, _cts.Token);

        var buffer = new byte[4096];
        var result = await _testClient.ReceiveAsync(new ArraySegment<byte>(buffer), _cts.Token);
        var receivedText = Encoding.UTF8.GetString(buffer, 0, result.Count);

        Assert.True(success);
        Assert.Equal(message, receivedText);
    }

    [Theory(Timeout = 10000)]
    [InlineData(new byte[] { 1, 2, 3, 4, 5 })]
    [InlineData(new byte[] { 0xFF, 0x00, 0xAA, 0xBB, 0xCC })]
    public async Task BroadcastBinaryAsync_byteArray_Success(byte[] data)
    {
        var serverTask = Task.Run(() => _server.StartAsync(_cts.Token));
        await ConnectTestClientAsync();

        var success = await _server.BroadcastBinaryAsync(data, _cts.Token);

        var buffer = new byte[4096];
        var result = await _testClient.ReceiveAsync(new ArraySegment<byte>(buffer), _cts.Token);
        var receivedData = new byte[result.Count];
        Array.Copy(buffer, receivedData, result.Count);

        Assert.True(success);
        Assert.Equal(data.Length, receivedData.Length);
        Assert.True(data.SequenceEqual(receivedData));
    }

    [Fact(Timeout = 10000)]
    public async Task SendToClientBinaryAsync_SpecificClient_Success()
    {
        var serverTask = Task.Run(() => _server.StartAsync(_cts.Token));
        await ConnectTestClientAsync();

        var client2 = new ClientWebSocket();
        await client2.ConnectAsync(_uri, _cts.Token);
        await Task.Delay(200);

        var clientIds = _server.GetClientIds();
        Assert.Equal(2, clientIds.Length);

        var testData = new byte[] { 42, 99, 255 };

        var success = await _server.SendToClientBinaryAsync(clientIds[0], testData);

        var buffer = new byte[4096];
        var result = await _testClient.ReceiveAsync(new ArraySegment<byte>(buffer), _cts.Token);
        var received = new byte[result.Count];
        Array.Copy(buffer, received, result.Count);

        Assert.True(success);
        Assert.True(testData.SequenceEqual(received));
    }

    [Theory(Timeout = 10000)]
    [InlineData("Hello", WebSocketMessageType.Text)]
    [InlineData("BinaryTest", WebSocketMessageType.Binary)]
    public async Task WebSocketClient_SendTextAsync_Success(string message, WebSocketMessageType expectedType)
    {
        var serverTask = Task.Run(() => _server.StartAsync(_cts.Token));
        var serverClient = await ConnectTestClientAsync();

        var success = await serverClient.SendTextAsync(message, _cts.Token);

        var buffer = new byte[4096];
        var result = await _testClient.ReceiveAsync(new ArraySegment<byte>(buffer), _cts.Token);

        Assert.True(success);
        Assert.Equal(expectedType, result.MessageType);
        var receivedText = Encoding.UTF8.GetString(buffer, 0, result.Count);
        Assert.Equal(message, receivedText);
    }

    [Theory(Timeout = 10000)]
    [InlineData(1000)]
    [InlineData(10000)]
    public async Task WebSocketClient_SendAsync_ArraySegment_Success(int size)
    {
        var serverTask = Task.Run(() => _server.StartAsync(_cts.Token));
        var serverClient = await ConnectTestClientAsync();
        var data = new byte[size];
        new Random(42).NextBytes(data);

        var segment = new ArraySegment<byte>(data);
        var success = await serverClient.SendAsync(segment, _cts.Token);

        using var ms = new MemoryStream();
        WebSocketReceiveResult result;
        do
        {
            var buffer = new byte[4096];
            result = await _testClient.ReceiveAsync(new ArraySegment<byte>(buffer), _cts.Token);
            ms.Write(buffer, 0, result.Count);
        } while (!result.EndOfMessage);

        var received = ms.ToArray();

        Assert.True(success);
        Assert.True(data.SequenceEqual(received));
    }

    [Fact(Timeout = 10000)]
    public async Task WebSocketClient_SendAsync_ReadOnlyMemory_Success()
    {
        var serverTask = Task.Run(() => _server.StartAsync(_cts.Token));
        var serverClient = await ConnectTestClientAsync();
        var data = "MemoryTest"u8.ToArray();

        var success = await serverClient.SendAsync(new ReadOnlyMemory<byte>(data), _cts.Token);

        var buffer = new byte[4096];
        var result = await _testClient.ReceiveAsync(new ArraySegment<byte>(buffer), _cts.Token);
        Assert.True(success);
        Assert.Equal("MemoryTest", Encoding.UTF8.GetString(buffer, 0, result.Count));
    }

    [Fact(Timeout = 10000)]
    public async Task WebSocketClient_SendTextAsync_CustomEncoding_Success()
    {
        var serverTask = Task.Run(() => _server.StartAsync(_cts.Token));
        var serverClient = await ConnectTestClientAsync();

        var success = await serverClient.SendTextAsync("UTF16-Test", _cts.Token);

        var buffer = new byte[4096];
        var result = await _testClient.ReceiveAsync(new ArraySegment<byte>(buffer), _cts.Token);
        var received = Encoding.Unicode.GetString(buffer, 0, result.Count);

        Assert.True(success);
        Assert.Equal("UTF16-Test", received);
    }

    [Fact(Timeout = 10000)]
    public async Task WebSocketClient_SendStreamAsync_Success()
    {
        var serverTask = Task.Run(() => _server.StartAsync(_cts.Token));
        var serverClient = await ConnectTestClientAsync();
        var streamData = new byte[5000];
        new Random().NextBytes(streamData);
        var stream = new MemoryStream(streamData);

        var success = await serverClient.SendStreamAsync(stream, _cts.Token);

        using var ms = new MemoryStream();
        WebSocketReceiveResult result;
        do
        {
            var buffer = new byte[4096];
            result = await _testClient.ReceiveAsync(new ArraySegment<byte>(buffer), _cts.Token);
            ms.Write(buffer, 0, result.Count);
        } while (!result.EndOfMessage);

        var received = ms.ToArray();

        Assert.True(success);
        Assert.True(streamData.SequenceEqual(received));
    }

    [Fact(Timeout = 10000)]
    public async Task SendMethods_FailWhenSocketClosed()
    {
        var serverTask = Task.Run(() => _server.StartAsync(_cts.Token));
        var serverClient = await ConnectTestClientAsync();

        await _testClient.CloseAsync(WebSocketCloseStatus.NormalClosure, "Test", _cts.Token);
        await Task.Delay(100);

        var success = await serverClient.SendTextAsync("Should fail", _cts.Token);

        Assert.False(success);
    }

    [Fact(Timeout = 10000)]
    public async Task Server_HandlesMultipleClients()
    {
        var serverTask = Task.Run(() => _server.StartAsync(_cts.Token));

        var clients = new List<ClientWebSocket>();
        for (var i = 0; i < 3; i++)
        {
            var client = new ClientWebSocket();
            await client.ConnectAsync(_uri, _cts.Token);
            clients.Add(client);
            await Task.Delay(100);
        }

        await _server.BroadcastTextAsync("Multi-Client-Test", _cts.Token);

        foreach (var client in clients)
        {
            var buffer = new byte[1024];
            var result = await client.ReceiveAsync(new ArraySegment<byte>(buffer), _cts.Token);
            var msg = Encoding.UTF8.GetString(buffer, 0, result.Count);
            Assert.Equal("Multi-Client-Test", msg);
        }
    }

    private static int GetAvailablePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}