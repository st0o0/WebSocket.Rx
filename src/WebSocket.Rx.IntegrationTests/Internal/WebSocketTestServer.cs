using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;

namespace WebSocket.Rx.IntegrationTests.Internal;

public class WebSocketTestServer(int? port = null) : IAsyncDisposable
{
    private readonly HttpListener _httpListener = new();
    private readonly ConcurrentBag<System.Net.WebSockets.WebSocket> _clients = [];
    private readonly CancellationTokenSource _cts = new();
    private Task? _serverTask;

    public int Port { get; private set; } = port ?? 0;
    public string Url => $"http://127.0.0.1:{Port}/";
    public string WebSocketUrl => $"ws://127.0.0.1:{Port}/";
    public int ClientCount => _clients.Count(c => c.State == WebSocketState.Open);
    public event Action<string>? OnMessageReceived;
    public event Action<byte[]>? OnBytesReceived;

    public async Task StartAsync()
    {
        const int maxRetries = 10;
        for (var i = 0; i < maxRetries; i++)
        {
            try
            {
                if (Port == 0 || i > 0)
                {
                    Port = GetAvailablePort();
                }

                _httpListener.Prefixes.Clear();
                _httpListener.Prefixes.Add(Url);
                _httpListener.Start();
                _serverTask = Task.Run(() => AcceptConnectionsAsync(_cts.Token));
                await Task.Delay(100);
                return;
            }
            catch (HttpListenerException) when (i < maxRetries - 1)
            {
                await Task.Delay(100);
            }
        }

        // Final attempt that will throw if it fails
        if (!_httpListener.IsListening)
        {
            _httpListener.Prefixes.Clear();
            _httpListener.Prefixes.Add(Url);
            _httpListener.Start();
            _serverTask = Task.Run(() => AcceptConnectionsAsync(_cts.Token));
            await Task.Delay(100);
        }
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
        catch (Exception)
        {
            // noop
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
            catch (Exception)
            {
                // noop
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
            catch (Exception)
            {
                // noop
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
            catch (Exception)
            {
                // noop
            }
        });

        await Task.Delay(50);

        foreach (var client in clientsCopy)
        {
            try
            {
                client.Dispose();
            }
            catch (Exception)
            {
                // noop
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

        try
        {
            await DisconnectAllAsync();
        }
        catch
        {
            // noop
        }

        try
        {
            _httpListener.Stop();
            _httpListener.Close();
        }
        catch
        {
            // noop
        }

        _cts.Dispose();

        if (_serverTask != null)
        {
            try
            {
                await _serverTask.WaitAsync(TimeSpan.FromSeconds(5));
            }
            catch
            {
                // noop
            }
        }

        GC.SuppressFinalize(this);
    }
}