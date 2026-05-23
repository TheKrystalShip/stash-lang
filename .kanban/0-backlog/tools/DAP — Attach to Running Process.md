# DAP — Attach to Running Process

> **Status:** Draft v0.1
> **Created:** April 28, 2026
> **Author:** Architect
> **Stage:** Backlog — design in progress

---

## 1. Motivation

Stash's current debugger (the DAP server in `Stash.Dap`) operates exclusively in **launch mode**: it starts a fresh interpreter process, runs a script, and you debug that process from the beginning. This is fine for one-shot scripts but completely wrong for a growing class of Stash use cases:

- **Long-running daemons** started with `timeout` blocks and `retry` loops that may need a "why is this stuck?" inspection hours after launch
- **Background services** managed by the scheduler that cannot be stopped and restarted to attach a debugger
- **Scripts with expensive startup** (network connections, loaded caches) where re-running to hit a breakpoint is impractical
- **Incident response** — a deployment script is misbehaving in production, you want to inspect its current state without killing it

The DAP specification explicitly supports `attach` requests, and the capability is already stubbed as `false` in `docs/DAP — Debug Adapter Protocol.md`. This spec defines how to implement it.

---

## 2. Current State

From investigation of the codebase:

| Area | Current state |
|---|---|
| `StashDebugServer.cs` | 18 handlers registered, none for `attach` |
| `DebugSession.cs` | `Launch()` only; no `Attach()` method |
| `IDebugger` interface | Designed for in-process injection; no remote protocol |
| `VirtualMachine.Debugger` | Property settable at any time, BUT the compile-time generic dispatch (`RunInner<DebugOff>` vs `RunInner<DebugOn>`) makes mid-flight activation non-trivial |
| `--debug` CLI flag | Emits a "not yet supported with bytecode VM" warning; no `--debug-listen` flag exists |
| IPC infrastructure | None. No named pipes, Unix domain sockets, or debug control channels |
| Signal handling | SIGUSR1/SIGUSR2 exposed to scripts via `sys.onSignal()`; nothing registered for native VM debug activation |
| Process discovery | Nothing |
| Extension `launch.json` | No `attach` configuration type |

**Critical constraint identified:** The VM's debug dispatch uses a C# generic type parameter (`TDebugMode`) chosen once when `Execute()` is called. Setting `vm.Debugger` after execution starts while the VM is running in `RunInner<DebugOff>` will have no effect — the debug checks are compiled out at the JIT level. **Ad-hoc attach (SIGUSR1 on a script not started with `--debug-listen`) requires a "safe-point restart" mechanism in the VM.**

---

## 3. Design Goals

1. **Attach to any running Stash script** — both pre-configured (`--debug-listen`) and ad-hoc (SIGUSR1).
2. **Zero overhead when not attached** — scripts not started with `--debug-listen` run at full speed until signaled. Pre-configured scripts run in DebugOn mode from startup (small overhead, tolerable for explicit opt-in).
3. **Non-disruptive** — attaching and detaching must not kill the script. The script must continue running after the debugger detaches.
4. **Full feature parity with launch mode** — breakpoints, stepping, variable inspection, expression evaluation, logpoints, hit counts, function breakpoints, exception breakpoints, multiple threads.
5. **Cross-platform** — TCP/loopback transport works on all three platforms. Ad-hoc SIGUSR1 wakeup is Linux/macOS only; Windows requires pre-configured `--debug-listen`.
6. **AOT-compatible in-process code** — the in-process debug server lives in `Stash.Bytecode`, which is compiled into the AOT CLI binary. No OmniSharp in the running script process.
7. **Secure** — auth token prevents unauthorized processes from attaching via the debug port.

---

## 4. Architecture Overview

### 4.1 Current Architecture (Launch Mode)

```
┌──────────────────────────────┐
│   VS Code (DAP Client)       │
└──────────────┬───────────────┘
               │  DAP (JSON over stdio)
┌──────────────▼───────────────┐
│   stash-dap process          │
│   DebugSession (owns VM)     │
│   IDebugger callbacks ◄──┐   │
│   VirtualMachine         │   │
└──────────────────────────┘   │
                               │ in-process calls
                    (VM calls OnBeforeExecute, etc.)
```

### 4.2 Proposed Architecture (Attach Mode)

```
┌──────────────────────────────┐
│   VS Code (DAP Client)       │
└──────────────┬───────────────┘
               │  DAP (JSON over stdio)
┌──────────────▼───────────────┐
│   stash-dap process          │
│   StashAttachHandler (NEW)   │
│   DebugSession.Attach() (NEW)│
│   SipBridgeClient (NEW)      │
└──────────────┬───────────────┘
               │  SIP — Stash Inspector Protocol
               │  (NDJ over TCP loopback 127.0.0.1:PORT)
┌──────────────▼───────────────┐
│   stash CLI process          │
│   StashDebugBridge (NEW)     │ ← implements IDebugger
│   ──────────────────────     │
│   VirtualMachine (running)   │ ← callbacks: OnBeforeExecute, etc.
└──────────────────────────────┘
```

The in-process `StashDebugBridge` owns all debug logic in attach mode: breakpoint state, stepping state machine, variable reference IDs, thread management. The external `stash-dap`'s `SipBridgeClient` is a thin translator: DAP request → SIP command → SIP response → DAP response.

### 4.3 Three New Components

| Component | Location | Purpose |
|---|---|---|
| `StashDebugBridge` | `Stash.Bytecode/Debugging/` | In-process TCP debug server; implements `IDebugger`; speaks SIP |
| `SipBridgeClient` | `Stash.Dap/` | External DAP↔SIP translator; used by `DebugSession` in attach mode |
| `StashAttachHandler` | `Stash.Dap/Handlers/` | Thin DAP `attach` request handler |

