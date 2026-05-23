# IStashCallable — StashValue-Native Interface

**Status:** Backlog — Design Phase
**Created:** 2026-04-07
**Category:** Performance / Architecture
**Estimated Impact:** 10-20% improvement on Built-in Functions benchmark, reduced GC pressure across all workloads

---

## 1. Problem Statement

Every built-in function call in the bytecode VM crosses a boxing boundary:

```
VM stack (StashValue[])
  → ToObject() per arg  (boxes long/double/bool → object)
  → List<object?> construction
  → IStashCallable.Call(ctx, List<object?>)
  → Args.Long/Double/Bool/String/... extraction (pattern-match + unbox)
  → ... function body ...
  → return object? (box result)
  → StashValue.FromObject() (pattern-match + unbox)
```

For the Built-in Functions benchmark (2.8M calls, mostly `math.*`, `conv.*`, `str.*` with 1-2 numeric args), this means:

- **~5.6M boxing allocations** (2 args × 2.8M calls, most are `long` or `double`)
- **~2.8M result boxing allocations** (return values)
- **~8.4M unnecessary type checks** in `Args.*` helpers (the VM already knew the types from `StashValue.Tag`)
- **List pooling overhead** (Clear/refill per call, even though the pooling itself is optimized)

The current interface was designed for the tree-walk interpreter, which used `object?` throughout. The tree-walk interpreter has been removed. The bytecode VM is the sole execution engine, and its native value type is `StashValue`. The interface should reflect this.

---

## 2. Proposed Design

### 2.1 New Interface Method

Add a new method to `IStashCallable` that operates on `ReadOnlySpan<StashValue>`:

```csharp
// IStashCallable.cs — new method with default implementation
public interface IStashCallable
{
    // Existing (preserved for backward compatibility during migration)
    object? Call(IInterpreterContext context, List<object?> arguments);

    // New: zero-allocation call path for the bytecode VM
    StashValue CallDirect(IInterpreterContext context, ReadOnlySpan<StashValue> arguments)
    {
        // Default implementation: convert to List<object?> and delegate to Call()
        var list = new List<object?>(arguments.Length);
        foreach (StashValue sv in arguments)
            list.Add(sv.ToObject());
        return StashValue.FromObject(Call(context, list));
    }

    // ... existing members unchanged ...
}
```

**Why `ReadOnlySpan<StashValue>` instead of `List<StashValue>`:**

- Zero allocation — the span points directly into the VM's `_stack` array
- No copying — args are read in-place from `_stack[argStart..argStart+argc]`
- `Span` naturally conveys "these are borrowed values, don't store them"
- Aligns with the VM's existing stack-based architecture

**Why `StashValue` return instead of `object?`:**

- Eliminates the `FromObject()` pattern-match + potential boxing on return
- Built-ins that return `long`, `double`, or `bool` can return `StashValue.FromInt/Float/Bool` directly — no boxing

### 2.2 VM Dispatch Change

In `VirtualMachine.Functions.cs`, the `CallValue()` IStashCallable section becomes:

```csharp
if (callee is IStashCallable callable)
{
    // ... arity checking (same as today) ...

    int argStart = _sp - argc;

    // Pass a span directly into the VM stack — zero allocation, zero copying
    ReadOnlySpan<StashValue> argSpan = _stack.AsSpan(argStart, argc);

    _sp = argStart - 1; // pop args + callee slot

    StashValue result;
    try
    {
        if (callSpan is not null) _context.CurrentSpan = callSpan;
        result = callable.CallDirect(_context, argSpan);
    }
    catch (Exception ex) when (ex is not RuntimeError and not Stash.Tpl.TemplateException)
    {
        throw new RuntimeError($"Built-in function error: {ex.Message}",
            callSpan ?? _context.CurrentSpan);
    }
    Push(result);  // No FromObject() needed — already a StashValue
    return;
}
```

**Eliminated per-call:**

