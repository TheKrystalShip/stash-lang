# Streaming Pipe Chains — Direct `$<(a | b | c)` Support

**Status:** Backlog (follow-up to Streaming Command Output)
**Created:** 2026-05-05
**Parent spec:** `.kanban/4-done/Streaming Command Output — $<(cmd) Sigil.md`
**Type:** Feature gap — closes a deferred limitation from Phase C

---

## Problem

The streaming command spec promises:

```stash
for (let line in $<(cat huge.log | grep ERROR)) { ... }
```

The current implementation **rejects this at compile time**. The lexer's `ScanCommandLiteral` splits on top-level `|` for streaming sigils (the inline-pipe split is gated on `&& !passthrough`, not `&& !streaming`), so `$<(a | b)` reaches the compiler as a pipe of two separate `CommandExpr` stages. `Compiler.Strings.cs` `FlattenPipeChain` then rejects any chain that contains a `Mode == Stream` operand and emits the SA0711 / `CompileError` "streaming command cannot appear in a pipe chain".

The shell-wrap workaround `$<(sh -c "cat huge.log | grep ERROR")` works but:
- Spawns an extra shell process per invocation
- Quoting becomes painful when the inner command contains `${...}` interpolation, quotes, or `$`
- It's not what the spec example shows
- It's not what users will write

The deferred Phase C work item is to make `$<(a | b | c)` "just work" the way `$(a | b | c)` does: intermediate stages run captured-piped together, the **last stage's stdout** feeds the `StashStreamingProcess` handle, only the last stage's exit code is observed.

## Goal

Lift the streaming-in-pipe-chain restriction so that `$<(stage1 | stage2 | ... | stageN)` and `$!<(stage1 | ... | stageN)` compile and run correctly. After this spec:

- `$<(cat huge.log | grep ERROR)` streams matching lines as `grep` produces them.
- `$<(kubectl get pods -w -o json | jq -c '.items[]').json()` streams parsed pod events.
- `$!<(make 2>&1 | tee build.log)` streams `tee`'s output and throws `CommandError` if `tee` exits non-zero at natural completion.
- All Phase C semantics (cleanup contract, dual iteration, framing methods, `.kill()`, `.wait()`, single-consumption) apply unchanged to the resulting handle.
- SA0711 is **removed** (or substantially narrowed — see "Static analysis" below). The lexer-level rejection of `$<>(`/`$><(` remains.

## Design

### Semantics

A streaming pipe chain `$<(s1 | s2 | ... | sN)` behaves as:

1. **All stages spawn at iteration start.** The handle is constructed eagerly when the `$<(...)` expression evaluates, so `s.pid` is populated immediately. The PID exposed on `StashStreamingProcess.pid` is the **last stage's PID** (the one whose stdout we read), matching the bash convention where `${PIPESTATUS[-1]}` and `$!` of `cmd | tail` refer to `tail`.

2. **Intermediate stages are captured-piped.** Stage `s_k`'s stdout is wired to stage `s_{k+1}`'s stdin via OS pipes (no buffering through the VM). This is the same wiring used by `$(s1 | s2)` today via `ExecPipelineStreaming` in `VirtualMachine.Process.cs`.

3. **The last stage's stdout becomes the streaming source.** Instead of being captured into a buffer (as `$(s1 | s2)` does), the last stage's stdout pipe is exposed via the `StashStreamingProcess` handle's reader.

4. **Stderr handling.**
   - Single-var iteration: stderr from **every stage** is silently discarded (background-drained on a per-stage Task to prevent pipe-buffer deadlock).
   - Dual-var iteration `for (out, err in handle)`: stderr from **every stage** is interleaved into the bounded channel, in arrival order. Lines are not tagged by which stage produced them — this matches the existing single-stage Phase C dual-iteration semantics. (A future spec could introduce a `.staged()` framing method that tags lines.)

5. **Exit codes.**
   - `s.exitCode` is the **last stage's exit code only**. This matches `$(a | b | c)` today and matches bash's `${PIPESTATUS[-1]}`.
   - `$!<(...)` strict mode throws `CommandError` if the **last stage** exits non-zero at natural completion. Intermediate stages with non-zero exits are not surfaced. (`pipefail`-style behavior is out of scope; can be a future option bag.)

6. **Cleanup contract applies to the whole pipeline.** When iteration exits early (break, return, throw, future timeout), the runtime sends `SIGTERM` to **every stage's process**, waits 5 seconds, then sends `SIGKILL` to any survivors. FDs are closed and all stages are reaped. Order: signal all, then wait, then kill survivors — not stage-by-stage, to minimize total latency. The handle's `signal` field reflects the **last stage's** signal disposition (since that's what `pid` and `exitCode` already refer to).

