# Bytecode VM — Implementation Roadmap

**Status:** Backlog — Master Plan
**Created:** 2026-04-04
**Parent:** Interpreter Performance — Tree-Walk Optimization and Bytecode VM Analysis
**Purpose:** Phased implementation plan for replacing Stash's tree-walk interpreter with a stack-based bytecode VM, while preserving all existing tooling (LSP, DAP, Analysis, Playground).

---

## 1. Executive Summary

Stash's tree-walk interpreter is 10–25× slower than CPython and Ruby (both bytecode VMs). Tree-walk optimizations have a ceiling of ~2× improvement — insufficient to close the gap. This roadmap defines 8 sequential phases to implement a bytecode VM that:

- Compiles the existing AST to a flat bytecode instruction stream
- Executes on a stack-based virtual machine with O(1) variable access
- Preserves the AST for LSP, static analysis, and formatting (zero impact)
- Maintains full DAP debugging via source maps (bytecode offset → SourceSpan)
- Keeps the tree-walk interpreter as a reference/debug backend

**Target:** Achieve performance within 2–5× of CPython on all benchmarks, down from the current 10–25×.

**Current benchmarks (ms, median of 3 runs):**

| Benchmark             | Stash | Python | Node.js | Ruby | Lua | Perl |   Bash |
| --------------------- | ----: | -----: | ------: | ---: | --: | ---: | -----: |
| Algorithms            | 1,895 |     86 |       6 |   52 |  34 |  209 | 10,649 |
| Function Calls        | 1,824 |     77 |       3 |   17 |  13 |  102 |  3,573 |
| Expression Throughput | 1,177 |    188 |      13 |  187 |  85 |  132 |  5,001 |
| Built-in Functions    |   737 |    294 |      27 |  343 | 208 |  321 | 23,478 |
| Scope Lookup          | 1,487 |    104 |       4 |  134 |  58 |  261 |  3,297 |

---

## 2. Architectural Invariant

The AST remains the single source of truth for all non-execution consumers. The bytecode compiler is a **new AST consumer**, parallel to the existing Analysis engine and LSP visitors:

```
Source → Lexer → Parser → AST ──→ Resolver ──→ Tree-Walk Interpreter (existing, preserved)
                           │                ╲
                           │                 ╲──→ Bytecode Compiler → VM (new)
                           │
                           └──→ Analysis Engine → LSP / DAP Visitors (unchanged)
                           └──→ Formatter (unchanged)
```

**What changes:** Only the execution backend. Everything from Lexer through Resolver stays identical.
**What doesn't change:** Stash.Core (AST, Lexer, Parser), Stash.Analysis, Stash.Lsp, Stash.Format, Stash.Check, Stash.Playground (beyond swapping the execution call).

---

## 3. Current Architecture — What the VM Replaces

### 3.1 Execution Pipeline (Current)

```
StashEngine.Run(source)
  → Lexer.ScanTokens()         → List<Token>
  → Parser.Parse()             → List<Stmt>
  → Resolver.Resolve(stmts)    → Annotates AST with (Distance, Slot) on Expr nodes
  → Interpreter.Interpret()    → Tree-walk via switch(expr.NodeType) / stmt.Accept(this)
```

### 3.2 What the Interpreter Does That the VM Must Replicate

| Capability                | Current Implementation                                   | VM Equivalent                                            |
| ------------------------- | -------------------------------------------------------- | -------------------------------------------------------- |
| **Expression evaluation** | 28 VisitXxxExpr methods, switch dispatch                 | ~45 opcodes, single switch loop                          |
| **Statement execution**   | 23 VisitXxxStmt methods                                  | Control flow opcodes + scope management                  |
| **Variable lookup**       | `Expr.ResolvedDistance/Slot` → `Environment.GetAtSlot()` | `OP_LOAD_LOCAL slot` / `OP_LOAD_UPVALUE idx`             |
| **Global lookup**         | `Environment.Get(name)` on globals dictionary            | `OP_LOAD_GLOBAL name_idx`                                |
| **Function calls**        | New Environment + List\<object?\> args + ReturnException | Call frames on VM frame stack                            |
| **Closures**              | `StashFunction.Closure` → Environment chain              | Upvalue objects (open/closed)                            |
| **Control flow**          | BreakException / ContinueException / ReturnException     | Jump instructions + frame unwinding                      |
| **Error handling**        | try/catch via C# exception handlers                      | Exception handler table per function                     |
| **Built-in dispatch**     | `StashNamespace.GetMember()` → `BuiltInFunction.Call()`  | Same — callables remain `IStashCallable`                 |
| **Shell commands**        | `VisitCommandExpr` → `Process.Start`                     | `OP_COMMAND` → same Process.Start code                   |
| **Modules**               | `LoadModule()` → lex/parse/resolve/execute in new env    | Compile module → execute on VM in module frame           |
| **Async/tasks**           | `Interpreter.Fork()` → new ExecutionContext              | `VM.Fork()` → new value stack + frame stack              |
| **Debugger hooks**        | `_debugger?.OnBeforeExecute(stmt.Span, ...)`             | `_debugger?.OnBeforeExecute(sourceMap.GetSpan(ip), ...)` |
| **Cancellation**          | Check `CancellationToken` per statement                  | Check per backward jump (loops) + call boundaries        |
| **Step limits**           | `++StepCount > _stepLimit` per statement                 | Same, at same check points as cancellation               |

### 3.3 Runtime Type Inventory

The VM must handle all current runtime types stored as `object?`:

| Type              | C# Type                          | Frequency             | Notes              |
| ----------------- | -------------------------------- | --------------------- | ------------------ |
| Null              | `null`                           | Very high             | Falsy              |
| Boolean           | `bool` (boxed)                   | Very high             | Falsy: `false`     |
| Integer           | `long` (boxed)                   | Very high             | Falsy: `0`         |
| Float             | `double` (boxed)                 | High                  | Falsy: `0.0`       |
| String            | `string`                         | Very high             | Falsy: `""`        |
| Array             | `List<object?>`                  | High                  | Always truthy      |
| Dictionary        | `StashDictionary`                | High                  | Always truthy      |
| Struct instance   | `StashInstance`                  | Medium                | Always truthy      |
| Struct definition | `StashStruct`                    | Low                   | Type template      |
| Enum definition   | `StashEnum`                      | Low                   | Type template      |
| Enum value        | `StashEnumValue`                 | Medium                | Always truthy      |
| Function          | `UserCallable` / `StashFunction` | Medium                | Callable           |
| Lambda            | `StashLambda`                    | Medium                | Callable           |
| Built-in function | `BuiltInFunction`                | High (via namespaces) | Callable           |
| Namespace         | `StashNamespace`                 | Low (global only)     | Container          |
| Range             | `StashRange`                     | Low                   | Iterable           |
| Duration          | `StashDuration`                  | Low                   | Arithmetic-capable |
| ByteSize          | `StashByteSize`                  | Low                   | Arithmetic-capable |
| SemVer            | `StashSemVer`                    | Low                   | Comparable         |
| IP Address        | `StashIpAddress`                 | Low                   | Value type         |
| Future            | `StashFuture`                    | Low                   | Awaitable          |
| Error             | `StashError`                     | Low                   | Throwable          |
| Bound method      | `StashBoundMethod`               | Medium                | Callable           |
| Extension method  | `ExtensionBoundMethod`           | Low                   | UFCS callable      |

