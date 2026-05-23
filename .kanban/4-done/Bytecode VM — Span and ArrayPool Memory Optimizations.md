# Bytecode VM — Span<T> and ArrayPool<T> Memory Optimizations

**Status:** Backlog — Analysis Complete
**Created:** 2025-07-05
**Goal:** Reduce GC pressure and heap allocations in the bytecode compilation pipeline and VM execution loop to approach sub-millisecond lex→parse→compile→execute latency for small-to-medium scripts.

---

## Context

This spec catalogs every Span<T>, ArrayPool<T>, stackalloc, and related modern .NET optimization opportunity in the `Stash.Bytecode` project. The project targets .NET 10 with `IsAotCompatible` and `AllowUnsafeBlocks` enabled, so all techniques below are available.

**Existing optimization baseline (already done well):**

- `StashValue` is a `readonly struct` tagged union — zero per-value allocation
- `SourceSpan` is a `readonly record struct` — stack-allocated
- `CallFrame` is a struct accessed by `ref` — no boxing
- VM stack (`StashValue[]`) and frames (`CallFrame[]`) are flat arrays
- `Push`/`Pop`/`Peek` use `AggressiveInlining`
- Integer fast-paths on arithmetic opcodes avoid `RuntimeOps` dispatch
- Only 1 existing `AsSpan()` usage in `VMContext.cs` L62 (tilde expansion)
- Zero `ArrayPool<T>`, zero `stackalloc` usage in the entire project

---

## Opportunity 1 — ChunkBuilder: ArrayPool for Bytecode Buffer

**Priority: HIGH | Impact: Compilation phase | Affects: Every script**

### Current Code

```csharp
// ChunkBuilder.cs
private readonly List<byte> _code = new();
// ... hundreds of _code.Add() calls during compilation ...
public Chunk Build() => new Chunk(code: _code.ToArray(), ...);
```

`List<byte>` starts at capacity 0, grows through 4→8→16→32→... doublings. Every doubling allocates a new `byte[]` and copies. The final `ToArray()` allocates yet another copy.

### Proposed Fix

```csharp
private byte[] _code = ArrayPool<byte>.Shared.Rent(256);
private int _codeCount;

public void Emit(OpCode opCode)
{
    EnsureCodeCapacity(1);
    _code[_codeCount++] = (byte)opCode;
}

public void Emit(OpCode opCode, ushort operand)
{
    EnsureCodeCapacity(3);
    _code[_codeCount++] = (byte)opCode;
    _code[_codeCount++] = (byte)(operand >> 8);
    _code[_codeCount++] = (byte)(operand & 0xFF);
}

[MethodImpl(MethodImplOptions.AggressiveInlining)]
private void EnsureCodeCapacity(int needed)
{
    if (_codeCount + needed > _code.Length)
        GrowCode();
}

private void GrowCode()
{
    byte[] newBuf = ArrayPool<byte>.Shared.Rent(_code.Length * 2);
    _code.AsSpan(0, _codeCount).CopyTo(newBuf);
    ArrayPool<byte>.Shared.Return(_code);
    _code = newBuf;
}

public Chunk Build()
{
    byte[] code = _code.AsSpan(0, _codeCount).ToArray(); // exact-sized final copy
    ArrayPool<byte>.Shared.Return(_code);
    _code = null!; // prevent reuse after Build
    return new Chunk(code: code, ...);
}
```

### Impact

- Eliminates all intermediate `byte[]` allocations during compilation
- The rented buffer is returned to the pool and reused by subsequent compilations
- `List<byte>` overhead includes internal array + count + version — all eliminated
- Jump patching (`_code[patchOffset] = ...`) works identically on raw arrays

### Risks

- Must ensure `Build()` is always called (or add `IDisposable` for safety)
- `EmitJump` and `PatchJump` index directly — works fine with raw arrays
- `CurrentOffset` becomes `_codeCount` instead of `_code.Count`

---

