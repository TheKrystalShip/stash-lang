# VM Debug Elision — Zero-Cost Debug Hooks via Generic Specialization

**Status:** Backlog — Design
**Created:** 2026-04-09
**Author:** Architect

## 1. Problem Statement

The bytecode VM's dispatch loop (`RunInner`) contains debug hooks — breakpoint checks, step tracking, function-entry/exit notifications, and debug call stack management — that execute a per-instruction `if (debugger is not null)` branch even when no debugger is attached. While the branch predictor handles this well in steady state, the debug code has three concrete costs:

1. **Code size bloat in the hot loop.** The debug block at the top of the dispatch loop (lines 85–109 in `VirtualMachine.Dispatch.cs`) adds ~25 lines of rarely-taken code inside the tightest loop in the interpreter. This increases the instruction cache footprint and can prevent the JIT/AOT compiler from fitting the fast path in a single cache line or inlining aggressively.

2. **Register pressure from `debugger` parameter threading.** Five methods (`ExecuteCall`, `ExecuteCallSpread`, `ExecuteCallBuiltIn`, `ExecuteCallBuiltInSlow`, `ExecuteReturn`) accept `IDebugger? debugger` as a parameter. This consumes a register or stack slot on every call dispatch — one of the hottest operations in the interpreter.

3. **Unconditional bookkeeping.** `_debugCallStack.Clear()` is called in `Execute()` and `ExecuteRepl()` on every script run, and `_lastDebugLinePerFrame` is checked even when null. Minor individually, but cumulative.

The goal is to **completely eliminate all debug-related code from the native instruction stream** when no debugger is attached, while keeping a single codebase with no `#if` conditional compilation.

## 2. Design Decision

### Chosen: Generic Specialization over Value-Type Marker Structs

.NET's JIT and AOT compilers fully specialize generic methods over value-type type parameters. A `typeof(T) == typeof(SomeStruct)` check inside a generic method becomes a compile-time constant — the compiler eliminates the dead branch entirely from the generated native code.

We introduce two zero-size marker structs and make the dispatch loop generic over a `TDebugMode` type parameter:

```csharp
// Marker structs — zero allocation, zero size
internal readonly struct DebugOn { }
internal readonly struct DebugOff { }
```

The dispatch entry points route to the appropriate specialization:

```csharp
// In VirtualMachine.cs — Execute()
if (_debugger is not null)
    return RunDebug();      // calls RunInner<DebugOn>(0)
return Run();               // calls RunInner<DebugOff>(0)
```

Inside `RunInner<TDebugMode>`, all debug checks use:

```csharp
if (typeof(TDebugMode) == typeof(DebugOn))
{
    // This entire block is eliminated at JIT/AOT time
    // when TDebugMode = DebugOff
}
```

### Alternatives Rejected

| Alternative                                               | Why Rejected                                                                                                                                                                                                                                                                |
| --------------------------------------------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| **`#if STASH_NO_DEBUG` compile-time flag**                | Requires two separate builds per platform (6 → 12 CI targets). Creates invisible code paths that rot without testing. Confusing UX — users must choose which binary to install. The generic approach achieves identical native code elimination with zero build complexity. |
| **Separate `RunInnerFast()` / `RunInnerDebug()` methods** | Massive code duplication of the entire 300+ line dispatch loop. Every opcode change must be mirrored. Guaranteed divergence over time.                                                                                                                                      |
| **Runtime `bool _isDebugging` field check**               | Semantically identical to the current null check. Same branch, same code bloat, same register pressure. No improvement.                                                                                                                                                     |
| **Function pointer dispatch (delegate per-mode)**         | Adds an indirect call at a hot boundary. Worse than a predictable branch.                                                                                                                                                                                                   |

### Risks

- **Code size doubling in the binary.** The AOT compiler will emit two copies of `RunInner` — one with debug code, one without. For a ~300-line method, this is a few KB of additional binary size. Acceptable given binaries are currently ~15–25 MB.
- **Generic complexity.** The `typeof(TDebugMode) == typeof(DebugOn)` pattern is well-known in .NET performance work but unfamiliar to some contributors. A comment block explaining the technique at the method header mitigates this.
- **JIT warmup.** On .NET JIT (non-AOT), the first invocation of each specialization triggers compilation. This is negligible compared to normal startup costs and only happens once.

