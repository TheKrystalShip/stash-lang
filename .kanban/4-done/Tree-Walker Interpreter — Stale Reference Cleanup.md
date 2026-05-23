# Tree-Walker Interpreter — Stale Reference Cleanup

**Status:** Backlog
**Created:** 2026-05-05
**Type:** Cleanup / documentation hygiene
**Scope:** Documentation, build configuration, code comments, one broken example
**Risk:** Low — no production code paths affected

---

## 1. Purpose

Stash migrated from a tree-walking AST interpreter (`Stash.Interpreter/` project) to a register-based bytecode VM (`Stash.Bytecode/`). The migration is **complete** — `Stash.Interpreter/` no longer exists in the repository or the solution. However, an audit (three parallel `Explore` subagents, May 2026) found **~15 stale references** scattered across:

- Documentation (`.github/instructions/*.md`, per-project `CLAUDE.md` files, VS Code extension README)
- Build configuration (`.dockerignore`)
- Code comments (XML doc comments on debugger interfaces, a stale comment in `RuntimeOps.cs`)
- One **broken example** (`examples/EmbeddingDemo/`) that won't compile

This spec removes those references so the documentation matches reality and the example builds again. It is a **pure cleanup** — no behavior changes, no API changes, no test changes other than verifying the example compiles.

> **Audit conclusion (verified):** The active code paths are 100% on the bytecode VM. There is **no surviving tree-walking interpreter logic** to remove or migrate. The 6 documented `IExprVisitor`/`IStmtVisitor` implementations (Compiler, SemanticResolver, SemanticValidator, SymbolCollector, SemanticTokenWalker, StashFormatter) are the only visitors in the codebase. `IInterpreterContext`, `IDebugExecutor`, `IDebugScope`, `IStdlibProvider`, `StashEngine`, etc. are all current bytecode-era abstractions, not leftovers.

---

## 2. Background — Why this is the only thing left

The completed DAP migration spec [`.kanban/4-done/DAP Bytecode VM — Exclusive Debugger Migration.md`](../4-done/DAP%20Bytecode%20VM%20—%20Exclusive%20Debugger%20Migration.md) explicitly noted that:

- `Stash.Dap` would temporarily keep a `Stash.Interpreter` project reference solely so the `SemanticResolver` could run.
- A "future spec" would extract the Resolver and drop the reference entirely.

That follow-up work has already happened: `SemanticResolver` now lives at [Stash.Core/Resolution/SemanticResolver.cs](../../Stash.Core/Resolution/SemanticResolver.cs) and `Stash.Dap.csproj` references only `Stash.Bytecode`, `Stash.Core`, `Stash.Stdlib`, and `Stash.Tap` — **no `Stash.Interpreter` reference**. The `Stash.Interpreter/` directory was deleted from the repo and removed from `Stash.sln`.

What was missed during that deletion: the surrounding documentation, the broken external example, the `.dockerignore` line, and a few stale code comments. This spec mops those up.

---

## 3. Findings — Categorized Inventory

Each item is tagged with a fix verb: **DELETE** (line/file removal), **REWRITE** (text update), **REPOINT** (path / project-reference update), or **KEEP** (false positive — explicitly noted to prevent accidental "fix").

### 3.1 Broken — `examples/EmbeddingDemo/` will not compile  (REPOINT + REWRITE)

[examples/EmbeddingDemo/EmbeddingDemo.csproj](../../examples/EmbeddingDemo/EmbeddingDemo.csproj) line 12 references the deleted project:

```xml
<ProjectReference Include="../../Stash.Interpreter/Stash.Interpreter.csproj" />
```

[examples/EmbeddingDemo/Program.cs](../../examples/EmbeddingDemo/Program.cs) line 5:

```csharp
using Stash.Interpreting;
```

The `StashEngine` class invoked by the demo is real and lives at `Stash.Bytecode/StashEngine.cs` in the `Stash.Bytecode` namespace. Fix:

