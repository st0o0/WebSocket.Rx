using WebSocket.Rx.Internal;

namespace WebSocket.Rx.UnitTests;

public class AsyncLockTests
{
    [Fact]
    public void Constructor_CreatesValidInstance()
    {
        // Act
        var asyncLock = new AsyncLock();

        // Assert
        Assert.Multiple(() =>
        {
            Assert.False(asyncLock.IsLocked);
            Assert.NotNull(asyncLock);
        });
    }

    [Fact]
    public void Lock_Synchronous_AcquiresAndTracksState()
    {
        // Arrange
        var asyncLock = new AsyncLock();

        // Act
        using var releaser = asyncLock.Lock();

        // Assert
        Assert.True(asyncLock.IsLocked);
    }

    [Fact]
    public void Lock_Synchronous_ReleasesAfterDispose()
    {
        var asyncLock = new AsyncLock();

        using (asyncLock.Lock())
        {
            Assert.True(asyncLock.IsLocked);
        }

        Assert.False(asyncLock.IsLocked);
    }

    [Fact]
    public void LockAsync_FastPath_ReturnsImmediately()
    {
        var asyncLock = new AsyncLock();

        var lockTask = asyncLock.LockAsync(TestContext.Current.CancellationToken);

        Assert.True(lockTask.IsCompletedSuccessfully);
#pragma warning disable xUnit1031
        using var releaser = lockTask.Result;
#pragma warning restore xUnit1031
        Assert.True(asyncLock.IsLocked);
    }

    [Fact(Timeout = 10000)]
    public async Task Lock_NonReentrantBehavior()
    {
        var asyncLock = new AsyncLock();

        // ReSharper disable once MethodHasAsyncOverload
        using var first = asyncLock.Lock();
        Assert.True(asyncLock.IsLocked);

        var secondTask = Task.Run(() => asyncLock.Lock(), TestContext.Current.CancellationToken);

        Assert.False(secondTask.IsCompleted);

        // ReSharper disable once DisposeOnUsingVariable
        first.Dispose();
        _ = await Task.Run(() => secondTask.Result, TestContext.Current.CancellationToken);
    }

    [Fact(Timeout = 10000)]
    public async Task LockAsync_SlowPath_WaitsAndAcquires()
    {
        var asyncLock = new AsyncLock();
        var tcs = new TaskCompletionSource<bool>();

        _ = Task.Run(async () =>
        {
            using var _ = asyncLock.Lock();
            await tcs.Task;
        }, TestContext.Current.CancellationToken);
        await Task.Delay(50, TestContext.Current.CancellationToken);

        var lockTask = asyncLock.LockAsync(TestContext.Current.CancellationToken);
        Assert.False(lockTask.IsCompleted);

        tcs.SetResult(true);
        using var releaser = await lockTask;
        Assert.True(asyncLock.IsLocked);
    }

    [Fact(Timeout = 10000)]
    public async Task LockAsync_ContinueWithPath_ExecutesCorrectly()
    {
        // Arrange
        var asyncLock = new AsyncLock();

        using (asyncLock.Lock())
        {
            await Task.Yield();
        }

        var lockTask = asyncLock.LockAsync(TestContext.Current.CancellationToken);
        using var releaser = await lockTask;

        Assert.True(asyncLock.IsLocked);
    }

    [Fact(Timeout = 10000)]
    public async Task MultipleConcurrentLocks_SerializesCorrectly()
    {
        var asyncLock = new AsyncLock();
        var order = new List<int>();
        const int taskCount = 10;

        var tasks = Enumerable.Range(0, taskCount)
            .Select(i => Task.Run(async () =>
            {
                using var _ = await asyncLock.LockAsync(TestContext.Current.CancellationToken);
                lock (order) order.Add(i);
            })).ToArray();

        await Task.WhenAll(tasks);

        Assert.Multiple(() =>
        {
            Assert.Equal(taskCount, order.Count);
            Assert.Equal(taskCount, order.Distinct().Count());
        });
    }

