---
description: "Use when: modifying the bytecode compiler, VM execution engine, opcode definitions, register allocation, instruction encoding/decoding, serialization (.stashc format), inline caching, closures/upvalues, or any file in Stash.Bytecode/. Covers the full compilation pipeline and runtime architecture."
applyTo: "Stash.Bytecode/**"
---

# Bytecode VM Guidelines

The Stash bytecode system compiles AST → fixed-size 32-bit instructions → register-based execution. The pipeline is: Lexer → Parser → SemanticResolver → Compiler → Chunk → VirtualMachine.

## Project Layout

```
Stash.Bytecode/
├── StashEngine.cs                    # High-level embedding API (Execute, Call, SetGlobal)
├── StashCompilationPipeline.cs       # Lex→Parse→Resolve→Compile static helpers
├── StashTypeConverter.cs             # CLR ↔ Stash type bridge
│
├── Bytecode/                         # Instruction definitions & metadata
│   ├── OpCode.cs                     # 120+ opcodes in 14 categories
│   ├── Instruction.cs                # 32-bit encode/decode (ABC, ABx, AsBx, Ax formats)
│   ├── Chunk.cs                      # Immutable compiled function prototype
│   ├── ChunkBuilder.cs               # Incremental bytecode builder (constants, jumps)
│   ├── Metadata.cs                   # StructMetadata, EnumMetadata, ImportMetadata, etc.
│   ├── SourceMap.cs                  # Instruction index → source location mapping
│   ├── Disassembler.cs               # Human-readable bytecode dump
│   └── ICSlot.cs                     # Inline cache slot for GetFieldIC
│
├── Compilation/                      # AST → Bytecode compiler (partial class)
│   ├── Compiler.cs                   # Main visitor, entry points, register management
│   ├── Compiler.Expressions.cs       # Literals, binary/unary ops, calls, indexing
│   ├── Compiler.Statements.cs        # Declarations, assignments, blocks
│   ├── Compiler.Collections.cs       # Arrays, dicts, ranges, spread, destructuring
│   ├── Compiler.ComplexExprs.cs      # Closures, lambdas, ternary, short-circuit
│   ├── Compiler.ControlFlow.cs       # Loops, if-else, try-catch, elevate, retry
│   ├── Compiler.Declarations.cs      # Struct/enum/interface type declarations
│   ├── Compiler.Exceptions.cs        # Try-catch-finally, throw
│   ├── Compiler.Strings.cs           # String interpolation, templates
│   ├── Compiler.Helpers.cs           # Constant folding, utilities
│   ├── CompilerScope.cs              # Register allocation, local tracking, scoping
│   ├── GlobalSlotAllocator.cs        # Global variable slot assignment
│   └── CompileError.cs               # Compiler diagnostic errors
│
├── VM/                               # Bytecode execution engine (partial class)
│   ├── VirtualMachine.cs             # Core: stack, frames, globals, Execute()
│   ├── VirtualMachine.Dispatch.cs    # Main execution loop, switch dispatch
│   ├── VirtualMachine.Functions.cs   # Call/return, closure creation
│   ├── VirtualMachine.Variables.cs   # Global/upvalue access
│   ├── VirtualMachine.Arithmetic.cs  # Math operators
│   ├── VirtualMachine.TypeOps.cs     # Type checks, comparisons
│   ├── VirtualMachine.Collections.cs # Array/dict/table operations
│   ├── VirtualMachine.Strings.cs     # String interpolation at runtime
│   ├── VirtualMachine.ControlFlow.cs # Jumps, loops, iteration
│   ├── VirtualMachine.Modules.cs     # Import/module loading
│   ├── VirtualMachine.Async.cs       # Async execution
│   ├── VirtualMachine.Process.cs     # Shell commands, pipes
│   └── VirtualMachine.Debug.cs       # DAP debugger integration
│
├── Runtime/                          # Execution support structures
│   ├── CallFrame.cs                  # Stack frame (Chunk, IP, BaseSlot, Upvalues)
│   ├── VMFunction.cs                 # Compiled closure (Chunk + Upvalue[])
│   ├── VMBoundMethod.cs              # Bound method (receiver + function)
│   ├── Upvalue.cs                    # Captured variable (open → stack, closed → heap)
│   ├── UpvalueDescriptor.cs          # Capture metadata (index, isLocal)
│   ├── VMContext.cs                  # IInterpreterContext implementation for VM
│   ├── ExtensionRegistry.cs          # Extension method + UFCS registry
│   ├── RuntimeOps.cs                 # Shared runtime helper operations
│   ├── SpreadMarker.cs               # Sentinel for spread arguments
│   ├── StashIterator.cs              # Iterator protocol implementation
│   ├── SynchronizedTextWriter.cs     # Thread-safe output writer
│   ├── VMDebugScope.cs               # Debugger variable scope view
│   └── VMTemplateEvaluator.cs        # TPL template rendering bridge
│
└── Serialization/                    # Binary .stashc format
    ├── BytecodeWriter.cs             # Chunk → binary serialization
    └── BytecodeReader.cs             # Binary → Chunk deserialization
```

