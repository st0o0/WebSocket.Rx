using System.Buffers;
using System.Net.WebSockets;
using System.Text;
using R3;
using WebSocket.Rx.Internal;

namespace WebSocket.Rx;

public static class Extensions
{
    extension(IReactiveWebSocketClient client)
    {
        public Observable<bool> SendInstant(Observable<Message> messages)
        {
            return messages.SelectAwait(async (send, ct) =>
            {
                return send.Type switch
                {
                    _ when send.IsText => await client.SendAsync(send.Text, send.Type, ct).ConfigureAwait(false),
                    _ when send.IsBinary => await client.SendAsync(send.Binary, send.Type, ct).ConfigureAwait(false),
                    _ => false
                };
            }, maxConcurrent: 1);
        }

        public Observable<bool> Send(Observable<Message> messages)
        {
            return messages.SelectAwait(async (send, ct) =>
            {
                return send.Type switch
                {
                    _ when send.IsText => await client.SendAsync(send.Text, send.Type, ct).ConfigureAwait(false),
                    _ when send.IsBinary => await client.SendAsync(send.Binary, send.Type, ct).ConfigureAwait(false),
                    _ => false
                };
            }, maxConcurrent: 1);
        }

        public Observable<bool> TrySend(Observable<Message> messages)
        {
            return messages.Select(send =>
            {
                return send.Type switch
                {
                    _ when send.IsText => client.TrySend(send.Text, send.Type),
                    _ when send.IsBinary => client.TrySend(send.Binary, send.Type),
                    _ => false
                };
            });
        }
    }

    extension(IReactiveWebSocketServer server)
    {
        public Observable<bool> SendInstant(Observable<ServerMessage> messages)
        {
            return messages.SelectAwait(async (send, ct) =>
            {
                var msg = send.Message;
                return msg.Type switch
                {
                    _ when msg.IsText => await server.SendAsync(send.Metadata.Id, msg.Text, msg.Type, ct).ConfigureAwait(false),
                    _ when msg.IsBinary => await server.SendAsync(send.Metadata.Id, msg.Binary, msg.Type, ct).ConfigureAwait(false),
                    _ => false
                };
            }, maxConcurrent: 1);
        }

        public Observable<bool> Send(Observable<ServerMessage> messages)
        {
            return messages.SelectAwait(async (send, ct) =>
            {
                var msg = send.Message;
                return msg.Type switch
                {
                    _ when msg.IsText => await server.SendAsync(send.Metadata.Id, msg.Text, msg.Type, ct).ConfigureAwait(false),
                    _ when msg.IsBinary => await server.SendAsync(send.Metadata.Id, msg.Binary, msg.Type, ct).ConfigureAwait(false),
                    _ => false
                };
            }, maxConcurrent: 1);
        }

        public Observable<bool> TrySend(Observable<ServerMessage> messages)
        {
            return messages.Select(send =>
            {
                var msg = send.Message;
                return msg.Type switch
                {
                    _ when msg.IsText => server.TrySend(send.Metadata.Id, msg.Text, msg.Type),
                    _ when msg.IsBinary => server.TrySend(send.Metadata.Id, msg.Binary, msg.Type),
                    _ => false
                };
            });
        }

        public Observable<bool> BroadcastInstant(Observable<ServerMessage> messages)
        {
            return messages.SelectAwait(async (send, ct) =>
            {
                var msg = send.Message;
                return msg.Type switch
                {
                    _ when msg.IsText => await server.BroadcastInstantAsync(msg.Text, msg.Type, ct).ConfigureAwait(false),
                    _ when msg.IsBinary => await server.BroadcastInstantAsync(msg.Binary, msg.Type, ct).ConfigureAwait(false),
                    _ => false
                };
            });
        }

        public Observable<bool> BroadcastAsync(Observable<ServerMessage> messages)
        {
            return messages.SelectAwait(async (send, ct) =>
            {
                var msg = send.Message;
                return msg.Type switch
                {
                    _ when msg.IsText => await server.BroadcastAsync(msg.Text, msg.Type, ct).ConfigureAwait(false),
                    _ when msg.IsBinary => await server.BroadcastAsync(msg.Binary, msg.Type, ct).ConfigureAwait(false),
                    _ => false
                };
            });
        }

        public Observable<bool> TryBroadcast(Observable<ServerMessage> messages)
        {
            return messages.Select(send =>
            {
                var msg = send.Message;
                return msg.Type switch
                {
                    _ when msg.IsText => server.TryBroadcast(msg.Text, msg.Type),
                    _ when msg.IsBinary => server.TryBroadcast(msg.Binary, msg.Type),
                    _ => false
                };
            });
        }
    }

    internal static Payload ToPayload(this ReadOnlyMemory<char> value, Encoding encoding, WebSocketMessageType type)
    {
        var maxByteCount = encoding.GetMaxByteCount(value.Length);
        var rentedBuffer = ArrayPool<byte>.Shared.Rent(maxByteCount);
        var actualBytes = encoding.GetBytes(value.Span, rentedBuffer);
        return new Payload(rentedBuffer, actualBytes, type);
    }
}