using System.Net.WebSockets;
using R3;
using WebSocket.Rx.Tests.Internal;

namespace WebSocket.Rx.Tests;

public class KeepAliveTests : IAsyncLifetime
{
    private WebSocketTestServer _server = null!;
    private ReactiveWebSocketClient _client = null!;

    public async ValueTask InitializeAsync()
    {
        _server = new WebSocketTestServer();
        await _server.StartAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await _client.DisposeAsync();
        await _server.DisposeAsync();
    }

    [Fact]
    public async Task KeepAliveInterval_ShouldBeConfigurable()
    {
        // Arrange
        var keepAliveInterval = TimeSpan.FromSeconds(5);
        _client = new ReactiveWebSocketClient(new Uri(_server.WebSocketUrl))
        {
            KeepAliveInterval = keepAliveInterval
        };

        // Act
        await _client.StartOrFailAsync();

        // Assert
        Assert.Equal(keepAliveInterval, _client.KeepAliveInterval);
        Assert.Equal(keepAliveInterval, _client.NativeClient.Options.KeepAliveInterval);
    }

    [Fact]
    public async Task KeepAliveTimeout_ShouldBeConfigurable()
    {
        // Arrange
        var keepAliveTimeout = TimeSpan.FromSeconds(15);
        _client = new ReactiveWebSocketClient(new Uri(_server.WebSocketUrl))
        {
            KeepAliveTimeout = keepAliveTimeout
        };

        // Act
        await _client.StartOrFailAsync();

        // Assert
        Assert.Equal(keepAliveTimeout, _client.KeepAliveTimeout);
        Assert.Equal(keepAliveTimeout, _client.NativeClient.Options.KeepAliveTimeout);
    }

    [Fact]
    public async Task KeepAlive_ShouldApplyDefaultValues()
    {
        // Arrange
        _client = new ReactiveWebSocketClient(new Uri(_server.WebSocketUrl));

        // Act
        await _client.StartOrFailAsync();

        // Assert
        Assert.Equal(TimeSpan.FromSeconds(30), _client.KeepAliveInterval);
        Assert.Equal(TimeSpan.FromSeconds(10), _client.KeepAliveTimeout);
        Assert.Equal(TimeSpan.FromSeconds(30), _client.NativeClient.Options.KeepAliveInterval);
        Assert.Equal(TimeSpan.FromSeconds(10), _client.NativeClient.Options.KeepAliveTimeout);
    }

    [Fact]
    public async Task KeepAlive_ShouldBeAppliedBeforeConnection()
    {
        // Arrange
        var keepAliveInterval = TimeSpan.FromSeconds(2);
        var keepAliveTimeout = TimeSpan.FromSeconds(5);

        _client = new ReactiveWebSocketClient(new Uri(_server.WebSocketUrl))
        {
            KeepAliveInterval = keepAliveInterval,
            KeepAliveTimeout = keepAliveTimeout
        };

        // Act
        await _client.StartOrFailAsync();

        // Assert
        Assert.True(_client.IsStarted);
        Assert.True(_client.IsRunning);
        Assert.Equal(keepAliveInterval, _client.NativeClient.Options.KeepAliveInterval);
        Assert.Equal(keepAliveTimeout, _client.NativeClient.Options.KeepAliveTimeout);
    }

    [Fact]
    public async Task KeepAlive_SettingsChangedAfterConnection_ShouldNotAffectCurrentConnection()
    {
        // Arrange
        var initialInterval = TimeSpan.FromSeconds(30);
        _client = new ReactiveWebSocketClient(new Uri(_server.WebSocketUrl))
        {
            KeepAliveInterval = initialInterval
        };

        await _client.StartOrFailAsync();
        var connectedInterval = _client.NativeClient.Options.KeepAliveInterval;

        // Act
        _client.KeepAliveInterval = TimeSpan.FromSeconds(5);

        // Assert
        Assert.Equal(initialInterval, connectedInterval);
        Assert.Equal(TimeSpan.FromSeconds(5), _client.KeepAliveInterval);
        Assert.Equal(initialInterval, _client.NativeClient.Options.KeepAliveInterval);
    }

    [Fact]
    public async Task KeepAlive_AfterReconnect_ShouldApplyNewSettings()
    {
        // Arrange
        _client = new ReactiveWebSocketClient(new Uri(_server.WebSocketUrl))
        {
            KeepAliveInterval = TimeSpan.FromSeconds(30),
            KeepAliveTimeout = TimeSpan.FromSeconds(10)
        };

        await _client.StartOrFailAsync();

        _client.KeepAliveInterval = TimeSpan.FromSeconds(5);
        _client.KeepAliveTimeout = TimeSpan.FromSeconds(3);

        // Act
        await _client.ReconnectOrFailAsync();

        // Assert
        Assert.Equal(TimeSpan.FromSeconds(5), _client.NativeClient.Options.KeepAliveInterval);
        Assert.Equal(TimeSpan.FromSeconds(3), _client.NativeClient.Options.KeepAliveTimeout);
    }

