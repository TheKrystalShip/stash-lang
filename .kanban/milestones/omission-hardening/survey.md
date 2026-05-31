# Codebase Construct-Survey (UNVERIFIED)

> **Status:** Step-1 deliverable for the omission-hardening milestone — "which projects benefit?"
> **Created:** 2026-05-31
> **Provenance:** 10 parallel `explore` subagents, one per project (read-only). This is
> locally-plausible, globally-unverified output — the exact thing this milestone distrusts.
> **Every finding is a hypothesis. Each project's `/spec` phase-1 reproduces it before acting.**
> Parent: `MILESTONE.md`.

## Reading caveat

All 10 explorers returned "Strongly benefits" — that verdict is motivated-reasoning noise, not
signal. What's ranked below is my filtering by **concreteness × confidence × value**, not the
agents' self-assessment. "Mostly already Construct" projects are a *good* result (the reference
pattern is spreading) and need only a light confirmation, not a unit.

## Per-project (highest-value promotion only; detail re-derived at spec time)

| Project | Today | Highest-value promotion (HYPOTHESIS) | Conf. | Tier |
| --- | --- | --- | --- | --- |
| **Bytecode** | dispatch + opcode-metadata already Construct (startup ctor asserts every opcode has `[OpCode]`; dispatch default throws) | `BytecodeVerifier` switch has **no default** + ~20 opcodes skip operand validation → a new opcode silently bypasses verification. Add throwing default; drive operand-roles from metadata not a hand switch (`OpcodeOperands.GetWrittenReg`). | **High** | **A** |
| **Analysis** | visitor coverage already Construct; `DiagnosticDescriptors` is a source of truth | `RuleRegistry.GetAllRules()` is a hand-list → a new `IAnalysisRule` **silently never fires**. `BuildCodeLookup` hand-maintained, only spot-checked. Auto-discover rules / reflection-validate code lookup. | **High** | **A** |
| **Dap** | handlers hand-registered | `GetVariables` switch returns empty for unhandled value kinds (8 uncovered) → new runtime type silently un-expandable; step-mode switch no throwing default. Cheap fail-closed defaults + handler meta-test. | **High** | **B** |
| **Lsp** | OmniSharp reflection/DI — true Construct often unavailable | handler registration, completion-kind mapping, semantic-token legend↔constants are Instruct/Detect. Realistic ceiling is **Detect meta-tests** (enumerate handlers/providers/kinds), not Construct — record that as the justification. | Med-High | **B** |
| **Cli** | AOT (no reflection) — flags scattered across parse/dispatch/help/validation (3-4 sites) | source-gen a flag table (single source → parse+dispatch+help+incompat). Higher effort; cheap interim win = an `ExitCodes` const class. | Med | **C** |
| **Registry** (non-authz) | authz already hardened; EF columns partly CHECK-constrained | exception→HTTP-status is scattered → unhandled custom exception silently 500. `sealed RegistryServiceException` w/ `GetStatusCode()`. (Verify EF-column claims — likely overstated.) | Med | **C** |
| **Scheduler** | small/new; platform dispatch already Construct | `ServiceState` mapped per-platform with `_ => Unknown` fallback → new state silently misreports across 3 impls. Exhaustive mappers; `ServiceMode`/`ScheduleMode` enums. | Med | **C** |
| **Tpl** | template-engine lib | filter registry is a hand `switch` (DETECT) → new filter silently "unknown"; renderer node switch no default; stop-tag list duplicated 4×. Reflection/source-gen filter table. | Med | **C** |
| **Core** | **REFERENCE CONFIRMED** — visitor pattern is compiler-forced across 6 visitors | only real gap: `TokenType` enum ↔ `_keywords` dict can desync (add keyword to enum, forget dict → silently lexed as identifier). Small. → folds into the cross-project keyword unit below. | High | **D** |
| **Tooling** (Format/Docs/Tap) | already Construct (Format = visitor interface; Docs = regenerative test; Tap = small interface) | none worth a unit. | High | **D** |
| **Playground** | Monarch tokenizer keyword list | **confirmed desync**: Monarch lists `onRetry/until/spawn/typeof/delete/match/export/from` as hard keywords the lexer doesn't → folds into the cross-project keyword unit. | High | (X-cut) |
| **Stdlib** | — | **already specced as unit 1** (`stdlib-omission-hardening`, in worktree). | — | done-ish |

## Cross-project shared concerns (the real payoff of surveying holistically)

A per-project architect would scope these to one project and miss the other participants — this is
why the survey ran before any spec:

1. **Keyword vocabulary** — one bounded set, ≥3 participants: `Stash.Core` Lexer `_keywords`
   (source of truth) → `Stash.Playground` Monarch JS → `.vscode` TextMate grammar. Desync already
   found in Playground. **One unit** (a meta-test deriving every grammar's keyword set from the
   Lexer, minus a known soft-keyword allowlist) covers Core + Playground + VSCode at once.
2. **Symbol/value kind → presentation** — a new Stash value/symbol kind must be rendered in
   `Stash.Lsp` (completion kind, semantic token, document symbol) AND `Stash.Dap` (variable tree).
   Shared `SymbolKind` exhaustiveness; pairs naturally with the Lsp+Dap units.
3. **Rule registry** — `Stash.Analysis` `RuleRegistry` is also consumed by `Stash.Check`; harden once.

## Recommended rolling-wave order (revisable)

1. **`stdlib`** — in flight (unit 1).
2. **`bytecode`** — Tier A, highest-confidence safety gap (silent verifier bypass). True Construct (throwing default).
3. **`analysis`** — Tier A, "new rule silently never fires" is a real correctness gap.
4. **`keyword-vocabulary-sync`** — cross-project unit (Core + Playground + VSCode); concrete desync, clean Detect meta-test.
5. **`lsp`+`dap`** (paired) — Tier B; mostly Detect-meta-test ceiling (DI/reflection), record justification.
6. **`cli` / `registry-nonauthz` / `scheduler` / `tpl`** — Tier C, as appetite allows.
7. **Core / Format / Docs / Tap** — Tier D, confirmation only; likely no unit (fold Core's keyword bit into #4).
