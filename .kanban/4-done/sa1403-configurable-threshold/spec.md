# SA1403 Configurable String Concatenation Threshold — Design Spec

> **Status:** draft
> **Owner:** cristian.moraru@live.com
> **Created:** 2026-05-16
> **Slug:** sa1403-configurable-threshold

## 1. Motivation

`SA1403 — Prefer string interpolation over concatenation` currently fires on **any** `string-literal + non-literal` expression. That includes the perfectly idiomatic two-operand case like `"Hello, " + name`, which most users do not consider a problem — interpolation is *equivalent*, not *strictly better*, at that size.

The rule today is noisy in real codebases (every log line, every error message constructed with a single `+` lights up the squiggle). Users either:

1. Globally disable SA1403 in `.stashcheck` (losing the genuinely useful warnings on chains of three or more), or
2. Spam suppression comments.

We want a middle ground: keep SA1403 enabled, but only complain when the concatenation chain becomes long enough that interpolation is meaningfully more readable. The user has identified **three string literal operands** as the natural default: at three pieces, an interpolated string starts to clearly win on legibility.

Additionally, "three" is a judgment call — different teams have different opinions on where this line sits. The `.stashcheck` file already supports rule-specific options via the `options.<CODE>.<key>` syntax (used by SA0902, SA0109, SA1002, SA0405). SA1403 should follow the same convention.

## 2. Goals & Non-Goals

**Goals**

- SA1403 fires only when a `+` chain contains **at least N string literal operands**, where N defaults to **3**.
- The threshold is configurable via `.stashcheck`: `options.SA1403.threshold = <N>`.
- The threshold key name follows existing convention (`max_function_lines`, `maxComplexity`, `maxDepth`, `maxParams`) — see Decision Log §5.
- Configuration plumbing reuses the existing `IConfigurableRule` interface and `ProjectConfig.RuleOptions` plumbing — no new infrastructure.
- Loop suppression behavior (SA1202 handles loops, SA1403 stays out) is preserved exactly.
- Diagnostic descriptor message text is updated to reflect the threshold (so users see *why* it fired).
- A single diagnostic is emitted per concatenation chain — at the top-level `BinaryExpr`, not once per `+`.
- Two-literal case (`"a" + "b"`) and pure non-string concat (e.g. `1 + 2`) remain non-events.

**Non-Goals**

- Changing the rule's category, severity, or ID (still `SA1403`, `Information`, `Suggestions`).
- Counting interpolated string fragments (`"x={x}" + something`) — interpolations are already strings; we count them as one literal operand each, the same as plain string literals (i.e. `StringInterpolationExpr` is **not** counted; only `LiteralExpr` whose value is `string`). See Decision Log §3.
- Counting non-literal string operands. The threshold is specifically about *literal* operands, because that is the signal that interpolation would replace pluses.
- Reporting on chains that include mixed types (e.g. `"x" + 1 + "y"`) — current rule does not type-check operands, and we keep the literal-vs-non-literal heuristic.
- Cross-file or whole-program analysis. Per-expression only.
- Adding new `.stashcheck` syntax or semantics — `options.<CODE>.<key>` already exists.
- Migrating any other rule. SA1403 only.

## 3. Design

### 3.1 Surface

**Configuration (`.stashcheck`):**

```ini
# Only fire SA1403 when 5 or more string literals are concatenated
options.SA1403.threshold = 5
```

The key `threshold` is chosen to match `FunctionBodyTooLongRule`'s public surface (a `Threshold` property). See Decision Log §5 for the rejected alternatives.

**Behavior table:**

| Expression                          | Literal count | Default (N=3) fires? | With `threshold=2`? |
| ----------------------------------- | ------------- | -------------------- | ------------------- |
| `"a" + name`                        | 1             | no                   | no                  |
| `"a" + "b"`                         | 2 (all-literal) | no — see §3.2      | no — see §3.2       |
| `"a" + name + "b"`                  | 2             | no                   | yes                 |
| `"a" + "b" + "c"` (all-literal)     | 3             | no — see §3.2        | no — see §3.2       |
| `"a" + name + "b" + ", " + other`   | 3             | **yes**              | yes                 |
| `name + " " + other`                | 1             | no                   | no                  |
| `name + other`                      | 0             | no                   | no                  |

**Diagnostic message** changes from:

> `String concatenation can be simplified using string interpolation.`

to:

> `String concatenation of {0} literals can be simplified using string interpolation.`

