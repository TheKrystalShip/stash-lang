# Bytecode VM — Instruction Set Reference

> Generated reference for the Stash bytecode VM instruction set. Opcode names, numeric values,
> categories, and effects are extracted from `Stash.Bytecode/Bytecode/OpCode.cs`; encoding
> formats and table hash metadata come from `Stash.Bytecode`. The generator always overwrites
> this file from source metadata; do not edit it by hand. Run
> `dotnet run --project Stash.Docs/ --bytecode` to regenerate after changing opcodes.
>
> **Companion documents:**
>
> - [Bytecode VM — Binary Format (.stashc)](Bytecode%20VM%20—%20Binary%20Format%20%28.stashc%29.md)
> - [Language Specification](Stash%20—%20Language%20Specification.md)
> - [DAP — Debug Adapter Protocol](DAP%20—%20Debug%20Adapter%20Protocol.md)

| Property             | Value        |
| -------------------- | ------------ |
| Opcode count         | `101`        |
| Numeric range        | `0..100`     |
| Opcode table hash    | `0xD2A262BE` |
| Instruction width    | 32 bits      |
| Register index width | 8 bits       |

---

## Table of Contents

1. [Architecture Overview](#1-architecture-overview)
2. [Instruction Encoding](#2-instruction-encoding)
3. [Register Model](#3-register-model)
4. [Notation Conventions](#4-notation-conventions)
5. [Instruction Reference](#5-instruction-reference)
   - [5.1 Loads & Constants](#51-loads--constants)
   - [5.2 Global Variables](#52-global-variables)
   - [5.3 Upvalues](#53-upvalues)
   - [5.4 Arithmetic](#54-arithmetic)
   - [5.5 Bitwise](#55-bitwise)
   - [5.6 Comparison (produce bool in R(A))](#56-comparison-produce-bool-in-ra)
   - [5.7 Logic](#57-logic)
   - [5.8 Control Flow](#58-control-flow)
   - [5.9 Iteration](#59-iteration)
   - [5.10 Tables & Fields](#510-tables--fields)
   - [5.11 Collections](#511-collections)
   - [5.12 Closures & Types](#512-closures--types)
   - [5.13 Error Handling](#513-error-handling)
   - [5.14 Type Declarations](#514-type-declarations)
   - [5.15 Shell](#515-shell)
   - [5.16 Modules](#516-modules)
   - [5.17 Strings](#517-strings)
   - [5.18 Misc](#518-misc)
   - [5.19 Specialized Iteration (compile-time)](#519-specialized-iteration-compile-time)
   - [5.20 Constant Fusion](#520-constant-fusion)
   - [5.21 Typed Arrays](#521-typed-arrays)
   - [5.22 Defer](#522-defer)
   - [5.23 Exception Type Matching](#523-exception-type-matching)
   - [5.24 File-Based Mutual Exclusion (Lock)](#524-file-based-mutual-exclusion-lock)
   - [5.25 Global Bindings](#525-global-bindings)
   - [5.26 Iterator Cleanup](#526-iterator-cleanup)
   - [5.27 Streaming Pipe Chains](#527-streaming-pipe-chains)
6. [Companion Words](#6-companion-words)
7. [Compatibility](#7-compatibility)
8. [Change Rules](#8-change-rules)

---

## 1. Architecture Overview

The Stash bytecode VM is a register-based virtual machine that executes fixed-width
32-bit instruction words. A compiled chunk contains an instruction stream, constant pool,
source maps, global metadata, closure metadata, and inline-cache metadata.

| Physical CPU concept | Stash VM equivalent                                                         |
| -------------------- | --------------------------------------------------------------------------- |
| Instruction set      | `101` opcodes                                                               |
| Registers            | Virtual registers `r0..rN` in the current call frame                        |
| Machine code         | `uint` instruction words                                                    |
| Program counter      | `IP` per call frame                                                         |
| Call stack           | `CallFrame[]` with base slot, instruction pointer, chunk, and closure state |
| Constant memory      | Per-chunk constant pool `K(i)`                                              |
| Inline caches        | `ICSlot` entries used by selected field and built-in call opcodes           |

The VM dispatch loop decodes an opcode from the low byte of each instruction word and
dispatches to the matching handler. Some instructions consume companion words after the
primary instruction; companion words are part of the instruction stream but do not contain
an opcode in their low byte.

## 2. Instruction Encoding

All instructions use one 32-bit little-endian word in memory and on disk. The low byte
always stores the opcode value.

| Format | Layout                  | Operand range                   | Typical use                             |
| ------ | ----------------------- | ------------------------------- | --------------------------------------- |
| `ABC`  | `[op:8][A:8][B:8][C:8]` | `A`, `B`, `C`: `0..255`         | Registers and small immediates          |
| `ABx`  | `[op:8][A:8][Bx:16]`    | `A`: `0..255`, `Bx`: `0..65535` | Constant pool and metadata indexes      |
| `AsBx` | `[op:8][A:8][sBx:16]`   | `sBx`: `-32767..32768`          | Relative jumps and signed immediates    |
| `Ax`   | `[op:8][Ax:24]`         | `Ax`: `0..16777215`             | Large payload without register operands |

Signed `sBx` fields are bias-encoded with `32767`.
Encoding and decoding helpers live in `Instruction`.

## 3. Register Model

Each call frame owns a register window over the VM stack. `R(i)` means register `i` in
the current frame, implemented as `stack[frame.BaseSlot + i]`.

- Function parameters and locals occupy low-numbered registers.
- Temporary expression values occupy compiler-assigned registers above locals.
- Function calls place the callee in `R(A)` and arguments in following registers.
- Return values overwrite the caller's callee register.

## 4. Notation Conventions

| Notation       | Meaning                                            |
| -------------- | -------------------------------------------------- |
| `R(A)`         | Register `A` in the current frame                  |
| `K(i)`         | Constant pool entry `i`                            |
| `G(i)`         | Global slot `i`                                    |
| `UV(i)`        | Upvalue `i` in the current closure                 |
| `IP`           | Instruction pointer, measured in instruction words |
| companion word | Extra `uint` consumed after an opcode word         |

## 5. Instruction Reference

This section is generated from the `OpCode` enum. The **Encoding** column is the
VM's decoded operand format. The **Operands** column is the operand shape documented
on the opcode itself, including companion-word notes where applicable.

### 5.1 Loads & Constants

| Value | Opcode     | Encoding | Operands | Effect                                             |
| ----: | ---------- | -------- | -------- | -------------------------------------------------- |
|   `0` | `LoadK`    | `ABx`    | `ABx`    | R(A) = K(Bx) — load constant from pool.            |
|   `1` | `LoadNull` | `ABC`    | `ABC`    | R(A) = null.                                       |
|   `2` | `LoadBool` | `ABC`    | `ABC`    | R(A) = (B != 0); if C != 0, skip next instruction. |
|   `3` | `Move`     | `ABC`    | `ABC`    | R(A) = R(B) — copy register.                       |

### 5.2 Global Variables

| Value | Opcode            | Encoding | Operands | Effect                             |
| ----: | ----------------- | -------- | -------- | ---------------------------------- |
|   `4` | `GetGlobal`       | `ABx`    | `ABx`    | R(A) = Globals[Bx].                |
|   `5` | `SetGlobal`       | `ABx`    | `ABx`    | Globals[Bx] = R(A).                |
|   `6` | `InitConstGlobal` | `ABx`    | `ABx`    | Globals[Bx] = R(A), mark as const. |

### 5.3 Upvalues

| Value | Opcode       | Encoding | Operands | Effect                        |
| ----: | ------------ | -------- | -------- | ----------------------------- |
|   `7` | `GetUpval`   | `ABC`    | `ABC`    | R(A) = Upvalues[B].           |
|   `8` | `SetUpval`   | `ABC`    | `ABC`    | Upvalues[B] = R(A).           |
|   `9` | `CloseUpval` | `ABC`    | `ABC`    | Close upvalue for register A. |

### 5.4 Arithmetic

| Value | Opcode | Encoding | Operands | Effect                                    |
| ----: | ------ | -------- | -------- | ----------------------------------------- |
|  `10` | `Add`  | `ABC`    | `ABC`    | R(A) = R(B) + R(C).                       |
|  `11` | `Sub`  | `ABC`    | `ABC`    | R(A) = R(B) - R(C).                       |
|  `12` | `Mul`  | `ABC`    | `ABC`    | R(A) = R(B) \* R(C).                      |
|  `13` | `Div`  | `ABC`    | `ABC`    | R(A) = R(B) / R(C).                       |
|  `14` | `Mod`  | `ABC`    | `ABC`    | R(A) = R(B) % R(C).                       |
|  `15` | `Pow`  | `ABC`    | `ABC`    | R(A) = R(B) \*\* R(C).                    |
|  `16` | `Neg`  | `ABC`    | `ABC`    | R(A) = -R(B).                             |
|  `17` | `AddI` | `AsBx`   | `AsBx`   | R(A) = R(A) + sBx — add signed immediate. |

### 5.5 Bitwise

| Value | Opcode | Encoding | Operands | Effect               |
| ----: | ------ | -------- | -------- | -------------------- |
|  `18` | `BAnd` | `ABC`    | `ABC`    | R(A) = R(B) & R(C).  |
|  `19` | `BOr`  | `ABC`    | `ABC`    | R(A) = R(B) \| R(C). |
|  `20` | `BXor` | `ABC`    | `ABC`    | R(A) = R(B) ^ R(C).  |
|  `21` | `BNot` | `ABC`    | `ABC`    | R(A) = ~R(B).        |
|  `22` | `Shl`  | `ABC`    | `ABC`    | R(A) = R(B) << R(C). |
|  `23` | `Shr`  | `ABC`    | `ABC`    | R(A) = R(B) >> R(C). |

### 5.6 Comparison (produce bool in R(A))

| Value | Opcode | Encoding | Operands | Effect                 |
| ----: | ------ | -------- | -------- | ---------------------- |
|  `24` | `Eq`   | `ABC`    | `ABC`    | R(A) = (R(B) == R(C)). |
|  `25` | `Ne`   | `ABC`    | `ABC`    | R(A) = (R(B) != R(C)). |
|  `26` | `Lt`   | `ABC`    | `ABC`    | R(A) = (R(B) < R(C)).  |
|  `27` | `Le`   | `ABC`    | `ABC`    | R(A) = (R(B) <= R(C)). |
|  `28` | `Gt`   | `ABC`    | `ABC`    | R(A) = (R(B) > R(C)).  |
|  `29` | `Ge`   | `ABC`    | `ABC`    | R(A) = (R(B) >= R(C)). |

### 5.7 Logic

| Value | Opcode    | Encoding | Operands | Effect                                                               |
| ----: | --------- | -------- | -------- | -------------------------------------------------------------------- |
|  `30` | `Not`     | `ABC`    | `ABC`    | R(A) = !IsTruthy(R(B)).                                              |
|  `31` | `TestSet` | `ABC`    | `ABC`    | if IsTruthy(R(B)) == C then R(A) = R(B) else skip next. For &&/\|\|. |
|  `32` | `Test`    | `ABC`    | `ABC`    | if IsTruthy(R(A)) != C then skip next instruction.                   |

### 5.8 Control Flow

| Value | Opcode     | Encoding | Operands | Effect                                                    |
| ----: | ---------- | -------- | -------- | --------------------------------------------------------- |
|  `33` | `Jmp`      | `AsBx`   | `AsBx`   | IP += sBx — unconditional jump.                           |
|  `34` | `JmpFalse` | `AsBx`   | `AsBx`   | if !IsTruthy(R(A)) then IP += sBx.                        |
|  `35` | `JmpTrue`  | `AsBx`   | `AsBx`   | if IsTruthy(R(A)) then IP += sBx.                         |
|  `36` | `Loop`     | `AsBx`   | `AsBx`   | IP += sBx — backward jump with cancellation check.        |
|  `37` | `Call`     | `ABC`    | `ABC`    | Call R(A) with C args starting at R(A+1); result in R(A). |
|  `38` | `Return`   | `ABC`    | `ABC`    | Return R(A). B=0 means return null.                       |

### 5.9 Iteration

| Value | Opcode     | Encoding | Operands | Effect                                                                   |
| ----: | ---------- | -------- | -------- | ------------------------------------------------------------------------ |
|  `39` | `ForPrep`  | `AsBx`   | `AsBx`   | Numeric for init: R(A) -= R(A+2); IP += sBx.                             |
|  `40` | `ForLoop`  | `AsBx`   | `AsBx`   | R(A) += R(A+2); if R(A) <= R(A+1) then { IP += sBx; R(A+3) = R(A) }.     |
|  `41` | `IterPrep` | `ABC`    | `ABC`    | Create iterator from R(A), store state in R(A)..R(A+2).                  |
|  `42` | `IterLoop` | `AsBx`   | `AsBx`   | Advance iterator; if exhausted, continue; else set values and IP += sBx. |

### 5.10 Tables & Fields

| Value | Opcode     | Encoding | Operands | Effect                                                   |
| ----: | ---------- | -------- | -------- | -------------------------------------------------------- |
|  `43` | `GetTable` | `ABC`    | `ABC`    | R(A) = R(B)[R(C)] — array index or dict key lookup.      |
|  `44` | `SetTable` | `ABC`    | `ABC`    | R(A)[R(B)] = R(C) — array/dict element store.            |
|  `45` | `GetField` | `ABC`    | `ABC`    | R(A) = R(B).K(C) — field access by constant key.         |
|  `46` | `SetField` | `ABC`    | `ABC`    | R(A).K(B) = R(C) — field store by constant key.          |
|  `47` | `Self`     | `ABC`    | `ABC`    | R(A+1) = R(B); R(A) = R(B)[K(C)] — method lookup + self. |

### 5.11 Collections

| Value | Opcode     | Encoding | Operands | Effect                                                         |
| ----: | ---------- | -------- | -------- | -------------------------------------------------------------- |
|  `48` | `NewArray` | `ABC`    | `ABC`    | R(A) = new array with B elements from R(A+1)..R(A+B).          |
|  `49` | `NewDict`  | `ABC`    | `ABC`    | R(A) = new dict with B key-value pairs from R(A+1)..R(A+2\*B). |
|  `50` | `NewRange` | `ABC`    | `ABC`    | R(A) = range(R(B), R(C)).                                      |
|  `51` | `Spread`   | `ABC`    | `ABC`    | Expand R(B) into sequential registers starting at R(A).        |

### 5.12 Closures & Types

| Value | Opcode      | Encoding | Operands | Effect                                                                  |
| ----: | ----------- | -------- | -------- | ----------------------------------------------------------------------- |
|  `52` | `Closure`   | `ABx`    | `ABx`    | R(A) = new closure from Prototype[Bx], followed by upvalue descriptors. |
|  `53` | `NewStruct` | `ABC`    | `ABC`    | R(A) = new instance of struct K(B) with C field values from R(A+1).     |
|  `54` | `TypeOf`    | `ABC`    | `ABC`    | R(A) = typeof(R(B)) as string.                                          |
|  `55` | `Is`        | `ABC`    | `ABC`    | R(A) = (R(B) is type K(C)).                                             |

### 5.13 Error Handling

| Value | Opcode     | Encoding | Operands | Effect                                                        |
| ----: | ---------- | -------- | -------- | ------------------------------------------------------------- |
|  `56` | `TryBegin` | `ABx`    | `ABx`    | Push exception handler; catch at IP + Bx; error value → R(A). |
|  `57` | `TryEnd`   | `Ax`     | `Ax`     | Pop exception handler (no operands needed, Ax unused).        |
|  `58` | `Throw`    | `ABC`    | `ABC`    | Throw R(A) as error.                                          |
|  `59` | `TryExpr`  | `ABC`    | `ABC`    | R(A) = try evaluate R(B); null on error.                      |

### 5.14 Type Declarations

| Value | Opcode       | Encoding | Operands | Effect                                                                       |
| ----: | ------------ | -------- | -------- | ---------------------------------------------------------------------------- |
|  `60` | `StructDecl` | `ABx`    | `ABx`    | R(A) = declare struct with metadata K(Bx), methods from following registers. |
|  `61` | `EnumDecl`   | `ABx`    | `ABx`    | R(A) = declare enum with metadata K(Bx).                                     |
|  `62` | `IfaceDecl`  | `ABx`    | `ABx`    | R(A) = declare interface with metadata K(Bx).                                |
|  `63` | `Extend`     | `ABx`    | `ABx`    | Extend type with metadata K(Bx), methods from registers.                     |

### 5.15 Shell

| Value | Opcode      | Encoding | Operands                  | Effect                                                                                                                                   |
| ----: | ----------- | -------- | ------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------- |
|  `64` | `Command`   | `ABC`    | `ABC`                     | R(A) = execute command with B parts from R(A+1)..R(A+B).                                                                                 |
|  `65` | `PipeChain` | `ABC`    | `ABC + B companion words` | execute streaming pipe chain. A=dest, B=stageCount, C=partsBase. Each companion word: bits15-8=partCount, bits7-0=flags (bit0=isStrict). |
|  `66` | `Redirect`  | `ABC`    | `ABC`                     | Redirect R(A) stream (B flags) to file R(C).                                                                                             |

### 5.16 Modules

| Value | Opcode     | Encoding | Operands | Effect                                         |
| ----: | ---------- | -------- | -------- | ---------------------------------------------- |
|  `67` | `Import`   | `ABx`    | `ABx`    | R(A) = import module with metadata K(Bx).      |
|  `68` | `ImportAs` | `ABx`    | `ABx`    | R(A) = import module as alias, metadata K(Bx). |

### 5.17 Strings

| Value | Opcode        | Encoding | Operands | Effect                                          |
| ----: | ------------- | -------- | -------- | ----------------------------------------------- |
|  `69` | `Interpolate` | `ABC`    | `ABC`    | R(A) = interpolate B parts from R(A+1)..R(A+B). |

### 5.18 Misc

| Value | Opcode         | Encoding | Operands        | Effect                                                                                                               |
| ----: | -------------- | -------- | --------------- | -------------------------------------------------------------------------------------------------------------------- |
|  `70` | `In`           | `ABC`    | `ABC`           | R(A) = R(B) in R(C) — containment check.                                                                             |
|  `71` | `Switch`       | `ABx`    | `ABx`           | Switch on R(A) with jump table K(Bx).                                                                                |
|  `72` | `Destructure`  | `ABx`    | `ABx`           | Destructure R(A) per metadata K(Bx) into registers.                                                                  |
|  `73` | `ElevateBegin` | `ABC`    | `ABC`           | R(A) = begin elevation from R(B).                                                                                    |
|  `74` | `ElevateEnd`   | `Ax`     | `Ax`            | End elevation.                                                                                                       |
|  `75` | `Retry`        | `ABx`    | `ABx`           | Retry block with metadata K(Bx), body/until/onRetry from registers.                                                  |
|  `76` | `Timeout`      | `ABx`    | `ABx`           | Timeout block. R(A)=duration, body closure at R(A+1). Returns result in R(A).                                        |
|  `77` | `Await`        | `ABC`    | `ABC`           | R(A) = await R(B).                                                                                                   |
|  `78` | `CallSpread`   | `ABC`    | `ABC`           | Call R(A) with spread arguments.                                                                                     |
|  `79` | `CheckNumeric` | `ABC`    | `ABC`           | Check that R(A) is numeric, throw if not.                                                                            |
|  `80` | `GetFieldIC`   | `ABC`    | `ABC+companion` | R(A) = R(B).K(C) with inline cache; companion word = IC slot index.                                                  |
|  `81` | `CallBuiltIn`  | `ABC`    | `ABC+companion` | Fused GetField+Call for namespace built-ins; R(A) = R(B).K[ic.ConstantIndex](<R(A+1)..R(A+C)>); companion = IC slot. |

### 5.19 Specialized Iteration (compile-time)

| Value | Opcode      | Encoding | Operands | Effect                                                                                                                 |
| ----: | ----------- | -------- | -------- | ---------------------------------------------------------------------------------------------------------------------- |
|  `82` | `ForPrepII` | `AsBx`   | `AsBx`   | Integer-specialized ForPrep. R(A) -= R(A+2); IP += sBx. Skips type checks when counter/step are compile-time integers. |
|  `83` | `ForLoopII` | `AsBx`   | `AsBx`   | Integer-specialized ForLoop. Guard-free: R(A) += R(A+2); if in-bounds: IP += sBx; R(A+3) = R(A).                       |

### 5.20 Constant Fusion

| Value | Opcode | Encoding | Operands | Effect                                                            |
| ----: | ------ | -------- | -------- | ----------------------------------------------------------------- |
|  `84` | `AddK` | `ABC`    | `ABC`    | R(A) = R(B) + K(C) — add constant from pool.                      |
|  `85` | `SubK` | `ABC`    | `ABC`    | R(A) = R(B) - K(C) — subtract constant from pool.                 |
|  `86` | `EqK`  | `ABC`    | `ABC`    | R(A) = (R(B) == K(C)) — equality with constant from pool.         |
|  `87` | `NeK`  | `ABC`    | `ABC`    | R(A) = (R(B) != K(C)) — inequality with constant from pool.       |
|  `88` | `LtK`  | `ABC`    | `ABC`    | R(A) = (R(B) < K(C)) — less-than with constant from pool.         |
|  `89` | `LeK`  | `ABC`    | `ABC`    | R(A) = (R(B) <= K(C)) — less-or-equal with constant from pool.    |
|  `90` | `GtK`  | `ABC`    | `ABC`    | R(A) = (R(B) > K(C)) — greater-than with constant from pool.      |
|  `91` | `GeK`  | `ABC`    | `ABC`    | R(A) = (R(B) >= K(C)) — greater-or-equal with constant from pool. |

### 5.21 Typed Arrays

| Value | Opcode      | Encoding | Operands | Effect                                               |
| ----: | ----------- | -------- | -------- | ---------------------------------------------------- |
|  `92` | `TypedWrap` | `ABx`    | `ABx`    | R(A) = TypedArray(elementType=K(Bx), elements=R(A)). |

### 5.22 Defer

| Value | Opcode  | Encoding | Operands | Effect                                                                  |
| ----: | ------- | -------- | -------- | ----------------------------------------------------------------------- |
|  `93` | `Defer` | `ABC`    | `A`      | Push deferred closure R(A) onto the current frame's defer stack (LIFO). |

### 5.23 Exception Type Matching

| Value | Opcode       | Encoding | Operands | Effect                                                                                     |
| ----: | ------------ | -------- | -------- | ------------------------------------------------------------------------------------------ |
|  `94` | `CatchMatch` | `ABx`    | `ABx`    | Check if caught error in R(A) matches type names K(Bx); on match, skip the following jump. |
|  `95` | `Rethrow`    | `ABC`    | `A`      | Re-throw the original RuntimeError that was caught into R(A)'s handler register.           |

### 5.24 File-Based Mutual Exclusion (Lock)

| Value | Opcode      | Encoding | Operands | Effect                                                                                                                          |
| ----: | ----------- | -------- | -------- | ------------------------------------------------------------------------------------------------------------------------------- |
|  `96` | `LockBegin` | `ABC`    | `ABC`    | Acquire exclusive file lock. A=errReg (scratch), B=pathReg, C=constIdx for LockMetadata. R(B+1)=waitOption, R(B+2)=staleOption. |
|  `97` | `LockEnd`   | `Ax`     | `Ax`     | Release the top lock from VMContext.ActiveLocks. No operands (A=0).                                                             |

### 5.25 Global Bindings

| Value | Opcode        | Encoding | Operands | Effect                                                            |
| ----: | ------------- | -------- | -------- | ----------------------------------------------------------------- |
|  `98` | `UnsetGlobal` | `Ax`     | `Ax`     | Remove the global binding at slot Ax from the globals dictionary. |

### 5.26 Iterator Cleanup

| Value | Opcode      | Encoding | Operands | Effect                                                                                  |
| ----: | ----------- | -------- | -------- | --------------------------------------------------------------------------------------- |
|  `99` | `IterClose` | `ABC`    | `A`      | Dispose iterator at R(A) if IDisposable; clear R(A) to null. Used at for-in loop exits. |

### 5.27 Streaming Pipe Chains

| Value | Opcode              | Encoding | Operands                                  | Effect                                                                                                                                                                                                                                                         |
| ----: | ------------------- | -------- | ----------------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `100` | `StreamingPipeline` | `ABC`    | `ABC + B companion words (one per stage)` | A=destReg, B=stageCount, C=partsBase. Each companion word: bits 15-8 = partCount, bits 7-0 = flags (bit 0x01 = strict on the last stage). Spawns all stages with intermediate stages captured-piped, exposes the last stage's stdout via a multi-stage handle. |

## 6. Companion Words

Most opcodes consume exactly one instruction word. The following opcodes consume
additional companion words immediately after the primary opcode word:

| Opcode              | Companion contract                                                                                                          |
| ------------------- | --------------------------------------------------------------------------------------------------------------------------- |
| `Closure`           | One companion word per upvalue descriptor in the target function prototype.                                                 |
| `PipeChain`         | `B` companion words, one per pipeline stage. Bits `15..8` store part count; bits `7..0` store stage flags.                  |
| `StreamingPipeline` | `B` companion words, one per pipeline stage. Bits `15..8` store part count; bit `0x01` marks strict mode on the last stage. |
| `GetFieldIC`        | One companion word storing the inline-cache slot index.                                                                     |
| `CallBuiltIn`       | One companion word storing the inline-cache slot index.                                                                     |

Companion words are serialized in the code array and count toward bytecode offsets.

## 7. Compatibility

Serialized `.stashc` files store an opcode table hash in the binary header. The hash
is computed from opcode names and numeric values. A reader rejects bytecode when its
computed hash does not match the file header.

Changing an opcode name, numeric value, or order is therefore a bytecode compatibility
change. Adding an opcode also changes the hash and requires regenerating this document.

## 8. Change Rules

When changing the instruction set:

- Add or update the opcode in `Stash.Bytecode/Bytecode/OpCode.cs`.
- Keep the XML summary in the form `FORMAT: effect`, for example `ABC: R(A) = R(B) + R(C)`.
- Place the opcode under the correct `// === Category ===` comment.
- Update `OpCodeInfo.GetFormat` when the encoded operand format is not the default `ABC`.
- Update verifier, disassembler, optimizer, serializer, and VM dispatch behavior as required.
- Run `dotnet run --project Stash.Docs/ --bytecode` and commit the regenerated Markdown.
- Add or update tests for execution, verification, disassembly, and generated documentation.
