# Bytecode VM — Project Refactoring & Decomposition

**Status:** Backlog — Refactoring Spec
**Created:** 2026-04-06
**Scope:** `Stash.Bytecode/` — all 23 source files (~9,400 LOC)

---

## 1. Purpose

The Stash bytecode VM backend is functionally complete but has grown organically during an 8-phase implementation push. Two files (`VirtualMachine.cs` at 3,485 lines and `Compiler.cs` at 2,904 lines) account for 68% of the project. The dispatch loop alone (`RunInner`) is 2,206 lines — a single method with a switch statement spanning every opcode.

This spec plans a systematic decomposition targeting:

- **Every file under 500 lines** (hard cap)
- **Separation of concerns** — one responsibility per file
- **Elimination of code duplication** — especially in `RuntimeOps.cs` comparisons
- **Documentation** — XML doc comments on all public/internal types and methods
- **Reusable components** — extract patterns used in multiple places

### Non-Goals

- No behavioral changes. Every test must pass identically before and after.
- No new features. No opcode changes, no API additions.
- No namespace reorganization. Everything stays in `namespace Stash.Bytecode`.
- No performance regressions. Benchmark before/after on `bench_algorithms.stash`.

---

## 2. Current State Inventory

| File                      | Lines | Well-Scoped? | Verdict                                                   |
| ------------------------- | ----: | :----------: | --------------------------------------------------------- |
| VirtualMachine.cs         | 3,485 |    **NO**    | Split into 10+ partial class files                        |
| Compiler.cs               | 2,904 |    **NO**    | Split into 6+ partial class files                         |
| RuntimeOps.cs             |   640 | **Partial**  | Consolidate duplicated comparison/arithmetic logic        |
| StashEngine.cs            |   540 |    **NO**    | Extract responsibilities into focused classes             |
| OpCode.cs                 |   380 | **Partial**  | OK as-is; add stack-effect metadata later (out of scope)  |
| ChunkBuilder.cs           |   280 |     Yes      | Minor: extract magic numbers to named constants           |
| VMContext.cs              |   280 |    **NO**    | Simplify; document the god-object tradeoff                |
| CompilerScope.cs          |   210 |    Mostly    | Remove redundant tracking dictionaries                    |
| VMDebugScope.cs           |   150 |    **NO**    | Replace 3-constructor pattern with static factory methods |
| Disassembler.cs           |   135 |     Yes      | No changes needed                                         |
| VMTemplateEvaluator.cs    |   115 |    Mostly    | Minor cleanup                                             |
| StashValue.cs             |    95 |     Yes      | No changes needed (exemplary code)                        |
| SourceMap.cs              |    90 |     Yes      | Add sorting validation in constructor                     |
| Chunk.cs                  |    75 |     Yes      | No changes needed                                         |
| Upvalue.cs                |    66 |    Mostly    | Add XML docs; document stack-resize contract              |
| Metadata.cs               |    50 |     Yes      | No changes needed (small records)                         |
| SynchronizedTextWriter.cs |    45 |    **NO**    | Override more TextWriter methods                          |
| VMBoundMethod.cs          |    40 |     Yes      | No changes needed                                         |
| ExtensionRegistry.cs      |    25 |     Yes      | Add thread-safety (ConcurrentDictionary)                  |
| CallFrame.cs              |    24 |     Yes      | No changes needed                                         |
| CompileError.cs           |    20 |     Yes      | No changes needed                                         |
| UpvalueDescriptor.cs      |    18 |     Yes      | No changes needed                                         |
| StashValueTag.cs          |    10 |     Yes      | No changes needed                                         |

**Summary:** 5 files need major surgery, 4 need moderate work, 14 are fine as-is.

---

## 3. Phase 1 — VirtualMachine.cs Decomposition

### 3.1 Strategy: Partial Classes

The `VirtualMachine` is a `sealed class` with ~15 private fields that are tightly coupled. Extracting into separate classes would require either leaking internals or passing excessive parameters. **Partial classes** preserve encapsulation while splitting the file:

