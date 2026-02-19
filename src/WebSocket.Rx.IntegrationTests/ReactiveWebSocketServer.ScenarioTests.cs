using System.Net.WebSockets;
using R3;
using WebSocket.Rx.IntegrationTests.Internal;

namespace WebSocket.Rx.IntegrationTests;

public class ReactiveWebSocketServerScenarioTests(ITestOutputHelper output) : ReactiveWebSocketServerTestBase(output)
{
    [Fact(Timeout = DefaultTimeoutMs)]
    public async Task EchoServer_Should_Reply_To_All_Messages()
    {
        // Arrange
        using var subscription = Server.Messages.SubscribeAwait(async (msg, ct) =>
        {
            await Server.SendInstantAsync(msg.Metadata.Id, msg.Message.Text, msg.Message.Type, ct);
        });

        using var client = await ConnectClientAsync(TestContext.Current.CancellationToken);

        // Act & Assert
        await SendTextAsync(client, "Ping 1", TestContext.Current.CancellationToken);
        Assert.Equal("Ping 1", await ReceiveTextAsync(client, TestContext.Current.CancellationToken));

        await SendTextAsync(client, "Ping 2", TestContext.Current.CancellationToken);
        Assert.Equal("Ping 2", await ReceiveTextAsync(client, TestContext.Current.CancellationToken));
    }

    [Fact(Timeout = DefaultTimeoutMs)]
    public async Task ChatRoom_Should_Broadcast_Messages_To_Other_Clients()
    {
        // Arrange
        using var subscription = Server.Messages.SubscribeAwait(async (msg, ct) =>
        {
            var otherClients = Server.ConnectedClients.Keys.Where(id => id != msg.Metadata.Id);
            foreach (var clientId in otherClients)
            {
                await Server.SendInstantAsync(clientId, $"{msg.Metadata.Id}: {msg.Message.Text}".AsMemory(),
                    WebSocketMessageType.Binary,
                    ct);
            }
        });

        var connectionTask1 = WaitUntilAsync(Server.ClientConnected, () => Server.ClientCount >= 1);
        using var client1 = await ConnectClientAsync(TestContext.Current.CancellationToken);
        await connectionTask1;

        var connectionTask2 = WaitUntilAsync(Server.ClientConnected, () => Server.ClientCount >= 2);
        using var client2 = await ConnectClientAsync(TestContext.Current.CancellationToken);
        await connectionTask2;

        // Act
        await SendTextAsync(client1, "Hello everyone", TestContext.Current.CancellationToken);

        // Assert
        var received = await ReceiveTextAsync(client2, TestContext.Current.CancellationToken);
        Assert.Contains("Hello everyone", received);
    }

    [Fact(Timeout = DefaultTimeoutMs)]
    public async Task Should_Handle_Concurrent_Messages_From_Multiple_Clients()
    {
        // Arrange
        const int clientCount = 5;
        const int messagesPerClient = 20;
        var clients = new List<ClientWebSocket>();
        for (var i = 0; i < clientCount; i++)
        {
            var connectionTask = WaitUntilAsync(Server.ClientConnected, () => Server.ClientCount >= i + 1);
            clients.Add(await ConnectClientAsync(TestContext.Current.CancellationToken));
            await connectionTask;
        }

        var totalReceived = 0;
        using var subscription = Server.Messages.Subscribe(_ => Interlocked.Increment(ref totalReceived));

        // Act
        var receiveTask = WaitForEventAsync(Server.Messages, _ => totalReceived == clientCount * messagesPerClient);
        var sendTasks = clients.SelectMany(c =>
            Enumerable.Range(0, messagesPerClient)
                .Select(i => SendTextAsync(c, $"msg {i}", TestContext.Current.CancellationToken))
        );
        await Task.WhenAll(sendTasks);

        await receiveTask;

        // Assert
        Assert.Equal(clientCount * messagesPerClient, totalReceived);

        foreach (var c in clients) c.Dispose();
    }
}