    [Fact(Timeout = 30000)]
    public async Task MultipleConcurrentLocks_NoOverlaps()
    {
        var asyncLock = new AsyncLock();
        var timings = new List<(DateTime Start, DateTime End)>();
        var timingsLock = new object();

        var tasks = Enumerable.Range(0, 5).Select(_ => Task.Run(async () =>
        {
            using var t = await asyncLock.LockAsync(TestContext.Current.CancellationToken);
            var start = DateTime.UtcNow;
            await Task.Delay(1, TestContext.Current.CancellationToken);
            var end = DateTime.UtcNow;
            lock (timingsLock)
            {
                timings.Add((start, end));
            }
        })).ToArray();

        await Task.WhenAll(tasks);

        var sorted = timings.OrderBy(t => t.Start).ToList();
        for (var i = 0; i < sorted.Count - 1; i++)
        {
            Assert.True(sorted[i].End < sorted[i + 1].Start,
                $"Overlap at index {i}: {sorted[i].End:O} >= {sorted[i + 1].Start:O}");
        }
    }


    [Fact]
    public void LockAndLockAsync_MixWorks()
    {
        var asyncLock = new AsyncLock();

        using (asyncLock.Lock())
        {
            Assert.True(asyncLock.IsLocked);

            var asyncTask = asyncLock.LockAsync(TestContext.Current.CancellationToken);
            Assert.False(asyncTask.IsCompleted);
        }

        Assert.True(asyncLock.IsLocked);
    }

    [Fact(Timeout = 10000)]
    public async Task Releaser_MultipleDisposeCalls_Idempotent()
    {
        var asyncLock = new AsyncLock();
        var releaser = await asyncLock.LockAsync(TestContext.Current.CancellationToken);

        releaser.Dispose();
        Assert.False(asyncLock.IsLocked);

        releaser.Dispose();
        Assert.False(asyncLock.IsLocked);
    }

    [Fact]
    public async Task ExceptionInCriticalSection_ReleasesLock()
    {
        // Arrange
        var asyncLock = new AsyncLock();

        // Assert
        await Assert.ThrowsAsync<InvalidOperationException>(TestMethod);
        Assert.False(asyncLock.IsLocked);
        return;

        // Act
        async Task TestMethod()
        {
            using var releaser = await asyncLock.LockAsync();
            throw new InvalidOperationException("Test exception");
        }
    }

    [Fact(Timeout = 10000)]
    public async Task IsLocked_ReflectsRealTimeState()
    {
        var asyncLock = new AsyncLock();

        Assert.False(asyncLock.IsLocked);

        using var releaser = await asyncLock.LockAsync(TestContext.Current.CancellationToken);
        Assert.True(asyncLock.IsLocked);
    }

    [Fact(Timeout = 10000)]
    public async Task StressTest_100ConcurrentLocks()
    {
        // Arrange
        var asyncLock = new AsyncLock();
        var tasks = Enumerable.Range(0, 100).Select(_ => Task.Run(async () =>
        {
            using var t = await asyncLock.LockAsync(TestContext.Current.CancellationToken);
        })).ToArray();

        // Act
        await Task.WhenAll(tasks);

        // Assert
        Assert.False(asyncLock.IsLocked);
    }

    [Fact]
    public async Task LockAsync_WithAlreadyCancelledToken_ShouldThrow()
    {
        var asyncLock = new AsyncLock();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await asyncLock.LockAsync(cts.Token));
    }

    [Theory(Timeout = 10000)]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(10)]
    public async Task RepeatedLockUnlock_CorrectState(int iterations)
    {
        var asyncLock = new AsyncLock();

        for (var i = 0; i < iterations; i++)
        {
            Assert.False(asyncLock.IsLocked);
            var releaser = await asyncLock.LockAsync(TestContext.Current.CancellationToken);

            try
            {
                Assert.True(asyncLock.IsLocked);
            }
            finally
            {
                releaser.Dispose();
            }

            Assert.False(asyncLock.IsLocked);
        }
    }
}