```
Stash.Bytecode/
  VM/
    VirtualMachine.cs                     # Fields, constructor, properties, Execute/ExecuteRepl, stack ops
    VirtualMachine.Dispatch.cs            # Run, RunDebug, RunInner top-level dispatch routing
    VirtualMachine.Arithmetic.cs          # Arithmetic + bitwise opcode cases
    VirtualMachine.ControlFlow.cs         # Jump, loop, exception handling opcode cases
    VirtualMachine.Variables.cs           # Global/local/upvalue load/store opcode cases
    VirtualMachine.Functions.cs           # Call, closure, return, async + CallValue
    VirtualMachine.Collections.cs         # Array, dict, struct, field/index access opcode cases
    VirtualMachine.Strings.cs             # Interpolation, command, pipe, redirect opcode cases
    VirtualMachine.TypeOps.cs             # Is, typeof, iterator opcode cases
    VirtualMachine.Modules.cs             # Import opcodes + LoadModule + ResolvePackagePath
    VirtualMachine.Debug.cs               # BuildFrameScope, BuildGlobalScope, RunDebug, RunUntilFrameDebug
    VirtualMachine.Process.cs             # ExecCaptured, ExecPassthrough
```

### 3.2 RunInner Decomposition

The 2,206-line `RunInner` method is the core problem. Approach:

**Step 1 — Extract opcode handlers as private methods.** Each logical group of opcodes becomes a handler method that the switch dispatches to:

```csharp
// In VirtualMachine.Dispatch.cs — the main loop stays here
private void RunInner(ref CallFrame frame, byte[] code, object[] constants)
{
    while (true)
    {
        OpCode op = (OpCode)ReadByte(code, ref frame);
        switch (op)
        {
            // Constants & Stack
            case OpCode.Constant: Push(constants[ReadU16(code, ref frame)]); break;
            case OpCode.Null: Push(StashValue.Null); break;
            case OpCode.True: Push(StashValue.True); break;
            case OpCode.False: Push(StashValue.False); break;
            case OpCode.Pop: Pop(); break;
            case OpCode.Dup: Push(Peek(0)); break;

            // Arithmetic (dispatched to VirtualMachine.Arithmetic.cs)
            case OpCode.Add: ExecuteAdd(ref frame); break;
            case OpCode.Subtract: ExecuteSubtract(ref frame); break;
            // ... etc

            // Control Flow (dispatched to VirtualMachine.ControlFlow.cs)
            case OpCode.Jump: ExecuteJump(code, ref frame); break;
            case OpCode.JumpIfFalse: ExecuteJumpIfFalse(code, ref frame); break;
            // ... etc
        }
    }
}
```

**Step 2 — Move handler methods to category files.** Each `Execute*` method lives in the partial class file for its category. Trivial opcodes (Push/Pop/Dup/Constant) stay inline in the switch.

**Step 3 — Extract complex standalone methods:**

| Method                           | Current Lines | Target File                     |
| -------------------------------- | ------------- | ------------------------------- |
| `CallValue` (~230 lines)         | VM.cs         | `VirtualMachine.Functions.cs`   |
| `SpawnAsyncFunction` (~55 lines) | VM.cs         | `VirtualMachine.Functions.cs`   |
| `GetFieldValue` (~100 lines)     | VM.cs         | `VirtualMachine.Collections.cs` |
| `SetFieldValue` (~12 lines)      | VM.cs         | `VirtualMachine.Collections.cs` |
| `GetIndexValue` (~27 lines)      | VM.cs         | `VirtualMachine.Collections.cs` |
| `SetIndexValue` (~22 lines)      | VM.cs         | `VirtualMachine.Collections.cs` |
| `CheckIsType` (~20 lines)        | VM.cs         | `VirtualMachine.TypeOps.cs`     |
| `CreateIterator` (~15 lines)     | VM.cs         | `VirtualMachine.TypeOps.cs`     |
| `LoadModule` (~90 lines)         | VM.cs         | `VirtualMachine.Modules.cs`     |
| `ResolvePackagePath` (~90 lines) | VM.cs         | `VirtualMachine.Modules.cs`     |
| `ExecCaptured` (~42 lines)       | VM.cs         | `VirtualMachine.Process.cs`     |
| `ExecPassthrough` (~25 lines)    | VM.cs         | `VirtualMachine.Process.cs`     |
| `BuildFrameScope` (~40 lines)    | VM.cs         | `VirtualMachine.Debug.cs`       |
| `BuildGlobalScope` (~4 lines)    | VM.cs         | `VirtualMachine.Debug.cs`       |
| `RunDebug` (~42 lines)           | VM.cs         | `VirtualMachine.Debug.cs`       |
| `RunUntilFrameDebug` (~35 lines) | VM.cs         | `VirtualMachine.Debug.cs`       |