7. **`.kill(signal)` on a pipeline handle.** Sends the signal to **every stage**, not just the last. Rationale: the handle conceptually represents the whole pipeline; killing only the last stage would leave intermediate stages alive (often hanging on a closed downstream pipe, but not always — `cat /dev/zero | head -n 1` can outlive the head). PID-reuse safety still applies: `.kill()` is a no-op once `exitCode` is non-null.

### Implementation

#### 1. Compiler (`Stash.Bytecode/Compilation/Compiler.Strings.cs`)

Lift the rejection in `FlattenPipeChain` for `Mode == Stream`. The current passthrough rejection stays — passthrough cannot appear in a pipe chain because it inherits stdout. For streaming, we need a new opcode path or a new flag.

**Recommended encoding:** introduce a new opcode `OpCode.StreamingPipeline` (or extend `OpCode.Pipeline` with a flag bit) that:
- Operand A: destination register
- Operand B: stage count
- Operand C: flags byte (`0x02 = strict`)
- Followed by per-stage `CommandMetadata` records in the constant pool, **with the streaming flag set on the last stage only**.

Rationale: keep the existing `Pipeline` opcode untouched for backward compat (no bytecode format bump needed if we add a new opcode at the next free number — currently 99 is `IterClose`, so `100` for `StreamingPipeline`). Alternatively, extend `OpCode.Pipeline` operand C with a `0x04` streaming bit and bump format version to 3; this is simpler but requires the same backward-compat shim that v1→v2 used in Phase B.

**Decision pending implementation:** prefer the new-opcode route. It avoids a format-version bump and makes the dispatch path explicit at the cost of a small amount of code duplication in the VM.

#### 2. VM (`Stash.Bytecode/VM/VirtualMachine.Process.cs` + `VirtualMachine.Strings.cs`)

Add a new method:

```csharp
internal StashStreamingProcess ExecStreamingPipeline(
    List<PipeStage> stages,
    bool isStrict,
    string commandText,
    SourceSpan? span,
    CancellationToken ct)
```

Mirror `ExecPipelineStreaming` (which already supports captured-stream piping) but:
- Do **not** drain the last stage's stdout to a buffer.
- Instead, hand the last stage's `StandardOutput.BaseStream` (wrapped in a `StreamReader`) to a new `StashStreamingProcess` constructor overload that accepts a list of `Process` objects (every stage) rather than a single one.
- Background-drain every stage's stderr into the same bounded channel that single-stage dual iteration uses (`BlockingCollection<(string?, string?)>` cap 256).
- Single-var iteration discards stderr exactly as today (the bounded channel has a "drop" sink in single-var mode).

Update `StashStreamingProcess` to hold `IReadOnlyList<Process> _stages` instead of (or alongside) `Process _process`. `_process` becomes `_stages[^1]`. The `pid` field returns `_stages[^1].Id`. `EnsureCleanedUp` iterates `_stages`:

```
foreach (stage) PosixKill(stage.Id, SIGTERM);   // or stage.CloseMainWindow on Windows
WaitAny up to 5s for ALL stages to exit
foreach (stage that's still alive) PosixKill(stage.Id, SIGKILL);   // or stage.Kill()
foreach (stage) stage.WaitForExit(); stage.Dispose();
```

`.kill(signal)` iterates the same way. `_finalized` guard remains idempotent.

#### 3. Lexer

No change required — the lexer already produces a streaming command literal token whose payload contains the full inner text. The `|` splitting that produces multiple stages is parser/compiler-side via `FlattenPipeChain` walking the parsed `BinaryExpr` tree.

Verify: search `Stash.Core/Lexing/Lexer.cs` `ScanCommandLiteral` for the pipe-related early split and confirm streaming sigils take the same code path as `$(...)`. If they currently take a different path, align them.

#### 4. Static analysis

- **SA0711 — remove or restrict.** With this spec, `$<(...) | $(...)` and `$(...) | $<(...)` (a streaming command as a stage in an *outer* pipe expression) is still nonsensical (the outer pipe would try to read from the streaming handle as if it were a string). So SA0711 should be **kept** but its message updated to clarify it's about streaming-as-stage-in-OUTER-pipe, not streaming-with-inner-pipes. The existing detection (a `CommandExpr` with `Mode == Stream` appearing as an operand of `PipeExpr`) is correct — that case remains an error.
- The new behavior is fine because `$<(a | b)` is not a `PipeExpr` at the AST level: the inner `|` is parsed as part of the command literal's text, producing a single `CommandExpr` whose `Parts` reflect the textual content. Verify this in tests.

