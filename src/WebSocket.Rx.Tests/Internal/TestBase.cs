using R3;

namespace WebSocket.Rx.Tests.Internal;

public abstract class TestBase(ITestOutputHelper output)
{
    protected readonly ITestOutputHelper Output = output;
    protected const int DefaultTimeoutMs = 30000;

    protected async Task<T> WaitForEventAsync<T>(
        Observable<T> observable,
        Func<T, bool>? predicate = null,
        int? timeoutMs = null)
    {
        var timeout = timeoutMs ?? DefaultTimeoutMs;
        var tcs = new TaskCompletionSource<T>();
        using var cts = new CancellationTokenSource(timeout);
        using var registration = cts.Token.Register(() =>
        {
            var msg = $"Event {typeof(T).Name} not received within {timeout}ms";
            Output.WriteLine($"[TIMEOUT] {msg}");
            tcs.TrySetException(new TimeoutException(msg));
        });

        using var subscription = observable.Subscribe(value =>
        {
            if (predicate == null || predicate(value))
            {
                tcs.TrySetResult(value);
            }
        });

        return await tcs.Task;
    }

    protected async Task WaitForConditionAsync(
        Func<bool> condition,
        TimeSpan? timeout = null,
        string? errorMessage = null)
    {
        timeout ??= TimeSpan.FromMilliseconds(DefaultTimeoutMs);
        var endTime = DateTime.UtcNow.Add(timeout.Value);

        while (!condition() && DateTime.UtcNow < endTime)
        {
            await Task.Delay(10);
        }

        if (!condition())
        {
            var msg = errorMessage ?? $"Condition was not met within {timeout.Value.TotalSeconds}s";
            Output.WriteLine($"[TIMEOUT] {msg}");
            throw new TimeoutException(msg);
        }
    }
}