- `.csproj`: replace the `Stash.Interpreter` ProjectReference with a `Stash.Bytecode` reference (and add `Stash.Stdlib` if `StashEngine` requires it — verify by attempting build).
- `Program.cs`: change `using Stash.Interpreting;` → `using Stash.Bytecode;`. Re-check any other types referenced (`StashCapabilities`, `CreateFunction`, etc.) and update namespaces as needed.

**Verification:** `dotnet build examples/EmbeddingDemo/EmbeddingDemo.csproj` must succeed. Optionally run the demo to confirm output is identical.

> **Open question for implementer:** Is `examples/EmbeddingDemo/` part of `Stash.sln` or a standalone project? If standalone, decide whether to add it to the solution so it's covered by `dotnet build`. Recommendation: add it to the solution to prevent future bitrot of the embedding API surface.

### 3.2 Stale `applyTo:` globs in `.github/instructions/` (REPOINT)

These globs target a directory that doesn't exist, so the instruction files **never apply to anything**. Both must be repointed to the current locations.

| File | Current `applyTo` | Should be |
|------|-------------------|-----------|
| [.github/instructions/tap.instructions.md](../../.github/instructions/tap.instructions.md) | `Stash.Interpreter/Testing/**` | TAP runtime is now split across `Stash.Tap/**` (TapReporter) and `Stash.Stdlib/BuiltIns/TestBuiltIns.cs` (test/describe/skip/assert built-ins). Recommendation: `applyTo: "Stash.Tap/**, Stash.Stdlib/BuiltIns/TestBuiltIns.cs"` |
| [.github/instructions/tpl.instructions.md](../../.github/instructions/tpl.instructions.md) | `Stash.Interpreter/Interpreting/Templating/**` | Templating engine is now `Stash.Tpl/**` and `Stash.Stdlib/BuiltIns/TplBuiltIns.cs`. Recommendation: `applyTo: "Stash.Tpl/**, Stash.Stdlib/BuiltIns/TplBuiltIns.cs"` |

The body of each file also needs the architecture diagram (§3.3) updated.

### 3.3 Outdated architecture descriptions in instruction files (REWRITE)

| File | Line(s) | Current claim | Reality |
|------|---------|---------------|---------|
| `.github/instructions/tap.instructions.md` | ~13 (architecture block), ~49 (path mention) | `Stash.Interpreter/Testing/{ITestHarness, TapReporter, AssertionError}.cs` and `Stash.Interpreter/Interpreting/BuiltIns/TestBuiltIns.cs` | TapReporter lives in `Stash.Tap/TapReporter.cs`. AssertionError lives in `Stash.Core/Runtime/` (verify exact path). TestBuiltIns lives in `Stash.Stdlib/BuiltIns/TestBuiltIns.cs`. ITestHarness — verify whether this interface still exists or was replaced by VM hooks. |
| `.github/instructions/tpl.instructions.md` | ~13 (architecture block), ~22 (renderer description), ~33 (Pipeline section calling `Interpreter.EvaluateString()`) | "Tree-walk renderer, delegates expressions to Stash interpreter" and "Template expressions reuse the full Stash interpreter (`Interpreter.EvaluateString()`)" | Template renderer lives at `Stash.Tpl/TemplateRenderer.cs`. Expression evaluation now goes through `VMTemplateEvaluator` at `Stash.Bytecode/Runtime/VMTemplateEvaluator.cs` which compiles to bytecode. The renderer still walks the *template* AST (correct — separate domain), but it no longer calls a tree-walking Stash interpreter. Reword to: "Tree-walk renderer over the template AST; expressions are compiled to Stash bytecode via `VMTemplateEvaluator`." |
| `.github/instructions/dap.instructions.md` | ~19, ~86, ~144 | Dependencies: `Stash.Core, Stash.Interpreter`; `IDebugger` "defined in `Stash.Interpreter/Debugging/IDebugger.cs`"; section heading "Supporting Types (Stash.Interpreter/Debugging/)" | Actual `Stash.Dap.csproj` references: `Stash.Bytecode, Stash.Core, Stash.Stdlib, Stash.Tap`. `IDebugger` (and `IDebugExecutor`, `IDebugScope`, `CallFrame`) live in `Stash.Core/Debugging/`. |
| `.github/instructions/playground.instructions.md` | ~31 | Dependencies: `Stash.Core + Stash.Interpreter` | Actual `Stash.Playground.csproj` references: `Stash.Analysis, Stash.Bytecode, Stash.Core, Stash.Stdlib`. |

