# RFC: Readable Disassembly

> **Status:** Draft
> **Owner:** Cristian Moraru
> **Created:** 2026-05-17
> **Slug:** readable-disassembly

## Summary

The output of `stash --disassemble` is faithful to the bytecode but hard to read. Register
operands are raw slot numbers (`move r5, r2`), `CallBuiltIn` shows an opaque `[ic:N]` cache
slot instead of a function name like `io.println`, and there is no per-chunk legend telling
the reader which variable lives in which register. This feature reworks the disassembler
output to surface the names the compiler already records — variable names from
`Chunk.LocalNames`, the builtin method name from `Chunk.ICSlots[i].ConstantIndex`, and a
complete locals legend at the top of every function — so a human can trace register/value
flow without cross-referencing unrelated code.

No bytecode, VM, compiler, or language semantics change. The feature is purely a
presentation improvement in `Stash.Bytecode/Bytecode/Disassembler.cs` and
`BespokeOperandFormatters.cs`, plus golden-file test coverage.

## Motivation

Today, inspecting `stash --disassemble build.stash` for a non-trivial script produces output
like:

```
  0010:  move                r5, r2
  0011:  call.builtin        r7, r3, 2               ; (2 args) [ic:4]
```

The reader has to:

1. Manually correlate `r5`, `r2`, `r3`, `r7` to source-level variables — but the compiler
   already recorded that mapping in `Chunk.LocalNames`.
2. Decode `[ic:4]` by hand: open `Chunk.ICSlots[4]`, read `ConstantIndex`, then look up the
   constant pool entry — again, information the compiler already wrote down.
3. Scroll back to the chunk header to remember the function's parameter and local layout —
   but there is no such header section; the dormant `EmitLocalsSection()` is commented out.

This makes the disassembler less useful than it could be for performance work, bytecode
verification, and bug investigation. Every concrete improvement uses information the
compiler already produces — the cost is presentation only.

## Goals

- Annotate register operands with their variable names (when known) in the comment column.
- Resolve `[ic:N]` on `CallBuiltIn` to a readable method name (`.println`, `.sqrt`, ...) and,
  when statically derivable, the fully-qualified form (`io.println`, `math.sqrt`).
- Enable a `.locals:` section at the top of every chunk header, modeled on the existing
  `.const:` / `.globals:` sections, listing each register slot with its name and role
  (param, local, const, internal).
- Preserve `Compact` mode output verbatim — readability work applies only to the default
  (non-compact) renderer.
- Preserve column alignment in the existing instruction renderer; do not regress diff-ability.
- Add golden-file disassembly tests that lock in the new output shape.

## Non-Goals

- No changes to bytecode encoding, opcode set, VM dispatch, or runtime IC behavior.
- No serialization/format-version bump — `LocalNames`, `LocalIsConst`, `UpvalueNames`, and
  `ICSlots[i].ConstantIndex` already exist on `Chunk` and round-trip through `.stashc`.
- Not building a full data-flow analysis to recover the namespace identity behind every
  `CallBuiltIn` receiver register. We use a bounded, best-effort backward scan within the
  current chunk (described below) and fall back to the method name only.
- No changes to LSP / DAP / Playground / VS Code / static-analysis surfaces. See
  "Tooling Compatibility" in the Implementation Path table — explicitly N/A.
- No new CLI flag. The improved output replaces the current default; compact mode remains
  the escape hatch for callers that want the old terse form.

## Design

### Surface

`stash --disassemble path.stash` (and the programmatic `Disassembler.Disassemble(chunk)` /
`DisassembleAll(chunk)` entry points) produce output with three new presentation features:

1. **Locals legend** — every non-compact chunk header gains a `.locals:` section, e.g.:

   ```
   .locals:
     [r0] this                    ; param
     [r1] count                   ; param
     [r2] total                   ; local
     [r3] <for_counter>           ; internal
     [r4] PI                      ; const
   ```

   Compiler-managed slots whose name is wrapped in angle brackets (existing convention —
   e.g. `<for_counter>`, `<lock_err>`) keep that name with role `internal`. Slots beyond
   `LocalNames.Length` (pure temporaries) are not listed.

2. **Register-name annotations** — instruction comments include a short `r3=total` style
   annotation for each named register operand referenced by the instruction. Example:

   ```
   0010:  move                r5, r2                  ; r5=accum  r2=count
   0023:  add                 r7, r2, r4              ; r7=sum  r2=count  r4=delta
   ```

   Slots without a `LocalNames[i]` entry are omitted from the annotation (they are
   compiler temporaries, not user-visible). When an instruction already carries a
   bespoke comment (e.g. `; .println [ic:4]`), the register annotations are appended
   after a separator (`  |  `), preserving the existing comment first.

3. **`CallBuiltIn` method name** — the `[ic:N]` token is replaced by the resolved name:

   ```
   0030:  call.builtin        r7, r3, 2               ; io.println(2 args)
   ```

   The method name is recovered via `Chunk.ICSlots[icSlot].ConstantIndex` →
   `Chunk.Constants[constIdx]` (a string). The namespace prefix (`io.`) is recovered
   by a bounded backward scan of at most `MAX_BACKWARD_STEPS = 8` prior instructions in
   the same chunk, looking for the most recent `GetGlobal`/`GetUpval`/`Move` whose
   destination matches operand B of the `CallBuiltIn`. If the scan finds a `GetGlobal`,
   the global name table provides the namespace; if it finds a named local via `Move` or
   `GetUpval`, that name is used. On any miss the comment degrades gracefully to
   `.println(2 args)` with no namespace prefix.

### Semantics

