# Static Analysis Engine — Long-Term Improvement Roadmap

> **Status:** Backlog — Design Phase
> **Created:** April 2026
> **Purpose:** Bring Stash's static analysis engine (Stash.Analysis + Stash.Check) to industry-standard quality, benchmarked against ESLint, Ruff, Roslyn Analyzers, and rust-clippy.

---

## Table of Contents

1. [Current State Assessment](#1-current-state-assessment)
2. [Industry Benchmark Comparison](#2-industry-benchmark-comparison)
3. [Gap Analysis — Architecture](#3-gap-analysis--architecture)
4. [Gap Analysis — Missing Diagnostics](#4-gap-analysis--missing-diagnostics)
5. [Gap Analysis — CLI & Output](#5-gap-analysis--cli--output)
6. [Gap Analysis — Configuration](#6-gap-analysis--configuration)
7. [Gap Analysis — Suppression System](#7-gap-analysis--suppression-system)
8. [Gap Analysis — Diagnostic Quality](#8-gap-analysis--diagnostic-quality)
9. [Gap Analysis — Testing & Tooling](#9-gap-analysis--testing--tooling)
10. [Phased Roadmap](#10-phased-roadmap)
11. [Decision Log](#11-decision-log)

---

## 1. Current State Assessment

### 1.1 Stash.Analysis — What Exists Today

**Architecture:** 24 files across 5 directories. A 10-stage sequential pipeline:

```
Lex → Parse → SymbolCollector → ImportResolver → TypeInferenceEngine
  → DocCommentResolver → SemanticValidator → SuppressionDirectiveParser
  → ProjectConfig → Cache
```

**Diagnostics:** 33 diagnostic codes across 8 categories (SA00xx–SA08xx), covering:

- Control flow (break/continue/return context, unreachable code)
- Declarations (unused variables, undefined identifiers, constant reassignment)
- Type safety (type mismatches on init/assign/fields, unknown type annotations)
- Function calls (arity checking, argument types)
- Spread/rest (type checks, null/empty spread)
- Commands (nested elevate, retry validation)
- Imports (dynamic paths)
- Infrastructure (malformed suppression codes)

**Type Inference:** Forward-only, literal/annotation-based, 11 inference rules. No bidirectional inference, no constraint solving, no union types. Type narrowing declared but never populated.

**Symbol Table:** Proper scope hierarchy with bidirectional def↔ref links, indexed field lookup, multi-map for shadowing. No unique symbol IDs.

**Suppression:** `// stash-disable`, `// stash-disable-line`, `// stash-disable-next-line`, `// stash-restore` with per-code or blanket suppression. Malformed code detection (SA0001/SA0002).

**Configuration:** `.stashcheck` file, INI-like format, with `disable`, `severity.*`, and `require-suppression-reason` settings. Single file per project (first found walking up from script directory).

### 1.2 Stash.Check — What Exists Today

**Architecture:** Thin CLI wrapper around AnalysisEngine. Native AOT binary. 7 files.

**CLI Options:** `--format sarif`, `--output`, `--exclude`, `--severity`, `--no-imports` (parsed but unimplemented), `--version`, `--help`.

**Output:** SARIF v2.1.0 only.

**Exit Codes:** 0 (clean), 1 (diagnostics), 2 (error).

---

## 2. Industry Benchmark Comparison

### 2.1 Feature Matrix

| Feature                          | ESLint                           | Ruff                       | Roslyn                           | rust-clippy                         | **Stash**             |
| -------------------------------- | -------------------------------- | -------------------------- | -------------------------------- | ----------------------------------- | --------------------- |
| **Rule-based architecture**      | ✅ Individual rule objects       | ✅ Individual rule structs | ✅ DiagnosticAnalyzer subclasses | ✅ Lint passes                      | ❌ Monolithic visitor |
| **Autofix infrastructure**       | ✅ fix() + suggest[]             | ✅ Safe/unsafe fixes       | ✅ CodeFixProvider               | ✅ MachineApplicable/MaybeIncorrect | ❌ None               |
| **Control flow graph**           | ✅ Code path events              | ✅ Via red-knot            | ✅ Full CFG                      | ✅ MIR-based                        | ❌ None               |
| **Data flow analysis**           | ⚠️ Via scope manager             | ✅ Via red-knot            | ✅ Full DFA                      | ✅ MIR-based                        | ❌ None               |
| **Plugin/extension system**      | ✅ Full plugin API               | ❌ Closed rules            | ✅ NuGet analyzers               | ✅ Compiler plugins                 | ❌ None               |
| **Hierarchical config**          | ✅ Cascading configs             | ✅ Closest config wins     | ✅ .editorconfig + .globalconfig | ✅ Cargo.toml + clippy.toml         | ❌ Single config      |
| **Per-file rule overrides**      | ✅ Files-based config            | ✅ per-file-ignores        | ✅ Generated-code config         | ❌                                  | ❌ None               |
| **Rule profiles/presets**        | ✅ recommended, all              | ✅ select by prefix        | ✅ Severity configs              | ✅ warn/deny/allow groups           | ❌ None               |
| **Multiple output formats**      | ✅ 10+ formatters                | ✅ 12 formats              | ✅ MSBuild + SARIF               | ✅ Text, JSON                       | ⚠️ SARIF only         |
| **Human-readable output**        | ✅ Default                       | ✅ Default ("full")        | ✅ Default                       | ✅ Default                          | ❌ Missing            |
| **Watch mode**                   | ✅                               | ✅ --watch                 | ✅ IDE-driven                    | ❌                                  | ❌ None               |
| **Autofix CLI**                  | ✅ --fix                         | ✅ --fix / --unsafe-fixes  | ✅ dotnet format                 | ✅ --fix                            | ❌ None               |
| **Statistics/summary**           | ⚠️ Via formatters                | ✅ --statistics            | ✅ Build logs                    | ❌                                  | ❌ None               |
| **Show matched files**           | ✅ --debug                       | ✅ --show-files            | ❌                               | ❌                                  | ❌ None               |
| **Auto-add suppressions**        | ✅ --disable-next-line           | ✅ --add-noqa              | ❌                               | ❌                                  | ❌ None               |
| **Stdin support**                | ✅ --stdin                       | ✅ --stdin-filename        | ❌                               | ❌                                  | ❌ None               |
| **Caching**                      | ✅ --cache                       | ✅ File-based cache        | ✅ Incremental compilation       | ❌                                  | ❌ None               |
| **Unused suppression detection** | ✅ reportUnusedDisableDirectives | ✅ RUF100                  | ✅ IDE_0079                      | ✅                                  | ❌ None               |
| **Related diagnostic locations** | ✅                               | ✅                         | ✅                               | ✅                                  | ❌ None               |
| **Diagnostic tags**              | ✅ unnecessary, deprecated       | ✅                         | ✅                               | ✅                                  | ⚠️ Only "unnecessary" |
| **Incremental analysis**         | ✅ File-level                    | ✅ File-level              | ✅ Syntax-level                  | ✅ Query-based                      | ❌ Full re-analysis   |
| **Rule performance profiling**   | ✅ TIMING env var                | ✅ Internal                | ✅ /timing                       | ✅ -Z time-passes                   | ❌ None               |

### 2.2 Diagnostic Category Coverage

| Category                   | ESLint                         | Ruff       | Roslyn           | **Stash** |
| -------------------------- | ------------------------------ | ---------- | ---------------- | --------- |
| Unused variables/imports   | ✅                             | ✅         | ✅               | ✅        |
| Undefined identifiers      | ✅                             | ✅         | ✅               | ✅        |
| Type mismatches            | ✅ (TS)                        | ✅ (mypy)  | ✅               | ✅        |
| Unreachable code           | ✅                             | ✅         | ✅               | ✅        |
| Unused parameters          | ✅ no-unused-vars              | ✅ ARG     | ✅ IDE0060       | ❌        |
| Shadow variables           | ✅ no-shadow                   | ✅ A001    | ✅ IDE0072       | ❌        |
| Definite assignment        | N/A                            | N/A        | ✅ CS0165        | ❌        |
| Tautological comparisons   | ✅ no-self-compare             | ✅ PLR0124 | ✅ CS1718        | ❌        |
| Null safety                | ✅ (TS strict)                 | ✅ (mypy)  | ✅ Nullable refs | ❌        |
| Duplicate branches/cases   | ✅ no-duplicate-case           | ✅ SIM     | ✅ IDE0066       | ❌        |
| Complexity metrics         | ✅ complexity                  | ✅ C901    | ✅ CA1502        | ❌        |
| `let` → `const` suggestion | ✅ prefer-const                | ✅         | ✅ IDE0044       | ❌        |
| Naming conventions         | ✅ camelCase                   | ✅ N       | ✅ IDE1006       | ❌        |
| Dead store detection       | ✅ no-unused-vars              | ✅ F841    | ✅ IDE0059       | ❌        |
| Empty body detection       | ✅ no-empty                    | ✅         | ✅               | ❌        |
| Missing return/yield       | ✅ array-callback-return       | ✅         | ✅ CS0161        | ❌        |
| Deprecated API usage       | ✅                             | ✅         | ✅               | ❌        |
| String/template issues     | ✅ no-template-curly-in-string | ✅         | ✅               | ❌        |
| Import organization        | ✅ sort-imports                | ✅ I001    | ✅ IDE0065       | ❌        |

---

## 3. Gap Analysis — Architecture

### 3.1 Monolithic Validator → Rule-Based Architecture

**Current Problem:** `SemanticValidator.cs` is a ~1,200-line monolithic file implementing all validation logic in a single visitor. Adding a new diagnostic requires modifying this central file, risking merge conflicts and making it hard to enable/disable individual checks.

**Industry Pattern:** Every major linter uses isolated, self-contained rule objects:

- **ESLint:** Each rule is a JS module with `meta` (type, docs, fixable, schema, messages) + `create()` returning AST visitors
- **Ruff:** Each rule is a Rust function registered in a rule table, with metadata including `Violation`, `fix_title`, `Applicability`
- **Roslyn:** Each analyzer is a `DiagnosticAnalyzer` subclass registering `AnalysisContext` callbacks

**Proposed Design:**

```csharp
public interface IAnalysisRule
{
    DiagnosticDescriptor Descriptor { get; }

    // Which AST nodes this rule wants to visit
    IEnumerable<Type> SubscribedNodeTypes { get; }

    // Analyze a single node
    void Analyze(AnalysisContext context);
}

public class AnalysisContext
{
    public Stmt Node { get; }
    public ScopeTree Scopes { get; }
    public TypeInferenceEngine Types { get; }
    public Action<SemanticDiagnostic> ReportDiagnostic { get; }
    public Action<CodeFix> SuggestFix { get; }  // Future: autofix
}
```

**Benefits:**

- Each rule is a single file in `Rules/` — easy to find, test, and configure
- Rules can be individually enabled/disabled without code changes
- Rule metadata (fixable, category, default severity) is co-located with the rule
- Foundation for future plugin system

**Migration path:** Incremental — extract one rule at a time from SemanticValidator into individual IAnalysisRule implementations. SemanticValidator becomes a rule host/dispatcher.

### 3.2 No Autofix Infrastructure

**Current Problem:** Diagnostics are fire-and-forget — they report problems but never suggest how to fix them. The LSP cannot offer code actions for any analysis diagnostic.

**Industry Pattern:**

- **ESLint:** `fix(fixer)` returns text edits. `suggest[]` provides alternative fixes. Rule metadata declares `fixable: "code"` or `fixable: "whitespace"`.
- **Ruff:** Each fix has an `Applicability` (safe / unsafe / display-only). Users opt into fix application with `--fix` / `--unsafe-fixes`.
- **Roslyn:** `CodeFixProvider` paired with `DiagnosticAnalyzer`, offering `RegisterCodeFix` actions.

**Proposed Design:**

```csharp
public record CodeFix(
    string Title,
    FixApplicability Applicability,  // Safe, Unsafe, Suggestion
    IReadOnlyList<TextEdit> Edits
);

public enum FixApplicability
{
    Safe,       // Preserves semantics — safe for --fix
    Unsafe,     // May change semantics — requires --unsafe-fixes
    Suggestion  // Multiple options — user must choose (LSP code action only)
}

public record TextEdit(SourceSpan Span, string NewText);
```

**Integration:** Diagnostics carry an optional `IReadOnlyList<CodeFix>` — the LSP maps these to code actions, and `stash-check --fix` applies safe fixes.

### 3.3 No Control Flow Graph

**Current Problem:** Without a CFG, analysis is limited to syntactic patterns. Can't detect:

- All paths through an if/else return, making subsequent code unreachable
- Variables used on only some paths (definite assignment)
- Conditions that are always true/false after narrowing

**Industry Pattern:** ESLint exposes `onCodePathStart`, `onCodePathEnd`, `onCodePathSegmentStart` events. Roslyn builds a full CFG with `BasicBlock` and `ControlFlowBranch`. Ruff (red-knot) uses a CFG for type narrowing.

**Proposed Phase:** This is a significant infrastructure investment. Build a lightweight CFG representation after parsing:

```csharp
public class BasicBlock
{
    public int Id { get; }
    public List<Stmt> Statements { get; }
    public List<BasicBlock> Successors { get; }
    public List<BasicBlock> Predecessors { get; }
    public BranchKind BranchKind { get; }  // Conditional, Unconditional, Return, Throw
}
```

The CFG enables a whole class of flow-sensitive diagnostics (unreachable branches, definite assignment, exhaustive returns, null safety).

### 3.4 No Incremental Analysis

**Current Problem:** Every call to `Analyze()` runs the full 10-stage pipeline from scratch. For the LSP (keystroke-driven), this means re-lexing, re-parsing, and re-validating the entire file on every edit.

**Industry Pattern:**

- **Ruff:** File-level caching with content hash. Skip files whose hash matches cache.
- **ESLint:** `--cache` flag stores results keyed by file path + hash.
- **Roslyn:** Syntax-level incrementality — only re-parses changed spans.

**Proposed Levels:**

1. **Level 1 — Content-hash caching:** Before analysis, hash the source. If hash matches cached result, return cached result. Zero-cost for unchanged files. Minimal implementation effort.
2. **Level 2 — Dependency-aware invalidation:** When file A changes, automatically re-analyze files that import A. Currently `GetDependents()` exists but must be manually called.
3. **Level 3 — Incremental parsing (long-term):** Re-parse only changed spans. Significant complexity; defer until performance demands it.

---

## 4. Gap Analysis — Missing Diagnostics

Each diagnostic below is categorized by implementation complexity and value.

### 4.1 High Value, Low Complexity

| ID     | Name                                      | Description                                                                      | Example                                                 | Fix?                           |
| ------ | ----------------------------------------- | -------------------------------------------------------------------------------- | ------------------------------------------------------- | ------------------------------ |
| SA0205 | `let`-could-be-`const`                    | Variable declared with `let` but never reassigned                                | `let x = 5; print(x)` → suggest `const`                 | ✅ Safe                        |
| SA0206 | Unused parameter                          | Function parameter never referenced in body                                      | `fn foo(x, y) { return x }` → `y` unused                | ⚠️ Suggestion (rename to `_y`) |
| SA0207 | Shadow variable                           | Inner scope variable hides outer scope variable of same name                     | `let x = 1; { let x = 2; }`                             | No fix                         |
| SA0105 | Empty block body                          | `if`, `while`, `for`, `try` with empty body                                      | `if (cond) {}`                                          | No fix                         |
| SA0106 | Unreachable branch (both paths terminate) | Code after if/else where both paths return/throw                                 | `if (x) { return 1 } else { return 2 }; print("dead")`  | ✅ Remove dead code            |
| SA0208 | Dead store                                | Variable assigned but value is never read before next assignment or end of scope | `let x = 1; x = 2; print(x)` → first assignment is dead | No fix                         |

### 4.2 High Value, Medium Complexity

| ID     | Name                       | Description                                                           | Example                                         | Fix?                               |
| ------ | -------------------------- | --------------------------------------------------------------------- | ----------------------------------------------- | ---------------------------------- |
| SA0306 | Self-comparison            | Comparing a value to itself                                           | `x === x`                                       | ✅ Suggestion                      |
| SA0307 | Tautological condition     | Condition that is always true or always false (with literal operands) | `if (true)`, `if (1 > 2)`                       | ✅ Remove branch                   |
| SA0404 | Missing return in function | Function with explicit return type has paths without return           | `fn foo(): string { if (cond) { return "a" } }` | No fix                             |
| SA0107 | Duplicate condition        | Same condition appears in if/else-if chain                            | `if (x) {} else if (x) {}`                      | No fix                             |
| SA0108 | Empty catch block          | `catch` block with no statements or only comments                     | `try { ... } catch (e) {}`                      | Suggestion: add comment or rethrow |
| SA0802 | Unused import              | Import that brings a symbol never referenced in the file              | `import { foo } from "./bar.stash"`             | ✅ Safe: remove import             |
| SA0803 | Duplicate import           | Same symbol imported twice from same module                           |                                                 | ✅ Safe: remove duplicate          |

### 4.3 Medium Value, Medium Complexity

| ID     | Name                        | Description                                                   | Example                               | Fix?                       |
| ------ | --------------------------- | ------------------------------------------------------------- | ------------------------------------- | -------------------------- |
| SA0308 | Possible null access        | Accessing field/method on expression that may be null         | `let x = dict.get("key"); x.length`   | Suggestion: optional chain |
| SA0309 | Unnecessary optional chain  | Optional chain on expression that can never be null           | `let x = 5; x?.toString()`            | ✅ Safe: remove `?`        |
| SA0209 | Naming convention violation | Variable/function/struct names don't follow Stash conventions | `let MyVar = 1` (should be camelCase) | ✅ Safe: rename            |
| SA0405 | Too many parameters         | Function has more than N parameters (configurable)            | `fn foo(a, b, c, d, e, f, g)`         | No fix                     |
| SA0109 | Cyclomatic complexity       | Function exceeds configured complexity threshold              | Deeply nested if/else/loops           | No fix                     |
| SA0804 | Import ordering             | Imports not in canonical order (stdlib → packages → relative) |                                       | ✅ Safe: reorder           |

### 4.4 Medium Value, High Complexity (Requires CFG)

| ID     | Name                      | Description                                                                 | Example                                          | Fix?            |
| ------ | ------------------------- | --------------------------------------------------------------------------- | ------------------------------------------------ | --------------- |
| SA0210 | Definite assignment       | Variable used before being initialized                                      | `let x: string; if (cond) { x = "a" }; print(x)` | No fix          |
| SA0310 | Exhaustive type narrowing | After `is` checks covering all possibilities, remaining path is unreachable | Narrowing checks that cover all enum variants    | No fix          |
| SA0311 | Redundant type check      | `is` check that is always true given current type                           | `let x: string = "a"; if (x is string)`          | ✅ Remove check |

### 4.5 Low Priority / Long Term

| ID     | Name                         | Description                                                | Fix?                 |
| ------ | ---------------------------- | ---------------------------------------------------------- | -------------------- |
| SA0110 | Duplicate dict key           | Same key appears twice in dict literal                     | ✅ Remove duplicate  |
| SA0111 | Unreachable match arm        | Match case that can never be reached due to prior patterns | No fix               |
| SA0312 | Unnecessary type annotation  | Explicit type annotation matches what would be inferred    | ✅ Remove annotation |
| SA0406 | Deprecated function usage    | Calling a built-in or user function marked as deprecated   | Suggestion           |
| SA0507 | Unnecessary spread           | `...arr` in position where array is already expected       | ✅ Remove spread     |
| SA0710 | Command in non-async context | Shell commands inside functions that should be pure        | No fix               |

---

## 5. Gap Analysis — CLI & Output

### 5.1 Human-Readable Output Format

**Gap:** `stash-check` only outputs SARIF JSON. Running from a terminal for quick feedback requires external tools to parse the output.

**Industry Standard:** Every linter defaults to human-readable terminal output:

```
src/deploy.stash:15:3: SA0201 [info] Unused variable 'temp'
src/deploy.stash:28:1: SA0103 [error] 'return' used outside of function
```

**Proposal:** Add `TextFormatter` as default output. SARIF available via `--format sarif`.

Output formats to support (phased):

1. **Text** (default) — one line per diagnostic, with path:line:col, code, severity, message
2. **SARIF** (existing) — for CI/CD
3. **JSON** — machine-readable, per-result objects
4. **GitHub** — `::error file=...,line=...,col=...::message` for GitHub Actions annotations
5. **Grouped** — diagnostics grouped by file, with context lines

### 5.2 `--fix` / `--unsafe-fixes` Support

**Gap:** No way to auto-fix problems from CLI.

**Proposal:** Once autofix infrastructure exists (§3.2), expose via CLI:

```bash
stash-check --fix src/                 # Apply safe fixes only
stash-check --fix --unsafe-fixes src/  # Apply all fixes
stash-check --diff src/                # Show fixes as diff without applying
```

### 5.3 `--statistics` Mode

**Gap:** No way to see a summary of rule violations.

**Proposal:**

```bash
$ stash-check --statistics src/
Rule    | Count | Fixable
--------|-------|--------
SA0201  |    14 | ✓
SA0202  |     3 |
SA0305  |     7 | ✓
```

### 5.4 `--show-files` Mode

**Gap:** No way to see which files would be analyzed given current config and exclude patterns.

**Proposal:**

```bash
$ stash-check --show-files src/
src/main.stash
src/lib/utils.stash
src/lib/deploy.stash
```

### 5.5 Stdin Support

**Gap:** Can't pipe source code into `stash-check` for editor integration or scripted workflows.

**Proposal:**

```bash
echo 'let x = 1;' | stash-check --stdin-filename main.stash -
```

### 5.6 Auto-Add Suppressions

**Gap:** No way to automatically suppress all existing diagnostics (onboarding existing projects).

**Proposal:**

```bash
stash-check --add-suppress src/        # Add // stash-disable-next-line to all violations
stash-check --add-suppress --reason "Legacy code, tracked in ISSUE-123" src/
```

### 5.7 Watch Mode

**Gap:** No file-watching re-analysis.

**Proposal:**

```bash
stash-check --watch src/    # Re-run on file changes
```

Implementation: Use `FileSystemWatcher`, re-analyze only changed files (requires §3.4 incremental analysis).

### 5.8 File-Level Caching

**Gap:** Every invocation re-analyzes all files from scratch.

**Proposal:** Content-hash based cache in `.stash-cache/` directory:

```bash
stash-check src/               # Uses cache
stash-check --no-cache src/    # Ignores cache
stash-check clean              # Clears cache directory
```

### 5.9 Implement `--no-imports`

**Gap:** Flag is parsed in CheckOptions.cs but never passed to AnalysisEngine.

**Proposal:** Thread the option through to AnalysisEngine.Analyze to skip import resolution. Useful for single-file analysis and CI speed.

---

## 6. Gap Analysis — Configuration

### 6.1 Hierarchical Configuration

**Gap:** Single `.stashcheck` file applies to entire project. Can't have different rules for `src/` vs `tests/`.

**Industry Pattern:**

- **ESLint:** Cascading config — each directory can have its own config, merged with parent
- **Ruff:** Closest config wins; `extend` directive imports parent settings

**Proposal:** Walk up directory tree, merge configs. Closest file wins for same key. Explicit `extend` for inheritance:

```ini
# tests/.stashcheck
extend = ../.stashcheck
disable = SA0201   # Allow unused vars in tests
```

### 6.2 Per-File Rule Overrides

**Gap:** Can't configure rules differently for files matching a pattern (e.g., test files, generated files).

**Proposal:**

```ini
# .stashcheck
[per-file-overrides]
"**/*.test.stash" = disable SA0201, SA0202
"**/generated/**" = disable ALL
```

### 6.3 Rule Profiles / Presets

**Gap:** No way to select a curated set of rules. Users must individually disable unwanted rules.

**Proposal:** Named presets:

- `recommended` — the default, curated set of high-value, low-noise rules
- `strict` — all rules enabled, including style and complexity
- `minimal` — only errors, no warnings or info

```ini
# .stashcheck
preset = strict
severity.SA0109 = off  # But disable complexity check
```

### 6.4 Rule Selection by Prefix

**Gap:** Can only disable individual codes. Can't disable an entire category (all SA01xx = control flow).

**Proposal:**

```ini
disable = SA01    # Disable all control flow checks
disable = SA03    # Disable all type safety checks
enable = SA0305   # But re-enable one specific type check
```

### 6.5 Config Format Upgrade

**Gap:** Current INI-like format is limited and doesn't support nested structures well.

**Decision needed:** Stay with INI (simple, matches current codebase) or move to TOML (richer, supports per-file overrides naturally). Consider supporting both during transition.

---

## 7. Gap Analysis — Suppression System

### 7.1 Unused Suppression Detection

**Gap:** Suppression directives that suppress no actual diagnostic are silently ignored. In Ruff, `RUF100` flags these. In ESLint, `reportUnusedDisableDirectives` catches them.

**Proposal:** New diagnostic:

- **SA0003** — Unused suppression directive: `// stash-disable SA0201` on a line with no SA0201 violation

This is critical for codebase hygiene — stale suppressions hide real problems.

### 7.2 File-Level Suppression

**Gap:** No way to suppress all diagnostics in an entire file with a single directive.

**Proposal:**

```stash
// stash-disable-file SA0201
// stash-disable-file          // suppress all
```

### 7.3 Suppression Reason Enforcement

**Current:** `require-suppression-reason` is parsed in ProjectConfig but enforcement is not clearly tested.

**Proposal:** Ensure this is robustly enforced, and extend to support reason text in the comment:

```stash
// stash-disable SA0201 -- Legacy code, tracked in PROJ-456
```

---

## 8. Gap Analysis — Diagnostic Quality

### 8.1 Related Diagnostic Locations

**Gap:** Diagnostics have a single `SourceSpan`. When reporting "variable X is unused", there's no link to where it was declared vs. where it was expected to be used.

**Industry Pattern:** SARIF supports `relatedLocations`. LSP supports `DiagnosticRelatedInformation`.

**Proposal:** Extend `SemanticDiagnostic`:

```csharp
public record RelatedLocation(string Message, SourceSpan Span, Uri? Uri);

// In SemanticDiagnostic:
public IReadOnlyList<RelatedLocation> RelatedLocations { get; }
```

Use cases:

- "Declared here" / "Also defined here" (for duplicate declarations)
- "Imported from" (for import-related diagnostics)
- "Expected type from" (for type mismatch, point to the annotation)

### 8.2 Diagnostic Tags

**Gap:** Only `IsUnnecessary` tag exists. SARIF/LSP also support "deprecated".

**Proposal:** Add `IsDeprecated` flag to `SemanticDiagnostic` for deprecated API usage warnings. The LSP should map this to the `deprecated` diagnostic tag.

### 8.3 Diagnostic Documentation URLs

**Gap:** Diagnostics have no URL pointing to documentation. ESLint rules link to docs, Ruff links to rule pages.

**Proposal:** Each `DiagnosticDescriptor` gets a `HelpUrl` property:

```csharp
public string? HelpUrl => $"https://stash-lang.dev/docs/rules/{Code}";
```

Displayed in SARIF output and LSP hover for diagnostics.

---

## 9. Gap Analysis — Testing & Tooling

### 9.1 Missing Test Coverage

The following diagnostic codes lack explicit, dedicated test cases:

- **SA0701** (nested elevate) — no dedicated test
- **SA0708** (backoff without delay) — indirect coverage only
- **SA0001, SA0002** (infrastructure/suppression) — indirect coverage only
- **SA0104** (unreachable code) — needs edge cases (throw, both-branches-return)

### 9.2 Rule Performance Profiling

**Gap:** No way to measure how long each analysis pass/rule takes.

**Industry Pattern:** ESLint's `TIMING=1` environment variable shows per-rule timing.

**Proposal:** `stash-check --timing` outputs per-pass timings:

```
Pass             | Time (ms)
-----------------|----------
Lexer            |     2.3
Parser           |     4.1
SymbolCollector  |     1.8
ImportResolver   |     3.2
TypeInference    |     0.9
SemanticValidator|    12.4
Suppression      |     0.3
Total            |    25.0
```

### 9.3 Rule Testing Framework

**Gap:** Tests use raw string source → Validate() → assert on diagnostics. No structured way to verify fix output.

**Proposal (future):** Once autofix exists, add a `FixVerifier` test helper:

```csharp
FixVerifier.Verify(
    source: "let x = 5; print(x);",
    fixedSource: "const x = 5; print(x);",
    diagnosticId: "SA0205"
);
```

---

## 10. Phased Roadmap

### Phase 1 — Foundation & Quick Wins (Est. Scope: Medium)

**Goal:** Improve daily usability, add missing low-hanging diagnostics, fix existing gaps.

1. **Human-readable text output** — Add `TextFormatter`, make it the default. SARIF via `--format sarif`.
2. **Implement `--no-imports`** — Wire up the already-parsed flag to AnalysisEngine.
3. **Implement `--statistics`** — Summary mode showing violation counts per rule.
4. **Add `--show-files`** — List files that would be analyzed.
5. **Add 4 high-value diagnostics:**
   - SA0205: `let`-could-be-`const`
   - SA0206: Unused parameter (opt-in, respecting `_` prefix)
   - SA0207: Shadow variable
   - SA0105: Empty block body
6. **Unused suppression detection (SA0003)** — Flag `// stash-disable` that suppress nothing.
7. **Fill test gaps** — Explicit tests for SA0701, SA0708, SA0001, SA0002, SA0104 edge cases.
8. **Content-hash caching** — Cache analysis results by file content hash for stash-check.
9. **Diagnostic help URLs** — Add `HelpUrl` to DiagnosticDescriptor.

### Phase 2 — Autofix Infrastructure (Est. Scope: Large)

**Goal:** Enable code fixes in both CLI and LSP, unlocking a major category of user value.

1. **CodeFix model** — `CodeFix` record with `TextEdit[]`, `FixApplicability`, `Title`.
2. **Extend DiagnosticDescriptor** — Add `IsFixable`, `FixApplicability` fields.
3. **Extend SemanticDiagnostic** — Add `Fixes: IReadOnlyList<CodeFix>`.
4. **Implement fixes for existing diagnostics:**
   - SA0203 constant reassignment → suggest changing `const` to `let` (unsafe)
   - SA0802 unused import → remove import statement (safe)
   - SA0205 let→const → change `let` to `const` (safe)
5. **`stash-check --fix`** — Apply safe fixes, rewrite files.
6. **`stash-check --fix --unsafe-fixes`** — Apply all fixes.
7. **`stash-check --diff`** — Show fixes as unified diff without applying.
8. **LSP code actions** — Map CodeFix to LSP `CodeAction` for quick-fix in editor.
9. **Fix conflict resolution** — When multiple fixes overlap, pick the first / most specific.

### Phase 3 — Rule Architecture Refactor (Est. Scope: Large)

**Goal:** Move from monolithic validator to isolated, composable rules.

1. **Define `IAnalysisRule` interface** — `Descriptor`, `SubscribedNodeTypes`, `Analyze(context)`.
2. **Rule registry** — Central registry that discovers and instantiates rules.
3. **Rule context** — `AnalysisContext` providing scope tree, type inference, diagnostic reporting, fix suggestions.
4. **Migrate existing checks** — Extract each SemanticValidator check into its own `IAnalysisRule` implementation. One rule per file in new `Stash.Analysis/Rules/` directory:
   - `Rules/ControlFlow/BreakOutsideLoopRule.cs` (SA0101)
   - `Rules/ControlFlow/ContinueOutsideLoopRule.cs` (SA0102)
   - `Rules/ControlFlow/ReturnOutsideFunctionRule.cs` (SA0103)
   - `Rules/ControlFlow/UnreachableCodeRule.cs` (SA0104)
   - `Rules/Declarations/UnusedDeclarationRule.cs` (SA0201)
   - `Rules/Declarations/UndefinedIdentifierRule.cs` (SA0202)
   - ... etc.
5. **Rule filtering in AnalysisEngine** — Only run rules that are enabled in config.
6. **SemanticValidator becomes thin** — Hosts rule dispatcher, delegates to individual rules.
7. **Rule metadata enrichment** — Each rule declares: category, default severity, fixable, documentation URL, related rules.

### Phase 4 — Advanced Configuration (Est. Scope: Medium)

**Goal:** Bring configuration to parity with Ruff/ESLint.

1. **Hierarchical config** — Walk directory tree, merge configs, support `extend`.
2. **Per-file rule overrides** — `[per-file-overrides]` section in config.
3. **Rule selection by prefix** — `enable = SA01`, `disable = SA03`.
4. **Rule profiles** — `preset = recommended | strict | minimal`.
5. **CLI rule selection** — `--select SA0201,SA0205 --ignore SA0207`.
6. **File-level suppression** — `// stash-disable-file`.
7. **Auto-add suppressions** — `stash-check --add-suppress`.
8. **Stdin support** — `stash-check --stdin-filename main.stash -`.

### Phase 5 — Flow Analysis & Advanced Diagnostics (Est. Scope: Very Large)

**Goal:** Build CFG infrastructure and enable flow-sensitive diagnostics.

1. **CFG construction** — Build `BasicBlock` graph from AST after parsing.
2. **Flow-sensitive unreachable code** — Detect unreachable code after all branches return/throw.
3. **Definite assignment** — Variable used before initialization on some paths.
4. **Type narrowing** — Populate `Scope.TypeNarrowings` from `is` checks, null guards, truthiness checks.
5. **Null safety analysis** — Track possible null through assignments and branches.
6. **Dead store detection** — Variable assigned but overwritten before read.
7. **Exhaustive match analysis** — All enum variants covered.
8. **Missing return analysis** — Function with return type annotation has paths without return.

### Phase 6 — Polish & Ecosystem (Est. Scope: Medium)

**Goal:** Professional-grade tooling and community features.

1. **Related diagnostic locations** — Add `RelatedLocations` to SemanticDiagnostic.
2. **Deprecated API tag** — `IsDeprecated` diagnostic tag.
3. **Watch mode** — `stash-check --watch`.
4. **Additional output formats** — JSON, GitHub annotations, grouped.
5. **Rule performance profiling** — `--timing` flag.
6. **Diagnostic documentation site** — One page per SA-code with examples, rationale, fix guidance.
7. **Complexity metrics** — Cyclomatic complexity (SA0109), function length (SA0405).
8. **Naming convention rules** — Configurable naming patterns for variables, functions, structs.
9. **Import organization** — Canonical import ordering with autofix.
10. **Fix verifier test helpers** — Structured test framework for validating fixes.

---

## 11. Decision Log

### D1: Rule Architecture Pattern

**Decision:** `IAnalysisRule` interface with AST node subscriptions.
**Alternatives considered:**

- (a) ESLint-style event emitter (emit node type as event name) — too loose for a statically-typed language; poor discoverability
- (b) Roslyn-style `RegisterSyntaxNodeAction<T>()` — closest match to existing pattern, but requires generic dispatch infrastructure
- (c) Visitor-per-rule (each rule is a full IStmtVisitor/IExprVisitor) — too much boilerplate per rule, most rules only care about 1-3 node types
  **Rationale:** Interface with subscribed node types gives the dispatcher enough info to only invoke relevant rules, while keeping rule implementations minimal. The `AnalysisContext` avoids coupling rules to infrastructure.
  **Risk:** Interface may need extension as more complex rules are encountered (e.g., rules that need start/end events for scopes). Mitigate by including an optional `Initialize(AnalysisSession)` method.

### D2: Fix Applicability Model

**Decision:** Three-tier: Safe / Unsafe / Suggestion (per Ruff model).
**Alternatives considered:**

- (a) Binary fixable/not-fixable (ESLint v8 style) — too coarse
- (b) Four-tier with "display-only" (Roslyn) — display-only is a subset of Suggestion
  **Rationale:** Safe/Unsafe maps cleanly to `--fix` vs `--fix --unsafe-fixes` CLI. Suggestion covers LSP-only code actions with multiple alternatives.

### D3: Config Format

**Decision:** Keep INI-like format for Phase 1-4. Evaluate TOML migration in Phase 4 if nesting becomes painful.
**Rationale:** INI is already shipped and in use. TOML would be a breaking change to `.stashcheck`. The `[per-file-overrides]` section can be expressed in INI with quoted-key syntax, deferring the TOML decision.
**Risk:** INI may feel limiting for complex per-file override patterns. TOML migration path should be designed even if deferred.

### D4: Migration Strategy for Monolithic Validator

**Decision:** Incremental extraction — rules extracted one at a time from SemanticValidator.
**Alternatives considered:**

- (a) Big-bang rewrite — risky, blocks all other work
- (b) Keep monolithic, just add new rules as IAnalysisRule — inconsistent, two patterns forever
  **Rationale:** Incremental extraction keeps the test suite green at every step and allows proving the interface design before committing to full migration. Target: fully migrated by end of Phase 3.

### D5: Content-Hash Caching Strategy

**Decision:** File content SHA-256 hash → cached analysis result, stored as binary in `.stash-cache/` directory.
**Alternatives considered:**

- (a) mtime-based caching — unreliable across git operations (checkout changes mtime without content change)
- (b) AST-hash caching — expensive to compute (requires parsing first), marginal benefit over content hash
  **Rationale:** Content hash is fast (SHA-256 of file bytes), reliable, and catches all changes. Cache stored per-project, excluded from version control.
  **Risk:** Cache invalidation when analyzer rules change (new rules, changed severity). Mitigate by including analyzer version in cache key.

---

## Appendix A: Diagnostic Code Allocation Plan

Reserving code ranges for future diagnostics:

| Range  | Category          | Current       | Planned                     |
| ------ | ----------------- | ------------- | --------------------------- |
| SA00xx | Infrastructure    | SA0001-SA0003 | SA0003 (unused suppression) |
| SA01xx | Control flow      | SA0101-SA0104 | SA0105-SA0111               |
| SA02xx | Declarations      | SA0201-SA0203 | SA0205-SA0210               |
| SA03xx | Type safety       | SA0301-SA0305 | SA0306-SA0312               |
| SA04xx | Functions & calls | SA0401-SA0403 | SA0404-SA0406               |
| SA05xx | Spread/rest       | SA0501-SA0506 | SA0507                      |
| SA06xx | Style & naming    | (unused)      | SA0601-SA0610               |
| SA07xx | Commands          | SA0701-SA0709 | SA0710                      |
| SA08xx | Imports           | SA0801        | SA0802-SA0804               |
| SA09xx | Complexity        | (unused)      | SA0901-SA0905               |

## Appendix B: Key Source Files Reference

| File                                                  | Role                                               |
| ----------------------------------------------------- | -------------------------------------------------- |
| `Stash.Analysis/Engines/AnalysisEngine.cs`            | Pipeline orchestrator, cache, dependency tracking  |
| `Stash.Analysis/Engines/TypeInferenceEngine.cs`       | Forward-only type inference, 11 rules              |
| `Stash.Analysis/Visitors/SemanticValidator.cs`        | Monolithic validation visitor (~1,200 lines)       |
| `Stash.Analysis/Visitors/SymbolCollector.cs`          | Scope tree builder, symbol/reference registration  |
| `Stash.Analysis/Resolvers/ImportResolver.cs`          | Cross-file import resolution, dependency tracking  |
| `Stash.Analysis/Models/DiagnosticDescriptors.cs`      | Single source of truth for all 33 diagnostic codes |
| `Stash.Analysis/Models/ScopeTree.cs`                  | Symbol table queries, definition/reference lookup  |
| `Stash.Analysis/Models/ProjectConfig.cs`              | .stashcheck configuration loader                   |
| `Stash.Analysis/Models/SuppressionDirectiveParser.cs` | Comment-based suppression parsing                  |
| `Stash.Check/Program.cs`                              | CLI entry point                                    |
| `Stash.Check/CheckRunner.cs`                          | File discovery, analysis orchestration             |
| `Stash.Check/Formatters/SarifFormatter.cs`            | SARIF v2.1.0 output                                |
