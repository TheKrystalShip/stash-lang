# Error Type System — Built-in Error Types and Struct Throw Semantics

**Status:** Backlog — Design
**Created:** 2025-07-24
**Author:** Spec Architect
**Scope:** `Stash.Core`, `Stash.Stdlib`, `Stash.Bytecode`, `Stash.Analysis`, `docs/`

---

## 1. Problem Statement

Two distinct but related problems need solving:

### Problem 1 — Analysis Engine Doesn't Know Built-in Error Types

The stdlib throws typed errors using magic strings:

```csharp
throw new RuntimeError("Value out of range.", span, "ValueError");
```

When Stash user code writes:

```stash
throw ValueError { message: "invalid input" };
```

The analysis engine fires **SA0202** ("'ValueError' is not defined") because `ValueError` is not in the symbol table or `KnownNames` set. The same applies to `catch (ValueError e)` — the catch clause type token is checked against SA0163 (RuntimeError guard) but otherwise silently resolved; if the name isn't known, the validator doesn't warn — however auto-completion and hover won't work either.

### Problem 2 — Magic Strings Throughout the Stdlib

All error type names are inline string literals with no central registry:

```csharp
// Stash.Stdlib/BuiltIns/StrBuiltIns.cs
throw new RuntimeError("...", span, "ValueError");
throw new RuntimeError("...", span, "ParseError");

// Stash.Stdlib/BuiltIns/FsBuiltIns.cs
throw new RuntimeError("...", span, "IOError");
throw new RuntimeError("...", span, "TypeError");
```

This is brittle: rename one string in C# and the catch clause in Stash code silently stops matching. There's no way to discover what error types exist without grepping the entire stdlib.

### Problem 3 — Struct Throw Doesn't Preserve Type (Critical Semantic Bug)

This is the most important finding from the investigation. The VM's `ExecuteThrow` in `VirtualMachine.ControlFlow.cs` handles:

| Value type          | Behaviour                                                                                             |
| ------------------- | ----------------------------------------------------------------------------------------------------- |
| `StashError`        | Rethrow — preserves type                                                                              |
| `string`            | Throws as `RuntimeError` (type = null)                                                                |
| `StashDictionary`   | Extracts `type` and `message` fields — typed throw works                                              |
| **`StashInstance`** | **Falls through to default — becomes `RuntimeError` with stringified instance as message, type lost** |

This means `throw ValueError { message: "bad" }` currently:

1. ✅ Compiles without error (if SA0202 is suppressed or ValueError is known)
2. ❌ Throws a RuntimeError with no type (type = null, shown as "RuntimeError")
3. ❌ `catch (ValueError e)` will NOT catch it — it matches "RuntimeError", not "ValueError"

Users who write `throw ValueError { message: "..." }` expecting typed catch semantics are silently broken. **This must be fixed as part of this spec.**

---

## 2. Complete Error Type Inventory

### 2.1 Magic Strings in `Stash.Stdlib/BuiltIns/`

Grepped via: `grep -rn 'new RuntimeError' Stash.Stdlib/BuiltIns/ | grep '"[A-Z][a-zA-Z]*Error"'`

| Error Type          | Primary Namespaces Using It                       |
| ------------------- | ------------------------------------------------- |
| `ValueError`        | str, arr, math, json, conv, crypto, net, encoding |
| `TypeError`         | str, arr, conv, io                                |
| `ParseError`        | json, conv, ini, str                              |
| `IndexError`        | arr                                               |
| `IOError`           | fs, io, http                                      |
| `NotSupportedError` | platform/capability gates                         |
| `TimeoutError`      | net, http, async                                  |

### 2.2 Magic Strings in `Stash.Bytecode/VM/`

| Error Type     | File                        | Usage                                                                          |
| -------------- | --------------------------- | ------------------------------------------------------------------------------ |
| `CommandError` | `VirtualMachine.Strings.cs` | Strict command failure (`$!(...)`, `$!>(...)`), `PipeChain` last-stage failure |

### 2.3 Error Types NOT Using Magic Strings (Special Cases)