- `List<object?>` allocation/pooling + `Clear()`
- N × `ToObject()` boxing
- 1 × `FromObject()` pattern match
- `args.Add()` loop

### 2.3 BuiltInFunction Changes

`BuiltInFunction` wraps the new delegate type:

```csharp
public class BuiltInFunction : IStashCallable
{
    // New delegate type — takes span of StashValues, returns StashValue
    public delegate StashValue DirectHandler(IInterpreterContext context, ReadOnlySpan<StashValue> args);

    private readonly DirectHandler? _directBody;
    private readonly Func<IInterpreterContext, List<object?>, object?>? _legacyBody;

    // New constructor for StashValue-native built-ins
    public BuiltInFunction(string name, int arity, DirectHandler body) { ... }

    // Old constructor preserved for migration period
    public BuiltInFunction(string name, int arity, Func<IInterpreterContext, List<object?>, object?> body) { ... }

    public StashValue CallDirect(IInterpreterContext context, ReadOnlySpan<StashValue> arguments)
    {
        if (_directBody is not null)
            return _directBody(context, arguments);

        // Legacy fallback: convert span → List<object?>, call old body, convert result
        var list = new List<object?>(arguments.Length);
        foreach (StashValue sv in arguments)
            list.Add(sv.ToObject());
        return StashValue.FromObject(_legacyBody!(context, list));
    }

    // Keep old Call() for non-VM callers (BuiltInBoundMethod, tests, etc.)
    public object? Call(IInterpreterContext context, List<object?> arguments)
    {
        return _legacyBody is not null
            ? _legacyBody(context, arguments)
            : CallDirect(context, /* convert List to span */).ToObject();
    }
}
```

### 2.4 NamespaceBuilder Changes

Add a new `Function()` overload that accepts the direct handler:

```csharp
public NamespaceBuilder Function(string name, BuiltInParam[] parameters,
    BuiltInFunction.DirectHandler body,
    string? returnType = null, bool isVariadic = false, string? documentation = null)
{
    int arity = isVariadic ? -1 : parameters.Length;
    string qualifiedName = string.IsNullOrEmpty(_name) ? name : $"{_name}.{name}";
    _namespace.Define(name, new BuiltInFunction(qualifiedName, arity, body));
    _functions.Add(new NamespaceFunction(_name, name, parameters, returnType, isVariadic, documentation));
    return this;
}
```

The old overload accepting `Func<IInterpreterContext, List<object?>, object?>` is preserved.

### 2.5 Args Helper Migration — `SvArgs` Class

A new `SvArgs` static class mirrors the existing `Args` but operates on `ReadOnlySpan<StashValue>`:

```csharp
public static class SvArgs
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static long Long(ReadOnlySpan<StashValue> args, int index, string funcName)
    {
        if (args[index].IsInt) return args[index].AsInt;
        if (args[index].IsFloat) return (long)args[index].AsFloat;
        throw new RuntimeError($"First argument to '{funcName}' must be a number.");
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double Double(ReadOnlySpan<StashValue> args, int index, string funcName)
    {
        if (args[index].IsFloat) return args[index].AsFloat;
        if (args[index].IsInt) return (double)args[index].AsInt;
        throw new RuntimeError($"...");
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static string String(ReadOnlySpan<StashValue> args, int index, string funcName)
    {
        if (args[index].IsObj && args[index].AsObj is string s) return s;
        throw new RuntimeError($"...");
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool Bool(ReadOnlySpan<StashValue> args, int index, string funcName)
    {
        if (args[index].IsBool) return args[index].AsBool;
        throw new RuntimeError($"...");
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static List<object?> List(ReadOnlySpan<StashValue> args, int index, string funcName)
    {
        if (args[index].IsObj && args[index].AsObj is List<object?> l) return l;
        throw new RuntimeError($"...");
    }

    // ... Dict, Callable, Instance, IpAddress, Future, etc. — same pattern ...
    // Reference types: extract via args[index].AsObj pattern match
    // Value types: extract via direct tag check + AsInt/AsFloat/AsBool
}
```

