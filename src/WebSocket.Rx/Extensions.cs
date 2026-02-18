using System.Buffers;
using System.Net.WebSockets;
using System.Text;
using R3;

namespace WebSocket.Rx;

public static class Extensions
{
    // new 
    extension(IReactiveWebSocketClient client)
    {
        public Observable<bool> SendInstant(Observable<(byte[], WebSocketMessageType)> messages)
        {
            return Observable.Create<bool>(observer =>
            {
                var disposables = new CompositeDisposable();
                disposables.Add(messages.SubscribeAwait(async (tuple, ct) =>
                {
                    var result = await client.SendInstantAsync(tuple.Item1, tuple.Item2, ct).ConfigureAwait(false);
                    observer.OnNext(result);
                }));

                return disposables;
            });
        }

        public Observable<bool> SendInstant(Observable<(string, WebSocketMessageType)> messages)
        {
            return Observable.Create<bool>(observer =>
            {
                var disposables = new CompositeDisposable();
                disposables.Add(messages.SubscribeAwait(async (tuple, ct) =>
                {
                    var result = await client.SendInstantAsync(tuple.Item1.AsMemory(), tuple.Item2, ct)
                        .ConfigureAwait(false);
                    observer.OnNext(result);
                }));

                return disposables;
            });
        }

        public Observable<bool> Send(Observable<(byte[], WebSocketMessageType)> messages)
        {
            return Observable.Create<bool>(observer =>
            {
                var disposables = new CompositeDisposable();
                disposables.Add(messages.SubscribeAwait(async (tuple, ct) =>
                {
                    var result = await client.SendAsync(tuple.Item1, tuple.Item2, ct).ConfigureAwait(false);
                    observer.OnNext(result);
                }));

                return disposables;
            });
        }

        public Observable<bool> Send(Observable<(string, WebSocketMessageType)> messages)
        {
            return Observable.Create<bool>(observer =>
            {
                var disposables = new CompositeDisposable();
                disposables.Add(messages.SubscribeAwait(async (tuple, ct) =>
                {
                    var result = await client.SendAsync(tuple.Item1.AsMemory(), tuple.Item2, ct).ConfigureAwait(false);
                    observer.OnNext(result);
                }));

                return disposables;
            });
        }

        public Observable<bool> TrySend(Observable<(string, WebSocketMessageType)> messages)
        {
            return Observable.Create<bool>(observer =>
            {
                var disposables = new CompositeDisposable();
                disposables.Add(messages.Subscribe(tuple =>
                {
                    var result = client.TrySend(tuple.Item1.AsMemory(), tuple.Item2);
                    observer.OnNext(result);
                }));

                return disposables;
            });
        }

        public Observable<bool> TrySend(Observable<(byte[], WebSocketMessageType)> messages)
        {
            return Observable.Create<bool>(observer =>
            {
                var disposables = new CompositeDisposable();
                disposables.Add(messages.Subscribe(tuple =>
                {
                    var result = client.TrySend(tuple.Item1, tuple.Item2);
                    observer.OnNext(result);
                }));

                return disposables;
            });
        }
    }

    // old
    extension(IReactiveWebSocketClient client)
    {
        public Observable<bool> SendInstant(Observable<byte[]> message)
            => client.SendInstant(message.Select(data => (data, WebSocketMessageType.Binary)));

        public Observable<bool> SendInstant(Observable<string> message)
            => client.SendInstant(message.Select(data => (data, WebSocketMessageType.Binary)));

        public Observable<bool> SendInstantAsBinary(Observable<byte[]> messages)
            => client.SendInstant(messages.Select(data => (data, WebSocketMessageType.Binary)));

        public Observable<bool> SendInstantAsBinary(Observable<string> messages)
            => client.SendInstant(messages.Select(data => (data, WebSocketMessageType.Binary)));

        public Observable<bool> SendInstantAsText(Observable<byte[]> messages)
            => client.SendInstant(messages.Select(data => (data, WebSocketMessageType.Text)));

        public Observable<bool> SendInstantAsText(Observable<string> messages)
            => client.SendInstant(messages.Select(data => (data, WebSocketMessageType.Text)));

        public Observable<bool> TrySendAsBinary(Observable<byte[]> messages)
            => client.TrySend(messages.Select(data => (data, WebSocketMessageType.Binary)));

        public Observable<bool> TrySendAsBinary(Observable<string> messages)
            => client.TrySend(messages.Select(data => (data, WebSocketMessageType.Binary)));

        public Observable<bool> TrySendAsText(Observable<byte[]> messages)
            => client.TrySend(messages.Select(data => (data, WebSocketMessageType.Text)));

        public Observable<bool> TrySendAsText(Observable<string> messages)
            => client.TrySend(messages.Select(data => (data, WebSocketMessageType.Text)));

        public async Task<bool> SendInstantAsync(byte[] message, CancellationToken cancellationToken = default)
            => await client.SendInstantAsync(message.AsMemory(), WebSocketMessageType.Binary, cancellationToken);

        public async Task<bool> SendInstantAsync(string message, CancellationToken cancellationToken = default)
            => await client.SendInstantAsync(message.AsMemory(), WebSocketMessageType.Binary, cancellationToken);

        public async Task<bool> SendAsBinaryAsync(byte[] message, CancellationToken cancellationToken = default)
            => await client.SendAsync(message.AsMemory(), WebSocketMessageType.Binary, cancellationToken);

        public async Task<bool> SendAsBinaryAsync(string message, CancellationToken cancellationToken = default)
            => await client.SendAsync(message.AsMemory(), WebSocketMessageType.Binary, cancellationToken);

        public async Task<bool> SendAsTextAsync(byte[] message, CancellationToken cancellationToken = default)
            => await client.SendAsync(message.AsMemory(), WebSocketMessageType.Text, cancellationToken);

        public async Task<bool> SendAsTextAsync(string message, CancellationToken cancellationToken = default)
            => await client.SendAsync(message.AsMemory(), WebSocketMessageType.Text, cancellationToken);

        public bool TrySendAsBinary(byte[] message)
            => client.TrySend(message.AsMemory(), WebSocketMessageType.Binary);

        public bool TrySendAsBinary(string message)
            => client.TrySend(message.AsMemory(), WebSocketMessageType.Binary);

        public bool TrySendAsText(byte[] message)
            => client.TrySend(message.AsMemory(), WebSocketMessageType.Text);

        public bool TrySendAsText(string message)
            => client.TrySend(message.AsMemory(), WebSocketMessageType.Text);
    }

    internal static Payload ToPayload(this ReadOnlyMemory<char> value, Encoding encoding, WebSocketMessageType type)
    {
        var maxByteCount = encoding.GetMaxByteCount(value.Length);
        var rentedBuffer = ArrayPool<byte>.Shared.Rent(maxByteCount);
        var actualBytes = encoding.GetBytes(value.Span, rentedBuffer);
        return new Payload(rentedBuffer, actualBytes, type);
    }
}