using System;
using System.Net;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using R3;

namespace WebSocket.Rx.Benchmarks;

/// <summary>
/// End-to-end benchmarks using <c>ReactiveWebSocketServer</c> and
/// <c>ReactiveWebSocketClient</c> over loopback.
///
/// Measures:
///   - Single round-trip latency (Send → Echo → MessageReceived)
///   - Throughput for N sequential messages
///   - Connection setup latency (ConnectAsync)
///
/// The server uses <c>ReactiveWebSocketServer</c> with a built-in echo
/// mechanism via the <c>Messages</c> stream and <c>SendAsTextAsync</c>.
///
/// NOTE: These tests take longer to run. Execute individually with:
///   dotnet run -c Release -- --filter *EndToEnd*
/// </summary>
[ShortRunJob]
[MemoryDiagnoser]
[HideColumns("Job", "RatioSD", "Error")]
public class EndToEndBenchmarks
{
    private ReactiveWebSocketServer _server = null!;
    private ReactiveWebSocketClient _client = null!;
    private string _prefix = null!;
    private Uri _serverUri = null!;

    [GlobalSetup]
    public async Task SetupAsync()
    {
        var port = FreeTcpPort();
        _prefix = $"http://localhost:{port}/ws/";
        _serverUri = new Uri($"ws://localhost:{port}/ws/");

        _server = new ReactiveWebSocketServer(_prefix);

        _server.Messages.SubscribeAwait(async (msg, ct) =>
        {
            if (msg.Message.IsText)
            {
                await _server.SendAsync(msg.Metadata.Id, msg.Message.Text, WebSocketMessageType.Text, ct);
            }
        });

        await _server.StartAsync();

        _client = new ReactiveWebSocketClient(_serverUri)
        {
            IsReconnectionEnabled = false
        };
        var connectionHappenedTask = _client.ConnectionHappened.FirstAsync();
        await _client.StartAsync();

        await connectionHappenedTask;
    }

    [GlobalCleanup]
    public async Task CleanupAsync()
    {
        await _client.StopAsync(WebSocketCloseStatus.NormalClosure, "Benchmark done");
        await _server.StopAsync(WebSocketCloseStatus.NormalClosure, "Benchmark done");
        await _client.DisposeAsync();
        await _server.DisposeAsync();
    }

    // -----------------------------------------------------------------------
    // 1) Single Round-Trip
    // -----------------------------------------------------------------------
    [Benchmark(Baseline = true, Description = "Single text round-trip")]
    public async Task<string?> SingleRoundTrip()
    {
        var echoTask = _client.MessageReceived
            .Where(m => m is { IsText: true })
            .Where(m => m.Text.ToString() is "ping")
            .FirstAsync();

        await _client.SendAsync("ping".AsMemory(), WebSocketMessageType.Text);
        return (await echoTask).Text.ToString();
    }

    // -----------------------------------------------------------------------
    // 2) N sequentielle Round-Trips
    // -----------------------------------------------------------------------
    [Params(10, 50)] public int RoundTrips { get; set; }

    [Benchmark(Description = "Sequential round-trips (N messages)")]
    public async Task<int> SequentialRoundTrips()
    {
        var received = 0;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        using var sub = _client.MessageReceived
            .Where(m => m.IsText)
            .Subscribe(_ => Interlocked.Increment(ref received));

        for (var i = 0; i < RoundTrips; i++)
        {
            await _client.SendAsync($"msg-{i}".AsMemory(), WebSocketMessageType.Text, cts.Token);
        }

        while (received < RoundTrips && !cts.IsCancellationRequested)
        {
            await Task.Delay(1, cts.Token).ConfigureAwait(false);
        }

        return received;
    }

    // -----------------------------------------------------------------------
    // 3) TrySendAsText throughput (fire-and-forget, no echo wait)
    //    Measures pure Channel.TryWrite speed including the SendLoop
    // -----------------------------------------------------------------------
    [Benchmark(Description = "TrySendAsText throughput (no echo wait)")]
    public int TrySendAsText_Throughput()
    {
        var sent = 0;
        for (var i = 0; i < 1_000; i++)
        {
            if (_client.TrySend($"msg-{i}".AsMemory(), WebSocketMessageType.Text))
            {
                sent++;
            }
        }

        return sent;
    }

    // -----------------------------------------------------------------------
    // 4) ConnectAsync + StopAsync overhead (connection setup and teardown)
    //    Creates a fresh client each time without waiting for an echo
    // -----------------------------------------------------------------------
    [Benchmark(Description = "ConnectAsync + StopAsync (latency)")]
    public async Task ConnectAndStop()
    {
        var client = new ReactiveWebSocketClient(_serverUri)
        {
            IsReconnectionEnabled = false
        };
        await client.StartAsync();
        await client.StopAsync(WebSocketCloseStatus.NormalClosure, "done");
        client.Dispose();
    }

    private static int FreeTcpPort()
    {
        var l = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        l.Start();
        var port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }
}