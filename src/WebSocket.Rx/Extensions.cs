using R3;

namespace WebSocket.Rx;

public static class Extensions
{
    extension(IReactiveWebSocketClient client)
    {
        public Observable<bool> SendInstant(Observable<byte[]> messages)
        {
            return Observable.Create<bool>(observer =>
            {
                var disposables = new CompositeDisposable();
                disposables.Add(messages.SubscribeAwait(async (msg, ct) =>
                {
                    var result = await client.SendInstantAsync(msg, ct).ConfigureAwait(false);
                    observer.OnNext(result);
                }));

                return disposables;
            });
        }

        public Observable<bool> SendInstant(Observable<string> messages)
        {
            return Observable.Create<bool>(observer =>
            {
                var disposables = new CompositeDisposable();
                disposables.Add(messages.SubscribeAwait(async (msg, ct) =>
                {
                    var result = await client.SendInstantAsync(msg, ct).ConfigureAwait(false);
                    observer.OnNext(result);
                }));

                return disposables;
            });
        }

        public Observable<bool> SendAsBinary(Observable<byte[]> messages)
        {
            return Observable.Create<bool>(observer =>
            {
                var disposables = new CompositeDisposable();
                disposables.Add(messages.SubscribeAwait(async (msg, ct) =>
                {
                    var result = await client.SendAsBinaryAsync(msg, ct).ConfigureAwait(false);
                    observer.OnNext(result);
                }));

                return disposables;
            });
        }

        public Observable<bool> SendAsBinary(Observable<string> messages)
        {
            return Observable.Create<bool>(observer =>
            {
                var disposables = new CompositeDisposable();
                disposables.Add(messages.SubscribeAwait(async (msg, ct) =>
                {
                    var result = await client.SendAsBinaryAsync(msg, ct).ConfigureAwait(false);
                    observer.OnNext(result);
                }));

                return disposables;
            });
        }

        public Observable<bool> SendAsText(Observable<byte[]> messages)
        {
            return Observable.Create<bool>(observer =>
            {
                var disposables = new CompositeDisposable();
                disposables.Add(messages.SubscribeAwait(async (msg, ct) =>
                {
                    var result = await client.SendAsTextAsync(msg, ct).ConfigureAwait(false);
                    observer.OnNext(result);
                }));

                return disposables;
            });
        }

        public Observable<bool> SendAsText(Observable<string> messages)
        {
            return Observable.Create<bool>(observer =>
            {
                var disposables = new CompositeDisposable();
                disposables.Add(messages.SubscribeAwait(async (msg, ct) =>
                {
                    var result = await client.SendAsTextAsync(msg, ct).ConfigureAwait(false);
                    observer.OnNext(result);
                }));

                return disposables;
            });
        }

        public Observable<bool> TrySendAsBinary(Observable<string> messages)
        {
            return Observable.Create<bool>(observer =>
            {
                var disposables = new CompositeDisposable();
                disposables.Add(messages.Subscribe(msg =>
                {
                    var result = client.TrySendAsBinary(msg);
                    observer.OnNext(result);
                }));

                return disposables;
            });
        }

        public Observable<bool> TrySendAsBinary(Observable<byte[]> messages)
        {
            return Observable.Create<bool>(observer =>
            {
                var disposables = new CompositeDisposable();
                disposables.Add(messages.Subscribe(msg =>
                {
                    var result = client.TrySendAsBinary(msg);
                    observer.OnNext(result);
                }));

                return disposables;
            });
        }

        public Observable<bool> TrySendAsText(Observable<string> messages)
        {
            return Observable.Create<bool>(observer =>
            {
                var disposables = new CompositeDisposable();
                disposables.Add(messages.Subscribe(msg =>
                {
                    var result = client.TrySendAsText(msg);
                    observer.OnNext(result);
                }));

                return disposables;
            });
        }

        public Observable<bool> TrySendAsText(Observable<byte[]> messages)
        {
            return Observable.Create<bool>(observer =>
            {
                var disposables = new CompositeDisposable();
                disposables.Add(messages.Subscribe(msg =>
                {
                    var result = client.TrySendAsText(msg);
                    observer.OnNext(result);
                }));

                return disposables;
            });
        }
    }
}