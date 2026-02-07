namespace WebSocket.Rx;

public record ServerErrorOccurred(Metadata Metadata, ErrorSource Source, Exception Exception)
    : ErrorOccurred(Source, Exception);