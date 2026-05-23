# LSP Agent Tools — Bug Fixes & Quality Audit

> **Status:** Backlog
> **Created:** 2026-04-16
> **Parent Spec:** `LSP Agent Tools — VS Code Extension Spec.md` (in `3-review/`)
> **Purpose:** Catalog and specify fixes for bugs discovered during agent-driven QA testing of the `lsp-agent-tools` VS Code extension across two language servers (Stash LSP, Roslyn/C#).

---

## Table of Contents

1. [Testing Methodology](#1-testing-methodology)
2. [Bug Inventory](#2-bug-inventory)
3. [Bug Details & Fixes](#3-bug-details--fixes)
4. [Regression Test Plan](#4-regression-test-plan)
5. [Decision Log](#5-decision-log)

---

## 1. Testing Methodology

Each of the 7 tools was tested against two files with live LSPs:

| File                                  | Language Server    | Symbols Tested                                                                               |
| ------------------------------------- | ------------------ | -------------------------------------------------------------------------------------------- |
| `examples/deploy.stash`               | Stash LSP (custom) | enums, structs, functions, constants, variables, namespace builtins, struct methods, imports |
| `Stash.Bytecode/VM/VirtualMachine.cs` | Roslyn (OmniSharp) | class, partial class, fields, properties, methods, constants, nested structs, BCL types      |

Results were cross-checked against `vscode_listCodeUsages`, `grep_search`, and `read_file` to verify correctness.

---

## 2. Bug Inventory

Five bugs are in the **tool wrapper** (the `lsp-agent-tools` extension code). These are language-agnostic — they affect any LSP.

| #   | Tool                   | Severity     | Summary                                                                                        |
| --- | ---------------------- | ------------ | ---------------------------------------------------------------------------------------------- |
| B1  | `lsp_callHierarchy`    | **Critical** | Completely broken for C#/Roslyn — returns "no information" for every symbol                    |
| B2  | `lsp_codeActions`      | **High**     | `kind` filter returns zero results even when unfiltered query returns matching actions         |
| B3  | `lsp_documentSymbols`  | **Medium**   | `kind: variable` filter breaks parent hierarchy — children nest under wrong parent             |
| B4  | `lsp_workspaceSymbols` | **Low**      | Markdown prose indexed as `SymbolKind.String` pollutes results without kind filter             |
| B5  | `lsp_typeDefinition`   | **Info**     | Fallback to hover produces inconsistent output header (`## Hover:` vs `## Type Definition of`) |

---

## 3. Bug Details & Fixes

### B1 — `lsp_callHierarchy`: Broken for C#/Roslyn (Critical)

**Symptom:** Every call to `lsp_callHierarchy` on a C# file returns `"No call hierarchy information available for '{symbol}'."` — including obvious symbols like `Execute`, `PushFrame`, `GrowStack` that have many callers across partial class files.

**Works for:** Stash LSP (partially — finds `check_host` called by `deploy_to`, but misses top-level callers).

**Root Cause Analysis:**

The tool resolves position via `positionResolver.ts`, then calls:

```typescript
const rootItems = await vscode.commands.executeCommand<
  vscode.CallHierarchyItem[]
>("vscode.prepareCallHierarchy", resolved.uri, resolved.position);
```

The issue is likely in **position resolution for method definitions in C#**. When `findSymbolInLine` scans for `Execute` on line 206:

```csharp
    public object? Execute(Chunk chunk)
```

The word-boundary regex `\bExecute\b` should match. But consider that `resolvePosition` with no `lineContent` falls through to a full document scan and finds the **first** occurrence. For C# partial classes, the first occurrence of `Execute` might be in a `using` directive or comment, landing on a position where `prepareCallHierarchy` returns nothing.

**However**, testing confirmed the bug persists even when `lineContent: "public object? Execute(Chunk chunk)"` is provided — so position resolution isn't the sole issue. The `prepareCallHierarchy` command may require the cursor to be on the **method name identifier token specifically** — not just on a line containing it. If `findSymbolInLine` lands on the wrong column (e.g., a different `Execute` token in that line, though unlikely), the LSP might not recognize it as a call hierarchy item.

**More likely root cause:** Roslyn's `prepareCallHierarchy` may return `undefined` (not an empty array) when it doesn't have results ready, and the `?? []` fallback doesn't catch `undefined` from the command result type. Or the command name might need to be `vscode.prepareCallHierarchy` with a different invocation pattern for C# (the OmniSharp extension may register under a different command name or require a specific `CallHierarchyPrepareRequest` approach).

**Investigation Steps:**

1. Add diagnostic logging to `callHierarchy.ts` to capture the exact `resolved.position` (line, column) being passed to `prepareCallHierarchy`.
2. Test whether `vscode.prepareCallHierarchy` works at all from the extension host for C# by hardcoding a known-good position.
3. Check if Roslyn requires a `textDocument/prepareCallHierarchy` LSP request rather than the generic VS Code command — some LSP features are registered through `DocumentSelector` and may not respond to the generic command when the cursor position doesn't fall on a recognized token.
4. Verify that the resolved position's column lands on the method name itself (e.g., column 19 for `Execute` in `    public object? Execute(Chunk chunk)`) and not on a different token.

**Fix Strategy:**

The position resolver finds `Execute` via `\bExecute\b` regex — this should land at the correct column. The bug is more likely one of:

- **A:** The `uri` being passed is wrong (e.g., it's passing the file URI but Roslyn's call hierarchy provider expects a specific document version or scheme).
- **B:** The command returns `undefined` and the `!rootItems || rootItems.length === 0` check doesn't distinguish between "no provider" and "provider returned empty."
- **C:** Roslyn needs the document to be open and visible (not just `openTextDocument`'d) before `prepareCallHierarchy` works.

Proposed fix approach:

```typescript
// After resolvePosition, ensure the document is opened as a text editor
// (some LSP features require an active editor, not just an opened document)
await vscode.window.showTextDocument(resolved.document, {
  preview: true,
  preserveFocus: true,
  viewColumn: vscode.ViewColumn.Active,
});

// Wait briefly for LSP to catch up
await new Promise((r) => setTimeout(r, 100));

const rootItems = await vscode.commands.executeCommand<
  vscode.CallHierarchyItem[]
>("vscode.prepareCallHierarchy", resolved.uri, resolved.position);
```

If this doesn't work, try the raw LSP request approach:

```typescript
// Use the language-specific call hierarchy command
const provider = vscode.languages.getLanguages();
// Or call the Roslyn-specific command if it exists
```

**Acceptance Criteria:**

- `lsp_callHierarchy(file: "VirtualMachine.cs", symbol: "PushFrame")` returns 11+ incoming callers across partial class files.
- `lsp_callHierarchy(file: "VirtualMachine.cs", symbol: "Execute", direction: "outgoing")` returns `PushFrame`, `InitGlobalSlots`, `RunDebug`, `Run` as outgoing calls.
- `lsp_callHierarchy(file: "deploy.stash", symbol: "deploy_to", direction: "incoming")` returns the top-level call at line 140.

---

### B2 — `lsp_codeActions`: Kind filter returns empty results (High)

**Symptom:** Querying with `kind: "refactor"` at a line that has refactoring actions (confirmed by the same query without `kind`) returns `"No code actions available"`.

**Observed on:** Both Stash and C# — confirmed as tool wrapper bug.

**Root Cause:**

In `codeActions.ts`, the `queryActions` function passes the kind to `executeCodeActionProvider`:

```typescript
const kindStr = codeActionKind?.value; // e.g., "refactor"

const actions = await vscode.commands.executeCommand<vscode.CodeAction[]>(
  "vscode.executeCodeActionProvider",
  uri,
  range,
  kindStr, // <── Passed as a string
);
```

The `vscode.executeCodeActionProvider` command's third argument is the **kind filter** — but it expects a `string` that's used for _prefix matching_ by the LSP protocol. The problem is that when a `kindStr` is provided, VS Code already filters the results server-side. But then the code **also** applies a client-side filter:

```typescript
if (codeActionKind) {
  return results.filter((a) => a.kind && a.kind.contains(codeActionKind));
}
```

The `a.kind.contains(codeActionKind)` call checks if the action's kind **contains** (is a parent of) the filter kind. This is backwards — `refactor.rewrite.copilot.contains(refactor)` should be `true`, but the VS Code `CodeActionKind.contains()` method checks if the **receiver** is a parent-or-equal of the argument. So `vscode.CodeActionKind.Refactor.contains(action.kind)` would work, but `action.kind.contains(vscode.CodeActionKind.Refactor)` is asking "does `refactor.rewrite.copilot` contain `refactor`?" which returns `false` because `contains` means "is this kind a sub-kind of the argument."

Actually, re-reading the VS Code API: `CodeActionKind.contains(other)` returns `true` if `other` is a sub-kind of `this`. So `Refactor.contains(Refactor.Rewrite)` is `true`, but `Refactor.Rewrite.contains(Refactor)` is `false`.

The filter should be:

```typescript
if (codeActionKind) {
  return results.filter((a) => a.kind && codeActionKind.contains(a.kind));
}
```

But there's a **second issue**: when `kindStr` is passed to `executeCodeActionProvider`, VS Code may already filter server-side. Some providers may return nothing when a kind filter is passed (if they don't understand prefix matching). The double-filter (server + client) means:

1. Server filters to kind prefix → returns subset
2. Client filters again with inverted `contains` → returns empty

**Fix:**

1. Remove the server-side kind filter (pass `undefined` as the third arg always) to avoid double-filtering.
2. Fix the client-side filter direction:

```typescript
async function queryActions(
  uri: vscode.Uri,
  document: vscode.TextDocument,
  input: CodeActionsInput,
): Promise<vscode.CodeAction[]> {
  const lineIndex = input.line - 1;
  let range: vscode.Range;

  if (input.diagnosticMessage) {
    const diags = vscode.languages.getDiagnostics(uri);
    const needle = input.diagnosticMessage.toLowerCase();
    const match = diags.find((d) => d.message.toLowerCase().includes(needle));
    range = match ? match.range : document.lineAt(lineIndex).range;
  } else {
    range = document.lineAt(lineIndex).range;
  }

  // Don't pass kind to the command — do client-side filtering only
  const actions = await vscode.commands.executeCommand<vscode.CodeAction[]>(
    "vscode.executeCodeActionProvider",
    uri,
    range,
  );

  const results = actions ?? [];
  const codeActionKind = resolveKind(input.kind);

  if (codeActionKind) {
    // Fix: codeActionKind.contains(action.kind) — "is this action a sub-kind of the filter?"
    return results.filter((a) => a.kind && codeActionKind.contains(a.kind));
  }

  return results;
}
```

**Acceptance Criteria:**

- `lsp_codeActions(file: "VirtualMachine.cs", line: 44, kind: "refactor")` returns all 13 refactoring actions.
- `lsp_codeActions(file: "VirtualMachine.cs", line: 44, kind: "quickfix")` returns quick fixes (if any diagnostics exist).
- `lsp_codeActions(file: "deploy.stash", line: 10, kind: "refactor")` returns refactoring actions.

---

### B3 — `lsp_documentSymbols`: Kind filter breaks child nesting (Medium)

**Symptom:** When using `kind: "variable"` on `deploy.stash`, function parameters (`host`, `target`, `package`) are shown nested under the `targets` variable instead of under their respective parent functions.

**Root Cause:**

In `outputFormatter.ts`, `renderDocumentSymbol` renders children regardless of whether the parent was included by the filter:

```typescript
function renderDocumentSymbol(
  symbol: vscode.DocumentSymbol,
  kindFilter: Set<vscode.SymbolKind> | undefined,
  indent: string,
  collected: string[],
  count: { value: number },
): void {
  const include = !kindFilter || kindFilter.has(symbol.kind);
  if (include) {
    // ... render this symbol at current indent
    count.value++;
  }

  // Always recurse into children — but indent doesn't increase when parent was skipped
  for (const child of symbol.children) {
    renderDocumentSymbol(child, kindFilter, indent + "  ", collected, count);
  }
}
```

When the parent (e.g., `fn deploy_to`) is excluded by the filter, its children (parameters) still render — but the indent context is wrong because the indent was computed assuming the parent rendered. The children appear at the wrong nesting level and visually "attach" to whatever the previous rendered symbol was.

**Fix:**

When a parent is excluded by the kind filter, still recurse into children but **don't increase indent**:

```typescript
function renderDocumentSymbol(
  symbol: vscode.DocumentSymbol,
  kindFilter: Set<vscode.SymbolKind> | undefined,
  indent: string,
  collected: string[],
  count: { value: number },
): void {
  const include = !kindFilter || kindFilter.has(symbol.kind);
  if (include) {
    if (count.value >= MAX_SYMBOLS) {
      return;
    }
    const kind = symbolKindToString(symbol.kind);
    const detail = symbol.detail ? `: ${symbol.detail}` : "";
    const startLine = symbol.range.start.line + 1;
    const endLine = symbol.range.end.line + 1;
    const lineRange =
      startLine === endLine
        ? `line ${startLine}`
        : `line ${startLine}-${endLine}`;
    collected.push(`${indent}${kind} ${symbol.name}${detail} — ${lineRange}`);
    count.value++;
  }

  // Only increase indent if this symbol was rendered
  const childIndent = include ? indent + "  " : indent;
  for (const child of symbol.children) {
    renderDocumentSymbol(child, kindFilter, childIndent, collected, count);
  }
}
```

**Acceptance Criteria:**

- `lsp_documentSymbols(file: "deploy.stash", kind: "variable")` shows `host` (parameter of `check_host`) and `target`/`package` (parameters of `deploy_to`) at the same indent level as `targets`, `results`, etc. — not nested under `targets`.

---

### B4 — `lsp_workspaceSymbols`: Markdown string noise (Low)

**Symptom:** Searching for "Target" without a `kind` filter returns 83 results, 60+ of which are `string` type matches from `.kanban/` and `.md` files containing the word "target" in prose.

**Root Cause:**

The Stash LSP workspace symbol provider indexes markdown/spec files and returns their headings and content as `SymbolKind.String`. The tool wrapper doesn't filter these out — it shows everything the LSP returns.

**Note:** This is partially a Stash LSP issue (see Stash LSP bug spec, S4). But the tool wrapper should also be resilient.

**Fix:**

Add a default exclusion for `SymbolKind.String`, `SymbolKind.Number`, `SymbolKind.Boolean`, and `SymbolKind.Null` in workspace symbol results unless the user explicitly requests them. These kinds are almost never what an agent is looking for.

```typescript
const NON_CODE_KINDS = new Set([
  vscode.SymbolKind.String,
  vscode.SymbolKind.Number,
  vscode.SymbolKind.Boolean,
  vscode.SymbolKind.Null,
]);

// In WorkspaceSymbolsTool.invoke():
let filtered = symbols ?? [];
if (input.kind !== undefined) {
  const kindFilter = parseKindFilter(input.kind);
  if (kindFilter !== undefined) {
    filtered = filtered.filter((s) => kindFilter.has(s.kind));
  }
} else {
  // Default: exclude non-code symbol kinds
  filtered = filtered.filter((s) => !NON_CODE_KINDS.has(s.kind));
}
```

**Acceptance Criteria:**

- `lsp_workspaceSymbols(query: "Target")` without kind filter returns only code symbols (structs, functions, variables, fields) — not markdown headings.
- `lsp_workspaceSymbols(query: "Target", kind: "all")` still returns everything including strings (explicit opt-in).

> **Revision:** This means the `kind: "all"` filter needs to be handled specially — it should mean "all including non-code" while the default (no `kind`) means "code symbols only."

---

### B5 — `lsp_typeDefinition`: Inconsistent fallback header (Info)

**Symptom:** When `executeTypeDefinitionProvider` returns nothing (e.g., for `results: array`, `elapsed: int`), the tool falls back to `formatHover()`, which produces a `## Hover: \`symbol\``header — inconsistent with the expected`## Type Definition of \`symbol\`` header.

**Root Cause:**

In `typeDefinition.ts`, the fallback path calls `formatHover(hovers, input.symbol)` directly, which uses its own header format.

**Fix:**

Wrap the fallback to use the type definition header:

```typescript
// Fallback: hover usually includes type information
const hovers = await vscode.commands.executeCommand<vscode.Hover[]>(
  "vscode.executeHoverProvider",
  resolved.uri,
  resolved.position,
);

if (hovers && hovers.length > 0) {
  // Use hover content but with type-definition framing
  const hoverText = formatHover(hovers, input.symbol);
  // Replace the "## Hover:" header with "## Type Definition of" for consistency
  const reframed = hoverText.replace(
    `## Hover: \`${input.symbol}\``,
    `## Type Definition of \`${input.symbol}\` (from hover)`,
  );
  return new vscode.LanguageModelToolResult([
    new vscode.LanguageModelTextPart(reframed),
  ]);
}
```

The `(from hover)` suffix signals to the agent that this came from a fallback path, not a true type definition navigation.

**Acceptance Criteria:**

- `lsp_typeDefinition(file: "deploy.stash", symbol: "results")` returns `## Type Definition of \`results\` (from hover)`— not`## Hover: \`results\``.

---

## 4. Regression Test Plan

### Automated Tests

Each bug fix should include at least one test case. The extension's test suite should cover:

| Test ID | Bug | Scenario                                                       | Expected                                   |
| ------- | --- | -------------------------------------------------------------- | ------------------------------------------ |
| T1      | B1  | Call hierarchy on C# method with known callers                 | Returns incoming callers                   |
| T2      | B1  | Call hierarchy on Stash function called at top level           | Returns incoming caller                    |
| T3      | B2  | Code actions with `kind: "refactor"` on line with refactorings | Returns matching actions                   |
| T4      | B2  | Code actions with `kind: "quickfix"` on line with diagnostics  | Returns matching quick fixes               |
| T5      | B3  | Document symbols `kind: "variable"` with nested params         | Params don't nest under unrelated variable |
| T6      | B4  | Workspace symbols without kind filter                          | No `SymbolKind.String` results             |
| T7      | B4  | Workspace symbols with explicit `kind: "all"`                  | Includes all kinds                         |
| T8      | B5  | Type definition fallback to hover                              | Header says "Type Definition of"           |

### Manual QA Protocol

After fixes, re-run the same battery of tests that discovered the bugs:

1. Open `examples/deploy.stash` with Stash LSP running.
2. Open `Stash.Bytecode/VM/VirtualMachine.cs` with Roslyn running.
3. For each tool, run the same queries from the audit and verify results match expectations.

---

## 5. Decision Log

### D1: Server-side vs. client-side kind filtering for code actions

**Decision:** Remove server-side kind filter, do client-side only.

**Rationale:** The `executeCodeActionProvider` command's kind parameter has inconsistent behavior across LSPs — some filter server-side, others ignore it. Client-side filtering with correct `contains()` direction is predictable and works for all LSPs.

**Risk:** Slightly more data transferred from LSP server (all actions instead of filtered subset). Negligible for the typical number of code actions per line.

### D2: Default workspace symbol exclusion for non-code kinds

**Decision:** Exclude `String`, `Number`, `Boolean`, `Null` symbol kinds by default. Allow explicit opt-in via `kind: "all"`.

**Rationale:** Agents searching for symbols almost never want markdown headings or JSON string values. The noise significantly degrades the tool's usefulness.

**Risk:** An agent explicitly searching for string constants might miss them. Mitigated by the `kind: "all"` override.

### D3: Call hierarchy — investigate before committing to a fix approach

**Decision:** B1 requires investigation before implementation. The exact root cause is unclear — it could be position resolution, document visibility requirements, or a Roslyn-specific command registration issue.

**Rationale:** Guessing the fix would risk introducing regressions. The investigation steps are clear and scoped.
