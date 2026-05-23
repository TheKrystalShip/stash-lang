# Optional Chaining — Method Call Bugfix

> **Status:** Draft
> **Created:** April 2026
> **Purpose:** Fix runtime error when optional chaining is used with method calls: `x?.method()` throws "Can only call functions" when `x` is null instead of returning null.

---

## 1. Bug Description

### Reproduction

```stash
let x = null;
let result = x?.upper();   // Expected: null
                            // Actual: [runtime error] Can only call functions.
```

The bug affects **all** optional chaining + method call scenarios, not just UFCS:

```stash
let d = null;
d?.keys();       // Throws: "Can only call functions"

let obj = null;
obj?.greet();    // Throws: "Can only call functions"
```

**Property access with optional chaining works correctly:**

```stash
let x = null;
x?.name;         // Returns null ✓
x?.upper;        // Returns null ✓
```

### Discovery Context

Found during the UFCS review. The bug is pre-existing — it was present before the UFCS commit and affects all types, not just UFCS-eligible types.

---

## 2. Root Cause Analysis

### How `x?.method()` is Parsed

The parser creates:

```
CallExpr(
  callee = DotExpr(
    object = IdentifierExpr("x"),
    name = "method",
    isOptional = true       ← flag is on the DotExpr
  ),
  arguments = [],
  paren = Token(")")
)
```

The `IsOptional` flag lives on the **DotExpr**, not the **CallExpr**.

### Execution Trace

1. **`VisitCallExpr`** evaluates the callee: `callee = expr.Callee.Accept(this)`
2. **`VisitDotExpr`** runs, sees `obj` is null and `IsOptional` is true → returns `null` ✓
3. Back in **`VisitCallExpr`**, `callee` is now `null`
4. The check `callee is not IStashCallable` is true → throws `"Can only call functions"` ✗

The optional chaining context is **lost** when control returns from `VisitDotExpr` to `VisitCallExpr`. The `CallExpr` has no way to know that `null` came from optional chaining rather than a user error.

---

## 3. Design Options

### Option A: Check DotExpr.IsOptional in VisitCallExpr

Inspect the callee AST node before evaluating it:

```csharp
public object? VisitCallExpr(CallExpr expr)
{
    // Short-circuit: if callee is an optional DotExpr with null receiver, return null
    if (expr.Callee is DotExpr { IsOptional: true } dotExpr)
    {
        object? receiver = dotExpr.Object.Accept(this);
        if (receiver is null)
        {
            return null;
        }
    }

    object? callee = expr.Callee.Accept(this);
    // ... rest unchanged
}
```

**Pros:** Minimal change, no AST modifications
**Cons:** Evaluates `dotExpr.Object` twice (once for the null check, once via `Accept` on the callee). Could cause side effects if the receiver expression has side effects (e.g., `getObj()?.method()`).

### Option B: Propagate IsOptional to CallExpr (Recommended)

Add an `IsOptional` flag to `CallExpr` and propagate it from the parser when the callee is an optional `DotExpr`.

**Parser change** — in `FinishCall()` or wherever `CallExpr` is constructed:

```csharp
bool isOptionalCall = callee is DotExpr { IsOptional: true };
return new CallExpr(callee, paren, arguments, isOptionalCall, span);
```

**Interpreter change** — in `VisitCallExpr`:

```csharp
object? callee = expr.Callee.Accept(this);

// Optional chaining: x?.method() returns null when x is null
if (expr.IsOptional && callee is null)
{
    return null;
}

if (callee is not IStashCallable function)
{
    throw new RuntimeError("Can only call functions.", expr.Paren.Span);
}
```

**Pros:** Clean design, no double-evaluation, symmetric with `DotExpr.IsOptional`, explicit in the AST
**Cons:** Touches AST class (`CallExpr`), parser, interpreter, and all visitors (signature change)

### Option C: Sentinel NullChained value

Return a special sentinel value (e.g., `NullChainedResult`) from `VisitDotExpr` instead of plain `null`, which `VisitCallExpr` can detect and short-circuit on.

**Pros:** No AST changes
**Cons:** Leaks internal implementation detail into the value system, breaks `typeof`, interference with truthiness/equality semantics, fragile

### Decision: Option B

Option B is the cleanest design. The `IsOptional` flag is explicit, avoids double-evaluation, and is symmetric with how `DotExpr` already works. The parser already knows whether the callee is an optional dot access — propagating that to `CallExpr` is the natural place for it.

---

## 4. Implementation Plan

### 4.1 AST Change: CallExpr

Add `IsOptional` property to `CallExpr`:

```csharp
public class CallExpr(
    Expr callee,
    Token paren,
    List<Expr> arguments,
    bool isOptional,    // ← NEW
    SourceSpan span
) : Expr(span)
{
    public Expr Callee => callee;
    public Token Paren => paren;
    public List<Expr> Arguments => arguments;
    public bool IsOptional => isOptional;
}
```

**Migration:** All existing `CallExpr` construction sites pass `isOptional: false` (no behavioral change). Only the parser's call-finishing code needs to detect the optional callee.

### 4.2 Parser Change

In the method that constructs `CallExpr` (typically `FinishCall` or the call-parsing section of `Call()`), detect when the callee is an optional `DotExpr`:

```csharp
bool isOptional = callee is DotExpr { IsOptional: true };
return new CallExpr(callee, paren, arguments, isOptional, span);
```

### 4.3 Interpreter Change

In `VisitCallExpr`, add an early return before the `IStashCallable` check:

```csharp
object? callee = expr.Callee.Accept(this);

if (expr.IsOptional && callee is null)
{
    return null;
}

if (callee is not IStashCallable function)
{
    throw new RuntimeError("Can only call functions.", expr.Paren.Span);
}
```

### 4.4 Visitor Updates

`CallExpr` is visited by many visitors. The `IsOptional` property is read-only and doesn't affect traversal, so most visitors need no logic changes. But if the constructor signature changes (adding a new parameter), every construction site needs updating.

**Strategy:** If `CallExpr` uses a positional record or constructor with default value (`bool isOptional = false`), existing construction sites may not need changes. Verify each constructor call.

### 4.5 Analysis Impact

- **SemanticValidator**: `VisitCallExpr` should treat `IsOptional` calls as potentially returning null (no new diagnostic needed)
- **SemanticTokenWalker**: No change — `?.` is already tokenized correctly
- **SymbolCollector**: No change — optional calls don't affect reference recording
- **Resolver**: No change — optional calls resolve identically

---

## 5. Files Changed

| File                       | Change                                                |
| -------------------------- | ----------------------------------------------------- |
| CallExpr.cs                | Add `bool IsOptional` property                        |
| Parser.cs                  | Propagate `IsOptional` from optional `DotExpr` callee |
| Interpreter.Expressions.cs | Early null return when `IsOptional && callee is null` |
| InterpreterTests.cs        | New tests for optional chaining + method call         |
| UfcsTests.cs               | New test for optional chaining + UFCS method call     |

### Files NOT Changed (verify only)

- All other visitors (`SemanticValidator`, `SemanticTokenWalker`, `SymbolCollector`, `Resolver`) — no logic changes needed, but verify constructor calls if signature changes
- No lexer changes — `?.` is already lexed correctly
- No new AST nodes — reuses existing `CallExpr`

---

## 6. Test Scenarios

### Core Bug Fix

```stash
// All should return null, not throw
let x = null;
x?.upper();          // UFCS method call
x?.toString();       // Any method call
x?.nonexistent();    // Even nonexistent methods — null short-circuits before resolution
```

### Chained Optional Calls

```stash
let x = null;
x?.trim()?.upper();       // Should return null (first ?. short-circuits)

let s = "hello";
s?.trim()?.upper();       // Should return "HELLO" (no nulls)
```

### Dict Method Call

```stash
let d = null;
d?.keys();                // Should return null
```

### Non-null Path Still Works

```stash
let s = "hello";
s?.upper();               // Should return "HELLO" (non-null, normal execution)
```

### Mixed Optional Property + Method

```stash
struct Foo { name }
let x = null;
x?.name;                  // Property: returns null ✓ (already works)
x?.upper();               // Method: returns null (this fix)
```

### Error Cases (should NOT change)

```stash
let x = 42;
x.upper();                // Still throws: no method 'upper' on type 'int'

let f = null;
f();                      // Still throws: "Can only call functions" (no ?. used)
```

---

## 7. Cross-Platform

No cross-platform concerns. This is a pure interpreter logic change — null propagation is platform-independent.

---

## 8. Risks

### Risk: Side effects in receiver expressions

With Option B, `x?.method()` evaluates `x` exactly once (via `DotExpr.Object.Accept`). The `DotExpr` returns null, then `CallExpr` sees null and short-circuits. No double-evaluation risk.

### Risk: Breaking existing code

The only behavioral change is: `x?.method()` where `x` is null now returns `null` instead of throwing. No code that currently works would break — this fix only changes the error case into a successful null return.

### Risk: Visitor constructor migration

If `CallExpr` uses a positional constructor, all 30+ construction sites need the new parameter. Using a default value (`bool isOptional = false`) minimizes this impact.
