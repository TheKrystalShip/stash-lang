# Readable Disassembly — Review

> Produced by `/feature-review`. One finding per H2 section.

**Scope reviewed:** commits `43919a2..3b6d3c5` on branch `main`
**Brief:** ./brief.md
**Generated:** 2026-05-17

---

## F01 — [HIGH] CallBuiltIn comment changes in Compact mode — violates byte-for-byte regression contract

**Status:** fixed
**Fixed in:** 74e5ff1
**Files:** `Stash.Bytecode/Bytecode/BespokeOperandFormatters.cs:269-276`, `Stash.Bytecode/Bytecode/Disassembler.cs:306`
**Phase:** P1 (carried through to P3)
**Commit:** 29ca068

### Observation

The new `CallBuiltIn` bespoke formatter unconditionally calls
`Disassembler.ResolveBuiltinCallName(chunk, idx)` and assigns the result as the
instruction's `comment`. `FormatInstruction` runs in both compact and verbose modes,
and `EmitInstruction` emits the bespoke `comment` regardless of `options.Compact`
(see Disassembler.cs:306, 319-329 — only the *annotation* path is gated on
`!options.Compact` at line 315; the `comment` is not).

Pre-feature, Compact mode showed `(2 args) [ic:4]`. Post-feature, Compact mode now
shows `io.println(2 args)` or `.println(2 args)`. This is not a byte-for-byte change.

The P3 regression test `Disassemble_CompactMode_ByteForByteGoldenRegression`
(DisassemblerTests.cs:608-635) emits only `Move` + `Return` — it never exercises
`CallBuiltIn` in compact mode, so it does not catch this.

### Why this matters

The brief's acceptance criterion is explicit: *"Compact mode output is byte-for-byte
identical to today's Compact output (regression test)."* This is documented in
`brief.md:175-176` and as the third `done_when` bullet for P3. The current
implementation silently changes Compact output for every `call.builtin` line — the
most common opcode the feature touches. Any consumer that pipes `--disassemble`
through tooling expecting the old Compact format (diff workflows, golden bytecode
snapshots in downstream projects) will break.

### Suggested fix

In `Stash.Bytecode/Bytecode/BespokeOperandFormatters.cs`, gate the CallBuiltIn
formatter on the disassembler options, or move the resolution into `EmitInstruction`
so it can be suppressed for Compact mode. One approach: expose `DisassemblerOptions`
to the bespoke formatter (the signature already takes the chunk; adding a flag is a
minor type change) and return the original `($"({c} args) [ic:{icWord}]")` text
when `options.Compact` is true.

Add a CallBuiltIn-specific compact-mode golden test that locks in the legacy
`(N args) [ic:M]` form so a future regression is caught.

### Verify

```
dotnet test --filter "FullyQualifiedName~DisassemblerTests"
```

A new test should compile a script containing `io.println(...)` with
`DisassemblerOptions { Compact = true }` and assert the output contains
`[ic:` and does NOT contain `io.println`.

---

## F02 — [MEDIUM] Backward-scan stop condition treats non-jump AsBx opcodes as jumps

**Status:** fixed
**Fixed in:** 44f0be8
**Files:** `Stash.Bytecode/Bytecode/Disassembler.cs:687-689`
**Phase:** P1
**Commit:** 29ca068

### Observation

`ResolveBuiltinCallName` walks backward looking for the writer of the receiver
register. Its termination condition is:

```csharp
if (OpCodeMetadata.IsDefined((byte)op) && OpCodeMetadata.GetFormat(op) == OpCodeFormat.AsBx)
    break;
```

`OpCodeFormat.AsBx` is used by *more* than jumps. The `OpCode` table (OpCode.cs)
declares the following opcodes as `Format = OpCodeFormat.AsBx`:

- `addi` — signed-immediate add; writes RegA
- `for.prep`, `for.loop`, `for.prepII`, `for.loopII`, `iter.loop` — loop ops; write RegA
- `try.begin` — exception handler; writes RegA
- `jmp`, `jmp.false`, `jmp.true`, `loop` — actual jumps

The intended discriminator is the `IsBranching` flag (already on `[OpCode]`), not
the encoding format. Today the scan terminates early on any of the eight non-jump
AsBx opcodes, even when they don't write the receiver register and a valid
`GetGlobal`/`GetUpval`/`Move` exists just past them within the 8-step budget.

### Why this matters

The brief specifies the scan "stops at any label target, jump, or non-name-producing
opcode" (brief.md:134-135). `addi`, `for.prep`, `iter.loop`, etc. are not jumps —
they are perfectly valid intervening instructions. The current implementation
silently downgrades the namespace prefix to the method-only fallback (`.println`
instead of `io.println`) whenever any of these opcodes appears within the backward
window. This isn't a correctness contract per the brief (it's "best-effort
pretty-printing"), but it makes the resolution noticeably less useful for real
scripts that contain loops or immediate arithmetic before a builtin call.

### Suggested fix

Replace the format-based check with the explicit branch flag:

```csharp
if (OpCodeMetadata.IsDefined((byte)op) && OpCodeMetadata.IsBranching(op))
    break;
```

