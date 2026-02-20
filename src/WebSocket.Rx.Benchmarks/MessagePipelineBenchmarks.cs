using System;
using System.Linq;
using System.Threading;
using BenchmarkDotNet.Attributes;
using R3;

namespace WebSocket.Rx.Benchmarks;

/// <summary>
/// Measures the overhead of the incoming message pipeline.
///
/// <c>StreamFakeMessage</c> calls <c>MessageReceivedSource.OnNext</c> directly –
/// bypassing the network so we measure only:
///   - R3 Subject overhead (not System.Reactive!)
///   - ReceivedMessage allocation (Text vs. Binary)
///   - Rx operator overhead per subscriber (Where, Select, …)
/// </summary>
[MemoryDiagnoser]
[HideColumns("Job", "RatioSD", "Error")]
public class MessagePipelineBenchmarks
{
    [Params(100, 1_000)] public int MessageCount { get; set; }

    [Params(64, 512, 4_096)] public int PayloadSize { get; set; }

    private ReactiveWebSocketClient _client = null!;
    private Message _textMessage = null!;
    private Message _binaryMessage = null!;

    [GlobalSetup]
    public void Setup()
    {
        _client = new ReactiveWebSocketClient(new Uri("ws://localhost:9999"));
        _textMessage = Message.Create(new string('x', PayloadSize));
        _binaryMessage = Message.Create(new byte[PayloadSize]);
    }

    [GlobalCleanup]
    public void Cleanup() => _client.Dispose();

    // -----------------------------------------------------------------------
    // 1) Baseline: OnNext without subscriber
    // -----------------------------------------------------------------------
    [Benchmark(Baseline = true, Description = "StreamFakeMessage (no subscriber)")]
    public void StreamFakeMessage_NoSubscriber()
    {
        for (var i = 0; i < MessageCount; i++)
        {
            _client.StreamFakeMessage(_textMessage);
        }
    }

    // -----------------------------------------------------------------------
    // 2) One Subscribe, no filter
    // -----------------------------------------------------------------------
    [Benchmark(Description = "→ 1 subscriber (raw)")]
    public int OneSubscriber_Raw()
    {
        var count = 0;
        using var sub = _client.MessageReceived.Subscribe(_ => count++);
        for (var i = 0; i < MessageCount; i++)
        {
            _client.StreamFakeMessage(_textMessage);
        }

        return count;
    }

    // -----------------------------------------------------------------------
    // 3) Where filter (IsText)
    // -----------------------------------------------------------------------
    [Benchmark(Description = "→ Where(IsText)")]
    public int OneSubscriber_WhereText()
    {
        var count = 0;
        using var sub = _client.MessageReceived
            .Where(m => m.IsText)
            .Subscribe(_ => count++);
        for (var i = 0; i < MessageCount; i++)
        {
            _client.StreamFakeMessage(_textMessage);
        }

        return count;
    }

    // -----------------------------------------------------------------------
    // 4) Where + Select (extract text string)
    // -----------------------------------------------------------------------
    [Benchmark(Description = "→ Where + Select(Text)")]
    public int OneSubscriber_WhereAndSelect()
    {
        var count = 0;
        using var sub = _client.MessageReceived
            .Where(m => m.IsText)
            .Select(m => m.Text)
            .Subscribe(_ => count++);
        for (var i = 0; i < MessageCount; i++)
        {
            _client.StreamFakeMessage(_textMessage);
        }

        return count;
    }

    // -----------------------------------------------------------------------
    // 5) Binary messages (no encoding overhead in the ReceiveLoop)
    // -----------------------------------------------------------------------
    [Benchmark(Description = "→ Binary messages")]
    public int OneSubscriber_Binary()
    {
        var count = 0;
        using var sub = _client.MessageReceived.Subscribe(_ => count++);
        for (var i = 0; i < MessageCount; i++)
        {
            _client.StreamFakeMessage(_binaryMessage);
        }

        return count;
    }

    // -----------------------------------------------------------------------
    // 6) 5 concurrent subscribers (multicast overhead in R3 Subject)
    // -----------------------------------------------------------------------
    [Benchmark(Description = "→ 5 concurrent subscribers")]
    public int FiveSubscribers()
    {
        var count = 0;
        var subs = Enumerable.Range(0, 5)
            .Select(_ => _client.MessageReceived.Subscribe(_ => Interlocked.Increment(ref count)))
            .ToList();

        for (var i = 0; i < MessageCount; i++)
        {
            _client.StreamFakeMessage(_textMessage);
        }

        foreach (var s in subs)
        {
            s.Dispose();
        }

        return count;
    }
}