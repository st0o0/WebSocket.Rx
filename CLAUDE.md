# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

WebSocket.Rx is a .NET 10 NuGet library providing reactive WebSocket client and server implementations built on **R3** (modern Reactive Extensions). It is AOT-compatible, uses `System.Threading.Channels` for thread-safe message queuing, and `ArrayPool<byte>` / `RecyclableMemoryStream` for memory efficiency.

## Build & Test Commands

All commands run from the repo root. The solution file is at `src/WebSocket.Rx.sln`.

```shell
# Restore (locked mode, matching CI)
dotnet restore --locked-mode src/WebSocket.Rx.sln

# Build
dotnet build --configuration Release src/WebSocket.Rx.sln

# Run all tests
dotnet test --configuration Release src/WebSocket.Rx.sln

# Run a single test by name
dotnet test --configuration Release src/WebSocket.Rx.UnitTests -- --filter "FullyQualifiedName~AsyncLockTests"

# Run only unit or integration tests
dotnet test --configuration Release src/WebSocket.Rx.UnitTests
dotnet test --configuration Release src/WebSocket.Rx.IntegrationTests

# Run benchmarks
dotnet run --configuration Release --project src/WebSocket.Rx.Benchmarks

# Slopwatch (code quality gate, also runs in CI)
dotnet tool restore
dotnet tool run slopwatch analyze --fail-on error
```

## Architecture

### Core Types (namespace `WebSocket.Rx`)

- **`ReactiveWebSocketClient`** — wraps `ClientWebSocket`. A dedicated receive loop reads frames and pushes them into R3 subjects; a dedicated send loop drains a `Channel<Payload>`. Exposes `Observable<Message>`, `Observable<Connected>`, `Observable<Disconnected>`, `Observable<ErrorOccurred>`.
- **`ReactiveWebSocketServer`** — wraps `HttpListener`. Each accepted WebSocket connection becomes a `ServerWebSocketAdapter` (extends `ReactiveWebSocketClient`). Tracks clients in a `ConcurrentDictionary<Guid, Client>`. Exposes server-level observables for client connect/disconnect, messages, and errors.
- **`Message`** (record) — either text (`ReadOnlyMemory<char>`) or binary (`ReadOnlyMemory<byte>`). Factory methods `Message.Create(...)`.
- **`Payload`** (readonly struct) — internal send-queue item, rents from `ArrayPool<byte>` and returns on disposal.
- **`Extensions`** — Rx operator extensions: `.Send()`, `.SendInstant()`, `.TrySend()`, `.BroadcastAsync()` etc.

### Concurrency Model

Send path: caller → `Channel<Payload>` (unbounded, single-reader) → send loop task → `ClientWebSocket.SendAsync`.
Receive path: receive loop task → `ClientWebSocket.ReceiveAsync` → R3 subject → subscriber observables.
Critical sections use a custom `AsyncLock` (internal, SemaphoreSlim-based with fast-path).

### Test Structure

- **UnitTests** — xUnit v3 + NSubstitute. Tests internal types like `AsyncLock`.
- **IntegrationTests** — full client↔server round-trips using `WebSocketTestServer` (in-process test harness). Categories: Lifecycle, Connection, Reconnection, Sending, Receiving, Stress, Broadcast, KeepAlive, Encoding, ErrorHandling, Dispose.
- Both test projects have `InternalsVisibleTo` access.

### Key Design Decisions

- **R3 instead of System.Reactive** — R3 is the reactive library; all observables are `R3.Observable<T>`, not `System.IObservable<T>`.
- **AOT-compatible** — `IsAotCompatible=true`, no reflection, uses ILCompiler + ILLink.
- **InvariantGlobalization** — no culture-specific data; text encoding is explicit via `MessageEncoding` property.
- **Locked restore** — `RestorePackagesWithLockFile=true` with `packages.lock.json`; CI runs `--locked-mode`.

## CI/CD

Versioning uses **Release Please** with conventional commits. Workflows are split:

- **`ci.yml`** — runs on PR and push to main: restore → slopwatch → build → test with coverage report
- **`release.yml`** — runs on push to main: Release Please creates/updates a release PR; on merge, builds, packs, publishes to NuGet, and creates a GitHub Release
- **`commitlint.yml`** — validates PR titles and commit messages against conventional commit format
- **`codeql.yml`** — CodeQL static analysis (weekly + on push/PR)

Commit messages must follow [Conventional Commits](https://www.conventionalcommits.org/): `feat:`, `fix:`, `perf:`, `docs:`, `chore:`, `refactor:`, `test:`, `ci:`, `build:`, `deps:`.
