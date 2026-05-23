# Bytecode — OpCode Metadata Follow-Ups

**Status:** Draft (backlog)
**Created:** 2026-05-15
**Theme:** Tooling / Refactor — direct continuation of
[`Bytecode — OpCode Metadata Centralization`](../2-in-progress/Bytecode%20%E2%80%94%20OpCode%20Metadata%20Centralization.md).
**Priority:** Low — the parent spec's structural goals are already met
(single source of truth, fail-fast coverage assertion, no
`_opNames`/`GetFormat`/per-op switch left in the disassembler). The two items
below are code-quality wins, not bug fixes.

## 1. Motivation

The parent spec landed the centralized `OpCodeAttribute` / `OpCodeMetadata`
infrastructure and migrated every consumer (`Disassembler`, `OpCodeInfo`,
`CfgOpcodeInfo`, `OpcodeOperands`, `BytecodeVerifier`) onto it. Two pieces of
work were deliberately deferred from that PR because they are independent
refinements with their own design decisions that deserve dedicated review:

1. **The §3.2 template-grammar formatter was not implemented.** Every opcode
   currently declares `Operands = OperandTemplate.Bespoke` (see
   [`Stash.Bytecode/Bytecode/OperandTemplate.cs`](../../Stash.Bytecode/Bytecode/OperandTemplate.cs))
   and dispatches to a per-opcode case in
   [`Stash.Bytecode/Bytecode/BespokeOperandFormatters.cs`](../../Stash.Bytecode/Bytecode/BespokeOperandFormatters.cs).
   This keeps disassembly byte-identical to the pre-refactor baseline (which
   was the parent spec's Phase 1 snapshot contract) but leaves the
   ~85-opcodes-worth of regular operand patterns expressed as repetitive
   `($"r{a}, r{b}, r{c}", null)` rows.

2. **`OpcodeOperands.GetWrittenReg` was intentionally not migrated to
   `OpCodeMetadata.GetWrites`.** A few opcodes (`TypedWrap`, `AddI`) read AND
   write `R(A)` in place. Copy-propagation and dead-code elimination must NOT
   treat them as a clean kill of a prior definition of `R(A)`, so the
   structurally-correct `Writes = RegA` flag in the metadata is semantically
   too broad for that consumer. The DCE-specific list lives in
   [`Stash.Bytecode/Optimization/OpcodeOperands.cs::GetWrittenReg`](../../Stash.Bytecode/Optimization/OpcodeOperands.cs)
   with a note explaining the divergence. This is correct behaviour today, but
   the divergence between "structural writes" and "DCE-safe kills" should be
   captured *in the metadata* rather than as a parallel hand-maintained list.

Item 1 is purely cosmetic (deduplication of the bespoke table). Item 2 is a
soundness question about how the metadata schema should model the
read-then-write-same-register case so the optimizer can derive its
classification from it.

## 2. Goal

- Drain the bespoke-formatter table down to a small, *intentionally* irregular
  remainder by introducing the §3.2 template grammar incrementally.
- Give the metadata layer enough resolution to express "writes A but also
  reads A" so the DCE/copy-prop classifier can be derived rather than
  hand-maintained.

**Non-goals:**

- Changing disassembler output for any opcode. The §6 snapshot contract from
  the parent spec still holds — migration must be byte-identical.
- Touching the VM dispatch loop. Same AOT/perf rationale as the parent spec.
- Changing the wire format or any public API of `OpCodeMetadata` /
  `OpCodeAttribute`.

## 3. Item 1 — Adopt the §3.2 template grammar incrementally

### 3.1 Current state

`OperandTemplate` defines a single sentinel constant — `Bespoke = "<bespoke>"` —
and the attribute requires `Operands` to be set on every enum member. Every
opcode is currently `Bespoke`. The disassembler unconditionally hands off to
`BespokeOperandFormatters.Format`, which is a 150-row `switch` keyed by
`OpCode`.

The parent spec's §3.2 listed a target grammar:

```
R(A)  R(B)  R(C)               registers
K(Bx) K(C)                     constant references (auto-annotated)
G(Bx)                          global slot (annotated with name)
U(B)                           upvalue reference (annotated with name)
L(sBx)                         label reference (CollectLabels-resolved)
#B  #C  #Bx                    raw integer
K{StringField}(C)              constant rendered as ".fieldName"
K{Catch}(Bx)                   constant rendered as joined type names
K{Lock}(C)                     constant rendered as LockMetadata options
```

That grammar is not built yet. Building it all at once and migrating ~85
opcodes in one PR is risky — even with the snapshot test, the surface area is
large and the cost of a regression is "every disassembled instruction now
looks subtly wrong".

### 3.2 Design — migrate in waves keyed by template-token closure

The migration target is: every opcode whose disassembly can be expressed
entirely as a token list with no chunk-specific lookups beyond what the
grammar already covers should declare a template string instead of `Bespoke`.
We migrate in waves, each wave gated on a minimal set of grammar tokens being
implemented. Each wave is a self-contained PR with its own snapshot test.

#### Wave A — Plain register triples / pairs / singletons

**Tokens needed:** `R(A)`, `R(B)`, `R(C)`.

**Opcodes migrated:**

- ABC, three-register read+write:
  `Add`, `Sub`, `Mul`, `Div`, `Mod`, `Pow`, `BAnd`, `BOr`, `BXor`, `Shl`,
  `Shr`, `Eq`, `Ne`, `Lt`, `Le`, `Gt`, `Ge`, `GetTable`, `SetTable`, `In`,
  `NewRange`.
- Two-register `R(A), R(B)`:
  `Move`, `Neg`, `Not`, `BNot`, `TypeOf`, `Spread`, `Await`, `TryExpr`,
  `ElevateBegin`.
- Single-register: `LoadNull`, `CloseUpval`, `IterClose`, `Throw`, `Rethrow`,
  `CheckNumeric`, `Defer`, `CallSpread`.

These are exactly the rows in `BespokeOperandFormatters` that today read
`($"r{a}, r{b}, r{c}", null)`, `($"r{a}, r{b}", null)`, or `($"r{a}", null)`.
No comment column. ~35 opcodes.

This wave is the lowest-risk, highest-yield slice — uniform shape, no
annotation columns, and the existing snapshot test pins correctness.

#### Wave B — Constant references

**Tokens added:** `K(Bx)`, `K(C)`, `#B`, `#C`, `#Bx`.

**Opcodes migrated:**

- `LoadK`, `Closure`, `Destructure`, `Import`, `ImportAs`, `Switch`,
  `StructDecl`, `EnumDecl`, `IfaceDecl`, `Extend`, `Retry`, `TypedWrap` —
  `R(A), K(Bx)` with auto-annotation via the existing
  `DisassemblerHelpers.FormatConstant`.
- `AddK`, `SubK`, `EqK`, `NeK`, `LtK`, `LeK`, `GtK`, `GeK` — `R(A), R(B), K(C)`.
- `LoadBool` — `R(A), #B` with conditional `"skip next"` comment (this one is
  borderline — see "Tail" below).
- `NewArray`, `NewDict`, `Interpolate` — `R(A), #B`.
- `Call`, `Test` — `R(A), #C`.

This wave introduces the constant-annotation convention. The renderer reads
`chunk.Constants[bx]` and emits `FormatConstant(...)` as the comment. ~20
opcodes.

`LoadBool`'s conditional `"skip next"` comment is the first encounter with
"comment depends on operand value". Two options:

- **Sub-option (b1):** Keep `LoadBool` in the bespoke set indefinitely.
- **Sub-option (b2):** Extend the grammar with a conditional comment form, e.g.
  `R(A), #B[c!=0:skip next]`. Adds parser complexity for one opcode.

**Decision (Wave B):** Choose **b1**. The grammar should grow only when there
is repeated demand. One-off conditional comments stay bespoke.

#### Wave C — Jump / label references

**Tokens added:** `L(sBx)`.

**Opcodes migrated:** `Jmp`, `JmpFalse`, `JmpTrue`, `Loop`, `ForPrep`,
`ForLoop`, `ForPrepII`, `ForLoopII`, `IterLoop`, `TryBegin`.

The grammar token resolves to `DisassemblerHelpers.GetLabelRef(labels, idx + 1 + sbx)`
and emits the signed offset as the comment (`{sbx:+0;-0}`). ~10 opcodes.

Note: `Jmp` and `Loop` have *no* register operand — the operand line is just
the label. The grammar must permit a single-token operand list. The simplest
form is a literal `L(sBx)` with no `R(A),` prefix; the tokenizer accepts a
comma-separated list of any length including length 1.

#### Wave D — Global / upvalue references

**Tokens added:** `G(Bx)`, `U(B)`.

**Opcodes migrated:** `GetGlobal`, `SetGlobal`, `InitConstGlobal`, `GetUpval`,
`SetUpval`, `UnsetGlobal` (currently `Ax`-format — see "Tail").

Annotation uses `DisassemblerHelpers.FormatGlobal` / `GetUpvalueName`.
`SetGlobal` and `InitConstGlobal` reverse operand order
(`[g{bx}], r{a}` instead of `r{a}, [g{bx}]`). The grammar token order is
literal — the renderer emits tokens in the order they appear in the
template string. ~6 opcodes.

`UnsetGlobal` is `Ax`-format. The token `G(Ax)` is added in this wave for one
opcode; it pulls the slot index from `Instruction.GetAx(word)` instead of
`GetBx(word)`. This is the *only* `Ax` consumer that needs a global token.

#### Wave E — Annotated constant subtypes

**Tokens added:** `K{StringField}(C)`, `K{StringField}(B)`, `K{Catch}(Bx)`,
`K{Lock}(C)`.

**Opcodes migrated:** `GetField`, `Self`, `SetField` (uses B for the field
name slot), `CatchMatch`, `LockBegin`.

These differ from `K(Bx)` only in *how* the constant is rendered in the
comment column (`.fieldName` vs. `"…"` vs. joined catch types vs. lock
options). The grammar token names the renderer to call. ~5 opcodes.

`GetFieldIC` and `CallBuiltIn` belong to this wave shape-wise but have an
extra `[ic:{companionWord}]` comment fragment — they stay bespoke (the
companion word lookup is structurally different and the parent spec already
classifies it via `CompanionWords = OneIC`).

### 3.3 Tail — opcodes that stay bespoke

After all five waves, the following stay in `BespokeOperandFormatters` and
keep `Operands = OperandTemplate.Bespoke`:

| OpCode               | Why                                                                                                |
| -------------------- | -------------------------------------------------------------------------------------------------- |
| `Return`             | Operand list switches form based on `B` (`r{a}` vs. literal `"null"`).                             |
| `LoadBool`           | Conditional `"skip next"` comment (see Wave B).                                                    |
| `AddI`               | `R(A), #sBx` with signed-immediate decoding from `sBx` field — minor, could be `Wave F` later.     |
| `Is`                 | High bit of `C` carries metadata (type-vs-register flag); cannot be modeled as a plain register.   |
| `Timeout`            | Implicit second register `R(A+1)` is not a real operand field.                                     |
| `IterPrep`           | `R(A), #B` plus conditional `"indexed"` comment when `B != 0`.                                     |
| `GetFieldIC`         | Companion-word `[ic:...]` annotation.                                                              |
| `CallBuiltIn`        | Same — companion-word IC plus `({c} args)` comment.                                                |
| `PipeChain`          | `R(A), #B stages, R(C)` plus `"parts from r{c}"` comment.                                          |
| `StreamingPipeline`  | Same shape as `PipeChain`.                                                                         |
| `Redirect`           | Out-of-order operands (`r{a}, r{c}, {b}`).                                                         |
| `NewStruct`          | `R(A), K(B), #C` — irregular because field-name constant lives in `B`, not `C` or `Bx`.            |
| `Command`            | `R(A), #B, #C` plus shell-specific framing.                                                        |
| `TryEnd`, `LockEnd`, `ElevateEnd` | Empty operand lists already render as `""` — could go to a `()` template or stay bespoke; pick whichever is shorter. |

This is roughly 14 opcodes — exactly the "~15 irregular" tail the parent spec
predicted in §3.2.

### 3.4 Grammar engine implementation sketch

A single parser at startup, results cached per template string. Each opcode
that opts in is parsed once during the static-constructor pass that builds
`_table`, then the parsed tree is stored alongside the attribute (or in a
parallel `OpCode`-indexed array). The renderer is a small interpreter over
the token list:

```csharp
internal interface IOperandTemplate
{
    (string operands, string? comment) Render(
        Chunk chunk, Dictionary<int,string> labels, int idx, uint word);
}

// OperandTemplate.Parse(string) → IOperandTemplate
// OperandTemplate.Bespoke (sentinel) → null parse result; disassembler falls
// back to BespokeOperandFormatters.Format(...) as today.
```

Comment composition: each token contributes a fragment; fragments are
concatenated with `", "` (or with a leading space for the first one, depending
on which renderer in `BespokeOperandFormatters` we're emulating — pin this to
the existing output via the snapshot test).

The `Bespoke` sentinel stays — opcodes in the tail set keep using it, and any
*future* new opcode that doesn't fit the grammar can opt in.

### 3.5 Sequencing and acceptance per wave

Each wave is its own PR. Each PR:

1. Adds the new grammar tokens to `OperandTemplate` (parser + renderer).
2. Migrates the wave's opcodes from `Bespoke` to a template string in their
   `[OpCode(...)]` attribute.
3. Removes those opcodes' rows from `BespokeOperandFormatters.Format`.
4. Re-runs the disassembly snapshot test (which already exists from the
   parent spec) and verifies output is byte-identical.

No wave is gated on user-visible behaviour; the only correctness signal is
the snapshot. If a wave regresses a single instruction's output, the wave
reverts atomically.

### 3.6 Decision Log — Item 1

#### Decision: Migrate incrementally, not in one PR

**Chosen:** Five waves, A through E, plus a permanent bespoke tail.

**Alternatives:**

- *One PR, all 85 opcodes migrate at once.* Rejected — the snapshot test
  catches output divergence but doesn't help triage *which* token-renderer
  bug caused the divergence. Bisecting a 150-line PR is expensive.
- *No migration; leave `BespokeOperandFormatters` as-is forever.* Rejected —
  the table is the largest concentration of opcode-specific knowledge left
  in the bytecode layer, and every new opcode pays its tax.

**Rationale:** Waves group opcodes by which grammar tokens they need. Each
wave introduces a minimal grammar surface (3–5 new tokens), migrates a
homogeneous batch (5–35 ops), and ships independently. The bespoke tail is
explicit and named.

**Risks:**

- Some "regular" opcode turns out to have a subtle comment-formatting quirk
  not captured by its wave's tokens. Mitigated by the per-wave snapshot.
- Grammar accretes one-off tokens to absorb each next quirk. Mitigated by
  the "bespoke stays for genuine outliers" policy — items 2 and onward in
  the Tail are *not* candidates for further grammar bloat.

#### Decision: Comment-emitting tokens are part of the grammar (not a separate channel)

**Chosen:** A token like `K(Bx)` emits both an operand fragment (`k{bx}`)
and a comment fragment (`FormatConstant(chunk.Constants[bx])`). The renderer
collects each.

**Alternative:** Template strings declare operand-line tokens only; comments
are always bespoke.

**Rationale:** Constant-annotation comments are the largest single source of
boilerplate in the current bespoke table. Pulling them into the grammar is
the entire point of Wave B. The bespoke escape hatch remains for
non-uniform comments.

## 4. Item 2 — Distinguish in-place writes for DCE / copy-prop

### 4.1 Current state

`OpcodeOperands.GetWrittenReg` is intentionally NOT migrated to
`OpCodeMetadata.GetWrites`. The XML doc on the method explains why:

> *This intentionally does NOT delegate to `OpCodeMetadata.GetWrites`: a few
> opcodes (TypedWrap, AddI) read AND write R(A) in place — copy-propagation/DCE
> must treat them as not having a clean kill of a prior definition, so the
> metadata's "Writes = RegA" (which is structurally true) cannot be the sole
> driver here.*

The list in `GetWrittenReg` enumerates roughly 60 opcodes by name and
mirrors `ChunkBuilder.DceGetWrittenReg`. That list must be kept in sync with
*every* future opcode addition — the same drift hazard the parent spec
eliminated for every other consumer.

The consumers of `GetWrittenReg`:

- `OpcodeOperands.ForEachWrittenReg` (default branch).
- The copy-propagation pass (via the public `GetWrittenReg`).
- `ChunkBuilder.DceGetWrittenReg` (mirrors the same list one-to-one).

The salient semantic property is: **"does this instruction kill any prior
definition of R(A)?"**. For `TypedWrap` and `AddI`, the answer is *no, because
the new value is a function of the old value of R(A)*. For `Add` or `Move`,
the answer is *yes*.

### 4.2 Design — option (a): add a discriminator to `OperandRole`

Two new bit flags on `OperandRole`:

```csharp
[Flags]
public enum OperandRole : byte
{
    None       = 0,
    RegA       = 1 << 0,
    RegB       = 1 << 1,
    RegC       = 1 << 2,
    ConstBx    = 1 << 3,
    ConstC     = 1 << 4,
    JumpSBx    = 1 << 5,
    UpvalB     = 1 << 6,
    GlobalBx   = 1 << 7,
}
```

The flag set is already at 8 bits — we are at the cap of the `byte` backing
type. Extending `OperandRole` to `ushort` is mechanical; do it as part of
this change.

Add **one** new flag:

```csharp
ReadModifyA = 1 << 8,   // R(A) is read AND written in place (TypedWrap, AddI).
                         // Implies RegA in both Reads and Writes; DCE / copy-prop
                         // must NOT treat this as a clean kill of R(A).
```

Why a single new flag and not a separate `OperandRole.WritesAInPlace` distinct
from `Writes`:

- The "writes" axis already captures the structural fact. `ReadModifyA`
  is purely the *additional* assertion that the write depends on the
  pre-existing value of `R(A)`.
- An opcode that has `Reads = RegA | ...` and `Writes = RegA | ...` is
  ambiguous without an explicit marker: it could be `Self` (which writes
  both `R(A)` and `R(A+1)`, where `R(A)` is the result of a method lookup
  on `R(B)` — not a read-modify of `R(A)`) versus `AddI` (where `R(A) =
  R(A) + sBx`). Adding `ReadModifyA` disambiguates.

Affected opcodes (audit; this is the full list expected to set the flag):

- `AddI` — `R(A) = R(A) + sBx`.
- `TypedWrap` — `R(A) = wrap(R(A), K(Bx))`.

Both are already in `OpcodeOperands.RewriteReadRegs` with explanatory
comments noting the in-place dependency. No other opcode currently has this
shape — confirm during implementation by grepping for opcodes that appear in
both `Reads` and `Writes` sets after the attributes are populated.

`GetWrittenReg` then derives from metadata:

```csharp
public static int GetWrittenReg(uint instr)
{
    OpCode op = Instruction.GetOp(instr);
    OperandRole writes = OpCodeMetadata.GetWrites(op);
    OperandRole reads  = OpCodeMetadata.GetReads(op);

    // ReadModifyA means R(A) is updated but the new value depends on the old —
    // not a DCE-safe kill of prior R(A) definitions.
    if ((writes & OperandRole.ReadModifyA) != 0)
        return -1;

    // Multi-register writers (Call, ForPrep, Destructure, Import, Self, …)
    // continue to return -1 from this helper; ForEachWrittenReg handles them.
    // Sentinel: a written set that is exactly { RegA } means "single A write".
    if (writes == OperandRole.RegA)
        return Instruction.GetA(instr);

    return -1;
}
```

The hand-maintained 60-opcode switch goes away. New opcodes get the correct
classification automatically *because* the attribute declares it.

`ChunkBuilder.DceGetWrittenReg` is rewritten to delegate to the same
`OpcodeOperands.GetWrittenReg` (or to `OpCodeMetadata` directly). Today the
two definitions duplicate; centralizing here also kills *that* drift hazard.

### 4.3 Design — option (b): keep `GetWrittenReg` as a hand-audited table

Document the divergence formally in a header comment, lock the list with a
sanity test that asserts every entry in `GetWrittenReg` has `Writes = RegA`
in the metadata (catching the case where someone adds an opcode to the
metadata's RegA-writers but forgets the optimizer list), and stop here.

This is what we have today, minus the assertion test. Adding the assertion
is small.

### 4.4 Recommendation — option (a)

**Chosen:** Option (a) — add `ReadModifyA` to `OperandRole`, widen the enum
backing type to `ushort`, derive `GetWrittenReg` from metadata.

**Rationale:**

- Option (b) preserves the exact drift hazard the parent spec was written
  to eliminate. Every new opcode must be audited against a hand-maintained
  list with no compile-time enforcement.
- The semantic property "this instruction depends on the prior value of its
  destination register" is a fundamental fact about the opcode, not a
  consumer-specific optimization detail. Other future consumers (a
  register allocator with live-range analysis, a more sophisticated LVN
  pass, an SSA-form converter) will all need exactly the same
  distinction. Encoding it once in the metadata pays compounding interest.
- The cost is small: one new flag bit, an enum widening, two opcodes
  (`AddI`, `TypedWrap`) gain `ReadModifyA` in their attribute, the 60-row
  switch in `GetWrittenReg` collapses to a five-line metadata-driven
  derivation. `ChunkBuilder.DceGetWrittenReg` is removed in favour of the
  shared helper.

**Risks:**

- A future opcode that writes `R(A)` but also reads it conditionally
  (only on some path) needs a third discriminator. Defer until concrete —
  no such opcode exists today.
- Widening `OperandRole` from `byte` to `ushort` is binary-compatible at
  source level but anyone serializing the enum directly would break. Audit
  shows no such serialization site (the wire format is in `BytecodeWriter`
  and does not touch `OperandRole`). Confirm during implementation.

### 4.5 Impact map — Item 2

| File                                                              | Change                                                                                                                                                          |
| ----------------------------------------------------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `Stash.Bytecode/Bytecode/OpCodeAttribute.cs`                      | Widen `OperandRole` to `ushort` backing; add `ReadModifyA = 1 << 8`. Document the read-modify semantics.                                                        |
| `Stash.Bytecode/Bytecode/OpCode.cs`                               | Set `Writes = OperandRole.RegA \| OperandRole.ReadModifyA` and `Reads \|= OperandRole.RegA` on `AddI`, `TypedWrap`.                                             |
| `Stash.Bytecode/Optimization/OpcodeOperands.cs`                   | Replace the 60-row `GetWrittenReg` switch with metadata-derived logic. Keep the multi-reg cases in `ForEachWrittenReg` as-is — they remain bespoke by nature.   |
| `Stash.Bytecode/Bytecode/ChunkBuilder.cs` (`DceGetWrittenReg`)    | Delete the local copy; delegate to `OpcodeOperands.GetWrittenReg`.                                                                                              |
| `Stash.Bytecode/Optimization/OpCodeMetadata` consumer-coverage test | New test asserting that every opcode whose attribute declares both `Reads.HasFlag(RegA)` and `Writes.HasFlag(RegA)` also declares `ReadModifyA`, *or* is on an explicit allow-list with a documented exception (e.g., `Self`, whose `R(A)` write is a fresh method-lookup result and not a read-modify). |

The allow-list in the last row is important: it forces the auditor of any
future opcode to make a conscious decision rather than silently skipping the
flag.

## 5. Acceptance Criteria

### Item 1 — template grammar

- [ ] `OperandTemplate` parses the grammar described in §3.2 (Waves A–E
      tokens).
- [ ] Each of Waves A–E has been merged with its own snapshot-verified PR.
- [ ] After Wave E, `BespokeOperandFormatters.Format` handles at most the
      Tail set documented in §3.3 (≤ 15 opcodes).
- [ ] Disassembly output is byte-identical to the pre-refactor baseline
      established by the parent spec's snapshot test, before and after every
      wave.
- [ ] No new opcode added after Wave E is required to declare `Bespoke` —
      one of the grammar tokens covers its shape, OR a Decision Log entry
      explains the new exception.

### Item 2 — read-modify discriminator

- [ ] `OperandRole` is widened to `ushort` backing and includes
      `ReadModifyA`.
- [ ] `AddI` and `TypedWrap` declare `ReadModifyA` in their attribute.
- [ ] `OpcodeOperands.GetWrittenReg` is metadata-derived; the hand-maintained
      60-row switch is deleted.
- [ ] `ChunkBuilder.DceGetWrittenReg` is removed in favour of the shared
      helper.
- [ ] A coverage test asserts every opcode that both reads and writes `R(A)`
      either carries `ReadModifyA` or is on a named allow-list with
      rationale.
- [ ] All existing optimizer tests (`Stash.Tests/Bytecode/...` for DCE,
      copy-prop, LVN) pass without changes.

## 6. Test Plan

### Item 1

1. **Per-wave snapshot test.** Reuse the disassembly snapshot fixture from the
   parent spec (the fixture script that exercises every opcode). Each wave PR
   re-runs it; output must be byte-identical.
2. **Grammar parser unit test.** Token-list parser tests for each grammar
   form: registers, constants, labels, globals, upvalues, annotated
   constants. Cover malformed inputs (missing parens, unknown token) — these
   must throw at startup, *not* at first disassembly.
3. **Bespoke-coverage assertion.** A test that iterates every opcode and
   asserts: if its attribute declares `Bespoke`, it appears in
   `BespokeOperandFormatters.Format`'s switch; if it declares a template, it
   does not. Catches the "removed from bespoke table but forgot to update
   the attribute" mistake.

### Item 2

4. **Read-modify coverage test.** Iterate every opcode; for any where
   `Reads.HasFlag(RegA) && Writes.HasFlag(RegA)`, assert either
   `Writes.HasFlag(ReadModifyA)` or membership in the documented allow-list.
5. **`GetWrittenReg` equivalence test.** Before the metadata-driven
   rewrite, snapshot the current `(OpCode → written-reg-or-minus-one)`
   mapping for every opcode against a synthetic instruction word. After the
   rewrite, the mapping must match exactly. Pins behaviour across the
   refactor.
6. **Optimizer regression suite.** Re-run all DCE / copy-prop / LVN tests
   under both options. No semantic change is intended; any test failure
   indicates a misclassified opcode.

## 7. Adjacent — out of scope

`StandardLibraryReferenceTests.GeneratedReference_MatchesCheckedInDoc`
currently fails on `main` because the on-disk
`docs/Stash — Standard Library Reference.md` has drifted from the
`Stash.Docs` generator output. This is **not** part of this metadata
follow-up. The active spec covering the stdlib reference contract is at
[`.kanban/2-in-progress/Standard Library Reference — Generated Contract and Quality Rewrite.md`](../2-in-progress/Standard%20Library%20Reference%20%E2%80%94%20Generated%20Contract%20and%20Quality%20Rewrite.md);
the drift fix belongs there or in a dedicated spec, not bundled with the
opcode-metadata work.

## 8. Open Questions

1. **Grammar engine — array of parsed `IOperandTemplate` instances vs.
   re-parse each call?** Re-parsing is obviously wasteful but the
   one-time-at-startup parse needs somewhere to live. Suggest a sibling
   array keyed by `(byte)OpCode`, populated alongside `_table` in
   `OpCodeMetadata`'s static constructor. Decide during Wave A.
2. **Empty operand lists** (`TryEnd`, `LockEnd`, `ElevateEnd`) — assign
   them the explicit empty template `""` so they leave the bespoke set, or
   keep them bespoke for symmetry with the (non-empty) other end-opcodes
   (`Return`, `IterClose`)? Lean toward template empty; decide in Wave A.
3. **Does `Self` need `ReadModifyA`?** It writes `R(A)` *and* `R(A+1)`,
   and the `R(A)` write is the method lookup result, not a read-modify.
   Confirmed: no, `Self` is on the allow-list, not a `ReadModifyA`
   candidate. Document in the test.
