using System;
using System.Net.WebSockets;
using System.Text;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;

namespace WebSocket.Rx.Benchmarks;

/// <summary>
/// Measures the overhead of the send methods on <c>ReactiveWebSocketClient</c>.
///
/// There are four variants:
///   - <c>TrySendAsBinary</c> / <c>TrySendAsText</c>    → Channel.TryWrite (synchronous, non-blocking)
///   - <c>SendAsBinaryAsync</c> / <c>SendAsTextAsync</c> → Channel.WriteAsync (async, buffered)
///   - <c>SendInstantAsync</c>                           → bypasses the queue, goes directly to NativeClient.SendAsync
///
/// Since no real WebSocket server is running, we test the queue methods
/// with <c>IsRunning = true</c> and a Channel without a consumer,
/// which isolates the pure enqueue latency.
///
/// NOTE: The Channel is <c>UnboundedChannel</c> (SingleReader) –
///       without a consumer it fills up, but writes never block.
/// </summary>
[MemoryDiagnoser]
[HideColumns("Job", "RatioSD", "Error")]
public class SendChannelBenchmarks
{
    [Params(100, 1_000)]
    public int MessageCount { get; set; }

    [Params(64, 1_024)]
    public int PayloadSize { get; set; }

    private ReactiveWebSocketClient _client = null!;
    private string _textPayload = null!;
    private byte[] _binaryPayload = null!;

    [GlobalSetup]
    public void Setup()
    {
        _client = new ReactiveWebSocketClient(new Uri("ws://localhost:9999"));

        // IsRunning must be true so TrySend/SendAsync does not immediately return false.
        // We set the internal property via reflection (no public setter available).
        typeof(ReactiveWebSocketClient)
            .GetProperty(nameof(ReactiveWebSocketClient.IsRunning))!
            .SetValue(_client, true);

        _textPayload = new string('a', PayloadSize);
        _binaryPayload = new byte[PayloadSize];
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        // Reset IsRunning so Dispose completes cleanly
        typeof(ReactiveWebSocketClient)
            .GetProperty(nameof(ReactiveWebSocketClient.IsRunning))!
            .SetValue(_client, false);
        _client.Dispose();
    }

    // -----------------------------------------------------------------------
    // 1) Baseline: TrySend(string) – synchronous Channel.TryWrite
    // -----------------------------------------------------------------------
    [Benchmark(Baseline = true, Description = "TrySend(string, WebSocketMessageType.Binary)")]
    public bool TrySendAsBinary_String()
    {
        var result = false;
        var payload = _textPayload.AsMemory();
        for (var i = 0; i < MessageCount; i++)
        {
            result = _client.TrySend(payload, WebSocketMessageType.Binary);
        }

        return result;
    }

    // -----------------------------------------------------------------------
    // 2) TrySend(byte[])
    // -----------------------------------------------------------------------
    [Benchmark(Description = "TrySend(byte[], WebSocketMessageType.Binary)")]
    public bool TrySendAsBinary_Bytes()
    {
        var result = false;
        for (var i = 0; i < MessageCount; i++)
        {
            result = _client.TrySend(_binaryPayload, WebSocketMessageType.Binary);
        }

        return result;
    }

    // -----------------------------------------------------------------------
    // 3) TrySend(string)
    // -----------------------------------------------------------------------
    [Benchmark(Description = "TrySend(string, WebSocketMessageType.Text)")]
    public bool TrySendAsText_String()
    {
        var result = false;
        var payload = _textPayload.AsMemory();
        for (var i = 0; i < MessageCount; i++)
        {
            result = _client.TrySend(payload, WebSocketMessageType.Text);
        }

        return result;
    }

    // -----------------------------------------------------------------------
    // 4) TrySend(byte[])
    // -----------------------------------------------------------------------
    [Benchmark(Description = "TrySendAsText(byte[], WebSocketMessageType.Text)")]
    public bool TrySendAsText_Bytes()
    {
        var result = false;
        for (var i = 0; i < MessageCount; i++)
        {
            result = _client.TrySend(_binaryPayload, WebSocketMessageType.Text);
        }

        return result;
    }

    // -----------------------------------------------------------------------
    // 5) SendAsync(string) – Channel.WriteAsync (ValueTask)
    // -----------------------------------------------------------------------
    [Benchmark(Description = "SendAsync(string, WebSocketMessageType.Binary)")]
    public async Task<bool> SendAsBinaryAsync_String()
    {
        var result = false;
        var payload = _textPayload.AsMemory();
        for (var i = 0; i < MessageCount; i++)
        {
            result = await _client.SendAsync(payload, WebSocketMessageType.Binary);
        }

        return result;
    }

    // -----------------------------------------------------------------------
    // 6) SendAsync(string) – Channel.WriteAsync (ValueTask)
    // -----------------------------------------------------------------------
    [Benchmark(Description = "SendAsync(string, WebSocketMessageType.Text)")]
    public async Task<bool> SendAsTextAsync_String()
    {
        var result = false;
        var payload = _textPayload.AsMemory();
        for (var i = 0; i < MessageCount; i++)
        {
            result = await _client.SendAsync(payload, WebSocketMessageType.Text);
        }

        return result;
    }

    // -----------------------------------------------------------------------
    // 7) String → ReadOnlyMemory<byte> encoding overhead (Encoding.UTF8.GetBytes)
    //    – isolates the encoding cost hidden inside TrySend/SendAsync
    // -----------------------------------------------------------------------
    [Benchmark(Description = "Encoding.UTF8.GetBytes (overhead only)")]
    public ReadOnlyMemory<byte> EncodingOverhead()
    {
        var result = new ReadOnlyMemory<byte>([]);
        var payload = _textPayload.AsMemory();
        for (var i = 0; i < MessageCount; i++)
        {
            result = payload.ToPayload(Encoding.UTF8, WebSocketMessageType.Text).Data;
        }

        return result;
    }
}