## Instruction Encoding (32-bit Fixed Width)

All instructions are `uint` values. Four encoding formats:

| Format | Layout                  | Usage                             |
| ------ | ----------------------- | --------------------------------- |
| ABC    | `[op:8][A:8][B:8][C:8]` | Most ops (registers as operands)  |
| ABx    | `[op:8][A:8][Bx:16]`    | Constant/global index (unsigned)  |
| AsBx   | `[op:8][A:8][sBx:16]`   | Jump offsets (signed, bias=32767) |
| Ax     | `[op:8][Ax:24]`         | Large immediate (TryEnd, etc.)    |

Encode/decode via static methods in `Instruction.cs` — all O(1) bitmask shifts. Use `Instruction.GetA()`, `GetB()`, `GetBx()`, `GetSBx()`, etc. Never decode manually with raw bit ops.

## Compiler Conventions

### Register Allocation

- Registers `0..N` are **params and locals** (assigned by `CompilerScope.DeclareLocal()`)
- Registers `N+1..` are **temporaries** (allocated by `AllocTemp()`, freed by `FreeTemp()`)
- `_destReg` tracks the target register for expression results — always compile "into" the destination
- Use `TryGetLocalReg()` to avoid unnecessary `Move` instructions when a local is already in the right register

### Adding a New Opcode

> **WARNING:** Adding opcodes enlarges the dispatch switch in `VirtualMachine.Dispatch.cs`. The loop is already near the AOT optimization threshold — exceeding it causes a 10–20% performance regression. Always explore reusing existing opcodes or compound instruction sequences first. See "Dispatch Loop Size Limit" below.

1. Add the variant to the `OpCode` enum in `Bytecode/OpCode.cs` (place it in the correct category section)
2. Choose the encoding format (ABC/ABx/AsBx/Ax) based on operand needs
3. Add emission in the Compiler partial class that handles the relevant AST node
4. Add the dispatch case in `VirtualMachine.Dispatch.cs` — implement the handler in the appropriate VM partial class
5. Add disassembly support in `Disassembler.cs`
6. Update `BytecodeWriter.cs` and `BytecodeReader.cs` if the opcode uses metadata in the constant pool
7. The OpCode table hash in serialization will automatically invalidate old `.stashc` files

### Constant Pool

Constants are stored in `ChunkBuilder` and deduplicated via `_constantMap`. Use `AddConstant(StashValue)` — never add duplicates manually. Metadata records (struct/enum/import/command) are also stored as constants and indexed by `Bx`.

### Jump Patching

```csharp
int jumpIndex = _builder.EmitJump(OpCode.JmpFalse, condReg);
// ... emit body ...
_builder.PatchJump(jumpIndex);  // patches to current code offset
```

Always use `EmitJump()` + `PatchJump()`. For backward jumps (loops), use `OpCode.Loop` with a negative `sBx` offset.

### Constant Folding

`Compiler.Helpers.cs` contains `TryEvaluateConstant()` which folds binary expressions on literals at compile time. If you add a new foldable operation, add it there.