**Phase 1 decision: Use `object?` initially.** The VM will box primitives just like the tree-walk interpreter does today. This allows reusing all existing runtime types, built-in functions, and collections without modification. A future `StashValue` tagged union optimization (post-Phase 8) can eliminate boxing for a further 20–40% improvement.

---

## 4. Phased Implementation Plan

### Overview

| Phase | Name                                    | Scope                                                                                     | Success Criteria                                                                                                   |
| ----- | --------------------------------------- | ----------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------ |
| **1** | Bytecode Infrastructure                 | Opcode enum, Chunk type, constant pool, source maps, disassembler                         | Can define, serialize, and disassemble all opcodes                                                                 |
| **2** | Compiler: Expressions + Core Statements | AST → bytecode for expressions, variables, blocks, control flow                           | Compiler output verified by disassembler against hand-crafted expected bytecodes                                   |
| **3** | Virtual Machine: Core Execution         | Stack VM executing Phase 2 output                                                         | VM passes 500+ core tests (arithmetic, variables, control flow, scoping)                                           |
| **4** | Functions, Closures, and Calls          | Function declarations, calls, closures, upvalues, lambdas, built-ins                      | VM passes 1500+ tests including all function/closure/built-in tests; runs Function Calls + Scope Lookup benchmarks |
| **5** | Objects, Collections, and Types         | Structs, enums, arrays, dicts, strings, ranges, dot access, indexing                      | VM passes 3000+ tests; runs all 5 benchmarks                                                                       |
| **6** | Advanced Features                       | Error handling, modules, commands, async, pattern matching, retry, elevate, destructuring | VM passes 4000+ tests; can run all example scripts                                                                 |
| **7** | Debugger Integration                    | DAP source maps, breakpoints, stepping, variable inspection, watch eval                   | DAP test suite passes; VS Code debugging fully functional on VM                                                    |
| **8** | Integration and Migration               | StashEngine default backend, REPL, Playground, dual-mode, full test parity, benchmarks    | All 4400+ tests pass; benchmarks within 2–5× of CPython                                                            |

---

### Phase 1: Bytecode Infrastructure

**Goal:** Define the data structures that all subsequent phases build on. No compilation or execution — just the types, serialization, and a disassembler for debugging.

**New project:** `Stash.Bytecode` (class library, referenced by Stash.Interpreter)

#### 1.1 Opcode Enum

```
~65 opcodes organized by category:

Constants & Literals:
  OP_CONST <u16>          Load constant from pool
  OP_NULL                 Push null
  OP_TRUE                 Push true
  OP_FALSE                Push false

Stack Manipulation:
  OP_POP                  Discard top of stack
  OP_DUP                  Duplicate top of stack

Variable Access:
  OP_LOAD_LOCAL <u8>      Push local variable by slot index
  OP_STORE_LOCAL <u8>     Pop and store to local slot
  OP_LOAD_GLOBAL <u16>    Push global by name (constant pool index)
  OP_STORE_GLOBAL <u16>   Pop and store to global by name
  OP_LOAD_UPVALUE <u8>    Push captured upvalue
  OP_STORE_UPVALUE <u8>   Pop and store to upvalue

Arithmetic:
  OP_ADD                  Pop 2, push sum (long/double/string/duration/bytesize)
  OP_SUB                  Pop 2, push difference
  OP_MUL                  Pop 2, push product
  OP_DIV                  Pop 2, push quotient
  OP_MOD                  Pop 2, push remainder
  OP_POWER                Pop 2, push exponentiation
  OP_NEGATE               Pop 1, push negation

Bitwise:
  OP_BIT_AND              Pop 2, push bitwise AND
  OP_BIT_OR               Pop 2, push bitwise OR
  OP_BIT_XOR              Pop 2, push bitwise XOR
  OP_BIT_NOT              Pop 1, push bitwise NOT
  OP_SHL                  Pop 2, push left shift
  OP_SHR                  Pop 2, push right shift

Comparison:
  OP_EQ                   Pop 2, push equality result
  OP_NE                   Pop 2, push inequality result
  OP_LT                   Pop 2, push less-than result
  OP_LE                   Pop 2, push less-or-equal result
  OP_GT                   Pop 2, push greater-than result
  OP_GE                   Pop 2, push greater-or-equal result

Logic:
  OP_NOT                  Pop 1, push logical not
  OP_AND <u16>            Short-circuit: if falsy, jump ahead
  OP_OR <u16>             Short-circuit: if truthy, jump ahead
  OP_NULL_COALESCE <u16>  If not null, jump ahead (skip right operand)

Control Flow:
  OP_JUMP <i16>           Unconditional jump (signed offset)
  OP_JUMP_TRUE <i16>      Jump if top is truthy (pop condition)
  OP_JUMP_FALSE <i16>     Jump if top is falsy (pop condition)
  OP_LOOP <u16>           Backward jump (for loops — cancellation check point)

Functions:
  OP_CALL <u8>            Call function with N args on stack
  OP_RETURN               Return top of stack from current frame
  OP_CLOSURE <u16>        Create closure (constant pool index + upvalue descriptors)

Collections:
  OP_ARRAY <u16>          Build array from N stack values
  OP_DICT <u16>           Build dictionary from N key/value pairs
  OP_RANGE                Pop 2-3 values, push range (start..end[..step])
  OP_SPREAD               Spread iterable onto stack

Object Access:
  OP_GET_FIELD <u16>      Pop object, push field value (name from constant pool)
  OP_SET_FIELD <u16>      Pop value + object, set field
  OP_GET_INDEX             Pop index + object, push element
  OP_SET_INDEX             Pop value + index + object, set element

Type Operations:
  OP_STRUCT_DECL <u16>    Declare struct type
  OP_STRUCT_INIT <u16>    Instantiate struct with N fields
  OP_ENUM_DECL <u16>      Declare enum type
  OP_INTERFACE_DECL <u16> Declare interface
  OP_EXTEND <u16>         Register extension methods
  OP_IS <u16>             Type check (is expression)

Strings:
  OP_INTERPOLATE <u16>    Build interpolated string from N parts

Special:
  OP_COMMAND <u16>        Execute shell command
  OP_PIPE                 Pipe two command results
  OP_REDIRECT <u8>        Redirect command output
  OP_IMPORT <u16>         Import module
  OP_IMPORT_AS <u16>      Import module with alias
  OP_DESTRUCTURE <u8>     Destructure array/dict into locals

Error Handling:
  OP_THROW                Pop value, throw as error
  OP_TRY_BEGIN <u16>      Push exception handler (jump offset to catch)
  OP_TRY_END              Pop exception handler
  OP_TRY_EXPR             Pop expr, push result or null on error

Async:
  OP_AWAIT                Pop future, push resolved value

Misc:
  OP_UPDATE_PRE_INC       Pre-increment variable
  OP_UPDATE_PRE_DEC       Pre-decrement variable
  OP_UPDATE_POST_INC      Post-increment variable
  OP_UPDATE_POST_DEC      Post-decrement variable
  OP_SWITCH <u16>         Switch expression dispatch
  OP_ELEVATE_BEGIN        Enter elevated context
  OP_ELEVATE_END          Exit elevated context
  OP_RETRY <u16>          Retry block
```