## Opportunity 2 — ChunkBuilder: Dictionary-Based Constant Deduplication

**Priority: HIGH | Impact: Compilation phase | Affects: Scripts with many constants**

### Current Code

```csharp
// ChunkBuilder.cs — AddConstant
for (int i = 0; i < _constants.Count; i++)
{
    StashValue existing = _constants[i];
    if (existing.Tag == value.Tag)
    {
        bool match = value.Tag switch { ... };
        if (match) return (ushort)i;
    }
}
```

This is O(n) per constant addition. For a script with 500 unique constants, the total work is O(n²/2) = ~125,000 comparisons. Real scripts with heavy string usage can have 200+ constants easily.

### Proposed Fix

```csharp
// Alongside the List<StashValue>:
private readonly Dictionary<StashValue, ushort> _constantIndex = new(StashValueComparer.Instance);

public ushort AddConstant(StashValue value)
{
    // Only deduplicate primitives and strings (not arbitrary objects)
    if (value.Tag != StashValueTag.Obj || value.AsObj is string)
    {
        if (_constantIndex.TryGetValue(value, out ushort existing))
            return existing;
    }

    if (_constants.Count > ushort.MaxValue)
        throw new InvalidOperationException("Constant pool overflow.");

    ushort index = (ushort)_constants.Count;
    _constants.Add(value);

    if (value.Tag != StashValueTag.Obj || value.AsObj is string)
        _constantIndex[value] = index;

    return index;
}
```

Requires a simple `IEqualityComparer<StashValue>`:

```csharp
private sealed class StashValueComparer : IEqualityComparer<StashValue>
{
    public static readonly StashValueComparer Instance = new();

    public bool Equals(StashValue x, StashValue y)
    {
        if (x.Tag != y.Tag) return false;
        return x.Tag switch
        {
            StashValueTag.Null => true,
            StashValueTag.Bool => x.AsBool == y.AsBool,
            StashValueTag.Int => x.AsInt == y.AsInt,
            StashValueTag.Float => x.AsFloat == y.AsFloat,
            StashValueTag.Obj => object.Equals(x.AsObj, y.AsObj),
            _ => false,
        };
    }

    public int GetHashCode(StashValue v) => v.Tag switch
    {
        StashValueTag.Null => 0,
        StashValueTag.Bool => v.AsBool ? 1 : 0,
        StashValueTag.Int => v.AsInt.GetHashCode(),
        StashValueTag.Float => v.AsFloat.GetHashCode(),
        StashValueTag.Obj => v.AsObj?.GetHashCode() ?? 0,
        _ => 0,
    };
}
```

### Impact

- Constant lookup goes from O(n) to O(1) amortized
- For 500 constants: ~125,000 comparisons → ~500 hash lookups
- Dictionary itself is a one-time allocation per compilation

### Risks

- None significant. The `Dictionary` trades ~minimal memory for major CPU savings.

---

## Opportunity 3 — RuntimeOps.Interpolate: ValueStringBuilder / stackalloc

**Priority: HIGH | Impact: Every `$"..."` expression at runtime | Affects: Most scripts**

### Current Code

```csharp
// RuntimeOps.cs L491
public static string Interpolate(StashValue[] stack, int sp, int count)
{
    var sb = new StringBuilder();          // ← heap alloc + internal char[]
    int start = sp - count;
    for (int i = start; i < sp; i++)
    {
        sb.Append(RuntimeValues.Stringify(stack[i].ToObject()));
    }
    return sb.ToString();                  // ← another string alloc
}
```

Every string interpolation expression allocates a `StringBuilder` (which internally allocates a `char[16]` minimum), appends to it, then calls `ToString()` which allocates the final string. For a typical `$"Hello, {name}!"` with 3 parts, that's: 1 `StringBuilder` + 1 `char[16]` + 1 `string` = 3 allocations per interpolation.

### Proposed Fix — stackalloc for small interpolations