### 3.3 Estimated File Sizes After Split

| File                            | Est. Lines | Contents                                                                                                                                                |
| ------------------------------- | ---------: | ------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `VirtualMachine.cs`             |       ~200 | Fields, constructor, properties, Execute/ExecuteRepl, Push/Pop/Peek, GrowStack, ReadByte/ReadU16/ReadI16, GetCurrentSpan, CaptureUpvalue, CloseUpvalues |
| `VirtualMachine.Dispatch.cs`    |       ~250 | Run, RunInner (switch skeleton only — handler calls), RunUntilFrame                                                                                     |
| `VirtualMachine.Arithmetic.cs`  |       ~100 | Add, Subtract, Multiply, Divide, Modulo, Power, Negate, BitAnd/Or/Xor/Not, ShiftLeft/Right handler wrappers                                             |
| `VirtualMachine.ControlFlow.cs` |       ~250 | Jump, JumpIfFalse, JumpIfTrue, Loop, PushHandler, PopHandler, Throw, exception dispatch, And/Or short-circuit                                           |
| `VirtualMachine.Variables.cs`   |       ~150 | GetGlobal, SetGlobal, GetLocal, SetLocal, GetUpvalue, SetUpvalue, CloseUpvalue, InitGlobal, InitConstGlobal                                             |
| `VirtualMachine.Functions.cs`   |       ~400 | CallValue, Call/CallSpread, Closure, Return, SpawnAsyncFunction, ExecuteVMFunctionInline, CallClosure, ArgMark                                          |
| `VirtualMachine.Collections.cs` |       ~350 | Array, Dict, StructInit, StructDecl, EnumDecl, InterfaceDecl, Extend, GetField, SetField, GetIndex, SetIndex, Destructure, Spread                       |
| `VirtualMachine.Strings.cs`     |       ~150 | Interpolate, Command, Pipe, Redirect opcode handlers                                                                                                    |
| `VirtualMachine.TypeOps.cs`     |       ~100 | CheckIsType, CreateIterator, Is, Typeof handlers                                                                                                        |
| `VirtualMachine.Modules.cs`     |       ~250 | Import/ImportAs opcode handling, LoadModule, ResolvePackagePath                                                                                         |
| `VirtualMachine.Debug.cs`       |       ~150 | RunDebug, RunUntilFrameDebug, BuildFrameScope, BuildGlobalScope                                                                                         |
| `VirtualMachine.Process.cs`     |        ~80 | ExecCaptured, ExecPassthrough                                                                                                                           |

**Total:** ~2,430 lines (reduction from 3,485 due to reduced nesting and cleaner structure)
**Largest file:** ~400 lines (Functions) — **within the 500-line cap**

### 3.4 Risks

- **Performance:** Extracting opcodes into method calls adds call overhead per instruction. Mitigation: mark all handler methods `[MethodImpl(MethodImplOptions.AggressiveInlining)]`. The JIT should inline small handlers, and the switch dispatch is already branch-predicted.
- **`ref` parameter threading:** The `CallFrame` is passed by `ref` and the code/constants are locals from the frame. Handler methods need access to these. Solution: pass `ref frame`, `code`, `constants` as parameters (or access via `_frames[_frameCount - 1]`).
- **Field ordering in partial classes:** C# partial classes share all fields. No risk, but each file should have a comment header stating which category of operations it contains.

---

## 4. Phase 2 — Compiler.cs Decomposition

### 4.1 Strategy: Partial Classes

Same rationale as the VM — the `Compiler` has private fields (`_builder`, `_scope`, `_enclosing`, `_loops`, `_activeFinally`) shared across all visitor methods. Partial classes are the correct split.