(Or whatever the existing accessor for `IsBranching` is — verify by reading
`OpCodeMetadata.cs`.) Add a test that places `AddI` or `ForLoop` between the
`GetGlobal` namespace setup and the `CallBuiltIn` and confirms the namespace is
still recovered.

### Verify

```
dotnet test --filter "FullyQualifiedName~DisassemblerTests"
```

---

## F03 — [MEDIUM] "MethodOnlyFallback" test does not exercise the fallback path

**Status:** fixed
**Fixed in:** 89f2f73
**Files:** `Stash.Tests/Bytecode/DisassemblerTests.cs:380-391`
**Phase:** P1
**Commit:** 29ca068

### Observation

The test `Disassemble_CallBuiltIn_MethodOnlyFallbackWhenNamespaceUnknown` is named
for, and documents in its body comment, the method-only fallback path
(`.println(2 args)` form). The actual test body compiles `math.sqrt(4);` —
which fully resolves to `math.sqrt` — and asserts only `Assert.Contains("math.sqrt",
output)` and `Assert.DoesNotContain("[ic:", output)`. The fallback branch
(`nsName == null`, producing `.method(...)`) is never executed.

### Why this matters

P1's third `done_when` bullet states: *"When the scan fails, the comment falls back
to the method-only form (e.g. ".println(2 args)") and never prints "[ic:N]" for a
normal CallBuiltIn."* No test currently locks in the fallback shape. A regression
that produces `(2 args)` (no method) or `[ic:N]` (no resolution at all) when the
backward scan fails would pass the suite undetected.

### Suggested fix

Hand-build a chunk via `ChunkBuilder` where the receiver register is established
by an opcode the backward scan does NOT recognize (e.g. an arithmetic result or an
intervening jump), then emit `CallBuiltIn` with a valid `ICSlots[0].ConstantIndex`
pointing at a string constant `"println"`. Assert the disassembly contains
`.println(` and does NOT contain `io.println`, `[ic:`.

### Verify

```
dotnet test --filter "FullyQualifiedName~DisassemblerTests"
```

---

## F04 — [LOW] Compact-mode regression test does not cover the `.locals:` section

**Status:** fixed
**Fixed in:** 89f2f73
**Files:** `Stash.Tests/Bytecode/DisassemblerTests.cs:608-635`
**Phase:** P3
**Commit:** 3b6d3c5

### Observation

The `Disassemble_CompactMode_ByteForByteGoldenRegression` test asserts the absence
of `r0=`, `r1=`, and `  |  ` (the P3 annotation tokens) but does not assert the
absence of `.locals:` (the P2 section header) in compact mode. P2 includes a
separate `Disassemble_LocalsSection_AbsentInCompactMode` test that does cover
this, so today there is full coverage — but the dedicated "byte-for-byte
regression" test is the natural place to keep all compact-mode invariants
together, and a future refactor that breaks the P2 guard would only fail one test
instead of two.

### Why this matters

Defense-in-depth for the byte-for-byte contract. Low priority — coverage exists
elsewhere — but cheap to consolidate.

### Suggested fix

Add to the regression test:

```csharp
Assert.DoesNotContain(".locals:", compactOutput);
```

Optionally also add `Assert.DoesNotContain(".const:", compactOutput)` etc. to lock
in all header-section suppressions in one place.

### Verify

```
dotnet test --filter "FullyQualifiedName~Disassemble_CompactMode"
```

---

## F05 — [INFO] P3 verify command papered over by grep workaround

**Status:** fixed
**Fixed in:** 89f2f73
**Files:** `.kanban/2-in-progress/readable-disassembly/plan.yaml:78`
**Phase:** P3
**Commit:** 3b6d3c5

### Observation

The original P3 verify line was `dotnet test --filter "FullyQualifiedName~Bytecode"`.
It was replaced with:

```yaml
- "output=$(dotnet test --filter 'FullyQualifiedName~Bytecode' 2>&1); echo \"$output\" | tail -6; echo \"$output\" | grep -E 'Failed: +0,' > /dev/null"
```

…to work around a pre-existing crash in `FuzzHarnessTests.FuzzCorpus_PipelineOnAndOff_IdenticalOutput`
that aborts the test host after the test itself passes. The crash is unrelated to
this feature (documented in the prompt and pre-existing per `.claude/repo.md`),
but the workaround is now baked into the plan.

### Why this matters

The grep-based pass criterion masks any future case where the same fuzz crash
appears with a *different, real* failure also present in the run — `Failed: +0,`
will still match in the surviving line even when other regressions exist, as long
as the host crashes before the final summary updates. Workflow band-aid; not a
correctness issue for the feature itself, but worth a follow-up item.

### Suggested fix

After this feature lands, either:

1. Quarantine `FuzzHarnessTests.FuzzCorpus_PipelineOnAndOff_IdenticalOutput` with
   `[Skip(...)]` and a backlog ticket to investigate, OR
2. Investigate the actual fuzz-host crash and fix it.

Then restore the clean verify command for future features. This is a follow-up,
not a blocker — file under `.kanban/0-backlog/` if appropriate.

### Verify

```
dotnet test --filter "FullyQualifiedName~Bytecode"
```
should run cleanly with `Test Run Successful.` (not `Test Run Aborted.`).

---
