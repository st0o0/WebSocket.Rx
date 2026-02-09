using System.Net.WebSockets;
using R3;
using WebSocket.Rx.Tests.Internal;

namespace WebSocket.Rx.Tests;

public class ReactiveWebSocketClientKeepAliveTests(ITestOutputHelper output) : ReactiveWebSocketClientTestBase(output)
{
    [Fact(Timeout = DefaultTimeoutMs)]
    public async Task KeepAliveInterval_ShouldBeConfigurable()
    {
        // Arrange
        var keepAliveInterval = TimeSpan.FromSeconds(5);
        Client = new ReactiveWebSocketClient(new Uri(Server.WebSocketUrl))
        {
            KeepAliveInterval = keepAliveInterval
        };

        // Act
        await Client.StartOrFailAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(keepAliveInterval, Client.KeepAliveInterval);
        Assert.Equal(keepAliveInterval, Client.NativeClient.Options.KeepAliveInterval);
    }

    [Fact(Timeout = DefaultTimeoutMs)]
    public async Task KeepAliveTimeout_ShouldBeConfigurable()
    {
        // Arrange
        var keepAliveTimeout = TimeSpan.FromSeconds(15);
        Client = new ReactiveWebSocketClient(new Uri(Server.WebSocketUrl))
        {
            KeepAliveTimeout = keepAliveTimeout
        };

        // Act
        await Client.StartOrFailAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(keepAliveTimeout, Client.KeepAliveTimeout);
        Assert.Equal(keepAliveTimeout, Client.NativeClient.Options.KeepAliveTimeout);
    }

    [Fact(Timeout = DefaultTimeoutMs)]
    public async Task KeepAlive_ShouldApplyDefaultValues()
    {
        // Arrange
        Client = new ReactiveWebSocketClient(new Uri(Server.WebSocketUrl));

        // Act
        await Client.StartOrFailAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(TimeSpan.FromSeconds(30), Client.KeepAliveInterval);
        Assert.Equal(TimeSpan.FromSeconds(10), Client.KeepAliveTimeout);
        Assert.Equal(TimeSpan.FromSeconds(30), Client.NativeClient.Options.KeepAliveInterval);
        Assert.Equal(TimeSpan.FromSeconds(10), Client.NativeClient.Options.KeepAliveTimeout);
    }

    [Fact(Timeout = DefaultTimeoutMs)]
    public async Task KeepAlive_ShouldBeAppliedBeforeConnection()
    {
        // Arrange
        var keepAliveInterval = TimeSpan.FromSeconds(2);
        var keepAliveTimeout = TimeSpan.FromSeconds(5);

        Client = new ReactiveWebSocketClient(new Uri(Server.WebSocketUrl))
        {
            KeepAliveInterval = keepAliveInterval,
            KeepAliveTimeout = keepAliveTimeout
        };

        // Act
        await Client.StartOrFailAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.True(Client.IsStarted);
        Assert.True(Client.IsRunning);
        Assert.Equal(keepAliveInterval, Client.NativeClient.Options.KeepAliveInterval);
        Assert.Equal(keepAliveTimeout, Client.NativeClient.Options.KeepAliveTimeout);
    }

    [Fact(Timeout = DefaultTimeoutMs)]
    public async Task KeepAlive_SettingsChangedAfterConnection_ShouldNotAffectCurrentConnection()
    {
        // Arrange
        var initialInterval = TimeSpan.FromSeconds(30);
        Client = new ReactiveWebSocketClient(new Uri(Server.WebSocketUrl))
        {
            KeepAliveInterval = initialInterval
        };

        await Client.StartOrFailAsync(TestContext.Current.CancellationToken);
        var connectedInterval = Client.NativeClient.Options.KeepAliveInterval;

        // Act
        Client.KeepAliveInterval = TimeSpan.FromSeconds(5);

        // Assert
        Assert.Equal(initialInterval, connectedInterval);
        Assert.Equal(TimeSpan.FromSeconds(5), Client.KeepAliveInterval);
        Assert.Equal(initialInterval, Client.NativeClient.Options.KeepAliveInterval);
    }