```
Stash.Bytecode/
  Compilation/
    Compiler.cs                        # Fields, constructor, Compile/CompileExpression, nested types
    Compiler.Helpers.cs                # EmitVariable, ResolveUpvalue, EmitScopeCleanup, EmitScopePops,
                                       # EmitPendingFinally, PatchBreakJumps, PatchContinueJumps,
                                       # EmitDefaultPrologue, CompileFunction
    Compiler.Declarations.cs           # VisitFnDeclStmt, VisitStructDeclStmt, VisitEnumDeclStmt,
                                       # VisitInterfaceDeclStmt, VisitExtendStmt, VisitImportStmt,
                                       # VisitImportAsStmt, VisitDestructureStmt
    Compiler.ControlFlow.cs            # VisitIfStmt, VisitWhileStmt, VisitDoWhileStmt, VisitForStmt,
                                       # VisitForInStmt, VisitBreakStmt, VisitContinueStmt, VisitReturnStmt,
                                       # VisitTryCatchStmt, VisitElevateStmt
    Compiler.Expressions.cs            # VisitLiteralExpr, VisitIdentifierExpr, VisitGroupingExpr,
                                       # VisitUnaryExpr, VisitBinaryExpr, VisitTernaryExpr, VisitAssignExpr,
                                       # VisitCallExpr, VisitLambdaExpr, VisitUpdateExpr,
                                       # VisitNullCoalesceExpr, VisitSwitchExpr, VisitIsExpr,
                                       # VisitAwaitExpr, VisitSpreadExpr, VisitTryExpr, VisitRetryExpr
    Compiler.Collections.cs            # VisitArrayExpr, VisitIndexExpr, VisitIndexAssignExpr,
                                       # VisitDictLiteralExpr, VisitStructInitExpr, VisitDotExpr,
                                       # VisitDotAssignExpr, VisitRangeExpr
    Compiler.Strings.cs                # VisitInterpolatedStringExpr, VisitCommandExpr, VisitPipeExpr,
                                       # VisitRedirectExpr
                                       # VisitExprStmt, VisitVarDeclStmt, VisitConstDeclStmt,
                                       # VisitBlockStmt, VisitThrowStmt
```

### 4.2 Refactoring Within Methods

Three methods are excessively long and should be decomposed:

**`VisitTryCatchStmt` (139 lines):**

- Extract `EmitCatchHandler(...)` — the catch block emission pattern
- Extract `EmitFinallyBlock(...)` — the finally cleanup emission pattern
- The remaining orchestration stays in the visitor method

**`VisitRetryExpr` (154 lines):**

- Extract `CompileRetryBody(...)` — compiles the retry body as a closure
- Extract `CompileOnRetryClause(...)` — compiles the onRetry callback
- Extract `CompileUntilClause(...)` — compiles the until predicate
- The remaining setup (maxAttempts, options) stays in the visitor

**`VisitUpdateExpr` (91 lines):**

- Extract `EmitIdentifierUpdate(...)` — ++/-- on identifiers
- Extract `EmitFieldUpdate(...)` — ++/-- on dot expressions
- Extract `EmitIndexUpdate(...)` — ++/-- on index expressions

### 4.3 DRY: Extract Common Patterns

**Top-level declaration pattern** (repeated 4×: fn, struct, enum, interface):

```csharp
private void EmitTopLevelDeclaration(string name)
{
    // Dup + StoreGlobal pattern used for all top-level declarations
    _builder.Emit(OpCode.Dup);
    int nameIdx = _builder.AddConstant(name);
    _builder.Emit(OpCode.SetGlobal, (ushort)nameIdx);
}
```

**Method compilation with self parameter** (repeated in struct + extend):

```csharp
private Chunk CompileMethodWithSelf(FnDeclStmt method, string structName)
{
    // Shared pattern: prepend "self" to parameter list, compile as function
}
```

### 4.4 Estimated File Sizes After Split

| File                       | Est. Lines | Contents                                                                                                                                                 |
| -------------------------- | ---------: | -------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `Compiler.cs`              |       ~120 | Fields, constructor, Compile, CompileExpression, CompileStmt, CompileExpr, LoopContext, FinallyInfo                                                      |
| `Compiler.Helpers.cs`      |       ~250 | EmitVariable, ResolveUpvalue, scope/finally/break/continue helpers, CompileFunction, EmitDefaultPrologue, EmitTopLevelDeclaration, CompileMethodWithSelf |
| `Compiler.Declarations.cs` |       ~350 | 8 declaration visitors (fn, struct, enum, interface, extend, import, importAs, destructure) + VarDeclStmt, ConstDeclStmt, ExprStmt, BlockStmt, ThrowStmt |
| `Compiler.ControlFlow.cs`  |       ~400 | 10 control flow visitors (if, while, doWhile, for, forIn, break, continue, return, tryCatch, elevate) with extracted sub-methods                         |
| `Compiler.Expressions.cs`  |       ~450 | 17 expression visitors including retry, switch, update with extracted sub-methods                                                                        |
| `Compiler.Collections.cs`  |       ~200 | 8 collection/access visitors                                                                                                                             |
| `Compiler.Strings.cs`      |       ~100 | 4 string/command visitors                                                                                                                                |

