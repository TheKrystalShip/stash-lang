# Bytecode VM — Language-Agnostic Type System

**Status:** Backlog — Design Spec
**Created:** 2026-04-17
**Parent:** [Bytecode VM — Platform Target Readiness](../1-todo/Bytecode%20VM%20—%20Platform%20Target%20Readiness.md) §6.1
**Purpose:** Abstract the Stash VM's type system so that external languages can define custom types that participate fully in VM operations (arithmetic, field access, iteration, comparison, equality, stringification) without modifying VM source code.

---

## Table of Contents

1. [Motivation](#1-motivation)
2. [Current State Analysis](#2-current-state-analysis)
3. [Issues & Gaps — What We're Doing Wrong](#3-issues--gaps--what-were-doing-wrong)
4. [Prior Art](#4-prior-art)
5. [Proposed Design: Type Protocols](#5-proposed-design-type-protocols)
6. [Protocol Interfaces](#6-protocol-interfaces)
7. [VM Dispatch Refactoring](#7-vm-dispatch-refactoring)
8. [Type Registration](#8-type-registration)
9. [Impact on Existing Stash Types](#9-impact-on-existing-stash-types)
10. [Migration Strategy](#10-migration-strategy)
11. [What We Explicitly Defer](#11-what-we-explicitly-defer)
12. [Risks & Tradeoffs](#12-risks--tradeoffs)

---

## 1. Motivation

The Platform Target Readiness spec (§6.1) deferred a language-agnostic type system as "premature." The situation has changed:

1. **The VM hardcodes ~20 Stash-specific types into its dispatch loops.** `GetFieldValue` has a 12-tier type dispatch cascade. `RuntimeOps.Add` has 8 type branches. `ExecuteIterPrep` has 6. Every new Stash type (Duration, ByteSize, SemVer, IpAddress, Secret) required modifying VM source code in multiple files. This is unsustainable even for Stash itself, let alone external languages.

2. **The `Obj` tag is a "god tag."** `StashValueTag.Obj` holds strings, arrays, dicts, struct instances, enums, namespaces, durations, byte sizes, semvers, IP addresses, secrets, errors, futures, ranges, bound methods, and functions. The VM disambiguates via C# `is` pattern matching — a 20-way type dispatch hidden behind a single tag. There's no type information at the VM level for these objects.

3. **Domain types are not VM primitives.** Duration, ByteSize, SemVer, IpAddress, and Secret are sysadmin domain types. They're useful for Stash, but they're not fundamental VM operations. An external language shouldn't need to understand "adding an int to an IP address shifts the address" to use the VM.

4. **Independent of the multi-language story**, abstracting the type dispatch would improve Stash's own maintainability. Adding a new type today requires touching `RuntimeOps.cs`, `VirtualMachine.TypeOps.cs`, `VirtualMachine.Collections.cs`, `VirtualMachine.ControlFlow.cs`, `RuntimeValues.cs`, and potentially `VirtualMachine.Arithmetic.cs`. A protocol system would localize type behavior to the type's own definition.

### Design Principle

> **The VM should dispatch object operations via protocols (interfaces), not hardcoded type checks.** A type's behavior should be defined by the type, not by the VM. The VM's role is to ask "can you do X?" and dispatch accordingly — not to enumerate every possible type that might support X.

---

## 2. Current State Analysis

### 2.1 StashValue — The Tagged Union

```
StashValue (16 bytes on 64-bit: tag + padding + long + object ref)
├── Tag: StashValueTag (enum: Null=0, Bool=1, Int=2, Float=3, Obj=4, Byte=5)
├── _data: long       — inline storage for primitives
└── _obj: object?     — reference to heap objects
```

**Primitive tags (Null, Bool, Int, Float, Byte):** Excellent. Zero-allocation, type-safe, well-optimized with `AggressiveInlining`. No changes needed.

**The Obj tag:** This is where the problems live. Everything that isn't a primitive goes through `Obj`, and the VM uses C# pattern matching (`is`) to dispatch:

```
Obj contains:
├── string              ← VM has special Add (concat), GetField (.length), iteration, UFCS
├── List<StashValue>    ← VM has special GetField (.length), iteration, indexing, UFCS
├── StashDictionary     ← VM has special GetField, SetField, iteration, extension methods
├── StashInstance       ← VM has special GetField (IC-optimized), SetField, typeof, is
├── StashStruct         ← VM has special GetField (static methods), typeof
├── StashEnum           ← VM has special GetField (member access), iteration, typeof
├── StashEnumValue      ← VM has special GetField (.typeName, .memberName), typeof
├── StashNamespace      ← VM has special GetField (IC-optimized, frozen fast path)
├── StashError          ← VM has special GetField (.message, .type, .stack), truthiness
├── StashRange          ← VM has special iteration, typeof
├── StashDuration       ← VM has special arithmetic (+,-,*,/), comparison, GetField (12 properties)
├── StashByteSize       ← VM has special arithmetic, comparison, GetField (5 properties)
├── StashSemVer         ← VM has special comparison, GetField (5 properties)
├── StashIpAddress      ← VM has special arithmetic (+), comparison, GetField (4 properties), bitwise
├── StashSecret         ← VM has special taint propagation in Add, redaction in Stringify
├── StashFuture         ← VM has typeof
├── StashBoundMethod    ← VM has call dispatch
├── VMFunction          ← VM has call dispatch, typeof
├── IStashCallable      ← VM has call dispatch, typeof
├── StashTypedArray     ← VM has iteration, GetField (.length), typeof (element-specific)
└── IteratorState       ← Internal VM type for iteration state
```

Every one of these types has special-case handling sprayed across 4-6 VM files.

### 2.2 The Dispatch Cascades

**GetFieldValue** (`VirtualMachine.TypeOps.cs` lines 383–650): 12-tier type dispatch:

```
StashInstance → StashDictionary → StashNamespace → StashStruct → StashEnum →
StashEnumValue → StashError → StashDuration → StashByteSize → StashSemVer →
StashIpAddress → string/List<StashValue> (.length) → Extension methods → UFCS → Error
```

**RuntimeOps.Add** (`RuntimeOps.cs` lines 75–133): 8-tier dispatch:

```
byte→int promotion → int+int → numeric promotion → Secret taint →
string concat → IpAddress+int → Duration+Duration → ByteSize+ByteSize → Error
```

**RuntimeOps.Compare** (`RuntimeOps.cs` lines 347–408): 5-tier dispatch:

```
byte→int promotion → int vs int → numeric promotion →
IpAddress vs IpAddress → Duration vs Duration → ByteSize vs ByteSize →
SemVer vs SemVer → Error
```

**ExecuteIterPrep** (`VirtualMachine.ControlFlow.cs` lines 483–524): 6-tier dispatch:

```
StashTypedArray → List<StashValue> → StashDictionary → string → StashRange → StashEnum → Error
```

**ExecuteTypeOf** (`VirtualMachine.TypeOps.cs` lines 316–339): 20-way switch on `object` type.

### 2.3 What Works Well

- **Primitive tag dispatch** — The fast paths for int+int arithmetic, int comparison, etc. are clean and performant. These should not change.
- **Inline caching for field access** — The IC mechanism for namespace and struct field lookups is well-designed. It can coexist with protocol-based dispatch (IC is the fast path; protocol dispatch is the slow path that populates the IC).
- **StashValue's structure** — The tagged union itself is sound. The 6 primitive tags are the right abstraction. The issue is what happens inside `Obj`, not the tag system.

---

## 3. Issues & Gaps — What We're Doing Wrong

### 3.1 NaN Reflexivity Violation (Surprising Behavior)

**Current:** `NaN == NaN` → `true` (at the `StashValue` level, via bit-level `_data` comparison).

**Problem:** This violates IEEE 754, which mandates `NaN != NaN`. Every major language follows this: JavaScript, Python, Ruby, Go, Rust, C, C++, Java. Developers universally expect `NaN != NaN`.

**Why it exists:** The bit-level comparison in `StashValue.Equals()` compares `_data` fields for `Float` tag, and two `NaN` values with the same bit pattern have the same `_data`. This was likely unintentional — it's an artifact of using `_data == other._data` instead of `AsFloat == other.AsFloat`.

**Impact:** `StashValue.Equals()` is used by C# collections (`Dictionary`, `HashSet`) for `StashValue` keys. Making `NaN != NaN` at this level would break NaN-keyed dictionary entries (they'd become unretrievable). The JVM solves this by having `Float.equals()` (reflexive, for collections) differ from `==` (IEEE 754, for comparison operators).

**Recommendation:** The `RuntimeOps.IsEqual()` path (used by `==` operator) already delegates to `StashValue.Equals()` for same-tag comparisons. Split the semantics:

- `StashValue.Equals()` — Keep reflexive (NaN == NaN) for C# collection compatibility.
- `RuntimeOps.IsEqual()` — Add explicit NaN check for IEEE 754 compliance when both operands are Float.

**Decision:** Recommended. This is a correctness bug. The `==` operator should follow IEEE 754.

### 3.2 Integer Overflow Silently Wraps (Undocumented)

**Current:** `9223372036854775807 + 1` silently wraps to `-9223372036854775808` (Int64 overflow).

**Problem:** Not wrong per se — Java, C, Rust (in release mode), and Lua all allow integer overflow. But it's **undocumented**. A developer who doesn't know the int range will be surprised.

**Comparison:**

- Python: Arbitrary-precision integers, never overflow → developers expect this behavior from "scripting languages"
- JavaScript: No integers, everything is float64 → loses precision but never wraps
- Ruby: Auto-promotes to Bignum → no overflow
- Lua 5.3+: 64-bit integers with wrapping → same as Stash

**Recommendation:** Document as deliberate behavior. Do not add overflow checking (performance cost is too high for the dispatch loop). Consider adding a `math.maxInt` / `math.minInt` constant.

**Decision:** Document, don't change. This matches Lua and is acceptable for a sysadmin scripting language.

### 3.3 String + Non-String Auto-Concatenation (Questionable Design)

**Current:** `"hello" + 5` → `"hello5"`. If either operand is a string, the other is stringified and concatenated.

**Problem:** This is the JavaScript pattern, and it's widely considered a footgun:

```stash
let port = "8080";
let next = port + 1;  // "80801" — not 8081
```

**Comparison:**

- Python: `TypeError: can only concatenate str to str` → explicit is better
- Go: Compile error → no implicit conversion
- Ruby: `TypeError: no implicit conversion of Integer into String` → explicit
- JavaScript: `"8080" + 1 → "80801"` → the canonical "wat" example
- Lua: `"5" + 3 → 8.0` (coerces string to number!) → even more confusing

**Recommendation:** This is a language-level policy, not a VM-level concern. For the type protocol system, the important thing is that string concatenation behavior should be **defined by the string type's protocol implementation**, not hardcoded in `RuntimeOps.Add()`. An external language should be able to choose whether `string + int` concatenates, errors, or converts.

**Decision:** Move string concatenation logic out of `RuntimeOps.Add()` and into a protocol. Stash can keep its current behavior by implementing the protocol accordingly.

### 3.4 No Explicit Type Conversion Opcodes

**Current:** The VM has no `IntToFloat`, `FloatToInt`, `IntToString` etc. opcodes. Type conversions are either implicit (int→float promotion in mixed arithmetic) or handled by stdlib functions (`conv.toInt()`, `conv.toFloat()`).

**Problem:** An external language with explicit type conversions would need to call stdlib functions for something that should be a VM primitive. Function calls are expensive compared to inline opcodes.

**Recommendation:** Add conversion opcodes in a future phase. Not blocking for the type protocol work, but worth noting.

**Decision:** Defer to a separate spec. The stdlib functions work and the performance impact is marginal for a scripting language.

### 3.5 The FromObject/ToObject Boxing Bridge

**Current:** `IStashCallable.Call()` takes `List<object?>` arguments. `IStashCallable.CallDirect()` takes `ReadOnlySpan<StashValue>` but has a default implementation that boxes through `ToObject()`.

**Problem:** Built-in functions constantly cross the `StashValue` ↔ `object?` boundary. This causes:

- Boxing allocations for primitives
- Type information loss (a `long` becomes an `object`)
- Redundant re-wrapping (`StashValue.FromObject(call.ToObject())`)

This is already partially addressed by `CallDirect()` and `IBuiltInContext.InvokeCallbackDirect()`. But the protocol system needs to work natively with `StashValue` to avoid the same trap.

**Recommendation:** All protocol interfaces must operate on `StashValue`, never `object?`. This is a hard requirement.

**Decision:** Enforce `StashValue`-native protocols.

### 3.6 Mutable Struct Instances with Reference Equality

**Current:** `StashInstance` fields are mutable (`SetField` works). Equality is reference-based (two instances with identical fields are `!=`).

**Problem:** Combined, these create aliasing hazards:

```stash
struct Point { x, y }
let a = Point { x: 1, y: 2 };
let b = a;          // b is the SAME instance (reference copy)
b.x = 99;           // mutates a.x too!
io.println(a.x);    // 99 — surprise!
```

**Comparison:**

- Python: Same behavior (mutable objects, reference equality) — but has `copy.deepcopy()`
- JavaScript: Same behavior — widely considered a source of bugs
- Rust: Copy/Clone semantics prevent this
- Go: Structs are value types, copied on assignment

**Recommendation:** This is a Stash language design issue, not a VM type protocol issue. However, the type protocol should allow types to opt into **value equality** via an equatable protocol. Currently there's no way for a type to define custom equality without modifying `RuntimeValues.IsEqual()`.

**Decision:** The equatable protocol will address this. Individual types can choose reference or value equality.

### 3.7 Interface Conformance Is Declared, Not Verified

**Current:** `InstanceImplementsInterfaceName()` checks if the struct's `Interfaces` list contains the interface name. It does **not** verify that the struct actually has the required fields and methods.

```stash
interface Printable {
    fn toString() -> string
}

struct Broken implements Printable {
    x
    // Missing toString() method — no error!
}

let b = Broken { x: 1 };
b is Printable  // true — but calling toString() will fail
```

**Problem:** The VM trusts the compiler to validate interface conformance. This is fine for a single-compiler system, but an external compiler might get it wrong. More importantly, extension methods can add interface methods _after_ struct declaration, making compile-time validation incomplete.

**Recommendation:** Add optional runtime interface conformance checking (verify fields and methods exist when `IfaceDecl` is processed, or lazily on first `is` check). This overlaps with the bytecode verifier work from §5.5 of the Platform Target Readiness spec.

**Decision:** Address in the bytecode verifier, not in the type protocol. The protocol system should provide the introspection needed (`IIntrospectable` or similar) to make verification possible.

### 3.8 Extension Method Shadowing Is Silent

**Current:** `extend` blocks can add methods to any type. If a method already exists, the new one silently overwrites it (unless it's in `OriginalMethodNames`, which only protects methods from the original struct declaration).

**Problem:** Two independent modules could extend the same type with the same method name. The last one loaded wins. No warning, no error.

**Recommendation:** This is a language/module-system issue, not a VM type protocol issue. But the protocol system should document extension method resolution order clearly.

**Decision:** Out of scope for this spec. Note as a known issue.

### 3.9 No User-Defined Operators (Blocking for External Languages)

**Current:** Arithmetic operators (`+`, `-`, `*`, `/`, `%`, `**`) only work on types hardcoded in `RuntimeOps`. An external language that defines a `BigInt` or `Matrix` type cannot make `+` work on it.

**Problem:** Without operator protocols, external types are second-class citizens. They can only be manipulated via function calls, never via natural operator syntax.

**Recommendation:** The arithmetic protocol (§6.3) is the direct solution. Types that implement `IVMArithmetic` get operator dispatch automatically.

**Decision:** Core deliverable of this spec.

### 3.10 Typed Arrays Are a Separate Universe

**Current:** `StashTypedArray` subclasses (`StashIntArray`, `StashFloatArray`, etc.) have a completely different implementation from `List<StashValue>` (general arrays). They don't share interfaces, have different APIs, and the VM dispatches them separately everywhere.

**Problem:** This creates two parallel array systems:

- `int[]` is a `StashIntArray` — not iterable via the same path as regular arrays
- `[1, 2, 3]` is a `List<StashValue>` — different type, different dispatch
- Extension methods for "array" don't apply to typed arrays (different type name dispatch)

**Recommendation:** The type protocol should unify these. Both should implement `IVMIterable`, `IVMIndexable`, and `IVMFieldAccessible` — the VM dispatches uniformly. The specialized storage remains for performance, but the behavioral interface is shared.

**Decision:** Part of the protocol migration (§9).

### 3.11 No Map/Filter/Reduce at VM Level

**Current:** Array operations like `map`, `filter`, `reduce` are stdlib functions in the `arr` namespace. They work via UFCS (calling `myArray.map(fn)` dispatches to `arr.map(myArray, fn)`).

**Problem:** UFCS is hardcoded to specific namespace names ("arr" for arrays, "str" for strings). An external language can't register its own UFCS mappings. More fundamentally, UFCS is a Stash-specific language feature — it shouldn't be in the VM.

**Recommendation:** UFCS dispatch should be handled by the Stash compiler (emitting explicit `arr.map` calls), not by the VM's `GetFieldValue`. The VM should only dispatch via protocols.

**Decision:** Part of the VM dispatch refactoring (§7).

### 3.12 Truthiness Rules Are Hardcoded

**Current:** `IsFalsy()` hardcodes: null=falsy, false=falsy, 0=falsy, ""=falsy, Error=falsy, everything else=truthy.

**Problem:** An external language might have different truthiness rules (e.g., Python considers empty collections falsy; JavaScript considers `undefined` falsy but `{}` truthy; Lua considers only `nil` and `false` falsy).

**Recommendation:** Truthiness for primitives (null, bool, int, float) should remain hardcoded in the VM (these are universal). For `Obj`-tagged values, add a truthiness protocol that types can opt into. If a type doesn't implement the protocol, default to truthy (Lua's approach — simplest and least surprising).

**Decision:** Include in the protocol system (§6.7).

---

## 4. Prior Art

### 4.1 Lua Metatables

Lua's approach is the closest analog to what we need. Every table can have a **metatable** with metamethods:

- `__add`, `__sub`, `__mul`, `__div` — arithmetic operators
- `__eq`, `__lt`, `__le` — comparison operators
- `__index`, `__newindex` — field access
- `__call` — function call
- `__tostring` — string conversion
- `__len` — length operator
- `__pairs`, `__ipairs` — iteration

**Strengths:** Maximally flexible. Any type can override any operation.
**Weaknesses:** Runtime overhead (metatable lookup on every operation), hard to optimize (JIT must speculate on metatable contents), no static typing.

**Applicability to Stash:** Stash's types are C# objects, not Lua tables. We can use C# interfaces instead of metatables — same extensibility, better performance (interface dispatch is cheaper than dictionary lookup), and better tooling (compile-time checking of protocol implementation).

### 4.2 Python Dunder Methods

Python uses `__dunder__` methods for operator overloading:

- `__add__`, `__radd__` — forward and reverse addition
- `__eq__`, `__hash__` — equality and hashing
- `__getattr__`, `__setattr__` — attribute access
- `__iter__`, `__next__` — iteration protocol
- `__len__` — length
- `__bool__` — truthiness
- `__str__`, `__repr__` — string conversion

**Strengths:** Very mature, well-understood. Reverse operators (`__radd__`) handle asymmetric dispatch elegantly.
**Weaknesses:** Dynamic dispatch overhead. No way to know at compile time whether a type supports an operation.

**Applicability to Stash:** The concept of "forward + reverse" dispatch (try left operand first, then right) is worth adopting for asymmetric arithmetic (e.g., `Duration * int` vs `int * Duration`).

### 4.3 JVM Type System

The JVM has a fixed type system: primitives (`int`, `long`, `float`, `double`, etc.) and references (classes, interfaces, arrays). Operator overloading does not exist at the VM level — the JVM's `iadd` only works on `int`. Languages like Kotlin and Scala implement operator overloading via method calls that the compiler emits.

**Applicability to Stash:** The JVM proves that a VM can be a multi-language target without operator overloading at the VM level — if the compiler handles dispatch. But Stash's VM already has a universal `Add` opcode that works on multiple types, so we're past that point. The protocol approach is a better fit than the JVM's "compiler handles everything" model.

### 4.4 CLR Type System

The CLR has a richer type system than the JVM (value types, generics, operator overloading via static methods). Languages access operator overloading via `op_Addition` etc. static methods that the runtime knows how to dispatch.

**Applicability to Stash:** The CLR's approach of "static methods with known names" is essentially what C# interfaces give us. We can use that.

### Design Decision

**Use C# interfaces as the protocol mechanism.** This gives us:

- Zero runtime allocation (interface references are pointers)
- JIT-friendly dispatch (interface method tables)
- Compile-time verification (types that claim to implement a protocol are checked by the C# compiler)
- Familiar pattern for .NET developers

The interface approach is a hybrid of Lua's metatables (behavior defined by the type) and the CLR's operator methods (static dispatch when types are known).

---

## 5. Proposed Design: Type Protocols

### 5.1 Core Idea

Replace the VM's hardcoded type dispatch cascades with **protocol interfaces**. Each protocol represents a capability that a type can implement:

| Protocol             | Replaces                                                    | Controls                                   |
| -------------------- | ----------------------------------------------------------- | ------------------------------------------ | --- | --- |
| `IVMFieldAccessible` | 12-tier switch in `GetFieldValue`                           | `.field` access                            |
| `IVMFieldMutable`    | `SetFieldValue` hardcoded checks                            | `.field = value` assignment                |
| `IVMArithmetic`      | `RuntimeOps.Add/Sub/Mul/Div/Mod/Pow` cascades               | `+`, `-`, `*`, `/`, `%`, `**` operators    |
| `IVMComparable`      | `RuntimeOps.Compare` cascade                                | `<`, `>`, `<=`, `>=` operators             |
| `IVMEquatable`       | `RuntimeValues.IsEqual` cascade                             | `==`, `!=` operators                       |
| `IVMIterable`        | `ExecuteIterPrep` / `ExecuteIterLoop` cascades              | `for x in value` loops                     |
| `IVMIndexable`       | Index-based access in `GetFieldValue`                       | `value[index]` access                      |
| `IVMStringifiable`   | `RuntimeOps.Stringify` / `RuntimeValues.Stringify` cascades | String interpolation, `io.println`         |
| `IVMCallable`        | Already exists as `IStashCallable`                          | `value()` function calls                   |
| `IVMTyped`           | `ExecuteTypeOf` 20-way switch                               | `typeof` operator                          |
| `IVMTruthiness`      | `RuntimeOps.IsFalsy` cascade                                | Boolean coercion in `if`, `while`, `&&`, ` |     | `   |
| `IVMSized`           | Hardcoded `.length` checks                                  | `.length` property                         |

### 5.2 Dispatch Flow

The VM's dispatch logic changes from:

```
// BEFORE (hardcoded cascading type checks)
if (obj is StashInstance inst) return inst.GetField(name);
else if (obj is StashDictionary dict) return dict.Get(name);
else if (obj is StashNamespace ns) return ns.GetMember(name);
else if (obj is StashDuration dur) return dur switch { "totalMs" => ..., ... };
// ... 8 more branches
else throw new RuntimeError("Cannot access field");
```

To:

```
// AFTER (protocol dispatch)
if (obj is IVMFieldAccessible accessible)
    return accessible.VMGetField(name, span);
else
    throw new RuntimeError("Cannot access field");
```

**Critical constraint:** The IC mechanism remains the fast path. Protocol dispatch only fires on IC miss (state 0 or megamorphic). The IC already caches the result, so protocol dispatch overhead is amortized to near-zero for monomorphic call sites.

### 5.3 Naming Convention

All protocol interfaces are prefixed with `IVM` to:

1. Avoid collision with existing C# interfaces (`IComparable`, `IEquatable<T>`)
2. Signal that these are VM-level contracts, not general C# contracts
3. Group together in IntelliSense/autocomplete

All protocol methods are prefixed with `VM` (e.g., `VMGetField`, `VMAdd`) to avoid collision with existing methods on types that already have `GetField`, `Add`, etc.

---

## 6. Protocol Interfaces

All interfaces live in `Stash.Core/Runtime/Protocols/`. All methods operate on `StashValue` (never `object?`).

### 6.1 IVMTyped — Type Identity

```csharp
/// <summary>
/// Provides type identity for the typeof operator and type checking.
/// </summary>
public interface IVMTyped
{
    /// <summary>
    /// Returns the type name for the typeof operator.
    /// Must be stable — same type always returns same name.
    /// </summary>
    string VMTypeName { get; }
}
```

**Used by:** `ExecuteTypeOf`, `ExecuteIs`

**Current dispatch replaced:** 20-way switch in `ExecuteTypeOf` (for Obj-tagged values only; primitive tags remain hardcoded).

### 6.2 IVMFieldAccessible — Field/Property Read Access

```csharp
/// <summary>
/// Supports reading named fields/properties via the dot operator.
/// </summary>
public interface IVMFieldAccessible
{
    /// <summary>
    /// Get the value of a named field. Returns true if the field exists.
    /// </summary>
    bool VMTryGetField(string name, out StashValue value, SourceSpan? span);
}
```

**Design choice:** `TryGetField` with `bool` return instead of throwing. This lets the VM fall through to extension methods / UFCS if the type doesn't have the field. Throwing on missing fields is the type's choice (it can throw in the method body and return false from the catch, or simply return false for missing fields).

**Used by:** `ExecuteGetField`, `ExecuteGetFieldIC` (slow path)

### 6.3 IVMFieldMutable — Field/Property Write Access

```csharp
/// <summary>
/// Supports writing named fields/properties via dot-assignment.
/// </summary>
public interface IVMFieldMutable
{
    /// <summary>
    /// Set the value of a named field.
    /// </summary>
    void VMSetField(string name, StashValue value, SourceSpan? span);
}
```

**Separate from IVMFieldAccessible** because many types support reading but not writing (enums, namespaces, durations, etc.). Splitting follows Interface Segregation.

### 6.4 IVMArithmetic — Operator Overloading

```csharp
/// <summary>
/// Supports arithmetic operators (+, -, *, /, %, **).
/// The VM calls the LEFT operand's protocol first. If it returns false,
/// the VM calls the RIGHT operand's protocol (reverse dispatch).
/// </summary>
public interface IVMArithmetic
{
    /// <summary>
    /// Try to perform the arithmetic operation. Returns false if this type
    /// cannot handle the given operation with the given other operand.
    /// </summary>
    bool VMTryArithmetic(ArithmeticOp op, StashValue other, bool isLeftOperand,
                         out StashValue result, SourceSpan? span);
}

/// <summary>
/// Arithmetic operations dispatched via the protocol.
/// </summary>
public enum ArithmeticOp : byte
{
    Add,
    Subtract,
    Multiply,
    Divide,
    Modulo,
    Power,
    Negate  // Unary: 'other' is unused
}
```

**Reverse dispatch** (borrowed from Python's `__radd__`): When evaluating `a + b`:

1. If `a` implements `IVMArithmetic`, call `a.VMTryArithmetic(Add, b, isLeftOperand: true, ...)`. If it returns `true`, done.
2. If step 1 returns `false` or `a` doesn't implement `IVMArithmetic`, and `b` implements `IVMArithmetic`, call `b.VMTryArithmetic(Add, a, isLeftOperand: false, ...)`.
3. If both return `false`, throw `RuntimeError`.

This handles asymmetric cases like `Duration * 2` (Duration is left, knows how to multiply by int) and `2 * Duration` (int is left, doesn't implement IVMArithmetic; Duration is right, gets reverse dispatch with `isLeftOperand: false`).

**Primitives are excluded.** The VM's fast paths for int+int, float+float, etc. remain hardcoded and never touch the protocol. The protocol only fires for Obj-tagged values in the `RuntimeOps` slow path.

### 6.5 IVMComparable — Ordering

```csharp
/// <summary>
/// Supports ordering comparisons (<, >, <=, >=).
/// </summary>
public interface IVMComparable
{
    /// <summary>
    /// Compare this value to another. Returns negative, zero, or positive.
    /// Returns false if the values are not comparable.
    /// </summary>
    bool VMTryCompare(StashValue other, out int result, SourceSpan? span);
}
```

### 6.6 IVMEquatable — Custom Equality

```csharp
/// <summary>
/// Defines custom equality semantics for the == operator.
/// Types that don't implement this use reference equality (the default for Obj-tagged values).
/// </summary>
public interface IVMEquatable
{
    /// <summary>
    /// Returns true if this value equals the other value.
    /// Only called when both values have the same Obj tag.
    /// </summary>
    bool VMEquals(StashValue other);
}
```

**Note:** This is for the `==` operator only. `StashValue.Equals()` (used by C# collections) remains unchanged — it keeps its bit-level comparison for Float and `object.Equals` for Obj.

### 6.7 IVMTruthiness — Boolean Coercion

```csharp
/// <summary>
/// Defines truthiness for boolean coercion contexts (if, while, &&, ||).
/// Types that don't implement this are always truthy (Lua convention).
/// </summary>
public interface IVMTruthiness
{
    /// <summary>
    /// Returns true if this value is falsy.
    /// </summary>
    bool VMIsFalsy { get; }
}
```

**Default for Obj types without this protocol:** Truthy. This matches Lua and avoids surprises for external types.

### 6.8 IVMIterable — For-In Loop Support

```csharp
/// <summary>
/// Supports iteration via for-in loops.
/// </summary>
public interface IVMIterable
{
    /// <summary>
    /// Create an iterator for this value. The iterator must be an IVMIterator.
    /// </summary>
    IVMIterator VMGetIterator(bool indexed);
}

/// <summary>
/// Iterator state for for-in loops. The VM calls MoveNext/Current in the loop.
/// </summary>
public interface IVMIterator
{
    /// <summary>
    /// Advance to the next element. Returns false when exhausted.
    /// </summary>
    bool MoveNext();

    /// <summary>
    /// The current element value.
    /// </summary>
    StashValue Current { get; }

    /// <summary>
    /// The current index/key (for indexed iteration: `for value, key in collection`).
    /// </summary>
    StashValue CurrentKey { get; }
}
```

**Design:** This replaces the monolithic `IteratorState` class that uses `Collection` field + type dispatch. Each iterable type provides its own iterator with type-specific logic.

### 6.9 IVMIndexable — Bracket Access

```csharp
/// <summary>
/// Supports bracket-based index access (value[index]).
/// </summary>
public interface IVMIndexable
{
    StashValue VMGetIndex(StashValue index, SourceSpan? span);
    void VMSetIndex(StashValue index, StashValue value, SourceSpan? span);
}
```

### 6.10 IVMStringifiable — String Conversion

```csharp
/// <summary>
/// Defines how a value is converted to string for interpolation, println, etc.
/// Types that don't implement this fall back to object.ToString().
/// </summary>
public interface IVMStringifiable
{
    /// <summary>
    /// Convert this value to its string representation.
    /// </summary>
    string VMToString();
}
```

### 6.11 IVMSized — Length Property

```csharp
/// <summary>
/// Supports the .length property.
/// </summary>
public interface IVMSized
{
    /// <summary>
    /// Returns the length/count of this value.
    /// </summary>
    long VMLength { get; }
}
```

**Separated from IVMFieldAccessible** because `.length` is a hot path that the VM can special-case (check for `IVMSized` before falling through to general field access). This mirrors Lua's `__len` metamethod.

---

## 7. VM Dispatch Refactoring

### 7.1 Arithmetic Dispatch

The refactored `RuntimeOps.Add` becomes:

```csharp
public static StashValue Add(StashValue left, StashValue right, SourceSpan? span)
{
    // Byte → Int promotion (keep as-is)
    if (left.IsByte) left = StashValue.FromInt(left.AsByte);
    if (right.IsByte) right = StashValue.FromInt(right.AsByte);

    // Primitive fast paths (keep as-is)
    if (left.IsInt && right.IsInt)
        return StashValue.FromInt(left.AsInt + right.AsInt);
    if (left.IsNumeric && right.IsNumeric)
        return StashValue.FromFloat(ToDouble(left) + ToDouble(right));

    // String concatenation (keep as language-level behavior)
    // ... existing string logic ...

    // Protocol dispatch (replaces all domain-type branches)
    if (left.IsObj && left.AsObj is IVMArithmetic leftArith)
    {
        if (leftArith.VMTryArithmetic(ArithmeticOp.Add, right, true, out StashValue result, span))
            return result;
    }
    if (right.IsObj && right.AsObj is IVMArithmetic rightArith)
    {
        if (rightArith.VMTryArithmetic(ArithmeticOp.Add, left, false, out StashValue result, span))
            return result;
    }

    throw new RuntimeError("Operands must be numbers or strings.", span);
}
```

**String concatenation:** This is the one case where Stash-specific behavior should remain in `RuntimeOps` rather than moving to a protocol. Reason: strings are `Obj`-tagged but are raw C# `string` objects — they can't implement interfaces. The VM needs to handle `string + X` directly.

**Alternative considered:** Wrap strings in a `StashString` class that implements `IVMArithmetic`. Rejected — this would add a heap allocation for every string and break the current zero-copy string representation. The performance cost is unacceptable.

### 7.2 Field Access Dispatch

The refactored `GetFieldValue` becomes:

```csharp
private object? GetFieldValue(object? obj, string name, SourceSpan? span)
{
    // 1. Fast path: IVMSized for .length
    if (name == "length" && obj is IVMSized sized)
        return (long)sized.VMLength;

    // 2. Protocol dispatch: IVMFieldAccessible
    if (obj is IVMFieldAccessible accessible)
    {
        if (accessible.VMTryGetField(name, out StashValue value, span))
            return value.ToObject();
    }

    // 3. Special cases for raw C# types that can't implement interfaces
    if (obj is string s && name == "length")
        return (long)s.Length;
    if (obj is List<StashValue> list && name == "length")
        return (long)list.Count;

    // 4. Extension methods
    // ... existing extension method lookup ...

    // 5. UFCS (only for Stash — external languages won't use this)
    // ... existing UFCS lookup ...

    throw new RuntimeError($"Cannot access field '{name}' on {RuntimeValues.Stringify(obj)}.", span);
}
```

### 7.3 Iteration Dispatch

The refactored `ExecuteIterPrep` becomes:

```csharp
private void ExecuteIterPrep(ref CallFrame frame, uint inst)
{
    byte a = Instruction.GetA(inst);
    byte b = Instruction.GetB(inst);
    int @base = frame.BaseSlot;
    object? val = _stack[@base + a].ToObject();
    bool indexed = b != 0;

    IVMIterator iterator;

    if (val is IVMIterable iterable)
    {
        iterator = iterable.VMGetIterator(indexed);
    }
    // Special cases for raw C# types
    else if (val is string str)
    {
        iterator = new StringIterator(str);
    }
    else if (val is List<StashValue> list)
    {
        iterator = new ListIterator(new List<StashValue>(list)); // snapshot
    }
    else
    {
        throw new RuntimeError($"Value is not iterable: {RuntimeValues.Stringify(val)}.", span);
    }

    _stack[@base + a] = StashValue.FromObj(iterator);
}
```

### 7.4 What Stays Hardcoded

Some dispatch must remain hardcoded because the types are raw C# types that cannot implement interfaces:

| Type               | Why                              | Protocol Alternative                               |
| ------------------ | -------------------------------- | -------------------------------------------------- |
| `string`           | Raw C# `string`, no wrapper      | Hardcode string concat, `.length`, iteration, UFCS |
| `List<StashValue>` | Raw C# `List<T>`, no wrapper     | Hardcode `.length`, iteration, indexing, UFCS      |
| `long` (int)       | Stored in `_data`, not an object | N/A (primitive fast path)                          |
| `double` (float)   | Stored in `_data`, not an object | N/A (primitive fast path)                          |
| `bool`             | Stored in `_data`, not an object | N/A (primitive fast path)                          |
| `byte`             | Stored in `_data`, not an object | N/A (primitive fast path)                          |

**Future optimization:** If we ever wrap `List<StashValue>` in a `StashArray` class (which would implement all protocols), the hardcoded array dispatch can be removed too. This is deferred.

---

## 8. Type Registration

### 8.1 IVMTypeRegistrar

External languages need a way to register their types with the VM:

```csharp
/// <summary>
/// Registry for VM-level type information.
/// Allows external types to register type names for typeof/is dispatch.
/// </summary>
public interface IVMTypeRegistrar
{
    /// <summary>
    /// Register a type name mapping. When typeof encounters an object of the given
    /// CLR type, it returns the registered type name.
    /// </summary>
    void RegisterTypeName<T>(string vmTypeName) where T : class;

    /// <summary>
    /// Register a type check for the 'is' operator. When 'value is TypeName' is
    /// evaluated and TypeName matches, the predicate is called.
    /// </summary>
    void RegisterTypeCheck(string vmTypeName, Func<object, bool> predicate);
}
```

**Why this is needed:** Without protocols, `ExecuteTypeOf` uses a 20-way C# type pattern match. With protocols, types that implement `IVMTyped` are handled automatically. But external types might also need to register type-check predicates for the `is` operator (e.g., `value is Matrix` should work even if `Matrix` is defined by an external language).

### 8.2 Registration Flow

```csharp
// External language registers its types with the VM
var engine = new StashEngine();
engine.RegisterType<Matrix>("matrix", obj => obj is Matrix);
// Now: typeof(myMatrix) returns "matrix"
// And:  myMatrix is "matrix" returns true
```

The registration happens via `StashEngine` and is forwarded to the `VirtualMachine` instance. The VM stores registrations in a `Dictionary<Type, string>` (for typeof) and a `Dictionary<string, Func<object, bool>>` (for is).

---

## 9. Impact on Existing Stash Types

Each existing type gets migrated to implement the relevant protocols:

| Type                               | Implements                                                                                                         |
| ---------------------------------- | ------------------------------------------------------------------------------------------------------------------ |
| `StashInstance`                    | `IVMTyped`, `IVMFieldAccessible`, `IVMFieldMutable`, `IVMStringifiable`, `IVMEquatable`                            |
| `StashDictionary`                  | `IVMTyped`, `IVMFieldAccessible`, `IVMFieldMutable`, `IVMIterable`, `IVMIndexable`, `IVMSized`, `IVMStringifiable` |
| `StashStruct`                      | `IVMTyped`, `IVMFieldAccessible`, `IVMStringifiable`                                                               |
| `StashEnum`                        | `IVMTyped`, `IVMFieldAccessible`, `IVMIterable`, `IVMStringifiable`                                                |
| `StashEnumValue`                   | `IVMTyped`, `IVMFieldAccessible`, `IVMEquatable`, `IVMStringifiable`                                               |
| `StashNamespace`                   | `IVMTyped`, `IVMFieldAccessible`, `IVMStringifiable`                                                               |
| `StashError`                       | `IVMTyped`, `IVMFieldAccessible`, `IVMTruthiness`, `IVMStringifiable`                                              |
| `StashRange`                       | `IVMTyped`, `IVMIterable`, `IVMStringifiable`                                                                      |
| `StashDuration`                    | `IVMTyped`, `IVMFieldAccessible`, `IVMArithmetic`, `IVMComparable`, `IVMStringifiable`                             |
| `StashByteSize`                    | `IVMTyped`, `IVMFieldAccessible`, `IVMArithmetic`, `IVMComparable`, `IVMStringifiable`                             |
| `StashSemVer`                      | `IVMTyped`, `IVMFieldAccessible`, `IVMComparable`, `IVMStringifiable`                                              |
| `StashIpAddress`                   | `IVMTyped`, `IVMFieldAccessible`, `IVMArithmetic`, `IVMComparable`, `IVMStringifiable`                             |
| `StashSecret`                      | `IVMTyped`, `IVMTruthiness`, `IVMStringifiable`, `IVMArithmetic` (for taint propagation)                           |
| `StashFuture`                      | `IVMTyped`, `IVMStringifiable`                                                                                     |
| `StashBoundMethod`                 | `IVMTyped`                                                                                                         |
| `StashTypedArray` (all subclasses) | `IVMTyped`, `IVMIterable`, `IVMIndexable`, `IVMSized`, `IVMStringifiable`                                          |

### Migration Notes

- **StashDuration arithmetic:** Currently `RuntimeOps.Add` has `if (lObj is StashDuration durL && rObj is StashDuration durR) return durL.Add(durR)`. After migration, `StashDuration` implements `IVMArithmetic.VMTryArithmetic` and that branch is removed from `RuntimeOps`.

- **StashSecret taint propagation:** The secret's `IVMArithmetic` implementation wraps the result in a new `StashSecret` to propagate taint. The current taint logic in `RuntimeOps.Add` (which checks for secrets before string concat) moves into `StashSecret.VMTryArithmetic`.

- **StashInstance field access:** The IC mechanism doesn't change. The IC fast path continues to guard on struct identity and cache field slot indices. The protocol is only invoked on IC miss, and the protocol implementation does the same field lookup that `GetFieldValue` does today.

---

## 10. Migration Strategy

### Phase 1: Define Protocols (Non-Breaking)

1. Create `Stash.Core/Runtime/Protocols/` directory
2. Define all 12 protocol interfaces
3. Define `ArithmeticOp` enum
4. Define `IVMIterator` interface and common iterator implementations
5. No VM changes, no type changes — purely additive

### Phase 2: Implement Protocols on Stash Types (Non-Breaking)

1. Add protocol implementations to each Stash type (StashDuration, StashByteSize, etc.)
2. Move the dispatch logic from `RuntimeOps`/`GetFieldValue` into the types' protocol methods
3. Add unit tests verifying protocol implementations match current behavior
4. No VM dispatch changes yet — the old hardcoded paths still work

### Phase 3: Refactor VM Dispatch (Behavioral Parity)

1. Replace hardcoded type cascades in `RuntimeOps` with protocol dispatch
2. Replace `GetFieldValue` cascade with protocol dispatch
3. Replace `ExecuteIterPrep` cascade with protocol dispatch
4. Replace `ExecuteTypeOf` switch with protocol dispatch
5. Replace `RuntimeOps.IsFalsy` cascade with protocol dispatch
6. **Keep hardcoded paths for string and List<StashValue>** (raw C# types)
7. Run full test suite — all ~2,000 tests must pass with no changes

### Phase 4: External Type Registration (New Capability)

1. Implement `IVMTypeRegistrar` on `VirtualMachine` / `StashEngine`
2. Add documentation for external type authors
3. Add integration tests with a synthetic external type

### Estimated Impact

- **Phase 1:** ~200 lines of interface definitions
- **Phase 2:** ~800 lines of protocol implementations (mostly moving existing logic into methods)
- **Phase 3:** ~500 lines changed in VM dispatch files (net reduction — removing cascades, adding protocol calls)
- **Phase 4:** ~200 lines for registration mechanism

Total: ~1,700 lines of changes, with a net reduction in VM dispatch complexity.

---

## 11. What We Explicitly Defer

### 11.1 StashArray Wrapper Class

Currently arrays are raw `List<StashValue>`, which can't implement interfaces. A `StashArray` wrapper would enable full protocol participation, but requires migrating all array-related code (stdlib, compiler, tests). Deferred until protocol system proves its value.

### 11.2 StashString Wrapper Class

Same as above for strings. Even more impactful since strings are the most common Obj type.

### 11.3 Protocol-Based IC Guards

The current IC guards on C# type identity (`obj.AsObj == ic.Guard` for namespaces, `si.Struct == ic.Guard` for structs). A protocol-based IC would guard on protocol implementation. This is an optimization opportunity but not required — the current IC mechanism works with protocols because the IC handles the fast path and protocols handle the slow path.

### 11.4 Compile-Time Protocol Specialization

The Stash compiler could statically resolve protocol calls when the type is known (e.g., `Duration + Duration` could emit a direct call instead of going through the protocol dispatch). This is a compiler optimization, not a VM change.

### 11.5 Protocol Composition / Default Implementations

C# 8+ supports default interface methods. We could use these to provide common implementations (e.g., `IVMComparable` could provide default `<=` and `>=` based on `CompareTo`). Deferred to avoid complexity in Phase 1.

---

## 12. Risks & Tradeoffs

### 12.1 Performance Regression Risk (High Concern)

**Risk:** Interface dispatch (`is IVMFieldAccessible`) is slower than direct type checking (`is StashInstance`) due to vtable indirection.

**Measurement needed:** Benchmark the hot paths before and after. The critical path is `GetFieldIC` → slow path → `GetFieldValue`. If the IC hit rate is high (which it should be for monomorphic call sites), the protocol dispatch cost is amortized.

**Mitigation:**

1. Keep primitive fast paths untouched (int+int, float+float, etc.)
2. Keep IC mechanism untouched (fast path for monomorphic sites)
3. Profile before committing — if benchmarks show >5% regression on the `bench_*` suite, reconsider
4. The C# JIT is good at devirtualizing interface calls when there's a single implementer — use `[MethodImpl(MethodImplOptions.AggressiveInlining)]` on protocol methods where appropriate

### 12.2 API Surface Commitment (Medium Concern)

**Risk:** Every protocol interface becomes public API. Changing a protocol's signature is a breaking change for external type authors.

**Mitigation:** Mark all protocols as `[Experimental]` in Phase 1. Stabilize after Phase 3 proves the design works. Consider a stability tier system (stable / unstable / experimental).

### 12.3 Behavioral Divergence During Migration (Low Concern)

**Risk:** Moving dispatch logic from `RuntimeOps` into type implementations could introduce subtle behavioral differences.

**Mitigation:** Phase 2 runs both paths and asserts they produce identical results. The test suite has ~2,000 tests covering edge cases.

### 12.4 Complexity for Stash Contributors (Low Concern)

**Risk:** Adding a new type to Stash now requires implementing protocol interfaces instead of adding a branch to `RuntimeOps`.

**Counter-argument:** This is actually _easier_. Protocol implementation is localized to the type's class file. The current approach requires touching 4-6 VM files and knowing which dispatch cascades exist.

### 12.5 String/Array Asymmetry (Medium Concern)

**Risk:** Strings and arrays can't implement protocols (raw C# types), so they remain hardcoded. This creates a two-tier system where some types use protocols and others use hardcoded dispatch.

**Mitigation:** Document clearly which types are "VM primitives" (hardcoded) vs "protocol types" (extensible). The asymmetry is manageable because strings and arrays have stable, well-defined behavior that rarely changes.

**Long-term:** If this becomes a real problem, introduce `StashArray` and `StashString` wrapper types that implement protocols. This is a major migration but makes the system fully uniform.

---

## Decision Log

| Date       | Decision                                       | Rationale                                                                           |
| ---------- | ---------------------------------------------- | ----------------------------------------------------------------------------------- |
| 2026-04-17 | Created as backlog design spec                 | Upgrade from deferred §6.1 to active design based on growing VM dispatch complexity |
| 2026-04-17 | C# interfaces over Lua-style metatables        | Compile-time safety, JIT-friendly dispatch, familiar .NET pattern                   |
| 2026-04-17 | `IVM` prefix for all protocols                 | Avoid collision with existing C# interfaces, group in IntelliSense                  |
| 2026-04-17 | `StashValue`-native protocol methods           | Avoid the `FromObject`/`ToObject` boxing bridge                                     |
| 2026-04-17 | Keep string/array hardcoded                    | Wrapping would add allocation overhead; pragmatic compromise                        |
| 2026-04-17 | Forward+reverse arithmetic dispatch            | Handles asymmetric cases like `2 * Duration` cleanly                                |
| 2026-04-17 | Default truthy for types without IVMTruthiness | Matches Lua convention; least surprising for external types                         |
| 2026-04-17 | Fix NaN == NaN in `==` operator                | IEEE 754 compliance; current behavior is a bug, not a design choice                 |
