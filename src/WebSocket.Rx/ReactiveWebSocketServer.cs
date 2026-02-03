using System.Net;
using System.Net.WebSockets;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Text;

namespace WebSocket.Rx;

public class ReactiveWebSocketServer : IDisposable
{
    private readonly HttpListener _listener;
    private readonly Subject<string> _messages = new();
    private readonly Subject<WebSocketClient> _clients = new();
    private CancellationTokenSource? _cts;

    public IObservable<string> Messages => _messages.AsObservable();
    public IObservable<WebSocketClient> Clients => _clients.AsObservable();

    public ReactiveWebSocketServer(string prefix = "http://localhost:8080/")
    {
        _listener = new HttpListener();
        _listener.Prefixes.Add(prefix);
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _listener.Start();
        Console.WriteLine($"WebSocket Server gestartet auf {_listener.Prefixes.First()}");

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

        WebSocketStream.CreateReadableMessageStream(wsContext.WebSocket);
        
        var client = new WebSocketClient(wsContext.WebSocket);
        _clients.OnNext(client);

        client.Messages.Subscribe(
            message => _messages.OnNext(message),
            error => Console.WriteLine($"Client Fehler: {error.Message}"),
            () => Console.WriteLine("Client getrennt")
        );

        await client.ReceiveLoopAsync(_cts!.Token);
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _listener.Stop();
        _listener.Close();
        _messages.Dispose();
        _clients.Dispose();
        GC.SuppressFinalize(this);
    }
}

public class WebSocketClient
{
    private readonly System.Net.WebSockets.WebSocket _socket;
    private readonly Subject<string> _messages = new();

    public IObservable<string> Messages => _messages.AsObservable();

    public WebSocketClient(System.Net.WebSockets.WebSocket socket)
    {
        _socket = socket;
    }

    public async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        var buffer = new byte[1024 * 4];
        
        try
        {
            while (_socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
            {
                var result = await _socket.ReceiveAsync(
                    new ArraySegment<byte>(buffer), 
                    cancellationToken
                );

                switch (result.MessageType)
                {
                    case WebSocketMessageType.Close:
                        await _socket.CloseAsync(
                            WebSocketCloseStatus.NormalClosure, 
                            "Closing", 
                            cancellationToken
                        );
                        _messages.OnCompleted();
                        break;
                    case WebSocketMessageType.Text:
                    {
                        var message = Encoding.UTF8.GetString(buffer, 0, result.Count);
                        _messages.OnNext(message);
                        break;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _messages.OnError(ex);
        }
    }

    public async Task SendAsync(string message, CancellationToken cancellationToken = default)
    {
        var bytes = Encoding.UTF8.GetBytes(message);
        await _socket.SendAsync(
            new ArraySegment<byte>(bytes), 
            WebSocketMessageType.Text, 
            true, 
            cancellationToken
        );
    }
}