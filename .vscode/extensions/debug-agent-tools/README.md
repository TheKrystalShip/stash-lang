# Debug Agent Tools

Language Model Tools for AI-assisted debugging — lets AI agents autonomously launch, control, and inspect debug sessions through VS Code's debug API.

✦ **Tools** — 8 Language Model Tools · **DAP** — works with any compliant debug adapter · **Languages** — 11 built-in mappings · **Snapshot** — source context, call stack, locals, closures, output

---

## Overview

Debug Agent Tools registers 8 Language Model Tools that expose VS Code's debug API to AI agents. When an AI agent (such as GitHub Copilot in Agent mode) needs to investigate a bug, it can autonomously start a debug session, set conditional breakpoints, step through code, inspect variables, and evaluate expressions — all without human intervention at each step.

**Who it's for:** AI agents operating in autonomous or semi-autonomous debugging workflows. The tools are designed to be invoked by the agent, not by the user directly.

**What it is NOT:**

- Not a standalone debugger UI — it has no panels, views, or commands of its own
- Not language-specific — it delegates all execution to whichever debug adapter is installed for the target language
- Not a replacement for the existing VS Code debugger — it augments it with a tool-callable API

---

## Tools

All 8 tools are registered as VS Code Language Model Tools and are available to any agent that supports the Language Model Tools API.

| Tool                      | Purpose                                     | Key Parameters                                                                 |
| ------------------------- | ------------------------------------------- | ------------------------------------------------------------------------------ |
| `debug_startSession`      | Launch a debug session                      | `program`, `debugType`, `stopOnEntry`, `args`                                  |
| `debug_setBreakpoints`    | Set or replace breakpoints in a file        | `file`, `breakpoints[]` with `line`, `condition`, `hitCondition`, `logMessage` |
| `debug_removeBreakpoints` | Remove agent-managed breakpoints            | `file` (optional — omit to remove all)                                         |
| `debug_continue`          | Resume execution until next pause           | `timeout` (default 10s)                                                        |
| `debug_step`              | Step over, into, or out of a function       | `action`, `count` (max 20)                                                     |
| `debug_getSnapshot`       | Read current debug state                    | `variableDepth` (0–3), `stackDepth` (1–20), `includeGlobals`                   |
| `debug_evaluate`          | Evaluate an expression in the current frame | `expression`, `frameIndex`, `context` (`watch`/`repl`/`hover`)                 |
| `debug_stopSession`       | Terminate the debug session                 | `captureOutput`                                                                |

### `debug_startSession`

Launches a new debug session for the specified program. If `debugType` is omitted, the type is inferred from the file extension. The session must be stopped before starting a new one (single-session constraint).

| Parameter     | Type        | Description                                                                         |
| ------------- | ----------- | ----------------------------------------------------------------------------------- |
| `program`     | `string`    | Absolute or workspace-relative path to the entry point                              |
| `debugType`   | `string?`   | DAP debug type (e.g. `stash`, `python`, `node`). Inferred from extension if omitted |
| `stopOnEntry` | `boolean?`  | Pause on the first line of execution. Default: `false`                              |
| `args`        | `string[]?` | Command-line arguments passed to the program                                        |
| `env`         | `object?`   | Additional environment variables. Keys matching the blocklist are rejected          |

### `debug_setBreakpoints`

Sets or replaces all agent-managed breakpoints in a file. Agent breakpoints are tracked separately from user-set breakpoints and never interfere with them.

| Parameter                    | Type       | Description                            |
| ---------------------------- | ---------- | -------------------------------------- |
| `file`                       | `string`   | File path to set breakpoints in        |
| `breakpoints`                | `object[]` | Array of breakpoint descriptors        |
| `breakpoints[].line`         | `number`   | 1-based line number                    |
| `breakpoints[].condition`    | `string?`  | Expression — pauses only when truthy   |
| `breakpoints[].hitCondition` | `string?`  | Pause after the line is hit N times    |
| `breakpoints[].logMessage`   | `string?`  | Log message without pausing (logpoint) |

### `debug_removeBreakpoints`

Removes agent-managed breakpoints. Specify `file` to remove breakpoints in a single file; omit it to remove all agent-managed breakpoints across all files.

### `debug_continue`

