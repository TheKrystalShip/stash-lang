# Error Handling — Architectural Hardening

**Status:** Design complete — ready for review
**Created:** 2026-04-28
**Origin:** Post-fix architectural analysis after error handling regressions (is Error, is ParseError, catch (Error e) base-type semantics)
**Priority:** High — correctness and user trust

---

## 1. Summary

Stash's error handling system is functionally correct after recent fixes, but its architecture has accumulated structural debt that makes regressions easy to introduce and hard to detect. This spec defines a targeted hardening effort to eliminate the root causes without a rewrite.

The core problem: the string `"ValueError"` (and every other error type name) exists in six separate locations with no shared authority. There is no single place that says "these are the built-in error types and how they relate to each other." Each location was added independently and is maintained independently. When they drift, user-facing bugs appear — as they already have.

This spec addresses the debt in five focused, independently-shippable layers. Each layer stands on its own; none requires the others to be valuable.

---

## 2. Background and Motivation

### 2.1 What "An Error" Is at Runtime

Stash errors move through three representations:

```
StashInstance  (struct-init syntax only)
    ← created by: throw ValueError { message: "x" }
    ← immediately consumed by ExecuteThrow — never seen by user code
    ↓

RuntimeError   (C# exception, drives stack unwinding)
    ← thrown by: ExecuteThrow, stdlib BuiltIns, ExecuteRethrow
    .ErrorType: string (e.g. "ValueError")
    .Properties: Dictionary<string, object?>?
    ↓ caught by Dispatch.cs outer loop

StashError     (first-class Stash value, lives in VM registers)
    .Message:   string
    .Type:      string  ←── the only runtime connection to the type system
    .Stack:     List<string>?
    .Properties: Dictionary<string, object?>?
    .OriginalException: RuntimeError?  (C# side-channel for rethrow)
    .Suppressed: List<StashError>?     (errors from defer blocks)
```

Every user interaction with errors — `is ValueError`, `catch (ValueError e)`, `.type` field access, `try expr ?? default` — ultimately operates on the `.Type` string field of a `StashError`.

### 2.2 Where Error Type Knowledge Currently Lives

| Location                             | File                                              | Purpose                            |
| ------------------------------------ | ------------------------------------------------- | ---------------------------------- |
| `StashErrorTypes.ValueError` const   | `Stash.Core/Runtime/ErrorTypes.cs`                | stdlib throws use this string      |
| `b.Struct("ValueError", ...)`        | `Stash.Stdlib/BuiltIns/GlobalBuiltIns.cs`         | struct-init syntax at runtime      |
| `_errorStruct` BuiltInStruct entries | `Stash.Stdlib/Registry/StdlibRegistry.Types.cs`   | LSP/analysis metadata              |
| `_knownTypeNames` HashSet            | `Stash.Bytecode/VM/VirtualMachine.TypeOps.cs`     | `is Error` base check              |
| `"Error"` string literal             | `Stash.Core/Parsing/AST/CatchClause.cs`           | `IsCatchAll` property              |
| `se.Type` on StashError values       | `Stash.Bytecode/VM/VirtualMachine.ControlFlow.cs` | runtime CatchMatch string equality |

**Adding a new built-in error type today requires touching at minimum four of these six locations. There is no compile-time or test-time enforcement that they agree.**

### 2.3 The Two Matching Implementations

`is ValueError` and `catch (ValueError e)` implement the same semantic concept — "does this error satisfy a type test against ValueError?" — but through completely separate code paths:

**`is ValueError` path:**

```
ExecuteIs
  → globals dict lookup → StashStruct found
  → sd2.IsBuiltIn check
  → se.Type == sd2.Name   (Fix 2, current implementation)
```

**`catch (ValueError e)` path:**

```
ExecuteCatchMatch
  → typeNames.Length == 0 → catch-all (empty = IsCatchAll)
  → else: se.Type == typeName  (direct string equality, no struct lookup)
```

These are consistent today, but they are independently maintained. There is nothing enforcing that consistency.

