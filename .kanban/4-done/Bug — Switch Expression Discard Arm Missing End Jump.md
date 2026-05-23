# Bug — Switch Expression Discard Arm Missing End Jump

## Summary

The switch expression bytecode compiler (`VisitSwitchExpr` in `Compiler.Expressions.cs`) does not emit a jump-to-end after the discard/wildcard arm (`_`). When the discard arm is not the last arm, execution falls through to subsequent arm comparisons, potentially overwriting the result with a later arm's body.

## Discovery Context

Found during review of the switch statement implementation. The switch statement had the same bug (default case missing end jump), which was fixed. This is the pre-existing equivalent in the switch expression compiler.

## Root Cause

In `Stash.Bytecode/Compilation/Compiler.Expressions.cs`, `VisitSwitchExpr` method (around line 516-520):

```csharp
if (arm.IsDiscard)
{
    hasDefault = true;
    CompileExprTo(arm.Body, dest);
    // BUG: Missing endJumps.Add(_builder.EmitJump(OpCode.Jmp));
}
```

Non-discard arms correctly emit `endJumps.Add(_builder.EmitJump(OpCode.Jmp))` after their body, but the discard arm does not.

## Reproduction

```stash
let result = 2 switch {
    _ => "default",
    2 => "two"
};
// Expected: result == "default" (discard matches everything, tested first)
// Actual: result == "two" (discard body runs, then falls through to 2's comparison which also matches)
```

## Affected Files

- `Stash.Bytecode/Compilation/Compiler.Expressions.cs` — `VisitSwitchExpr` method

## Fix

Add `endJumps.Add(_builder.EmitJump(OpCode.Jmp));` after `CompileExprTo(arm.Body, dest);` in the `arm.IsDiscard` branch:

```csharp
if (arm.IsDiscard)
{
    hasDefault = true;
    CompileExprTo(arm.Body, dest);
    endJumps.Add(_builder.EmitJump(OpCode.Jmp)); // ← Fix
}
```

## Severity

Low — in practice, `_` is almost always the last arm. But the bug exists and should be fixed for correctness.
