using System.Net;
using LanguageExt;

namespace WebSocket.Rx.Internal;

internal static class ObjectExtensions
{
    public static async Task Try<T>(this T value, Func<T, Task> func) where T : notnull
    {
        try
        {
            await func.Invoke(value);
        }
        catch (Exception ex)
        {
            _ = ex;
        }
    }

    public static void Try<T>(this T value, Action<T> action) where T : class
    {
        try
        {
            action.Invoke(value);
        }
        catch (Exception ex)
        {
            _ = ex;
        }
    }

    public static Either<Exception, TResult> Try<T, TResult>(this T value, Func<T, TResult> function) where T : class
    {
        try
        {
            return Either<Exception, TResult>.Right(function.Invoke(value));
        }
        catch (Exception ex)
        {
            return Either<Exception, TResult>.Left(ex);
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
}