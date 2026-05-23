# Interpreter Performance — Tree-Walk Optimization and Bytecode VM Analysis

**Status:** Backlog — Analysis & Design
**Created:** 2026-04-04
**Purpose:** Analyze Stash's performance gap against Python/Ruby/Perl, identify all remaining tree-walk optimizations, and evaluate whether a bytecode VM is warranted.

---

## 1. Problem Statement

Benchmark results (median of 3 runs, identical workloads, identical checksums):

| Benchmark             |   Stash | Python | Node.js |  Ruby |  Perl |     Bash |
| --------------------- | ------: | -----: | ------: | ----: | ----: | -------: |
| Algorithms            | 2,031ms |   86ms |     5ms |  56ms | 209ms | 10,522ms |
| Function Calls        | 2,043ms |   77ms |     3ms |  17ms | 101ms |  3,714ms |
| Expression Throughput | 1,383ms |  190ms |    17ms | 188ms | 132ms |  4,944ms |
| Built-in Functions    |   902ms |  301ms |    27ms | 343ms | 341ms | 23,760ms |
| Scope Lookup          | 1,505ms |  106ms |     7ms | 131ms | 279ms |  3,273ms |

**Stash is 3–120× slower than Ruby and 10–680× slower than Node.js.** It solidly beats Bash (2–26×), but lags badly against every bytecode/JIT language.

Node.js (V8 JIT) is a different class — unfair comparison. But **Python 3 (CPython) and Ruby 3 (YARV)** are squarely in the same "scripting language for sysadmin" category, and they're 3–25× faster. Both use **bytecode VMs** compiled from ASTs — the approach Stash currently skips.

The question: Is there enough headroom in the tree-walk model to close the gap, or is bytecode compilation now a prerequisite for credibility?

---

## 2. Current Architecture — Identified Bottlenecks

### 2.1 Execution Model

Stash uses a classic tree-walk interpreter with the Visitor pattern:

```
Source → Lexer → Parser → AST → Resolver → Interpreter (tree-walk via IExprVisitor/IStmtVisitor)
```

The Resolver pre-computes `(distance, slot)` pairs for local variable accesses. These are stored in a `ConcurrentDictionary<Expr, (int Distance, int Slot)>` keyed by AST node reference identity.

### 2.2 Bottleneck Analysis (Ordered by Estimated Impact)

#### B1: Virtual Dispatch per AST Node (HIGH IMPACT)

Every expression and statement evaluation goes through:

```csharp
object? result = expr.Accept(this);  // vtable lookup + indirect call
```

For `a + b + c`, this is at minimum 5 virtual calls: BinaryExpr(outer).Accept → BinaryExpr(inner).Accept → IdentifierExpr(a).Accept → IdentifierExpr(b).Accept → IdentifierExpr(c).Accept. Each crosses a vtable boundary, defeats branch prediction, and may cause an instruction cache miss.

**Why this matters:** In a tight loop doing `sum = sum + i; i = i + 1;`, that's ~12 virtual dispatches per iteration. At 3M iterations (bench_numeric), that's 36M vtable lookups. Modern CPUs with branch prediction can partially mitigate this, but tree-walk interpreters fundamentally scatter the instruction pointer across dozens of method bodies.

**Bytecode advantage:** A bytecode `switch` loop keeps the instruction pointer in one tight function. CPUs predict the dispatch table well (common instruction pairs have stable patterns). Computed gotos (C) give separate branch points per opcode.

#### B2: `ConcurrentDictionary` Lookup per Variable Access (HIGH IMPACT)

Every resolved variable access does:

```csharp
if (_locals.TryGetValue(expr, out var resolved))  // hash table lookup
    return _environment.GetAtSlot(resolved.Distance, resolved.Slot);
```

`ConcurrentDictionary.TryGetValue` is thread-safe (read-lock-free but still involves memory barriers and hash computation). This is called **per identifier reference** — in a loop body with 6 variables, that's 6 hash lookups per iteration.

