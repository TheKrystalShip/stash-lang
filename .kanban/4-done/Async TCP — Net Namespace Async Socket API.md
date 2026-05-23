# Async TCP — Net Namespace Async Socket API

> **Status:** Draft
> **Created:** 2025-04-16
> **Purpose:** Add async TCP socket functions to the `net` namespace, mirroring the async WebSocket API pattern, with binary I/O support via the new `byte[]` type.

---

## Table of Contents

1. [Motivation](#1-motivation)
2. [Design Principles](#2-design-principles)
3. [API Surface](#3-api-surface)
4. [Function Specifications](#4-function-specifications)
5. [Model Types](#5-model-types)
6. [Semantics & Edge Cases](#6-semantics--edge-cases)
7. [Interaction with Existing Features](#7-interaction-with-existing-features)
8. [Cross-Platform Behavior](#8-cross-platform-behavior)
9. [Implementation Strategy](#9-implementation-strategy)
10. [Tooling Integration (LSP/DAP)](#10-tooling-integration-lspdap)
11. [Test Scenarios](#11-test-scenarios)
12. [Migration & Breaking Changes](#12-migration--breaking-changes)
13. [Future Work](#13-future-work)
14. [Decision Log](#14-decision-log)

---

## 1. Motivation

### The Problem

The current `net.tcp*` functions are entirely synchronous and blocking. A `net.tcpRecv` call blocks the calling thread until data arrives or the connection is closed — there is no timeout, no cancellation, and no way to do concurrent I/O on multiple connections.

Meanwhile, the WebSocket API (`net.ws*`) was designed async-first: `wsConnect`, `wsSend`, `wsRecv`, and `wsClose` all return `StashFuture`, enabling `await`, `task.all`, and cooperative concurrency. This asymmetry means:

- **NATS clients** (text protocol over TCP) cannot maintain persistent connections with concurrent send/receive.
- **SMTP with STARTTLS** cannot do non-blocking handshakes.
- **Custom TCP protocol agents** (monitoring, health-checking, data collection) cannot multiplex connections.
- **TCP servers** that need to handle more than one connection are impossible — `tcpListen` accepts exactly one client and blocks.

### What Exists Today

| Function                               | Sync/Async | Returns            | Cancellable  |
| -------------------------------------- | ---------- | ------------------ | ------------ |
| `net.tcpConnect(host, port, ?timeout)` | **Sync**   | `TcpConnection`    | Timeout only |
| `net.tcpSend(conn, data)`              | **Sync**   | `int` (bytes sent) | No           |
| `net.tcpRecv(conn, ?maxBytes)`         | **Sync**   | `string`           | No           |
| `net.tcpClose(conn)`                   | **Sync**   | `null`             | N/A          |
| `net.tcpListen(port, handler)`         | **Sync**   | `null`             | No           |

### What We Need

Async counterparts that return `StashFuture`, support the `byte[]` type for binary protocols, provide timeouts and cancellation, and enable multi-client TCP servers.

---

## 2. Design Principles

1. **Mirror the WebSocket pattern.** The `ws*` functions establish the async networking idiom in Stash. Async TCP must follow the same `Task.Run<object?>` → `StashFuture(task, cts)` pattern. Deviating would confuse users and create maintenance burden.

2. **Binary-first.** Now that `byte` and `byte[]` are first-class types with the `buf` namespace, async TCP should support binary I/O natively — not just UTF-8 strings. This is the primary reason async TCP was deferred until after the byte type shipped. Text convenience wrappers are fine, but the core must work with bytes.

3. **Don't break existing sync API.** The sync `tcp*` functions remain untouched. Async variants are new, additive functions. Scripts using sync TCP continue to work.

4. **Composable with `task.*`.** Async TCP futures must work with `task.all`, `task.race`, `task.awaitAll`, `task.timeout`, and `task.cancel` — no special-casing.

5. **Cross-platform.** .NET's `TcpClient`/`TcpListener` + async APIs are cross-platform. No platform-specific code paths needed.

---

## 3. API Surface

### New Functions (9)

| Function                                    | Returns                 | Description                                    |
| ------------------------------------------- | ----------------------- | ---------------------------------------------- |
| `net.tcpConnectAsync(host, port, ?options)` | `Future<TcpConnection>` | Async connection with optional timeout and TLS |
| `net.tcpSendAsync(conn, data)`              | `Future<int>`           | Async string send                              |
| `net.tcpSendBytesAsync(conn, data)`         | `Future<int>`           | Async binary send (`byte[]`)                   |
| `net.tcpRecvAsync(conn, ?options)`          | `Future<string\|null>`  | Async string receive with timeout              |
| `net.tcpRecvBytesAsync(conn, ?options)`     | `Future<byte[]\|null>`  | Async binary receive with timeout              |
| `net.tcpCloseAsync(conn)`                   | `Future<null>`          | Async graceful close                           |
| `net.tcpListenAsync(port, handler)`         | `Future<null>`          | Async multi-client server                      |
| `net.tcpIsOpen(conn)`                       | `bool`                  | Sync connection state check                    |
| `net.tcpState(conn)`                        | `TcpConnectionState`    | Sync connection state                          |

### Unchanged Sync Functions (5)

`net.tcpConnect`, `net.tcpSend`, `net.tcpRecv`, `net.tcpClose`, `net.tcpListen` — remain as-is.

---

## 4. Function Specifications

### 4.1 `net.tcpConnectAsync(host, port, ?options)`

**Signature:** `net.tcpConnectAsync(host: string, port: int, options?: TcpConnectOptions) → Future<TcpConnection>`

**Behavior:**

- Creates a TCP connection asynchronously.
- Returns a `StashFuture` that resolves to a `TcpConnection` struct.
- Respects interpreter cancellation via linked `CancellationTokenSource`.
- On failure, the future rejects with a descriptive `RuntimeError`.

**Options struct:**

```stash
struct TcpConnectOptions {
    timeout: int       // Connection timeout in ms (default: 5000)
    tls: bool          // Enable TLS wrapping (default: false) — DEFERRED, see §13
    noDelay: bool      // Disable Nagle's algorithm (default: false)
    keepAlive: bool    // Enable TCP keep-alive (default: false)
}
```

> **Decision:** Options use a struct, not positional args. The sync `tcpConnect` takes `timeout` as a 3rd positional arg — that was a mistake we're correcting here. A struct is extensible (TLS, keep-alive) without growing the parameter list. See Decision Log §14.1.

**Example:**

```stash
let conn = await net.tcpConnectAsync("redis.local", 6379);

// With options:
let conn = await net.tcpConnectAsync("nats.local", 4222, TcpConnectOptions {
    timeout: 3000,
    noDelay: true,
    keepAlive: true,
});
```

**Error cases:**

- Host unreachable → `"net.tcpConnectAsync: failed to connect to 'host:port': <message>"`
- Port out of range → `"net.tcpConnectAsync: port must be between 1 and 65535."`
- Timeout → `"net.tcpConnectAsync: connection timed out after <n>ms."`
- Cancelled → `"Future was cancelled."` (standard StashFuture cancellation)

**C# pattern:**

```csharp
var cts = CancellationTokenSource.CreateLinkedTokenSource(ctx.CancellationToken);
cts.CancelAfter(timeout);
var dotnetTask = Task.Run<object?>(async () =>
{
    var client = new TcpClient();
    if (noDelay) client.NoDelay = true;
    if (keepAlive) client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
    await client.ConnectAsync(host, port, cts.Token);
    int localPort = ((IPEndPoint)client.Client.LocalEndPoint!).Port;
    var conn = new StashInstance("TcpConnection", ...);
    _tcpClients.AddOrUpdate(conn, client);
    return (object?)conn;
});
return StashValue.FromObj(new StashFuture(dotnetTask, cts));
```

---

### 4.2 `net.tcpSendAsync(conn, data)`

**Signature:** `net.tcpSendAsync(conn: TcpConnection, data: string) → Future<int>`

**Behavior:**

- Encodes `data` as UTF-8 and sends over the connection's `NetworkStream`.
- Returns a `StashFuture` resolving to the number of bytes sent.

**Example:**

```stash
let sent = await net.tcpSendAsync(conn, "PING\r\n");
io.println("Sent ${sent} bytes");
```

**Error cases:**

- Connection closed/invalid → `"net.tcpSendAsync: invalid or closed TcpConnection."`
- Write failure → `"net.tcpSendAsync: send failed: <message>"`

---

### 4.3 `net.tcpSendBytesAsync(conn, data)`

**Signature:** `net.tcpSendBytesAsync(conn: TcpConnection, data: byte[]) → Future<int>`

**Behavior:**

- Sends raw bytes over the connection. Uses `StashByteArray.AsSpan()` for zero-copy write to `NetworkStream`.
- Returns bytes sent.

**Example:**

```stash
// Build a Redis RESP command manually
let cmd = buf.from("*1\r\n$4\r\nPING\r\n");
let sent = await net.tcpSendBytesAsync(conn, cmd);
```

**Error cases:**

- `data` is not `byte[]` → `"net.tcpSendBytesAsync: second argument must be a byte[]."`
- Same connection/write errors as `tcpSendAsync`.

---

### 4.4 `net.tcpRecvAsync(conn, ?options)`

**Signature:** `net.tcpRecvAsync(conn: TcpConnection, options?: TcpRecvOptions) → Future<string|null>`

**Behavior:**

- Reads up to `maxBytes` from the connection asynchronously.
- Returns UTF-8 decoded string, or `null` on timeout.
- A zero-byte read (peer closed connection) returns `""` (empty string) — distinct from timeout's `null`.

**Options struct:**

```stash
struct TcpRecvOptions {
    maxBytes: int      // Max bytes to read (default: 4096)
    timeout: int       // Receive timeout in ms (default: 30000)
}
```

**Example:**

```stash
let data = await net.tcpRecvAsync(conn);
if data == null {
    io.println("Timed out");
} else if data == "" {
    io.println("Connection closed by peer");
} else {
    io.println("Got: ${data}");
}

// With options:
let data = await net.tcpRecvAsync(conn, TcpRecvOptions { maxBytes: 8192, timeout: 5000 });
```

**C# pattern:**

```csharp
var dotnetTask = Task.Run<object?>(async () =>
{
    var stream = client.GetStream();
    var buffer = new byte[maxBytes];
    using var timeoutCts = new CancellationTokenSource(timeout);
    using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cts.Token, timeoutCts.Token);
    try
    {
        int bytesRead = await stream.ReadAsync(buffer, 0, maxBytes, linkedCts.Token);
        return (object?)Encoding.UTF8.GetString(buffer, 0, bytesRead);
    }
    catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
    {
        return null; // Timeout — not an error
    }
});
```

> **Decision:** Timeout returns `null` (not an exception). This matches the WebSocket `wsRecv` pattern where timeout returns `null` and is a normal flow control mechanism, not an error. See Decision Log §14.2.

---

### 4.5 `net.tcpRecvBytesAsync(conn, ?options)`

**Signature:** `net.tcpRecvBytesAsync(conn: TcpConnection, options?: TcpRecvOptions) → Future<byte[]|null>`

**Behavior:**

- Same as `tcpRecvAsync` but returns raw `byte[]` (a `StashByteArray`) instead of decoding to UTF-8.
- Returns `null` on timeout.
- A zero-byte read returns an empty `byte[]`.

**Example:**

```stash
let data = await net.tcpRecvBytesAsync(conn, TcpRecvOptions { maxBytes: 1024 });
if data == null {
    io.println("Timed out");
} else {
    io.println("Received ${buf.len(data)} bytes");
    // Parse binary protocol frame...
    let msgLen = buf.readUint32BE(data, 0);
}
```

**C# pattern:**

```csharp
int bytesRead = await stream.ReadAsync(buffer, 0, maxBytes, linkedCts.Token);
if (bytesRead == 0) return (object?)new StashByteArray(Array.Empty<byte>());
return (object?)new StashByteArray(buffer.AsSpan(0, bytesRead).ToArray());
```

---

### 4.6 `net.tcpCloseAsync(conn)`

**Signature:** `net.tcpCloseAsync(conn: TcpConnection) → Future<null>`

**Behavior:**

- Gracefully shuts down the connection (shutdown send, then close).
- Removes the client from the `ConditionalWeakTable`.
- Idempotent — closing an already-closed connection is not an error (returns resolved `null`).

**Example:**

```stash
await net.tcpCloseAsync(conn);
```

**C# pattern:**

```csharp
var dotnetTask = Task.Run<object?>(async () =>
{
    if (_tcpAsyncClients.TryGetValue(conn, out TcpClient? client))
    {
        try { client.Client.Shutdown(SocketShutdown.Both); } catch { }
        client.Dispose();
        _tcpAsyncClients.Remove(conn);
    }
    return null;
});
```

> **Decision:** Async close uses `Shutdown(Both)` + `Dispose()` for graceful TCP teardown. The sync `tcpClose` only calls `Dispose()` — we don't change that. See Decision Log §14.3.

---

### 4.7 `net.tcpListenAsync(port, handler)`

**Signature:** `net.tcpListenAsync(port: int, handler: fn(TcpConnection)) → Future<TcpServer>`

**Behavior:**

- Binds a TCP listener on the given port.
- Returns a `StashFuture` that resolves to a `TcpServer` handle **immediately after the listener starts**.
- Spawns an accept loop in the background that calls `handler` for each incoming connection.
- Each accepted connection gets its own forked interpreter context (`ctx.Fork()`).
- The accept loop runs until `net.tcpServerClose(server)` is called or the interpreter is cancelled.

**Example:**

```stash
let server = await net.tcpListenAsync(8080, fn(conn) {
    let request = await net.tcpRecvAsync(conn);
    await net.tcpSendAsync(conn, "HTTP/1.1 200 OK\r\n\r\nHello\n");
    await net.tcpCloseAsync(conn);
});

io.println("Listening on port 8080...");
// ... do other work ...

// When done:
net.tcpServerClose(server);
```

**C# pattern:**

```csharp
var listener = new TcpListener(IPAddress.Any, port);
listener.Start();

var serverInst = new StashInstance("TcpServer", new Dictionary<string, StashValue>
{
    ["port"] = StashValue.FromInt(port),
    ["active"] = StashValue.True,
});

// Store listener for later cleanup
_tcpServers.AddOrUpdate(serverInst, listener);

// Spawn accept loop
_ = Task.Run(async () =>
{
    try
    {
        while (!cts.Token.IsCancellationRequested)
        {
            var clientSocket = await listener.AcceptTcpClientAsync(cts.Token);
            var connInst = BuildTcpConnectionInstance(clientSocket);
            _tcpAsyncClients.AddOrUpdate(connInst, clientSocket);

            // Fire-and-forget: handle each connection in its own forked context
            var forkedCtx = ctx.Fork(cts.Token);
            _ = Task.Run(() =>
            {
                try { forkedCtx.InvokeCallbackDirect(handler, [StashValue.FromObj(connInst)]); }
                catch (Exception ex) { /* log or swallow — handler errors don't kill the server */ }
            });
        }
    }
    catch (OperationCanceledException) { /* normal shutdown */ }
    finally { listener.Stop(); }
});

// Resolve immediately with the server handle
return StashValue.FromObj(new StashFuture(Task.FromResult<object?>(serverInst), cts));
```

> **Decision:** `tcpListenAsync` returns a `TcpServer` handle, not a raw future-that-never-resolves. This is a departure from the single-shot sync `tcpListen` and follows the pattern of server frameworks. The accept loop is background work; the future resolves immediately. See Decision Log §14.4.

---

### 4.8 `net.tcpServerClose(server)`

**Signature:** `net.tcpServerClose(server: TcpServer) → null`

**Behavior:**

- Stops the accept loop and closes the listener.
- Does **not** close existing accepted connections — those must be closed individually.
- Synchronous (stopping a listener is fast).
- Idempotent.

**Example:**

```stash
net.tcpServerClose(server);
```

---

### 4.9 `net.tcpIsOpen(conn)` and `net.tcpState(conn)`

**Signatures:**

- `net.tcpIsOpen(conn: TcpConnection) → bool`
- `net.tcpState(conn: TcpConnection) → TcpConnectionState`

**Behavior:**

- Synchronous state queries, mirroring `net.wsIsOpen` / `net.wsState`.
- `tcpIsOpen` returns `true` if the underlying socket is connected.
- `tcpState` returns a `TcpConnectionState` enum value.

> **Note:** These work for both sync and async TCP connections. The underlying `TcpClient.Connected` property is checked.

**Example:**

```stash
if net.tcpIsOpen(conn) {
    await net.tcpSendAsync(conn, "PING\r\n");
}

match net.tcpState(conn) {
    TcpConnectionState.Open => io.println("Connected"),
    TcpConnectionState.Closed => io.println("Disconnected"),
}
```

---

## 5. Model Types

### New Structs

```stash
struct TcpConnectOptions {
    timeout: int       // Connection timeout in ms (default: 5000)
    noDelay: bool      // Disable Nagle's algorithm (default: false)
    keepAlive: bool    // Enable TCP keep-alive (default: false)
}

struct TcpRecvOptions {
    maxBytes: int      // Max bytes to read (default: 4096)
    timeout: int       // Receive timeout in ms (default: 30000)
}

struct TcpServer {
    port: int          // Listening port
    active: bool       // Whether the server is accepting connections
}
```

### New Enum

```stash
enum TcpConnectionState {
    Open,
    Closed,
}
```

> **Decision:** Only two states (Open/Closed), not four like WebSocket. TCP doesn't have a visible "Connecting" or "Closing" state from the user's perspective — the future resolves when connected, and close is immediate. WebSocket needs Connecting/Closing because those are observable protocol states during the WS handshake. See Decision Log §14.5.

### Existing Struct (unchanged)

```stash
struct TcpConnection {
    host: string
    port: int
    localPort: int
}
```

The `TcpConnection` struct is shared between sync and async functions. Internally, async connections store the `TcpClient` in a `ConditionalWeakTable` (matching the WebSocket pattern), while sync connections use the hidden `_client` field. Both patterns resolve to the same `TcpClient` — the helper function checks both locations.

---

## 6. Semantics & Edge Cases

### 6.1 Timeout vs Cancellation vs Connection Close

Three distinct conditions during `tcpRecvAsync`:

| Condition                      | Return Value           | Future State | User Action                 |
| ------------------------------ | ---------------------- | ------------ | --------------------------- |
| Data received                  | `string` or `byte[]`   | Resolved     | Process data                |
| Timeout (no data within limit) | `null`                 | Resolved     | Retry or close              |
| Connection closed by peer      | `""` or empty `byte[]` | Resolved     | Clean up                    |
| Interpreter cancelled          | N/A                    | Faulted      | `"Future was cancelled."`   |
| I/O error                      | N/A                    | Faulted      | `RuntimeError` with message |

### 6.2 Send on Closed Connection

Calling `tcpSendAsync` or `tcpSendBytesAsync` on a closed connection rejects the future with `"net.tcpSendAsync: invalid or closed TcpConnection."`. It does **not** throw synchronously — the error surfaces when the future is awaited.

### 6.3 Double Close

`tcpCloseAsync` is idempotent. Closing an already-closed connection resolves to `null` without error.

### 6.4 Connection Lifetime and GC

Async TCP connections use `ConditionalWeakTable<StashInstance, TcpClient>`. If the script drops all references to a `TcpConnection` without closing it:

- The `StashInstance` becomes GC-eligible.
- The `ConditionalWeakTable` releases the `TcpClient`.
- The `TcpClient` finalizer closes the socket.

This is the same behavior as WebSocket connections. It's not ideal (sockets may linger until GC), but it prevents resource leaks in the common case.

### 6.5 Concurrent Reads/Writes

.NET's `NetworkStream` supports one concurrent read and one concurrent write, but not multiple concurrent reads or writes. The async TCP functions do **not** add synchronization — this matches the WebSocket behavior. If a script issues two concurrent `tcpRecvAsync` calls on the same connection, behavior is undefined (likely data corruption or an exception from .NET). This is documented but not enforced.

### 6.6 Max Receive Buffer

`maxBytes` in `TcpRecvOptions` has a hard upper limit of **16 MB** (16,777,216 bytes), matching the WebSocket receive limit. Values above this are clamped. This prevents accidental OOM from `TcpRecvOptions { maxBytes: math.maxInt }`.

### 6.7 Empty `TcpConnectOptions` / `TcpRecvOptions`

All fields in option structs have defaults. Passing the struct with no fields overridden is equivalent to not passing options at all:

```stash
// These are equivalent:
await net.tcpRecvAsync(conn);
await net.tcpRecvAsync(conn, TcpRecvOptions {});
```

---

## 7. Interaction with Existing Features

### 7.1 `task.*` Namespace

All async TCP functions return `StashFuture` and compose naturally:

```stash
// Race: first server to respond wins
let result = await task.race([
    net.tcpRecvAsync(primaryConn),
    net.tcpRecvAsync(fallbackConn),
]);

// Timeout wrapper:
let data = await task.timeout(5000, fn() {
    return await net.tcpRecvAsync(conn);
});

// Parallel sends:
await task.all([
    net.tcpSendAsync(conn1, "PING\r\n"),
    net.tcpSendAsync(conn2, "PING\r\n"),
    net.tcpSendAsync(conn3, "PING\r\n"),
]);

// Cancel a stuck receive:
let future = net.tcpRecvAsync(conn);
task.cancel(future);
```

### 7.2 `try/catch` Error Handling

Errors from async TCP surface at the `await` point:

```stash
try {
    let conn = await net.tcpConnectAsync("unreachable.host", 9999);
} catch e {
    io.println("Connection failed: ${e.message}");
}
```

### 7.3 `buf.*` Namespace (Binary I/O)

The `byte[]` variants integrate directly with `buf.*`:

```stash
// Build a binary protocol frame
let header = buf.alloc(4);
buf.writeUint32BE(header, 0, str.len(payload));
let frame = buf.concat(header, buf.from(payload));
await net.tcpSendBytesAsync(conn, frame);

// Parse a binary response
let data = await net.tcpRecvBytesAsync(conn, TcpRecvOptions { maxBytes: 8192 });
let msgType = buf.readUint8(data, 0);
let msgLen = buf.readUint32BE(data, 1);
let body = buf.slice(data, 5, 5 + msgLen);
```

### 7.4 UFCS

Async TCP functions work with UFCS since the first argument is a `TcpConnection`:

```stash
let data = await conn.tcpRecvAsync();
await conn.tcpSendAsync("PONG\r\n");
await conn.tcpCloseAsync();
```

> **Note:** UFCS resolves `conn.tcpRecvAsync()` to `net.tcpRecvAsync(conn)` because `conn` is a `TcpConnection` from the `net` namespace.

### 7.5 Async Functions (`async fn`)

Async TCP functions can be called from both sync and async contexts (they return futures that can be `await`-ed anywhere). Common pattern for protocol clients:

```stash
async fn redisCommand(conn, cmd) {
    await net.tcpSendAsync(conn, cmd + "\r\n");
    return await net.tcpRecvAsync(conn, TcpRecvOptions { timeout: 5000 });
}
```

### 7.6 Sync TCP Functions

Sync and async functions share the `TcpConnection` struct. However, a connection created with `net.tcpConnect` (sync) stores the `TcpClient` in a hidden `_client` field, while a connection created with `net.tcpConnectAsync` stores it in a `ConditionalWeakTable`.

The async functions check **both** locations, so you can:

```stash
// Create sync, use async for I/O
let conn = net.tcpConnect("localhost", 6379);
let data = await net.tcpRecvAsync(conn);  // Works — checks _client field as fallback
```

This provides a smooth migration path from sync to async.

---

## 8. Cross-Platform Behavior

### Linux / macOS / Windows

All async TCP functions use .NET's `TcpClient`, `TcpListener`, and `NetworkStream` async methods, which are cross-platform. No platform-specific behavior expected.

### Platform-Specific Notes

| Aspect               | Linux                      | macOS                     | Windows               |
| -------------------- | -------------------------- | ------------------------- | --------------------- |
| `SO_KEEPALIVE`       | Kernel defaults (~2h)      | Kernel defaults (~2h)     | Registry-configurable |
| `TCP_NODELAY`        | Standard                   | Standard                  | Standard              |
| Max connections      | `ulimit -n` (default 1024) | `ulimit -n` (default 256) | No practical limit    |
| Ephemeral port range | 32768–60999                | 49152–65535               | 49152–65535           |

No Stash-level abstraction needed for these differences — they're OS-level and affect all TCP software equally.

---

## 9. Implementation Strategy

### 9.1 Files Changed

| File                                         | Change                                                                                                 |
| -------------------------------------------- | ------------------------------------------------------------------------------------------------------ |
| `Stash.Stdlib/BuiltIns/NetBuiltIns.cs`       | Add 9 new functions, 3 new structs, 1 new enum, connection storage table                               |
| `Stash.Stdlib/BuiltIns/Models/NetModels.cs`  | Add `TcpConnectOptions`, `TcpRecvOptions`, `TcpServer` struct registrations, `TcpConnectionState` enum |
| `docs/Stash — Standard Library Reference.md` | Document all new functions, structs, enum                                                              |
| `Stash.Tests/`                               | New test file `AsyncTcpBuiltInsTests.cs`                                                               |

### 9.2 Files NOT Changed

| File              | Reason                                                                          |
| ----------------- | ------------------------------------------------------------------------------- |
| `Stash.Core/`     | No new AST nodes, no parser changes, no type system changes                     |
| `Stash.Bytecode/` | No new opcodes — async TCP uses existing `StashFuture` + `await` infrastructure |
| `Stash.Analysis/` | No new diagnostics needed — type checking of arguments happens at runtime       |
| `Stash.Lsp/`      | Completions auto-discovered from stdlib metadata                                |
| `Stash.Dap/`      | No debugging changes needed                                                     |

### 9.3 Connection Storage

Add a new `ConditionalWeakTable` for async TCP connections, separate from the WebSocket table:

```csharp
private static readonly ConditionalWeakTable<StashInstance, TcpClient> _tcpAsyncClients = new();
private static readonly ConditionalWeakTable<StashInstance, TcpListener> _tcpServers = new();
```

### 9.4 Helper: Resolve TcpClient from Connection

A shared helper that works for both sync and async connections:

```csharp
private static TcpClient GetTcpClient(StashInstance conn, string funcName)
{
    // Try async storage first (ConditionalWeakTable)
    if (_tcpAsyncClients.TryGetValue(conn, out TcpClient? asyncClient))
        return asyncClient;

    // Fall back to sync hidden field
    var clientField = conn.GetField("_client", null);
    if (clientField.ToObject() is TcpClient syncClient)
        return syncClient;

    throw new RuntimeError($"{funcName}: invalid or closed TcpConnection.");
}
```

### 9.5 Implementation Order

1. **Struct/enum registration** — `TcpConnectOptions`, `TcpRecvOptions`, `TcpServer`, `TcpConnectionState`
2. **`tcpConnectAsync`** — foundation; needed to test everything else
3. **`tcpSendAsync`** + **`tcpSendBytesAsync`** — send path
4. **`tcpRecvAsync`** + **`tcpRecvBytesAsync`** — receive path
5. **`tcpCloseAsync`** — cleanup
6. **`tcpIsOpen`** + **`tcpState`** — state queries
7. **`tcpListenAsync`** + **`tcpServerClose`** — server (most complex, do last)
8. **Tests** — parallel with each function
9. **Docs** — after all functions are implemented and tested

---

## 10. Tooling Integration (LSP/DAP)

### LSP

- **Completions:** Auto-discovered from stdlib metadata. No manual registration needed. The `net.tcp*Async` functions appear in completions when typing `net.tcp`.
- **Hover:** Function documentation (the `documentation:` parameter in `ns.Function(...)`) provides hover info.
- **Signature help:** Parameter names and types from `Param(...)` definitions drive signature help.
- **Diagnostics:** No new static analysis diagnostics. Argument validation is runtime-only (consistent with all other stdlib functions).

### DAP

- **Step-over `await`:** The existing `await` handling in the DAP pauses at the `await` expression and steps over the future resolution. No changes needed.
- **Variable inspection:** `TcpConnection`, `TcpServer`, and option structs display as normal `StashInstance` values in the debugger. Hidden fields (`_client`) are not displayed.

---

## 11. Test Scenarios

### Unit Tests (loopback, no external dependencies)

Tests use `tcpListenAsync` on loopback to create a local server, then connect to it with `tcpConnectAsync`.

| #   | Test                                                | Category          |
| --- | --------------------------------------------------- | ----------------- |
| 1   | `TcpConnectAsync_Loopback_ReturnsConnection`        | Happy path        |
| 2   | `TcpConnectAsync_InvalidPort_Throws`                | Validation        |
| 3   | `TcpConnectAsync_UnreachableHost_TimesOut`          | Timeout           |
| 4   | `TcpConnectAsync_WithOptions_AppliesSettings`       | Options           |
| 5   | `TcpSendAsync_StringData_ReturnsByteCount`          | Send              |
| 6   | `TcpSendAsync_ClosedConnection_Throws`              | Error             |
| 7   | `TcpSendBytesAsync_ByteArray_ReturnsByteCount`      | Binary send       |
| 8   | `TcpSendBytesAsync_NonByteArray_Throws`             | Validation        |
| 9   | `TcpRecvAsync_ReceivesData_ReturnsString`           | Receive           |
| 10  | `TcpRecvAsync_Timeout_ReturnsNull`                  | Timeout           |
| 11  | `TcpRecvAsync_PeerClosed_ReturnsEmptyString`        | Close detection   |
| 12  | `TcpRecvAsync_WithMaxBytes_RespectsLimit`           | Options           |
| 13  | `TcpRecvBytesAsync_ReceivesBinary_ReturnsByteArray` | Binary receive    |
| 14  | `TcpRecvBytesAsync_Timeout_ReturnsNull`             | Timeout           |
| 15  | `TcpCloseAsync_OpenConnection_Succeeds`             | Close             |
| 16  | `TcpCloseAsync_AlreadyClosed_Idempotent`            | Idempotent        |
| 17  | `TcpIsOpen_OpenConnection_ReturnsTrue`              | State             |
| 18  | `TcpIsOpen_ClosedConnection_ReturnsFalse`           | State             |
| 19  | `TcpState_OpenConnection_ReturnsOpen`               | State             |
| 20  | `TcpState_ClosedConnection_ReturnsClosed`           | State             |
| 21  | `TcpListenAsync_AcceptsMultipleClients`             | Server            |
| 22  | `TcpListenAsync_HandlerError_DoesNotKillServer`     | Server resilience |
| 23  | `TcpServerClose_StopsAccepting`                     | Server shutdown   |
| 24  | `TcpConnectAsync_SyncConnection_WorksWithAsyncRecv` | Interop           |
| 25  | `TcpRecvAsync_Cancel_FutureRejectsCancelled`        | Cancellation      |
| 26  | `TcpRecvBytesAsync_MaxBytesExceedsLimit_Clamped`    | 16MB guard        |

### Integration Test Patterns

These are patterns for manual testing or package-level integration tests, not xUnit:

```stash
// Echo server
let server = await net.tcpListenAsync(0, fn(conn) {
    while net.tcpIsOpen(conn) {
        let data = await net.tcpRecvAsync(conn, TcpRecvOptions { timeout: 5000 });
        if data == null || data == "" { break; }
        await net.tcpSendAsync(conn, data);
    }
    await net.tcpCloseAsync(conn);
});

// NATS PING/PONG
let conn = await net.tcpConnectAsync("localhost", 4222);
let info = await net.tcpRecvAsync(conn); // Server sends INFO on connect
await net.tcpSendAsync(conn, "PING\r\n");
let pong = await net.tcpRecvAsync(conn, TcpRecvOptions { timeout: 2000 });
assert(str.startsWith(pong, "PONG"), "Expected PONG");
await net.tcpCloseAsync(conn);
```

---

## 12. Migration & Breaking Changes

**No breaking changes.** All existing `net.tcp*` functions are preserved. The async variants are purely additive.

The only behavioral note: `net.tcpIsOpen` and `net.tcpState` are new functions that work with **both** sync and async connections. Scripts that use sync TCP can start using these immediately without any other changes.

---

## 13. Future Work

### TLS for TCP (separate spec)

The `TcpConnectOptions.tls` field is defined but **deferred** in this spec. TLS wrapping requires:

- `SslStream` wrapping around `NetworkStream`
- Server name indication (SNI)
- Custom CA certificates (see Gap Analysis §Gap 6)
- Client certificate support (mTLS)

This is a separate spec: **"TLS for TCP — Secure Socket Layer for Raw TCP Connections"**. The `tls` field in `TcpConnectOptions` is reserved for it. In this implementation, passing `tls: true` should throw: `"net.tcpConnectAsync: TLS is not yet supported. See future release."`.

### `net.tcpRecvUntil(conn, delimiter)`

A convenience function that buffers reads until a delimiter is found (e.g., `\r\n` for line-based protocols). Useful for RESP, SMTP, NATS. Deferred because it requires internal buffering state that complicates the API.

### `net.tcpRecvExact(conn, numBytes)`

Reads exactly N bytes, looping internally. Useful for binary protocol framing where you know the exact message size from a header. Deferred to keep the initial API minimal.

### UDP Async

`net.udpSendAsync` / `net.udpRecvAsync` — same pattern. Lower priority since UDP is less common for persistent protocol clients.

---

## 14. Decision Log

### 14.1 Options Struct vs Positional Args

**Decision:** Use `TcpConnectOptions` struct for optional parameters.

**Alternatives:**

- Positional args (like sync `tcpConnect(host, port, timeout)`) — doesn't scale to TLS, keepAlive, noDelay.
- Dictionary bag — violates the project's "structs over dicts" convention.

**Rationale:** Structs are extensible, self-documenting, and enforce the project's design philosophy. The sync `tcpConnect` using a positional `timeout` was a pragmatic choice for a 1-parameter optional, but async needs more options from day one.

### 14.2 Timeout Returns `null` (Not Exception)

**Decision:** `tcpRecvAsync` timeout resolves to `null`.

**Alternatives:**

- Throw `TimeoutError` — makes timeout handling require try/catch.
- Return a tagged union / result type — overengineered for this use case.

**Rationale:** Matches `wsRecv` behavior. Timeouts are normal in network programming (polling loops, heartbeat checks). Forcing try/catch for normal flow control is hostile to scripting.

### 14.3 Graceful Shutdown in `tcpCloseAsync`

**Decision:** `tcpCloseAsync` calls `Shutdown(Both)` before `Dispose()`.

**Alternatives:**

- Just `Dispose()` (like sync `tcpClose`) — may cause RST instead of FIN.
- Add shutdown options (linger timeout, half-close) — overengineered for v1.

**Rationale:** Graceful shutdown (FIN instead of RST) is the correct default for protocol clients. The peer gets a clean close notification. `Shutdown` can throw if the socket is already dead — that's caught and ignored.

### 14.4 `tcpListenAsync` Returns `TcpServer` Handle

**Decision:** Returns a `TcpServer` handle immediately; accept loop runs in background.

**Alternatives:**

- Return a future that never resolves (runs forever) — confusing semantics, can't get port info.
- Callback-per-connection sync model (like current `tcpListen`) — blocks, single client only.
- Generator/iterator model — language doesn't support generators yet.

**Rationale:** The server handle pattern is standard (Node.js `net.createServer`, Go `net.Listen`). It gives the script control over the server lifecycle, provides the listening port, and doesn't block. The accept loop is an implementation detail.

### 14.5 Two-State `TcpConnectionState` Enum

**Decision:** `Open` and `Closed` only.

**Alternatives:**

- Four states like WebSocket (`Connecting`, `Open`, `Closing`, `Closed`) — TCP's connecting phase is hidden inside the future; closing is instantaneous.

**Rationale:** WebSocket needs `Connecting`/`Closing` because the WS handshake and close handshake are multi-step and observable. TCP connection setup is behind `tcpConnectAsync` (the future resolves only when connected), and `tcpCloseAsync` resolves only when closed. There's no observable intermediate state from Stash-level code.

---