Exact opcode encoding (1-byte opcode + 0–2 byte operands) will be determined in the Phase 1 dedicated spec.

#### 1.2 Chunk (Compiled Function)

```csharp
public class Chunk
{
    public byte[] Code;                    // Bytecode instruction stream
    public object?[] Constants;            // Constant pool (numbers, strings, nested Chunks)
    public SourceMapEntry[] SourceMap;     // Bytecodeoffset → SourceSpan mappings
    public int Arity;                      // Parameter count
    public int MinArity;                   // Minimum args (for default params)
    public int LocalCount;                 // Slot count for locals (from Resolver)
    public UpvalueDescriptor[] Upvalues;   // Upvalue capture descriptors
    public string? Name;                   // Function name (null for top-level script)
    public bool IsAsync;                   // Async function flag
    public bool HasRestParam;              // Variadic last parameter
}
```

#### 1.3 Source Map

```csharp
public readonly struct SourceMapEntry
{
    public readonly int BytecodeOffset;    // Starting offset in Chunk.Code
    public readonly SourceSpan Span;       // Original source location
}
```

Entries sorted by `BytecodeOffset`. Binary search for lookups. Compiler emits an entry at each statement boundary and at expression boundaries that might throw errors.

#### 1.4 Disassembler

A `Disassembler` class that takes a `Chunk` and produces human-readable output:

```
== <script> ==
0000    1 OP_CONST         0    ; 42
0003    | OP_STORE_LOCAL    0    ; x
0005    2 OP_LOAD_LOCAL     0    ; x
0007    | OP_CONST          1    ; 10
0010    | OP_ADD
0011    | OP_STORE_LOCAL    1    ; y
```

This is the primary debugging tool for Phase 2 compiler development.

#### 1.5 Deliverables

- [ ] `Stash.Bytecode/OpCode.cs` — Opcode enum
- [ ] `Stash.Bytecode/Chunk.cs` — Compiled function representation
- [ ] `Stash.Bytecode/ChunkBuilder.cs` — Helper for emitting bytes + patching jumps
- [ ] `Stash.Bytecode/SourceMap.cs` — Source mapping types
- [ ] `Stash.Bytecode/UpvalueDescriptor.cs` — Upvalue capture metadata
- [ ] `Stash.Bytecode/Disassembler.cs` — Human-readable bytecode dump
- [ ] Unit tests for serialization roundtrips and disassembler output

---

### Phase 2: Compiler — Expressions and Core Statements

**Goal:** An AST visitor that walks the resolved AST and emits bytecodes into a `Chunk`. Covers all expression types and core statements (variables, blocks, control flow). Tested by disassembling output and comparing against hand-written expected bytecodes.

**Depends on:** Phase 1 (Chunk, ChunkBuilder, opcodes)

#### 2.1 Compiler Architecture

```csharp
public class Compiler : IExprVisitor<object?>, IStmtVisitor<object?>
{
    private ChunkBuilder _builder;        // Current chunk being compiled
    private CompilerScope _scope;         // Local variable tracking (mirrors Resolver)

    public Chunk Compile(List<Stmt> statements);           // Top-level script
    public Chunk CompileFunction(FnDeclStmt fn);           // Named function
    public Chunk CompileLambda(LambdaExpr lambda);         // Lambda expression
}
```

The compiler implements the same `IExprVisitor<object?>` and `IStmtVisitor<object?>` interfaces as the existing Interpreter and Resolver — the AST dispatches to the correct `Visit*` method, which emits bytecodes instead of executing or resolving.

#### 2.2 Scope Tracking

The Compiler needs to mirror the Resolver's scope tracking to map variable names to local slot indices:

```csharp
private class CompilerScope
{
    public record Local(string Name, int Slot, int Depth, bool IsConst);
    public readonly List<Local> Locals;
    public int ScopeDepth;               // Current nesting depth
}
```

When the compiler encounters `var x = 5;`, it:

1. Records `x` in the current scope with slot index
2. Compiles the initializer expression (pushes `5` onto stack)
3. Emits `OP_STORE_LOCAL <slot>` (or, for top-of-scope declarations, the value is already at the correct stack position — no store needed)

#### 2.3 Expression Compilation (all 28 types)

| Expression               | Compilation Strategy                                                          |
| ------------------------ | ----------------------------------------------------------------------------- |
| `LiteralExpr`            | `OP_CONST idx` / `OP_NULL` / `OP_TRUE` / `OP_FALSE`                           |
| `IdentifierExpr`         | `OP_LOAD_LOCAL` / `OP_LOAD_GLOBAL` / `OP_LOAD_UPVALUE` based on resolution    |
| `UnaryExpr`              | Compile operand → `OP_NEGATE` or `OP_NOT`                                     |
| `BinaryExpr`             | Compile left → compile right → `OP_ADD` / `OP_SUB` / ...                      |
| `GroupingExpr`           | Compile inner expression (parens are purely syntactic)                        |
| `TernaryExpr`            | Compile condition → `OP_JUMP_FALSE` → compile then → `OP_JUMP` → compile else |
| `AssignExpr`             | Compile value → `OP_STORE_LOCAL` / `OP_STORE_GLOBAL` / `OP_STORE_UPVALUE`     |
| `CallExpr`               | Compile callee → compile each arg → `OP_CALL argc`                            |
| `ArrayExpr`              | Compile each element → `OP_ARRAY count`                                       |
| `IndexExpr`              | Compile object → compile index → `OP_GET_INDEX`                               |
| `IndexAssignExpr`        | Compile object → compile index → compile value → `OP_SET_INDEX`               |
| `StructInitExpr`         | Compile each field value → `OP_STRUCT_INIT field_count`                       |
| `DotExpr`                | Compile object → `OP_GET_FIELD name_idx`                                      |
| `DotAssignExpr`          | Compile object → compile value → `OP_SET_FIELD name_idx`                      |
| `InterpolatedStringExpr` | Compile each part → `OP_INTERPOLATE count`                                    |
| `CommandExpr`            | Compile interpolated command string → `OP_COMMAND flags`                      |
| `PipeExpr`               | Compile left command → compile right command → `OP_PIPE`                      |
| `TryExpr`                | `OP_TRY_EXPR` wrapping compiled expression                                    |
| `NullCoalesceExpr`       | Compile left → `OP_NULL_COALESCE` → compile right                             |
| `SwitchExpr`             | Compile subject → series of match/jump pairs → `OP_SWITCH`                    |
| `UpdateExpr`             | `OP_UPDATE_PRE_INC` / `OP_UPDATE_POST_INC` / etc. with variable reference     |
| `LambdaExpr`             | Compile body as nested Chunk → `OP_CLOSURE`                                   |
| `RedirectExpr`           | Compile inner → `OP_REDIRECT target`                                          |
| `RangeExpr`              | Compile start → compile end → [compile step] → `OP_RANGE`                     |
| `DictLiteralExpr`        | Compile alternating keys/values → `OP_DICT count`                             |
| `IsExpr`                 | Compile expression → `OP_IS type_name_idx`                                    |
| `AwaitExpr`              | Compile expression → `OP_AWAIT`                                               |
| `RetryExpr`              | Compile as loop with exception handler                                        |
| `SpreadExpr`             | Compile inner → `OP_SPREAD`                                                   |