where `{0}` is the actual literal count in the chain. This makes the threshold visible to users without forcing them to read docs. (See Decision Log §6.)

### 3.2 Semantics

**Chain identification.** The Stash AST represents `a + b + c + d` as a left-associative tree of `BinaryExpr` nodes (`((a + b) + c) + d`). The rule must:

1. Identify the **top-level** `BinaryExpr` of a `+` chain — i.e. one whose parent is **not** itself a `+` `BinaryExpr`. This is necessary so we report exactly once per chain, not once per `+`.
2. From that root, walk the chain (recursing into `BinaryExpr.Left`/`BinaryExpr.Right` whenever the operator is `+`) and count operands that are **string literals** (`LiteralExpr` with `Value is string`).
3. Stop counting / terminate the chain at any non-`+` `BinaryExpr` or any non-`BinaryExpr` operand — those are leaves.
4. Fire iff `count >= Threshold` **and** at least one operand in the chain is a non-literal (so we never fire on a fully constant chain, which a future constant folder may already have collapsed; see §3.3).
5. Fire iff the rule's loop-depth guard passes (existing behavior, preserved).

**Top-of-chain detection.** Two viable approaches:

- **(A) Parent-aware:** Check `RuleContext.Parent` (if available) — if parent is a `+` `BinaryExpr`, skip; we'll handle it from the root. *Preferred if `RuleContext` exposes parent.*
- **(B) Operand-aware:** When entering any `+` node, descend through `Left` to find leaves; do nothing unless this node *is* the root. Detect "this is the root" by structural means — but without parent context, that's impossible from this node alone.

We need to check whether `RuleContext` carries parent info. Phase 1 of plan.yaml budgets exploration for this; the implementer should pick (A) if it exists, otherwise add a minimal parent or "parent-is-plus" field to `RuleContext` (low-risk; tests cover it). See `context.md` for the exact pointer.

**Counting algorithm (recursive, no allocation):**

```
int CountStringLiterals(Expr e):
    if e is BinaryExpr b and b.Operator.Type == Plus:
        return CountStringLiterals(b.Left) + CountStringLiterals(b.Right)
    if e is LiteralExpr lit and lit.Value is string:
        return 1
    return 0

bool HasNonLiteral(Expr e):
    if e is BinaryExpr b and b.Operator.Type == Plus:
        return HasNonLiteral(b.Left) || HasNonLiteral(b.Right)
    return e is not LiteralExpr
```

Chain depth in user code is bounded; no stack-overflow concerns at realistic depths. Tests should include a chain of ~10 to prove it.

**Edge cases:**

| Case                                    | Behavior                                                                                          |
| --------------------------------------- | ------------------------------------------------------------------------------------------------- |
| `threshold = 0` or negative             | Treat as invalid; ignore (keep default 3). Matches `FunctionBodyTooLongRule`'s `v > 0` guard.     |
| `threshold = 1`                         | Valid; fires whenever a chain has ≥1 literal and any non-literal — i.e. the current behavior.     |
| Non-numeric `threshold` value           | Ignored; keep default. Matches existing `int.TryParse` pattern.                                   |
| `threshold` missing from `.stashcheck`  | Default 3.                                                                                        |
| Chain inside a loop                     | Suppressed (existing `LoopDepth > 0` guard, preserved verbatim).                                  |
| Chain with all literal operands         | Suppressed (constant-fold case). Today's rule also suppresses this; behavior preserved.           |
| Parenthesized sub-chain `("a"+b)+"c"+d` | If `()` is transparent in the AST: counted as a flat chain. If a Group node exists: counted as a leaf (truncates the chain at that boundary). Implementer must confirm in Phase 1 and document the choice. |
| Compound: `s += "x"`                    | Different statement type. **Out of scope.** Today's rule does not fire on `+=`; we preserve that. |
| `+` used for non-string addition        | `IsStringLiteral` already filters; `1 + 2 + 3` has zero literal *string* operands → no fire.      |

### 3.3 Interaction with existing features

