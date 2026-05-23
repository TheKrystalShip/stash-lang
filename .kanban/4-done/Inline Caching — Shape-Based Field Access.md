# Inline Caching for Field Access — Shape-Based Optimization

**Status:** Backlog — Design Spec
**Created:** 2026-04-05
**Parent:** Bytecode VM — Implementation Roadmap (Post-Phase 8 Optimization #2)
**Depends on:** None (can be implemented independently of StashValue, though combines well with it)
**Purpose:** Eliminate repeated dictionary lookups for struct field access by caching the field offset at each `OP_GET_FIELD` / `OP_SET_FIELD` call site. Exploit the fact that Stash structs have fixed shapes (no dynamic field addition) to make field access O(1) array-indexed instead of O(1)-amortized hash lookup.
**Expected Impact:** 10–20% improvement on struct-heavy benchmarks (Algorithms, especially).

---

## Table of Contents

1. [Motivation](#1-motivation)
2. [Current Architecture — Field Access Cost](#2-current-architecture--field-access-cost)
3. [Design — Hidden Shapes and Inline Caches](#3-design--hidden-shapes-and-inline-caches)
4. [Shape System](#4-shape-system)
5. [Inline Cache Mechanics](#5-inline-cache-mechanics)
6. [StashInstance Migration — Array-Backed Fields](#6-stashinstance-migration--array-backed-fields)
7. [StashDictionary — No Shape (Polymorphic)](#7-stashdictionary--no-shape-polymorphic)
8. [VM Integration](#8-vm-integration)
9. [Compiler Integration](#9-compiler-integration)
10. [Extension Methods and UFCS Interaction](#10-extension-methods-and-ufcs-interaction)
11. [Cross-Cutting Concerns](#11-cross-cutting-concerns)
12. [Migration Strategy](#12-migration-strategy)
13. [Test Strategy](#13-test-strategy)
14. [Risk Register](#14-risk-register)
15. [Decision Log](#15-decision-log)

---

## 1. Motivation

The Algorithms benchmark constructs struct instances, accesses fields in tight loops, and passes structs to functions. Every `obj.field` access currently performs:

1. **Type check cascade** — `GetFieldValue` checks 10 types sequentially (StashInstance, StashDictionary, StashNamespace, StashStruct, StashEnum, StashEnumValue, StashError, built-in .length, extension methods, UFCS)
2. **Dictionary lookup** — `StashInstance.GetField(name)` does `_fields.TryGetValue(name, ...)` — a string hash + equality check
3. **Method fallback** — if field not found, tries `Struct.Methods.TryGetValue(name, ...)`

For a hot loop accessing `point.x`, this entire chain runs on every iteration — even though the shape of the object never changes. V8, Ruby's YARV, and CPython all solve this with **inline caches**: after the first access, subsequent accesses at the same call site hit a cached offset and skip the lookup entirely.

### Key insight: Stash structs have fixed shapes

```csharp
// StashInstance.SetField — REJECTS unknown fields
if (!_fields.ContainsKey(name))
    throw new RuntimeError($"Undefined field '{name}' on struct '{TypeName}'.", span);
```

Stash does not allow dynamic field addition to struct instances. Every instance of `Point` has exactly the same fields, in the same order, every time. This is the ideal scenario for shape-based inline caching — **shapes never transition**.

### Expected improvement

| Benchmark             | Current (VM) | Field access weight                 | Estimated improvement |
| --------------------- | -----------: | ----------------------------------- | --------------------: |
| Algorithms            |       539 ms | High (struct fields in sort/search) |                15–20% |
| Function Calls        |       246 ms | Low                                 |                  0–5% |
| Expression Throughput |       659 ms | None                                |                    0% |
| Built-in Functions    |       778 ms | None                                |                    0% |
| Scope Lookup          |       261 ms | None                                |                    0% |

The improvement is concentrated on struct-intensive code. Real-world Stash scripts (system administration, config management) use structs heavily, so the practical impact extends beyond the Algorithms benchmark.

---

## 2. Current Architecture — Field Access Cost

### 2.1 GetFieldValue — The 10-Step Cascade

```csharp
// VirtualMachine.cs:1849-1948
private object? GetFieldValue(object? obj, string name, SourceSpan? span)
{
    if (obj is StashInstance instance)           // Step 1: struct field
    {
        object? result = instance.GetField(name, span);
        if (result is StashBoundMethod bound && bound.Method is VMFunction vmFunc)
            return new VMBoundMethod(bound.Instance, vmFunc);
        return result;
    }

    if (obj is StashDictionary dict)             // Step 2: dict key
    { ... }
    if (obj is StashNamespace ns)                // Step 3: namespace member
    { ... }
    if (obj is StashStruct structDef)            // Step 4: static method
    { ... }
    if (obj is StashEnum enumDef)                // Step 5: enum member
    { ... }
    if (obj is StashEnumValue enumValue)         // Step 6: enum value prop
    { ... }
    if (obj is StashError error)                 // Step 7: error prop
    { ... }
    if (obj is List<object?> list && name == "length")  // Step 8: .length
    { ... }
    if (obj is string s && name == "length")     // Step 8b: string.length
    { ... }
    // Step 9: extension methods
    // Step 10: UFCS namespace lookup
}
```

**Cost per call:** Even when the first branch hits (the common case for structs), the runtime still does:

- One `is StashInstance` type check (fast)
- One `Dictionary<string, object?>.TryGetValue(name, ...)` — hashes the string, probes the bucket, compares the key
- String hashing is O(n) in string length (though short field names are typical)

### 2.2 StashInstance.GetField

```csharp
// StashInstance.cs:40-52
public object? GetField(string name, SourceSpan? span)
{
    if (_fields.TryGetValue(name, out object? value))
        return value;
    if (Struct != null && Struct.Methods.TryGetValue(name, out IStashCallable? method))
        return new StashBoundMethod(this, method);
    throw new RuntimeError($"Undefined field '{name}' on struct '{TypeName}'.", span);
}
```

### 2.3 OP_GET_FIELD Current Handler

```csharp
case OpCode.GetField:
{
    ushort nameIdx = ReadU16(ref frame);
    string fieldName = (string)frame.Chunk.Constants[nameIdx]!;  // Constant pool lookup
    SourceSpan? span = GetCurrentSpan(ref frame);
    object? obj = Pop();
    Push(GetFieldValue(obj, fieldName, span));  // Full dispatch chain
    break;
}
```

**Every field access:** constant pool string lookup → full type dispatch → dictionary hash lookup. For `point.x` in a million-iteration loop, that's a million string hashes for the constant "x" alone (though .NET caches string hash codes after first computation).

---

## 3. Design — Hidden Shapes and Inline Caches

### 3.1 Core Concept

A **shape** (also called "hidden class" in V8, "object shape" in SpiderMonkey) describes the layout of an object's fields: which field names exist and at which index offset each is stored.

An **inline cache** is per-call-site memoization: each `OP_GET_FIELD` instruction remembers the shape it last saw and the field offset it resolved. On the next execution, if the shape matches, it skips the lookup.

### 3.2 Why This Works for Stash

1. **Structs are sealed** — no dynamic field addition (`SetField` rejects unknown fields)
2. **Shapes never transition** — an instance of `Point { x, y }` always has shape `{x: 0, y: 1}`
3. **One shape per struct type** — all instances of `Point` share the same shape
4. **Monomorphic sites dominate** — `point.x` in a loop always sees the same shape

This means inline caches will have a **100% hit rate** for struct field access after warmup — the ideal case. No megamorphic fallbacks, no shape transitions, no invalidation.

### 3.3 Vocabulary

| Term                  | Definition                                                                              |
| --------------------- | --------------------------------------------------------------------------------------- |
| **Shape**             | A mapping from field names to integer offsets, shared by all instances of a struct type |
| **Inline cache (IC)** | A per-call-site cache storing `(shape, offset)` for fast field access                   |
| **Monomorphic**       | An IC that has seen exactly one shape (ideal case, 100% in Stash)                       |
| **Polymorphic**       | An IC that has seen 2–4 shapes (not expected for struct fields in Stash)                |
| **Megamorphic**       | An IC that has seen >4 shapes (falls back to dictionary lookup)                         |
| **Call site**         | A specific bytecode offset where `OP_GET_FIELD` / `OP_SET_FIELD` appears                |

---

## 4. Shape System

### 4.1 Shape Class

```csharp
/// <summary>
/// Describes the field layout of a struct type. All instances of the same struct
/// share a single Shape instance. Shapes are immutable and never transition.
/// </summary>
internal sealed class Shape
{
    /// <summary>The struct type this shape belongs to.</summary>
    public StashStruct StructDef { get; }

    /// <summary>Maps field name → array index for O(1) field access.</summary>
    private readonly Dictionary<string, int> _fieldIndex;

    /// <summary>Total number of fields.</summary>
    public int FieldCount { get; }

    public Shape(StashStruct structDef)
    {
        StructDef = structDef;
        FieldCount = structDef.Fields.Count;
        _fieldIndex = new Dictionary<string, int>(FieldCount);
        for (int i = 0; i < FieldCount; i++)
            _fieldIndex[structDef.Fields[i]] = i;
    }

    /// <summary>Returns the field index for the given name, or -1 if not found.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int GetFieldIndex(string name)
    {
        return _fieldIndex.TryGetValue(name, out int index) ? index : -1;
    }
}
```

### 4.2 Shape Creation and Caching

Shapes are created once per struct type definition (at `OP_STRUCT_DECL` time) and cached on the `StashStruct`:

```csharp
// Added to StashStruct
public Shape? Shape { get; internal set; }
```

The VM creates the shape when it executes `OP_STRUCT_DECL`:

```csharp
case OpCode.StructDecl:
{
    // ... existing struct definition code ...
    StashStruct ss = /* newly created struct */;
    ss.Shape = new Shape(ss);  // Create shape once
    // ...
}
```

Since struct definitions are executed once (at load time), shape creation is a one-time cost.

### 4.3 No Shape Transitions

Unlike JavaScript (where objects can grow new properties), Stash structs are fixed. A `Shape` is **immutable** — it never transitions to a new shape. This eliminates the most complex part of inline caching in other VMs (transition chains, invalidation, recompilation).

---

## 5. Inline Cache Mechanics

### 5.1 IC Storage

Each `OP_GET_FIELD` and `OP_SET_FIELD` instruction needs to store its cached `(Shape, fieldIndex)`. There are two storage options:

**Option A: Chunk-level IC array**

```csharp
public class Chunk
{
    // ... existing fields ...
    public InlineCache[]? InlineCaches { get; set; }
}

internal struct InlineCache
{
    public Shape? CachedShape;
    public int CachedIndex;  // -1 = not cached
}
```

Each `OP_GET_FIELD` / `OP_SET_FIELD` instruction carries an additional u16 operand: the IC slot index. The compiler allocates IC slots sequentially as it encounters field access expressions.

**Option B: Instruction-stream embedded IC**

Reserve bytes in the instruction stream after `OP_GET_FIELD` for the cached shape pointer and offset. This is simpler but makes the bytecode mutable at runtime and doesn't work well with .NET's GC (can't embed managed references in a byte array).

**Decision: Option A — Chunk-level IC array.** It's GC-safe, the IC array is allocated once per chunk, and lookup is a simple array index.

### 5.2 Modified Opcode Encoding

```
OP_GET_FIELD_IC  <u16: name_constant_index>  <u16: ic_slot_index>
```

This is a new opcode (or an extended encoding of `OP_GET_FIELD`) that carries both the field name from the constant pool and the IC slot index. Total: 5 bytes (1 opcode + 2 name + 2 IC slot).

**Alternative:** Reuse the existing `OP_GET_FIELD` opcode and add the IC slot as an additional operand. This changes the instruction from 3 bytes to 5 bytes. Since the opcode switch dispatches on the first byte, this doesn't affect other instructions.

**Decision:** Add new opcodes `GetFieldIC` and `SetFieldIC` rather than changing existing opcodes. This preserves backward compatibility and allows the non-IC variants for cases where caching isn't beneficial (e.g., namespace member access, which is rare).

### 5.3 IC Lookup Flow

```csharp
case OpCode.GetFieldIC:
{
    ushort nameIdx = ReadU16(ref frame);
    ushort icSlot = ReadU16(ref frame);
    object? obj = Pop();

    ref InlineCache ic = ref frame.Chunk.InlineCaches![icSlot];

    // Fast path: monomorphic cache hit
    if (obj is StashInstance instance && instance.Shape == ic.CachedShape)
    {
        Push(instance.Fields[ic.CachedIndex]);   // O(1) array access!
        break;
    }

    // Cache miss: full lookup + cache update
    string fieldName = (string)frame.Chunk.Constants[nameIdx]!;
    SourceSpan? span = GetCurrentSpan(ref frame);
    object? result = GetFieldValue(obj, fieldName, span);

    // Update IC if the object is a struct instance with a shape
    if (obj is StashInstance inst && inst.Shape is not null)
    {
        int fieldIdx = inst.Shape.GetFieldIndex(fieldName);
        if (fieldIdx >= 0)
        {
            ic.CachedShape = inst.Shape;
            ic.CachedIndex = fieldIdx;
        }
    }

    Push(result);
    break;
}
```

### 5.4 IC for SetField

```csharp
case OpCode.SetFieldIC:
{
    ushort nameIdx = ReadU16(ref frame);
    ushort icSlot = ReadU16(ref frame);
    object? value = Pop();
    object? obj = Pop();

    ref InlineCache ic = ref frame.Chunk.InlineCaches![icSlot];

    // Fast path: monomorphic cache hit
    if (obj is StashInstance instance && instance.Shape == ic.CachedShape)
    {
        instance.Fields[ic.CachedIndex] = value;   // O(1) array write!
        Push(value);
        break;
    }

    // Cache miss: full set + cache update
    string fieldName = (string)frame.Chunk.Constants[nameIdx]!;
    SourceSpan? span = GetCurrentSpan(ref frame);
    SetFieldValue(obj, fieldName, value, span);

    if (obj is StashInstance inst && inst.Shape is not null)
    {
        int fieldIdx = inst.Shape.GetFieldIndex(fieldName);
        if (fieldIdx >= 0)
        {
            ic.CachedShape = inst.Shape;
            ic.CachedIndex = fieldIdx;
        }
    }

    Push(value);
    break;
}
```

### 5.5 Cache Hit Rate Analysis

| Site Pattern                         | Shape Stability             |       Expected Hit Rate |
| ------------------------------------ | --------------------------- | ----------------------: |
| `point.x` in a loop                  | Same struct every iteration |       100% after warmup |
| `items[i].name` in a for-each        | Same struct type in array   |                    100% |
| `config.timeout`                     | Single access               | 100% (after first call) |
| `dict.key`                           | No shape (StashDictionary)  |        0% — IC not used |
| `ns.func`                            | No shape (StashNamespace)   |        0% — IC not used |
| `if (val is Point) val.x else val.y` | Two struct types            |   Polymorphic — see 5.6 |

### 5.6 Polymorphic Sites

For call sites that see multiple struct types (e.g., a function that accepts different struct types), the monomorphic IC misses on every type change. Two strategies:

**Option A: Single-entry IC (monomorphic only).** On a miss, update the cache — it becomes "last-seen" caching. If types alternate, the hit rate drops to ~50% or 0%.

**Option B: Polymorphic IC (2–4 entries).** Store the last N `(shape, offset)` pairs. Check all entries on each access. Better hit rate for polymorphic sites, but adds complexity and cache footprint.

**Decision: Monomorphic ICs only.** Stash code patterns are overwhelmingly monomorphic — each call site typically accesses one struct type. Polymorphic call sites (rare) fall back to the full dictionary lookup at the same speed as today. This keeps the implementation simple and the cache structure minimal.

---

## 6. StashInstance Migration — Array-Backed Fields

### 6.1 Current: Dictionary-Based

```csharp
public class StashInstance
{
    private readonly Dictionary<string, object?> _fields;

    public object? GetField(string name, SourceSpan? span)
    {
        if (_fields.TryGetValue(name, out object? value))
            return value;
        // ... method fallback ...
    }
}
```

### 6.2 Proposed: Array-Backed with Shape

```csharp
public class StashInstance
{
    public Shape? Shape { get; }
    internal object?[] Fields { get; }   // Indexed by Shape.GetFieldIndex()
    public string TypeName { get; }
    public StashStruct? Struct { get; }

    internal StashInstance(string typeName, StashStruct? structDef, Shape? shape, object?[] fields)
    {
        TypeName = typeName;
        Struct = structDef;
        Shape = shape;
        Fields = fields;
    }

    public object? GetField(string name, SourceSpan? span)
    {
        // Fast path: shape-based lookup
        if (Shape is not null)
        {
            int idx = Shape.GetFieldIndex(name);
            if (idx >= 0)
                return Fields[idx];
        }

        // Fallback: struct method lookup
        if (Struct != null && Struct.Methods.TryGetValue(name, out IStashCallable? method))
            return new StashBoundMethod(this, method);

        throw new RuntimeError($"Undefined field '{name}' on struct '{TypeName}'.", span);
    }

    public void SetField(string name, object? value, SourceSpan? span)
    {
        if (Shape is not null)
        {
            int idx = Shape.GetFieldIndex(name);
            if (idx >= 0)
            {
                Fields[idx] = value;
                return;
            }
        }
        throw new RuntimeError($"Undefined field '{name}' on struct '{TypeName}'.", span);
    }

    /// <summary>
    /// Returns all fields as key-value pairs (for debugger, stringify, etc.).
    /// Uses shape's field ordering for consistent output.
    /// </summary>
    public IEnumerable<KeyValuePair<string, object?>> GetAllFields()
    {
        if (Shape is not null)
        {
            for (int i = 0; i < Shape.FieldCount; i++)
                yield return new KeyValuePair<string, object?>(
                    Struct!.Fields[i], Fields[i]);
        }
    }

    public IReadOnlyDictionary<string, object?> GetFields()
    {
        var dict = new Dictionary<string, object?>(Shape?.FieldCount ?? 0);
        if (Shape is not null)
        {
            for (int i = 0; i < Shape.FieldCount; i++)
                dict[Struct!.Fields[i]] = Fields[i];
        }
        return dict;
    }
}
```

### 6.3 Construction Change in VM

```csharp
// Before (OP_STRUCT_INIT):
var allFields = new Dictionary<string, object?>(ss.Fields.Count);
foreach (string f in ss.Fields) allFields[f] = null;
foreach (var kvp in providedFields) allFields[kvp.Key] = kvp.Value;
Push(new StashInstance(ss.Name, ss, allFields));

// After:
Shape shape = ss.Shape!;
var fields = new object?[shape.FieldCount];
// Initialize with provided values using shape index lookup
foreach (var kvp in providedFields)
{
    int idx = shape.GetFieldIndex(kvp.Key);
    if (idx < 0)
        throw new RuntimeError($"Unknown field '{kvp.Key}' on struct '{ss.Name}'.", span);
    fields[idx] = kvp.Value;
}
Push(new StashInstance(ss.Name, ss, shape, fields));
```

### 6.4 Memory Improvement

- **Before:** `Dictionary<string, object?>` per instance ≈ 128+ bytes overhead (hash buckets, entries array, string references)
- **After:** `object?[]` per instance ≈ 24 bytes overhead (array header) + 8 bytes × field count

For a `Point { x, y }` instance:

- Before: ~128 bytes (dictionary) + 16 bytes (2 entries) = ~144 bytes
- After: 24 bytes (array header) + 16 bytes (2 elements) = ~40 bytes

**3.6× reduction in per-instance memory.** This is a significant secondary benefit beyond the speed improvement.

### 6.5 Compatibility: Non-Struct StashInstance

Some code paths create `StashInstance` without a `StashStruct` (e.g., `CommandResult`, anonymous instances from built-ins). These continue to work via the `GetField` fallback path (shape is `null`, use the legacy dictionary approach).

For these cases, keep a backward-compatible constructor:

```csharp
// For anonymous instances (no struct, no shape)
public StashInstance(string typeName, Dictionary<string, object?> fields)
{
    TypeName = typeName;
    Struct = null;
    Shape = null;
    // Convert dictionary to array + create ad-hoc shape, OR keep dictionary
    // Decision: keep as a separate DictionaryInstance subclass or use a flag
}
```

**Decision:** Use a flag or null-shape approach. When `Shape` is null, `Fields` is still an array, but `GetField` falls back to a linear scan of `Struct.Fields` for the name. Alternatively, anonymous instances can use a different code path. The simplest approach:

- Struct-backed instances: `Shape` is set, `Fields` is indexed by shape
- Anonymous instances: Create an ad-hoc `Shape` at construction time from the dictionary keys. This is a one-time cost and lets all `StashInstance` code use the same array path.

---

## 7. StashDictionary — No Shape (Polymorphic)

`StashDictionary` allows arbitrary keys and dynamic add/remove. It does **not** benefit from shapes or inline caching.

When `OP_GET_FIELD_IC` encounters a `StashDictionary`, it takes the slow path (same as current `OP_GET_FIELD`). The IC remains unset because dictionaries have no shape. This is correct — dictionary field access is already a hash lookup, and dictionaries are used differently from structs.

**No changes to `StashDictionary` in this spec.**

---

## 8. VM Integration

### 8.1 New Opcodes

```csharp
// Added to OpCode enum:

/// <summary>Get field with inline cache (u16 name + u16 IC slot).</summary>
GetFieldIC,     // New

/// <summary>Set field with inline cache (u16 name + u16 IC slot).</summary>
SetFieldIC,     // New
```

### 8.2 InlineCache Structure

```csharp
internal struct InlineCache
{
    public Shape? CachedShape;
    public int CachedIndex;     // Field index within the shape's array

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryGet(StashInstance instance, out object? value)
    {
        if (instance.Shape == CachedShape)
        {
            value = instance.Fields[CachedIndex];
            return true;
        }
        value = default;
        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TrySet(StashInstance instance, object? value)
    {
        if (instance.Shape == CachedShape)
        {
            instance.Fields[CachedIndex] = value;
            return true;
        }
        return false;
    }
}
```

### 8.3 Chunk Extension

```csharp
public class Chunk
{
    // ... existing fields ...
    public InlineCache[]? InlineCaches { get; set; }
}
```

Allocated by the compiler when it emits the first `GetFieldIC` / `SetFieldIC` instruction. Sized to the number of IC-using field access sites in the chunk.

### 8.4 OpCodeInfo Update

```csharp
// GetFieldIC has 4 bytes of operands: u16 name + u16 IC slot
// SetFieldIC has 4 bytes of operands: u16 name + u16 IC slot
```

### 8.5 ReadU16 Helper for IC

The VM already has `ReadU16`. The IC handler reads two u16 operands in sequence:

```csharp
case OpCode.GetFieldIC:
{
    ushort nameIdx = ReadU16(ref frame);
    ushort icSlot = ReadU16(ref frame);
    // ... (as shown in Section 5.3)
    break;
}
```

---

## 9. Compiler Integration

### 9.1 IC Slot Allocation

The compiler tracks an IC slot counter per chunk:

```csharp
// In Compiler.cs / CompilerScope.cs
private int _nextIcSlot = 0;
```

When compiling a dot expression on a non-namespace, non-enum target, the compiler emits `GetFieldIC` instead of `GetField`:

```csharp
// Compiler.cs — VisitDotExpr (modified)
public object? VisitDotExpr(DotExpr expr)
{
    CompileExpr(expr.Object);
    ushort nameIdx = _builder.AddConstant(expr.Name.Lexeme);

    if (expr.IsOptional)
    {
        // Optional chaining — same as before, but use IC variant
        // ... existing optional chain code with GetFieldIC ...
    }

    // Emit IC variant for potential struct access
    ushort icSlot = AllocateIcSlot();
    _builder.Emit(OpCode.GetFieldIC, nameIdx, icSlot);
    return null;
}

private ushort AllocateIcSlot()
{
    return (ushort)_nextIcSlot++;
}
```

### 9.2 ChunkBuilder Extension

```csharp
// New emit overload for 4-byte operand instructions
public void Emit(OpCode opCode, ushort operand1, ushort operand2)
{
    _code.Add((byte)opCode);
    _code.Add((byte)(operand1 >> 8));
    _code.Add((byte)(operand1 & 0xFF));
    _code.Add((byte)(operand2 >> 8));
    _code.Add((byte)(operand2 & 0xFF));
}
```

### 9.3 Chunk Construction

After compilation, the compiler sets the IC array size:

```csharp
Chunk chunk = _builder.Build();
if (_nextIcSlot > 0)
    chunk.InlineCaches = new InlineCache[_nextIcSlot];
```

### 9.4 Which Field Accesses Get ICs?

| Expression Type              | Gets IC?          | Rationale                                                                                |
| ---------------------------- | ----------------- | ---------------------------------------------------------------------------------------- |
| `obj.field` (general)        | Yes               | May be a struct instance                                                                 |
| `ns.func` (namespace access) | No, if detectable | Namespace access is resolved at compile time when the target is a known global namespace |
| `Enum.Member`                | No, if detectable | Enum member access is resolved differently                                               |
| `error.message`              | Yes               | StashError is a potential struct-like access                                             |
| `dict.key`                   | Yes               | IC will miss, but the miss path is no slower than current                                |

**Heuristic:** Emit `GetFieldIC` for all dot accesses that aren't compile-time-known namespace/enum accesses. The IC miss path has minimal overhead (one extra `is StashInstance` check + one shape comparison), and the hit path is dramatically faster.

---

## 10. Extension Methods and UFCS Interaction

### 10.1 Extension Method Dispatch

The current `GetFieldValue` checks for extension methods after struct fields:

```csharp
if (_extensionRegistry.TryGetMethod("dict", name, out IStashCallable? dictExtMethod) && ...)
    return new VMExtensionBoundMethod(obj, dictExtFunc);
```

The IC fast path **only caches struct field access** — it does not cache method resolution. When the IC hits, it returns the field value directly. Methods (including extension methods and UFCS) are never cached by the IC because they require constructing a bound method wrapper, which depends on the specific instance.

### 10.2 Method Access on Structs

When `obj.method` is accessed on a struct and `method` is in `StashStruct.Methods`, the IC **does not** cache this because:

1. Methods return `StashBoundMethod` wrapping the specific instance — the result is instance-dependent
2. The field array only stores field values, not methods

The IC miss path handles method access correctly through the existing `GetField → Struct.Methods fallback`.

### 10.3 Future: Method IC

A future optimization could add a separate method inline cache that caches `(shape, method_callable)` pairs. Since the method callable is the same for all instances of a struct type (it's stored on `StashStruct`, not on the instance), the IC could cache the callable and create the `VMBoundMethod` wrapper on the fly. This is out of scope for this spec.

---

## 11. Cross-Cutting Concerns

### 11.1 Thread Safety

IC arrays are per-`Chunk`, and each VM instance is single-threaded. Chunks may be shared across VM instances (e.g., for async task.run), but since each VM writes to its own IC slots and IC updates are idempotent (shape + index), there's no correctness issue. At worst, two VMs overwrite the same IC slot with the same data.

### 11.2 Memory Overhead

Each IC slot is 12 bytes (8-byte reference + 4-byte int). A typical function with 10 field access sites adds 120 bytes of IC data. This is negligible.

### 11.3 Debugger Impact

No impact. The debugger never accesses IC data. Field inspection goes through `StashInstance.GetAllFields()`, which works with the array-backed storage directly.

### 11.4 Disassembler Update

The disassembler must handle the new 5-byte IC instructions:

```csharp
case OpCode.GetFieldIC:
case OpCode.SetFieldIC:
{
    ushort nameIdx = (ushort)((chunk.Code[offset + 1] << 8) | chunk.Code[offset + 2]);
    ushort icSlot = (ushort)((chunk.Code[offset + 3] << 8) | chunk.Code[offset + 4]);
    sb.Append($"{opName,-16} {nameIdx,4} ic:{icSlot}");
    if (nameIdx < chunk.Constants.Length && chunk.Constants[nameIdx] is string s)
        sb.Append($"    ; {s}");
    sb.AppendLine();
    return offset + 5;
}
```

### 11.5 Serialization / Caching

ICs are runtime state — they are not serialized. If bytecode serialization is implemented later, IC slots are initialized to empty on deserialization and warm up naturally on first execution.

### 11.6 WASM Compatibility

No unsafe code. `Shape` is a regular class, `InlineCache` is a regular struct. Fully compatible with Blazor WASM.

---

## 12. Migration Strategy

### 12.1 Phase A — Shape Infrastructure

1. Add `Shape.cs` to `Stash.Bytecode`
2. Add `Shape?` property to `StashStruct`
3. Create shapes in `OP_STRUCT_DECL` handler

**No behavior changes.** Shapes exist but aren't used yet.

### 12.2 Phase B — Array-Backed StashInstance

1. Change `StashInstance._fields` from `Dictionary<string, object?>` to `object?[]`
2. Add `Shape?` and `Fields` (the array) properties
3. Update `GetField` / `SetField` to use shape index when available
4. Update `GetAllFields` / `GetFields` to reconstruct from array + shape
5. Update `OP_STRUCT_INIT` to construct array-backed instances
6. Handle anonymous instances (no struct) with ad-hoc shapes or dictionary fallback

**Run full test suite.** All tests should pass — behavior is identical, only internal storage changed.

### 12.3 Phase C — Inline Cache Opcodes

1. Add `GetFieldIC` and `SetFieldIC` to `OpCode` enum
2. Add `InlineCache` struct and `Chunk.InlineCaches` property
3. Add IC slot allocation to the compiler
4. Add IC opcode handlers to the VM
5. Update `ChunkBuilder` with 4-byte operand emit
6. Update `Disassembler` for new opcodes
7. Update `OpCodeInfo.OperandSize` for new opcodes

**Run full test suite + benchmarks.** Measure improvement on Algorithms benchmark.

### 12.4 What Does NOT Change

| Component             | Why unchanged                                                  |
| --------------------- | -------------------------------------------------------------- |
| Tree-walk interpreter | Uses its own field access path, unaffected                     |
| `StashDictionary`     | No shapes, IC misses gracefully                                |
| Built-in functions    | Access fields via `GetField` / `GetAllFields` which still work |
| LSP / Analysis        | No runtime types involved                                      |
| DAP                   | Uses `GetAllFields()` which is updated in Phase B              |

---

## 13. Test Strategy

### 13.1 Shape Unit Tests

- Shape created for struct with 0, 1, 5, 20 fields
- `GetFieldIndex` returns correct index for all fields
- `GetFieldIndex` returns -1 for unknown field names
- Shape identity: same struct → same shape object

### 13.2 StashInstance Unit Tests

- Array-backed construction with all fields provided
- Array-backed construction with partial fields (remaining are null)
- `GetField` returns correct values by name
- `SetField` modifies correct index
- `SetField` rejects unknown fields
- `GetAllFields` returns all fields in declaration order
- `GetFields` returns correct dictionary

### 13.3 IC Integration Tests

- Cold IC (first access) → correct value returned, IC populated
- Warm IC (second access) → cache hit, correct value returned
- Different struct types at same call site → IC updates, correct values
- Non-struct objects at IC site → IC miss, correct fallback behavior
- IC with method access → IC does not cache, method bound correctly

### 13.4 Full Test Suite

All 4,400+ tests must pass after each phase (A, B, C).

### 13.5 Benchmark Validation

Run Algorithms benchmark before and after. Expected: 15–20% improvement (fewer dictionary lookups, better cache locality from array-backed fields).

---

## 14. Risk Register

| Risk                                                                            | Impact        | Probability | Mitigation                                                                                    |
| ------------------------------------------------------------------------------- | ------------- | ----------- | --------------------------------------------------------------------------------------------- |
| Array-backed StashInstance breaks built-in functions that iterate `_fields`     | Correctness   | Medium      | `GetAllFields()` and `GetFields()` return compatible types — audit all callers                |
| Anonymous StashInstance (no struct) path regression                             | Correctness   | Medium      | Ad-hoc shape or separate dictionary-backed subclass — test both paths                         |
| IC operand size increase (3→5 bytes) increases bytecode size                    | Minor perf    | Low         | Only for field accesses — total bytecode size increase <5%. IC benefit far outweighs the cost |
| `Shape` reference comparison (`==`) fails if shapes are accidentally duplicated | Correctness   | Low         | Enforce one shape per StashStruct via the `Shape` property — create at StructDecl time only   |
| `object?[]` Fields array publicly accessible via `internal`                     | Encapsulation | Low         | Mark as `internal` (only `Stash.Bytecode` needs direct access), public API uses `GetField`    |

---

## 15. Decision Log

| Date       | Decision                                                               | Alternatives Considered                          | Rationale                                                                                                                                                      |
| ---------- | ---------------------------------------------------------------------- | ------------------------------------------------ | -------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| 2026-04-05 | Monomorphic ICs only (no polymorphic/megamorphic)                      | 2-4 entry polymorphic ICs                        | Stash structs have fixed shapes — sites are overwhelmingly monomorphic. Polymorphic adds complexity with no benefit for this language                          |
| 2026-04-05 | Chunk-level IC array (not instruction-stream embedded)                 | Embed cache in bytecode bytes                    | .NET GC cannot track managed references in byte arrays. Chunk-level array is clean and GC-safe                                                                 |
| 2026-04-05 | New `GetFieldIC`/`SetFieldIC` opcodes (not modify existing `GetField`) | Change existing opcode encoding                  | Preserves backward compatibility. Non-IC variant still useful for namespace/enum access where IC wouldn't help                                                 |
| 2026-04-05 | Array-backed StashInstance fields (not keep dictionary)                | Keep dictionary, only use IC for dispatch bypass | Array access is faster than dictionary even without IC. The memory reduction (3.6×) is a significant secondary benefit. Shape system is needed anyway          |
| 2026-04-05 | No shapes for StashDictionary                                          | Add shapes for dictionaries                      | Dictionaries have dynamic keys — shapes would constantly transition. The complexity isn't worth it. Dictionary access is already O(1) amortized via hash table |
| 2026-04-05 | IC does not cache method resolution, only field access                 | Cache method resolution too                      | Methods require wrapping in `VMBoundMethod` per-instance. The wrapper allocation dominates any IC benefit. Future optimization                                 |
