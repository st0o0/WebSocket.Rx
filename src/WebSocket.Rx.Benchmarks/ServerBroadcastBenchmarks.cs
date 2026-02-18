using System.Net;
using System.Net.WebSockets;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;

namespace WebSocket.Rx.Benchmarks;

/// <summary>
/// Measures the overhead of the server broadcast methods on
/// <c>ReactiveWebSocketServer</c> without real network connections.
///
/// Since the broadcast methods internally iterate the client list and
/// use <c>Task.WhenAll</c> + LINQ, the overhead can already be meaningfully
/// measured by calling them directly on a started server with no connected clients.
///
/// For a full test with N connected clients see <c>EndToEndBenchmarks</c>.
/// </summary>
[ShortRunJob]
[MemoryDiagnoser]
[HideColumns("Job", "RatioSD", "Error")]
public class ServerBroadcastBenchmarks
{
    [Params(64, 1_024, 4_096)] public int PayloadSize { get; set; }

    private ReactiveWebSocketServer _server = null!;
    private string _textPayload = null!;
    private byte[] _binaryPayload = null!;

    [GlobalSetup]
    public async Task SetupAsync()
    {
        var port = FreeTcpPort();
        _server = new ReactiveWebSocketServer($"http://localhost:{port}/ws/");
        await _server.StartAsync();

        _textPayload = new string('x', PayloadSize);
        _binaryPayload = new byte[PayloadSize];
    }

    [GlobalCleanup]
    public async Task CleanupAsync()
    {
        await _server.StopAsync(WebSocketCloseStatus.NormalClosure, "done");
        await _server.DisposeAsync();
    }

    // -----------------------------------------------------------------------
    // 1) Baseline: BroadcastAsTextAsync – 0 clients (method overhead only)
    // -----------------------------------------------------------------------
    [Benchmark(Baseline = true, Description = "BroadcastAsTextAsync (0 clients)")]
    public async Task<bool> BroadcastAsTextAsync_Empty()
    {
        return await _server.BroadcastAsTextAsync(_textPayload);
    }

    // -----------------------------------------------------------------------
    // 2) BroadcastAsBinaryAsync with byte[]
    // -----------------------------------------------------------------------
    [Benchmark(Description = "BroadcastAsBinaryAsync(byte[]) (0 clients)")]
    public async Task<bool> BroadcastAsBinaryAsync_Bytes_Empty()
    {
        return await _server.BroadcastAsBinaryAsync(_binaryPayload);
    }

    // -----------------------------------------------------------------------
    // 3) TryBroadcastAsText – synchronous, no await
    // -----------------------------------------------------------------------
    [Benchmark(Description = "TryBroadcastAsText(string) (0 clients)")]
    public bool TryBroadcastAsText_Empty()
    {
        return _server.TryBroadcastAsText(_textPayload);
    }

    // -----------------------------------------------------------------------
    // 4) TryBroadcastAsBinary with byte[]
    // -----------------------------------------------------------------------
    [Benchmark(Description = "TryBroadcastAsBinary(byte[]) (0 clients)")]
    public bool TryBroadcastAsBinary_Empty()
    {
        return _server.TryBroadcastAsBinary(_binaryPayload);
    }

    // -----------------------------------------------------------------------
    // 5) ClientCount query + ConnectedClients dictionary snapshot
    //    (ConcurrentDictionary.ToDictionary() – called on every broadcast)
    // -----------------------------------------------------------------------
    [Benchmark(Description = "ClientCount + ConnectedClients snapshot")]
    public int ClientSnapshot()
    {
        var count = _server.ClientCount;
        var snapshot = _server.ConnectedClients;
        return count + snapshot.Count;
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