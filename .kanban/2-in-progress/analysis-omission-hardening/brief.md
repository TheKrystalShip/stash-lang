# RFC: Analysis Omission Hardening

> **Status:** Draft
> **Owner:** cristian.moraru@live.com
> **Created:** 2026-05-31
> **Slug:** analysis-omission-hardening
> **Milestone:** omission-hardening

## Summary

Audit `Stash.Analysis` (and its AOT consumer `Stash.Check`) end-to-end against the **Construct > Detect > Instruct** doctrine: classify every cross-cutting concern, then resolve each one — either promote it to a stronger level, or record an explicit written justification for keeping it where it is.

The audit itself is the first deliverable. Subsequent phases implement the small, high-confidence promotions the audit identifies, leaving the rest classified with justification.

## Motivation

`Stash.Analysis` is the static-analysis surface — rules, diagnostics, scope tree, visitors — consumed by `Stash.Check` (the AOT CLI) and `Stash.Lsp`. Three concrete omission risks survived the milestone's adversarial verification pass (see `.kanban/milestones/omission-hardening/survey.md`):

1. `RuleRegistry.GetAllRules()` is a hand-list. A new `IAnalysisRule` implementation silently never fires — `SemanticValidator` only sees what the list mentions.
2. The "reference" Core visitor pattern has a **default-throw escape hatch**: 4 export-related methods on `IStmtVisitor<T>` carry `=> throw new NotImplementedException(...)` defaults from the original export rollout. A visitor that fails to override one of these compiles green and throws *at runtime* — the exact silent shape the milestone exists to prevent. Today every visitor overrides all 4 (verified — see Decision Log), so the defaults are dead scaffolding.
3. `DiagnosticDescriptors.BuildCodeLookup()` is a hand-maintained mirror of the `SAxxxx` static fields. A new descriptor not added to the lookup is silently unsuppressable / unvalidated by `.stashcheck` configs.

This unit is the second child of the long-term `omission-hardening` milestone (see `.kanban/milestones/omission-hardening/MILESTONE.md`). It also folds the cross-project **visitor default-throw escape hatch** — a Core-level fix that genuinely belongs with the Analysis hardening pass because all six visitor implementations live across `Stash.Core/Resolution`, `Stash.Bytecode/Compilation`, and `Stash.Analysis/Visitors`; the audit and verification of those overrides is analysis work.

## Goals

- Produce, in P1, an **exhaustive classified table** of every cross-cutting concern in `Stash.Analysis` + `Stash.Check`'s rule pipeline (both the already-Construct mechanisms and the gaps), each reproduced against the code.
- Convert the 4 export methods on `IStmtVisitor<T>` from default-throw bodies to abstract members, making a missing override a **compile error** rather than a runtime throw. This is a pure Construct promotion that closes the milestone's "the reference has a hole" finding.
- Harden `RuleRegistry` so a new `IAnalysisRule` implementation cannot silently fail to register — either via a Construct promotion (source-generated registry, the same shape as `Stash.Stdlib.Generators`) or, if dispatch-order reproducibility makes that impractical, via a fail-closed enumerating **Detect** meta-test in `Stash.Tests` with a fail-path self-test and a pinned empty exemption list. P1 decides; the criterion is recorded below.
- Promote `DiagnosticDescriptors.BuildCodeLookup` to Detect via a reflection-enumerating meta-test in `Stash.Tests` (reflection is free there; `Stash.Tests` is not AOT).
- For each concern the audit recommends *not* promoting, record an explicit written justification in `Cross-Cutting Concerns` rather than churning the architecture.

## Non-Goals

