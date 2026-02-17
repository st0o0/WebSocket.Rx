using System.Text;
using WebSocket.Rx.IntegrationTests.Internal;

namespace WebSocket.Rx.IntegrationTests;

public class ReactiveWebSocketServerPropertyTests(ITestOutputHelper output) : ReactiveWebSocketServerTestBase(output)
{
    [Fact(Timeout = DefaultTimeoutMs)]
    public void Should_Have_Default_Properties()
    {
        // Arrange & Act
        var port = GetAvailablePort();
        var url = $"http://127.0.0.1:{port}/";
        using var server = new ReactiveWebSocketServer(url);

        // Assert
        Assert.Equal(TimeSpan.FromSeconds(30), server.IdleConnection);
        Assert.Equal(TimeSpan.FromSeconds(10), server.ConnectTimeout);
        Assert.Equal(Encoding.UTF8, server.MessageEncoding);
        Assert.True(server.IsTextMessageConversionEnabled);
        Assert.Equal(0, server.ClientCount);
        Assert.Empty(server.ConnectedClients);
    }
}