- **Constant folding.** If a constant evaluator pre-folds `"a"+"b"+"c"` into `"abc"` *before* analysis runs, the rule never sees a chain — that is fine, the user-visible behavior is "no warning on a literal-only chain," which matches what we want. Today's rule already passes `SA1403_TwoLiterals_NoInfo` despite no fold at the analysis layer; the suppression comes from the `IsLiteral(other)` guard, which we preserve via the `HasNonLiteral` check.
- **String interpolation expressions.** `StringInterpolationExpr` is **not** a `LiteralExpr`, so it counts as a non-literal operand in our chain — which is the correct semantic (mixing `+` with interpolation is the smell SA1403 catches).
- **SA1202 (concat in loops).** Untouched. The `LoopDepth > 0` guard runs *before* counting; the threshold logic never executes inside loops.
- **Suppression comments (`# stash-disable SA1403 [reason]`).** Untouched; works at the diagnostic-filter layer downstream.
- **Per-file overrides / domains.** Untouched; same diagnostic code, same filter pipeline.
- **`--select SA1403` / `--ignore SA1403` CLI.** Untouched.
- **LSP.** The diagnostic descriptor message format changes — Phase 2 must update any LSP-side tests that pin the exact message text. CompletionHandler/HoverHandler do not reference SA1403 specifically.
- **DAP, Playground, VS Code grammar.** No impact (rule is pure analysis-layer).
- **`stash-check` CLI rule docs.** `Stash.Check/RuleDocGenerator.cs` reads descriptor `Title`/`MessageFormat` — message update flows through automatically.

### 3.4 Cross-platform considerations

None. `.stashcheck` parsing is byte-level identical across platforms (already handled by `ProjectConfig.ParseContent`). No filesystem or path logic involved.

## 4. Implementation Surface

- [ ] Lexer / Parser / AST — *no changes*
- [ ] Compiler / Bytecode / Opcodes — *no changes*
- [ ] VM / Execution — *no changes*
- [ ] Stdlib — *no changes*
- [x] Static analysis (Stash.Analysis) — `PreferStringInterpolationRule.cs` (rewrite to count + chain-aware), `DiagnosticDescriptors.cs` (message format), possibly `RuleContext` (parent-aware field, only if needed)
- [ ] LSP / DAP — *no changes expected* (message text flows through descriptor; verify no pinned-string tests)
- [ ] Playground / VS Code grammar — *no changes*
- [x] Docs — `Stash.Check/README.md` (mention `options.SA1403.threshold` under the rule-options example); generated rule docs regenerate from descriptor metadata
- [x] Tests — `Stash.Tests/Analysis/StaticAnalysisEnhancementsTests.cs` (update SA1403 expectations — single-literal-plus-var should no longer fire by default), new `Phase6RuleOptionsTests` cases for `Configure`, new chain-length tests.

## 5. Decision Log

