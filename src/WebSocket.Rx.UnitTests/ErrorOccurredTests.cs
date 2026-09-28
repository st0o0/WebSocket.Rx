using System.Net;

namespace WebSocket.Rx.UnitTests;

public class ErrorOccurredTests
{
    [Fact]
    public void Constructor_SetsProperties()
    {
        var exception = new InvalidOperationException("test");

        var error = new ErrorOccurred(ErrorSource.Send, exception);

        Assert.Multiple(() =>
        {
            Assert.Equal(ErrorSource.Send, error.Source);
            Assert.Equal(exception, error.Exception);
        });
    }

    [Theory]
    [InlineData(ErrorSource.Undefined)]
    [InlineData(ErrorSource.Connection)]
    [InlineData(ErrorSource.Reconnection)]
    [InlineData(ErrorSource.Disconnection)]
    [InlineData(ErrorSource.Send)]
    [InlineData(ErrorSource.SendLoop)]
    [InlineData(ErrorSource.ReceiveLoop)]
    [InlineData(ErrorSource.Dispose)]
    public void Constructor_AllErrorSources(ErrorSource source)
    {
        var error = new ErrorOccurred(source, new Exception());

        Assert.Equal(source, error.Source);
    }

    [Fact]
    public void Equality_WithSameValues_ShouldBeEqual()
    {
        var exception = new InvalidOperationException("test");
        var error1 = new ErrorOccurred(ErrorSource.Send, exception);
        var error2 = new ErrorOccurred(ErrorSource.Send, exception);

        Assert.Multiple(() =>
        {
            Assert.Equal(error1, error2);
            Assert.True(error1 == error2);
        });
    }

    [Fact]
    public void Equality_WithDifferentSource_ShouldNotBeEqual()
    {
        var exception = new InvalidOperationException("test");
        var error1 = new ErrorOccurred(ErrorSource.Send, exception);
        var error2 = new ErrorOccurred(ErrorSource.ReceiveLoop, exception);

        Assert.NotEqual(error1, error2);
    }

    [Fact]
    public void Equality_WithDifferentException_ShouldNotBeEqual()
    {
        var error1 = new ErrorOccurred(ErrorSource.Send, new InvalidOperationException("a"));
        var error2 = new ErrorOccurred(ErrorSource.Send, new InvalidOperationException("b"));

        Assert.NotEqual(error1, error2);
    }
}

public class ServerErrorOccurredTests
{
    [Fact]
    public void Constructor_SetsAllProperties()
    {
        var metadata = new Metadata(Guid.NewGuid(), IPAddress.Loopback, 8080);
        var exception = new InvalidOperationException("server error");

        var error = new ServerErrorOccurred(metadata, ErrorSource.ReceiveLoop, exception);

        Assert.Multiple(() =>
        {
            Assert.Equal(metadata, error.Metadata);
            Assert.Equal(ErrorSource.ReceiveLoop, error.Source);
            Assert.Equal(exception, error.Exception);
        });
    }

    [Fact]
    public void InheritsFromErrorOccurred()
    {
        var error = new ServerErrorOccurred(new Metadata(Guid.NewGuid()), ErrorSource.Send, new Exception("test"));

        ErrorOccurred baseError = error;

        Assert.Multiple(() =>
        {
            Assert.Equal(error.Source, baseError.Source);
            Assert.Equal(error.Exception, baseError.Exception);
        });
    }

    [Fact]
    public void Equality_WithSameValues_ShouldBeEqual()
    {
        var metadata = new Metadata(Guid.NewGuid());
        var exception = new Exception("test");
        var error1 = new ServerErrorOccurred(metadata, ErrorSource.Send, exception);
        var error2 = new ServerErrorOccurred(metadata, ErrorSource.Send, exception);

        Assert.Multiple(() =>
        {
            Assert.Equal(error1, error2);
            Assert.True(error1 == error2);
        });
    }

    [Fact]
    public void Equality_WithDifferentMetadata_ShouldNotBeEqual()
    {
        var exception = new Exception("test");
        var error1 = new ServerErrorOccurred(new Metadata(Guid.NewGuid()), ErrorSource.Send, exception);
        var error2 = new ServerErrorOccurred(new Metadata(Guid.NewGuid()), ErrorSource.Send, exception);

        Assert.NotEqual(error1, error2);
    }
}
