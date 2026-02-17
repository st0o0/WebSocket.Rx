using System.Net.WebSockets;

namespace WebSocket.Rx.UnitTests;

public class DisconnectedTests
{
    private const int DefaultTimeoutMs = 5000;

    [Fact(Timeout = DefaultTimeoutMs)]
    public void Equality_WithSameValues_ShouldBeEqual()
    {
        // Arrange
        var error = new WebSocketException("Test");
        var disconnected1 = new Disconnected(DisconnectReason.Undefined, WebSocketCloseStatus.Empty, string.Empty,
            string.Empty, Exception: error);
        var disconnected2 = new Disconnected(DisconnectReason.Undefined, WebSocketCloseStatus.Empty, string.Empty,
            string.Empty, Exception: error);

        // Act & Assert
        Assert.Equal(disconnected1, disconnected2);
    }

    [Fact]
    public void Constructor_Minimal_SetsDefaults()
    {
        // Act
        var disconnected = new Disconnected(DisconnectReason.ClientInitiated);

        // Assert
        Assert.Equal(DisconnectReason.ClientInitiated, disconnected.Reason);
        Assert.Null(disconnected.CloseStatus);
        Assert.Null(disconnected.CloseStatusDescription);
        Assert.Null(disconnected.SubProtocol);
        Assert.Null(disconnected.Exception);
        Assert.False(disconnected.IsClosingCanceled);
        Assert.False(disconnected.IsReconnectionCanceled);
    }

    [Fact]
    public void Constructor_Full_SetsAllProperties()
    {
        // Arrange
        var exception = new WebSocketException();

        // Act
        var disconnected = new Disconnected(
            DisconnectReason.ClientInitiated,
            WebSocketCloseStatus.InternalServerError,
            "Server down",
            "my-protocol",
            exception);

        // Assert
        Assert.Equal(DisconnectReason.ClientInitiated, disconnected.Reason);
        Assert.Equal(WebSocketCloseStatus.InternalServerError, disconnected.CloseStatus);
        Assert.Equal("Server down", disconnected.CloseStatusDescription);
        Assert.Equal("my-protocol", disconnected.SubProtocol);
        Assert.Equal(exception, disconnected.Exception);
    }

    [Fact]
    public void CancelClosing_SetsFlag()
    {
        var disconnected = new Disconnected(DisconnectReason.ClientInitiated);

        disconnected.CancelClosing();

        Assert.True(disconnected.IsClosingCanceled);
        Assert.False(disconnected.IsReconnectionCanceled);
    }

    [Fact]
    public void CancelClosing_CanBeCalledMultipleTimes()
    {
        var disconnected = new Disconnected(DisconnectReason.ClientInitiated);

        disconnected.CancelClosing();
        disconnected.CancelClosing();

        Assert.True(disconnected.IsClosingCanceled);
    }

    [Fact]
    public void CancelReconnection_SetsFlag()
    {
        var disconnected = new Disconnected(DisconnectReason.ClientInitiated);

        disconnected.CancelReconnection();

        Assert.True(disconnected.IsReconnectionCanceled);
        Assert.False(disconnected.IsClosingCanceled);
    }

    [Fact]
    public void CancelReconnection_CanBeCalledMultipleTimes()
    {
        var disconnected = new Disconnected(DisconnectReason.ClientInitiated);

        disconnected.CancelReconnection();
        disconnected.CancelReconnection();

        Assert.True(disconnected.IsReconnectionCanceled);
    }

    [Fact]
    public void CancelClosing_And_CancelReconnection_AreIndependent()
    {
        var disconnected = new Disconnected(DisconnectReason.ClientInitiated);

        disconnected.CancelClosing();
        disconnected.CancelReconnection();

        Assert.True(disconnected.IsClosingCanceled);
        Assert.True(disconnected.IsReconnectionCanceled);
    }

    [Fact]
    public void CancelMethods_DoNotChangeConstructorValues()
    {
        var disconnected = new Disconnected(
            DisconnectReason.ClientInitiated,
            WebSocketCloseStatus.NormalClosure,
            "bye",
            "proto");

        disconnected.CancelClosing();
        disconnected.CancelReconnection();

        Assert.Equal(DisconnectReason.ClientInitiated, disconnected.Reason);
        Assert.Equal(WebSocketCloseStatus.NormalClosure, disconnected.CloseStatus);
        Assert.Equal("bye", disconnected.CloseStatusDescription);
        Assert.Equal("proto", disconnected.SubProtocol);
        Assert.Null(disconnected.Exception);
    }

    [Fact]
    public void Records_WithSameValues_AreEqual()
    {
        var a = new Disconnected(
            DisconnectReason.Undefined,
            WebSocketCloseStatus.NormalClosure,
            "desc",
            "proto");

        var b = new Disconnected(
            DisconnectReason.Undefined,
            WebSocketCloseStatus.NormalClosure,
            "desc",
            "proto");

        Assert.Equal(a, b);
        Assert.True(a == b);
    }

    [Fact]
    public void WithExpression_CopiesValues()
    {
        var original = new Disconnected(DisconnectReason.ClientInitiated);
        original.CancelClosing();
        original.CancelReconnection();

        var copy = original with { Reason = DisconnectReason.Undefined };

        Assert.Equal(DisconnectReason.Undefined, copy.Reason);

        Assert.True(copy.IsClosingCanceled);
        Assert.True(copy.IsReconnectionCanceled);
    }

    [Fact]
    public void Flags_DoAffect_RecordEquality()
    {
        var a = new Disconnected(DisconnectReason.ClientInitiated);
        var b = new Disconnected(DisconnectReason.ClientInitiated);

        a.CancelClosing();
        a.CancelReconnection();

        Assert.NotEqual(a, b);
    }
}