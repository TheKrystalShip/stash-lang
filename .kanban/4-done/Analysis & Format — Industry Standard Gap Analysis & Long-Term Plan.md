# Analysis & Format — Industry Standard Gap Analysis & Long-Term Plan

**Status:** Backlog — Design
**Created:** 2026-04-10
**Scope:** Stash.Analysis, Stash.Format, Stash.Check

---

## 1. Executive Summary

This spec compares Stash's static analysis engine (`Stash.Analysis`) and code formatter (`Stash.Format` / `StashFormatter`) against industry-standard tools — ESLint, Biome, Roslyn Analyzers, Prettier, and rustfmt — to identify architectural gaps, missing patterns, and absent features. It establishes a phased, long-term plan to bring both tools to a mature, production-quality standard.

**Current state:**

- **Analysis:** 33 diagnostics, scope tree, basic type inference, cross-file imports, suppression directives. No code fixes, no data flow, no CFG, no incremental analysis, no plugin system.
- **Formatter:** Deterministic single-pass AST visitor, trivia preservation, idempotent. No IR layer, no line-width wrapping, no range formatting, no configurable style rules.

**Target state:** A professional-grade toolchain where the analysis engine provides actionable diagnostics with auto-fixes, the formatter intelligently wraps code to a line width using an IR, and both tools are configurable, incremental, and deeply integrated with the LSP.

---

## 2. Industry Standard Comparison

### 2.1 Formatter Comparison

