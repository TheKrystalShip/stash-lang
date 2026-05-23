# Typed Arrays — Primitive Array Types for Stash

> **Status:** Draft
> **Created:** 2026-04-14
> **Depends on:** Nothing (foundational change)
> **Enables:** Byte Arrays / Binary Data (Phase 2), Struct/Enum Typed Arrays (Phase 3)
> **Impact:** Language-level change — Parser, VM, Stdlib, Analysis, LSP, DAP, Playground, Extension

---

## 1. Motivation & Goals

### Problem

Stash has a single `array` type backed by `List<StashValue>`. Every array is heterogeneous — `[1, "hello", true, null]` is legal. This creates three problems:

1. **No type safety** — a function receiving `scores` has no guarantee the array contains only integers. Every element must be checked individually, or the function silently produces wrong results.
2. **No memory efficiency path** — `List<StashValue>` stores 24 bytes per element (tag + data + object ref) regardless of element type. A `byte[]` should use 1 byte/element, an `int[]` should use 8. This 3-24x overhead blocks native binary protocol support.
3. **No expressiveness** — you cannot declare intent: `fn average(nums: int[])` is impossible today because type hints accept only bare identifiers, not parameterized types.

### Goals

1. **Type-safe arrays**: `int[]`, `float[]`, `string[]`, `bool[]` — runtime-enforced element types
2. **Extensible foundation**: The design MUST accommodate `byte[]` (Phase 2) with specialized storage without rearchitecting
3. **Backward compatible**: Existing `array` (`List<StashValue>`) continues to work unchanged. No breaking changes.
4. **Consistent**: Typed arrays work with `arr.*` functions, `for-in` loops, spread, destructuring, `typeof`, `is`, and all language features that touch arrays

### Non-Goals

