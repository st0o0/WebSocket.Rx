namespace WebSocket.Rx.Internal;

public static class ObjectExtensions
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
}