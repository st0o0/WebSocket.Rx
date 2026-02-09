using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;

namespace WebSocket.Rx.Tests.Internal;

public abstract class ReactiveWebSocketServerTestBase(ITestOutputHelper output) : TestBase(output), IAsyncLifetime
{
    protected ReactiveWebSocketServer Server = null!;
    protected string ServerUrl = string.Empty;
    protected string WebSocketUrl = string.Empty;

    public async ValueTask InitializeAsync()
    {
        const int maxRetries = 10;
        for (var i = 0; i < maxRetries; i++)
        {
            try
            {
                var port = GetAvailablePort();
                ServerUrl = $"http://127.0.0.1:{port}/";
                WebSocketUrl = ServerUrl.Replace("http://", "ws://");
                Server = new ReactiveWebSocketServer(ServerUrl);
                await Server.StartAsync();
                Output.WriteLine($"Server started on {ServerUrl}");
                return;
            }
            catch (Exception ex) when (ex is HttpListenerException or SocketException && i < maxRetries - 1)
            {
                Output.WriteLine($"Failed to start server on attempt {i + 1}: {ex.Message}. Retrying...");
                await Task.Delay(200);
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await Server.DisposeAsync();
    }

    protected static int GetAvailablePort()
    {
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        return ((IPEndPoint)socket.LocalEndPoint!).Port;
    }

    protected async Task<ClientWebSocket> ConnectClientAsync(CancellationToken ct = default)
    {
        var client = new ClientWebSocket();
        await client.ConnectAsync(new Uri(WebSocketUrl), ct);
        return client;
    }

    protected static async Task<string> ReceiveTextAsync(ClientWebSocket client, CancellationToken ct = default)
    {
        var buffer = new byte[1024 * 64];
        var result = await client.ReceiveAsync(buffer, ct);
        return Encoding.UTF8.GetString(buffer, 0, result.Count);
    }

    protected static async Task SendTextAsync(ClientWebSocket client, string message, CancellationToken ct = default)
    {
        var bytes = Encoding.UTF8.GetBytes(message);
        await client.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, ct);
    }
}