## 3. Scope of Changes

### 3.1 Complete Debug Hook Inventory

Every debug-related code point that must be wrapped in `typeof(TDebugMode) == typeof(DebugOn)` guards:

#### VirtualMachine.Dispatch.cs — `RunInner()`

| Location | Current Code                                                      | Action                                                     |
| -------- | ----------------------------------------------------------------- | ---------------------------------------------------------- |
| L68      | `IDebugger? debugger = _debugger;`                                | Move inside `DebugOn` guard; not needed in `DebugOff` path |
| L71–75   | `_lastDebugLinePerFrame` allocation check                         | Wrap in `DebugOn` guard                                    |
| L85–109  | Per-instruction debug hook (breakpoint/step check)                | Wrap in `DebugOn` guard                                    |
| L251–252 | Loop opcode pause-request reset                                   | Wrap in `DebugOn` guard                                    |
| L257     | `ExecuteCall(ref frame, inst, debugger)`                          | See §3.2 below                                             |
| L258     | `ExecuteCallSpread(ref frame, inst, debugger)`                    | See §3.2 below                                             |
| L259     | `ExecuteCallBuiltIn(ref frame, inst, debugger)`                   | See §3.2 below                                             |
| L261     | `ExecuteReturn(ref frame, inst, targetFrameCount, debugger, ...)` | See §3.2 below                                             |

#### VirtualMachine.Functions.cs — Call/Return Methods

| Method                          | Debug Code Location                                                        | Action                   |
| ------------------------------- | -------------------------------------------------------------------------- | ------------------------ |
| `ExecuteCall` (L358)            | L619–634: function-entry hook, `_debugCallStack.Add`, `OnFunctionEnter`    | Wrap in `DebugOn` guard  |
| `ExecuteCallSpread` (L720)      | L776–789: function-entry hook (duplicate of above)                         | Wrap in `DebugOn` guard  |
| `ExecuteReturn` (L793)          | L800–804: function-exit hook, `_debugCallStack.RemoveAt`, `OnFunctionExit` | Wrap in `DebugOn` guard  |
| `ExecuteCallBuiltIn` (L637)     | No direct debug hooks — delegates to `ExecuteCallBuiltInSlow`              | No change needed in body |
| `ExecuteCallBuiltInSlow` (L675) | L671: forwards debugger to `ExecuteCall`                                   | See §3.2                 |

#### VirtualMachine.Debug.cs

| Method                 | Action                                                                    |
| ---------------------- | ------------------------------------------------------------------------- |
| `RunDebug()`           | Calls `RunInner<DebugOn>(0)` — already on the debug path, no guard needed |
| `RunUntilFrameDebug()` | Calls `RunInner<DebugOn>(targetFrameCount)` — same                        |
| `BuildFrameScope()`    | Only called from debug-guarded code — no change                           |
| `BuildGlobalScope()`   | Only called externally by DAP — no change                                 |

#### VirtualMachine.cs — Entry Points

| Method               | Current                                                       | After                                                                                                                 |
| -------------------- | ------------------------------------------------------------- | --------------------------------------------------------------------------------------------------------------------- |
| `Execute()`          | `if (_debugger is not null) return RunDebug(); return Run();` | No structural change — Run/RunDebug already split. Run calls `RunInner<DebugOff>`, RunDebug calls `RunInner<DebugOn>` |
| `ExecuteRepl()`      | Same pattern                                                  | Same change                                                                                                           |
| `Execute()` L205     | `_debugCallStack.Clear()`                                     | Move inside `if (_debugger is not null)` block                                                                        |
| `ExecuteRepl()` L228 | `_debugCallStack.Clear()`                                     | Move inside `if (_debugger is not null)` block                                                                        |

#### VirtualMachine.Modules.cs