| Type             | How it's used                                                                                                                                                        |
| ---------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `AssertionError` | C# subclass of `RuntimeError`, no `ErrorType` string set → shows as `"RuntimeError"` in Stash. Used only by TAP reporter (C#-level). Not a user-facing struct throw. |
| `RuntimeError`   | The catch-all fallback. Users should not throw this explicitly (SA0163 warns when caught).                                                                           |

### 2.4 Fields per Error Type

All caught errors expose `message`, `type`, `stack`, `suppressed` via `StashError.VMTryGetField`. Additional fields come from `Properties`:

| Error Type          | Required Fields   | Properties (extras)                                                    |
| ------------------- | ----------------- | ---------------------------------------------------------------------- |
| `ValueError`        | `message: string` | —                                                                      |
| `TypeError`         | `message: string` | —                                                                      |
| `ParseError`        | `message: string` | —                                                                      |
| `IndexError`        | `message: string` | —                                                                      |
| `IOError`           | `message: string` | —                                                                      |
| `NotSupportedError` | `message: string` | —                                                                      |
| `TimeoutError`      | `message: string` | —                                                                      |
| `CommandError`      | `message: string` | `exitCode: int`, `stderr: string`, `stdout: string`, `command: string` |

---

## 3. Solution Design

### 3.1 Part A — String Constants Class

**Decision:** Create `Stash.Core/Runtime/ErrorTypes.cs` — a `public static class StashErrorTypes` with `public const string` fields.

**Why `Stash.Core`?** Both `Stash.Stdlib` and `Stash.Bytecode.VM` reference error type strings. Both layers depend on `Stash.Core`. Putting constants here makes them available to all consumers without introducing new inter-project dependencies.

**Why a static class with `const string` rather than an enum?** The error type is a runtime string comparison (`se.Type == typeName`). An enum would require `.ToString()` everywhere. String constants are simpler and directly usable in `new RuntimeError(msg, span, StashErrorTypes.ValueError)`.

```csharp
// Stash.Core/Runtime/ErrorTypes.cs
namespace Stash.Runtime;

/// <summary>
/// String constants for all built-in Stash error types.
/// Use these instead of inline string literals when throwing RuntimeError
/// to ensure error type names are centralised and consistent.
/// </summary>
public static class StashErrorTypes
{
    public const string ValueError       = "ValueError";
    public const string TypeError        = "TypeError";
    public const string ParseError       = "ParseError";
    public const string IndexError       = "IndexError";
    public const string IOError          = "IOError";
    public const string NotSupportedError = "NotSupportedError";
    public const string TimeoutError     = "TimeoutError";
    public const string CommandError     = "CommandError";
}
```

> **Note:** `RuntimeError` and `AssertionError` are NOT included — these are C#-level exception classes, not user-facing Stash error type names.

### 3.2 Part B — Stdlib Magic String Replacement

Every occurrence of a string literal third argument to `new RuntimeError(...)` in `Stash.Stdlib/BuiltIns/` and `Stash.Bytecode/VM/VirtualMachine.Strings.cs` must be replaced with the corresponding constant from `StashErrorTypes`.

Mechanical substitution. No semantic change — this is purely a refactoring for maintainability.

### 3.3 Part C — Error Type Struct Registration in StdlibRegistry

**Decision:** Add all error types to `StdlibRegistry._globalStructs` in `Stash.Stdlib/Registry/StdlibRegistry.Types.cs`.

This is the correct place for types that are language-global rather than namespace-scoped. The registration propagates automatically through the existing plumbing:

```
_globalStructs
    ↓
StdlibRegistry.Structs          (= _globalStructs + StdlibDefinitions.Structs)
    ↓
StdlibRegistry.ValidTypes       (= primitive types + Structs.Select(s.Name) + Enums)
    ↓
StdlibRegistry.KnownNames       (includes Structs.Select(s.Name))
    ↓
SemanticValidator._builtInNames (= StdlibRegistry.KnownNames)
    ↓
GetUnresolvedReferences()       (skips names in _builtInNames → SA0202 suppressed)
```