---

## 5. Stash Inspector Protocol (SIP)

SIP is a newline-delimited JSON (NDJ) protocol over a TCP loopback socket. Each message is a single JSON object terminated by `\n`. System.Text.Json (AOT-compatible) is used on both sides.

### 5.1 Rationale

SIP was chosen over alternatives:

| Option | Rejected because |
|---|---|
| Embed OmniSharp DAP inside the script process | OmniSharp requires reflection — incompatible with AOT CLI |
| Named pipes | Complex to make cross-platform (Windows named pipes ≠ Unix FIFOs) |
| Shared memory | No AOT-safe shared memory IPC in .NET for this use case |
| Unix domain sockets | Not supported on Windows pre-1803; TCP loopback achieves same security with auth token |
| Full DAP protocol over TCP | Requires implementing DAP headers and capability negotiation in AOT code |

SIP is a deliberate simplification of DAP: no HTTP-style headers, no `initialize` ceremony, no `configurationDone` handshake. It is designed to be implemented in ~500 lines of AOT-compatible C#.

### 5.2 Connection Handshake

1. `stash-dap` connects to `127.0.0.1:<port>` via TCP
2. `stash-dap` sends the handshake request (first message):

```json
{"type":"handshake","token":"<uuid>","clientVersion":1}
```

3. Bridge responds (first message from server):

```json
{"type":"handshakeResponse","success":true,"pid":12345,"scriptPath":"/path/to/script.stash","stashVersion":"1.x.x"}
```

If the token is wrong:
```json
{"type":"handshakeResponse","success":false,"error":"invalid token"}
```

After a failed handshake, the server closes the connection.

### 5.3 Message Types

All messages after handshake are one of: `event` (server→client), `request` (client→server), or `response` (server→client, correlated by `seq`).

#### 5.3.1 Server Events (SIP → DAP bridge)

```json
// Script paused (at breakpoint, step, exception, pause request, entry)
{"type":"event","event":"paused","threadId":1,"reason":"breakpoint","description":"","span":{"file":"/path/script.stash","startLine":42,"endLine":42,"startColumn":1,"endColumn":20}}

// Script resumed
{"type":"event","event":"continued","threadId":1}

// Output from script
{"type":"event","event":"output","category":"stdout","output":"Hello, world!\n"}

// Thread started (task.run())
{"type":"event","event":"threadStarted","threadId":2,"name":"task-1"}

// Thread exited
{"type":"event","event":"threadExited","threadId":2}

// New source file loaded (via import)
{"type":"event","event":"sourceLoaded","path":"/path/to/module.stash"}

// Script terminated
{"type":"event","event":"terminated"}

// Breakpoint confirmed/rejected after set
{"type":"event","event":"breakpointConfirmed","id":7,"verified":true,"line":42}
```

`reason` values for `paused`:
- `"breakpoint"` — line or function breakpoint hit
- `"step"` — stepping operation completed
- `"pause"` — explicit pause request from client
- `"exception"` — exception breakpoint triggered
- `"entry"` — `--debug-wait` first statement (or stopOnEntry analog)
- `"logpoint"` — logpoint fired (note: logpoints do NOT cause a full stop; bridge should NOT send `paused` for logpoints — they emit `output` events only)

#### 5.3.2 Client Requests (DAP bridge → SIP)

All requests carry a `seq` integer for correlation. The bridge must respond to every request.

```json
// Control flow
{"seq":1,"type":"request","command":"pause","threadId":1}
{"seq":2,"type":"request","command":"resume","threadId":1}
{"seq":3,"type":"request","command":"next","threadId":1}
{"seq":4,"type":"request","command":"stepIn","threadId":1}
{"seq":5,"type":"request","command":"stepOut","threadId":1}

// Breakpoints
{"seq":6,"type":"request","command":"setBreakpoints","file":"/path/script.stash","breakpoints":[
  {"line":10,"condition":null,"hitCondition":null,"logMessage":null},
  {"line":25,"condition":"x > 5","hitCondition":null,"logMessage":null}
]}
{"seq":7,"type":"request","command":"setFunctionBreakpoints","breakpoints":[
  {"name":"processItem","condition":null,"hitCondition":null}
]}
{"seq":8,"type":"request","command":"setExceptionBreakpoints","filters":["uncaught"]}

// Inspection (must be sent while paused)
{"seq":9,"type":"request","command":"threads"}
{"seq":10,"type":"request","command":"stackTrace","threadId":1,"startFrame":0,"levels":20}
{"seq":11,"type":"request","command":"scopes","frameId":0}
{"seq":12,"type":"request","command":"variables","variablesReference":42}
{"seq":13,"type":"request","command":"evaluate","expression":"x + 1","frameId":0,"context":"watch"}
{"seq":14,"type":"request","command":"setVariable","variablesReference":42,"name":"x","value":"99"}
{"seq":15,"type":"request","command":"loadedSources"}

// Session
{"seq":16,"type":"request","command":"disconnect","terminateScript":false}
```

The `terminateScript` field in `disconnect`: if `true`, the bridge terminates the VM (sends `terminated` event). If `false` (default), the script continues running and the bridge stops the debug server (allowing a future re-attach).

#### 5.3.3 Server Responses (correlated by seq)