### 2.4 Known Dead Code

`ExecuteCatchMatch` in `VirtualMachine.ControlFlow.cs` contains this code from Fix 3:

```csharp
if (typeName == "Error" && errObj is StashError)
{
    frame.IP++;
    return;
}
```

**This code can never execute.** `catch (Error e)` has `IsCatchAll = true` in the AST (because `CatchClause.IsCatchAll` returns true when `TypeTokens[0].Lexeme == "Error"`). The compiler emits an empty `typeNames` array for all catch-alls. `ExecuteCatchMatch` returns at the `typeNames.Length == 0` guard before any loop iteration. The dead code was added in good faith but should be removed.

### 2.5 `StashError.FromRuntimeError` Factory Is Orphaned

`StashError` has a static factory method `StashError.FromRuntimeError(error, callStack)`. The dispatch loop in `Dispatch.cs` does not use it — it constructs `StashError` inline:

```csharp
var stashError = new StashError(ex.Message, ex.ErrorType ?? "RuntimeError", stackLines, ex.Properties);
```

If any enrichment logic is added to the factory (future fields, structured error codes, timestamp, etc.), the inline construction won't pick it up.

---

## 3. Design Goals

1. **Single matching function** used by both `is` and `catch` — divergence becomes impossible.
2. **Single error type registry** that all sites derive from — adding a new type is one change.
3. **No magic string literals in wrong layers** — `"Error"` disappears from `CatchClause.cs`.
4. **Dead code removed** — the unreachable Fix 3 branch in `ExecuteCatchMatch`.
5. **Factory used** — `StashError.FromRuntimeError` becomes the canonical construction point.

Non-goals for this spec:

- Error type hierarchy / inheritance (`catch (IOError e)` catching `NetworkError`) — tracked separately.
- User-defined error types participating in `is` — partial improvement possible, full solution requires hierarchy support.
- Rewriting `try expr` vs `try/catch` subsystems — they are separate subsystems by design; not changing.

---

## 4. Layers

Each layer is independently shippable. They should be implemented in order since later layers depend on earlier ones for test coverage clarity, but each can be reviewed and merged separately.

---

### Layer 1: Remove Dead Code in ExecuteCatchMatch

**Files:** `Stash.Bytecode/VM/VirtualMachine.ControlFlow.cs`

Remove the unreachable `typeName == "Error"` block from `ExecuteCatchMatch`. This was added as Fix 3 in the previous session and is harmless but misleading — it implies the code participates in `catch (Error e)` semantics when it does not.

**Before:**

```csharp
foreach (string typeName in typeNames)
{
    // "Error" is the base type — catch (Error e) catches any StashError value
    if (typeName == "Error" && errObj is StashError)
    {
        frame.IP++;
        return;
    }
    if (errObj is StashError se && se.Type == typeName)
    {
        frame.IP++;
        return;
    }
}
```

**After:**

```csharp
foreach (string typeName in typeNames)
{
    if (errObj is StashError se && se.Type == typeName)
    {
        frame.IP++;
        return;
    }
}
```

Add a comment above the method explaining that `catch (Error e)` never reaches this method — it compiles to an empty `typeNames` array and is handled by the `typeNames.Length == 0` guard.

**Tests:** Existing `ErrorTypeHierarchyTests.cs` tests for `catch (Error e)` must continue to pass unchanged. No new tests needed for this layer.

---

### Layer 2: Introduce `ErrorTypeRegistry`

**Files:** New `Stash.Core/Runtime/ErrorTypeRegistry.cs`
**Dependencies:** None (Layer 2 can ship without Layer 1)

Create a single static class that is the sole authority on:

- Which type names are built-in error subtypes
- How matching works for `is` and `catch`

