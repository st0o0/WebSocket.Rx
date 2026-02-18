using System;
using BenchmarkDotNet.Attributes;
using Microsoft.IO;

namespace WebSocket.Rx.Benchmarks;

/// <summary>
/// Measures the overhead of creating <c>ReactiveWebSocketClient</c> instances.
///
/// The constructor optionally accepts a <c>RecyclableMemoryStreamManager</c>.
/// We measure:
///   - Allocation cost without vs. with a shared MemoryStreamManager
///   - GC pressure from many short-lived instances
/// No network connection required.
/// </summary>
[ShortRunJob]
[MemoryDiagnoser]
[HideColumns("Job", "RatioSD", "Error")]
public class ClientInstantiationBenchmarks
{
    private static readonly Uri ServerUri = new("ws://localhost:9999");
    
    // Shared manager – as it should be used in production code
    private readonly RecyclableMemoryStreamManager _sharedManager = new();

    // -----------------------------------------------------------------------
    // 1) Baseline: without a custom MemoryStreamManager (new() is created internally)
    // -----------------------------------------------------------------------
    [Benchmark(Baseline = true, Description = "new ReactiveWebSocketClient(url)")]
    public ReactiveWebSocketClient Instantiate_NoManager()
    {
        return new ReactiveWebSocketClient(ServerUri);
    }

    // -----------------------------------------------------------------------
    // 2) With a shared RecyclableMemoryStreamManager
    // -----------------------------------------------------------------------
    [Benchmark(Description = "new ReactiveWebSocketClient(url, sharedManager)")]
    public ReactiveWebSocketClient Instantiate_SharedManager()
    {
        return new ReactiveWebSocketClient(ServerUri, _sharedManager);
    }

    // -----------------------------------------------------------------------
    // 3) With a dedicated RecyclableMemoryStreamManager per instance
    // -----------------------------------------------------------------------
    [Benchmark(Description = "new ReactiveWebSocketClient(url, new Manager())")]
    public ReactiveWebSocketClient Instantiate_OwnManager()
    {
        return new ReactiveWebSocketClient(ServerUri, new RecyclableMemoryStreamManager());
    }

    // -----------------------------------------------------------------------
    // 4) GC pressure: 100 short-lived instances (disposed immediately)
    // -----------------------------------------------------------------------
    [Benchmark(Description = "100x create + Dispose")]
    public void Allocate_AndDispose_100()
    {
        for (var i = 0; i < 100; i++)
        {
            using var client = new ReactiveWebSocketClient(ServerUri, _sharedManager);
        }
    }
}