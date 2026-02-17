using R3;

namespace WebSocket.Rx.IntegrationTests.Internal;

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
        await using var registration = cts.Token.Register(() =>
        {
            var msg = $"Event {typeof(T).Name} not received within {timeout}ms";
            try
            {
                Output.WriteLine($"[TIMEOUT] {msg}");
            }
            catch
            {
                // Ignored if test is already finished
            }

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

    protected async Task WaitUntilAsync<T>(
        Observable<T> observable,
        Func<bool> condition,
        int? timeoutMs = null)
    {
        var timeout = timeoutMs ?? DefaultTimeoutMs;
        var tcs = new TaskCompletionSource<bool>();
        using var cts = new CancellationTokenSource(timeout);
        await using var registration = cts.Token.Register(() =>
        {
            var msg = $"Condition not met within {timeout}ms";
            try
            {
                Output.WriteLine($"[TIMEOUT] {msg}");
            }
            catch
            {
                // noop
            }

            tcs.TrySetException(new TimeoutException(msg));
        });

        if (condition()) return;

        // Fallback polling for robustness
        using var intervalSubscription = Observable.Interval(TimeSpan.FromMilliseconds(50)).Subscribe(_ =>
        {
            try
            {
                if (condition()) tcs.TrySetResult(true);
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
        });

        IDisposable? eventSubscription = null;
        try
        {
            eventSubscription = observable.Subscribe(_ =>
            {
                try
                {
                    if (condition()) tcs.TrySetResult(true);
                }
                catch (Exception ex)
                {
                    tcs.TrySetException(ex);
                }
            });
        }
        catch (ObjectDisposedException)
        {
            // noop
        }

        using (eventSubscription)
        {
            if (condition())
            {
                tcs.TrySetResult(true);
                return;
            }

            await tcs.Task;
        }
    }

    protected async Task WaitForConditionAsync(
        Func<bool> condition,
        TimeSpan? timeout = null,
        string? errorMessage = null)
    {
        timeout ??= TimeSpan.FromMilliseconds(DefaultTimeoutMs);
        var endTime = DateTime.UtcNow.Add(timeout.Value);
        var count = 0;

        while (!condition() && DateTime.UtcNow < endTime)
        {
            await Task.Delay(10);
            count++;
            if (count % 100 == 0) // Log every 1 second
            {
                Output.WriteLine($"Still waiting for condition... ({count * 10}ms elapsed)");
            }
        }

        if (!condition())
        {
            var msg = errorMessage ?? $"Condition was not met within {timeout.Value.TotalSeconds}s";
            Output.WriteLine($"[TIMEOUT] {msg}");
            throw new TimeoutException(msg);
        }
    }
}