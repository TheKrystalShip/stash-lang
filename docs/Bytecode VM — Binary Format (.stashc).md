# Bytecode VM - Binary Format (.stashc)

> **Status:** Stable binary format reference
> **Audience:** Compiler maintainers, tool authors that read or write `.stashc` files, and contributors changing the on-disk bytecode layout.
> **Purpose:** Defines the exact byte layout of a serialized Stash bytecode file, the validation rules a loader must apply, and the compatibility envelope the format guarantees.
>
> **Companion documents:**
>
> - [Bytecode VM - Instruction Set Reference](Bytecode%20VM%20—%20Instruction%20Set%20Reference.md) - opcode catalog, instruction encoding, and table hash.
> - [Language Specification](Stash%20—%20Language%20Specification.md) - language syntax and runtime semantics the bytecode encodes.
> - [PKG - Package Manager CLI](PKG%20—%20Package%20Manager%20CLI.md) - how compiled artifacts are packaged and distributed.

A `.stashc` file is the serialized form of a compiled Stash bytecode tree. It contains a fixed-size header followed by a top-level chunk (with recursively nested chunks for functions and closures), an optional stdlib manifest, and an optional copy of the original source text. The on-disk encoding is the boundary between the compiler and the loader and is the only contract this document covers; VM execution semantics belong to the [Instruction Set Reference](Bytecode%20VM%20—%20Instruction%20Set%20Reference.md).

All multi-byte integers are **little-endian**. All strings are UTF-8.

| Property               | Value                                            |
| ---------------------- | ------------------------------------------------ |
| File magic             | `0x53 0x54 0x42 0x43` (`STBC`)                   |
| Current format version | `1`                                              |
| Endianness             | Little-endian                                    |
| String encoding        | UTF-8                                            |
| Max code length        | 16,777,216 instruction words (64 MB code stream) |
| Max string length      | 16 MB                                            |
| Max constants/chunk    | 65,535                                           |
| Max upvalues/chunk     | 255                                              |

## 1. File Layout

```
┌──────────────────────────┐
│  Header (32 bytes)       │
├──────────────────────────┤
│  Top-Level Chunk         │
│  ├── Chunk Metadata      │
│  ├── Code Array          │
│  ├── Constant Pool       │  ← may contain nested chunks (tag 5)
│  ├── Upvalue Descriptors │
│  ├── Global Name Table   │
│  ├── IC Slot Metadata    │
│  ├── Const Global Inits  │
│  └── Debug Info (opt)    │
├──────────────────────────┤
│  Stdlib Manifest (opt)   │
├──────────────────────────┤
│  Embedded Source (opt)   │
└──────────────────────────┘
```

The three optional trailing sections appear in the order shown when their corresponding header flag is set. Readers must not assume any other ordering.

## 2. Header

The header is exactly 32 bytes.