#### 5. Tests (`Stash.Tests/Bytecode/StreamingCommandTests.cs`)

- `Pipeline_TwoStage_StreamsLastStageOutput` — `$<(printf 'a\nb\nc\n' | grep b)` yields one line "b".
- `Pipeline_ThreeStage_StreamsCorrectly` — `$<(seq 1 100 | head -20 | tail -5)` yields exactly the 5 expected lines.
- `Pipeline_LastStageExitCode_Surfaced` — `$<(true | false)` exitCode == 1; `$<(false | true)` exitCode == 0.
- `Pipeline_Strict_LastStageNonZero_ThrowsCommandError` — `$!<(true | false)` iterating throws CommandError with exitCode=1.
- `Pipeline_Strict_IntermediateNonZero_DoesNotThrow` — `$!<(false | true)` iterating completes cleanly (matches `$!(false | true)` semantics).
- `Pipeline_PidIsLastStage` — verify `s.pid` equals the last stage's PID by spawning a recognizable last stage.
- `Pipeline_Cleanup_Break_KillsAllStages` — `$<(yes | cat | cat); break` — capture stage PIDs, then verify `kill -0 pid` returns false for ALL of them within 6 seconds.
- `Pipeline_Cleanup_OnException_KillsAllStages` — same as above but exit via `throw`.
- `Pipeline_DualIteration_InterleavesAllStagesStderr` — pipeline where multiple stages emit to stderr; verify all stderr lines surface in the dual iterator.
- `Pipeline_Json_OnLastStage` — `$<(echo '{"a":1}' | cat).json()` yields one parsed dict.

#### 6. Documentation

- `docs/Stash — Language Specification.md` §"Streaming Command Output" — remove the deferred-limitation paragraph; add an explicit subsection "Pipe chains in streaming commands" documenting the semantics above.
- `docs/Stash — Standard Library Reference.md` — note the per-pipeline cleanup behavior in the `StreamingProcess Handle` section.
- `CHANGELOG.md` — single Unreleased entry "Streaming pipe chains: `$<(a | b | c)` now compiles and runs natively".
- Update `examples/streaming.stash` to add a pipe-chain demo (replacing or augmenting the `sh -c` workaround if present).

#### 7. Bytecode format

- If using the **new-opcode route**: no version bump. Add `OpCode.StreamingPipeline = 100` to `Stash.Bytecode/Bytecode/OpCode.cs`. Update opcode reference doc.
- If using the **flag-on-existing-Pipeline route**: bump `BytecodeWriter.FormatVersion` to 3, add v2-fallback in `BytecodeReader` (synthesize `IsStreamingPipeline = false` for v1/v2 input), thread version through nested-chunk recursion as Phase B did.

---

## Open questions for the architect to resolve at design time

1. **`pipefail` semantics?** Should there be an option-bag form like `$!<({ pipefail: true }, a | b | c)` that throws if any stage exits non-zero? Currently out of scope, but if the syntax is going in, we should at least sketch the future option-bag.
2. **Mixed sigils?** Is `$<(some-stream | $!(must-succeed))` ever sensible? No — at the syntactic level the inner `$!(...)` is just a child interpolation that produces text inserted into the command, so this is a non-issue. Document for clarity.
3. **PID exposure of intermediate stages.** Should the handle expose `pids: int[]` for the whole pipeline? Useful for diagnostic logging. **Recommendation:** add `pids: int[]` as an additional field, keep `pid` as `pids[^1]`. Backward compat preserved; debugging gets easier.

---

## Decisions to confirm before implementing

- [ ] New opcode (`StreamingPipeline`) vs. flag on existing `Pipeline` opcode + format-version bump? **Recommendation: new opcode.**
- [ ] Add `pids: int[]` field to the handle? **Recommendation: yes.**
- [ ] Should `.kill()` on a pipeline kill all stages or just the last? **Recommendation: all stages (semantic clarity over surprise).**
- [ ] Lift SA0711 entirely or restrict its message? **Recommendation: keep + reword.** The case "streaming as a stage in an outer `PipeExpr`" is still nonsensical and worth catching.

---

## Out of scope

- `pipefail` semantics (future option bag).
- Per-stage exit code exposure (`pids` field is enough; if users need per-stage codes, a `pipeStatus: int[]` field can be added later).
- Tagging stderr lines by stage in dual iteration (future framing method).
- Capability-sandbox integration (covered by the future capability-sandbox spec, which will treat each stage's binary independently).