```csharp
// Stash.Core/Runtime/ErrorTypeRegistry.cs
namespace Stash.Core.Runtime;

/// <summary>
/// Single source of truth for built-in error type names and matching semantics.
/// All type-check sites (ExecuteIs, ExecuteCatchMatch) must delegate to this class.
/// </summary>
internal static class ErrorTypeRegistry
{
    public const string BaseTypeName = "Error";

    /// <summary>All known built-in error subtype names.</summary>
    private static readonly HashSet<string> _subtypes = new(StringComparer.Ordinal)
    {
        StashErrorTypes.ValueError,
        StashErrorTypes.TypeError,
        StashErrorTypes.ParseError,
        StashErrorTypes.IndexError,
        StashErrorTypes.IOError,
        StashErrorTypes.NotSupportedError,
        StashErrorTypes.TimeoutError,
        StashErrorTypes.CommandError,
        StashErrorTypes.LockError,
    };

    /// <summary>Returns true if the given type name is a known built-in error subtype.</summary>
    public static bool IsBuiltInSubtype(string typeName)
        => _subtypes.Contains(typeName);

    /// <summary>Returns true if the given type name is the base Error type.</summary>
    public static bool IsBaseType(string typeName)
        => string.Equals(typeName, BaseTypeName, StringComparison.Ordinal);

    /// <summary>
    /// Core matching predicate used by both `is` and `catch`.
    /// Returns true if a StashError with errorType satisfies a type check against targetType.
    /// </summary>
    public static bool Matches(string errorType, string targetType)
    {
        // Base type "Error" matches any StashError regardless of subtype
        if (IsBaseType(targetType)) return true;
        // Exact subtype match
        return string.Equals(errorType, targetType, StringComparison.Ordinal);
    }
}
```

**Notes:**

- `LockError` is already registered in `StashErrorTypes` — include it here.
- The `Matches` method is intentionally simple. Hierarchy extension (where `NetworkError extends IOError`) can be added later by enriching this method — not by creating a new matching location.
- File lives in `Stash.Core` (not `Stash.Bytecode`) so it can be shared with analysis and LSP layers.

**Tests:** Unit tests for `ErrorTypeRegistry` directly (does not require VM):

- `IsBuiltInSubtype` returns true for all 9 known types, false for arbitrary strings
- `IsBaseType` returns true only for `"Error"`
- `Matches("ValueError", "Error")` → true
- `Matches("ValueError", "ValueError")` → true
- `Matches("ValueError", "TypeError")` → false
- `Matches("ValueError", "Error")` → true (base type always matches)

---

### Layer 3: Wire Registry into ExecuteIs and ExecuteCatchMatch

**Files:**

- `Stash.Bytecode/VM/VirtualMachine.TypeOps.cs`
- `Stash.Bytecode/VM/VirtualMachine.ControlFlow.cs`

Replace the independent matching logic in both methods with calls to `ErrorTypeRegistry.Matches`.

**ExecuteIs — current (after Fix 2):**

```csharp
StashStruct sd2 => (value is StashInstance inst2 && inst2.TypeName == sd2.Name) ||
                    (sd2.IsBuiltIn && value is StashError errSd2 && errSd2.Type == sd2.Name),
```

**ExecuteIs — after Layer 3:**

```csharp
StashStruct sd2 => (value is StashInstance inst2 && inst2.TypeName == sd2.Name) ||
                    (value is StashError errIs && sd2.IsBuiltIn &&
                     ErrorTypeRegistry.Matches(errIs.Type, sd2.Name)),
```

Note: this also makes `n is Error` work when `n` is a StashError and someone somehow obtains a reference to the Error struct (theoretical, but consistent now).

**ExecuteCatchMatch — current:**

```csharp
foreach (string typeName in typeNames)
{
    if (errObj is StashError se && se.Type == typeName)
    { ... }
}
```

**ExecuteCatchMatch — after Layer 3:**

```csharp
foreach (string typeName in typeNames)
{
    if (errObj is StashError se && ErrorTypeRegistry.Matches(se.Type, typeName))
    { ... }
}
```