| Location | Code                                        | Action                                                                                                                                                                          |
| -------- | ------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| L129     | `moduleVM.Debugger = _debugger;`            | Already behind implicit debug path (only matters when debugger is set). No change needed — the module VM's own `Execute()` will route to `DebugOff` if its `_debugger` is null. |
| L130     | `moduleVM._debugThreadId = _debugThreadId;` | Same — harmless assignment, no hot-path impact                                                                                                                                  |
| L137     | `_debugger?.OnSourceLoaded(resolvedPath);`  | Already null-conditional. Not in hot loop. No change needed.                                                                                                                    |

#### VirtualMachine.cs — Quicken Guard

| Location | Code                                                      | Action                                                    |
| -------- | --------------------------------------------------------- | --------------------------------------------------------- |
| L342     | `if (chunk.QuickenCounters is null && _debugger is null)` | No change — this is a one-time check outside the hot loop |

### 3.2 Eliminating the `debugger` Parameter from Call Methods

The current design threads `IDebugger? debugger` through five methods. With generic specialization, these methods should also become generic:

**Option A — Make call methods generic too:**

```csharp
private void ExecuteCall<TDebugMode>(ref CallFrame frame, uint inst) where TDebugMode : struct
{
    // ... call logic ...

    if (typeof(TDebugMode) == typeof(DebugOn))
    {
        IDebugger debugger = _debugger!;
        // function-entry hook
    }
}
```

Dispatch becomes:

```csharp
case OpCode.Call: ExecuteCall<TDebugMode>(ref frame, inst); break;
```

**Option B — Keep separate debug/non-debug call methods:**

```csharp
case OpCode.Call:
    if (typeof(TDebugMode) == typeof(DebugOn))
        ExecuteCallDebug(ref frame, inst);
    else
        ExecuteCall(ref frame, inst);  // no debugger param
    break;
```

**Decision: Option A.** It avoids method duplication and the compiler will specialize the generic call methods the same way it specializes `RunInner`. The `debugger` parameter is eliminated entirely — in the `DebugOff` specialization, the methods won't read `_debugger` at all.

The full set of methods that gain `<TDebugMode>`:

- `RunInner<TDebugMode>(int targetFrameCount)`
- `ExecuteCall<TDebugMode>(ref CallFrame frame, uint inst)`
- `ExecuteCallSpread<TDebugMode>(ref CallFrame frame, uint inst)`
- `ExecuteCallBuiltIn<TDebugMode>(ref CallFrame frame, uint inst)`
- `ExecuteCallBuiltInSlow<TDebugMode>(ref CallFrame frame, byte a, byte b, byte argc, int icIdx)`
- `ExecuteReturn<TDebugMode>(ref CallFrame frame, uint inst, int targetFrameCount, out object? result)`

Note: `ExecuteReturn` also loses the `IDebugger? debugger` parameter — it reads `_debugger` from the field inside the `DebugOn` guard instead.

### 3.3 Outer Loop Changes (Run / RunDebug)

```csharp
// Current Run() — exception handler loop
private object? Run()
{
    while (true)
    {
        try { return RunInner<DebugOff>(0); }
        catch (RuntimeError ex) { /* exception handler routing */ }
    }
}

// Current RunDebug() — exception handler loop + debugger error notification
private object? RunDebug()
{
    while (true)
    {
        try { return RunInner<DebugOn>(0); }
        catch (RuntimeError ex) { /* exception handler routing + debugger.OnError() */ }
    }
}
```

`Run()` and `RunDebug()` remain separate non-generic methods. They are not hot — they only execute on exception handler unwind. The debug/non-debug split at this level is already clean.

Similarly, `RunUntilFrameDebug()` calls `RunInner<DebugOn>()` — it's only invoked from the DAP expression evaluator.

## 4. Implementation Approach

### Phase 1 — Introduce Marker Structs and Genericize RunInner