```json
// Generic success (for control flow commands)
{"seq":1,"type":"response","success":true,"body":{}}

// Threads
{"seq":9,"type":"response","success":true,"body":{"threads":[
  {"id":1,"name":"Main Thread"},
  {"id":2,"name":"task-1"}
]}}

// Stack trace
{"seq":10,"type":"response","success":true,"body":{"stackFrames":[
  {"id":0,"name":"processItem","source":"/path/script.stash","line":42,"column":1},
  {"id":1,"name":"<main>","source":"/path/script.stash","line":87,"column":1}
],"totalFrames":2}}

// Scopes
{"seq":11,"type":"response","success":true,"body":{"scopes":[
  {"name":"Local","variablesReference":100,"expensive":false},
  {"name":"Closure","variablesReference":101,"expensive":false},
  {"name":"Global","variablesReference":102,"expensive":true}
]}}

// Variables
{"seq":12,"type":"response","success":true,"body":{"variables":[
  {"name":"x","value":"42","type":"int","variablesReference":0,"indexedVariables":0},
  {"name":"arr","value":"array[3]","type":"array","variablesReference":200,"indexedVariables":3}
]}}

// Evaluate
{"seq":13,"type":"response","success":true,"body":{"result":"43","type":"int","variablesReference":0}}

// setVariable
{"seq":14,"type":"response","success":true,"body":{"value":"99","type":"int","variablesReference":0}}

// setBreakpoints (includes verification per breakpoint)
{"seq":6,"type":"response","success":true,"body":{"breakpoints":[
  {"id":1,"verified":true,"line":10},
  {"id":2,"verified":true,"line":25}
]}}

// loadedSources
{"seq":15,"type":"response","success":true,"body":{"sources":[
  {"path":"/path/script.stash","name":"script.stash"},
  {"path":"/path/utils.stash","name":"utils.stash"}
]}}

// Error response
{"seq":13,"type":"response","success":false,"error":"variable 'foo' is not defined"}
```

### 5.4 Connection Lifecycle

```
Client connects
    │
    ▼
Handshake (auth token exchange)
    │
    ├─ Success → session begins
    │
    └─ Failure → server closes connection

Session active:
    Client sends requests    Server sends events (async, no seq correlation)
    Server sends responses

Client sends "disconnect" (terminateScript=false)
    │
    ├─ Bridge stops listening for new connections
    ├─ vm.Debugger = null (or a no-op debugger)
    ├─ Bridge sends "terminated" only if terminateScript=true
    └─ Script continues running (or terminates)
```

**Re-attach:** After a `disconnect(terminateScript=false)`, the bridge is shut down. The script can be re-attached only if:
1. It was started with `--debug-listen` (the bridge restarts and re-listens on the same port), OR
2. It receives SIGUSR1 again (new bridge started on a new port, new descriptor written)

---

## 6. StashDebugBridge (In-Process Debug Server)

### 6.1 Location

`Stash.Bytecode/Debugging/StashDebugBridge.cs`

This is a new file in `Stash.Bytecode`. It must be 100% AOT-compatible. No reflection, no OmniSharp.

### 6.2 Responsibilities

`StashDebugBridge` is the in-process component that:

1. Starts a `TcpListener` on `127.0.0.1` and an available port (or a specified port)
2. Writes a debug descriptor file to `~/.stash/debug/<PID>.json`
3. Implements `IDebugger` — receives VM callbacks (`OnBeforeExecute`, `OnFunctionEnter`, `OnFunctionExit`, etc.)
4. Manages all debug state: breakpoints, stepping, thread states, variable reference IDs
5. When a client connects (only one allowed), handles the SIP session
6. On disconnect, stops the server, removes the descriptor, and optionally removes itself as `IDebugger`

### 6.3 Debug State (mirrors DebugSession in launch mode)

`StashDebugBridge` must replicate the debug state management currently in `DebugSession.cs`:

| State | Type | Purpose |
|---|---|---|
| `_breakpoints` | `ConcurrentDictionary<string, List<BridgeBreakpoint>>` | Per-file breakpoints |
| `_functionBreakpoints` | `Dictionary<string, BridgeFunctionBreakpoint>` | Named function breakpoints |
| `_threads` | `ConcurrentDictionary<int, BridgeThreadState>` | Per-thread pause/step state |
| `_variableReferences` | `Dictionary<long, BridgeVariableContainer>` | Variable reference IDs for inspection |
| `_loadedSources` | `HashSet<string>` | All loaded source files |
| `_breakOnAllExceptions` | `volatile bool` | Exception breakpoint filter |
| `_nextVariableReference` | `long` (Interlocked) | Variable reference ID counter |

`BridgeBreakpoint` and `BridgeThreadState` are analogues of `StashBreakpoint` and `ThreadState` in `DebugSession.cs`. The stepping state machine logic (StepMode × depth tracking) is identical and should be extracted to a shared helper or documented clearly for the implementer.

> **Implementation note:** The breakpoint matching, hit-count evaluation, logpoint interpolation, stepping state machine, and variable formatting logic from `DebugSession.cs` must be duplicated (or moved to a shared location) in `StashDebugBridge`. The Orchestrator implementing this spec should evaluate whether to refactor `DebugSession` to extract a `DebugCore` class that both `DebugSession` and `StashDebugBridge` use, or to duplicate the logic. Refactoring is preferred but adds risk; duplication is pragmatic for a first implementation.

### 6.4 TCP Server Architecture