**Tests:** The existing `ErrorTypeHierarchyTests.cs` tests cover both paths. Run them after wiring; they must all pass. No additional tests needed for this layer alone — the registry unit tests from Layer 2 cover the logic, and the integration tests cover the wiring.

---

### Layer 4: Replace Magic String in CatchClause.IsCatchAll

**Files:** `Stash.Core/Parsing/AST/CatchClause.cs`

**Current:**

```csharp
public bool IsCatchAll => TypeTokens.Count == 0
    || (TypeTokens.Count == 1 && TypeTokens[0].Lexeme == "Error");
```

**After:**

```csharp
public bool IsCatchAll => TypeTokens.Count == 0
    || (TypeTokens.Count == 1 && ErrorTypeRegistry.IsBaseType(TypeTokens[0].Lexeme));
```

This is a cosmetic correctness change — the behavior is identical. The value is that `ErrorTypeRegistry` is now consulted at the parse/AST level, making the string `"Error"` a detail of the registry rather than a magic constant in four places.

**Notes:**

- `CatchClause.cs` is in `Stash.Core`, so `ErrorTypeRegistry` (also `Stash.Core`) is accessible with no new project dependencies.
- If the base type name ever changes (unlikely, but possible for i18n or aliasing), it changes in one place.

**Tests:** All existing `catch (Error e)` tests must continue to pass. The change is purely internal.

---

### Layer 5: Route StashError Construction Through the Factory

**Files:**

- `Stash.Bytecode/VM/VirtualMachine.Dispatch.cs`
- `Stash.Core/Runtime/Types/StashError.cs` (verify factory signature)

**Current inline construction in Dispatch.cs:**

```csharp
var stashError = new StashError(ex.Message, ex.ErrorType ?? "RuntimeError", stackLines, ex.Properties);
stashError.Suppressed = suppressed;
stashError.OriginalException = ex;
```

Verify that `StashError.FromRuntimeError` accepts call stack lines and suppressed errors. If not, update its signature to match. Then use it:

```csharp
var stashError = StashError.FromRuntimeError(ex, stackLines, suppressed);
```

If the factory signature cannot cleanly accommodate `suppressed` (which is built from defer block errors during unwinding), add an overload rather than modifying the inline path.

**Notes:**

- The existing factory signature: `StashError.FromRuntimeError(RuntimeError error, List<string>? callStack)` — needs to accept `suppressed` too, or the caller sets it afterwards via `stashError.Suppressed = suppressed`. Either is acceptable; avoid adding two different construction patterns.
- The `ex.ErrorType ?? "RuntimeError"` fallback should be preserved in the factory — it handles the case where a `RuntimeError` is thrown without an explicit error type.

**Tests:** No behavior change, so no new tests. Verify the full test suite passes.

---

## 5. Future Work (Out of Scope for This Spec)

### 5.1 Error Type Hierarchy

The current matching is flat: all named types are siblings under `Error`. There is no `NetworkError extends IOError` relationship. Supporting this would require:

- New syntax: `error NetworkError extends IOError { ... }` or similar
- A hierarchy table in `ErrorTypeRegistry.Matches` (parent chain walk)
- Compiler changes to encode parent types in the constant pool for `CatchMatch`
- Parser changes for the new declaration syntax

This is a standalone spec. The `ErrorTypeRegistry` from Layer 2 is the correct extension point — hierarchy support extends `Matches` without touching `ExecuteIs` or `ExecuteCatchMatch`.

### 5.2 User-Defined Error Types and `is`

Currently `is ConfigError` works only for built-in error types (registered as `IsBuiltIn = true`). A user doing `throw { type: "ConfigError", message: "x" }` can use `catch (ConfigError e)` (CatchMatch uses string equality) but not `is ConfigError` (ExecuteIs finds no struct for "ConfigError").

The consistent fix requires either:

- Letting `ExecuteIs` fall back to a `se.Type == targetName` check when the target is an identifier not found in globals (risky — too permissive)
- Or the user declaring `error ConfigError { message: string }` which registers a struct (requires error declaration syntax from 5.1)

