# Byte Type & Byte Arrays — Native Binary Data for Stash

> **Status:** Draft
> **Created:** 2026-04-14
> **Depends on:** Typed Arrays (Phase 1) — assumes `StashTypedArray` abstract base, `T[]` syntax, `TypeHint` record, `TypedWrap` opcode, and `SvArgs.AnyArray()` dual-dispatch are all implemented.
> **Enables:** Native binary protocol clients (Redis RESP, Kafka, PostgreSQL wire protocol, gRPC/protobuf), raw file I/O, efficient crypto operations, proper WebSocket binary frames
> **Impact:** New primitive type + new typed array subclass + stdlib additions + existing function overloads

---

## 1. Motivation

### The Problem

Every I/O boundary in Stash is string-based. Binary data is round-tripped through base64 or Latin-1 encoding, which means:

1. **Data corruption** — `net.tcpSend` encodes strings as UTF-8. Arbitrary byte sequences containing invalid UTF-8 (e.g., `0xFF 0xFE`) get mangled or replaced with `U+FFFD`. This is not a theoretical concern — every binary protocol starts with bytes that aren't valid UTF-8.
2. **Memory overhead** — base64 encoding inflates data by 33%. A 1 MB binary payload becomes 1.33 MB of base64 text, plus the string object overhead.
3. **API friction** — `net.wsSendBinary` requires the user to base64-encode data before sending and base64-decode on receive. This is busywork that every binary-aware function would need.
4. **Missing semantics** — there's no way to construct, inspect, or manipulate individual bytes. You can't build a RESP protocol frame, write a binary file, or process raw network data.

### What This Unlocks

| Protocol / Use Case        | Current Status                            | With byte type                          |
| -------------------------- | ----------------------------------------- | --------------------------------------- |
| Redis RESP                 | Blocked — binary framing                  | `byte[]` + TCP = native client possible |
| Kafka wire protocol        | Blocked — varint encoding, binary framing | Possible with byte manipulation         |
| PostgreSQL/MySQL wire      | Blocked — binary message format           | Possible with byte manipulation         |
| Binary file I/O            | Blocked — `fs.readFile` returns string    | `fs.readBytes` / `fs.writeBytes`        |
| Efficient WebSocket binary | Awkward — base64 round-trip               | Direct `byte[]` send/receive            |
| AWS SigV4 signing          | Works but fragile — string-based HMAC     | Byte-level HMAC + SHA operations        |
| Protocol Buffers           | Blocked — binary serialization            | Possible with byte manipulation         |
| Image/audio processing     | Blocked — no raw data access              | Read/write raw file bytes               |
| Cryptographic operations   | Working but limited — hex string output   | Direct byte output + comparison         |

### Design Philosophy

Stash is a sysadmin scripting language. The byte type is NOT intended to turn Stash into a systems programming language. The goal is:

1. **Enable binary I/O** — read/write raw bytes from files, network, and processes
2. **Enable protocol framing** — construct and parse binary message headers/payloads
3. **Keep it simple** — no pointer arithmetic, no unsafe memory, no endianness confusion beyond explicit conversion helpers

A Stash user should be able to write a Redis RESP client or parse a binary config file. They should NOT need to write a video codec.

---

## 2. Design Overview

Two additions:

1. **`byte` — a new primitive type** backed by `StashValueTag.Byte`, stored inline in `StashValue._data` (same pattern as `int`, `float`, `bool`). Range: 0–255 (unsigned 8-bit integer).
2. **`byte[]` — a new typed array subclass** (`StashByteArray`) backed by `byte[]` in .NET. Slots into the `StashTypedArray` hierarchy established by Phase 1.

Plus a new **`buf` namespace** with functions for constructing, inspecting, converting, and slicing byte arrays. The `buf` namespace is the primary API surface for binary data manipulation.

```stash
// Scalar byte values
let b: byte = 0xFF;
let zero: byte = 0;
typeof(b)              // "byte"
b is byte              // true

// Byte arrays — the workhorse
let header: byte[] = [0x48, 0x54, 0x54, 0x50];   // "HTTP" in ASCII
let payload: byte[] = buf.from("Hello, world!");   // string → UTF-8 bytes
let combined: byte[] = buf.concat(header, payload);

// Binary I/O
let data: byte[] = fs.readBytes("/path/to/file");
fs.writeBytes("/path/to/output", data);

// Network I/O
net.tcpSendBytes(conn, data);
let received: byte[] = net.tcpRecvBytes(conn, 1024);

// Crypto with native bytes
let hash: byte[] = crypto.sha256Bytes("hello");
let hmac_result: byte[] = crypto.hmacBytes("sha256", key_bytes, data_bytes);
```

---

## 3. The `byte` Primitive Type

### 3a. Value Semantics

- **Range:** 0–255 (unsigned 8-bit integer). No negative values.
- **Storage:** Stored in `StashValue._data` as a `long`, extracted via `(byte)_data`. Identical pattern to `bool` (which stores 0/1 in `_data`).
- **Tag:** `StashValueTag.Byte = 5` — new tag value in the enum.
- **Equality:** Byte-to-byte comparison uses `_data == other._data` (same branch as `Int`/`Bool` in the existing equality switch).
- **Truthiness:** `0` is falsy, all other byte values (1–255) are truthy. Consistent with integer truthiness.

### 3b. What a `byte` Is NOT

- **Not a small integer.** `byte` and `int` are distinct types. `5` is an int, not a byte. This is deliberate — byte means "I'm working with binary data", int means "I'm working with numbers".
- **Not a character.** Stash has string indexing for characters. `byte` represents a raw octet, not a Unicode code point.
- **Not unsigned int.** There's no `uint8`/`uint16`/`uint32` family. `byte` is a specialized single-width type for binary data, not the start of a fixed-width integer hierarchy.

### 3c. Literal Syntax

Bytes have **no unique literal syntax**. An integer literal in the range 0–255 becomes a `byte` through:

1. **Type annotation:** `let b: byte = 0xFF;` — the int literal `0xFF` (= 255) is narrowed to byte at the assignment boundary.
2. **Explicit conversion:** `conv.toByte(255)` — runtime conversion.
3. **Byte array construction:** Elements in a `byte[]` are automatically narrowed: `let data: byte[] = [0x48, 0x65, 0x6C];`

**All integer literal forms work at type boundaries.** The lexer produces `IntegerLiteral` tokens for decimal, hex, octal, and binary notations — they're all just integers with different spellings. The narrowing to `byte` happens at the type annotation boundary, not at the literal level:

```stash
let a: byte = 255;            // Decimal
let b: byte = 0xFF;           // Hex — most common for byte work
let c: byte = 0o377;          // Octal
let d: byte = 0b1111_1111;    // Binary — useful for bitmasks and flags
let e: byte = 0b1010_0101;    // Binary — the underscore separator helps readability

// Same applies to byte[] elements:
let flags: byte[] = [0b0000_0001, 0b0000_0010, 0b0000_0100, 0b0000_1000];
```