- This unit does NOT add, remove, or rename any analysis rule, diagnostic descriptor, or rule category. The 65 existing `IAnalysisRule` impls and the SA-code surface ship unchanged.
- This unit does NOT change the dispatch order of rules in `SemanticValidator` — order is load-bearing per `RuleRegistry`'s remarks and any Construct promotion must preserve it byte-for-byte.
- This unit does NOT touch sibling subsystems beyond what the visitor fix requires: a one-file edit to `Stash.Core/Parsing/AST/IStmtVisitor.cs` plus verification that every visitor in `Stash.Bytecode`, `Stash.Core/Resolution`, and `Stash.Analysis/Visitors` still compiles (they will — all 4 methods are already overridden, verified pre-P1).
- This unit does NOT spec or pre-empt the upcoming `bytecode-omission-hardening` unit (verifier bounds-checks on multi-register-range opcodes) or the `lsp+dap` pair.
- This unit does NOT redesign `IAnalysisRule`'s subscription model, the `IConfigurableRule` adapter, or `RuleContext`.

## Design

The intended end state is: every cross-cutting concern in `Stash.Analysis` + `Stash.Check`'s rule pipeline is named in a single classified table living in this brief, has a single source of truth, and either fails closed at compile/runtime today or has an explicit written justification for relying on a meta-test (Detect) or convention (Instruct).

### Surface

No user-facing surface changes. Author-facing changes are limited to:

- `IStmtVisitor<T>.VisitExportDeclStmt`, `VisitExportBlockStmt`, `VisitExportModuleAsStmt`, `VisitExportFromStmt` become abstract members (no default body). A new visitor implementation that omits one of them fails to compile. Today: silently throws `NotImplementedException` at runtime.
- A new test class `Stash.Tests/Analysis/RuleRegistryCoverageTests` (Detect — exact name TBD by P1) that enumerates every `IAnalysisRule` implementation in the `Stash.Analysis` assembly via reflection and asserts each is present in `RuleRegistry.GetAllRules()`. Ships with an empty exemption list and a fail-path self-test (a fake `IAnalysisRule` impl marked `[ExcludeFromRegistryCoverage]` proves the scan has teeth, with the marker itself on the pinned exemption list).
- A new test class `Stash.Tests/Analysis/DiagnosticDescriptorsCoverageTests` (Detect) that enumerates every `public static readonly DiagnosticDescriptor` field on `DiagnosticDescriptors` via reflection and asserts each is keyed in `AllByCode`.
- A new test class `Stash.Tests/Analysis/VisitorEscapeHatchMetaTests` (Detect) that scans `IStmtVisitor.cs` and `IExprVisitor.cs` (Roslyn syntax walk over the source text) and asserts no method declaration carries a `throw new NotImplementedException` (or `NotSupportedException`) default body — the guard against re-introducing the escape hatch a future contributor might add.
- **Conditional on P1's order-sensitivity verdict** — either (a) a new Roslyn source generator under `Stash.Analysis.Generators/` that scans for `IAnalysisRule` implementations at compile time and emits the registry list (Construct), making the Detect coverage test redundant and dropped; OR (b) `RuleRegistry.GetAllRules()` stays hand-curated for its intentional SA-category order, and the Detect coverage test above is the resolution. P1 records the verdict.

### Semantics

For each promoted concern, omission moves from "silent" to "fails closed":

- A new visitor implementation of `IStmtVisitor<T>` that does not implement `VisitExport*Stmt` **fails the build** (today: throws `NotImplementedException` at first export-statement visit).
- A new `IAnalysisRule` implementation that is not registered in `RuleRegistry.GetAllRules()` **fails an xUnit assertion** at test time, OR (if P1 picks Construct) **is automatically picked up by the generated registry list** and runs in dispatch — depending on which side of the P1 fork wins.
- A new `DiagnosticDescriptor` static field that is not keyed in `BuildCodeLookup()` **fails an xUnit assertion** at test time (today: silently absent from `AllByCode`).
- A future attempt to re-introduce a default-throw body on an `IStmtVisitor` / `IExprVisitor` method **fails an xUnit assertion** at test time (Construct + Detect on distinct shapes: the Construct fix removes the *existing* 4 defaults; the Detect test guards against *re-introduction*).

