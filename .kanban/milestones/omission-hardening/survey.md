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

## Verification pass — batch 2 (adversarial, 2026-05-31)

A second batch of explorers was sent as **skeptics** (default verdict REFUTED; confirm only by
quoting the exact unguarded code) against batch-1's specific claims. This counters the "finders
find work" bias by inverting the incentive. Reconciled verdicts:

| Finding (batch 1) | Batch-2 verdict | Reconciled |
| --- | --- | --- |
| Analysis `RuleRegistry` hand-list → new rule silently never fires | **CONFIRMED** (no meta-test enumerates `IAnalysisRule` impls vs registry) | **HIGH — strongest, most concrete finding.** Lead unit. |
| Bytecode verifier "no default, 20 opcodes skip ALL validation" | **REFINED** — two generic guards run before the switch (`IsDefined` + A-reg bounds), so not "no validation". BUT a sharper real gap: multi-register-range opcodes (`Call`,`NewArray`,`Interpolate`,`Command`,…) skip B/C/companion **register-bounds** checks → out-of-bounds regs pass verification, crash at runtime. | **MED-HIGH — real, narrower than claimed.** Unit justified on the multi-register-bounds gap specifically. |
| Dap "8 value kinds silently unexpandable" | **REFINED** — only **3** are real gaps (`StashFrozenArray`,`StashDuration`,`StashStreamingProcess`); the other ~5 are legitimately non-expandable leaves where empty is correct. | **MED — overstated 8→3.** `StashFrozenArray` is the clear bug. |
| Lsp handler/legend/completion gaps | **CONFIRMED** — and found concrete present bugs: `MapMemberKind`/`MapBareIdentifierKind` silently drop `Method` and `Interface` to Text/null. | **MED — ceiling is mostly Detect** (DI/legend can't be Construct), plus one real Construct fix (2 missing switch cases). |
| Registry (non-authz) exception→500 "scattered, missing handler" | **REFUTED** — it's ASP.NET's standard implicit 500 fallback, not a registry-specific scattered mapping. | **DROP — false positive.** |
| Bytecode `OpcodeOperands` -1 fallback "silently disables DCE" | **REFUTED** — the -1 is intentional/correct (opcode writes no single reg); at most a conservative-optimization choice, not a bug. | **DROP — non-bug.** |
| Scheduler `ServiceState _ => Unknown` ×3 platforms | **CONFIRMED** | **MED** (observability misreport, not safety). |
| Tpl filter `switch` DETECT; Cli magic exit codes | **CONFIRMED** | **MED** each. |
| **Core "the reference, fully Construct"** | **REFINED (enhancement)** — BOTH the Core and Analysis skeptics independently found the visitor interfaces define 4 export methods as **default-throw** (not abstract): a node added that way does NOT force the 6 visitors → silent runtime gap. The reference has a hole. | **NEW cross-cutting unit candidate:** harden visitor exhaustiveness (kill the default-throw escape hatch / meta-test that no visitor method has a `NotImplementedException` default). |
| Keyword vocabulary desync (Playground) | **CONFIRMED but COSMETIC** — Playground lists phantom keywords (`spawn`,`typeof`,`delete`,`match`,`onRetry`,`until`); `true/false/null` are correctly under `builtinConstants`. UX/highlighting, not correctness. | **LOW — downgrade.** Latent real risk (add keyword, forget grammar) keeps a small meta-test worthwhile. |

**Meta-result:** of batch-1's headline findings, the second pass produced **2 refutations** (Registry,
OpcodeOperands), **1 material overstatement** (Dap 8→3), **2 downgrades** (keyword cosmetic, Lsp
Detect-ceiling), **confirmations** of Analysis/Scheduler/Tpl/Cli, and **1 new finding** (visitor
default-throw escape hatch). A real false-positive rate — exactly why the adversarial pass was worth
running, and why each unit's `/spec` phase-1 still re-verifies.

## Recommended rolling-wave order (reconciled, revisable)

1. **`stdlib`** — in flight (unit 1).
2. **`analysis`** — strongest, highest-confidence: `RuleRegistry` auto-discovery (or enumerating meta-test) so a new rule can't silently never-fire. Also folds the visitor default-throw fix (shared with Core).
3. **`bytecode`** — real safety gap, narrowed: verifier must bounds-check multi-register-range opcodes. (Generic A-check + dispatch-throw already mitigate the worst case.)
4. **`lsp`+`dap`** (paired) — concrete small fixes (Lsp: add `Method`/`Interface` cases + Detect meta-tests; Dap: `StashFrozenArray` expansion + handler meta-test). Ceiling mostly Detect — record the justification.
5. **`tpl` / `cli` / `scheduler`** — Tier C, confirmed-MED, as appetite allows.
6. **`keyword-vocabulary-sync`** — LOW/cosmetic; small meta-test only.
7. **Core / Format / Docs / Tap** — confirmation only; the one real Core item (visitor escape hatch) rides with `analysis` (#2).
- **Dropped:** `registry-nonauthz` (refuted — generic ASP.NET 500, not an omission).