Binary notation (`0b...`) is particularly natural for byte values since it directly maps to the 8-bit representation — bit flags, masks, and protocol fields are often specified in binary. No lexer changes needed; the existing `ScanBinaryLiteral` → `IntegerLiteral` pipeline feeds into the same narrowing path.

There is no `0xFFb` or `255B` suffix. Rationale:

- Byte literals are rare in isolation — the common case is arrays of bytes.
- The `B` suffix is taken by `StashByteSize` (`100B` = 100 bytes as a size measurement).
- Adding a new suffix creates lexer ambiguity and adds complexity for marginal gain.
- Type-annotated declaration (`let b: byte = 0xFF`) is clear and consistent with how typed arrays work.
- All four integer literal forms (decimal, hex, octal, binary) already work through type annotation — no syntax extension required.

### 3d. StashValue Changes

```csharp
// StashValueTag.cs — add one value
public enum StashValueTag : byte
{
    Null  = 0,
    Bool  = 1,
    Int   = 2,
    Float = 3,
    Obj   = 4,
    Byte  = 5,  // NEW
}

// StashValue.cs — add factory + accessor
public static StashValue FromByte(byte value)
    => new(StashValueTag.Byte, (long)value, null);

public byte AsByte => (byte)_data;
public bool IsByte => Tag == StashValueTag.Byte;

// Equality — add to existing switch
case StashValueTag.Byte => _data == other._data,

// Singleton cache (optional, minor optimization)
public static readonly StashValue ByteZero = new(StashValueTag.Byte, 0, null);
```

### 3e. Type System Integration

| Expression         | Result                                  |
| ------------------ | --------------------------------------- |
| `typeof(byte_val)` | `"byte"`                                |
| `byte_val is byte` | `true`                                  |
| `byte_val is int`  | `false` — byte is NOT an int            |
| `int_val is byte`  | `false` — int is NOT a byte             |
| `0xFF is byte`     | `false` — bare literal `0xFF` is an int |

Add `"byte"` to `_knownTypeNames` in `VirtualMachine.TypeOps.cs`. `CheckIsType("byte") => value.Tag == StashValueTag.Byte` (or if boxed: `value is byte`).

> **No collision with `"bytes"` (StashByteSize).** `"byte"` (singular, new primitive) vs `"bytes"` (plural, existing byte-size type) are distinct strings. `typeof` returns `"byte"` for the primitive and `"bytes"` for `StashByteSize`. The naming is consistent with the convention: `"int"` (singular) for the integer type.

---

## 4. Conversions Between `byte` and Other Types

### 4a. Conversion Rules

| From              | To         | Method                                          | Behavior                                  |
| ----------------- | ---------- | ----------------------------------------------- | ----------------------------------------- |
| `int` → `byte`    | Narrowing  | `conv.toByte(n)` or type annotation             | Throws if n < 0 or n > 255                |
| `byte` → `int`    | Widening   | `conv.toInt(b)` or implicit in arithmetic       | Always succeeds — lossless                |
| `float` → `byte`  | Narrowing  | `conv.toByte(f)`                                | Truncates decimal, throws if out of range |
| `string` → `byte` | Parsing    | `conv.toByte("255")` or `conv.toByte("FF", 16)` | Parses as number, throws on failure       |
| `byte` → `string` | Formatting | `conv.toStr(b)` or string interpolation         | Returns decimal string: `"255"`           |
| `bool` → `byte`   | —          | Not supported                                   | Use `conv.toInt(b)` then `conv.toByte(n)` |

### 4b. Implicit Promotion: `byte` → `int` in Arithmetic

When a `byte` participates in arithmetic with an `int`, the byte is implicitly promoted to `int`:

```stash
let b: byte = 10;
let result = b + 5;       // result is int (15), not byte
let result2 = b * 2;      // result is int (20)
let result3 = b + b;      // result is int (20) — two bytes still promote to int
```