**Potential fix (tree-walk):** Store `(Distance, Slot)` directly on the `Expr` node as mutable fields. The Resolver writes them once; the Interpreter reads them with a simple null/sentinel check. Zero hash overhead. This is how most production tree-walk interpreters work (including Crafting Interpreters' jlox, which uses a `HashMap<Expr, Integer>` but could trivially inline it).

**Estimated speedup:** 10–20% overall. More on variable-heavy benchmarks (Scope Lookup, Function Calls).

#### B3: Environment Allocation per Function Call (MEDIUM-HIGH IMPACT)

Every user-defined function call allocates:

```csharp
var env = new Environment(Closure);  // 2 arrays: object?[4] + string[4]
```

Plus `List<object?>` for arguments (unless zero-arg, which reuses `_emptyArgs`). In the Function Calls benchmark (600K calls), that's 600K `Environment` objects and 600K `List<object?>` allocations per run. The GC pressure from these short-lived objects is significant.

**Potential fixes (tree-walk):**

- **Object pool** for `Environment` instances — return to pool on scope exit, reuse on next call
- **Pre-sized slot arrays** — the Resolver knows exactly how many locals a function has; pass that count to the Environment constructor to avoid resizing
- **ArrayPool\<object?\>** for slot arrays — rent/return from the shared pool instead of allocating
- **Arguments on a value stack** — avoid `List<object?>` allocation entirely by pushing args onto a shared stack

**Estimated speedup:** 15–30% on function-call-heavy code.

#### B4: Boxing of Numeric Values (MEDIUM IMPACT)

All values are `object?`. Every arithmetic operation on `long` or `double` values boxes and unboxes:

```csharp
object? left = expr.Left.Accept(this);   // returns boxed long
object? right = expr.Right.Accept(this); // returns boxed long
if (left is long li && right is long ri)
    return li + ri;                       // unbox, add, rebox
```

The existing `TryEvalLong` fast path mitigates this for **nested** arithmetic (pure long literal trees), but it doesn't help for the common case of `variable + variable` or `variable + literal`.

**Potential fixes (tree-walk):**

- **NaN-tagging/tagged union** — Pack values into a 8-byte `struct StashValue` with a type tag. Eliminates boxing for numbers, bools, null. This is what Wren, Lua, and high-performance JS engines do.
  - Trade-off: Pervasive change — every `object?` becomes `StashValue`. All visitors, built-in functions, and collections must be updated.
  - Bonus: Much better cache locality — values are inline instead of heap-allocated.
- **Dual return paths** — Less invasive: add `long VisitBinaryExprLong(BinaryExpr)` specializations that the interpreter calls when it knows both operands are integers. Avoids boxing on the hot arithmetic path.

**Estimated speedup:** 20–40% on arithmetic-heavy code with tagged unions. 5–10% with dual paths.

#### B5: Argument List Allocation (MEDIUM IMPACT)

Every function call with arguments allocates a `new List<object?>(expr.Arguments.Count)`:

```csharp
arguments = new List<object?>(expr.Arguments.Count);
foreach (Expr argument in expr.Arguments)
    arguments.Add(argument.Accept(this));
```

For built-in function calls (200K iterations × 13 calls per iteration = 2.6M calls), that's 2.6M list allocations and GC collections.

**Potential fixes (tree-walk):**

- **Stackalloc + span** for small arg counts (≤4 args covers ~95% of calls)
- **Reusable argument buffer** — thread-local `List<object?>` that gets `.Clear()`ed between calls
- **ObjectPool\<List\<object?\>\>** from `Microsoft.Extensions.ObjectPool`

**Estimated speedup:** 5–15% on built-in-heavy benchmarks.

#### B6: Per-Statement Overhead in `Execute()` (LOW-MEDIUM IMPACT)

Every statement pays:

```csharp
if (Ctx.CancellationToken.IsCancellationRequested) ...  // volatile read
if (_stepLimit > 0 && ++Ctx.StepCount > _stepLimit) ... // branch + increment
Ctx.CurrentSpan = stmt.Span;                            // field write
_debugger?.OnBeforeExecute(...);                         // null check
stmt.Accept(this);                                       // vtable dispatch
```

The cancellation token check is a volatile read (memory barrier). The debugger null check is cheap but contributes to the total per-statement cost.

**Potential fixes (tree-walk):**

- **Amortized cancellation checking** — Check every N statements instead of every statement (N=64 or N=256). Use a simple counter.
- **Eliminate step limit branch in production** — If `_stepLimit == 0` (the common case), remove the branch entirely via a specialized `Execute` path or a delegate swap.
- **Remove `Ctx.CurrentSpan` write** — only needed for error reporting. Set it at the point where an error is thrown instead.

**Estimated speedup:** 3–8% overall (compounding across millions of statements).

#### B7: `Ancestor(int distance)` Chain Walking (LOW IMPACT)

For deep closures (scope depth 5), each variable access walks 5 linked-list hops:

```csharp
private Environment Ancestor(int distance) {
    Environment env = this;
    for (int i = 0; i < distance; i++)
        env = env.Enclosing!;
    return env;
}
```

This is O(depth) per access. In the Scope Lookup benchmark (5-deep, 100K iterations), that's 500K linked-list traversals.

**Potential fixes (tree-walk):**

- **Flat environment array** — Instead of a linked list, maintain a `Environment[]` array indexed by scope depth. Push/pop on function entry/exit. Variable access becomes `_envStack[distance]._slots[slot]` — O(1).
- **Scope flattening** — When the Resolver detects that a closure only captures from one outer scope, copy those values into the closure's own environment (like Python's `__closure__` cells).