```csharp
// Pseudocode — not C# implementation
class StashDebugBridge : IDebugger
{
    TcpListener _listener;
    string _token;        // UUID auth token
    string _descriptorPath; // ~/.stash/debug/<PID>.json

    // Debug state (breakpoints, stepping, threads, variable refs)

    // Called by CLI startup or SIGUSR1 handler
    public int Start(int port = 0)  // returns actual port
    {
        _listener = new TcpListener(IPAddress.Loopback, port);
        _listener.Start();
        port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _token = Guid.NewGuid().ToString("N");
        WriteDescriptor(port, _token);
        Task.Run(() => AcceptLoop()); // background accept loop
        return port;
    }

    private async Task AcceptLoop()
    {
        // Accept exactly one connection at a time
        while (!_disposed)
        {
            var client = await _listener.AcceptTcpClientAsync();
            await HandleClient(client);
        }
    }

    private async Task HandleClient(TcpClient client)
    {
        // 1. Perform handshake
        // 2. Start event sender task
        // 3. Loop reading commands from client
        // 4. On disconnect, clean up
    }

    // IDebugger callbacks — called from VM thread
    public void OnBeforeExecute(SourceSpan span, IDebugScope env, int threadId)
    {
        // Check breakpoints, stepping — if should pause:
        //   Send "paused" event to client
        //   Block on thread's pause gate
        //   Wait for "resume"/"next"/"stepIn"/"stepOut" command
    }

    // ... other IDebugger methods
}
```

### 6.5 Variable Reference Management

The `StashDebugBridge` manages variable reference IDs **within the process** so that the SIP response carries pre-computed reference IDs. This mirrors exactly what `DebugSession.cs` does. Variable reference IDs are cleared on every `resumed` event (exactly as in launch mode). Variable containers hold `IDebugScope` references (valid only while paused) and complex values.

> **Thread safety:** Variable reference containers may be read from the SIP command handler thread while the VM is paused. They are cleared from the VM thread on resume. A lock is required around `_variableReferences` access, matching the pattern in `DebugSession.cs`.

### 6.6 Expression Evaluation

The existing `VMDebugAdapter.EvaluateExpression()` method in `Stash.Dap/VMDebugAdapter.cs` implements expression evaluation by compiling a temp chunk and running it in an isolated VM seeded with current scope bindings. This exact logic must be available to `StashDebugBridge`.

**Options:**
1. Move `VMDebugAdapter.EvaluateExpression()` to `Stash.Bytecode` (correct layer — it depends on `VirtualMachine` and `Compiler`).
2. Duplicate the logic in `StashDebugBridge`.

Option 1 is the right answer. The method belongs in `Stash.Bytecode` anyway (it references `VirtualMachine`, `Compiler`, `Chunk`, etc.). `VMDebugAdapter.cs` in `Stash.Dap` should be refactored to call the relocated method.

---

## 7. Debug Descriptor Files

### 7.1 Location

```
Linux/macOS: ~/.stash/debug/<PID>.json
Windows:     %LOCALAPPDATA%\stash\debug\<PID>.json
```

Cross-platform path resolution:
```csharp
string debugDir = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
    ".stash", "debug");
// Windows fallback: if UserProfile not writable, use LocalApplicationData
```

### 7.2 Descriptor Format

```json
{
  "pid": 12345,
  "port": 54321,
  "token": "a1b2c3d4e5f6789012345678901234ab",
  "scriptPath": "/absolute/path/to/script.stash",
  "startedAt": "2026-04-28T18:00:00.000Z",
  "mode": "listen"
}
```

`mode` is `"listen"` (pre-configured with `--debug-listen`) or `"on-demand"` (activated by SIGUSR1).

### 7.3 Lifecycle

- **Written:** When `StashDebugBridge.Start()` is called
- **Deleted:** When any of the following occurs:
  - The script process exits (registered via `AppDomain.ProcessExit`)
  - `StashDebugBridge.Stop()` is called (disconnect with `terminateScript=false`)
  - The script receives SIGTERM/SIGHUP while the bridge is active
- **Stale descriptors:** When `stash-dap` reads a descriptor, it checks if the PID is still alive before connecting. If the process is gone, the descriptor is deleted and an error is returned to the DAP client.

### 7.4 Directory Initialization

The `~/.stash/debug/` directory is created with mode `0700` (owner-only) on Linux/macOS. On Windows, the directory inherits ACLs from `%LOCALAPPDATA%`.

---

## 8. CLI Changes

### 8.1 New Flags

```
stash script.stash --debug-listen          # Start bridge on a random port; run immediately
stash script.stash --debug-listen:9229     # Start bridge on port 9229; run immediately
stash script.stash --debug-wait            # Start bridge on a random port; pause at first statement
stash script.stash --debug-wait:9229       # Same, with specific port
```

`--debug-listen` and `--debug-wait` are exclusive — combining them is a parse error.

When `--debug-listen` or `--debug-wait` is active:
1. `StashDebugBridge.Start(port)` is called before the VM executes the script
2. The VM's `Debugger` property is set to the bridge **before** `Execute()` is called
3. The VM runs in `RunInner<DebugOn>` mode from the beginning
4. The port and descriptor are logged to stderr: `[stash] Debug server listening on port 54321 (PID 12345)`
5. For `--debug-wait`: the bridge pauses at the very first `OnBeforeExecute` call until a client connects and sends a `resume` command

### 8.2 SIGUSR1 Ad-Hoc Attach (Linux/macOS Only)

When a Stash script is started **without** `--debug-listen`, it runs in `RunInner<DebugOff>` — the debug path is inactive. Sending SIGUSR1 to the process must activate debug mode.

**This requires the VM to support mid-flight debug activation.**

#### 8.2.1 Safe-Point Restart Mechanism

The VM must support a "restart in debug mode" mechanism that can be triggered from a signal handler:

1. Add `volatile bool _debugActivationRequested` to `VirtualMachine`
2. In `RunInner<DebugOff>`, check `_debugActivationRequested` at **safe points**: every backward jump (loop backs) and every `Call` opcode dispatch
3. When detected, throw a `DebugActivationException` (a new internal exception type)
4. In the VM's `Execute()` method, catch `DebugActivationException`, set `Debugger = _pendingDebugger`, and re-call `Execute()` (or the appropriate continuation) in DebugOn mode

