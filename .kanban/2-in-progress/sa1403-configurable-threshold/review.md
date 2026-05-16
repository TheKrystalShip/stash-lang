# SA1403 Configurable String Concatenation Threshold — Review

> Produced by `/feature-review`. One finding per H2 section.
> Each finding header MUST be parseable: `## Fxx — [SEVERITY] short title`.
> `/resolve <feature> Fxx` reads exactly one section and dispatches a Resolver.

**Scope reviewed:** commits `3394128^..78ca35f` on branch `main`
**Spec:** ./spec.md
**Generated:** 2026-05-16

---

## F01 — [CRITICAL] `_parentBinaryOperator` leaks across non-binary AST boundaries, suppressing valid SA1403 reports

**Status:** fixed
**Fixed in:** b95b356
**Files:** `Stash.Analysis/Visitors/SemanticValidator.cs:693-702`, `Stash.Analysis/Rules/Suggestions/PreferStringInterpolationRule.cs:42`
**Phase:** 1B
**Commit:** 78ca35f

### Observation

`SemanticValidator.VisitBinaryExpr` save/restores `_parentBinaryOperator` around the dispatch of `expr.Left` and `expr.Right` — but no other `Visit*` method does. `VisitCallExpr`, `VisitGroupingExpr`, `VisitTernaryExpr`, `VisitAssignExpr`, `VisitUnaryExpr`, `VisitIsExpr`, `VisitSwitchExpr`, etc. all leave the field at whatever value the enclosing `BinaryExpr` set, and then descend into their own children carrying that stale value.

The practical consequence: any `+` chain nested inside a non-binary expression that is itself an operand of an outer `+` chain incorrectly sees `ParentBinaryOperator == TokenType.Plus` and is silently skipped at its chain root.

Concrete reproducer:

```stash
let x = "a";
let y = "b";
let s = name + f("a" + x + "b" + y + "c");
```

The outer `name + f(...)` is a `+` `BinaryExpr`. Before descending into the right child (`f(...)`), `_parentBinaryOperator` is set to `Plus`. `VisitCallExpr` then runs without resetting it, walks each argument, and reaches the inner chain `"a" + x + "b" + y + "c"`. `VisitBinaryExpr` builds a `RuleContext` whose `ParentBinaryOperator` is `Plus`, so the rule short-circuits at line 42 (`if (context.ParentBinaryOperator == TokenType.Plus) return;`) and the 3-literal inner chain produces no SA1403 diagnostic.

The same leak happens through `GroupingExpr` — directly contradicting the documented Phase 1A behavior:

```stash
let s = name + ("a" + x + "b" + y + "c");
```