Additionally, `SymbolCollector.RegisterBuiltIns()` iterates `StdlibRegistry.Structs` and adds each as a `SymbolKind.Struct` in global scope with full field metadata. This enables:

- SA0202 suppression for `throw ValueError { ... }` and `catch (ValueError e)`
- LSP hover/completion showing the struct fields
- Semantic token classification as `struct`

**Struct definitions to add:**

```csharp
// Basic error types — all share the same minimal shape
new BuiltInStruct("ValueError",       [new BuiltInField("message", "string")]),
new BuiltInStruct("TypeError",        [new BuiltInField("message", "string")]),
new BuiltInStruct("ParseError",       [new BuiltInField("message", "string")]),
new BuiltInStruct("IndexError",       [new BuiltInField("message", "string")]),
new BuiltInStruct("IOError",          [new BuiltInField("message", "string")]),
new BuiltInStruct("NotSupportedError",[new BuiltInField("message", "string")]),
new BuiltInStruct("TimeoutError",     [new BuiltInField("message", "string")]),

// CommandError carries additional diagnostic fields
new BuiltInStruct("CommandError", [
    new BuiltInField("message", "string"),
    new BuiltInField("exitCode", "int"),
    new BuiltInField("stderr",   "string"),
    new BuiltInField("stdout",   "string"),
    new BuiltInField("command",  "string"),
]),
```

> **The existing `Error` struct** (`{ message, type, stack }`) remains as-is. It is the generic base form for `throw Error { type: "...", message: "..." }` with explicit type override. The new named structs are the preferred semantic form.

### 3.4 Part D — Fix `ExecuteThrow` for StashInstance (VM Semantic Bug)

**Decision:** Update `ExecuteThrow` in `Stash.Bytecode/VM/VirtualMachine.ControlFlow.cs` to handle `StashInstance`.

**When a `StashInstance` is thrown:**

1. Use `instance.TypeName` as the `ErrorType` (e.g., `"ValueError"`)
2. Read the `message` field (from `FieldSlots` or `_fields` dict) as `errMsg`; fallback to `RuntimeValues.Stringify(instance)` if field not found
3. Collect all remaining fields into `Properties`

This makes `throw ValueError { message: "x" }` semantically equivalent to `throw { type: "ValueError", message: "x" }`.

**Insertion point** — after the `StashDictionary` case, before the final fallback:

```csharp
if (errorVal is StashInstance instance)
{
    // Use the struct type name as the error type
    string instType = instance.TypeName;
    string instMsg = "";
    var props = new Dictionary<string, object?>();

    // Extract message field if present
    bool hasMessage = false;
    // Try GetField via the standard VMTryGetField path but we need to iterate
    // Use the existing GetField method with a null span (no span for this read)
    try
    {
        var msgVal = instance.GetField("message", null);
        instMsg = msgVal.ToObject()?.ToString() ?? "";
        hasMessage = true;
    }
    catch (RuntimeError) { /* no message field */ }

    if (!hasMessage)
        instMsg = RuntimeValues.Stringify(instance);

    // Collect all fields into Properties (for structured access after catch)
    // This mirrors the StashDictionary handling
    // ... (see Implementation Notes below)

    throw new RuntimeError(instMsg, span, instType) { Properties = props.Count > 0 ? props : null };
}
```

> **Implementation Note:** `StashInstance.GetField` throws `RuntimeError` if the field is absent rather than returning null/default. The implementation should use `VMTryGetField` (the `IVMFieldAccessible` interface) to safely probe for `message`. For `Properties`, iterate known fields via `Struct.FieldIndices` if struct-backed, or `_fields.Keys` if dict-backed. **The implementation team should prefer calling `instance.VMTryGetField("message", out var v, null)` rather than `instance.GetField(...)` with a try/catch.**

**Alternative considered:** Do nothing. Let users use `throw { type: "ValueError", message: "x" }` dict syntax. **Rejected** — the struct syntax is idiomatic Stash. The existing `throw Error { type: "...", message: "..." }` form is in the example code. Silently losing the type string on throw is a correctness bug that would confuse every user who writes typed struct throws.

