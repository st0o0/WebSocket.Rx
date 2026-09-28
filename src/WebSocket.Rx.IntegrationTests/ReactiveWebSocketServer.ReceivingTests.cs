using System.Collections.Concurrent;
using System.Net.WebSockets;
using R3;
using WebSocket.Rx.IntegrationTests.Internal;

namespace WebSocket.Rx.IntegrationTests;

public class ReactiveWebSocketServerReceivingTests(ITestOutputHelper output) : ReactiveWebSocketServerTestBase(output)
{
    [Fact(Timeout = 15000)]
    public async Task Should_Receive_Text_Message_From_Client()
    {
        // Arrange
        using var client = await ConnectClientAsync(TestContext.Current.CancellationToken);
        var messageTask = WaitForEventAsync(Server.Messages);

        // Act
        await SendTextAsync(client, "Hello Server", TestContext.Current.CancellationToken);

        // Assert
        var receivedMessage = await messageTask;
        Assert.Multiple(() =>
        {
            Assert.NotNull(receivedMessage);
            Assert.Equal("Hello Server", receivedMessage.Message.Text.ToString());
            Assert.NotNull(receivedMessage.Metadata);
        });
    }

    [Fact(Timeout = 15000)]
    public async Task Should_Receive_Binary_Message_From_Client()
    {
        // Arrange
        using var client = await ConnectClientAsync(TestContext.Current.CancellationToken);
        var messageTask = WaitForEventAsync(Server.Messages);
        var binaryData = new byte[] { 1, 2, 3, 4, 5 };

        // Act
        await client.SendAsync(
            new ArraySegment<byte>(binaryData),
            WebSocketMessageType.Binary,
            true,
            TestContext.Current.CancellationToken);

        // Assert
        var receivedMessage = await messageTask;
        Assert.Multiple(() =>
        {
            Assert.NotNull(receivedMessage);
            Assert.Equal(binaryData, receivedMessage.Message.Binary);
        });
    }

    [Fact(Timeout = 15000)]
    public async Task Should_Receive_Multiple_Messages_From_Same_Client()
    {
        // Arrange
        var messages = new ConcurrentQueue<ServerMessage>();
        using var subscription = Server.Messages.Subscribe(messages.Enqueue);
        using var client = await ConnectClientAsync(TestContext.Current.CancellationToken);

        // Act
        var receiveTask = WaitForEventAsync(Server.Messages, _ => messages.Count == 2);
        await SendTextAsync(client, "Message 1", TestContext.Current.CancellationToken);
        await SendTextAsync(client, "Message 2", TestContext.Current.CancellationToken);

        await receiveTask;

        // Assert
        var received = messages.ToArray();
        Assert.Multiple(() =>
        {
            Assert.Equal(2, received.Length);
            Assert.Equal("Message 1", received[0].Message.Text.ToString());
            Assert.Equal("Message 2", received[1].Message.Text.ToString());
        });
    }

    [Fact(Timeout = 15000)]
    public async Task Should_Handle_Large_Messages()
    {
        // Arrange
        using var client = await ConnectClientAsync(TestContext.Current.CancellationToken);
        var messageTask = WaitForEventAsync(Server.Messages);
        var largeData = new byte[1024 * 1024]; // 1MB
        new Random().NextBytes(largeData);

        // Act
        await client.SendAsync(
            new ArraySegment<byte>(largeData),
            WebSocketMessageType.Binary,
            true,
            TestContext.Current.CancellationToken);

        // Assert
        var receivedMessage = await messageTask;
        Assert.Equal(largeData, receivedMessage.Message.Binary);
    }

    [Fact(Timeout = 15000)]
    public async Task Should_Handle_Empty_Messages()
    {
        // Arrange
        using var client = await ConnectClientAsync(TestContext.Current.CancellationToken);
        var messageTask = WaitForEventAsync(Server.Messages);

        // Act
        await client.SendAsync(
            new ArraySegment<byte>([]),
            WebSocketMessageType.Binary,
            true,
            TestContext.Current.CancellationToken);

        // Assert
        var receivedMessage = await messageTask;
        Assert.Empty(receivedMessage.Message.Binary.ToArray());
    }

    [Fact(Timeout = 15000)]
    public async Task Should_Maintain_Message_Order()
    {
        // Arrange
        using var client = await ConnectClientAsync(TestContext.Current.CancellationToken);
        var receivedTexts = new ConcurrentQueue<string>();
        using var subscription = Server.Messages.Subscribe(msg =>
        {
            if (!msg.Message.Text.IsEmpty)
            {
                receivedTexts.Enqueue(msg.Message.Text.ToString());
            }
        });

        // Act
        var receiveTask = WaitForEventAsync(Server.Messages, _ => receivedTexts.Count == 100);
        for (var i = 0; i < 100; i++)
        {
            await SendTextAsync(client, $"Message {i}", TestContext.Current.CancellationToken);
        }

        await receiveTask;

        // Assert
        var received = receivedTexts.ToArray();
        for (var i = 0; i < 100; i++)
        {
            Assert.Equal($"Message {i}", received[i]);
        }
    }

    [Fact(Timeout = 15000)]
    public async Task Should_Handle_Many_Small_Messages()
    {
        // Arrange
        using var client = await ConnectClientAsync(TestContext.Current.CancellationToken);
        var messageCount = 0;
        using var subscription = Server.Messages.Subscribe(_ => Interlocked.Increment(ref messageCount));

        // Act
        var receiveTask = WaitForEventAsync(Server.Messages, _ => messageCount == 500);
        var tasks = Enumerable.Range(0, 500).Select(i => SendTextAsync(client, $"msg{i}", TestContext.Current.CancellationToken));
        await Task.WhenAll(tasks);

        await receiveTask;

        // Assert
        Assert.Equal(500, messageCount);
    }
}