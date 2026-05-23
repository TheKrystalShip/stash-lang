# Bytecode — OpCode Metadata Centralization

**Status:** Draft (backlog)
**Created:** 2026-05-14
**Theme:** Tooling / Refactor (cross-cutting; touches Bytecode, Optimization, VM verification)
**Priority:** Medium — no user-visible breakage today, but already producing latent disassembly bugs and will compound with every new opcode.

## 1. Problem

Each `OpCode` value's metadata — mnemonic, encoding format, operand shape, register read/write roles, branching behaviour, companion-word count — is currently re-declared in **four** separate switch statements / dictionaries that must be kept in lock-step by hand:

| File                                                                                                       | What it duplicates                                                                                                              |
| ---------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------- |
| [`Stash.Bytecode/Bytecode/Disassembler.cs`](../../../Stash.Bytecode/Bytecode/Disassembler.cs)              | `_opNames` mnemonic map, private `GetFormat` switch, per-opcode `FormatInstruction` operand renderer, companion-word skip logic |
| [`Stash.Bytecode/Bytecode/OpCode.cs`](../../../Stash.Bytecode/Bytecode/OpCode.cs) (`OpCodeInfo.GetFormat`) | A _second_ format switch                                                                                                        |
| [`Stash.Bytecode/Optimization/CfgOpcodeInfo.cs`](../../../Stash.Bytecode/Optimization/CfgOpcodeInfo.cs)    | Branching/companion-word classification                                                                                         |
| [`Stash.Bytecode/Optimization/OpcodeOperands.cs`](../../../Stash.Bytecode/Optimization/OpcodeOperands.cs)  | Register read/write classification                                                                                              |

Adding a new opcode requires updating each table by hand. The compiler emits no warning when one is forgotten — the failure surfaces later as wrong disassembly output, mis-classified CFG edges, or skipped operand validation.

### Latent bugs already present from this drift