```csharp
public static string Interpolate(StashValue[] stack, int sp, int count)
{
    if (count == 1)
    {
        // Single-part: stringify directly — no builder needed
        return RuntimeValues.Stringify(stack[sp - 1].ToObject());
    }

    // Estimate total length from parts
    // For small counts, use stack-allocated buffer
    Span<char> stackBuf = stackalloc char[256];
    var vsb = new ValueStringBuilder(stackBuf);

    int start = sp - count;
    for (int i = start; i < sp; i++)
    {
        vsb.Append(RuntimeValues.Stringify(stack[i].ToObject()));
    }

    string result = vsb.ToString();
    vsb.Dispose(); // returns rented buffer if it grew beyond stackalloc
    return result;
}
```

This requires adding a `ValueStringBuilder` type (a well-known pattern from the .NET runtime itself). It uses `stackalloc` for the initial buffer and only falls back to `ArrayPool<char>` if the string grows beyond the stack buffer.

### Impact

- Eliminates `StringBuilder` heap allocation on every string interpolation
- For strings ≤ 256 chars (the vast majority): zero heap allocations besides the final string
- String interpolation is extremely common in sysadmin scripts (`$"Deploying {name} to {server}..."`)

### Risks

- Requires implementing `ValueStringBuilder` (a `ref struct`). Can copy from `dotnet/runtime` internal implementation — it's ~100 lines.

---

## Opportunity 4 — VM Stack/Frame Growth: ArrayPool Instead of Array.Resize

**Priority: MEDIUM | Impact: Deep recursion / large scripts | Affects: Edge cases mainly**

### Current Code

```csharp
// VirtualMachine.cs
private void GrowStack()
{
    Array.Resize(ref _stack, _stack.Length * 2);
    foreach (Upvalue uv in _openUpvalues)
        uv.UpdateStack(_stack);
}

// PushFrame
if (_frameCount >= _frames.Length)
    Array.Resize(ref _frames, _frames.Length * 2);
```

`Array.Resize` allocates a new array and copies. The old array becomes garbage.

### Proposed Fix

```csharp
private void GrowStack()
{
    int newSize = _stack.Length * 2;
    StashValue[] newStack = ArrayPool<StashValue>.Shared.Rent(newSize);
    _stack.AsSpan(0, _sp).CopyTo(newStack);

    // Clear old references beyond _sp to help GC
    if (_stack.Length > 0)
        ArrayPool<StashValue>.Shared.Return(_stack, clearArray: true);

    _stack = newStack;
    foreach (Upvalue uv in _openUpvalues)
        uv.UpdateStack(_stack);
}
```

### Impact

- Moderate. Stack growth is rare (default 1024 slots handles most scripts). When it does happen, the old array is pooled instead of becoming GC garbage.
- More impactful for the `CallFrame[]` growth which starts at only 256 slots.

### Risks

- `ArrayPool<T>.Shared.Rent(n)` may return a buffer larger than `n`. This is fine — the VM uses `_sp` and `_frameCount` as length bounds, not `_stack.Length`.
- **Must call `Return` with `clearArray: true`** for `StashValue[]` since it holds `object?` references in `_obj`. Without clearing, pooled arrays would root dead objects.
- For `CallFrame[]`, same concern — `Chunk`, `Upvalues`, `ModuleGlobals` are references that must be cleared.

---

## Opportunity 5 — Debug Line Tracking: stackalloc / ArrayPool

**Priority: MEDIUM | Impact: Every VM execution with debugger attached | Affects: DAP sessions**

### Current Code

```csharp
// VirtualMachine.Dispatch.cs L69
int[] lastDebugLinePerFrame = new int[DefaultFrameDepth]; // DefaultFrameDepth = 256
Array.Fill(lastDebugLinePerFrame, -1);
```

This allocates a 256-element `int[]` (1 KB) on every `RunInner()` call. When no debugger is attached, this is still allocated but never read.

### Proposed Fix