> **Performance impact:** Adding a volatile read at every backward jump and Call opcode in `RunInner<DebugOff>` adds approximately one `volatile` read per loop iteration or function call. For typical sysadmin scripts, this is negligible. For tight arithmetic loops (the main perf benchmark target), backward jumps may be frequent. The Orchestrator should measure the impact on `bench_algorithms.stash` and `bench_function_calls.stash` before committing.

> **Alternative (lower perf impact):** Check `_debugActivationRequested` only at **function entry** (the `Call` opcode) and not at backward jumps. This delays activation until the next function call, which for many scripts happens quickly. For scripts stuck in a tight loop without function calls, the delay could be indefinite.

> **Recommendation:** Start with function-entry-only checks. If the scenario of "attach to a script in a tight loop" is important, add backward-jump checks.

#### 8.2.2 Signal Handler Registration

In `Stash.Cli/Program.cs`, before starting any script execution:

```csharp
// Register SIGUSR1 handler for all non-Windows script runs
if (!OperatingSystem.IsWindows())
{
    PosixSignalRegistration.Create(PosixSignal.SIGUSR1, context =>
    {
        context.Cancel = true; // Don't terminate the process
        if (_activeVM?.Debugger == null)
        {
            var bridge = new StashDebugBridge(_activeVM!);
            int port = bridge.Start();
            _activeVM.PendingDebugger = bridge; // triggers safe-point restart
            Console.Error.WriteLine($"[stash] Debug server activated on port {port}");
        }
    });
}
```

Note: `_activeVM` is already a static field in `Program.cs` (it exists for lock cleanup). This extends its use.

### 8.3 `--debug` Flag Interaction

The existing `--debug` flag (which currently prints a warning about bytecode VM not being supported) should be updated to redirect users to `--debug-listen` for the attach workflow, or re-purposed as the CLI interactive debugger (a separate concern).

---

## 9. Stash.Dap Changes

### 9.1 StashAttachHandler

New file: `Stash.Dap/Handlers/StashAttachHandler.cs`

```csharp
// Pseudocode
public class StashAttachHandler : AttachHandlerBase
{
    private readonly DebugSession _session;

    public override Task<AttachResponse> Handle(AttachRequestArguments request, CancellationToken ct)
    {
        // Extract from raw JSON:
        //   processId (int, optional)
        //   host (string, default "127.0.0.1")
        //   port (int, optional — required if processId not given)
        //   token (string, optional — read from descriptor if processId given)

        _session.Attach(processId, host, port, token);
        return Task.FromResult(new AttachResponse());
    }
}
```

Register in `StashDebugServer.cs` via `.WithHandler<StashAttachHandler>()`.

### 9.2 DebugSession.Attach()

New method on `DebugSession`:

```csharp
// Pseudocode — no implementation code intended
public void Attach(int? processId, string host, int? port, string? token)
{
    // 1. Resolve connection details
    if (processId.HasValue && !port.HasValue)
    {
        var descriptor = ReadDescriptor(processId.Value); // reads ~/.stash/debug/<PID>.json
        port = descriptor.Port;
        token ??= descriptor.Token;
        _scriptPath = descriptor.ScriptPath;
    }

    // 2. Verify PID alive (if known)
    if (processId.HasValue && !IsProcessAlive(processId.Value))
        throw new InvalidOperationException($"Process {processId} is not running");

    // 3. Create SIP bridge client
    _sipClient = new SipBridgeClient(host, port.Value, token);
    _sipClient.Connect();

    // The handshake is performed inside Connect()
    // If token mismatch, Connect() throws an exception
    _scriptPath ??= _sipClient.ScriptPath;

    // 4. Start event pump — reads SIP events and sends DAP events
    Task.Run(() => SipEventPump(_sipClient));

    // 5. Signal initialized (DAP handshake complete)
    _server!.SendNotification(new InitializedEvent());

    _isAttachMode = true;
}
```

Key behavioral difference in attach mode vs. launch mode:
- No interpreter thread is started
- `ConfigurationDone()` immediately signals "ready" (no gate to wait for)
- All handler calls (GetStackTrace, GetVariables, etc.) are forwarded to `_sipClient`
- SIP events are translated to DAP events by the event pump

### 9.3 SipBridgeClient

New file: `Stash.Dap/SipBridgeClient.cs`

`SipBridgeClient` wraps the TCP connection to `StashDebugBridge` and provides:

1. **`Connect()`** — establishes TCP connection, performs handshake, throws on auth failure
2. **`SendCommandAsync(object cmd)`** — serializes and sends a SIP command, returns the correlated response
3. **`EventReceived`** — event/callback invoked for each incoming SIP event (from the async reader loop)
4. **High-level API mirroring `DebugSession`'s public methods:**
   - `PauseAsync(int threadId)`
   - `ResumeAsync(int threadId)`
   - `NextAsync(int threadId)`, `StepInAsync()`, `StepOutAsync()`
   - `SetBreakpointsAsync(string file, IEnumerable<SourceBreakpoint> bps)`
   - `GetStackTraceAsync(int threadId)`
   - `GetScopesAsync(int frameId)`
   - `GetVariablesAsync(long variablesReference)`
   - `EvaluateAsync(string expression, int? frameId)`
   - `SetVariableAsync(long variablesReference, string name, string value)`
   - `GetThreadsAsync()`
   - `GetLoadedSourcesAsync()`
   - `DisconnectAsync(bool terminateScript)`

All methods are async (Task-returning) to avoid blocking the OmniSharp handler threads.