**Key benefit:** No boxing for `long`, `double`, `bool` extraction. The value is read directly from the `StashValue` union without going through `object?`.

### 2.6 InvokeCallback Changes

The `InvokeCallback` method and the 39 callback sites in stdlib are the most complex part. The callbacks construct `new List<object?> { ... }` with already-boxed values (from the built-in's own computation).

**Approach:** Add a new `InvokeCallback` overload that accepts `ReadOnlySpan<StashValue>`:

```csharp
// On IInterpreterContext:
StashValue InvokeCallback(IStashCallable callable, ReadOnlySpan<StashValue> args)
{
    // Default: convert to List<object?> and use old path
    var list = new List<object?>(args.Length);
    foreach (var sv in args) list.Add(sv.ToObject());
    return StashValue.FromObject(InvokeCallback(callable, list));
}
```

```csharp
// On VMContext — optimized for same-thread VMFunction:
public StashValue InvokeCallback(IStashCallable callable, ReadOnlySpan<StashValue> args)
{
    if (callable is VMFunction vmFn && ActiveVM != null
        && Thread.CurrentThread.ManagedThreadId == MainThreadId)
    {
        return ActiveVM.ExecuteVMFunctionInlineDirect(vmFn, args, null);
    }
    // fallback to boxing path for non-VMFunction or cross-thread
    ...
}
```

Migrated built-in call sites become:

```csharp
// Before:
ctx.InvokeCallback(fn, new List<object?> { item });

// After (for hot paths):
Span<StashValue> cbArgs = stackalloc StashValue[1];
cbArgs[0] = StashValue.FromObject(item);  // item is already object? from list iteration
ctx.InvokeCallback(fn, cbArgs);
```

**Note:** Many callback args are already `object?` (e.g., elements from `List<object?>` being iterated). For these, the conversion to `StashValue` via `FromObject()` is still needed. The real win is small: skipping the list allocation and the round-trip through `Call()`. This optimization is **lower priority** than the direct call path for simple built-ins.

---

## 3. Migration Strategy

### Phase 1: Infrastructure (non-breaking)

Add the new types and interfaces without changing any existing behavior:

1. Move `StashValue` + `StashValueTag` from `Stash.Bytecode/Runtime/` to `Stash.Core/Runtime/`
2. Add `CallDirect()` to `IStashCallable` with a default implementation that delegates to `Call()`
3. Add `BuiltInFunction.DirectHandler` delegate and dual-constructor `BuiltInFunction`
4. Add `SvArgs` static class in `Stash.Stdlib/`
5. Add new `NamespaceBuilder.Function()` overload for `DirectHandler`
6. Change VM's `CallValue()` IStashCallable section to call `CallDirect()` instead of `Call()`

**Critical dependency:** The project reference graph is strictly one-directional:

```
Stash.Core          (zero project references)
    ↑
Stash.Stdlib        (references Core only)
    ↑
Stash.Bytecode      (references Core, Stdlib, Tpl)
    ↑
Stash.Cli           (references Bytecode, Core, Tap)
```

`IStashCallable` and `BuiltInFunction` are in `Stash.Core`. `StashValue` is in `Stash.Bytecode`. `NamespaceBuilder` is in `Stash.Stdlib`. Neither Core nor Stdlib can see Bytecode types.

Options:
- **(a)** Move `StashValue` + `StashValueTag` to `Stash.Core` — The tagged union becomes a core value representation type visible to all layers. `IStashCallable` gains `CallDirect()` directly. `NamespaceBuilder` gets the new overload. Clean and simple.
- **(b)** Create `Stash.Common` shared project — New project with `StashValue`, referenced by Core and Bytecode. Clean separation but adds project complexity.
- **(c)** Wrapper class in `Stash.Bytecode` — Define `IDirectCallable` and `DirectBuiltInFunction` in Bytecode. VM wraps `BuiltInFunction` at dispatch time. Avoids moving types but adds runtime overhead and complexity.
- **(d)** Separate `IDirectCallable` in Bytecode implemented by `BuiltInFunction` — **IMPOSSIBLE.** `BuiltInFunction` (Core) cannot implement an interface from Bytecode (no reverse reference).

**Recommendation:** Option **(a)** — Move `StashValue` to `Stash.Core`. See §5.1 for full tradeoff analysis.

### Phase 2: Hot-path namespaces (measurable impact)

Migrate the highest-frequency built-in functions to `DirectHandler`:

| Priority | Namespace | Functions                                                             | Call frequency in benchmark | Est. impact                                                  |
| -------- | --------- | --------------------------------------------------------------------- | --------------------------- | ------------------------------------------------------------ |
| P0       | `math`    | `abs`, `ceil`, `floor`, `round`, `sqrt`, `pow`, `min`, `max`, `clamp` | ~900K/benchmark             | High — all-numeric args, numeric return                      |
| P0       | `conv`    | `toStr`, `toInt`, `toFloat`                                           | ~900K/benchmark             | High — type conversion hot path                              |
| P0       | `str`     | `upper`, `lower`, `len`, `trim`, `contains`, `split`                  | ~900K/benchmark             | Medium — string args don't box, but result boxing eliminated |
| P1       | `arr`     | `push`, `pop`, `len`, `contains`                                      | Variable                    | Medium                                                       |
| P1       | Global    | `len`, `typeof`, `hash`                                               | Variable                    | Medium                                                       |
| P2       | Others    | All remaining                                                         | Low frequency               | Low — migrate for consistency                                |

### Phase 3: Callback paths (optional, lower priority)

Migrate `InvokeCallback` to span-based overloads. This is lower ROI because callback args are typically `object?` values from list iteration (already boxed). Defer unless profiling shows significant impact.

### Phase 4: Cleanup (after all namespaces migrated)

- Remove old `Call()` method from `IStashCallable` (breaking change — do in a major version)
- Remove `Args` class (replaced by `SvArgs`)
- Remove `_callArgList` pooling field from VM
- Remove `ToObject()` / `FromObject()` if no longer needed

---

## 4. Interaction with Existing Features

### 4.1 BuiltInBoundMethod

Currently wraps `IStashCallable` and injects receiver as first arg by creating a new list:

```csharp
public object? Call(IInterpreterContext context, List<object?> arguments)
{
    var newArgs = new List<object?>(arguments.Count + 1) { _receiver };
    newArgs.AddRange(arguments);
    return _function.Call(context, newArgs);
}
```

For `CallDirect`, this needs to allocate a temporary `StashValue[]` for receiver + args:

```csharp
public StashValue CallDirect(IInterpreterContext context, ReadOnlySpan<StashValue> arguments)
{
    // stackalloc up to reasonable size, heap-allocate for large arg counts
    int total = arguments.Length + 1;
    Span<StashValue> newArgs = total <= 8
        ? stackalloc StashValue[total]
        : new StashValue[total];
    newArgs[0] = StashValue.FromObject(_receiver);
    arguments.CopyTo(newArgs[1..]);
    return _function.CallDirect(context, newArgs);
}
```

This is fine — `BuiltInBoundMethod` is used for UFCS method dispatch which is lower frequency than direct namespace calls.

### 4.2 StashBoundMethod (struct methods)

`StashBoundMethod` delegates to `_method.CallWithSelf()`. For the direct path:

```csharp
public StashValue CallDirect(IInterpreterContext context, ReadOnlySpan<StashValue> arguments)
{
    // Struct methods are VMFunction closures dispatched by the VM, not IStashCallable
    // StashBoundMethod.Call() uses CallWithSelf() which sets up the environment
    // Delegate to the default implementation which converts to List<object?> and calls Call()
    var list = new List<object?>(arguments.Length);
    foreach (StashValue sv in arguments)
        list.Add(sv.ToObject());
    return StashValue.FromObject(Call(context, list));
}
```

Struct methods are VMFunction-based — the VM already handles them via the VMFunction fast path in `ExecuteCall`. `StashBoundMethod` only appears when a method reference is stored and called later, which is rare.

### 4.3 VMFunction.Call() — not an issue

`VMFunction` already throws `NotSupportedException` from `Call()`. It's dispatched directly by the VM via `PushFrame()`. No change needed — the VM's `ExecuteCall` checks for `VMFunction` first, before falling through to `IStashCallable`.

### 4.4 ReadOnlySpan and async

`ReadOnlySpan<T>` is a `ref struct` and cannot be captured in async state machines or stored on the heap. This is fine because:

- All built-in function bodies execute synchronously within `CallDirect()`
- Async functions in Stash use `Task.Run()` + `VMFunction` closures — they don't go through `IStashCallable.Call()`
- The span's lifetime is the duration of the `CallDirect()` invocation — never escapes the stack frame

Built-ins that launch background work (e.g., `task.run`, `fs.watch`) capture `IStashCallable` references for later invocation, but they don't capture the args span — they construct their own arg lists when invoking callbacks.

### 4.5 Playground (Blazor WASM)

Blazor WASM runs on a single-threaded runtime. `ReadOnlySpan<T>` is fully supported in .NET WASM. No issues.

### 4.6 LSP/DAP

LSP and DAP don't call `IStashCallable.Call()` directly. They use the VM for expression evaluation. No impact.

### 4.7 Static Analysis

Static analysis doesn't execute functions. No impact on `Stash.Analysis`.

---

## 5. Design Decisions

### 5.1 Where to put `StashValue` and `CallDirect`

The project reference graph is strictly one-directional:

```
Stash.Core          (zero project references)
    ↑
Stash.Stdlib        (references Core only)
    ↑
Stash.Bytecode      (references Core, Stdlib, Tpl)
    ↑
Stash.Cli           (references Bytecode, Core, Tap)
```

This means `BuiltInFunction` (Core) **cannot** implement interfaces from `Stash.Bytecode`, and `NamespaceBuilder` (Stdlib) **cannot** see types from `Stash.Bytecode`. This eliminates options that place new interfaces in Bytecode.

| Option | Description | Pros | Cons |
|--------|-------------|------|------|
| **(a)** Move `StashValue` to `Stash.Core` | Tagged union becomes a core value type, `IStashCallable` gains `CallDirect()` directly | Clean single interface; `BuiltInFunction` implements it directly; `NamespaceBuilder` gets new overload; zero wrapper classes | Core takes on a value type it previously didn't have |
| **(b)** Create `Stash.Common` shared project | New project with `StashValue`, referenced by Core and Bytecode | Clean separation of concerns | Adds solution complexity; another project to version/build |
| **(c)** Wrapper in `Stash.Bytecode` | `DirectBuiltInFunction` in Bytecode implements `IDirectCallable`; VM wraps `BuiltInFunction` at dispatch | No type moves needed | Runtime wrapping overhead; can't use new `NamespaceBuilder` overload (Stdlib can't see Bytecode types); complex plumbing |
| **(d)** `IDirectCallable` in Bytecode, `BuiltInFunction` implements it | — | — | **IMPOSSIBLE** — Core cannot reference Bytecode |