| # | Decision | Alternatives | Rationale | Risks |
|---|---|---|---|---|
| 1 | Count **string-literal operands** only, not all operands | Count all `+` operands; count non-literal operands too | Literals are the signal that interpolation would replace pluses; non-literal `+` chains are a different smell (often SA1202 territory). | A `"prefix" + a + b + c + d` chain (1 literal, 4 non-literals) won't fire under N=3. Acceptable — that's not the interpolation case. |
| 2 | Default threshold = **3** | 2 (status quo); 4 (more conservative) | User-stated requirement. Matches the legibility tipping point — three pieces is where `${a} ${b} ${c}` clearly wins. | Existing users who relied on the noisy default will see fewer warnings — that's the *point*, called out in the rule docs. |
| 3 | `StringInterpolationExpr` is **not** counted as a literal | Count it (it's "string-shaped") | A `StringInterpolationExpr` already *is* the interpolated form; counting it would mean "you used interpolation, now please use more interpolation." Absurd. | Mild user surprise if someone reads the rule docs without examples — mitigated by test §6.10. |
| 4 | Fire at the **root** of the `+` chain only | Fire at every `BinaryExpr` in the chain | Avoid N duplicate diagnostics for one chain. Standard practice. | Requires parent-aware context (see §3.2); if `RuleContext` doesn't expose parent, we add a `Parent` or `ParentBinaryOp` field in Phase 1. Tiny, isolated change. |
| 5 | Option key is `threshold` | `max_string_literals`, `min_literals`, `chain_length`, `count` | `threshold` matches `FunctionBodyTooLongRule`'s public surface. Cross-rule consistency > self-explanatory verbosity (and the diagnostic message itself now explains it). | Slightly less self-documenting at `.stashcheck` read time. Mitigated by Stash.Check README mention. |
| 6 | Update descriptor `MessageFormat` to include the count | Keep current static message | Visibility — users seeing the squiggle should understand *why* it fired and what threshold applied. Aligns with SA0902 message style. | One-line change. Any LSP/snapshot test pinning the exact old message text will break — Phase 2 grepping covers it. |
| 7 | Invalid threshold values (≤0, non-numeric) silently fall back to default | Emit a config warning | Existing convention — `FunctionBodyTooLongRule` and `CyclomaticComplexityRule` both silently ignore invalid values. Consistency matters more than chattiness for config parse errors. | User typos in `.stashcheck` are silent. Acceptable; we may revisit with a dedicated "config diagnostics" pass later. |

## 6. Test Plan

Scenario list (test names will follow `SA1403_<scenario>_<expected>` convention used in `StaticAnalysisEnhancementsTests`):

**Existing test updates (Phase 2):**

1. `SA1403_StringLiteralPlusVar_ReportsInfo` — *becomes negative under new default*. Rename to `SA1403_OneLiteralPlusVar_NoInfoAtDefault`, and
2. add a paired `SA1403_OneLiteralPlusVar_WithThreshold1_ReportsInfo` for the configured equivalent.
3. `SA1403_TwoLiterals_NoInfo` — keep, still passes (literal-only chain, no non-literal operand).
4. `SA1403_StringConcatInLoop_NoInfo` — keep verbatim.

**New tests (Phase 2):**

5. `SA1403_ThreeLiteralsInChain_ReportsInfo` — `"a" + name + "b" + ", " + other` (3 string literals, mixed non-literals) → fires at default.
6. `SA1403_ChainBelowThreshold_NoInfo` — `"a" + name + "b"` (2 literals) → does not fire.
7. `SA1403_FiresOnceAtChainRoot` — long chain emits exactly **one** SA1403 diagnostic.
8. `SA1403_ConfigureThreshold5_ChainOfFour_NoInfo` — set threshold=5, chain of 4 literals → no fire.
9. `SA1403_ConfigureThreshold2_ChainOfTwoLiteralsPlusVar_ReportsInfo` — set threshold=2, `"a"+name+"b"` → fires.
10. `SA1403_ConfigureInvalidThreshold_FallsBackToDefault` — `options.SA1403.threshold = abc`, `= 0`, `= -1` → still default 3.
11. `SA1403_InterpolationOperand_NotCountedAsLiteral` — `"a" + "b={x}" + name` — interpolated string is NOT a `LiteralExpr`, so 1 literal counted → no fire at default.
12. `SA1403_AllLiteralChain_NoInfo` — `"a" + "b" + "c"` (3 literals, **no** non-literal) → no fire.
13. `SA1403_NonStringPlus_NoInfo` — `1 + 2 + 3` → no fire.
14. `SA1403_MessageIncludesCount` — diagnostic message contains the actual literal count.

**Config-parse tests** (extend `Phase6RuleOptionsTests`):

15. `ProjectConfig_ParsesSA1403Threshold` — `options.SA1403.threshold = 5` → `RuleOptions["SA1403"]["threshold"] == "5"`.
16. `PreferStringInterpolationRule_Configure_SetsThreshold`.
17. `PreferStringInterpolationRule_Configure_InvalidValueIgnored`.

## 7. Open Questions

1. **`RuleContext.Parent`** — does the existing `RuleContext` give a rule visibility into the parent AST node? Phase 1 answers this. If absent, Phase 2 adds a minimal parent-binary-op field or equivalent. (See `context.md` for the exact pointer.)
2. **Should the message also include the configured threshold (like SA0902 does)?** Decision Log §6 only commits to the count. Adding "...threshold of {1}" makes the configurability discoverable from the squiggle. Lean: **yes** — adopt `"String concatenation of {0} literals exceeds the threshold of {1}. Consider using string interpolation."`. Confirm in Phase 2 when wiring the descriptor; this is a one-arg-vs-two-arg call at the emission site.
3. **Group/parenthesis nodes in the AST.** Phase 1 must confirm whether `(expr)` produces a distinct `GroupExpr`/`ParenExpr` node or is transparent. The spec commits both ways in §3.2.

## 8. Phases

| ID  | Title                                                                | Deps | Files (approx.)                                                                       | Est. tokens |
| --- | -------------------------------------------------------------------- | ---- | ------------------------------------------------------------------------------------- | ----------- |
| 1   | Confirm RuleContext parent visibility and AST grouping semantics      | —    | `Stash.Analysis/Rules/RuleContext.cs`, `Stash.Analysis/Engines/SemanticValidator.cs`  | ~20k        |
| 2   | Rewrite SA1403 rule + descriptor + tests + config wiring + docs       | 1    | `PreferStringInterpolationRule.cs`, `DiagnosticDescriptors.cs`, tests, `Stash.Check/README.md` | ~55k        |