**Risk:** Iterating fields on `StashInstance` for `Properties` requires exposing field iteration. `StashInstance` uses either `FieldSlots` (indexed by `Struct.FieldIndices`) or `_fields` (dictionary). A new `public IEnumerable<(string Name, StashValue Value)> GetAllFields()` helper method on `StashInstance` would make this clean. **This is the preferred approach.**

---

## 4. Decision Log

### Decision 1: String Constants in `Stash.Core` vs `Stash.Stdlib`

**Chosen:** `Stash.Core/Runtime/ErrorTypes.cs`
**Rejected:** `Stash.Stdlib/Registry/StdlibErrorTypes.cs`
**Rationale:** `CommandError` is thrown from `Stash.Bytecode/VM/`, which depends on `Stash.Core` but NOT on `Stash.Stdlib`. Putting constants in `Stash.Core` makes them accessible from both the VM layer and the stdlib layer without new project dependencies.

### Decision 2: `const string` vs enum vs interface hierarchy

**Chosen:** `public const string` in a static class
**Rejected alternatives:**

- `enum` — requires `.ToString()` at every use site; incompatible with the string-comparison catch dispatch
- Interface/base class hierarchy — massive scope creep; Stash structs don't have inheritance at the language level
- `static readonly string` — `const string` is inlinable at compile time, slight performance advantage for `new RuntimeError(msg, span, StashErrorTypes.ValueError)`

### Decision 3: Register as `BuiltInStruct` vs add to `KnownNames` only

**Chosen:** Register as full `BuiltInStruct` entries
**Rejected:** Add names to `KnownNames` only (would suppress SA0202 but no field metadata)
**Rationale:** Full struct registration gives LSP hover/completion for field names. Users writing `catch (ValueError e) { io.println(e.message); }` benefit from completion on `e.message`. The cost (a few extra entries in `_globalStructs`) is negligible.

### Decision 4: Fix `ExecuteThrow` for `StashInstance` vs leave as-is

**Chosen:** Fix the VM
**Rejected:** Document as-is and tell users to use dict throws
**Rationale:** The struct throw form is already in example code (`elevate.stash`). Any user writing `throw ValueError { message: "..." }` expecting it to behave like a typed error would be silently broken. The fix is a single `if` branch addition — low risk, high correctness value. **Not fixing this would make Part C (struct registration) somewhat misleading** — users would see completion for `ValueError`, write the throw, get no analysis warning, but then find `catch (ValueError e)` never fires.

### Decision 5: `CommandError` Constants Location

**Chosen:** `StashErrorTypes.CommandError` in `Stash.Core`
**Context:** `CommandError` is thrown by the VM (`Stash.Bytecode`), not the stdlib. The constants class in `Stash.Core` naturally covers this.

---

## 5. Files to Change

| File                                              | Change                                                                                                 |
| ------------------------------------------------- | ------------------------------------------------------------------------------------------------------ |
| `Stash.Core/Runtime/ErrorTypes.cs`                | **New file** — `StashErrorTypes` static constants class                                                |
| `Stash.Core/Runtime/Types/StashInstance.cs`       | **New method** — `GetAllFields()` returning `IEnumerable<(string, StashValue)>` for both storage modes |
| `Stash.Stdlib/Registry/StdlibRegistry.Types.cs`   | Add 8 error type entries to `_globalStructs`                                                           |
| `Stash.Stdlib/BuiltIns/*.cs` (all affected)       | Replace `"ValueError"`, `"TypeError"`, etc. with `StashErrorTypes.X`                                   |
| `Stash.Bytecode/VM/VirtualMachine.Strings.cs`     | Replace `"CommandError"` literals with `StashErrorTypes.CommandError`                                  |
| `Stash.Bytecode/VM/VirtualMachine.ControlFlow.cs` | Add `StashInstance` handling branch in `ExecuteThrow`                                                  |
| `docs/Stash — Language Specification.md`          | Document built-in error type taxonomy; clarify struct throw semantics                                  |
| `docs/Stash — Standard Library Reference.md`      | Add "Built-in Error Types" section listing all error types with their fields                           |
| `examples/error_handling.stash`                   | Add examples using `throw ValueError { ... }` and typed catch                                          |
| `Stash.Tests/Bytecode/ErrorTypeTests.cs`          | **New file** — tests for typed struct throws and catches                                               |
| `Stash.Tests/Analysis/ErrorTypeAnalysisTests.cs`  | **New file** — tests that SA0202 does NOT fire for built-in error types                                |

