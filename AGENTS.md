# AGENTS.md

## Project

WebSocket.Rx — .NET 10 NuGet library for reactive WebSocket client/server on
**R3** (modern Reactive Extensions, not System.Reactive). AOT-compatible, zero
reflection, `System.Threading.Channels` for send queuing, `ArrayPool<byte>` /
`RecyclableMemoryStream` for memory efficiency.

**Repository:** `github.com/st0o0/WebSocket.Rx` · **License:** MIT · **Target:** `net10.0`

## Solution structure

```
src/
  WebSocket.Rx.slnx
  WebSocket.Rx/                    # Library: client, server, messages, extensions
  WebSocket.Rx.UnitTests/          # xUnit v3 + NSubstitute
  WebSocket.Rx.IntegrationTests/   # Full client↔server round-trips
  WebSocket.Rx.Benchmarks/         # BenchmarkDotNet
```

## Architecture

### Send/Receive Model

```
Send:    caller → Channel<Payload> (unbounded, single-reader) → send loop → ClientWebSocket.SendAsync
Receive: receive loop → ClientWebSocket.ReceiveAsync → R3 Subject → subscriber observables
```

Critical sections use `AsyncLock` (internal, SemaphoreSlim-based with fast-path).

### Core Types (`WebSocket.Rx` namespace)

| Type | Role |
|------|------|
| `ReactiveWebSocketClient` | Wraps `ClientWebSocket`. Dedicated send/receive loops. Exposes `Observable<Message>`, `Observable<Connected>`, `Observable<Disconnected>`, `Observable<ErrorOccurred>` |
| `ReactiveWebSocketServer` | Wraps `HttpListener`. Each connection → `ServerWebSocketAdapter` (extends client). Tracks clients via `ConcurrentDictionary<Guid, Metadata>` |
| `Message` (record) | Text (`ReadOnlyMemory<char>`) or binary (`ReadOnlyMemory<byte>`). Factory: `Message.Create(...)` |
| `Payload` (readonly struct) | Send-queue item. Rents from `ArrayPool<byte>`, returns on `Dispose()` |
| `Extensions` | C# 14 extension blocks on `IReactiveWebSocketClient` / `IReactiveWebSocketServer`: `.Send()`, `.SendInstant()`, `.TrySend()`, `.BroadcastInstant()`, `.BroadcastAsync()`, `.TryBroadcast()` |

### Namespaces

- `WebSocket.Rx` — public API: interfaces, client, server, records, extensions
- `WebSocket.Rx.Internal` — internal helpers (`AsyncLock`, internal extensions).
  `InternalsVisibleTo` test projects.

## Architecture guardrails

- **R3 only.** All observables are `R3.Observable<T>`, never `System.IObservable<T>`.
  Never suggest System.Reactive patterns.
- **AOT non-negotiable.** `IsAotCompatible=true`, ILCompiler + ILLink. No
  reflection, no `Activator.CreateInstance`, no runtime code generation.
- **InvariantGlobalization.** No `CultureInfo`, no locale-sensitive string
  operations. Text encoding explicit via `MessageEncoding` property.
- **C# 14 extensions.** `Extensions.cs` uses `extension(T)` blocks, not
  `static class` with `this` params.
- **Locked restore.** `packages.lock.json` checked in; CI uses `--locked-mode`.
- **Send via Channel.** `Channel<Payload>` is the thread-safe send queue; never
  bypass it for direct `SendAsync` from caller code.
- **ArrayPool rentals.** `Payload` rents buffers; ensure all disposal paths
  return them via `ArrayPool<byte>.Shared.Return()`.
- **Interface-first API.** Public surface is `IReactiveWebSocketClient` /
  `IReactiveWebSocketServer`. Consumers program against the interface.
- **Central Package Management.** All package versions live in
  `Directory.Packages.props`. Do not add `Version=` attributes in csproj files.
- **Dependency-light library.** Do not add NuGet packages without good reason.
  This is a library consumed by others.

## C# conventions

- `sealed` by default, `record` for message types, nullable enabled everywhere.
- `var` when type is apparent.
- Private fields prefixed with `_fieldName`.
- No XML docs. Code speaks through naming.
- Never use `async void`, `.Result`, or `.Wait()`.
- Always pass `CancellationToken` through async call chains.

## Test conventions

### Unit tests

- **xUnit v3** — not v2. Uses `Assert`, not FluentAssertions.
- **NSubstitute** for mocking.
- **No `!.` in tests** — use `Assert.NotNull(value)` first, then access properties.
- **`Assert.Multiple`** for grouped assertions on the same object or outcome.
- **Sync tests:** plain `[Fact]` / `[Theory]` — no `Timeout` (synchronous tests
  can't be cancelled via `CancellationToken`, so the timeout is ineffective).
- **Async tests:** `[Fact(Timeout = N)]` with inline millisecond values + pass
  `TestContext.Current.CancellationToken` through all async call chains.
  No `DefaultTimeoutMs` constants — use inline values per test:
  - Unit tests: `5000` default, `10000` for concurrency, `30000` for stress.
  - Integration tests: `15000` default, `30000` for stress/reconnection.

### Integration tests

- Full client↔server round-trips via `WebSocketTestServer` (in-process).
- Split by concern: Lifecycle, Connection, Reconnection, Sending, Receiving,
  Stress, Broadcast, KeepAlive, Encoding, ErrorHandling, Dispose, ResourceLeak.
- Inherit `ReactiveWebSocketClientTestBase` or `ReactiveWebSocketServerTestBase`.
- Both projects have `InternalsVisibleTo` access.

### Assertion patterns

- **Never `Assert.True(true)`.** Use `Record.Exception` / `Record.ExceptionAsync`
  + `Assert.Null(exception)` for "should not throw" tests.
- **`Assert.Multiple`** when asserting multiple properties of the same result.
- **`Assert.Equal` over `Assert.True`** — prefer `Assert.Equal(expected, actual)`
  over `Assert.True(actual == expected)` for better failure messages.

## Commits & CI

**Conventional commits:** `feat:`, `fix:`, `perf:`, `docs:`, `chore:`,
`refactor:`, `test:`, `ci:`, `build:`, `deps:`.

Versioning: **Release Please** + conventional commits. Shared workflows from
`st0o0/github-workflows`.

| Workflow | Trigger | Purpose |
|----------|---------|---------|
| `ci.yml` | PR | slopwatch → restore → build → test with coverage |
| `release.yml` | push to main | Release Please PR; on merge: build → pack → NuGet publish → GitHub Release |
| `security.yml` | PR (deps changes) + weekly | Trivy filesystem scan |

## Build & test

All commands run from repo root:

```shell
dotnet restore --locked-mode src/WebSocket.Rx.slnx
dotnet build --configuration Release src/WebSocket.Rx.slnx
dotnet test --configuration Release src/WebSocket.Rx.slnx
dotnet test --configuration Release src/WebSocket.Rx.UnitTests -- --filter "FullyQualifiedName~AsyncLockTests"
dotnet test --configuration Release src/WebSocket.Rx.UnitTests
dotnet test --configuration Release src/WebSocket.Rx.IntegrationTests
dotnet run --configuration Release --project src/WebSocket.Rx.Benchmarks
dotnet tool restore && dotnet tool run slopwatch analyze --fail-on error
```

## Tech stack

- .NET 10 (`net10.0`), C# 14
- Central Package Management (`Directory.Packages.props`)
- R3 (reactive streams)
- xUnit v3 (`xunit.v3`), NSubstitute
- BenchmarkDotNet (benchmarks)
- SourceLink, embedded debug symbols
- RecyclableMemoryStream, ArrayPool<byte>