- **Not** adding generics to the language (no `Array<T>` or `Dict<K, V>`)
- **Not** changing untyped arrays — `[1, "hello", true]` remains valid
- **Not** adding auto-inference — `let x = [1, 2, 3]` stays `array`, not `int[]`
- **Not** adding `byte` as a type (that's Phase 2)
- **Not** adding struct/enum typed arrays yet (Phase 3)

---

## 2. Design Overview

A new abstract runtime type `StashTypedArray` provides a common interface for per-type subclasses that use **native C# backing arrays** (`long[]`, `double[]`, `string[]`, `bool[]`). Each subclass validates elements on mutation and wraps/unwraps `StashValue` at the API boundary. The type annotation syntax is extended to support `T[]`. Typed arrays participate in the existing type system as a family of types that are also recognized as arrays.

> **Revision (2026-04-14):** Original design used `StashGenericTypedArray` wrapping `List<StashValue>` with validation. Changed to per-type native backing arrays for three reasons: (1) **memory** — `long[]` uses 8 bytes/element vs 24 bytes for `List<StashValue>`, a 3x saving; (2) **safety** — no raw `List<StashValue>` can leak to bypass validation; (3) **Phase 2 alignment** — `StashByteArray` with `byte[]` backing is the same architectural pattern, not a special case.

```stash
let scores: int[] = [95, 87, 100, 64];
let names: string[] = ["alice", "bob"];
let ratios: float[] = [0.5, 1.0, 1.5];
let flags: bool[] = [true, false, true];

arr.push(scores, 42);         // OK
arr.push(scores, "hello");    // RuntimeError: cannot add string to int[]

typeof(scores)                // "int[]"
scores is int[]               // true
scores is array               // true  (typed arrays ARE arrays)
```

### Key Design Principles

1. **Typed arrays ARE arrays** — `x is array` returns `true` for typed arrays. Functions that accept `array` also accept `int[]`. The subtype relationship is: `int[] ⊂ array`.
2. **Enforcement at mutation boundaries** — type checking happens on `push`, `set`, `splice`, `fill`, `unshift` etc. Read operations are unchecked.
3. **No auto-inference** — `[1, 2, 3]` always creates a generic `List<StashValue>`. To get `int[]`, you must explicitly declare: `let x: int[] = [1, 2, 3]` or use `arr.typed([1, 2, 3], "int")`.
4. **No implicit conversion** — assigning `int[]` to a variable is fine, but implicit element coercion follows strict rules (see Section 6).

---

## 3. Syntax

### 3a. Type Annotation Grammar

Current grammar for type hints:

```
type_hint ::= IDENTIFIER
```

Extended grammar:

```
type_hint ::= IDENTIFIER
            | IDENTIFIER "[" "]"
```

The `[]` suffix is unambiguous: after a type hint, the next token is always `=`, `,`, `)`, `{`, or `->`, none of which start with `[`.

### 3b. Where Typed Array Annotations Appear

All positions that currently accept type hints gain `T[]` support:

```stash
// Variable declarations
let scores: int[] = [95, 87, 100];
const NAMES: string[] = ["alice", "bob"];

// Function parameters
fn average(nums: float[]) -> float {
    return arr.reduce(nums, 0.0, fn(acc, n) => acc + n) / arr.len(nums);
}

// Function return types
fn getScores() -> int[] {
    return [95, 87, 100];
}

// Struct fields
struct Config {
    hosts: string[],
    ports: int[],
    verbose: bool
}

// For-in loop variables (rare but valid)
for (let item: int[] in array_of_arrays) {
    // item is typed as int[]
}
```

### 3c. Array Construction

Three paths to create typed arrays:

**Path 1: Type-annotated declaration (primary)**

```stash
let nums: int[] = [1, 2, 3];
```

The RHS `[1, 2, 3]` evaluates to a generic `List<StashValue>`. The type annotation triggers conversion: all elements are validated, and the list is wrapped in a `StashTypedArray`. If any element fails validation, a `RuntimeError` is thrown at the declaration site.

**Path 2: Explicit construction via `arr.typed()`**

```stash
let nums = arr.typed([1, 2, 3], "int");      // from existing elements
let empty = arr.typed([], "float");           // empty typed array
let copy = arr.typed(other_array, "string");  // validate + wrap existing array
```

This is the programmatic path — useful when the element type is determined at runtime, or when creating typed arrays inside expressions (function arguments, etc.).

**Path 3: `arr.new()` with capacity (zero-initialized)**

```stash
let buffer: int[] = arr.new("int", 100);  // 100 zeros
```

Useful for pre-allocating. Zero values by element type:

- `int` → `0`
- `float` → `0.0`
- `string` → `""`
- `bool` → `false`

### 3d. What Is NOT Supported

```stash
// NO typed array literals (ambiguous with indexing)
let x = int[1, 2, 3];         // ✗ — Not valid syntax

// NO fixed-size arrays in type syntax
let x: int[10] = [...];       // ✗ — Use arr.new("int", 10) for pre-allocation

// NO nested typed arrays (Phase 3+)
let x: int[][] = [[1], [2]];  // ✗ — Not in Phase 1

// NO generic syntax
let x: Array<int> = [1, 2];   // ✗ — Stash has no generics
```

> **Decision:** Fixed-size arrays (`int[10]`) are rejected in type syntax. The size is a capacity concern, not a type concern. Putting runtime values (`int[n]`) in type annotations would require expression parsing in type positions — a major grammar complication. Pre-allocation is handled by `arr.new("int", 100)`, which creates a growable typed array with the specified initial capacity. If true fixed-size arrays are needed later (ring buffers, protocol frames), they should be a separate concept (e.g., `arr.fixed("int", 10)` returning a fixed-capacity subclass).

---

## 4. Runtime Type: `StashTypedArray`

### 4a. Abstract Base Class

```
StashTypedArray (abstract)
├── ElementTypeName: string         — "int", "float", "string", "bool"
├── Count: int                      — number of elements
├── Capacity: int                   — current backing array capacity
├── Get(index: int): StashValue     — read element (wraps native → StashValue)
├── Set(index: int, val): void      — write element (validates + unwraps)
├── Add(val): void                  — append element (validates, grows if needed)
├── RemoveLast(): StashValue        — pop last element
├── Insert(index, val): void        — insert at position (validates, shifts)
├── RemoveAt(index): void           — remove at position (shifts)
├── Clone(): StashTypedArray        — shallow copy preserving type + backing
├── CopyTo(dest, srcIdx, len): void — bulk copy for slice/spread operations
└── Enumerator: custom              — iterates yielding StashValue per element
```

### 4b. Per-Type Subclasses with Native Backing

Each primitive type gets a dedicated subclass backed by the matching C# array:

```
StashTypedArray (abstract)
├── StashIntArray       — long[]    backing  (8 bytes/element)
├── StashFloatArray     — double[]  backing  (8 bytes/element)
├── StashStringArray    — string[]  backing  (8 bytes/element — refs only)
├── StashBoolArray      — bool[]    backing  (1 byte/element)
└── StashByteArray      — byte[]    backing  (1 byte/element, Phase 2)
```

**Memory savings over `List<StashValue>` (24 bytes/element):**

| Type               | Backing    | Bytes/Element | Savings | 1000 elements |
| ------------------ | ---------- | ------------- | ------- | ------------- |
| `int[]`            | `long[]`   | 8             | 3x      | 8 KB vs 24 KB |
| `float[]`          | `double[]` | 8             | 3x      | 8 KB vs 24 KB |
| `string[]`         | `string[]` | 8 (ref)       | 3x      | 8 KB vs 24 KB |
| `bool[]`           | `bool[]`   | 1             | 24x     | 1 KB vs 24 KB |
| `byte[]` (Phase 2) | `byte[]`   | 1             | 24x     | 1 KB vs 24 KB |

**How wrapping works (example — `StashIntArray`):**

```csharp
public override StashValue Get(int index)
{
    return StashValue.FromInt(_data[index]);  // long → StashValue (inlined, no alloc)
}

public override void Set(int index, StashValue val)
{
    if (!val.IsInt) throw new RuntimeError(...);
    _data[index] = val.AsInt;  // StashValue → long (inlined)
}

public override void Add(StashValue val)
{
    if (!val.IsInt) throw new RuntimeError(...);
    EnsureCapacity(_count + 1);  // grow if needed (doubling strategy)
    _data[_count++] = val.AsInt;
}
```

**Growth strategy:** Same as `List<T>` — double capacity when full, starting from 4. Use `Array.Copy` for resize. Growth amortizes to O(1) per append, same as today.

**Why per-type subclasses, not a generic `StashTypedArray<T>`:** Native AOT. The CLI uses `PublishAot=true`. Generic virtual methods with value-type parameters can cause issues with AOT's static compilation model. Concrete subclasses are fully statically analyzable.

### 4c. Safety: No Leaking Raw Lists

The original design exposed `GetInnerList(): List<StashValue>` for `arr.*` compat. This is now removed. With native backing arrays, there IS no `List<StashValue>` to return. All access goes through `Get(i)`/`Set(i, val)`/`Count` on the abstract base. This means:

- `arr.*` read-only functions use the `StashTypedArray` interface directly instead of extracting a raw list
- No mutation path can bypass validation
- Type safety is structurally enforced, not just conventionally enforced

### 4d. StashValue Integration

`StashTypedArray` is a heap-allocated object stored via `StashValue.FromObj()`:

```
StashValue = { Tag: Obj, _obj: StashTypedArray instance }
```

It participates in the type system through the existing `Obj` tag, just like `StashDictionary`, `StashRange`, `StashDuration`, etc.

### 4c. StashValue Integration

`StashTypedArray` is a heap-allocated object stored via `StashValue.FromObj()`:

```
StashValue = { Tag: Obj, _obj: StashTypedArray instance }
```

It participates in the type system through the existing `Obj` tag, just like `StashDictionary`, `StashRange`, `StashDuration`, etc.

---

## 5. Type System Integration

### 5a. `typeof()` Returns

| Value                 | `typeof()`   |
| --------------------- | ------------ |
| `[1, 2, 3]` (untyped) | `"array"`    |
| `int[] [1, 2, 3]`     | `"int[]"`    |
| `float[] [1.0]`       | `"float[]"`  |
| `string[] ["a"]`      | `"string[]"` |
| `bool[] [true]`       | `"bool[]"`   |

Implementation: In `GlobalBuiltIns.cs`, add `StashTypedArray ta => $"{ta.ElementTypeName}[]"` to the `typeof` switch before the generic `"array"` case.

### 5b. `is` Operator

| Expression             | Result  | Why                           |
| ---------------------- | ------- | ----------------------------- |
| `int_arr is int[]`     | `true`  | Exact type match              |
| `int_arr is array`     | `true`  | Typed arrays are arrays       |
| `int_arr is string[]`  | `false` | Element type mismatch         |
| `int_arr is float[]`   | `false` | No implicit promotion on `is` |
| `generic_arr is int[]` | `false` | Generic arrays are NOT typed  |
| `generic_arr is array` | `true`  | Obviously                     |

Implementation: In `VirtualMachine.TypeOps.cs`:

- `"array"` check becomes: `value is List<StashValue> || value is StashTypedArray`
- Add pattern: `typeName ends with "[]"` → parse element type, check `value is StashTypedArray ta && ta.ElementTypeName == parsedElement`
- Add `"int[]"`, `"float[]"`, `"string[]"`, `"bool[]"` to `_knownTypeNames`

### 5c. Equality

Typed arrays follow the same **reference equality** rule as generic arrays and dictionaries. Two typed arrays with the same contents are NOT equal unless they are the same object:

```stash
let a: int[] = [1, 2, 3];
let b: int[] = [1, 2, 3];
a == b;    // false (reference equality)
a == a;    // true
```

### 5d. Truthiness

Same rule as generic arrays: typed arrays are **always truthy** (even empty ones `int[]` evaluates truthy, same as `[]`).

> **Open question:** Should empty arrays be falsy? Current behavior is that `[]` is truthy (it's a non-null object). Changing this would be a broader discussion outside this spec's scope. Typed arrays match whatever generic arrays do.

---

## 6. Element Type Rules

### 6a. Validation Matrix

| Typed Array | Accepted Values                                                   | Rejected                           |
| ----------- | ----------------------------------------------------------------- | ---------------------------------- |
| `int[]`     | `StashValue.IsInt` (long)                                         | float, string, bool, null, objects |
| `float[]`   | `StashValue.IsFloat` (double), `StashValue.IsInt` (auto-promoted) | string, bool, null, objects        |
| `string[]`  | `string` objects                                                  | int, float, bool, null             |
| `bool[]`    | `StashValue.IsBool`                                               | int, float, string, null, objects  |

### 6b. Int-to-Float Promotion

`float[]` accepts integers with automatic promotion to float. This is the **only** implicit conversion:

```stash
let ratios: float[] = [1, 2.5, 3];   // OK — 1 and 3 promoted to 1.0 and 3.0
arr.push(ratios, 42);                 // OK — 42 promoted to 42.0
```

Rationale: `int → float` is a safe widening conversion with no information loss (for values that fit in a double). This matches mathematical intuition — integers are a subset of reals. All other conversions are rejected because they lose information or change semantics.

### 6c. Null Handling

**Null is NOT allowed in typed arrays.** Typed arrays store primitives. Null indicates absence and has no meaningful place in a collection of `int` or `string` values.

```stash
let scores: int[] = [1, null, 3];   // RuntimeError: cannot add null to int[]
arr.push(names, null);              // RuntimeError: cannot add null to string[]
```

> **Decision rationale:** If users need nullable elements, they should use a generic `array`. Typed arrays are for cases where you KNOW the element type. Allowing null defeats the purpose.

> **Alternative considered:** `int?[]` syntax for nullable typed arrays. Rejected — adds complexity with little value. Use generic `array` for mixed/nullable collections.

### 6d. Error Messages

Error messages must state what was expected, what was received, and where:

```
RuntimeError: cannot add string "hello" to int[] — expected int
RuntimeError: cannot assign float 3.14 to element of int[] at index 2 — expected int
RuntimeError: cannot create int[] — element at index 4 is string "bad"
```

---

## 7. `arr.*` Namespace Compatibility

### 7a. The `SvArgs` Integration Strategy

Currently, all `arr.*` functions extract arrays via `SvArgs.StashList()`, which returns `List<StashValue>`. With native backing arrays, there is no `List<StashValue>` to extract from a typed array. The strategy is **dual dispatch**:

**`SvArgs.StashList()`** — unchanged behavior. Continues to extract `List<StashValue>` from generic arrays. If passed a `StashTypedArray`, throws (does NOT silently materialize a list).

**`SvArgs.TypedArray()`** — new method. Extracts `StashTypedArray` from a `StashValue`. Throws if not a typed array.

**`SvArgs.AnyArray()`** — new method. Returns `object` that is either `List<StashValue>` or `StashTypedArray`. Each `arr.*` function then dispatches:

```csharp
object arr = SvArgs.AnyArray(args, 0, "arr.len");
int len = arr switch
{
    List<StashValue> list => list.Count,
    StashTypedArray ta => ta.Count,
    _ => throw ...
};
```

This is mechanical — each read-only function gets a type-switch on `Count`/`Get(i)`. Mutation functions dispatch to `StashTypedArray.Add()`/`Set()`/etc. which enforce validation internally.

> **Why not materialize a `List<StashValue>` from typed arrays?** Two reasons: (1) O(n) allocation + copy on every read operation is expensive for large arrays; (2) returning a mutable list would let callers bypass type validation — the exact safety hole native backing arrays are designed to prevent.

### 7b. Function Categorization

**Read-only functions — work unchanged via `SvArgs.StashList()`:**

- `arr.len`, `arr.isEmpty`, `arr.contains`, `arr.indexOf`, `arr.lastIndexOf`
- `arr.find`, `arr.findIndex`, `arr.findLast`, `arr.findLastIndex`
- `arr.every`, `arr.some`, `arr.count`
- `arr.reduce`, `arr.reduceRight`
- `arr.forEach`
- `arr.join`
- `arr.first`, `arr.last`

**Type-preserving functions — return typed array if input is typed:**

- `arr.filter` → same element type, new typed array
- `arr.sort` → same elements reordered, new typed array
- `arr.reverse` → same elements reversed, new typed array
- `arr.slice` → subset, new typed array
- `arr.unique` → subset, new typed array
- `arr.flat` — N/A for typed arrays (single-level primitive elements)
- `arr.concat` → if both same typed, returns typed; otherwise generic

**Type-transforming functions — always return generic array:**

- `arr.map` → closure return type unknown at compile time, returns generic `array`
- `arr.flatMap` → same as map

**Mutation functions — must validate via `StashTypedArray`:**

- `arr.push(typed_arr, value)` → validates value against element type
- `arr.unshift(typed_arr, value)` → validates value
- `arr.splice(typed_arr, start, deleteCount, ...items)` → validates inserted items
- `arr.fill(typed_arr, value, start?, end?)` → validates value
- `arr.set(typed_arr, index, value)` → validates value (if this exists as a function vs indexing)

**Mutation functions — no validation needed (removing/reordering):**

- `arr.pop`, `arr.shift` → removes element, no type concern
- `arr.sortInPlace`, `arr.reverseInPlace` → reorders existing elements
- `arr.clear` → empties the array

### 7c. `arr.typed()` — New Construction Function

```stash
arr.typed(source: array, elementType: string) -> typed_array
```

Validates every element in `source` against `elementType`. Throws if any element fails. Returns a new `StashTypedArray`.

```stash
let nums = arr.typed([1, 2, 3], "int");        // int[]
let names = arr.typed(getNames(), "string");    // string[]
```

### 7d. `arr.untyped()` — Conversion Function

```stash
arr.untyped(source: typed_array) -> array
```

Returns a new generic `List<StashValue>` with the same elements. The elements are copied (shallow copy, same as the existing convention).

```stash
let generic = arr.untyped(int_scores);   // generic array
arr.push(generic, "now I can add anything");
```

### 7e. `arr.elementType()` — Introspection Function

```stash
arr.elementType(source: array) -> string | null
```

Returns the element type name for typed arrays (`"int"`, `"float"`, `"string"`, `"bool"`), or `null` for generic arrays.

```stash
arr.elementType(int_scores)     // "int"
arr.elementType([1, 2, 3])      // null
```

---

## 8. For-in Iteration

Typed arrays must work with for-in loops identically to generic arrays:

```stash
let scores: int[] = [95, 87, 100];

// Single variable — receives each element
for (let score in scores) {
    io.println(score);
}

// Two variables — index + element
for (let i, score in scores) {
    io.println(str.from(i) + ": " + str.from(score));
}
```

### Implementation

The `IterPrep` opcode currently checks `value is List<StashValue>` to enter list iteration mode. This must be extended to also match `StashTypedArray`. When iterating a typed array, `IterPrep` stores the `StashTypedArray` reference directly in `IteratorState`. `IterLoop` advances `.Index` and calls `ta.Get(index)` to retrieve each element as a `StashValue`.

The `IteratorState` class gains a new field: `StashTypedArray? TypedArray`. The iteration loop checks this field and dispatches accordingly:

```
if (iter.TypedArray != null)
    element = iter.TypedArray.Get(iter.Index);
else
    element = iter.Collection[iter.Index];  // existing List<StashValue> path
```

Performance is equivalent — `StashIntArray.Get(i)` is `StashValue.FromInt(_data[i])`, which is inlined.

---

## 9. Spread and Destructuring

### 9a. Spread

Spread works transparently — a typed array spreads its elements:

```stash
let a: int[] = [1, 2, 3];
let b: int[] = [4, 5, 6];

// Spread into generic array — always works
let combined = [...a, ...b, 7, 8];       // generic array [1,2,3,4,5,6,7,8]

// Spread into typed array — validated at construction
let combined: int[] = [...a, ...b];       // int[] — all elements validated
let mixed: int[] = [...a, "bad"];         // RuntimeError
```

Implementation: In `ExecuteNewArray`, when spread encounters a `StashTypedArray` source, iterate via `ta.Count`/`ta.Get(i)` and add each element to the result `List<StashValue>`. If the destination is a typed variable, validation happens at the assignment boundary (the `TypedWrap` opcode validates and copies into the native backing array).

### 9b. Destructuring

Array destructuring works unchanged — elements are extracted by index:

```stash
let scores: int[] = [95, 87, 100, 64];

let [first, second, ...rest] = scores;
// first = 95 (StashValue int)
// second = 87 (StashValue int)
// rest = [100, 64] (generic array — rest collection is always generic)
```

> **Decision:** `...rest` in destructuring produces a **generic array**, not a typed array. Rationale: destructuring is about extracting values, not preserving container semantics. The elements themselves carry their types. If the user needs a typed rest, they can: `let rest_typed: int[] = rest;`

---

## 10. Stringification

### 10a. Display Format

Typed arrays display with a type prefix to distinguish them from generic arrays:

```stash
io.println(scores);          // int[95, 87, 100, 64]
io.println(names);           // string["alice", "bob"]
io.println(flags);           // bool[true, false, true]
io.println([1, 2, 3]);      // [1, 2, 3]  (generic — no prefix)
```

### 10b. `json.stringify` Behavior

When serialized to JSON, typed arrays produce standard JSON arrays (no type prefix):

```stash
json.stringify(scores);      // "[95,87,100,64]"
json.stringify(names);       // '["alice","bob"]'
```

JSON is a transport format — it has no typed array concept. The type information is lost on serialization, which is consistent with how struct types are also lost in JSON.

---

## 11. Parser Changes

### 11a. Type Hint Parsing

In `Parser.cs`, every location that parses a type hint (variable declarations, function parameters, return types, struct fields, for-in variables) currently does:

```csharp
typeHint = Consume(TokenType.Identifier, "Expected type name after ':'.");
```

This becomes:

```csharp
Token typeName = Consume(TokenType.Identifier, "Expected type name after ':'.");
if (Match(TokenType.LeftBracket))
{
    Consume(TokenType.RightBracket, "Expected ']' after '[' in typed array type.");
    // typeName + isArray flag
}
```

### 11b. Type Hint Representation

Currently, type hints are stored as `Token?`. This is insufficient for `T[]` because a single token can't represent a parameterized type.

**Option A: Compound Token** — Store type hint as `(Token name, bool isArray)` tuple or a small record.

**Option B: Synthetic Token** — Create a synthetic token with lexeme `"int[]"` combining the type name and brackets.

**Option C: TypeHint record** — New mini-AST node:

```
TypeHint
├── Name: Token        — "int", "float", "string", "bool", etc.
├── IsArray: bool      — true if T[] was parsed
└── Span: SourceSpan   — covers entire type annotation
```

**Recommendation: Option C** — it's explicit, extensible (Phase 2 could add `IsNullable` etc.), and doesn't hack the token semantics.

### 11c. Affected AST Nodes

These nodes currently have `Token? TypeHint` fields that must change to the new type representation:

| AST Node         | Field                          | Usage                          |
| ---------------- | ------------------------------ | ------------------------------ |
| `VarDeclStmt`    | `TypeHint`                     | `let x: int[] = ...`           |
| `FnDeclStmt`     | `ParameterTypes`, `ReturnType` | `fn foo(x: int[]) -> string[]` |
| `StructDeclStmt` | Field type hints               | `struct S { data: int[] }`     |
| `ForInStmt`      | `TypeHint`                     | `for (let x: int[] in ...)`    |

### 11d. No New Expressions

No new expression types are needed. Typed array creation happens at the **assignment boundary** (when a generic array literal is assigned to a typed variable) or via `arr.typed()` (a stdlib function call). The parser does not need a "typed array literal" expression.

---

## 12. Bytecode VM Changes

### 12a. Opcodes

**No new opcodes required for Phase 1.** The existing opcodes are sufficient:

- `NewArray` — continues to create `List<StashValue>`. If the destination variable has a typed array type hint, the compiler emits a `TypedWrap` (new opcode) after `NewArray` to validate and wrap.
- `GetTable` — reads from arrays. Updated to also handle `StashTypedArray` (via `Get(index)`).
- `SetTable` — writes to arrays. Updated to handle `StashTypedArray` (via `Set(index, value)`, which validates).
- `IterPrep`/`IterLoop` — iteration. `IterPrep` stores `StashTypedArray` reference in `IteratorState`; `IterLoop` calls `ta.Get(index)` per element.

**One new opcode:**

```
TypedWrap  A B    — R(A) = new StashTypedArray(elementType=K(B), elements=R(A))
```

Takes the `List<StashValue>` in register A, validates all elements against the type name in constant B, creates the appropriate subclass (`StashIntArray`, `StashFloatArray`, `StashStringArray`, or `StashBoolArray`), copies elements into native backing storage, and replaces R(A) with the typed array. This is emitted when the compiler knows a variable has a typed array annotation.

The subclass selection is a simple string switch on K(B):

```
"int"    → new StashIntArray(elements)
"float"  → new StashFloatArray(elements)
"string" → new StashStringArray(elements)
"bool"   → new StashBoolArray(elements)
```

Each constructor validates + unwraps every element from the source list into its native array. On validation failure, a `RuntimeError` is thrown with the index and type mismatch details.

### 12b. `GetTable` / `SetTable` Changes

`GetTable` currently handles three types: `List<StashValue>`, `StashDictionary`, `string`. Add `StashTypedArray`:

```
case StashTypedArray ta:
    // Integer index → ta.Get(index) (no validation needed for reads)
    // Negative indexing supported (same as List)
```

`SetTable` currently handles `List<StashValue>` and `StashDictionary`. Add `StashTypedArray`:

```
case StashTypedArray ta:
    // ta.Set(index, value) — validates element type, throws on mismatch
```

### 12c. `CheckIsType` Updates

Add typed array checks to the `CheckIsType` method:

```
"array" => value is List<StashValue> || value is StashTypedArray
"int[]" => value is StashTypedArray ta && ta.ElementTypeName == "int"
"float[]" => value is StashTypedArray ta && ta.ElementTypeName == "float"
"string[]" => value is StashTypedArray ta && ta.ElementTypeName == "string"
"bool[]" => value is StashTypedArray ta && ta.ElementTypeName == "bool"
```

### 12d. Constant Pool

The type name strings (`"int"`, `"float"`, `"string"`, `"bool"`) are stored in the constant pool and referenced by the `TypedWrap` opcode.

---

## 13. Static Analysis Changes

### 13a. Type Inference

The type inference engine in `TypeInferenceEngine.cs` currently returns `"array"` for `ArrayExpr`. This changes:

- If the array expression is the initializer of a variable with a typed array type hint, infer the typed array type: `"int[]"`, `"float[]"`, etc.
- If no type hint, continue inferring `"array"`.
- `arr.typed(source, "int")` infers `"int[]"` (return type from stdlib metadata).

### 13b. New Diagnostics

| Code      | Severity | Message                                                                                                   |
| --------- | -------- | --------------------------------------------------------------------------------------------------------- |
| `STA0301` | Error    | `Typed array element type mismatch: expected {expected}, found {actual}`                                  |
| `STA0302` | Warning  | `Implicit int-to-float promotion in float[] assignment`                                                   |
| `STA0303` | Error    | `Null is not allowed in typed arrays`                                                                     |
| `STA0304` | Warning  | `Variable declared as {type}[] but initialized with generic array that may contain incompatible elements` |

### 13c. Type Hint Validation

Analysis should warn when a typed array variable is assigned from a source whose element types can't be verified:

```stash
let data = getData();           // returns generic array
let nums: int[] = data;         // STA0304 warning — can't verify at analysis time
```

This is a warning, not an error — the assignment may succeed at runtime.

---

## 14. LSP / DAP / Playground / Extension Impact

### 14a. LSP

- **Completions**: After `let x:`, suggest `int[]`, `float[]`, `string[]`, `bool[]` alongside existing type names.
- **Hover**: Show `int[]` in hover tooltips for typed array variables.
- **Signature help**: Function parameters with typed array types show `nums: int[]`.
- **Semantic tokens**: `int` in `int[]` gets type-name highlighting (same as in `let x: int`). The `[]` gets punctuation highlighting.
- **Diagnostics**: Surface `STA0301`–`STA0304` from analysis.

### 14b. DAP

- **Variable display**: Typed arrays show as `int[3]` (type + count) in the debugger's Variables panel, similar to how .NET debuggers show `int[3]` for `int[]`.
- **Children**: Expanding a typed array shows indexed elements `[0] = 95`, `[1] = 87`, etc. (same as generic arrays).
- **Watch expressions**: `typeof(scores)` returns `"int[]"`, `scores is int[]` returns `true`.

### 14c. Playground

- **Monarch tokenizer**: No changes needed — `int`, `float`, `string`, `bool` are already keyword/type tokens, and `[]` is existing punctuation.
- **Examples**: Add a typed array example to the curated example list.

### 14d. VS Code Extension

- **TextMate grammar**: May need a pattern for `identifier[]` in type annotation positions to get consistent highlighting. Test with existing grammar first — it might work already since `[` and `]` have their own scope.

---

## 15. Implementation Phases

### Phase 1a: Runtime Type + `SvArgs` (foundation)

1. Create `StashTypedArray` abstract class in `Stash.Core/Runtime/Types/`
2. Create per-type subclasses: `StashIntArray`, `StashFloatArray`, `StashStringArray`, `StashBoolArray`
3. Add `SvArgs.TypedArray()` and `SvArgs.AnyArray()` extraction methods
4. Add `typeof` and `is` support
5. Update `RuntimeValues.Stringify()` for display
6. Add `arr.typed()`, `arr.untyped()`, `arr.elementType()`, `arr.new()` to `ArrBuiltIns.cs`

### Phase 1b: Parser + Type Hints

1. Create `TypeHint` record
2. Update type hint parsing in all positions
3. Update AST nodes: `VarDeclStmt`, `FnDeclStmt`, `StructDeclStmt`, `ForInStmt`
4. Update AST visitors across all projects

### Phase 1c: Bytecode VM

1. Add `TypedWrap` opcode
2. Update `GetTable` / `SetTable` for typed arrays
3. Update `IterPrep` / `IterLoop`
4. Update `CheckIsType`
5. Update `ExecuteNewArray` spread handling

### Phase 1d: `arr.*` Mutation Functions

1. Update `arr.push`, `arr.unshift`, `arr.splice`, `arr.fill` to validate via typed array
2. Update type-preserving functions (`arr.filter`, `arr.sort`, `arr.reverse`, `arr.slice`, `arr.unique`, `arr.concat`) to return typed arrays when input is typed

### Phase 1e: Static Analysis + Tooling

1. Update `TypeInferenceEngine` for typed array types
2. Add `STA0301`–`STA0304` diagnostics
3. Update LSP handlers (completions, hover, signature help)
4. Update DAP variable display
5. Update Playground examples

### Phase 1f: Documentation + Tests

1. Update language specification
2. Update stdlib reference
3. Create `examples/typed_arrays.stash`
4. Write xUnit tests for all scenarios in Section 16

---

## 16. Test Scenarios

### Happy Path

```
TypedArray_IntDeclaration_CreatesIntArray()
TypedArray_FloatDeclaration_CreatesFloatArray()
TypedArray_StringDeclaration_CreatesStringArray()
TypedArray_BoolDeclaration_CreatesBoolArray()
TypedArray_EmptyDeclaration_CreatesEmptyTypedArray()
TypedArray_PushValidElement_Succeeds()
TypedArray_IndexRead_ReturnsElement()
TypedArray_IndexWrite_ValidatesAndSets()
TypedArray_ForIn_IteratesElements()
TypedArray_ForInIndexed_IteratesWithIndex()
TypedArray_Spread_ExtractsElements()
TypedArray_SpreadIntoTyped_ValidatesElements()
TypedArray_Destructuring_ExtractsElements()
TypedArray_FloatAllowsIntPromotion_Succeeds()
TypedArray_FunctionParameter_AcceptsTypedArray()
TypedArray_FunctionReturn_ReturnsTypedArray()
TypedArray_StructField_StoresTypedArray()
TypedArray_ArrTyped_CreatesFromGeneric()
TypedArray_ArrUntyped_ConvertsToGeneric()
TypedArray_ArrElementType_ReturnsTypeName()
TypedArray_ArrFilter_PreservesType()
TypedArray_ArrSort_PreservesType()
TypedArray_ArrReverse_PreservesType()
TypedArray_ArrSlice_PreservesType()
TypedArray_ArrMap_ReturnsGenericArray()
TypedArray_ArrConcat_SameType_PreservesType()
TypedArray_ArrConcat_DifferentTypes_ReturnsGeneric()
TypedArray_JsonStringify_ProducesStandardJson()
```

### Edge Cases

```
TypedArray_EmptyArray_TypeofReturnsTypedName()
TypedArray_SingleElement_Works()
TypedArray_LargeArray_PerformanceAcceptable()
TypedArray_IntToFloatPromotion_OnDeclaration()
TypedArray_IntToFloatPromotion_OnPush()
TypedArray_NegativeIndex_Works()
TypedArray_NestedInGenericArray_Works()
TypedArray_PassedToUntypedParameter_Works()
TypedArray_SpreadInFunctionCall_Works()
TypedArray_RestDestructuring_ProducesGenericArray()
TypedArray_ConcatTypedAndGeneric_ReturnsGeneric()
TypedArray_ArrLen_Works()
TypedArray_ArrIsEmpty_EmptyReturnsTrue()
TypedArray_ArrContains_FindsElement()
TypedArray_ArrReduce_Works()
TypedArray_ArrEvery_Works()
TypedArray_ArrSome_Works()
```

### Error Cases

```
TypedArray_PushWrongType_Throws()
TypedArray_IndexWriteWrongType_Throws()
TypedArray_NullElement_Throws()
TypedArray_DeclarationMixedTypes_Throws()
TypedArray_SpliceInsertWrongType_Throws()
TypedArray_FillWrongType_Throws()
TypedArray_ArrTypedInvalidElements_Throws()
TypedArray_FloatIntoIntArray_Throws()
TypedArray_BoolIntoIntArray_Throws()
TypedArray_StringIntoIntArray_Throws()
TypedArray_IntIntoStringArray_Throws()
TypedArray_InvalidElementTypeName_Throws()
```

### Type System

```
TypedArray_TypeofReturnsCorrectString()
TypedArray_IsOwnType_ReturnsTrue()
TypedArray_IsArray_ReturnsTrue()
TypedArray_IsWrongTypedArray_ReturnsFalse()
TypedArray_GenericIsTypedArray_ReturnsFalse()
TypedArray_EqualityIsReference()
TypedArray_IsTruthy()
```

---

## 17. Decision Log

| #   | Decision                                         | Alternatives Considered                                                  | Rationale                                                                                                                                                                                                                                                                                               | Risk                                                                                                                                                                                                                  |
| --- | ------------------------------------------------ | ------------------------------------------------------------------------ | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| D1  | `T[]` syntax (not `Array<T>`)                    | `Array<T>`, `T array`, `[T]`                                             | Matches C#/Java/TypeScript familiarity. No generics needed — fixed set of element types. `Array<T>` implies generics which Stash doesn't have.                                                                                                                                                          | If generics are ever added, there's a syntax overlap to manage. Low risk — `T[]` and `Array<T>` coexist fine in C#.                                                                                                   |
| D2  | Typed arrays ARE arrays (`is array` = true)      | Separate type hierarchy                                                  | If `int[] is array` were false, every function accepting arrays would need updating. Subtype relationship is natural and avoids ecosystem breakage.                                                                                                                                                     | `arr.*` mutation functions on typed arrays can accidentally bypass validation if not updated. Mitigated by explicit mutation function updates.                                                                        |
| D3  | No auto-inference from literals                  | Auto-infer `[1,2,3]` as `int[]`                                          | Auto-inference would break existing code: `let x = [1, "2", 3]` suddenly fails. Stash is dynamically typed — forcing inference contradicts that. Explicit is better.                                                                                                                                    | Users must always annotate to get typed arrays. Minor verbosity cost.                                                                                                                                                 |
| D4  | Null not allowed in typed arrays                 | Allow null, add `T?[]` syntax                                            | Typed arrays represent sequences of known-type values. Null is not an int, float, string, or bool. Allowing null weakens guarantees. Use generic `array` for nullable collections.                                                                                                                      | Users accustomed to nullable collections may find this restrictive. They can use generic arrays.                                                                                                                      |
| D5  | Int→float promotion only                         | No promotions (strict), or also float→int rounding                       | `int → float` is safe widening. Every integer is a valid float. `float → int` loses information. All other conversions change semantics. One promotion rule is easy to remember.                                                                                                                        | Edge case: Very large integers (>2^53) lose precision when promoted to double. Acceptable for a scripting language.                                                                                                   |
| D6  | Per-type subclasses with native C# backing       | Concrete class with type switch, interface, generic `StashTypedArray<T>` | Per-type subclasses (`StashIntArray`/`StashFloatArray`/etc.) use native C# arrays for 3-24x memory savings, eliminate raw-list leaks, are fully AOT-compatible, and make Phase 2's `StashByteArray` the same pattern rather than a special case.                                                        | 4 subclasses to maintain instead of 1. Each `arr.*` function needs dual dispatch. Mechanical but tedious. Virtual dispatch overhead is negligible.                                                                    |
| D7  | `TypeHint` record (not compound token)           | Synthetic token with `"int[]"` lexeme, tuple                             | A proper record is extensible (add `IsNullable`, `IsGeneric` later), explicit, and doesn't hack token semantics. Tuples are unnamed and error-prone in a large codebase.                                                                                                                                | Requires updating all AST nodes that store `Token? TypeHint`. Manageable — 4 nodes.                                                                                                                                   |
| D8  | Rest destructuring produces generic array        | Preserve typed array                                                     | Destructuring extracts values — it's about pulling apart, not preserving containers. The elements themselves carry type info. Adding typed-array-preservation to destructuring is complex for marginal benefit.                                                                                         | Users who destructure typed arrays and want typed rest must re-wrap. Minor inconvenience.                                                                                                                             |
| D9  | `arr.map` returns generic array                  | Infer from closure return type                                           | The closure's return type is unknown at compile time in a dynamically typed language. Attempting to infer it at runtime (by inspecting the first return value) is fragile and surprising. Explicit is better.                                                                                           | Users must re-wrap `arr.map` results if they want typed output. `arr.typed(arr.map(...), "string")` works.                                                                                                            |
| D10 | Phase 1: primitives only (int/float/string/bool) | All types including struct/enum                                          | Primitives have simple validation rules (tag checks). Struct/enum typed arrays need subtype checking, interface conformance, and more complex validation. Scope control.                                                                                                                                | User-defined typed arrays deferred. If demand is high, Phase 3 addresses it.                                                                                                                                          |
| D11 | Native C# backing arrays per subclass            | `List<StashValue>` with validation wrapper, generic `StashTypedArray<T>` | Three benefits: (1) 3-24x memory savings; (2) no raw `List<StashValue>` can leak to bypass validation — safety is structural; (3) Phase 2 `StashByteArray` is the same pattern, not a special case. Per-type subclasses (`StashIntArray`, etc.) avoid AOT issues with generic virtual methods.          | More code — 4 subclasses instead of 1. Each `arr.*` function needs dual dispatch (`List<StashValue>` vs `StashTypedArray`). Mechanical but tedious. Virtual dispatch overhead is negligible for a scripting language. |
| D12 | No fixed-size arrays in type syntax              | `int[10]` syntax, `arr.fixed()`                                          | Putting sizes in type annotations requires expression parsing in type positions (for `int[n]`), creates two behavioral modes (growable vs fixed) behind the same `arr.*` API, and serves a narrow use case in sysadmin scripting. Pre-allocation via `arr.new("int", 100)` covers the capacity concern. | If protocol work demands true fixed-size buffers later, a separate `arr.fixed()` can be added without syntax changes.                                                                                                 |

---

## 18. Blast Radius Summary

| Component                           | Files Affected              | Scope                                                                                                                                        |
| ----------------------------------- | --------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------- |
| **Parser** (`Stash.Core/Parsing/`)  | ~5 files                    | Type hint parsing, AST nodes                                                                                                                 |
| **Runtime** (`Stash.Core/Runtime/`) | 6 new files + 2 modified    | `StashTypedArray` (abstract), `StashIntArray`, `StashFloatArray`, `StashStringArray`, `StashBoolArray`, `RuntimeValues` stringify            |
| **Bytecode VM** (`Stash.Bytecode/`) | 4-5 files                   | New opcode, GetTable/SetTable, IterPrep, TypeOps, Compiler                                                                                   |
| **Stdlib** (`Stash.Stdlib/`)        | 3 files                     | `ArrBuiltIns.cs` (major), `SvArgs.cs` (new methods), `GlobalBuiltIns.cs` (typeof)                                                            |
| **Analysis** (`Stash.Analysis/`)    | 2-3 files                   | Type inference, new diagnostics                                                                                                              |
| **LSP** (`Stash.Lsp/`)              | 3-4 files                   | Completions, hover, signature help                                                                                                           |
| **DAP** (`Stash.Dap/`)              | 1 file                      | Variable display                                                                                                                             |
| **Tests** (`Stash.Tests/`)          | 1-2 new files               | ~60-80 new tests                                                                                                                             |
| **Docs**                            | 2 files                     | Language spec, stdlib reference                                                                                                              |
| **Examples**                        | 1 new file                  | `typed_arrays.stash`                                                                                                                         |
| **Playground**                      | 0-1 files                   | Example update                                                                                                                               |
| **Extension**                       | 0-1 files                   | TextMate grammar check                                                                                                                       |
| **Total `List<StashValue>` sites**  | ~38 files × 176 occurrences | `SvArgs.StashList()` unchanged; new `SvArgs.AnyArray()` handles typed arrays. Each `arr.*` function updated individually with dual dispatch. |

---

## 19. Open Questions

1. ~~**Should `const x: int[] = [1,2,3]` make the array immutable (no push/pop)?**~~ **Resolved: No.** Stash's `const` matches JavaScript/TypeScript semantics — it freezes the binding, not the value. This is the correct choice for a dynamically typed scripting language. C#/Go `const` requires compile-time constant expressions, which requires an entire constant-expression evaluator that Stash doesn't need. Typed arrays follow the same rule: `const scores: int[] = [95, 87]` prevents `scores = other_array` but allows `arr.push(scores, 100)`. Deep immutability, if ever needed, would be a separate mechanism (e.g., `freeze()`).

2. ~~**Should `arr.from()` or `arr.of()` be the primary construction function?**~~ **Resolved: `arr.typed()` is kept as the programmatic path.** The primary construction path is type-annotated declarations (`let x: int[] = [1,2,3]`). `arr.typed()` exists for cases where the element type is determined at runtime or where typed arrays are needed in expression context (function arguments, etc.). The name `arr.typed()` is explicit and unambiguous.

3. **Should typed arrays support UFCS?** E.g., `scores.push(42)` — this already works for generic arrays via UFCS. If `SvArgs` dispatching is updated, UFCS should work transparently. Verify during implementation.

4. **Serialization (.stashc format):** Typed arrays in constant pools need encoding. The type name string + element data need to be serialized. This affects `ChunkBuilder.cs` and the serializer. Verify format compatibility.

5. **Short-form typed array — `let x = int[]`:** Should this create an empty `int[]`? Currently `int[]` in expression position would be ambiguous: is it a type or an expression? Recommendation: Not supported — use `arr.typed([], "int")` or `let x: int[] = []`.
