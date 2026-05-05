---
applyTo: "Stash.Dap/**"
---

# DAP Debug Adapter Guidelines

The Stash DAP server implements the Debug Adapter Protocol using **OmniSharp** (v0.19.9). It must **never** be built with Native AOT — OmniSharp uses reflection-heavy DryIoc. `build.stash` enforces a >20MB binary-size guard. See `docs/DAP — Debug Adapter Protocol.md` for feature details.

## Project Structure

```
Stash.Dap/
├── Program.cs             → Entry point: calls StashDebugServer.RunAsync()
├── StashDebugServer.cs    → Server builder: DI registration + 18 handlers + capabilities
├── DebugSession.cs        → Core state: IDebugger impl, breakpoints, stepping, variables
└── Handlers/              → 18 thin DAP request handlers (delegate to DebugSession)
```

**Dependencies:** `OmniSharp.Extensions.DebugAdapter.Server` v0.19.9, `Stash.Bytecode`, `Stash.Core`, `Stash.Stdlib`, `Stash.Tap` (project references).

## DI Services

Single singleton registered in `StashDebugServer.cs`:

| Service        | Role                                                                              |
| -------------- | --------------------------------------------------------------------------------- |
| `DebugSession` | Central debug state — implements `IDebugger`, bridges DAP protocol to interpreter |

## Capabilities

Configured in `StashDebugServer.cs`:

- Conditional breakpoints, hit-conditional breakpoints, logpoints
- Function breakpoints (break on named function entry)
- Evaluate for hovers (inspect expressions while paused)
- Set variable (modify values during debugging)
- Loaded sources request
- Exception filters: `"all"` (default off), `"uncaught"` (default on)

## Handler Pattern

All 18 handlers are thin adapters — extract request data and delegate to `DebugSession`:

```csharp
public class StashFooHandler : FooHandlerBase
{
    private readonly DebugSession _session;

    public StashFooHandler(DebugSession session) { _session = session; }

    public override Task<FooResponse> Handle(FooArguments request, CancellationToken ct)
    {
        var result = _session.Foo(...);
        return Task.FromResult(new FooResponse { ... });
    }
}
```

Register new handlers in `StashDebugServer.cs` via `.WithHandler<StashNewHandler>()`.

### All 18 Handlers

| Handler                               | DAP Request             | Delegates To                                                            |
| ------------------------------------- | ----------------------- | ----------------------------------------------------------------------- |
| `StashLaunchHandler`                  | Launch                  | `session.Launch(program, cwd, stopOnEntry, args, testMode, testFilter)` |
| `StashConfigurationDoneHandler`       | ConfigurationDone       | `session.ConfigurationDone()`                                           |
| `StashDisconnectHandler`              | Disconnect              | `session.Disconnect()`                                                  |
| `StashSetBreakpointsHandler`          | SetBreakpoints          | `session.SetBreakpoints(path, breakpoints)`                             |
| `StashSetExceptionBreakpointsHandler` | SetExceptionBreakpoints | `session.SetExceptionBreakpoints(filters)`                              |
| `StashSetFunctionBreakpointsHandler`  | SetFunctionBreakpoints  | `session.SetFunctionBreakpoints(breakpoints)`                           |
| `StashThreadsHandler`                 | Threads                 | Returns single thread (id=1, "Main Thread")                             |
| `StashContinueHandler`                | Continue                | `session.Continue()`                                                    |
| `StashNextHandler`                    | Next                    | `session.Next()`                                                        |
| `StashStepInHandler`                  | StepIn                  | `session.StepIn()`                                                      |
| `StashStepOutHandler`                 | StepOut                 | `session.StepOut()`                                                     |
| `StashPauseHandler`                   | Pause                   | `session.Pause()`                                                       |
| `StashStackTraceHandler`              | StackTrace              | `session.GetStackTrace()`                                               |
| `StashScopesHandler`                  | Scopes                  | `session.GetScopes(frameId)`                                            |
| `StashVariablesHandler`               | Variables               | `session.GetVariables(variablesReference)`                              |
| `StashEvaluateHandler`                | Evaluate                | `session.Evaluate(expression, frameId)`                                 |
| `StashSetVariableHandler`             | SetVariable             | `session.SetVariable(ref, name, value)`                                 |
| `StashLoadedSourcesHandler`           | LoadedSources           | `session.GetLoadedSources()`                                            |

