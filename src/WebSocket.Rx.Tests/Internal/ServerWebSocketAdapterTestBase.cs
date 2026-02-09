using System.Net.WebSockets;
using NSubstitute;

namespace WebSocket.Rx.Tests.Internal;

public abstract class ServerWebSocketAdapterTestBase(ITestOutputHelper output) : TestBase(output), IAsyncLifetime
{
    protected readonly System.Net.WebSockets.WebSocket
        MockWebSocket = Substitute.For<System.Net.WebSockets.WebSocket>();

    protected ReactiveWebSocketServer.ServerWebSocketAdapter? Adapter;

    public async ValueTask InitializeAsync()
    {
        MockWebSocket.State.Returns(WebSocketState.Open);
        await Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        if (Adapter is not null)
        {
            await Adapter.DisposeAsync();
        }
    }
}