### 9.4 DebugSession Routing in Attach Mode

`DebugSession` gains an `_isAttachMode` bool. All handler-facing methods check this flag:

```csharp
public IReadOnlyList<StashStackFrame> GetStackTrace(int threadId)
{
    if (_isAttachMode)
    {
        var response = _sipClient!.GetStackTraceAsync(threadId).GetAwaiter().GetResult();
        return MapSipStackFrames(response);
    }

    // ... existing in-process logic
}
```

> **Alternative:** Use a `IDebugBackend` interface (bridge pattern) to route calls. This is cleaner architecturally but adds more types. The Orchestrator should choose based on how much `DebugSession.cs` diverges between modes.

### 9.5 SIP Event Pump → DAP Events

The `SipEventPump` task runs in a background Task and translates:

| SIP Event | DAP Event |
|---|---|
| `paused` | `StoppedEvent` with reason, threadId, description |
| `continued` | `ContinuedEvent` |
| `output` | `OutputEvent` with category + output |
| `threadStarted` | `ThreadEvent` with reason=started |
| `threadExited` | `ThreadEvent` with reason=exited |
| `sourceLoaded` | `LoadedSourceEvent` with path + name |
| `terminated` | `TerminatedEvent` + `ExitedEvent(0)` |

### 9.6 ConfigurationDone in Attach Mode

In attach mode, `ConfigurationDone()` must:
1. Forward any breakpoints set during the configuration phase to the bridge via `SetBreakpointsAsync()`
2. Release the configuration gate (same pattern as launch mode)
3. Return immediately (no interpreter thread to unblock)

> **Note:** In attach mode, the client may send `setBreakpoints` before `configurationDone`. These must be queued and forwarded to the bridge after `Connect()` succeeds, or buffered and sent in `ConfigurationDone()`.

### 9.7 Capabilities Update

In `StashDebugServer.cs`, add:
```csharp
options.Capabilities.SupportsTerminateRequest = true; // terminateScript support
```

And the `initialize` response must now declare:
```
"supportsAttachRequest": true
```

