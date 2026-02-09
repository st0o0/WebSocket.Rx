namespace WebSocket.Rx.Tests.Internal;

public abstract class ReactiveWebSocketClientTestBase(ITestOutputHelper output) : TestBase(output), IAsyncLifetime
{
    protected WebSocketTestServer Server = null!;
    protected ReactiveWebSocketClient? Client;

    public async ValueTask InitializeAsync()
    {
        Server = new WebSocketTestServer();
        await Server.StartAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (Client is not null)
        {
            await Client.DisposeAsync();
        }

        await Server.DisposeAsync();
    }
}