| Feature                  | Prettier                                     | Biome                        | rustfmt               | **Stash.Format**                    |
| ------------------------ | -------------------------------------------- | ---------------------------- | --------------------- | ----------------------------------- |
| IR / Document model      | Wadler-Lindig IR (group, fill, indent, line) | Wadler-Lindig IR             | IR-based              | **None — direct AST printing**      |
| Line width / print width | ✅ (80 default, configurable)                | ✅ (80 default)              | ✅ (100 default)      | **❌**                              |
| Smart line breaking      | ✅ (fit-or-break groups)                     | ✅                           | ✅                    | **❌ (source-span heuristic only)** |
| Range formatting         | ✅ (--range-start/end)                       | ❌                           | ✅                    | **❌**                              |
| Trailing commas          | ✅ (configurable)                            | ✅ (configurable)            | ✅                    | **❌ (hardcoded: none)**            |
| End-of-line config       | ✅ (lf/crlf/cr/auto)                         | ✅ (lf/crlf)                 | ✅                    | **❌ (hardcoded: LF)**              |
| Ignore comments          | ✅ (prettier-ignore)                         | ✅ (biome-ignore format)     | ✅ (#[rustfmt::skip]) | **❌**                              |
| Ignore file pragma       | ✅ (@format/@noprettier)                     | ✅ (biome-ignore-all format) | ✅                    | **❌**                              |
| .editorconfig support    | ✅                                           | ✅                           | ❌                    | **❌**                              |
| Config file              | ✅ (.prettierrc)                             | ✅ (biome.json)              | ✅ (rustfmt.toml)     | **❌ (CLI flags only)**             |
| Bracket spacing config   | ✅                                           | ✅                           | N/A                   | **❌ (hardcoded)**                  |
| Idempotent               | ✅                                           | ✅                           | ✅                    | **✅**                              |
| Comment preservation     | ✅                                           | ✅                           | ✅                    | **✅**                              |
| Multi-line heuristics    | ✅ (object-wrap option)                      | ✅                           | ✅                    | **✅ (source-span based)**          |

### 2.2 Analysis / Linter Comparison

| Feature                      | ESLint                          | Biome                                                              | Roslyn                           | **Stash.Analysis**                             |
| ---------------------------- | ------------------------------- | ------------------------------------------------------------------ | -------------------------------- | ---------------------------------------------- |
| Total rules                  | 300+                            | 472                                                                | 100s (built-in + community)      | **33**                                         |
| Rule categories              | problem/suggestion/layout       | correctness/suspicious/style/complexity/perf/a11y/security/nursery | multiple                         | **6 (control flow/decl/type/func/spread/cmd)** |
| Code fixes (auto-fix)        | ✅ (safe fixes per rule)        | ✅ (safe + unsafe distinction)                                     | ✅ (CodeFixProvider)             | **❌**                                         |
| Code suggestions             | ✅ (non-auto alternatives)      | ✅                                                                 | ✅ (refactorings)                | **❌**                                         |
| Plugin / extensibility       | ✅ (plugin system)              | ✅ (GritQL plugins)                                                | ✅ (NuGet analyzers)             | **❌**                                         |
| Per-rule options             | ✅ (schema-validated)           | ✅                                                                 | ✅                               | **❌**                                         |
| Rule groups / profiles       | ✅ (recommended, all, custom)   | ✅ (recommended, all, nursery)                                     | ✅ (severity sets)               | **Partial (.stashcheck disable/severity)**     |
| Domains / contexts           | ❌                              | ✅ (react, solid, test, project)                                   | N/A                              | **❌**                                         |
| Data flow analysis           | Partial (code paths API)        | ✅ (v2 — type inference, module graph)                             | ✅ (full DFA)                    | **❌**                                         |
| Control flow graph           | ✅ (code path events)           | ✅                                                                 | ✅ (full CFG)                    | **❌**                                         |
| Unused imports               | ✅                              | ✅ (noUnusedImports)                                               | ✅                               | **✅ (SA0201, but coarse)**                    |
| Unused variables             | ✅ (no-unused-vars)             | ✅ (noUnusedVariables)                                             | ✅                               | **✅ (SA0201)**                                |
| Unreachable code             | ✅ (no-unreachable)             | ✅                                                                 | ✅                               | **✅ (SA0104)**                                |
| Complexity metrics           | ✅ (max-complexity, max-depth)  | ✅ (noExcessiveCognitiveComplexity)                                | ❌ (third party)                 | **❌**                                         |
| Naming conventions           | ✅ (camelcase, etc.)            | ✅ (useNamingConvention)                                           | ✅ (IDE naming rules)            | **❌**                                         |
| Best practices               | ✅ (no-eval, no-debugger, etc.) | ✅ (100+ rules)                                                    | ✅                               | **❌**                                         |
| Performance lint rules       | ❌                              | ✅ (noAccumulatingSpread, etc.)                                    | ✅ (CA performance)              | **❌**                                         |
| Security rules               | ❌ (third party)                | ✅ (noDangerouslySetInnerHtml, etc.)                               | ✅ (CA security)                 | **❌**                                         |
| Incremental analysis         | ✅ (per-file, cached)           | ✅ (per-file)                                                      | ✅ (Roslyn incremental pipeline) | **❌ (full re-analysis)**                      |
| SARIF output                 | ❌ (third party)                | ❌                                                                 | ✅                               | **✅ (via Stash.Check)**                       |
| Diagnostic severity override | ✅                              | ✅                                                                 | ✅                               | **✅ (.stashcheck)**                           |
| Exit code semantics          | ✅                              | ✅ (--error-on-warnings)                                           | N/A                              | **✅**                                         |
| Rule documentation URLs      | ✅                              | ✅                                                                 | ✅                               | **❌**                                         |
| Per-rule perf profiling      | ✅ (TIMING env var)             | ✅                                                                 | ✅                               | **❌**                                         |

---

## 3. Gap Analysis — Prioritized

### 3.1 Critical Gaps (P0) — These are table-stakes for a serious tool

| #      | Gap                            | Impact                                                                                           | Effort               |
| ------ | ------------------------------ | ------------------------------------------------------------------------------------------------ | -------------------- |
| **F1** | **No IR layer in formatter**   | Cannot do intelligent line-breaking; multi-line decisions are fragile source-span heuristics     | High                 |
| **A1** | **No code fixes / auto-fixes** | Users get told about problems but must fix them manually — the #1 feature request for any linter | High                 |
| **A2** | **Only 33 diagnostic rules**   | Tiny rule set compared to 300-472 rules in competing tools                                       | Ongoing              |
| **F2** | **No line-width wrapping**     | Cannot enforce consistent line length — fundamental formatter capability                         | High (depends on F1) |

### 3.2 High Priority Gaps (P1) — Expected by power users and CI pipelines

| #      | Gap                                      | Impact                                                                             | Effort         |
| ------ | ---------------------------------------- | ---------------------------------------------------------------------------------- | -------------- |
| **F3** | **No formatter ignore comments**         | Cannot exclude specific code blocks from formatting (e.g., alignment tables)       | Low            |
| **F4** | **No range formatting**                  | LSP range formatting is faked; cannot format selection in editor                   | Medium         |
| **F5** | **No config file**                       | All formatting rules are hardcoded; no `.stashformat` or unified config            | Medium         |
| **F6** | **No trailing comma option**             | Common stylistic preference with no control                                        | Low (needs F5) |
| **F7** | **No end-of-line configuration**         | Cross-platform teams can't choose CRLF vs LF                                       | Low (needs F5) |
| **A3** | **No control flow graph (CFG)**          | Cannot do advanced dead code detection, definite assignment, or code path analysis | High           |
| **A4** | **No complexity metrics**                | Cannot warn about overly complex functions (cyclomatic/cognitive complexity)       | Medium         |
| **A5** | **No naming convention rules**           | Cannot enforce camelCase, PascalCase, snake_case conventions                       | Medium         |
| **A6** | **No code suggestions**                  | Cannot offer non-auto alternatives (e.g., "consider using const")                  | Medium         |
| **A7** | **No documentation URLs in diagnostics** | Users can't click through to learn about a rule                                    | Low            |

### 3.3 Medium Priority Gaps (P2) — Differentiation and professional polish

| #       | Gap                              | Impact                                                                                                            | Effort          |
| ------- | -------------------------------- | ----------------------------------------------------------------------------------------------------------------- | --------------- |
| **F8**  | **No .editorconfig support**     | Teams with existing .editorconfig can't share indent/line-width settings                                          | Medium          |
| **F9**  | **No bracket spacing option**    | Struct/dict literal brace spacing is hardcoded                                                                    | Low (needs F5)  |
| **F10** | **No file-level ignore pragma**  | Cannot mark generated files as unformattable                                                                      | Low             |
| **A8**  | **No data flow analysis**        | Cannot track value propagation, taint, null-definite-assignment                                                   | Very High       |
| **A9**  | **No inter-procedural analysis** | Cannot trace issues across function boundaries                                                                    | Very High       |
| **A10** | **No best-practice rules**       | Missing: no-shadow, no-redeclare, prefer-const, no-self-assign, no-duplicate-case, no-fallthrough, no-empty, etc. | Medium per rule |
| **A11** | **No performance lint rules**    | Cannot detect accumulating spreads in loops, unnecessary copies, etc.                                             | Medium          |
| **A12** | **No incremental analysis**      | Full re-analysis on every keystroke; scales poorly for large projects                                             | High            |
| **A13** | **No per-rule options**          | Rules are binary on/off; cannot customize thresholds or behaviors                                                 | Medium          |
| **A14** | **No rule profiling**            | Cannot identify slow rules that degrade LSP responsiveness                                                        | Low             |

### 3.4 Future / Aspirational (P3) — Nice to have, builds ecosystem

| #       | Gap                                | Impact                                                                | Effort          |
| ------- | ---------------------------------- | --------------------------------------------------------------------- | --------------- |
| **A15** | **No plugin/extensibility system** | Users can't write custom rules                                        | Very High       |
| **A16** | **No security rules**              | Cannot detect command injection, path traversal, etc.                 | Medium per rule |
| **A17** | **No domain-based rule grouping**  | Cannot auto-enable rules based on context (test files vs. production) | Medium          |
| **F11** | **No sort imports**                | Prettier doesn't do this either (intentionally), but Biome does       | Medium          |
| **A18** | **No advanced type inference**     | No union types, generics, constraint checking                         | Very High       |

---

## 4. Architecture Recommendations

### 4.1 Formatter — Introduce an IR Layer

**Current:** Direct AST → string via visitor pattern with `PendingWs` state machine.

**Problem:** Cannot reason about line-fitting. The formatter has no concept of "this group of tokens should stay on one line if they fit, otherwise break." Multi-line decisions rely entirely on the source's original span, which means the formatter can't reflow code.

**Proposed IR (Wadler-Lindig document model):**

```
Doc =
  | Text(string)           -- literal text
  | Line                   -- line break or space (in flat mode)
  | HardLine               -- always a line break
  | SoftLine               -- line break or nothing (in flat mode)
  | Indent(Doc)            -- increase indent level
  | Dedent(Doc)            -- decrease indent level
  | Group(Doc)             -- try to fit on one line; break if it doesn't fit
  | Fill(Doc[])            -- fill as many items per line as possible
  | IfBreak(Doc, Doc)      -- choose based on whether group breaks
  | LineSuffix(Doc)        -- append after the line (for trailing comments)
  | Concat(Doc[])          -- concatenate documents
```

**Migration path:**

1. Define `Doc` type (discriminated union or sealed hierarchy)
2. Implement `PrintDoc(Doc, lineWidth, indentWidth) → string` (the Wadler-Lindig algorithm)
3. Rewrite formatter visitors to emit `Doc` instead of writing to `StringBuilder`
4. Add `--print-width` option
5. The old `PendingWs` approach naturally maps to this model

**Key design decisions:**

- `Group` is the core primitive that enables fit-or-break behavior
- `Fill` handles comma-separated lists that should pack as many items per line as possible
- `LineSuffix` handles trailing comments elegantly
- The IR is inspectable/debuggable (can dump the document tree)

### 4.2 Analysis — Code Fix Architecture

**Inspired by:** Roslyn `CodeFixProvider` + ESLint `fix(fixer)` + Biome safe/unsafe distinction.

**Proposed model:**

```
CodeFix:
  DiagnosticCode: string         -- which diagnostic this fixes
  Title: string                  -- displayed in editor lightbulb
  IsSafe: bool                   -- safe = won't change semantics
  Edits: TextEdit[]              -- source text changes to apply

TextEdit:
  Span: SourceSpan               -- range to replace
  NewText: string                -- replacement text
```

**Integration points:**

- `SemanticValidator` emits `(diagnostic, codeFix?)` pairs
- LSP `CodeActionHandler` converts `CodeFix` → LSP `CodeAction`
- `stash-check --fix` applies safe fixes automatically
- `stash-check --fix --unsafe` applies all fixes

**Safe vs. unsafe distinction (from Biome):**

- **Safe:** Guaranteed to preserve semantics (e.g., remove unused import, add missing semicolon)
- **Unsafe:** May change semantics (e.g., rename variable, rewrite expression)

### 4.3 Analysis — Control Flow Graph

**Prerequisite for:** definite assignment, exhaustive code path analysis, advanced dead code detection, data flow analysis.

**Proposed model:**

```
CFG:
  EntryBlock: BasicBlock
  ExitBlock: BasicBlock
  Blocks: BasicBlock[]

BasicBlock:
  Id: int
  Statements: Stmt[]
  Terminator: Branch | Return | Throw | Fallthrough
  Successors: BasicBlock[]
  Predecessors: BasicBlock[]
```

**Build from:** AST → CFG transformation pass (walks function bodies)
**Use for:**

- Definite assignment: "variable used before initialization"
- Exhaustive return: "not all code paths return a value"
- True dead code: unreachable blocks (not just post-return statements)
- Data flow analysis (future)

### 4.4 Analysis — Rule Category Expansion

**Current categories:** Control Flow, Declarations, Type Safety, Functions, Spread, Commands, Imports.

**Proposed new categories (inspired by Biome/ESLint):**

| Category           | Code Range | Example Rules                                                             |
| ------------------ | ---------- | ------------------------------------------------------------------------- |
| **Style**          | SA09xx     | naming-conventions, prefer-const, no-unnecessary-else, consistent-return  |
| **Complexity**     | SA10xx     | max-complexity, max-depth, max-params, max-lines-per-function             |
| **Best Practices** | SA11xx     | no-shadow, no-self-assign, no-duplicate-case, no-empty, no-lone-blocks    |
| **Performance**    | SA12xx     | no-accumulating-spread-in-loop, prefer-for-over-foreach-when-index-needed |
| **Security**       | SA13xx     | no-hardcoded-credentials, no-command-injection-risk                       |
| **Suggestions**    | SA14xx     | use-optional-chaining, use-nullish-coalescing, use-template-literal       |

---

## 5. Implementation Phases

### Phase 1: Formatter Foundations (P0 + quick P1 wins)

**Goal:** Make the formatter configurable and add critical missing features.

| Task | Item                                                               | Effort | Depends On |
| ---- | ------------------------------------------------------------------ | ------ | ---------- |
| 1.1  | Formatter ignore comments (`// stash-ignore format`)               | Low    | —          |
| 1.2  | File-level ignore pragma (`// stash-ignore-all format: reason`)    | Low    | —          |
| 1.3  | Configuration file (`.stashformat` or section in `.stashcheck`)    | Medium | —          |
| 1.4  | Trailing commas option (none/all)                                  | Low    | 1.3        |
| 1.5  | End-of-line option (lf/crlf/auto)                                  | Low    | 1.3        |
| 1.6  | Bracket spacing option                                             | Low    | 1.3        |
| 1.7  | .editorconfig support (indent_size, indent_style, max_line_length) | Medium | 1.3        |

### Phase 2: Code Fix Infrastructure (P0)

**Goal:** Enable auto-fixable diagnostics in the analysis engine.

| Task | Item                                                                                  | Effort | Depends On |
| ---- | ------------------------------------------------------------------------------------- | ------ | ---------- |
| 2.1  | Define `CodeFix`, `TextEdit`, `FixKind` (safe/unsafe) models                          | Low    | —          |
| 2.2  | Extend `SemanticDiagnostic` with optional `CodeFix` field                             | Low    | 2.1        |
| 2.3  | Implement fixes for existing rules: SA0201 (remove unused import), SA0203 (const→let) | Medium | 2.2        |
| 2.4  | LSP `CodeActionHandler` to surface fixes as lightbulb actions                         | Medium | 2.2        |
| 2.5  | `stash-check --fix` and `--fix --unsafe` CLI modes                                    | Medium | 2.2        |
| 2.6  | Add `fix` field to `.stashcheck` per-rule config (none/safe/unsafe)                   | Low    | 2.5        |

### Phase 3: Rule Expansion — Best Practices & Style (P1)

**Goal:** Grow the rule set from 33 to ~55-60 rules with the most impactful additions.

| Task | Item                                                                                | Category       | Has Fix?      |
| ---- | ----------------------------------------------------------------------------------- | -------------- | ------------- |
| 3.1  | `SA0901` no-unnecessary-else                                                        | Style          | Safe          |
| 3.2  | `SA0902` prefer-const (let → const when never reassigned)                           | Style          | Safe          |
| 3.3  | `SA0903` naming-convention (configurable patterns per SymbolKind)                   | Style          | Unsafe        |
| 3.4  | `SA0904` consistent-return (all paths return or none do)                            | Style          | No            |
| 3.5  | `SA0905` no-unnecessary-spread (spreading a value that's already the right type)    | Style          | Safe          |
| 3.6  | `SA1001` max-complexity (cyclomatic, configurable threshold)                        | Complexity     | No            |
| 3.7  | `SA1002` max-depth (max nesting depth, configurable)                                | Complexity     | No            |
| 3.8  | `SA1003` max-params (max function parameter count)                                  | Complexity     | No            |
| 3.9  | `SA1101` no-shadow (inner variable shadows outer)                                   | Best Practices | No            |
| 3.10 | `SA1102` no-self-assign (`x = x`)                                                   | Best Practices | Safe (remove) |
| 3.11 | `SA1103` no-duplicate-case (duplicate values in switch)                             | Best Practices | No            |
| 3.12 | `SA1104` no-empty (empty block bodies)                                              | Best Practices | No            |
| 3.13 | `SA1105` no-lone-blocks (blocks that don't introduce scope)                         | Best Practices | Safe (unwrap) |
| 3.14 | `SA1106` no-self-compare (`x == x`)                                                 | Best Practices | No            |
| 3.15 | `SA1107` no-constant-condition (`if (true)`, `while (false)`)                       | Best Practices | No            |
| 3.16 | `SA1108` no-unreachable-loop (loop body always breaks/returns on first iteration)   | Best Practices | No            |
| 3.17 | `SA1401` use-optional-chaining (suggest `a?.b` instead of `a != null ? a.b : null`) | Suggestions    | Unsafe        |
| 3.18 | `SA1402` use-null-coalescing (suggest `a ?? b` instead of `a != null ? a : b`)      | Suggestions    | Unsafe        |

### Phase 4: Formatter IR & Line-Width Wrapping (P0)

**Goal:** Introduce the Wadler-Lindig IR and intelligent line breaking.

| Task | Item                                                                                                     | Effort | Depends On |
| ---- | -------------------------------------------------------------------------------------------------------- | ------ | ---------- |
| 4.1  | Define `Doc` IR types (Text, Line, SoftLine, HardLine, Indent, Group, Fill, IfBreak, LineSuffix, Concat) | Medium | —          |
| 4.2  | Implement `DocPrinter` (the fit-checking, line-breaking algorithm)                                       | High   | 4.1        |
| 4.3  | Rewrite `StashFormatter` visitors to emit `Doc` instead of `StringBuilder`                               | High   | 4.1, 4.2   |
| 4.4  | Add `--print-width` / `printWidth` option (default: 80)                                                  | Low    | 4.3        |
| 4.5  | Implement `Fill` for comma-separated lists (arrays, params, struct fields)                               | Medium | 4.3        |
| 4.6  | Handle trailing comments via `LineSuffix`                                                                | Medium | 4.3        |
| 4.7  | Exhaustive idempotency testing (format(format(x)) == format(x) for all examples/)                        | Medium | 4.3        |
| 4.8  | Range formatting support (LSP + CLI `--range-start/--range-end`)                                         | Medium | 4.3        |

### Phase 5: Control Flow Graph & Advanced Analysis (P1-P2)

**Goal:** Build the infrastructure for advanced diagnostics.

| Task | Item                                                                                   | Effort | Depends On |
| ---- | -------------------------------------------------------------------------------------- | ------ | ---------- |
| 5.1  | Define `BasicBlock`, `CFG`, `Terminator` models                                        | Medium | —          |
| 5.2  | Implement AST → CFG builder pass                                                       | High   | 5.1        |
| 5.3  | `SA0105` definite-assignment (variable used before initialization)                     | Medium | 5.2        |
| 5.4  | `SA0106` exhaustive-return (not all code paths return a value)                         | Medium | 5.2        |
| 5.5  | Improve `SA0104` unreachable code detection using CFG (currently post-terminator only) | Medium | 5.2        |
| 5.6  | `SA1108` no-unreachable-loop using CFG analysis                                        | Medium | 5.2        |

### Phase 6: Diagnostic Documentation & UX Polish (P1)

**Goal:** Make diagnostics self-explanatory and easier to work with.

| Task | Item                                                                    | Effort | Depends On |
| ---- | ----------------------------------------------------------------------- | ------ | ---------- |
| 6.1  | Add `HelpUrl` field to `DiagnosticDescriptor`                           | Low    | —          |
| 6.2  | Generate rule documentation pages (one page per SA code)                | Medium | 6.1        |
| 6.3  | Include URL in SARIF output from `stash-check`                          | Low    | 6.1        |
| 6.4  | Include URL in LSP diagnostic `codeDescription.href`                    | Low    | 6.1        |
| 6.5  | Per-rule configuration options in `.stashcheck`                         | Medium | —          |
| 6.6  | Rule profiling mode (`stash-check --timing`)                            | Low    | —          |
| 6.7  | Diagnostic "Related Information" locations (e.g., "first defined here") | Medium | —          |

### Phase 7: Performance & Scale (P2)

**Goal:** Make the analysis engine fast enough for large projects.

| Task | Item                                                                                | Effort | Depends On |
| ---- | ----------------------------------------------------------------------------------- | ------ | ---------- |
| 7.1  | Incremental analysis: hash-based cache invalidation per file                        | High   | —          |
| 7.2  | Incremental analysis: dependency graph for cross-file invalidation                  | Medium | 7.1        |
| 7.3  | Parallel file analysis (already using ConcurrentDictionary, but analysis is serial) | Medium | —          |
| 7.4  | Lazy rule evaluation (skip rules disabled in config before AST walk)                | Low    | —          |

### Phase 8: Advanced Rules & Ecosystem (P2-P3)

**Goal:** Reach feature parity with mid-tier linters.

| Task | Item                                                                 | Effort    | Depends On |
| ---- | -------------------------------------------------------------------- | --------- | ---------- |
| 8.1  | Performance rules: `SA1201` no-accumulating-spread-in-loop           | Medium    | —          |
| 8.2  | Security rules: `SA1301` no-hardcoded-credentials                    | Medium    | —          |
| 8.3  | Security rules: `SA1302` no-unsafe-command-interpolation             | Medium    | —          |
| 8.4  | Basic data flow analysis framework                                   | Very High | 5.2        |
| 8.5  | Null-safety analysis using DFA                                       | High      | 8.4        |
| 8.6  | Domain-based rule grouping (auto-enable test rules in \*.test.stash) | Medium    | —          |

---

## 6. Missing Patterns (Things We Should Be Doing But Aren't)

### 6.1 Formatter Patterns

1. **Ignore directives** — Every major formatter supports `// prettier-ignore`, `// biome-ignore format`, `#[rustfmt::skip]`. Stash has suppression directives for the _linter_ but not the _formatter_. This is a glaring inconsistency.

2. **Configuration file** — The formatter's rules are entirely hardcoded. Tools like Prettier deliberately minimize options but still expose ~15. Stash exposes exactly 2 (indent size, tabs). At minimum: `printWidth`, `trailingComma`, `endOfLine`, `bracketSpacing` should be configurable.

3. **IR-based printing** — Direct AST → string is the simplest approach, but it cannot reason about line fitting. Every major formatter (Prettier, Biome, rustfmt, gofmt's internal printer) uses an intermediate representation. Without this, the formatter can never intelligently reflow code.

4. **Unified configuration** — The formatter, linter, and checker each have separate config mechanisms (CLI flags, `.stashcheck`, nothing). A unified config file (like `biome.json` with `formatter` and `linter` sections) would reduce friction.

### 6.2 Analysis Patterns

1. **Code fixes are industry standard** — ESLint, Biome, and Roslyn all emit fixes alongside diagnostics. This is arguably the single most important missing feature. A diagnostic without a fix is a complaint; a diagnostic with a fix is a productivity tool.

2. **Rule categorization by intent** — ESLint uses `problem/suggestion/layout`. Biome uses `correctness/suspicious/style/complexity/performance/security`. Stash groups by language construct (control flow, declarations, etc.). Industry tools group by _intent_ — which better communicates _why_ a rule exists and lets users configure by concern.

3. **Recommended/all/off rule profiles** — Biome and ESLint ship with a `recommended` set that's enabled by default, plus `all` for strict mode. Stash's `.stashcheck` only supports disabling specific rules. Should support `profile = recommended | strict | none` as a baseline.

4. **Safe/unsafe fix classification** — Biome's distinction between safe and unsafe fixes is excellent UX. Safe fixes can be applied on save; unsafe ones require manual review. This should be baked in from the start.

5. **Related diagnostic locations** — Roslyn and Biome attach "related information" to diagnostics, e.g., "SA0201: Unused variable 'x' — defined here [link to definition]". Stash diagnostics are single-location only.

6. **Nursery/experimental rules** — Biome has a `nursery` group for rules still under development. This lowers the bar for contributing new rules without risking stability.

7. **Diagnostic "pillars" pattern** — Biome's three pillars (explain error, explain why, tell what to do) is a useful design principle for diagnostic messages. Some Stash diagnostics are terse.

---

## 7. Unified Configuration Proposal

Long-term, Stash should converge on a single configuration file (e.g., `stash.toml` or `.stashconfig`) that covers both formatting and linting:

```toml
# stash.toml

[format]
printWidth = 80
indentSize = 2
useTabs = false
endOfLine = "lf"
trailingComma = "none"
bracketSpacing = true

[lint]
profile = "recommended"     # recommended | strict | none

[lint.rules]
SA0201 = "off"              # disable unused variable warning
SA0301 = "warn"             # downgrade type mismatch to warning
SA1001 = { level = "warn", options = { maxComplexity = 15 } }

[lint.domains]
test = "recommended"        # auto-enable test rules in *.test.stash

[lint.fix]
SA0201 = "safe"             # auto-fix unused imports (safe)
SA0902 = "unsafe"           # prefer-const fix is unsafe
```

> **Decision:** This is aspirational. Phase 1 extends the existing `.stashcheck` format. A unified config is a future milestone.

---

## 8. Decision Log

| Date       | Decision                                                | Rationale                                                                                  | Alternatives Considered                                                     |
| ---------- | ------------------------------------------------------- | ------------------------------------------------------------------------------------------ | --------------------------------------------------------------------------- |
| 2026-04-10 | Phase code fixes before formatter IR                    | Code fixes are the highest-impact missing feature for users; formatter IR is higher effort | Doing IR first would unblock line-width but benefits fewer users day-to-day |
| 2026-04-10 | Wadler-Lindig IR model (not ad-hoc)                     | Industry standard; proven by Prettier, Biome, and academic literature                      | Custom fit-check heuristics (fragile, non-composable)                       |
| 2026-04-10 | Safe/unsafe fix distinction from day one                | Biome's UX is excellent; retrofitting this later is painful                                | Single fix type (ESLint's original model, less safe)                        |
| 2026-04-10 | CFG before data flow analysis                           | CFG is prerequisite for DFA; and independently enables several useful rules                | Jump straight to DFA (impossible without CFG)                               |
| 2026-04-10 | Keep categories by construct + add intent categories    | Backward compatible with existing SA code ranges; new ranges for new categories            | Renumber everything to match ESLint's model (breaking change)               |
| 2026-04-10 | Formatter ignore uses linter's directive prefix pattern | Consistency: `// stash-ignore format` mirrors `// stash-disable SA0201`                    | Prettier-style `// @format` pragmas (new pattern to learn)                  |

---

## 9. Risks & Mitigations

| Risk                                                    | Impact                             | Mitigation                                                                               |
| ------------------------------------------------------- | ---------------------------------- | ---------------------------------------------------------------------------------------- |
| IR rewrite breaks formatter idempotency                 | High — CI pipelines fail           | Exhaustive round-trip tests on all `examples/` files; phased migration with fallback     |
| Code fix infrastructure is complex                      | Medium — delays downstream rules   | Start with simple text-edit model; avoid over-engineering (no document-level transforms) |
| Rule explosion creates maintenance burden               | Medium — hard to keep quality high | Nursery group for new rules; graduated promotion; each rule requires tests               |
| CFG construction is difficult for async/try-catch       | High — incorrect analysis          | Limit initial CFG to sync code; add async/exception edges incrementally                  |
| Unified config file conflicts with existing .stashcheck | Low — migration friction           | Support both during transition; emit deprecation warning for .stashcheck                 |

---

## 10. Success Metrics

| Metric              | Current | Phase 2 Target  | Phase 4 Target | Long-term Target |
| ------------------- | ------- | --------------- | -------------- | ---------------- |
| Diagnostic rules    | 33      | 33 + code fixes | ~55            | 80+              |
| Rules with auto-fix | 0       | 5+              | 15+            | 30+              |
| Formatter options   | 2       | 8+              | 8+ (with IR)   | 10+              |
| LSP code actions    | 0       | 5+              | 15+            | 30+              |

---

## 11. Priority Summary & Suggested Ordering

```
Phase 1  ──  Formatter config + ignore directives       ■□□□□□□□  (quick wins)
Phase 2  ──  Code fix infrastructure + first fixes       ■■■□□□□□  (highest impact)
Phase 3  ──  Rule expansion (style, complexity, best)    ■■□□□□□□  (breadth)
Phase 4  ──  Formatter IR + line-width wrapping          ■■■■□□□□  (hardest, most transformative)
Phase 5  ──  Control flow graph + advanced diagnostics   ■■■□□□□□  (enables future power)
Phase 6  ──  Diagnostic docs + UX polish                 ■□□□□□□□  (polish)
Phase 7  ──  Performance & scale                         ■■□□□□□□  (needed at scale)
Phase 8  ──  Advanced rules & ecosystem                  ■■■□□□□□  (long tail)
```

Phases 1 and 2 can run in parallel. Phase 3 can start as soon as 2.2 is done (code fix model defined). Phase 4 is independent and can start any time but is best saved for a focused sprint due to its scope.