**Estimated speedup:** 5–10% on closure-heavy code only.

---

## 3. Tree-Walk Optimization Strategy

### Phase 1: Low-Hanging Fruit (Estimated Total: 20–40% Improvement)

These changes are **low-risk, low-effort**, and don't change the fundamental architecture:

| #   | Optimization                                                                     | Impact | Risk | Effort |
| --- | -------------------------------------------------------------------------------- | ------ | ---- | ------ |
| 1a  | Inline resolver results on Expr nodes (eliminate `_locals` ConcurrentDictionary) | 10–20% | Low  | Low    |
| 1b  | Pre-size Environment slot arrays from Resolver knowledge                         | 5–10%  | Low  | Low    |
| 1c  | Reusable argument buffer (thread-local `List<object?>`)                          | 5–10%  | Low  | Low    |
| 1d  | Amortized cancellation token check (every 64 statements)                         | 2–5%   | Low  | Low    |
| 1e  | Lazy `Ctx.CurrentSpan` (set on error only)                                       | 1–3%   | Low  | Low    |

### Phase 2: Medium-Effort Structural Changes (Estimated Total: 15–30% Improvement)

These require more careful implementation and testing:

| #   | Optimization                                                  | Impact | Risk   | Effort |
| --- | ------------------------------------------------------------- | ------ | ------ | ------ |
| 2a  | Environment object pooling                                    | 10–15% | Medium | Medium |
| 2b  | Flat environment stack (replace linked list with array)       | 5–10%  | Medium | Medium |
| 2c  | Specialized arithmetic fast paths for IdentifierExpr operands | 5–10%  | Medium | Medium |

### Phase 3: Tagged Value Representation (Estimated Total: 20–40% Improvement)

This is a **major** change — every `object?` return type becomes a `StashValue` struct:

```csharp
[StructLayout(LayoutKind.Explicit)]
public readonly struct StashValue
{
    [FieldOffset(0)] public readonly double AsDouble;
    [FieldOffset(0)] public readonly long AsLong;
    [FieldOffset(0)] public readonly nint AsPointer; // for heap-allocated objects
    [FieldOffset(8)] public readonly StashValueType Type;
}
```

- Eliminates boxing for numbers, bools, null
- Values are 16 bytes inline (or 12 with careful layout)
- Pervasive change: all visitors return `StashValue`, all built-ins accept/return `StashValue`, all collections store `StashValue`
- **High risk, high reward** — this is essentially rebuilding the value layer

### Cumulative Tree-Walk Ceiling

With all three phases: **approximately 55–100% total improvement** (i.e., 1.5–2× faster). This would put Stash roughly in the:

- **Algorithms:** ~1,000–1,300ms range (still 10× slower than Python)
- **Function Calls:** ~1,000–1,400ms range (still 13× slower than Python)

**Verdict:** Tree-walk optimizations can make Stash ~2× faster, but they cannot close the fundamental 10–25× gap against bytecode interpreters. The ceiling is real.

---

## 4. Bytecode VM — Architecture Proposal

### 4.1 Why Bytecode Wins

The performance gap comes from three fundamental advantages bytecode VMs have over tree-walkers:

1. **Data locality:** A flat `byte[]` instruction stream fits in L1 cache. AST nodes are scattered across the heap with pointer indirection.
2. **Dispatch cost:** One `switch` on an opcode byte vs. vtable dispatch through an interface. The `switch` is 1–2 cycles; the vtable is 5–15 cycles with cache miss risk.
3. **No per-node allocation overhead:** Intermediate results live on a value stack (a flat array), not as boxed heap objects returned through the call chain.

CPython achieves its performance with a relatively simple stack-based bytecode VM. Ruby's YARV is similar. Both maintain full debugging capabilities through source maps (bytecode offset → source location mappings).

### 4.2 Proposed Architecture

```
Source → Lexer → Parser → AST → Analysis/LSP (unchanged)
                            ↓
                        Compiler → Bytecode + SourceMap
                            ↓
                          VM (stack-based bytecode interpreter)
```

**Key insight:** The AST is preserved. LSP, static analysis, and the formatter continue to work on AST nodes exactly as they do today. The bytecode compiler is a **new consumer** of the AST, parallel to the existing visitors — not a replacement.

### 4.3 Bytecode Design Sketch

```
Instruction format: 1-byte opcode + 0-3 byte operands

Stack-based: operands and results live on a value stack (StashValue[])

Example bytecodes:
  OP_CONST       <idx:u16>     — push constant from pool
  OP_LOAD_LOCAL  <slot:u8>     — push local variable
  OP_STORE_LOCAL <slot:u8>     — pop and store to local
  OP_LOAD_UPVAL  <idx:u8>      — push captured upvalue
  OP_STORE_UPVAL <idx:u8>      — pop and store to upvalue
  OP_ADD                       — pop 2, push sum
  OP_SUB                       — pop 2, push difference
  OP_MUL / OP_DIV / OP_MOD    — arithmetic
  OP_NEGATE                    — pop 1, push negation
  OP_NOT                       — pop 1, push logical not
  OP_EQ / OP_LT / OP_GT       — comparison
  OP_JUMP        <offset:i16>  — unconditional jump
  OP_JUMP_FALSE  <offset:i16>  — conditional jump (pop condition)
  OP_CALL        <argc:u8>     — call function with argc args on stack
  OP_RETURN                    — return top of stack
  OP_GET_FIELD   <name:u16>    — struct/dict field access
  OP_SET_FIELD   <name:u16>    — struct/dict field set
  OP_GET_NS      <ns:u8><fn:u8> — namespace function lookup (pre-resolved)
  OP_ARRAY       <count:u16>   — build array from stack
  OP_DICT        <count:u16>   — build dict from stack pairs
  OP_POP                       — discard top of stack
  OP_DUP                       — duplicate top of stack
  OP_CLOSURE     <fn:u16><upvalues...> — create closure with upvalue descriptors
```

### 4.4 Compilation Unit

Each script/function compiles to a `CompiledFunction`:

```csharp
public class CompiledFunction
{
    public byte[] Code;              // bytecode
    public StashValue[] Constants;    // constant pool
    public SourceMap SourceMap;       // bytecode offset → SourceSpan
    public int Arity;
    public int LocalCount;           // pre-computed by compiler
    public UpvalueDescriptor[] Upvalues;
    public string? Name;
}
```

### 4.5 Value Stack

```csharp
public struct StashValue  // 16 bytes, no heap allocation for primitives
{
    public StashValueType Type;  // 1 byte: Null, Bool, Long, Double, Object
    private long _bits;          // raw bits: long value, double bits, or GC handle

    // Fast constructors
    public static StashValue FromLong(long v) => new() { Type = StashValueType.Long, _bits = v };
    public static StashValue FromDouble(double v) => ...;
    public static StashValue FromBool(bool v) => ...;
    public static readonly StashValue Null = new() { Type = StashValueType.Null };
    public static StashValue FromObject(object obj) => ...; // GCHandle or reference

    // Fast accessors
    public long AsLong => _bits;
    public double AsDouble => Unsafe.As<long, double>(ref _bits);
}
```

The VM maintains a flat `StashValue[]` stack:

```csharp
public class VirtualMachine
{
    private StashValue[] _stack = new StashValue[256];  // grows as needed
    private int _sp;  // stack pointer

    // Hot loop
    public StashValue Execute(CompiledFunction fn)
    {
        byte[] code = fn.Code;
        int ip = 0;
        int baseSlot = _sp;  // frame base for local access

        while (true)
        {
            byte op = code[ip++];
            switch (op)
            {
                case OP_ADD:
                    ref var b = ref _stack[--_sp];
                    ref var a = ref _stack[_sp - 1];
                    if (a.Type == StashValueType.Long && b.Type == StashValueType.Long)
                        a = StashValue.FromLong(a.AsLong + b.AsLong);
                    else
                        a = AddSlow(a, b);  // handles double, string, duration, etc.
                    break;

                case OP_LOAD_LOCAL:
                    _stack[_sp++] = _stack[baseSlot + code[ip++]];
                    break;

                // ... ~40 more opcodes
            }
        }
    }
}
```

### 4.6 Source Maps for Debugging

```csharp
public class SourceMap
{
    // Sorted by bytecode offset for binary search
    private readonly (int BytecodeOffset, SourceSpan Span)[] _entries;

    public SourceSpan? GetSpan(int bytecodeOffset)
    {
        // Binary search for the entry covering this offset
        ...
    }
}
```

The compiler emits source map entries at each statement boundary. The DAP debugger hooks remain structurally identical:

```csharp
// Before (tree-walk):
_debugger?.OnBeforeExecute(stmt.Span, Ctx.Environment, DebugThreadId);

// After (bytecode VM):
if (_debugger != null && sourceMap.HasEntry(ip))
    _debugger.OnBeforeExecute(sourceMap.GetSpan(ip), currentFrame.Locals, threadId);
```

Breakpoints match by `SourceSpan.StartLine` — unchanged from today.

### 4.7 LSP/Analysis Impact

**Zero.** The LSP and Analysis engine work on the AST, which is preserved:

```
Source → Lexer → Parser → AST ──→ Analysis Engine → LSP handlers (unchanged)
                            │
                            └──→ Bytecode Compiler → VM (new execution path)
```

The `AnalysisEngine` caches `(tokens, statements, symbolTable, diagnostics)` — none of this changes. The bytecode compiler is invoked only when executing code, not when analyzing it.

### 4.8 DAP Impact

The DAP hooks change from per-statement AST callbacks to per-instruction bytecode callbacks, but the **IDebugger interface stays the same**:

| Capability          | Tree-Walk (current)         | Bytecode VM                                      |
| ------------------- | --------------------------- | ------------------------------------------------ |
| Breakpoints         | Match `stmt.Span.StartLine` | Match `sourceMap.GetSpan(ip).StartLine`          |
| Step Over           | Step one statement          | Step to next source line change                  |
| Step Into           | Call.Accept → function body | OP_CALL → function's first instruction           |
| Variable Inspection | Walk `Environment` chain    | Read from `CallFrame.Locals[]` (actually easier) |
| Call Stack          | `_callStack` list           | `_frames[]` array                                |
| Watch Expressions   | Interpret expression AST    | Compile mini-expression → execute                |

**Watch expressions** are the one nuance — the tree-walk interpreter can evaluate arbitrary expressions by walking their AST. A bytecode VM would need a mini-compiler for watch expressions. This is standard practice (Python's `eval()`, Ruby's `binding.eval`).

---

## 5. Recommendation

### Short-Term: Tree-Walk Phase 1 Optimizations

Implement Phase 1 optimizations (Section 3) immediately. They're low-risk, low-effort, and provide a measurable 20–40% improvement that compounds:

1. Inline `(Distance, Slot)` on `Expr` nodes
2. Pre-size Environment from Resolver
3. Reusable argument buffer
4. Amortized cancellation checks
5. Lazy CurrentSpan

This is valuable regardless of whether a bytecode VM follows — it demonstrates the optimization methodology and builds profiling infrastructure.

### Medium-Term: Bytecode VM

A bytecode VM is the right move. The evidence is overwhelming:

- **Every language Stash competes with uses bytecode.** CPython, Ruby YARV, Perl (op-tree), Lua, Wren — all moved from tree-walk to bytecode. None moved back.
- **The performance ceiling is real.** Even with aggressive tree-walk optimization, Stash will remain ~10× slower than Python. That's an embarrassing gap for a language positioning itself as a modern Bash replacement.
- **The architecture supports it cleanly.** AST is preserved for LSP/Analysis. Source maps maintain debugging. The IDebugger interface is VM-agnostic.
- **It's been done before** in .NET. IronPython, IronRuby, and DynamicLanguage Runtime all demonstrate that bytecode VMs work well on .NET with full debugging support.

### Implementation Order

```
1. Tree-Walk Phase 1 (quick wins)
2. StashValue tagged union (prerequisite for both tree-walk Phase 3 AND bytecode VM)
3. Bytecode compiler (AST → bytecod)
4. Stack-based VM
5. DAP integration with source maps
6. Deprecate tree-walk interpreter (or keep as reference/debug mode)
```

Step 6 is optional — keeping the tree-walk interpreter as a "debug mode" or "reference implementation" has value. Some languages (Lua) maintain both.

---

## 6. Risks and Open Questions

| Risk                                     | Mitigation                                                                                                                                                  |
| ---------------------------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Bytecode VM is a 3–6 month effort        | Phase 1 tree-walk optimizations deliver value immediately while VM is developed                                                                             |
| Feature parity with tree-walk            | Maintain a comprehensive test suite (4,400+ tests already exist). Run both interpreters against the same tests during development.                          |
| Watch expressions in debugger            | Implement an expression mini-compiler early. It's a small subset of the full compiler.                                                                      |
| Closures/upvalues are tricky in bytecode | Well-studied problem — follow Lua's upvalue design (open/closed upvalues on the stack). Crafting Interpreters Chapter 25 covers this in detail.             |
| `$( )` command execution                 | Shell commands can remain as a special opcode (`OP_EXEC_CMD`) that calls into the same .NET `Process.Start` infrastructure. No architectural change needed. |
| Async/await and task.run                 | Tasks already snapshot the Environment. With bytecode, they'd snapshot the `CallFrame` + value stack slice. Structurally similar.                           |
| WASM Playground                          | Bytecode VM in C# runs fine under Blazor WASM — may actually be faster than tree-walk there too.                                                            |
| StashEngine embedding API                | `engine.Run("code")` → now compiles then executes. Same external API, different internal path.                                                              |

### Open Questions

1. **Should the VM be register-based or stack-based?** Stack-based is simpler to implement and good enough (CPython, Ruby YARV, Lua 4 are all stack-based). Register-based (Lua 5, Dalvik) can be faster but significantly more complex to compile for. **Recommendation: stack-based.**

2. **Should we implement NaN-tagging?** On .NET, NaN-tagging is less natural than on C (no raw pointer manipulation without `unsafe`). A discriminated struct (`StashValue` with Type tag + union bits) is the pragmatic .NET choice. **Recommendation: tagged struct, not NaN-tagging.**

3. **Should the VM be written in C# or drop down to C for the hot loop?** C# with `Unsafe` and careful struct layout can get within ~80% of C performance. The maintainability benefit of staying in C# is enormous — same language, same tooling, same team. **Recommendation: C#, with `[MethodImpl(AggressiveOptimization)]` on the hot loop.**

4. **What about .NET's `System.Reflection.Emit` / DynamicMethod?** We could JIT-compile hot functions to IL at runtime. This is what IronPython does. It's powerful but complex, and it breaks Native AOT (the CLI uses AOT). **Recommendation: Not for Phase 1. Consider as a Phase 2 JIT layer if bytecode VM performance is still insufficient.**

---

## 7. Decision Log

| Date       | Decision                                              | Rationale                                                                                                                                                                            |
| ---------- | ----------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| 2026-04-04 | Created this analysis document                        | Benchmark results showed 10–25× gap vs. Python/Ruby. Need to evaluate whether tree-walk optimization is sufficient or bytecode is required.                                          |
| 2026-04-04 | Recommended stack-based bytecode VM                   | Every comparable language uses bytecode. The architecture cleanly supports it (AST preserved for LSP/Analysis). The tree-walk performance ceiling (~2× improvement) is insufficient. |
| 2026-04-04 | Recommended keeping tree-walk as reference/debug mode | Low cost to maintain alongside bytecode VM. Useful for debugging the compiler itself and as a correctness oracle.                                                                    |