```csharp
private object? RunInner(int targetFrameCount = 0)
{
    IDebugger? debugger = _debugger;
    int[]? lastDebugLinePerFrame = null;
    if (debugger is not null)
    {
        lastDebugLinePerFrame = ArrayPool<int>.Shared.Rent(DefaultFrameDepth);
        lastDebugLinePerFrame.AsSpan(0, DefaultFrameDepth).Fill(-1);
    }

    try
    {
        // ... dispatch loop ...
    }
    finally
    {
        if (lastDebugLinePerFrame is not null)
            ArrayPool<int>.Shared.Return(lastDebugLinePerFrame);
    }
}
```

### Impact

- Eliminates 1 KB allocation per `RunInner()` call when no debugger is attached (the common case)
- When debugger IS attached, the array is pooled instead of becoming garbage

### Risks

- The `Array.Resize` on line 87 also needs to use `ArrayPool`. Straightforward.

---

## Opportunity 6 — ExecuteCommand: Reduce StringBuilder and List Allocations

**Priority: MEDIUM | Impact: Every `$(...)` command expression | Affects: Sysadmin scripts heavily**

### Current Code

```csharp
// VirtualMachine.Strings.cs L23
var sb = new StringBuilder();
int partStart = _sp - cmdMetadata.PartCount;
for (int i = partStart; i < _sp; i++)
{
    sb.Append(RuntimeOps.Stringify(_stack[i]));
}
_sp = partStart;
string command = _context.ExpandTilde(sb.ToString().Trim());
```

And later the elevation prefix path:

```csharp
var prefixedArgs = new List<string>(arguments.Count + 1) { program };
prefixedArgs.AddRange(arguments);
```

### Proposed Fix

Use `ValueStringBuilder` for command assembly (same as Opportunity 3):

```csharp
Span<char> stackBuf = stackalloc char[256];
var vsb = new ValueStringBuilder(stackBuf);
int partStart = _sp - cmdMetadata.PartCount;
for (int i = partStart; i < _sp; i++)
{
    vsb.Append(RuntimeOps.Stringify(_stack[i]));
}
_sp = partStart;

ReadOnlySpan<char> trimmed = vsb.AsSpan().Trim();
string command = _context.ExpandTilde(trimmed.ToString());
vsb.Dispose();
```

### Impact

- Eliminates `StringBuilder` allocation on every command execution
- Shell commands are extremely common in Stash's target use case (sysadmin scripts)

### Risks

- `CommandParser.Parse()` takes a `string`, so we still need the final `.ToString()` — but we avoid the intermediate `StringBuilder` allocation

---

## Opportunity 7 — String Concatenation in Add(): Avoid Double Stringify

**Priority: MEDIUM | Impact: Every `+` on strings | Affects: String-heavy code**

### Current Code

```csharp
// RuntimeOps.cs L53
if (lObj is string || rObj is string)
{
    return StashValue.FromObj(Stringify(left) + Stringify(right));
}
```

When one side is already a string, `Stringify()` still calls `RuntimeValues.Stringify()` which does a type check and returns the same string. The `+` operator then calls `string.Concat(string, string)` which allocates the result.

### Proposed Fix

```csharp
if (lObj is string ls && rObj is string rs)
{
    // Both strings: direct concat, no Stringify overhead
    return StashValue.FromObj(string.Concat(ls, rs));
}
if (lObj is string ls2)
{
    return StashValue.FromObj(string.Concat(ls2, Stringify(right)));
}
if (rObj is string rs2)
{
    return StashValue.FromObj(string.Concat(Stringify(left), rs2));
}
```

### Impact

- Avoids unnecessary `Stringify()` call and its virtual dispatch when the value is already a string
- `string.Concat(string, string)` is heavily optimized in the runtime for exactly 2 args

### Risks

- None. This is a pure fast-path addition.

---

## Opportunity 8 — String Repeat: Use string.Create Instead of Enumerable.Repeat