1. **`UnsetGlobal` (opcode 98) is entirely missing from the disassembler.** It appears in [`OpCode.cs:258`](../../../Stash.Bytecode/Bytecode/OpCode.cs#L258) but is absent from `_opNames`, the format switch, and `FormatInstruction`. Disassembled output prints it as `op_62` with default ABC formatting.
2. **The two `GetFormat` definitions disagree.** [`Disassembler.cs:165-167`](../../../Stash.Bytecode/Bytecode/Disassembler.cs#L165) classifies `IterPrep` as `AsBx`; [`OpCodeInfo.GetFormat`](../../../Stash.Bytecode/Bytecode/OpCode.cs#L290) leaves it as ABC. At most one is correct.
3. **`BytecodeVerifier` previously had a hardcoded opcode range** (`> 93`), recently fixed in a separate bug spec — the same class of drift this proposal eliminates structurally.

## 2. Goal

Make the `OpCode` enum the **single source of truth** for everything a consumer needs to know about an opcode, by decorating each enum member with an attribute carrying all metadata. Reduce every consumer (`Disassembler`, `OpCodeInfo`, `CfgOpcodeInfo`, `OpcodeOperands`, `BytecodeVerifier`) to reading from one centrally-built lookup. Adding a new opcode without complete metadata should fail at startup (assertion) — not silently produce wrong output later.

**Non-goals:**

- Changing the wire format of `.stashc` files.
- Changing dispatch performance in `RunInner<TDebugMode>()` — metadata lookup is a tooling concern, not a hot-path concern.
- Replacing the per-opcode `switch` in `VirtualMachine.Dispatch.cs`. (Dispatch needs the switch for AOT codegen; see [Bytecode VM CLAUDE.md "Dispatch Loop Size Limit"](../../../Stash.Bytecode/CLAUDE.md).)

## 3. Design

### 3.1 The `[OpCode(...)]` attribute

```csharp
[AttributeUsage(AttributeTargets.Field, AllowMultiple = false)]
public sealed class OpCodeAttribute : Attribute
{
    /// <summary>Disassembly mnemonic, e.g. "load.k", "get.field.ic".</summary>
    public required string Mnemonic { get; init; }

    /// <summary>Instruction encoding format.</summary>
    public required OpCodeFormat Format { get; init; }

    /// <summary>Operand-shape template — see §3.2.</summary>
    public required string Operands { get; init; }

    /// <summary>One-line description (used by disassembler section headers / docs).</summary>
    public required string Summary { get; init; }

    /// <summary>Which operand positions this opcode writes to. Default: RegA.</summary>
    public OperandRole Writes { get; init; } = OperandRole.RegA;

    /// <summary>Which operand positions this opcode reads from. Default: none.</summary>
    public OperandRole Reads { get; init; } = OperandRole.None;

    /// <summary>True if this opcode can transfer control (jumps, returns, throws, loop heads).</summary>
    public bool IsBranching { get; init; }

    /// <summary>True if this opcode terminates a basic block (Jmp/Return/Throw/Rethrow + the for-loops + TryEnd).</summary>
    public bool IsTerminator { get; init; }

    /// <summary>How many companion words follow this opcode (Closure: variable; GetFieldIC/CallBuiltIn: 1; PipeChain: stage count from B).</summary>
    public CompanionWordKind CompanionWords { get; init; } = CompanionWordKind.None;
}

[Flags]
public enum OperandRole : byte
{
    None = 0,
    RegA = 1 << 0,
    RegB = 1 << 1,
    RegC = 1 << 2,
    ConstBx = 1 << 3,
    ConstC = 1 << 4,
    JumpSBx = 1 << 5,
    UpvalB = 1 << 6,
    GlobalBx = 1 << 7,
}

public enum CompanionWordKind : byte
{
    None,
    OneIC,             // GetFieldIC, CallBuiltIn — exactly 1 companion word
    UpvalueDescriptors,// Closure — count = nested chunk's Upvalues.Length
    PipeStages,        // PipeChain, StreamingPipeline — count from operand B
}
```

`required` keeps every new opcode honest: omit a field and the C# compiler errors at the attribute construction site.

### 3.2 Operand-shape template

Most disassembler output follows a small grammar. The template string is interpreted by a shared formatter:

```
Tokens:
  R(A)   R(B)   R(C)             — register references
  K(Bx)  K(C)                    — constant pool reference (with auto-annotation)
  G(Bx)                          — global slot reference (annotated with name)
  U(B)                           — upvalue reference (annotated with name)
  L(sBx) — label reference (resolved via CollectLabels)
  #B  #C  #Bx                    — raw integer operand
  K{StringField}(C)              — constant rendered as ".fieldName"
  K{Catch}(Bx)                   — constant rendered as joined type names
  K{Lock}(C)                     — constant rendered as LockMetadata options
```

Examples:

```csharp
LoadK       => Operands = "R(A), K(Bx)"
GetField    => Operands = "R(A), R(B), K{StringField}(C)"
JmpFalse    => Operands = "R(A), L(sBx)"
Call        => Operands = "R(A), #C"
Return      => Operands = "Return"   // sentinel — uses bespoke renderer
```

Roughly 85 of the ~101 opcodes fit the template grammar. The remaining ~15 (closure upvalue descriptor walking, pipe-chain companion-word annotation, `LockBegin`'s `R(B+1)`/`R(B+2)` register pair, etc.) declare `Operands = "<bespoke>"` and stay as named cases in a `BespokeFormatters` static class keyed by `OpCode`. The set of bespoke formatters lives in **one** file, not scattered across switches.

### 3.3 The central lookup — `OpCodeMetadata`

```csharp
public static class OpCodeMetadata
{
    private static readonly OpCodeAttribute?[] _table = BuildTable();

    public static OpCodeAttribute Get(OpCode op)
    {
        var entry = _table[(byte)op];
        if (entry is null)
            throw new InvalidOperationException($"Missing [OpCode] attribute on {op}.");
        return entry;
    }

    public static OpCodeFormat GetFormat(OpCode op) => Get(op).Format;
    public static string GetMnemonic(OpCode op) => Get(op).Mnemonic;
    // ... etc.

    static OpCodeMetadata()
    {
        // Fail fast at process startup if any enum member lacks an attribute.
        foreach (OpCode op in Enum.GetValues<OpCode>())
        {
            if (_table[(byte)op] is null)
                throw new InvalidOperationException(
                    $"OpCode.{op} ({(byte)op}) is missing [OpCode] attribute. " +
                    "Every enum member must declare metadata in OpCode.cs.");
        }
    }

    private static OpCodeAttribute?[] BuildTable() { /* reflection over enum fields */ }
}
```

Reflection cost is paid once per process at type-initialization time. Lookup is O(1) array index. No allocations on the disassembly hot path.

### 3.4 Consumer migration

| Consumer                         | Change                                                                                                                                                      |
| -------------------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `Disassembler._opNames`          | Delete. Replace usage with `OpCodeMetadata.GetMnemonic(op)`.                                                                                                |
| `Disassembler.GetFormat`         | Delete. Use `OpCodeMetadata.GetFormat(op)`.                                                                                                                 |
| `Disassembler.FormatInstruction` | Replace giant switch with a template-driven formatter that handles ~85 ops generically and dispatches to `BespokeFormatters[op]` for the rest.              |
| `Disassembler.CollectLabels`     | Use `Companion.Words` and `IsBranching` from metadata instead of hardcoded checks for `Closure`/`GetFieldIC`/`CallBuiltIn`/`PipeChain`/`StreamingPipeline`. |
| `OpCodeInfo.GetFormat`           | Delegate to `OpCodeMetadata.GetFormat`. (Keep the public API for backwards compatibility with external consumers.)                                          |
| `CfgOpcodeInfo`                  | Use `Attr.IsTerminator` / `Attr.IsBranching` / `Attr.CompanionWords`.                                                                                       |
| `OpcodeOperands`                 | Use `Attr.Reads` / `Attr.Writes`.                                                                                                                           |
| `BytecodeVerifier`               | Use the metadata to validate operand ranges and detect unknown opcodes via the `_table[(byte)op] is null` check instead of hardcoded numeric bounds.        |

### 3.5 AOT considerations

The CLI is published with `dotnet publish -c Release` for AOT. Reflection over enum fields + custom attributes is supported under .NET Native AOT as long as the enum type and attribute type are not trimmed. Two safeguards:

1. Mark `OpCodeAttribute` with `[DynamicallyAccessedMembers(...)]` annotations where reflection touches it.
2. Add a small benchmark/smoke test that loads `OpCodeMetadata.GetMnemonic(OpCode.LoadK)` under the published AOT binary, to catch trimming regressions immediately rather than as silent runtime errors.

If AOT trimming proves hostile to the reflection approach, the fallback is a source generator that emits the same static table at compile time from the same attributes — the public API stays identical. We won't pre-empt that complexity; we'll add it only if AOT fails.

## 4. Decision Log

### Decision: Attributes on enum members vs. a static partial-class table

**Chosen:** Attributes on enum members.

**Alternatives considered:**

- _Static `Dictionary<OpCode, OpCodeMetadata>` defined in a partial class._ Rejected — same drift hazard as today, just one consolidated table instead of four. Doesn't structurally prevent the "forgot to register" failure.
- _Source generator emitting the table from XML doc comments._ Rejected — XML comments aren't typed, so format/role fields would be stringly-encoded. Brittle.
- _Source generator from attributes._ Deferred — strictly an AOT-trimming fallback. Adds build complexity for no functional gain in the JIT case.

**Rationale:** Attributes co-locate metadata with the enum member. `required` properties make omission a compile error at the attribute call site. A static-constructor assertion guarantees `Enum.GetValues<OpCode>()` is fully covered before any consumer runs.

**Risks:**

- AOT trimming might strip attributes — mitigated by §3.5.
- Reflection at startup adds a one-time cost (~milliseconds for 101 entries). Acceptable for a tooling table.

### Decision: Template-driven operand rendering vs. keep per-op renderer

**Chosen:** Template grammar for the ~85 regular opcodes; named bespoke formatters for the ~15 irregular ones.

**Alternatives considered:**

- _Pure template grammar, no escape hatch._ Rejected — pipe-chain companion words and closure upvalue descriptors require multi-word rendering that doesn't fit a single-instruction grammar.
- _Keep `FormatInstruction`'s per-op switch, just consult metadata for mnemonic/format._ Rejected (this is option **(a)** from the investigation). Half-measure: solves "forgot the mnemonic" but not "forgot the operand renderer". Doesn't close the loop.

**Rationale:** The grammar covers the long tail cheaply, and the bespoke set is small enough to live in one file. New ops default to the grammar; only genuinely irregular ones opt out.

**Risks:**

- Template parser is a new ~150-line component to maintain. Mitigated by keeping the grammar fixed and small.

### Decision: Static-constructor assertion vs. Roslyn analyzer

**Chosen:** Static-constructor assertion that throws if any `OpCode` value lacks an attribute.

**Alternatives considered:**

- _Roslyn analyzer emitting a build-time diagnostic._ Stronger guarantee but significant added complexity (analyzer project, MSBuild wiring, CI integration).

**Rationale:** Assertion catches the bug on every test-suite run and every `stash` invocation. Test coverage already executes the path. Analyzer can be added later if the assertion proves insufficient.

**Risks:**

- A new opcode added but never exercised by a test could slip past local development. Mitigated by adding one test that _just_ loads `OpCodeMetadata` and exercises `Get` for every enum value.

## 4a. In-scope companion fixes (land before the refactor)

These are tiny, surface-level corrections that the larger refactor would eventually subsume. We land them **first**, in the same spec, for two reasons: (1) they're real bugs shipping today that shouldn't wait on a multi-file refactor, and (2) doing them up front lets the refactor's snapshot tests use _correct_ output as the baseline, instead of baking the bugs into the comparison.

### Fix 1 — `UnsetGlobal` (opcode 98) missing from disassembler

[`Stash.Bytecode/Bytecode/Disassembler.cs`](../../../Stash.Bytecode/Bytecode/Disassembler.cs) currently omits this opcode from `_opNames` and from `FormatInstruction`. Add:

```csharp
// in _opNames:
[OpCode.UnsetGlobal] = "unset.global",

// in FormatInstruction (Ax format, slot index in Ax):
OpCode.UnsetGlobal => ($"[g{Instruction.GetAx(word)}]", FormatGlobal(chunk, (ushort)Instruction.GetAx(word))),
```

The mnemonic `unset.global` mirrors the existing `set.global` / `init.const.global` convention.

### Fix 2 — `IterPrep` mis-classified as `AsBx` in `Disassembler.GetFormat`

The correct format is **ABC**, confirmed by:

- The emitter uses `EmitAB(OpCode.IterPrep, iterableReg, hasIndex ? 1 : 0)` ([`Compiler.ControlFlow.cs:193`](../../../Stash.Bytecode/Compilation/Compiler.ControlFlow.cs#L193)) — only A and B populated, C unused, no signed offset.
- The VM handler `ExecuteIterPrep` reads only `GetA(inst)` and `GetB(inst)` ([`VirtualMachine.ControlFlow.cs:594-599`](../../../Stash.Bytecode/VM/VirtualMachine.ControlFlow.cs#L594)) — no jump-target use of `sBx`.
- `OpCodeInfo.GetFormat` already classifies it as ABC (the default fall-through).

Remove `IterPrep` from the `AsBx` group in [`Disassembler.cs:165-167`](../../../Stash.Bytecode/Bytecode/Disassembler.cs#L165). The current output happens to render correctly only because `FormatInstruction` ignores the `sBx` value for `IterPrep` — but the format misclassification is also consulted by `CollectLabels`, which currently creates a phantom label from the misinterpreted offset. Fix surfaces a real (if low-impact) labeling bug.

While we're there: update `FormatInstruction` to also surface the B operand (`R(A), #B` where B is the `indexed` flag) — currently the disassembly drops it entirely, which is misleading.

### Why these are in-scope here, not separate specs

Both fixes are < 10 lines each and live in the same file the refactor gutts. Splitting them into separate specs would just create three PRs that all touch `Disassembler.cs` — guaranteed merge friction. Treat 4a as "phase 0" of this spec: a focused first commit, mergeable on its own, that the refactor commits build on.

## 5. Affected Files

**Modified:**

- `Stash.Bytecode/Bytecode/OpCode.cs` — add `[OpCode(...)]` to each enum member; keep `OpCodeInfo` as a thin facade.
- `Stash.Bytecode/Bytecode/Disassembler.cs` — gut `_opNames`, `GetFormat`, `FormatInstruction`; replace with template formatter + bespoke dispatch.
- `Stash.Bytecode/Optimization/CfgOpcodeInfo.cs` — replace hardcoded switches with metadata reads.
- `Stash.Bytecode/Optimization/OpcodeOperands.cs` — replace hardcoded switches with `Reads`/`Writes` reads.
- `Stash.Bytecode/Bytecode/BytecodeVerifier.cs` — use metadata for opcode-range validation.

**Created:**

- `Stash.Bytecode/Bytecode/OpCodeAttribute.cs` — the attribute + `OperandRole` + `CompanionWordKind` enums.
- `Stash.Bytecode/Bytecode/OpCodeMetadata.cs` — central lookup with startup validation.
- `Stash.Bytecode/Bytecode/OperandTemplate.cs` — grammar tokenizer + renderer.
- `Stash.Bytecode/Bytecode/BespokeOperandFormatters.cs` — the ~15 irregular cases.

**No changes:**

- `Stash.Bytecode/VM/VirtualMachine.Dispatch.cs` — dispatch switch stays exactly as-is (perf-critical, AOT-bound).
- `Stash.Bytecode/Serialization/BytecodeWriter.cs` / `BytecodeReader.cs` — wire format unchanged.

## 6. Test Plan

### Phase 0 — companion fixes (§4a)

1. **`UnsetGlobal` disassembly test** — compile `unset x;` and assert the disassembly contains `unset.global` with the global slot rendered and the variable name annotated. (Today this prints `op_62 r0, r0, r0`.)
2. **`IterPrep` label-collection test** — compile a `for x in [1,2,3] { ... }` loop and verify `CollectLabels` no longer emits a spurious label derived from the misinterpreted `sBx`. Also assert the disassembly now surfaces the `indexed` B flag.

### Phase 1 — refactor

3. **Coverage assertion test** — iterate every `OpCode` value and call `OpCodeMetadata.Get(op)`. Any missing entry fails the test before runtime.
4. **Disassembly snapshot test** — compile a fixture script that exercises every opcode. Capture the snapshot _after_ the Phase 0 fixes land but _before_ Phase 1. The Phase 1 snapshot must be byte-identical.
5. **Format consistency test** — assert `OpCodeMetadata.GetFormat(op) == OpCodeInfo.GetFormat(op)` for every opcode. After Phase 0 these two functions agree; the test pins that invariant.
6. **CFG regression test** — run the existing `Bytecode Optimizer` test suite; basic-block boundaries must be identical pre/post refactor.
7. **AOT smoke test** — `dotnet publish -c Release` the CLI, run `stash --disassemble examples/lock.stash`, confirm output is non-empty and matches the JIT disassembly.

## 7. Acceptance Criteria

**Phase 0 (companion fixes):**

- [ ] `UnsetGlobal` has an entry in `_opNames` rendering as `unset.global` and a `FormatInstruction` case rendering the Ax-encoded global slot with name annotation.
- [ ] `IterPrep` is removed from the `AsBx` group in `Disassembler.GetFormat` (matches `OpCodeInfo.GetFormat`).
- [ ] `IterPrep` disassembly renders `R(A), #B` so the `indexed` flag is visible.
- [ ] Phase 0 tests from §6 pass.

**Phase 1 (refactor):**

- [ ] Every `OpCode` enum member has an `[OpCode(...)]` attribute with all `required` fields filled.
- [ ] `OpCodeMetadata` static constructor asserts full coverage at process start.
- [ ] `Disassembler` no longer contains `_opNames`, no `GetFormat` switch, no per-op operand switch (only the bespoke-formatter dispatch).
- [ ] `OpCodeInfo.GetFormat`, `CfgOpcodeInfo`, `OpcodeOperands`, and `BytecodeVerifier` consult `OpCodeMetadata` rather than their own switches.
- [ ] All existing tests pass; new coverage/format/snapshot tests pass.
- [ ] AOT-published binary produces correct disassembly output.

## 8. Resolved Questions

1. **`IterPrep` format is ABC** (not AsBx). Confirmed by reading the emitter ([`Compiler.ControlFlow.cs:193`](../../../Stash.Bytecode/Compilation/Compiler.ControlFlow.cs#L193) uses `EmitAB`) and the VM handler ([`VirtualMachine.ControlFlow.cs:594-599`](../../../Stash.Bytecode/VM/VirtualMachine.ControlFlow.cs#L594) reads only A and B, never `sBx`). The `Disassembler` classification is wrong; the fix is in §4a.
2. **Mnemonic stability** — existing mnemonics (`load.k`, `get.field.ic`, etc.) are a public contract; no renames. `UnsetGlobal` gets the new mnemonic `unset.global` (mirrors `set.global` / `init.const.global`).
3. **`OperandRole.RegA` semantics** — `Writes` means "writes in at least one execution path". Consumers that care about conditional writes (e.g., LVN handling `TestSet`) annotate at use site rather than splitting the role flag. Keeps the metadata schema small.