**Decision:** **(a)** — Move `StashValue` + `StashValueTag` to `Stash.Core`.

**Rationale:**
- `StashValue` is fundamentally a value representation (tagged union for null/bool/int/float/obj). It's not VM-specific — it's a general concept for representing Stash runtime values.
- `StashValue` has no dependencies on VM-specific types. `ToObject()` and `FromObject()` only use primitive types (`null`, `bool`, `long`, `double`) and `object` — all available in Core.
- Moving it makes `IStashCallable.CallDirect()` a first-class interface method that all implementors can override.
- `NamespaceBuilder` in Stdlib can offer the `DirectHandler` overload directly.
- No wrapper classes, no runtime type checks, no complex plumbing.
- The only downside is conceptual: Core gains a type that was created for the VM. But since the tree-walk interpreter is gone and the VM is the sole execution engine, this is appropriate.

**Migration note:** After moving, update all `using Stash.Bytecode;` references to `StashValue` → `using Stash.Runtime;` (or whatever namespace it lands in within Core). This is a mechanical change across `Stash.Bytecode/` files.

### 5.2 `ReadOnlySpan<StashValue>` vs `StashValue[]` vs `List<StashValue>`

| Option                     | Allocation                     | Safety                    | Ergonomics                              |
| -------------------------- | ------------------------------ | ------------------------- | --------------------------------------- |
| `ReadOnlySpan<StashValue>` | Zero — borrows stack           | Cannot escape stack frame | Slightly unfamiliar; can't use in async |
| `StashValue[]`             | One array per call (or pooled) | Standard heap reference   | More familiar; can capture in closures  |
| `List<StashValue>`         | One list per call (or pooled)  | Standard heap reference   | Same as current but typed               |

