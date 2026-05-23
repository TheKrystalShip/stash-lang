# UFCS — LSP Integration

> **Status:** Draft
> **Created:** April 2026
> **Parent:** [UFCS — Uniform Function Call Syntax](../4-done/UFCS%20—%20Uniform%20Function%20Call%20Syntax.md) (completed)
> **Purpose:** Add LSP support for UFCS: autocomplete after `.` on strings/arrays, hover info with `(UFCS)` label, and Find References for UFCS method calls.

---

## Table of Contents

1. [Background](#1-background)
2. [Scope](#2-scope)
3. [Completion Handler](#3-completion-handler)
4. [Hover Handler](#4-hover-handler)
5. [SymbolCollector — Find References](#5-symbolcollector--find-references)
6. [SemanticValidator — Optional Arity Checking](#6-semanticvalidator--optional-arity-checking)
7. [Implementation Strategy](#7-implementation-strategy)
8. [Test Scenarios](#8-test-scenarios)
9. [Risks & Tradeoffs](#9-risks--tradeoffs)

---

## 1. Background

The UFCS runtime implementation is complete — `"hello".upper()` and `[1,2,3].map(...)` work at the interpreter level. The original UFCS spec (Section 9) defined three LSP features that were deferred during the initial implementation:

1. **Autocomplete** — offer UFCS methods after `.` on string/array values
2. **Hover** — show `(UFCS)` hover info for UFCS method calls
3. **Find References** — record UFCS method calls so they appear in Find References for the underlying namespace function

Additionally, the review identified that the **SemanticValidator** does not validate UFCS call arity — arity mismatches are caught only at runtime.

### What Already Works

- `SemanticTokenWalker` classifies UFCS method tokens as functions (token highlighting works)
- `StdlibRegistry` has `GetUfcsNamespace()` and `HasUfcsSupport()` query methods
- `TypeInferenceEngine` can infer `"string"` and `"array"` types from literals and namespace function return types
- Completion, hover, and references all work for **direct** namespace calls (`str.upper(s)`)

### What Doesn't Work

- Typing `"hello".` or `myString.` shows **no UFCS completions**
- Hovering over `.upper()` on a string doesn't show the `str.upper` documentation
- Find References on `str.upper` doesn't find UFCS usages like `s.upper()`
- `"hello".split()` (wrong arity) produces no diagnostic — only caught at runtime

---

## 2. Scope

This spec covers LSP-only changes. No interpreter, parser, or runtime changes are needed.

### In Scope

| Feature                | Handler                | Impact                                            |
| ---------------------- | ---------------------- | ------------------------------------------------- |
| UFCS dot-completion    | `CompletionHandler.cs` | New code path in `HandleDotCompletion`            |
| UFCS hover             | `HoverHandler.cs`      | New UFCS resolution path                          |
| UFCS references        | `SymbolCollector.cs`   | Record UFCS member references in `VisitDotExpr`   |
| UFCS arity diagnostics | `SemanticValidator.cs` | Optional — check UFCS call arity at analysis time |

### Out of Scope

- Runtime behavior changes (already correct)
- DAP/debugger changes (UFCS method calls display correctly)
- Playground changes (no LSP in playground)
- TextMate grammar changes (generic `.method()` patterns already match)

---

## 3. Completion Handler

### Current State

`HandleDotCompletion(prefix, uri, lspLine, lspCol)` resolves the prefix identifier through this priority:

1. Built-in namespace → suggest namespace functions/constants
2. Import alias → suggest module symbols
3. Struct instance → suggest fields/methods (via TypeHint or narrowing)
4. User-defined struct type → suggest fields
5. Built-in struct type → suggest registered fields
6. Enum → suggest members
7. Qualified namespace.enum → suggest members

**Gap:** No case handles UFCS-eligible types (`"string"` or `"array"`).

### Required Change

After the existing struct/enum resolution attempts fail, add a UFCS completion path:

1. Resolve the prefix variable's type via the existing `TypeInferenceEngine`:
   - Check `prefixDef.TypeHint` (already available for typed/inferred variables)
   - Check `GetNarrowedTypeHint()` for `is`-narrowed variables
2. If the resolved type is `"string"` or `"array"`, query `StdlibRegistry.GetUfcsNamespace(type)` to get the namespace name
3. Fetch all functions from that namespace via `StdlibRegistry.GetNamespaceMembers(namespaceName)`
4. Build completion items with **arity-adjusted** signatures:
   - Remove the first parameter (the implicit receiver) from the displayed signature
   - Use `CompletionItemKind.Method` (not `Function`) to visually distinguish UFCS from namespace calls
   - Add `(UFCS)` to the detail or documentation field

### Completion Item Format

For `myString.` where `myString` is inferred as `"string"`:

```
upper() → string          // Method (UFCS: str.upper)
lower() → string          // Method (UFCS: str.lower)
trim() → string           // Method (UFCS: str.trim)
split(delimiter) → array  // Method (UFCS: str.split)  ← first param removed
contains(sub) → bool      // Method (UFCS: str.contains)
...
```

For `myArray.` where `myArray` is inferred as `"array"`:

```
push(item) → null         // Method (UFCS: arr.push)
pop() → any               // Method (UFCS: arr.pop)
map(callback) → array     // Method (UFCS: arr.map)
filter(callback) → array  // Method (UFCS: arr.filter)
...
```

### Literal Completion

For completions on **literals** like `"hello".` or `[1,2,3].`:

The current `HandleDotCompletion` receives a `prefix` from `FindDotPrefix()` which returns the identifier before the dot. For literals, there is no prefix identifier — the prefix would be empty or the literal text itself.

**Decision:** Phase 1 supports variable-based UFCS completion only (e.g., `myString.`). Phase 2 would add literal UFCS completion by recognizing string/array literal patterns before the dot.

**Rationale:** Variable completion covers the majority of real-world UFCS usage. Literal completion requires changes to how `FindDotPrefix` extracts context, which is more invasive and lower value.

### Extension Method Interaction

When a type has both UFCS methods (from built-in namespaces) and extension methods (from `extend` blocks), completions should include both. UFCS completions should be visually distinct — use `(UFCS)` vs `(extension)` in the detail field.

### Files Changed

| File                                      | Change                                             |
| ----------------------------------------- | -------------------------------------------------- |
| `Stash.Lsp/Handlers/CompletionHandler.cs` | New UFCS path in `HandleDotCompletion` (~30 lines) |

---

## 4. Hover Handler

### Current State

`HoverHandler` resolves hover info by:

1. Detecting dot-access context via `FindDotPrefix()`
2. Looking up namespace members (prefix + "." + word)
3. Checking built-in functions and type descriptions

**Gap:** When hovering over `s.upper()` where `s` is a string, the handler sees prefix `"s"` and word `"upper"`. It tries `StdlibRegistry.TryGetNamespaceFunction("s.upper")` which fails (`s` is not a namespace). No UFCS resolution follows.

### Required Change

After the namespace member lookup fails, add a UFCS hover fallback:

1. Resolve the prefix variable's type using analysis results (same as completion handler):
   - Look up `prefix` in visible symbols for the cursor position
   - Read its `TypeHint`
2. Query `StdlibRegistry.GetUfcsNamespace(typeName)` to get the namespace name
3. Try `StdlibRegistry.TryGetNamespaceFunction(namespace + "." + word)`
4. If found, format the hover with a `(UFCS)` label

### Hover Format

Hovering over `upper` in `s.upper()`:

```markdown
**(UFCS)** `str.upper(s: string) → string`

Returns the string converted to uppercase.

---

_UFCS: `s.upper()` is equivalent to `str.upper(s)`_

The hover should:

- Include `(UFCS)` prefix to indicate this is syntactic sugar
- Show the full namespace function signature (with all parameters including receiver)
- Include the documentation string from `NamespaceFunction.Documentation`
- Include a footnote explaining the UFCS equivalence

### Files Changed

| File            | Change                                        |
| --------------- | --------------------------------------------- |
| HoverHandler.cs | UFCS fallback in hover resolution (~20 lines) |

---

## 5. SymbolCollector — Find References

### Current State

`SymbolCollector.VisitDotExpr` visits the receiver but does **not** record a reference for the member name:

```csharp
public object? VisitDotExpr(DotExpr expr)
{
    expr.Object.Accept(this);
    return null;  // ← member name NOT recorded
}
```

This means UFCS method calls like `s.upper()` don't appear in Find References for `str.upper`.

### Required Change

In `VisitDotExpr`, after visiting the receiver, check if the member name matches a UFCS-eligible function. If so, record a reference.

**Approach:**

1. If `expr.Object` is an `IdentifierExpr`, look up its `TypeHint` from the current scope
2. If the type maps to a UFCS namespace (via `StdlibRegistry.GetUfcsNamespace()`), and the namespace has a function matching `expr.Name.Lexeme`
3. Record a reference using the qualified name (e.g., `"str.upper"`) with `ReferenceKind.Call`

**Limitation:** Only records UFCS references when the receiver is an identifier with a known type. Literal receivers or untyped variables will not be recorded. This matches how struct field references work today.

### Files Changed

| File               | Change                                                 |
| ------------------ | ------------------------------------------------------ |
| SymbolCollector.cs | UFCS reference recording in `VisitDotExpr` (~15 lines) |

---

## 6. SemanticValidator — Optional Arity Checking

### Current State

The SemanticValidator does not check arity for UFCS calls. A call like `"hello".split()` (missing required delimiter argument) produces no diagnostic — the error is only caught at runtime.

### Proposed Change

In `VisitCallExpr`, when the callee is a `DotExpr` whose receiver has a known UFCS-eligible type:

1. Resolve the receiver type (from TypeHint or literal type)
2. Look up the namespace function via `StdlibRegistry.TryGetNamespaceFunction()`
3. Check `expr.Arguments.Count` against adjusted arity (`function.Parameters.Length - 1`)
4. Emit a diagnostic on mismatch

### Complexity Assessment

This adds moderate complexity:

- Requires type inference for the receiver inside the validator
- The validator currently has no type inference dependency
- False positives possible if type inference is wrong (dynamic typing)

**Decision:** **Optional / Phase 2.** The runtime catches these errors with clear messages. LSP arity checking for UFCS is desirable but lower priority than completion and hover.

### Files Changed (if implemented)

| File                 | Change                                          |
| -------------------- | ----------------------------------------------- |
| SemanticValidator.cs | UFCS arity check in `VisitCallExpr` (~25 lines) |

---

## 7. Implementation Strategy

### Phase 1 — Completion + Hover (high value, moderate effort)

1. Add UFCS completion path in `CompletionHandler.HandleDotCompletion` for variables with `TypeHint` of `"string"` or `"array"`
2. Add UFCS hover fallback in `HoverHandler` for dot-access on typed variables
3. Both use existing `StdlibRegistry.GetUfcsNamespace()` — no new infrastructure needed

### Phase 2 — Find References + Arity (moderate value, moderate effort)

4. Add UFCS reference recording in `SymbolCollector.VisitDotExpr`
5. Optionally add UFCS arity checking in `SemanticValidator.VisitCallExpr`

### No New Infrastructure Needed

All changes consume existing APIs:

- `StdlibRegistry.GetUfcsNamespace(typeName)` — already exists
- `StdlibRegistry.HasUfcsSupport(typeName)` — already exists
- `StdlibRegistry.GetNamespaceMembers(namespaceName)` — already exists
- `SymbolInfo.TypeHint` — already populated by `TypeInferenceEngine`

---

## 8. Test Scenarios

### Completion

| Scenario                                        | Expected                                               |
| ----------------------------------------------- | ------------------------------------------------------ |
| Type `s.` where `let s = "hello"`               | Show str namespace functions with adjusted signatures  |
| Type `a.` where `let a = [1,2,3]`               | Show arr namespace functions with adjusted signatures  |
| Type `s.` where `let s: string = getValue()`    | Show str functions (explicit type hint)                |
| Type `d.` where `let d = dict.new()`            | Do NOT show UFCS functions (dict excluded)             |
| Type `n.` where `let n = 42`                    | Do NOT show UFCS functions (int has no mapping)        |
| Type `s.up` then trigger completion             | Filter to functions starting with "up" (e.g., `upper`) |
| Type `s.` where `s` has extend methods AND UFCS | Show both, visually distinguished                      |

### Hover

| Scenario                                        | Expected                                                 |
| ----------------------------------------------- | -------------------------------------------------------- |
| Hover over `upper` in `s.upper()` (s is string) | Show `(UFCS) str.upper(s: string) → string` with docs    |
| Hover over `map` in `a.map(cb)` (a is array)    | Show `(UFCS) arr.map(array, callback) → array` with docs |
| Hover over `keys` in `d.keys` (d is dict)       | Do NOT show UFCS hover — this is dict key access         |
| Hover over `upper` in `str.upper(s)`            | Show normal namespace function hover (no UFCS label)     |

### Find References

| Scenario                                          | Expected                                             |
| ------------------------------------------------- | ---------------------------------------------------- |
| Find References on `str.upper`                    | Include both `str.upper(s)` and `s.upper()` usages   |
| Find References on `arr.map`                      | Include both `arr.map(a, cb)` and `a.map(cb)` usages |
| Find References on `s.upper()` where s is untyped | No reference recorded (acceptable limitation)        |

---

## 9. Risks & Tradeoffs

### Risk: False completions on untyped variables

If a variable has no TypeHint (e.g., `let x = getUnknown()`), no UFCS completions will be offered. Acceptable — same limitation struct field completion has today.

**Mitigation:** As `TypeInferenceEngine` improves, more variables will get inferred types automatically.

### Risk: Completion noise on string variables

String variables show ~38 completion items from the `str` namespace.

**Mitigation:** Standard completion filtering by typed characters reduces noise quickly. `CompletionItemKind.Method` provides visual distinction from fields.

### Tradeoff: No literal UFCS completion in Phase 1

`"hello".` won't show completions because there's no prefix identifier to resolve. Deliberate deferral — literal usage is less common than variable usage.

### Tradeoff: SymbolCollector can only track identifier receivers

Find References for UFCS only works when the receiver is a variable with a known type, not for literal expressions like `"hello".upper()`. Consistent with how struct method references work today.

### Decision: SemanticValidator arity checking deferred

Marked optional/Phase 2. Runtime provides clear error messages for arity mismatches. Adding a validator dependency on type inference introduces false-positive risk in a dynamically typed language.
