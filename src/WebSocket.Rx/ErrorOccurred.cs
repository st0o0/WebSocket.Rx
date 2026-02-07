namespace WebSocket.Rx;

public record ErrorOccurred(ErrorSource Source, Exception Exception);