**Decision:** `ReadOnlySpan<StashValue>`.

**Rationale:** The whole point is zero allocation. Built-in functions are synchronous, short-lived, and never need to store the args span. The span directly slices the VM's `_stack[]` array — no copy, no allocation, no pooling.

### 5.3 Return type: `StashValue` vs `object?`

**Decision:** `StashValue` return.

**Rationale:** Returning `object?` would preserve boxing for int/float/bool results. The VM would still need `FromObject()` to convert back. Returning `StashValue` directly lets built-ins like `math.abs` return `StashValue.FromInt(result)` without boxing.

### 5.4 Migration strategy: Big-bang vs incremental

**Decision:** Incremental with dual-path dispatch.

**Rationale:** 350+ built-in functions across 32 files is too much for a single change. The dual `Call()`/`CallDirect()` approach lets us migrate one namespace at a time while everything continues to work. The default `CallDirect()` implementation on `IStashCallable` (which delegates to `Call()`) means un-migrated functions pay zero additional cost — they just use the existing boxing path.

---

## 6. Risks and Mitigations

| Risk | Impact | Likelihood | Mitigation |
|------|--------|-----------|-----------|
| Moving `StashValue` to Core breaks API assumptions | Widespread using-directive changes in Bytecode | High (but mechanical) | Bulk find-replace `using Stash.Bytecode;` → `using Stash.Runtime;` for StashValue references. Compiler errors guide the process. |
| `ReadOnlySpan<StashValue>` lifetime bugs (use-after-pop) | Memory corruption | Low | Span points into stack; `_sp` is decremented after `CallDirect()` returns, not before. Span is only valid during the call. |
| `ReadOnlySpan` in interface default implementations unsupported on target runtime | Build failure | Low | .NET 10 supports this. Verify during Phase 1; fall back to abstract method if needed. |
| Built-ins that mutate their argument list (rare) | Behavior change | Very Low | `ReadOnlySpan` prevents mutation by design — such built-ins fail to compile, surfacing the issue immediately. |
| Callback-heavy code (arr.map, arr.filter) sees no improvement from this change | Wasted effort on Phase 3 | Medium | Defer Phase 3; focus on direct-call hot paths first. |
| `StashValue` in Core creates a conceptual inconsistency (value type without the VM) | Code clarity | Low | Document that `StashValue` is the canonical runtime value representation, not a VM-specific type. |

