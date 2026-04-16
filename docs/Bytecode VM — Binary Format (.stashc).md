# Bytecode VM — Binary Format (.stashc)

## Overview

The `.stashc` format is a binary serialization of compiled Stash bytecode chunks. It contains a fixed-size header followed by a serialized chunk tree (top-level chunk + nested sub-chunks for closures/functions), optional debug info, and optional embedded source text. All multi-byte integers are **little-endian**.

## File Structure

```
┌──────────────────────────┐
│  Header (32 bytes)       │
├──────────────────────────┤
│  Top-Level Chunk         │
│  ├── Chunk Metadata      │
│  ├── Code Array          │
│  ├── Constant Pool       │  ← may contain nested Chunks (tag 5)
│  ├── Upvalue Descriptors │
│  ├── Global Name Table   │
│  ├── IC Slot Metadata    │
│  ├── Const Global Inits  │
│  └── Debug Info (opt)    │
├──────────────────────────┤
│  Embedded Source (opt)   │
└──────────────────────────┘
```

## 1. Header (32 bytes)

| Offset | Size | Field           | Description                                                                                                                              |
| ------ | ---- | --------------- | ---------------------------------------------------------------------------------------------------------------------------------------- |
| 0x00   | 4    | Magic           | `0x53 0x54 0x42 0x43` ("STBC") — identifies the file format                                                                              |
| 0x04   | 2    | FormatVersion   | `u16 LE` — currently `1`                                                                                                                 |
| 0x06   | 1    | Flags           | Bit flags: bit 0 = HasDebugInfo, bit 1 = Optimized, bit 2 = HasEmbeddedSource                                                            |
| 0x07   | 1    | Reserved        | Always `0x00`, reserved for future use                                                                                                   |
| 0x08   | 4    | CompilerHash    | `u32 LE` — hash of the compiler assembly version (informational, not validated on read)                                                  |
| 0x0C   | 4    | OpCodeTableHash | `u32 LE` — FNV-1a hash of all OpCode names + numeric values. Must match the reader's computed hash; mismatch means incompatible bytecode |
| 0x10   | 16   | SourceSHA256    | First 16 bytes of the SHA-256 hash of the original source text (all zeros if no source provided). Used for cache invalidation            |

**Total:** 32 bytes.

### OpCode Table Hash Algorithm

FNV-1a (32-bit), iterating over `Enum.GetValues<OpCode>()`:

```
hash = 2166136261 (FNV offset basis)
for each opcode:
    for each char c in opcode.ToString():
        hash ^= (byte)c
        hash *= 16777619 (FNV prime)
    hash ^= (uint)(byte)opcode   // fold in numeric value
    hash *= 16777619
```

## 2. Chunk Serialization

A chunk represents a single compiled function (or the top-level script). Chunks can be nested — a `closure` instruction's constant pool entry is itself a serialized chunk.

### 2.1 Chunk Metadata

| Size | Field           | Description                                                                       |
| ---- | --------------- | --------------------------------------------------------------------------------- |
| 2+N  | Name            | Nullable string: `u16 LE` length + UTF-8 bytes. `0xFFFF` = null (anonymous chunk) |
| 2    | Arity           | `u16 LE` — number of declared parameters                                          |
| 2    | MinArity        | `u16 LE` — minimum args (accounts for default params)                             |
| 2    | MaxRegs         | `u16 LE` — total register slots used by this function                             |
| 2    | GlobalSlotCount | `u16 LE` — total global variable slots                                            |
| 1    | ChunkFlags      | Bit 0: IsAsync, Bit 1: HasRestParam, Bit 2: MayHaveCapturedLocals                 |

### 2.2 Code Array

| Size | Field        | Description                                                                                 |
| ---- | ------------ | ------------------------------------------------------------------------------------------- |
| 4    | CodeLength   | `u32 LE` — number of instruction words (NOT byte count). Max: 16,777,216 (16M instructions) |
| 4×N  | Instructions | `u32 LE` each — the instruction stream (includes both opcodes and companion words)          |

### 2.3 Constant Pool

| Size | Field   | Description                                        |
| ---- | ------- | -------------------------------------------------- |
| 2    | Count   | `u16 LE` — number of constant entries (max 65,535) |
| var  | Entries | Tagged constant values (see below)                 |

Each constant entry starts with a **1-byte type tag** followed by tag-specific payload:

| Tag | Type                | Payload                                                                                                                                                                                                              |
| --- | ------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| 0   | Null                | (none)                                                                                                                                                                                                               |
| 1   | Bool                | `u8`: 0=false, 1=true                                                                                                                                                                                                |
| 2   | Int                 | `i64 LE` (8 bytes)                                                                                                                                                                                                   |
| 3   | Float               | `i64 LE` (IEEE 754 double, bit-cast to i64)                                                                                                                                                                          |
| 4   | String              | `u32 LE` byte length + UTF-8 bytes                                                                                                                                                                                   |
| 5   | Chunk               | Recursive chunk serialization (same format as Section 2)                                                                                                                                                             |
| 6   | CommandMetadata     | `u16 LE` PartCount + `u8` IsPassthrough + `u8` IsStrict                                                                                                                                                              |
| 7   | StructMetadata      | String Name + StringArray Fields + StringArray MethodNames + StringArray InterfaceNames                                                                                                                              |
| 8   | EnumMetadata        | String Name + StringArray Members                                                                                                                                                                                    |
| 9   | InterfaceMetadata   | String Name + `u16` field count + [String Name + NullableString TypeHint]× + `u16` method count + [String Name + `u16` Arity + StringArray ParamNames + NullableStringArray ParamTypes + NullableString ReturnType]× |
| 10  | ExtendMetadata      | String TypeName + StringArray MethodNames + `u8` IsBuiltIn                                                                                                                                                           |
| 11  | ImportMetadata      | StringArray Names                                                                                                                                                                                                    |
| 12  | ImportAsMetadata    | String AliasName                                                                                                                                                                                                     |
| 13  | DestructureMetadata | String Kind + StringArray Names + NullableString RestName + `u8` IsConst                                                                                                                                             |
| 14  | RetryMetadata       | `u16 LE` OptionCount + `u8` HasUntilClause + `u8` HasOnRetryClause + `u8` OnRetryIsReference                                                                                                                         |
| 15  | StructInitMetadata  | String TypeName + `u8` HasTypeReg + StringArray FieldNames                                                                                                                                                           |
| 16  | Byte                | `u8` value                                                                                                                                                                                                           |

