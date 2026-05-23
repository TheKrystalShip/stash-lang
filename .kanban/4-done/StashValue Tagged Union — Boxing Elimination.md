# StashValue Tagged Union — Boxing Elimination

**Status:** Backlog — Design Spec
**Created:** 2026-04-05
**Parent:** Bytecode VM — Implementation Roadmap (Post-Phase 8 Optimization #1)
**Purpose:** Replace `object?` value representation throughout the bytecode VM with a struct-based tagged union that stores primitives inline, eliminating boxing allocations on the hot path.
**Expected Impact:** 20–40% performance improvement across all benchmarks.

---

## Table of Contents

1. [Motivation](#1-motivation)
2. [Current Architecture — Boxing Cost Analysis](#2-current-architecture--boxing-cost-analysis)
3. [Design — StashValue Struct](#3-design--stashvalue-struct)
4. [Encoding Strategy](#4-encoding-strategy)
5. [VM Integration](#5-vm-integration)
6. [Runtime Operations Migration](#6-runtime-operations-migration)
7. [Built-in Function Bridge](#7-built-in-function-bridge)
8. [Collection Types Impact](#8-collection-types-impact)
9. [Upvalue and Closure Impact](#9-upvalue-and-closure-impact)
10. [Constant Pool Migration](#10-constant-pool-migration)
11. [Cross-Cutting Concerns](#11-cross-cutting-concerns)
12. [Migration Strategy](#12-migration-strategy)
13. [Test Strategy](#13-test-strategy)
14. [Risk Register](#14-risk-register)
15. [Decision Log](#15-decision-log)

---

## 1. Motivation

The bytecode VM currently represents all values on the stack as `object?`. This means every primitive operation — arithmetic, comparison, truthiness — requires **boxing** (allocating a heap object for value types) and **unboxing** (type-checking and extracting the value). These are the dominant costs in the hot loop.

### Concrete boxing points in the current VM

Every arithmetic operation boxes its result:

```csharp
// VirtualMachine.cs line 431-440
case OpCode.Add:
{
    object? b = Pop();          // unbox: check type, extract long
    object? a = Pop();          // unbox: check type, extract long
    if (a is long la && b is long lb)
        Push(la + lb);          // BOX: allocate 24-byte heap object for long result
    else
        Push(RuntimeOps.Add(a, b, ...));  // BOX again
    break;
}
```

For a tight loop `for (let i = 0; i < 1000000; i++) { x = x + 1; }`, every iteration boxes:

1. Load `i` → already boxed on stack ✓ (no alloc)
2. Load `1000000` → boxed constant ✓ (no alloc)
3. `LessThan` → unbox both, compare, **box** `bool` result
4. Load `x` → already on stack ✓
5. Load `1` → boxed constant ✓
6. `Add` → unbox both, add, **box** `long` result
7. `StoreLocal` → store boxed result ✓

That's **2 boxing allocations per iteration** just from this fragment — 2,000,000 heap allocations for a million iterations. Each `long` box is 24 bytes on a 64-bit .NET runtime (16-byte object header + 8-byte payload). At 2M allocations, that's 48 MB of garbage generated for a trivial loop.

### Benchmark impact estimate

| Benchmark             | Current (VM) | Boxing-heavy operations         | Estimated improvement |
| --------------------- | -----------: | ------------------------------- | --------------------: |
| Algorithms            |       539 ms | fib recursion, sort comparisons |                30–40% |
| Function Calls        |       246 ms | arg passing, return values      |                20–30% |
| Expression Throughput |       659 ms | 70-variable arithmetic chains   |                30–40% |
| Built-in Functions    |       778 ms | args bridge to `List<object?>`  |                10–15% |
| Scope Lookup          |       261 ms | local load/store cycles         |                25–35% |

Built-in Functions improves least because the `IStashCallable.Call(context, List<object?>)` interface forces re-boxing at the boundary. That bridge is explicitly out of scope for this optimization (see Section 7).

---

## 2. Current Architecture — Boxing Cost Analysis

### 2.1 Value Stack

```csharp
// VirtualMachine.cs line 28
private object?[] _stack;
```

Every element is a heap reference. Value types (`long`, `double`, `bool`) are boxed when stored, unboxed when read via pattern matching (`is long la`).

### 2.2 Type Dispatch Pattern

The VM uses C# pattern matching for fast-path type checks:

```csharp
if (a is long la && b is long lb)    // JIT emits: load type handle, compare, branch
    Push(la + lb);                    // JIT emits: add, box, store to array
```

.NET's JIT handles `is long` efficiently (single type-handle comparison), but the **boxing on output** is unavoidable with `object?[]`.

### 2.3 Where boxing dominates

| Site                             | File                       | Boxing?                   | Frequency               |
| -------------------------------- | -------------------------- | ------------------------- | ----------------------- |
| `Push(la + lb)`                  | VirtualMachine.cs:434      | Yes — long result boxed   | Every integer add       |
| `Push(la - lb)`                  | VirtualMachine.cs:444      | Yes                       | Every integer sub       |
| `Push(la * lb)`                  | VirtualMachine.cs:456      | Yes                       | Every integer mul       |
| `Push(-l)`                       | VirtualMachine.cs:475      | Yes                       | Every negate            |
| `Push(RuntimeOps.IsEqual(a, b))` | VirtualMachine.cs:535      | Yes — bool boxed          | Every equality check    |
| `Push(RuntimeOps.LessThan(...))` | VirtualMachine.cs: various | Yes — bool boxed          | Every comparison        |
| `RuntimeOps.Add()` return        | RuntimeOps.cs:41           | Yes — returns `object?`   | Every non-fast-path add |
| `Upvalue.Value` set              | Upvalue.cs:40              | Yes — stores to `object?` | Every upvalue write     |
| `Chunk.Constants[]`              | Chunk.cs:12                | Yes — `object?[]`         | Every const load        |

### 2.4 Where boxing does NOT occur (already optimal)

| Site                       | Why no boxing                                  |
| -------------------------- | ---------------------------------------------- |
| String operations          | `string` is a reference type — no boxing       |
| Array/Dict/Instance        | Reference types — no boxing                    |
| `Push(null)`               | `null` is already a reference — no boxing      |
| `LoadLocal` / `StoreLocal` | Moves `object?` references — no new allocation |

---

## 3. Design — StashValue Struct

### 3.1 Tagged Union Layout

```csharp
[StructLayout(LayoutKind.Explicit, Size = 16)]
public readonly struct StashValue
{
    // --- Tag byte: discriminates the union ---
    [FieldOffset(0)]
    public readonly StashValueTag Tag;

    // --- Inline primitive payload (8 bytes) ---
    [FieldOffset(8)]
    private readonly long _intValue;

    [FieldOffset(8)]
    private readonly double _floatValue;

    [FieldOffset(8)]
    private readonly byte _boolValue;

    // --- Object reference for heap types ---
    [FieldOffset(8)]  // NOT overlapped with primitives — see Section 4
    private readonly object? _objValue;
}
```

> **Critical .NET constraint:** In the actual implementation, `_objValue` **cannot** overlap with primitive fields in the same struct via `LayoutKind.Explicit` because the GC must be able to distinguish reference fields from value fields. See Section 4 for the encoding strategy that solves this.

### 3.2 Tag Enum

```csharp
public enum StashValueTag : byte
{
    Null = 0,
    Bool = 1,
    Int = 2,        // long (64-bit)
    Float = 3,      // double (64-bit)
    Obj = 4,        // heap-allocated object (string, list, dict, instance, callable, ...)
}
```

Only 5 tags. All non-primitive types share the `Obj` tag — the specific type is determined by runtime type checks on the object reference, just like today.

### 3.3 Why only 5 tags?

Stash has 20+ runtime types, but only 4 are value types that benefit from inline storage:

| Type       | C# Type           | Value type?   | Boxing cost? | Tag?    |
| ---------- | ----------------- | ------------- | ------------ | ------- |
| Null       | `null`            | N/A           | None         | `Null`  |
| Bool       | `bool`            | Yes           | 24 bytes/box | `Bool`  |
| Int        | `long`            | Yes           | 24 bytes/box | `Int`   |
| Float      | `double`          | Yes           | 24 bytes/box | `Float` |
| String     | `string`          | No (ref type) | None         | `Obj`   |
| Array      | `List<object?>`   | No            | None         | `Obj`   |
| Dict       | `StashDictionary` | No            | None         | `Obj`   |
| Instance   | `StashInstance`   | No            | None         | `Obj`   |
| All others | Various classes   | No            | None         | `Obj`   |

Adding more tags (e.g., separate tags for String, Array, Dict) would bloat the switch dispatch without eliminating any allocations — those types are already heap-allocated.

---

## 4. Encoding Strategy

### 4.1 The GC Constraint

.NET's garbage collector needs to know which fields in a struct contain object references so it can trace them during collection. `LayoutKind.Explicit` with overlapping reference and value-type fields is **unsupported** — the runtime throws a `TypeLoadException`.

### 4.2 Solution: Two-field struct (no explicit layout)

```csharp
public readonly struct StashValue
{
    // 1 byte tag + 7 bytes padding + 8 bytes data + 8 bytes reference = 24 bytes
    // But with struct packing, .NET will lay this out as:
    //   Tag (1 byte) + padding (7 bytes) + Data (8 bytes) + Obj (8 bytes) = 24 bytes

    public readonly StashValueTag Tag;
    private readonly long _data;        // Stores long, double (bit-cast), or bool (0/1)
    private readonly object? _obj;      // Stores heap-allocated values (string, list, etc.)

    // --- Constructors ---

    private StashValue(StashValueTag tag, long data, object? obj)
    {
        Tag = tag;
        _data = data;
        _obj = obj;
    }

    // --- Factory methods ---

    public static readonly StashValue Null = new(StashValueTag.Null, 0, null);
    public static readonly StashValue True = new(StashValueTag.Bool, 1, null);
    public static readonly StashValue False = new(StashValueTag.Bool, 0, null);
    public static readonly StashValue Zero = new(StashValueTag.Int, 0, null);
    public static readonly StashValue One = new(StashValueTag.Int, 1, null);

    public static StashValue FromInt(long value) => new(StashValueTag.Int, value, null);
    public static StashValue FromFloat(double value) =>
        new(StashValueTag.Float, BitConverter.DoubleToInt64Bits(value), null);
    public static StashValue FromBool(bool value) => value ? True : False;
    public static StashValue FromObj(object value) => new(StashValueTag.Obj, 0, value);

    // --- Accessors ---

    public long AsInt
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _data;
    }

    public double AsFloat
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => BitConverter.Int64BitsToDouble(_data);
    }

    public bool AsBool
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _data != 0;
    }

    public object? AsObj
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _obj;
    }

    // --- Type checks ---

    public bool IsNull => Tag == StashValueTag.Null;
    public bool IsInt => Tag == StashValueTag.Int;
    public bool IsFloat => Tag == StashValueTag.Float;
    public bool IsBool => Tag == StashValueTag.Bool;
    public bool IsObj => Tag == StashValueTag.Obj;
    public bool IsNumeric => Tag == StashValueTag.Int || Tag == StashValueTag.Float;
}
```

### 4.3 Size analysis

The struct is **24 bytes** on 64-bit .NET:

- `Tag` (1 byte) + 7 bytes alignment padding
- `_data` (8 bytes)
- `_obj` (8 bytes — GC-tracked reference)

This is the same size as a single boxed `long` (16-byte object header + 8-byte payload), but the struct lives **on the stack** — no heap allocation, no GC pressure.

### 4.4 Pre-allocated singletons

Frequently-used values are pre-allocated as `static readonly` fields:

- `StashValue.Null` — used for null pushes, default returns
- `StashValue.True` / `StashValue.False` — used for boolean results of comparisons
- `StashValue.Zero` / `StashValue.One` — common loop counters

These eliminate allocation even in the struct case (the struct is copied, but it's a stack copy — free).

### 4.5 Alternative considered: NaN-boxing

NaN-boxing encodes all values in a single 8-byte `double` by exploiting the NaN payload bits. This is more compact (8 bytes vs 24) but:

1. **Requires `unsafe` code** — bit manipulation of pointers
2. **Breaks AOT compatibility** — Stash CLI uses Native AOT
3. **Limits integer range** — only 48-bit integers fit in the NaN payload (vs 64-bit `long`)
4. **Complicates debugging** — values are opaque bit patterns

The 24-byte tagged union is the right tradeoff: zero unsafe code, full 64-bit integers, GC-compatible, and still eliminates all hot-path boxing.

---

## 5. VM Integration

### 5.1 Stack Type Change

```csharp
// Before:
private object?[] _stack;
private void Push(object? value) { _stack[_sp++] = value; }
private object? Pop() => _stack[--_sp];

// After:
private StashValue[] _stack;

[MethodImpl(MethodImplOptions.AggressiveInlining)]
private void Push(StashValue value) { _stack[_sp++] = value; }

[MethodImpl(MethodImplOptions.AggressiveInlining)]
private StashValue Pop() => _stack[--_sp];

[MethodImpl(MethodImplOptions.AggressiveInlining)]
private ref StashValue Peek() => ref _stack[_sp - 1];
```

### 5.2 Arithmetic Fast Paths

```csharp
// Before — boxing on every result:
case OpCode.Add:
{
    object? b = Pop();
    object? a = Pop();
    if (a is long la && b is long lb)
        Push(la + lb);              // BOX: 24-byte allocation
    else
        Push(RuntimeOps.Add(a, b, ...));
    break;
}

// After — zero allocation:
case OpCode.Add:
{
    StashValue b = Pop();
    StashValue a = Pop();
    if (a.Tag == StashValueTag.Int && b.Tag == StashValueTag.Int)
        Push(StashValue.FromInt(a.AsInt + b.AsInt));  // Struct copy, no heap alloc
    else
        Push(RuntimeOps.Add(a, b, GetCurrentSpan(ref frame)));
    break;
}
```

### 5.3 Comparison Fast Paths

```csharp
// Before:
case OpCode.LessThan:
{
    object? b = Pop(), a = Pop();
    Push(RuntimeOps.LessThan(a, b, ...));  // BOX: bool → object
    break;
}

// After:
case OpCode.LessThan:
{
    StashValue b = Pop();
    StashValue a = Pop();
    if (a.Tag == StashValueTag.Int && b.Tag == StashValueTag.Int)
        Push(StashValue.FromBool(a.AsInt < b.AsInt));  // Returns pre-alloc True/False
    else
        Push(RuntimeOps.LessThan(a, b, ...));
    break;
}
```

### 5.4 Variable Load/Store

```csharp
// Before:
case OpCode.LoadLocal:
{
    byte slot = ReadByte(ref frame);
    Push(_stack[frame.BaseSlot + slot]);  // Copy object reference
    break;
}

// After — identical semantics, no boxing:
case OpCode.LoadLocal:
{
    byte slot = ReadByte(ref frame);
    Push(_stack[frame.BaseSlot + slot]);  // Copy 24-byte StashValue struct
    break;
}
```

`LoadLocal` / `StoreLocal` are essentially unchanged — the struct is copied by value, which is a fast stack operation.

### 5.5 Truthiness Check

```csharp
// Before:
[MethodImpl(MethodImplOptions.AggressiveInlining)]
private static bool IsFalsy(object? value) => !RuntimeValues.IsTruthy(value);

// After:
[MethodImpl(MethodImplOptions.AggressiveInlining)]
private static bool IsFalsy(StashValue value)
{
    return value.Tag switch
    {
        StashValueTag.Null => true,
        StashValueTag.Bool => !value.AsBool,
        StashValueTag.Int => value.AsInt == 0,
        StashValueTag.Float => value.AsFloat == 0.0,
        StashValueTag.Obj => value.AsObj is string s ? s.Length == 0 :
                             value.AsObj is StashError ? true : false,
        _ => false,
    };
}
```

### 5.6 Equality Check

```csharp
[MethodImpl(MethodImplOptions.AggressiveInlining)]
private static bool AreEqual(StashValue a, StashValue b)
{
    if (a.Tag != b.Tag) return false;     // No type coercion
    return a.Tag switch
    {
        StashValueTag.Null => true,       // null == null
        StashValueTag.Bool => a.AsBool == b.AsBool,
        StashValueTag.Int => a.AsInt == b.AsInt,
        StashValueTag.Float => a.AsFloat == b.AsFloat,
        StashValueTag.Obj => object.Equals(a.AsObj, b.AsObj),
        _ => false,
    };
}
```

This is faster than the current `RuntimeValues.IsEqual` because it avoids `GetType()` calls and `object.Equals` virtual dispatch for primitives.

---

## 6. Runtime Operations Migration

### 6.1 RuntimeOps Signature Changes

All `RuntimeOps` methods change from `object?` to `StashValue`:

```csharp
// Before:
public static object? Add(object? left, object? right, SourceSpan? span)

// After:
public static StashValue Add(StashValue left, StashValue right, SourceSpan? span)
```

### 6.2 Full Add Migration

```csharp
public static StashValue Add(StashValue left, StashValue right, SourceSpan? span)
{
    // Integer fast path (usually handled inline by VM, this is the fallback)
    if (left.IsInt && right.IsInt)
        return StashValue.FromInt(left.AsInt + right.AsInt);

    // Float promotion
    if (left.IsNumeric && right.IsNumeric)
        return StashValue.FromFloat(ToDouble(left) + ToDouble(right));

    // String concatenation
    if (left.IsObj && left.AsObj is string || right.IsObj && right.AsObj is string)
        return StashValue.FromObj(Stringify(left) + Stringify(right));

    // Unbox for complex type dispatch
    object? lObj = left.IsObj ? left.AsObj : null;
    object? rObj = right.IsObj ? right.AsObj : null;

    // IP address + offset
    if (lObj is StashIpAddress ipL && right.IsInt) return StashValue.FromObj(ipL.Add(right.AsInt));
    if (left.IsInt && rObj is StashIpAddress ipR) return StashValue.FromObj(ipR.Add(left.AsInt));

    // Duration + Duration
    if (lObj is StashDuration durL && rObj is StashDuration durR)
        return StashValue.FromObj(durL.Add(durR));

    // ByteSize + ByteSize
    if (lObj is StashByteSize bsL && rObj is StashByteSize bsR)
        return StashValue.FromObj(bsL.Add(bsR));

    throw new RuntimeError("Operands must be numbers or strings.", span);
}

[MethodImpl(MethodImplOptions.AggressiveInlining)]
private static double ToDouble(StashValue v)
{
    return v.Tag switch
    {
        StashValueTag.Int => (double)v.AsInt,
        StashValueTag.Float => v.AsFloat,
        _ => throw new InvalidOperationException("Not a number"),
    };
}
```

### 6.3 Methods that become trivially cheaper

| Method            | Before (boxing cost)                  | After (cost)                        |
| ----------------- | ------------------------------------- | ----------------------------------- |
| `IsFalsy`         | Unbox `object?` → bool/long/double    | Tag compare + inline data read      |
| `IsEqual`         | `GetType()` + `object.Equals()`       | Tag compare + inline data compare   |
| `LessThan` et al. | Unbox both operands + box bool result | Tag compare + inline compare        |
| `Negate`          | Unbox long → negate → box result      | Read `_data` → negate → struct copy |

---

## 7. Built-in Function Bridge

### 7.1 The Boundary Problem

All 26 namespaces of built-in functions use the `IStashCallable` interface:

```csharp
public interface IStashCallable
{
    int Arity { get; }
    int MinArity { get; }
    object? Call(IInterpreterContext context, List<object?> arguments);
}
```

This interface returns `object?` and takes `List<object?>` arguments. Changing this interface would require modifying **every built-in function** across all 26 namespaces — hundreds of methods.

### 7.2 Bridge Strategy

Keep the `IStashCallable` interface unchanged. Convert at the boundary:

```csharp
// In CallValue, when calling IStashCallable:
if (callee.AsObj is IStashCallable callable)
{
    var args = new List<object?>(argc);
    int argStart = _sp - argc;
    for (int i = argStart; i < _sp; i++)
        args.Add(_stack[i].ToObject());    // StashValue → object? conversion

    _sp = argStart - 1;

    object? result = callable.Call(_context, args);
    Push(StashValue.FromObject(result));    // object? → StashValue conversion
    return;
}
```

### 7.3 Conversion Methods

```csharp
// StashValue → boxed object (for passing to built-ins)
public object? ToObject()
{
    return Tag switch
    {
        StashValueTag.Null => null,
        StashValueTag.Bool => _data != 0,           // Box bool
        StashValueTag.Int => _data,                  // Box long
        StashValueTag.Float => AsFloat,              // Box double
        StashValueTag.Obj => _obj,
        _ => null,
    };
}

// Boxed object → StashValue (for receiving from built-ins)
public static StashValue FromObject(object? value)
{
    return value switch
    {
        null => Null,
        bool b => FromBool(b),
        long l => FromInt(l),
        double d => FromFloat(d),
        _ => FromObj(value),
    };
}
```

### 7.4 Future: IStashCallableV2

A future optimization (explicitly out of scope for this spec) can introduce a V2 interface:

```csharp
public interface IStashCallableV2
{
    StashValue Call(IInterpreterContext context, ReadOnlySpan<StashValue> arguments);
}
```

This eliminates the `List<object?>` allocation and conversion overhead entirely. Built-in functions can be migrated incrementally — the VM checks for V2 first, falls back to V1.

---

## 8. Collection Types Impact

### 8.1 Arrays

Currently `List<object?>`. **No change for this optimization.** Arrays still store boxed values because `List<T>` requires a reference type or value type — `List<StashValue>` would work but would change the type surfaced to built-in functions.

**Decision:** Keep `List<object?>` for arrays. The VM converts to/from `StashValue` at the boundary (array construction and element access). This limits the optimization's reach for array-heavy code but avoids cascading changes.

### 8.2 Dictionaries

Currently `StashDictionary` wrapping `Dictionary<object, object?>`. **No change.** Same rationale as arrays.

### 8.3 Struct Instances

Currently `StashInstance` wrapping `Dictionary<string, object?>`. **No change** for this spec. A future optimization could change `StashInstance` to store fields as `StashValue[]` with a shape-based index mapping (see Inline Caching spec).

### 8.4 Boundary Summary

| Boundary                       | Direction                       | Cost                  |
| ------------------------------ | ------------------------------- | --------------------- |
| VM stack → array element store | `StashValue.ToObject()`         | Boxing for primitives |
| Array element load → VM stack  | `StashValue.FromObject()`       | Unbox + struct init   |
| VM stack → dict value store    | `StashValue.ToObject()`         | Boxing for primitives |
| Dict value load → VM stack     | `StashValue.FromObject()`       | Unbox + struct init   |
| VM stack → struct field store  | `StashValue.ToObject()`         | Boxing for primitives |
| Struct field load → VM stack   | `StashValue.FromObject()`       | Unbox + struct init   |
| VM stack → built-in args       | `StashValue.ToObject()` per arg | Boxing for primitives |
| Built-in result → VM stack     | `StashValue.FromObject()`       | Unbox + struct init   |

**These boundaries are where boxing survives.** The optimization eliminates boxing only within the VM's internal execution — arithmetic, comparisons, control flow, local variables, constants. For code that stays within the VM (pure computation), the improvement is maximal. For code that crosses into collections or built-ins, the improvement is partial.

---

## 9. Upvalue and Closure Impact

### 9.1 Current Upvalue

```csharp
internal sealed class Upvalue
{
    private object?[] _stack;       // ← references the VM's object? stack
    private object? _closed;        // ← stores boxed value when closed
    public int StackIndex { get; }
    public bool IsOpen { get; private set; }
}
```

### 9.2 Migrated Upvalue

```csharp
internal sealed class Upvalue
{
    private StashValue[] _stack;
    private StashValue _closed;
    public int StackIndex { get; }
    public bool IsOpen { get; private set; }

    public StashValue Value
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => IsOpen ? _stack[StackIndex] : _closed;
        set
        {
            if (IsOpen)
                _stack[StackIndex] = value;
            else
                _closed = value;
        }
    }

    public void Close()
    {
        if (!IsOpen) return;
        _closed = _stack[StackIndex];  // Copy 24-byte struct (no boxing!)
        IsOpen = false;
    }
}
```

**Improvement:** Closing an upvalue no longer boxes primitives. Accessing a closed upvalue for arithmetic no longer unboxes. This directly benefits the Scope Lookup benchmark (nested closures).

---

## 10. Constant Pool Migration

### 10.1 Current

```csharp
// Chunk.cs
public object?[] Constants { get; }
```

Constants include `long`, `double`, `string`, and nested `Chunk` objects. The first two are boxed.

### 10.2 Migrated

```csharp
public StashValue[] Constants { get; }
```

During compilation, the `ChunkBuilder` stores constants as `StashValue`:

```csharp
// ChunkBuilder
public ushort AddConstant(long value) => AddConstant(StashValue.FromInt(value));
public ushort AddConstant(double value) => AddConstant(StashValue.FromFloat(value));
public ushort AddConstant(string value) => AddConstant(StashValue.FromObj(value));
public ushort AddConstant(Chunk value) => AddConstant(StashValue.FromObj(value));

private ushort AddConstant(StashValue value) { ... }
```

The VM's `OP_CONST` handler becomes:

```csharp
case OpCode.Const:
{
    ushort idx = ReadU16(ref frame);
    Push(frame.Chunk.Constants[idx]);  // StashValue copy — no boxing
    break;
}
```

**Improvement:** Loading integer and float constants no longer involves any boxing.

---

## 11. Cross-Cutting Concerns

### 11.1 Memory Layout

The `StashValue[]` stack at 1024 elements = 24 KB (vs the current `object?[]` at 8 KB for references alone, but with hidden boxing allocations).

The larger stack footprint is more than offset by eliminating per-operation heap allocations. Net memory usage decreases significantly for computation-heavy workloads.

### 11.2 Cache Friendliness

A `StashValue[]` has better cache locality than `object?[]` for primitives:

- **Before:** `_stack[i]` is a pointer to a heap-allocated box. Reading the value requires a pointer chase (cache miss).
- **After:** `_stack[i]` contains the value inline in the array. Reading is a direct indexed access (cache hit).

### 11.3 GC Impact

The current VM generates millions of short-lived boxed primitives per benchmark. These fill Gen0, triggering frequent garbage collections. With `StashValue`, primitive values never touch the heap, dramatically reducing GC pressure.

### 11.4 Debugger Compatibility

The DAP integration uses `BuildFrameScope()` to expose local variables. This must convert `StashValue` back to `object?` for the existing `IDebugScope` interface:

```csharp
IDebugScope BuildFrameScope(ref CallFrame frame)
{
    // Convert StashValue locals to object? for DAP
    var locals = new Dictionary<string, object?>();
    for (int i = 0; i < frame.Chunk.LocalCount; i++)
    {
        string? name = frame.Chunk.LocalNames?[i];
        if (name is not null)
            locals[name] = _stack[frame.BaseSlot + i].ToObject();
    }
    return new FrameScope(locals, ...);
}
```

This conversion only happens when a debugger is attached and paused — zero overhead in production.

### 11.5 WASM Compatibility

`StashValue` uses no `unsafe` code, no `Unsafe.As`, no pointer manipulation. It is fully compatible with Blazor WASM's Mono runtime. The `BitConverter.DoubleToInt64Bits` / `Int64BitsToDouble` methods are supported on all .NET platforms.

### 11.6 Native AOT Compatibility

No reflection, no dynamic code generation. Fully compatible with the CLI's Native AOT build.

---

## 12. Migration Strategy

### 12.1 Phase Ordering

This is a pervasive change — it touches nearly every file in `Stash.Bytecode/`. The migration must be atomic within that project (no partial migration possible, since `VirtualMachine`, `RuntimeOps`, `Chunk`, `ChunkBuilder`, and `Upvalue` all share the value type).

**Order of changes:**

1. **Add `StashValue.cs` and `StashValueTag.cs`** — new files, no existing code changes
2. **Migrate `Chunk.Constants`** to `StashValue[]` — update `ChunkBuilder.AddConstant`
3. **Migrate `Upvalue`** — change `_stack` reference type, `_closed` type, `Value` property
4. **Migrate `VirtualMachine._stack`** to `StashValue[]` — update `Push`, `Pop`, `Peek`
5. **Migrate `RuntimeOps`** — change all signatures from `object?` to `StashValue`
6. **Update `CallValue`** — add `StashValue.ToObject()` / `FromObject()` bridge for `IStashCallable`
7. **Update `GetFieldValue` / `SetFieldValue`** — convert at collection boundaries
8. **Update `Disassembler`** — use `StashValue` for constant display
9. **Run full test suite** — verify all 4,400+ tests pass

### 12.2 What Does NOT Change

| Component                                                | Why unchanged                                                      |
| -------------------------------------------------------- | ------------------------------------------------------------------ |
| `Stash.Core` (AST, Lexer, Parser)                        | No runtime values involved                                         |
| `Stash.Analysis`                                         | No runtime values involved                                         |
| `Stash.Stdlib`                                           | All built-in functions use `IStashCallable` with `object?` bridge  |
| `Stash.Lsp`                                              | No runtime values involved                                         |
| `Stash.Dap`                                              | Uses `IDebugger` interface — adapter in VM handles conversion      |
| `Stash.Interpreter` (tree-walk)                          | Completely separate execution path, keeps `object?`                |
| `Stash.Playground`                                       | Uses `StashEngine` API — conversion at engine boundary             |
| Runtime types (`StashInstance`, `StashDictionary`, etc.) | Keep `object?` internally — collection optimization is future work |

### 12.3 Estimated Scope

| File                     | Changes                                               |
| ------------------------ | ----------------------------------------------------- |
| `StashValue.cs` (new)    | ~150 lines                                            |
| `StashValueTag.cs` (new) | ~10 lines                                             |
| `VirtualMachine.cs`      | ~300 lines modified (stack type, all opcode handlers) |
| `RuntimeOps.cs`          | ~200 lines modified (all method signatures + bodies)  |
| `Chunk.cs`               | ~10 lines (Constants type)                            |
| `ChunkBuilder.cs`        | ~30 lines (AddConstant overloads)                     |
| `Upvalue.cs`             | ~20 lines (stack type, closed type)                   |
| `CallFrame.cs`           | Unchanged                                             |
| `Compiler.cs`            | Unchanged (emits opcodes, not values)                 |
| `Disassembler.cs`        | ~15 lines (constant display)                          |

---

## 13. Test Strategy

### 13.1 Unit Tests for StashValue

- Roundtrip: `FromInt(42).AsInt == 42`
- Roundtrip: `FromFloat(3.14).AsFloat == 3.14`
- Roundtrip: `FromBool(true).AsBool == true`
- Roundtrip: `FromObj("hello").AsObj == "hello"`
- Tag discrimination: `FromInt(0).Tag == StashValueTag.Int` (not Null or Bool)
- Null: `Null.Tag == StashValueTag.Null`, `Null.IsNull == true`
- Singleton identity: `FromBool(true)` returns `StashValue.True`
- Object roundtrip: `FromObject(42L).AsInt == 42`, `FromObject(null).IsNull == true`
- ToObject roundtrip: `FromInt(42).ToObject()` is `(long)42`

### 13.2 Integration: Full Test Suite

All 4,400+ tests must pass without modification. The tests exercise Stash source code via `StashEngine` — the `StashValue` change is invisible to them because the engine's public API still uses `object?`.

### 13.3 Benchmark Validation

Run all 5 benchmarks with both backends before and after the change. Document the actual improvement per benchmark vs the estimates in Section 1.

---

## 14. Risk Register

| Risk                                                                             | Impact                                       | Probability | Mitigation                                                                                           |
| -------------------------------------------------------------------------------- | -------------------------------------------- | ----------- | ---------------------------------------------------------------------------------------------------- |
| 24-byte struct copy overhead exceeds boxing savings for some patterns            | Performance regression on specific workloads | Low         | Profile before/after; the JIT often eliminates struct copies for short-lived values                  |
| StashValue array increases stack memory footprint 3×                             | Memory pressure on deep recursion            | Low         | Default stack size (1024 × 24 = 24 KB) is still tiny; stack growth is rare                           |
| Bridge overhead at collection boundaries negates gains for collection-heavy code | Partial improvement                          | Medium      | Expected — this spec explicitly documents the boundary costs. Collection optimization is future work |
| `BitConverter.DoubleToInt64Bits` not inlined on some platforms                   | Float operations slower                      | Very Low    | This method is a JIT intrinsic on all mainstream .NET platforms                                      |
| Subtle behavior change in equality for edge cases                                | Correctness                                  | Low         | `AreEqual` must exactly match `RuntimeValues.IsEqual` semantics — test coverage is comprehensive     |

---

## 15. Decision Log

| Date       | Decision                                                            | Alternatives Considered                                              | Rationale                                                                                                                                                                                              |
| ---------- | ------------------------------------------------------------------- | -------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| 2026-04-05 | Two-field struct (tag + data + ref) over `LayoutKind.Explicit`      | Explicit layout with overlapping fields                              | .NET GC cannot track references in explict-layout structs with overlapping ref/value fields; `TypeLoadException` at runtime                                                                            |
| 2026-04-05 | Only 5 tags (Null, Bool, Int, Float, Obj)                           | Separate tags for String, Array, Dict, etc.                          | Additional tags add switch branches without eliminating allocations — those types are already heap-allocated reference types                                                                           |
| 2026-04-05 | Keep `IStashCallable` interface unchanged                           | Change all built-in functions to use `StashValue`                    | Would require modifying hundreds of built-in methods across 26 namespaces. Bridge approach isolates the change to `Stash.Bytecode`                                                                     |
| 2026-04-05 | Keep collections as `List<object?>` / `Dictionary<object, object?>` | Migrate to `List<StashValue>` / `Dictionary<StashValue, StashValue>` | Collections are surfaced to built-in functions via `IStashCallable.Call(context, List<object?>)`. Changing collection types cascades into stdlib. Future optimization                                  |
| 2026-04-05 | 24-byte struct over NaN-boxing                                      | NaN-boxing (8 bytes, all values in a double)                         | NaN-boxing requires unsafe pointer manipulation, limits integers to 48 bits, breaks WASM and AOT compatibility, and makes debugging harder. The 24-byte struct is safe, correct, and performant enough |
| 2026-04-04 | (from parent roadmap) Use `object?` initially                       | Start with StashValue from Phase 1                                   | Correct decision — validated all VM features first, now optimize with confidence                                                                                                                       |