---

## 7. Test Scenarios

### Unit tests (per migrated function)

- Existing tests continue to pass (no behavior change)
- Add specific `StashValue`-path tests for type preservation:
  - `math.abs(-5)` returns `StashValue.Int`, not `StashValue.Float`
  - `conv.toInt("42")` returns `StashValue.Int`
  - `str.upper("hello")` returns `StashValue.Obj` containing string

### Integration tests

- Full benchmark suite passes with no regressions
- Mixed VMFunction + IStashCallable call patterns (e.g., `arr.map(fn)` where `fn` calls `str.len`)
- Recursive built-in calls (callback from callback) work correctly
- UFCS method calls (BuiltInBoundMethod path) work correctly

### Performance tests

- Built-in Functions benchmark: target <778ms (original baseline)
- Function Calls benchmark: no regression (VMFunction path unchanged)
- Expression Throughput: no regression (arithmetic ops unchanged)

---

## 8. Implementation Checklist

When this spec moves to `1-todo/`, an Orchestrator should execute these steps:

### Phase 1: Infrastructure

- [ ] Move `StashValue.cs` and `StashValueTag` from `Stash.Bytecode/Runtime/` to `Stash.Core/Runtime/`
- [ ] Update namespace from `Stash.Bytecode` to `Stash.Runtime` (or appropriate Core namespace)
- [ ] Fix all using-directive changes across Bytecode project (mechanical bulk replace)
- [ ] Add `CallDirect(IInterpreterContext, ReadOnlySpan<StashValue>)` to `IStashCallable` with default implementation
- [ ] Add `DirectHandler` delegate type to `BuiltInFunction`
- [ ] Add dual-constructor to `BuiltInFunction` (legacy + direct)
- [ ] Implement `CallDirect()` override on `BuiltInFunction`
- [ ] Add `SvArgs` static class in `Stash.Stdlib/`
- [ ] Add new `NamespaceBuilder.Function()` overload accepting `DirectHandler`
- [ ] Modify VM `CallValue()` to call `CallDirect()` with stack span instead of building `List<object?>`
- [ ] Implement `CallDirect()` on `BuiltInBoundMethod` (receiver injection via stackalloc)
- [ ] Verify all tests pass
- [ ] Verify benchmarks are not slower (baseline measurement)

