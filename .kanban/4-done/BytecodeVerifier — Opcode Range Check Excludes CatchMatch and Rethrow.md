# Bug: BytecodeVerifier Opcode Range Check Excludes CatchMatch and Rethrow

**Discovered during:** Review of streaming pipe (`|`) implementation
**Priority:** Medium

## Problem

`BytecodeVerifier.VerifyChunk` rejects valid opcodes 94 and 95:

```csharp
// Stash.Bytecode/Bytecode/BytecodeVerifier.cs, line ~47
// 1. Validate opcode range (0–93)
if ((byte)op > 93)
{
    AddError(errors, instrIdx, prefix, $"Invalid opcode {(byte)op}.");
    lastInstrIdx = instrIdx;
    continue;
}
```

But the opcode enum currently defines:
- `Defer = 93`
- `CatchMatch = 94`
- `Rethrow = 95`

`CatchMatch` (94) and `Rethrow` (95) were added as part of the typed-catch feature *after* the verifier was originally written with the `> 93` limit. Any chunk containing a `catch (TypeError e)` clause or a bare `rethrow` will fail verification with a spurious "Invalid opcode" error.

## Secondary Manifestation

The "last instruction should be Return" diagnostic also uses a hardcoded boundary:

```csharp
// line ~323
string opName = (byte)lastOp <= 92 ? lastOp.ToString() : $"op_{(byte)lastOp:x2}";
```

This incorrectly treats `Defer` (93), `CatchMatch` (94), and `Rethrow` (95) as unknown opcodes in diagnostic messages.

## Affected Files

- `Stash.Bytecode/Bytecode/BytecodeVerifier.cs` — two locations:
  1. Range check (`> 93`)
  2. Last-instruction diagnostic (`<= 92`)

## Fix

1. Update the range check to use the actual maximum opcode value. The cleanest approach is to compute it from the enum:
   ```csharp
   int maxOpcode = Enum.GetValues<OpCode>().Cast<int>().Max();
   if ((byte)op > maxOpcode)
       AddError(...);
   ```
   Or simply update the constant to `95` and add a comment linking it to the `OpCode` enum.

2. Add `CatchMatch` and `Rethrow` cases to the verifier's `switch` statement so their operands are validated:
   - `CatchMatch` (ABC): A=errReg (register), B=constant index (string[] type names), C=signed jump offset
   - `Rethrow` (A): A=catch register — no additional validation needed

3. Fix the last-instruction boundary (`<= 92` → use `Enum.IsDefined` or the computed max).

## Reproduction

```stash
try {
    $(false)
} catch (CommandError e) {
    // CatchMatch opcode is emitted here
}
```

Run `BytecodeVerifier.Verify(chunk)` on the compiled chunk — it will report "Invalid opcode 94."