---

## 6. Stash Code Semantics After This Change

### Before (current workaround — dict throw)

```stash
throw { type: "ValueError", message: "index must be positive" };
```

### After (idiomatic struct throw — both forms work)

```stash
// Struct form (preferred — typed, completion-friendly)
throw ValueError { message: "index must be positive" };

// Dict form (still supported — required for dynamic type at runtime)
throw { type: "ValueError", message: "index must be positive" };
```

### Typed catch (works after VM fix)

```stash
fn parse_age(s: string) -> int {
    let n = conv.toInt(s);
    if (n is null) {
        throw ParseError { message: $"'{s}' is not a valid integer" };
    }
    if (n < 0 || n > 150) {
        throw ValueError { message: $"age {n} is out of range [0, 150]" };
    }
    return n;
}

try {
    let age = parse_age("abc");
} catch (ParseError e) {
    io.println($"parse failed: {e.message}");
} catch (ValueError e) {
    io.println($"value invalid: {e.message}");
} catch (e) {
    io.println($"unexpected: {e.type}: {e.message}");
}
```

### CommandError with structured fields

```stash
try {
    let result = $!(git push origin main);
} catch (CommandError e) {
    io.println($"git push failed: exit {e.exitCode}");
    io.println($"stderr: {e.stderr}");
}
```

---

## 7. Test Scenarios

### 7.1 VM Semantics (Stash.Tests/Bytecode/ErrorTypeTests.cs)

| Test Name                                       | Scenario                                                                     |
| ----------------------------------------------- | ---------------------------------------------------------------------------- |
| `StructThrow_ValueError_TypePreserved`          | `throw ValueError { message: "x" }` → catch sees type `"ValueError"`         |
| `StructThrow_TypeError_TypePreserved`           | Same pattern for TypeError                                                   |
| `StructThrow_ParseError_TypePreserved`          | Same pattern for ParseError                                                  |
| `StructThrow_CommandError_PropertiesAccessible` | Throw `CommandError` with all fields; catch reads `exitCode`, `stderr`, etc. |
| `StructThrow_CaughtByTypedCatch_Matches`        | `catch (ValueError e)` catches a `throw ValueError { ... }`                  |
| `StructThrow_NotCaughtByWrongType`              | `catch (TypeError e)` does NOT catch a `throw ValueError { ... }`            |
| `StructThrow_CaughtByCatchAll_Fallback`         | Untyped `catch (e)` catches any struct throw                                 |
| `StructThrow_MessageField_Accessible`           | `e.message` is accessible after catching struct throw                        |
| `DictThrow_StillWorks_BackwardsCompat`          | Dict throw `{ type: "ValueError", ... }` still works as before               |
| `StructThrow_NoMessageField_Fallback`           | Struct without `message` field → stringified instance as message             |

### 7.2 Analysis (Stash.Tests/Analysis/ErrorTypeAnalysisTests.cs)

| Test Name                                    | Scenario                                            |
| -------------------------------------------- | --------------------------------------------------- |
| `Throw_ValueError_NoUndefinedWarning`        | `throw ValueError { message: "x" }` → 0 diagnostics |
| `Throw_TypeError_NoUndefinedWarning`         | Same for TypeError                                  |
| `Throw_ParseError_NoUndefinedWarning`        | Same for ParseError                                 |
| `Throw_IndexError_NoUndefinedWarning`        | Same for IndexError                                 |
| `Throw_IOError_NoUndefinedWarning`           | Same for IOError                                    |
| `Throw_NotSupportedError_NoUndefinedWarning` | Same for NotSupportedError                          |
| `Throw_TimeoutError_NoUndefinedWarning`      | Same for TimeoutError                               |
| `Throw_CommandError_NoUndefinedWarning`      | Same for CommandError                               |
| `Catch_ValueError_NoUndefinedWarning`        | `catch (ValueError e)` → no SA0202                  |
| `ErrorTypes_InSymbolTable_AreStructKind`     | Symbol table query confirms symbol kind is Struct   |