### Implementation Path

AST nodes (`Stash.Core/Parsing/AST/`) -> visitor interfaces (`IStmtVisitor<T>`, `IExprVisitor<T>`) with **all methods abstract, no default-throw escape hatches** -> 6 concrete visitor implementations (Compiler, SemanticResolver, SemanticValidator, SymbolCollector, SemanticTokenWalker, StashFormatter) override every method -> `SemanticValidator` constructs from `RuleRegistry.GetAllRules()` (or the source-gen'd equivalent) -> rules dispatch in registration order -> diagnostics carry `SAxxxx` codes from `DiagnosticDescriptors` -> `AllByCode` provides suppression lookup -> `Stash.Check` (AOT) runs the engine; `Stash.Tests` provides the Detect floor (rule-coverage, descriptor-coverage, visitor-escape-hatch).

Every promotion in this unit hardens an edge of that path *without* changing the path itself. The visitor-interface edit is the only `Stash.Core` change, and it is verified pre-P1 to be a no-op for all current implementations.

### Cross-Cutting Concerns

This is the audit's first-pass classification. **P1 reproduces every row against the code**; the table may be revised in P1's commit (mark revisions in the Decision Log).

| Concern | Single source of truth | Today's level | Disposition | Resolution |
| --- | --- | --- | --- | --- |
| Every AST node has a corresponding `Visit*` method on `IStmtVisitor<T>`/`IExprVisitor<T>` | `IStmtVisitor.cs` / `IExprVisitor.cs` (abstract interface — Accept dispatches forces each node onto the interface) | **Construct (with a hole)** | Promote | The interface IS the source of truth, but **4 export methods on `IStmtVisitor<T>` carry default-throw bodies** (lines 155, 162, 169, 176 of `Stash.Core/Parsing/AST/IStmtVisitor.cs`) — a visitor that omits one of them throws `NotImplementedException` at runtime instead of failing to compile. Verified pre-P1: every existing visitor implements all 4, so removing the defaults is a zero-impact compile-error promotion. **Resolved in P2** (Construct). |
| No `Visit*` method may carry a default-throw body | (none — invariant property) | **Instruct** today | Promote to Detect | Add `VisitorEscapeHatchMetaTests` in `Stash.Tests`: Roslyn syntax walk over both interface files asserts no method declaration body contains `throw new NotImplementedException` / `NotSupportedException`. Detect (not Construct) because the C# type system cannot prevent a future contributor from re-adding a default. Ships fail-path self-test (a fixture file with a thrown default that the test recognises and the test class explicitly excludes). **Resolved in P2** (Detect — distinct shape from above). |
| Every `IAnalysisRule` implementation is dispatched by `SemanticValidator` | `RuleRegistry.GetAllRules()` (hand-maintained list, order matters per the type-level remarks) | **Instruct** today | Promote — P1 picks Construct OR Detect on the order-sensitivity criterion below | **P1 fork.** If the dispatch order in `GetAllRules()` is genuinely *intentional* SA-category curation (the remarks say so, and the file is sectioned by SA01xx/SA02xx/...), a source-generated list is doctrinally muddled — a generator would have to re-encode that human ordering, becoming a new omission surface. In that case → **Detect**: enumerate every `IAnalysisRule` impl in `Stash.Analysis` via reflection in `Stash.Tests` (reflection is free; `Stash.Tests` is not AOT), assert each is in the registry, ship an empty exemption list and a fail-path self-test. Test is **green immediately** because all 65 impls are already registered. If the order is incidental and the generator can derive a deterministic stable order (e.g. by `DiagnosticDescriptor.Code`) without changing observed behaviour, → **Construct**: new `Stash.Analysis.Generators` Roslyn project, same shape as `Stash.Stdlib.Generators`. P1 records the verdict with rationale; **Resolved in P3** under whichever choice. |
| Every `DiagnosticDescriptor` static field is reachable via `AllByCode` | `DiagnosticDescriptors.BuildCodeLookup()` (hand-maintained mirror of static fields) | **Instruct** today | Promote to Detect | Add `DiagnosticDescriptorsCoverageTests` in `Stash.Tests`: reflect over `typeof(DiagnosticDescriptors).GetFields(BindingFlags.Public \| Static)`, filter to `DiagnosticDescriptor`, assert each is keyed in `AllByCode` with the matching `Code`. Detect (not Construct via source-gen) because the descriptor surface is small, infrequently changed, and the test is one screen of code that runs in the existing `Stash.Tests` build. Construct via source-gen is over-engineering for the cardinality. **Resolved in P4**. |
| Diagnostic emission must go through `DiagnosticDescriptors.SAxxxx.CreateDiagnostic` (no hand-coded codes/messages) | `Stash.Analysis/CLAUDE.md` rule + `SemanticDiagnostic` ctor | **Instruct** today | Justify | Keep Instruct. The doctrine's escape valve is "free-form text with no closed set"; a meta-test scanning for hand-coded `new SemanticDiagnostic(...)` calls with literal code strings would have to distinguish legitimate test-only constructions from production violations, and the existing CLAUDE.md guidance plus code review have held. Re-open if a violation is ever observed in production code. |
| Every rule has a unique `DiagnosticDescriptor.Code` | `DiagnosticDescriptors.cs` (manual SAxxxx assignment) | **Detect-ish** today (would be caught by duplicate-key in `BuildCodeLookup` IF the code reached the lookup; silent if not) | Justify | Resolved as a side-effect of the `BuildCodeLookup` coverage test above: if every descriptor is keyed and the lookup is a dictionary, duplicate codes throw on dict insert. No additional work. |
| `RuleContext.Diagnostics` flow back to the engine | `SemanticValidator._diagnostics` aggregation | **Construct** | Keep | Verified — single aggregation point, no parallel paths. |
| `Stash.Check` AOT consumes the same `RuleRegistry` (no divergent rule list) | `Stash.Check/CheckRunner.cs` calls into `AnalysisEngine` which calls `RuleRegistry.GetAllRules()` | **Construct** | Keep | Verified — single call site. Both engine consumers (`AnalysisEngine` and `SemanticValidator`) reach the same list. |
| `IAnalysisRule.SubscribedNodeTypes` matches the rule's actual `Analyze` switch | (none — invariant property between metadata and body) | **Instruct** | Justify | Keep Instruct. The subscription set is what the dispatcher uses to route nodes; a rule that subscribes to a type it doesn't handle in `Analyze` is a no-op (not a silent failure mode worth a meta-test), and a rule that handles a node it doesn't subscribe to never sees that node — the symptom is "rule never fires on X", caught by the rule's own unit tests. Not promoted. |
| `IConfigurableRule.ApplyOptions` is honoured for every config-aware rule | `AnalysisEngine` (config wiring) — verify in P1 | TBD | TBD (P1) | P1 inspects the call site. If `AnalysisEngine` enumerates rules and calls `ApplyOptions` once per `IConfigurableRule`, the concern is Construct via the interface check and we keep. If there is any rule-specific switch, promote. |
| `DiagnosticLevel` / `Category` / `ScopeKind` / `BranchKind` / `NullState` enums are exhaustively switched at dispatch sites | enum definitions in `Models/` & `FlowAnalysis/` | TBD | TBD (P1) | P1 surveys every `switch` on each of these enums and either confirms a throwing default exists OR records a one-line addition for P3. (Likely several throwing-default-already; the milestone Stdlib precedent treated `Stability` this way.) |
| **CANDIDATE — Bounded-domain literal scan on `Stash.Analysis`** | SA-code literals, rule category strings | Instruct today | Justify (default) | The bounded-domain enforcement lives in `NoMagicAuthStringsMetaTests` for Registry; the Analysis equivalent would be a sink-targeted scan on `new SemanticDiagnostic(...)` calls with literal code arguments. P1 spot-checks for any such hand-coded production call. Default disposition: keep Instruct (CLAUDE.md rule + code review). Promote only if P1 finds an actual violation. |

This table is the audit. P1's job is to commit it verified, with any reproducibility deltas folded back into the rows.

## Acceptance Criteria

- The classified table above is committed verified — each row's "today's level" reproducibly matches the code, each row marked "Resolved in phase N" has a phase that lands the resolution, and the P1 fork on `RuleRegistry` is decided with a recorded rationale.
- The 4 export methods on `IStmtVisitor<T>` are abstract (no `throw new NotImplementedException` default). A test fixture (or grep-based test) confirms no method in either visitor interface carries a default-throw body. `dotnet build` of the entire solution is green — proving every existing visitor already overrides the 4 methods.
- `VisitorEscapeHatchMetaTests` ships with: (a) the scan of both `IStmtVisitor.cs` and `IExprVisitor.cs`, (b) a fail-path self-test demonstrating the scan trips on a synthetic offender, (c) a green run on today's interfaces post-P2 fix.
- The `RuleRegistry` concern is resolved by whichever side of the P1 fork wins. If Detect: `RuleRegistryCoverageTests` enumerates every `IAnalysisRule` impl in `Stash.Analysis`, asserts each is in `GetAllRules()`, ships with an empty exemption list and a fail-path self-test, and is green on the current code. If Construct: a new `Stash.Analysis.Generators` Roslyn project source-generates the rule list, the hand-list in `RuleRegistry.cs` is replaced, the existing rule-engine tests stay green, and the dispatch order matches the previous hand-list (verified by snapshot or by an order-preserving rule).
- `DiagnosticDescriptorsCoverageTests` reflects over every `DiagnosticDescriptor` static field on `DiagnosticDescriptors` and asserts each is keyed in `AllByCode` with the matching `Code`. Disconnecting a descriptor from the lookup (proven by removing one `dict[...]` line in a fixture or by a fail-path self-test) makes the test fail.
- All language/stdlib checklist gates and the documented-flaky filter from `stdlib-omission-hardening` remain in `final_verify`; the suite stays green.

## Phases

The phase list lives in `plan.yaml`. Summary:

- **P1 — Verify & classify.** Reproduce every row of the Cross-Cutting Concerns table; commit the verified table as the source-of-truth audit deliverable, including the recorded verdict on the RuleRegistry fork.
- **P2 — Construct: visitor escape-hatch removal + Detect re-introduction guard.** Convert 4 export methods on `IStmtVisitor<T>` to abstract; ship `VisitorEscapeHatchMetaTests` (and its fail-path self-test).
- **P3 — RuleRegistry hardening.** Per P1's verdict: either build `Stash.Analysis.Generators` (Construct) OR ship `RuleRegistryCoverageTests` (Detect). Includes any throwing-default additions surfaced by the enum-exhaustiveness row in P1.
- **P4 — Detect: DiagnosticDescriptors coverage test.** Ship `DiagnosticDescriptorsCoverageTests`. Small phase deliberately separated from P3 so the rule-registry fork doesn't bottleneck this independent gap.

## Open Questions

- **RuleRegistry fork (the P1 decision).** Is `RuleRegistry.GetAllRules()`'s SA-category ordering load-bearing semantics (in which case → Detect with justification, the Stdlib `GAP C` pattern), or is it just a comment-grouping that a source generator could reproduce by sorting on `DiagnosticDescriptor.Code` without changing observed dispatch behaviour? P1 decides empirically: (a) Are there observable rule-interaction tests that depend on order? (b) Is "list-order = dispatch-order" exercised anywhere except inside `SemanticValidator`'s loop? If neither holds, source-gen is feasible.
- Is `IConfigurableRule.ApplyOptions` called via a uniform-loop in `AnalysisEngine` (Construct) or per-rule (Instruct gap)? P1 verifies.
- Should the rule-coverage Detect test, if chosen, also assert each rule's `Descriptor.Code` matches a registered `DiagnosticDescriptor` (cross-link `RuleRegistry` ↔ `AllByCode`)? Probably yes — it's free given both reflection passes already exist. P3 includes if the Detect side wins.

## Decision Log

| Date | Decision | Rationale |
| --- | --- | --- |
| 2026-05-31 | Pre-P1 verification: all 6 visitor implementations (Compiler, SemanticResolver, SemanticValidator, SymbolCollector, SemanticTokenWalker, StashFormatter) already override `VisitExportDeclStmt`, `VisitExportBlockStmt`, `VisitExportModuleAsStmt`, `VisitExportFromStmt`. Defaults in `IStmtVisitor<T>` are dead scaffolding. | Conditional inclusion: the architect's plan would have deferred this if any visitor depended on a default. Verified by `grep -rn 'VisitExport*Stmt' --include='*.cs'` across the solution. Including is a clean compile-error promotion with zero behavioural change. |
| 2026-05-31 | Pre-P1 verification: all 65 `IAnalysisRule` implementations in `Stash.Analysis/Rules/**/*.cs` are already in `RuleRegistry.GetAllRules()`. There is no current omission. | Verified by enumerating files implementing `IAnalysisRule` and diffing against `new XxxRule()` mentions in `RuleRegistry.cs`. The concern is forward — the next rule added could be silently unregistered. This is why the brief frames it as risk-of-omission, not as a current bug. |
| 2026-05-31 | Pre-P1 verification: `Stash.Check` is AOT (`<PublishAot>true</PublishAot>` in `Stash.Check.csproj`). Runtime reflection for rule discovery is not viable in `Stash.Check`. Compile-time source-gen IS viable (precedent: `Stash.Stdlib.Generators`, `netstandard2.0`, Roslyn analyzer). `Stash.Tests` is not AOT, so reflection-based Detect tests are free there. | Frames the P3 fork: a Construct promotion needs source-gen, not runtime reflection. A Detect promotion can use reflection because it lives in `Stash.Tests`. |
| 2026-05-31 | Treat "every `IStmtVisitor` method is abstract (no default throw)" and "no future `IStmtVisitor` method may be added with a default throw" as **two concerns** with two participants — current interface and future contributor. The first is Construct (P2 fix); the second is Detect (P2 guard). Not a double-up. | The Construct fix removes the existing 4 defaults; the Detect test prevents re-introduction. These are *distinct failure shapes* (state vs. transition), not the same invariant guarded twice. Mirror of the milestone's "one concern with two participants" treatment of `Stability` in Stdlib. |
| 2026-05-31 | `RuleRegistry` and `BuildCodeLookup` are NOT the same concern even though both are hand-maintained mirrors. | Different surfaces (rules vs. descriptors), different cardinalities (65 vs. ~100), different consumers, different fix shapes (source-gen-or-reflection vs. reflection-only). Two rows, two phases. |
| 2026-05-31 | Do NOT pre-commit "build `Stash.Analysis.Generators`" in the brief. The brief presents this as a P1-decided fork on the order-sensitivity criterion. | A new Roslyn generator project is itself a large new omission surface; weighed against `Stash.Stdlib.Generators` (the precedent) staying 4 small phases by reusing existing mechanisms. The decision must rest on whether `RuleRegistry`'s dispatch order is intentional curation (→ Detect) or incidental (→ Construct). |
| 2026-05-31 | `final_verify` filter inherits the `stdlib-omission-hardening` shape (documented flakies + language/stdlib gates + `validate-spec.py`). | Per `.claude/repo.md` Known Issues; the precedent is one merge ahead so the same flakies apply. `validate-spec.py` is safe to include now that the predecessor is in `4-done/`. |
