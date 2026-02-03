using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;

namespace WebSocket.Rx.Tests.Internal;

public class WebSocketTestServer : IAsyncDisposable
{
    private readonly HttpListener _httpListener;
    private readonly ConcurrentBag<System.Net.WebSockets.WebSocket> _clients = new();
    private readonly CancellationTokenSource _cts;
    private Task? _serverTask;

    public int Port { get; }
    public string Url => $"http://localhost:{Port}/";
    public string WebSocketUrl => $"ws://localhost:{Port}/";

    public event Action<string>? OnMessageReceived;
    public event Action<byte[]>? OnBytesReceived;

    public WebSocketTestServer(int? port = null)
    {
        Port = port ?? GetAvailablePort();
        _httpListener = new HttpListener();
        _httpListener.Prefixes.Add(Url);
        _cts = new CancellationTokenSource();
    }

    public async Task StartAsync()
    {
        _httpListener.Start();
        _serverTask = Task.Run(() => AcceptConnectionsAsync(_cts.Token));
        await Task.Delay(100);
    }

    private async Task AcceptConnectionsAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var context = await _httpListener.GetContextAsync();

                if (context.Request.IsWebSocketRequest)
                {
                    _ = HandleWebSocketConnectionAsync(context, cancellationToken);
                }
                else
                {
                    context.Response.StatusCode = 400;
                    context.Response.Close();
                }
            }
            catch (Exception) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (HttpListenerException)
            {
                break;
            }
        }
    }

    private async Task HandleWebSocketConnectionAsync(HttpListenerContext context, CancellationToken cancellationToken)
    {
        WebSocketContext? wsContext = null;

        try
        {
            wsContext = await context.AcceptWebSocketAsync(null);
            var webSocket = wsContext.WebSocket;
            _clients.Add(webSocket);

            await ReceiveLoopAsync(webSocket, cancellationToken);
        }
        catch (Exception)
        {
            context.Response.StatusCode = 500;
            context.Response.Close();
        }
        finally
        {
            wsContext?.WebSocket.Dispose();
        }
    }

    private async Task ReceiveLoopAsync(System.Net.WebSockets.WebSocket webSocket, CancellationToken cancellationToken)
    {
        var buffer = new byte[1024 * 4];

        try
        {
            while (webSocket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
            {
                var result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    await webSocket.CloseAsync(
                        WebSocketCloseStatus.NormalClosure,
                        "Closing",
                        CancellationToken.None
                    );
                    break;
                }

                switch (result.MessageType)
                {
                    case WebSocketMessageType.Text:
                    {
                        var message = Encoding.UTF8.GetString(buffer, 0, result.Count);
                        OnMessageReceived?.Invoke(message);
                        break;
                    }
                    case WebSocketMessageType.Binary:
                    {
                        var bytes = new byte[result.Count];
                        Array.Copy(buffer, bytes, result.Count);
                        OnBytesReceived?.Invoke(bytes);
                        break;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _ = ex;
        }
    }

    public async Task SendToAllAsync(string message)
    {
        var bytes = Encoding.UTF8.GetBytes(message);

        foreach (var client in _clients)
        {
            if (client.State is not WebSocketState.Open) continue;
            try
            {
                await client.SendAsync(
                    new ArraySegment<byte>(bytes),
                    WebSocketMessageType.Text,
                    true,
                    CancellationToken.None
                );
            }
            catch (Exception ex)
            {
                _ = ex;
            }
        }
    }

    public async Task SendBinaryToAllAsync(byte[] data)
    {
        foreach (var client in _clients)
        {
            if (client.State is not WebSocketState.Open) continue;
            try
            {
                await client.SendAsync(
                    new ArraySegment<byte>(data),
                    WebSocketMessageType.Binary,
                    true,
                    CancellationToken.None
                );
            }
            catch (Exception ex)
            {
                _ = ex;
            }
        }
    }

    public async Task DisconnectAllAsync()
    {
        var clientsCopy = _clients.ToList();
        Parallel.ForEach(clientsCopy, client =>
        {
            try
            {
                if (client.State is WebSocketState.Open or WebSocketState.CloseReceived)
                {
                    client.Abort();
                }
            }
            catch (Exception ex)
            {
                _ = ex;
            }
        });

        await Task.Delay(50);

        foreach (var client in clientsCopy)
        {
            try
            {
                client.Dispose();
            }
            catch (Exception ex)
            {
                _ = ex;
            }
        }

        _clients.Clear();
    }

    private static int GetAvailablePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    public async ValueTask DisposeAsync()
    {
        await _cts.CancelAsync();
        _httpListener.Stop();
        _httpListener.Close();
        _cts.Dispose();
        await (_serverTask?.WaitAsync(TimeSpan.FromSeconds(2)) ?? Task.CompletedTask);
        GC.SuppressFinalize(this);
    }
}