#### 2.4 Statement Compilation (core subset)

Phase 2 covers the **core structural statements** needed for basic programs:

| Statement       | Compilation Strategy                                                          |
| --------------- | ----------------------------------------------------------------------------- |
| `ExprStmt`      | Compile expression → `OP_POP` (discard result)                                |
| `VarDeclStmt`   | Compile initializer (or `OP_NULL`) → record local in scope                    |
| `ConstDeclStmt` | Same as VarDeclStmt with const flag                                           |
| `BlockStmt`     | Begin scope → compile body → end scope (emit `OP_POP` for expired locals)     |
| `IfStmt`        | Compile condition → `OP_JUMP_FALSE` → compile then → `OP_JUMP` → compile else |
| `WhileStmt`     | Loop start → compile condition → `OP_JUMP_FALSE` → compile body → `OP_LOOP`   |
| `DoWhileStmt`   | Loop body → compile condition → `OP_JUMP_TRUE` to start                       |
| `ForStmt`       | Compile init → loop: condition → `OP_JUMP_FALSE` → body → update → `OP_LOOP`  |
| `ForInStmt`     | Compile iterable → `OP_ITERATOR` → loop with `OP_ITERATE`                     |
| `BreakStmt`     | `OP_JUMP` → patch target to after enclosing loop                              |
| `ContinueStmt`  | `OP_JUMP` → patch target to loop update/condition                             |
| `ReturnStmt`    | Compile value (or `OP_NULL`) → `OP_RETURN`                                    |

**Deferred to Phase 4:** FnDeclStmt, LambdaExpr compilation (closures)
**Deferred to Phase 5:** StructDeclStmt, EnumDeclStmt, InterfaceDeclStmt, ExtendStmt
**Deferred to Phase 6:** ImportStmt, TryCatchStmt, ThrowStmt, ElevateStmt, DestructureStmt

#### 2.5 Jump Patching

Control flow requires forward jumps to targets not yet known at emit time:

```csharp
// Emit placeholder jump with 0 offset
int jumpAddr = _builder.EmitJump(OpCode.JUMP_FALSE);
// ... compile then-branch ...
// Patch the jump to point here
_builder.PatchJump(jumpAddr);
```

Loop compilation requires tracking break/continue jump sites for backpatching.

#### 2.6 Deliverables

- [ ] `Stash.Bytecode/Compiler.cs` — Main compiler class (AST → Chunk)
- [ ] `Stash.Bytecode/CompilerScope.cs` — Local variable tracking
- [ ] Compiler tests: hand-verified disassembly output for each expression and statement type
- [ ] Correctness oracle: compile expression → disassemble → verify against expected opcodes

---

### Phase 3: Virtual Machine — Core Execution

**Goal:** A stack-based VM that executes Chunks produced by the Phase 2 compiler. Covers arithmetic, variables, control flow — enough to run the Algorithms and Expression Throughput benchmarks.

**Depends on:** Phase 2 (Compiler producing valid Chunks)

#### 3.1 VM Architecture

```csharp
public class VirtualMachine
{
    // Value stack — flat array for cache locality
    private object?[] _stack;
    private int _sp;                        // Stack pointer (next free slot)

    // Call frame stack
    private CallFrame[] _frames;
    private int _frameCount;

    // Shared state
    private readonly Environment _globals;  // Reuse existing global Environment
    private IDebugger? _debugger;
    private CancellationToken _ct;

    // Hot loop
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public object? Execute(Chunk chunk);
}
```

#### 3.2 Call Frame

```csharp
private struct CallFrame
{
    public Chunk Chunk;             // Bytecode being executed
    public int IP;                  // Instruction pointer into Chunk.Code
    public int BaseSlot;            // Stack index where this frame's locals begin
    public Upvalue[]? Upvalues;     // Captured upvalues (null for top-level)
    public string? FunctionName;    // For call stack / error reporting
}
```

Locals live directly on the value stack. `LOAD_LOCAL slot` → `_stack[frame.BaseSlot + slot]`. No separate Environment allocation per call.

#### 3.3 Execution Loop

```csharp
public object? Execute(Chunk topLevel)
{
    PushFrame(topLevel, baseSlot: 0);

    while (true)
    {
        ref CallFrame frame = ref _frames[_frameCount - 1];
        byte op = frame.Chunk.Code[frame.IP++];

        switch (op)
        {
            case OP_CONST:
                ushort idx = ReadU16(ref frame);
                Push(frame.Chunk.Constants[idx]);
                break;

            case OP_ADD:
                object? b = Pop();
                object? a = Peek();  // Modify in place for speed
                if (a is long la && b is long lb)
                    _stack[_sp - 1] = la + lb;
                else
                    _stack[_sp - 1] = AddSlow(a, b);
                break;

            case OP_LOAD_LOCAL:
                byte slot = frame.Chunk.Code[frame.IP++];
                Push(_stack[frame.BaseSlot + slot]);
                break;

            case OP_STORE_LOCAL:
                slot = frame.Chunk.Code[frame.IP++];
                _stack[frame.BaseSlot + slot] = Peek();
                break;

            case OP_JUMP_FALSE:
                short offset = ReadI16(ref frame);
                if (IsFalsy(Pop()))
                    frame.IP += offset;
                break;

            case OP_LOOP:
                ushort loopOffset = ReadU16(ref frame);
                frame.IP -= loopOffset;
                // Cancellation + step limit check on backward jumps
                if (_ct.IsCancellationRequested)
                    throw new ScriptCancelledException();
                break;

            case OP_RETURN:
                object? result = Pop();
                PopFrame();
                if (_frameCount == 0) return result;
                Push(result);
                break;

            // ... ~60 more cases
        }
    }
}
```

#### 3.4 Key Design Decisions

**Locals on the value stack:** Variable access is `_stack[baseSlot + slot]` — a single array indexing operation. No Environment object allocation, no linked-list traversal. This is the single biggest performance win over tree-walk.

**Cancellation at backward jumps only:** Every loop iteration hits `OP_LOOP` which checks cancellation. Forward-only code paths don't need checking — they're bounded by program size.

**Globals via existing Environment:** Global variables (including all built-in namespaces like `math`, `arr`, `str`) remain in the existing `Environment._values` ConcurrentDictionary. This avoids duplicating the global registration infrastructure.

**IsFalsy matching Stash semantics:** `null`, `false`, `0`, `0.0`, `""` are falsy. Everything else is truthy. Must match tree-walk exactly.

#### 3.5 Success Criteria

- All existing `ExpressionTests`, `VariableTests`, `ControlFlowTests`, `LoopTests`, `ScopeTests` pass when run against VM backend
- Algorithms benchmark runs correctly (fib=121393, found=10000, sum=37497500)
- Expression Throughput benchmark produces matching checksum

#### 3.6 Deliverables

