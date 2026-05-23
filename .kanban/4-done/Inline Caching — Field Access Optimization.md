# Inline Caching — Field Access Optimization

**Status:** Backlog — Design Spec (Draft)
**Created:** 2026-04-08
**Parent:** Bytecode VM — Implementation Roadmap (Post-Phase 8 Optimization)
**Depends on:** Slot-based globals (DONE), StashValue tagged union (DONE). Independent of Superinstructions — benefits compound.
**Purpose:** Cache the result of field/member lookups at each `GetField` call site so repeated accesses to the same field on the same receiver type skip the full dispatch chain. This is the #1 remaining bottleneck after slot-based globals: `ExecuteGetField` + `FrozenDictionary` account for 8.9% + 4.9% = **13.8% of self-time** in the built-in functions benchmark.
**Expected Impact:** 8–15% performance improvement on namespace-heavy code (built-in functions benchmark), 3–5% on general code.

---

## Table of Contents

1. [Motivation](#1-motivation)
2. [Current Field Access Architecture](#2-current-field-access-architecture)
3. [Profiling Evidence](#3-profiling-evidence)
4. [Inline Caching Design](#4-inline-caching-design)
5. [IC Slot Architecture](#5-ic-slot-architecture)
6. [Monomorphic IC for Namespace Members](#6-monomorphic-ic-for-namespace-members)
7. [Monomorphic IC for Struct Instance Fields](#7-monomorphic-ic-for-struct-instance-fields)
8. [IC State Machine](#8-ic-state-machine)
9. [VM Handler — GetFieldIC](#9-vm-handler--getfieldic)
10. [Compiler Changes](#10-compiler-changes)
11. [Cross-Cutting Concerns](#11-cross-cutting-concerns)
12. [Migration Strategy](#12-migration-strategy)
13. [Test Strategy](#13-test-strategy)
14. [Risk Register](#14-risk-register)
15. [Decision Log](#15-decision-log)

---

## 1. Motivation

Every `GetField` opcode in the Stash VM performs a full dispatch chain to resolve a field name on an object. For namespace member access (e.g., `math.sqrt`, `str.upper`), this involves:

1. Pop the receiver from the stack
2. Check if receiver is `StashNamespace` (tag check + type test)
3. Call `StashNamespace.GetMemberValue(fieldName, null)`
4. Inside `GetMemberValue`: `FrozenDictionary<string, StashValue>.TryGetValue(name, out value)`
5. Inside `FrozenDictionary.TryGetValue`: compute string hash → probe hash table → string equality compare
6. Push result

Steps 3–5 are pure overhead when the same call site always resolves to the same namespace and member. In a loop like:

```stash
while (i < ITERATIONS) {
    let sq = math.sqrt(i + 1);       // GetField: math → sqrt (same every time)
    let ab = math.abs(-i - 1);       // GetField: math → abs  (same every time)
    let up = str.upper("hello");     // GetField: str  → upper (same every time)
}
```

Each `GetField` performs the same `FrozenDictionary.TryGetValue` lookup millions of times, always returning the same `StashValue`. This is the textbook case for **monomorphic inline caching**: cache the result directly at the call site and guard with a cheap identity check.

### 1.1 Why Not Just Optimize FrozenDictionary?

`FrozenDictionary` is already .NET's most optimized read-only dictionary — it uses perfect hashing for small collections and length-bucketed string hashing for larger ones. The hash computation + string equality is the minimum cost for any hash-based lookup. The only way to beat it is to **skip the lookup entirely**, which is what inline caching does.

---

## 2. Current Field Access Architecture

### 2.1 ExecuteGetField — Opcode Handler

**File:** `Stash.Bytecode/VM/VirtualMachine.Collections.cs` (line ~400)

```csharp
private void ExecuteGetField(ref CallFrame frame)
{
    ushort nameIdx = ReadU16(ref frame);
    string fieldName = (string)frame.Chunk.Constants[nameIdx].AsObj!;
    StashValue objVal = Pop();

    // Fast path: namespace member access
    if (objVal.Tag == StashValueTag.Obj)
    {
        object? rawObj = objVal.AsObj;
        if (rawObj is StashNamespace ns)
        {
            Push(ns.GetMemberValue(fieldName, null));
            return;
        }
    }

    // General path via GetFieldValue (10-step type dispatch)
    object? obj = objVal.ToObject();
    Push(StashValue.FromObject(GetFieldValue(obj, fieldName, GetCurrentSpan(ref frame))));
}
```

### 2.2 GetFieldValue — 10-Step Type Dispatch

**File:** `Stash.Bytecode/VM/VirtualMachine.TypeOps.cs` (line ~242)

The general path goes through a cascading type-test chain:

1. `StashInstance` → `instance.GetField(name)` → `Dictionary<string, StashValue>.TryGetValue`
2. `StashDictionary` → extension methods check → `dict.Get(name)` → `Dictionary<object, StashValue>.TryGetValue`
3. `StashNamespace` → `ns.GetMember(name)` → `FrozenDictionary.TryGetValue`
4. `StashStruct` → `structDef.Methods.TryGetValue(name)` (static methods)
5. `StashEnum` → `enumDef.GetMember(name)`
6. `StashEnumValue` → property switch (`typeName`, `memberName`)
7. `StashError` → property switch (`message`, `type`, `stack`, custom)
8. Built-in types → `StashDuration`, `StashByteSize`, `StashSemVer`, `StashIpAddress` property switches
9. Array/string `.length` → direct `Count`/`Length` access
10. Extension methods + UFCS fallback → `_extensionRegistry` + global namespace lookup

Every step is a type test (`is` pattern match) that fails before reaching the correct case. For namespace access, only step 1 is checked (the fast path in `ExecuteGetField` catches it), but the `FrozenDictionary` lookup inside step 1 is still the bottleneck.

### 2.3 StashNamespace Internal Storage

**File:** `Stash.Core/Runtime/Types/StashNamespace.cs`

```csharp
public class StashNamespace
{
    private FrozenDictionary<string, StashValue>? _frozenMembers;

    public StashValue GetMemberValue(string name, SourceSpan? span)
    {
        if (_frozenMembers is not null)
        {
            if (_frozenMembers.TryGetValue(name, out StashValue frozenValue))
                return frozenValue;
        }
        // ...
    }
}
```

All built-in namespaces (`math`, `str`, `arr`, `dict`, `fs`, `path`, etc.) are frozen before script execution. Once frozen, the `FrozenDictionary` is immutable — members never change. This is the **perfect condition for caching**: the result is guaranteed stable.

### 2.4 StashInstance Field Storage

**File:** `Stash.Core/Runtime/Types/StashInstance.cs`

```csharp
public class StashInstance
{
    private readonly Dictionary<string, StashValue> _fields;

    public StashValue GetField(string name, SourceSpan? span)
    {
        if (_fields.TryGetValue(name, out StashValue value))
            return value;
        if (Struct != null && Struct.Methods.TryGetValue(name, out IStashCallable? method))
            return StashValue.FromObj(new StashBoundMethod(this, method));
        throw new RuntimeError($"Undefined field '{name}' on struct '{TypeName}'.", span);
    }
}
```

Struct instance fields change per-instance but the **field set is fixed by the struct definition**. If we know the receiver is a `StashInstance` with a particular `StashStruct`, we can cache the fact that the field exists (avoiding the dictionary probe) — though the value itself must still be read per-access since it varies per-instance.

---

## 3. Profiling Evidence

### 3.1 perf record — Built-in Functions Benchmark (Post Slot-Based Globals)

| Symbol                             |   Self % | Category            |
| ---------------------------------- | -------: | ------------------- |
| `RunInner` (dispatch loop)         |    47.2% | Dispatch overhead   |
| **`ExecuteGetField`**              | **8.9%** | **Field access**    |
| **`FrozenDictionary.TryGetValue`** | **4.9%** | **Hash lookup**     |
| `ExecuteCall` + `PushFrame`        |     6.3% | Function calls      |
| `StashValue` copies / GC barriers  |     5.1% | Value type overhead |

**Combined field access overhead: 13.8%.** This is the single largest category after raw dispatch.

### 3.2 Namespace Calls Benchmark Profile

| Symbol                                    | Self % | Notes                              |
| ----------------------------------------- | -----: | ---------------------------------- |
| `ExecuteGetField`                         |   8.9% | 24 GetField ops per loop iteration |
| `FrozenDictionary<>.GetValueRefOrNullRef` |   4.9% | String hash + probe                |

The namespace calls benchmark executes ~4.8M `GetField` opcodes (24 per iteration × 200,000 iterations). Each performs a `FrozenDictionary` lookup for the same namespace member.

### 3.3 Call Site Monomorphism

In Stash code, namespace member access sites are **universally monomorphic**:

- `math.sqrt` always resolves to the same `StashNamespace` (`math`) and the same member (`sqrt`)
- `str.upper` always resolves to `str` namespace, `upper` member
- The namespace object identity never changes (frozen at startup)

Struct field access is also **strongly monomorphic** in practice:

- `point.x` on a loop variable always accesses the same struct type
- Polymorphism (different struct types at the same call site) is rare

This means monomorphic inline caching will hit the fast path >99% of the time.

---

## 4. Inline Caching Design

### 4.1 Design Choice: Side-Table IC Slots

Two common approaches for inline caching in bytecode VMs:

**Option A: Mutable Bytecode (CPython ADAPTIVE style)**
Rewrite the opcode in-place from `GetField` → `GetField_NS` or `GetField_Instance` after the first execution. Store cached data inline in the bytecode stream.

_Pros:_ No extra allocation, cache data is co-located with the instruction.
_Cons:_ Mutable bytecode complicates sharing chunks across modules/closures; bytecode is no longer immutable after compilation; thread-safety concerns.

**Option B: Side-Table IC Slots**
Each `GetField` instruction references an IC slot index. The chunk owns an array of IC slot structs. The VM reads/writes the IC slot during execution.

_Pros:_ Bytecode remains immutable; IC state is per-chunk (natural ownership); easy to reset; thread-safe with per-VM IC arrays.
_Cons:_ Extra indirection (IC slot array access); slightly larger chunk metadata.

**Decision: Option B — Side-Table IC Slots.**

Rationale: Stash chunks are shared across closures and module imports. Mutable bytecode would require copy-on-write semantics or per-instance bytecode arrays. Side-table IC slots are simpler and align with the existing architecture where chunks are immutable after compilation.

> **Revision note:** If profiling shows the IC slot array access is a bottleneck (unlikely — it's a single array index), we can consider mutable bytecode as a follow-up.

### 4.2 IC Slot Lifetime

IC slots are populated on first execution and persist for the lifetime of the chunk. Since:

- Built-in namespaces are frozen and never mutate → IC never invalidates
- User-defined namespaces could theoretically mutate → guard check needed
- Struct definitions don't change after declaration → IC never invalidates

The guard strategy is: **check receiver identity (for namespaces) or receiver type (for struct instances)**. If the guard fails, fall back to the full lookup and optionally update the IC slot.

---

## 5. IC Slot Architecture

### 5.1 ICSlot Struct

```csharp
/// <summary>
/// Inline cache slot for GetField operations. Stores the result of a
/// previous field lookup to avoid repeated hash-table probes.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct ICSlot
{
    /// <summary>The cached receiver object (namespace, struct type, etc.).</summary>
    public object? Guard;

    /// <summary>The cached result — the resolved StashValue.</summary>
    public StashValue CachedValue;

    /// <summary>IC state: 0 = uninitialized, 1 = monomorphic, 2 = megamorphic.</summary>
    public byte State;
}
```

**Size:** object reference (8 bytes) + StashValue (24 bytes) + byte (1 byte) + padding = ~40 bytes per slot. For a typical script with 50-100 GetField sites, that's 2-4 KB — negligible.

### 5.2 IC Slot Storage

```csharp
// In Chunk:
public ICSlot[]? ICSlots { get; internal set; }

// Populated lazily on first GetFieldIC execution,
// or eagerly during chunk finalization
```

**Why on Chunk, not on VM?** Each chunk has its own set of GetField call sites with their own IC indices. Sharing IC slots across VMs that execute the same chunk (e.g., parallel execution, module imports) is fine because:

- For frozen namespaces: the cached value is the same regardless of which VM executes
- For struct instances: the guard checks the StashStruct reference, which is the same across VMs when they share the same struct definition

> **Thread safety:** `ICSlot` writes are not atomic (the struct is >8 bytes). For parallel VMs sharing the same chunk, we need either:
>
> - Per-VM IC slot arrays (copy on first access)
> - Accept benign races (worst case: a stale read falls back to full lookup)
>
> **Decision:** Accept benign races. The guard check catches stale reads, and the fallback is the same code path that runs without IC. No correctness risk — only a performance miss on the rare race.

### 5.3 IC Encoding in Bytecode

The `GetField` opcode currently uses a u16 operand that indexes into the constant pool (to get the field name string). For IC-enabled field access, we need an additional IC slot index.

**Option A: New opcode `GetFieldIC` with two u16 operands**
`GetFieldIC <u16: name_idx> <u16: ic_slot_idx>` — 5 bytes total

**Option B: Derive IC slot index from bytecode offset**
Each `GetField` instruction is at a unique bytecode offset. A side-mapping from offset → IC slot index avoids changing the opcode encoding.

**Option C: Use the same u16 as a dual-purpose IC slot index**
The constant pool index for the field name string happens to be unique per GetField site. Use it directly as the IC slot index too.

**Decision: Option A — New `GetFieldIC` opcode.** Reasons:

- Clean separation: `GetField` remains unchanged for non-IC contexts (REPL, eval, debugging)
- The IC slot index can differ from the constant pool index (multiple GetField sites may reference the same field name but have different IC slots)
- The peephole optimizer (from the Superinstructions spec) can convert `GetField` → `GetFieldIC` as a post-pass, or the compiler can emit `GetFieldIC` directly
- 5 bytes vs 3 bytes is a minor code size increase

### 5.4 IC Slot Allocation

The compiler (or peephole optimizer) assigns IC slot indices to `GetFieldIC` instructions. Each `GetFieldIC` in a chunk gets a unique IC slot, allocated sequentially:

```csharp
// In the compiler or peephole optimizer:
int nextICSlot = 0;

// For each GetField that qualifies for IC:
int icSlot = nextICSlot++;
EmitGetFieldIC(nameIdx, icSlot);

// After compilation:
chunk.ICSlots = new ICSlot[nextICSlot];
```

### 5.5 Which GetField Sites Get IC?

Not all `GetField` sites benefit from IC. The compiler should emit `GetFieldIC` for:

- Namespace member access patterns (receiver is a global variable that's a known namespace)
- Struct field access patterns (receiver is a known struct instance)
- Any GetField in a loop body (high iteration count amplifies the benefit)

For simplicity in the first implementation: **emit `GetFieldIC` for ALL `GetField` sites.** The IC overhead for uninitialized/megamorphic sites is a single byte comparison (`State == Monomorphic`) — negligible. This avoids needing heuristics in the compiler.

---

## 6. Monomorphic IC for Namespace Members

This is the highest-value IC target. Namespaces are frozen objects — their member set never changes. The IC guard is a simple **reference equality check** on the namespace object.

### 6.1 Fast Path

```csharp
// Pseudocode for GetFieldIC handler — namespace fast path
ref ICSlot ic = ref chunk.ICSlots[icSlotIdx];
StashValue receiver = Pop();

if (ic.State == 1 && receiver.Tag == StashValueTag.Obj && receiver.AsObj == ic.Guard)
{
    // IC hit: receiver is the same namespace as last time
    Push(ic.CachedValue);
    return;
}

// IC miss: fall through to full lookup, then update IC
```

### 6.2 Guard Validity

For frozen namespaces (`StashNamespace.Freeze()` has been called):

- The namespace object identity is stable (the global variable `math` always points to the same object)
- The member set is immutable (`FrozenDictionary` returns the same values forever)
- **The IC never invalidates.** Once populated, it returns the cached value for the lifetime of the program.

For user-defined namespaces (rare — e.g., `namespace mylib { ... }`):

- If the namespace is mutable, members can be added/changed
- The reference equality guard still catches reassignment of the global variable
- But if the same namespace object has a member _value_ changed, the IC could return stale data
- **Mitigation:** Only cache frozen namespaces. Check `ns.IsFrozen` (or `_frozenMembers != null`) before populating the IC. Mutable namespaces use the uncached path.

### 6.3 What Gets Cached

The `StashValue` returned by `StashNamespace.GetMemberValue` — typically a `StashValue.FromObj(IStashCallable)` wrapping a `BuiltInFunction` or `VMFunction`.

This value is safe to cache because:

- For built-in functions: the `BuiltInFunction` object is stateless and never changes
- For user functions in frozen namespaces: the `VMFunction` object is stable after definition

---

## 7. Monomorphic IC for Struct Instance Fields

Struct field access on `StashInstance` objects is the second IC target. The field _name_ resolves to the same slot in every instance of the same struct type, but the field _value_ differs per instance. IC can skip the dictionary probe but not the value read.

### 7.1 Why Dictionary Probe Is the Bottleneck

`StashInstance._fields` is a `Dictionary<string, StashValue>`. Every `instance.fieldName` call does:

1. Compute `fieldName.GetHashCode()` (string hash — ~10 ns)
2. Probe the hash table (`FindValue` — ~5–15 ns depending on table size)
3. String equality comparison (~2–5 ns)

For a struct with 5 fields, this is ~20 ns per field access. In a loop processing thousands of struct instances, this dominates.

### 7.2 Approach: Indexed Field Access

Instead of caching the resolved `StashValue` (which changes per instance), cache **field existence + optimize the lookup path**:

**Option A: Cache field index in ICSlot**
If `StashInstance._fields` were backed by a `StashValue[]` (indexed by position) instead of a `Dictionary<string, StashValue>`, we could cache the integer index. The IC guard would check the struct type; the fast path would do `instance._fieldSlots[cachedIndex]`.

_This requires changing `StashInstance` storage — significant refactor._

**Option B: Cache validated field name hash**
Store the pre-computed hash code and bucket index for the field name in the IC slot. On IC hit, skip hash computation and jump directly to the bucket.

_Complex and fragile — dictionary internal layout is implementation-dependent._

**Option C: Direct field slot array on StashInstance**
Change `StashInstance` to store fields in a `StashValue[]` alongside the name→index mapping on the `StashStruct` definition. The struct definition owns `Dictionary<string, int> FieldIndices`, and instances use `StashValue[] _fieldSlots`.

_Clean, significant improvement, moderate refactor._

**Decision: Option C — Direct field slot array.** This is the proper IC-compatible field storage. It requires:

1. `StashStruct` gains `Dictionary<string, int> FieldIndices` (or `FrozenDictionary<string, int>`) mapping field names to integer indices
2. `StashInstance._fields` changes from `Dictionary<string, StashValue>` to `StashValue[]`
3. `StashInstance.GetField(name)` does `int idx = Struct.FieldIndices[name]; return _fieldSlots[idx]`
4. IC caches `(StashStruct reference, int fieldIndex)` in the ICSlot
5. On IC hit: verify receiver is `StashInstance` with matching `Struct` reference → `_fieldSlots[cachedIndex]`

### 7.3 IC Fast Path for Struct Fields

```csharp
// Pseudocode
ref ICSlot ic = ref chunk.ICSlots[icSlotIdx];
StashValue receiver = Pop();

if (ic.State == 1 && receiver.Tag == StashValueTag.Obj)
{
    object? rawObj = receiver.AsObj;
    if (rawObj is StashInstance inst && inst.Struct == ic.Guard)
    {
        // IC hit: same struct type — use cached field index
        Push(inst.FieldSlots[ic.CachedFieldIndex]);
        return;
    }
}

// IC miss: full lookup
```

> **Open question:** The ICSlot struct currently stores `CachedValue` (a StashValue). For struct field IC, we need to store an `int fieldIndex` instead. Options:
>
> - Use `CachedValue.AsInt` to store the field index (reuse existing field, tag = Int)
> - Add a separate `int CachedFieldIndex` field to ICSlot
> - Use two different IC slot types (generic but complex)
>
> **Decision:** Store the field index in `CachedValue` as `StashValue.FromInt(fieldIndex)`. This reuses the existing struct layout without adding fields. The handler extracts it via `(int)ic.CachedValue.AsInt`.

### 7.4 StashInstance Refactor Requirements

This optimization requires changing `StashInstance` internals:

```csharp
// Current:
public class StashInstance
{
    private readonly Dictionary<string, StashValue> _fields;
}

// Proposed:
public class StashInstance
{
    public readonly StashValue[] FieldSlots;  // Indexed access
    // StashStruct owns the name→index mapping
}
```

And `StashStruct` gains:

```csharp
public class StashStruct
{
    public FrozenDictionary<string, int> FieldIndices { get; }  // name → slot index
    // Built at struct declaration time
}
```

**This is a separate refactoring story** that benefits IC but also speeds up non-IC field access (array index vs dictionary probe). It should be implemented before or alongside IC.

---

## 8. IC State Machine

Each IC slot transitions through three states:

```
Uninitialized ──(first access)──► Monomorphic ──(type mismatch)──► Megamorphic
     (0)                              (1)                              (2)
```

### 8.1 State Transitions

| Current State | Event                     | Action                            | New State   |
| ------------- | ------------------------- | --------------------------------- | ----------- |
| Uninitialized | First GetFieldIC executed | Perform full lookup, cache result | Monomorphic |
| Monomorphic   | Guard matches (IC hit)    | Return cached value               | Monomorphic |
| Monomorphic   | Guard fails (IC miss)     | Perform full lookup, transition   | Megamorphic |
| Megamorphic   | Any access                | Perform full lookup (no caching)  | Megamorphic |

### 8.2 Why No Polymorphic State?

Classic IC implementations have a polymorphic state that caches 2–4 receiver types. In Stash:

- Namespace access is universally monomorphic (same namespace at each site)
- Struct field access is almost always monomorphic (one struct type per variable)
- The rare polymorphic case (e.g., interface method calls on different struct types) is best handled by the full lookup

Adding polymorphic IC would increase `ICSlot` size and handler complexity for minimal benefit. **Start with monomorphic + megamorphic; add polymorphic only if profiling shows a need.**

### 8.3 Megamorphic Behavior

A megamorphic IC slot is effectively permanent — it never reverts to monomorphic. The full lookup path runs every time. This is the same performance as today's `GetField` (no regression), just without the IC benefit.

In practice, megamorphic sites are rare in Stash code. If a site cycles between two types (e.g., `obj.field` where `obj` alternates between two struct types), it becomes megamorphic on the second miss and stays there. This is acceptable — such sites are already slow today.

---

## 9. VM Handler — GetFieldIC

### 9.1 Opcode Encoding

```
GetFieldIC <u16: name_idx> <u16: ic_slot_idx>
```

Total: 5 bytes (1 opcode + 2 operands of u16 each).

### 9.2 Handler Implementation

```csharp
private void ExecuteGetFieldIC(ref CallFrame frame)
{
    ushort nameIdx = ReadU16(ref frame);
    ushort icSlotIdx = ReadU16(ref frame);
    StashValue objVal = Pop();

    // --- IC fast path ---
    ref ICSlot ic = ref frame.Chunk.ICSlots![icSlotIdx];

    if (ic.State == 1) // Monomorphic
    {
        if (objVal.Tag == StashValueTag.Obj && objVal.AsObj == ic.Guard)
        {
            // Namespace IC hit — direct cached value
            Push(ic.CachedValue);
            return;
        }

        if (objVal.Tag == StashValueTag.Obj && objVal.AsObj is StashInstance inst
            && inst.Struct == ic.Guard)
        {
            // Struct field IC hit — cached field index
            Push(inst.FieldSlots[(int)ic.CachedValue.AsInt]);
            return;
        }

        // Guard mismatch → megamorphic
        ic.State = 2;
    }

    // --- IC slow path (uninitialized or megamorphic) ---
    string fieldName = (string)frame.Chunk.Constants[nameIdx].AsObj!;

    // Namespace fast path (same as current GetField)
    if (objVal.Tag == StashValueTag.Obj)
    {
        object? rawObj = objVal.AsObj;
        if (rawObj is StashNamespace ns)
        {
            StashValue result = ns.GetMemberValue(fieldName, null);

            // Populate IC if uninitialized and namespace is frozen
            if (ic.State == 0 && ns.IsFrozen)
            {
                ic.Guard = rawObj;
                ic.CachedValue = result;
                ic.State = 1;
            }

            Push(result);
            return;
        }

        // Struct instance — populate IC if uninitialized
        if (rawObj is StashInstance inst2)
        {
            StashValue result = inst2.GetFieldAsValue(fieldName, GetCurrentSpan(ref frame));

            if (ic.State == 0 && inst2.Struct is not null
                && inst2.Struct.FieldIndices.TryGetValue(fieldName, out int fieldIdx))
            {
                ic.Guard = inst2.Struct;
                ic.CachedValue = StashValue.FromInt(fieldIdx);
                ic.State = 1;
            }

            Push(result);
            return;
        }
    }

    // General path — no IC for other receiver types
    object? obj = objVal.ToObject();
    Push(StashValue.FromObject(GetFieldValue(obj, fieldName, GetCurrentSpan(ref frame))));
}
```

### 9.3 Performance Characteristics

**IC hit (namespace):** Object tag check (1 compare) + AsObj read (1 load) + reference equality (1 compare) + Push cached StashValue (1 array write). Total: **~4–6 cycles**.

**IC hit (struct field):** Tag check + type test (`is StashInstance`) + reference equality on `Struct` + array index read. Total: **~8–12 cycles**.

**IC miss / megamorphic:** Falls through to the same code path as current `GetField`. No regression.

**Current `GetField` (no IC):** Tag check + type test + `FrozenDictionary.TryGetValue` (hash + probe + equality). Total: **~30–50 cycles**.

**Expected speedup on IC hit: 5–10× per field access.**

---

## 10. Compiler Changes

### 10.1 IC Slot Counter

The compiler (or `ChunkBuilder`) tracks an IC slot counter. Every `GetFieldIC` emission increments it:

```csharp
// In ChunkBuilder:
private int _icSlotCount;

public ushort AllocateICSlot() => (ushort)_icSlotCount++;

// In Build():
chunk.ICSlots = _icSlotCount > 0 ? new ICSlot[_icSlotCount] : null;
```

### 10.2 GetField → GetFieldIC Conversion

**Option A: Compiler emits GetFieldIC directly**
The compiler always emits `GetFieldIC` instead of `GetField`. Simple, no post-pass needed.

**Option B: Peephole optimizer converts GetField → GetFieldIC**
If the Superinstructions peephole optimizer already exists, it can also convert `GetField` → `GetFieldIC` during the same pass.

**Decision: Option A for the first implementation.** Direct compiler emission is simpler and avoids adding another peephole pattern. The compiler already knows when it's emitting a GetField — it just adds an IC slot allocation.

### 10.3 OpCode Addition

```csharp
/// <summary>Get field with inline cache (u16 name_idx, u16 ic_slot_idx).</summary>
GetFieldIC,    // New opcode
```

`OpCodeInfo.OperandSize`: `OpCode.GetFieldIC => 4` (two u16 operands).

### 10.4 Backward Compatibility

`GetField` remains a valid opcode for:

- REPL mode (where IC state would be lost between evaluations)
- Debug evaluation (watch expressions, conditional breakpoints)
- Any context where chunk IC slots are not allocated

---

## 11. Cross-Cutting Concerns

### 11.1 SetField Interaction

`SetField` on a struct instance modifies the instance's field value. This does NOT invalidate the IC because the IC caches the **field index** (which field slot to read), not the **field value**. The value is always read fresh from `inst.FieldSlots[cachedIdx]`.

`SetField` on a namespace (extremely rare — mutable namespace member assignment) WOULD invalidate the IC. But we only populate IC for frozen namespaces, so this case never arises.

### 11.2 Extend Blocks

Extend blocks add methods to existing struct types at runtime. If a GetField IC is populated for a struct's field, and an extend block later adds a method with the same name... this is not an issue because:

1. IC caches field access, not method access
2. Fields take precedence over methods in `StashInstance.GetField`
3. The extend block adds to `StashStruct.Methods`, not to instance fields

However, if a `GetField` site accesses a method (not a field) on a struct, the IC wouldn't cache it (methods go through the non-IC path). This is acceptable — method access on struct instances is less frequent than field access.

### 11.3 Module System

Modules have separate compilation units but may share struct definitions (via imports). IC slots are per-chunk, so each module's chunks have their own IC state. This is correct — different modules may use the same struct type at different GetField sites.

### 11.4 Debugger

When a debugger is attached, IC should still function normally. The cached values are the same values the debugger would see through the normal lookup path. Stepping through code with IC-enabled GetField shows the correct values.

However, **watch expressions** should NOT use `GetFieldIC` — they run in a separate evaluation context that doesn't share chunk IC state. The compiler should emit `GetField` (not `GetFieldIC`) for debug evaluation contexts.

### 11.5 WASM / Playground

No unsafe code. IC slots are plain managed arrays. Fully compatible with Blazor WASM.

### 11.6 REPL

REPL evaluations compile fresh chunks each time. IC slots are allocated per-chunk, so each REPL line gets its own IC state. The IC will populate on first access and benefit repeated accesses within the same REPL-compiled chunk (e.g., a for-loop typed into the REPL).

### 11.7 Interaction with Superinstructions

Superinstructions and IC are orthogonal:

- Superinstructions fuse `LoadLocal + LoadLocal + Add` type patterns — they don't touch `GetField`
- IC optimizes `GetField` lookup — it doesn't affect arithmetic or control flow
- Both can operate on the same code with multiplicative benefits:
  - Superinstructions reduce dispatch count in arithmetic loops
  - IC reduces per-access cost for namespace/field lookups
  - In mixed code (arithmetic + namespace calls), both contribute

The 5-byte encoding of `GetFieldIC` is too large for practical superinstruction fusion (`LoadLocal + GetFieldIC` would be 7 bytes with a u8 + two u16 operands). This is fine — GetField is typically followed by Call, not by another GetField, so fusion opportunities are limited.

---

## 12. Migration Strategy

### 12.1 Phase A — StashInstance Field Slot Refactor (Optional Pre-Step)

1. Add `FrozenDictionary<string, int> FieldIndices` to `StashStruct`, built at struct declaration
2. Change `StashInstance._fields` from `Dictionary<string, StashValue>` to `StashValue[]`
3. Update `StashInstance.GetField`, `SetField`, constructor, serialization
4. Update all tests — behavior is identical, only internal storage changes
5. Benchmark: struct field access should be faster even without IC

> **Note:** This step improves struct field access independently of IC. It can be done as a standalone optimization or as part of the IC implementation.

### 12.2 Phase B — IC Infrastructure

1. Add `ICSlot` struct to `Stash.Bytecode/Bytecode/`
2. Add `ICSlots` property to `Chunk`
3. Add `GetFieldIC` opcode to `OpCode.cs`
4. Add `OperandSize` entry to `OpCodeInfo`
5. Add IC slot allocation to `ChunkBuilder`

### 12.3 Phase C — VM Handler + Compiler

1. Implement `ExecuteGetFieldIC` in `VirtualMachine.Collections.cs`
2. Add `StashNamespace.IsFrozen` property (check `_frozenMembers != null`)
3. Change compiler to emit `GetFieldIC` for all GetField sites (except REPL/debug eval contexts)
4. Update `Disassembler.cs` for `GetFieldIC` opcode display
5. Wire up `GetFieldIC` case in dispatch loop

### 12.4 Phase D — Validation

1. Run full test suite
2. Run all 5 benchmarks — measure improvement
3. Profile with `perf record` — verify `FrozenDictionary.TryGetValue` drops from top
4. Test IC invalidation: megamorphic fallback with polymorphic receivers

### 12.5 What Does NOT Change

| Component                 | Why unchanged                                                 |
| ------------------------- | ------------------------------------------------------------- |
| `SetField` opcode         | IC caches field index, not value — no SetField interaction    |
| `GetField` opcode         | Remains for REPL / debug eval — IC is additive, not replacing |
| Peephole optimizer        | GetFieldIC is emitted by compiler, not by peephole            |
| All non-Bytecode projects | Zero impact                                                   |

---

## 13. Test Strategy

### 13.1 IC Correctness Tests

- **Namespace IC hit:** Compile `math.sqrt(x)` in a loop, verify correct results, verify IC slot transitions to monomorphic
- **Namespace IC — different namespaces at same site:** Compile a function that receives a namespace parameter and accesses `.member` — should go monomorphic then megamorphic
- **Struct field IC hit:** Compile `point.x` in a loop with same struct type, verify correct values for different instances
- **Struct field IC miss:** Pass different struct types to same function parameter, verify megamorphic fallback returns correct values
- **IC never invalidates for frozen namespaces:** Verify IC stays monomorphic indefinitely
- **Mutable namespace — no IC:** Verify that non-frozen namespaces don't populate IC

### 13.2 Edge Cases

- **Null receiver:** `GetFieldIC` on a null value should throw RuntimeError with correct source span
- **Non-object receiver:** GetFieldIC on an int/float/bool should fall through to general path
- **Field not found:** IC miss on a field that doesn't exist should throw RuntimeError
- **Nested field access:** `a.b.c` — two sequential GetFieldIC instructions, each with own IC slot
- **IC in closures:** Lambda captures a variable, accesses field inside — IC on the closure's chunk should work correctly

### 13.3 Benchmark Validation

| Benchmark             | Expected improvement | Reason                                           |
| --------------------- | -------------------: | ------------------------------------------------ |
| Built-in Functions    |               10–15% | Dominated by namespace member lookups            |
| Namespace Calls       |               15–20% | 24 GetField per iteration, all namespace members |
| Algorithms            |                 1–3% | Minimal field access in sort/fibonacci code      |
| Function Calls        |                 2–4% | Some namespace access for test setup             |
| Expression Throughput |                 1–2% | No field access in tight arithmetic loops        |
| Scope Lookup          |                 2–4% | Some namespace access in closure tests           |

### 13.4 Optimizer Toggle

If `--no-optimize` disables IC (emits `GetField` instead of `GetFieldIC`), verify:

1. All tests pass with IC ON
2. All tests pass with IC OFF
3. Results are identical

---

## 14. Risk Register

| Risk                                                                  | Impact                 | Probability | Mitigation                                                                             |
| --------------------------------------------------------------------- | ---------------------- | ----------- | -------------------------------------------------------------------------------------- |
| Stale IC cache returns wrong value                                    | Correctness (critical) | Low         | Guard check on every IC hit; only cache frozen namespaces; struct IC caches index only |
| ICSlot struct size regresses cache performance                        | Performance            | Low         | ICSlot is ~40 bytes; typical chunks have <100 IC slots = <4KB (fits in L1 cache)       |
| Benign race on IC population corrupts state                           | Correctness            | Very Low    | Guard check catches any stale read; worst case is a redundant full lookup              |
| StashInstance refactor breaks JSON serialization                      | Functionality          | Medium      | Update serialization to use FieldIndices mapping; test roundtrip                       |
| GetFieldIC 5-byte encoding increases code size                        | Code size              | Low         | 2 extra bytes per GetField site; typical script has <200 GetField — 400 bytes total    |
| Megamorphic fallback is slower than plain GetField                    | Performance            | Very Low    | Only adds 1 byte comparison (`State == 1`); effectively free on miss                   |
| IC invalidation needed for future features (e.g., namespace mutation) | Design debt            | Medium      | Guard + frozen check is forward-compatible; non-frozen namespaces skip IC              |

---

## 15. Decision Log

| Date       | Decision                                           | Alternatives Considered                           | Rationale                                                                                                                                          |
| ---------- | -------------------------------------------------- | ------------------------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------- |
| 2026-04-08 | Side-table IC slots (not mutable bytecode)         | Mutable bytecode (CPython ADAPTIVE)               | Stash chunks are shared across closures and module imports; mutable bytecode requires copy-on-write or per-instance arrays; side-table is simpler  |
| 2026-04-08 | Monomorphic + megamorphic only (no polymorphic)    | Full mono/poly/mega state machine                 | Namespace access is universally monomorphic; struct access is nearly always monomorphic; polymorphic adds complexity for a case that rarely occurs |
| 2026-04-08 | New GetFieldIC opcode (not repurpose GetField)     | Reuse GetField with optional IC slot              | Clean separation for REPL/debug contexts that don't want IC; IC slot index needs its own u16 operand                                               |
| 2026-04-08 | Compiler emits GetFieldIC directly (not peephole)  | Peephole optimizer converts GetField → GetFieldIC | Simpler; compiler already knows it's emitting GetField; avoids another peephole pattern and opcode size change during optimization                 |
| 2026-04-08 | Accept benign races on IC slots (no locking)       | Per-VM IC slot arrays; atomic IC writes           | Guard check catches stale reads; worst case is redundant full lookup (no correctness risk); locking would negate IC performance benefit            |
| 2026-04-08 | Cache field index (not value) for struct instances | Cache field value (stale on mutation)             | Struct fields change per-instance; caching index is always valid; caching value would require invalidation on SetField                             |
| 2026-04-08 | Only cache frozen namespaces                       | Cache all namespaces                              | Mutable namespaces could have members changed; frozen namespaces are guaranteed stable; all built-in namespaces are frozen                         |
| 2026-04-08 | StashInstance slot array refactor (Option C)       | Hash caching (Option B), no struct IC (skip)      | Array indexing is O(1) with no hash; aligns with how other VMs store object fields; benefits non-IC access too; moderate refactor but high payoff  |

---

## Open Questions

1. **Should StashInstance field slot refactor be a prerequisite or can it be deferred?**
   Without it, struct field IC provides no benefit (still does dictionary lookup). With it, struct field access is faster even without IC. Recommendation: make it Phase A (prerequisite).

2. **Should the IC `IsFrozen` check be on StashNamespace or should we add a global "all namespaces frozen" flag?**
   Individual check is safer and forward-compatible. A global flag is slightly faster (single boolean) but fragile if user-defined namespaces are ever introduced. Recommendation: per-namespace check.

3. **Should SetFieldIC exist too?**
   Setting fields is less common than getting them, and the IC benefit for set is smaller (still needs to write to the slot). Recommendation: defer — measure first.
