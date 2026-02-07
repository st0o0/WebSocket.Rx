using System.Net;

namespace WebSocket.Rx.Internal;

internal static class Extensions
{
    public static async Task Try<T>(this T value, Func<T, Task> func) where T : notnull
    {
        try
        {
            await func.Invoke(value);
        }
        catch (Exception)
        {
            // noop
        }
    }

    public static void Try<T>(this T value, Action<T> action) where T : class
    {
        try
        {
            action.Invoke(value);
        }
        catch (Exception)
        {
            // noop
        }
    }

    public static Metadata GetMetadata(this HttpListenerContext ctx)
    {
        var address = ctx.Request.RemoteEndPoint.Address;
        var port = ctx.Request.RemoteEndPoint.Port;

        var idHeaderString = ctx.Request.Headers.Get(Headers.IdHeader);
        var id = Guid.TryParse(idHeaderString, out var guid) ? guid : Guid.NewGuid();
        return new Metadata(id, address, port);
    }

    public static async Task<bool> Async<TSource, TResult>(this IEnumerable<TSource> values,
        Func<TSource, CancellationToken, Task<TResult>> func, Func<TResult, bool> condition,
        CancellationToken cancellationToken = default)
    {
        var tasks = new List<Task<TResult>>();
        tasks.AddRange(values.Select(x => func(x, cancellationToken)));
        var results = await Task.WhenAll(tasks);
        return results.All(condition);
    }
}