- [ ] `Stash.Bytecode/VirtualMachine.cs` — Stack VM with execution loop
- [ ] `Stash.Bytecode/CallFrame.cs` — Frame representation
- [ ] `Stash.Bytecode/RuntimeOps.cs` — Shared runtime operations (Add, Subtract, IsFalsy, Stringify, etc.)
- [ ] VM test suite mirroring existing interpreter tests
- [ ] Benchmark harness comparing VM vs tree-walk execution times

---

### Phase 4: Functions, Closures, and Calls

**Goal:** Complete function support — declarations, calls with default/rest params, closures with upvalue capture, lambdas, and built-in function dispatch. This is the most complex phase due to upvalue lifecycle management.

**Depends on:** Phase 3 (working VM for basic execution)

#### 4.1 Function Compilation

`FnDeclStmt` compiles to:

1. A nested `Chunk` for the function body (compiled recursively)
2. An `OP_CLOSURE` instruction that wraps the Chunk with upvalue descriptors
3. An `OP_STORE_LOCAL` / `OP_STORE_GLOBAL` to bind the name

#### 4.2 Function Calls

`OP_CALL argc`:

1. Pop function from stack (below arguments)
2. If `IStashCallable` (covers `BuiltInFunction`, extension methods, bound methods):
   - Call `.Call(context, argsList)` directly — reusing existing built-in implementation
   - Push result
3. If `VMFunction` (compiled user function):
   - Push new `CallFrame` with the function's `Chunk`
   - Arguments are already on the stack at the right positions (they become locals 0..N-1)
   - Handle default values: emit `OP_NULL` for missing args, then optionally evaluate defaults
   - Handle rest params: collect excess args into array

**Critical:** Built-in functions (`IStashCallable.Call()`) continue to receive `List<object?>` arguments. The VM must materialize this list before calling. This is intentional — rewriting all 900+ built-in functions for a new calling convention is out of scope. The list allocation overhead is acceptable because built-in calls do real work (math, I/O, string ops) that dominates the call overhead.

#### 4.3 Closures and Upvalues

This follows the Lua/Crafting Interpreters upvalue model:

```csharp
public class Upvalue
{
    public int StackIndex;          // While open: index into VM's value stack
    public object? Closed;          // After closing: captured value
    public bool IsOpen;

    public object? Value
    {
        get => IsOpen ? _vm._stack[StackIndex] : Closed;
        set { if (IsOpen) _vm._stack[StackIndex] = value; else Closed = value; }
    }
}
```

