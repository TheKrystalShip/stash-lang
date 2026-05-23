# Bug — Exhaustive Match Analysis Not Implemented

> **Status:** Backlog  
> **Discovered during:** Review of "Static Analysis Engine — Long-Term Improvement Roadmap"  
> **Severity:** Low — missing feature, not a regression  

## Description

Phase 5 of the Static Analysis Roadmap spec listed "Exhaustive match analysis" as item 7, but the feature was not implemented. The proposed diagnostic (SA0310) would warn when a `switch`/`match` expression doesn't cover all enum variants.

## Root Cause

The feature was listed in the spec's Phase 5 under "Flow Analysis & Advanced Diagnostics" but was intentionally skipped during implementation because it falls in the "Medium Value, High Complexity (Requires CFG)" category. It requires:

1. Tracking all enum variant definitions from the type system
2. Analyzing SwitchExpr nodes for enum patterns in case branches
3. Computing which variants are covered vs. missing
4. Reporting the diagnostic for uncovered variants

The other 7 of 8 Phase 5 items were implemented.

## Affected Files

- **No files exist yet** — requires a new rule file: `Stash.Analysis/Rules/ControlFlow/ExhaustiveMatchRule.cs`
- Diagnostic code `SA0310` is allocated in the spec but not registered in `DiagnosticDescriptors.cs`

## Reproduction

```stash
enum Color { Red, Green, Blue }

fn describe(c: Color): string {
    return switch (c) {
        Color.Red => "warm",
        Color.Green => "cool",
        // Missing Color.Blue — should warn SA0310
    };
}
```

## Required Work

1. Register `SA0310` in `DiagnosticDescriptors.cs`
2. Create `ExhaustiveMatchRule.cs` subscribing to `SwitchExpr` (or as post-walk)
3. Resolve enum definition from type annotations
4. Compute covered vs. missing variants
5. Add 5+ tests in `FlowAnalysisTests.cs`