**Total:** ~1,870 lines (reduction from 2,904 due to DRY extraction and reduced nesting)
**Largest file:** ~450 lines (Expressions) — **within the 500-line cap**

---

## 5. Phase 3 — RuntimeOps.cs Consolidation

### 5.1 Problem: Comparison Method Duplication

The four comparison methods (`LessThan`, `LessEqual`, `GreaterThan`, `GreaterEqual`) are 85% identical — ~140 lines of duplicated type-checking logic that only differs in the comparison operator. The same pattern exists in arithmetic methods.

### 5.2 Solution: Unified Compare Method

Introduce a private `Compare` method returning an `int`:

```csharp
/// <summary>
/// Compares two StashValues. Returns negative, zero, or positive.
/// Throws RuntimeError for incomparable types.
/// </summary>
private static int Compare(StashValue left, StashValue right, SourceSpan span)
{
    // Fast path: int/int
    if (left.IsInt && right.IsInt)
        return left.AsInt.CompareTo(right.AsInt);

    // Numeric promotion
    if (left.IsNumeric && right.IsNumeric)
        return ToDouble(left).CompareTo(ToDouble(right));

    // Object comparisons (Duration, ByteSize, SemVer, IpAddress)
    object? lObj = left.IsObj ? left.AsObj : null;
    object? rObj = right.IsObj ? right.AsObj : null;

    return (lObj, rObj) switch
    {
        (string ls, string rs)           => string.Compare(ls, rs, StringComparison.Ordinal),
        (StashIpAddress l, StashIpAddress r) => l.CompareTo(r),
        (StashDuration l, StashDuration r)   => l.CompareTo(r),
        (StashByteSize l, StashByteSize r)   => l.CompareTo(r),
        (StashSemVer l, StashSemVer r)       => l.CompareTo(r),
        _ => throw new RuntimeError($"Cannot compare {RuntimeValues.TypeName(left.ToObject())} and {RuntimeValues.TypeName(right.ToObject())}.", span)
    };
}

// Each public method becomes a one-liner:
public static bool LessThan(StashValue left, StashValue right, SourceSpan span)
    => Compare(left, right, span) < 0;

public static bool LessEqual(StashValue left, StashValue right, SourceSpan span)
    => Compare(left, right, span) <= 0;

public static bool GreaterThan(StashValue left, StashValue right, SourceSpan span)
    => Compare(left, right, span) > 0;

public static bool GreaterEqual(StashValue left, StashValue right, SourceSpan span)
    => Compare(left, right, span) >= 0;
```

**Impact:** Eliminates ~120 lines of duplication. Four 35-line methods become four 2-line methods + one 30-line `Compare`.

### 5.3 Extract Object Extraction Helper

The pattern `object? lObj = left.IsObj ? left.AsObj : null;` appears 8 times. Extract:

```csharp
[MethodImpl(MethodImplOptions.AggressiveInlining)]
private static (object? left, object? right) ExtractObjects(StashValue left, StashValue right)
    => (left.IsObj ? left.AsObj : null, right.IsObj ? right.AsObj : null);
```

### 5.4 Estimated Result

`RuntimeOps.cs` goes from 640 lines to ~380 lines. No split needed — within the 500-line cap.

### 5.5 Risk: IsFalsy/IsEqual/Stringify Wrappers

Three methods are thin wrappers delegating to `RuntimeValues`:

```csharp
public static bool IsFalsy(StashValue value) => !RuntimeValues.IsTruthy(value.ToObject());
public static bool IsEqual(StashValue left, StashValue right) => RuntimeValues.IsEqual(left.ToObject(), right.ToObject());
public static string Stringify(StashValue value) => RuntimeValues.Stringify(value.ToObject());
```

These exist to provide a `StashValue`-typed API so the VM doesn't need to call `.ToObject()` at every usage site. The boxing overhead is the real cost, but eliminating these wrappers would spread `.ToObject()` calls throughout the VM. **Decision: Keep the wrappers.** They're cheap, inlined, and provide a cleaner VM-side API. If/when `StashValue`-native truthiness/equality is needed for performance, these methods are the single place to add it.

