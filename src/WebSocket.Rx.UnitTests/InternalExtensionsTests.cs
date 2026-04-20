using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using WebSocket.Rx.Internal;

namespace WebSocket.Rx.UnitTests;

public class InternalExtensionsTests
{
    private const int DefaultTimeoutMs = 10000;

    [Fact(Timeout = DefaultTimeoutMs)]
    public void Try_Action_ShouldExecuteAndSwallowExceptions()
    {
        var value = new object();
        var called = false;

        value.Try(_ => called = true);

        Action<object> action = _ => throw new InvalidOperationException("boom");
        var exception = Record.Exception(() => value.Try(action));

        Assert.True(called);
        Assert.Null(exception);
    }

    [Fact(Timeout = DefaultTimeoutMs)]
    public async Task Try_Async_ShouldExecuteAndSwallowExceptions()
    {
        var value = new object();
        var called = false;

        await value.Try(async _ =>
        {
            called = true;
            await Task.Yield();
        });

        var exception = await Record.ExceptionAsync(() => value.Try(async _ =>
        {
            await Task.Yield();
            throw new InvalidOperationException("boom");
        }));

        Assert.True(called);
        Assert.Null(exception);
    }

    [Fact(Timeout = DefaultTimeoutMs)]
    public async Task Async_AllResultsMeetCondition_ReturnsTrue()
    {
        var values = new[] { 1, 2, 3 };
        var seen = new List<int>();
        var sync = new object();

        var result = await values.Async(async (value, _) =>
        {
            await Task.Delay(10, TestContext.Current.CancellationToken);
            lock (sync) seen.Add(value);
            return value * 2;
        }, output => output > 0, TestContext.Current.CancellationToken);

        Assert.True(result);
        Assert.Equal(values.Length, seen.Count);
    }

    [Fact(Timeout = DefaultTimeoutMs)]
    public async Task Async_ConditionFails_ReturnsFalse()
    {
        var values = new[] { 1, 2, 3 };

        var result = await values.Async((value, _) => Task.FromResult(value - 2), output => output > 0,
            TestContext.Current.CancellationToken);

        Assert.False(result);
    }

    [Fact(Timeout = DefaultTimeoutMs)]
    public async Task GetMetadata_WithHeader_UsesProvidedId()
    {
        var expectedId = Guid.NewGuid();
        var (metadata, remote) = await GetMetadataAsync(request =>
        {
            request.Headers.Add(Headers.IdHeader, expectedId.ToString());
        });

        Assert.Equal(expectedId, metadata.Id);
        Assert.Equal(remote.Address, metadata.Address);
        Assert.Equal(remote.Port, metadata.Port);
    }

    [Fact(Timeout = DefaultTimeoutMs)]
    public async Task GetMetadata_WithoutHeader_GeneratesNewId()
    {
        var (metadata, remote) = await GetMetadataAsync();

        Assert.NotEqual(Guid.Empty, metadata.Id);
        Assert.Equal(remote.Address, metadata.Address);
        Assert.Equal(remote.Port, metadata.Port);
    }

    private static async Task<(Metadata metadata, IPEndPoint remote)> GetMetadataAsync(
        Action<HttpRequestMessage>? configureRequest = null)
    {
        var port = GetAvailablePort();
        var prefix = $"http://localhost:{port}/";

        using var listener = new HttpListener();
        listener.Prefixes.Add(prefix);
        listener.Start();

        var contextTask = listener.GetContextAsync();

        using var client = new HttpClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, prefix);
        configureRequest?.Invoke(request);

        var responseTask = client.SendAsync(request, TestContext.Current.CancellationToken);
        var context = await contextTask.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        var metadata = context.GetMetadata();
        var remote = context.Request.RemoteEndPoint ?? new IPEndPoint(IPAddress.None, 0);

        context.Response.StatusCode = 200;
        context.Response.Close();
        await responseTask;

        return (metadata, remote);
    }

    private static int GetAvailablePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