| Offset | Size | Field             | Description                                                                                                                                                  |
| ------ | ---- | ----------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| `0x00` | 4    | `Magic`           | `0x53 0x54 0x42 0x43` (`STBC`). Identifies the file format.                                                                                                  |
| `0x04` | 2    | `FormatVersion`   | `u16 LE`. Currently `1`. A mismatch is a hard error.                                                                                                         |
| `0x06` | 1    | `Flags`           | Bit 0 = `HasDebugInfo`, bit 1 = `Optimized`, bit 2 = `HasEmbeddedSource`, bit 3 = `HasStdlibManifest`. All other bits must be zero.                          |
| `0x07` | 1    | `Reserved`        | Must be `0x00`. Reserved for future use.                                                                                                                     |
| `0x08` | 4    | `CompilerHash`    | `u32 LE`. Hash of the compiler assembly version. Informational only - readers must not reject on mismatch.                                                   |
| `0x0C` | 4    | `OpCodeTableHash` | `u32 LE`. FNV-1a hash of the OpCode table (see [2.1](#21-opcode-table-hash)). Must match the reader's computed hash, otherwise the bytecode is incompatible. |
| `0x10` | 16   | `SourceSHA256`    | First 16 bytes of the SHA-256 of the original source text. All zeros when no source was supplied. Intended for build-cache invalidation.                     |

### 2.1 OpCode Table Hash

The OpCode table hash protects against silent ABI breakage when the instruction set changes. Readers compute it the same way the writer did and reject any file whose stored hash does not match.

```
hash = 2166136261                       // FNV-1a 32-bit offset basis
for each value in Enum.GetValues<OpCode>():
    for each char c in value.ToString():
        hash ^= (byte) c
        hash *= 16777619                // FNV prime
    hash ^= (uint)(byte) value          // fold in numeric value
    hash *= 16777619
```

The hash changes whenever opcodes are added, removed, renamed, or renumbered.

## 3. Chunk

A chunk is the unit of compiled code: the top-level script is a chunk, and every function and closure is a nested chunk that appears as a constant of tag `5` (see [3.3](#33-constant-pool)). Chunks are serialized in the order listed below.

### 3.1 Chunk Metadata

| Size | Field             | Description                                                                                  |
| ---- | ----------------- | -------------------------------------------------------------------------------------------- |
| 2+N  | `Name`            | Nullable string (`u16 LE` length + UTF-8 bytes; `0xFFFF` = null for anonymous chunks).       |
| 2    | `Arity`           | `u16 LE`. Declared parameter count.                                                          |
| 2    | `MinArity`        | `u16 LE`. Minimum required argument count (accounts for parameters with default values).     |
| 2    | `MaxRegs`         | `u16 LE`. Total register slots reserved by the function.                                     |
| 2    | `GlobalSlotCount` | `u16 LE`. Total global variable slots.                                                       |
| 1    | `ChunkFlags`      | Bit 0 = `IsAsync`, bit 1 = `HasRestParam`, bit 2 = `MayHaveCapturedLocals`. Other bits zero. |

### 3.2 Code Array

| Size | Field          | Description                                                                                                   |
| ---- | -------------- | ------------------------------------------------------------------------------------------------------------- |
| 4    | `CodeLength`   | `u32 LE`. Number of 32-bit instruction words, **not** bytes. Maximum `16,777,216`.                            |
| 4×N  | `Instructions` | `u32 LE` per word. Includes both opcodes and the companion words documented in the Instruction Set Reference. |

### 3.3 Constant Pool

| Size | Field     | Description                                 |
| ---- | --------- | ------------------------------------------- |
| 2    | `Count`   | `u16 LE`. Number of constants (max 65,535). |
| var  | `Entries` | `Count` tagged entries (see below).         |

Each entry starts with a one-byte type tag, followed by tag-specific payload:

| Tag  | Type                  | Payload                                                                                                                                                                                                                            |
| ---- | --------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `0`  | `Null`                | (none)                                                                                                                                                                                                                             |
| `1`  | `Bool`                | `u8`: `0` = false, `1` = true.                                                                                                                                                                                                     |
| `2`  | `Int`                 | `i64 LE`.                                                                                                                                                                                                                          |
| `3`  | `Float`               | `i64 LE` (bit-cast IEEE 754 double).                                                                                                                                                                                               |
| `4`  | `String`              | `u32 LE` byte length + UTF-8 bytes.                                                                                                                                                                                                |
| `5`  | `Chunk`               | A recursively serialized chunk (the layout described in this Section 3).                                                                                                                                                           |
| `6`  | `CommandMetadata`     | `u16 LE` PartCount + `u8` IsPassthrough + `u8` IsStrict.                                                                                                                                                                           |
| `7`  | `StructMetadata`      | `String` Name + `StringArray` Fields + `StringArray` MethodNames + `StringArray` InterfaceNames.                                                                                                                                   |
| `8`  | `EnumMetadata`        | `String` Name + `StringArray` Members.                                                                                                                                                                                             |
| `9`  | `InterfaceMetadata`   | `String` Name + `u16` field count + [`String` Name + `NullableString` TypeHint]× + `u16` method count + [`String` Name + `u16` Arity + `StringArray` ParamNames + `NullableStringArray` ParamTypes + `NullableString` ReturnType]× |
| `10` | `ExtendMetadata`      | `String` TypeName + `StringArray` MethodNames + `u8` IsBuiltIn.                                                                                                                                                                    |
| `11` | `ImportMetadata`      | `StringArray` Names.                                                                                                                                                                                                               |
| `12` | `ImportAsMetadata`    | `String` AliasName.                                                                                                                                                                                                                |
| `13` | `DestructureMetadata` | `String` Kind + `StringArray` Names + `NullableString` RestName + `u8` IsConst.                                                                                                                                                    |
| `14` | `RetryMetadata`       | `u16 LE` OptionCount + `u8` HasUntilClause + `u8` HasOnRetryClause + `u8` OnRetryIsReference.                                                                                                                                      |
| `15` | `StructInitMetadata`  | `String` TypeName + `u8` HasTypeReg + `StringArray` FieldNames.                                                                                                                                                                    |
| `16` | `Byte`                | `u8` value.                                                                                                                                                                                                                        |
| `17` | `LockMetadata`        | `i32 LE` OptionCount + `u8` HasWait + `u8` HasStale.                                                                                                                                                                               |

Any other tag value is invalid and must be rejected.

#### String encoding helpers

The constant pool and metadata payloads use these reusable encodings:

| Name                  | Layout                                                                       |
| --------------------- | ---------------------------------------------------------------------------- |
| `String` (u16)        | `u16 LE` byte length + UTF-8 bytes.                                          |
| `NullableString`      | Same as `String` (u16), but `0xFFFF` length signals null (no payload bytes). |
| `String` (u32)        | `u32 LE` byte length + UTF-8 bytes. Used for tag `4` string constants.       |
| `StringArray`         | `u16 LE` count + `count × String` (u16).                                     |
| `NullableStringArray` | `u16 LE` count + `count × NullableString`.                                   |

### 3.4 Upvalue Descriptors

| Size | Field         | Description                                                           |
| ---- | ------------- | --------------------------------------------------------------------- |
| 1    | `Count`       | `u8`. Number of upvalues (max 255).                                   |
| 2×N  | `Descriptors` | Per upvalue: `u8` Index + `u8` IsLocal (`0` inherited, `1` captured). |

### 3.5 Global Name Table

| Size | Field   | Description                                                            |
| ---- | ------- | ---------------------------------------------------------------------- |
| 2    | `Count` | `u16 LE`. Number of entries. `0` means no name table is emitted.       |
| var  | `Names` | `Count × String` (u16). Maps a global slot index to its variable name. |

### 3.6 IC Slot Metadata

| Size | Field             | Description                                                      |
| ---- | ----------------- | ---------------------------------------------------------------- |
| 2    | `Count`           | `u16 LE`. Number of inline-cache slots.                          |
| 2×N  | `ConstantIndices` | `u16 LE` per slot. Constant pool index of the cached field name. |

IC slots are allocated at compile time. The runtime state (guard, cached value, transition state) is not serialized; every slot is reset to the uninitialized state on load.

### 3.7 Const Global Initializations

| Size | Field   | Description                                                                                                      |
| ---- | ------- | ---------------------------------------------------------------------------------------------------------------- |
| 2    | `Count` | `u16 LE`. Number of `(slot, const)` pairs.                                                                       |
| 4×N  | `Pairs` | Per pair: `u16 LE` slot index + `u16 LE` constant index. Records which global slots hold compile-time constants. |

### 3.8 Debug Info

Present only when bit 0 (`HasDebugInfo`) is set in the header. When present, every chunk in the file emits a debug-info block in this order. The flag is file-wide: it applies to the top-level chunk and every nested chunk consistently.

#### Source Map Entries

| Size | Field     | Description                                                                                                          |
| ---- | --------- | -------------------------------------------------------------------------------------------------------------------- |
| 4    | `Count`   | `u32 LE`. Number of entries.                                                                                         |
| 12×N | `Entries` | Per entry: `u32` BytecodeOffset + `u16` FileIndex + `u16` StartLine + `u16` StartCol + `u16` EndLine + `u16` EndCol. |

#### Source File Table

| Size | Field   | Description                                                           |
| ---- | ------- | --------------------------------------------------------------------- |
| 2    | `Count` | `u16 LE`. Number of unique source file paths.                         |
| var  | `Paths` | `Count × String` (u16). Deduplicated paths referenced by `FileIndex`. |

#### Local Variable Names

| Size | Field   | Description                                                    |
| ---- | ------- | -------------------------------------------------------------- |
| 2    | `Count` | `u16 LE`. Number of named locals (maps register index → name). |
| var  | `Names` | `Count × NullableString`. One name per register slot.          |

#### Local Const Flags

| Size | Field   | Description                                 |
| ---- | ------- | ------------------------------------------- |
| 2    | `Count` | `u16 LE`. Number of entries.                |
| N    | `Flags` | `u8` per entry: `0` = mutable, `1` = const. |

#### Upvalue Names

| Size | Field   | Description                                                |
| ---- | ------- | ---------------------------------------------------------- |
| 1    | `Count` | `u8`. Number of upvalue names.                             |
| var  | `Names` | `Count × NullableString`. One name per upvalue descriptor. |

## 4. Stdlib Manifest

Present only when bit 3 (`HasStdlibManifest`) is set in the header. The manifest appears immediately after the chunk tree and before the embedded source. It records the standard-library surface the bytecode requires, allowing a host to verify that the namespaces, globals, and capabilities the chunk expects are actually injected at load time.

| Size | Field            | Description                                                                                           |
| ---- | ---------------- | ----------------------------------------------------------------------------------------------------- |
| 2    | `NamespaceCount` | `u16 LE`. Number of required stdlib namespace names.                                                  |
| var  | `Namespaces`     | `NamespaceCount × String` (u16). Namespaces the bytecode references.                                  |
| 2    | `GlobalCount`    | `u16 LE`. Number of required top-level global names.                                                  |
| var  | `Globals`        | `GlobalCount × String` (u16). Globals the bytecode references.                                        |
| 4    | `Capabilities`   | `u32 LE`. Bitmask of `StashCapabilities` values that must be granted to the VM for this chunk to run. |

## 5. Embedded Source

Present only when bit 2 (`HasEmbeddedSource`) is set in the header.

| Size | Field    | Description                                                              |
| ---- | -------- | ------------------------------------------------------------------------ |
| 4    | `Length` | `u32 LE`. Byte length of the UTF-8 source text. `0` denotes a null body. |
| N    | `Source` | UTF-8 bytes.                                                             |

## 6. Reader Validation

A conforming reader must, in this order:

1. Verify the magic bytes equal `STBC`.
2. Verify `FormatVersion` equals the implementation's supported version (currently `1`).
3. Verify `OpCodeTableHash` matches the reader's locally computed hash.
4. Reject any code stream longer than `16,777,216` instruction words.
5. Reject any string with declared length greater than 16 MB.
6. Reject any constant entry whose tag is outside the documented range (currently `0`-`17`).
7. Reject any header flag bit that is not currently defined.
8. Reject any reserved byte that is not `0x00`.

The `CompilerHash` field is informational only and must not cause rejection. The `SourceSHA256` field is also informational and is exposed to consumers as cache key material.

## 7. Compatibility

Two independent versioning surfaces govern compatibility:

| Surface             | Field             | Bump when                                                                                     | Mismatch behavior |
| ------------------- | ----------------- | --------------------------------------------------------------------------------------------- | ----------------- |
| Format version      | `FormatVersion`   | The binary layout changes (new sections, reordered fields, new flag bits, new constant tags). | Hard error.       |
| Instruction set ABI | `OpCodeTableHash` | Any opcode is added, removed, renamed, or renumbered (see [Instruction Set Reference][isr]).  | Hard error.       |

[isr]: Bytecode%20VM%20—%20Instruction%20Set%20Reference.md

Both fields must match for a file to load. This separation lets the format version and the instruction set evolve independently: an opcode rename does not require a format-version bump, and a layout change does not require revisiting every opcode.

## 8. Change Rules

Changes to this format must preserve the following:

- **New trailing sections** must be gated by a new header flag bit, must appear after all existing trailing sections, and must bump `FormatVersion`.
- **New constant tags** must use the next unused tag value and bump `FormatVersion`.
- **New chunk flags or header flag bits** must bump `FormatVersion` and define their semantics in this document.
- **Removing or reordering fields** in an existing section is always a `FormatVersion` bump.
- **Adding, removing, renaming, or renumbering opcodes** changes the OpCode table hash automatically. No format-version bump is required, but the affected files are not loadable on the prior build, which is the intended behavior.
- **Reader validation rules** in [Section 6](#6-reader-validation) must be updated whenever a new validated invariant is introduced.

Implementation details (writer internals, optimization passes, IC runtime structure) belong in source comments or engineering notes, not in this format reference.
