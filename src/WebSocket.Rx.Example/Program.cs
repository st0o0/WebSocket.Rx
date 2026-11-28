using System.Net.WebSockets;
using R3;
using WebSocket.Rx;

using var client = new ReactiveWebSocketClient(new Uri("ws://localhost:8080"));
using var subscription = client.MessageReceived.Subscribe(message => Console.WriteLine(message));

await client.StartAsync().ConfigureAwait(false);
client.TrySend("ping".AsMemory(), WebSocketMessageType.Text);
await client.StopAsync(WebSocketCloseStatus.NormalClosure, "done").ConfigureAwait(false);