### 3.4 Outdated per-project `CLAUDE.md` files (REWRITE)

These mirror the same architecture mistakes as the instruction files and need the same edits:

| File | Issue |
|------|-------|
| [Stash.Tap/CLAUDE.md](../../Stash.Tap/CLAUDE.md) | Architecture diagram (lines ~6–14) shows `Stash.Interpreter/Testing/` and `Stash.Interpreter/Interpreting/BuiltIns/`. Rewrite to point at `Stash.Tap/` and `Stash.Stdlib/BuiltIns/TestBuiltIns.cs`. Also revise the intro line "harness attaches at interpreter startup via `--test`" → describe VM startup wiring. |
| [Stash.Tpl/CLAUDE.md](../../Stash.Tpl/CLAUDE.md) | Architecture diagram (lines ~7–15) shows `Stash.Interpreter/Interpreting/Templating/`; line ~13 says "Tree-walk renderer, delegates expressions to Stash interpreter"; line ~28 says "Template expressions reuse the full Stash interpreter (`Interpreter.EvaluateString()`)". Rewrite per §3.3. |
| [Stash.Dap/CLAUDE.md](../../Stash.Dap/CLAUDE.md) | Line ~15: "Dependencies: ... `Stash.Core`, `Stash.Interpreter`". Line ~82: `IDebugger` defined in `Stash.Interpreter/Debugging/IDebugger.cs`. Update both per §3.3. |
| [Stash.Playground/CLAUDE.md](../../Stash.Playground/CLAUDE.md) | Line ~26: "`Stash.Core` + `Stash.Interpreter` project references". Update per §3.3. |

### 3.5 Stale code comments (REWRITE)

These are XML/inline doc comments only — no behavior impact, but actively misleading because they describe a "tree-walk" implementer that doesn't exist.

| File | Line | Current text | Action |
|------|------|--------------|--------|
| [Stash.Core/Debugging/IDebugExecutor.cs](../../Stash.Core/Debugging/IDebugExecutor.cs) | 7 | `Implemented by <c>Interpreter</c> (tree-walk) and <c>VMDebugAdapter</c> (bytecode).` | Reword: `Implemented by <c>VMDebugAdapter</c> (bytecode VM).` Drop the tree-walk half. |
| [Stash.Core/Debugging/IDebugScope.cs](../../Stash.Core/Debugging/IDebugScope.cs) | 7 | `Implemented by <c>Environment</c> (tree-walk) and VM frame adapters (bytecode).` | Reword: `Implemented by VM frame adapters in <c>Stash.Bytecode</c>.` |
| [Stash.Bytecode/Runtime/RuntimeOps.cs](../../Stash.Bytecode/Runtime/RuntimeOps.cs) | 14–16 (class summary) | `These exactly replicate the tree-walk interpreter's semantics.` | Reword to describe the operations as the canonical Stash runtime semantics (e.g., "Implements Stash's runtime semantics for arithmetic, comparison, indexing, etc., shared by every VM dispatch path."). |

> **Decision: keep the interface name `IInterpreterContext`.** It is the live bytecode-era context interface (713+ usages) — the word "Interpreter" here refers to the Stash language implementation generally, not the deleted tree-walker. Renaming would be a massive sweep with zero functional value. Same reasoning preserves `IInterpreterHost`-style names if any exist.