**String encoding helpers used throughout:**

| Pattern                       | Format                                                              |
| ----------------------------- | ------------------------------------------------------------------- |
| String (non-null, u16 length) | `u16 LE` byte length + UTF-8 bytes                                  |
| NullableString                | Same as String, but `0xFFFF` for length = null (no payload bytes)   |
| String (u32 length)           | `u32 LE` byte length + UTF-8 bytes (used for constant pool strings) |
| StringArray                   | `u16 LE` count + count × String(u16)                                |
| NullableStringArray           | `u16 LE` count + count × NullableString                             |

### 2.4 Upvalue Descriptors

| Size | Field       | Description                                              |
| ---- | ----------- | -------------------------------------------------------- |
| 1    | Count       | `u8` — number of upvalues (max 255)                      |
| 2×N  | Descriptors | `u8` Index + `u8` IsLocal (0=inherited, 1=local capture) |

### 2.5 Global Name Table

| Size | Field | Description                                           |
| ---- | ----- | ----------------------------------------------------- |
| 2    | Count | `u16 LE` — number of entries (0 means no table)       |
| var  | Names | count × String(u16) — maps slot index → variable name |

### 2.6 IC Slot Metadata

| Size | Field           | Description                                                  |
| ---- | --------------- | ------------------------------------------------------------ |
| 2    | Count           | `u16 LE` — number of inline cache slots                      |
| 2×N  | ConstantIndices | `u16 LE` each — constant pool index of the cached field name |

IC slots are allocated at compile time. At runtime, the IC state (Guard, CachedValue, State) is mutable but not serialized — slots are re-initialized to state 0 (uninitialized) on deserialization.

### 2.7 Const Global Initializations

| Size | Field | Description                                                                                        |
| ---- | ----- | -------------------------------------------------------------------------------------------------- |
| 2    | Count | `u16 LE` — number of const global init pairs                                                       |
| 4×N  | Pairs | `u16 LE` Slot + `u16 LE` ConstIndex — records which global slots hold compile-time constant values |

### 2.8 Debug Info (conditional)

Only present when the `HasDebugInfo` flag (bit 0) is set in the file header. The debug info flag applies to all chunks in the file (including nested sub-chunks).

#### Source Map Entries

| Size | Field   | Description                                                                                                         |
| ---- | ------- | ------------------------------------------------------------------------------------------------------------------- |
| 4    | Count   | `u32 LE` — number of source map entries                                                                             |
| 12×N | Entries | Per entry: `u32` BytecodeOffset + `u16` FileIndex + `u16` StartLine + `u16` StartCol + `u16` EndLine + `u16` EndCol |

#### Source File Table

| Size | Field | Description                                                           |
| ---- | ----- | --------------------------------------------------------------------- |
| 2    | Count | `u16 LE` — number of unique source file paths                         |
| var  | Paths | count × String(u16) — deduplicated file paths referenced by FileIndex |

#### Local Variable Names

| Size | Field | Description                                                            |
| ---- | ----- | ---------------------------------------------------------------------- |
| 2    | Count | `u16 LE` — number of local names (maps register index → variable name) |
| var  | Names | count × NullableString — name for each register slot                   |

#### Local Const Flags

| Size | Field | Description                        |
| ---- | ----- | ---------------------------------- |
| 2    | Count | `u16 LE` — number of entries       |
| N    | Flags | `u8` per entry: 0=mutable, 1=const |

#### Upvalue Names

| Size | Field | Description                                         |
| ---- | ----- | --------------------------------------------------- |
| 1    | Count | `u8` — number of upvalue names                      |
| var  | Names | count × NullableString — name for each upvalue slot |

## 3. Embedded Source (conditional)

Only present when the `HasEmbeddedSource` flag (bit 2) is set in the file header. Appears after the complete chunk serialization.

| Size | Field  | Description                                                         |
| ---- | ------ | ------------------------------------------------------------------- |
| 4    | Length | `u32 LE` — byte length of the UTF-8 encoded source text (0 if null) |
| N    | Source | UTF-8 bytes                                                         |

## 4. Validation on Read

The reader validates:

- Magic bytes match `STBC`
- Format version matches (currently `1`)
- OpCode table hash matches the current build's computed hash
- Code length does not exceed 16 MB
- String lengths do not exceed 16 MB
- All tag values in the constant pool are recognized (0–16)

## 5. Compatibility

The `.stashc` format is versioned at two levels:

1. **Format version** (`u16` at offset 0x04): Bumped when the binary layout itself changes (new sections, reordered fields, etc.). A version mismatch is a hard error.
2. **OpCode table hash** (`u32` at offset 0x0C): Changes whenever opcodes are added, removed, renamed, or renumbered. A hash mismatch means the bytecode uses a different instruction set and cannot be executed.

Both must match for a `.stashc` file to be loadable.