**Priority: LOW | Impact: `"abc" * n` expressions | Affects: Rare usage pattern**

### Current Code

```csharp
// RuntimeOps.cs L136
return StashValue.FromObj(string.Concat(Enumerable.Repeat(ls, (int)right.AsInt)));
```

`Enumerable.Repeat` allocates an iterator, then `string.Concat(IEnumerable<string>)` allocates a `StringBuilder` internally.

### Proposed Fix

```csharp
return StashValue.FromObj(string.Create(ls.Length * (int)right.AsInt, (ls, (int)right.AsInt), static (span, state) =>
{
    ReadOnlySpan<char> src = state.ls;
    for (int i = 0; i < state.Item2; i++)
    {
        src.CopyTo(span[(i * src.Length)..]);
    }
}));
```

### Impact

- Eliminates `IEnumerable` iterator allocation + internal `StringBuilder`
- Single allocation for the result string with exact-size buffer

### Risks

- `string.Create` requires the length upfront — with very large counts, `ls.Length * count` could overflow. Guard with a checked multiply.

---

## Opportunity 9 — CompilerScope: Avoid Repeated Array Allocations

**Priority: LOW | Impact: Compilation phase | Affects: Functions with many locals**

### Current Code

```csharp
// CompilerScope.cs L134, L146
public string[] GetLocalNames()
{
    var names = new string[_locals.Count];
    for (int i = 0; i < _locals.Count; i++)
        names[i] = _locals[i].Name;
    return names;
}
```

These methods are called at `Build()` time to snapshot local metadata. Each call allocates a fresh array.

### Proposed Fix

These are called once per function compilation, so the allocation is proportional to function count × local count. For most scripts this is negligible. However, `GetPeakLocalNames()` and `GetPeakLocalIsConst()` could use `ArrayPool<string>` if profiling shows they're a bottleneck.

### Decision

**DEFER.** The compilation-time cost of these allocations is trivial compared to runtime dispatch. Only revisit if profiles show compilation taking >10% of end-to-end time.

---

## Opportunity 10 — IStashCallable Call: Args List Allocation

**Priority: MEDIUM-HIGH | Impact: Every built-in function call | Affects: All stdlib usage**

### Current Code

```csharp
// VirtualMachine.Functions.cs L276
var args = new List<object?>(argc);
int argStart = _sp - argc;
for (int i = argStart; i < _sp; i++)
{
    args.Add(_stack[i].ToObject());
}
_sp = argStart - 1;
object? result = callable.Call(_context, args);
```

Every single built-in function call (`arr.push`, `str.len`, `io.println`, etc.) allocates a `List<object?>`. The `IStashCallable.Call` signature takes `List<object?>`. This is arguably the single highest-frequency allocation in the entire VM for typical Stash scripts.

### Proposed Fix

This requires an interface change. Two options:

**Option A — ReadOnlySpan overload (breaking change to IStashCallable):**

```csharp
// Change IStashCallable.Call to accept ReadOnlySpan<StashValue>
object? Call(VMContext context, ReadOnlySpan<StashValue> args);
```

Then the VM can pass a slice of the stack directly:

```csharp
ReadOnlySpan<StashValue> args = _stack.AsSpan(argStart, argc);
_sp = argStart - 1;
object? result = callable.Call(_context, args);
```

This eliminates the `List` allocation AND the per-element `ToObject()` boxing entirely.

**Option B — Reusable list (non-breaking):**

```csharp
// VM field:
private readonly List<object?> _callArgs = new(16);

// In dispatch:
_callArgs.Clear();
for (int i = argStart; i < _sp; i++)
    _callArgs.Add(_stack[i].ToObject());
object? result = callable.Call(_context, _callArgs);
```

One `List` allocation per VM lifetime instead of per call.

### Decision

**Option B is pragmatic for now** — it's non-breaking and eliminates ~95% of the allocation cost. Option A is the ideal long-term target but requires touching every `IStashCallable` implementation (all ~200+ built-in functions and any user-defined callables).