The grouped sub-expression should be analyzed as its own chain root (see `notes/1A.md` §2 — `GroupingExpr` "acts as a leaf from the outer chain's perspective" but should NOT inherit the outer's parent operator).

### Why this matters

Spec parity break + correctness bug. Spec §3.2 requires firing on chains of N literal operands wherever they appear; this implementation silently suppresses any chain nested under a call, grouping, ternary, assignment, unary, etc. that is itself inside a `+` `BinaryExpr`. The tests don't catch this because every test uses chains either at the top level or trivially nested under `let s = …`.

This also makes `ParentBinaryOperator` semantically misleading as a general-purpose `RuleContext` field — any future rule using it will hit the same trap.

### Suggested fix

The save/restore pattern needs to be applied around every visitor call site that descends into expression operands, not just `VisitBinaryExpr`. The cleanest, least invasive option is to reset `_parentBinaryOperator` to `null` in every `Visit*Expr` method that is **not** `VisitBinaryExpr`, before they recurse into their children. Equivalently, hoist the save/restore into the dispatch path so `_parentBinaryOperator` is only non-null for the immediate children of a `BinaryExpr`.

Minimal patch sketch: in every `Visit*Expr` whose body calls `*.Accept(this)` on a sub-expression (and which is **not** `VisitBinaryExpr`), wrap the recursive call(s) in:

```csharp
var savedParent = _parentBinaryOperator;
_parentBinaryOperator = null;
expr.X.Accept(this);
// … other Accept calls …
_parentBinaryOperator = savedParent;
```

A more robust alternative is to make `BuildExprContext` the only writer (e.g. push/pop a small struct stack when dispatching the immediate children of a `BinaryExpr` and clear in all other paths). Either approach must include tests for the four nesting shapes:

1. `name + f("a" + x + "b" + y + "c")` — chain inside CallExpr argument inside `+` chain → must fire.
2. `name + ("a" + x + "b" + y + "c")` — chain inside GroupingExpr inside `+` chain → must fire.
3. `name + (cond ? "a" + x + "b" + y + "c" : other)` — chain inside TernaryExpr branch inside `+` chain → must fire.
4. `obj.field = "a" + x + "b" + y + "c"; let s = name + obj.field;` (the assignment case, in isolation) — should still fire.

### Verify

```
dotnet test Stash.Tests/Stash.Tests.csproj --filter "FullyQualifiedName~StaticAnalysisEnhancementsTests|FullyQualifiedName~Phase6RuleOptionsTests"
```

And add tests covering the four shapes above (the new ones should assert `Contains(diagnostics, d => d.Code == "SA1403")`).

---

## F02 — [IMPORTANT] Spec test case `SA1403_InterpolationOperand_NotCountedAsLiteral` was never added

**Status:** fixed
**Fixed in:** af5d24f
**Files:** `Stash.Tests/Analysis/StaticAnalysisEnhancementsTests.cs:480-560`
**Phase:** 1B
**Commit:** 78ca35f

### Observation

Spec §6 lists 14 scenario tests and 3 config-parse tests. Test #11, `SA1403_InterpolationOperand_NotCountedAsLiteral` (`"a" + "b={x}" + name` — interpolated string is NOT a `LiteralExpr`, so 1 literal counted → no fire at default), is missing from the test file. This is the test that specifically guards Decision Log §3 ("`StringInterpolationExpr` is not counted as a literal"), one of the load-bearing design decisions for this feature.

### Why this matters

The decision that `StringInterpolationExpr` does not count as a string literal has no regression guard. A future refactor could plausibly add `StringInterpolationExpr` to the `CountStringLiterals` recursion (since it is "string-shaped") and nothing in the test suite would object. The Phase 1B `non_goals` explicitly enumerated the test plan; this one was simply omitted.

### Suggested fix

Add the test from spec §6.11 verbatim:

```csharp
[Fact]
public void SA1403_InterpolationOperand_NotCountedAsLiteral()
{
    // "a" + "b={x}" + name — the interpolated string is NOT a LiteralExpr,
    // so only 1 literal is counted → does not fire at default threshold 3.
    var diagnostics = Validate("let x = 1; let name = \"y\"; let s = \"a\" + \"b={x}\" + name;");
    Assert.DoesNotContain(diagnostics, d => d.Code == "SA1403");
}
```

(Adjust the source if Stash interpolation syntax differs — verify against `Stash — Language Specification.md`.)

### Verify

```
dotnet test Stash.Tests/Stash.Tests.csproj --filter "FullyQualifiedName~SA1403_InterpolationOperand_NotCountedAsLiteral"
```

---

## F03 — [MINOR] Phase 1B modified `Stash.Analysis/Visitors/SemanticValidator.cs` outside its declared `files` scope

**Status:** fixed
**Fixed in:** eb70c6a
**Files:** `.kanban/2-in-progress/sa1403-configurable-threshold/plan.yaml:61-66`
**Phase:** 1B
**Commit:** 78ca35f

### Observation

`plan.yaml` Phase 1B `files` lists only:
- `Stash.Analysis/Rules/Suggestions/PreferStringInterpolationRule.cs`
- `Stash.Analysis/Models/DiagnosticDescriptors.cs`
- `Stash.Tests/Analysis/StaticAnalysisEnhancementsTests.cs`
- `Stash.Tests/Analysis/Phase6RuleOptionsTests.cs`
- `Stash.Check/README.md`

But commit 78ca35f also modified `Stash.Analysis/Visitors/SemanticValidator.cs` to populate `_parentBinaryOperator`. Phase 1A notes explicitly flagged this as a Phase 1B requirement: *"Phase 1B must add `Stash.Analysis/Visitors/SemanticValidator.cs` to its `files` list (or the global `scope`) so it can populate `ParentBinaryOperator` in `VisitBinaryExpr`."* The implementer made the change anyway. The global `scope:` list in `plan.yaml:19` does include the file, so `verify-phase.sh`'s scope check presumably passed against the global scope rather than the per-phase `files`, but the per-phase declaration is still inaccurate.

### Why this matters

`plan.yaml` `files` lists are the contract for `verify-phase.sh` file-scope enforcement and for the next agent's mental model of what changed where. Leaving them stale weakens the checkpoint workflow's guarantees — future audits comparing diff-to-plan will see drift.

### Suggested fix

Update `plan.yaml` Phase 1B `files` to include `Stash.Analysis/Visitors/SemanticValidator.cs`. No code change.

### Verify

```
grep "Visitors/SemanticValidator.cs" .kanban/2-in-progress/sa1403-configurable-threshold/plan.yaml
```

Expect to see the path appear under Phase 1B's `files:` block.

---

## F04 — [MINOR] Diagnostic message reads awkwardly at threshold=1 and is technically wrong (says "exceeds" when count equals threshold)

**Status:** fixed
**Fixed in:** efab25e
**Files:** `Stash.Analysis/Models/DiagnosticDescriptors.cs:202`
**Phase:** 1B
**Commit:** 78ca35f

### Observation

The new `MessageFormat` is:

> `String concatenation of {0} literals exceeds the threshold of {1}. Consider using string interpolation.`

The rule fires when `count >= Threshold` (`PreferStringInterpolationRule.cs:46`), not strictly `>`. So when `count == Threshold` (the boundary case), the message claims the count "exceeds" the threshold even though it merely meets it. The default-threshold three-literal case will display: *"String concatenation of 3 literals exceeds the threshold of 3."* — which is logically wrong.

Additionally, at `threshold = 1, count = 1` the text reads *"…of 1 literals exceeds…"* — singular/plural agreement is off.

### Why this matters

User-facing message clarity. Users reading the squiggle should not have to puzzle out that "exceeds" means ">=". Aligns with spec §3.1 wording goal that the message makes the threshold visible.

### Suggested fix

Replace "exceeds the threshold of" with "meets the configured threshold of" (or "reaches the threshold of"), e.g.:

```csharp
public static readonly DiagnosticDescriptor SA1403 = new(
    "SA1403",
    "Prefer string interpolation over concatenation",
    DiagnosticLevel.Information,
    "Suggestions",
    "String concatenation reaches {0} string literals (threshold {1}). Consider using string interpolation.");
```

The existing `SA1403_MessageIncludesCount` test (which only asserts the count "3" is present) will still pass with this wording.

### Verify

```
dotnet test Stash.Tests/Stash.Tests.csproj --filter "FullyQualifiedName~SA1403_MessageIncludesCount"
```

---

## F05 — [MINOR] `Configure` is non-resetting — repeated invocations on the same rule instance cannot clear a previously-set threshold

**Status:** fixed
**Fixed in:** c60c34d
**Files:** `Stash.Analysis/Rules/Suggestions/PreferStringInterpolationRule.cs:24-27`
**Phase:** 1B
**Commit:** 78ca35f

### Observation

```csharp
public void Configure(IReadOnlyDictionary<string, string> options)
{
    if (options.TryGetValue("threshold", out string? val) && int.TryParse(val, out int v) && v > 0)
        Threshold = v;
}
```

If `Configure` is called a first time with `threshold=5`, then called again with no `threshold` key (or with an invalid value), `Threshold` stays at 5 instead of returning to `DefaultThreshold`. Today `AnalysisEngine` always builds rules via `RuleRegistry.GetAllRules()` (fresh instances per analysis), so this does not bite in production — but it's still a latent footgun for tests that share a rule instance, and it is inconsistent with what callers would reasonably expect "configure with these options" to mean.

### Why this matters

`FunctionBodyTooLongRule` and other prior-art configurable rules have the same shape, so this finding is consistency-with-convention versus correctness-on-its-own. Filed as Minor because it is a documented pattern; consider revisiting across all configurable rules as a separate cleanup.

### Suggested fix

Reset to default at the top of `Configure`:

```csharp
public void Configure(IReadOnlyDictionary<string, string> options)
{
    Threshold = DefaultThreshold;
    if (options.TryGetValue("threshold", out string? val) && int.TryParse(val, out int v) && v > 0)
        Threshold = v;
}
```

If accepted, mirror the fix across the other `IConfigurableRule` implementations for consistency. If declined (matching prior-art behavior wins), close as wontfix.

### Verify

```
dotnet test Stash.Tests/Stash.Tests.csproj --filter "FullyQualifiedName~PreferStringInterpolationRule_Configure"
```

Add a test:

```csharp
[Fact]
public void PreferStringInterpolationRule_Configure_ResetsToDefaultWhenOptionAbsent()
{
    var rule = new PreferStringInterpolationRule();
    rule.Configure(new Dictionary<string, string> { ["threshold"] = "5" });
    rule.Configure(new Dictionary<string, string>());
    Assert.Equal(PreferStringInterpolationRule.DefaultThreshold, rule.Threshold);
}
```

---