---

## 6. Phase 4 — StashEngine.cs Decomposition

### 6.1 Problem: Five Responsibilities in One Class

`StashEngine` currently handles:

1. **Configuration** — Output, ErrorOutput, Input, StepLimit, CancellationToken, OnOutput/OnError callbacks
2. **Compilation** — Lex → Parse → Resolve → Compile pipeline
3. **Execution** — Run, Evaluate, Run(StashScript)
4. **Type conversion** — ToDictionary, ToList, CreateDictionary, ToStashValue, etc.
5. **VM lifecycle** — VM lazy creation, settings sync, global management

### 6.2 Approach: Keep Single Class, Extract Static Utilities

Full decomposition into 5 classes would over-engineer the embedding API. The `StashEngine` is the public surface for C# embedders — a single point of entry is valuable. Instead:

**A. Extract `StashTypeConverter` (static class):**

```csharp
/// <summary>
/// Converts between .NET types and Stash runtime types.
/// </summary>
public static class StashTypeConverter
{
    public static Dictionary<string, object?> ToDictionary(object? value) { ... }
    public static List<object?> ToList(object? value) { ... }
    public static object? CreateDictionary(Dictionary<string, object?> values) { ... }
    // ... etc
}
```

**B. Extract `StashCompilationPipeline` (internal class):**

```csharp
/// <summary>
/// Lexes, parses, resolves, and compiles Stash source to bytecode.
/// </summary>
internal sealed class StashCompilationPipeline
{
    public Chunk Compile(string source, string fileName = "<script>") { ... }
    public StashScript CompileScript(string source, string fileName = "<script>") { ... }
}
```

**C. Simplify StashEngine** to delegate to the above.

### 6.3 Estimated Result

| File                          | Est. Lines |
| ----------------------------- | ---------: |
| `StashEngine.cs`              |       ~250 |
| `StashCompilationPipeline.cs` |       ~120 |
| `StashTypeConverter.cs`       |       ~100 |

**Total:** ~470 lines (down from 540, with better separation)

---

## 7. Phase 5 — Smaller File Improvements

### 7.1 VMDebugScope.cs — Factory Method Redesign

**Problem:** Three constructors with completely different semantics (snapshot, live-stack, dictionary). Caller must know which overload to use and the internal mode it selects.

**Solution:** Replace constructors with static factory methods:

```csharp
internal sealed class VMDebugScope : IDebugScope
{
    private VMDebugScope(...) { ... } // Private constructor

    /// <summary>Creates an immutable scope from a snapshot of captured upvalue names/values.</summary>
    public static VMDebugScope FromSnapshot(string[] names, StashValue[] values) { ... }

    /// <summary>Creates a live scope backed by the VM stack for a specific call frame.</summary>
    public static VMDebugScope FromStack(StashValue[] stack, int baseSlot, string[] names, bool[] isConst) { ... }

    /// <summary>Creates a scope backed by the globals dictionary.</summary>
    public static VMDebugScope FromGlobals(Dictionary<string, object?> globals, HashSet<string> constGlobals) { ... }
}
```

### 7.2 VMContext.cs — Documentation, Not Decomposition

**Problem:** VMContext implements 5 logical contexts (execution, process, test, template, file-watch). It's a god object.

**Decision: Accept the tradeoff.** VMContext exists because child VMs need to share a single context object. Splitting it would require child VMs to manage 5 separate context references — more complex, not simpler. Instead:

- Add **XML doc sections** clearly delineating each logical context
- Add **region comments** (`// --- Process Tracking ---`, etc.)
- Document the **fork semantics** (which state is shared vs copied)

### 7.3 ExtensionRegistry.cs — Thread Safety

Replace `Dictionary<string, Dictionary<string, IStashCallable>>` with `ConcurrentDictionary` for the outer level:

```csharp
private readonly ConcurrentDictionary<string, Dictionary<string, IStashCallable>> _methods = new();
```

Inner dictionaries are write-once-read-many (extensions registered at startup, read during execution), so they don't need concurrent access — but the outer lookup does because `LoadModule` can run concurrently.

### 7.4 SynchronizedTextWriter.cs — Extended Coverage

Override the additional `TextWriter` methods that could bypass the lock:

- `Write(char)`, `Write(char[])`, `Write(char[], int, int)`
- `Write(ReadOnlySpan<char>)`
- `WriteLine(char)`, `WriteLine(char[])`
- `WriteAsync` / `WriteLineAsync` variants (throw `NotSupportedException` — synchronized writes should not be async)