    [Fact(Timeout = DefaultTimeoutMs)]
    public async Task KeepAlive_AfterReconnect_ShouldApplyNewSettings()
    {
        // Arrange
        Client = new ReactiveWebSocketClient(new Uri(Server.WebSocketUrl))
        {
            KeepAliveInterval = TimeSpan.FromSeconds(30),
            KeepAliveTimeout = TimeSpan.FromSeconds(10)
        };

        await Client.StartOrFailAsync(TestContext.Current.CancellationToken);

        Client.KeepAliveInterval = TimeSpan.FromSeconds(5);
        Client.KeepAliveTimeout = TimeSpan.FromSeconds(3);

        // Act
        await Client.ReconnectOrFailAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(TimeSpan.FromSeconds(5), Client.NativeClient.Options.KeepAliveInterval);
        Assert.Equal(TimeSpan.FromSeconds(3), Client.NativeClient.Options.KeepAliveTimeout);
    }

    [Fact(Timeout = DefaultTimeoutMs)]
    public async Task KeepAlive_WithZeroInterval_ShouldDisableKeepAlive()
    {
        // Arrange
        Client = new ReactiveWebSocketClient(new Uri(Server.WebSocketUrl))
        {
            KeepAliveInterval = TimeSpan.Zero
        };

        // Act
        await Client.StartOrFailAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(TimeSpan.Zero, Client.KeepAliveInterval);
        Assert.Equal(TimeSpan.Zero, Client.NativeClient.Options.KeepAliveInterval);
    }

