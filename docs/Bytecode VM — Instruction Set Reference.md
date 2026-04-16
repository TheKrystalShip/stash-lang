# Bytecode VM — Instruction Set Reference

## Table of Contents

1. [Architecture Overview](#1-architecture-overview)
2. [Instruction Encoding](#2-instruction-encoding)
3. [Register Model](#3-register-model)
4. [Notation Conventions](#4-notation-conventions)
5. [Instruction Reference](#5-instruction-reference)
   - 5.1 [Loads & Data Movement](#51-loads--data-movement)
   - 5.2 [Global Variables](#52-global-variables)
   - 5.3 [Upvalues (Closure Captures)](#53-upvalues-closure-captures)
   - 5.4 [Arithmetic](#54-arithmetic)
   - 5.5 [Bitwise Operations](#55-bitwise-operations)
   - 5.6 [Comparison](#56-comparison)
   - 5.7 [Logic & Truthiness](#57-logic--truthiness)
   - 5.8 [Control Flow](#58-control-flow)
   - 5.9 [Numeric Iteration](#59-numeric-iteration)
   - 5.10 [Generic Iteration](#510-generic-iteration)
   - 5.11 [Collections & Indexing](#511-collections--indexing)
   - 5.12 [Field Access](#512-field-access)
   - 5.13 [Functions & Closures](#513-functions--closures)
   - 5.14 [Type System](#514-type-system)
   - 5.15 [Error Handling](#515-error-handling)
   - 5.16 [Strings](#516-strings)
   - 5.17 [Shell & Process](#517-shell--process)
   - 5.18 [Modules](#518-modules)
   - 5.19 [Concurrency](#519-concurrency)
   - 5.20 [Miscellaneous](#520-miscellaneous)
6. [Inline Caching](#6-inline-caching)
7. [Constant Pool](#7-constant-pool)
8. [Disassembly Format](#8-disassembly-format)

---

## 1. Architecture Overview

The Stash bytecode VM is a **register-based virtual machine** that executes fixed-width 32-bit instructions. It mirrors the fundamental architecture of a physical CPU:

| Physical CPU    | Stash VM                                                            |
| --------------- | ------------------------------------------------------------------- |
| Instruction Set | 92 opcodes across 14 categories                                     |
| Registers       | Virtual registers `r0..rN` (windows into a flat value stack)        |
| Machine code    | 32-bit instruction words                                            |
| Program counter | `IP` (instruction pointer) per call frame                           |
| Call stack      | `CallFrame[]` array with base slot, IP, chunk, and upvalue pointers |
| RAM             | `StashValue[]` — flat stack array shared across all frames          |
| CPU cache       | Inline cache slots (`ICSlot`) for field/method lookups              |

The VM uses a **fetch-decode-execute** cycle: each iteration reads a 32-bit instruction word, extracts the opcode via bitmask, and dispatches to the appropriate handler through a switch statement. The dispatch loop uses generic specialization (`DebugOn`/`DebugOff`) to eliminate debug instrumentation at zero cost in release builds.

---

## 2. Instruction Encoding

Every instruction is a single **32-bit unsigned integer** in one of four formats:

### ABC — Three-Operand

```
 31      24 23     16 15      8 7       0
┌──────────┬─────────┬─────────┬─────────┐
│  opcode  │    A    │    B    │    C    │
│  (8 bit) │ (8 bit) │ (8 bit) │ (8 bit) │
└──────────┴─────────┴─────────┴─────────┘
```

Used by most instructions. A, B, C are unsigned 8-bit values (0–255), typically register indices or small immediates.

### ABx — Register + Unsigned 16-bit

```
 31      24 23     16 15                 0
┌──────────┬─────────┬───────────────────┐
│  opcode  │    A    │        Bx         │
│  (8 bit) │ (8 bit) │     (16 bit)      │
└──────────┴─────────┴───────────────────┘
```

Used when an instruction needs a register and a larger index (constant pool, global slot). Bx is unsigned (0–65535).

### AsBx — Register + Signed 16-bit

```
 31      24 23     16 15                 0
┌──────────┬─────────┬───────────────────┐
│  opcode  │    A    │       sBx         │
│  (8 bit) │ (8 bit) │  (16 bit signed)  │
└──────────┴─────────┴───────────────────┘
```

Used for jump offsets. sBx is bias-encoded: stored as `(offset + 32767)`, giving a range of −32767 to +32768. Decode: `sBx = raw_value − 32767`.

### Ax — 24-bit Payload

```
 31      24 23                           0
┌──────────┬─────────────────────────────┐
│  opcode  │            Ax               │
│  (8 bit) │         (24 bit)            │
└──────────┴─────────────────────────────┘
```

Rare. Used for instructions that need only a large immediate with no register operand (e.g., `try.end`, `elevate.end`).

---

## 3. Register Model

The VM uses a **register-window** architecture over a shared flat stack:

```
_stack:  [ Frame 0 registers | Frame 1 registers | Frame 2 registers | ... ]
           ^BaseSlot=0         ^BaseSlot=8          ^BaseSlot=14
```

- Each function call gets a contiguous **window** of registers starting at `BaseSlot`.
- `R(i)` in the current frame maps to `_stack[BaseSlot + i]`.
- Registers `0..N` hold **parameters and local variables** (assigned at compile time).
- Registers `N+1..` are **temporaries** for intermediate expression results.
- Each compiled function records its `MaxRegs` — the total register slots required.
- The stack grows dynamically (doubles capacity) when needed, using `ArrayPool<T>` to reduce GC pressure.

### Calling Convention

```
Caller frame:        [ ... | callee | arg0 | arg1 | arg2 | ... ]
                              ^R(A)   ^R(A+1) ^R(A+2)
                                 ↓
Callee frame:        [ callee | arg0 | arg1 | arg2 | locals... | temps... ]
                      ^BaseSlot  ^R(0)  ^R(1)  ^R(2)
```

The callee's `BaseSlot` is set to `caller.BaseSlot + A + 1` (one past the callee register). Arguments are already positioned in the correct slots. The return value is written back to `R(A)` in the **caller's** frame (overwriting the callee reference).

#### `call` — Standard Function Call (ABC)

```
Emission:    call R(A), 0, C
Layout:      R(A)=callee  R(A+1)=arg0  R(A+2)=arg1  ...  R(A+C)=argC-1
```

- **C** = argument count
- **A** = callee register (overwritten with return value after call)
- Callee's new frame: `BaseSlot = caller.BaseSlot + A + 1`
- Arguments occupy `R(0)..R(C-1)` in the callee's frame (they're already in position)
- Return: `return R(A)` in the callee writes the value back to `caller.R(A)` via `frame.BaseSlot - 1`

**Arity handling:**

- If `argc == chunk.Arity` and no rest param / async: fast path (no arg validation)
- If `argc < chunk.MinArity`: runtime error ("Expected at least N arguments")
- If `argc < chunk.Arity`: default parameter values are loaded for missing args
- If `chunk.HasRestParam`: excess arguments are collected into a rest array at `R(Arity-1)`

#### `call.builtin` — Built-In Namespace Call (ABC + companion)

```
Emission:    call.builtin R(A), R(B), C    + companion(icSlot)
Layout:      R(A)=result  R(B)=namespace  R(A+1)=arg0  ...  R(A+C)=argC-1
```

- **A** = destination register for the result
- **B** = register holding the namespace object (used as IC guard)
- **C** = argument count
- Arguments are in `R(A+1)..R(A+C)`
- The companion word holds the IC slot index

**Fast path (IC hit):** Guard check `R(B) == ic.Guard` succeeds → call cached `BuiltInFunction` delegate directly with a `ReadOnlySpan<StashValue>` view over the argument registers. No field lookup overhead.

**Slow path (IC miss):** Resolve the field name (from `K(ic.ConstantIndex)`) on the object in `R(B)`, call the resolved callable, then populate the IC if the receiver is a frozen namespace.

#### `call.spread` — Call with Spread Arguments

```
Emission:    call.spread R(A), B
```

- **A** = callee register
- **B** = total argument register count (including spread markers)
- Arguments at `R(A+1)..R(A+B)` may include `SpreadMarker` sentinel values
- The VM expands spread markers: arrays/iterables are flattened into the argument list
- After expansion, the call proceeds as a standard `call`

#### `self` — Method Binding (ABC)

```
Emission:    self R(A), R(B), K(C)
Effect:      R(A) = R(B).K(C)  (the method/callable)
             R(A+1) = R(B)      (the receiver, for subsequent call)
```

- **B** = receiver register
- **C** = constant index of the method name
- After `self`, a `call R(A), 0, N` follows, where `R(A+1)` is already the `self` receiver

#### `closure` — Create Closure (ABx + N companions)

```
Emission:    closure R(A), K(Bx)   + N companion words
```

- **Bx** = constant pool index of the sub-chunk (`Chunk` object)
- **N** = `Constants[Bx].Upvalues.Length`
- Each companion word encodes one upvalue capture (see Companion Words section)
- Creates a `VMFunction` with the captured upvalues attached

---

## 4. Notation Conventions

Throughout this document:

| Notation | Meaning                                                    |
| -------- | ---------------------------------------------------------- |
| `R(A)`   | Register A in the current frame (`_stack[BaseSlot + A]`)   |
| `K(i)`   | Constant pool entry at index `i`                           |
| `G(i)`   | Global slot at index `i`                                   |
| `UV(i)`  | Upvalue at index `i` in the current closure                |
| `IP`     | Instruction pointer (post-increment: points to next instr) |
| `sBx`    | Signed 16-bit bias-encoded offset                          |
| `→`      | "produces" / "is assigned"                                 |
| `[ic:N]` | Inline cache slot index N                                  |

**Type shorthand:** `int` = 64-bit integer, `float` = 64-bit double, `numeric` = int or float.

---

## 5. Instruction Reference

### 5.1 Loads & Data Movement

#### `load.k` — Load Constant

| Field    | Value         |
| -------- | ------------- |
| Format   | ABx           |
| Operands | `R(A), K(Bx)` |

**Operation:** `R(A) → K(Bx)`

Loads a value from the constant pool into a register. Constants include strings, integers, floats, and `null`.

**Disassembly:** `load.k r0, k3` with the constant value shown as a comment (e.g., `; "hello"`, `; 42`).

---

#### `load.null` — Load Null

| Field    | Value  |
| -------- | ------ |
| Format   | A only |
| Operands | `R(A)` |

**Operation:** `R(A) → null`

Sets register A to the null value.

---

#### `load.bool` — Load Boolean

| Field    | Value        |
| -------- | ------------ |
| Format   | ABC          |
| Operands | `R(A), B, C` |

**Operation:** `R(A) → (B ≠ 0)`. If `C ≠ 0`, skip the next instruction (`IP += 1`).

Loads `true` (B=1) or `false` (B=0) into register A. The skip flag (C) is used to implement short-circuit patterns where a conditional branch needs to skip over a subsequent jump.

**Disassembly:** `load.bool r0, true` or `load.bool r0, false skip next`.

---

#### `move` — Copy Register

| Field    | Value        |
| -------- | ------------ |
| Format   | ABC          |
| Operands | `R(A), R(B)` |

**Operation:** `R(A) → R(B)`

Copies the value from register B into register A. This is a value copy for primitives and a reference copy for objects (arrays, dicts, struct instances).

---

### 5.2 Global Variables

#### `get.global` — Read Global

| Field    | Value         |
| -------- | ------------- |
| Format   | ABx           |
| Operands | `R(A), G(Bx)` |

**Operation:** `R(A) → G(Bx)`

Loads the value from global slot Bx into register A.

- **Main script:** Direct array access into `_globalSlots[Bx]`.
- **Module function:** Dictionary lookup via the frame's `ModuleGlobals` using the name from `GlobalNameTable[Bx]`.
- **Error:** `"Undefined variable '{name}'."` if the slot contains the undefined sentinel.

**Disassembly:** `get.global r3, [g0]` with the global name as comment (e.g., `; env`).

---

#### `set.global` — Write Global

| Field    | Value         |
| -------- | ------------- |
| Format   | ABx           |
| Operands | `G(Bx), R(A)` |

**Operation:** `G(Bx) → R(A)`

Stores the value from register A into global slot Bx.

- **Error:** `"Assignment to constant variable."` if the slot was initialized with `init.const.global`.
- **Dual-write:** The value is written to both the fast-path slot array and the named globals dictionary (for module loading, debugger, and REPL compatibility).

**Disassembly:** `set.global [g5], r0` with the global name as comment.

---

#### `init.const.global` — Initialize Constant Global

| Field    | Value         |
| -------- | ------------- |
| Format   | ABx           |
| Operands | `G(Bx), R(A)` |

**Operation:** `G(Bx) → R(A)`, mark slot Bx as read-only.

Stores the value and marks the global slot as constant. Any subsequent `set.global` targeting this slot will throw. Only emitted during top-level `const` declarations.

**Disassembly:** `init.const.global [g1], r0` with `; VarName (const)` as comment.

---

### 5.3 Upvalues (Closure Captures)

Upvalues are the mechanism by which closures capture variables from enclosing scopes. While the captured variable is still live on the stack, the upvalue points directly to the stack slot (**open**). When the enclosing function returns, the upvalue copies the value to a heap-allocated cell (**closed**).

#### `get.upval` — Read Upvalue

| Field    | Value         |
| -------- | ------------- |
| Format   | ABC           |
| Operands | `R(A), UV(B)` |

**Operation:** `R(A) → UV(B).Value`

Reads the captured variable from upvalue slot B.

**Disassembly:** `get.upval r0, [uv2]` with the upvalue name as comment if available.

---

#### `set.upval` — Write Upvalue

| Field    | Value         |
| -------- | ------------- |
| Format   | ABC           |
| Operands | `UV(B), R(A)` |

**Operation:** `UV(B).Value → R(A)`

Writes the value from register A into upvalue slot B. This mutates the captured variable — if it's still open, the write goes directly to the original stack slot.

**Disassembly:** `set.upval [uv2], r0`.

---

#### `close.upval` — Close Upvalues

| Field    | Value  |
| -------- | ------ |
| Format   | A only |
| Operands | `R(A)` |

**Operation:** Close all open upvalues that reference stack slots at or above `BaseSlot + A`.

Promotes captured variables from stack references to heap-allocated cells. Emitted when a local variable goes out of scope and has been captured by a closure. After closing, the upvalue holds its own copy of the value independent of the stack.

---

### 5.4 Arithmetic

All arithmetic instructions follow the same type dispatch pattern:

1. **Fast path (int + int):** Direct 64-bit integer operation, result is `int`. Aggressively inlined.
2. **Slow path (numeric + numeric):** Both operands converted to `double`, result is `float`.
3. **Fallback:** Delegates to `RuntimeOps` for type-specific behavior (e.g., string concatenation for `add`).

#### `add` — Addition

| Field    | Value              |
| -------- | ------------------ |
| Format   | ABC                |
| Operands | `R(A), R(B), R(C)` |

**Operation:** `R(A) → R(B) + R(C)`

- `int + int` → `int`
- `numeric + numeric` → `float`
- `string + string` → `string` (concatenation)
- Other combinations: delegates to `RuntimeOps.Add()`

---

#### `sub` — Subtraction

| Field    | Value              |
| -------- | ------------------ |
| Format   | ABC                |
| Operands | `R(A), R(B), R(C)` |

**Operation:** `R(A) → R(B) − R(C)`

- `int − int` → `int`
- `numeric − numeric` → `float`

---

#### `mul` — Multiplication

| Field    | Value              |
| -------- | ------------------ |
| Format   | ABC                |
| Operands | `R(A), R(B), R(C)` |

**Operation:** `R(A) → R(B) × R(C)`

- `int × int` → `int`
- `numeric × numeric` → `float`

---

#### `div` — Division

| Field    | Value              |
| -------- | ------------------ |
| Format   | ABC                |
| Operands | `R(A), R(B), R(C)` |

**Operation:** `R(A) → R(B) ÷ R(C)`

- `int ÷ int` → `int` (integer division)
- `numeric ÷ numeric` → `float`
- **Error:** `"Division by zero."` if R(C) is `0` or `0.0`.

---

#### `mod` — Modulo

| Field    | Value              |
| -------- | ------------------ |
| Format   | ABC                |
| Operands | `R(A), R(B), R(C)` |

**Operation:** `R(A) → R(B) % R(C)`

- `int % int` → `int`
- `numeric % numeric` → `float`
- **Error:** `"Division by zero."` if R(C) is `0` or `0.0`.

---

#### `pow` — Exponentiation

| Field    | Value              |
| -------- | ------------------ |
| Format   | ABC                |
| Operands | `R(A), R(B), R(C)` |

**Operation:** `R(A) → R(B) ** R(C)`

- `int ** int` → `int` (cast from `Math.Pow` to `long`)
- `numeric ** numeric` → `float`

---

#### `neg` — Negation

| Field    | Value        |
| -------- | ------------ |
| Format   | ABC          |
| Operands | `R(A), R(B)` |

**Operation:** `R(A) → −R(B)`

- `int` → negated `int`
- `float` → negated `float`

---

#### `addi` — Add Signed Immediate

| Field    | Value       |
| -------- | ----------- |
| Format   | AsBx        |
| Operands | `R(A), sBx` |

**Operation:** `R(A) → R(A) + sBx`

Adds a signed 16-bit immediate value directly to the register without touching the constant pool. Used for `++` and `--` operators.

- `int + sBx` → `int`
- `float + sBx` → `float`
- **Error:** `"Operand of '++' or '--' must be a number."` for non-numeric types.

**Disassembly:** `addi r0, +1` or `addi r0, -1`.

---

#### `addk` — Add with Constant Operand

| Field    | Value              |
| -------- | ------------------ |
| Format   | ABC                |
| Operands | `R(A), R(B), K(C)` |

**Operation:** `R(A) → R(B) + K(C)`

Fused load-and-add: one operand comes from the constant pool instead of a register. Emitted when the compiler detects a literal on the right-hand side of `+` and the constant index fits in 8 bits (≤ 255).

- Same type dispatch as `add`, but the second operand is `K(C)` instead of `R(C)`.

**Disassembly:** `addk r0, r1, k3` with the constant value shown inline.

---

#### `subk` — Subtract with Constant Operand

| Field    | Value              |
| -------- | ------------------ |
| Format   | ABC                |
| Operands | `R(A), R(B), K(C)` |

**Operation:** `R(A) → R(B) − K(C)`

Same as `addk` but for subtraction.

---

### 5.5 Bitwise Operations

All bitwise operations require integer operands. Non-integer types delegate to `RuntimeOps`.

#### `band` — Bitwise AND

| Field    | Value              |
| -------- | ------------------ |
| Format   | ABC                |
| Operands | `R(A), R(B), R(C)` |

**Operation:** `R(A) → R(B) & R(C)`

---

#### `bor` — Bitwise OR

| Field    | Value              |
| -------- | ------------------ |
| Format   | ABC                |
| Operands | `R(A), R(B), R(C)` |

**Operation:** `R(A) → R(B) | R(C)`

---

#### `bxor` — Bitwise XOR

| Field    | Value              |
| -------- | ------------------ |
| Format   | ABC                |
| Operands | `R(A), R(B), R(C)` |

**Operation:** `R(A) → R(B) ^ R(C)`

---

#### `bnot` — Bitwise NOT

| Field    | Value        |
| -------- | ------------ |
| Format   | ABC          |
| Operands | `R(A), R(B)` |

**Operation:** `R(A) → ~R(B)`

---

#### `shl` — Shift Left

| Field    | Value              |
| -------- | ------------------ |
| Format   | ABC                |
| Operands | `R(A), R(B), R(C)` |

**Operation:** `R(A) → R(B) << R(C)`

---

#### `shr` — Shift Right

| Field    | Value              |
| -------- | ------------------ |
| Format   | ABC                |
| Operands | `R(A), R(B), R(C)` |

**Operation:** `R(A) → R(B) >> R(C)`

---

### 5.6 Comparison

All comparison instructions produce a `bool` result. Each has a **constant-fused variant** (suffix `.k`) where operand C is a constant pool index instead of a register.

**Equality semantics:** No type coercion. `5 != "5"`, `0 != false`, `0 != null`. Reference equality for dicts and struct instances.

**Ordering semantics:** Fast path for `int` vs `int`. Mixed numeric types promote to `float`. Non-numeric ordering delegates to `RuntimeOps`.

#### `eq` / `eq.k` — Equal

| Field    | Value                                   |
| -------- | --------------------------------------- |
| Format   | ABC                                     |
| Operands | `R(A), R(B), R(C)` / `R(A), R(B), K(C)` |

**Operation:** `R(A) → (R(B) == R(C))` or `R(A) → (R(B) == K(C))`

- **int == int:** Direct 64-bit comparison.
- **Other types:** `RuntimeOps.IsEqual()` — value equality for primitives, reference equality for objects.

---

#### `ne` / `ne.k` — Not Equal

**Operation:** `R(A) → (R(B) != R(C))` or `R(A) → (R(B) != K(C))`

Logical inverse of `eq`.

---

#### `lt` / `lt.k` — Less Than

**Operation:** `R(A) → (R(B) < R(C))` or `R(A) → (R(B) < K(C))`

---

#### `le` / `le.k` — Less Than or Equal

**Operation:** `R(A) → (R(B) <= R(C))` or `R(A) → (R(B) <= K(C))`

---

#### `gt` / `gt.k` — Greater Than

**Operation:** `R(A) → (R(B) > R(C))` or `R(A) → (R(B) > K(C))`

---

#### `ge` / `ge.k` — Greater Than or Equal

**Operation:** `R(A) → (R(B) >= R(C))` or `R(A) → (R(B) >= K(C))`

---

### 5.7 Logic & Truthiness

Stash's truthiness rules: **Falsy** values are `null`, `false`, `0`, `0.0`, and `""` (empty string). Everything else is **truthy**, including empty arrays and empty dicts.

#### `not` — Logical NOT

| Field    | Value        |
| -------- | ------------ |
| Format   | ABC          |
| Operands | `R(A), R(B)` |

**Operation:** `R(A) → !IsTruthy(R(B))`

Returns `true` if the operand is falsy, `false` if truthy. Always produces a `bool`.

---

#### `test.set` — Conditional Copy (Short-Circuit)

| Field    | Value           |
| -------- | --------------- |
| Format   | ABC             |
| Operands | `R(A), R(B), C` |

**Operation:**

```
if IsTruthy(R(B)) == (C ≠ 0):
    R(A) → R(B)           // copy the actual value, not a bool
else:
    IP += 1               // skip next instruction
```

The core instruction for `&&` and `||` operators. Critically, it copies the **operand value itself**, not a boolean — this is what makes `null || "default"` return `"default"` and `"a" && "b"` return `"b"`.

- For `||` (logical OR): C=0. If R(B) is falsy, copy it and continue; if truthy, skip the jump to evaluate the right side.
- For `&&` (logical AND): C=1. If R(B) is truthy, copy it and continue; if falsy, skip the jump.

The skipped instruction is always a `jmp` to the right-hand operand.

---

#### `test` — Conditional Skip

| Field    | Value     |
| -------- | --------- |
| Format   | ABC       |
| Operands | `R(A), C` |

**Operation:**

```
if IsTruthy(R(A)) != (C ≠ 0):
    IP += 1               // skip next instruction
```

A branch-only variant of `test.set` — it tests the truthiness of R(A) but does **not** copy any value. The following instruction is typically a `jmp`. Used in conditional statement compilation where the value doesn't need to be preserved.

---

### 5.8 Control Flow

#### `jmp` — Unconditional Jump

| Field    | Value |
| -------- | ----- |
| Format   | AsBx  |
| Operands | `sBx` |

**Operation:** `IP += sBx`

Relative jump by signed offset. Forward jumps skip instructions; backward jumps are not used for loops (see `loop` instead).

**Disassembly:** `jmp .L003` with offset shown as comment (e.g., `; +5`).

---

#### `jmp.false` — Jump if Falsy

| Field    | Value       |
| -------- | ----------- |
| Format   | AsBx        |
| Operands | `R(A), sBx` |

**Operation:** If `IsFalsy(R(A))`, then `IP += sBx`.

Conditional jump taken when R(A) is falsy. Falls through otherwise.

---

#### `jmp.true` — Jump if Truthy

| Field    | Value       |
| -------- | ----------- |
| Format   | AsBx        |
| Operands | `R(A), sBx` |

**Operation:** If `IsTruthy(R(A))`, then `IP += sBx`.

Conditional jump taken when R(A) is truthy. Falls through otherwise.

---

#### `loop` — Loop Back-Edge

| Field    | Value |
| -------- | ----- |
| Format   | AsBx  |
| Operands | `sBx` |

**Operation:** `IP += sBx` (always negative — jumps backward).

Functionally identical to `jmp` but carries additional semantics: every 256 iterations, it checks the cancellation token and enforces the step limit. This prevents infinite loops without per-instruction overhead. Also triggers debug line resets in debug mode.

---

#### `call` — Function Call

| Field    | Value     |
| -------- | --------- |
| Format   | ABC       |
| Operands | `R(A), C` |

**Operation:** Call the callable in R(A) with C arguments from R(A+1)..R(A+C). Result stored in R(A).

**Dispatch by callable type:**

| Callable Type     | Behavior                                                                        |
| ----------------- | ------------------------------------------------------------------------------- |
| `VMFunction`      | Push new call frame. BaseSlot = current base + A + 1. Args already in position. |
| `VMBoundMethod`   | Shift args right by 1, insert receiver at R(0), push frame.                     |
| `BuiltInFunction` | Call .NET delegate directly. No frame push. Result in R(A).                     |
| `IStashCallable`  | Call via interface. No frame push. Result in R(A).                              |

**Arity validation:**

- With rest parameter: `argc >= minArity` (excess collected into rest array)
- Without rest parameter: `minArity <= argc <= arity` (missing optionals set to sentinel)
- Async functions: spawn on background thread instead of pushing frame.

---

#### `return` — Function Return

| Field    | Value     |
| -------- | --------- |
| Format   | ABC       |
| Operands | `R(A), B` |

**Operation:** Return from current function. If B=1, return value is R(A); if B=0, return `null`.

1. Close any open upvalues referencing this frame's locals.
2. Pop the call frame.
3. Write return value to caller's R(A) (the register that held the callee).
4. Restore stack pointer to caller's frame extent.
5. If this was the top-level frame, return the value to the host.

---

### 5.9 Numeric Iteration

Numeric for-loops use **4 consecutive registers** starting at R(A):

| Register | Purpose       | Example: `for (let i = 0; i < 10; i++)` |
| -------- | ------------- | --------------------------------------- |
| R(A)     | Counter       | Internal counter (modified by step)     |
| R(A+1)   | Limit         | `10`                                    |
| R(A+2)   | Step          | `1`                                     |
| R(A+3)   | Loop variable | `i` (visible to loop body)              |

#### `for.prep` — Numeric For-Loop Init

| Field    | Value       |
| -------- | ----------- |
| Format   | AsBx        |
| Operands | `R(A), sBx` |

**Operation:** `R(A) → R(A) − R(A+2)`, then `IP += sBx` (skip to loop end for first-iteration test).

Pre-decrements the counter by the step value so that the first `for.loop` iteration will increment it back to the starting value. Validates that counter and step are numeric.

---

#### `for.loop` — Numeric For-Loop Step

| Field    | Value       |
| -------- | ----------- |
| Format   | AsBx        |
| Operands | `R(A), sBx` |

**Operation:**

```
R(A) → R(A) + R(A+2)           // increment counter by step
if (step > 0 and R(A) <= R(A+1)) or (step < 0 and R(A) >= R(A+1)):
    R(A+3) → R(A)              // assign loop variable
    IP += sBx                  // jump back to loop body
else:
    fall through                // loop exit
```

---

#### `for.prepII` — Integer-Specialized For-Loop Init

| Field    | Value       |
| -------- | ----------- |
| Format   | AsBx        |
| Operands | `R(A), sBx` |

Same as `for.prep` but only takes the fast path when all three registers (counter, limit, step) are `int`. Falls back to `for.prep` if any register is non-integer. Avoids float promotion overhead in pure-integer loops.

---

#### `for.loopII` — Integer-Specialized For-Loop Step

| Field    | Value       |
| -------- | ----------- |
| Format   | AsBx        |
| Operands | `R(A), sBx` |

Same as `for.loop` but optimized for all-integer operands. No type guards per iteration on the fast path.

---

### 5.10 Generic Iteration

Generic iteration (`for..in`) uses an iterator state object and supports arrays, dicts, strings, ranges, and enums.

#### `iter.prep` — Initialize Iterator

| Field    | Value     |
| -------- | --------- |
| Format   | ABC       |
| Operands | `R(A), B` |

**Operation:** Create an `IteratorState` from the collection in R(A). B indicates indexed mode (0 = single variable, 1 = key-value pair).

**Collection-specific initialization:**

| Collection Type | Behavior                                                     |
| --------------- | ------------------------------------------------------------ |
| Array           | **Snapshots** the array (prevents mutation-during-iteration) |
| Typed array     | Snapshots as `List<StashValue>`                              |
| Dictionary      | Stores a dictionary enumerator                               |
| String          | Stores string reference for character iteration              |
| Range           | Stores range reference for value generation                  |
| Enum            | Builds list of `StashEnumValue` members                      |

**Error:** Throws if the value is not iterable.

---

#### `iter.loop` — Advance Iterator

| Field    | Value       |
| -------- | ----------- |
| Format   | AsBx        |
| Operands | `R(A), sBx` |

**Operation:** Advance the iterator state in R(A). Assign current element(s) to R(A+1) and optionally R(A+2). If exhausted, jump by sBx (to loop exit).

**Per-collection output:**

| Collection | R(A+1)                    | R(A+2)                    |
| ---------- | ------------------------- | ------------------------- |
| Array      | Element value             | Index (int)               |
| Dictionary | Key (or value if indexed) | Value (or key if indexed) |
| String     | Character (string)        | Index (int)               |
| Range      | Current value (int)       | Iteration index           |
| Enum       | Enum member value         | Index (int)               |

---

### 5.11 Collections & Indexing

#### `get.table` — Index Read

| Field    | Value              |
| -------- | ------------------ |
| Format   | ABC                |
| Operands | `R(A), R(B), R(C)` |

**Operation:** `R(A) → R(B)[R(C)]`

| Receiver Type | Key Type | Behavior                                         |
| ------------- | -------- | ------------------------------------------------ |
| Array         | int      | Negative indices wrap (`arr[-1]` → last element) |
| Typed array   | int      | Same wrapping behavior                           |
| Dictionary    | any      | Key lookup; returns `null` if key doesn't exist  |
| String        | int      | Returns single-character string; negative wraps  |

**Error:** `IndexError` if array/string index is out of bounds after wrapping. Null dictionary keys throw.

---

#### `set.table` — Index Write

| Field    | Value              |
| -------- | ------------------ |
| Format   | ABC                |
| Operands | `R(A), R(B), R(C)` |

**Operation:** `R(A)[R(B)] → R(C)`

| Receiver Type | Behavior                              |
| ------------- | ------------------------------------- |
| Array         | Negative indices wrap; bounds-checked |
| Typed array   | Same; type validation on value        |
| Dictionary    | Auto-inserts; null keys throw         |
| String        | **Error** — strings are immutable     |

---

#### `new.array` — Array Literal

| Field    | Value     |
| -------- | --------- |
| Format   | ABC       |
| Operands | `R(A), B` |

**Operation:** `R(A) → [R(A+1), R(A+2), ..., R(A+B)]`

Collects B elements from consecutive registers into a new array. Elements marked as `SpreadMarker` are unpacked: their contents are flattened into the result array.

---

#### `new.dict` — Dictionary Literal

| Field    | Value     |
| -------- | --------- |
| Format   | ABC       |
| Operands | `R(A), B` |

**Operation:** `R(A) → {R(A+1): R(A+2), R(A+3): R(A+4), ..., R(A+2B-1): R(A+2B)}`

Collects B key-value pairs from consecutive registers. A null key signals a spread entry: the corresponding value (a dict or struct instance) is merged into the result.

---

#### `new.range` — Range Construction

| Field    | Value              |
| -------- | ------------------ |
| Format   | ABC                |
| Operands | `R(A), R(B), R(C)` |

**Operation:** `R(A) → Range(R(B), R(C), step)`

Creates a range from start (R(B)) to end (R(C)). Step is pre-loaded in R(A+1) by the compiler; if null, auto-inferred as `+1` (start ≤ end) or `−1` (start > end).

**Error:** Step of `0` throws.

---

#### `spread` — Spread Marker

| Field    | Value        |
| -------- | ------------ |
| Format   | ABC          |
| Operands | `R(A), R(B)` |

**Operation:** `R(A) → SpreadMarker(R(B))`

Wraps an iterable in a `SpreadMarker` sentinel. This marker is consumed by `new.array`, `new.dict`, and `call.spread` to unpack the contents at the call site. It is never visible to user code.

---

#### `destructure` — Destructuring Assignment

| Field    | Value         |
| -------- | ------------- |
| Format   | ABx           |
| Operands | `R(A), K(Bx)` |

**Operation:** Unpack the value in R(A) into multiple registers according to the destructuring metadata at K(Bx).

- **Array destructuring:** `[a, b, ...rest] = R(A)` — elements assigned to R(A)..R(A+N), rest collects remainder as new array.
- **Object destructuring:** `{x, y, ...rest} = R(A)` — fields matched by name from struct instances or dict keys, rest collects unmatched entries.
- Missing elements are padded with `null`.

---

#### `in` — Membership Test

| Field    | Value              |
| -------- | ------------------ |
| Format   | ABC                |
| Operands | `R(A), R(B), R(C)` |

**Operation:** `R(A) → R(B) in R(C)`

Delegates to `RuntimeOps.Contains()`. Result is `bool`.

---

### 5.12 Field Access

#### `get.field` — Field Read

| Field    | Value              |
| -------- | ------------------ |
| Format   | ABC                |
| Operands | `R(A), R(B), K(C)` |

**Operation:** `R(A) → R(B).fieldName` where fieldName = K(C) (a string constant).

Resolves the named field on the receiver object. Resolution order:

1. **StashInstance:** direct field access, method binding
2. **StashDictionary:** extension methods, then key access
3. **StashNamespace:** member lookup
4. **StashStruct:** static method access
5. **StashEnum:** enum member access
6. **StashEnumValue:** properties (`typeName`, `memberName`)
7. **StashError:** properties (`message`, `type`, `stack`, custom)
8. **Built-in properties:** `duration.totalMs`, `bytesize.kb`, `string.length`, `array.length`, etc.
9. **Extension methods:** registry lookup
10. **UFCS:** fallback to namespace functions as methods

**Disassembly:** `get.field r0, r1, k3` with `.fieldName` shown as comment.

---

#### `get.field.ic` — Field Read with Inline Cache

| Field    | Value                     |
| -------- | ------------------------- |
| Format   | ABC + companion word      |
| Operands | `R(A), R(B), K(C) [ic:N]` |

**Operation:** Same as `get.field` but with an inline cache slot for fast repeated access.

Consumes a **companion word** (the next instruction word) which holds the inline cache slot index. See [Section 6: Inline Caching](#6-inline-caching) for the state machine.

- **IC hit (State 1):** Guard matches → return cached value directly. No field lookup.
- **IC miss (State 0 or guard mismatch):** Full lookup, then populate cache.
- **Megamorphic (State 2):** Always full lookup, no caching.

**Disassembly:** `get.field.ic r0, r1, k3` with `.fieldName [ic:0]` as comment.

---

#### `set.field` — Field Write

| Field    | Value              |
| -------- | ------------------ |
| Format   | ABC                |
| Operands | `R(A), K(B), R(C)` |

**Operation:** `R(A).fieldName → R(C)` where fieldName = K(B).

Sets a named field on the receiver. Only valid for struct instances and dictionaries. Immutable types (namespaces, enums, built-in types) throw.

---

#### `self` — Method Binding

| Field    | Value              |
| -------- | ------------------ |
| Format   | ABC                |
| Operands | `R(A), R(B), K(C)` |

**Operation:** Look up method K(C) on receiver R(B) and store in R(A) as a bound method. Also stores the receiver in R(A+1) for the subsequent `call`.

Used to compile method calls like `obj.method(args)` where the receiver needs to be passed as the implicit first argument.

---

### 5.13 Functions & Closures

#### `closure` — Create Closure

| Field    | Value         |
| -------- | ------------- |
| Format   | ABx           |
| Operands | `R(A), K(Bx)` |

**Operation:** Create a `VMFunction` from the chunk at K(Bx), capturing upvalues from the current scope.

The instruction is followed by N **companion words** (one per upvalue), each encoding:

- **Bits 0–7:** `isLocal` — 1 if capturing a local from this frame, 0 if inheriting an upvalue from the enclosing closure.
- **Bits 8–15:** `index` — the register index (if local) or upvalue index (if inherited).

For local captures, `CaptureUpvalue()` creates a new open upvalue pointing to the stack slot. For inherited captures, the existing upvalue reference is shared.

**Disassembly:** `closure r0, k5` with `; <fn:myFunc(2p)>` as comment, followed by `; upvalue [0]: local 3` etc.

---

#### `call.builtin` — Built-In Namespace Call with IC

| Field    | Value                  |
| -------- | ---------------------- |
| Format   | ABC + companion word   |
| Operands | `R(A), R(B), C [ic:N]` |

**Operation:** Call a built-in namespace function. R(B) is the namespace/receiver, C is the argument count. Arguments are in R(A+1)..R(A+C). Result stored in R(A).

This is a specialized call instruction that combines field access + function call with inline caching. The companion word holds the IC slot index.

**Fast path (IC hit):**

1. Guard check: `R(B)` is the same namespace object as cached.
2. Call the cached `BuiltInFunction` delegate directly with args.
3. No field lookup overhead.

**Slow path (IC miss):**

1. Resolve field name from `K(ic.ConstantIndex)` on the object in R(B).
2. Execute the resolved callable.
3. Populate IC if the receiver is a frozen namespace.

**Disassembly:** `call.builtin r1, r3, 1` with `; (1 args) [ic:0]` as comment.

---

#### `call.spread` — Call with Spread Arguments

| Field    | Value     |
| -------- | --------- |
| Format   | ABx       |
| Operands | `R(A), B` |

**Operation:** Call R(A) with B arguments (some of which may be `SpreadMarker`s). Spread markers are expanded: their contents are flattened into the argument list before the call.

---

### 5.14 Type System

#### `typeof` — Get Type Name

| Field    | Value        |
| -------- | ------------ |
| Format   | ABC          |
| Operands | `R(A), R(B)` |

**Operation:** `R(A) → typeof(R(B))` as a string.

**Type mapping:**

| Value Type      | Result String      |
| --------------- | ------------------ |
| null            | `"null"`           |
| bool            | `"bool"`           |
| int (long)      | `"int"`            |
| float (double)  | `"float"`          |
| string          | `"string"`         |
| array (List)    | `"array"`          |
| typed array     | `"ElementType[]"`  |
| dict            | `"dict"`           |
| range           | `"range"`          |
| duration        | `"duration"`       |
| byte size       | `"bytes"`          |
| semver          | `"semver"`         |
| secret          | `"secret"`         |
| ip address      | `"ip"`             |
| error           | `"Error"`          |
| struct instance | Instance type name |
| enum value      | Enum type name     |
| struct def      | `"struct"`         |
| enum def        | `"enum"`           |
| interface def   | `"interface"`      |
| namespace       | `"namespace"`      |
| future          | `"Future"`         |
| function        | `"function"`       |

---

#### `is` — Runtime Type Check

| Field    | Value              |
| -------- | ------------------ |
| Format   | ABC                |
| Operands | `R(A), R(B), R(C)` |

**Operation:** `R(A) → (R(B) is Type)` where the type is resolved from R(C).

Bit 7 of the raw C operand encodes a `isDynamic` flag:

- `isDynamic = true`: If the type name is unrecognized, throw `"Right-hand side of 'is' must be a type."`.
- `isDynamic = false`: If the type name is unrecognized, return `false`.

Supports all built-in type names, user-defined structs/enums/interfaces, and typed array syntax (`int[]`, `float[]`). Interface checks verify that the struct implements the interface.

---

#### `struct.decl` — Declare Struct

| Field    | Value         |
| -------- | ------------- |
| Format   | ABx           |
| Operands | `R(A), K(Bx)` |

**Operation:** Create a `StashStruct` definition from metadata at K(Bx). Method closures are read from R(A+1)..R(A+N).

Validates interface compliance: all required fields must be present, all required methods must exist with correct arity (excluding the implicit `self` parameter).

---

#### `enum.decl` — Declare Enum

| Field    | Value         |
| -------- | ------------- |
| Format   | ABx           |
| Operands | `R(A), K(Bx)` |

**Operation:** Create a `StashEnum` from metadata at K(Bx). Stores in R(A).

---

#### `iface.decl` — Declare Interface

| Field    | Value         |
| -------- | ------------- |
| Format   | ABx           |
| Operands | `R(A), K(Bx)` |

**Operation:** Create a `StashInterface` from metadata at K(Bx). Stores in R(A).

---

#### `extend` — Extend Type

| Field    | Value         |
| -------- | ------------- |
| Format   | ABx           |
| Operands | `R(A), K(Bx)` |

**Operation:** Add methods to an existing type. Method closures read from R(A+1)..R(A+N).

- **Built-in types:** Methods registered in the extension registry, accessible via UFCS.
- **User-defined structs:** Methods added to the struct's method dictionary (existing methods not overwritten).

---

#### `new.struct` — Instantiate Struct

| Field    | Value           |
| -------- | --------------- |
| Format   | ABC             |
| Operands | `R(A), K(B), C` |

**Operation:** Create a `StashInstance` of the struct type from metadata at K(B) with C field values from registers.

**Two layouts:**

- Without type register: Type resolved from globals by name. Fields at R(A+1)..R(A+C).
- With type register: Type reference in R(A+1). Fields at R(A+2)..R(A+1+C).

**Validation:** All field names must exist in the struct definition. No duplicates.

---

#### `check.numeric` — Numeric Guard

| Field    | Value  |
| -------- | ------ |
| Format   | A only |
| Operands | `R(A)` |

**Operation:** If `R(A)` is not numeric, throw `"Operand of '++' or '--' must be a number."`.

Emitted before `addi` to guard `++`/`--` on variables that might not be numbers.

---

#### `typed.wrap` — Type Narrowing

| Field    | Value         |
| -------- | ------------- |
| Format   | ABx           |
| Operands | `R(A), K(Bx)` |

**Operation:** Narrow R(A) to the type specified by the string at K(Bx).

- **Byte narrowing:** `int` or `float` → `byte` with range check [0, 255].
- **Array wrapping:** `List<StashValue>` → `StashTypedArray` with element type validation.
- **Null:** Passes through unchanged.

**Error:** Out-of-range values, type mismatches.

---

### 5.15 Error Handling

The VM maintains an exception handler stack. Each handler records where to resume (catch IP), what stack state to restore, and which register receives the error.

#### `try.begin` — Enter Try Block

| Field    | Value       |
| -------- | ----------- |
| Format   | AsBx        |
| Operands | `R(A), sBx` |

**Operation:** Push an exception handler. A = error register (where the caught error will be stored). sBx = signed offset to the catch handler IP.

The handler records the current stack pointer, frame count, and computed catch IP. If a `RuntimeError` occurs while this handler is active, the VM unwinds to this state.

---

#### `try.end` — Exit Try Block

| Field    | Value |
| -------- | ----- |
| Format   | Ax    |
| Operands | none  |

**Operation:** Pop the innermost exception handler from the stack.

Emitted at the end of a try block's normal exit path. If execution reaches here, no error occurred and the handler is no longer needed.

---

#### `throw` — Raise Error

| Field    | Value  |
| -------- | ------ |
| Format   | A only |
| Operands | `R(A)` |

**Operation:** Throw the value in R(A) as an error.

| Value Type | Behavior                                                       |
| ---------- | -------------------------------------------------------------- |
| StashError | Re-throw with properties preserved                             |
| String     | Wrap in `RuntimeError` with the string as the message          |
| Dictionary | Extract `"message"` and `"type"` fields, throw with properties |
| Other      | Stringify and wrap in `RuntimeError`                           |

The thrown `RuntimeError` is caught by the outermost VM loop. If an exception handler exists, control transfers to its catch IP; the error is stored in the handler's error register, and the stack/frame state is restored. If no handler exists, the error propagates to the host.

---

#### `try.expr` — Try Expression Result

| Field    | Value        |
| -------- | ------------ |
| Format   | ABC          |
| Operands | `R(A), R(B)` |

**Operation:** `R(A) → R(B)`

Simple register copy used after a catch clause to move the exception handler's result to the destination register. Used in `try` expressions (as opposed to `try` statements).

---

### 5.16 Strings

#### `interpolate` — String Interpolation

| Field    | Value     |
| -------- | --------- |
| Format   | ABC       |
| Operands | `R(A), B` |

**Operation:** `R(A) → concat(R(A+1), R(A+2), ..., R(A+B))`

Concatenates B parts from consecutive registers into a single string. Parts alternate between literal string fragments (pre-compiled) and expression results (stringified via `RuntimeOps.Stringify()`). This is the runtime implementation of `$"hello {name}"` string interpolation.

---

### 5.17 Shell & Process

#### `command` — Execute Shell Command

| Field    | Value        |
| -------- | ------------ |
| Format   | ABC          |
| Operands | `R(A), B, C` |

**Operation:** Assemble command string from B parts in R(A+1)..R(A+B), execute it as a subprocess.

**Tilde expansion:** `~` and `~/path` expanded to user home directory.

**Elevation:** If an `elevate` block is active, the command is prefixed with the elevation command (`sudo`/`gsudo`).

**Mode flags (C):**

| Flag   | Meaning                                                |
| ------ | ------------------------------------------------------ |
| `0x01` | **Passthrough** — stream output to console, no capture |
| `0x02` | **Strict** — throw if exit code ≠ 0                    |
| `0x00` | **Default** — capture stdout/stderr silently           |

**Result:** A `CommandResult` struct instance with fields `stdout`, `stderr`, `exitCode`.

---

#### `pipe` — Command Pipeline

| Field    | Value              |
| -------- | ------------------ |
| Format   | ABC                |
| Operands | `R(A), R(B), R(C)` |

**Operation:** `R(A) → R(B) | R(C)` (pipe left command's stdout into right command).

Both operands must be command results. The compiler handles the actual piping during command execution; this opcode stores the right-side result.

---

#### `redirect` — I/O Redirection

| Field    | Value           |
| -------- | --------------- |
| Format   | ABC             |
| Operands | `R(A), R(C), B` |

**Operation:** Write command output to file.

**Flags (B):**

- Bits [1:0]: Stream selector — 0=stdout, 1=stderr, 2=both
- Bit 2: Append mode — 0=overwrite, 1=append

Extracts the selected stream(s) from the command result, writes to the file path in R(C), and clears the redirected stream in the result.

---

### 5.18 Modules

#### `import` — Selective Import

| Field    | Value         |
| -------- | ------------- |
| Format   | ABx           |
| Operands | `R(A), K(Bx)` |

**Operation:** Load module from path in R(A), import named exports into registers according to metadata at K(Bx).

**Module loading:**

1. Resolve path (relative to current file, with `.stash` extension auto-appended).
2. Check module cache — if already loaded, reuse cached globals.
3. Compile and execute module in an isolated child VM.
4. Extract named exports from module globals.
5. Assign each imported name to consecutive registers starting at R(A).

**Error:** Throws if imported name not found in module. Circular imports detected and rejected.

**Package resolution:** Non-path specifiers (no `.`, `/`, or `.stash`) resolve via the package system: walk up to find `stash.json`, look in `stashes/{packageName}/`.

---

#### `import.as` — Import as Namespace

| Field    | Value         |
| -------- | ------------- |
| Format   | ABx           |
| Operands | `R(A), K(Bx)` |

**Operation:** Load module and wrap all its exports as a frozen namespace with the alias name from metadata at K(Bx).

The resulting namespace is immutable — its members can be accessed but not modified. Built-in namespace names are excluded from the export.

---

### 5.19 Concurrency

#### `await` — Await Future

| Field    | Value        |
| -------- | ------------ |
| Format   | ABC          |
| Operands | `R(A), R(B)` |

**Operation:** `R(A) → await R(B)`

If R(B) is a `StashFuture`, blocks until the async task completes and stores the result in R(A). If R(B) is not a future, passes the value through unchanged (no-op).

---

### 5.20 Miscellaneous

#### `switch` — Switch Metadata (No-Op)

| Field    | Value         |
| -------- | ------------- |
| Format   | ABx           |
| Operands | `R(A), K(Bx)` |

**Operation:** No operation at runtime.

The compiler expands switch statements into comparison chains and conditional jumps. This opcode exists only to carry metadata for tooling (disassembler, debugger).

---

#### `elevate.begin` — Enter Elevation Block

| Field    | Value        |
| -------- | ------------ |
| Format   | ABC          |
| Operands | `R(A), R(B)` |

**Operation:** Activate privilege escalation. Subsequent `command` instructions will be prefixed with the elevation command.

- R(B) specifies the elevation command. If null, defaults to `sudo` (Unix) or `gsudo` (Windows).
- **Error:** Throws in embedded mode (privilege escalation not allowed).

---

#### `elevate.end` — Exit Elevation Block

| Field    | Value |
| -------- | ----- |
| Format   | Ax    |
| Operands | none  |

**Operation:** Deactivate privilege escalation. Clears the elevation command.

---

#### `retry` — Retry Block

| Field    | Value         |
| -------- | ------------- |
| Format   | ABx           |
| Operands | `R(A), K(Bx)` |

**Operation:** Execute a function repeatedly until success, a predicate passes, or max attempts exhausted.

**Registers:**

- R(A): max attempts (positive integer)
- R(A+1): options (optional — struct with `delay` field)
- Subsequent registers: body function, until predicate (optional), onRetry callback (optional)

**Per-attempt:**

1. Call body with attempt context: `{current, max, remaining, errors}`.
2. On `RuntimeError`: wrap as `StashError`, call onRetry (if not final attempt), continue.
3. Evaluate until predicate (if present): `until(result, attempt)` — must return truthy.
4. Sleep `delay` milliseconds before next attempt (if configured).

**Error:** `RetryExhaustedError` if all attempts fail.

---

#### `timeout` — Timeout Block

| Field    | Value          |
| -------- | -------------- |
| Format   | ABC            |
| Operands | `R(A), R(A+1)` |

**Operation:** Execute the function in R(A+1) with a time limit from R(A).

- R(A): duration (`StashDuration` or numeric milliseconds, must be > 0).
- R(A+1): body function.

Creates a linked cancellation token with the timeout. If the body exceeds the time limit, throws `TimeoutError`. Result stored in R(A).

---

## 6. Inline Caching

Inline caching (IC) is a runtime optimization that caches the result of field lookups at specific call sites. Two instructions use IC: `get.field.ic` and `call.builtin`.

### IC Slot Structure

Each IC slot contains:

| Field         | Type       | Description                                          |
| ------------- | ---------- | ---------------------------------------------------- |
| Guard         | object?    | Cached receiver identity (namespace or struct)       |
| CachedValue   | StashValue | Resolved field value or field index                  |
| State         | byte       | 0 = uninitialized, 1 = monomorphic, 2 = megamorphic  |
| ConstantIndex | ushort     | Field name in constant pool (for slow-path fallback) |

### State Machine

```
                   ┌─────────────────┐
         first     │                 │  guard matches
         access    │  Uninitialized  │──────────────────→ skip (populate on miss)
                   │   (State = 0)   │
                   └────────┬────────┘
                            │ first successful lookup
                            ▼
                   ┌─────────────────┐
                   │                 │  guard matches → fast path (return cached)
                   │  Monomorphic    │
                   │   (State = 1)   │  guard mismatch → transition ↓
                   └────────┬────────┘
                            │ different receiver type
                            ▼
                   ┌─────────────────┐
                   │                 │
                   │  Megamorphic    │  always slow path (no caching)
                   │   (State = 2)   │
                   └─────────────────┘
```

### Guard Types

- **Namespace access:** Guard is the `StashNamespace` object reference. Cache stores the resolved member value (typically a `BuiltInFunction` delegate).
- **Struct field access:** Guard is the `StashStruct` definition reference. Cache stores the field slot index for O(1) array access on instances.

### Companion Words

Several opcodes consume **companion words** — additional 32-bit words that immediately follow the primary instruction in the code array. Companion words are NOT executable instructions; they carry metadata that the opcode's handler reads by advancing IP.

**Key rule:** When walking the code array (for disassembly, verification, or analysis), companion words must be skipped. They will not decode as valid instructions.

#### Opcodes with Companion Words

| Opcode              | Companion Count                 | Companion Encoding       | IP Advancement               |
| ------------------- | ------------------------------- | ------------------------ | ---------------------------- |
| `get.field.ic` (80) | 1                               | IC slot index (u32)      | +2 (instruction + companion) |
| `call.builtin` (81) | 1                               | IC slot index (u32)      | +2 (instruction + companion) |
| `closure` (52)      | N (= sub-chunk's upvalue count) | Upvalue descriptor (u32) | +1+N                         |

#### IC Slot Companion (get.field.ic, call.builtin)

```
Primary:    [op:8][A:8][B:8][C:8]    ← the opcode instruction
Companion:  [        icSlot:32     ]  ← IC slot array index
```

The companion is read by the handler as `frame.Chunk.Code[frame.IP++]`, which:

1. Reads the IC slot index
2. Advances IP past the companion, so the next fetch gets the next real instruction

The IC slot index references `Chunk.ICSlots[icSlot]`. Each IC slot stores:

- `Guard` — object reference for identity check (e.g., the namespace object)
- `CachedValue` — the resolved field/function value
- `State` — 0 (uninitialized), 1 (monomorphic), 2 (megamorphic)
- `ConstantIndex` — constant pool index of the field name (for slow-path fallback)

#### Upvalue Descriptor Companion (closure)

```
Primary:    [op:8][A:8][   Bx:16  ]    ← Closure instruction, Bx = sub-chunk constant index
Word 1:     [isLocal:8][index:8][ unused:16 ]  ← upvalue 0
Word 2:     [isLocal:8][index:8][ unused:16 ]  ← upvalue 1
...
Word N:     [isLocal:8][index:8][ unused:16 ]  ← upvalue N-1
```

Each companion word encodes one upvalue capture:

- **Bits 0–7 (`isLocal`):** 1 = capture a local variable from the immediately enclosing function's register window. 0 = inherit an upvalue from the enclosing closure's upvalue array.
- **Bits 8–15 (`index`):** If `isLocal=1`, the register index in the enclosing frame. If `isLocal=0`, the upvalue array index in the enclosing closure.
- **Bits 16–31:** Unused (zero).

The number of companion words N equals the length of the sub-chunk's `Upvalues` array (`Constants[Bx].Upvalues.Length`).

---

## 7. Constant Pool

Each compiled function (`Chunk`) has a constant pool — an array of `StashValue` entries containing:

- **String literals:** `"hello"`, field names, module paths
- **Numeric literals:** integers and floats that don't fit in immediate operands
- **Metadata records:** `StructMetadata`, `EnumMetadata`, `ImportMetadata`, `CommandMetadata`, `StructInitMetadata`, `DestructureMetadata`, `RetryMetadata`
- **Sub-chunks:** Compiled function bodies referenced by `closure`
- **Null/bool:** Canonical constant values

Constants are **deduplicated** at compile time via a hash map in `ChunkBuilder`. The constant pool is immutable after compilation.

Constants are referenced by:

- `Bx` (16-bit unsigned) in ABx-format instructions — supports up to 65,536 constants.
- `C` (8-bit unsigned) in ABC-format instructions with constant-fused variants — limited to 256 constants.

---

## 8. Disassembly Format

The disassembler produces human-readable output with the following structure:

```
  ADDR:  MNEMONIC            OPERANDS                ; COMMENT
```

### Example

```
.code:
  ; 3: const HOME_DIR: string = env.get("HOME");
  0000:  get.global          r3, [g0]                ; env
  0001:  load.k              r2, k1                  ; "HOME"
  0002:  call.builtin        r1, r3, 1               ; (1 args) [ic:0]
  0004:  move                r0, r1
  0005:  init.const.global   [g1], r0                ; HOME_DIR (const)
```

### Operand Notation

| Notation   | Meaning                     |
| ---------- | --------------------------- |
| `r{N}`     | Register N                  |
| `k{N}`     | Constant pool index N       |
| `[g{N}]`   | Global slot N               |
| `[uv{N}]`  | Upvalue slot N              |
| `.L{N}`    | Label (jump target address) |
| `[ic:{N}]` | Inline cache slot N         |

### Source Mapping

Lines starting with `; N:` are source comments showing the original Stash source line. The disassembler maps each instruction back to its source location via the chunk's `SourceMap`.

### Address Gaps

Instructions that consume companion words (like `call.builtin` and `get.field.ic`) cause visible gaps in the address sequence (e.g., 0002 → 0004). The companion word at address 0003 is not displayed as a separate instruction.

### Labels

Jump targets are displayed as `.L{N}` labels. Both the label and the signed offset are shown:

```
  0010:  jmp.false           r0, .L015               ; +5
  0020:  loop                .L010                    ; -10
```