### 7.5 CompilerScope.cs — Remove Redundant Tracking

The class maintains both:

- `_locals` list (List of Local records — `Name`, `Depth`, `IsInitialized`, `IsCaptured`)
- `_localNamesBySlot` dictionary (slot → name)
- `_localConstBySlot` dictionary (slot → is-const)

The two dictionaries duplicate data already in `_locals`. Consolidate:

```csharp
// Before: 3 data structures
private readonly List<Local> _locals;
private readonly Dictionary<int, string> _localNamesBySlot;
private readonly Dictionary<int, bool> _localConstBySlot;

// After: 1 data structure (Local already has Name)
private readonly List<Local> _locals;
// Add IsConst to Local record:
public readonly record struct Local(string Name, int Depth, bool IsInitialized, bool IsCaptured, bool IsConst);
```

Methods like `GetPeakLocalNames()` and `GetPeakLocalIsConst()` would iterate `_locals` directly.

### 7.6 SourceMap.cs — Sorting Validation

Add a debug assertion in the constructor:

```csharp
#if DEBUG
for (int i = 1; i < entries.Length; i++)
    Debug.Assert(entries[i].Offset >= entries[i - 1].Offset, "SourceMap entries must be sorted by offset");
#endif
```

### 7.7 VMFunction.cs — Remove Misleading Interface

`VMFunction` implements `IStashCallable` but `Call()` throws `NotSupportedException`. The VM handles `VMFunction` as a special case in `CallValue()`. Options:

- **Option A:** Remove `IStashCallable` from `VMFunction`. Audit all `is IStashCallable` checks to ensure they don't rely on VMFunction matching.
- **Option B:** Keep it (status quo) and document why with an XML comment.

**Decision: Option B.** The interface is used in `StashEngine` type conversions and some stdlib functions that check `is IStashCallable`. Removing it requires auditing every check site across 4 projects. The cost exceeds the benefit. Add a clear `/// <remarks>` comment.

---

## 8. Directory Structure After Refactoring

```
Stash.Bytecode/
  VM/
    VirtualMachine.cs                    (~200 lines)
    VirtualMachine.Dispatch.cs           (~250 lines)
    VirtualMachine.Arithmetic.cs         (~100 lines)
    VirtualMachine.ControlFlow.cs        (~250 lines)
    VirtualMachine.Variables.cs          (~150 lines)
    VirtualMachine.Functions.cs          (~400 lines)
    VirtualMachine.Collections.cs        (~350 lines)
    VirtualMachine.Strings.cs            (~150 lines)
    VirtualMachine.TypeOps.cs            (~100 lines)
    VirtualMachine.Modules.cs            (~250 lines)
    VirtualMachine.Debug.cs              (~150 lines)
    VirtualMachine.Process.cs            (~80 lines)
  Compilation/
    Compiler.cs                          (~120 lines)
    Compiler.Helpers.cs                  (~250 lines)
    Compiler.Declarations.cs             (~350 lines)
    Compiler.ControlFlow.cs              (~400 lines)
    Compiler.Expressions.cs              (~450 lines)
    Compiler.Collections.cs              (~200 lines)
    Compiler.Strings.cs                  (~100 lines)
    CompilerScope.cs                     (~180 lines)
    CompileError.cs                      (~20 lines)
  Runtime/
    RuntimeOps.cs                        (~380 lines)
    StashValue.cs                        (~95 lines)
    StashValueTag.cs                     (~10 lines)
    CallFrame.cs                         (~24 lines)
    Upvalue.cs                           (~66 lines)
    UpvalueDescriptor.cs                 (~18 lines)
    VMFunction.cs                        (~35 lines)
    VMBoundMethod.cs                     (~40 lines)
    VMContext.cs                          (~290 lines)
    VMDebugScope.cs                      (~140 lines)
    ExtensionRegistry.cs                 (~30 lines)
    SynchronizedTextWriter.cs            (~70 lines)
    VMTemplateEvaluator.cs               (~115 lines)
  Bytecode/
    OpCode.cs                            (~380 lines)
    Chunk.cs                             (~75 lines)
    ChunkBuilder.cs                      (~280 lines)
    SourceMap.cs                         (~95 lines)
    Disassembler.cs                      (~135 lines)
    Metadata.cs                          (~50 lines)
  StashEngine.cs                         (~250 lines)
  StashCompilationPipeline.cs            (~120 lines)
  StashTypeConverter.cs                  (~100 lines)
```

