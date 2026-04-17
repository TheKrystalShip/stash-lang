---
description: "Use when: querying language server features (hover, symbols, call hierarchy, code actions, signature help, type definitions) or debugging programs (breakpoints, stepping, variable inspection, expression evaluation). These are VS Code Language Model Tools available to AI agents."
---

# Agent Tools — LSP & Debugging

Two VS Code extensions expose language server and debugging capabilities as Language Model Tools for AI agents. These tools are **always preferred** over manual file reading or terminal-based workarounds when the information they provide is sufficient.

## LSP Tools (lsp-agent-tools)

Seven tools wrapping VS Code's LSP integration. Work with **any language that has an active language server** — Stash, TypeScript, Python, C#, etc.

### When to Use LSP Tools Instead of File Reading

| Need                               | Use This                       | Not This                                         |
| ---------------------------------- | ------------------------------ | ------------------------------------------------ |
| Function signature or type         | `lsp_hover`                    | Reading the file and scanning for the definition |
| File structure / outline           | `lsp_documentSymbols`          | Reading the entire file                          |
| What calls a function              | `lsp_callHierarchy` (incoming) | `grep_search` for the function name              |
| What a function calls              | `lsp_callHierarchy` (outgoing) | Reading the function body                        |
| Find a symbol across the workspace | `lsp_workspaceSymbols`         | `file_search` + `grep_search`                    |
| How to call a function             | `lsp_signatureHelp`            | Reading the function definition                  |
| Navigate to a type's definition    | `lsp_typeDefinition`           | `grep_search` for the type name                  |
| Available quick fixes for an error | `lsp_codeActions`              | Inventing a fix manually                         |

### Tool Reference

| Tool                   | Purpose                                       | Required Params      | Key Optional Params                                                                                      |
| ---------------------- | --------------------------------------------- | -------------------- | -------------------------------------------------------------------------------------------------------- |
| `lsp_hover`            | Type info, docs, signatures for a symbol      | `filePath`, `symbol` | `lineContent`, `line` (disambiguation)                                                                   |
| `lsp_documentSymbols`  | Structured outline of all symbols in a file   | `filePath`           | `kind` filter: function, class, method, variable, constant, struct, enum, interface, namespace, property |
| `lsp_callHierarchy`    | Incoming callers / outgoing callees           | `filePath`, `symbol` | `direction` (incoming/outgoing/both), `depth` (1-3), `lineContent`                                       |
| `lsp_workspaceSymbols` | Search symbols by name across workspace       | `query`              | `kind` filter, `maxResults` (1-50)                                                                       |
| `lsp_codeActions`      | Quick fixes, refactorings at a line           | `filePath`, `line`   | `kind` (quickfix/refactor/source/all), `apply` (execute by title), `diagnosticMessage`                   |
| `lsp_signatureHelp`    | Parameter names, types, order at a call site  | `filePath`, `symbol` | `lineContent` (must match a **call site**, not a definition)                                             |
| `lsp_typeDefinition`   | Navigate from variable to its type definition | `filePath`, `symbol` | `lineContent`                                                                                            |

### Position Resolution

Tools that accept `symbol` use word-boundary matching to find the symbol in the file. Resolution priority:

1. **`lineContent`** — scans for a matching line, then finds symbol within it (most precise)
2. **`line`** — uses the 1-based line number directly
3. **Fallback** — scans the entire file for the first match

For dotted symbols like `arr.push` or `io.println`, the cursor is placed on the **member** part (after the dot), not the namespace prefix.

**Tip:** When a symbol appears multiple times in a file, always provide `lineContent` or `line` to disambiguate.

### Limitations

- **`lsp_signatureHelp`** requires the cursor at a **call site** (where the function is called with parentheses), not at the function definition. Point it to the file containing the call.
- **`lsp_callHierarchy`** depends on the language server's call hierarchy provider. Some language servers (e.g., C# in certain configurations) may not register one — the tool will report this.
- **`lsp_codeActions`** results depend on what the language server offers. Use `kind` to filter (e.g., `quickfix` to see only fixes, not refactorings).

## Debug Tools (debug-agent-tools)

Eight tools for interactive debugging via VS Code's Debug Adapter Protocol. Work with **any language that has a debug adapter** — Stash, Python, Node.js, etc.

### Typical Debug Workflow

```
1. debug_setBreakpoints  → Set breakpoints in the file(s) of interest
2. debug_startSession    → Launch the program (optionally with stopOnEntry)
3. debug_getSnapshot     → Inspect current state (file, line, locals, stack)
4. debug_step            → Step over/in/out to advance execution
5. debug_evaluate        → Evaluate expressions in the current frame
6. debug_continue        → Resume to next breakpoint or completion
7. debug_stopSession     → End session and get summary
```

### Tool Reference

| Tool                      | Purpose                                       | Required Params         | Key Optional Params                                                                                                                      |
| ------------------------- | --------------------------------------------- | ----------------------- | ---------------------------------------------------------------------------------------------------------------------------------------- |
| `debug_startSession`      | Launch a debug session                        | `program`               | `debugType` (auto-detected), `args`, `cwd`, `env`, `stopOnEntry`, `exceptionBreakpoints` (none/uncaught/all), `noDebug`, `configuration` |
| `debug_setBreakpoints`    | Set breakpoints in a file (replace semantics) | `file`, `breakpoints[]` | Each breakpoint: `line` (required), `condition`, `hitCondition`, `logMessage`                                                            |
| `debug_removeBreakpoints` | Remove agent breakpoints                      | —                       | `file` (if omitted, removes all)                                                                                                         |
| `debug_continue`          | Resume until next pause or exit               | —                       | `threadId`, `timeout` (default: 10s)                                                                                                     |
| `debug_step`              | Step over/in/out                              | `action` (over/in/out)  | `threadId`, `count` (1-20)                                                                                                               |
| `debug_getSnapshot`       | Read current debug state                      | —                       | `variableDepth` (0-3), `stackDepth` (1-20), `includeGlobals`                                                                             |
| `debug_evaluate`          | Evaluate expression in frame context          | `expression`            | `frameIndex` (0 = top), `context` (watch/repl/hover)                                                                                     |
| `debug_stopSession`       | End session, get summary                      | —                       | `captureOutput` (default: true)                                                                                                          |

### Key Behaviors

- **One session at a time** — starting a new session terminates the previous one.
- **Replace semantics for breakpoints** — `debug_setBreakpoints` replaces all agent breakpoints in the specified file. Pass an empty array to clear.
- **Agent vs. user breakpoints** — agent tools never affect user-set breakpoints. Cleanup is automatic on `debug_stopSession`.
- **Blocking calls** — `debug_continue` and `debug_step` block until the program pauses, exits, or times out.
- **Snapshots** — `debug_getSnapshot`, `debug_continue`, and `debug_step` all return the current state: file, line, source context, call stack, locals, and captured output.

### Breakpoint Types

```
Simple:       { "line": 42 }
Conditional:  { "line": 42, "condition": "x > 5" }
Hit count:    { "line": 42, "hitCondition": ">= 10" }
Logpoint:     { "line": 42, "logMessage": "x = {x}, y = {y}" }
```

### Stash-Specific Debug Configuration

For Stash files, the debug type is `stash` and uses the Stash DAP server. Example:

```
debug_startSession({
  program: "/path/to/script.stash",
  stopOnEntry: true,
  args: ["--verbose"],
  exceptionBreakpoints: "all"
})
```

The Stash DAP supports all standard features: breakpoints, stepping, variable inspection (including struct fields, dict entries, array elements), closure variables, and expression evaluation.