OmniSharp sets this automatically when `StashAttachHandler` is registered (OmniSharp's `AttachHandlerBase` is detected by the framework).

### 9.8 Disconnect Behavior in Attach Mode

`Disconnect()` in attach mode must:
1. Send `disconnect(terminateScript=false)` to the bridge (script continues)
2. Clear variable references
3. Do NOT interrupt any interpreter thread (there is none in `stash-dap`)
4. Set `_isAttachMode = false` and null out `_sipClient`

If the user explicitly terminates (e.g., VS Code's "Stop" button with terminate intent), send `disconnect(terminateScript=true)`.

> **DAP protocol note:** The `disconnect` request has `terminateDebuggee: boolean`. Map this to `terminateScript` in the SIP command.

---

## 10. VS Code Extension Changes

### 10.1 New Launch Configuration: Attach

The extension must register an `attach` configuration type in `package.json`:

```json
{
  "type": "stash",
  "request": "attach",
  "name": "Attach to Stash Process",
  "processId": "${command:stash.pickProcess}",
  "host": "localhost",
  "port": null
}
```

Configuration fields:

| Field | Type | Required | Default | Description |
|---|---|---|---|---|
| `processId` | `int \| string` | either this or `port` | — | PID of the running Stash process. Use `${command:stash.pickProcess}` for interactive picker. |
| `host` | `string` | no | `"127.0.0.1"` | Host for direct port connect. Only meaningful with `port`. |
| `port` | `int` | either this or `processId` | — | Direct port for pre-configured `--debug-listen`. |

Two configuration snippets should be added to the extension's snippet templates:

```json
"Stash: Attach to Process": {
  "body": {
    "type": "stash",
    "request": "attach",
    "name": "Attach to Stash Process",
    "processId": "${command:stash.pickProcess}"
  }
},
"Stash: Attach to Debug Port": {
  "body": {
    "type": "stash",
    "request": "attach",
    "name": "Attach by Port",
    "host": "localhost",
    "port": 9229
  }
}
```

### 10.2 Process Picker Command

New VS Code command: `stash.pickProcess`

This command:
1. Scans `~/.stash/debug/` for all `*.json` files
2. For each descriptor, verifies the PID is alive (cross-platform process check)
3. Removes stale descriptors automatically
4. Presents a QuickPick UI with: `PID 12345 — script.stash (started 5 minutes ago, port 54321)`
5. Returns the selected PID as a string (DAP's `${command:...}` substitution)

On Windows: scan `%LOCALAPPDATA%\stash\debug\*.json` instead.

### 10.3 Configuration Schema

Add to the extension's `debugger.configurationAttributes` for `attach` request type in `package.json`:

```json
"attach": {
  "required": [],
  "properties": {
    "processId": {
      "type": ["integer", "string"],
      "description": "PID of the running Stash process to attach to. Use ${command:stash.pickProcess} for a picker."
    },
    "host": {
      "type": "string",
      "description": "Hostname for direct port attach. Defaults to 127.0.0.1.",
      "default": "127.0.0.1"
    },
    "port": {
      "type": "integer",
      "description": "TCP port for direct attach (when using --debug-listen:PORT). Not needed for process picker."
    }
  }
}
```

### 10.4 Extension Settings

No new settings needed for the core attach feature. `stash.dapPath` already controls the DAP server binary path.

---

## 11. Security Model

### 11.1 Auth Token

The debug descriptor file contains a random UUID token (`_token = Guid.NewGuid().ToString("N")`). The bridge requires the connecting client to send this exact token in the handshake. Wrong token → connection closed.

**Why this is sufficient:**
- The descriptor file is readable only by the process owner (`chmod 700` on the directory)
- The TCP socket is bound to `127.0.0.1` (loopback only — no remote access)
- An attacker on the same machine with the same user ID already has full access to the process

**Why authentication is still needed:**
- Prevents other processes running as the same user from accidentally (or maliciously) attaching — e.g., a misbehaving script that probes debug ports
- Makes it explicit that attachment is intentional

### 11.2 Loopback-Only Binding

`TcpListener` must always bind to `IPAddress.Loopback` (127.0.0.1), never `IPAddress.Any`. This is enforced in `StashDebugBridge.Start()`. The `host` field in `StashAttachHandler` is validated to be `"127.0.0.1"` or `"localhost"` — remote attach is explicitly not supported.

> **Future consideration:** Allow specifying a host via a new flag (e.g., `--debug-listen:0.0.0.0:9229`) for containerized/remote debugging scenarios. This would require additional security measures (TLS, token-in-URL) and is explicitly out of scope for this spec.

### 11.3 Descriptor File Permissions

```csharp
// After creating ~/.stash/debug/ directory:
if (!OperatingSystem.IsWindows())
{
    // Set directory permissions: rwx------ (owner only)
    File.SetUnixFileMode(debugDir, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
}
```

Individual descriptor files inherit directory permissions.

---

## 12. Platform Differences

| Feature | Linux/macOS | Windows |
|---|---|---|
| Pre-configured attach | `--debug-listen[:PORT]` | `--debug-listen[:PORT]` |
| Ad-hoc attach (no flag) | SIGUSR1 | **Not supported** |
| Descriptor location | `~/.stash/debug/` | `%LOCALAPPDATA%\stash\debug\` |
| Directory permissions | `chmod 0700` | ACL inherits from LocalAppData |
| Transport | TCP loopback (127.0.0.1) | TCP loopback (127.0.0.1) |
| PID alive check | `/proc/<pid>` or `kill(pid, 0)` | `Process.GetProcessById(pid)` |

Windows users who need ad-hoc attach (script not started with `--debug-listen`) have no equivalent. Options to document for them:
1. Use `--debug-listen` from the start (recommended for production scripts)
2. Add a Stash code snippet to call `sys.debugListen()` at startup (future stdlib addition — see Section 15)

---

## 13. Implementation Checklist

This is the complete list of files that must be created or modified. The Orchestrator should treat this as the implementation task list.

### New Files

| File | Description |
|---|---|
| `Stash.Bytecode/Debugging/StashDebugBridge.cs` | In-process debug server (IDebugger + TCP server + SIP protocol) |
| `Stash.Bytecode/Debugging/SipMessage.cs` | SIP message model types for System.Text.Json serialization |
| `Stash.Dap/SipBridgeClient.cs` | External DAP↔SIP bridge client |
| `Stash.Dap/Handlers/StashAttachHandler.cs` | DAP `attach` request handler |

### Modified Files

| File | Change |
|---|---|
| `Stash.Bytecode/VM/VirtualMachine.cs` | Add `PendingDebugger` property + `_debugActivationRequested` volatile flag |
| `Stash.Bytecode/VM/VirtualMachine.Dispatch.cs` | Check `_debugActivationRequested` at safe points in `RunInner<DebugOff>` |
| `Stash.Bytecode/VM/VirtualMachine.cs` | Move `EvaluateExpression` equivalent out of DAP layer into Bytecode layer |
| `Stash.Dap/DebugSession.cs` | Add `Attach()`, `_isAttachMode`, `_sipClient`; route all handler methods through flag check |
| `Stash.Dap/StashDebugServer.cs` | Register `StashAttachHandler`; set `SupportsTerminateRequest = true` |
| `Stash.Cli/Program.cs` | Add `--debug-listen[:PORT]` and `--debug-wait[:PORT]` flags; SIGUSR1 handler; `RunFileWithDebugBridge()` method |
| `.vscode/extensions/stash-lang/package.json` | Add `attach` configuration type; add `stash.pickProcess` command; update schema |
| `.vscode/extensions/stash-lang/src/debugAdapterFactory.ts` | Handle `request: "attach"` in factory (pass config fields through) |
| `.vscode/extensions/stash-lang/src/extension.ts` | Register `stash.pickProcess` command handler |
| `docs/DAP — Debug Adapter Protocol.md` | Document attach mode: launch config, process picker, `--debug-listen`, SIP protocol (high-level), platform notes |

### New Test Files

| File | Description |
|---|---|
| `Stash.Tests/Dap/DapAttachTests.cs` | Integration tests for attach mode |
| `Stash.Tests/Dap/SipBridgeClientTests.cs` | Unit tests for SIP protocol handling |
| `Stash.Tests/Dap/SipBridgeTests.cs` | Unit tests for StashDebugBridge |

---

## 14. Test Scenarios

### 14.1 Happy Path

| Test | Description |
|---|---|
| `PreConfiguredListen_AttachByPid` | Script started with `--debug-listen`, attach by PID, set breakpoint, hit it |
| `PreConfiguredListen_AttachByPort` | Script started with `--debug-listen:9229`, attach directly by port |
| `AttachAfterSigusr1` | Script started normally, SIGUSR1 sent, attach by PID, set breakpoint |
| `AttachSetsBreakpointBeforeStart` | Set breakpoint before `configurationDone`, verify it fires |
| `DetachAndReattach` | Attach, debug, detach (script continues), re-attach by SIGUSR1 |
| `TerminateScript` | Attach, send disconnect with `terminateScript=true`, verify process exits |
| `StepOverAcrossAttach` | Attach to paused script, step over, observe next line |
| `InspectVariablesAtBreakpoint` | Arrays, dicts, struct instances expand correctly |
| `EvaluateExpressionWhilePaused` | Watch expression evaluates in correct scope |
| `SetVariableAndContinue` | Modify a variable via attach, continue, verify effect |
| `FunctionBreakpointInAttachMode` | Function breakpoint fires on function entry |
| `MultipleThreadsVisible` | Script with `task.run()` — both threads visible, independent stepping |
| `LoadedSourcesOnAttach` | Already-loaded modules appear in `loadedSources` response |

### 14.2 Error Cases

| Test | Description |
|---|---|
| `AttachToNonExistentPid` | Descriptor exists but process is gone → clear descriptor, return error |
| `AttachNoDescriptor` | PID given but no descriptor file → meaningful error |
| `AttachWrongToken` | Forge a descriptor with wrong token → handshake fails |
| `AttachAlreadyAttached` | Second attach attempt while one is active → reject with error |
| `SipConnectionDropped` | TCP connection lost mid-session → DAP session terminates gracefully |
| `ScriptExitsDuringAttach` | Script finishes while debugger is attached → `terminated` event sent |

### 14.3 Platform-Specific

| Test | Platform | Description |
|---|---|---|
| `Sigusr1ActivatesDebugBridge` | Linux/macOS | Send SIGUSR1 to running process; verify bridge starts |
| `Sigusr1WhileAlreadyListening` | Linux/macOS | Send SIGUSR1 twice; second is no-op |
| `WindowsRequiresDebugListen` | Windows | Attach without `--debug-listen` returns informative error |

---

## 15. Open Questions / Deferred Decisions

### OQ-1: Logic Sharing Between DebugSession and StashDebugBridge

Should the Orchestrator extract a shared `DebugCore` class (breakpoint matching, stepping state machine, hit count evaluation, logpoint formatting, variable formatting) that both `DebugSession` and `StashDebugBridge` use?

**Arguments for:** Avoids duplicating ~400 lines of logic. Ensures consistency.
**Arguments against:** Requires moving `DebugCore` to a layer both can access. `DebugSession` is in `Stash.Dap` (non-AOT); `StashDebugBridge` is in `Stash.Bytecode` (AOT). `DebugCore` would need to live in `Stash.Bytecode` (or a new `Stash.Debug.Core` assembly), and `DebugSession` would reference it. Adds a new assembly or deepens Stash.Bytecode.

**Recommended:** Extract to `Stash.Bytecode/Debugging/`. It already references all the types needed. `Stash.Dap` references `Stash.Bytecode`, so `DebugSession` can use the shared logic.

### OQ-2: Safe-Point Check Granularity

Should `_debugActivationRequested` be checked at:
- Every backward jump + every Call opcode (more responsive, slightly more overhead)
- Every Call opcode only (less overhead, potentially delayed activation)

**Recommended:** Start with Call-opcode-only. Document the limitation. If a user-reported case proves important (long tight loop with no calls), add backward-jump checks as a follow-up.

### OQ-3: Re-Attach After Detach

After `disconnect(terminateScript=false)`:
- Option A: Bridge is completely torn down. Re-attach requires a new SIGUSR1 or a restart.
- Option B: Bridge keeps the TCP listener alive for a `--debug-listen` script. Re-attach is seamless.

**Recommended:** Option B for `--debug-listen` scripts (they opted into persistent debug mode). Option A for SIGUSR1-triggered bridges (ad-hoc sessions are inherently one-shot).

### OQ-4: `sys.debugListen()` Stdlib Function

A future addition: scripts could call `sys.debugListen(port?)` to self-activate the debug bridge programmatically, without the `--debug-listen` CLI flag or SIGUSR1. This is valuable for:
- Scripts that conditionally enable debugging based on an env var
- Scripts embedded in other tools
- Platform-agnostic ad-hoc attach on Windows

This is deferred from this spec but should be tracked as a follow-up.

### OQ-5: Remote Attach (Non-Loopback)

Supporting remote attach (e.g., debugging a Stash script on a remote server) would require:
- Non-loopback binding option (security risk without TLS)
- TLS certificate management
- Token-based auth (already designed)

This is explicitly out of scope. The recommended path is SSH port forwarding: `ssh -L 9229:localhost:9229 user@server` then attach to `localhost:9229`.

### OQ-6: Windows Ad-Hoc Attach

Windows lacks SIGUSR1. Potential mechanisms:
- Named events (`EventWaitHandle` with a well-known name `Global\StashDebug_<PID>`)
- A polling thread inside the CLI that checks for a flag file or named event

Deferred. Document Windows limitation clearly. `--debug-listen` is the recommended approach for Windows production scripts.

---

## 16. Decision Log

| Date | Decision | Rationale |
|---|---|---|
| 2026-04-28 | SIP over TCP NDJ | AOT-compatible; cross-platform; simple to implement without OmniSharp |
| 2026-04-28 | Logic in StashDebugBridge (not SipBridgeClient) | Bridge owns debug state; external DAP server is thin translator |
| 2026-04-28 | Auth token in descriptor file | Prevents accidental attach; filesystem permissions provide identity verification |
| 2026-04-28 | `terminateScript=false` on disconnect | Sysadmin use case: inspect a running script without killing it |
| 2026-04-28 | Safe-point restart at Call opcode | Balance: responsive enough for most scripts; low VM overhead |
| 2026-04-28 | SIGUSR1 only on non-Windows for ad-hoc attach | Platform reality; document clearly; `--debug-listen` is the Windows path |
| 2026-04-28 | EvaluateExpression moved to Stash.Bytecode layer | Correct layering; VMDebugAdapter in Stash.Dap calls the shared method |
