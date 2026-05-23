# WebSocket Client — `net.ws*` Stdlib Addition

> **Document type:** Implementation Spec
> **Status:** Draft (Orchestrator-ready pending approval)
> **Created:** 2026-04-14
> **Parent:** Message Queue Integration — Design Options Analysis.md
> **Motivation:** WebSocket client support in the `net` namespace enables persistent bidirectional connections — unlocking STOMP-based message queue consumers, real-time service integrations (Slack, GitHub, monitoring dashboards), and any WebSocket-based API.

---

## 1. Scope

Add **7 functions**, **2 structs**, and **1 enum** to the existing `net` namespace. This is a **client-only** WebSocket implementation — no server-side `accept`/`upgrade` support.

### What's in scope

- Connect to `ws://` and `wss://` (TLS) endpoints
- Send text and binary messages
- Receive text and binary messages (async with timeout)
- Query connection state
- Graceful close handshake
- Custom headers on connect (for auth, subprotocols, etc.)

### What's out of scope

- WebSocket server (listening/accepting upgrades) — separate spec if needed
- Automatic reconnection — belongs in userland/packages
- Streaming/chunked frames — messages are always complete (buffered internally)
- Compression (permessage-deflate) — can be added later without API changes
- Event-driven callback model — Stash's concurrency model is future-based, not event-loop-based; `task.run` + `await wsRecv` loop is the right primitive

---

## 2. API Design

### 2.1 Functions

All functions live in the `net` namespace alongside TCP/UDP functions. The `ws` prefix groups them visually.

| Function           | Signature                | Returns                       | Description                                                       |
| ------------------ | ------------------------ | ----------------------------- | ----------------------------------------------------------------- |
| `net.wsConnect`    | `(url, options?)`        | `Future<WsConnection>`        | Async. Opens a WebSocket connection to a `ws://` or `wss://` URL  |
| `net.wsSend`       | `(conn, data)`           | `Future<int>`                 | Async. Sends a text message. Resolves to bytes sent               |
| `net.wsSendBinary` | `(conn, data)`           | `Future<int>`                 | Async. Sends binary data (base64-encoded). Resolves to bytes sent |
| `net.wsRecv`       | `(conn, timeout?)`       | `Future<WsMessage>` or `null` | Async. Receives next message. Resolves to `null` on timeout       |
| `net.wsClose`      | `(conn, code?, reason?)` | `Future<null>`                | Async. Initiates graceful close handshake                         |
| `net.wsState`      | `(conn)`                 | `WsConnectionState`           | Sync. Returns connection state enum value                         |
| `net.wsIsOpen`     | `(conn)`                 | `bool`                        | Sync. Returns `true` if state is `WsConnectionState.Open`         |

### 2.2 Structs

#### `WsConnection`

Returned by `net.wsConnect`. Opaque handle — the underlying `ClientWebSocket` is stored in a `ConditionalWeakTable`, not exposed as a field.

| Field      | Type     | Description                                   |
| ---------- | -------- | --------------------------------------------- |
| `url`      | `string` | The URL connected to                          |
| `protocol` | `string` | Negotiated subprotocol (empty string if none) |

#### `WsMessage`

Returned by `net.wsRecv`.

| Field   | Type     | Description                                                   |
| ------- | -------- | ------------------------------------------------------------- |
| `data`  | `string` | Message payload (UTF-8 text, or base64-encoded if binary)     |
| `type`  | `string` | `"text"` or `"binary"`                                        |
| `close` | `bool`   | `true` if this is a close frame (connection is shutting down) |

#### 2.2.1 Enums

##### WsConnectionState

```stash
enum WsConnectionState {
  Open,
  Closing,
  Closed
}
```

> **Design decision: Why `close` as a field instead of a separate type?**
> A close frame arrives through the same `wsRecv` channel. Making it a field on `WsMessage` avoids introducing a third struct or a tagged union. The caller checks `msg.close` and handles it — this mirrors how WebSocket frames actually work. When `close` is `true`, `data` contains the close reason (if any) and `type` is `"text"`.

### 2.3 Detailed Function Specifications

#### `net.wsConnect(url, options?)`

Opens a WebSocket connection. **Returns a `Future<WsConnection>`** — must be `await`ed.

**Parameters:**

- `url` (string, required) — Must start with `ws://` or `wss://`. Any other scheme throws a RuntimeError.
- `options` (dict, optional) — Connection options:

| Key           | Type       | Default | Description                                                                                                                                |
| ------------- | ---------- | ------- | ------------------------------------------------------------------------------------------------------------------------------------------ |
| `headers`     | `dict`     | `{}`    | Custom HTTP headers for the upgrade request. Keys and values are strings. Common use: `Authorization`, `Cookie`, `Sec-WebSocket-Protocol`. |
| `timeout`     | `duration` | `10s`   | Connection timeout as a duration literal.                                                                                                  |
| `subprotocol` | `string`   | `null`  | Requested WebSocket subprotocol (e.g., `"stomp"`, `"graphql-ws"`).                                                                         |

**Returns:** `Future<WsConnection>` — resolves to `WsConnection` struct on success.

**Errors (propagated on `await`):**

- `"net.wsConnect: url must start with 'ws://' or 'wss://'"` — invalid scheme
- `"net.wsConnect: connection timed out after 10s"` — timeout
- `"net.wsConnect: connection refused — <detail>"` — server rejected / unreachable
- `"net.wsConnect: server rejected upgrade with status <code>"` — HTTP error during handshake

**Example:**

```stash
// Basic connection
let ws = await net.wsConnect("ws://localhost:8080/events");

// With auth headers and subprotocol
let ws = await net.wsConnect("wss://broker.example.com/ws", {
    headers: {
        "Authorization": "Basic " + encoding.base64Encode("user:pass")
    },
    subprotocol: "stomp",
    timeout: 5s
});
io.println(ws.url);       // "wss://broker.example.com/ws"
io.println(ws.protocol);  // "stomp"
```

---

#### `net.wsSend(conn, data)`

Sends a UTF-8 text message. **Returns a `Future<int>`** — must be `await`ed.

**Parameters:**

- `conn` (WsConnection, required) — An open WebSocket connection.
- `data` (string, required) — The text message to send.

**Returns:** `Future<int>` — resolves to number of bytes sent (UTF-8 encoded length).

**Errors (propagated on `await`):**

- `"net.wsSend: first argument must be a WsConnection"` — wrong type
- `"net.wsSend: connection is not open"` — state is not Open
- `"net.wsSend: send failed — <detail>"` — I/O error