Rationale: Arithmetic on bytes can easily overflow (255 + 1 = 256, which doesn't fit in a byte). Promoting to int avoids silent truncation. This matches C#'s behavior where `byte + byte` returns `int`.

**Byte-to-byte arithmetic returning byte does NOT happen.** If you want a byte result, narrow explicitly:

```stash
let b: byte = conv.toByte((b1 + b2) & 0xFF);  // explicit truncation
```

### 4c. Implicit Narrowing: `int` → `byte` at Typed Boundaries

At typed boundaries (type annotations, `byte[]` push, `byte[]` construction), integers in range 0–255 are **implicitly narrowed** to `byte`:

```stash
let b: byte = 42;                    // OK — 42 is in [0, 255]
let data: byte[] = [0x48, 0x65];     // OK — each element narrowed to byte
arr.push(data, 0x6C);               // OK — 0x6C narrowed to byte
```

```stash
let b: byte = 256;                   // RuntimeError — 256 > 255
let b: byte = -1;                    // RuntimeError — negative
arr.push(data, 300);                // RuntimeError — 300 > 255
```

This is the same pattern as `float[]` accepting int with auto-promotion (from Phase 1). The difference: `int → float` is always safe (widening), while `int → byte` can fail (narrowing). The range check is mandatory.

### 4d. `float[]` Analogy in Typed Arrays

`byte[]` follows the same typed-array convention for implicit int-to-byte narrowing that `float[]` follows for int-to-float promotion:

| Typed Array | Accepts                   | Implicit Conversion                     | Throws When                 |
| ----------- | ------------------------- | --------------------------------------- | --------------------------- |
| `int[]`     | `int` only                | None                                    | Non-int pushed              |
| `float[]`   | `float`, `int` (promoted) | `int → float` (widening, always safe)   | Non-numeric pushed          |
| `byte[]`    | `byte`, `int` (narrowed)  | `int → byte` (narrowing, range-checked) | Out of range or non-numeric |
| `string[]`  | `string` only             | None                                    | Non-string pushed           |
| `bool[]`    | `bool` only               | None                                    | Non-bool pushed             |

---

## 5. `byte[]` — The `StashByteArray` Subclass

### 5a. Architecture

`StashByteArray` is a new subclass of `StashTypedArray` (the abstract base from Phase 1):

```
StashTypedArray (abstract)
├── StashIntArray       — long[]
├── StashFloatArray     — double[]
├── StashStringArray    — string[]
├── StashBoolArray      — bool[]
└── StashByteArray      — byte[]   ← NEW
```

Backed by `byte[]` in .NET. 1 byte per element. Same growth strategy as other typed arrays (double on capacity exceeded, `Array.Copy` for resize).

### 5b. Key Implementation Details

```csharp
public sealed class StashByteArray : StashTypedArray
{
    private byte[] _data;
    private int _count;

    public override string ElementTypeName => "byte";
    public override int Count => _count;

    public override StashValue Get(int index)
    {
        if ((uint)index >= (uint)_count) throw IndexOutOfRange(index);
        return StashValue.FromByte(_data[index]);
    }

    public override void Set(int index, StashValue val)
    {
        if ((uint)index >= (uint)_count) throw IndexOutOfRange(index);
        _data[index] = ExtractByte(val, index);
    }

    public override void Add(StashValue val)
    {
        EnsureCapacity(_count + 1);
        _data[_count++] = ExtractByte(val, _count);
    }

    // Direct byte[] access for stdlib functions (NOT exposed to Stash code)
    internal byte[] GetBackingArray() => _data;
    internal int GetCount() => _count;
    internal ReadOnlySpan<byte> AsSpan() => _data.AsSpan(0, _count);

    private static byte ExtractByte(StashValue val, int index)
    {
        if (val.IsByte) return val.AsByte;
        if (val.IsInt)
        {
            long n = val.AsInt;
            if (n is < 0 or > 255)
                throw new RuntimeError($"Value {n} is out of byte range [0, 255] at index {index}");
            return (byte)n;
        }
        throw new RuntimeError($"Cannot add {TypeName(val)} to byte[] — expected byte or int [0–255]");
    }
}
```

### 5c. Internal Access: `AsSpan()` and `GetBackingArray()`

The `internal` methods are the critical advantage of native backing. Stdlib functions that move bytes in bulk — `net.tcpSendBytes`, `fs.writeBytes`, `crypto.sha256Bytes` — call `AsSpan()` to get a `ReadOnlySpan<byte>` directly. No conversion, no copying, no allocation. The bytes flow straight from the typed array backing into the .NET I/O APIs.

This is why the byte type exists as a primitive and not just as "int values stored in a generic array". A `List<StashValue>` containing integers 0–255 would require O(n) conversion to `byte[]` at every I/O boundary. `StashByteArray` is O(1).

### 5d. Construction

| Method                 | Example                                  | Notes                                               |
| ---------------------- | ---------------------------------------- | --------------------------------------------------- |
| Type annotation        | `let data: byte[] = [0x48, 0x65, 0x6C];` | Each int element narrowed to byte via `ExtractByte` |
| `arr.typed()`          | `arr.typed([72, 101], "byte")`           | Same — validates + wraps                            |
| `arr.new()`            | `arr.new("byte", 1024)`                  | Pre-allocated with zeros                            |
| `buf.from()`           | `buf.from("Hello")`                      | UTF-8 encode string → byte[]                        |
| `buf.fromHex()`        | `buf.fromHex("48656c6c6f")`              | Hex decode → byte[]                                 |
| `buf.fromBase64()`     | `buf.fromBase64("SGVsbG8=")`             | Base64 decode → byte[]                              |
| `buf.alloc()`          | `buf.alloc(256)`                         | Zero-filled byte[] of given size                    |
| `buf.alloc()`          | `buf.alloc(256, 0xFF)`                   | Filled with specified byte value                    |
| `crypto.randomBytes()` | `crypto.randomBytes(32)` (overload)      | Returns byte[] directly (no encoding)               |
| `fs.readBytes()`       | `fs.readBytes("/path/to/file")`          | Read entire file as byte[]                          |

### 5e. Type System

| Expression           | Result                           |
| -------------------- | -------------------------------- |
| `typeof(byte_arr)`   | `"byte[]"`                       |
| `byte_arr is byte[]` | `true`                           |
| `byte_arr is array`  | `true` — typed arrays are arrays |
| `byte_arr is int[]`  | `false` — different element type |

### 5f. Stringification

```stash
io.println(data);    // byte[0x48, 0x65, 0x6C, 0x6C, 0x6F]  (hex display)
```

Byte arrays display in hex, not decimal. Rationale: Bytes are binary data — `0x48` is more recognizable as "the letter H" than `72`. This is the universal convention in hex editors, debuggers, and protocol analyzers.

For long arrays, truncate with count:

```stash
io.println(large_data);   // byte[0x48, 0x65, 0x6C, ... (1024 bytes)]
```

JSON serialization: byte arrays become base64-encoded strings (standard convention for binary data in JSON):

```stash
json.stringify(data);    // "\"SGVsbG8=\""
```

---

## 6. The `buf` Namespace — Binary Data Operations

The `buf` namespace is the primary API for constructing, inspecting, and manipulating byte arrays. It's a new stdlib namespace (registered in `BufBuiltIns.cs`).

### 6a. Construction

| Function         | Signature                                  | Description                                                                                                  |
| ---------------- | ------------------------------------------ | ------------------------------------------------------------------------------------------------------------ |
| `buf.from`       | `(s: string, encoding?: string) -> byte[]` | Encode string to bytes. Default encoding: `"utf-8"`. Supports `"ascii"`, `"latin1"`, `"utf-16"`, `"utf-32"`. |
| `buf.fromHex`    | `(hex: string) -> byte[]`                  | Decode hex string to bytes. Case-insensitive. Throws on invalid hex.                                         |
| `buf.fromBase64` | `(b64: string) -> byte[]`                  | Decode base64 string to bytes. Supports URL-safe variant via optional flag.                                  |
| `buf.alloc`      | `(size: int, fill?: byte) -> byte[]`       | Create zero-filled (or fill-value) byte array of given size.                                                 |
| `buf.of`         | `(...values: int) -> byte[]`               | Create byte array from individual byte values: `buf.of(0x48, 0x65, 0x6C)`.                                   |

### 6b. Conversion to String

| Function       | Signature                                     | Description                                 |
| -------------- | --------------------------------------------- | ------------------------------------------- |
| `buf.toString` | `(data: byte[], encoding?: string) -> string` | Decode bytes to string. Default: `"utf-8"`. |
| `buf.toHex`    | `(data: byte[]) -> string`                    | Encode bytes as lowercase hex string.       |
| `buf.toBase64` | `(data: byte[], urlSafe?: bool) -> string`    | Encode bytes as base64 string.              |

### 6c. Inspection

| Function       | Signature                                                   | Description                                                                   |
| -------------- | ----------------------------------------------------------- | ----------------------------------------------------------------------------- |
| `buf.len`      | `(data: byte[]) -> int`                                     | Number of bytes. (Also available via `arr.len`.)                              |
| `buf.get`      | `(data: byte[], index: int) -> byte`                        | Read byte at index. Negative indexing supported. (Also via `data[i]`.)        |
| `buf.indexOf`  | `(data: byte[], search: byte[] \| byte, from?: int) -> int` | Find first occurrence. Returns -1 if not found.                               |
| `buf.includes` | `(data: byte[], search: byte[] \| byte) -> bool`            | Check if bytes contain a subsequence or single byte.                          |
| `buf.equals`   | `(a: byte[], b: byte[]) -> bool`                            | Constant-time byte comparison. Critical for crypto — prevents timing attacks. |

### 6d. Manipulation

| Function      | Signature                                                                            | Description                                                        |
| ------------- | ------------------------------------------------------------------------------------ | ------------------------------------------------------------------ |
| `buf.slice`   | `(data: byte[], start?: int, end?: int) -> byte[]`                                   | Copy a range. Negative indices wrap. (Also via `arr.slice`.)       |
| `buf.concat`  | `(...arrays: byte[]) -> byte[]`                                                      | Concatenate multiple byte arrays into one. Variadic.               |
| `buf.copy`    | `(src: byte[], dest: byte[], destOffset?: int, srcStart?: int, srcEnd?: int) -> int` | Copy bytes from source to destination. Returns bytes copied.       |
| `buf.fill`    | `(data: byte[], value: byte, start?: int, end?: int) -> byte[]`                      | Fill range with a byte value. Mutates in-place, returns the array. |
| `buf.reverse` | `(data: byte[]) -> byte[]`                                                           | Reverse copy.                                                      |

### 6e. Binary Encoding / Packing

These are the functions that enable protocol work:

| Function            | Signature                                           | Description                                         |
| ------------------- | --------------------------------------------------- | --------------------------------------------------- |
| `buf.readUint8`     | `(data: byte[], offset: int) -> int`                | Read unsigned 8-bit integer at offset.              |
| `buf.readUint16BE`  | `(data: byte[], offset: int) -> int`                | Read unsigned 16-bit integer, big-endian.           |
| `buf.readUint16LE`  | `(data: byte[], offset: int) -> int`                | Read unsigned 16-bit integer, little-endian.        |
| `buf.readUint32BE`  | `(data: byte[], offset: int) -> int`                | Read unsigned 32-bit integer, big-endian.           |
| `buf.readUint32LE`  | `(data: byte[], offset: int) -> int`                | Read unsigned 32-bit integer, little-endian.        |
| `buf.readInt8`      | `(data: byte[], offset: int) -> int`                | Read signed 8-bit integer.                          |
| `buf.readInt16BE`   | `(data: byte[], offset: int) -> int`                | Read signed 16-bit, big-endian.                     |
| `buf.readInt16LE`   | `(data: byte[], offset: int) -> int`                | Read signed 16-bit, little-endian.                  |
| `buf.readInt32BE`   | `(data: byte[], offset: int) -> int`                | Read signed 32-bit, big-endian.                     |
| `buf.readInt32LE`   | `(data: byte[], offset: int) -> int`                | Read signed 32-bit, little-endian.                  |
| `buf.readInt64BE`   | `(data: byte[], offset: int) -> int`                | Read signed 64-bit, big-endian. (Stash int = long.) |
| `buf.readInt64LE`   | `(data: byte[], offset: int) -> int`                | Read signed 64-bit, little-endian.                  |
| `buf.readFloatBE`   | `(data: byte[], offset: int) -> float`              | Read 32-bit IEEE 754 float, big-endian.             |
| `buf.readFloatLE`   | `(data: byte[], offset: int) -> float`              | Read 32-bit IEEE 754 float, little-endian.          |
| `buf.readDoubleBE`  | `(data: byte[], offset: int) -> float`              | Read 64-bit IEEE 754 double, big-endian.            |
| `buf.readDoubleLE`  | `(data: byte[], offset: int) -> float`              | Read 64-bit IEEE 754 double, little-endian.         |
| `buf.writeUint8`    | `(data: byte[], offset: int, value: int) -> void`   | Write unsigned 8-bit integer at offset.             |
| `buf.writeUint16BE` | `(data: byte[], offset: int, value: int) -> void`   | Write unsigned 16-bit, big-endian.                  |
| `buf.writeUint16LE` | `(data: byte[], offset: int, value: int) -> void`   | Write unsigned 16-bit, little-endian.               |
| `buf.writeUint32BE` | `(data: byte[], offset: int, value: int) -> void`   | Write unsigned 32-bit, big-endian.                  |
| `buf.writeUint32LE` | `(data: byte[], offset: int, value: int) -> void`   | Write unsigned 32-bit, little-endian.               |
| `buf.writeInt8`     | `(data: byte[], offset: int, value: int) -> void`   | Write signed 8-bit.                                 |
| `buf.writeInt16BE`  | `(data: byte[], offset: int, value: int) -> void`   | Write signed 16-bit, big-endian.                    |
| `buf.writeInt16LE`  | `(data: byte[], offset: int, value: int) -> void`   | Write signed 16-bit, little-endian.                 |
| `buf.writeInt32BE`  | `(data: byte[], offset: int, value: int) -> void`   | Write signed 32-bit, big-endian.                    |
| `buf.writeInt32LE`  | `(data: byte[], offset: int, value: int) -> void`   | Write signed 32-bit, little-endian.                 |
| `buf.writeInt64BE`  | `(data: byte[], offset: int, value: int) -> void`   | Write signed 64-bit, big-endian.                    |
| `buf.writeInt64LE`  | `(data: byte[], offset: int, value: int) -> void`   | Write signed 64-bit, little-endian.                 |
| `buf.writeFloatBE`  | `(data: byte[], offset: int, value: float) -> void` | Write 32-bit float, big-endian.                     |
| `buf.writeFloatLE`  | `(data: byte[], offset: int, value: float) -> void` | Write 32-bit float, little-endian.                  |
| `buf.writeDoubleBE` | `(data: byte[], offset: int, value: float) -> void` | Write 64-bit double, big-endian.                    |
| `buf.writeDoubleLE` | `(data: byte[], offset: int, value: float) -> void` | Write 64-bit double, little-endian.                 |

All read/write functions validate offset bounds and throw `RuntimeError` on out-of-bounds access.

> **Design note:** The explicit `BE`/`LE` suffixes (big-endian / little-endian) are mandatory. There is no default endianness. Network protocols overwhelmingly use big-endian, but Windows APIs and x86 native formats use little-endian. Making the developer choose prevents silent bugs — the most dangerous kind in binary protocol work.

### 6f. Implementation Notes

All `buf.read*`/`buf.write*` functions use `BinaryPrimitives` from `System.Buffers.Binary`:

```csharp
// buf.readUint16BE example
public static int ReadUint16BE(ReadOnlySpan<byte> data, int offset)
{
    return BinaryPrimitives.ReadUInt16BigEndian(data.Slice(offset));
}
```

This is a single method call — no manual byte shifting. `BinaryPrimitives` is optimized, endian-aware, and available in all .NET targets including AOT.

---

## 7. Existing Stdlib Changes

### 7a. Functions That Gain `byte[]` Overloads

These existing functions currently accept/return strings for binary data. With the byte type, they gain **new overloads or variants** that work with `byte[]` directly.

#### `crypto` Namespace

| Function             | Current Signature                             | New Variant                                                   | Notes                                                                                                        |
| -------------------- | --------------------------------------------- | ------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------ |
| `crypto.randomBytes` | `(n, encoding?) -> string`                    | `(n: int) -> byte[]`                                          | No encoding param = returns `byte[]` directly. If encoding param provided, returns string (backward compat). |
| `crypto.md5`         | `(data: string) -> string`                    | `crypto.md5Bytes(data: byte[]) -> byte[]`                     | Raw hash output as bytes, not hex string.                                                                    |
| `crypto.sha1`        | `(data: string) -> string`                    | `crypto.sha1Bytes(data: byte[]) -> byte[]`                    | Same pattern.                                                                                                |
| `crypto.sha256`      | `(data: string) -> string`                    | `crypto.sha256Bytes(data: byte[]) -> byte[]`                  | Same.                                                                                                        |
| `crypto.sha512`      | `(data: string) -> string`                    | `crypto.sha512Bytes(data: byte[]) -> byte[]`                  | Same.                                                                                                        |
| `crypto.hmac`        | `(algo, key: string, data: string) -> string` | `crypto.hmacBytes(algo, key: byte[], data: byte[]) -> byte[]` | Key and data as bytes, result as bytes. Critical for SigV4.                                                  |

**Backward compatibility:** ALL existing string-based functions remain unchanged. The `*Bytes` variants are new additions, not replacements.

#### `fs` Namespace

| Function         | New | Signature                              | Notes                                        |
| ---------------- | --- | -------------------------------------- | -------------------------------------------- |
| `fs.readBytes`   | NEW | `(path: string) -> byte[]`             | Read file as raw bytes.                      |
| `fs.writeBytes`  | NEW | `(path: string, data: byte[]) -> null` | Write raw bytes to file. Creates/overwrites. |
| `fs.appendBytes` | NEW | `(path: string, data: byte[]) -> null` | Append raw bytes to file.                    |

No changes to `fs.readFile`/`fs.writeFile` — those remain string-based.

#### `net` Namespace

| Function           | New     | Signature                                     | Notes                                                                   |
| ------------------ | ------- | --------------------------------------------- | ----------------------------------------------------------------------- |
| `net.tcpSendBytes` | NEW     | `(conn, data: byte[]) -> int`                 | Send raw bytes over TCP. Returns bytes sent.                            |
| `net.tcpRecvBytes` | NEW     | `(conn, maxBytes: int) -> byte[]`             | Receive raw bytes from TCP. Returns byte[].                             |
| `net.wsSendBinary` | CHANGED | `(conn, data: byte[] \| string) -> Future`    | Accepts byte[] directly (preferred) OR base64 string (backward compat). |
| `net.wsRecv`       | CHANGED | WsMessage.data is `byte[]` when type="binary" | Binary frames return byte[] instead of base64 string.                   |

> **Breaking change in `net.wsRecv`:** Binary WebSocket messages currently return base64-encoded strings in `WsMessage.data`. After this change, they return `byte[]`. This is a **deliberate breaking change** — the base64 encoding was a workaround for the lack of byte arrays, not an intentional design choice. Scripts that depend on base64 binary messages will need updating.

#### `encoding` Namespace

| Function                | Current              | Change                                              | Notes                                    |
| ----------------------- | -------------------- | --------------------------------------------------- | ---------------------------------------- |
| `encoding.base64Encode` | `(string) -> string` | Add overload: `(byte[]) -> string`                  | Encode bytes to base64 string.           |
| `encoding.base64Decode` | `(string) -> string` | Add: `encoding.base64DecodeBytes(string) -> byte[]` | Decode base64 to raw bytes (not string). |
| `encoding.hexEncode`    | `(string) -> string` | Add overload: `(byte[]) -> string`                  | Encode bytes to hex string.              |
| `encoding.hexDecode`    | `(string) -> string` | Add: `encoding.hexDecodeBytes(string) -> byte[]`    | Decode hex to raw bytes.                 |

#### `conv` Namespace

| Function      | New | Signature                                             | Notes                                                             |
| ------------- | --- | ----------------------------------------------------- | ----------------------------------------------------------------- |
| `conv.toByte` | NEW | `(value: int \| float \| string, base?: int) -> byte` | Convert to byte. Range-checked. Supports `conv.toByte("FF", 16)`. |

---

## 8. `arr.*` Compatibility

`byte[]` is a `StashTypedArray` subclass, so it automatically works with all `arr.*` functions that were updated in Phase 1:

```stash
let data: byte[] = [0x48, 0x65, 0x6C, 0x6C, 0x6F];

arr.len(data)                    // 5
arr.contains(data, 0x48)         // true
arr.push(data, 0x21);            // OK — 0x21 is in byte range
arr.push(data, 300);             // RuntimeError — 300 > 255
arr.slice(data, 0, 3)            // byte[0x48, 0x65, 0x6C]

// For-in iteration
for (let b in data) {
    io.println(conv.toHex(conv.toInt(b)));   // "48", "65", "6c", "6c", "6f"
}

// arr.map — returns generic array (same as Phase 1 design)
let hex_strings = arr.map(data, fn(b) => buf.toHex(buf.of(conv.toInt(b))));
```

### `arr.filter`, `arr.sort`, `arr.reverse`, `arr.slice` — Type-Preserving

These return `byte[]` when the input is `byte[]` (same Phase 1 convention):

```stash
let filtered: byte[] = arr.filter(data, fn(b) => conv.toInt(b) > 0x60);  // byte[]
let sorted: byte[] = arr.sort(data);                                       // byte[]
let reversed: byte[] = arr.reverse(data);                                  // byte[]
```

---

## 9. Bytecode Changes

### 9a. New Constant Pool Tag

The `.stashc` serialization format needs a new constant tag for byte values:

```
Tag 16: Byte constant
Format: [tag: 1 byte = 16][value: 1 byte]
```

Add to `BytecodeWriter.cs`:

```csharp
case StashValueTag.Byte:
    writer.Write((byte)16);
    writer.Write((byte)value.AsByte);
    break;
```

Add to `BytecodeReader.cs`:

```csharp
16 => StashValue.FromByte(reader.ReadByte()),
```

### 9b. TypedWrap Opcode

The `TypedWrap` opcode's string switch (from Phase 1) gains one case:

```
"byte" → new StashByteArray(elements)
```

### 9c. Arithmetic Operations

The VM's arithmetic dispatch needs to handle `StashValueTag.Byte`:

- **Binary ops** (`+`, `-`, `*`, `/`, `%`): If either operand is `Byte`, promote both to `Int` and perform integer arithmetic. Result is always `Int`.
- **Bitwise ops** (`&`, `|`, `^`, `<<`, `>>`): Same — promote byte to int, result is int.
- **Comparison ops** (`<`, `>`, `<=`, `>=`): Promote byte to int for comparison. Result is bool.
- **Equality** (`==`, `!=`): Byte-to-byte uses `_data == other._data`. Byte-to-int: **always false** (different types, no implicit equality promotion). Consistent with Stash's "no type coercion on equality" rule.
- **Unary negate** (`-b`): Promotes to int. Result is int (could be negative).
- **Unary bitwise NOT** (`~b`): Promotes to int. Result is int.

### 9d. GetTable / SetTable

Already handled by Phase 1 — `StashByteArray` is a `StashTypedArray` subclass, dispatched via the existing `case StashTypedArray ta:` branch.

---

## 10. Parser Changes

### 10a. `byte` as a Type Name

The parser already accepts any `Identifier` in type positions (Phase 1's `TypeHint` record). `byte` needs to be recognized as a valid type name. Two options:

**Option A:** `byte` is a keyword. Add `TokenType.Byte` to the lexer, reserve the word.

**Option B:** `byte` is a regular identifier that happens to be a recognized type name (like `int`, `float`, etc. — which are also identifiers in Stash, not keywords).

**Recommendation: Option B.** In Stash, type names like `int`, `float`, `string`, `bool` are identifiers, not reserved keywords. They're used as type hints and in `typeof()` returns but can't be used as variable names because the analysis engine warns against it. `byte` should follow the same pattern.

### 10b. Byte Literal Narrowing

No parser changes for byte literals. Integer literals (`0xFF`, `42`, `0b11111111`) remain `TokenType.IntegerLiteral`. The narrowing to `byte` happens at the VM level via `TypedWrap` (for type-annotated declarations) or via `ExtractByte()` in `StashByteArray` (for array mutations).

---

## 11. Static Analysis Changes

### 11a. Type Inference

- `buf.from(str)` → infer `"byte[]"`
- `buf.alloc(n)` → infer `"byte[]"`
- `crypto.randomBytes(n)` (no encoding param) → infer `"byte[]"`
- `crypto.sha256Bytes(data)` → infer `"byte[]"`
- `fs.readBytes(path)` → infer `"byte[]"`
- `conv.toByte(n)` → infer `"byte"`

### 11b. New Diagnostics

| Code      | Severity | Message                                                                                                                                                                      |
| --------- | -------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `STA0305` | Error    | `Value {value} is out of byte range [0, 255]` — for literal narrowing that can be statically detected.                                                                       |
| `STA0306` | Warning  | `Byte value used in equality comparison with int — types differ, will always be false` — catches `byte_val == 42` when the user probably meant `conv.toInt(byte_val) == 42`. |

> **Decision on STA0306:** This is a warning, not an error. The comparison is well-defined (returns false). But it's almost certainly a bug — the user forgot that byte ≠ int in Stash. The warning helps catch it.

---

## 12. LSP / DAP / Playground / Extension Impact

### 12a. LSP

- **Completions:** `byte` and `byte[]` in type hint positions.
- **Hover:** Show `byte` or `byte[]` types. For byte values, show decimal + hex: `byte = 72 (0x48)`.
- **`buf.*` completions:** New namespace with all functions.

### 12b. DAP

- **Variable display:** Byte values show as `0x48 (72)` — hex primary, decimal in parens.
- **Byte arrays:** Show as `byte[5]` in variable list. Expanding shows hex-indexed elements: `[0] = 0x48`, `[1] = 0x65`, etc.
- **Hex view:** Consider a "View as Hex Dump" option for large byte arrays — formatted like a hex editor with offset, hex, and ASCII columns. This is a nice-to-have, not blocking.

### 12c. Playground

- **Monarch tokenizer:** `byte` added to type keywords. `buf` added to namespace keywords.
- **Examples:** Add a binary data example showing `buf.*` usage.

### 12d. VS Code Extension

- **TextMate grammar:** `byte` and `buf` added to keyword/type patterns.

---

## 13. Cross-Platform Considerations

`byte` is unsigned 8-bit — this is universally consistent across Linux, macOS, and Windows. No platform-specific behavior.

The `buf.read*BE`/`buf.read*LE` functions are endian-explicit, so they produce identical results on all platforms regardless of the CPU's native endianness. `BinaryPrimitives` handles the conversion.

File I/O (`fs.readBytes`/`fs.writeBytes`) is binary — no line-ending translation (`\n` vs `\r\n`). This matches `.NET`'s `FileMode.Open` with `FileAccess.Read` on raw byte streams.

---

## 14. Implementation Phases

### Phase 2a: Byte Primitive Type (foundation)

1. Add `StashValueTag.Byte = 5` to enum
2. Add `FromByte`/`AsByte`/`IsByte` to `StashValue`
3. Update `Equals`, `GetHashCode`, `ToObject`, `FromObject` in `StashValue`
4. Update `typeof` in `GlobalBuiltIns.cs` — add `StashValueTag.Byte => "byte"` before the `ToObject()` switch
5. Update `CheckIsType` in `VirtualMachine.TypeOps.cs` — add `"byte"` case
6. Add `"byte"` and `"byte[]"` to `_knownTypeNames`
7. Add `conv.toByte()` to `ConvBuiltIns.cs`
8. Update `RuntimeValues.Stringify()` for byte display

### Phase 2b: StashByteArray

1. Create `StashByteArray : StashTypedArray` in `Stash.Core/Runtime/Types/`
2. Add `"byte"` case to `TypedWrap` opcode subclass selection
3. Update `StashByteArray.ExtractByte()` for int→byte narrowing
4. Verify all `arr.*` functions work via Phase 1's dual-dispatch pattern
5. Update `RuntimeValues.Stringify()` for hex display of byte arrays

### Phase 2c: `buf` Namespace

1. Create `Stash.Stdlib/BuiltIns/BufBuiltIns.cs`
2. Implement construction: `buf.from`, `buf.fromHex`, `buf.fromBase64`, `buf.alloc`, `buf.of`
3. Implement conversion: `buf.toString`, `buf.toHex`, `buf.toBase64`
4. Implement inspection: `buf.len`, `buf.get`, `buf.indexOf`, `buf.includes`, `buf.equals`
5. Implement manipulation: `buf.slice`, `buf.concat`, `buf.copy`, `buf.fill`, `buf.reverse`
6. Implement pack/unpack: all `buf.read*`/`buf.write*` functions

### Phase 2d: Existing Stdlib Overloads

1. `crypto.*Bytes` variants in `CryptoBuiltIns.cs`
2. `crypto.randomBytes` overload (no encoding param → byte[])
3. `fs.readBytes`/`fs.writeBytes`/`fs.appendBytes` in `FsBuiltIns.cs`
4. `net.tcpSendBytes`/`net.tcpRecvBytes` in `NetBuiltIns.cs`
5. `net.wsSendBinary` — accept byte[] directly
6. `net.wsRecv` — return byte[] for binary frames
7. `encoding.*` overloads and `*Bytes` variants

### Phase 2e: VM Arithmetic + Serialization

1. Update arithmetic dispatch for `StashValueTag.Byte` promotion
2. Update comparison dispatch
3. Add constant pool tag 16 for byte values in `BytecodeWriter`/`BytecodeReader`

### Phase 2f: Analysis + Tooling

1. Update `TypeInferenceEngine` for byte/byte[] types
2. Add `STA0305`/`STA0306` diagnostics
3. LSP completions, hover, signature help
4. DAP variable display (hex format)
5. Playground/Extension keyword updates

### Phase 2g: Documentation + Tests

1. Update language specification — new primitive type section
2. Update stdlib reference — `buf` namespace + all overloads
3. Create `examples/binary_data.stash`
4. Write xUnit tests (~80–100 tests covering all scenarios in Section 15)

---

## 15. Test Scenarios

### Byte Primitive

```
Byte_FromIntAnnotation_CreatesByte()
Byte_FromHex_CreatesByte()
Byte_Typeof_ReturnsByteString()
Byte_IsByte_ReturnsTrue()
Byte_IsInt_ReturnsFalse()
Byte_IsNotInt()
Byte_Equality_SameValue_True()
Byte_Equality_DifferentValue_False()
Byte_Equality_WithInt_AlwaysFalse()
Byte_Truthiness_ZeroIsFalsy()
Byte_Truthiness_NonZeroIsTruthy()
Byte_ArithmeticWithInt_PromotesToInt()
Byte_BitwiseOps_PromotesToInt()
Byte_Comparison_PromotesToInt()
Byte_OutOfRange_Throws()
Byte_NegativeValue_Throws()
Byte_FloatNarrowing_Truncates()
Byte_ConvToByte_FromInt()
Byte_ConvToByte_FromString()
Byte_ConvToByte_FromHexString()
Byte_ConvToInt_FromByte()
Byte_Stringify_ShowsDecimal()
```

### Byte Array

```
ByteArray_Declaration_CreatesFromIntLiterals()
ByteArray_Declaration_NarrowsIntsToBytes()
ByteArray_Declaration_OutOfRange_Throws()
ByteArray_Typeof_ReturnsByteArrayString()
ByteArray_IsArray_ReturnsTrue()
ByteArray_IsByteArray_ReturnsTrue()
ByteArray_Push_ValidByte_Succeeds()
ByteArray_Push_ValidInt_NarrowsToByte()
ByteArray_Push_OutOfRange_Throws()
ByteArray_IndexRead_ReturnsByte()
ByteArray_IndexWrite_ValidatesRange()
ByteArray_ForIn_IteratesBytes()
ByteArray_Spread_Works()
ByteArray_Stringify_ShowsHex()
ByteArray_JsonStringify_ReturnsBase64()
ByteArray_ArrFilter_PreservesType()
ByteArray_ArrSort_PreservesType()
ByteArray_ArrSlice_PreservesType()
ByteArray_ArrLen_Works()
ByteArray_ArrContains_FindsByte()
ByteArray_ArrMap_ReturnsGenericArray()
ByteArray_ArrNew_PreAllocatesZeros()
ByteArray_ArrTyped_FromGeneric()
```

### `buf` Namespace

```
Buf_From_Utf8_EncodesCorrectly()
Buf_From_Ascii_EncodesCorrectly()
Buf_From_Latin1_EncodesCorrectly()
Buf_FromHex_DecodesCorrectly()
Buf_FromHex_InvalidHex_Throws()
Buf_FromBase64_DecodesCorrectly()
Buf_Alloc_ZeroFilled()
Buf_Alloc_WithFillValue()
Buf_Of_CreatesFromValues()
Buf_ToString_Utf8_DecodesCorrectly()
Buf_ToHex_EncodesCorrectly()
Buf_ToBase64_EncodesCorrectly()
Buf_Len_ReturnsCount()
Buf_IndexOf_FindsByte()
Buf_IndexOf_FindsSubsequence()
Buf_IndexOf_NotFound_ReturnsNegOne()
Buf_Includes_True()
Buf_Includes_False()
Buf_Equals_SameContent_True()
Buf_Equals_DifferentContent_False()
Buf_Equals_DifferentLength_False()
Buf_Slice_CopiesRange()
Buf_Slice_NegativeIndices()
Buf_Concat_MultipleArrays()
Buf_Copy_SourceToDest()
Buf_Fill_Range()
Buf_Reverse_Works()
Buf_ReadUint8_Works()
Buf_ReadUint16BE_Works()
Buf_ReadUint16LE_Works()
Buf_ReadUint32BE_Works()
Buf_ReadInt32BE_SignedNegative()
Buf_ReadInt64BE_Works()
Buf_ReadFloatBE_Works()
Buf_ReadDoubleBE_Works()
Buf_WriteUint16BE_Works()
Buf_WriteUint32LE_Works()
Buf_WriteInt64BE_Works()
Buf_ReadWrite_Roundtrip()
Buf_ReadOutOfBounds_Throws()
Buf_WriteOutOfBounds_Throws()
```

### Stdlib Overloads

```
Crypto_RandomBytes_NoEncoding_ReturnsByteArray()
Crypto_RandomBytes_WithEncoding_ReturnsString()
Crypto_Sha256Bytes_ReturnsByteArray()
Crypto_HmacBytes_ReturnsByteArray()
Fs_ReadBytes_ReadsRawFile()
Fs_WriteBytes_WritesRawFile()
Fs_AppendBytes_AppendsToFile()
Net_TcpSendBytes_SendsRaw()
Net_TcpRecvBytes_ReceivesRaw()
Net_WsSendBinary_AcceptsByteArray()
Net_WsSendBinary_AcceptsBase64String_BackwardCompat()
Net_WsRecv_BinaryFrame_ReturnsByteArray()
Encoding_Base64Encode_AcceptsByteArray()
Encoding_Base64DecodeBytes_ReturnsByteArray()
Encoding_HexEncode_AcceptsByteArray()
Encoding_HexDecodeBytes_ReturnsByteArray()
Conv_ToByte_FromInt()
Conv_ToByte_OutOfRange_Throws()
```

---

## 16. Decision Log

| #   | Decision                                                            | Alternatives Considered                                                 | Rationale                                                                                                                                                                                                                                                                             | Risk                                                                                                                                                                |
| --- | ------------------------------------------------------------------- | ----------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| D1  | `byte` is a new primitive type with its own `StashValueTag`         | Store bytes as ints, use `StashByteSize` reuse, use a `Buffer` struct   | Own tag enables zero-cost tag checking (`val.IsByte`), clean `typeof` returns, and distinct type identity. Storing bytes as ints creates ambiguity — is `42` an int or a byte? The tag resolves it.                                                                                   | New tag means every switch on `StashValueTag` needs a `Byte` case. Blast radius is manageable — most switches already have a default/`Obj` fallback.                |
| D2  | No unique byte literal syntax                                       | `0xFFb` suffix, `b"..."` byte string, `255B` suffix                     | `B` suffix conflicts with `StashByteSize` (`100B`). A new suffix adds lexer complexity for a niche case. Type-annotated declarations (`let b: byte = 0xFF`) are clear and consistent with the typed arrays pattern.                                                                   | Users must use type annotations or `conv.toByte()` — no way to create a byte in expression position without conversion. Acceptable tradeoff.                        |
| D3  | `byte ≠ int` (distinct types, no equality)                          | `byte == int` when values match, byte as subtype of int                 | Stash's core rule: "no type coercion on equality" (`5 != "5"`, `0 != false`). Making byte == int would violate this. `byte` is binary data, `int` is numeric data — different semantics, different types.                                                                             | Users may be surprised that `let b: byte = 42; b == 42` is false. STA0306 diagnostic catches this.                                                                  |
| D4  | Byte promotes to int in arithmetic                                  | Byte arithmetic stays byte (with overflow/wrap), separate byte math ops | Byte arithmetic overflows at 255. Silent wrapping would be a bug magnet. Promoting to int is safe, lossless, and matches C#'s behavior. If a user wants byte-width results, `& 0xFF` is explicit.                                                                                     | Every byte operation returns int, so users building byte arrays from computed values need `conv.toByte()` or rely on the implicit narrowing at `byte[]` boundaries. |
| D5  | Implicit int→byte narrowing at typed boundaries                     | Always require explicit `conv.toByte()`, auto-narrow everywhere         | Typed boundaries (type annotation, `byte[]` push/set) are the natural "I want bytes" declaration. Implicit narrowing there is ergonomic and range-checked. Implicit narrowing _everywhere_ would make `let x = 0xFF` silently produce a byte, breaking backward compat.               | Range errors at runtime if value > 255. Mitigated by clear error messages and static analysis (STA0305).                                                            |
| D6  | New `buf` namespace for binary operations                           | Extend `arr` namespace, extend `encoding` namespace, add to `str`       | Binary data manipulation is a distinct domain with unique operations (endian reads, hex conversion, constant-time comparison). A dedicated namespace keeps it organized and discoverable. `arr.*` is for generic array ops; `buf.*` is for byte-specific binary work.                 | Users need to learn one more namespace. But the namespace name is discoverable and the functions are domain-specific.                                               |
| D7  | Explicit endianness (`BE`/`LE`) on all read/write functions         | Default to platform endianness, default to network byte order (BE)      | Silent endianness bugs are devastating in protocol work — data looks correct on one platform and wrong on another. Forced choice eliminates the entire class of bug. Node.js's `Buffer` and Go's `binary` package both require explicit endianness.                                   | More verbose than a default. But verbosity at binary boundaries is a feature — it documents the protocol.                                                           |
| D8  | `buf.equals` is constant-time                                       | Standard equality (early-exit on mismatch)                              | Byte comparison in crypto contexts (HMAC verification, token comparison) MUST be constant-time to prevent timing attacks. Making this the default for `buf.equals` means crypto-safe comparison is the easy path, not the hard one. `==` retains reference equality for typed arrays. | Slight performance cost for non-crypto comparisons (constant-time is slower). Negligible for sysadmin workloads.                                                    |
| D9  | Breaking change: `net.wsRecv` returns `byte[]` for binary           | Keep base64 string, add separate `net.wsRecvBinary`                     | The base64 encoding was a workaround, not a feature. Keeping it creates a permanent API wart. Binary messages should return binary data. Scripts using binary WebSocket are rare and the migration is simple (`buf.toBase64(msg.data)` to get the old behavior).                      | Breaks existing scripts that process binary WebSocket messages. Migration path is documented and straightforward.                                                   |
| D10 | `crypto.randomBytes` overload detection: no encoding param → byte[] | Always return byte[], add `crypto.randomBytesStr` for string            | Overload by parameter count is clean: `randomBytes(32)` → byte[], `randomBytes(32, "hex")` → string. Both forms remain intuitive. A separate function name adds unnecessary API surface.                                                                                              | Polymorphic return type can confuse type inference. Static analysis handles this: if 1 arg → infer byte[], if 2 args → infer string.                                |

---

## 17. Blast Radius Summary

| Component                                        | Files Affected | Scope                                                                             |
| ------------------------------------------------ | -------------- | --------------------------------------------------------------------------------- |
| **StashValue** (`Stash.Core/Runtime/`)           | 2 files        | Tag enum + value struct (new tag, factory, accessors)                             |
| **StashByteArray** (`Stash.Core/Runtime/Types/`) | 1 new file     | New class ~150 lines                                                              |
| **Parser** (`Stash.Core/Parsing/`)               | 0 files        | No parser changes — `byte` is a regular identifier                                |
| **Bytecode VM** (`Stash.Bytecode/`)              | 3-4 files      | Arithmetic dispatch, TypeOps, Serialization reader/writer                         |
| **New Stdlib** (`Stash.Stdlib/`)                 | 1 new file     | `BufBuiltIns.cs` (~500-600 lines — new namespace)                                 |
| **Modified Stdlib**                              | 5 files        | `CryptoBuiltIns`, `FsBuiltIns`, `NetBuiltIns`, `EncodingBuiltIns`, `ConvBuiltIns` |
| **GlobalBuiltIns**                               | 1 file         | typeof switch                                                                     |
| **SvArgs**                                       | 1 file         | Add `SvArgs.Byte()` extraction method                                             |
| **Analysis**                                     | 2 files        | Type inference for byte/byte[], new diagnostics                                   |
| **LSP**                                          | 2-3 files      | Completions, hover                                                                |
| **DAP**                                          | 1 file         | Variable display (hex format)                                                     |
| **Tests**                                        | 1-2 new files  | ~80-100 new tests                                                                 |
| **Docs**                                         | 2 files        | Language spec, stdlib reference                                                   |
| **Examples**                                     | 1 new file     | `binary_data.stash`                                                               |

---

## 18. Open Questions

1. **Should `str.charCodeAt(s, i)` and `str.fromCharCode(n)` be added alongside the byte type?** These convert between string characters and numeric values. They're adjacent to byte work but orthogonal.
Answer: Add them — they're useful independently and the byte type makes them more relevant.

2. **Should `buf.equals` be the ONLY way to compare byte array contents?** Currently `==` on typed arrays is reference equality (Phase 1 decision). Should `byte[]` override this?
Answer: No — stay consistent with Phase 1. `==` is reference equality for all arrays. `buf.equals` is content equality. Document the difference clearly.

3. **Should `json.parse` produce `byte[]` for base64 fields?** JSON has no byte array type. Currently all strings decode as strings. Adding heuristic base64 detection would be fragile.
Answer: No — `json.parse` returns strings. Use `buf.fromBase64` explicitly on the relevant fields.

4. **Stream-oriented `buf` operations?** Should `buf.reader(data)` / `buf.writer()` provide cursor-advancing sequential read/write? This would simplify protocol parsing (read header, then payload, then checksum — without manual offset tracking).
Answer: Defer to a follow-up. The offset-based API is sufficient for Phase 2. A stream/cursor API can layer on top later if demand warrants it.

5. **Should there be `buf.readVarInt` / `buf.writeVarInt` for variable-length encoded integers?** Used by Protocol Buffers and Kafka.
Answer: Defer — this is protocol-specific. A `@stash/protobuf` package can implement varint on top of the `buf.read*`/`buf.write*` primitives.
