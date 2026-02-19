using System.Net;

namespace WebSocket.Rx;

public record Metadata(Guid Id, IPAddress? Address = null, int? Port = null);