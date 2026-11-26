using System.Net.WebSockets;
using R3;
using WebSocket.Rx;

var ws = new ClientWebSocket();
var observable = ws.ToObservable();
observable.Subscribe();