    [Fact]
    public async Task KeepAlive_WithZeroInterval_ShouldDisableKeepAlive()
    {
        // Arrange
        _client = new ReactiveWebSocketClient(new Uri(_server.WebSocketUrl))
        {
            KeepAliveInterval = TimeSpan.Zero
        };

        // Act
        await _client.StartOrFailAsync();

        // Assert
        Assert.Equal(TimeSpan.Zero, _client.KeepAliveInterval);
        Assert.Equal(TimeSpan.Zero, _client.NativeClient.Options.KeepAliveInterval);
    }

    [Fact]
    public async Task KeepAlive_ConnectionShouldStayAliveWithinInterval()
    {
        // Arrange
        var receivedMessages = new List<ReceivedMessage>();
        _client = new ReactiveWebSocketClient(new Uri(_server.WebSocketUrl))
        {
            KeepAliveInterval = TimeSpan.FromMilliseconds(500),
            IsTextMessageConversionEnabled = true
        };

        _client.MessageReceived.Subscribe(msg => receivedMessages.Add(msg));

        // Act
        await _client.StartOrFailAsync();
        await Task.Delay(100);
        await _server.SendToAllAsync("test");
        await Task.Delay(100);

        // Assert
        Assert.True(_client.IsRunning);
        Assert.Equal(WebSocketState.Open, _client.NativeClient.State);
        Assert.Single(receivedMessages);
        Assert.Equal("test", receivedMessages[0].Text);
    }

    [Fact]
    public async Task KeepAlive_MultipleReconnects_ShouldMaintainSettings()
    {
        // Arrange
        var keepAliveInterval = TimeSpan.FromSeconds(3);
        var keepAliveTimeout = TimeSpan.FromSeconds(2);

        _client = new ReactiveWebSocketClient(new Uri(_server.WebSocketUrl))
        {
            KeepAliveInterval = keepAliveInterval,
            KeepAliveTimeout = keepAliveTimeout
        };

        await _client.StartOrFailAsync();

        for (var i = 0; i < 3; i++)
        {
            await _client.ReconnectOrFailAsync();

            Assert.Equal(keepAliveInterval, _client.NativeClient.Options.KeepAliveInterval);
            Assert.Equal(keepAliveTimeout, _client.NativeClient.Options.KeepAliveTimeout);
        }
    }

    [Fact]
    public async Task KeepAlive_VeryShortInterval_ShouldStillWork()
    {
        // Arrange
        _client = new ReactiveWebSocketClient(new Uri(_server.WebSocketUrl))
        {
            KeepAliveInterval = TimeSpan.FromMilliseconds(100),
            KeepAliveTimeout = TimeSpan.FromMilliseconds(50)
        };

        // Act
        await _client.StartOrFailAsync();
        await Task.Delay(250);

        // Assert
        Assert.True(_client.IsRunning);
        Assert.Equal(WebSocketState.Open, _client.NativeClient.State);
    }

    [Fact]
    public async Task KeepAlive_VeryLongInterval_ShouldBeConfigurable()
    {
        // Arrange
        var longInterval = TimeSpan.FromMinutes(5);
        _client = new ReactiveWebSocketClient(new Uri(_server.WebSocketUrl))
        {
            KeepAliveInterval = longInterval
        };

        // Act
        await _client.StartOrFailAsync();

        // Assert
        Assert.Equal(longInterval, _client.KeepAliveInterval);
        Assert.Equal(longInterval, _client.NativeClient.Options.KeepAliveInterval);
    }

    [Fact]
    public async Task KeepAlive_TimeoutShorterThanInterval_ShouldBeAllowed()
    {
        // Arrange
        _client = new ReactiveWebSocketClient(new Uri(_server.WebSocketUrl))
        {
            KeepAliveInterval = TimeSpan.FromSeconds(10),
            KeepAliveTimeout = TimeSpan.FromSeconds(3)
        };

        // Act
        await _client.StartOrFailAsync();

        // Assert
        Assert.True(_client.KeepAliveTimeout < _client.KeepAliveInterval);
        Assert.Equal(WebSocketState.Open, _client.NativeClient.State);
    }

