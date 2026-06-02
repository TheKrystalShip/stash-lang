# Code Navigation & Search Tools

How an agent should navigate and search this codebase, and which tool to reach for. There are **two
contexts** with different toolsets:

- **Terminal / CLI Claude Code (the common case here):** the native **`LSP` tool** (below) plus
  `grep`/`rg`/`Read`. No debugger tool.
- **Claude Code running *inside* VS Code:** additionally exposes the project-built `lsp-agent-tools`
  and `debug-agent-tools` extensions (see the last section). These do **not** exist in a terminal
  session — don't assume them.

## Choosing a tool: grep vs Read vs LSP

The deciding question is **"am I matching text, or asking about a symbol's meaning?"**

| Need | Reach for | Why |
| ---- | --------- | --- |
| *Where does this **text/pattern** appear?* (literal, config key, TODO, concept, name when location unknown) | **`grep` / `rg`** | Broad, fast, language-agnostic; works on comments, strings, markdown, config, and file types with no language server. |
| *Understand the logic/flow of a file I've already located* | **`Read`** | Comprehension in context — not search. |
| *A **semantic** question about a specific symbol* — where is **this** defined, who really references this binding, its type/signature/doc, a file's outline, the call graph | **`LSP`** | Understands scopes, bindings, and types, so it excludes the false positives a text search can't (`grep deploy` also hits a comment, the string `"deployed"`, and an unrelated `deploy`). |

These **compose**: `grep` to find an unknown thing by name → `LSP` for its precise definition/references
→ `Read` to understand the surrounding logic.

**This is a heuristic, not a mandate.** Use the cheapest tool that actually answers the question.
Don't force `LSP` when a text sweep is the real need; don't `grep` for a symbol's definition when `LSP`
gives it precisely. The bias to add (vs. pure habit): for **definition / references / type / file-outline
of a known symbol in a `.cs` or `.stash` file**, prefer `LSP` — that's where it's strictly better, and
for `.stash` it's the only semantic option that exists.

## The native `LSP` tool (terminal / CLI)

**Deferred tool.** Each session it appears only as the name `LSP` in a `<system-reminder>` listing
deferred tools. Load its schema once with `ToolSearch` (`select:LSP`) before the first call, then invoke
it like any tool. It is gated behind `ENABLE_LSP_TOOL=1` in `~/.claude/settings.json` plus enabled LSP
plugins — already configured on this machine (see [[csharp-lsp-not-in-cli]] in agent memory for the full
setup story).

**Operations** (`operation` + `filePath` + `line` + `character`, both 1-based as shown in an editor):

`goToDefinition`, `findReferences`, `hover`, `documentSymbol`, `workspaceSymbol`, `goToImplementation`,
`prepareCallHierarchy`, `incomingCalls`, `outgoingCalls`.

**Language coverage:**

| File type | Server | Plugin |
| --------- | ------ | ------ |
| `.cs` | `csharp-ls` | `csharp-lsp@claude-plugins-official` |
| `.stash` | `stash-lsp` | `stash-lsp@stash-local` (local marketplace at `.claude/lsp-marketplace/`) |

Any other extension returns `No LSP server available for file type`.

**Two limits to know:**

- **No diagnostics.** The `LSP` tool has no diagnostics operation, and this CLI build does **not**
  auto-surface "red squiggles" after edits. For errors/warnings use the compiler: **`dotnet build`** for
  C#, and `dotnet test` / running the script for Stash. The `LSP` tool is for *navigation*, not linting.
- **Lazy + cold start.** A server launches on the first touch of a matching file. `csharp-ls` loads the
  whole `Stash.sln` (~11 projects, up to a minute) before whole-program results like cross-project
  `findReferences` are complete; `documentSymbol`/`hover` answer almost immediately.

## When running inside VS Code (not the terminal CLI)

This project ships two VS Code extensions (`.vscode/extensions/…`, specs in `.kanban/4-done/`) that expose
*additional* Language Model tools **only** when Claude Code runs as the VS Code extension with a live
language server:

- **`lsp-agent-tools`** — `lsp_hover`, `lsp_documentSymbols`, `lsp_callHierarchy`, `lsp_workspaceSymbols`,
  `lsp_codeActions`, `lsp_signatureHelp`, `lsp_typeDefinition`. (Note `lsp_codeActions` *can* surface
  quick-fix diagnostics — a capability the terminal `LSP` tool lacks.)
- **`debug-agent-tools`** — `debug_setBreakpoints`, `debug_startSession`, `debug_getSnapshot`,
  `debug_step`, `debug_evaluate`, `debug_continue`, `debug_stopSession`, `debug_removeBreakpoints` (DAP).

The same heuristic applies: prefer these over manual file reading for the information they provide. Full
reference (parameters, position-resolution rules, limitations) lives in
`.github/instructions/agent-tools.instructions.md`. If `VSCODE_PID` is unset / `CLAUDE_CODE_ENTRYPOINT=cli`,
you are **not** in VS Code — use the native `LSP` tool and `grep`/`Read` instead.
