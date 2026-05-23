# AI Debug Agent Tools — VS Code Extension Spec

> **Status:** Backlog
> **Created:** 2026-04-15
> **Purpose:** Design a VS Code extension that exposes debugging capabilities as Language Model Tools, enabling AI agents to autonomously start debug sessions, set breakpoints, step through code, inspect variables, and evaluate expressions.

---

## Table of Contents

1. [Motivation](#1-motivation)
2. [Design Philosophy](#2-design-philosophy)
3. [What This Is NOT](#3-what-this-is-not)
4. [Architecture Overview](#4-architecture-overview)
5. [Tool Inventory](#5-tool-inventory)
6. [Tool Specifications](#6-tool-specifications)
7. [Statefulness Model](#7-statefulness-model)
8. [Debug Snapshot Format](#8-debug-snapshot-format)
9. [Interaction with Existing Stash Extension](#9-interaction-with-existing-stash-extension)
10. [Cross-Platform Considerations](#10-cross-platform-considerations)
11. [Security & Safety](#11-security--safety)
12. [Error Handling](#12-error-handling)
13. [Implementation Roadmap](#13-implementation-roadmap)
14. [Test Scenarios](#14-test-scenarios)
15. [Design Decisions & Alternatives](#15-design-decisions--alternatives)
16. [Open Questions](#16-open-questions)

---

## 1. Motivation

AI coding agents today can read files, edit code, run terminals, execute tests, and even interact with web browsers. Debugging is a conspicuous gap. The agents currently work around this by running tests and reading error output — but that workaround fails in several real-world scenarios:

1. **One-shot scripts without tests.** Sysadmin scripts, deployment automation, data migration jobs — written once, run once. Nobody writes a test suite for a `deploy.stash` they'll use three times.

2. **Programs that are hard to test.** GUI applications, hardware interactions, programs with complex environmental dependencies, multi-process orchestration. These exist and always will.

3. **Runtime verification.** Even well-tested programs must eventually be _run_. Tests verify contracts; debugging verifies reality. A test can pass while the program behaves incorrectly in production — different config, different data, different timing.

4. **Regression diagnosis.** "It worked yesterday." The fastest path to understanding _what changed_ at runtime is often: set a breakpoint, inspect state, compare with expectations. An agent that can do this autonomously saves significant human time.

The VS Code API already has all the building blocks:

- `vscode.debug` namespace: `startDebugging()`, `addBreakpoints()`, `removeBreakpoints()`, `stopDebugging()`, `activeDebugSession.customRequest()` for DAP commands
- `vscode.lm.registerTool()`: Finalized API for registering `LanguageModelTool<T>` instances that AI agents can invoke
- Stash DAP: Full-featured debug adapter supporting breakpoints (source, conditional, hit-conditional, logpoints, function), stepping (in/over/out), stack traces, scopes, variable inspection, expression evaluation, exception breakpoints

These have never been wired together. This extension bridges that gap.

---

## 2. Design Philosophy

### 2.1 Snapshot-Observe-Act Loop

The core interaction model mirrors how VS Code's browser tools work for AI agents:

```
Agent reasons about code
  → Agent calls debug tool (act)
    → Tool returns snapshot of debug state (observe)
      → Agent reasons about snapshot
        → Agent calls next debug tool (act)
          → ...
```

Each tool invocation is **self-contained**: it performs an action and returns the resulting state. The agent never needs to "poll" or "wait" — the tool blocks until the debugger reaches a stable state (paused, terminated, or timed out).

### 2.2 Coarse Over Fine

Agents think in high-level steps, not individual DAP requests. A single tool call like "continue and return snapshot when paused" is more useful than separate "send continue", "wait for stopped event", "get stack trace", "get scopes", "get variables" calls. Fewer round-trips = less token waste = faster debugging.

### 2.3 Hypothesis-Driven Debugging

The expected workflow is NOT step-step-step-step. It's:

1. Agent reads code and error output
2. Agent forms hypothesis: "I think `config` is null at line 47"
3. Agent sets conditional breakpoint at line 47
4. Agent runs program → breakpoint hits (or doesn't)
5. Agent inspects variables to confirm/refute hypothesis
6. Agent fixes code or forms new hypothesis

This means **conditional breakpoints** and **expression evaluation** are first-class, not afterthoughts.

### 2.4 Language-Agnostic

While this spec is motivated by Stash, the extension must work with **any** debug adapter registered in VS Code. The tools operate through the VS Code `debug` API and DAP's `customRequest`, not through Stash-specific interfaces. A user debugging Python, Node.js, C++, or Rust should get the same tools.

---

## 3. What This Is NOT

- **Not a replacement for test-driven debugging.** Running tests + reading errors is still the 80% path. This covers the other 20%.
- **Not a step-by-step debugger UI.** The agent won't literally "click" Step Over 500 times. It uses strategic breakpoints and evaluation.
- **Not Stash-specific.** It works with any DAP-compliant debugger, though Stash's DAP is the primary validation target.
- **Not a standalone extension.** It registers Language Model Tools that are consumed by Copilot (or any LM-based agent). It has no standalone UI.

---

## 4. Architecture Overview

```
┌─────────────────────────────────────────────┐
│  AI Agent (Copilot / Custom Chat Mode)      │
│  - Reasons about code                       │
│  - Invokes tools via lm.tools               │
└─────────────┬───────────────────────────────┘
              │ LanguageModelTool invocations
              ▼
┌─────────────────────────────────────────────┐
│  debug-agent-tools extension                │
│                                             │
│  ┌─────────────────────────────────────┐    │
│  │  Tool Registry                      │    │
│  │  - 8 registered tools               │    │
│  │  - Input validation                 │    │
│  │  - Confirmation messages (safety)   │    │
│  └──────────────┬──────────────────────┘    │
│                 │                           │
│  ┌──────────────▼──────────────────────┐    │
│  │  Debug Session Manager              │    │
│  │  - Session lifecycle tracking       │    │
│  │  - Event subscription management    │    │
│  │  - Snapshot assembly                │    │
│  │  - Timeout handling                 │    │
│  └──────────────┬──────────────────────┘    │
│                 │                           │
│  ┌──────────────▼──────────────────────┐    │
│  │  VS Code Debug API Bridge           │    │
│  │  - debug.startDebugging()           │    │
│  │  - debug.addBreakpoints()           │    │
│  │  - session.customRequest() → DAP    │    │
│  │  - debug.stopDebugging()            │    │
│  └─────────────────────────────────────┘    │
└─────────────────────────────────────────────┘
              │
              ▼
┌─────────────────────────────────────────────┐
│  Debug Adapter (any DAP server)             │
│  e.g. stash-dap, debugpy, node-debug, ...   │
└─────────────────────────────────────────────┘
```

### 4.1 Extension Identity

| Property          | Value                                                                                |
| ----------------- | ------------------------------------------------------------------------------------ |
| Extension ID      | `debug-agent-tools`                                                                  |
| Display Name      | Debug Agent Tools                                                                    |
| Activation        | `onStartupFinished` (lazy — tools register immediately but do nothing until invoked) |
| Dependencies      | None (uses only stable VS Code API)                                                  |
| AOT Compatibility | N/A (TypeScript extension, not .NET)                                                 |

### 4.2 Key Internal Components

**DebugSessionManager** — Singleton that tracks active debug sessions started by agent tools. Manages the lifecycle: start → paused → stepping → terminated. Subscribes to `debug.onDidStartDebugSession`, `debug.onDidTerminateDebugSession`, and listens for DAP `stopped` events via `debug.onDidReceiveDebugSessionCustomEvent` or `DebugAdapterTracker`.

**SnapshotBuilder** — Assembles a `DebugSnapshot` from multiple DAP requests (`stackTrace`, `scopes`, `variables`, `threads`). Applies depth limits and size budgets to prevent token explosion.

**TimeoutController** — Wraps blocking operations (continue, step) with configurable timeouts. Returns partial state if the program doesn't pause within the timeout window.

---

## 5. Tool Inventory

Eight tools, organized by purpose:

| #   | Tool Name                 | Purpose                                            | Stateful?              |
| --- | ------------------------- | -------------------------------------------------- | ---------------------- |
| 1   | `debug_startSession`      | Launch a debug session                             | Yes — creates session  |
| 2   | `debug_setBreakpoints`    | Add/replace breakpoints on a file                  | Idempotent             |
| 3   | `debug_removeBreakpoints` | Remove breakpoints from a file                     | Idempotent             |
| 4   | `debug_continue`          | Resume execution, return snapshot when paused      | Yes — blocking         |
| 5   | `debug_step`              | Step over/in/out, return snapshot                  | Yes — blocking         |
| 6   | `debug_getSnapshot`       | Get current debug state without changing execution | Read-only              |
| 7   | `debug_evaluate`          | Evaluate expression in current frame context       | Read-only\*            |
| 8   | `debug_stopSession`       | Terminate the debug session                        | Yes — destroys session |

\* Expression evaluation is technically read-only from the debugger's perspective, but the evaluated expression _could_ have side effects. This is addressed in [Security & Safety](#11-security--safety).

### 5.1 Why Not More Tools?

**Rejected: `debug_setExceptionBreakpoints`** — Folded into `debug_startSession` options. Exception breakpoint config is session-level, not something you toggle mid-session in typical agent workflows.

**Rejected: `debug_getThreads`** — Thread information is included in the snapshot. No need for a separate tool.

**Rejected: `debug_setVariable`** — Mutating runtime state is a significant safety concern. An agent that can silently change variable values during debugging could mask bugs rather than find them. If needed in the future, it should require explicit user confirmation per mutation.

**Rejected: `debug_pause`** — Pausing a running program at an arbitrary point is rarely useful for hypothesis-driven debugging. If the agent wants to stop at a specific point, it should use breakpoints.

---

## 6. Tool Specifications

### 6.1 `debug_startSession`

Launches a new debug session using the VS Code debug API.

**Input Schema:**

```json
{
  "type": "object",
  "properties": {
    "program": {
      "type": "string",
      "description": "Absolute path to the file to debug"
    },
    "debugType": {
      "type": "string",
      "description": "Debug adapter type (e.g., 'stash', 'python', 'node'). If omitted, inferred from file extension."
    },
    "args": {
      "type": "array",
      "items": { "type": "string" },
      "description": "Command-line arguments to pass to the program"
    },
    "cwd": {
      "type": "string",
      "description": "Working directory. Defaults to workspace root."
    },
    "env": {
      "type": "object",
      "additionalProperties": { "type": "string" },
      "description": "Additional environment variables"
    },
    "stopOnEntry": {
      "type": "boolean",
      "description": "Pause on the first line of the program. Default: false."
    },
    "exceptionBreakpoints": {
      "type": "string",
      "enum": ["none", "uncaught", "all"],
      "description": "Exception breakpoint filter. Default: 'uncaught'."
    },
    "noDebug": {
      "type": "boolean",
      "description": "Run without debugging (no breakpoints, no stepping). Default: false."
    }
  },
  "required": ["program"]
}
```

**Behavior:**

1. If a debug session started by this extension is already active, stop it first (one session at a time — see [Design Decisions](#15-design-decisions--alternatives)).
2. Resolve `debugType` from file extension if not provided. Use a mapping: `.stash` → `stash`, `.py` → `python`, `.js`/`.ts` → `node`, etc. If unmappable, return error.
3. Build a `DebugConfiguration` object with `type`, `request: "launch"`, `name: "Agent Debug"`, `program`, `args`, `cwd`, `env`, `stopOnEntry`, `noDebug`.
4. Call `debug.startDebugging(workspaceFolder, config)`.
5. If `stopOnEntry` is true, wait for the `stopped` event and return a snapshot.
6. If `stopOnEntry` is false, return a confirmation: `{ status: "running", sessionId: "..." }`.

**Output:** `DebugSnapshot` if stopped on entry, or `{ status: "running", sessionId }` if running.

**Confirmation Message:** "Start a debug session for `{program}`?" — Always shown to user.

**Timeout:** 30 seconds for session startup. If the program doesn't start within 30s, return error.

---

### 6.2 `debug_setBreakpoints`

Sets breakpoints in a source file. Uses **replace semantics** — all existing agent-managed breakpoints in the specified file are replaced with the new set.

**Input Schema:**

```json
{
  "type": "object",
  "properties": {
    "file": {
      "type": "string",
      "description": "Absolute path to the source file"
    },
    "breakpoints": {
      "type": "array",
      "items": {
        "type": "object",
        "properties": {
          "line": { "type": "integer", "description": "1-based line number" },
          "condition": {
            "type": "string",
            "description": "Conditional expression (e.g., 'x > 5')"
          },
          "hitCondition": {
            "type": "string",
            "description": "Hit count condition (e.g., '== 3', '>= 10')"
          },
          "logMessage": {
            "type": "string",
            "description": "Log message template (logpoint). Use {expr} for interpolation."
          }
        },
        "required": ["line"]
      },
      "description": "Breakpoints to set. Empty array clears all breakpoints in the file."
    }
  },
  "required": ["file", "breakpoints"]
}
```

**Behavior:**

1. Remove all previously agent-set `SourceBreakpoint`s for `file`.
2. Create new `SourceBreakpoint` objects with the specified line, condition, hitCondition, logMessage.
3. Call `debug.addBreakpoints(newBreakpoints)`.
4. Return the list of verified breakpoints (DAP may adjust line numbers).

**Output:**

```json
{
  "file": "/path/to/file.stash",
  "breakpoints": [
    { "line": 47, "verified": true, "condition": "config == null" },
    { "line": 82, "verified": true }
  ]
}
```

**Confirmation:** None required — setting breakpoints is non-destructive and reversible.

**Note:** This tool works both before and during a debug session. Breakpoints set before a session starts will be active when the session launches.

---

### 6.3 `debug_removeBreakpoints`

Removes agent-managed breakpoints from a file.

**Input Schema:**

```json
{
  "type": "object",
  "properties": {
    "file": {
      "type": "string",
      "description": "Absolute path to the source file. If omitted, removes ALL agent-managed breakpoints."
    }
  }
}
```

**Behavior:**

1. If `file` is provided, remove only agent-managed breakpoints for that file.
2. If `file` is omitted, remove all agent-managed breakpoints across all files.
3. Call `debug.removeBreakpoints(toRemove)`.
4. Never remove user-set breakpoints (breakpoints not created by this extension).

**Output:** `{ "removed": 3 }` — count of breakpoints removed.

---

### 6.4 `debug_continue`

Resume program execution. Blocks until the program pauses again (breakpoint hit, exception, or program exits), or until timeout.

**Input Schema:**

```json
{
  "type": "object",
  "properties": {
    "threadId": {
      "type": "integer",
      "description": "Thread to continue. If omitted, continues the currently stopped thread."
    },
    "timeout": {
      "type": "integer",
      "description": "Maximum milliseconds to wait for the program to pause. Default: 10000 (10s)."
    }
  }
}
```

**Behavior:**

1. Validate that a debug session is active and paused.
2. Send `continue` request via `session.customRequest('continue', { threadId })`.
3. Wait for `stopped` event or `terminated` event, up to `timeout` ms.
4. If `stopped`: assemble and return a full `DebugSnapshot`.
5. If `terminated`: return `{ status: "terminated", exitCode, output }`.
6. If timeout: return `{ status: "running", message: "Program did not pause within {timeout}ms. It may be in a long-running operation or waiting for input. Use debug_stopSession to terminate, or debug_continue with a longer timeout." }`.

**Output:** `DebugSnapshot | TerminatedResult | TimeoutResult`

---

### 6.5 `debug_step`

Execute a single step operation and return the resulting state.

**Input Schema:**

```json
{
  "type": "object",
  "properties": {
    "action": {
      "type": "string",
      "enum": ["over", "in", "out"],
      "description": "Step type: 'over' (next line), 'in' (enter function), 'out' (exit function)"
    },
    "threadId": {
      "type": "integer",
      "description": "Thread to step. If omitted, steps the currently stopped thread."
    },
    "count": {
      "type": "integer",
      "minimum": 1,
      "maximum": 20,
      "description": "Number of steps to perform. Default: 1. Max: 20."
    }
  },
  "required": ["action"]
}
```

**Behavior:**

1. Validate active session, paused state.
2. For `count` iterations:
   a. Send `stepOver`, `stepIn`, or `stepOut` via `customRequest`.
   b. Wait for `stopped` event (5s timeout per step).
   c. If program terminates mid-step, break and return terminated result.
3. After final step, assemble and return `DebugSnapshot`.

**Output:** `DebugSnapshot | TerminatedResult`

**Design Note:** `count` is capped at 20 to prevent agents from using step as a substitute for breakpoints. If an agent wants to skip 100 lines, it should set a breakpoint and continue.

---

### 6.6 `debug_getSnapshot`

Read the current debug state without modifying execution. Useful when the agent needs to re-examine state after prior reasoning.

**Input Schema:**

```json
{
  "type": "object",
  "properties": {
    "variableDepth": {
      "type": "integer",
      "minimum": 0,
      "maximum": 3,
      "description": "How many levels deep to expand nested variables (arrays, dicts, structs). Default: 1."
    },
    "stackDepth": {
      "type": "integer",
      "minimum": 1,
      "maximum": 20,
      "description": "Maximum stack frames to include. Default: 5."
    },
    "includeGlobals": {
      "type": "boolean",
      "description": "Include global scope variables. Default: false (locals + closures only)."
    }
  }
}
```

**Behavior:**

1. Validate active session, paused state.
2. Assemble snapshot with the requested depth/scope parameters.
3. Return `DebugSnapshot`.

**Output:** `DebugSnapshot`

---

### 6.7 `debug_evaluate`

Evaluate an expression in the context of the current stack frame.

**Input Schema:**

```json
{
  "type": "object",
  "properties": {
    "expression": {
      "type": "string",
      "description": "Expression to evaluate in the current frame context"
    },
    "frameIndex": {
      "type": "integer",
      "description": "Stack frame index (0 = top/current). Default: 0."
    },
    "context": {
      "type": "string",
      "enum": ["watch", "repl", "hover"],
      "description": "Evaluation context. 'watch' for side-effect-free, 'repl' for interactive. Default: 'watch'."
    }
  },
  "required": ["expression"]
}
```

**Behavior:**

1. Validate active session, paused state.
2. Resolve the `frameId` from the requested `frameIndex` via stack trace.
3. Send `evaluate` request via `customRequest('evaluate', { expression, frameId, context })`.
4. Return the result string and type.

**Output:**

```json
{
  "expression": "config.port",
  "result": "8080",
  "type": "int",
  "variablesReference": 0
}
```

If the expression references a structured value (array, dict, struct), include the expanded contents up to 1 level deep.

**Note on side effects:** The `context: "watch"` hint tells the debug adapter that the evaluation should be side-effect-free. Not all adapters respect this. The Stash DAP evaluates in an isolated temp VM, which is safe. Other adapters may execute arbitrary code — this is documented as a known limitation.

---

### 6.8 `debug_stopSession`

Terminate the active debug session.

**Input Schema:**

```json
{
  "type": "object",
  "properties": {
    "captureOutput": {
      "type": "boolean",
      "description": "Return stdout/stderr output captured during the session. Default: true."
    }
  }
}
```

**Behavior:**

1. If no active agent-managed session, return `{ status: "no_session" }`.
2. Call `debug.stopDebugging(session)`.
3. Wait for `onDidTerminateDebugSession` event (5s timeout).
4. Clean up: remove all agent-managed breakpoints, dispose event listeners.
5. Return session summary.

**Output:**

```json
{
  "status": "terminated",
  "duration": "4.2s",
  "output": "Server started on port 8080\nProcessing request...\n",
  "breakpointsHit": 3
}
```

**Confirmation:** None required — stopping a debug session is a normal operation.

---

## 7. Statefulness Model

### 7.1 The Problem

Debugging is inherently stateful: the program is at a specific point in execution, with specific variable values, and the agent's next action depends on that state. This contrasts with most agent tools which are stateless (read a file, search text).

### 7.2 The Solution: Same Pattern as Browser and Terminal

VS Code already solved this problem for two other stateful domains:

| Domain       | State Carrier   | Act                                   | Observe                        | Wait Pattern                          |
| ------------ | --------------- | ------------------------------------- | ------------------------------ | ------------------------------------- |
| **Browser**  | Web page DOM    | `click_element`, `type_in_page`       | `read_page`, `screenshot_page` | Instant — page updates immediately    |
| **Terminal** | Running process | `run_in_terminal`, `send_to_terminal` | `get_terminal_output`          | Sync with timeout, async notification |
| **Debugger** | Paused program  | `debug_continue`, `debug_step`        | `debug_getSnapshot`            | **Block until paused, with timeout**  |

The debugger follows the terminal pattern: **blocking with timeout + async notification**.

### 7.3 Session Lifecycle State Machine

```
           startSession
               │
               ▼
         ┌──────────┐    stopOnEntry=true     ┌────────┐
         │ STARTING ├────────────────────────►│ PAUSED │
         └────┬─────┘                         └───┬────┘
              │ stopOnEntry=false                 │
              ▼                                   │ continue / step
         ┌──────────┐                             ▼
         │ RUNNING  │◄────────────────────── ┌─────────┐
         └────┬─────┘  breakpoint / exception│ RUNNING │
              │                              └────┬────┘
              │ program exits                     │ breakpoint hit
              ▼                                   ▼
         ┌──────────────┐                    ┌────────┐
         │ TERMINATED   │◄───────────────────│ PAUSED │
         └──────────────┘    program exits   └────────┘
```

### 7.4 One Session at a Time

The extension enforces a single active debug session (from the agent's perspective). If the agent calls `debug_startSession` while a session is active, the old session is terminated first.

**Rationale:** Multi-session debugging is complex even for humans. For an AI agent working through sequential tool calls, managing multiple concurrent sessions would be error-prone and wasteful. The single-session constraint dramatically simplifies state management and reduces the chance of the agent getting confused about which session it's interacting with.

**User-initiated sessions:** The agent's session is tracked separately. If the user starts their own debug session manually, the agent tools will not interfere with it. The agent tools only operate on sessions they started.

---

## 8. Debug Snapshot Format

The `DebugSnapshot` is the primary return type. It must be **compact** (token-efficient) while providing enough context for the agent to reason about program state.

### 8.1 Schema

```json
{
  "status": "paused",
  "reason": "breakpoint",
  "file": "deploy.stash",
  "line": 47,
  "column": 1,
  "sourceContext": {
    "before": [
      "45: let config = loadConfig(path);",
      "46: if (config == null) {"
    ],
    "current": "47:     panic(\"Config not found: \" + path);",
    "after": ["48: }", "49: "]
  },
  "callStack": [
    { "name": "deploy", "file": "deploy.stash", "line": 47 },
    { "name": "main", "file": "deploy.stash", "line": 112 }
  ],
  "locals": {
    "path": { "value": "\"/etc/app/config.ini\"", "type": "string" },
    "config": { "value": "null", "type": "null" },
    "retries": { "value": "3", "type": "int" }
  },
  "closures": {},
  "output": "Starting deployment...\nLoading config from /etc/app/config.ini\n"
}
```

### 8.2 Field Descriptions

| Field           | Type                                    | Description                                                                           |
| --------------- | --------------------------------------- | ------------------------------------------------------------------------------------- |
| `status`        | `"paused" \| "running" \| "terminated"` | Current session state                                                                 |
| `reason`        | `string`                                | Why the program paused: `"breakpoint"`, `"step"`, `"exception"`, `"entry"`, `"pause"` |
| `file`          | `string`                                | Workspace-relative path of the current source file                                    |
| `line`          | `integer`                               | 1-based line number                                                                   |
| `column`        | `integer`                               | 1-based column number                                                                 |
| `sourceContext` | `object`                                | 2 lines before, current line, 2 lines after — with line numbers prefixed              |
| `callStack`     | `array`                                 | Top N stack frames (name, file, line). Default N=5.                                   |
| `locals`        | `object`                                | Local variables in the current frame: `{ name: { value, type } }`                     |
| `closures`      | `object`                                | Closure/captured variables (same format as `locals`)                                  |
| `output`        | `string`                                | Captured stdout/stderr since last snapshot (delta, not cumulative)                    |

### 8.3 Size Budget

To prevent token explosion, the snapshot enforces size limits:

| Component             | Limit                                               |
| --------------------- | --------------------------------------------------- |
| Source context lines  | 2 before + 1 current + 2 after = 5 lines            |
| Call stack frames     | Max 5 (configurable via `debug_getSnapshot`)        |
| Local variables       | Max 20 variables per scope                          |
| Variable value length | Truncated at 200 characters with `"...(truncated)"` |
| Nested variable depth | Default 1 level (configurable 0–3)                  |
| Array/dict elements   | Max 10 elements shown, with `"...(+N more)"`        |
| Output                | Last 500 characters of stdout/stderr                |

### 8.4 Exception Snapshots

When the program pauses on an exception, the snapshot includes additional fields:

```json
{
  "status": "paused",
  "reason": "exception",
  "exception": {
    "type": "RuntimeError",
    "message": "Cannot read property 'port' of null",
    "breakMode": "uncaught"
  },
  "file": "server.stash",
  "line": 23,
  ...
}
```

---

## 9. Interaction with Existing Stash Extension

The Stash VS Code extension (`stash-lang`) already provides:

- DAP factory that launches `stash-dap` binary
- Debug configuration provider for `.stash` files
- Test Explorer with Run + Debug profiles
- Inline values during debugging

### 9.1 Non-Interference Principle

The debug-agent-tools extension operates **alongside** the Stash extension, not inside it. They share the VS Code `debug` API surface but don't directly communicate.

| Concern                                                                   | Resolution                                                                                            |
| ------------------------------------------------------------------------- | ----------------------------------------------------------------------------------------------------- |
| Agent starts session → Stash extension's DAP factory resolves the adapter | Works naturally — the agent provides `type: "stash"`, VS Code routes to the Stash extension's factory |
| Agent sets breakpoints → Stash extension shows inline decorations         | Works naturally — breakpoints are in the shared `debug.breakpoints` collection                        |
| User has existing breakpoints → Agent adds more                           | Agent tracks its own breakpoints separately, never removes user breakpoints                           |
| Stash extension's inline values during debug                              | Work automatically — they respond to the active debug session                                         |
| Agent stops session → Stash extension detects termination                 | Works naturally via `onDidTerminateDebugSession`                                                      |

### 9.2 Potential Conflicts

**Conflict 1:** Agent starts a debug session while the user is already debugging something.

- **Resolution:** The agent only manages sessions it started. Both sessions can coexist (VS Code supports multiple debug sessions). However, the agent's tools only operate on the agent's session.

**Conflict 2:** The user manually stops the agent's debug session.

- **Resolution:** The session manager detects `onDidTerminateDebugSession` and cleans up. Next tool call returns an error indicating no active session.

---

## 10. Cross-Platform Considerations

### 10.1 File Paths

- All file paths in tool inputs/outputs use the OS-native format (forward slashes on Linux/macOS, backslashes on Windows).
- The extension normalizes paths internally using `vscode.Uri.file()`.
- Workspace-relative paths in snapshots always use forward slashes for consistency.

### 10.2 Debug Adapter Availability

The tool requires the appropriate debug adapter to be installed. If the user tries to debug a `.py` file without the Python extension, the tool returns a clear error: "No debug adapter found for type 'python'. Install the Python extension."

### 10.3 Line Endings

No impact — DAP operates on line numbers, not byte offsets. The debug adapter handles line ending normalization.

### 10.4 Terminal Output Capture

Output capture (stdout/stderr) relies on the debug adapter forwarding output events. The Stash DAP does this; other adapters may not. The `output` field in snapshots is best-effort.

---

## 11. Security & Safety

### 11.1 User Confirmation

The `prepareInvocation` method returns `LanguageModelToolConfirmationMessages` for destructive/significant actions:

| Tool                      | Confirmation Required?   | Message                                                                       |
| ------------------------- | ------------------------ | ----------------------------------------------------------------------------- |
| `debug_startSession`      | **Yes**                  | "Start debugging `{program}` with args `{args}`?"                             |
| `debug_setBreakpoints`    | No                       | —                                                                             |
| `debug_removeBreakpoints` | No                       | —                                                                             |
| `debug_continue`          | No                       | —                                                                             |
| `debug_step`              | No                       | —                                                                             |
| `debug_getSnapshot`       | No                       | —                                                                             |
| `debug_evaluate`          | **Only if context=repl** | "Evaluate `{expression}` in the running program? This may have side effects." |
| `debug_stopSession`       | No                       | —                                                                             |

### 11.2 Expression Evaluation Safety

- Default context is `"watch"`, which signals side-effect-free evaluation.
- The `"repl"` context allows side effects and requires user confirmation.
- The Stash DAP evaluates watch expressions in an isolated VM, which is inherently safe.
- Other debug adapters may execute arbitrary code even in `"watch"` context. This is documented as a known adapter-specific behavior, not a flaw in the tool design.

### 11.3 Environment Variable Sanitization

When the agent provides `env` in `debug_startSession`, the extension does NOT blindly pass them through. It:

1. Rejects known-sensitive variable names: `AWS_SECRET_ACCESS_KEY`, `GITHUB_TOKEN`, `DATABASE_PASSWORD`, etc. (configurable blocklist).
2. Shows all environment variables in the confirmation dialog.

### 11.4 Program Execution

Starting a debug session **runs a program**. This is inherently not sandboxed. The confirmation dialog is the primary safety mechanism — the user must approve what program is being run.

### 11.5 Resource Limits

- Maximum 1 concurrent debug session per agent
- Step count capped at 20 per tool call
- Snapshot variable depth capped at 3
- Output capture capped at 10KB per session
- Session auto-terminated after 5 minutes of no tool interaction (configurable)

---

## 12. Error Handling

### 12.1 Error Categories

| Error                           | Tool Response                                                                                                                                                                             |
| ------------------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| No active debug session         | `{ "error": "no_session", "message": "No active debug session. Call debug_startSession first." }`                                                                                         |
| Session not paused              | `{ "error": "not_paused", "message": "Debug session is running. Cannot inspect state while running. Use debug_continue to wait for the next pause, or debug_stopSession to terminate." }` |
| Debug adapter not found         | `{ "error": "no_adapter", "message": "No debug adapter found for type '{type}'. Install the appropriate extension." }`                                                                    |
| Session start failed            | `{ "error": "start_failed", "message": "Failed to start debug session: {details}" }`                                                                                                      |
| Breakpoint verification failed  | Non-fatal — breakpoint is set but `verified: false` in response                                                                                                                           |
| Evaluation error                | `{ "error": "eval_failed", "message": "Evaluation failed: {error}", "expression": "..." }`                                                                                                |
| Timeout                         | `{ "error": "timeout", "message": "Operation timed out after {ms}ms." }`                                                                                                                  |
| Session terminated unexpectedly | `{ "error": "session_terminated", "message": "Debug session ended unexpectedly.", "output": "..." }`                                                                                      |

### 12.2 Graceful Degradation

If a specific DAP capability is not supported by the current debug adapter:

- Missing `supportsConditionalBreakpoints` → condition is silently ignored, breakpoint set without condition, and a warning is included in the response.
- Missing `supportsEvaluateForHovers` → `debug_evaluate` returns an error explaining the adapter doesn't support evaluation.
- Missing `supportsLogPoints` → logMessage is silently ignored, similar warning.

The extension queries the adapter's `Capabilities` response during session startup and adjusts behavior accordingly.

---

## 13. Implementation Roadmap

### Phase 1: Core Loop (MVP)

The minimum viable set that enables the snapshot-observe-act debugging workflow:

1. `debug_startSession` — basic launch (program, args, stopOnEntry)
2. `debug_setBreakpoints` — source breakpoints only (no conditions)
3. `debug_continue` — with blocking + timeout
4. `debug_getSnapshot` — full snapshot assembly
5. `debug_stopSession` — clean termination

**Validation target:** An agent can debug a Stash script by setting a breakpoint, running to it, inspecting variables, and understanding the bug.

### Phase 2: Smart Debugging

Add the features that make hypothesis-driven debugging effective:

6. Conditional breakpoints and logpoints in `debug_setBreakpoints`
7. `debug_evaluate` — expression evaluation
8. `debug_step` — step over/in/out
9. Exception breakpoints in `debug_startSession`
10. `debug_removeBreakpoints`

### Phase 3: Polish

11. Output capture and streaming
12. Multi-thread awareness (thread selection in continue/step)
13. Session auto-timeout
14. Environment variable safety blocklist
15. Adapter capability negotiation and graceful degradation

### Package Structure

```
debug-agent-tools/
├── package.json              — Extension manifest, tool contributions
├── tsconfig.json
├── src/
│   ├── extension.ts          — Activation, tool registration
│   ├── tools/
│   │   ├── startSession.ts   — debug_startSession tool
│   │   ├── setBreakpoints.ts — debug_setBreakpoints tool
│   │   ├── removeBreakpoints.ts
│   │   ├── continue.ts       — debug_continue tool
│   │   ├── step.ts           — debug_step tool
│   │   ├── getSnapshot.ts    — debug_getSnapshot tool
│   │   ├── evaluate.ts       — debug_evaluate tool
│   │   └── stopSession.ts    — debug_stopSession tool
│   ├── debugSessionManager.ts — Session lifecycle management
│   ├── snapshotBuilder.ts     — DebugSnapshot assembly
│   ├── timeoutController.ts   — Timeout utilities
│   ├── breakpointTracker.ts   — Agent vs. user breakpoint tracking
│   ├── adapterCapabilities.ts — Capability negotiation
│   └── types.ts               — Shared type definitions
├── test/
│   └── ...                    — Unit tests
└── README.md
```

---

## 14. Test Scenarios

### 14.1 Happy Path

| #   | Scenario                | Steps                                                                       | Expected                                               |
| --- | ----------------------- | --------------------------------------------------------------------------- | ------------------------------------------------------ |
| 1   | Start and stop          | `startSession` → `stopSession`                                              | Session starts, program runs, clean termination        |
| 2   | Stop on entry           | `startSession(stopOnEntry: true)`                                           | Returns snapshot paused at first line                  |
| 3   | Breakpoint hit          | `setBreakpoints(line 10)` → `startSession` → `continue`                     | Snapshot at line 10 with correct locals                |
| 4   | Conditional breakpoint  | `setBreakpoints(line 10, condition: "i > 5")` → `startSession` → `continue` | Pauses only when condition is true                     |
| 5   | Step over               | `startSession(stopOnEntry)` → `step(over)`                                  | Advances one line, snapshot shows next line            |
| 6   | Step into function      | At function call → `step(in)`                                               | Enters function, snapshot shows first line of function |
| 7   | Step out                | Inside function → `step(out)`                                               | Returns to caller, snapshot shows call site            |
| 8   | Evaluate expression     | Paused → `evaluate("x + y")`                                                | Returns computed value                                 |
| 9   | Multiple breakpoints    | Set 3 breakpoints → run → continue through each                             | Snapshot at each breakpoint in order                   |
| 10  | Full debugging workflow | Set breakpoint → run → inspect → evaluate → continue → stop                 | End-to-end hypothesis-driven debugging                 |

### 14.2 Edge Cases

| #   | Scenario                              | Expected                                                         |
| --- | ------------------------------------- | ---------------------------------------------------------------- |
| 11  | Program exits before breakpoint       | `debug_continue` returns `{ status: "terminated" }`              |
| 12  | Program hangs (infinite loop)         | `debug_continue` returns timeout after 10s                       |
| 13  | Breakpoint on non-existent line       | Breakpoint set but `verified: false`                             |
| 14  | Evaluate syntax error                 | Returns `eval_failed` error with message                         |
| 15  | Start session without debug adapter   | Returns `no_adapter` error                                       |
| 16  | Tool call with no active session      | Returns `no_session` error                                       |
| 17  | User stops agent's session manually   | Next tool call detects termination, returns `session_terminated` |
| 18  | Start new session while one is active | Old session terminated first, new one starts                     |
| 19  | Step count > 20                       | Rejected with error                                              |
| 20  | Large variable (1000-element array)   | Truncated to 10 elements + count                                 |

### 14.3 Error Cases

| #   | Scenario                                       | Expected                                  |
| --- | ---------------------------------------------- | ----------------------------------------- |
| 21  | `debug_continue` when not paused               | Returns `not_paused` error                |
| 22  | `debug_step` when not paused                   | Returns `not_paused` error                |
| 23  | `debug_evaluate` when session is running       | Returns `not_paused` error                |
| 24  | Invalid file path in `setBreakpoints`          | Breakpoints created but `verified: false` |
| 25  | `debug_startSession` with invalid program path | Returns `start_failed` error              |

---

## 15. Design Decisions & Alternatives

### Decision 1: Language Model Tool API vs. MCP Server

**Chosen:** VS Code Language Model Tool API (`lm.registerTool()`)

**Alternatives Considered:**

- **MCP Server wrapping DAP:** Could build a standalone MCP server that speaks DAP. Would work with any MCP-capable client, not just VS Code.
- **Chat Participant:** Could build a `@debug` chat participant that interprets natural language commands.

**Rationale:**

- `lm.registerTool()` is the native VS Code mechanism for giving tools to AI agents. It's what Copilot uses.
- MCP server loses tight VS Code integration — no access to `debug.breakpoints`, no event subscription, no confirmation dialogs.
- Chat participant would require natural language parsing for every command, adding latency and ambiguity.
- Language Model Tools get automatic schema validation, confirmation UI, and integration with any VS Code agent mode.

**Risk:** The `lm.registerTool()` API is relatively new. If it changes significantly, the extension needs updating. Mitigated by the API being marked as finalized (not proposed).

---

### Decision 2: Single Session vs. Multi-Session

**Chosen:** Single concurrent session

**Alternatives Considered:**

- **Multi-session with session IDs:** Every tool takes a `sessionId` parameter. Agent can manage multiple sessions.

**Rationale:**

- Multi-session doubles the API surface complexity.
- Agents are sequential reasoners — they process one thing at a time.
- The overwhelmingly common case is: debug one program, find the bug, fix it.
- If multi-session is needed later, the `sessionId` parameter can be added without breaking existing tools (it would be optional, defaulting to the active session).

**Risk:** Can't debug client + server simultaneously. Accepted — this is an advanced scenario that can be addressed in a future version.

---

### Decision 3: Blocking Continue vs. Fire-and-Forget + Poll

**Chosen:** Blocking continue with timeout

**Alternatives Considered:**

- **Fire-and-forget:** `debug_continue` returns immediately, agent calls `debug_getSnapshot` to check status.
- **Event-driven:** Extension sends notifications to the agent when the program pauses.

**Rationale:**

- Fire-and-forget requires the agent to poll, which wastes tokens and adds latency.
- Event-driven isn't supported by the current Language Model Tool API (tools are request-response, not event-driven).
- Blocking with timeout is the same pattern used by terminal tools (`run_in_terminal` with `mode: "sync"`).
- The timeout ensures the tool never hangs indefinitely.

**Risk:** If the timeout is too short, the agent gets a "running" response and has to decide what to do. The default 10s timeout is generous for most cases; the agent can specify a longer timeout if needed.

---

### Decision 4: Replace Semantics for Breakpoints

**Chosen:** `debug_setBreakpoints` replaces all agent breakpoints in a file

**Alternatives Considered:**

- **Additive:** Each call adds breakpoints; separate `removeBreakpoint(id)` to remove.
- **Per-line toggle:** `setBreakpoint(file, line)` / `removeBreakpoint(file, line)`.

**Rationale:**

- Replace semantics match DAP's `setBreakpoints` request, which replaces all breakpoints in a source.
- Simplifies state management — the agent doesn't need to track breakpoint IDs.
- To add a breakpoint, the agent includes all desired breakpoints in the call. To remove one, it calls with the remaining set.
- An empty array clears all breakpoints in the file, which is a clean "reset" operation.

**Risk:** Agent must remember existing breakpoints when adding new ones. Mitigated by `debug_getSnapshot` including active breakpoints in the response if needed (could add this field).

---

### Decision 5: Snapshot Size Limits

**Chosen:** Conservative defaults with configurable overrides

**Rationale:**

- Token budgets are real constraints. A snapshot with 50 variables, each with nested objects, could consume 2000+ tokens.
- The defaults (5 stack frames, 20 locals, 1-level depth) are sufficient for 90% of debugging scenarios.
- The agent can request deeper inspection via `debug_getSnapshot` with custom `variableDepth`/`stackDepth`.
- Individual variables can be inspected deeper via `debug_evaluate`.

---

### Decision 6: Separate Extension vs. Part of Stash Extension

**Chosen:** Separate extension

**Alternatives Considered:**

- **Embed in stash-lang extension:** Add the tools directly to the existing Stash VS Code extension.

**Rationale:**

- Debug agent tools are language-agnostic. Embedding them in the Stash extension would make them unavailable for Python, Node.js, etc.
- Separation of concerns: the Stash extension provides language features; the debug tools extension provides agent capabilities.
- Users who don't use AI agents don't need the extra code loaded.
- Can be published to the marketplace independently, with its own release cycle.

**Risk:** Two extensions to install. Mitigated by adding it as an extension pack recommendation or dependency in the future.

---

## 16. Open Questions

### Q1: Tool Naming Convention

Should tools use a `debug_` prefix (as in this spec) or a `debug.` dot-separated namespace? The VS Code tool API uses flat names, but there's no established convention for namespacing.

**Leaning:** `debug_` prefix. Dots might cause issues with tool name parsing in some LM implementations.

### Q2: Output Streaming

Should `debug_continue` stream stdout/stderr as it accumulates, or only return it as part of the final snapshot? The Language Model Tool API supports returning `LanguageModelToolResult` with multiple content parts, but not streaming.

**Leaning:** Return accumulated output in the snapshot. No streaming — it's not supported by the tool API and would complicate the implementation.

### Q3: Breakpoint Persistence

Should agent-managed breakpoints survive between sessions? Currently, breakpoints are cleared when `debug_stopSession` is called. But if the agent sets breakpoints → stops → fixes code → starts again, it would have to re-set them.

**Leaning:** Persist breakpoints across sessions. Only `debug_removeBreakpoints` explicitly removes them. This matches how VS Code breakpoints work (they persist in the workspace).

### Q4: Custom Launch Configurations

Should `debug_startSession` support referencing a `launch.json` configuration by name, instead of always building an ad-hoc config?

**Leaning:** Yes, add an optional `configuration` parameter that references a named launch config. This lets the agent use project-specific debug settings (source maps, pre-launch tasks, etc.) without having to reconstruct them.

### Q5: Which Extension Marketplace?

Should this be published to the VS Code Marketplace as a general-purpose extension, or kept as a local/private extension?

**Leaning:** Marketplace. The tool is language-agnostic and useful to anyone using Copilot for debugging. But this decision depends on maturity and testing.
