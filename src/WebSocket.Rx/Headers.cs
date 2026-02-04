using System.Net;

namespace WebSocket.Rx;

public static class Headers
{
    public const string IdHeader = "X-ID";
}

public record Metadata(Guid Id, IPAddress Address, int Port);