---

## 8. Documentation Changes

### 8.1 `docs/Stash — Language Specification.md`

Add a subsection to the Error Handling section: **"Built-in Error Types"**

Content:

- Table of all built-in error type names with their fields
- Explanation that struct throw form `throw ValueError { message: "..." }` is semantically equivalent to `throw { type: "ValueError", message: "..." }`
- Note that `CommandError` is thrown automatically by strict command expressions; additional fields (`exitCode`, `stderr`, `stdout`, `command`) are accessible after a typed catch
- Note that `RuntimeError` is the catch-all type name for errors not explicitly typed (SA0163 warns if you explicitly catch "RuntimeError" by name)

### 8.2 `docs/Stash — Standard Library Reference.md`

Add new top-level section: **"Built-in Error Types"** (before the namespace listing)

Content:

- `ValueError`, `TypeError`, `ParseError`, `IndexError`, `IOError`, `NotSupportedError`, `TimeoutError`, `CommandError`
- For each: description of when it's thrown (which stdlib namespaces, which conditions), available fields, example throw/catch pattern

---

## 9. Cross-Platform and Tooling Notes

- **Cross-platform:** No platform differences. Error type strings are runtime strings — consistent on Linux, macOS, Windows.
- **LSP:** Once error types are in `StdlibRegistry.Structs`, the `SymbolCollector.RegisterBuiltIns()` path handles everything. Struct field completions on caught error variables require the type inference engine to know the catch variable's type — verify `Stash.Analysis/Engines/TypeInferenceEngine.cs` propagates struct type from catch clause binding.
- **DAP:** Caught error instances displayed in the debug watch panel are `StashError` objects — their `Type` string will now match the struct name. No DAP changes needed.
- **Playground:** No changes needed — `ExecuteThrow` fix is in the VM, which Playground uses.
- **Static analysis:** `UndefinedIdentifierRule.cs` (SA0202) and `SymbolCollector` both use `StdlibRegistry.KnownNames`/`Structs` — registration alone handles SA0202 suppression.

---

## 10. Risks and Open Questions

### Risk: `GetAllFields()` on StashInstance

The VM `ExecuteThrow` fix needs to iterate all fields of a `StashInstance` to populate `Properties`. `StashInstance` has two storage modes: `FieldSlots` (array-indexed via `Struct.FieldIndices`) and `_fields` (dictionary). A `GetAllFields()` method needs to handle both.

**Resolution:** Add `public IEnumerable<(string Name, StashValue Value)> GetAllFields()` to `StashInstance`. This is a clean, minimal addition.

### Open Question: Should built-in error types be visible as type hints?

Currently, `RecordTypeReference()` in `SymbolCollector` filters out names in `StdlibRegistry.ValidTypes` (they're not recorded as unresolved). Since `ValidTypes` includes `Structs.Select(s => s.Name)`, error type names will be in `ValidTypes` after registration. This means `let e: ValueError = ...` (type annotation) will also be valid. This is correct behaviour.

### Open Question: TypeInferenceEngine and catch variable types

After `catch (ValueError e)`, the variable `e` should be typed as a `ValueError` struct for LSP purposes (field completion on `e.message`). Verify this works through `TypeInferenceEngine`. If it doesn't, create a follow-up spec.

### Out of Scope: Error type inheritance

Making `ValueError` "extend" an `Error` base type is not specified here. Stash structs don't have inheritance at the language level. This could be explored in a future "Struct Interfaces" spec.

### Out of Scope: AssertionError

`AssertionError` is a C# subclass used by the TAP test framework for reporting. It does not set `ErrorType`, shows as `"RuntimeError"` in Stash, and is not meant for user-level typed catches. No changes needed.