### Impact

- Option B: Eliminates potentially thousands of `List<object?>` allocations per script execution
- Option A (future): Also eliminates all `ToObject()` boxing for numeric args

### Risks

- Option B: `_callArgs` is shared state — must not be retained by callables (they shouldn't be, but verify). If a callable stores the args list, it would see stale data on the next call. Most stdlib functions consume args immediately.
- Option B is NOT safe across async boundaries without additional guardrails.

---

## Implementation Priority

| #   | Opportunity                      | Priority | Phase | Rationale                                                       |
| --- | -------------------------------- | -------- | ----- | --------------------------------------------------------------- |
| 3   | Interpolate: ValueStringBuilder  | HIGH     | 1     | Highest frequency runtime allocation. Every `$"..."` hits this. |
| 10  | Callable args: Reusable list     | MED-HIGH | 1     | Every built-in call allocates a list. Thousands per script.     |
| 2   | Constant dedup: Dictionary       | HIGH     | 1     | O(n²) → O(n). Pure compilation win, zero runtime risk.          |
| 1   | ChunkBuilder: ArrayPool for code | HIGH     | 1     | Eliminates all list growth allocations during compilation.      |
| 7   | Add string fast-path             | MEDIUM   | 1     | Trivial change, avoids unnecessary Stringify.                   |
| 6   | Command: ValueStringBuilder      | MEDIUM   | 2     | Same pattern as #3. Benefits shell-heavy scripts.               |
| 5   | Debug line tracking: lazy alloc  | MEDIUM   | 2     | 1 KB wasted when no debugger. Easy fix.                         |
| 4   | Stack/Frame: ArrayPool           | MEDIUM   | 2     | Rare growth, but good hygiene.                                  |
| 8   | String repeat: string.Create     | LOW      | 3     | Rare operation. Nice-to-have.                                   |
| 9   | CompilerScope arrays             | LOW      | DEFER | Negligible vs runtime costs.                                    |

## Prerequisites

- **ValueStringBuilder:** Must be implemented as a shared utility (in `Stash.Common` or `Stash.Bytecode/Util/`). Can be adapted from the [dotnet/runtime internal implementation](https://github.com/dotnet/runtime/blob/main/src/libraries/Common/src/System/Text/ValueStringBuilder.cs). This is a `ref struct` that uses `stackalloc` + `ArrayPool<char>` fallback.
- **Phase 1 items are independent** of each other and can be implemented in parallel.

## Cross-Cutting Concerns

- **AOT Compatibility:** All proposed changes use value types, arrays, and `ArrayPool` — fully AOT-compatible. No reflection.
- **Thread Safety:** `ArrayPool<T>.Shared` is thread-safe. Reusable `_callArgs` list (Opportunity 10) must not cross async boundaries.
- **Debugger Impact:** Opportunity 5 changes debug behavior — verify DAP test suite passes.
- **LSP/DAP:** No impact on protocol. These optimizations are internal to the VM.
- **Benchmark Tracking:** Recommend running `benchmarks/bench_*.stash` before and after Phase 1 to measure real-world impact.

## Measurement Strategy

Before implementing, capture baseline measurements:

1. `dotnet run --project Stash.Cli/ -- benchmarks/bench_algorithms.stash` — exercises string ops, loops, function calls
2. `dotnet run --project Stash.Cli/ -- benchmarks/bench_function_calls.stash` — exercises callable dispatch (Opportunity 10)
3. `dotnet run --project Stash.Cli/ -- benchmarks/bench_namespace_calls.stash` — exercises built-in namespace calls
4. `dotnet run --project Stash.Cli/ -- benchmarks/bench_lexer_heavy.stash` — exercises string interpolation

Use `dotnet-counters` or `dotnet-trace` to capture GC Gen0/Gen1 collection counts and allocation rates before/after.
