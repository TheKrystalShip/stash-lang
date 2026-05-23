# Stash LSP — Bug Fixes from Agent Tool Audit

> **Status:** Backlog
> **Created:** 2026-04-16
> **Related:** `LSP Agent Tools — Bug Fixes & Quality Audit.md` (in `0-backlog/`)
> **Purpose:** Catalog and specify fixes for bugs in the Stash LSP server discovered during agent-driven QA testing of the `lsp-agent-tools` extension. These are server-side issues — they affect the LSP protocol responses regardless of which client consumes them.

---

## Table of Contents

1. [Bug Inventory](#1-bug-inventory)
2. [Bug Details & Fixes](#2-bug-details--fixes)
3. [Test Plan](#3-test-plan)
4. [Decision Log](#4-decision-log)

---

## 1. Bug Inventory

| #   | Handler           | Severity   | Summary                                                                                                |
| --- | ----------------- | ---------- | ------------------------------------------------------------------------------------------------------ |
| S1  | Call Hierarchy    | **High**   | Top-level (module-scope) call sites silently dropped from incoming callers                             |
| S2  | Hover             | **High**   | Namespace function calls (`arr.push`, `io.println`) return "namespace X" instead of function signature |
| S3  | Workspace Symbols | **Medium** | Ghost duplicate results from `git:`-scheme URIs in analysis cache                                      |
| S4  | Workspace Symbols | **Low**    | Non-Stash files (markdown, specs) pollute results via `GetAllCachedUris()`                             |
| S5  | Call Hierarchy    | **Low**    | Outgoing calls miss imported module function calls (`utils.log`)                                       |

---

## 2. Bug Details & Fixes

### S1 — Call Hierarchy: Top-level call sites dropped (High)

**Symptom:** `deploy_to()` is called at line 140 of `deploy.stash` (at module scope, not inside any function), but `lsp_callHierarchy(symbol: "deploy_to", direction: "incoming")` reports `(none)` for incoming callers.

**Root Cause:** In [CallHierarchyHandler.cs](Stash.Lsp/Handlers/CallHierarchyHandler.cs), the incoming calls handler at ~line 119-154 groups call references by their enclosing function via `FindEnclosingFunction()`. When a call site is at module scope (top-level code outside any `fn` declaration), `FindEnclosingFunction()` returns `null`, and that call reference is silently discarded.

The relevant logic:

```csharp
// ~line 138-145 in CallHierarchyHandler.cs
var enclosingFn = FindEnclosingFunction(allSymbols, r.Span.StartLine, r.Span.StartColumn);
if (enclosingFn == null)
{
    continue;  // ← BUG: silently drops top-level call sites
}
```

**Why this matters:** In Stash, top-level code is the primary execution entry point. Scripts like `deploy.stash` have most of their orchestration logic at module scope — function calls there are the main call sites that an agent needs to discover.

**Fix:**

When `enclosingFn` is `null`, represent the caller as a synthetic `<module>` or `<top-level>` item rather than discarding it. The call hierarchy LSP protocol allows any `CallHierarchyItem` as a caller — it doesn't require it to be a function.

```csharp
var enclosingFn = FindEnclosingFunction(allSymbols, r.Span.StartLine, r.Span.StartColumn);

// Use the module file itself as the enclosing "function" for top-level calls
string callerName;
SourceSpan callerSpan;
if (enclosingFn != null)
{
    callerName = enclosingFn.Name;
    callerSpan = enclosingFn.FullSpan ?? enclosingFn.Span;
}
else
{
    callerName = "<module>";
    callerSpan = new SourceSpan(0, 0, document.LineCount - 1, 0); // Entire file
}
```

The `<module>` name follows the convention used in Python's LSP and other scripting language servers that have top-level executable code.

**Acceptance Criteria:**

- `deploy_to` incoming callers includes `<module>` at `deploy.stash:140`.
- `check_host` incoming callers still includes `deploy_to` at `deploy.stash:87`.
- Functions called only from other functions (not top-level) are unaffected.

---

### S2 — Hover: Namespace functions return "namespace X" instead of signature (High)

**Symptom:** Hovering over `arr.push(results, status)` returns:

```
namespace arr
```

Instead of the expected:

```
fn arr.push(array: array, value)
```

Meanwhile, `lsp_signatureHelp` on the same call correctly returns the full function signature. This means the LSP has the information — hover just doesn't access it.

**Root Cause:** In [HoverHandler.cs](Stash.Lsp/Handlers/HoverHandler.cs), the hover resolution for dotted expressions follows a three-tier fallback:

1. **Tier 1 (~line 105-116):** Calls `ResolveNamespaceMember()` to look up the member in imported module symbols. For built-in namespaces (`arr`, `dict`, `str`, etc.), this resolves the **namespace itself** rather than the specific function member, because built-in namespaces aren't imported as module symbols with individual function entries.

2. **Tier 2 (~line 135-153):** Falls back to `StdlibRegistry.TryGetNamespaceFunction()` which correctly looks up the built-in function. **But this path only runs if Tier 1 returned null.** When Tier 1 returns a Namespace symbol (instead of null), Tier 2 is never reached.

The issue is that Tier 1's `ResolveNamespaceMember()` returns a non-null result (the namespace itself) for built-in namespace prefixes like `arr`, which short-circuits the lookup before the stdlib registry is consulted.

**Fix:**

Two options:

**Option A (Preferred):** In the Tier 1 handler, when `ResolveNamespaceMember()` returns a symbol with `Kind == SymbolKind.Namespace`, don't treat it as a successful resolution — fall through to Tier 2:

```csharp
var resolved = ResolveNamespaceMember(prefix, member, analysisResult);
if (resolved != null && resolved.Kind != StashSymbolKind.Namespace)
{
    // Only use Tier 1 result if it resolved to an actual function/variable, not a namespace
    return FormatHover(resolved);
}

// Fall through to Tier 2: stdlib registry
```

**Option B:** Reorder the tiers — check `StdlibRegistry.TryGetNamespaceFunction()` first for dotted expressions where the prefix matches a known built-in namespace, then fall back to `ResolveNamespaceMember()` for custom module imports.

Option A is simpler and maintains the existing precedence for custom module members (which should shadow built-in namespaces if there's a conflict).

**Acceptance Criteria:**

- Hovering over `arr.push` returns `fn arr.push(array: array, value)` with parameter info.
- Hovering over `arr.filter` returns `fn arr.filter(array: array, fn: function)`.
- Hovering over `io.println` returns the `io.println` function signature.
- Hovering over `str.padEnd` returns `fn str.padEnd(s: string, width: int, padChar: string) -> string`.
- Custom module imports (`utils.log`) still resolve correctly via Tier 1.

---

### S3 — Workspace Symbols: Ghost duplicates from git-scheme URIs (Medium)

**Symptom:** Every Stash symbol appears twice in workspace symbol results — once from the real file path (e.g., `examples/deploy.stash:10`) and once from a ghost path (e.g., `deploy.stash.git:10`). This wastes result slots and confuses agents.

Not observed with C#/Roslyn — confirming this is Stash LSP-specific.

**Root Cause:** The workspace symbol handler in [WorkspaceSymbolHandler.cs](Stash.Lsp/Handlers/WorkspaceSymbolHandler.cs) at ~line 80-84 collects URIs from two sources:

```csharp
var uris = new HashSet<Uri>(_documents.GetOpenDocumentUris());
foreach (var cachedUri in _analysis.GetAllCachedUris())
{
    uris.Add(cachedUri);
}
```

`GetAllCachedUris()` returns all URIs in the analysis engine cache without filtering. When VS Code has a file open, it may also open a `git:`-scheme version of the same file (for diff views, source control, etc.). If the LSP client sends `textDocument/didOpen` for git-scheme URIs, the analysis engine caches them, and they appear as separate documents.

**Fix:**

Filter `GetAllCachedUris()` results to only include `file:`-scheme URIs:

```csharp
var uris = new HashSet<Uri>(_documents.GetOpenDocumentUris()
    .Where(u => u.Scheme == "file"));
foreach (var cachedUri in _analysis.GetAllCachedUris())
{
    if (cachedUri.Scheme == "file")
    {
        uris.Add(cachedUri);
    }
}
```

Alternatively, the filtering could be done in `GetAllCachedUris()` itself — any URI that isn't `file://` is a virtual document that shouldn't appear in workspace-wide queries.

**Acceptance Criteria:**

- `lsp_workspaceSymbols(query: "DeployResult")` returns exactly 1 result per symbol, not 2.
- Git-scheme URIs are excluded from workspace symbol results.
- Opening a diff view for a `.stash` file doesn't introduce phantom duplicates.

---

### S4 — Workspace Symbols: Non-Stash files pollute results (Low)

**Symptom:** Searching for "Target" returns `SymbolKind.String` matches from `.kanban/` markdown files and `.md` docs. These are prose headings, not code symbols.

**Root Cause:** Same URI collection logic as S3. If markdown files are ever opened or analyzed by the LSP, their content gets cached and indexed. The [WorkspaceScanner.cs](Stash.Lsp/Analysis/WorkspaceScanner.cs) at ~line 162-184 only queues `.stash` files for background scanning, but any document opened by the client (including `.md` files) triggers analysis caching via `textDocument/didOpen`.

When the analysis engine processes a non-Stash file, it may extract headings/strings as symbols (likely from a fallback or error-tolerant parsing path).

**Fix:**

In `WorkspaceSymbolHandler`, filter to `.stash` file extensions:

```csharp
foreach (var cachedUri in _analysis.GetAllCachedUris())
{
    if (cachedUri.Scheme == "file" && cachedUri.AbsolutePath.EndsWith(".stash", StringComparison.OrdinalIgnoreCase))
    {
        uris.Add(cachedUri);
    }
}
```

This is a stricter filter than S3 and subsumes it. If both are fixed, this filter handles both issues.

> **Note:** This won't affect the LSP's ability to provide diagnostics, hover, etc. for open non-Stash files — those features work on the active document, not the workspace symbol index.

**Acceptance Criteria:**

- `lsp_workspaceSymbols(query: "Target")` returns only results from `.stash` files.
- No `SymbolKind.String` results from `.md` or `.kanban/` files.
- The LSP still provides diagnostics/hover for non-`.stash` files if opened.

---

### S5 — Call Hierarchy: Outgoing calls miss imported function calls (Low)

**Symptom:** `deploy_to`'s outgoing calls show only `check_host()` but miss 6 calls to `utils.log()` — a function from an imported module (`import "lib/utils.stash" as utils`).

**Root Cause:** The outgoing calls handler in [CallHierarchyHandler.cs](Stash.Lsp/Handlers/CallHierarchyHandler.cs) at ~line 156-182 filters call references using `IsInsideSpan(bodySpan, r.Span.StartLine, r.Span.StartColumn)`. This correctly scopes to calls within the function body. However, the analysis engine's reference tracking may not classify dotted namespace calls (`utils.log(...)`) as `ReferenceKind.Call` — it may track them differently (as member access + call, or as two separate references: one for `utils` and one for `log`).

The `check_host()` call is a simple identifier call, which is cleanly tracked as `ReferenceKind.Call`. The `utils.log()` call involves a member access expression, which the reference tracker may decompose differently.

**Investigation Required:**

Before fixing, verify:

1. What `ReferenceKind` does the analysis engine assign to `utils.log(...)` calls?
2. Does the reference point to `utils` (the namespace) or `log` (the function)?
3. If it points to `log`, does the outgoing call filter correctly resolve the target function's definition location?

**Likely Fix:**

If the analysis engine tracks `utils.log` as a member access on `utils` rather than a call to `log`:

- Add dotted call resolution to the outgoing calls handler: when a call reference targets a member of an imported module, resolve the member function and include it as an outgoing call.

**Acceptance Criteria:**

- `deploy_to` outgoing calls include `utils.log` (6 calls) in addition to `check_host`.
- Outgoing calls for namespace built-in calls (e.g., `arr.push`, `io.println`) are also included.

---

## 3. Test Plan

### Unit Tests (Stash.Tests)

| Test ID | Bug | Scenario                                                  | Expected                                                     |
| ------- | --- | --------------------------------------------------------- | ------------------------------------------------------------ |
| T1      | S1  | Call hierarchy: function called at module scope           | Incoming callers includes `<module>` entry                   |
| T2      | S1  | Call hierarchy: function called only from other functions | Incoming callers lists the calling functions (no `<module>`) |
| T3      | S2  | Hover: `arr.push`                                         | Returns function signature, not "namespace arr"              |
| T4      | S2  | Hover: `io.println`                                       | Returns function signature                                   |
| T5      | S2  | Hover: `utils.log` (imported module)                      | Returns function signature from module                       |
| T6      | S3  | Workspace symbols: no git-scheme duplicates               | Each symbol appears once                                     |
| T7      | S4  | Workspace symbols: no markdown files                      | No `.md` file results                                        |
| T8      | S5  | Call hierarchy outgoing: imported function calls          | `utils.log` appears in outgoing calls                        |

### Integration Tests

The LSP test suite should include a test script with:

- A function called from module scope and from another function
- Namespace built-in calls (`arr.push`, `str.padEnd`)
- Imported module calls (`utils.log`)
- A struct method call (`target.label()`)

Run the full call hierarchy, hover, and workspace symbol queries against this script and assert correct results.

---

## 4. Decision Log

### D1: `<module>` as synthetic caller name for top-level code

**Decision:** Use `<module>` as the caller name for top-level call sites in call hierarchy results.

**Alternatives considered:**

- `<top-level>` — more descriptive but not conventional.
- `<script>` — used by Node.js but Stash files aren't always "scripts."
- The filename (e.g., `deploy.stash`) — accurate but could be confused with a function name.

**Rationale:** Python LSPs use `<module>` for the same concept. It's terse, conventional, and clearly signals "not a function."

### D2: Hover — check symbol kind, don't reorder tiers

**Decision:** Fix S2 by adding a `Kind != Namespace` guard in Tier 1 (Option A), not by reordering lookup tiers.

**Rationale:** Reordering would change precedence semantics — if a user imports a module `arr` that shadows the built-in namespace, the built-in would take priority instead of the user's module. The current tier ordering (custom modules first, stdlib second) is correct. The bug is that Tier 1 returns a namespace symbol instead of failing to find the specific function.

### D3: URI filtering — filter in handler, not in cache

**Decision:** Filter git-scheme and non-`.stash` URIs in `WorkspaceSymbolHandler`, not in `GetAllCachedUris()`.

**Rationale:** Other LSP features (diagnostics, hover) should still work on non-file-scheme and non-Stash documents when they're open. The workspace symbol handler is the only consumer that should be restricted to the "real" workspace files. Filtering at the cache level would break other features.