**Example:**

```stash
let bytes = await net.wsSend(ws, "CONNECT\naccept-version:1.2\nhost:/\n\n\0");
io.println($"Sent {bytes} bytes");
```

---

#### `net.wsSendBinary(conn, data)`

Sends a binary message. The `data` parameter is a base64-encoded string which is decoded to raw bytes before sending. **Returns a `Future<int>`** — must be `await`ed.

**Parameters:**

- `conn` (WsConnection, required) — An open WebSocket connection.
- `data` (string, required) — Base64-encoded binary payload.

**Returns:** `Future<int>` — resolves to number of raw bytes sent (post-decode).

**Errors (propagated on `await`):**

- `"net.wsSendBinary: first argument must be a WsConnection"` — wrong type
- `"net.wsSendBinary: invalid base64 data"` — decode failure
- `"net.wsSendBinary: connection is not open"` — state is not Open

**Example:**

```stash
let payload = encoding.base64Encode("binary\x00data\x01here");
let bytes = net.wsSendBinary(ws, payload);
```

> **Design decision: Why base64 for binary?**
> Stash strings are UTF-8. Arbitrary binary data cannot be safely stored in a Stash string without corruption. Base64 is the natural bridge — it's lossless, Stash already has `encoding.base64Encode/Decode`, and the common binary WebSocket use case (protocol buffers, CBOR, binary AMQP frames) requires binary-safe transport. The implementation decodes base64 → `byte[]` → sends as `WebSocketMessageType.Binary`. For text-protocol use cases (STOMP, JSON, GraphQL subscriptions), `wsSend` is the right choice.

---

#### `net.wsRecv(conn, timeout?)`

Receives the next complete message from the WebSocket. **Returns a `Future`** — must be `await`ed. The future blocks internally until a message arrives or the timeout expires.

**Parameters:**

- `conn` (WsConnection, required) — An open WebSocket connection.
- `timeout` (duration, optional, default: `30s`) — Maximum time to wait, given as a duration literal. Pass `0s` for no timeout (block indefinitely).

**Returns:** `Future<WsMessage|null>` — resolves to `WsMessage` struct, or `null` if the timeout expires with no message.

**Behavior:**

- Reassembles fragmented WebSocket frames internally — always returns complete messages.
- If the server sends a **close frame**, resolves to a `WsMessage` with `close: true`, `type: "text"`, and `data` containing the close reason. After receiving a close frame, the connection transitions to `Closing` state.
- For **binary messages**, `data` is base64-encoded and `type` is `"binary"`.
- For **text messages**, `data` is the raw UTF-8 string and `type` is `"text"`.

**Errors (propagated on `await`):**

- `"net.wsRecv: first argument must be a WsConnection"` — wrong type
- `"net.wsRecv: connection is closed"` — already fully closed
- `"net.wsRecv: receive failed — <detail>"` — I/O error (connection dropped unexpectedly)

**Example:**

```stash
// Blocking receive with 5-second timeout
let msg = await net.wsRecv(ws, 5s);
if (msg == null) {
    io.println("No message within 5 seconds");
} else if (msg.close) {
    io.println("Server closing: " + msg.data);
} else if (msg.type == "text") {
    io.println("Got text: " + msg.data);
} else {
    let raw = encoding.base64Decode(msg.data);
    io.println("Got binary: " + raw);
}
```

---

#### `net.wsClose(conn, code?, reason?)`

Initiates the WebSocket close handshake. **Returns a `Future<null>`** — must be `await`ed.

**Parameters:**

- `conn` (WsConnection, required) — The WebSocket connection to close.
- `code` (int, optional, default: `1000`) — Close status code (RFC 6455 §7.4). Common values: `1000` (normal), `1001` (going away).
- `reason` (string, optional, default: `""`) — Close reason string (max 123 bytes UTF-8 per RFC 6455).

**Returns:** `Future<null>` — resolves when close handshake completes.

**Behavior:**

- Sends a close frame and waits up to 5 seconds for the server's close frame response.
- If the server doesn't respond within 5 seconds, force-closes the connection.
- Idempotent — calling on an already-closed connection resolves immediately (no-op).
- After close resolves, the connection's state becomes `WsConnectionState.Closed`. Any pending `wsRecv` on another task resolves to `null`.

**Errors (propagated on `await`):**

- `"net.wsClose: first argument must be a WsConnection"` — wrong type
- `"net.wsClose: invalid close code <X> — must be 1000–4999"` — invalid code range

**Example:**

```stash
await net.wsClose(ws);                        // Normal close (1000)
await net.wsClose(ws, 1001, "going away");    // Custom close
```

---

#### `net.wsState(conn)`

Returns the current connection state as a `WsConnectionState` enum value. **Synchronous** — no await needed.

**Parameters:**

- `conn` (WsConnection, required) — A WebSocket connection (any state).

**Returns:** `WsConnectionState` enum value — one of: `Connecting`, `Open`, `Closing`, `Closed`.

**Mapping from .NET `WebSocketState`:**

| .NET State                   | Stash State                    |
| ---------------------------- | ------------------------------ |
| `None`, `Connecting`         | `WsConnectionState.Connecting` |
| `Open`                       | `WsConnectionState.Open`       |
| `CloseSent`, `CloseReceived` | `WsConnectionState.Closing`    |
| `Closed`, `Aborted`          | `WsConnectionState.Closed`     |

**Example:**

```stash
io.println(net.wsState(ws));  // WsConnectionState.Open
await net.wsClose(ws);
io.println(net.wsState(ws));  // WsConnectionState.Closed

// Enum comparison (no magic strings)
if (net.wsState(ws) == WsConnectionState.Open) {
    await net.wsSend(ws, "still connected");
}
```

---

#### `net.wsIsOpen(conn)`

Convenience function — equivalent to `net.wsState(conn) == WsConnectionState.Open`. **Synchronous** — no await needed.

**Parameters:**

- `conn` (WsConnection, required) — A WebSocket connection.

**Returns:** `bool`

**Example:**

```stash
while (net.wsIsOpen(ws)) {
    let msg = await net.wsRecv(ws, 5s);
    if (msg != null && !msg.close) {
        io.println(msg.data);
    }
}
```

---

## 3. Implementation Plan

### 3.1 C# Implementation — `NetBuiltIns.cs`

WebSocket functions are added to the existing `NetBuiltIns.cs` file, not a separate file. They're part of the `net` namespace.

**Connection tracking:**

