# Stash — DAP (Debug Adapter Protocol)

> **Status:** Draft v0.1
> **Created:** March 2026
> **Purpose:** Source of truth for the Stash DAP server — implementation details, capabilities, and editor integration.
>
> **Companion documents:**
>
> - [Language Specification](../Stash%20—%20Language%20Specification.md) — syntax, type system, debugging hooks (`IDebugger`), interpreter architecture
> - [Standard Library Reference](../Stash%20—%20Standard%20Library%20Reference.md) — built-in namespace functions
> - [LSP — Language Server Protocol](LSP%20—%20Language%20Server%20Protocol.md) — language server
> - [TAP — Testing Infrastructure](TAP%20—%20Testing%20Infrastructure.md) — testing primitives and harness

The Stash DAP server exposes a standard [Debug Adapter Protocol](https://microsoft.github.io/debug-adapter-protocol/) interface over stdio, enabling any DAP-compatible editor (VS Code, Neovim, Emacs, etc.) to debug Stash scripts with breakpoints, stepping, variable inspection, and expression evaluation.

For the language-level debugging hooks (`IDebugger` interface, `CallFrame`, `SourceSpan`) that the DAP server builds on, see [Section 11 of the Language Specification](../Stash%20—%20Language%20Specification.md#11-debugging-support). For runtime value types and their CLR mappings, see [Section 4 of the Language Specification](../Stash%20—%20Language%20Specification.md#4-type-system).

---

## Architecture

### Three-Player Model

```
┌─────────────────────────────────────────────────────────────────┐
│                       DAP Client (e.g. VS Code)                 │
└──────────────────────────────┬──────────────────────────────────┘
                               │  DAP protocol (JSON over stdio)
┌──────────────────────────────▼──────────────────────────────────┐
│                         Stash.Dap (DAP Server)                  │
│                                                                 │
│   StashDebugServer  ──registers──►  16 Request Handlers         │
│          │                                                      │
│          └──► DebugSession (IDebugger) ◄──── Interpreter calls  │
└──────────────────────────────┬──────────────────────────────────┘
                               │  IDebugger interface
┌──────────────────────────────▼──────────────────────────────────┐
│                   Stash.Interpreter (tree-walk evaluator)       │
│                                                                 │
│   Calls OnBeforeExecute() before each statement                 │
│   Calls OnFunctionEnter() / OnFunctionExit() around calls       │
└─────────────────────────────────────────────────────────────────┘
```

There are three players:

1. **DAP Client** — the editor or IDE. Sends requests (`setBreakpoints`, `continue`, `variables`, …) and receives events (`stopped`, `output`, `terminated`).
2. **Stash.Dap** — translates between the DAP wire format and the interpreter. Owns `DebugSession`, which is the central coordinator.
3. **Stash.Interpreter** — the tree-walk evaluator. It calls back into `DebugSession` via the `IDebugger` interface at every statement and function boundary.

The interpreter thread and the DAP I/O thread are distinct. `ManualResetEventSlim` gates keep them synchronized.

---

### Project Structure

```
Stash.Dap/
├── Program.cs                          # Entry point — calls StashDebugServer.RunAsync()
├── StashDebugServer.cs                 # Bootstrap: OmniSharp server + DI wiring
├── DebugSession.cs                     # Core bridge, ~908 lines (IDebugger impl)
└── Handlers/
    ├── StashInitializeHandler.cs       # Capability negotiation
    ├── StashLaunchHandler.cs           # Parse launch args, start interpreter
    ├── StashConfigurationDoneHandler.cs
    ├── StashDisconnectHandler.cs
    ├── StashSetBreakpointsHandler.cs
    ├── StashSetExceptionBreakpointsHandler.cs
    ├── StashThreadsHandler.cs
    ├── StashContinueHandler.cs
    ├── StashNextHandler.cs             # Step over
    ├── StashStepInHandler.cs
    ├── StashStepOutHandler.cs
    ├── StashPauseHandler.cs
    ├── StashStackTraceHandler.cs
    ├── StashScopesHandler.cs
    ├── StashVariablesHandler.cs
    └── StashEvaluateHandler.cs
```

**Dependencies:**

| Package                                    | Version       | Role                                   |
| ------------------------------------------ | ------------- | -------------------------------------- |
| `Stash.Core`                               | (project ref) | Lexer and parser                       |
| `Stash.Interpreter`                        | (project ref) | Tree-walk evaluator and runtime values |
| `OmniSharp.Extensions.DebugAdapter.Server` | 0.19.9        | DAP protocol implementation            |

---

### Execution Flow

```
Editor sends "launch"
        │
        ▼
StashLaunchHandler
  → parse program/args/cwd/stopOnEntry from JSON
  → DebugSession.Launch(...)
        │
        ▼
  ┌─────────────────────────────────────────────────────┐
  │  Background thread                                  │
  │  Lex → Parse → Interpreter.Run()                    │
  │        │                                            │
  │        ▼  (at every statement)                      │
  │  DebugSession.OnBeforeExecute(line, env, callStack) │
  │        │                                            │
  │        ├─ stopOnEntry? → pause (send "stopped")     │
  │        ├─ breakpoint hit? → pause                   │
  │        ├─ stepping? → check depth → pause           │
  │        └─ _pauseGate.Wait()  ← blocks until resume  │
  └─────────────────────────────────────────────────────┘
        │
        ▼
SendInitialized event → editor sends "configurationDone"
        │
        ▼
StashConfigurationDoneHandler
  → DebugSession.ConfigurationDone() → signal _configurationDone
        │
        ▼
Interpreter thread unblocks, execution begins
```

---

## Supported Features

### Breakpoints

#### Line Breakpoints

Set a breakpoint on any line that contains a statement. The server normalizes file paths (resolves symlinks, normalizes separators) so paths from the editor match paths the interpreter reports.

#### Conditional Breakpoints

A condition is a Stash expression string evaluated in the scope at the breakpoint site. The breakpoint only pauses execution when the expression evaluates to a truthy value.

```js
// Only pause when i > 10
i > 10;

// Only pause when the variable matches
name == "admin";
```

Falsy values in Stash: `false`, `null`, `0`, `0.0`, `""`. See [truthiness rules](../Stash%20—%20Language%20Specification.md#4-type-system).

#### Hit Count Breakpoints

Pause based on how many times the breakpoint has been hit. Supported operators:

| Pattern    | Meaning                                                      |
| ---------- | ------------------------------------------------------------ |
| `N`        | Same as `== N`                                               |
| `== N`     | Exactly on the Nth hit                                       |
| `>= N`     | On the Nth hit and all subsequent hits                       |
| `> N`      | After the Nth hit (hit N+1 onward)                           |
| `<= N`     | On hits 1 through N                                          |
| `< N`      | On hits 1 through N-1                                        |
| `% M == R` | Every Mth hit, offset by R (e.g. `% 3 == 0` = every 3rd hit) |

If the pattern cannot be parsed, the breakpoint is treated as always-hit.

#### Logpoints

Instead of pausing, logpoints emit a message to the debug console. The message template may contain `{expression}` placeholders, which are evaluated in the current scope.

```
Loop iteration {i}: value = {arr[i]}
```

The output appears as a `console` category output event in the client.

---

### Stepping

Four stepping modes are supported:

| Command       | Behavior                                                                                     |
| ------------- | -------------------------------------------------------------------------------------------- |
| **Step In**   | Stops at the very next statement, following function calls into their bodies                 |
| **Step Over** | Steps to the next statement at the same or shallower call depth (skips over function bodies) |
| **Step Out**  | Runs until returning to a shallower call depth; at top-level, acts as Continue               |
| **Continue**  | Resumes execution with no stepping constraint until the next breakpoint or end               |

Stepping depth is tracked via the interpreter's call stack. `OnFunctionEnter` and `OnFunctionExit` increment and decrement the depth counter.

---

### Variable Inspection

When execution is paused, the client can inspect variables through a three-tier scope model.

#### Scope Types

| Scope       | Contents                                                      |
| ----------- | ------------------------------------------------------------- |
| **Local**   | Variables declared in the current function or innermost block |
| **Closure** | Variables in enclosing scopes between local and global        |
| **Global**  | Top-level variables and built-in functions                    |

Each scope is backed by a `VariableContainer` with an integer reference ID. The client requests variables by container ID. Expandable values (arrays, dicts, struct instances) get their own container IDs for nested expansion.

#### Value Display

| Stash Type       | DAP `type`   | DAP `value`                     |
| ---------------- | ------------ | ------------------------------- |
| `null`           | `"null"`     | `"null"`                        |
| `int` (long)     | `"int"`      | numeric string                  |
| `float` (double) | `"float"`    | numeric string                  |
| `bool`           | `"bool"`     | `"true"` / `"false"`            |
| `string`         | `"string"`   | quoted string (e.g. `"hello"`)  |
| `array`          | `"array"`    | `"array[N]"` — expandable       |
| `dict`           | `"dict"`     | `"dict[N]"` — expandable        |
| struct instance  | type name    | `"TypeName {...}"` — expandable |
| function         | `"function"` | function's `ToString()`         |
| lambda           | `"function"` | `"<lambda>"`                    |
| enum member      | `"enum"`     | `"Type.Member"`                 |

Arrays expand with numeric index names (`[0]`, `[1]`, …). Dicts expand with their key names. Struct instances expand with field names.

---

### Expression Evaluation

The `evaluate` request evaluates arbitrary Stash expressions in the current scope. This powers:

- **Watch expressions** — evaluated at each pause
- **Debug console REPL** — interactive evaluation while paused
- **Hover evaluation** (`evaluateForHovers` capability) — editor hovering over a variable shows its value

Evaluation errors are returned as a DAP error response with the error message, rather than crashing the session.

---

### Exception Handling

Two exception breakpoint filters are available:

| Filter ID  | Label               | Behavior                                                    |
| ---------- | ------------------- | ----------------------------------------------------------- |
| `all`      | All Exceptions      | Pause on every runtime error, including caught ones         |
| `uncaught` | Uncaught Exceptions | Pause on unhandled runtime errors (currently same as `all`) |

When an exception breakpoint fires, the interpreter thread pauses and the client receives a `stopped` event with reason `"exception"` and the error message as description.

If no exception filter is active, runtime errors print to stderr and terminate the session.

---

### Stop on Entry

When `stopOnEntry: true` is set in the launch configuration, the interpreter pauses at the very first statement before executing anything. The client receives a `stopped` event with reason `"entry"`.

This is useful for inspecting initial variable state or setting breakpoints dynamically after launch.

---

### Pause

The client can send a `pause` request at any time to interrupt a running script. The interpreter will pause at the next statement it executes after the pause request is received.

---

## Launch Configuration

The `launch` request accepts the following fields:

| Field         | Type       | Required | Default          | Description                                                                  |
| ------------- | ---------- | -------- | ---------------- | ---------------------------------------------------------------------------- |
| `program`     | `string`   | **yes**  | —                | Path to the `.stash` script to debug                                         |
| `stopOnEntry` | `boolean`  | no       | `false`          | If `true`, pause at the first statement before executing                     |
| `cwd`         | `string`   | no       | script directory | Working directory for the script process                                     |
| `args`        | `string[]` | no       | `[]`             | Command-line arguments passed to the script (accessible via `args` in Stash) |

Example `launch.json`:

```json
{
  "version": "0.2.0",
  "configurations": [
    {
      "type": "stash",
      "request": "launch",
      "name": "Debug Stash Script",
      "program": "${file}",
      "stopOnEntry": false,
      "cwd": "${workspaceFolder}",
      "args": []
    }
  ]
}
```

---

## VS Code Integration

The `stash-lang` VS Code extension includes built-in debugging support. The extension registers a `DebugAdapterDescriptorFactory` that launches the Stash DAP server as an executable process, communicating over stdio.

### Setup

1. **Publish the DAP server** so it's available as a standalone binary:

   ```bash
   dotnet publish Stash.Dap/ -c Release -o publish/
   ```

2. **Make the binary accessible** — either:
   - Add the publish directory to your system `PATH` and ensure the binary is named `stash-dap`, **or**
   - Set the `stash.dapPath` extension setting to the absolute path of the published binary

3. **Start debugging** — open a `.stash` file and press `F5`, or use the Run and Debug panel. The extension provides two built-in launch configuration snippets:
   - **Stash: Launch Script** — run the active file with the debugger
   - **Stash: Launch with Stop on Entry** — pause at the first statement

### Extension Settings

| Setting         | Type     | Default | Description                                                                                              |
| --------------- | -------- | ------- | -------------------------------------------------------------------------------------------------------- |
| `stash.dapPath` | `string` | `""`    | Absolute path to the Stash DAP binary. If empty, the extension looks for `stash-dap` on the system PATH. |
| `stash.lspPath` | `string` | `""`    | Absolute path to the Stash LSP server binary. If empty, looks for `stash-lsp` on the system PATH.        |

### How It Works

When a debug session starts, the extension's `StashDebugAdapterFactory`:

1. Reads the `stash.dapPath` setting (falls back to `"stash-dap"` on PATH)
2. Returns a `vscode.DebugAdapterExecutable` pointing to the DAP binary
3. VS Code manages the process lifecycle, piping stdin/stdout for DAP communication

No manual server management or port configuration is needed.

### Neovim / nvim-dap

For Neovim with [nvim-dap](https://github.com/mfussenegger/nvim-dap), add an adapter and configuration:

```lua
local dap = require('dap')

dap.adapters.stash = {
    type = 'executable',
    command = '/path/to/publish/StashDap',
}

dap.configurations.stash = {
    {
        type = 'stash',
        request = 'launch',
        name = 'Debug Stash Script',
        program = function()
            return vim.fn.input('Path to script: ', vim.fn.getcwd() .. '/', 'file')
        end,
        stopOnEntry = false,
        cwd = '${workspaceFolder}',
    }
}
```

---

## Building & Running

### Build

```bash
# Build only the DAP server
dotnet build Stash.Dap/

# Build the entire solution (recommended, validates all dependencies)
dotnet build
```

### Run

The DAP server communicates over **stdin/stdout**. It is not meant to be run directly in a terminal — it is launched by the editor when a debug session starts.

```bash
# Launched by the editor automatically; for manual testing:
dotnet run --project Stash.Dap/
```

### Publish

```bash
# Framework-dependent (requires .NET runtime on the target machine)
dotnet publish Stash.Dap/ -c Release -o publish/

# Self-contained single-file executable
dotnet publish Stash.Dap/ -c Release -o publish/ \
    --self-contained true \
    -p:PublishSingleFile=true \
    -r linux-x64
```

---

## Testing

113 tests across 3 files in `Stash.Tests/Dap/`:

### DebugSessionTests.cs (59 tests)

Unit tests for `DebugSession` in isolation:

- Breakpoint management (set, clear, normalize paths)
- Variable formatting for all Stash types
- Hit condition parsing and evaluation (`== N`, `>= N`, `% M == R`, …)
- Logpoint message interpolation (`{expr}` template substitution)
- Stepping state machine (step in, step over, step out, continue)
- Exception breakpoint filter enable/disable
- Launch argument validation
- Pause/resume gate behavior

### DapHandlerTests.cs (34 tests)

Handler-level tests that exercise the 16 request handlers:

- `Initialize` — capabilities response shape
- `Threads` — single thread response
- `SetBreakpoints` — breakpoint validation and confirmation
- `Continue`, `Next`, `StepIn`, `StepOut` — step mode transitions
- `Evaluate` — expression evaluation and error responses
- `Disconnect` — session teardown

### DapIntegrationTests.cs (20 tests)

End-to-end tests that run real Stash scripts and validate DAP events:

- Breakpoint hits on specific lines
- Conditional breakpoint evaluation
- Hit count breakpoints (all operators)
- Logpoint output
- Variable inspection (stack frames → scopes → variables)
- Array and dict expansion
- Nested call stack inspection
- Stepping: step in, step over, step out
- Stop on entry
- Pause mid-execution
- Disconnect during execution
- Script arguments (`args`) passed correctly

### Running Tests

```bash
# All DAP tests
dotnet test --filter "FullyQualifiedName~Dap"

# One test class
dotnet test --filter "FullyQualifiedName~DapIntegrationTests"

# One specific test
dotnet test --filter "FullyQualifiedName~DapIntegrationTests.BreakpointHit"

# All tests in the solution
dotnet test
```

---

## Design Decisions

### Single Interpreter Thread + Gate Synchronization

The interpreter runs on a dedicated background thread; the DAP I/O runs on a separate thread managed by OmniSharp. Rather than marshalling work items or using locks around every statement, a `ManualResetEventSlim` (`_pauseGate`) acts as a simple gate:

- **Running**: gate is _set_ — `_pauseGate.Wait()` returns immediately.
- **Paused**: gate is _reset_ — interpreter thread blocks inside `OnBeforeExecute()`.
- **Resume**: any handler (Continue, Next, StepIn, StepOut) calls `_pauseGate.Set()`.

This avoids complex lock choreography while keeping the interpreter's inner loop clean.

### Configuration Done Gate

A second `ManualResetEventSlim` (`_configurationDone`) prevents the interpreter from starting until the client has finished sending its initial configuration (breakpoints, exception filters, etc.). The sequence is:

1. `launch` → start interpreter thread → interpreter calls `OnBeforeExecute` immediately
2. `SendInitialized` event → client sends `setBreakpoints`, `setExceptionBreakpoints`, …
3. `configurationDone` → `_configurationDone.Set()` → interpreter unblocks

Without this gate, the interpreter could race past the first breakpoint before the client has set it.

### Variable Reference IDs

The DAP protocol uses integer "variable reference" IDs to navigate nested data structures. Each call to `GetScopes` or `GetVariables` can create new `VariableContainer` entries in a `ConcurrentDictionary<int, VariableContainer>`. IDs are assigned with `Interlocked.Increment` for thread safety. All variable reference state is cleared on every `Continue` / step (since the interpreter has moved on and old references are stale).

### Normalized Path Matching for Breakpoints

The editor and the interpreter may represent the same file with different path forms (relative vs. absolute, symlinks, mixed separators on Windows). Breakpoints are keyed by `Path.GetFullPath()` of the normalized path, and the interpreter reports statement locations using the same normalization. This ensures `source.path` from the client always matches the key in `_breakpoints`.

### Control Flow via Exceptions

Stash's `return`, `break`, and `continue` are implemented as C# exceptions (`ReturnException`, `BreakException`, `ContinueException`) that unwind the call stack. `DebugSession.OnFunctionExit` is hooked at `finally` blocks in the interpreter's function-call logic so the call depth counter stays accurate even when control flow unwinds non-linearly.

---

## Capabilities

The `Initialize` response declares the following capabilities:

| Capability                                       | Supported |
| ------------------------------------------------ | --------- |
| `supportsConfigurationDoneRequest`               | ✅        |
| `supportsEvaluateForHovers`                      | ✅        |
| `supportsConditionalBreakpoints`                 | ✅        |
| `supportsHitConditionalBreakpoints`              | ✅        |
| `supportsLogPoints`                              | ✅        |
| `exceptionBreakpointFilters` (`all`, `uncaught`) | ✅        |
| `supportsSetVariable`                            | ❌        |
| `supportsFunctionBreakpoints`                    | ❌        |
| `supportsExceptionInfoRequest`                   | ❌        |
| `supportsLoadedSourcesRequest`                   | ❌        |
| `supportsAttachRequest`                          | ❌        |

---

## Limitations & Future Work

### Current Limitations

| Limitation                  | Notes                                                                                                                                                                         |
| --------------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| **Launch only**             | `attach` mode is not supported; the DAP server always launches a new interpreter process                                                                                      |
| **Single thread**           | The Stash interpreter is single-threaded; DAP multi-thread concepts (pause all threads, per-thread stepping) do not apply                                                     |
| **No set-variable**         | Variables cannot be mutated from the debug UI at runtime                                                                                                                      |
| **No function breakpoints** | Cannot break on function entry by name; use a line breakpoint on the first line of the function instead                                                                       |
| **No loaded sources**       | The editor cannot enumerate all loaded source files via DAP                                                                                                                   |
| **`uncaught` = `all`**      | Currently both exception filters behave identically; distinguishing caught vs. uncaught exceptions requires restructuring the interpreter's error model                       |
| **No source maps**          | `import` statements load other `.stash` files; breakpoints in imported modules use the imported file's absolute path correctly, but there is no higher-level source-map layer |

### Potential Future Work

- **Set variable**: Allow the debug UI to modify variable values at a breakpoint — requires exposing mutation APIs through `IDebugger`.
- **Function breakpoints**: Parse `StashFunctionBreakpoint` names and inject breakpoints at function-definition sites.
- **Exception info request**: Return structured exception details (type, message, stack) when paused on an exception.
- **Distinguish caught vs. uncaught**: Track a try/catch depth counter in the interpreter to implement true `uncaught`-only filtering.
- **Data breakpoints**: Pause when a specific variable's value changes.
- **Inline values**: Report variable values as inline annotations in the editor (requires DAP `supportsInlineValues` capability).