**Open upvalue:** References a local variable still on the stack (the enclosing function hasn't returned yet). Access goes through the stack index — zero copy.

**Closed upvalue:** When the enclosing function returns, all open upvalues pointing at its locals are "closed" — the value is copied from the stack into the `Upvalue.Closed` field.

**Link list for deduplication:** The VM maintains a linked list of open upvalues. When creating a closure, if an upvalue for the same stack slot already exists, it's shared (so multiple closures capturing the same variable see the same upvalue).

#### 4.4 Lambda Compilation

`LambdaExpr` compiles identically to `FnDeclStmt` but:

- Expression body `(x) => x + 1` compiles body as expression + implicit `OP_RETURN`
- Block body `(x) => { ... }` compiles as normal function body
- Result is pushed on stack (not stored to a local)

#### 4.5 Return Handling

`OP_RETURN` replaces `ReturnException`:

1. Pop the return value
2. Close all upvalues in the current frame's stack window
3. Pop the call frame
4. Restore stack pointer to caller's level
5. Push return value for the caller

No exception throw/catch overhead.

#### 4.6 Recursive Calls

The same `Chunk` can appear in multiple frames simultaneously (recursion). Each frame has its own `IP` and `BaseSlot`, so recursion is naturally supported. This is critical for the Algorithms benchmark (fibonacci).

#### 4.7 Success Criteria

- All function-related tests pass (user functions, lambdas, default params, rest params, closures, recursion)
- Built-in namespace calls work (`math.sqrt()`, `arr.push()`, etc.)
- Function Calls benchmark runs correctly (checksum 30005400000)
- Scope Lookup benchmark runs correctly (checksum 3600000, 5-level closures)

#### 4.8 Deliverables

- [ ] `Stash.Bytecode/Upvalue.cs` — Upvalue representation
- [ ] `Stash.Bytecode/VMFunction.cs` — VM-compiled function wrapper
- [ ] Compiler updates for FnDeclStmt, LambdaExpr, CallExpr
- [ ] VM updates for OP_CALL, OP_RETURN, OP_CLOSURE, OP_LOAD_UPVALUE, OP_STORE_UPVALUE
- [ ] Upvalue open/close lifecycle in VM
- [ ] Built-in function bridge (VM → IStashCallable.Call)

---

### Phase 5: Objects, Collections, and Types

**Goal:** Support for Stash's type system — structs, enums, interfaces, arrays, dictionaries, string interpolation, ranges, extension methods, dot access, and index access.

**Depends on:** Phase 4 (function calls needed for methods and UFCS)

#### 5.1 Struct Compilation

`StructDeclStmt`:

1. Compile each method body as a nested `Chunk`
2. Emit `OP_STRUCT_DECL` with field names and method chunks in the constant pool
3. At runtime, creates a `StashStruct` object and binds it to the local/global name

`StructInitExpr` (`Name { field: value, ... }`):

1. Load the struct definition
2. Compile each field value
3. Emit `OP_STRUCT_INIT field_count`
4. At runtime, creates a `StashInstance` with the provided field values

#### 5.2 Field Access

`DotExpr` (`obj.field`):

- Compile object → `OP_GET_FIELD name_idx`
- At runtime: check `StashInstance.GetField()`, `StashStruct` method lookup, `StashNamespace.GetMember()`, `StashEnum` member lookup, etc.
- UFCS is handled here: if `obj` is a string/array/etc. and field is a method, produce a `BuiltInBoundMethod` or `ExtensionBoundMethod`

`DotAssignExpr` (`obj.field = value`):

- Compile object → compile value → `OP_SET_FIELD name_idx`

#### 5.3 Collections

Arrays: `OP_ARRAY count` pops N elements, creates `List<object?>`
Dicts: `OP_DICT count` pops N key/value pairs, creates `StashDictionary`
Ranges: `OP_RANGE` pops start, end, optional step, creates `StashRange`
Indexing: `OP_GET_INDEX` / `OP_SET_INDEX` handle arrays, dicts, and strings

#### 5.4 String Interpolation

`InterpolatedStringExpr`:

1. Compile each part (literal string or expression)
2. Emit `OP_INTERPOLATE count`
3. At runtime: stringify each element, concatenate

#### 5.5 Enum and Interface

`EnumDeclStmt`: Emit `OP_ENUM_DECL` — creates `StashEnum` + `StashEnumValue` members
`InterfaceDeclStmt`: Emit `OP_INTERFACE_DECL` — creates `StashInterface`
`ExtendStmt`: Emit `OP_EXTEND` — registers extension methods on the `ExtensionRegistry`

#### 5.6 Spread and Is

`SpreadExpr`: `OP_SPREAD` — expands array/iterable onto the stack (for function args and array construction)
`IsExpr`: `OP_IS type_name` — runtime type check against type name string

#### 5.7 Success Criteria

- All struct, enum, interface, array, dict, range, string tests pass
- Extension method (UFCS) tests pass
- All 5 benchmarks run correctly on the VM
- Namespace Calls benchmark matches checksum

#### 5.8 Deliverables

- [ ] Compiler updates for all object/collection expression and statement types
- [ ] VM runtime operations for struct/enum/dict/array/range/interpolation
- [ ] UFCS dispatch in OP_GET_FIELD
- [ ] OP_SPREAD implementation with variadic function interaction

---

### Phase 6: Advanced Features

**Goal:** The remaining language features — error handling, modules, shell commands, async, pattern matching, and miscellaneous — bringing the VM to full language coverage.

**Depends on:** Phase 5 (types and collections)

#### 6.1 Error Handling

**TryCatchStmt** requires an exception handler table:

```csharp
public struct ExceptionHandler
{
    public int TryStart;       // Bytecode offset of try block start
    public int TryEnd;         // Bytecode offset of try block end
    public int CatchStart;     // Bytecode offset of catch handler
    public int FinallyStart;   // Bytecode offset of finally handler (-1 if none)
    public int CatchVarSlot;   // Local slot for caught error variable
}
```

When an error occurs during VM execution:

1. Search the current frame's handler table for a handler covering the current IP
2. If found: unwind stack to handler's stack level, push error, jump to catch
3. If not found: pop the frame and repeat in the caller
4. If no handler in any frame: convert to `RuntimeError` and propagate to host

**ThrowStmt:** `OP_THROW` pops a value, wraps it as `RuntimeError` if needed, triggers the handler search.

**TryExpr:** `OP_TRY_EXPR` — compile-time sugar: `OP_TRY_BEGIN` around a single expression, evaluates to null on error.

#### 6.2 Module System

`ImportStmt` / `ImportAsStmt`:

1. Resolve module path (same path resolution logic as tree-walk)
2. Check shared module cache
3. If uncached: lex → parse → resolve → **compile** → execute on a fresh VM frame
4. Cache the resulting module environment
5. Bind imported names into current scope

**Key change:** Modules compile to `Chunk` instead of being tree-walked. The module cache stores the compiled `Chunk` + executed environment, not the AST.

#### 6.3 Shell Commands

`OP_COMMAND`: Compile the command template (with expression interpolation) → call the same `CommandParser.Parse()` + `Process.Start` infrastructure. Return `StashInstance("CommandResult", ...)`.

`OP_PIPE` / `OP_REDIRECT`: Wire stdout/stdin between processes. Identical runtime logic to tree-walk, just invoked via opcodes.

#### 6.4 Async/Await

`task.run()` (built-in function):

1. Takes a `VMFunction` closure
2. Forks the VM: new value stack, new frame stack, shared globals + module cache
3. Executes the closure on a `ThreadPool` thread
4. Returns `StashFuture` wrapping the `Task`

`OP_AWAIT`:

1. Pop `StashFuture`
2. Call `Task.Wait()` (or `Task.GetAwaiter().GetResult()`)
3. Push result

#### 6.5 Pattern Matching (Switch Expressions)

`SwitchExpr` compilation:

1. Compile subject
2. For each arm: emit comparison + `OP_JUMP_FALSE` to next arm
3. On match: compile arm body → `OP_JUMP` to end
4. Default arm (no guard) falls through

#### 6.6 Remaining Features

| Feature             | Approach                                                                  |
| ------------------- | ------------------------------------------------------------------------- |
| **Null coalescing** | `OP_NULL_COALESCE offset` — peek top, jump if non-null                    |
| **Ternary**         | Compile as if/else jump pattern                                           |
| **Update (++/--)**  | Specialized opcodes for pre/post increment/decrement                      |
| **Destructuring**   | `OP_DESTRUCTURE` — pops value, assigns to multiple local slots            |
| **Elevate**         | `OP_ELEVATE_BEGIN` / `OP_ELEVATE_END` — toggle elevation flag in context  |
| **Retry**           | Compile as loop with attempt counter + exception handler                  |
| **For-in**          | `OP_ITERATOR` to create iterator, `OP_ITERATE` to advance + break on done |

#### 6.7 Success Criteria

- 4000+ existing tests pass on VM backend
- All example scripts in `examples/` execute correctly
- Import system works (including circular import detection)
- Shell command execution works cross-platform
- Try/catch/finally behavior matches tree-walk exactly

#### 6.8 Deliverables

- [ ] Exception handler table per Chunk
- [ ] Module compilation and caching
- [ ] Command execution opcodes
- [ ] VM.Fork() for async/task support
- [ ] Switch expression compilation
- [ ] All remaining opcode implementations

---

### Phase 7: Debugger Integration

**Goal:** Full DAP debugging support for bytecode-executed scripts — breakpoints, stepping, variable inspection, call stack, and watch expression evaluation.

**Depends on:** Phase 6 (all language features must work before debugging can be fully tested)

#### 7.1 Source Map Usage

The compiler emits `SourceMapEntry` records at each statement boundary. The VM uses these for:

**Breakpoint matching:**

```csharp
bool HitBreakpoint(ref CallFrame frame)
{
    SourceSpan? span = frame.Chunk.SourceMap.GetSpan(frame.IP);
    if (span == null) return false;
    return _debugger.HasBreakpoint(span.File, span.StartLine);
}
```

**Step Over:** Advance until the source line changes (different from current line).
**Step Into:** Stop at the first instruction of a called function.
**Step Out:** Continue until current frame is popped.

#### 7.2 IDebugger Interface Compatibility

The existing `IDebugger` interface remains unchanged. The VM calls the same methods:

```csharp
// Before (tree-walk):
_debugger.OnBeforeExecute(stmt.Span, _environment, threadId);

// After (VM):
var span = frame.Chunk.SourceMap.GetSpan(frame.IP);
if (span != null)
    _debugger.OnBeforeExecute(span, GetFrameEnvironment(frame), threadId);
```

**Variable inspection:** The DAP currently walks `Environment.GetAllBindings()`. The VM must provide an equivalent view of the current frame's locals:

```csharp
Environment GetFrameEnvironment(CallFrame frame)
{
    // Build a temporary Environment containing the frame's local variables
    // for the debugger to enumerate
    var env = new Environment(_globals, frame.Chunk.LocalCount);
    for (int i = 0; i < frame.Chunk.LocalCount; i++)
        env.DefineAt(i, localNames[i], _stack[frame.BaseSlot + i]);
    return env;
}
```

This adapter approach means the existing DAP `EvaluateHandler`, `VariablesHandler`, and `ScopesHandler` work without modification.

#### 7.3 Watch Expression Evaluation

The tree-walk interpreter evaluates watch expressions by calling `interpreter.Interpret(expr)` on parsed AST. With the VM, there are two options:

**Option A (Simpler):** Keep a tree-walk interpreter instance for watch expression evaluation. Parse the expression → tree-walk it in the debugger's scope. This is simple and works immediately.

**Option B (Cleaner):** Compile the watch expression to a mini-Chunk → execute on the VM. Requires setting up a frame with the current scope as context.

**Recommendation:** Start with Option A, migrate to Option B later.

#### 7.4 Function Enter/Exit Hooks

```csharp
// On OP_CALL:
_debugger?.OnFunctionEnter(function.Name, callSiteSpan, locals, threadId);

// On OP_RETURN:
_debugger?.OnFunctionExit(function.Name, threadId);
```

#### 7.5 Thread Tracking

VM.Fork() for async tasks calls:

```csharp
_debugger?.OnThreadStarted(childThreadId, taskName, childVM);
// ... task executes ...
_debugger?.OnThreadExited(childThreadId);
```

#### 7.6 Error Reporting

When an error propagates to the user (uncaught):

```csharp
_debugger?.OnError(error, BuildCallStack(), threadId);
```

`BuildCallStack()` walks the frame stack and produces `CallFrame` objects matching the existing format.

#### 7.7 Conditional Debugger Overhead

When no debugger is attached (`_debugger == null`), all debug checks compile to null-checks that the JIT can optimize away. The hot loop should detect debugger presence once and use a specialized loop:

```csharp
if (_debugger != null)
    ExecuteWithDebugger(chunk);
else
    ExecuteFast(chunk);
```

This eliminates per-instruction debugger null-checks in production execution.

#### 7.8 Success Criteria

- All existing DAP tests pass with VM backend
- VS Code debugging: set breakpoint, hit breakpoint, step over/into/out, inspect locals/globals
- Watch expressions evaluate correctly in paused context
- Call stack shows correct function names and source locations
- Multi-threaded debugging (task.run) shows separate threads

#### 7.9 Deliverables

- [ ] Source map lookup integration in VM execution loop
- [ ] Breakpoint hit detection
- [ ] Step over/into/out state machine
- [ ] Frame-to-Environment adapter for variable inspection
- [ ] Watch expression evaluation (tree-walk fallback initially)
- [ ] Thread tracking for forked VMs

---

### Phase 8: Integration and Migration

**Goal:** Wire the bytecode VM into the full Stash stack as the default execution backend, achieve full test parity, validate benchmark improvements, and support all entry points (CLI, REPL, Playground, StashEngine embedding).

**Depends on:** Phase 7 (all features + debugging working)

#### 8.1 StashEngine Integration

```csharp
public class StashEngine
{
    public ExecutionBackend Backend { get; set; } = ExecutionBackend.Bytecode;

    public ExecutionResult Run(string source)
    {
        var tokens = Lexer.ScanTokens(source);
        var stmts = Parser.Parse(tokens);
        Resolver.Resolve(stmts);

        return Backend switch
        {
            ExecutionBackend.Bytecode => RunBytecode(stmts),
            ExecutionBackend.TreeWalk => RunTreeWalk(stmts),
        };
    }

    private ExecutionResult RunBytecode(List<Stmt> stmts)
    {
        Chunk chunk = Compiler.Compile(stmts);
        return _vm.Execute(chunk);
    }
}
```

#### 8.2 Dual-Mode Execution

Both backends available via configuration:

- **CLI flag:** `stash --backend=vm script.stash` (default) / `stash --backend=treewalk script.stash`
- **StashEngine API:** `engine.Backend = ExecutionBackend.TreeWalk`
- **Environment variable:** `STASH_BACKEND=treewalk` (useful for debugging the VM itself)

The tree-walk interpreter is preserved indefinitely as a reference implementation and correctness oracle.

#### 8.3 REPL Support

The REPL requires incremental compilation — each line/block the user enters must compile and execute in the context of previous lines. This means:

- Global state persists across REPL inputs (existing behavior)
- Each input compiles to a Chunk that references globals from previous inputs
- Local variables from previous inputs are promoted to globals

#### 8.4 Playground (Blazor WASM)

The Playground uses `StashEngine` via `PlaygroundExecutor`. Swapping the backend requires:

1. Including `Stash.Bytecode` in the WASM build
2. Setting VM backend as default
3. Verifying execution under Mono WASM runtime (no unsafe code issues)

The Playground may actually benefit more than native from the VM due to WASM's interpreter overhead — fewer function calls and better data locality help the WASM runtime.

#### 8.5 Test Parity

**Hard requirement:** All 4,414+ passing tests must pass on the VM backend before it ships as default.

Strategy:

1. Run the full test suite with VM backend
2. Track failures in a matrix: `{TestName, TreeWalk=Pass/Fail, VM=Pass/Fail}`
3. Fix VM-only failures until the matrix is clean
4. Any test that passes on tree-walk but fails on VM is a compiler/VM bug

#### 8.6 Benchmark Validation

Run the full 7-language benchmark suite with VM backend. Expected improvement targets:

| Benchmark             | Tree-Walk | VM Target  | Rationale                                                     |
| --------------------- | --------- | ---------- | ------------------------------------------------------------- |
| Algorithms            | 1,895 ms  | 200–400 ms | Recursion + arithmetic: massive dispatch + boxing elimination |
| Function Calls        | 1,824 ms  | 150–300 ms | No Environment allocation per call, no ReturnException        |
| Expression Throughput | 1,177 ms  | 150–300 ms | Flat stack ops vs 70-variable environment lookups             |
| Built-in Functions    | 737 ms    | 400–600 ms | Built-in call overhead remains (List\<object?\> bridge)       |
| Scope Lookup          | 1,487 ms  | 100–250 ms | Stack locals + upvalues vs chain-walking                      |

These targets would place Stash within 2–5× of CPython on most benchmarks, a dramatic improvement from the current 10–25×.

#### 8.7 Performance Profiling

After initial benchmarks, profile the VM hot loop to identify:

- Branch misprediction hotspots in the opcode switch
- Memory allocation hotspots (still boxing into `object?`)
- Cache miss patterns

This data feeds into the future `StashValue` tagged union optimization (not part of this roadmap, but the natural next step).

#### 8.8 Success Criteria

- All 4,414+ tests pass on VM backend
- Benchmarks show 3–10× improvement over tree-walk
- REPL works with VM backend (state persistence across inputs)
- Playground works with VM backend under WASM
- `stash --backend=treewalk` fallback works for any script
- DAP debugging works end-to-end with VM

#### 8.9 Deliverables

- [ ] `ExecutionBackend` enum and StashEngine dual-mode support
- [ ] CLI `--backend` flag
- [ ] REPL incremental compilation
- [ ] Playground WASM integration
- [ ] Full test suite parity report
- [ ] Benchmark comparison report (tree-walk vs VM vs other languages)
- [ ] Performance profiling report with optimization recommendations

---

## 5. Cross-Cutting Concerns

### 5.1 Project Structure

```
Stash.Bytecode/
├── OpCode.cs                 # Opcode enum (Phase 1)
├── Chunk.cs                  # Compiled function (Phase 1)
├── ChunkBuilder.cs           # Bytecode emission helper (Phase 1)
├── SourceMap.cs              # Bytecode → source mapping (Phase 1)
├── UpvalueDescriptor.cs      # Upvalue metadata (Phase 1)
├── Disassembler.cs           # Debug output (Phase 1)
├── Compiler.cs               # AST → Bytecode (Phases 2–6)
├── CompilerScope.cs          # Local variable tracking (Phase 2)
├── VirtualMachine.cs         # Bytecode execution (Phases 3–7)
├── CallFrame.cs              # VM call frame (Phase 3)
├── Upvalue.cs                # Runtime upvalue (Phase 4)
├── VMFunction.cs             # Compiled function wrapper (Phase 4)
├── ExceptionHandler.cs       # Try/catch metadata (Phase 6)
└── RuntimeOps.cs             # Shared runtime operations (Phase 3)
```

### 5.2 Testing Strategy

Each phase adds tests at two levels:

1. **Compiler tests:** Verify that compiling a specific AST produces the expected bytecodes (via disassembler comparison).
2. **VM integration tests:** Verify that compile → execute produces the same result as tree-walk for the same source code. This leverages the existing 4,414+ test suite by adding a VM execution path.

**Dual-execution test harness:**

```csharp
[Theory]
[InlineData("1 + 2", 3L)]
[InlineData("'hello' + ' world'", "hello world")]
public void Expression_ProducesSameResult(string source, object? expected)
{
    // Tree-walk
    var twResult = TreeWalkEngine.Evaluate(source);
    Assert.Equal(expected, twResult);

    // Bytecode VM
    var vmResult = BytecodeEngine.Evaluate(source);
    Assert.Equal(expected, vmResult);
}
```

### 5.3 Error Messages

The VM must produce error messages with accurate source locations. Every `RuntimeError` thrown from the VM must include a `SourceSpan` derived from the source map at the current IP. Error messages and stack traces should be identical to tree-walk output.

### 5.4 Memory Management

The VM relies on .NET's garbage collector for all heap-allocated values. There is no custom memory management. The primary arena for optimization is reducing allocations:

- Locals live on the flat value stack (no per-frame allocation)
- Upvalues are individual heap objects (shared between closures)
- Constants are shared across all executions of the same Chunk
- Built-in call args require `List<object?>` allocation (optimizable later)

### 5.5 Thread Safety

- Each VM instance is single-threaded (single execution context)
- `VM.Fork()` creates independent VM instances for async tasks
- Globals are shared via the same `ConcurrentDictionary`-backed `Environment`
- Module cache is shared and thread-safe (same `_sharedModuleCache`)
- No need for locks within the VM execution loop

---

## 6. Future Optimizations (Post-Phase 8)

These are explicitly **out of scope** for this roadmap but documented as the next steps:

| Optimization                          | Expected Impact                             | Complexity                                   |
| ------------------------------------- | ------------------------------------------- | -------------------------------------------- |
| **StashValue tagged union**           | 20–40% (eliminates boxing)                  | High — pervasive type change                 |
| **Inline caching for field access**   | 10–20% (faster struct/dict field lookups)   | Medium                                       |
| **Superinstructions**                 | 5–15% (fused LOAD_LOCAL + ADD, etc.)        | Medium                                       |
| **Register-based opcodes**            | 10–30% (fewer push/pop for common patterns) | High — compiler rewrite                      |
| **NaN-boxing**                        | 10–30% (compact value representation)       | High — requires unsafe code                  |
| **Computed gotos**                    | 5–10% (eliminates switch overhead)          | Low — but .NET doesn't support this natively |
| **JIT compilation (Reflection.Emit)** | 5–50× for hot functions                     | Very high — breaks AOT                       |
| **Bytecode serialization**            | Startup time improvement (skip compilation) | Low                                          |

---

## 7. Risk Register

| Risk                                      | Impact       | Probability | Mitigation                                                                                |
| ----------------------------------------- | ------------ | ----------- | ----------------------------------------------------------------------------------------- |
| Upvalue lifecycle bugs in closures        | Correctness  | Medium      | Extensive closure test suite; compare output vs tree-walk for every closure pattern       |
| Exception handler edge cases              | Correctness  | Medium      | Try/catch/finally combinatorial tests; test nested try blocks, returns from finally, etc. |
| WASM compatibility issues                 | Deployment   | Low         | Test early on Blazor WASM; avoid unsafe code in critical paths                            |
| Built-in call overhead limits improvement | Performance  | Medium      | Accepted for initial release; StashValue optimization addresses this later                |
| Module compilation changes import timing  | Correctness  | Low         | Run full import test suite; test circular imports, re-imports, dynamic paths              |
| DAP variable inspection lag from adapter  | Debugging UX | Low         | Cache Environment adapters; only build on demand                                          |
| REPL state management complexity          | Correctness  | Medium      | Test multi-line REPL sessions with closures, imports, struct definitions                  |
| Phase duration exceeds estimates          | Schedule     | Medium      | Each phase is independently valuable; can ship intermediate phases                        |

---

## 8. Phase Dependency Graph

```
Phase 1: Infrastructure ─── no dependencies
    │
    ▼
Phase 2: Compiler ────────── depends on Phase 1 (Chunk, opcodes)
    │
    ▼
Phase 3: VM Core ─────────── depends on Phase 2 (compiled Chunks to execute)
    │
    ▼
Phase 4: Functions ────────── depends on Phase 3 (VM to run compiled functions)
    │
    ▼
Phase 5: Objects ─────────── depends on Phase 4 (method calls, UFCS)
    │
    ▼
Phase 6: Advanced ─────────── depends on Phase 5 (types needed for error handling, modules, etc.)
    │
    ▼
Phase 7: Debugger ─────────── depends on Phase 6 (all features needed for full debugging)
    │
    ▼
Phase 8: Integration ──────── depends on Phase 7 (debugger needed for full shipping)
```

Each phase produces a working, testable artifact. The VM is usable (without debugging) after Phase 6. Debugging adds Phase 7. Full shipping readiness is Phase 8.

---

## 9. Decision Log

| Date       | Decision                                    | Rationale                                                                                                         |
| ---------- | ------------------------------------------- | ----------------------------------------------------------------------------------------------------------------- |
| 2026-04-04 | Use `object?` initially (not StashValue)    | Allows reusing all existing runtime types, built-ins, and collections. StashValue is a follow-up optimization.    |
| 2026-04-04 | Stack-based VM (not register-based)         | Simpler to compile for, well-proven (CPython, Ruby YARV, Lua 4, CLR). Register-based is a future optimization.    |
| 2026-04-04 | New `Stash.Bytecode` project                | Clean separation. Does not modify existing Stash.Interpreter project. Both backends coexist.                      |
| 2026-04-04 | Keep tree-walk interpreter permanently      | Low maintenance cost. Serves as correctness oracle, debug mode, and regression tool.                              |
| 2026-04-04 | Built-ins called via IStashCallable bridge  | Avoids rewriting 900+ built-in function implementations. List\<object?\> allocation overhead is acceptable.       |
| 2026-04-04 | Locals on value stack (not Environment)     | Eliminates per-call Environment allocation. O(1) array access instead of linked-list chain walking.               |
| 2026-04-04 | Cancellation checked at backward jumps only | Loops always have backward jumps. Forward-only code is bounded by program size. Reduces per-instruction overhead. |
| 2026-04-04 | Watch expressions via tree-walk fallback    | Simplest approach. Full VM compilation for watch expressions is a Phase 8+ optimization.                          |
| 2026-04-04 | 8 phases, strictly sequential               | Each phase builds on the previous. No phase can be started before its dependency is complete and tested.          |