### 3.6 `.dockerignore` (DELETE)

[.dockerignore](../../.dockerignore) line 25:

```
Stash.Interpreter/
```

Inside the comment block "Projects not needed to build Stash.Registry". Delete this single line. The other entries in that block are still valid.

### 3.7 VS Code extension README build instructions (REWRITE)

[.vscode/extensions/stash-lang/README.md](../../.vscode/extensions/stash-lang/README.md) lines ~374 and ~395 contain build commands like `dotnet build Stash.Interpreter/`. Replace with the current command — likely `dotnet build Stash.Cli/` (since the extension users build the CLI binary, not a runtime library). Verify by checking what the surrounding paragraph describes.

> **Keep:** the `stash.interpreterPath` setting key (line 313, `package.json` line ~245). This setting points at the compiled CLI binary; the name is part of the extension's public API and renaming would break user `settings.json` entries. The setting key reflects the user-facing concept ("which Stash interpreter binary to invoke"), not the deleted project.

### 3.8 Old kanban backlog spec with outdated paths (REWRITE — lower priority)

[.kanban/0-backlog/packages/PowerShell Integration — Feasibility Analysis.md](../../.kanban/0-backlog/packages/PowerShell%20Integration%20—%20Feasibility%20Analysis.md):

| Line | Current | Should be |
|------|---------|-----------|
| 451 | `Stash.Interpreter/Capabilities/StashCapabilities.cs` | `Stash.Core/Runtime/StashCapabilities.cs` |
| 905 | "in `Stash.Stdlib`, `Stash.Interpreter`, and `Stash.Core`" | "in `Stash.Stdlib`, `Stash.Bytecode`, and `Stash.Core`" |

This is a backlog feasibility doc that hasn't been picked up. It's low-priority because the spec hasn't begun implementation, but cleaning it up now prevents an implementer from chasing dead paths later.

### 3.9 KEEP (do not "fix")

Listed explicitly so a future agent doesn't undo these on autopilot:

| Reference | Why it stays |
|-----------|--------------|
| `README.md:32` "No tree-walking overhead." | This is a marketing/comparison statement about the bytecode VM's design, not a reference to the old project. It's accurate and a deliberate selling point. |
| `IInterpreterContext` interface name | Live abstraction with 713+ usages; "interpreter" is generic language-implementation terminology here, not the deleted project. Rename would be churn. |
| `stash.interpreterPath` VS Code setting | User-facing API; renaming breaks existing user settings. |
| `examples/interfaces.stash:53`, `examples/retry_blocks.stash:150` | Generic uses of "the interpreter" in user-facing docstrings — refer to "the Stash runtime" abstractly, not the deleted project. |
| Kanban specs in `.kanban/4-done/` (e.g. `LSP Bytecode VM Migration`, `DAP Bytecode VM — Exclusive Debugger Migration`, `Stash Language — Comprehensive Project Analysis`) | Historical record of the migration. The whole point of `4-done/` is to preserve the as-completed text. **Do not edit.** |
| Template tree-walk renderer in `Stash.Tpl/TemplateRenderer.cs` | Walks the *template* AST (a separate, tiny grammar). Has nothing to do with the deleted Stash AST tree-walker. |

---

## 4. Out of Scope

- Renaming `IInterpreterContext` or any other interface containing the word "Interpreter". (Massive sweep, zero functional value, breaks downstream embedders.)
- Editing `.kanban/4-done/` historical specs.
- Any change to runtime behavior, public stdlib API, or the bytecode VM itself.
- Restructuring `Stash.Tap/` or `Stash.Tpl/` projects.
- Re-auditing visitor implementations (the audit confirmed all 6 are canonical and current).

---

## 5. Implementation Plan

Implementer (or Orchestrator) should execute in this order — each step is independent and individually verifiable:

### Step 1 — Fix the broken example  (highest value)

