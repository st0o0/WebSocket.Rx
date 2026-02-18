using System;
using BenchmarkDotNet.Attributes;
using R3;

namespace WebSocket.Rx.Benchmarks;

/// <summary>
/// Measures the overhead of the <c>ConnectionHappened</c>,
/// <c>DisconnectionHappened</c> and <c>ErrorOccurred</c> event streams.
///
/// These events are emitted via R3 <c>Subject&lt;T&gt;</c>.
/// We use <c>StreamFakeMessage</c> as a reference and call the internal
/// subjects directly via reflection – exercising exactly the same code
/// paths as the production code does.
/// </summary>
[ShortRunJob]
[MemoryDiagnoser]
[HideColumns("Job", "RatioSD", "Error")]
public class EventStreamBenchmarks
{
    [Params(10, 100)]
    public int EventCount { get; set; }

    private ReactiveWebSocketClient _client = null!;

    private Subject<Connected> _connectionSource = null!;
    private Subject<Disconnected> _disconnectionSource = null!;
    private Subject<ErrorOccurred> _errorSource = null!;

    [GlobalSetup]
    public void Setup()
    {
        _client = new ReactiveWebSocketClient(new Uri("ws://localhost:9999"));

        var t = typeof(ReactiveWebSocketClient);
        _connectionSource    = (Subject<Connected>)t.GetField("ConnectionHappenedSource",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(_client)!;
        _disconnectionSource = (Subject<Disconnected>)t.GetField("DisconnectionHappenedSource",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(_client)!;
        _errorSource         = (Subject<ErrorOccurred>)t.GetField("ErrorOccurredSource",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(_client)!;
    }

    [GlobalCleanup]
    public void Cleanup() => _client.Dispose();

    // -----------------------------------------------------------------------
    // 1) Baseline: ConnectionHappened without subscriber
    // -----------------------------------------------------------------------
    [Benchmark(Baseline = true, Description = "ConnectionHappened (no subscriber)")]
    public void Emit_Connection_NoSubscriber()
    {
        for (var i = 0; i < EventCount; i++)
        {
            _connectionSource.OnNext(new Connected(ConnectReason.Initialized));
        }
    }

    // -----------------------------------------------------------------------
    // 2) ConnectionHappened with one subscriber
    // -----------------------------------------------------------------------
    [Benchmark(Description = "ConnectionHappened → 1 subscriber")]
    public int Emit_Connection_OneSubscriber()
    {
        var count = 0;
        using var sub = _client.ConnectionHappened.Subscribe(_ => count++);
        for (var i = 0; i < EventCount; i++)
        {
            _connectionSource.OnNext(new Connected(ConnectReason.Initialized));
        }
        return count;
    }

    // -----------------------------------------------------------------------
    // 3) DisconnectionHappened with one subscriber + Where filter
    // -----------------------------------------------------------------------
    [Benchmark(Description = "DisconnectionHappened → Where(ServerInitiated)")]
    public int Emit_Disconnection_Filtered()
    {
        var count = 0;
        using var sub = _client.DisconnectionHappened
            .Where(d => d.Reason == DisconnectReason.ServerInitiated)
            .Subscribe(_ => count++);

        for (var i = 0; i < EventCount; i++)
        {
            _disconnectionSource.OnNext(new Disconnected(
                i % 2 == 0 ? DisconnectReason.ServerInitiated : DisconnectReason.ClientInitiated));
        }
        return count;
    }

    // -----------------------------------------------------------------------
    // 4) ErrorOccurred with Select (extract error source)
    // -----------------------------------------------------------------------
    [Benchmark(Description = "ErrorOccurred → Select(Source)")]
    public int Emit_Error_Select()
    {
        var count = 0;
        using var sub = _client.ErrorOccurred
            .Select(e => e.Source)
            .Subscribe(_ => count++);

        for (var i = 0; i < EventCount; i++)
        {
            _errorSource.OnNext(new ErrorOccurred(ErrorSource.ReceiveLoop,
                new InvalidOperationException("test")));
        }
        return count;
    }

    // -----------------------------------------------------------------------
    // 5) All three streams simultaneously with one subscriber each
    //    (mirrors typical production code)
    // -----------------------------------------------------------------------
    [Benchmark(Description = "All 3 streams → 1 subscriber each")]
    public int Emit_AllStreams()
    {
        var count = 0;
        using var s1 = _client.ConnectionHappened.Subscribe(_ => count++);
        using var s2 = _client.DisconnectionHappened.Subscribe(_ => count++);
        using var s3 = _client.ErrorOccurred.Subscribe(_ => count++);

        for (var i = 0; i < EventCount; i++)
        {
            _connectionSource.OnNext(new Connected(ConnectReason.Reconnected));
            _disconnectionSource.OnNext(new Disconnected(DisconnectReason.Dropped));
        }

        return count;
    }
}