1. Add `DebugOn` and `DebugOff` structs to `VirtualMachine.cs` (or a small companion file).
2. Change `RunInner(int targetFrameCount)` to `RunInner<TDebugMode>(int targetFrameCount) where TDebugMode : struct`.
3. Replace `if (debugger is not null)` with `if (typeof(TDebugMode) == typeof(DebugOn))` throughout `RunInner`.
4. Inside `DebugOn` guards, read `_debugger` from the field (it's guaranteed non-null on this path).
5. Remove the `IDebugger? debugger` local variable.
6. Update `Run()` to call `RunInner<DebugOff>(0)`.
7. Update `RunDebug()` to call `RunInner<DebugOn>(0)`.
8. Update `RunUntilFrameDebug()` to call `RunInner<DebugOn>(targetFrameCount)`.
9. Keep `[MethodImpl(MethodImplOptions.AggressiveOptimization)]` on `RunInner<TDebugMode>` — this attribute is compatible with generic methods.

### Phase 2 — Genericize Call/Return Methods

1. Change `ExecuteCall`, `ExecuteCallSpread`, `ExecuteCallBuiltIn`, `ExecuteCallBuiltInSlow`, and `ExecuteReturn` to accept `<TDebugMode>` instead of `IDebugger? debugger`.
2. Replace all internal `if (debugger is not null)` blocks with `if (typeof(TDebugMode) == typeof(DebugOn))`.
3. Inside `DebugOn` guards, read `IDebugger debugger = _debugger!;`.
4. Update all call sites in the dispatch switch to pass `<TDebugMode>`.

### Phase 3 — Clean Up Entry Points

1. Move `_debugCallStack.Clear()` in `Execute()` and `ExecuteRepl()` inside the `if (_debugger is not null)` block.
2. Verify `_lastDebugLinePerFrame` is only allocated/accessed inside `DebugOn`-guarded code.

### Phase 4 — Validation

1. Run full test suite (`dotnet test`) — all ~2,000 tests must pass.
2. Run DAP integration tests — debugger must still work (breakpoints, stepping, function breakpoints, exception breakpoints).
3. Run benchmarks — compare before/after on `bench_function_calls`, `bench_scope_lookup`, `bench_algorithms`, and `bench_namespace_calls`.
4. Verify AOT compilation still succeeds: `dotnet publish Stash.Cli -c Release -r linux-x64 --self-contained`.
5. Inspect disassembly of `RunInner<DebugOff>` (via `DOTNET_JitDisasm` or ILSpy on AOT output) to confirm debug code is absent.

## 5. Expected Performance Impact

The primary win is **code density in the hot loop**, not branch elimination. Specifics:

| Factor                                   | Current                                            | After                                                                                         |
| ---------------------------------------- | -------------------------------------------------- | --------------------------------------------------------------------------------------------- |
| `RunInner` body size (IL)                | ~300 lines with debug code interleaved             | `RunInner<DebugOff>`: ~250 lines, no debug code. `RunInner<DebugOn>`: ~300 lines (unchanged). |
| Per-instruction branch                   | `if (debugger is not null)` — 1 branch/instruction | Eliminated in `DebugOff` specialization                                                       |
| Call dispatch register pressure          | 5 methods carry `IDebugger?` param                 | Eliminated — no param, no field read                                                          |
| `_debugCallStack.Clear()` per-Execute    | Always runs                                        | Only when debugger attached                                                                   |
| Branch predictor pressure in tight loops | Slight overhead from debug branch                  | Zero — code doesn't exist                                                                     |

Expected improvement: **2–8% on tight benchmarks** (function calls, scope lookups, numeric loops). Wider scripts with I/O will see less because the loop isn't the bottleneck. The improvement should show most clearly on `bench_function_calls` and `bench_algorithms`.

## 6. Interaction with Existing Features

### Quickening

The quicken guard at L342 checks `_debugger is null` to decide whether quickening is safe. This check is outside the hot loop and remains a simple field read. No change needed — quickening is orthogonal to the dispatch specialization.

### Module Loading

Module VMs get `_debugger` set via the property setter (L129 in Modules.cs). Each module VM's `Execute()` will independently route to `Run()` or `RunDebug()` based on its own `_debugger` field. Correct by construction.

### REPL Mode

`ExecuteRepl()` already has the same Run/RunDebug split. Same change applies.

### Cancellation Token / Step Limit

These checks (`_ct.ThrowIfCancellationRequested()`, `StepLimit`) are in the Loop opcode handler, not in debug code. They are unaffected.

### Superinstructions

If/when superinstructions are added, they should follow the same pattern — their handler methods would be generic over `TDebugMode` if they need debug hooks (most won't).

## 7. Cross-Platform Considerations

Generic specialization over value types is fully supported by:

- **.NET JIT (RyuJIT)** — specializes on first call per type argument
- **.NET Native AOT (ILC)** — specializes at compile time, dead code fully stripped
- **Mono** (if ever relevant) — supports value-type generic specialization

No platform-specific behavior. No cross-platform differences.

## 8. LSP / DAP Implications

- **DAP:** Zero changes to `Stash.Dap`. The `DebugSession` still sets `vm.Debugger = this`, which causes `Execute()` to route to `RunDebug()` → `RunInner<DebugOn>()`. The DAP protocol is unchanged. All `IDebugger` callbacks fire exactly as before.
- **LSP:** No impact. The LSP does not attach a debugger to the VM.
- **Analysis:** No impact. Static analysis doesn't run the VM.

## 9. Test Scenarios

### Unit Tests (existing — must continue passing)

All ~2,000 existing tests run without a debugger attached. They will exercise `RunInner<DebugOff>` exclusively. If any test regresses, the `DebugOff` specialization has a bug.

### DAP Tests (existing)

All DAP tests attach a debugger. They will exercise `RunInner<DebugOn>`. If any DAP test regresses, the `DebugOn` specialization has a bug in the refactored guard placement.

### New Tests to Add

| Test                                           | Purpose                                                                                                                                                                                         |
| ---------------------------------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `DebugMode_SpecializationDoesNotAffectResults` | Run a set of scripts with and without a mock debugger attached; assert identical return values                                                                                                  |
| `DebugOff_DoesNotCallDebugger`                 | Attach a mock `IDebugger` that throws on any callback; run without setting `vm.Debugger`; assert no throws — confirms debug code is unreachable (logical correctness, not codegen verification) |
| `DebugOn_StillFiresAllCallbacks`               | Attach a counting mock `IDebugger`; run a script with function calls in debug mode; assert `OnBeforeExecute`, `OnFunctionEnter`, `OnFunctionExit` were all called                               |

### Benchmark Validation

Run the following before and after and compare:

```bash
stash benchmarks/bench_function_calls.stash
stash benchmarks/bench_algorithms.stash
stash benchmarks/bench_scope_lookup.stash
stash benchmarks/bench_namespace_calls.stash
stash benchmarks/bench_numeric.stash
```

## 10. Migration / Breaking Changes

**None.** This is a purely internal refactoring. The public API surface of `VirtualMachine` does not change:

- `vm.Debugger` property: unchanged
- `vm.Execute()`: unchanged
- `vm.ExecuteRepl()`: unchanged
- `IDebugger` interface: unchanged

No user-visible behavior changes. No breaking changes for DAP or any external consumer.

## 11. Implementation Checklist

- [ ] Add `DebugOn` / `DebugOff` marker structs
- [ ] Genericize `RunInner<TDebugMode>`
- [ ] Replace all `if (debugger is not null)` in `RunInner` with `typeof(TDebugMode)` guards
- [ ] Genericize `ExecuteCall<TDebugMode>`, `ExecuteCallSpread<TDebugMode>`, `ExecuteCallBuiltIn<TDebugMode>`, `ExecuteCallBuiltInSlow<TDebugMode>`, `ExecuteReturn<TDebugMode>`
- [ ] Remove `IDebugger? debugger` parameter from all genericized methods
- [ ] Update dispatch switch call sites to forward `<TDebugMode>`
- [ ] Update `Run()` → `RunInner<DebugOff>(0)`
- [ ] Update `RunDebug()` → `RunInner<DebugOn>(0)`
- [ ] Update `RunUntilFrameDebug()` → `RunInner<DebugOn>(targetFrameCount)`
- [ ] Gate `_debugCallStack.Clear()` behind `_debugger is not null` in `Execute()` / `ExecuteRepl()`
- [ ] Add explanatory comment block at `RunInner` documenting the generic specialization technique
- [ ] Run full test suite
- [ ] Run DAP tests
- [ ] Run benchmarks, record before/after numbers
- [ ] Verify AOT publish succeeds
- [ ] (Optional) Inspect disassembly to confirm dead-code elimination