    [Fact]
    public async Task KeepAlive_DuringMessageExchange_ShouldNotInterfere()
    {
        // Arrange
        var receivedMessages = new List<byte[]>();
        _server.OnBytesReceived += bytes => receivedMessages.Add(bytes);

        _client = new ReactiveWebSocketClient(new Uri(_server.WebSocketUrl))
        {
            KeepAliveInterval = TimeSpan.FromMilliseconds(200),
            KeepAliveTimeout = TimeSpan.FromMilliseconds(100)
        };

        await _client.StartOrFailAsync();

        // Act
        for (var i = 0; i < 5; i++)
        {
            await _client.SendInstantAsync($"Message {i}");
            await Task.Delay(50);
        }

        await Task.Delay(100);

        // Assert
        Assert.Equal(5, receivedMessages.Count);
        Assert.True(_client.IsRunning);
        Assert.Equal(WebSocketState.Open, _client.NativeClient.State);
    }

    [Fact]
    public async Task KeepAlive_AfterServerDisconnect_ShouldTriggerReconnectWithSameSettings()
    {
        // Arrange
        var disconnections = new List<Disconnected>();
        var reconnections = new List<Connected>();

        _client = new ReactiveWebSocketClient(new Uri(_server.WebSocketUrl))
        {
            KeepAliveInterval = TimeSpan.FromSeconds(2),
            KeepAliveTimeout = TimeSpan.FromSeconds(1),
            IsReconnectionEnabled = true
        };

        _client.DisconnectionHappened.Subscribe(d => disconnections.Add(d));
        _client.ConnectionHappened.Subscribe(c => reconnections.Add(c));

        await _client.StartOrFailAsync();
        var initialInterval = _client.NativeClient.Options.KeepAliveInterval;

        // Act
        await _server.DisconnectAllAsync();
        await Task.Delay(200);

        // Assert
        Assert.True(disconnections.Count > 0);
        Assert.True(_client.IsStarted);
        Assert.Equal(initialInterval, _client.NativeClient.Options.KeepAliveInterval);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(30)]
    [InlineData(60)]
    public async Task KeepAlive_DifferentIntervals_ShouldAllWork(int seconds)
    {
        // Arrange
        var interval = TimeSpan.FromSeconds(seconds);
        _client = new ReactiveWebSocketClient(new Uri(_server.WebSocketUrl))
        {
            KeepAliveInterval = interval
        };

        // Act
        await _client.StartOrFailAsync();

        // Assert
        Assert.Equal(interval, _client.KeepAliveInterval);
        Assert.Equal(WebSocketState.Open, _client.NativeClient.State);
        Assert.True(_client.IsRunning);
    }

    [Fact]
    public async Task KeepAlive_SettingsBeforeStart_ShouldPersist()
    {
        // Arrange
        _client = new ReactiveWebSocketClient(new Uri(_server.WebSocketUrl))
        {
            KeepAliveInterval = TimeSpan.FromSeconds(7),
            KeepAliveTimeout = TimeSpan.FromSeconds(4)
        };

        var intervalBeforeStart = _client.KeepAliveInterval;
        var timeoutBeforeStart = _client.KeepAliveTimeout;

        // Act
        await _client.StartOrFailAsync();

        // Assert
        Assert.Equal(intervalBeforeStart, _client.KeepAliveInterval);
        Assert.Equal(timeoutBeforeStart, _client.KeepAliveTimeout);
        Assert.Equal(intervalBeforeStart, _client.NativeClient.Options.KeepAliveInterval);
        Assert.Equal(timeoutBeforeStart, _client.NativeClient.Options.KeepAliveTimeout);
    }

    [Fact]
    public async Task KeepAlive_NegativeInterval_ShouldBeHandledByWebSocket()
    {
        // Arrange & Act
        _client = new ReactiveWebSocketClient(new Uri(_server.WebSocketUrl))
        {
            KeepAliveInterval = TimeSpan.FromSeconds(-1)
        };

        // Assert
        Assert.Equal(TimeSpan.FromSeconds(-1), _client.KeepAliveInterval);
    }

    [Fact]
    public async Task KeepAlive_InfiniteInterval_ShouldBeConfigurable()
    {
        // Arrange
        _client = new ReactiveWebSocketClient(new Uri(_server.WebSocketUrl))
        {
            KeepAliveInterval = Timeout.InfiniteTimeSpan
        };

        // Act
        await _client.StartOrFailAsync();

        // Assert
        Assert.Equal(Timeout.InfiniteTimeSpan, _client.KeepAliveInterval);
        Assert.True(_client.IsRunning);
    }

    [Fact]
    public async Task KeepAlive_ConnectionStaysAliveWithoutActivity()
    {
        // Arrange
        var disconnections = new List<Disconnected>();

        _client = new ReactiveWebSocketClient(new Uri(_server.WebSocketUrl))
        {
            KeepAliveInterval = TimeSpan.FromMilliseconds(200),
            KeepAliveTimeout = TimeSpan.FromMilliseconds(100)
        };

        _client.DisconnectionHappened.Subscribe(d => disconnections.Add(d));

        // Act
        await _client.StartOrFailAsync();
        await Task.Delay(600);

        // Assert
        Assert.Empty(disconnections);
        Assert.True(_client.IsRunning);
        Assert.Equal(WebSocketState.Open, _client.NativeClient.State);
    }