### 5.3 Errors as Proper Stash Values (Long-Term Vision)

The current architecture has `StashError` as a separate C# class from `StashInstance`. A long-term direction is for errors to be first-class struct values implementing an `Error` interface. Under this model:

- `throw ValueError { message: "x" }` would NOT convert to `RuntimeError` — the `StashInstance` would be the error value
- `is ValueError` would use the normal struct-type check
- `catch (ValueError e)` would use the normal protocol dispatch
- The `StashError` class would be retired
- The C# exception machinery (`RuntimeError`) would become purely internal

This is a major VM rearchitecting effort (changes to the dispatch loop, the exception handler boundary, `try expr` semantics) and is not pursued here.

---

## 6. Implementation Checklist

Per `.github/instructions/language-changes.instructions.md` — items that apply to this hardening spec:

- [ ] Layer 1: Remove dead code in `ExecuteCatchMatch`
- [ ] Layer 2: Create `Stash.Core/Runtime/ErrorTypeRegistry.cs` with unit tests
- [ ] Layer 3: Update `ExecuteIs` to use `ErrorTypeRegistry.Matches`
- [ ] Layer 3: Update `ExecuteCatchMatch` to use `ErrorTypeRegistry.Matches`
- [ ] Layer 4: Update `CatchClause.IsCatchAll` to use `ErrorTypeRegistry.IsBaseType`
- [ ] Layer 5: Verify/update `StashError.FromRuntimeError` factory signature
- [ ] Layer 5: Replace inline `StashError` construction in `Dispatch.cs` with factory call
- [ ] Full test suite passes (6,850+ tests, zero regressions)
- [ ] No new magic strings for `"Error"` or any error type name introduced outside of `ErrorTypeRegistry` and `StashErrorTypes`

---

## 7. Decision Log

### Decision: `ErrorTypeRegistry` lives in `Stash.Core`, not `Stash.Bytecode`

**Chosen:** `Stash.Core/Runtime/ErrorTypeRegistry.cs`
**Alternative:** `Stash.Bytecode/Runtime/ErrorTypeRegistry.cs`
**Rationale:** `Stash.Core` is Layer 0 — the foundation with no dependencies. Analysis and LSP tools (which need to know about error types for diagnostics and completion) depend on `Stash.Core` but not on `Stash.Bytecode`. Placing the registry in `Core` makes it universally accessible. The `CatchClause.IsCatchAll` change (Layer 4) also requires it in `Core` since AST nodes live there.

### Decision: `Matches` does not implement hierarchy (flat matching only)

**Chosen:** Flat string equality with only base-type ("Error") special-casing.
**Alternative:** Build a parent-chain table into the registry now, even if only one level deep.
**Rationale:** No Stash feature currently relies on hierarchy. Encoding it now would add complexity with no consumer. The `ErrorTypeRegistry` design makes hierarchy trivially addable when a spec for it exists. YAGNI applies.

### Decision: `catch (Error e)` remains a catch-all, not a typed match

**Chosen:** Preserve `IsCatchAll = true` for `catch (Error e)`.
**Alternative:** Treat "Error" as a type in the CatchMatch loop (which is what Fix 3 attempted to do).
**Rationale:** The current behavior is correct and established. Changing it would mean `catch (Error e)` stops catching non-StashError C# exceptions that are wrapped as errors (currently: all RuntimeErrors, regardless of `.ErrorType`). The catch-all semantics are right for the base type. Fix 3 was dead code and should be removed, not promoted.

### Decision: User-defined error type `is` support deferred

**Chosen:** Only `IsBuiltIn = true` structs get StashError type matching in `ExecuteIs`.
**Alternative:** Fall back to `se.Type == targetName` for any identifier not found as a struct.
**Rationale:** The fallback approach is too permissive — it would make `err is SomeRandomString` silently return true/false based on string equality against an unregistered type, with no indication to the user that they're not checking against a declared type. The correct fix is the error declaration syntax (Future Work 5.2).