1. Edit `examples/EmbeddingDemo/EmbeddingDemo.csproj`: replace `Stash.Interpreter` ProjectReference with `Stash.Bytecode` (add `Stash.Stdlib` if needed — try a build to find out).
2. Edit `examples/EmbeddingDemo/Program.cs`: change `using Stash.Interpreting;` → `using Stash.Bytecode;`. Resolve any other namespace breakages.
3. `dotnet build examples/EmbeddingDemo/EmbeddingDemo.csproj` must succeed.
4. (Optional) Add the project to `Stash.sln` so it's covered by the main build going forward — see open question in §3.1.

### Step 2 — Update `.dockerignore`

Delete the single line `Stash.Interpreter/`.

### Step 3 — Fix the four instruction files

Edit `.github/instructions/{tap,tpl,dap,playground}.instructions.md`:

- Repoint stale `applyTo:` globs (tap, tpl).
- Rewrite architecture diagrams and dependency lists per §3.3.

### Step 4 — Fix the four per-project CLAUDE.md files

Same edits as Step 3 applied to `Stash.Tap/`, `Stash.Tpl/`, `Stash.Dap/`, `Stash.Playground/` `CLAUDE.md` files.

### Step 5 — Fix code comments

Three small edits per §3.5 — `IDebugExecutor.cs`, `IDebugScope.cs`, `RuntimeOps.cs`.

### Step 6 — Fix the VS Code extension README

Rewrite the two `dotnet build Stash.Interpreter/` commands per §3.7.

### Step 7 — (Lower priority) Fix the PowerShell Integration backlog spec

Two find-and-replace edits per §3.8.

### Step 8 — Final verification

```bash
# 1. Confirm no live Stash.Interpreter references survive in code/config:
git grep -n 'Stash\.Interpreter\|Stash\.Interpreting' \
  -- '*.cs' '*.csproj' '*.sln' '*.md' '.dockerignore' \
  ':(exclude).kanban/4-done/'  # historical — keep
# Expected: zero or only KEEP items from §3.9

# 2. Confirm no live tree-walk comments survive:
git grep -nE 'tree.?walk(er|ing)?' -- '*.cs' \
  | grep -v 'Stash.Tpl/TemplateRenderer.cs'  # legitimate template AST walker
# Expected: zero results outside Tpl

# 3. Build everything:
dotnet build

# 4. Run tests:
dotnet test
```

All four checks must pass. No test changes are expected (this is a docs/comments cleanup); existing tests should remain green.

---

## 6. Risk Analysis

| Risk | Likelihood | Severity | Mitigation |
|------|------------|----------|------------|
| `EmbeddingDemo` uses an API that changed signature between tree-walker `StashEngine` and bytecode `StashEngine` | Medium | Low | Build will surface it. The bytecode `StashEngine` is documented to be the canonical embedding API — fix any signature drift in the demo, not the engine. |
| Renaming KEEP items by accident | Medium (without §3.9) | Medium | §3.9 enumerates KEEP items explicitly. Reviewer must check for unintended changes to these. |
| Verification grep misses something because of unusual quoting/casing | Low | Low | Step 8's grep uses `-i`-equivalent regex alternation. Reviewer should also do a visual scan of changed files. |
| Editing `.kanban/4-done/` specs by accident | Medium | High (loses historical record) | Step 8 grep explicitly excludes `.kanban/4-done/`. Reviewer must verify no `4-done/` paths appear in the diff. |

---

## 7. Decision Log