## DebugSession Architecture

`DebugSession` implements `IDebugger` (defined in `Stash.Core/Debugging/IDebugger.cs`) and manages all debug state.

### Threading Model

Single-threaded interpreter. The interpreter runs in a dedicated `System.Threading.Thread`. Pause/resume uses `ManualResetEventSlim` gate synchronization — the interpreter blocks on the gate in `OnBeforeExecute()` when paused.

### IDebugger Callbacks

The interpreter calls these hooks during execution:

| Callback                               | When                                                                  |
| -------------------------------------- | --------------------------------------------------------------------- |
| `OnBeforeExecute(span, env)`           | Before every statement — checks breakpoints, stepping, pause requests |
| `OnFunctionEnter(name, callSite, env)` | On function call — checks function breakpoints                        |
| `OnFunctionExit(name)`                 | On function return — used for step-out depth tracking                 |
| `OnError(error, callStack)`            | On runtime error — breaks if exception breakpoints active             |
| `OnSourceLoaded(filePath)`             | When a new source file is loaded — sends DAP LoadedSource event       |
| `OnOutput(category, text)`             | Interpreter stdout/stderr — forwarded through DAP output events       |
| `ShouldBreakOnException(error)`        | Returns true if `_breakOnAllExceptions`                               |
| `ShouldBreakOnFunctionEntry(name)`     | Returns true if function breakpoint exists for name                   |

### Stepping State Machine

```
StepMode.None     → Normal execution (run to next breakpoint or pause)
StepMode.StepIn   → Stop at very next statement
StepMode.StepOver → Stop at next statement at same or shallower call depth
StepMode.StepOut  → Stop at next statement at shallower call depth
```

Depth tracked via `_stepDepth` (call stack count when step initiated).

### Breakpoint Types

1. **Source breakpoints** (`ConcurrentDictionary<string, List<StashBreakpoint>>`) — file path + line, with optional condition, hit condition, log message
2. **Function breakpoints** (`Dictionary<string, FunctionBreakpointEntry>`) — function name, with optional condition/hit condition

**Breakpoint evaluation:** `CheckBreakpointAtSpan()` evaluates conditions, increments hit counts, handles logpoints (interpolate `{expression}` in log messages).

**Hit condition syntax:** `== 5`, `>= 3`, `% 2 == 0` (parsed in `EvaluateHitCondition()`).

### Variable References

DAP uses integer reference IDs for expandable objects. `DebugSession` allocates IDs on demand:

- `AllocateExpansion(name, value)` → assigns monotonic long ID
- `_variableReferences: Dictionary<long, VariableContainer>` maps IDs to containers
- `FormatVariable(name, value)` → renders value as DAP `Variable` with type string and display value
- Supports: null, long, double, bool, string, list, dict, struct instance, enum, function, lambda, bound method, namespace

### Scope Resolution

`GetScopes(frameId)` walks the environment chain to produce Local → Closure → Global scopes. `ResolveEnvironmentForFrame(frameId)` maps DAP frame IDs to `StashEnv`:

- Top frame → `_pausedEnvironment`
- Intermediate frames → `CallFrame.LocalScope`
- Script frame (id=0) → `_interpreter.Globals`

## Supporting Types (Stash.Core/Debugging/)

| Type          | Purpose                                                                  |
| ------------- | ------------------------------------------------------------------------ |
| `IDebugger`   | Interface contract between interpreter and debug adapter                 |
| `Breakpoint`  | Breakpoint state (id, file, line, condition, hit count, logpoint)        |
| `CallFrame`   | Stack frame info (id, function name, call site, local scope)             |
| `PauseReason` | Enum: Breakpoint, Step, Pause, Exception, Entry, FunctionBreakpoint      |
| `DebugScope`  | Scope classification (Local, Closure, Global) with environment reference |

## Tests

DAP tests live in `Stash.Tests/Dap/` (3 files):

- **`DapHandlerTests.cs`** — Handler unit tests: instantiate `DebugSession` + handler, call `Handle()` directly, assert response fields
- **`DebugSessionTests.cs`** — Session state tests: breakpoint management, stepping, scope resolution, variable formatting. Uses reflection helpers for private methods (`EvaluateHitCondition`, `FormatVariable`, `InterpolateLogMessage`)
- **`DapIntegrationTests.cs`** — End-to-end tests with interpreter
