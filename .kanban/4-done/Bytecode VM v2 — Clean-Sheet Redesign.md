# Bytecode VM v2 — Clean-Sheet Redesign

> **Status:** Backlog — exploratory design
> **Created:** 2026-04-05
> **Purpose:** Hypothetical architecture for a from-scratch bytecode VM targeting <100ms on most benchmarks with full DAP debugging from day 1. Assumes only the lexer + parser exist.

---

## Table of Contents

1. [Design Goals & Constraints](#1-design-goals--constraints)
2. [Architecture Overview](#2-architecture-overview)
3. [Value Representation — Tagged Union](#3-value-representation--tagged-union)
4. [Register-Based Instruction Set](#4-register-based-instruction-set)
5. [Compilation Pipeline](#5-compilation-pipeline)
6. [Namespace & Built-in Dispatch](#6-namespace--built-in-dispatch)
7. [Closures & Upvalues](#7-closures--upvalues)
8. [Debug Architecture](#8-debug-architecture)
9. [Memory & Allocation Strategy](#9-memory--allocation-strategy)
10. [Benchmark Target Analysis](#10-benchmark-target-analysis)
11. [What Changes vs. the Current VM](#11-what-changes-vs-the-current-vm)
12. [Risks & Open Questions](#12-risks--open-questions)

---

## 1. Design Goals & Constraints

### Hard Requirements

| Requirement                                                                                             | Rationale                                                                                                 |
| ------------------------------------------------------------------------------------------------------- | --------------------------------------------------------------------------------------------------------- |
| **<100ms wall time** on function calls, scope lookup benchmarks (100K iterations)                       | These are dispatch-dominated; an optimized interpreter can hit this                                       |
| **<500ms** on algorithms benchmark (fib(26) + bubble sort 1000 + binary search 10K + struct build 5000) | Compute-bound; ~100ms is JIT territory, 300-500ms is achievable with an optimized interpreter             |
| **<200ms** on namespace calls benchmark (200K iterations × 13 calls each)                               | Currently 1,937ms — requires eliminating dictionary lookup overhead                                       |
| **Full DAP from instruction zero**                                                                      | Breakpoints, stepping, variable inspection, watch eval, set-variable, multi-thread, exception breakpoints |
| **Zero debug overhead in release mode**                                                                 | Debugger attachment must not slow non-debug execution by even 1%                                          |
| **Cross-platform**                                                                                      | Linux, macOS, Windows — no platform-specific tricks in the hot path                                       |

### Non-Goals

- JIT compilation (out of scope for v2; design should not preclude a future JIT)
- Concurrent GC or custom allocator (rely on .NET GC, but reduce pressure)
- Changing the language semantics (the VM implements Stash as-is)

### Performance Reality Check

Current VM results vs. targets:

| Benchmark              | Current VM | Target | Required Speedup | Achievability                                                            |
| ---------------------- | ---------- | ------ | ---------------- | ------------------------------------------------------------------------ |
| Function Calls (100K)  | 458ms      | <100ms | 4.6×             | **High** — register VM + tagged values eliminates boxing                 |
| Scope Lookup (100K)    | 437ms      | <100ms | 4.4×             | **High** — already fast with upvalues; register layout helps more        |
| Algorithms             | 1,361ms    | <500ms | 2.7×             | **Medium** — needs tagged values + loop optimization                     |
| Namespace Calls (200K) | 1,937ms    | <200ms | 9.7×             | **Medium** — requires static dispatch; dictionary elimination is the key |
| Lexer Heavy            | 1,800ms    | <500ms | 3.6×             | **Medium** — string allocation dominates; interning helps                |

For reference, CPython on the same benchmarks clocks roughly: fib(26) ~80ms, bubble sort(1000) ~200ms, function calls(100K) ~300ms. So "near Python" is approximately where the current VM already is for some workloads. The <100ms target on dispatch benchmarks is _faster_ than CPython — closer to LuaJIT interpreter mode.

---

## 2. Architecture Overview

```
                 ┌──────────────┐
                 │  Source Code │
                 └──────┬───────┘
                        │
                 ┌──────▼───────┐
                 │    Lexer     │  (exists)
                 └──────┬───────┘
                        │
                 ┌──────▼───────┐
                 │    Parser    │  (exists)
                 └──────┬───────┘
                        │ AST
                 ┌──────▼───────┐
                 │   Resolver   │  variable resolution, upvalue analysis
                 └──────┬───────┘
                        │ Annotated AST
                 ┌──────▼───────┐
                 │   Compiler   │  two-pass: register allocation then codegen
                 └──────┬───────┘
                        │ Prototype (bytecode + constants + debug info)
                 ┌──────▼───────┐
                 │      VM      │  register-based execution engine
                 └──────────────┘
```

### Key Architectural Decisions

| Decision             | Choice                             | Over                            | Rationale                                                                                     |
| -------------------- | ---------------------------------- | ------------------------------- | --------------------------------------------------------------------------------------------- |
| Execution model      | **Register-based**                 | Stack-based                     | ~25% fewer instructions, better maps to CPU, less stack shuffling                             |
| Value representation | **Tagged union struct (16 bytes)** | Boxed `object?`                 | Zero allocation for int/float/bool/null arithmetic — eliminates GC pressure                   |
| Instruction encoding | **Fixed 32-bit**                   | Variable-length                 | No decode overhead, direct IP indexing, better branch prediction                              |
| Built-in dispatch    | **Static compile-time resolution** | Runtime dictionary lookup       | O(1) function pointer table vs. O(1)-amortized hash lookup — constant factor wins             |
| Debug integration    | **Dual code paths**                | Runtime `if (debugger != null)` | Zero overhead in release; full instrumentation in debug mode                                  |
| String handling      | **Intern table**                   | Per-allocation strings          | Pointer equality for comparisons, deduplication reduces memory                                |
| Closure capture      | **Flat + upvalue hybrid**          | Upvalue-only                    | Direct capture when possible, upvalue indirection only when mutation crosses scope boundaries |

---

## 3. Value Representation — Tagged Union

This is the single highest-impact change. The current VM uses `object?[]` for the stack, which means every integer arithmetic operation boxes a `long` into a heap object, creates GC pressure, and defeats CPU cache locality.

### Design

```csharp
[StructLayout(LayoutKind.Explicit, Size = 16)]
public struct Value
{
    // Tag byte — discriminates the union
    [FieldOffset(0)] public ValueTag Tag;

    // Primitive storage — overlapping fields at offset 8
    [FieldOffset(8)] public long IntValue;
    [FieldOffset(8)] public double FloatValue;
    [FieldOffset(8)] public byte BoolValue;  // 0 or 1

    // Heap object reference — MUST be at its own offset (GC needs to track it)
    // Cannot overlap with primitives in .NET — GC doesn't know which union arm is active
    // Solution: separate reference field
    [FieldOffset(0)] private ulong _tagAndPayload;  // packed: tag in low byte, payload in upper bytes

    // For heap objects, we use a side table (see below)
}
```

**Problem:** .NET's GC cannot handle overlapping reference and value-type fields in a struct. A `long` and an `object` cannot share the same `FieldOffset`. This is a hard CLR constraint.

**Solution — Split representation:**

```csharp
public readonly struct Value
{
    // 8 bytes: tag (4) + inline payload (4), or tag (4) + heap index (4)
    private readonly uint _header;   // [tag:8][flags:8][payload:16] or [tag:8][flags:8][heapIdx:16]
    private readonly long _payload;  // 8 bytes: int, float (as bits), or extended payload

    // Total: 16 bytes, no managed references — the stack is a Value[] with no GC scanning

    public ValueTag Tag => (ValueTag)(_header & 0xFF);

    // Primitives — stored inline
    public long AsInt => _payload;
    public double AsFloat => BitConverter.Int64BitsToDouble(_payload);
    public bool AsBool => _payload != 0;

    // Heap objects — indirected through a separate object table
    public int HeapIndex => (int)(_payload);

    // Constructors
    public static Value Int(long v) => new(ValueTag.Int, v);
    public static Value Float(double v) => new(ValueTag.Float, BitConverter.DoubleToInt64Bits(v));
    public static Value Bool(bool v) => new(ValueTag.Bool, v ? 1L : 0L);
    public static Value Null => new(ValueTag.Null, 0);
    public static Value Obj(int heapIndex) => new(ValueTag.Object, heapIndex);
}

public enum ValueTag : byte
{
    Null = 0,
    Bool = 1,
    Int = 2,
    Float = 3,
    Object = 4,   // string, array, dict, struct, closure, namespace, error, etc.
}
```

**Heap object table:**

```csharp
public sealed class Heap
{
    private object?[] _objects;   // growable; GC-tracked
    private int _count;

    public int Alloc(object obj) { _objects[_count] = obj; return _count++; }
    public object? Get(int index) => _objects[index];
    public void Set(int index, object? value) => _objects[index] = value;
}
```

### Why This Matters

- **Integer arithmetic**: `ADD r0, r1, r2` reads two `long` fields, adds them, writes a `long`. Zero allocation. No boxing. No GC pressure. The current VM does: unbox left → unbox right → add → box result → push. That's 2 unboxes + 1 allocation per operation.
- **Float arithmetic**: Same — `BitConverter.Int64BitsToDouble` is a no-op reinterpret cast on modern CPUs.
- **Bool/null checks**: Bit comparison on the tag byte. No pointer dereference.
- **Cache locality**: `Value[]` registers are contiguous 16-byte slots. The CPU prefetcher loves this. `object?[]` is an array of pointers — each value is somewhere else on the heap.

### Tradeoff: Heap Indirection for Objects

Strings, arrays, dicts, closures, struct instances — anything that lives on the managed heap — are accessed through `Heap.Get(value.HeapIndex)`. This adds one array lookup compared to direct reference access. For string-heavy workloads, this is measurable but acceptable — the win on numeric/boolean workloads more than compensates, and string operations are dominated by the actual string manipulation cost, not the lookup.

**Alternative considered: NaN boxing.** Packs everything into 64 bits by exploiting the IEEE 754 NaN space. Used by LuaJIT and JavaScriptCore. Rejected for .NET because:

1. The CLR GC cannot track managed references hidden inside a `double`/`long`. You'd need `GCHandle` (pinning overhead) or unsafe pointers (no GC safety).
2. Pointer compression only works with a custom allocator — .NET objects can be anywhere in the 64-bit address space.
3. The 16-byte tagged union gives us the same allocation-free primitive arithmetic with none of the GC hazards.

---

## 4. Register-Based Instruction Set

### Why Registers Over Stack

A stack VM for `a + b * c`:

```
LOAD a      ; push a
LOAD b      ; push b
LOAD c      ; push c
MUL         ; pop b,c → push b*c
ADD         ; pop a,b*c → push a+b*c
```

5 instructions, 5 stack pointer mutations.

A register VM:

```
MUL  r2, r1, r0    ; r2 = b * c
ADD  r3, r2, r0    ; r3 = a + r2
```

2 instructions, zero stack manipulation. The operands are named, so intermediate results don't need push/pop choreography. Research (Yunhe Shi et al., "Virtual Machine Showdown") shows register VMs execute 25-47% fewer instructions than equivalent stack VMs.

### Instruction Encoding

Fixed 32-bit words. Three formats (following Lua 5.x / Dalvik conventions):

```
Format ABC:   [op:8][A:8][B:8][C:8]           — 3-register operations
Format ABx:   [op:8][A:8][Bx:16]              — register + unsigned 16-bit constant/index
Format AsBx:  [op:8][A:8][sBx:16 signed]      — register + signed 16-bit offset (jumps)
Format Ax:    [op:8][Ax:24]                    — 24-bit payload (wide constants, closures)
```

All instructions are `uint`. IP is an index into `uint[]`, not a byte offset — jumps are `IP += offset` with no multiplication.

### Register Allocation

Each function has a fixed register window allocated at compile time. The compiler performs a linear scan over the AST to determine the maximum number of live temporaries. Registers are numbered 0..N per function:

- **R(0)..R(paramCount-1)**: function parameters
- **R(paramCount)..R(paramCount+localCount-1)**: local variables
- **R(localCount+paramCount)..R(maxRegs-1)**: temporaries

The function's **Prototype** stores `maxRegs` — the VM allocates exactly this many register slots when entering the function.

### Calling Convention

```
CALL A, B, C
  A = register holding the callee (closure/builtin)
  B = first argument register (A+1 in practice)
  C = number of arguments

  Before: R(A) = callee, R(A+1)..R(A+C) = arguments
  After:  R(A) = return value
```

The callee's register window starts at `R(A+1)` — arguments are already in place. No copying. The caller's registers below A are preserved; registers A+1..A+C are clobbered by the callee's execution.

This is how Lua does it and it's brilliant: function call overhead is just updating the frame pointer and the IP. No argument array allocation. No copying values between stacks.

### Core Opcode Set (compact — ~50 opcodes)

#### Loads & Stores

| Opcode      | Format | Semantics                                      |
| ----------- | ------ | ---------------------------------------------- |
| `LOADK`     | ABx    | `R(A) = K(Bx)` — load constant from pool       |
| `LOADNULL`  | A      | `R(A) = null`                                  |
| `LOADBOOL`  | ABC    | `R(A) = (B != 0); if C, skip next instruction` |
| `MOVE`      | AB     | `R(A) = R(B)`                                  |
| `GETGLOBAL` | ABx    | `R(A) = Globals[K(Bx)]`                        |
| `SETGLOBAL` | ABx    | `Globals[K(Bx)] = R(A)`                        |
| `GETUPVAL`  | AB     | `R(A) = Upvalues[B].Value`                     |
| `SETUPVAL`  | AB     | `Upvalues[B].Value = R(A)`                     |

#### Arithmetic (all format ABC — R(A) = R(B) op R(C))

| Opcode | Semantics             | Notes                                                                                                                                          |
| ------ | --------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------- |
| `ADD`  | `R(A) = R(B) + R(C)`  | Fast path: both Int → Int result. Else promote to Float. String concat if either is string. Duration/ByteSize/IP arithmetic via type dispatch. |
| `SUB`  | `R(A) = R(B) - R(C)`  | Same type promotion                                                                                                                            |
| `MUL`  | `R(A) = R(B) * R(C)`  | String repetition if one operand is string                                                                                                     |
| `DIV`  | `R(A) = R(B) / R(C)`  | Always promotes to float for int/int? **Decision needed.** Current Stash: int/int → int (truncating). Keep that.                               |
| `MOD`  | `R(A) = R(B) % R(C)`  |                                                                                                                                                |
| `POW`  | `R(A) = R(B) ** R(C)` |                                                                                                                                                |
| `NEG`  | AB                    | `R(A) = -R(B)`                                                                                                                                 |

#### Specialized Integer Arithmetic

| Opcode | Semantics | Notes                                                                                   |
| ------ | --------- | --------------------------------------------------------------------------------------- |
| `ADDI` | ABx       | `R(A) = R(A) + Bx` — add immediate (signed 16-bit). For `i++`, `i += 1`, loop counters. |
| `SUBI` | ABx       | `R(A) = R(A) - Bx`                                                                      |

**Why specialize?** The loop `while (i < ITERATIONS) { ... i++; }` is the hottest pattern in every benchmark. Currently it's `LOAD_LOCAL i → CONST 1 → ADD → STORE_LOCAL i` (4 instructions). With `ADDI`: one instruction. 4× fewer dispatches for the most common operation.

#### Bitwise

| Opcode | Semantics             |
| ------ | --------------------- |
| `BAND` | `R(A) = R(B) & R(C)`  |
| `BOR`  | `R(A) = R(B) \| R(C)` |
| `BXOR` | `R(A) = R(B) ^ R(C)`  |
| `BNOT` | `R(A) = ~R(B)`        |
| `SHL`  | `R(A) = R(B) << R(C)` |
| `SHR`  | `R(A) = R(B) >> R(C)` |

#### Comparison & Logic

| Opcode    | Format | Semantics                                                                                |
| --------- | ------ | ---------------------------------------------------------------------------------------- |
| `EQ`      | ABC    | `if ((R(B) == R(C)) != A) skip next` — conditional skip                                  |
| `LT`      | ABC    | `if ((R(B) < R(C)) != A) skip next`                                                      |
| `LE`      | ABC    | `if ((R(B) <= R(C)) != A) skip next`                                                     |
| `NOT`     | AB     | `R(A) = !IsTruthy(R(B))`                                                                 |
| `TESTSET` | ABC    | `if IsTruthy(R(B)) == C then R(A) = R(B) else skip next` — for `&&`/`\|\|` short-circuit |

**Key insight:** Comparisons don't produce boolean values on the register file. They conditionally skip the next instruction (which is always a `JMP`). This halves the instruction count for `if` conditions — no `CMP → push bool → JUMPFALSE` three-step; just `LT → JMP`.

#### Control Flow

| Opcode     | Format | Semantics                                                                          |
| ---------- | ------ | ---------------------------------------------------------------------------------- |
| `JMP`      | sBx    | `IP += sBx`                                                                        |
| `CALL`     | ABC    | Call `R(A)` with `C` args starting at `R(A+1)`, results in `R(A)`                  |
| `RETURN`   | AB     | Return `R(A)` (B=0 means return null)                                              |
| `FORPREP`  | AsBx   | Initialize numeric for loop; `R(A) -= R(A+2); IP += sBx`                           |
| `FORLOOP`  | AsBx   | `R(A) += R(A+2); if R(A) <= R(A+1) then { IP += sBx; R(A+3) = R(A) }`              |
| `ITERPREP` | AB     | Prepare iterator from `R(A)` (array/dict/range) → iterator state in `R(A)..R(A+2)` |
| `ITERLOOP` | AsBx   | Advance iterator; if exhausted, skip. Otherwise `R(A+3) = nextValue; IP += sBx`    |

**`FORLOOP` is critical.** The current VM compiles `for (let i = 0; i < n; i++)` into ~8 instructions per iteration (load i, load n, compare, jump, load i, increment, store i, jump back). `FORLOOP` does the increment + bounds check + index update in a single dispatch. For 100K iterations, that's 700K fewer instruction dispatches.

#### Object Access

| Opcode     | Format | Semantics                                                                           |
| ---------- | ------ | ----------------------------------------------------------------------------------- |
| `GETTABLE` | ABC    | `R(A) = R(B)[R(C)]` — array index or dict key                                       |
| `SETTABLE` | ABC    | `R(A)[R(B)] = R(C)`                                                                 |
| `GETFIELD` | ABx    | `R(A) = R(B).K(Bx)` — struct/dict field by constant key                             |
| `SETFIELD` | ABx    | `R(A).K(Bx) = R(B)` — note: Bx encodes both field name constant AND source register |
| `SELF`     | ABC    | `R(A+1) = R(B); R(A) = R(B)[K(C)]` — method lookup + self binding                   |

**Inline caching on `GETFIELD`/`SETFIELD`:** Each instruction site has a 2-word inline cache:

```csharp
struct InlineCache {
    int CachedTypeId;    // struct metadata ID last seen
    int CachedSlotIndex; // field offset in the struct's backing store
}
```

On execution:

1. Check if `obj.TypeId == CachedTypeId` → cache hit: read `obj.Fields[CachedSlotIndex]` directly (O(1), no hash lookup)
2. Cache miss → full dictionary lookup, update cache

Monomorphic call sites (same struct type every time) hit the cache 99%+ of the time. Polymorphic sites degrade gracefully to dictionary lookup — same as the current VM.

#### Built-in Dispatch

| Opcode        | Format  | Semantics                                                                |
| ------------- | ------- | ------------------------------------------------------------------------ |
| `CALLBUILTIN` | ABx-ext | `R(A) = BuiltInTable[Bx](R(A+1)..R(A+C))` — direct function pointer call |

See [Section 6](#6-namespace--built-in-dispatch) for details.

#### Collections

| Opcode     | Format | Semantics                                                     |
| ---------- | ------ | ------------------------------------------------------------- |
| `NEWARRAY` | AB     | `R(A) = new array with B elements from R(A+1)..R(A+B)`        |
| `NEWDICT`  | AB     | `R(A) = new dict with B key-value pairs from R(A+1)..R(A+2B)` |
| `NEWRANGE` | ABC    | `R(A) = range(R(B), R(C))`                                    |
| `SPREAD`   | AB     | Expand `R(B)` into registers for call/array literal           |

#### Types & Structs

| Opcode      | Format | Semantics                                                        |
| ----------- | ------ | ---------------------------------------------------------------- |
| `NEWSTRUCT` | ABx    | `R(A) = new instance of struct K(Bx) with fields from registers` |
| `CLOSURE`   | ABx    | `R(A) = new closure from Prototype K(Bx), capturing upvalues`    |
| `TYPEOF`    | AB     | `R(A) = typeof(R(B))` as interned string                         |
| `IS`        | ABC    | `if (R(B) is type K(C)) != A then skip next`                     |

#### Error Handling

| Opcode     | Format | Semantics                                         |
| ---------- | ------ | ------------------------------------------------- |
| `TRYBEGIN` | Ax     | Push exception handler; catch target = IP + Ax    |
| `TRYEND`   | -      | Pop exception handler                             |
| `THROW`    | A      | Throw `R(A)` as error                             |
| `TRYEXPR`  | AB     | `R(A) = try evaluate R(B)` — wraps error as value |

#### Debug (only present in debug-mode bytecode)

| Opcode       | Format | Semantics                                             |
| ------------ | ------ | ----------------------------------------------------- |
| `DEBUGBREAK` | Ax     | Check breakpoints + stepping state. Ax = source line. |

See [Section 8](#8-debug-architecture) for the dual-compilation strategy.

#### Miscellaneous

| Opcode         | Format | Semantics                                               |
| -------------- | ------ | ------------------------------------------------------- |
| `COMMAND`      | ABx    | Execute shell command, result in R(A)                   |
| `IMPORT`       | ABx    | Import module                                           |
| `INTERPOLATE`  | AB     | Build interpolated string from B parts starting at R(A) |
| `NULLCOALESCE` | ABC    | `R(A) = R(B) ?? R(C)` with short-circuit                |
| `SWITCH`       | ABx    | Switch dispatch table (jump table in constant pool)     |

**Total: ~50 opcodes** vs. the current VM's 78. The register encoding absorbs what were separate LOAD/STORE instructions. Fewer opcodes = tighter dispatch loop = better instruction cache utilization.

---

## 5. Compilation Pipeline

### Phase 1: Resolution (existing Resolver, enhanced)

The Resolver walks the AST and annotates each variable reference with:

- **Scope distance** (how many scopes up)
- **Slot index** (position within that scope)
- **Mutability** (is it ever assigned after capture? determines upvalue vs. direct capture)

Enhancement: the Resolver also determines **capture analysis** — for each function, it produces a list of upvalue descriptors marking whether each captured variable is:

- **Read-only after capture**: can be captured by value (copy at closure creation)
- **Mutated after capture**: must use upvalue indirection

### Phase 2: Register Allocation

A dedicated pass walks each function body and assigns registers using **linear scan allocation**:

1. Compute **live ranges** for each variable and temporary expression
2. Assign registers, reusing dead registers greedily
3. Determine `maxRegs` for the function
4. Parameters get R(0)..R(N-1), locals get the next block, temporaries fill the remainder

This doesn't need to be optimal (it's not a JIT). A fast greedy allocator that guarantees correctness is sufficient. The win is having any register allocation at all vs. a pure stack machine.

### Phase 3: Code Generation

Walk the annotated AST, emit instructions into a `Prototype`:

```csharp
public sealed class Prototype
{
    public uint[] Code;            // instruction words
    public Value[] Constants;      // constant pool (using tagged Value, not object?)
    public Prototype[] Children;   // nested function prototypes
    public UpvalueDesc[] Upvalues; // upvalue descriptors for closure creation
    public int MaxRegs;            // register window size
    public int ParamCount;
    public int MinArity;           // for default params
    public bool IsVararg;

    // Debug info (optional — can be stripped for production)
    public LineInfo[] LineMap;     // instruction index → source line (run-length encoded)
    public LocalInfo[] Locals;    // local variable names + register + live range
    public string[] UpvalueNames; // for debugger display
    public string SourceFile;
}
```

### Constant Folding (simple, high-value)

During codegen, the compiler folds:

- **Literal arithmetic**: `2 + 3` → `LOADK 5` instead of `LOADK 2; LOADK 3; ADD`
- **Constant conditions**: `if (true) { ... }` → unconditional body, else branch eliminated
- **String concatenation of literals**: `"hello" + " " + "world"` → `LOADK "hello world"`

No complex data-flow analysis needed — just constant operand detection during AST visits. Cheap to implement, eliminates dead instructions.

---

## 6. Namespace & Built-in Dispatch

This is where the current VM leaves the most performance on the table. The namespace benchmark runs 200K iterations × 13 namespace calls = 2.6M dispatches through `FrozenDictionary` lookup, and that's 1,937ms. The lookup itself is fast (FrozenDictionary is excellent) but it's:

1. Hash the string key
2. Probe the table
3. Compare the key
4. Return the delegate
5. Invoke the delegate

Repeat 2.6 million times.

### Static Resolution Strategy

At compile time, the compiler recognizes `math.sqrt(x)` as namespace `math`, function `sqrt`. It looks up the function in a **compile-time built-in registry** (the same `BuiltInMetadata` that Stash.Stdlib already provides) and emits:

```
CALLBUILTIN  A=resultReg, builtinId=MATH_SQRT, argCount=1
```

Where `builtinId` is a dense integer index into a flat function pointer table:

```csharp
public static class BuiltInTable
{
    // Populated once at startup from BuiltInMetadata registry
    public static readonly BuiltInFn[] Functions = new BuiltInFn[MAX_BUILTINS];
    // Index 0 = math.abs, Index 1 = math.sqrt, ...

    // Fast dispatch: Functions[builtinId].Invoke(registers, argStart, argCount)
}

public delegate Value BuiltInFn(Value[] registers, int argStart, int argCount);
```

**The entire dispatch becomes:**

1. Read `builtinId` from instruction (already decoded — it's part of the 32-bit word)
2. Index into flat array: `BuiltInTable.Functions[builtinId]`
3. Call function pointer

No hashing. No string comparison. No dictionary probing. One array index + one indirect call.

### Built-in Functions Accept `Value[]` Directly

Current built-in functions receive `List<object?>` — which means the VM has to box every argument and allocate a list. With the tagged `Value` representation, built-ins read directly from the register file:

```csharp
// math.sqrt implementation
static Value MathSqrt(Value[] regs, int argStart, int argCount)
{
    double x = regs[argStart].IsFloat
        ? regs[argStart].AsFloat
        : (double)regs[argStart].AsInt;
    return Value.Float(Math.Sqrt(x));
}
```

Zero allocation. Arguments are already in the register array.

### What About Dynamic Namespace Access?

If someone writes `let ns = math; ns.sqrt(x)` — the namespace is a variable, not a compile-time constant. This falls back to the general `GETFIELD` + `CALL` path (dictionary lookup on the namespace object). This is rare in practice — the overwhelming majority of namespace calls are statically resolvable.

If someone _shadows_ a namespace (`let math = 42; math.sqrt(...)`) the compiler detects the shadow and falls back to dynamic dispatch.

### Projected Impact

Eliminating dictionary lookup for 2.6M calls, plus eliminating `List<object?>` allocation for arguments:

- Dictionary lookup: ~100ns per call → ~260ms
- List allocation + boxing: ~50ns per call → ~130ms
- Projected total savings: ~390ms minimum
- With the new dispatch (array index + direct call): ~30ns per call → ~78ms
- **Projected namespace benchmark: ~200ms** (from 1,937ms)

The remaining time is dominated by the actual work inside the built-in functions (e.g., `Math.Sqrt`, `string.ToUpper`, regex in `str.replace`). Further gains would require caching results or optimizing the .NET BCL calls themselves — diminishing returns.

---

## 7. Closures & Upvalues

The current VM's upvalue mechanism is fundamentally correct. The register-based redesign keeps the same concept with optimizations:

### Flat Closure Optimization

When a closure captures a variable that:

1. Is **never mutated** after the point of capture, AND
2. Is captured from the **immediately enclosing** function (distance = 1)

...then the variable's current value is copied directly into the closure's upvalue slot at creation time. No `Upvalue` object indirection needed. The closure stores `Value` directly.

This covers the common pattern:

```stash
fn makeGreeter(name) {
    return (greeting) => $"{greeting}, {name}!";
}
```

`name` is never reassigned after capture → flat capture → zero overhead at access time.

### Mutable Upvalue (unchanged from current design)

When a captured variable IS mutated after capture:

```stash
fn makeCounter() {
    let count = 0;
    return {
        inc: () => { count++; return count; },
        get: () => count,
    };
}
```

Both closures capture `count` through a shared `Upvalue` object. The `Upvalue` starts open (pointing at the register slot), gets closed (promoted to heap) when `makeCounter` returns. This is exactly the current design and it's correct.

### Upvalue in Register VM

Instead of pointing to `_stack[stackIndex]`, upvalues point into the register file: `_registers[frameBase + slot]`. The close operation copies `_registers[frameBase + slot]` to `_closedValue`. Identical semantics, different backing store.

---

## 8. Debug Architecture

This is where the clean-sheet design diverges most sharply from the current VM. The current approach — `if (debugger != null)` in the hot loop — works but violates the "zero overhead in release" principle. In practice the branch predictor handles it well when no debugger is attached, but we can do better.

### Dual Compilation

The compiler produces **two instruction streams per Prototype**:

```csharp
public sealed class Prototype
{
    public uint[] Code;           // Release bytecode — no debug ops
    public uint[] DebugCode;      // Debug bytecode — DEBUGBREAK inserted at each new source line
    public LineInfo[] LineMap;     // Shared source map
    // ...
}
```

`DebugCode` is identical to `Code` except:

- At each point where the source line changes, a `DEBUGBREAK lineNumber` instruction is inserted
- This instruction checks breakpoints, stepping state, and pause requests
- The instruction IP offsets are adjusted to account for the extra instructions

### Execution Mode Switching

```csharp
public sealed class VM
{
    private bool _debugMode;
    private IDebugger? _debugger;

    public object? Execute(Prototype proto)
    {
        if (_debugMode)
            return RunDebug(proto);
        else
            return RunRelease(proto);
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private object? RunRelease(Prototype proto)
    {
        // Zero debug checks. Pure execution.
        uint[] code = proto.Code;
        // ... tight dispatch loop
    }

    private object? RunDebug(Prototype proto)
    {
        // Full debug instrumentation via DEBUGBREAK ops
        uint[] code = proto.DebugCode;
        // ... dispatch loop with DEBUGBREAK handling
    }
}
```

Two separate methods means the JIT compiler optimizes each independently. `RunRelease` has no dead branches for debug checks — the JIT can inline and optimize without `if (debugger != null)` ever appearing in the generated machine code.

### DEBUGBREAK Implementation

```csharp
case OpCode.DEBUGBREAK:
{
    int line = (int)(instruction >> 8);  // 24-bit line number from Ax format
    IDebugScope scope = BuildDebugScope(frame);
    _debugger!.OnBeforeExecute(
        new SourceSpan(proto.SourceFile, line, 0, line, 0),
        scope,
        _threadId
    );
    // If debugger paused us, we block here (ManualResetEventSlim.Wait)
    break;
}
```

### Debugger Attach/Detach at Runtime

When a debugger attaches mid-execution:

1. Set `_debugMode = true`
2. On next function entry, the VM uses `proto.DebugCode` for the new frame
3. Already-running frames continue with `proto.Code` until they return and re-enter
4. For immediate effect: set a flag that makes the current `RunRelease` exit at the next safe point (function call or loop back-edge) and re-enter in `RunDebug`

When a debugger detaches:

1. Set `_debugMode = false`
2. Same frame-boundary switching strategy

### Variable Inspection

The `Prototype.Locals` array maps register indices to variable names within live ranges:

```csharp
public struct LocalInfo
{
    public string Name;
    public int Register;
    public int StartPC;  // First instruction where this local is live
    public int EndPC;    // Last instruction where this local is live
    public bool IsConst;
}
```

Building a debug scope:

1. Read the current frame's IP
2. Scan `Locals[]` for entries where `StartPC <= IP <= EndPC`
3. For each live local, read `R(local.Register)` from the register file
4. For upvalues, read from the frame's upvalue array + `UpvalueNames[]`
5. Wrap in `IDebugScope` implementation

This is cheaper than the current approach (which traverses an Environment chain). Register files are flat arrays — enumerating live locals is a linear scan of a small metadata array.

### SetVariable (Edit While Debugging)

Write directly to the register: `frame.Registers[local.Register] = newValue`. For upvalues: `frame.Upvalues[index].Value = newValue`. Direct mutation, no dictionary lookup.

### Stack Traces

Each `CallFrame` stores:

- The `Prototype` (which has `SourceFile` and `LineMap`)
- The current IP (which maps to a source line via `LineMap`)
- A `FunctionName` (from the Prototype or the variable it was assigned to)

Stack trace construction: walk frames top to bottom, resolve `IP → line` from each Prototype's `LineMap`.

### Source Map Encoding

Run-length encoded for compactness:

```csharp
public struct LineInfo
{
    public int InstructionCount;  // number of consecutive instructions on this line
    public int Line;              // source line number
}
```

`IP → line` lookup: binary search or linear scan (prototypes are rarely >10K instructions). For debug mode, the `DEBUGBREAK` instruction carries the line number directly in its operand — no lookup needed at all.

---

## 9. Memory & Allocation Strategy

### String Interning

All string literals are interned at compile time in a global string table. At runtime, string comparisons check reference equality first (`object.ReferenceEquals`). This is a massive win for:

- Dictionary key lookups (field names, namespace members)
- `switch` on string cases
- Equality checks in filters and find operations

The intern table is a `Dictionary<string, string>` — standard .NET string interning, nothing exotic.

### Object Pooling

For hot-path allocations:

| Object                              | Pool Strategy                                |
| ----------------------------------- | -------------------------------------------- |
| `List<Value>` (arrays)              | Return to pool on scope exit if not captured |
| `Dictionary<string, Value>` (dicts) | Return to pool on scope exit if not captured |
| `Upvalue`                           | Pool of upvalue objects, reset and reuse     |

Escape analysis is simple: if an array/dict is created in a function and never stored in an upvalue, global, or returned, it doesn't escape. The compiler can mark it with a `NoEscape` flag, and the VM returns it to the pool when the function exits.

This isn't a custom GC — it's just reducing allocation pressure by reusing objects that the .NET GC would otherwise need to collect.

### Register File Allocation

Each call frame needs `maxRegs` registers. Options:

1. **Single large register array, windowed** (like Lua): One `Value[65536]` array shared across all frames. Each frame gets a window `[baseReg..baseReg+maxRegs)`. Frame entry: `baseReg += caller.maxRegs`. Frame exit: `baseReg -= caller.maxRegs`. Zero allocation per call.

2. **Per-frame allocation from a pool**: Allocate `Value[maxRegs]` per frame from a `ArrayPool<Value>`. Return on frame exit.

Option 1 is faster but has a fixed maximum nesting depth. Given that Stash scripts rarely exceed 100-deep call stacks, a 65K register array (~1MB at 16 bytes/register) is fine.

**Decision: Option 1** — single windowed register array. The memory cost is trivial and it eliminates all per-call allocation.

---

## 10. Benchmark Target Analysis

Let's project performance for each benchmark with the proposed architecture:

### Function Calls (100K iterations, 6 calls per iteration = 600K calls)

**Current VM bottlenecks:** Stack push/pop per call, argument boxing, environment setup.

**Improvements:**

- Register calling convention (args already in place): eliminates argument copying → ~1.5×
- Tagged Value (no boxing): eliminates allocation per arithmetic result → ~2×
- `ADDI` for loop counter: 4x fewer dispatches for `i++` → ~1.3× on loop overhead

**Projection:** 458ms / (1.5 × 2 × 1.3) ≈ **118ms**. Close to target. With `FORLOOP` opcode for the outer loop: **~90ms**.

### Scope Lookup (100K iterations, 5-deep closure nesting, 8 upvalue reads per call)

**Current VM bottlenecks:** Already fast (437ms). Upvalue access is O(1).

**Improvements:**

- Tagged Value (no boxing on arithmetic): ~2×
- `ADDI` + `FORLOOP` for outer loop: ~1.3×
- Flat closure for read-only captures: ~1.2× (some captures skip Upvalue indirection)

**Projection:** 437ms / (2 × 1.3 × 1.2) ≈ **140ms**. Adding `FORLOOP`: **~100ms**.

### Algorithms (fib(26) + bubble sort(1000) + binary search(10K) + struct build(5000))

**Current VM bottlenecks:** Recursive fib dominates (~60% of time). Boxing on every arithmetic op. Stack push/pop per recursive call.

**Improvements:**

- Tagged Value: ~2× on all arithmetic
- Register calling convention for fib recursion: ~1.5×
- `ADDI` for loop counters in sort/search: ~1.2×
- Inline cache on struct field access: ~1.1×

**Projection:** 1,361ms / (2 × 1.5 × 1.2 × 1.1) ≈ **344ms**. Under 500ms target. Fib(26) alone: ~150ms (vs CPython's ~80ms — CPython has decades of call optimization).

### Namespace Calls (200K iterations × 13 calls = 2.6M dispatches)

**Current VM bottleneck:** FrozenDictionary lookup per call + argument list allocation.

**Improvements:**

- Static dispatch (`CALLBUILTIN`): eliminates dictionary lookup → ~5×
- Direct register access (no `List<object?>` allocation): ~2×

**Projection:** 1,937ms / (5 × 2) ≈ **194ms**. Right at the 200ms target. The remaining cost is the actual BCL calls (`Math.Sqrt`, `string.ToUpper`, etc.) — irreducible.

### Lexer Heavy

**Current VM bottlenecks:** String allocation dominates (building identifier strings, string comparisons).

**Improvements:**

- String interning: ~1.5× on string comparisons
- Tagged Value: ~1.5× on mixed numeric/string workloads

**Projection:** 1,800ms / (1.5 × 1.5) ≈ **800ms**. Still above 500ms. Lexer-heavy workloads are string-allocation-dominated — further gains need string slicing (view into source without allocation) or a more fundamental string representation change.

---

## 11. What Changes vs. the Current VM

| Aspect                   | Current VM (v1)                        | Proposed VM (v2)                                 | Why Change                                              |
| ------------------------ | -------------------------------------- | ------------------------------------------------ | ------------------------------------------------------- |
| **Execution model**      | Stack-based                            | Register-based                                   | 25-47% fewer instructions                               |
| **Value type**           | `object?` (boxed)                      | `Value` struct (16-byte tagged union)            | Zero allocation for primitive arithmetic                |
| **Instruction encoding** | Variable-length (1-3 bytes)            | Fixed 32-bit                                     | No decode cost, direct IP indexing                      |
| **Instruction count**    | 78 opcodes                             | ~50 opcodes                                      | Registers absorb load/store; tighter dispatch           |
| **Built-in dispatch**    | `FrozenDictionary<string, delegate>`   | `BuiltInFn[]` indexed by compile-time ID         | O(1) array index vs. hash lookup                        |
| **Built-in arguments**   | `List<object?>` allocated per call     | `Value[]` register slice (zero allocation)       | Eliminates 2.6M list allocations in namespace benchmark |
| **Loop compilation**     | LOAD + CONST + ADD + STORE (4 ops)     | `FORLOOP` or `ADDI` (1 op)                       | 4× fewer dispatches per iteration                       |
| **Comparisons**          | Produce `bool` value, then `JUMPFALSE` | Conditional skip of next `JMP`                   | Half the instructions for conditions                    |
| **Field access**         | Dictionary lookup every time           | Inline cache (type check + array index)          | ~10× faster for monomorphic sites                       |
| **Debug mode**           | `if (debugger != null)` in hot loop    | Dual compilation + `DEBUGBREAK` opcode           | True zero overhead in release                           |
| **Closures**             | All upvalue-based                      | Flat capture for read-only + upvalue for mutable | Fewer indirections for common case                      |
| **Register allocation**  | N/A (stack-based)                      | Two-pass: live range analysis + linear scan      | Named operands, register reuse                          |
| **String handling**      | Standard .NET strings                  | Interned string table                            | Reference equality for comparisons                      |
| **Call frame registers** | `new object?[N]` per frame             | Windowed into global `Value[65536]`              | Zero allocation per function call                       |

### What Stays the Same

- **AST node types**: No changes to the parser output
- **Resolver**: Enhanced but same fundamental design (scope distance + slot)
- **Language semantics**: Identical behavior — this is a runtime optimization, not a language change
- **Upvalue close semantics**: Same promotion-on-scope-exit model
- **Error handling**: Same try/catch/throw model with exception handler chain
- **DAP protocol surface**: Same breakpoints, stepping, watches, variable inspection
- **Built-in function implementations**: Same logic, different calling convention wrapper
- **Shell command execution**: Same `Process.Start()` under the hood

---

## 12. Risks & Open Questions

### Risk: .NET Value Type Limitations

The `Value` struct with heap indirection through `Heap.Get(index)` adds one array lookup for every string/array/dict access. If a workload is dominated by string manipulation (not arithmetic), this indirection could **slow things down** compared to direct `object?` references.

**Mitigation:** Benchmark both representations on the string-heavy lexer benchmark before committing. If the indirection cost exceeds the boxing savings for string workloads, consider a hybrid: use `Value` for the operand stack but unwrap to `object?` when calling built-in functions that are string-heavy.

**Alternative considered:** Store `object?` reference directly in the Value struct alongside the tag. This makes `Value` a managed type (the GC needs to scan it), but eliminates the indirection. The cost: the `Value[]` register file becomes GC-scannable, which may affect GC pause times for large register files. Worth benchmarking.

### Risk: Two-Pass Compilation Speed

Adding a register allocation pass increases compilation time. For interactive use (REPL, short scripts), compilation overhead must stay under 10ms.

**Mitigation:** The register allocator is a linear scan, not graph coloring. Linear scan on AST-derived live ranges is O(N) in the number of locals. Even a 1000-local function allocates in microseconds.

### Risk: Dual Bytecode Memory

Storing both release and debug bytecode doubles the bytecode memory. For a typical script with 10K instructions (40KB release), that's 80KB total. Negligible. For huge codebases (100K+ instructions), it could matter.

**Mitigation:** Lazy generation — only compile `DebugCode` when a debugger attaches. The `Prototype` stores `DebugCode = null` until needed.

### Risk: Inline Cache Invalidation

If Stash ever supports modifying struct definitions at runtime (e.g., adding fields), inline caches become stale. Currently Stash doesn't support this, but it's worth noting.

**Mitigation:** Type IDs are monotonic. If struct metadata changes, its type ID changes. Stale caches auto-invalidate on the next type check miss.

### Open Question: Integer Division Semantics

Current Stash: `int / int → int` (truncating). Some languages produce float. The VM must match current behavior exactly. Confirm with language spec.

> **Answer from spec:** Section 3 shows `count /= 4` as integer operation, and the type system section says "When an `int` and a `float` are used in arithmetic, the `int` is promoted to `float`." So `int / int → int` (truncating), `int / float → float`. Confirmed.

### Open Question: Stack Overflow Detection

With a 65K register window, deeply recursive functions (fib(35) creates ~70K frames naively) could overflow. Need a configurable max call depth with a clear runtime error. The current VM uses 256-slot initial frames array that resizes — the register window approach needs a hard limit or dynamic growth.

**Decision:** Start with a 1M register window (16MB) with a configurable `MaxCallDepth` (default 10000). At an average of 10 registers per function, that's 100K registers for 10K frames. Generous. Fib(35) needs ~35 frames deep × 6 registers = 210 registers at peak — trivial.

### Open Question: `FORLOOP` vs. C-style For

Stash has two loop styles:

1. `for (let i = 0; i < n; i++)` — C-style
2. `for (let x in collection)` — iterator-style

`FORLOOP` optimizes case 1 when the compiler can prove:

- Init is `let i = <integer>`
- Condition is `i < <expr>` (simple comparison)
- Update is `i++` or `i += <integer>`

If the loop doesn't match this pattern (e.g., `for (let i = 0; someCondition(i); weirdUpdate(i))`), it falls back to the general `JMP`/`JUMPFALSE` pattern. The compiler does pattern matching on the `ForStmt` AST node to decide.

---

## Appendix A: Implementation Phases (if this were built)

| Phase  | Focus                                            | Deliverables                                                            | Est. Effort |
| ------ | ------------------------------------------------ | ----------------------------------------------------------------------- | ----------- |
| **0**  | Value representation                             | `Value` struct, `Heap`, benchmarks comparing boxing vs. tagged union    | Small       |
| **1**  | Instruction set & Prototype                      | Opcodes, encoding, `Prototype`, disassembler                            | Medium      |
| **2**  | Compiler (pass 1: resolution)                    | Enhanced Resolver with capture analysis                                 | Small       |
| **3**  | Compiler (pass 2: register allocation + codegen) | `Compiler` producing `Prototype` from AST                               | Large       |
| **4**  | VM core (RunRelease)                             | Arithmetic, logic, control flow, function calls                         | Large       |
| **5**  | Object system                                    | Structs, enums, dicts, arrays, inline caches                            | Medium      |
| **6**  | Built-in integration                             | `CALLBUILTIN`, built-in function table, built-in rewrites               | Medium      |
| **7**  | Closures & upvalues                              | Flat capture, mutable upvalues, close-on-scope-exit                     | Medium      |
| **8**  | Error handling & commands                        | Try/catch/throw, shell commands, pipes, redirects                       | Medium      |
| **9**  | Debug mode (RunDebug)                            | Dual compilation, DEBUGBREAK, IDebugScope from registers, attach/detach | Large       |
| **10** | Integration                                      | StashEngine wiring, CLI flag, REPL, Playground                          | Medium      |

---

## Appendix B: What Would Push This Into JIT Territory

The proposed interpreter VM targets **3-10× over the current VM**, putting it at roughly CPython parity or slightly better for most workloads. To get another 10-100× (LuaJIT/V8 territory), you'd need:

1. **Tracing JIT**: Record hot loops, compile traces to native code. The register-based bytecode is an excellent IR for a tracing JIT — the register allocation is already done.
2. **Type specialization**: Record observed types at each instruction site, compile specialized machine code (e.g., "this ADD always sees two ints → emit native `add rax, rbx`").
3. **Inline caching → polymorphic inline caching → JIT-compiled dispatch**: The inline cache infrastructure in Section 4 is the entry point for a JIT.

The proposed design **does not preclude** any of these. The fixed 32-bit instruction encoding, register-based layout, and inline cache sites are all JIT-friendly. If Stash ever needs V8-class performance, the VM v2 architecture provides the right foundation.

---

> **Revision history:**
>
> - 2026-04-05: Initial draft — full clean-sheet design