**File count:** 23 → 39 files
**Total lines:** ~9,400 → ~6,500 (estimated, due to DRY + reduced nesting)
**Largest file:** ~450 lines (`Compiler.Expressions.cs`) — within 500-line cap
**All files use `namespace Stash.Bytecode;`** — no breaking changes

---

## 9. Implementation Order

The phases should be executed sequentially. Each phase must pass the full test suite before proceeding to the next.

| Step | Phase                                                                                                | Est. Files Changed | Risk     | Dependency |
| ---- | ---------------------------------------------------------------------------------------------------- | ------------------ | -------- | ---------- |
| 1    | Create directory structure (`VM/`, `Compilation/`, `Runtime/`, `Bytecode/`) and move unchanged files | 23 (moves)         | Low      | None       |
| 2    | VirtualMachine.cs → 12 partial class files                                                           | 1→12               | **High** | Step 1     |
| 3    | Compiler.cs → 7 partial class files                                                                  | 1→7                | **High** | Step 1     |
| 4    | RuntimeOps.cs consolidation                                                                          | 1                  | Medium   | None       |
| 5    | StashEngine.cs decomposition                                                                         | 1→3                | Medium   | None       |
| 6    | Smaller file improvements (7.1–7.7)                                                                  | ~6                 | Low      | None       |
| 7    | XML documentation pass on all public/internal types                                                  | ~20                | Low      | Steps 1-6  |

Steps 4, 5, and 6 are independent of each other and can be parallelized.

---

## 10. Verification Checklist

Each step must satisfy:

- [ ] `dotnet build` succeeds with zero warnings
- [ ] `dotnet test` passes all ~2,000+ tests
- [ ] No file exceeds 500 lines (verified via `wc -l`)
- [ ] No new public API surface (all new types/methods are `internal` or `private`)
- [ ] Benchmark `bench_algorithms.stash` shows no regression >5%
- [ ] `git diff --stat` confirms no content changes in non-target files

---

## 11. Decision Log

| Decision                           | Alternatives Considered                                          | Rationale                                                                                                                                                                    |
| ---------------------------------- | ---------------------------------------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Partial classes for VM + Compiler  | Separate classes with shared state object; Static dispatch table | Partial classes preserve encapsulation, require zero API changes, and are idiomatic C# for large visitors. Separate classes leak internals. Dispatch tables add indirection. |
| Keep VMContext as god object       | Split into 5 strategy classes                                    | VMContext is shared by child VMs via single reference. 5 separate context objects = 5 references to manage during fork. Complexity increase exceeds benefit.                 |
| Subdirectories with same namespace | Flat directory; New sub-namespaces                               | Flat directory at 39 files is hard to navigate. Sub-namespaces break public API. Same namespace + subdirectories is the pragmatic middle ground.                             |
| Keep IStashCallable on VMFunction  | Remove interface                                                 | Removing requires cross-project audit of 4+ assemblies. Cost exceeds benefit of removing a misleading but documented interface.                                              |
| Inline trivial opcodes in switch   | Extract all opcodes to methods                                   | Trivial opcodes (Pop, Push, Dup) as method calls add overhead for zero readability benefit. Keep them inline; only extract complex handlers.                                 |
| Unified Compare() for comparisons  | Keep 4 separate methods; Generic comparer                        | Unified Compare() eliminates 120 lines of duplication. Generic comparer is over-engineered for 6 comparable types.                                                           |

---

## 12. Out of Scope (Future Work)

These improvements were identified during analysis but are explicitly deferred:

1. **OpCode metadata enrichment** — stack-effect annotations, side-effect flags, category lookup. Useful for static analysis and tooling but a separate spec.
2. **StashValue equality/hash overrides** — needed if `StashValue` becomes a dictionary key. Not currently the case.
3. **Integer overflow checking** — arithmetic on `long` values can silently overflow. Language design decision, not a refactoring issue.
4. **Expression caching in VMTemplateEvaluator** — creates a new child VM per template expression. Performance optimization, not decomposition.
5. **VMFunction.Call() proper implementation** — making VMFunction truly callable from C# would unlock simpler stdlib integration. Feature work.
