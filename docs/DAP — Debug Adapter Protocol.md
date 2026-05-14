# DAP - Debug Adapter Protocol

> **Status:** Stable protocol reference
> **Audience:** Editor integrators, debugger maintainers, and contributors changing debug behavior.
> **Purpose:** Defines the Stash Debug Adapter Protocol surface, launch configuration, debugger behavior, and known limits.
>
> **Companion documents:**
>
> - [Language Specification](Stash%20—%20Language%20Specification.md) - language syntax, runtime semantics, imports, values, and errors.
> - [Standard Library Reference](Stash%20—%20Standard%20Library%20Reference.md) - built-in namespace APIs available while debugging.
> - [LSP - Language Server Protocol](LSP%20—%20Language%20Server%20Protocol.md) - language intelligence protocol reference.
> - [TAP - Testing Infrastructure](TAP%20—%20Testing%20Infrastructure.md) - test execution and TAP output contract.

The Stash debug adapter exposes the standard [Debug Adapter Protocol](https://microsoft.github.io/debug-adapter-protocol/) over standard input and standard output. DAP clients use it to launch Stash programs, set breakpoints, control execution, inspect stack frames, evaluate expressions, and edit variables while execution is paused.

This document describes the public debugger contract. It intentionally avoids VM architecture, implementation history, and roadmap material except where a current limitation affects client behavior.

## 1. Roles

A Stash debug session has three participants:

| Participant | Responsibility                                                                             |
| ----------- | ------------------------------------------------------------------------------------------ |
| DAP client  | Editor or IDE that sends DAP requests and renders responses and events.                    |
| `stash-dap` | Stash debug adapter process. It translates DAP requests into Stash VM debug operations.    |
| Stash VM    | Executes the target program and calls debugger hooks at statement and function boundaries. |

The adapter is a launch adapter. The client starts `stash-dap`, sends `initialize`, configures the session, and then sends `launch`.

## 2. Transport

The adapter uses JSON DAP messages over stdio.

| Property            | Value                                                              |
| ------------------- | ------------------------------------------------------------------ |
| Transport           | Standard input and standard output                                 |
| Server process      | `stash-dap` or `dotnet run --project Stash.Dap` during development |
| Session model       | One debug adapter process per debug session                        |
| Port usage          | None                                                               |
| Manual terminal use | Not supported as a user interface                                  |

The adapter writes protocol traffic to stdout. Diagnostic logging must not corrupt stdout; any diagnostic output belongs on stderr or in the configured client log channel.

## 3. Lifecycle

A normal launch session follows this sequence:

1. The client starts the adapter process.
2. The client sends `initialize`.
3. The adapter responds with capabilities.
4. The client sends `launch`.
5. The adapter starts the target program on a background execution thread.
6. The adapter sends `initialized`.
7. The client sends initial breakpoint and exception configuration.
8. The client sends `configurationDone`.
9. The target program begins or resumes execution.
10. The adapter sends `stopped`, `output`, `loadedSource`, `exited`, and `terminated` events as execution proceeds.

The target program must not run past the initial configuration gate before `configurationDone` is received. This guarantees that breakpoints sent immediately after `initialized` can bind before the first statement executes.

## 4. Launch Configuration

The `launch` request accepts the following fields:

| Field         | Type         | Required | Default           | Meaning                                                            |
| ------------- | ------------ | -------- | ----------------- | ------------------------------------------------------------------ |
| `program`     | string       | yes      | none              | Path to the `.stash` script to execute.                            |
| `cwd`         | string       | no       | Program directory | Working directory used by the target program.                      |
| `args`        | string array | no       | `[]`              | Command-line arguments exposed to the Stash program as `args`.     |
| `stopOnEntry` | boolean      | no       | `false`           | If true, execution stops before the first statement.               |
| `testMode`    | boolean      | no       | `false`           | Internal test-runner mode used by the VS Code testing integration. |
| `testFilter`  | string       | no       | none              | Internal test-runner filter used with `testMode`.                  |

Example:

```json
{
  "type": "stash",
  "request": "launch",
  "name": "Debug Stash Script",
  "program": "${file}",
  "cwd": "${workspaceFolder}",
  "args": [],
  "stopOnEntry": false
}
```

The adapter supports `launch` only. It does not support attaching to an already-running Stash process.

## 5. Capabilities

The `initialize` response declares the following capabilities:

| Capability                          | Supported |
| ----------------------------------- | --------- |
| `supportsConfigurationDoneRequest`  | Yes       |
| `supportsConditionalBreakpoints`    | Yes       |
| `supportsHitConditionalBreakpoints` | Yes       |
| `supportsLogPoints`                 | Yes       |
| `supportsFunctionBreakpoints`       | Yes       |
| `supportsEvaluateForHovers`         | Yes       |
| `supportsSetVariable`               | Yes       |
| `supportsLoadedSourcesRequest`      | Yes       |
| `exceptionBreakpointFilters`        | Yes       |
| `supportsExceptionInfoRequest`      | No        |
| Attach request support              | No        |

The exception breakpoint filters are:

| Filter     | Label               | Default | Current behavior                     |
| ---------- | ------------------- | ------- | ------------------------------------ |
| `all`      | All Exceptions      | false   | Stops on runtime errors.             |
| `uncaught` | Uncaught Exceptions | true    | Currently behaves the same as `all`. |

Clients must not assume support for `exceptionInfo` or attach workflows.

## 6. Breakpoints

### 6.1 Line Breakpoints

Line breakpoints are set with `setBreakpoints`. A line breakpoint applies to a source line in a normalized absolute file path.

The adapter normalizes source paths before storing or matching breakpoints. This allows clients to use relative paths, absolute paths, and platform-specific separators while the VM reports canonical source locations.

A breakpoint on a line with no executable statement may be returned as verified by the adapter, but it only stops when the VM reaches an executable statement whose source span matches the requested line.

### 6.2 Conditional Breakpoints

A conditional breakpoint includes a Stash expression in `condition`. The expression is evaluated in the current paused or executing scope when the breakpoint location is reached.

The breakpoint stops only if the condition evaluates to a truthy value. The Stash truthiness rules are defined by the [Language Specification](Stash%20—%20Language%20Specification.md).

Examples:

```stash
i > 10
name == "admin"
user.enabled && retries >= 3
```

If condition evaluation fails, the breakpoint is not considered hit and the adapter may emit diagnostic output for the failed condition.

### 6.3 Hit Conditions

A hit condition stops based on the number of times that breakpoint has been reached.

| Pattern    | Meaning                         |
| ---------- | ------------------------------- |
| `N`        | Stop exactly on hit `N`.        |
| `== N`     | Stop exactly on hit `N`.        |
| `>= N`     | Stop on hit `N` and later hits. |
| `> N`      | Stop after hit `N`.             |
| `<= N`     | Stop on hits `1` through `N`.   |
| `< N`      | Stop before hit `N`.            |
| `% M == R` | Stop when `hitCount % M == R`.  |

If a hit condition cannot be parsed, the adapter treats the breakpoint as if no hit condition were present.

### 6.4 Logpoints

A logpoint emits output instead of stopping execution. Logpoint messages may contain `{expression}` placeholders. Each placeholder is evaluated as a Stash expression in the current scope.

Example:

```text
Loop iteration {i}: value = {items[i]}
```

Logpoint output is sent as a DAP `output` event with console-style categorization.

### 6.5 Function Breakpoints

Function breakpoints are set with `setFunctionBreakpoints`. The breakpoint name must match the function declaration name exactly.

Function breakpoints support conditions and hit conditions with the same semantics as line breakpoints. When a function breakpoint stops execution, the adapter sends a `stopped` event with reason `function breakpoint`.

## 7. Execution Control

The adapter supports the standard execution-control requests:

| Request    | Behavior                                                                                           |
| ---------- | -------------------------------------------------------------------------------------------------- |
| `continue` | Resumes the selected thread until the next stop condition or program termination.                  |
| `next`     | Steps over calls and stops at the next statement at the same or shallower call depth.              |
| `stepIn`   | Stops at the next statement, including statements inside called functions.                         |
| `stepOut`  | Runs until the current function returns to its caller. At top level, this behaves like `continue`. |
| `pause`    | Requests that the selected thread stop at the next statement boundary.                             |

Stepping is statement-based. The adapter does not promise instruction-level stepping or expression-level stepping.

`stopOnEntry: true` stops before the first statement executes and sends a `stopped` event with reason `entry`.

## 8. Threads

The main script runs as DAP thread `1`. Each Stash `task.run()` child execution is registered as a separate DAP thread while the task is active.

The `threads` request returns the currently active debug threads. Breakpoints, stepping state, paused locations, and pause gates are maintained per thread.

Parallel array operations such as `arr.parMap`, `arr.parFilter`, and `arr.parForEach` are not registered as DAP threads. Breakpoints inside callbacks executed by these operations are not guaranteed to stop under the debugger.

## 9. Stack Frames

When execution is stopped, `stackTrace` returns the call stack for the selected thread. Stack frames include source path, line, column, and frame identity suitable for `scopes` and `evaluate`.

Frame identifiers are valid only while the associated pause state remains current. Continuing or stepping invalidates frame-specific state and variable references from the previous stop.

## 10. Scopes and Variables

The `scopes` request exposes variables through three scope categories:

| Scope   | Contents                                                        |
| ------- | --------------------------------------------------------------- |
| Local   | Variables declared in the current function or innermost block.  |
| Closure | Captured or enclosing variables between local and global scope. |
| Global  | Top-level variables and built-in functions.                     |

The `variables` request expands a scope or expandable value by `variablesReference`.

| Stash value     | DAP type    | Display form                |
| --------------- | ----------- | --------------------------- |
| `null`          | `null`      | `null`                      |
| integer         | `int`       | Decimal integer text        |
| float           | `float`     | Decimal floating-point text |
| boolean         | `bool`      | `true` or `false`           |
| string          | `string`    | Quoted string               |
| array           | `array`     | `array[N]`                  |
| dictionary      | `dict`      | `dict[N]`                   |
| struct instance | Struct name | `TypeName {...}`            |
| function        | `function`  | Function display text       |
| lambda          | `function`  | `<lambda>`                  |
| enum member     | `enum`      | `Type.Member`               |

Arrays expand with numeric element names such as `[0]`. Dictionaries expand with key names. Struct instances expand with field names.

Variable reference identifiers are transient. Clients must discard old references after `continue`, `next`, `stepIn`, or `stepOut`.

## 11. Evaluation

The `evaluate` request evaluates a Stash expression in the selected stack frame or current paused scope. It is used for watch expressions, debug console evaluation, and hover evaluation.

Examples:

```stash
user.name
items.len()
total + tax
```

Evaluation must not resume the program. If parsing or evaluation fails, the adapter returns an error response or an empty result with error information, depending on the request path used by the DAP library.

Evaluated values may be expandable and may receive their own `variablesReference`.

## 12. Variable Mutation

The `setVariable` request changes a variable or nested value while execution is paused. The new value is parsed and evaluated as a Stash expression, not as a raw string.

Examples:

| Input value | Assigned value                                    |
| ----------- | ------------------------------------------------- |
| `42`        | Integer `42`                                      |
| `"ready"`   | String `ready`                                    |
| `x + 1`     | Result of evaluating `x + 1` in the current scope |
| `[1, 2, 3]` | New array value                                   |

Supported mutation targets:

| Container         | Mutation behavior                           |
| ----------------- | ------------------------------------------- |
| Environment scope | Assigns the named variable in that scope.   |
| Array             | Assigns the element at the requested index. |
| Dictionary        | Assigns the value for the requested key.    |
| Struct instance   | Assigns the named field.                    |

The adapter rejects mutation of constants, namespace members, undefined struct fields, invalid references, and values whose assignment expression fails to parse or evaluate.

## 13. Exceptions

Runtime errors may stop the debugger when an exception breakpoint filter is active. The adapter sends a `stopped` event with reason `exception` and includes the error message where the DAP client supports it.

If no exception breakpoint filter applies, a runtime error is reported through normal program output and the session terminates.

The adapter currently does not distinguish caught from uncaught exceptions for filter behavior. Both `all` and `uncaught` use the same stop behavior.

## 14. Loaded Sources

The `loadedSources` request returns all source files loaded by the current session:

| Source kind               | Included |
| ------------------------- | -------- |
| Main script               | Yes      |
| Imported `.stash` modules | Yes      |
| Generated source maps     | No       |

When an import loads a new file, the adapter sends a `loadedSource` event with reason `new`. Returned source paths are normalized absolute paths, and source names are file names.

## 15. Output

The adapter forwards relevant program and debugger output as DAP `output` events. Clients should treat output categories according to DAP conventions and must not infer language semantics from output formatting.

Logpoint output is debugger output. Program output is target output.

## 16. Editor Integration

### 16.1 VS Code

The VS Code extension registers the `stash` debug type and starts the adapter as an executable process.

Relevant settings:

| Setting                 | Default | Meaning                                                                                      |
| ----------------------- | ------- | -------------------------------------------------------------------------------------------- |
| `stash.dapPath`         | empty   | Absolute path to the DAP binary. If empty, the extension searches for `stash-dap` on `PATH`. |
| `stash.lspPath`         | empty   | Absolute path to the LSP binary. If empty, the extension searches for `stash-lsp` on `PATH`. |
| `stash.interpreterPath` | `stash` | Stash interpreter path used by non-debug run and test tasks.                                 |

The extension can generate a launch configuration for the active `.stash` file.

### 16.2 nvim-dap

Example adapter configuration:

```lua
local dap = require("dap")

dap.adapters.stash = {
  type = "executable",
  command = "/path/to/stash-dap",
}

dap.configurations.stash = {
  {
    type = "stash",
    request = "launch",
    name = "Debug Stash Script",
    program = function()
      return vim.fn.input("Path to script: ", vim.fn.getcwd() .. "/", "file")
    end,
    cwd = "${workspaceFolder}",
    stopOnEntry = false,
    args = {},
  },
}
```

## 17. Build and Test

Build the adapter:

```bash
dotnet build Stash.Dap/
```

Publish a standalone adapter:

```bash
dotnet publish Stash.Dap/ -c Release -r linux-x64 --self-contained
```

Run DAP tests:

```bash
dotnet test --filter "FullyQualifiedName~Dap"
```

Protocol behavior should be validated with handler tests and integration tests whenever launch, breakpoint, stepping, variable, expression, or source-loading behavior changes.

## 18. Limitations

| Limitation               | Contract                                                                              |
| ------------------------ | ------------------------------------------------------------------------------------- |
| Attach                   | Not supported. Scripts must be launched under the debugger.                           |
| Parallel array callbacks | Not attached as debug threads. Breakpoints inside these callbacks are not guaranteed. |
| Exception filters        | `all` and `uncaught` currently behave the same.                                       |
| Source maps              | Not supported. Breakpoints bind directly to loaded `.stash` files.                    |
| Variable references      | Valid only for the current pause state.                                               |

Attach is not supported because the VM chooses debug dispatch when execution starts, and the distributed CLI/runtime model does not host an attachable in-process DAP server. The supported workflow is to launch the target under the debugger from the beginning.

## 19. Change Rules

Changes to the DAP adapter should preserve these rules:

- New request support must be reflected in the capability response where the DAP protocol requires it.
- New launch fields must be documented in [Launch Configuration](#4-launch-configuration).
- Changes to breakpoint, stepping, scope, evaluation, or mutation semantics must update this document and the corresponding tests.
- Client-specific behavior belongs in editor integration docs or extension code unless it changes the DAP contract.
- Implementation details belong in source comments or engineering notes, not in this protocol reference.