- **Decision:** Single cleanup spec covering all 8 finding categories rather than one spec per file. **Rationale:** All findings share the same root cause (incomplete cleanup after the bytecode migration) and the fixes are mechanical text edits with no inter-dependencies. Splitting would multiply review/PR overhead without adding value. **Risk if reversed:** Spec churn; harder to verify no item was missed.
- **Decision:** Keep `IInterpreterContext` and `stash.interpreterPath` despite the word "Interpreter". **Rationale:** Both are live, semantically accurate APIs (the language's runtime, not the deleted project). Renaming is breaking churn. **Risk if reversed:** Wide-scope refactor with zero behavior win and potential downstream breakage for embedders/users with `settings.json` entries.
- **Decision:** Fix the `EmbeddingDemo` example rather than delete it. **Rationale:** It documents the embedding API surface for users of `Stash.Bytecode.StashEngine`. Deleting it loses example coverage. **Risk if reversed:** Future embedders have no working sample; embedding-related regressions go undetected.
- **Decision:** Edit the PowerShell Integration backlog spec (§3.8) even though it's not active. **Rationale:** The spec author wrote against the old layout; an implementer reading it later will chase dead paths. Cheap to fix now. **Risk if reversed:** Low — it's a backlog item that may be revised before pickup anyway.
- **Decision:** Do **not** rename `RuntimeOps.cs` or restructure `Stash.Bytecode/Runtime/`. **Rationale:** The class is correctly placed and named; only its summary comment is stale. **Risk if reversed:** Unnecessary file churn that complicates `git blame`.

---

## 8. Acceptance Criteria

- [ ] `examples/EmbeddingDemo/` builds cleanly via `dotnet build`.
- [ ] `git grep 'Stash\.Interpreter\|Stash\.Interpreting' -- '*.cs' '*.csproj' '*.sln' '*.md' '.dockerignore' ':(exclude).kanban/4-done/'` returns only KEEP items from §3.9 (or zero).
- [ ] `git grep -E 'tree.?walk(er|ing)?' -- '*.cs'` returns only the legitimate `Stash.Tpl/TemplateRenderer.cs` reference.
- [ ] All four `.github/instructions/{tap,tpl,dap,playground}.instructions.md` files have correct `applyTo:` globs that match real directories, and architecture descriptions that match the current project layout.
- [ ] All four affected `CLAUDE.md` files (Tap, Tpl, Dap, Playground) describe the current architecture.
- [ ] `Stash.Core/Debugging/IDebugExecutor.cs`, `IDebugScope.cs`, and `Stash.Bytecode/Runtime/RuntimeOps.cs` have updated doc comments.
- [ ] `.dockerignore` no longer mentions `Stash.Interpreter/`.
- [ ] `.vscode/extensions/stash-lang/README.md` build commands point at the correct project.
- [ ] `.kanban/0-backlog/packages/PowerShell Integration — Feasibility Analysis.md` paths updated.
- [ ] No files in `.kanban/4-done/` modified.
- [ ] `dotnet build` and `dotnet test` both succeed (no functional regressions expected).

---

## 9. Estimated Footprint

- **Files touched:** ~13–14 (1 example .csproj, 1 example .cs, 1 .dockerignore, 4 instruction files, 4 CLAUDE.md files, 3 source-comment files, 1 extension README, 1 backlog spec).
- **Lines changed:** ~80–120 (mostly small text edits; the architecture diagrams in two instruction files account for the largest blocks).
- **New tests:** None. (This is a docs/comments cleanup. The `EmbeddingDemo` build is the only functional verification.)
- **Public API impact:** None.
- **Migration / breaking changes:** None.

---

## 10. References

- Audit findings: three parallel `Explore` subagent reports, May 2026 (this spec consolidates them).
- Predecessor work: [.kanban/4-done/DAP Bytecode VM — Exclusive Debugger Migration.md](../4-done/DAP%20Bytecode%20VM%20—%20Exclusive%20Debugger%20Migration.md), [.kanban/4-done/LSP Bytecode VM Migration — Feasibility Analysis.md](../4-done/LSP%20Bytecode%20VM%20Migration%20—%20Feasibility%20Analysis.md).
- Project layering: `.github/copilot-instructions.md` and `CLAUDE.md` (root) — both already reflect the current bytecode-VM architecture and need no edits.
