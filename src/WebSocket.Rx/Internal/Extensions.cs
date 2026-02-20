using System.Net;

namespace WebSocket.Rx.Internal;

internal static class Extensions
{
    extension<T>(T value) where T : notnull
    {
        public async Task Try(Func<T, Task> func)
        {
            try
            {
                await func.Invoke(value).ConfigureAwait(false);
            }
            catch (Exception)
            {
                // noop
            }
        }

        public void Try(Action<T> action)
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
        var results = await Task.WhenAll(tasks).ConfigureAwait(false);
        return results.All(condition);
    }
}