```csharp
// Weak reference table — allows GC of abandoned connections
private static readonly ConditionalWeakTable<StashInstance, ClientWebSocket> _wsClients = new();
```

This follows the pattern established by `SshBuiltIns.cs` (see `_clients` ConditionalWeakTable there). The `WsConnection` StashInstance is the key; the underlying `ClientWebSocket` is the value. When the StashInstance goes out of scope, the `ClientWebSocket` becomes eligible for GC and its finalizer calls `Dispose()`.

**Connection extraction helper (private):**

```csharp
private static ClientWebSocket GetWsClient(object? arg, string funcName)
{
    if (arg is not StashInstance inst || inst.TypeName != "WsConnection")
        throw new RuntimeError($"First argument to '{funcName}' must be a WsConnection.");

    if (!_wsClients.TryGetValue(inst, out ClientWebSocket? ws))
        throw new RuntimeError($"{funcName}: connection is invalid or closed.");

    return ws;
}
```

**Key C# implementation notes:**

| Concern                      | Approach                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                  |
| ---------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| **Async model**              | All I/O functions (`wsConnect`, `wsSend`, `wsSendBinary`, `wsRecv`, `wsClose`) return `StashFuture` wrapping the underlying .NET `Task`. This follows the same pattern as `task.run()` / `task.delay()` in `TaskBuiltIns.cs`. The C# handler constructs a `Task<object?>` from the async .NET API, wraps it in `new StashFuture(task, cts)`, and returns it. The Stash `await` keyword (or `task.await()`) calls `StashFuture.GetResult()` to block and retrieve the result. State-reading functions (`wsState`, `wsIsOpen`) remain synchronous — they inspect the `ClientWebSocket.State` property directly without I/O. |
| **Cancellation integration** | Each async operation creates a `CancellationTokenSource` linked to `ctx.CancellationToken` (via `CancellationTokenSource.CreateLinkedTokenSource`). This means `timeout` blocks, script cancellation, and per-operation timeouts all propagate correctly — if a `timeout` block expires while `wsRecv` is waiting, the linked token fires and the future resolves with a `TimeoutError`.                                                                                                                                                                                                                                  |
| **Receive buffering**        | Use a dynamic buffer (start at 4KB, grow as needed) that accumulates fragments until `EndOfMessage` is true. Return the complete reassembled message.                                                                                                                                                                                                                                                                                                                                                                                                                                                                     |
| **Timeout on connect**       | Extract `StashDuration` from options dict, convert via `TimeSpan.FromMilliseconds(duration.TotalMilliseconds)`, apply to the linked CTS via `CancelAfter()`.                                                                                                                                                                                                                                                                                                                                                                                                                                                              |
| **Timeout on receive**       | Accept optional `StashDuration` argument, convert to `TimeSpan`, apply via `CancelAfter()` on the linked CTS. On `OperationCanceledException` from the per-operation timeout (not the outer `ctx.CancellationToken`), resolve the future to `null`. If the outer token fired (script-level `timeout` block), propagate as `TimeoutError`.                                                                                                                                                                                                                                                                                 |
| **Close handshake**          | Call `CloseAsync(closeStatus, reason, cts)` with a 5-second timeout. On timeout, call `Abort()` + `Dispose()`.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                            |
| **Thread safety**            | .NET's `ClientWebSocket` supports exactly one concurrent send and one concurrent receive. Because the functions return futures, the actual .NET async work runs on the thread pool. The user must `await` one send before starting another — but can have one send and one receive outstanding concurrently (one in `task.run`, one on the main path).                                                                                                                                                                                                                                                                    |
| **Native AOT**               | `System.Net.WebSockets.ClientWebSocket` is fully AOT-compatible. No reflection, no dynamic code generation. Already available in the .NET runtime used by Stash.Cli. No additional packages needed.                                                                                                                                                                                                                                                                                                                                                                                                                       |
| **TLS (wss://)**             | Handled transparently by `ClientWebSocket` — it uses the system's TLS stack. No additional configuration needed. Certificate validation follows system defaults.                                                                                                                                                                                                                                                                                                                                                                                                                                                          |
| **Duration extraction**      | A new `SvArgs.Duration(args, index, funcName)` helper should be added to `SvArgs.cs` to extract `StashDuration` values from arguments, consistent with the existing `SvArgs.Long`, `SvArgs.String`, etc. pattern.                                                                                                                                                                                                                                                                                                                                                                                                         |

### 3.2 Struct & Enum Registration

Add to the struct/enum definitions block at the end of `NetBuiltIns.Define()`:

```csharp
ns.Struct("WsConnection", [
    new BuiltInField("url", "string"),
    new BuiltInField("protocol", "string"),
]);

ns.Struct("WsMessage", [
    new BuiltInField("data", "string"),
    new BuiltInField("type", "string"),
    new BuiltInField("close", "bool"),
]);

ns.Enum("WsConnectionState", ["Connecting", "Open", "Closing", "Closed"]);
```

### 3.3 Capability Gate

WebSocket functions require the **Network** capability, which the `net` namespace already gates via `ns.RequiresCapability(StashCapabilities.Network)`. No additional capability changes needed.

### 3.4 Playground Sandbox

The Playground runs in Blazor WASM where `ClientWebSocket` is available but restricted by browser CORS policies. WebSocket connections from the Playground will only work to endpoints that allow the browser's origin. This is acceptable — the Playground already has similar limitations for `http.*` and `net.tcp*` functions.

No sandbox capability gate changes needed — the Network capability is already disabled in the Playground executor.

---

## 4. Cross-Platform Behavior

| Platform        | Notes                                                                                                                                      |
| --------------- | ------------------------------------------------------------------------------------------------------------------------------------------ |
| **Linux**       | Fully supported. `ClientWebSocket` uses managed sockets.                                                                                   |
| **macOS**       | Fully supported. Same managed implementation.                                                                                              |
| **Windows**     | Fully supported. Uses WinHTTP on older .NET, managed sockets on .NET 8+.                                                                   |
| **Blazor WASM** | Uses browser's `WebSocket` API under the hood. Subject to browser CORS. (Playground only — `Network` capability is disabled there anyway.) |

No platform-specific code paths needed. `ClientWebSocket` abstracts all platform differences.

---

## 5. Interaction with Existing Features

### 5.1 Error Handling — `try` Expression

WebSocket errors propagate when the returned future is `await`ed. The `try` expression provides elegant error-as-value handling:

```stash
// try expression catches error and returns it as a value
let ws = try await net.wsConnect("ws://localhost:9999");
if (ws is Error) {
    io.println("Connection failed: " + ws.message);  // "net.wsConnect: connection refused — ..."
} else {
    io.println("Connected to " + ws.url);
}

// Compose with ?? for fallback values
let msg = try await net.wsRecv(ws, 5s) ?? null;

// Traditional try/catch also works
try {
    let ws = await net.wsConnect("ws://localhost:9999");
} catch (e) {
    io.println(e.message);
}
```

The `$!(...)` strict command syntax is not relevant (WebSocket calls are function calls, not shell commands).

### 5.2 Async/Await — First-Class Integration

WebSocket functions return `StashFuture` values, making them native async citizens. This is a deliberate design choice — unlike `http.*` which blocks synchronously, WebSocket operations are inherently long-lived and benefit from non-blocking semantics.

**Basic await pattern:**

```stash
let ws = await net.wsConnect("ws://localhost:8080/events");
await net.wsSend(ws, "hello");
let msg = await net.wsRecv(ws, 30s);
await net.wsClose(ws);
```

**Background receiver with `task.run`:**

```stash
let ws = await net.wsConnect("ws://localhost:8080/events");

// Receiver runs in background — wsRecv future resolves on thread pool
let receiver = task.run(() => {
    while (net.wsIsOpen(ws)) {
        let msg = await net.wsRecv(ws, 5s);
        if (msg != null && !msg.close) {
            io.println("Event: " + msg.data);
        }
    }
});

// Main thread can send concurrently (1 send + 1 recv supported in parallel)
await net.wsSend(ws, "subscribe:deployments");
time.sleep(30);
await net.wsClose(ws);
task.await(receiver);
```

**Parallel connect with `task.all`:**

```stash
let futures = [
    net.wsConnect("ws://server1:8080/events"),
    net.wsConnect("ws://server2:8080/events"),
    net.wsConnect("ws://server3:8080/events"),
];
let connections = await task.all(futures);  // Connect to all 3 in parallel
```

**Race — first server to respond:**

```stash
let ws = await task.race([
    net.wsConnect("ws://primary:8080/events"),
    net.wsConnect("ws://fallback:8080/events"),
]);
```

### 5.3 `timeout` Blocks — Automatic Cancellation Propagation

Stash's `timeout` block propagates cancellation through `ctx.CancellationToken`. Because all async WebSocket functions create linked cancellation tokens, `timeout` blocks work automatically — no special WebSocket implementation needed:

```stash
// Timeout wraps the entire connection + message exchange
let msg = timeout 10s {
    let ws = await net.wsConnect("ws://slow-server:8080/feed");
    await net.wsSend(ws, "subscribe");
    await net.wsRecv(ws, 30s);  // inner recv timeout is 30s, but outer timeout fires at 10s
};

// Compose with try for graceful degradation
let data = try timeout 5s {
    let ws = await net.wsConnect("ws://broker:8080");
    await net.wsSend(ws, json.stringify({ action: "get_status" }));
    let msg = await net.wsRecv(ws);
    json.parse(msg.data);
} ?? { status: "unknown" };

// Per-server timeout in a loop
for (let server in servers) {
    let status = try timeout 3s {
        let ws = await net.wsConnect($"ws://{server}:8080/health");
        let msg = await net.wsRecv(ws, 3s);
        await net.wsClose(ws);
        json.parse(msg.data);
    };
    if (status is Error) {
        io.println($"{server}: timeout or error");
    } else {
        io.println($"{server}: {status.health}");
    }
}
```

> **Implementation note:** This works because the `timeout` block creates a `CancellationTokenSource` that fires after the duration, and the WebSocket functions link to it via `ctx.CancellationToken`. When the timeout fires, the pending `ConnectAsync`/`SendAsync`/`ReceiveAsync` call receives an `OperationCanceledException`, which the runtime translates to a `TimeoutError`. No WebSocket-specific timeout handling code is needed — the existing `timeout` infrastructure handles it.

### 5.4 `retry` Blocks — Resilient Connections

The `retry` block works naturally with WebSocket operations for reconnection patterns:

```stash
// Retry connection with exponential backoff
let ws = retry (5, delay: 1s, backoff: Backoff.Exponential, maxDelay: 30s) {
    await net.wsConnect("ws://broker:8080/events", { timeout: 5s });
};

// Retry with logging hook
let ws = retry (3, delay: 2s) onRetry (attempt, error) {
    io.println($"Connection attempt {attempt.current} failed: {error.message}");
} {
    await net.wsConnect("ws://broker:8080/events");
};

// Full resilient consumer pattern: retry + timeout + try
retry (10, delay: 5s, backoff: Backoff.Exponential, maxDelay: 1m) {
    let ws = await net.wsConnect("ws://broker:8080/events", { timeout: 5s });

    while (net.wsIsOpen(ws)) {
        let msg = try await net.wsRecv(ws, 30s);
        if (msg is Error) {
            io.println("Receive error, reconnecting...");
            break;  // break inner loop → retry reconnects
        }
        if (msg == null) {
            // Timeout, send heartbeat
            await net.wsSend(ws, "ping");
            continue;
        }
        if (msg.close) break;

        let payload = json.parse(msg.data);
        io.println($"Event: {payload.type}");
    }

    await net.wsClose(ws);
};

// Filter retry to only network errors
let ws = retry (5, delay: 1s, on: ["NetworkError", "TimeoutError"]) {
    await net.wsConnect("ws://broker:8080/events");
};
```

> **Why this works well:** When `wsConnect` or `wsRecv` throws (connection refused, timeout, I/O error), the `retry` block catches the error and retries. The per-attempt fresh scope means each retry gets a clean `ws` variable. The `break` from the inner `while` loop causes the retry body to exit normally, which — if combined with an `until` predicate — can trigger the next retry.

### 5.5 Duration Literals — Native Timeout Syntax

All timeout-related parameters accept Stash duration literals natively instead of raw integer milliseconds:

```stash
// Connect with 5-second timeout
let ws = await net.wsConnect("ws://broker:8080", { timeout: 5s });

// Receive with various timeout durations
let msg = await net.wsRecv(ws, 30s);
let msg = await net.wsRecv(ws, 500ms);
let msg = await net.wsRecv(ws, 2m);

// Durations compose with timeout blocks
timeout 1m {
    let ws = await net.wsConnect("ws://broker:8080", { timeout: 10s });
    // ...
}
```

The C# implementation extracts durations via `StashDuration.TotalMilliseconds` and converts to `TimeSpan`. A new `SvArgs.Duration()` helper provides consistent extraction with proper error messages.

### 5.6 UFCS (Uniform Function Call Syntax)

If UFCS is enabled, these calls would also work:

```stash
ws.wsSend("hello");             // Unlikely to be useful, included for completeness
let msg = ws.wsRecv(5s);
ws.wsClose();
```

UFCS doesn't require any implementation changes — it works automatically with namespace functions.

### 5.7 Scope and Cleanup

WebSocket connections are not automatically closed when the enclosing scope exits. This matches TCP socket behavior (`net.tcpConnect` doesn't auto-close either). Users must call `net.wsClose()` explicitly.

If a `WsConnection` instance goes out of scope without being closed:

1. The `ConditionalWeakTable` allows the `ClientWebSocket` to become GC-eligible.
2. The .NET finalizer calls `Dispose()` on the `ClientWebSocket`, which sends an abort (not a graceful close).
3. This is acceptable for script exit, but scripts should `await net.wsClose(ws)` for clean shutdowns.

### 5.8 Bytecode VM

The bytecode VM calls the same `BuiltInFunction` delegates as the tree-walk interpreter. Functions that return `StashFuture` work identically — the VM's `ExecuteAwait` opcode calls `StashFuture.GetResult()` to block and retrieve the value. No special VM opcodes needed.

---

## 6. LSP / DAP / Tooling Impact

### 6.1 LSP

- **Completions:** Automatic — `StdlibRegistry` picks up the new functions from the builder definitions. Typing `net.ws` will autocomplete all 7 functions.
- **Hover:** Automatic — `documentation:` strings flow to hover display. Parameter names and types show via `@param` tags.
- **Signature help:** Automatic — parameter lists from `b.Function()` definitions.
- **Diagnostics:** No changes — argument count/type validation happens at runtime.

**No LSP code changes needed.** The existing metadata-driven system handles everything.

### 6.2 DAP

- **Variable display:** `WsConnection` and `WsMessage` are `StashInstance` types — the DAP already knows how to display their fields.
- **Watch expressions:** `net.wsState(ws)` works in watch expressions (it's a pure read, no side effects beyond checking state).

**No DAP code changes needed.**

### 6.3 VS Code Extension

- **TextMate grammar:** No changes — `net` is already a recognized namespace in the grammar.
- **Syntax highlighting:** No new keywords or syntax.

**No extension changes needed.**

### 6.4 Playground — Monarch Tokenizer

No changes needed — `net.*` functions are already tokenized. New function names will be recognized automatically.

### 6.5 Static Analysis

- **Type inference:** The struct definitions in the builder provide all type information. The analysis engine already understands `StashInstance` return types from namespace functions.
- **No new diagnostics** specific to WebSocket needed.

**No analysis code changes needed.**

---

## 7. Documentation Updates

### 7.1 Standard Library Reference (`docs/Stash — Standard Library Reference.md`)

Add a new subsection under the existing `net` namespace section, after "UDP Datagrams" and before "Advanced DNS":

````markdown
### WebSocket Client

| Function                            | Description                                                                    |
| ----------------------------------- | ------------------------------------------------------------------------------ | ------- |
| `net.wsConnect(url, options?)`      | Async. Opens a WebSocket connection. Returns `Future<WsConnection>`.           |
| `net.wsSend(conn, data)`            | Async. Sends a text message. Returns `Future<int>` (bytes sent).               |
| `net.wsSendBinary(conn, data)`      | Async. Sends binary data (base64-encoded). Returns `Future<int>` (bytes sent). |
| `net.wsRecv(conn, timeout?)`        | Async. Receives next message. Returns `Future<WsMessage                        | null>`. |
| `net.wsClose(conn, code?, reason?)` | Async. Initiates graceful close handshake.                                     |
| `net.wsState(conn)`                 | Returns `WsConnectionState` enum value.                                        |
| `net.wsIsOpen(conn)`                | Returns `true` if connection is `WsConnectionState.Open`.                      |

#### `net.wsConnect(url, options?)`

Async. Opens a WebSocket connection to a `ws://` or `wss://` URL.

- `url` — WebSocket URL (must start with `ws://` or `wss://`)
- `options` — Optional dict:

| Key           | Type       | Default | Description                                 |
| ------------- | ---------- | ------- | ------------------------------------------- |
| `headers`     | `dict`     | `{}`    | Custom HTTP headers for the upgrade request |
| `timeout`     | `duration` | `10s`   | Connection timeout                          |
| `subprotocol` | `string`   | `null`  | Requested WebSocket subprotocol             |

```stash
let ws = await net.wsConnect("ws://localhost:8080/events");

// With auth and subprotocol
let ws = await net.wsConnect("wss://broker.example.com/ws", {
    headers: { "Authorization": "Bearer " + token },
    subprotocol: "stomp",
    timeout: 5s
});
```
````

#### `net.wsSend(conn, data)`

Async. Sends a UTF-8 text message over an open WebSocket connection.

- Returns `Future<int>` — bytes sent

```stash
let bytes = await net.wsSend(ws, json.stringify({ event: "deploy", app: "web-api" }));
```

#### `net.wsSendBinary(conn, data)`

Async. Sends binary data. The `data` parameter must be base64-encoded; it is decoded to raw bytes before sending.

```stash
let payload = encoding.base64Encode(rawBytes);
await net.wsSendBinary(ws, payload);
```

#### `net.wsRecv(conn, timeout?)`

Async. Receives the next complete message. Blocks until a message arrives or the timeout (default: `30s`) expires.

- Returns `Future<WsMessage|null>` — `WsMessage` struct or `null` on timeout
- Fragmented frames are reassembled internally

```stash
let msg = await net.wsRecv(ws, 5s);
if (msg == null) {
    io.println("Timed out");
} else if (msg.close) {
    io.println("Server closed: " + msg.data);
} else {
    io.println(msg.type + ": " + msg.data);
}
```

#### `net.wsClose(conn, code?, reason?)`

Async. Initiates the WebSocket close handshake.

- `code` — Close status code (default: `1000`)
- `reason` — Close reason (default: `""`)
- Idempotent — safe to call on already-closed connections

```stash
await net.wsClose(ws);
await net.wsClose(ws, 1001, "going away");
```

#### `net.wsState(conn)` / `net.wsIsOpen(conn)`

Sync. Query connection state. Returns `WsConnectionState` enum value or `bool`.

```stash
if (net.wsIsOpen(ws)) {
    await net.wsSend(ws, "ping");
}
io.println(net.wsState(ws));  // WsConnectionState.Open, .Closing, or .Closed
```

#### `WsConnectionState` Enum

| Value        | Description                                   |
| ------------ | --------------------------------------------- |
| `Connecting` | Handshake in progress                         |
| `Open`       | Connection established, ready to send/receive |
| `Closing`    | Close handshake initiated                     |
| `Closed`     | Connection fully closed                       |

#### `WsConnection`

| Field      | Type     | Description                                   |
| ---------- | -------- | --------------------------------------------- |
| `url`      | `string` | The URL connected to                          |
| `protocol` | `string` | Negotiated subprotocol (empty string if none) |

#### `WsMessage`

| Field   | Type     | Description                                               |
| ------- | -------- | --------------------------------------------------------- |
| `data`  | `string` | Message payload (UTF-8 text, or base64-encoded if binary) |
| `type`  | `string` | `"text"` or `"binary"`                                    |
| `close` | `bool`   | `true` if this is a close frame                           |

### 7.2 Language Specification

No changes needed — this adds stdlib functions, not syntax or language features.

---

## 8. Example Script

Create `examples/websockets.stash`:

```stash
/// WebSocket Client Examples
/// Demonstrates net.ws* functions for real-time communication

// ─── Basic Echo Client ───────────────────────────────────
// Connect to a WebSocket echo server

let ws = await net.wsConnect("wss://echo.websocket.org");
io.println("Connected: " + ws.url);
io.println("State: " + net.wsState(ws));  // WsConnectionState.Open

// Send a text message
let bytes = await net.wsSend(ws, "Hello, WebSocket!");
io.println($"Sent {bytes} bytes");

// Receive the echo
let msg = await net.wsRecv(ws, 5s);
if (msg != null) {
    io.println($"Received ({msg.type}): {msg.data}");
}

// Clean close
await net.wsClose(ws);
io.println("State after close: " + net.wsState(ws));  // WsConnectionState.Closed


// ─── Try Expression — Graceful Error Handling ─────────────
// Use `try` to catch connection errors as values

let result = try await net.wsConnect("ws://unreachable:9999", { timeout: 2s });
if (result is Error) {
    io.println("Expected failure: " + result.message);
}

// Compose with ?? for defaults
let msg = try await net.wsRecv(ws, 1s) ?? null;
io.println(msg == null ? "No message" : msg.data);


// ─── Timeout Blocks — Bounded Operations ─────────────────
// Wrap entire WebSocket workflow in a timeout

let status = try timeout 10s {
    let ws = await net.wsConnect("ws://localhost:8080/api", {
        headers: { "Authorization": "Bearer " + env.get("API_TOKEN") },
        timeout: 5s
    });
    await net.wsSend(ws, json.stringify({ action: "get_status" }));
    let msg = await net.wsRecv(ws);
    await net.wsClose(ws);
    json.parse(msg.data);
} ?? { status: "unreachable" };

io.println($"Server status: {status.status}");


// ─── Retry — Resilient Connection ────────────────────────
// Automatically retry failed connections with backoff

let ws2 = retry (5, delay: 1s, backoff: Backoff.Exponential, maxDelay: 15s) onRetry (attempt, error) {
    io.println($"Attempt {attempt.current} failed: {error.message}");
} {
    await net.wsConnect("ws://broker:15674/ws", {
        headers: { "Authorization": "Basic " + encoding.base64Encode("guest:guest") },
        subprotocol: "stomp",
        timeout: 5s
    })
};
io.println($"Connected after retries: {ws2.url}");


// ─── JSON Message Pattern ────────────────────────────────
// Common pattern: JSON over WebSocket

let ws3 = await net.wsConnect("ws://localhost:8080/api", {
    headers: { "Authorization": "Bearer " + env.get("API_TOKEN") },
    timeout: 5s
});

// Send structured data
let event = {
    type: "subscribe",
    channels: ["deployments", "alerts"]
};
await net.wsSend(ws3, json.stringify(event));

// Receive loop with timeout
while (net.wsIsOpen(ws3)) {
    let msg = await net.wsRecv(ws3, 10s);

    if (msg == null) {
        // Timeout — send a heartbeat
        await net.wsSend(ws3, json.stringify({ type: "ping" }));
        continue;
    }

    if (msg.close) {
        io.println("Server closed connection: " + msg.data);
        break;
    }

    let payload = json.parse(msg.data);
    io.println($"[{payload.type}] {payload.message}");
}

await net.wsClose(ws3);


// ─── Background Receiver with Tasks ─────────────────────
// Use task.run() for concurrent send/receive

let ws4 = await net.wsConnect("ws://localhost:8080/stream");

// Receiver runs in background
let receiver = task.run(() => {
    let count = 0;
    while (net.wsIsOpen(ws4)) {
        let msg = await net.wsRecv(ws4, 5s);
        if (msg != null && !msg.close) {
            count = count + 1;
            io.println($"Message #{count}: {msg.data}");
        }
    }
    return count;
});

// Main thread sends commands
await net.wsSend(ws4, "start-streaming");
time.sleep(10);
await net.wsSend(ws4, "stop-streaming");

// Close and collect results
await net.wsClose(ws4);
let total = task.await(receiver);
io.println($"Received {total} messages total");


// ─── Full Resilient Consumer — retry + timeout + try ────
// Production-style pattern combining all language features

retry (10, delay: 5s, backoff: Backoff.Exponential, maxDelay: 1m) {
    let ws = await net.wsConnect("ws://broker:8080/events", { timeout: 5s });

    while (net.wsIsOpen(ws)) {
        let msg = try await net.wsRecv(ws, 30s);

        if (msg is Error) {
            io.println("Receive error, will reconnect...");
            break;
        }
        if (msg == null) {
            await net.wsSend(ws, "heartbeat");
            continue;
        }
        if (msg.close) break;

        let payload = json.parse(msg.data);
        io.println($"[{time.format(time.now(), "HH:mm:ss")}] {payload.type}: {payload.data}");
    }

    await net.wsClose(ws);
}
```

---

## 9. Test Scenarios

Tests go in `Stash.Tests/Interpreting/NetBuiltInsTests.cs` (extend the existing file, add a `#region WebSocket` block). Tests require a local echo WebSocket server — use a test helper that starts one via `System.Net.WebSockets` server-side APIs.

### 9.1 Happy Path Tests

| Test Name                                      | Scenario                                                            |
| ---------------------------------------------- | ------------------------------------------------------------------- |
| `WsConnect_ValidUrl_ReturnsWsConnection`       | `await` connect to echo server, verify `url` and `protocol` fields  |
| `WsConnect_WithSubprotocol_NegotiatesProtocol` | Request subprotocol, verify `ws.protocol` matches                   |
| `WsConnect_Wss_TlsWorks`                       | Connect to `wss://` endpoint (may need test cert or external echo)  |
| `WsSend_TextMessage_ReturnsByteCount`          | `await` send "hello", expect future resolves to 5                   |
| `WsSendBinary_Base64Data_Succeeds`             | Send base64 payload, verify round-trip                              |
| `WsRecv_EchoServer_ReturnsTextMessage`         | Send + recv, verify `data`, `type == "text"`, `close == false`      |
| `WsRecv_BinaryMessage_ReturnsBase64`           | Binary echo, verify `type == "binary"`, decodable data              |
| `WsRecv_Timeout_ReturnsNull`                   | Recv with `100ms` timeout, no message, verify null                  |
| `WsClose_GracefulClose_Succeeds`               | Close, verify `wsState` returns `WsConnectionState.Closed`          |
| `WsClose_WithCodeAndReason_Succeeds`           | Close with 1001/"going away", no error                              |
| `WsClose_Idempotent_NoError`                   | Close twice, second call is no-op                                   |
| `WsState_Open_ReturnsEnumValue`                | After connect, verify `net.wsState(ws) == WsConnectionState.Open`   |
| `WsState_AfterClose_ReturnsClosed`             | After close, verify `WsConnectionState.Closed`                      |
| `WsIsOpen_Open_ReturnsTrue`                    | After connect, verify `true`                                        |
| `WsIsOpen_Closed_ReturnsFalse`                 | After close, verify `false`                                         |
| `WsRecv_DurationLiteral_Accepted`              | Recv with `5s` duration literal, verify it's accepted as timeout    |
| `WsConnect_DurationTimeout_Accepted`           | Connect with `{ timeout: 3s }`, verify duration is parsed correctly |
| `WsRecv_BinaryMessage_ReturnsBase64`           | Binary echo, verify `type == "binary"`, decodable data              |
| `WsRecv_Timeout_ReturnsNull`                   | Recv with 100ms timeout, no message, verify null                    |
| `WsClose_GracefulClose_Succeeds`               | Close, verify `wsState` returns `"closed"`                          |
| `WsClose_WithCodeAndReason_Succeeds`           | Close with 1001/"going away", no error                              |
| `WsClose_Idempotent_NoError`                   | Close twice, second call is no-op                                   |
| `WsState_Open_ReturnsOpen`                     | After connect, verify `"open"`                                      |
| `WsState_AfterClose_ReturnsClosed`             | After close, verify `"closed"`                                      |
| `WsIsOpen_Open_ReturnsTrue`                    | After connect, verify `true`                                        |
| `WsIsOpen_Closed_ReturnsFalse`                 | After close, verify `false`                                         |

### 9.2 Error Tests

| Test Name                                | Scenario                                   |
| ---------------------------------------- | ------------------------------------------ |
| `WsConnect_InvalidScheme_ThrowsError`    | `http://` URL → RuntimeError               |
| `WsConnect_Unreachable_ThrowsError`      | `ws://localhost:1` → connection refused    |
| `WsConnect_Timeout_ThrowsError`          | Tiny timeout + slow server → timeout error |
| `WsSend_ClosedConnection_ThrowsError`    | Send after close → error                   |
| `WsSend_WrongType_ThrowsError`           | Pass non-WsConnection → type error         |
| `WsRecv_ClosedConnection_ThrowsError`    | Recv after close → error                   |
| `WsSendBinary_InvalidBase64_ThrowsError` | Non-base64 string → error                  |
| `WsClose_InvalidCode_ThrowsError`        | Code 999 → out of range error              |

### 9.3 Integration Tests

| Test Name                                      | Scenario                                                                            |
| ---------------------------------------------- | ----------------------------------------------------------------------------------- |
| `WsRecv_ServerCloseFrame_ReturnsCloseMessage`  | Server initiates close, verify `msg.close == true`                                  |
| `WsConnect_CustomHeaders_SentInUpgrade`        | Echo server inspects headers, confirms custom header present                        |
| `Ws_ConcurrentSendRecv_Works`                  | Use `task.run` for recv, main thread sends, verify no deadlock                      |
| `Ws_LargeMessage_ReassemblesFragments`         | Send message > 4KB, verify complete reassembly                                      |
| `Ws_TimeoutBlock_CancelsRecv`                  | Wrap `wsRecv(ws, 30s)` in `timeout 1s {}`, verify `TimeoutError`                    |
| `Ws_RetryBlock_ReconnectsOnFailure`            | Use `retry (3) { wsConnect(...) }` with failing server, verify retries attempted    |
| `Ws_TryExpression_ReturnsErrorValue`           | Use `try await wsConnect("ws://invalid")`, verify error value with `.message` field |
| `Ws_TryExpressionWithFallback_ReturnsFallback` | Use `try await wsRecv(ws, 100ms) ?? null`, verify null fallback                     |
| `Ws_AsyncAwait_FutureReturned`                 | Call `wsConnect` without await, verify return is `StashFuture`, then await it       |

### 9.4 Test Echo Server Helper

The test suite needs a helper to start a temporary WebSocket echo server:

```csharp
private static (HttpListener listener, Task serverTask) StartEchoServer(int port)
{
    // Uses System.Net.WebSockets server-side to accept
    // and echo back messages. Returns after first connection closes.
}
```

This follows the pattern used in the existing TCP tests where `net.tcpListen` serves as the test server.

---

## 10. Migration / Breaking Changes

**None.** This is purely additive — new functions and structs in an existing namespace. No existing behavior is modified and no reserved keywords are used.

---

## 11. Open Design Questions

### 11.1 Should `wsRecv` distinguish close frames differently?

**Current design:** Close frames come through `wsRecv` with `msg.close == true`. The alternative would be to have `wsRecv` return `null` on close (like timeout) and let the user check `wsState`.

**Decision:** Current design is better. Returning `null` for both "timeout" and "server closed" is ambiguous — the caller can't distinguish "nothing happened" from "connection is dead." The `close` field makes intent explicit.

### 11.2 Should we add `net.wsOnMessage(conn, callback)` for event-driven consumption?

**Decision: No.** Stash's concurrency model is `async/await` + `task.run`, not event loops. A `task.run` + `await wsRecv` loop is the idiomatic pattern. Adding an event-driven API would require either:

- A hidden background thread + callback invocation (breaks Stash's execution model)
- A polling shim that pretends to be event-driven (worse than the explicit loop)

If demand arises, this could be added later as a package-level helper function, not a stdlib primitive.

### 11.3 Should connect timeout be separate from options?

**Decision: No.** Keeping it in the options dict is consistent with `http.*` functions. A separate parameter would make the signature `wsConnect(url, timeout?, options?)` which is awkward — is the second arg a timeout or an options dict? The options dict avoids this ambiguity.

### 11.4 Default receive buffer and message size limit?

**Decision:** Start with a 4KB buffer, grow dynamically up to **16MB** max message size. If a single message exceeds 16MB, throw an error. This prevents unbounded memory growth from misbehaving servers. The limit is hardcoded but generous enough for any reasonable use case. A future enhancement could make it configurable via connect options.

### 11.5 Why return `StashFuture` instead of blocking synchronously like `http.*`?

**Decision:** WebSocket operations are fundamentally different from HTTP. HTTP is request-response — you fire one request and wait for one response. WebSocket connections are long-lived with interleaved send/receive. Making them async enables:

- Concurrent send + receive (one `task.run` for recv, main thread sends) — this is the dominant WebSocket usage pattern
- Natural composition with `timeout` blocks (cancellation propagates through linked CTS)
- Parallel connect via `task.all([wsConnect(...), wsConnect(...)])`
- Non-blocking fire-and-forget sends when the result isn't needed

The `http.*` precedent of synchronous blocking is appropriate for fire-and-forget requests but would make WebSocket consumers awkward — you'd need `task.run` wrapping every recv call just to not block the main thread.

### 11.6 Should `wsState`/`wsIsOpen` be async too?

**Decision: No.** These read the `ClientWebSocket.State` property, which is an in-memory field — no I/O, no network. Making them async would add unnecessary `await` ceremony for what is functionally a field read. Keeping them synchronous also makes them safe for use in `while` loop conditions without await.

---

## 12. Implementation Checklist

Per `language-changes.instructions.md`:

- [ ] **Implementation** — Add 7 functions + 2 structs + 1 enum to `NetBuiltIns.cs`
- [ ] **SvArgs.Duration helper** — Add `SvArgs.Duration()` to `SvArgs.cs` for consistent duration extraction
- [ ] **Documentation** — Update `docs/Stash — Standard Library Reference.md` with WebSocket subsection
- [ ] **Example script** — Create `examples/websockets.stash`
- [ ] **Tests** — Add tests to `Stash.Tests/Interpreting/NetBuiltInsTests.cs`
- [ ] **LSP verification** — Confirm completions, hover, signature help work (automatic via builder)
- [ ] **DAP verification** — Confirm `WsConnection`, `WsMessage`, and `WsConnectionState` display correctly in debugger
- [ ] **Playground verification** — Confirm Network capability gate prevents WebSocket in sandbox
- [ ] **VS Code extension verification** — Confirm no grammar changes needed
- [ ] **Static analysis verification** — Confirm no analysis changes needed
- [ ] **Cross-platform testing** — Verify on Linux, macOS, Windows

---

## 13. Decision Log

| Date       | Decision                                               | Rationale                                                                                                                                                                                                                                  |
| ---------- | ------------------------------------------------------ | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| 2026-04-14 | Add to `net` namespace, not a new `ws` namespace       | WebSocket is a network protocol — belongs in `net.*`. Consistent with `net.tcp*` and `net.udp*` grouping. Avoids namespace proliferation.                                                                                                  |
| 2026-04-14 | Client-only, no server support                         | Stash is a scripting language for sysadmin. Connecting to WebSocket services is the use case, not hosting them. Server support can be added later.                                                                                         |
| 2026-04-14 | Base64 for binary messages                             | Stash strings are UTF-8 — binary data can't be safely round-tripped through Stash strings. Base64 is the established pattern (see `encoding.*` namespace).                                                                                 |
| 2026-04-14 | Close frames come through `wsRecv`                     | Explicit is better than ambiguous. `msg.close` flag distinguishes "server closed" from "timeout."                                                                                                                                          |
| 2026-04-14 | `ConditionalWeakTable` for connection tracking         | Follows `SshBuiltIns.cs` pattern. Allows GC of abandoned connections. No manual connection registry to leak memory.                                                                                                                        |
| 2026-04-14 | No streaming/chunked frame API                         | Reassembling fragments internally keeps the API simple. 99% of WebSocket usage sends complete messages. Streaming can be added later if needed.                                                                                            |
| 2026-04-14 | 16MB max message size                                  | Prevents unbounded memory growth. Generous enough for any sysadmin use case. Hardcoded for simplicity.                                                                                                                                     |
| 2026-04-14 | 7 functions, not fewer                                 | `wsSendBinary` is separate from `wsSend` for type clarity. `wsIsOpen` is a convenience wrapper. Could argue for fewer, but explicitness wins for a scripting language aimed at sysadmins.                                                  |
| 2026-04-14 | Async (`StashFuture`-returning) I/O functions          | WebSocket is inherently long-lived and concurrent. Async enables natural `task.run` + recv patterns, `timeout` block integration, parallel connect via `task.all`. Unlike HTTP's request-response model where sync blocking is acceptable. |
| 2026-04-14 | Sync `wsState`/`wsIsOpen` (no futures)                 | No I/O — reads in-memory `ClientWebSocket.State` property. Avoids unnecessary `await` ceremony for what is a field read. Safe for `while` loop conditions.                                                                                 |
| 2026-04-14 | `WsConnectionState` enum, not magic strings            | Stash has first-class enums — use them. Enum comparison is type-safe, discoverable via LSP completions, and refactor-friendly. Matches the .NET `WebSocketState` enum semantics.                                                           |
| 2026-04-14 | Duration literals for all timeout parameters           | Stash has rich duration literals (`5s`, `30s`, `2m`) — use them instead of raw millisecond integers. More readable, self-documenting, consistent with `timeout` / `retry` block syntax.                                                    |
| 2026-04-14 | `timeout` blocks propagate via `ctx.CancellationToken` | No WebSocket-specific timeout code needed — linked CancellationTokenSource means `timeout` blocks automatically cancel pending WS operations. Zero extra implementation cost.                                                              |
| 2026-04-14 | `retry` blocks work out-of-the-box                     | Retry catches thrown errors and re-executes the body. WS connect/send/recv throw on failure. No adapter code needed — composable by design.                                                                                                |
