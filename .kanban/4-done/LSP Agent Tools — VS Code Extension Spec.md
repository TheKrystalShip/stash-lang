# LSP Agent Tools — VS Code Extension Spec

> **Status:** Backlog
> **Created:** 2026-04-16
> **Purpose:** Design a VS Code extension that exposes LSP (Language Server Protocol) capabilities as Language Model Tools, enabling AI agents to query type information, navigate symbol hierarchies, discover code structure, and apply code actions through the language server.

---

## Table of Contents

1. [Motivation](#1-motivation)
2. [Design Philosophy](#2-design-philosophy)
3. [What This Is NOT](#3-what-this-is-not)
4. [Existing Agent Tools — Gap Analysis](#4-existing-agent-tools--gap-analysis)
5. [Architecture Overview](#5-architecture-overview)
6. [Tool Inventory](#6-tool-inventory)
7. [Tool Specifications](#7-tool-specifications)
8. [Output Formatting Strategy](#8-output-formatting-strategy)
9. [Position Resolution — The Abstraction Layer](#9-position-resolution--the-abstraction-layer)
10. [Interaction with Language Servers](#10-interaction-with-language-servers)
11. [Cross-Platform Considerations](#11-cross-platform-considerations)
12. [Error Handling](#12-error-handling)
13. [Implementation Roadmap](#13-implementation-roadmap)
14. [Test Scenarios](#14-test-scenarios)
15. [Design Decisions & Alternatives](#15-design-decisions--alternatives)
16. [Open Questions](#16-open-questions)

---

## 1. Motivation

AI coding agents navigating unfamiliar codebases currently rely on three strategies: reading files (slow, high-token), text search (no semantic understanding), and natural language search (approximate). Meanwhile, the language server sitting right there in VS Code already _knows_ the codebase: every type, every reference, every call chain, every doc comment. That knowledge is locked behind UI interactions — hover your mouse, click "Go to Definition," right-click "Find All References."

Agents already have two tools that tap into this: `vscode_listCodeUsages` (references/definitions) and `vscode_renameSymbol` (rename). These prove the concept — but they cover a fraction of what LSPs provide. The remaining gaps are costly:

1. **"What type is this variable?"** — An agent reading `let result = processData(input)` doesn't know the return type. It has to find and read `processData`'s definition. The hover provider already computed this; the agent just can't access it.

2. **"What's in this file?"** — To understand a file's structure, the agent reads the whole thing (hundreds of lines, hundreds of tokens). The document symbol provider already built a clean tree of functions, classes, and methods. The agent can't see it.

3. **"Who calls this function?"** — Before changing a function's signature, the agent needs to know all callers. `vscode_listCodeUsages` gives references, but call hierarchy gives _directed call relationships_ — incoming callers and outgoing callees — which is materially more useful for impact analysis.

4. **"What quick fixes are available?"** — When the agent sees a diagnostic, it could ask the language server for its suggested fixes instead of inventing its own. Many LSPs provide high-quality auto-fixes (add import, fix typo, extract method) that the agent should leverage rather than reinvent.

5. **"What functions match this name pattern?"** — Workspace symbol search finds symbols by name across the entire project, semantically — not text matching, but actual symbol resolution. This finds `MyService` even if it's re-exported under a different module name.

These are not theoretical gaps. Every time an agent reads 200 lines to find a return type that hover would give in one call, that's wasted tokens and latency. Every time an agent uses grep to find function boundaries that document symbols would give structurally, that's fragile parsing that breaks on edge cases.

---

## 2. Design Philosophy

### 2.1 Symbol-Based, Not Position-Based

The underlying VS Code commands (`vscode.executeHoverProvider`, `vscode.executeDefinitionProvider`, etc.) all take `[Uri, Position]` — a file and a cursor position. This is fine for UI interactions but terrible for AI agents, who think in terms of _symbols_ and _file paths_, not line/column coordinates.

Every tool in this spec accepts **symbol names** and/or **file paths** and resolves positions internally. This matches the pattern established by `vscode_listCodeUsages` and `vscode_renameSymbol`.

### 2.2 Token-Efficient Output

LSP responses are rich, structured objects with fields that are irrelevant to agents (edit ranges, sort keys, filter text, etc.). Every tool serializes its output into a compact, human-readable text format that maximizes information per token. JSON structures are used only when hierarchy matters.

### 2.3 Graceful Degradation

Not every language server implements every feature. A tool that calls `executeHoverProvider` on a file whose LSP doesn't support hover should return a clear "not supported" message, not crash. The extension queries capabilities where possible and falls back gracefully.

### 2.4 Language-Agnostic

Like the debug agent tools, these tools work with **any** language server registered in VS Code. Stash, TypeScript, Python, C#, Rust — if there's an LSP, the tools work. Stash's LSP is the primary validation target.

### 2.5 Complementary, Not Competing

These tools complement existing agent capabilities — they don't replace `grep_search`, `read_file`, or `semantic_search`. Each has its strength:

| Need                                | Best Tool                        |
| ----------------------------------- | -------------------------------- |
| "Find exact text across files"      | `grep_search`                    |
| "Read 50 lines of a file"           | `read_file`                      |
| "Find code relevant to a concept"   | `semantic_search`                |
| "What type is `x`?"                 | **`lsp_hover`** (new)            |
| "What functions are in this file?"  | **`lsp_documentSymbols`** (new)  |
| "Who calls `processData`?"          | **`lsp_callHierarchy`** (new)    |
| "Where is `MyStruct` defined/used?" | `vscode_listCodeUsages` (exists) |
| "Rename `foo` to `bar` everywhere"  | `vscode_renameSymbol` (exists)   |

---

## 3. What This Is NOT

- **Not a replacement for reading files.** When the agent needs to understand logic flow, it reads the code. These tools help it navigate _to_ the right code faster.
- **Not a replacement for grep/semantic search.** Text search and fuzzy search remain the right tools for many queries. These tools add _type-aware_ and _structure-aware_ queries.
- **Not an LSP client.** The extension doesn't speak LSP directly. It uses VS Code's `vscode.commands.executeCommand('vscode.executeHoverProvider', ...)` API, which delegates to whatever language server is active.
- **Not language-specific.** No Stash-specific code. Works with any language server.

---

## 4. Existing Agent Tools — Gap Analysis

### What Agents Already Have

| Agent Tool              | LSP Feature Used                           | Strength                                                                | Limitation                                              |
| ----------------------- | ------------------------------------------ | ----------------------------------------------------------------------- | ------------------------------------------------------- |
| `vscode_listCodeUsages` | References + Definitions + Implementations | Combined view of where a symbol is defined, referenced, and implemented | No type information, no doc comments, no call direction |
| `vscode_renameSymbol`   | Rename                                     | Semantics-aware rename across workspace                                 | Write-only — no read capability                         |
| `get_errors`            | Diagnostics                                | Compile/lint errors for files                                           | No suggested fixes, no code actions                     |
| `semantic_search`       | (not LSP)                                  | Natural language code search                                            | Approximate, not type-aware                             |
| `grep_search`           | (not LSP)                                  | Exact text matching                                                     | No semantic understanding                               |
| `read_file`             | (not LSP)                                  | Read raw file contents                                                  | High-token, no structure                                |
| `file_search`           | (not LSP)                                  | Find files by pattern                                                   | No symbol awareness                                     |

### What's Missing

| Gap                              | Impact                                                          | How Often Agents Hit This                                |
| -------------------------------- | --------------------------------------------------------------- | -------------------------------------------------------- |
| **Type information for symbols** | Agent guesses types, reads wrong files, makes incorrect edits   | Very frequently — every interaction with unfamiliar code |
| **File structure overview**      | Agent reads entire files to find function boundaries            | Frequently — every time it opens a new file              |
| **Call graph navigation**        | Agent uses text search to find callers, misses indirect calls   | Frequently — before any refactoring                      |
| **Code action discovery**        | Agent invents fixes instead of using LSP-suggested ones         | Moderately — when handling diagnostics                   |
| **Workspace-wide symbol search** | Agent relies on text search which misses re-exports and aliases | Moderately — when navigating large projects              |
| **Function signature lookup**    | Agent reads function source to find parameter types             | Moderately — when generating function calls              |

---

## 5. Architecture Overview

```
┌─────────────────────────────────────────────┐
│  AI Agent (Copilot / Custom Chat Mode)      │
│  - Navigating codebase                      │
│  - Understanding types and structure        │
│  - Planning refactors                       │
└─────────────┬───────────────────────────────┘
              │ LanguageModelTool invocations
              ▼
┌─────────────────────────────────────────────┐
│  lsp-agent-tools extension                  │
│                                             │
│  ┌─────────────────────────────────────┐    │
│  │  Tool Registry                      │    │
│  │  - 7 registered tools               │    │
│  │  - Input validation                 │    │
│  │  - Position resolution              │    │
│  └──────────────┬──────────────────────┘    │
│                 │                           │
│  ┌──────────────▼──────────────────────┐    │
│  │  Position Resolver                  │    │
│  │  - Symbol name → (Uri, Position)    │    │
│  │  - Line content matching            │    │
│  │  - Document text scanning           │    │
│  └──────────────┬──────────────────────┘    │
│                 │                           │
│  ┌──────────────▼──────────────────────┐    │
│  │  Output Formatter                   │    │
│  │  - LSP types → compact text         │    │
│  │  - Size budget enforcement          │    │
│  │  - Hierarchy flattening             │    │
│  └──────────────┬──────────────────────┘    │
│                 │                           │
│  ┌──────────────▼──────────────────────┐    │
│  │  VS Code Command Bridge             │    │
│  │  - vscode.executeHoverProvider      │    │
│  │  - vscode.executeDocumentSymbol...  │    │
│  │  - vscode.prepareCallHierarchy      │    │
│  │  - vscode.executeCodeActionProvider │    │
│  │  - vscode.executeWorkspaceSymbol... │    │
│  │  - vscode.executeSignatureHelp...   │    │
│  └─────────────────────────────────────┘    │
└─────────────────────────────────────────────┘
              │
              ▼
┌─────────────────────────────────────────────┐
│  Language Server (any LSP server)           │
│  e.g. stash-lsp, tsserver, pylsp, ...       │
└─────────────────────────────────────────────┘
```

### 5.1 Extension Identity

| Property     | Value                                |
| ------------ | ------------------------------------ |
| Extension ID | `lsp-agent-tools`                    |
| Display Name | LSP Agent Tools                      |
| Activation   | `onStartupFinished`                  |
| Dependencies | None (uses only stable VS Code APIs) |

### 5.2 Key Internal Components

**PositionResolver** — Converts symbol-based tool inputs (symbol name + file path + optional line content) into `[Uri, Position]` pairs that the `vscode.execute*Provider` commands require. Uses document text scanning with word-boundary matching. Falls back to line content matching when symbol names are ambiguous.

**OutputFormatter** — Serializes LSP response types (`Hover`, `DocumentSymbol[]`, `CallHierarchyItem[]`, etc.) into compact, agent-readable text. Enforces token budgets. Strips irrelevant metadata.

---

## 6. Tool Inventory

Seven tools, organized by purpose:

| #   | Tool Name              | Wraps                                      | Purpose                                                                   | Read/Write |
| --- | ---------------------- | ------------------------------------------ | ------------------------------------------------------------------------- | ---------- |
| 1   | `lsp_hover`            | `vscode.executeHoverProvider`              | Get type info, doc comments, signatures for a symbol                      | Read       |
| 2   | `lsp_documentSymbols`  | `vscode.executeDocumentSymbolProvider`     | Get structured outline of a file (functions, classes, methods, variables) | Read       |
| 3   | `lsp_callHierarchy`    | `prepareCallHierarchy` + incoming/outgoing | Get incoming callers and outgoing callees for a function                  | Read       |
| 4   | `lsp_codeActions`      | `vscode.executeCodeActionProvider`         | Query available quick fixes and refactorings at a location                | Read       |
| 5   | `lsp_workspaceSymbols` | `vscode.executeWorkspaceSymbolProvider`    | Search for symbols by name across the entire workspace                    | Read       |
| 6   | `lsp_signatureHelp`    | `vscode.executeSignatureHelpProvider`      | Get function parameter info at a call site                                | Read       |
| 7   | `lsp_typeDefinition`   | `vscode.executeTypeDefinitionProvider`     | Navigate from a variable/expression to its type's definition              | Read       |

All tools are **read-only** — they query LSP information without modifying any files. No confirmation dialogs needed.

### 6.1 Why Not More Tools?

**Rejected: `lsp_definition`** — Already covered by `vscode_listCodeUsages`, which returns definitions, references, and implementations in one call. Adding a separate definition tool would be pure duplication.

**Rejected: `lsp_references`** — Same reason. `vscode_listCodeUsages` already does this.

**Rejected: `lsp_rename`** — Already exists as `vscode_renameSymbol`.

**Rejected: `lsp_diagnostics`** — Already exists as `get_errors`.

**Rejected: `lsp_completion`** — Completion providers return hundreds of items per position. The output is too noisy to be useful as a tool. Agents generate code directly; they don't need a completion menu.

**Rejected: `lsp_format`** — VS Code already auto-formats on save (configurable). Adding a tool would duplicate this, and agents can use `editor.action.formatDocument` via commands if needed.

**Rejected: `lsp_documentHighlights`** — Same-file occurrence highlighting. `grep_search` with `includePattern` covers this use case.

**Rejected: `lsp_semanticTokens`** — Low-level token classification. Agents don't need to know that `foo` is token type 5 (variable) — they can read the code.

**Rejected: `lsp_inlayHints`** — Inferred types and parameter names. Valuable information, but `lsp_hover` provides the same type information in richer form.

**Rejected: `lsp_typeHierarchy`** — Useful for deeply OOP codebases but Stash doesn't have class inheritance (only interface conformance). Added complexity without proportional value. Can be added later if demanded.

---

## 7. Tool Specifications

### 7.1 `lsp_hover`

Get type information, documentation, and signatures for a symbol at a specific location.

**Why this matters:** This is the single highest-value tool. Every time an agent reads 50 lines of a file to figure out what type a variable is, `lsp_hover` would have answered the question in one call. Language servers compute rich hover content: type signatures, doc comments, parameter descriptions, inferred types, enum variants, namespace documentation.

**Input Schema:**

```json
{
  "type": "object",
  "properties": {
    "filePath": {
      "type": "string",
      "description": "Absolute path or workspace-relative path to the file"
    },
    "symbol": {
      "type": "string",
      "description": "Name of the symbol to hover over"
    },
    "lineContent": {
      "type": "string",
      "description": "A substring of the line where the symbol appears. Used to locate the exact position."
    },
    "line": {
      "type": "integer",
      "description": "1-based line number where the symbol appears. Optional — used when symbol appears multiple times."
    }
  },
  "required": ["filePath", "symbol"]
}
```

**Position Resolution:**

1. If `lineContent` is provided, scan the document for a line containing `lineContent`, then find `symbol` within that line.
2. If only `line` is provided, find `symbol` on that line.
3. If neither, find the first occurrence of `symbol` in the file.

**Behavior:**

1. Resolve `(Uri, Position)` from inputs.
2. Execute `vscode.executeHoverProvider(uri, position)`.
3. The provider returns `Hover[]` — typically one hover with `contents: MarkdownString[]`.
4. Extract and format the hover content.

**Output (example for Stash):**

````
## Hover: `loadConfig`

```stash
fn loadConfig(path: string): dict | null
````

Loads a configuration file from the given path.

**Parameters:**

- `path` — Absolute or relative path to the config file

**Returns:** Parsed config as a dictionary, or `null` if the file doesn't exist.

**Defined in:** config_manager.stash:15

```

**Output (example for TypeScript):**
```

## Hover: `result`

```typescript
const result: Promise<Response>;
```

(local variable) Inferred from `fetch(url)`.

````

**Confirmation:** None — read-only operation.

---

### 7.2 `lsp_documentSymbols`

Get a structured outline of all symbols in a file.

**Why this matters:** Agents frequently need to understand "what's in this file?" before diving into details. Currently they read the whole file (expensive) or use grep to find function definitions (fragile). Document symbols gives a clean, hierarchical tree: classes contain methods, modules contain functions, etc.

**Input Schema:**
```json
{
  "type": "object",
  "properties": {
    "filePath": {
      "type": "string",
      "description": "Absolute path or workspace-relative path to the file"
    },
    "kind": {
      "type": "string",
      "enum": ["all", "function", "class", "method", "variable", "constant", "struct", "enum", "interface", "namespace", "property"],
      "description": "Filter by symbol kind. Default: 'all'."
    }
  },
  "required": ["filePath"]
}
````

**Behavior:**

1. Execute `vscode.executeDocumentSymbolProvider(uri)`.
2. Returns `DocumentSymbol[]` — a hierarchical tree of symbols.
3. Flatten or preserve hierarchy based on the language server's response.
4. Filter by `kind` if specified.
5. Format into compact outline.

**Output (example):**

```
## Symbols in deploy.stash (14 symbols)

  fn main() — line 1-45
  fn loadConfig(path: string) — line 47-62
  fn validateConfig(config: dict) — line 64-89
    fn checkPort(port: int) — line 70-75 [nested]
    fn checkHost(host: string) — line 77-85 [nested]
  struct DeployTarget — line 91-97
    property host: string — line 92
    property port: int — line 93
    property user: string — line 94
    property keyPath: string — line 95
    property sudo: bool — line 96
  fn deploy(target: DeployTarget, artifact: string) — line 99-142
  enum DeployStatus — line 144-149
    member SUCCESS — line 145
    member FAILED — line 146
    member TIMEOUT — line 147
    member ROLLBACK — line 148
  fn rollback(target: DeployTarget) — line 151-180
```

**Size Budget:** Maximum 100 symbols. If the file has more, truncate with `"...(+N more symbols)"`. This cap handles the monster files that exist in real projects.

---

### 7.3 `lsp_callHierarchy`

Get the incoming callers and/or outgoing callees of a function.

**Why this matters:** Before modifying a function's signature or behavior, an agent needs to understand the impact. `vscode_listCodeUsages` returns _references_ — every mention of the symbol. Call hierarchy returns _directed call relationships_ — who calls this function, and what functions does it call. This is a fundamentally different (and more useful) query for refactoring.

**Input Schema:**

```json
{
  "type": "object",
  "properties": {
    "filePath": {
      "type": "string",
      "description": "Absolute path or workspace-relative path to the file"
    },
    "symbol": {
      "type": "string",
      "description": "Name of the function/method"
    },
    "lineContent": {
      "type": "string",
      "description": "A substring of the line where the symbol is defined or called"
    },
    "direction": {
      "type": "string",
      "enum": ["incoming", "outgoing", "both"],
      "description": "Which direction to traverse. 'incoming' = who calls this, 'outgoing' = what this calls. Default: 'both'."
    },
    "depth": {
      "type": "integer",
      "minimum": 1,
      "maximum": 3,
      "description": "How many levels deep to traverse. Default: 1."
    }
  },
  "required": ["filePath", "symbol"]
}
```

**Behavior:**

1. Resolve `(Uri, Position)` from inputs.
2. Execute `vscode.commands.executeCommand('vscode.prepareCallHierarchy', uri, position)` → `CallHierarchyItem[]`.
3. If `direction` includes `"incoming"`: execute `vscode.commands.executeCommand('vscode.provideIncomingCalls', item)` for each item.
4. If `direction` includes `"outgoing"`: execute `vscode.commands.executeCommand('vscode.provideOutgoingCalls', item)` for each item.
5. If `depth > 1`, recurse on the results (up to `depth` levels).
6. Format into a tree.

**Output (example):**

```
## Call Hierarchy: `validateConfig`

### Incoming (who calls validateConfig):
  main() — deploy.stash:12
    └─ called at deploy.stash:12, col 15
  reloadConfig() — config_manager.stash:45
    └─ called at config_manager.stash:52, col 8

### Outgoing (what validateConfig calls):
  checkPort() — deploy.stash:70
    └─ called at deploy.stash:72, col 5
  checkHost() — deploy.stash:77
    └─ called at deploy.stash:78, col 5
  str.startsWith() — (built-in)
    └─ called at deploy.stash:68, col 12
```

**Size Budget:** Maximum 50 items per direction per level. If exceeded, truncate with count.

**Depth cap rationale:** Depth 3 means "callers of callers of callers" which can explode combinatorially. Depth 1 covers the 95% use case; depth 2 covers impact-of-impact; depth 3 is the reasonable maximum.

---

### 7.4 `lsp_codeActions`

Query available code actions (quick fixes, refactorings) at a specific location or for a specific diagnostic.

**Why this matters:** When an agent encounters a diagnostic from `get_errors`, it currently invents its own fix. But the language server often has a high-quality fix already computed — "add missing import," "change type to match," "did you mean `forEach`?" The agent should check for LSP-suggested fixes before writing its own.

**Input Schema:**

```json
{
  "type": "object",
  "properties": {
    "filePath": {
      "type": "string",
      "description": "Absolute path or workspace-relative path to the file"
    },
    "line": {
      "type": "integer",
      "description": "1-based line number to query code actions at"
    },
    "diagnosticMessage": {
      "type": "string",
      "description": "Filter to code actions associated with a specific diagnostic message (substring match)"
    },
    "kind": {
      "type": "string",
      "enum": ["quickfix", "refactor", "source", "all"],
      "description": "Filter by code action kind. Default: 'all'."
    },
    "apply": {
      "type": "string",
      "description": "Title of a specific code action to apply. If provided, the action is applied instead of just listed."
    }
  },
  "required": ["filePath", "line"]
}
```

**Behavior (query mode — `apply` not set):**

1. Build a `Range` covering the entire line.
2. If `diagnosticMessage` is provided, filter `vscode.languages.getDiagnostics(uri)` to matching diagnostics and use their ranges.
3. Execute `vscode.commands.executeCommand('vscode.executeCodeActionProvider', uri, range, kind)`.
4. Filter results by `kind` if specified.
5. Format action titles and descriptions.

**Behavior (apply mode — `apply` is set):**

1. Query actions as above.
2. Find the action whose `title` matches `apply` (case-insensitive substring).
3. If the action has a `WorkspaceEdit`, apply it via `vscode.workspace.applyEdit()`.
4. If the action has a `Command`, execute it via `vscode.commands.executeCommand()`.
5. Return the applied action's description and the files that were modified.

**Output (query mode):**

```
## Code Actions at deploy.stash:47

### Quick Fixes:
  1. "Import 'loadConfig' from './config_manager'" — quickfix
  2. "Change 'conifg' to 'config'" — quickfix (spelling)

### Refactorings:
  3. "Extract to function" — refactor.extract
  4. "Convert to arrow function" — refactor.rewrite
```

**Output (apply mode):**

```
## Applied: "Import 'loadConfig' from './config_manager'"

Modified files:
  - deploy.stash (added import at line 3)
```

**Confirmation:** Apply mode requires user confirmation: "Apply code action: '{title}'? This will modify {file}."

---

### 7.5 `lsp_workspaceSymbols`

Search for symbols by name across the entire workspace.

**Why this matters:** `grep_search` finds text; `semantic_search` finds concepts; `file_search` finds files. None of them find _symbols as the language server understands them_. Workspace symbol search finds `MyService` whether it's defined as a class, function, struct, or re-exported alias — and returns its kind, location, and container.

**Input Schema:**

```json
{
  "type": "object",
  "properties": {
    "query": {
      "type": "string",
      "description": "Symbol name to search for. Supports partial matching (e.g., 'Config' matches 'loadConfig', 'ConfigManager', 'parseConfig')."
    },
    "kind": {
      "type": "string",
      "enum": [
        "all",
        "function",
        "class",
        "method",
        "variable",
        "constant",
        "struct",
        "enum",
        "interface",
        "namespace",
        "property"
      ],
      "description": "Filter by symbol kind. Default: 'all'."
    },
    "maxResults": {
      "type": "integer",
      "minimum": 1,
      "maximum": 50,
      "description": "Maximum results to return. Default: 20."
    }
  },
  "required": ["query"]
}
```

**Behavior:**

1. Execute `vscode.commands.executeCommand('vscode.executeWorkspaceSymbolProvider', query)`.
2. Returns `SymbolInformation[]` with name, kind, location, container.
3. Filter by `kind` if specified.
4. Sort by relevance (exact matches first, then prefix matches, then substring matches).
5. Truncate to `maxResults`.

**Output:**

```
## Workspace Symbols matching "Config" (8 results)

  struct DeployConfig — deploy.stash:15 (in module deploy)
  fn loadConfig(path: string) — config_manager.stash:47 (in module config_manager)
  fn parseConfig(content: string) — config_manager.stash:89 (in module config_manager)
  fn validateConfig(config: dict) — deploy.stash:64 (in module deploy)
  struct ConfigError — config_manager.stash:12 (in module config_manager)
  enum ConfigFormat — config_manager.stash:5 (in module config_manager)
  fn reloadConfig() — config_manager.stash:120 (in module config_manager)
  const DEFAULT_CONFIG_PATH — config_manager.stash:3 (in module config_manager)
```

---

### 7.6 `lsp_signatureHelp`

Get function parameter information at a call site.

**Why this matters:** When an agent needs to call a function, it needs to know the parameter names, types, and order. Currently it reads the function's definition. Signature help provides this information directly, including which parameter is "active" at the cursor position — matching the tooltip you see when typing a function call in the editor.

**Input Schema:**

```json
{
  "type": "object",
  "properties": {
    "filePath": {
      "type": "string",
      "description": "Absolute path or workspace-relative path to the file"
    },
    "symbol": {
      "type": "string",
      "description": "Name of the function being called"
    },
    "lineContent": {
      "type": "string",
      "description": "A substring of the line where the function call appears"
    }
  },
  "required": ["filePath", "symbol"]
}
```

**Behavior:**

1. Resolve `(Uri, Position)` — position should be inside the function call's argument list (after the opening parenthesis).
2. Execute `vscode.commands.executeCommand('vscode.executeSignatureHelpProvider', uri, position)`.
3. Returns `SignatureHelp` with signatures, active signature, active parameter.
4. Format into readable output.

**Output:**

```
## Signature: `deploy`

  fn deploy(target: DeployTarget, artifact: string, options?: DeployOptions): DeployStatus

  Parameters:
    1. target: DeployTarget — The deployment target configuration
    2. artifact: string — Path to the artifact to deploy
    3. options?: DeployOptions — Optional deployment options (timeout, retries, dryRun)

  Returns: DeployStatus
```

---

### 7.7 `lsp_typeDefinition`

Navigate from a variable or expression to the definition of its type.

**Why this matters:** `vscode_listCodeUsages` takes you to where a _symbol_ is defined. Type definition takes you to where a symbol's _type_ is defined. Given `let target: DeployTarget = ...`, usage lookup finds where `target` is declared; type definition finds where `DeployTarget` is defined. These answer fundamentally different questions.

**Input Schema:**

```json
{
  "type": "object",
  "properties": {
    "filePath": {
      "type": "string",
      "description": "Absolute path or workspace-relative path to the file"
    },
    "symbol": {
      "type": "string",
      "description": "Name of the variable/expression whose type you want to find"
    },
    "lineContent": {
      "type": "string",
      "description": "A substring of the line where the symbol appears"
    }
  },
  "required": ["filePath", "symbol"]
}
```

**Behavior:**

1. Resolve `(Uri, Position)` from inputs.
2. Execute `vscode.commands.executeCommand('vscode.executeTypeDefinitionProvider', uri, position)`.
3. Returns `Location[]` or `LocationLink[]` — locations where the type is defined.
4. For each location, read the first few lines to provide context.

**Output:**

```
## Type Definition of `target`

  Type: DeployTarget
  Defined at: deploy.stash:91-97

  struct DeployTarget {
      host: string;
      port: int;
      user: string;
      keyPath: string;
      sudo: bool;
  }
```

**Fallback:** If the language server doesn't support type definition, attempt `lsp_hover` on the same symbol — hover content usually includes type information.

---

## 8. Output Formatting Strategy

### 8.1 Principles

1. **Markdown over JSON.** Agents process markdown more naturally than deeply nested JSON. Use headers, lists, and code blocks.
2. **Location as context.** Always include `file:line` so the agent can navigate. Use workspace-relative paths.
3. **Code snippets.** When a tool returns a location, include 1–3 lines of source context so the agent doesn't need a separate `read_file` call.
4. **Counts and truncation.** Always show total counts. When truncating, say `"...(+N more)"` so the agent knows there's more.

### 8.2 Size Budgets

| Tool                   | Default Budget                    | Rationale                        |
| ---------------------- | --------------------------------- | -------------------------------- |
| `lsp_hover`            | ~500 tokens                       | Hover is usually compact         |
| `lsp_documentSymbols`  | ~2000 tokens (100 symbols)        | File outlines can be large       |
| `lsp_callHierarchy`    | ~1500 tokens (50 items/direction) | Call trees can explode           |
| `lsp_codeActions`      | ~500 tokens                       | Usually few actions per location |
| `lsp_workspaceSymbols` | ~1000 tokens (20 results)         | Search results are compact       |
| `lsp_signatureHelp`    | ~300 tokens                       | Single function signature        |
| `lsp_typeDefinition`   | ~500 tokens                       | Single type definition           |

These budgets are enforced by truncation, not by error. The tool always returns _something_ useful within the budget.

---

## 9. Position Resolution — The Abstraction Layer

The hardest part of this extension is converting agent-friendly inputs (symbol name, file path, optional line content) into the `[Uri, Position]` pairs that VS Code commands require.

### 9.1 Resolution Algorithm

```
Input: { filePath, symbol, lineContent?, line? }

1. Open the document: vscode.workspace.openTextDocument(filePath)
2. If lineContent is provided:
   a. Scan document lines for one containing lineContent (substring match)
   b. Within the matched line, find the symbol (word-boundary match)
   c. Position = (matchedLine, symbolStartColumn)
3. Else if line is provided:
   a. Go to the specified line
   b. Find symbol within that line (word-boundary match)
   c. Position = (line - 1, symbolStartColumn)  // convert 1-based to 0-based
4. Else (only filePath + symbol):
   a. Scan entire document for first occurrence of symbol (word-boundary match)
   b. Position = (firstMatchLine, firstMatchColumn)
5. If symbol not found → return error
```

### 9.2 Ambiguity Handling

When `symbol` appears multiple times and `lineContent`/`line` aren't provided:

- For `lsp_hover`: Use first occurrence. Hover content is usually the same regardless of occurrence.
- For `lsp_callHierarchy`: Use the _definition_ occurrence (typically the function declaration, not a call site). Try to find a line where the symbol is at the start of a function-like pattern.
- For `lsp_typeDefinition`: Use first occurrence. The type is the same at any occurrence.

### 9.3 Pattern Matching Rules

The `symbol` parameter is matched with **word boundaries** to avoid false positives:

- `symbol: "config"` matches `config` but not `configuration` or `loadConfig`
- `symbol: "loadConfig"` matches `loadConfig` but not `Config` or `reloadConfig`
- Word boundaries: start/end of line, whitespace, punctuation, operators

---

## 10. Interaction with Language Servers

### 10.1 Capability Detection

Not all language servers support all features. Before calling a provider, the extension should gracefully handle the case where no provider is registered.

| Tool                   | Required LSP Capability   | Detection                                                 |
| ---------------------- | ------------------------- | --------------------------------------------------------- |
| `lsp_hover`            | `hoverProvider`           | Check if `executeHoverProvider` returns results           |
| `lsp_documentSymbols`  | `documentSymbolProvider`  | Check if `executeDocumentSymbolProvider` returns results  |
| `lsp_callHierarchy`    | `callHierarchyProvider`   | Check if `prepareCallHierarchy` returns results           |
| `lsp_codeActions`      | `codeActionProvider`      | Check if `executeCodeActionProvider` returns results      |
| `lsp_workspaceSymbols` | `workspaceSymbolProvider` | Check if `executeWorkspaceSymbolProvider` returns results |
| `lsp_signatureHelp`    | `signatureHelpProvider`   | Check if `executeSignatureHelpProvider` returns results   |
| `lsp_typeDefinition`   | `typeDefinitionProvider`  | Check if `executeTypeDefinitionProvider` returns results  |

**Detection strategy:** The `vscode.execute*Provider` commands return empty arrays or `undefined` when no provider is registered. The extension checks for this and returns a clear message: `"No hover information available. The language server for this file type may not support hover."`.

### 10.2 Language Server Startup Timing

Language servers start lazily — often not until the first file of that type is opened. If a tool is invoked before the language server is ready:

1. Opening the document via `vscode.workspace.openTextDocument(filePath)` triggers server startup.
2. The extension waits briefly (up to 5s) for the server to initialize.
3. If the server still isn't ready, return: `"Language server for '{language}' is starting. Try again in a moment."`

### 10.3 Stash LSP Specifics

While the extension is language-agnostic, the Stash LSP is the primary validation target. Stash-specific LSP features that map well to these tools:

| Tool                   | Stash LSP Handler                                                 | Quality                                                  |
| ---------------------- | ----------------------------------------------------------------- | -------------------------------------------------------- |
| `lsp_hover`            | Hover: symbols, namespace functions, constants, struct fields     | Rich — includes signatures, doc comments, namespace info |
| `lsp_documentSymbols`  | Document Symbols: functions, structs, enums, constants, variables | Complete tree with nesting                               |
| `lsp_callHierarchy`    | Call Hierarchy (incoming + outgoing)                              | Fully implemented                                        |
| `lsp_codeActions`      | Code Actions: "Did you mean?", import suggestions                 | Partial — growing                                        |
| `lsp_workspaceSymbols` | Workspace Symbols                                                 | Implemented                                              |
| `lsp_signatureHelp`    | Signature Help: parameter names and types                         | Implemented                                              |
| `lsp_typeDefinition`   | Go to Type Definition                                             | Implemented                                              |

---

## 11. Cross-Platform Considerations

### 11.1 File Paths

Same as the debug tools spec: OS-native paths in tool inputs, workspace-relative paths with forward slashes in outputs. `vscode.Uri.file()` handles normalization.

### 11.2 Language Server Availability

Platform-dependent language servers (e.g., servers that require native binaries) may not be available on all platforms. This is not the extension's problem — it delegates to whatever VS Code has. If no server is available, the tool reports it cleanly.

### 11.3 Encoding

Document text is read via `vscode.workspace.openTextDocument()` which handles encoding. No manual encoding work needed.

---

## 12. Error Handling

### 12.1 Error Categories

| Error                     | Response                                                                                                                                          |
| ------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------- |
| Symbol not found in file  | `{ "error": "symbol_not_found", "message": "Symbol '{symbol}' not found in {file}. Check the symbol name and file path." }`                       |
| No language server        | `{ "error": "no_provider", "message": "No language server provides {feature} for {language} files. Install an appropriate language extension." }` |
| Language server not ready | `{ "error": "server_starting", "message": "Language server is initializing. Try again in a few seconds." }`                                       |
| File not found            | `{ "error": "file_not_found", "message": "File '{filePath}' not found in the workspace." }`                                                       |
| Empty result              | Not an error — return `"No {feature} found for '{symbol}'."`                                                                                      |
| Language server crash     | `{ "error": "provider_error", "message": "Language server returned an error: {details}" }`                                                        |

### 12.2 Empty vs. Missing

Important distinction:

- **Empty result** (provider exists, returned nothing): `"No hover information available for 'foo'. The symbol may not have type information."` — This is informational, not an error.
- **Missing provider** (no server registered for this language): `"No language server provides hover for '.xyz' files."` — This is actionable — the user can install an extension.

---

## 13. Implementation Roadmap

### Phase 1: Navigation Essentials

The tools that most directly reduce agent token consumption and improve accuracy:

1. `lsp_hover` — Type info and doc comments
2. `lsp_documentSymbols` — File structure overview
3. `lsp_typeDefinition` — Navigate to type definitions

**Validation:** Agent can hover over a symbol, get its type, navigate to the type's definition, and understand a file's structure — all without reading entire files.

### Phase 2: Call Graph & Search

The tools that enable impact analysis and semantic navigation:

4. `lsp_callHierarchy` — Incoming and outgoing calls
5. `lsp_workspaceSymbols` — Semantic symbol search

**Validation:** Agent can answer "who calls this function?" and "find all structs with 'Config' in the name" using LSP data.

### Phase 3: Actions & Signatures

The tools that help with code modification:

6. `lsp_codeActions` — Query and apply quick fixes
7. `lsp_signatureHelp` — Function parameter info

**Validation:** Agent can discover and apply LSP-suggested fixes, and look up function signatures before generating calls.

### Package Structure

```
lsp-agent-tools/
├── package.json
├── tsconfig.json
├── src/
│   ├── extension.ts           — Activation, tool registration
│   ├── tools/
│   │   ├── hover.ts           — lsp_hover tool
│   │   ├── documentSymbols.ts — lsp_documentSymbols tool
│   │   ├── callHierarchy.ts   — lsp_callHierarchy tool
│   │   ├── codeActions.ts     — lsp_codeActions tool
│   │   ├── workspaceSymbols.ts— lsp_workspaceSymbols tool
│   │   ├── signatureHelp.ts   — lsp_signatureHelp tool
│   │   └── typeDefinition.ts  — lsp_typeDefinition tool
│   ├── positionResolver.ts    — Symbol → Position resolution
│   ├── outputFormatter.ts     — LSP types → compact text
│   └── types.ts               — Shared type definitions
├── test/
│   └── ...
└── README.md
```

---

## 14. Test Scenarios

### 14.1 Happy Path

| #   | Scenario                    | Steps                                                            | Expected                                      |
| --- | --------------------------- | ---------------------------------------------------------------- | --------------------------------------------- |
| 1   | Hover on function           | `lsp_hover(file, "loadConfig")`                                  | Returns signature + doc comment               |
| 2   | Hover on variable           | `lsp_hover(file, "port", lineContent: "let port = config.port")` | Returns inferred type (`int`)                 |
| 3   | Hover on namespace function | `lsp_hover(file, "arr.map")`                                     | Returns built-in function signature           |
| 4   | Document symbols            | `lsp_documentSymbols(file)`                                      | Returns tree of all functions, structs, enums |
| 5   | Document symbols filtered   | `lsp_documentSymbols(file, kind: "function")`                    | Returns only functions                        |
| 6   | Call hierarchy incoming     | `lsp_callHierarchy(file, "validate", direction: "incoming")`     | Returns all callers                           |
| 7   | Call hierarchy outgoing     | `lsp_callHierarchy(file, "validate", direction: "outgoing")`     | Returns all callees                           |
| 8   | Call hierarchy both         | `lsp_callHierarchy(file, "validate", direction: "both")`         | Returns callers and callees                   |
| 9   | Code actions for diagnostic | `lsp_codeActions(file, line: 47)`                                | Returns available quick fixes                 |
| 10  | Apply code action           | `lsp_codeActions(file, line: 47, apply: "Import 'loadConfig'")`  | Applies the import fix                        |
| 11  | Workspace symbol search     | `lsp_workspaceSymbols(query: "Config")`                          | Returns all symbols matching "Config"         |
| 12  | Workspace symbols filtered  | `lsp_workspaceSymbols(query: "Config", kind: "struct")`          | Returns only structs                          |
| 13  | Signature help              | `lsp_signatureHelp(file, "deploy")`                              | Returns parameter names/types                 |
| 14  | Type definition             | `lsp_typeDefinition(file, "target")`                             | Returns `DeployTarget` struct definition      |

### 14.2 Edge Cases

| #   | Scenario                                   | Expected                                                 |
| --- | ------------------------------------------ | -------------------------------------------------------- |
| 15  | Hover on keyword (`if`, `fn`)              | Returns language keyword info or empty                   |
| 16  | Symbol appears multiple times              | Uses first occurrence (or `lineContent`-specified one)   |
| 17  | File with no language server               | Returns `no_provider` message                            |
| 18  | Very large file (1000+ symbols)            | Truncated to 100 symbols with count                      |
| 19  | Call hierarchy on function with no callers | Returns empty incoming list                              |
| 20  | Deeply nested call hierarchy (depth: 3)    | Returns 3 levels, capped                                 |
| 21  | Workspace symbol search with no results    | Returns "No symbols found matching '{query}'"            |
| 22  | Code actions on line with no diagnostics   | Returns empty or refactoring-only actions                |
| 23  | Apply non-existent code action             | Returns error: "No code action matching '{title}' found" |
| 24  | Type definition for primitive type         | Returns empty or language-built-in info                  |
| 25  | Language server still starting             | Returns `server_starting` message                        |

### 14.3 Multi-Language Validation

| #   | Language         | Tool                  | Expected Behavior                            |
| --- | ---------------- | --------------------- | -------------------------------------------- |
| 26  | Stash (.stash)   | All tools             | Full coverage — primary validation target    |
| 27  | TypeScript (.ts) | `lsp_hover`           | Returns TS type information from tsserver    |
| 28  | Python (.py)     | `lsp_documentSymbols` | Returns functions/classes from Pylance/pylsp |
| 29  | C# (.cs)         | `lsp_callHierarchy`   | Returns call graph from OmniSharp/Roslyn     |
| 30  | Markdown (.md)   | `lsp_hover`           | Returns `no_provider` or empty               |

---

## 15. Design Decisions & Alternatives

### Decision 1: Seven Focused Tools vs. One Swiss-Army Tool

**Chosen:** Seven separate tools, one per LSP feature.

**Alternatives Considered:**

- **Single `lsp_query` tool** with a `feature` parameter: `lsp_query(feature: "hover", file, symbol)`. One tool, many modes.
- **Three composite tools:** `lsp_inspect` (hover + type def + signature), `lsp_navigate` (symbols + call hierarchy), `lsp_fix` (code actions).

**Rationale:**

- LLMs perform better with focused tools that have clear, distinct purposes. A swiss-army tool with a `feature` discriminator adds cognitive overhead.
- Separate tools allow each to have a schema optimized for its specific inputs (call hierarchy needs `direction`/`depth`; code actions need `diagnosticMessage`/`apply`; hover just needs a symbol).
- The tool inventory (7) is manageable. Agent tool lists commonly have 10-20 tools.
- Composite tools would couple unrelated features — an agent that wants hover doesn't need call hierarchy in the same response.

**Risk:** Seven tools is a lot to add. Mitigated by phased implementation — Phase 1 is only 3 tools.

---

### Decision 2: Symbol-Based Input vs. Position-Based Input

**Chosen:** Symbol-based (symbol name + file + optional line content)

**Alternatives Considered:**

- **Raw position-based:** `{ file, line, column }` — direct mapping to LSP.
- **Hybrid:** Accept either symbol or position.

**Rationale:**

- Agents think in symbols, not cursor positions. When an agent says "I want to know the type of `config`," it knows the name, not that it's at line 47, column 12.
- This matches the pattern established by `vscode_listCodeUsages` and `vscode_renameSymbol`, which both accept `symbol` + `lineContent`.
- Position-based would be more precise but less natural. The position resolver handles the translation.
- Hybrid adds complexity. If agents occasionally need raw positions, they can construct `lineContent` that pins the exact occurrence.

**Risk:** Position resolution might be ambiguous when a symbol appears many times. Mitigated by `lineContent` parameter for disambiguation and by word-boundary matching.

---

### Decision 3: Standalone Extension vs. Extend Stash Extension

**Chosen:** Standalone extension

**Alternatives Considered:**

- **Add tools to the stash-lang extension** directly.

**Rationale:**

- Same reasoning as the debug tools spec: these tools are language-agnostic. Embedding them in the Stash extension makes them unavailable for TypeScript, Python, etc.
- Separation of concerns: stash-lang provides language features; lsp-agent-tools provides agent tools.
- Independent release cycle.

**Risk:** Two more extensions to install (alongside debug-agent-tools). Mitigated by potential extension pack bundling.

---

### Decision 4: `lsp_codeActions` Apply Mode — Tool or Command?

**Chosen:** The same tool queries _and_ applies actions (controlled by the `apply` parameter).

**Alternatives Considered:**

- **Separate tool:** `lsp_applyCodeAction` for applying.
- **Query only:** Tool only lists actions; agent applies edits itself.

**Rationale:**

- Two-step (query then apply in same tool) reduces round-trips. The common workflow is: query actions → pick one → apply it.
- Having the agent apply edits itself (by reading the WorkspaceEdit and making changes via `replace_string_in_file`) would be fragile and error-prone.
- The `apply` parameter makes it explicit when the tool has side effects. Without `apply`, the tool is read-only.

**Risk:** Applying code actions is a write operation that could produce unexpected results. Mitigated by user confirmation dialog for apply mode.

---

### Decision 5: Include `lsp_signatureHelp` vs. Rely on `lsp_hover`

**Chosen:** Include both.

**Alternatives Considered:**

- **Drop `lsp_signatureHelp`** since hover already shows signatures.

**Rationale:**

- Hover shows the _definition_ signature — what the function looks like where it's declared.
- Signature help shows the _call-site_ context — which parameter you're currently filling, including overloads.
- For languages with overloaded functions (TypeScript, C#), signature help shows all overloads and highlights the active one based on argument count. Hover shows the primary signature.
- For Stash (no overloads), the overlap is higher. But the tool is trivial to implement and useful for multi-language support.

**Risk:** Minimal. Signature help is a thin wrapper; the implementation cost is low.

---

### Decision 6: Markdown Output vs. JSON Output

**Chosen:** Markdown-formatted text in `LanguageModelToolResult`.

**Alternatives Considered:**

- **Structured JSON** with `LanguageModelDataPart.json()`
- **Mix:** JSON for structured data (symbols list), markdown for prose (hover content)

**Rationale:**

- Agents process markdown naturally. Headers, lists, and code blocks map directly to how agents structure their reasoning.
- JSON would be more machine-parseable but LLMs don't parse JSON — they read it as text anyway. Compact markdown is more token-efficient than verbose JSON with field names.
- The `LanguageModelToolResult` accepts `LanguageModelTextPart` which renders as text — markdown formatting is preserved and displayed.
- Consistency: hover content from LSPs is already markdown. We'd be converting markdown to JSON and back.

**Risk:** If a future agent architecture wants to compose tool results programmatically, markdown is harder to parse than JSON. Acceptable trade-off for current agent architectures.

---

## 16. Open Questions

### Q1: Relationship to `vscode_listCodeUsages`

`vscode_listCodeUsages` already wraps references + definitions + implementations. Should `lsp_hover` or `lsp_typeDefinition` return the same location information, or deliberately _not_ include it to avoid redundancy?

**Answer:** Include location info (file + line) but not the full reference list. The tools serve different purposes: `vscode_listCodeUsages` answers "where is this used?" while `lsp_hover` answers "what type is this?" and `lsp_typeDefinition` answers "where is this type defined?"

### Q2: Tool Naming Convention

Should tools use `lsp_` prefix or `code_` prefix? The `lsp_` prefix is technically accurate but leaks the implementation detail that these are backed by LSP. Agents don't care about LSP — they care about code navigation. `code_hover`, `code_outline`, `code_callers` might be more intuitive.

**Answer:** `lsp_` prefix. It's precise, it groups all the tools in tool lists, and it signals to the agent that these tools depend on a language server being available (which sets correct expectations when the server is missing).

### Q3: Bundling with Debug Agent Tools

Should this be a separate extension from `debug-agent-tools`, or should both be combined into a single `agent-tools` extension?

**Answer:** Separate for now. Different feature areas, different dependencies (debug API vs. language API), different test surfaces. Can be bundled into an extension pack later.

### Q4: `lsp_codeActions` — Scope Creep Risk

Code actions can be anything from "add missing import" to "extract entire class into new file." Should the apply mode have a complexity limit? For example, refuse to apply actions that modify more than N files or more than N lines?

**Leaning:** No hard limit, but the confirmation dialog should clearly show the scope: "This action will modify 3 files (12 total edits)." Let the user decide.

### Q5: Caching

Should the extension cache LSP responses? Document symbols for a file don't change unless the file changes. Caching could reduce LSP round-trips when the agent calls multiple tools on the same file.

**Answer:** No caching in v1. Language servers already cache internally. Adding an extension-level cache introduces staleness risk. If performance becomes an issue, add caching with document version invalidation.

### Q6: `lsp_hover` on Multiple Symbols

Should `lsp_hover` accept multiple symbols in one call? An agent might want to hover over three variables at once to compare types.

**Answer:** No. Keep the tool simple. The agent can make three calls. Batching adds input/output complexity disproportionate to the performance gain.