- All three improvements are **comment-column only** changes for instructions; operand
  text to the left of the `;` is byte-for-byte unchanged. This preserves the existing
  column alignment logic in `EmitInstruction` and means no instruction-renderer pass needs
  to be re-tuned.
- The new `.locals:` section is a header-only addition between `.const_global_inits:` and
  `.code:`. It is suppressed when `chunk.LocalNames` is null or empty, and entirely skipped
  in `Compact` mode.
- The register-annotation set per instruction is deduplicated (so `add r2, r2, r4` shows
  `r2=count` once, not twice) and rendered in the order operands appear in the instruction.
- The backward scan for namespace resolution is strictly bounded (`MAX_BACKWARD_STEPS = 8`)
  and stops at any label target, jump, or non-name-producing opcode. It is best-effort
  pretty-printing only — there is no correctness contract on its output.

### Implementation Path

Disassembler is the only layer that changes. The path:

`Chunk` debug metadata already populated by `Compiler`/`ChunkBuilder` (`LocalNames`,
`LocalIsConst`, `UpvalueNames`, `ICSlots`, `Constants`) → new helpers in `Disassembler`
and `BespokeOperandFormatters` read that metadata → `EmitInstruction` composes the
comment column from existing-comment + new-annotation parts → new `EmitLocalsSection`
call site in `DisassembleChunk` renders the per-chunk legend → golden-file tests in
`Stash.Tests/Bytecode/DisassemblerTests.cs` lock in the new shape.

Tooling compatibility check (per `.claude/language-changes.md`): explicitly N/A.

| Component          | Affected? | Why                                                              |
| ------------------ | --------- | ---------------------------------------------------------------- |
| LSP                | No        | Disassembler output is not consumed by the LSP.                  |
| DAP                | No        | DAP variable views read `Chunk.LocalNames` directly, not text.   |
| Playground         | No        | Playground does not surface disassembly.                         |
| VS Code extension  | No        | No grammar / language config changes.                            |
| Static analysis    | No        | No semantic surface change.                                      |
| Spec / stdlib docs | No        | No language or stdlib API change.                                |

Final acceptance requires `dotnet test` to pass — no test outside the disassembler suite
should change behavior, since no code path used at runtime is touched.

## Acceptance Criteria

- Running `stash --disassemble` on a script that calls a builtin (e.g. `io.println(...)`)
  shows `; io.println(2 args)` on the corresponding `call.builtin` line — not `[ic:N]`.
  When the namespace cannot be statically recovered, the line shows `; .println(2 args)`
  (method-only fallback) rather than the raw IC slot.
- Running `stash --disassemble` shows a `.locals:` section in every non-compact chunk header,
  with role labels (`param`, `local`, `const`, `internal`) matching `EmitLocalsSection`
  semantics. The section is absent when `chunk.LocalNames` is null/empty and in `Compact`
  mode.
- Every register operand that names a slot in `LocalNames` is annotated in the instruction
  comment column, deduplicated, in operand order. Annotations on an instruction that
  already has a bespoke comment are appended after `  |  `.
- `Compact` mode output is byte-for-byte identical to today's `Compact` output (regression
  test).
- Golden-file disassembly tests in `Stash.Tests/Bytecode/DisassemblerTests.cs` cover at
  minimum: (a) a script with `io.println` resolving the namespace; (b) a function with
  named locals showing register annotations; (c) the locals legend appearing with mixed
  param/local/const/internal slots; (d) compact-mode regression.
- `dotnet test` passes with no regressions in `Bytecode/` or any other test category.

## Phases

The phase list lives in `plan.yaml`. Three phases:

1. **P1 — Builtin name resolution.** Replace `[ic:N]` with the method name (and best-effort
   namespace prefix) in the `CallBuiltIn` bespoke formatter. Smallest, most user-visible
   win; isolated to one formatter.
2. **P2 — Locals legend.** Wire up the dormant `EmitLocalsSection`; verify its role
   classification still matches conventions; uncomment the call site.
3. **P3 — Register-name annotations.** Add register-annotation post-processing in
   `EmitInstruction`; integrate with existing per-opcode comments via the `  |  ` separator.

P3 is last because it touches every instruction's render path and benefits from the
golden-file baselines added in P1 and P2.

## Open Questions

- Should the namespace backward-scan also follow through `GetField`/`GetFieldIC` to support
  cases like `let p = io.process; p.spawn(...)`? Default answer: no — keep the scan simple,
  accept the graceful fallback. Revisit if real scripts trip on it.
- Should the locals legend include `MaxRegs` slots beyond `LocalNames.Length` as `tmp$N`?
  Default answer: no — temporaries change across compiler passes and listing them is noise.
- Should register annotations be enabled in `Compact` mode? Default answer: no — `Compact`
  exists precisely to suppress the comment column.

## Decision Log

| Date       | Decision                                                                                     | Rationale                                                                                                  |
| ---------- | -------------------------------------------------------------------------------------------- | ---------------------------------------------------------------------------------------------------------- |
| 2026-05-17 | Resolve builtin names from `ICSlots[i].ConstantIndex`, not a new compiler-emitted side table | Information already on `Chunk`; zero compile-time cost; zero serialization change.                         |
| 2026-05-17 | Use a bounded backward scan for namespace prefix instead of an explicit name side table      | Avoids `.stashc` format change and compiler-side work; graceful fallback when the scan fails is acceptable. |
| 2026-05-17 | Annotations go in the comment column, not as inline parenthesized text after operands        | Preserves existing instruction column alignment and `Compact` mode output verbatim.                        |
| 2026-05-17 | Three phases (builtin name → locals legend → register annotations) instead of one big phase  | Each phase delivers an independently-shippable readability win; later phases reuse golden tests from earlier ones. |