## VM Conventions

### Stack Layout

```
_stack:  [ Frame0 regs | Frame1 regs | Frame2 regs | ... | free ]
           ^BaseSlot[0]  ^BaseSlot[1]  ^BaseSlot[2]
```

Register `R(i)` in the current frame = `_stack[frame.BaseSlot + i]`. The stack and frame arrays are rented from `ArrayPool` — never allocate new arrays for these.

### Frame IP Semantics

`CallFrame.IP` uses **post-increment** — it points to the **next** instruction after fetch. A jump sets `frame.IP = target`, not `target - 1`.

### Dispatch Loop Performance

**CRITICAL — Dispatch Loop Size Limit:** The main `switch` dispatch in `VirtualMachine.Dispatch.cs` is near the threshold where .NET's Native AOT compiler can no longer optimize it effectively. If the dispatch body grows much larger, AOT will fall back to unoptimized codegen, causing a **10–20% performance regression across the board**. Before adding new opcodes, consider:
- Can the behavior be implemented by **reusing existing opcodes** (e.g., a helper called from an existing handler)?
- Can it be expressed as a **compound sequence** of existing instructions rather than a new opcode?
- If a new opcode is truly necessary, keep its dispatch handler **minimal** — delegate to a method in the appropriate VM partial file rather than inlining logic.

Other dispatch conventions:
- The main loop is in `RunInner<TDebugMode>()` using generic specialization for zero-cost debug mode (`DebugOn`/`DebugOff` structs)
- Each opcode handler should be in the appropriate VM partial file (Arithmetic, Collections, etc.)
- Use `[MethodImpl(MethodImplOptions.AggressiveInlining)]` for small hot-path handlers
- Cancellation and step-limit checks happen every 256 loop iterations, not every instruction

### Closures & Upvalues

- `Closure` opcode creates a `VMFunction` with `Upvalue[]` captured from the enclosing scope
- Open upvalues reference the stack directly; `CloseUpval` copies the value to heap (`Upvalue.Close()`)
- Open upvalues are kept sorted by stack index in `_openUpvalues`

### Built-In Function Calls

Built-in namespaces (arr, dict, str, etc.) are registered as globals. The VM calls them via `IStashCallable.Call(VMContext, args)`. All argument extraction uses `Args.*` helpers from `Stash.Stdlib`.

## Serialization (.stashc)

Binary format with a 32-byte header including magic (`STBC`), format version, flags, compiler hash, and OpCode table hash. The OpCode hash auto-invalidates on any enum change. See `BytecodeWriter.cs` header comment for the full format specification.

When modifying serialization:

- Bump the format version if the binary layout changes
- Ensure `BytecodeReader` validates all size fields to prevent buffer overruns
- Keep read/write symmetry — every `Write*` must have a matching `Read*`

## Key Optimization Patterns

| Pattern           | Location                         | What it does                                             |
| ----------------- | -------------------------------- | -------------------------------------------------------- |
| Inline caching    | `ICSlot.cs`, `GetFieldIC` opcode | Monomorphic/megamorphic cache for field access           |
| Zero-cost debug   | `RunInner<TDebugMode>()`         | JIT eliminates debug checks when `TDebugMode = DebugOff` |
| Constant folding  | `Compiler.Helpers.cs`            | Evaluates literal expressions at compile time            |
| Integer for-loops | `ForPrepII`/`ForLoopII` opcodes  | Avoids boxing for integer iteration                      |
| AddI instruction  | `AddI` opcode                    | Signed immediate add without constant pool lookup        |
| ArrayPool stacks  | `VirtualMachine.cs`              | Rented arrays for stack/frames to reduce GC pressure     |

## Testing

Tests live in `Stash.Tests/`. Run bytecode-specific tests:

```bash
dotnet test --filter "FullyQualifiedName~BytecodeVmTests"
dotnet test --filter "FullyQualifiedName~CompilerTests"
```

When adding a new opcode or modifying compilation, add tests that verify both the **compiled output** (instruction sequence) and the **runtime behavior** (execution result).
