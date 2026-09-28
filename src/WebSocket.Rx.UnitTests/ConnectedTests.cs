namespace WebSocket.Rx.UnitTests;

public class ConnectedTests
{
    [Fact]
    public void Equality_WithSameValues_ShouldBeEqual()
    {
        var connected1 = new Connected(ConnectReason.Initialized);
        var connected2 = new Connected(ConnectReason.Initialized);

        Assert.Multiple(() =>
        {
            Assert.Equal(connected1, connected2);
            Assert.True(connected1 == connected2);
        });
    }

    [Fact]
    public void Equality_WithDifferentValues_ShouldNotBeEqual()
    {
        var connected1 = new Connected(ConnectReason.Initialized);
        var connected2 = new Connected(ConnectReason.Reconnected);

        Assert.Multiple(() =>
        {
            Assert.NotEqual(connected1, connected2);
            Assert.True(connected1 != connected2);
        });
    }

    [Theory]
    [InlineData(ConnectReason.Undefined)]
    [InlineData(ConnectReason.Initialized)]
    [InlineData(ConnectReason.Reconnected)]
    public void Constructor_SetsReason(ConnectReason reason)
    {
        var connected = new Connected(reason);

        Assert.Equal(reason, connected.Reason);
    }

    [Fact]
    public void WithExpression_ChangesReason()
    {
        var original = new Connected(ConnectReason.Initialized);

        var copy = original with { Reason = ConnectReason.Reconnected };

        Assert.Multiple(() =>
        {
            Assert.Equal(ConnectReason.Reconnected, copy.Reason);
            Assert.Equal(ConnectReason.Initialized, original.Reason);
        });
    }
}