    [Fact]
    public async Task KeepAlive_WithDisabledReconnection_ShouldStillMaintainConnection()
    {
        // Arrange
        var disconnections = new List<Disconnected>();

        _client = new ReactiveWebSocketClient(new Uri(_server.WebSocketUrl))
        {
            KeepAliveInterval = TimeSpan.FromMilliseconds(300),
            KeepAliveTimeout = TimeSpan.FromMilliseconds(150),
            IsReconnectionEnabled = false
        };

        _client.DisconnectionHappened.Subscribe(d => disconnections.Add(d));

        // Act
        await _client.StartOrFailAsync();
        await Task.Delay(1000);

        // Assert
        Assert.Empty(disconnections);
        Assert.True(_client.IsRunning);
        Assert.Equal(WebSocketState.Open, _client.NativeClient.State);
    }

    [Fact]
    public async Task KeepAlive_LongIdlePeriod_ShouldKeepConnectionAlive()
    {
        // Arrange
        var disconnections = new List<Disconnected>();

        _client = new ReactiveWebSocketClient(new Uri(_server.WebSocketUrl))
        {
            KeepAliveInterval = TimeSpan.FromMilliseconds(200),
            KeepAliveTimeout = TimeSpan.FromMilliseconds(100)
        };


        _client.DisconnectionHappened.Subscribe(d => disconnections.Add(d));

        // Act
        await _client.StartOrFailAsync();
        await Task.Delay(1000);

        var messageReceived = false;
        _client.MessageReceived.Subscribe(_ => messageReceived = true);

        await _server.SendToAllAsync("test");
        await Task.Delay(100);

        // Assert
        Assert.Empty(disconnections);
        Assert.True(messageReceived);
        Assert.True(_client.IsRunning);
        Assert.Equal(WebSocketState.Open, _client.NativeClient.State);
    }

    [Fact]
    public async Task KeepAlive_ServerRespondsToClientMessages_ShouldResetKeepAliveTimer()
    {
        // Arrange
        var disconnections = new List<Disconnected>();

        _client = new ReactiveWebSocketClient(new Uri(_server.WebSocketUrl))
        {
            KeepAliveInterval = TimeSpan.FromMilliseconds(300),
            KeepAliveTimeout = TimeSpan.FromMilliseconds(150)
        };

        _client.DisconnectionHappened.Subscribe(d => disconnections.Add(d));

        // Act
        await _client.StartOrFailAsync();

        for (var i = 0; i < 5; i++)
        {
            await _client.SendInstantAsync($"msg{i}");
            await Task.Delay(100);
        }

        // Assert
        Assert.Empty(disconnections);
        Assert.True(_client.IsRunning);
        Assert.Equal(WebSocketState.Open, _client.NativeClient.State);
    }

    [Fact]
    public async Task KeepAlive_ZeroInterval_DisablesKeepAlive_ConnectionStillWorks()
    {
        // Arrange
        var messageReceived = false;

        _client = new ReactiveWebSocketClient(new Uri(_server.WebSocketUrl))
        {
            KeepAliveInterval = TimeSpan.Zero,
            IsTextMessageConversionEnabled = true
        };

        _client.MessageReceived.Subscribe(_ => messageReceived = true);

        // Act
        await _client.StartOrFailAsync();
        await Task.Delay(200);
        await _server.SendToAllAsync("test");
        await Task.Delay(100);

        // Assert
        Assert.True(messageReceived);
        Assert.True(_client.IsRunning);
        Assert.Equal(WebSocketState.Open, _client.NativeClient.State);
    }

    [Fact]
    public async Task KeepAlive_MultipleClientsWithDifferentIntervals_ShouldWorkIndependently()
    {
        // Arrange
        var client2 = new ReactiveWebSocketClient(new Uri(_server.WebSocketUrl))
        {
            KeepAliveInterval = TimeSpan.FromMilliseconds(100),
            KeepAliveTimeout = TimeSpan.FromMilliseconds(50)
        };

        _client = new ReactiveWebSocketClient(new Uri(_server.WebSocketUrl))
        {
            KeepAliveInterval = TimeSpan.FromMilliseconds(400),
            KeepAliveTimeout = TimeSpan.FromMilliseconds(200)
        };

        try
        {
            // Act
            await _client.StartOrFailAsync();
            await client2.StartOrFailAsync();
            await Task.Delay(500);

            // Assert
            Assert.True(_client.IsRunning);
            Assert.True(client2.IsRunning);
            Assert.Equal(WebSocketState.Open, _client.NativeClient.State);
            Assert.Equal(WebSocketState.Open, client2.NativeClient.State);
        }
        finally
        {
            client2.Dispose();
        }
    }
}