# DAP Bytecode VM — Exclusive Debugger Migration

**Status:** Backlog — Design Spec
**Created:** 2026-04-05
**Parent:** Bytecode VM — Implementation Roadmap (Post-Phase 8)
**Depends on:** Phase 7 (Debugger Integration), Phase 8 (Integration and Migration)
**Purpose:** Wire the DAP debugger to use the bytecode VM exclusively, replacing all tree-walk interpreter usage in `Stash.Dap/`. This is a precursor to eventual tree-walker removal.

---

## Table of Contents

1. [Motivation](#1-motivation)
2. [Current State Analysis](#2-current-state-analysis)
3. [Gap Analysis — VM vs Tree-Walker Parity](#3-gap-analysis--vm-vs-tree-walker-parity)
4. [Design Decisions](#4-design-decisions)
5. [Implementation Plan](#5-implementation-plan)
6. [IDebugScope Extension — Variable Mutation](#6-idebugscope-extension--variable-mutation)
7. [VM Expression Evaluation](#7-vm-expression-evaluation)
8. [DebugSession Rewrite](#8-debugsession-rewrite)
9. [VMDebugAdapter Rewrite](#9-vmdebugadapter-rewrite)
10. [VM Loaded Sources](#10-vm-loaded-sources)
11. [VM Test Harness Integration](#11-vm-test-harness-integration)
12. [Cross-Cutting Concerns](#12-cross-cutting-concerns)
13. [Test Strategy](#13-test-strategy)
14. [Migration Strategy — Tree-Walker Removal Path](#14-migration-strategy--tree-walker-removal-path)
15. [Risk Register](#15-risk-register)
16. [Decision Log](#16-decision-log)

---

## 1. Motivation

Phase 7 added debug abstractions (`IDebugger`, `IDebugScope`, `IDebugExecutor`) and Phase 8 wired the bytecode VM as the default execution backend. However, the DAP debugger still creates and runs a tree-walk `Interpreter` for:

1. **Script execution** — `Launch()` creates `new Interpreter()` and calls `Interpret(stmts)`
2. **Watch expression evaluation** — `VMDebugAdapter` creates a second `new Interpreter()` for `EvaluateString()`
3. **Variable mutation** — `SetVariable()` casts `IDebugScope` to concrete `Environment` (tree-walk type)
4. **Test harness** — `TestHarness`/`TestFilter` configured on tree-walk `Interpreter` only

The goal is to eliminate **all** tree-walk `Interpreter` usage from `Stash.Dap/` so that:

- Debugging uses the same execution backend as normal script execution (bytecode VM)
- Debug stepping, breakpoints, and variable inspection reflect the VM's actual state
- The `Stash.Dap` project can eventually drop its `Stash.Interpreter` project reference entirely

---

## 2. Current State Analysis

### 2.1 Tree-Walk Dependencies in Stash.Dap/

**21 direct tree-walk references** across 2 files. Categorized by what they do:

| Category          | File:Line                 | What It Does                                                                         |
| ----------------- | ------------------------- | ------------------------------------------------------------------------------------ |
| **Execution**     | `DebugSession.cs:228`     | `new Interpreter()` — creates execution engine                                       |
| **Execution**     | `DebugSession.cs:229-238` | Configures `Debugger`, `CurrentFile`, `SetScriptArgs()`, `Output`, `ErrorOutput`     |
| **Execution**     | `DebugSession.cs:301`     | `_treeWalkInterpreter.Interpret(stmts)` — runs the script                            |
| **Execution**     | `DebugSession.cs:245`     | `Executor = _treeWalkInterpreter` — registers as thread executor                     |
| **Test harness**  | `DebugSession.cs:252-257` | Configures `TestHarness`, `TestFilter` on interpreter                                |
| **Test harness**  | `DebugSession.cs:304`     | Reads `TestHarness` after execution for TAP reporting                                |
| **Eval fallback** | `DebugSession.cs:779`     | `executor ??= _treeWalkInterpreter` — fallback for expression eval                   |
| **Eval fallback** | `DebugSession.cs:815`     | `IDebugExecutor executor = _treeWalkInterpreter` — SetVariable executor              |
| **Scope cast**    | `DebugSession.cs:858`     | `container.Scope is not Environment` — casts to tree-walk `Environment` for mutation |
| **Scope cast**    | `DebugSession.cs:858+`    | `envScope.Contains(name)`, `envScope.IsConstant(name)` — tree-walk methods           |
| **Guard**         | `DebugSession.cs:559,798` | Null checks on `_treeWalkInterpreter`                                                |
| **Eval engine**   | `VMDebugAdapter.cs:16`    | `_evalInterpreter: Interpreter` — second interpreter for watch eval                  |
| **Eval engine**   | `VMDebugAdapter.cs:21`    | `new Interpreter()` — creates eval-only interpreter                                  |
| **Eval engine**   | `VMDebugAdapter.cs:47`    | `new Environment()` — creates tree-walk scope for eval                               |
| **Eval engine**   | `VMDebugAdapter.cs:51`    | `_evalInterpreter.EvaluateString()` — tree-walk expression eval                      |

### 2.2 What the VM Already Provides

Phase 7 and 8 built significant infrastructure:

| Capability                                                   | Status       | Where                                       |
| ------------------------------------------------------------ | ------------ | ------------------------------------------- |
| Debug hooks (OnBeforeExecute, OnFunctionEnter/Exit, OnError) | ✅ Working   | `VirtualMachine.RunInner()`                 |
| Line-based stepping (lastDebugLine dedup)                    | ✅ Working   | `VirtualMachine.RunInner()`                 |
| Debug call stack tracking                                    | ✅ Working   | `VirtualMachine._debugCallStack`            |
| Frame scope building (locals + globals)                      | ✅ Working   | `VirtualMachine.BuildFrameScope()`          |
| Local variable names                                         | ✅ Working   | `Chunk.LocalNames`                          |
| Source map (bytecode offset → SourceSpan)                    | ✅ Working   | `SourceMap.GetSpan()`                       |
| Exception breakpoints                                        | ✅ Working   | `VirtualMachine.RunDebug()`                 |
| Function entry detection                                     | ✅ Working   | `VirtualMachine.RunInner()` OP_CALL handler |
| Module loading                                               | ✅ Working   | `VirtualMachine.LoadModule()`               |
| Standalone expression compilation                            | ✅ Working   | `Compiler.CompileExpression()`              |
| VMDebugScope (IDebugScope adapter)                           | ✅ Working   | `VMDebugScope.cs`                           |
| VMDebugAdapter (IDebugExecutor adapter)                      | ✅ Partial   | Phase 7 fallback — uses tree-walk for eval  |
| `IDebugger` interface                                        | ✅ Working   | `Stash.Core/Debugging/IDebugger.cs`         |
| `IDebugExecutor` interface                                   | ✅ Working   | `Stash.Core/Debugging/IDebugExecutor.cs`    |
| `IDebugScope` interface                                      | ✅ Read-only | No mutation support                         |

---

## 3. Gap Analysis — VM vs Tree-Walker Parity

### 3.1 CRITICAL Gaps (Must fix to match tree-walker)

| Gap                                        | Impact                                                                                                                                                      | Effort |
| ------------------------------------------ | ----------------------------------------------------------------------------------------------------------------------------------------------------------- | ------ |
| **G1. No VM-native expression evaluation** | Watch expressions, conditional breakpoints, logpoints, debug console — all rely on tree-walk `EvaluateString()`. Without this, debugging is non-functional. | Medium |
| **G2. `IDebugScope` is read-only**         | `SetVariable()` casts to concrete `Environment` to call `Assign()`, `Contains()`, `IsConstant()`. VM scopes can't mutate.                                   | Medium |
| **G3. No loaded sources notification**     | VM's `LoadModule()` doesn't call `_debugger?.OnSourceLoaded()`. DAP clients won't see imported files.                                                       | Small  |
| **G4. No test harness on VM**              | `TestHarness`/`TestFilter` are configured on tree-walk `Interpreter` only. Debug-mode test runs via DAP won't work with VM.                                 | Small  |
| **G5. Launch() hardcoded to tree-walk**    | `Launch()` creates `Interpreter`, lexes/parses/interprets — needs to compile to bytecode and use VM instead.                                                | Medium |
| **G6. Child VM debugger propagation**      | When `VirtualMachine.LoadModule()` creates child VMs for imports, it doesn't propagate the debugger. Module code won't hit breakpoints.                     | Small  |

### 3.2 MODERATE Gaps (Quality-of-life, but not blockers)

| Gap                             | Impact                                                                                                                                                                                                                                     | Effort |
| ------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ | ------ |
| **G7. No const tracking in VM** | Tree-walker tracks which variables are const and prevents mutation via SetVariable. VM enforces const-ness at compile time only — no runtime metadata.                                                                                     | Small  |
| **G8. No closure scope chain**  | Tree-walk `Environment` has a natural scope chain (local → closure → global). VM's `BuildFrameScope()` returns flat local + global — no intermediate "Closure" scope in the DAP Scopes view. Upvalue scope has no separate representation. | Small  |

### 3.3 Non-Gaps (Already handled by VM)

- **Breakpoints** — VM's `OnBeforeExecute` fires on each new source line; DebugSession's `CheckBreakpointAtSpan()` is backend-agnostic.
- **Stepping** — `ShouldStopForStep()` uses `executor.CallStack.Count` which works with `VMDebugAdapter.CallStack`.
- **Stack traces** — `GetStackTrace()` reads from `executor.CallStack` which VMDebugAdapter provides.
- **Variable display** — `FormatVariable()` operates on `object?` values, which are the same for both backends.
- **Pause/resume** — `ManualResetEventSlim` gate works regardless of backend — `OnBeforeExecute` is called synchronously in the VM's instruction loop, so blocking the debugger callback blocks the VM.
- **Function breakpoints** — VM calls `debugger.ShouldBreakOnFunctionEntry()` and `debugger.OnFunctionEnter()`.

---

## 4. Design Decisions

### D1. Expression Evaluation Strategy

**Decision: VM-native eval via temporary VM with scope seeding**

The bytecode pipeline already supports standalone expression compilation (`Compiler.CompileExpression()`). The approach:

1. Parse expression string → AST `Expr` node (Lexer + Parser, no Resolver)
2. Compile via `Compiler.CompileExpression(expr)` → `Chunk`
   - Unresolved variables default to `OP_LOAD_GLOBAL` (Compiler behavior when no Resolver annotations)
3. Create a temporary `VirtualMachine` with the debug scope's bindings seeded as globals
4. Execute the chunk → return value

**Scope seeding order:** Walk the scope chain from outermost (global) to innermost (local). This means innermost bindings naturally shadow outer ones, matching lexical scoping.

```
tempGlobals = {}
for scope in chain (global → ... → local):
    for (name, value) in scope.GetAllBindings():
        tempGlobals[name] = value
```

**Why not keep the tree-walk eval?**

- The tree-walk will eventually be removed. Building on it creates another migration debt.
- VM eval is already proven to work (StashEngine.EvaluateBytecode exists and passes all tests).
- Expression evaluation via bytecode is significantly faster, which matters for conditional breakpoints evaluated on every statement.

**Why not run expressions in the main VM?**

- Watch expressions could have side effects that corrupt the program's state. A temp VM isolates evaluation.
- The main VM is paused mid-instruction-loop — pushing new frames would corrupt the stack.

**Risks:**

- Expressions that call functions or access closures may behave differently in a temporary VM vs. the original execution context. Functions defined in the program will be in the seeded globals but won't have access to their original closure state.
- Built-in namespace functions need to be available in the temp VM. Seed from the main VM's globals.

> **Revision note:** Phase 7 used a tree-walk `Interpreter` as an acknowledged fallback ("full VM eval deferred to Phase 8+"). This spec delivers that deferred work.

### D2. Variable Mutation Strategy

**Decision: Extend `IDebugScope` with optional mutation methods + `VMDebugScope` backed by live stack reference**

Add to `IDebugScope`:

```csharp
bool TryGetValue(string name, out object? value) => false;
bool Contains(string name) => false;
bool IsConstant(string name) => false;
bool TryAssign(string name, object? value) => false;
```

Default implementations return `false` (read-only), so existing implementations don't break. `VMDebugScope` and `Environment` implement the mutations.

For the VM specifically:

- `VMDebugScope` stores a reference to the VM's stack array + base slot + local count, not just a snapshot
- `TryAssign()` writes directly to the stack slot: `_stack[_baseSlot + slotIndex] = value`
- `Contains()` checks `LocalNames` array
- `IsConstant()` checks a new `Chunk.LocalIsConst` metadata array (resolves G7)

**Why not a separate `IMutableDebugScope`?**

- It splits the hierarchy for no benefit. Default implementations keep the interface backward-compatible.
- DebugSession can call `scope.TryAssign()` polymorphically without casting.

### D3. Launch Rewrite Strategy

**Decision: Mirror StashEngine's bytecode path inside DebugSession**

`Launch()` will:

1. Set up I/O redirects, test harness, script args on `VMContext`
2. Create `VirtualMachine` with globals populated (same as `StashEngine.EnsureVM()`)
3. Attach `this` (DebugSession) as `vm.Debugger`
4. Create `VMDebugAdapter(vm)` as the `IDebugExecutor`
5. Lex → Parse → Resolve → Compile → `vm.Execute(chunk)`
6. Handle exceptions, send termination events

The Resolver still needs to run to annotate variable distances for correct compilation. The Resolver lives in `Stash.Interpreter` and uses the tree-walk `Interpreter` — but only for resolution, not execution. This is the same pattern Phase 8 uses in StashEngine.

**Important:** The Resolver dependency on `Interpreter` is a separate concern. The Resolver should eventually be factored out of `Stash.Interpreter` into `Stash.Analysis` or `Stash.Core`. This spec does NOT tackle that refactor — it accepts the Resolver dependency for now.

### D4. Closure Scope Display

**Decision: Show upvalue scope as "Closure" in DAP Scopes view**

Currently `BuildFrameScope(ref frame)` returns a flat `VMDebugScope` of locals with globals as its enclosing scope. When a frame has upvalues (`frame.Upvalues is not null`), insert an intermediate scope:

```
Local Scope (locals from frame slots)
  → Closure Scope (captured upvalues from frame.Upvalues[])
    → Global Scope
```

This requires reading upvalue names from chunk metadata. Add `Chunk.UpvalueNames: string[]?` alongside the existing `LocalNames`.

---

## 5. Implementation Plan

### Sub-phase A: IDebugScope Extension + VM Scope Mutation

**Files modified:**

- `Stash.Core/Debugging/IDebugScope.cs` — Add `TryGetValue`, `Contains`, `IsConstant`, `TryAssign` with default implementations
- `Stash.Interpreter/Interpreting/Environment.cs` — Implement the new methods (delegates to existing `Contains`, `IsConstant`, `Assign`)
- `Stash.Bytecode/VMDebugScope.cs` — Rewrite to support both snapshot mode (globals) and live-stack mode (locals)
- `Stash.Bytecode/Chunk.cs` — Add `LocalIsConst: bool[]?` property
- `Stash.Bytecode/ChunkBuilder.cs` — Add `LocalIsConst` assignment
- `Stash.Bytecode/CompilerScope.cs` — Track const-ness per local, expose `GetLocalIsConst()`
- `Stash.Bytecode/Compiler.cs` — Mark locals as const when compiling `ConstDeclStmt`
- `Stash.Bytecode/VirtualMachine.cs` — Update `BuildFrameScope()` to create live-stack VMDebugScope

### Sub-phase B: VM Expression Evaluation

**Files modified:**

- `Stash.Dap/VMDebugAdapter.cs` — Rewrite `EvaluateExpression()` to use VM-native eval. Remove `_evalInterpreter` field and `using Stash.Interpreting`.
- Remove `Environment globals` constructor parameter — it's no longer needed.

### Sub-phase C: DebugSession VM Wiring

**Files modified:**

- `Stash.Dap/DebugSession.cs` — Rewrite `Launch()` to use bytecode pipeline. Replace `_treeWalkInterpreter` field with `_vm: VirtualMachine?` and `_executor: IDebugExecutor?`. Update all usages:
  - Replace `_treeWalkInterpreter` null checks with `_executor` null checks
  - Replace `executor ??= _treeWalkInterpreter` with `executor ??= _executor`
  - Replace `IDebugExecutor executor = _treeWalkInterpreter` with `_executor`
  - Replace `container.Scope is not Environment` with `scope.TryAssign(name, newValue)`
  - Remove `_interpreterThread` in favor of a generically-named `_executionThread`

### Sub-phase D: VM Loaded Sources + Debugger Propagation

**Files modified:**

- `Stash.Bytecode/VirtualMachine.cs` — In `LoadModule()`, call `_debugger?.OnSourceLoaded(resolvedPath)` after resolving the module path. Also propagate `_debugger` to child VMs.

### Sub-phase E: VM Test Harness for DAP

**Files modified:**

- `Stash.Dap/DebugSession.cs` — Configure `vm.TestHarness` and `vm.TestFilter` via `VMContext` properties when `testMode` is true.

### Sub-phase F: Closure Scope Display

**Files modified:**

- `Stash.Bytecode/Chunk.cs` — Add `UpvalueNames: string[]?`
- `Stash.Bytecode/ChunkBuilder.cs` — Add `UpvalueNames` assignment
- `Stash.Bytecode/Compiler.cs` — Populate upvalue names from resolved variable names
- `Stash.Bytecode/VirtualMachine.cs` — Update `BuildFrameScope()` to include closure scope when `frame.Upvalues` is non-null

---

## 6. IDebugScope Extension — Variable Mutation

### New interface methods (with default implementations):

```csharp
public interface IDebugScope
{
    // Existing:
    IEnumerable<KeyValuePair<string, object?>> GetAllBindings();
    IDebugScope? EnclosingScope { get; }
    IEnumerable<IDebugScope> GetScopeChain() { /* existing default */ }

    // New — Mutation support for SetVariable:
    bool Contains(string name) => false;
    bool IsConstant(string name) => false;
    bool TryAssign(string name, object? value) => false;
}
```

### VMDebugScope — Live-Stack Variant

```csharp
internal sealed class VMDebugScope : IDebugScope
{
    // Snapshot mode (globals, closure scopes)
    private readonly KeyValuePair<string, object?>[]? _bindings;

    // Live-stack mode (locals)
    private readonly object?[]? _stack;
    private readonly int _baseSlot;
    private readonly string[]? _names;
    private readonly bool[]? _isConst;
    private readonly int _localCount;

    // Dictionary mode (for mutable globals)
    private readonly Dictionary<string, object?>? _globals;

    private readonly IDebugScope? _enclosing;

    // Snapshot constructor (existing)
    public VMDebugScope(KeyValuePair<string, object?>[] bindings, IDebugScope? enclosing) { ... }

    // Live-stack constructor (new — for frame locals)
    public VMDebugScope(object?[] stack, int baseSlot, int localCount,
                        string[]? names, bool[]? isConst, IDebugScope? enclosing) { ... }

    // Dictionary constructor (new — for mutable globals)
    public VMDebugScope(Dictionary<string, object?> globals, IDebugScope? enclosing) { ... }

    public bool TryAssign(string name, object? value)
    {
        // Live-stack mode: find slot index, write to stack
        if (_stack is not null && _names is not null)
        {
            for (int i = 0; i < _localCount; i++)
            {
                if (_names[i] == name)
                {
                    _stack[_baseSlot + i] = value;
                    return true;
                }
            }
            return false;
        }
        // Dictionary mode: write to globals dict
        if (_globals is not null && _globals.ContainsKey(name))
        {
            _globals[name] = value;
            return true;
        }
        return false;
    }

    public bool Contains(string name) { /* check names array or globals dict */ }
    public bool IsConstant(string name) { /* check _isConst array */ }
}
```

### DebugSession.SetVariable() — After Rewrite

```csharp
// Before (tree-walk cast):
if (container.Scope is not Stash.Interpreting.Environment envScope)
    throw new InvalidOperationException("Cannot set variable in abstract scope.");
if (!envScope.Contains(name)) throw ...;
if (envScope.IsConstant(name)) throw ...;
envScope.Assign(name, newValue);

// After (polymorphic):
if (!container.Scope.Contains(name))
    throw new InvalidOperationException($"Variable '{name}' not found in scope.");
if (container.Scope.IsConstant(name))
    throw new InvalidOperationException($"Cannot modify constant '{name}'.");
if (!container.Scope.TryAssign(name, newValue))
    throw new InvalidOperationException($"Cannot set variable '{name}' in this scope.");
```

---

## 7. VM Expression Evaluation

### VMDebugAdapter.EvaluateExpression() — Rewritten

```csharp
public (object? Value, string? Error) EvaluateExpression(string expression, IDebugScope scope)
{
    try
    {
        // 1. Parse
        var lexer = new Lexer(expression);
        var tokens = lexer.ScanTokens();
        if (lexer.Errors.Count > 0)
            return (null, lexer.Errors[0]);

        var parser = new Parser(tokens);
        var expr = parser.Parse();
        if (parser.Errors.Count > 0)
            return (null, parser.Errors[0]);

        // 2. Compile (no resolver → all variables resolve as globals via OP_LOAD_GLOBAL)
        Chunk chunk = Compiler.CompileExpression(expr);

        // 3. Seed temp VM with scope bindings (outermost first so innermost shadows)
        var tempGlobals = new Dictionary<string, object?>();
        var chain = scope.GetScopeChain().ToList();
        for (int i = chain.Count - 1; i >= 0; i--)  // global → ... → local
        {
            foreach (var (name, value) in chain[i].GetAllBindings())
                tempGlobals[name] = value;
        }

        // 4. Execute in isolated VM
        var tempVm = new VirtualMachine(tempGlobals, CancellationToken.None);
        // Copy I/O streams from main VM
        tempVm.Output = _vm.Output;
        tempVm.ErrorOutput = _vm.ErrorOutput;
        object? result = tempVm.Execute(chunk);
        return (result, null);
    }
    catch (RuntimeError ex)
    {
        return (null, ex.Message);
    }
    catch (Exception ex)
    {
        return (null, ex.Message);
    }
}
```

### Edge Cases

| Expression                 | Behavior                                                      | Notes                                                                                                                                               |
| -------------------------- | ------------------------------------------------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------- |
| `x + 1`                    | Loads `x` from seeded globals, adds 1                         | Works                                                                                                                                               |
| `arr.len(myList)`          | `arr` is a built-in namespace in globals, `myList` from scope | Works — built-ins are in globals                                                                                                                    |
| `fn(a, b) { a + b }(1, 2)` | Lambda creation + call — pure expression                      | Works                                                                                                                                               |
| `myClosure()`              | Calls function from seeded globals                            | Works — function value is seeded, but its original closure environment is lost. May produce unexpected results for functions that capture upvalues. |
| `dict.get(d, "key")`       | Built-in function + scope variable                            | Works                                                                                                                                               |

**Known limitation:** Closure-captured functions will execute correctly as values but may not see their original captured variables (since they're flattened into globals). This is inherent to any "eval in isolated context" approach and matches how most debuggers work — watch expressions see the visible state, not hidden implementation details of closures.

---

## 8. DebugSession Rewrite

### Field Changes

```csharp
// Remove:
private Interpreter? _treeWalkInterpreter;

// Add:
private VirtualMachine? _vm;
private IDebugExecutor? _executor;  // VMDebugAdapter wrapping _vm
```

### Launch() — Rewritten Flow

```
1. Validate script path, store working directory
2. Create VirtualMachine with populated globals (built-in namespaces from BuiltInRegistry)
3. Configure VM: CurrentFile, ScriptArgs, Output/ErrorOutput (DapOutputWriter), EmbeddedMode=true
4. If testMode: set vm context TestHarness + TestFilter
5. Attach debugger: vm.Debugger = this
6. Create VMDebugAdapter(_vm) as IDebugExecutor → _executor
7. Register main thread with _executor
8. Start execution thread:
   a. Wait on _configurationDone gate
   b. Lex + Parse script
   c. Run Resolver (for variable distance annotations)
   d. Compile to bytecode: Compiler.Compile(stmts)
   e. vm.Execute(chunk)
   f. If testMode: finalize TAP reporting
   g. Catch RuntimeError, OperationCanceledException
   h. Send Terminated event
```

### Resolver Dependency

The Resolver currently lives in `Stash.Interpreter` and requires an `Interpreter` instance to call `ResolveStatements()`. To avoid creating a full `Interpreter` just for resolution:

**Option A (Pragmatic):** Create a lightweight `Interpreter` instance solely for resolution — it won't execute anything, just annotate the AST. This matches Phase 8's approach in StashEngine.

**Option B (Clean):** Factor the Resolver out of `Stash.Interpreter`. This is a larger refactor out of scope for this spec.

**Decision:** Option A for now. Create a resolver-only `Interpreter` with no output/debugger configuration. Document this as tech debt for future Resolver extraction.

### Global Population

The VM needs built-in namespaces and functions in its globals dictionary. This is done via the same pattern as `StashEngine.EnsureVM()` and `Program.CreateVMGlobals()`:

```csharp
var globals = new Dictionary<string, object?>();
// Populate from BuiltInRegistry — all 26 namespaces + top-level built-in functions
foreach (var ns in BuiltInRegistry.GetAllNamespaces())
    globals[ns.Name] = ns;
foreach (var fn in BuiltInRegistry.GetTopLevelFunctions())
    globals[fn.Name] = fn;
```

---

## 9. VMDebugAdapter Rewrite

### Before (Phase 7)

```csharp
internal sealed class VMDebugAdapter : IDebugExecutor
{
    private readonly VirtualMachine _vm;
    private readonly Interpreter _evalInterpreter;  // Tree-walk fallback

    public VMDebugAdapter(VirtualMachine vm, Environment globals) { ... }
    public (object? Value, string? Error) EvaluateExpression(string expression, IDebugScope scope)
    {
        var env = new Environment(_evalInterpreter.Globals, 16);
        // ... tree-walk eval ...
    }
}
```

### After (This Spec)

```csharp
internal sealed class VMDebugAdapter : IDebugExecutor
{
    private readonly VirtualMachine _vm;

    public VMDebugAdapter(VirtualMachine vm) { _vm = vm; }

    public IReadOnlyList<CallFrame> CallStack => _vm.DebugCallStack;

    public IDebugScope GlobalScope => _vm.BuildGlobalScope();

    public (object? Value, string? Error) EvaluateExpression(string expression, IDebugScope scope)
    {
        // Pure VM-native eval — no tree-walk dependency
        // (See Section 7 for full implementation)
    }
}
```

**Removals:**

- `using Stash.Interpreting;`
- `_evalInterpreter` field
- `Environment` parameter from constructor

**Note:** `BuildGlobalScope()` must be made `internal` (currently `private`) on VirtualMachine so VMDebugAdapter can call it. Alternatively, expose it through a new `IReadOnlyDictionary<string, object?> Globals` property.

---

## 10. VM Loaded Sources

### Change: VirtualMachine.LoadModule()

Add debugger notification after module path resolution:

```csharp
private Dictionary<string, object?> LoadModule(string modulePath, SourceSpan? span)
{
    // ... path resolution to resolvedPath ...

    // Notify debugger of newly loaded source
    _debugger?.OnSourceLoaded(resolvedPath);

    if (_moduleCache.TryGetValue(resolvedPath, out var cached))
        return cached;

    // ... rest of module loading ...
}
```

### Change: Debugger Propagation to Child VMs

```csharp
var moduleVM = new VirtualMachine(moduleGlobals, _ct)
{
    _moduleLoader = _moduleLoader,
    _moduleCache = _moduleCache,
};
moduleVM._context.CurrentFile = resolvedPath;
moduleVM._context.Output = _context.Output;
moduleVM._context.ErrorOutput = _context.ErrorOutput;
moduleVM._context.Input = _context.Input;
moduleVM.Debugger = _debugger;              // NEW: propagate debugger
moduleVM._debugThreadId = _debugThreadId;   // NEW: same thread for modules
```

With debugger propagation, breakpoints in imported modules will fire correctly — `OnBeforeExecute` in the child VM calls the same `DebugSession` which checks breakpoints against the file path + line.

### Main Script Source Notification

Add at the start of `Execute()` or in DebugSession's launch sequence:

```csharp
_debugger?.OnSourceLoaded(_context.CurrentFile);
```

---

## 11. VM Test Harness Integration

### Change: DebugSession.Launch() — Test Mode

```csharp
if (testMode)
{
    var reporter = new TapReporter(_vm.Output);  // Use VM's output stream
    _vm.TestHarness = reporter;                  // VMContext.TestHarness
    if (testFilter is not null)
        _vm.TestFilter = testFilter.Split(';', StringSplitOptions.RemoveEmptyEntries);
}
```

`VirtualMachine` already has `TestHarness` and `TestFilter` accessible through its `VMContext`. The VM's built-in functions (`test.it`, `test.describe`, etc.) access these via `IInterpreterContext`, which both tree-walk and VM implement.

### Post-Execution TAP Reporting

```csharp
if (_vm.TestHarness is TapReporter tapReporter)
{
    tapReporter.OnRunComplete(tapReporter.PassedCount, tapReporter.FailedCount, tapReporter.SkippedCount);
}
```

This is identical to the current tree-walk path — same TapReporter type, same finalization.

---

## 12. Cross-Cutting Concerns

### 12.1 Project References

After this spec is implemented:

| Project     | Keeps Ref To                                   | Drops Ref To                                  |
| ----------- | ---------------------------------------------- | --------------------------------------------- |
| `Stash.Dap` | `Stash.Core`, `Stash.Bytecode`, `Stash.Stdlib` | —                                             |
| `Stash.Dap` | `Stash.Interpreter` (**temporarily**)          | `Stash.Interpreter` (when Resolver extracted) |

The `Stash.Interpreter` reference survives because the Resolver still lives there. Once the Resolver is extracted (separate future spec), the reference can be dropped entirely.

### 12.2 Parser.Parse() for Expressions

Currently `Parser.Parse()` returns a single `Expr` (for REPL / expression evaluation). Verify this is the correct method for watch expression parsing. It should handle:

- Simple identifiers: `x`
- Property access: `obj.field`
- Method calls: `arr.len(list)`
- Arithmetic: `x + 1`
- String interpolation: `"value: ${x}"`
- Ternary: `x > 0 ? "pos" : "neg"`

### 12.3 Platform Behavior

No cross-platform concerns — this is purely internal architecture. File paths are already normalized by DebugSession via `NormalizePath()`.

### 12.4 LSP Impact

None. LSP uses tree-walk `Interpreter` independently for analysis. This spec only touches DAP.

### 12.5 Performance

- **Expression eval via temp VM:** Creating a VirtualMachine is cheap (stack array allocation). Compiling a small expression is fast. This should be comparable to or faster than tree-walk eval.
- **Conditional breakpoint overhead:** Each breakpoint condition evaluation creates a temp VM. For hot breakpoints (e.g., inside a loop with a condition), this could be measurable. Profile and consider caching the compiled `Chunk` per breakpoint condition string if needed.
- **Live-stack VMDebugScope:** Reading/writing directly to the VM stack array has zero overhead vs. the tree-walk `Environment.Assign()`.

### 12.6 Thread Safety

No change from current model. Single interpreter thread, pause gate synchronization. The VM runs in the same dedicated thread that the tree-walk interpreter used.

---

## 13. Test Strategy

### Existing Tests (Must Continue Passing)

- `Stash.Tests/Dap/DapHandlerTests.cs` — Handler → DebugSession delegation
- `Stash.Tests/Dap/DebugSessionTests.cs` — Breakpoint, stepping, scope, variable formatting
- `Stash.Tests/Dap/DapIntegrationTests.cs` — End-to-end with interpreter
- `Stash.Tests/Bytecode/VMDebugTests.cs` — VM debug hooks (Phase 7)

### New Tests

| Test                                     | What It Validates                                                      |
| ---------------------------------------- | ---------------------------------------------------------------------- |
| **VMEval_SimpleExpression**              | `EvaluateExpression("2 + 3")` returns `5` via VM-native eval           |
| **VMEval_VariableFromScope**             | Expression `"x + 1"` with `x=10` in scope returns `11`                 |
| **VMEval_ShadowedVariable**              | Inner scope's `x` shadows outer scope's `x`                            |
| **VMEval_BuiltInFunction**               | `"arr.len([1,2,3])"` returns `3` — built-ins accessible                |
| **VMEval_ErrorReturnsMessage**           | Invalid expression returns `(null, "error message")`                   |
| **VMSetVariable_Local**                  | Mutate a local variable in a VM frame via `TryAssign()`                |
| **VMSetVariable_Global**                 | Mutate a global variable via `TryAssign()`                             |
| **VMSetVariable_Const**                  | `IsConstant()` returns true, `TryAssign()` blocked                     |
| **VMLoadedSources_Import**               | VM module load fires `OnSourceLoaded` callback                         |
| **VMLoadedSources_MainScript**           | Main script file appears in loaded sources                             |
| **VMDebugSession_Launch**                | End-to-end: Launch with bytecode VM, hit breakpoint, inspect variables |
| **VMDebugSession_StepOver**              | Step over works with VM execution                                      |
| **VMDebugSession_ConditionalBreakpoint** | Conditional breakpoint evaluates via VM expression eval                |
| **VMDebugSession_SetVariable**           | SetVariable works through IDebugScope.TryAssign()                      |
| **VMDebugSession_ClosureScope**          | Closure scope appears in DAP Scopes view with upvalue names            |
| **VMDebugSession_TestMode**              | Test harness runs correctly with VM execution                          |
| **VMDebugSession_ExceptionBreakpoint**   | Break on exception works with VM error handling                        |

### Integration Tests for Parity

Run the existing `DapIntegrationTests` against the VM backend. These tests should pass without modification since the DAP protocol surface is unchanged — only the internal execution engine changes.

---

## 14. Migration Strategy — Tree-Walker Removal Path

This spec is one step in a multi-step migration:

```
Phase 7  ─► Debug abstractions (IDebugger, IDebugScope, IDebugExecutor)         ✅ Done
Phase 8  ─► StashEngine, CLI, REPL, Playground use bytecode VM                  ✅ Done
THIS SPEC ─► DAP uses bytecode VM exclusively for execution + eval               ⬜ This
FUTURE   ─► Extract Resolver from Stash.Interpreter into Stash.Analysis          ⬜ Separate spec
FUTURE   ─► Drop Stash.Dap → Stash.Interpreter project reference                ⬜ After Resolver extraction
FUTURE   ─► Remove tree-walk Interpreter class entirely                          ⬜ Final step
```

After this spec, `Stash.Interpreter` is still referenced by `Stash.Dap` **only** for the Resolver. No `new Interpreter()` calls remain for execution or evaluation purposes.

---

## 15. Risk Register

| Risk                                                                                   | Likelihood | Impact | Mitigation                                                                                                                                                                     |
| -------------------------------------------------------------------------------------- | ---------- | ------ | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| **Watch expressions with closures behave differently in temp VM vs. original context** | Medium     | Low    | Document as known limitation. Most watch expressions are simple value inspections. Edge case: calling a closure that captures a mutable upvalue.                               |
| **Conditional breakpoint perf regression from creating temp VM per check**             | Low        | Medium | Profile early. If measurable, cache compiled Chunk per condition string and reuse across evaluations. VM creation is cheap (stack allocation).                                 |
| **Resolver dependency prevents clean Stash.Interpreter removal**                       | Certain    | Low    | Accepted tech debt. Resolver extraction is a separate, well-defined refactor. Doesn't block this spec's value.                                                                 |
| **VMDebugScope live-stack write corrupts VM state**                                    | Low        | High   | Live-stack mode only activated when paused (gate blocks VM thread). Stack writes are atomic single-slot operations. Add assertions that VM is paused before allowing mutation. |
| **Module debugger propagation causes double breakpoint hits**                          | Low        | Medium | Child VMs share the same debugger but different thread IDs. DebugSession already handles per-thread state. Test with multi-module scripts.                                     |

---

## 16. Decision Log

| #   | Decision                                                 | Alternatives Considered                                                                   | Rationale                                                                                                                       |
| --- | -------------------------------------------------------- | ----------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------- |
| D1  | VM-native expression eval via temp VM with scope seeding | (a) Keep tree-walk eval, (b) Eval in main VM, (c) Build standalone expression interpreter | (a) blocks tree-walker removal, (b) corrupts main VM state, (c) more code than leveraging existing Compiler.CompileExpression() |
| D2  | Extend IDebugScope with mutation methods (default impls) | (a) IMutableDebugScope sub-interface, (b) Cast to concrete type per backend               | (a) unnecessary hierarchy split, (b) doesn't work polymorphically — defeats the abstraction                                     |
| D3  | Create resolver-only Interpreter in Launch()             | Factor Resolver out of Stash.Interpreter                                                  | Refactoring Resolver is out of scope; resolver-only instance has zero execution overhead                                        |
| D4  | Show upvalue scope as "Closure" in DAP                   | Flat locals-only view                                                                     | Tree-walker shows closure scopes; users expect to see captured variables separately                                             |