Resumes execution and waits up to `timeout` milliseconds for the program to pause again (at a breakpoint, exception, or step). Returns a snapshot of the new paused state.

### `debug_step`

Executes one or more step actions. `action` must be one of `over`, `in`, or `out`. `count` controls how many steps to take in a single call (maximum 20). Returns a snapshot after the final step.

### `debug_getSnapshot`

Returns the current debug snapshot without advancing execution. Use this to re-read state after evaluating expressions. `variableDepth` controls how many levels of nested objects are expanded (0–3); `stackDepth` controls how many call stack frames are included (1–20).

### `debug_evaluate`

Evaluates an expression in the context of a specific stack frame. `frameIndex` is 0-based (0 = innermost frame). `context` controls how the adapter interprets the expression: `watch` for display, `repl` for side-effecting evaluation, `hover` for tooltip display.

> **Note:** REPL-context evaluation may have side effects. User confirmation is required before the first `repl` evaluation in a session.

### `debug_stopSession`

Terminates the active debug session. If `captureOutput` is `true`, returns all captured output from the session. Cleans up all agent-managed breakpoints automatically.

---

## Debug Snapshot

Most tools return a **debug snapshot** — a structured JSON object describing the complete state of the program at the current pause point.

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

### Snapshot Size Limits

The snapshot is intentionally bounded to keep agent context usage predictable.

| Component             | Limit                                              |
| --------------------- | -------------------------------------------------- |
| Source context        | 2 lines before + current line + 2 lines after      |
| Call stack frames     | Max 5 (configurable up to 20 via `stackDepth`)     |
| Local variables       | Max 20 per scope                                   |
| Variable value length | 200 characters (truncated with `…`)                |
| Nested variable depth | 1 level (configurable up to 3 via `variableDepth`) |
| Array / dict elements | 10 shown (with count of remaining)                 |
| Output                | Last 500 characters                                |

---

## Debugging Workflow

The tools are designed for an iterative hypothesis-and-verify loop:

```
1. Agent reads code and error output
2. Agent forms hypothesis ("config is null at line 47")
3. debug_setBreakpoints — set conditional breakpoint at the suspected location
4. debug_startSession  — launch the program
5. debug_continue      — run to the breakpoint
6. debug_getSnapshot   — read locals, call stack, and output
7. debug_evaluate      — evaluate expressions to confirm or refute the hypothesis
8. Agent fixes code or forms a new hypothesis
9. debug_stopSession   — clean up and capture final output
```

The agent may iterate steps 3–8 multiple times within a single session (re-setting breakpoints, stepping through code, evaluating expressions) before stopping.

---

## Supported Languages

The extension works with **any language** that has a debug adapter installed in VS Code. When `debugType` is omitted from `debug_startSession`, the extension automatically:

1. Opens the target file in VS Code's document model (without showing it in the editor)
2. Reads the detected language ID (e.g., `python`, `javascript`, `stash`)
3. Scans all installed extensions for a debug adapter that supports that language
4. Uses the first matching adapter

This means any language with a debug extension — Python, JavaScript/TypeScript, Go, Rust, C/C++, C#, Java, Ruby, PHP, Stash, and any future language — works automatically without configuration.

If automatic detection fails (e.g., unrecognized file type), the error message includes a list of all available debug adapters so the agent can retry with an explicit `debugType`.

---

## Security

Debug Agent Tools enforces several safeguards to prevent unintended or harmful actions:

- **User confirmation** — VS Code prompts the user before the first `debug_startSession` call and before any `repl`-context evaluation that could have side effects
- **Environment variable blocklist** — sensitive keys (`SECRET`, `TOKEN`, `PASSWORD`, `API_KEY`, `PRIVATE_KEY`, and others) are rejected; the agent cannot inject them into the debugged process
- **Single-session constraint** — only one debug session may be active at a time; starting a second session requires the first to be stopped
- **Auto-timeout** — sessions idle for more than 5 minutes are automatically terminated and cleaned up
- **Output cap** — captured output is capped at 10 KB to prevent runaway memory consumption

---

## Requirements

- **VS Code 1.99.0** or later
- A **debug adapter extension** for the target language (e.g. the Python extension for `.py` files, the C# Extension for `.cs` files, etc)
- An **AI agent** capable of invoking Language Model Tools (e.g. GitHub Copilot in Agent mode)