### Phase 2: Hot-path migration

- [ ] Migrate `MathBuiltIns` — all numeric functions
- [ ] Migrate `ConvBuiltIns` — `toStr`, `toInt`, `toFloat`
- [ ] Migrate `StrBuiltIns` — hot-path string functions
- [ ] Migrate `GlobalBuiltIns` — `len`, `typeof`
- [ ] Benchmark after each namespace — measure cumulative improvement
- [ ] Verify all 4721 tests pass after each namespace

### Phase 3: Remaining namespaces (optional, for consistency)

- [ ] Migrate all remaining namespaces to `DirectHandler`
- [ ] Implement `CallDirect()` on `BuiltInBoundMethod`
- [ ] Update `BuiltInBoundMethod` to implement `IDirectCallable`

### Phase 4: Cleanup (after full migration)

- [ ] Remove legacy `Call()` from `IStashCallable` (or deprecate)
- [ ] Remove `_callArgList` pool from VM
- [ ] Remove old `Args` class (replaced by `SvArgs`)
- [ ] Update README benchmark numbers

---

## 9. Example: Migrated math.abs

### Before (current)

```csharp
ns.Function("abs", [Param("n", "number")], (interp, args) =>
{
    if (args[0] is long l) return Math.Abs(l);
    if (args[0] is double d) return Math.Abs(d);
    throw new RuntimeError("First argument to 'math.abs' must be a number.");
}, returnType: "number", documentation: "...");
```

Call overhead: `ToObject()` → box `long` → `args.Add(boxed)` → `args[0] is long l` → unbox → `Math.Abs()` → box result → `FromObject()` → unbox

### After (migrated)

```csharp
ns.Function("abs", [Param("n", "number")], static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
{
    StashValue n = args[0];
    if (n.IsInt) return StashValue.FromInt(Math.Abs(n.AsInt));
    if (n.IsFloat) return StashValue.FromFloat(Math.Abs(n.AsFloat));
    throw new RuntimeError("First argument to 'math.abs' must be a number.");
}, returnType: "number", documentation: "...");
```

Call overhead: read `_stack[argStart]` directly → tag check → `Math.Abs()` → `StashValue.FromInt()` (no boxing, struct return)

**Boxing eliminated:** 2 allocations per call (arg + result) → 0 allocations per call.
