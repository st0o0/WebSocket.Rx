using System;
using System.Buffers;
using System.Net;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using R3;

namespace WebSocket.Rx.Benchmarks;

// ---------------------------------------------------------------------------
// Shared infrastructure – server + client are started once per benchmark class
// ---------------------------------------------------------------------------

/// <summary>
/// Base class that spins up a loopback echo server and a connected client.
/// Concrete benchmark classes inherit from this and add their own [Params].
/// </summary>
public abstract class EndToEndBase
{
    protected ReactiveWebSocketServer Server = null!;
    protected ReactiveWebSocketClient Client = null!;
    protected Uri ServerUri = null!;

    [GlobalSetup]
    public virtual async Task SetupAsync()
    {
        var port = FreeTcpPort();
        var prefix = $"http://localhost:{port}/ws/";
        ServerUri = new Uri($"ws://localhost:{port}/ws/");

        Server = new ReactiveWebSocketServer(prefix);

        // Echo handler: text → text, binary → binary
        Server.Messages.SubscribeAwait(async (msg, ct) =>
        {
            if (msg.Message.IsText)
            {
                await Server.SendAsync(msg.Metadata.Id, msg.Message.Text, WebSocketMessageType.Text, ct);
            }
            else if (msg.Message.IsBinary)
            {
                await Server.SendAsync(msg.Metadata.Id, msg.Message.Binary, WebSocketMessageType.Binary, ct);
            }
        });

        await Server.StartAsync();

        Client = new ReactiveWebSocketClient(ServerUri) { IsReconnectionEnabled = false };

        var connected = Client.ConnectionHappened.FirstAsync();
        await Client.StartAsync();
        await connected;
    }

    [GlobalCleanup]
    public virtual async Task CleanupAsync()
    {
        await Client.StopAsync(WebSocketCloseStatus.NormalClosure, "Benchmark done");
        await Server.StopAsync(WebSocketCloseStatus.NormalClosure, "Benchmark done");
        await Client.DisposeAsync();
        await Server.DisposeAsync();
    }

    [IterationCleanup]
    public void IterationCleanup()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    private static int FreeTcpPort()
    {
        var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}

// ---------------------------------------------------------------------------
// 1) Latency benchmarks  – no [Params], pure connection / single-message cost
// ---------------------------------------------------------------------------

/// <summary>
/// Measures single round-trip latency and connection setup/teardown overhead.
///
/// Run with:
///   dotnet run -c Release -- --filter *Latency*
/// </summary>
[MemoryDiagnoser]
[GcServer(true)]
[HideColumns("Job", "RatioSD")]
public class LatencyBenchmarks : EndToEndBase
{
    // -----------------------------------------------------------------------
    // 1a) Single text round-trip
    // -----------------------------------------------------------------------
    [Benchmark(Baseline = true, Description = "Single text round-trip")]
    public async Task<string?> SingleRoundTrip()
    {
        var echoTask = Client.MessageReceived
            .Where(m => m is { IsText: true })
            .Where(m => m.Text.Span.SequenceEqual("ping".AsSpan()))
            .FirstAsync();

        await Client.SendAsync("ping".AsMemory(), WebSocketMessageType.Text);
        return (await echoTask).Text.ToString();
    }

    // -----------------------------------------------------------------------
    // 1b) ConnectAsync + StopAsync overhead
    // -----------------------------------------------------------------------
    [Benchmark(Description = "ConnectAsync + StopAsync (latency)")]
    public async Task ConnectAndStop()
    {
        var client = new ReactiveWebSocketClient(ServerUri) { IsReconnectionEnabled = false };
        await client.StartAsync();
        await client.StopAsync(WebSocketCloseStatus.NormalClosure, "done");
        client.Dispose();
    }
}

// ---------------------------------------------------------------------------
// 2) Throughput benchmarks  – small messages, varying message counts
// ---------------------------------------------------------------------------

/// <summary>
/// Measures sequential and concurrent throughput for small text messages.
///
/// Run with:
///   dotnet run -c Release -- --filter *Throughput*
/// </summary>
[MemoryDiagnoser]
[GcServer(true)]
[HideColumns("Job", "RatioSD")]
public class ThroughputBenchmarks : EndToEndBase
{
    // RoundTrips is used by SequentialRoundTrips only.
    // MessageCount is shared by EndToEndThroughput and TrySendThroughputWithConfirmation.
    // Keeping them separate avoids a cartesian-product explosion.

    [Params(10, 100)] public int RoundTrips { get; set; }

    [Params(100, 500, 1000)] public int MessageCount { get; set; }

    // -----------------------------------------------------------------------
    // 2a) N sequential round-trips  (send → wait for echo → repeat)
    // -----------------------------------------------------------------------
    [Benchmark(Baseline = true, Description = "Sequential round-trips (N messages)")]
    public async Task<int> SequentialRoundTrips()
    {
        var received = 0;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        using var sub = Client.MessageReceived
            .Where(m => m.IsText)
            .Subscribe(_ =>
            {
                if (Interlocked.Increment(ref received) >= RoundTrips)
                {
                    tcs.TrySetResult();
                }
            });

        for (var i = 0; i < RoundTrips; i++)
        {
            await Client.SendAsync($"msg-{i}".AsMemory(), WebSocketMessageType.Text, cts.Token);
        }

        await tcs.Task.WaitAsync(cts.Token).ConfigureAwait(false);
        return received;
    }
    
