namespace WebSocket.Rx.Internal;

internal sealed class AsyncLock
{
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    public bool IsLocked => _semaphore.CurrentCount == 0;

    public IDisposable Lock()
    {
        _semaphore.Wait();
        return new Releaser(_semaphore);
    }

    public Task<IDisposable> LockAsync(CancellationToken cancellationToken = default)
    {
        var waitTask = _semaphore.WaitAsync(cancellationToken);
        return waitTask.IsCompletedSuccessfully
            ? Task.FromResult<IDisposable>(new Releaser(_semaphore))
            : waitTask.ContinueWith<IDisposable>(_ => new Releaser(_semaphore),
                cancellationToken,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
    }

    private class Releaser(SemaphoreSlim semaphore) : IDisposable
    {
        private bool _released;

        public void Dispose()
        {
            if (_released) return;
            _released = true;
            semaphore.Release();
        }
    }
}