    [Fact(Timeout = DefaultTimeoutMs)]
    public async Task KeepAlive_ConnectionShouldStayAliveWithinInterval()
    {
        // Arrange
        var receivedMessages = new List<ReceivedMessage>();
        Client = new ReactiveWebSocketClient(new Uri(Server.WebSocketUrl))
        {
            KeepAliveInterval = TimeSpan.FromMilliseconds(500),
            IsTextMessageConversionEnabled = true
        };

        Client.MessageReceived.Subscribe(msg => receivedMessages.Add(msg));

        // Act
        await Client.StartOrFailAsync(TestContext.Current.CancellationToken);
        await Task.Delay(100, TestContext.Current.CancellationToken);
        await Server.SendToAllAsync("test");
        await Task.Delay(100, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(Client.IsRunning);
        Assert.Equal(WebSocketState.Open, Client.NativeClient.State);
        Assert.Single(receivedMessages);
        Assert.Equal("test", receivedMessages[0].Text);
    }

    [Fact(Timeout = DefaultTimeoutMs)]
    public async Task KeepAlive_MultipleReconnects_ShouldMaintainSettings()
    {
        // Arrange
        var keepAliveInterval = TimeSpan.FromSeconds(3);
        var keepAliveTimeout = TimeSpan.FromSeconds(2);

        Client = new ReactiveWebSocketClient(new Uri(Server.WebSocketUrl))
        {
            KeepAliveInterval = keepAliveInterval,
            KeepAliveTimeout = keepAliveTimeout
        };

        await Client.StartOrFailAsync(TestContext.Current.CancellationToken);

        for (var i = 0; i < 3; i++)
        {
            await Client.ReconnectOrFailAsync(TestContext.Current.CancellationToken);

            Assert.Equal(keepAliveInterval, Client.NativeClient.Options.KeepAliveInterval);
            Assert.Equal(keepAliveTimeout, Client.NativeClient.Options.KeepAliveTimeout);
        }
    }

    [Fact(Timeout = DefaultTimeoutMs)]
    public async Task KeepAlive_VeryShortInterval_ShouldStillWork()
    {
        // Arrange
        Client = new ReactiveWebSocketClient(new Uri(Server.WebSocketUrl))
        {
            KeepAliveInterval = TimeSpan.FromMilliseconds(100),
            KeepAliveTimeout = TimeSpan.FromMilliseconds(50)
        };

        // Act
        await Client.StartOrFailAsync(TestContext.Current.CancellationToken);
        await Task.Delay(250, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(Client.IsRunning);
        Assert.Equal(WebSocketState.Open, Client.NativeClient.State);
    }

    [Fact(Timeout = DefaultTimeoutMs)]
    public async Task KeepAlive_VeryLongInterval_ShouldBeConfigurable()
    {
        // Arrange
        var longInterval = TimeSpan.FromMinutes(5);
        Client = new ReactiveWebSocketClient(new Uri(Server.WebSocketUrl))
        {
            KeepAliveInterval = longInterval
        };

        // Act
        await Client.StartOrFailAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(longInterval, Client.KeepAliveInterval);
        Assert.Equal(longInterval, Client.NativeClient.Options.KeepAliveInterval);
    }

    [Fact(Timeout = DefaultTimeoutMs)]
    public async Task KeepAlive_TimeoutShorterThanInterval_ShouldBeAllowed()
    {
        // Arrange
        Client = new ReactiveWebSocketClient(new Uri(Server.WebSocketUrl))
        {
            KeepAliveInterval = TimeSpan.FromSeconds(10),
            KeepAliveTimeout = TimeSpan.FromSeconds(3)
        };

        // Act
        await Client.StartOrFailAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.True(Client.KeepAliveTimeout < Client.KeepAliveInterval);
        Assert.Equal(WebSocketState.Open, Client.NativeClient.State);
    }

    [Fact(Timeout = DefaultTimeoutMs)]
    public async Task KeepAlive_DuringMessageExchange_ShouldNotInterfere()
    {
        // Arrange
        var receivedMessages = new List<byte[]>();
        Server.OnBytesReceived += bytes => receivedMessages.Add(bytes);

        Client = new ReactiveWebSocketClient(new Uri(Server.WebSocketUrl))
        {
            KeepAliveInterval = TimeSpan.FromMilliseconds(200),
            KeepAliveTimeout = TimeSpan.FromMilliseconds(100)
        };

        await Client.StartOrFailAsync(TestContext.Current.CancellationToken);

        // Act
        for (var i = 0; i < 5; i++)
        {
            await Client.SendInstantAsync($"Message {i}", TestContext.Current.CancellationToken);
            await Task.Delay(50, TestContext.Current.CancellationToken);
        }

        await Task.Delay(100, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(5, receivedMessages.Count);
        Assert.True(Client.IsRunning);
        Assert.Equal(WebSocketState.Open, Client.NativeClient.State);
    }

    [Fact(Timeout = DefaultTimeoutMs)]
    public async Task KeepAlive_AfterServerDisconnect_ShouldTriggerReconnectWithSameSettings()
    {
        // Arrange
        var disconnections = new List<Disconnected>();
        var reconnections = new List<Connected>();

        Client = new ReactiveWebSocketClient(new Uri(Server.WebSocketUrl))
        {
            KeepAliveInterval = TimeSpan.FromSeconds(2),
            KeepAliveTimeout = TimeSpan.FromSeconds(1),
            IsReconnectionEnabled = true
        };

        Client.DisconnectionHappened.Subscribe(d => disconnections.Add(d));
        Client.ConnectionHappened.Subscribe(c => reconnections.Add(c));

        await Client.StartOrFailAsync(TestContext.Current.CancellationToken);
        var initialInterval = Client.NativeClient.Options.KeepAliveInterval;

        // Act
        await Server.DisconnectAllAsync();
        await Task.Delay(200, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(disconnections.Count > 0);
        Assert.True(Client.IsStarted);
        Assert.Equal(initialInterval, Client.NativeClient.Options.KeepAliveInterval);
    }

    [Theory(Timeout = DefaultTimeoutMs)]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(30)]
    [InlineData(60)]
    public async Task KeepAlive_DifferentIntervals_ShouldAllWork(int seconds)
    {
        // Arrange
        var interval = TimeSpan.FromSeconds(seconds);
        Client = new ReactiveWebSocketClient(new Uri(Server.WebSocketUrl))
        {
            KeepAliveInterval = interval
        };

        // Act
        await Client.StartOrFailAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(interval, Client.KeepAliveInterval);
        Assert.Equal(WebSocketState.Open, Client.NativeClient.State);
        Assert.True(Client.IsRunning);
    }

    [Fact(Timeout = DefaultTimeoutMs)]
    public async Task KeepAlive_SettingsBeforeStart_ShouldPersist()
    {
        // Arrange
        Client = new ReactiveWebSocketClient(new Uri(Server.WebSocketUrl))
        {
            KeepAliveInterval = TimeSpan.FromSeconds(7),
            KeepAliveTimeout = TimeSpan.FromSeconds(4)
        };

        var intervalBeforeStart = Client.KeepAliveInterval;
        var timeoutBeforeStart = Client.KeepAliveTimeout;

        // Act
        await Client.StartOrFailAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(intervalBeforeStart, Client.KeepAliveInterval);
        Assert.Equal(timeoutBeforeStart, Client.KeepAliveTimeout);
        Assert.Equal(intervalBeforeStart, Client.NativeClient.Options.KeepAliveInterval);
        Assert.Equal(timeoutBeforeStart, Client.NativeClient.Options.KeepAliveTimeout);
    }

    [Fact]
    public void KeepAlive_NegativeInterval_ShouldBeHandledByWebSocket()
    {
        // Arrange & Act
        Client = new ReactiveWebSocketClient(new Uri(Server.WebSocketUrl))
        {
            KeepAliveInterval = TimeSpan.FromSeconds(-1)
        };

        // Assert
        Assert.Equal(TimeSpan.FromSeconds(-1), Client.KeepAliveInterval);
    }

    [Fact(Timeout = DefaultTimeoutMs)]
    public async Task KeepAlive_InfiniteInterval_ShouldBeConfigurable()
    {
        // Arrange
        Client = new ReactiveWebSocketClient(new Uri(Server.WebSocketUrl))
        {
            KeepAliveInterval = Timeout.InfiniteTimeSpan
        };

        // Act
        await Client.StartOrFailAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(Timeout.InfiniteTimeSpan, Client.KeepAliveInterval);
        Assert.True(Client.IsRunning);
    }

    [Fact(Timeout = DefaultTimeoutMs)]
    public async Task KeepAlive_ConnectionStaysAliveWithoutActivity()
    {
        // Arrange
        var disconnections = new List<Disconnected>();

        Client = new ReactiveWebSocketClient(new Uri(Server.WebSocketUrl))
        {
            KeepAliveInterval = TimeSpan.FromMilliseconds(200),
            KeepAliveTimeout = TimeSpan.FromMilliseconds(100)
        };

        Client.DisconnectionHappened.Subscribe(d => disconnections.Add(d));

        // Act
        await Client.StartOrFailAsync(TestContext.Current.CancellationToken);
        await Task.Delay(600, TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(disconnections);
        Assert.True(Client.IsRunning);
        Assert.Equal(WebSocketState.Open, Client.NativeClient.State);
    }


    [Fact(Timeout = DefaultTimeoutMs)]
    public async Task KeepAlive_WithDisabledReconnection_ShouldStillMaintainConnection()
    {
        // Arrange
        var disconnections = new List<Disconnected>();

        Client = new ReactiveWebSocketClient(new Uri(Server.WebSocketUrl))
        {
            KeepAliveInterval = TimeSpan.FromMilliseconds(300),
            KeepAliveTimeout = TimeSpan.FromMilliseconds(150),
            IsReconnectionEnabled = false
        };

        Client.DisconnectionHappened.Subscribe(d => disconnections.Add(d));

        // Act
        await Client.StartOrFailAsync(TestContext.Current.CancellationToken);
        await Task.Delay(1000, TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(disconnections);
        Assert.True(Client.IsRunning);
        Assert.Equal(WebSocketState.Open, Client.NativeClient.State);
    }

    [Fact(Timeout = DefaultTimeoutMs)]
    public async Task KeepAlive_LongIdlePeriod_ShouldKeepConnectionAlive()
    {
        // Arrange
        var disconnections = new List<Disconnected>();

        Client = new ReactiveWebSocketClient(new Uri(Server.WebSocketUrl))
        {
            KeepAliveInterval = TimeSpan.FromMilliseconds(200),
            KeepAliveTimeout = TimeSpan.FromMilliseconds(100)
        };


        Client.DisconnectionHappened.Subscribe(d => disconnections.Add(d));

        // Act
        await Client.StartOrFailAsync(TestContext.Current.CancellationToken);
        await Task.Delay(1000, TestContext.Current.CancellationToken);

        var messageReceived = false;
        Client.MessageReceived.Subscribe(_ => messageReceived = true);

        await Server.SendToAllAsync("test");
        await Task.Delay(100, TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(disconnections);
        Assert.True(messageReceived);
        Assert.True(Client.IsRunning);
        Assert.Equal(WebSocketState.Open, Client.NativeClient.State);
    }

    [Fact(Timeout = DefaultTimeoutMs)]
    public async Task KeepAlive_ServerRespondsToClientMessages_ShouldResetKeepAliveTimer()
    {
        // Arrange
        var disconnections = new List<Disconnected>();

        Client = new ReactiveWebSocketClient(new Uri(Server.WebSocketUrl))
        {
            KeepAliveInterval = TimeSpan.FromMilliseconds(300),
            KeepAliveTimeout = TimeSpan.FromMilliseconds(150)
        };

        Client.DisconnectionHappened.Subscribe(d => disconnections.Add(d));

        // Act
        await Client.StartOrFailAsync(TestContext.Current.CancellationToken);

        for (var i = 0; i < 5; i++)
        {
            await Client.SendInstantAsync($"msg{i}", TestContext.Current.CancellationToken);
            await Task.Delay(100, TestContext.Current.CancellationToken);
        }

        // Assert
        Assert.Empty(disconnections);
        Assert.True(Client.IsRunning);
        Assert.Equal(WebSocketState.Open, Client.NativeClient.State);
    }

    [Fact(Timeout = DefaultTimeoutMs)]
    public async Task KeepAlive_ZeroInterval_DisablesKeepAlive_ConnectionStillWorks()
    {
        // Arrange
        var messageReceived = false;

        Client = new ReactiveWebSocketClient(new Uri(Server.WebSocketUrl))
        {
            KeepAliveInterval = TimeSpan.Zero,
            IsTextMessageConversionEnabled = true
        };

        Client.MessageReceived.Subscribe(_ => messageReceived = true);

        // Act
        await Client.StartOrFailAsync(TestContext.Current.CancellationToken);
        await Task.Delay(200, TestContext.Current.CancellationToken);
        await Server.SendToAllAsync("test");
        await Task.Delay(100, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(messageReceived);
        Assert.True(Client.IsRunning);
        Assert.Equal(WebSocketState.Open, Client.NativeClient.State);
    }

    [Fact(Timeout = DefaultTimeoutMs)]
    public async Task KeepAlive_MultipleClientsWithDifferentIntervals_ShouldWorkIndependently()
    {
        // Arrange
        var client2 = new ReactiveWebSocketClient(new Uri(Server.WebSocketUrl))
        {
            KeepAliveInterval = TimeSpan.FromMilliseconds(100),
            KeepAliveTimeout = TimeSpan.FromMilliseconds(50)
        };

        Client = new ReactiveWebSocketClient(new Uri(Server.WebSocketUrl))
        {
            KeepAliveInterval = TimeSpan.FromMilliseconds(400),
            KeepAliveTimeout = TimeSpan.FromMilliseconds(200)
        };

        try
        {
            // Act
            await Client.StartOrFailAsync(TestContext.Current.CancellationToken);
            await client2.StartOrFailAsync(TestContext.Current.CancellationToken);
            await Task.Delay(200, TestContext.Current.CancellationToken);

            // Assert
            Assert.True(Client.IsRunning);
            Assert.True(client2.IsRunning);
            Assert.Equal(WebSocketState.Open, Client.NativeClient.State);
            Assert.Equal(WebSocketState.Open, client2.NativeClient.State);
        }
        finally
        {
            await client2.DisposeAsync();
        }
    }
}