    // NOTE: Since SendAsync enqueues into a single Channel<T>, concurrent sends
    // do not parallelize actual I/O – they only pipeline the enqueue step.
    // For large payloads the SendLoop becomes the bottleneck and concurrent
    // offers no advantage over sequential.
    // -----------------------------------------------------------------------
    // 2b) End-to-end throughput  (all sends fired concurrently via WhenAll)
    // -----------------------------------------------------------------------
    [Benchmark(Description = "End-to-end throughput (N concurrent sends)")]
    public async Task<int> EndToEndThroughput()
    {
        var received = 0;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        using var sub = Client.MessageReceived
            .Where(m => m.IsText)
            .Subscribe(_ =>
            {
                if (Interlocked.Increment(ref received) >= MessageCount)
                {
                    tcs.TrySetResult();
                }
            });

        var sends = new Task[MessageCount];
        for (var i = 0; i < MessageCount; i++)
        {
            sends[i] = Client.SendInstantAsync($"msg-{i}".AsMemory(), WebSocketMessageType.Text, cts.Token);
        }

        await Task.WhenAll(sends).ConfigureAwait(false);
        await tcs.Task.WaitAsync(cts.Token).ConfigureAwait(false);
        return received;
    }

    // -----------------------------------------------------------------------
    // 2c) TrySend fire-and-forget  (Channel.TryWrite, no echo wait)
    // -----------------------------------------------------------------------
    [Benchmark(Description = "TrySendAsText throughput (no echo wait)")]
    public int TrySendAsText_Throughput()
    {
        var sent = 0;
        for (var i = 0; i < 1_000; i++)
        {
            if (Client.TrySend($"msg-{i}".AsMemory(), WebSocketMessageType.Text))
            {
                sent++;
            }
        }

        return sent;
    }

    // -----------------------------------------------------------------------
    // 2d) TrySend with echo confirmation  (Channel.TryWrite + backpressure)
    // -----------------------------------------------------------------------
    [Benchmark(Description = "TrySend throughput (fire + confirm all echoed)")]
    public async Task<int> TrySendThroughputWithConfirmation()
    {
        var received = 0;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        using var sub = Client.MessageReceived
            .Where(m => m.IsText)
            .Subscribe(_ =>
            {
                if (Interlocked.Increment(ref received) >= MessageCount)
                {
                    tcs.TrySetResult();
                }
            });

        for (var i = 0; i < MessageCount; i++)
        {
            while (!Client.TrySend($"msg-{i}".AsMemory(), WebSocketMessageType.Text))
            {
                await Task.Yield();
            }
        }

        await tcs.Task.WaitAsync(cts.Token).ConfigureAwait(false);
        return received;
    }
}

// ---------------------------------------------------------------------------
// 3) Large-message benchmarks  – binary payloads, varying size + count
// ---------------------------------------------------------------------------

/// <summary>
/// Measures throughput and latency for large binary payloads.
///
/// Payload is allocated once per iteration in [IterationSetup] so allocation
/// cost is not included in the benchmark measurement.
///
/// Run with:
///   dotnet run -c Release -- --filter *LargeMessage*
/// </summary>
[MemoryDiagnoser]
[GcServer(true)]
[HideColumns("Job", "RatioSD")]
public class LargeMessageBenchmarks : EndToEndBase
{
    [Params(1024, 16 * 1024, 256 * 1024)] public int PayloadBytes { get; set; }

    [Params(10, 100)] public int MessageCount { get; set; }

    private byte[] _payload = null!;

    // Allocate outside the timed region so payload size doesn't skew allocations
    [IterationSetup]
    public void IterationSetup()
    {
        _payload = ArrayPool<byte>.Shared.Rent(PayloadBytes);
        Random.Shared.NextBytes(_payload.AsSpan(0, PayloadBytes));
    }

    [IterationCleanup]
    public new void IterationCleanup()
    {
        ArrayPool<byte>.Shared.Return(_payload);
        _payload = null!;
        base.IterationCleanup();
    }

    // -----------------------------------------------------------------------
    // 3a) Sequential large-message round-trips
    // -----------------------------------------------------------------------
    [Benchmark(Baseline = true, Description = "Large message round-trip (sequential)")]
    public async Task<int> LargeMessageRoundTripSequential()
    {
        var received = 0;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        using var sub = Client.MessageReceived
            .Where(m => m.IsBinary)
            .Subscribe(_ =>
            {
                if (Interlocked.Increment(ref received) >= MessageCount)
                {
                    tcs.TrySetResult();
                }
            });

        for (var i = 0; i < MessageCount; i++)
        {
            await Client.SendAsync(_payload.AsMemory(0, PayloadBytes), WebSocketMessageType.Binary, cts.Token);
        }

        await tcs.Task.WaitAsync(cts.Token).ConfigureAwait(false);
        return received;
    }

    // -----------------------------------------------------------------------
    // 3b) Concurrent large-message sends (WhenAll)
    // -----------------------------------------------------------------------
    [Benchmark(Description = "Large message round-trip (concurrent)")]
    public async Task<int> LargeMessageRoundTripConcurrent()
    {
        var received = 0;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        using var sub = Client.MessageReceived
            .Where(m => m.IsBinary)
            .Subscribe(_ =>
            {
                if (Interlocked.Increment(ref received) >= MessageCount)
                {
                    tcs.TrySetResult();
                }
            });

        var sends = new Task[MessageCount];
        for (var i = 0; i < MessageCount; i++)
        {
            sends[i] = Client.SendAsync(_payload.AsMemory(0, PayloadBytes), WebSocketMessageType.Binary, cts.Token);
        }

        await Task.WhenAll(sends).ConfigureAwait(false);
        await tcs.Task.WaitAsync(cts.Token).ConfigureAwait(false);
        return received;
    }
}