# Pipe Implementation — Streaming Execution with SIGPIPE Propagation

**Status:** Design
**Created:** 2025-07-19
**Revised:** 2026-04-26
**Author:** Spec Architect
**Priority:** Critical — blocks all `$(cmd | cmd)` usage

> **Revision (2026-04-26):** The original spec proposed a two-phase approach: buffer-based first, streaming deferred to Phase 2. This was wrong. Buffering is a **correctness failure**, not a performance shortcut — it breaks `$(yes | head -5)`, `$(seq 1000000 | head -5)`, and `$(tail -f /var/log/syslog | grep ERROR | head -1)` permanently. The spec has been rewritten to specify a complete streaming implementation directly.

---

## 1. Problem Statement

The `|` pipe operator inside Stash command expressions is completely broken. A user writing `$(echo "hello world" | grep "hello")` gets a `CommandResult` for `echo`, not the output of `grep`. The `grep` command runs with no stdin and produces no output. Five tests covering this feature have been skipped in `Stash.Tests/Interpreting/InterpreterTests.cs` since the feature was introduced due to "a deadlock issue."

The deadlock is a red herring from the original implementation attempt. The real problem is architectural: the compiler emits both sides of the pipe as independent, fully-executed commands before the `Pipe` opcode fires. By the time `ExecutePipe` runs, there is nothing left to do — the damage is already done.

---

## 2. Root Cause Analysis — Why the Current Implementation Is Completely Broken

### 2.1 The Compiler Compiles Both Sides Too Eagerly

In `Stash.Bytecode/Compilation/Compiler.Strings.cs`, `VisitPipeExpr`:

```
public object? VisitPipeExpr(PipeExpr expr)
{
    byte dest = _destReg;
    _builder.AddSourceMapping(expr.Span);
    byte leftReg  = CompileExpr(expr.Left);   // ← executes $(cmd) at VM runtime
    byte rightReg = CompileExpr(expr.Right);  // ← executes $(cmd) at VM runtime, no stdin
    _builder.EmitABC(OpCode.Pipe, dest, leftReg, rightReg);
    ...
}
```

Both commands are compiled as independent `OpCode.Command` instructions. By the time `OpCode.Pipe` fires, both processes have already run and exited. Streaming is architecturally impossible in this model.

### 2.2 The VM Handler Ignores Left Stdout

In `Stash.Bytecode/VM/VirtualMachine.Strings.cs`, `ExecutePipe`:

```
_stack[@base + a] = _stack[@base + c];  // just returns right side
```

Left stdout is never read. Right stdin is never written.

### 2.3 Why Buffering Does Not Fix This

The obvious patch — run left to completion, feed its stdout string as right's stdin — fixes the byte-passing problem but does not fix the architecture. It is a correctness failure for the following common patterns:

| Pattern                                               | Buffering result                                           |
| ----------------------------------------------------- | ---------------------------------------------------------- |
| `$(yes \| head -5)`                                   | Hangs forever — `yes` never exits, buffer phase never ends |
| `$(seq 1000000 \| head -5)`                           | Buffers one million lines before head runs                 |
| `$(tail -f /var/log/syslog \| grep ERROR \| head -1)` | Hangs forever — `tail -f` never exits                      |
| `$(cat bigfile.gz \| gunzip \| wc -l)`                | Buffers entire decompressed file in memory                 |

All four of these patterns are standard sysadmin use cases. Buffering makes the pipe operator unusable for any producer that generates large or unbounded output.

**The fix must be streaming.** Both sides must run concurrently, connected by OS-level pipe semantics. SIGPIPE propagation — where a consumer exiting causes its upstream producer to receive SIGPIPE and exit — is the mechanism that makes finite results emerge from theoretically-infinite producers.

---

## 3. Design Decisions

### Decision 1 — Streaming Directly, No Buffer Phase

**Decision: Implement full streaming from the start.**

There is no Phase 1 buffer approach. No migration path. This is a pre-production project and breaking changes are acceptable.

**The alternatives and why they were rejected:**

| Alternative                               | Why rejected                                                                                                                                                                                                                      |
| ----------------------------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Buffer approach first, streaming later    | Breaks infinite-stream use cases permanently until "later" arrives. Ships broken-by-design to production.                                                                                                                         |
| Async streaming via `System.IO.Pipelines` | Adds a complex abstraction with no meaningful benefit over Thread-per-pump with `ReadAsync`. The existing VM is synchronous; `System.IO.Pipelines` does not compose cleanly here.                                                 |
| Native OS pipe handles via P/Invoke       | Required on older .NET frameworks, but .NET's `Process` class with `RedirectStandard*` gives us sufficient control using `Task.Run` pump threads. P/Invoke would be more correct for `execvpe`-style chaining but is unnecessary. |

The **pump thread model** — reading chunks from `process[i].StandardOutput` and writing them to `process[i+1].StandardInput` asynchronously — is the correct .NET approach. It is what `CliWrap` (the most popular .NET process library) uses. SIGPIPE cascade is achieved by closing the upstream stdout reader when the downstream write throws `IOException`.

### Decision 2 — New `PipeChain` Opcode (Repurposing Opcode 65)

For streaming to work, all processes in the chain must start concurrently. The current model of sequential `Command` instructions cannot achieve this — by the time you'd start stage N, stage N-1 hasn't started yet, so there is nothing to pipe from.

**The fix requires a single opcode that encodes the entire pipe chain and executes it atomically.**

`OpCode.Pipe = 65` is renamed to `OpCode.PipeChain = 65` with completely new semantics. The old opcode emitted two pre-evaluated CommandResults and was a no-op; the new opcode encodes the full chain (all stages, all parts, all flags) and triggers concurrent process startup.

**Alternatives considered:**

| Option                                                                  | Verdict                                                                                                                                                                                                                                      |
| ----------------------------------------------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Extend `Command` with a `hasPipedStdin` flag, keep sequential execution | Rejected — sequential execution means stage N-1 runs to completion before stage N starts. That is buffering, not streaming.                                                                                                                  |
| New dedicated `PipeExec` opcode per stage                               | Rejected — requires VM state to accumulate "pending pipeline stages" between instructions, which is mutable global state in the dispatch loop.                                                                                               |
| Single `PipeChain` opcode with per-stage metadata in companion words    | **Accepted.** Follows the existing `closure` and `call.builtin` companion-word pattern. `ChunkBuilder.EmitRaw(uint)` already exists for this. The VM reads companion words via `frame.Chunk.Code[frame.IP++]`. No new infrastructure needed. |

### Decision 3 — `CommandResult` Shape Is Unchanged

The `CommandResult` that users interact with remains `{ stdout: string, stderr: string, exitCode: int }`. Internally, the VM collects all stage exit codes but exposes only the last stage's exit code in the result. This matches bash default behavior (`echo $?` after `cmd1 | cmd2` gives cmd2's exit code).

### Decision 4 — Stderr: Last Stage Only, Intermediate Discarded

Each stage's stderr pipe must be actively drained to prevent the OS stderr pipe buffer (~64 KB on Linux) from filling up and deadlocking the stage. The drain task for intermediate stages discards the collected stderr. The drain task for the final stage stores the stderr in the `CommandResult`.

**Why not concatenate all stages' stderr?** Ordering would be non-deterministic (concurrent drain tasks complete in arbitrary order). **Why not forward intermediate stderr to the terminal?** That would leak subprocess diagnostic output to users who are running `$(...)` capture mode expecting no side-effects on their terminal.

### Decision 5 — Strict Mode Applies to the Last Stage

When `!$(cmd1 | cmd2)` is written, the `!` prefix applies strict mode to the final `CommandResult`. If the last stage exits non-zero, `RuntimeError("CommandError")` is thrown. Intermediate stages' non-zero exit codes are not checked. This matches bash's default `set -e` behavior (without `pipefail`).

Users who want per-stage strict checking should not use pipes — they should sequence commands with semicolons or `&&`.

### Decision 6 — Passthrough (`$>()`) in Pipe Chains: Out of Scope

`$>(cmd1 | cmd2)` — where the entire pipe chain is passthrough — is a distinct and useful feature (the `tail -f` case where you don't want to capture, just let output flow to the terminal). However, it requires its own parser/AST work to propagate the passthrough flag to the `PipeChain` opcode. This is deferred to a separate spec. For now: any pipe chain compiled as passthrough is a compile-time error.

### Decision 7 — Infinite Pipes Without Terminating Stage Still Block

`$(tail -f /var/log/syslog | grep ERROR)` blocks forever. This is correct. The user is asking Stash to capture the output of an expression that never terminates. They should add a terminating stage: `$(tail -f /var/log/syslog | grep ERROR | head -1)`. Document this clearly.

---

## 4. Concrete Specification

### 4.1 Opcode: `PipeChain` (Opcode 65)

`OpCode.Pipe = 65` is **renamed** to `OpCode.PipeChain = 65`. The XML doc comment, display name in `Disassembler.cs`, and enum documentation in `OpCode.cs` are updated. The numeric value 65 is preserved.

**Instruction format:** ABC

```
 31      24 23     16 15      8 7       0
┌──────────┬─────────┬─────────┬─────────┐
│  opcode  │    A    │    B    │    C    │
│ PipeChn  │  dest   │  count  │ partsB  │
└──────────┴─────────┴─────────┴─────────┘
```

- **A** = destination register — receives the final `CommandResult`
- **B** = stage count (number of pipe stages, 2–255)
- **C** = base register of the flattened parts block

Immediately following the `PipeChain` instruction word, there are **B companion words** (one per stage), read via `frame.Chunk.Code[frame.IP++]`:

```
Bits 31–16: reserved (0)
Bits 15–8:  partCount for this stage (0–255)
Bits 7–0:   flags for this stage
              bit 0 = isStrict (throw on non-zero exit code)
              bit 1 = reserved (isPassthrough is not valid in a pipe chain)
```

The **flattened parts block** starting at `R(C)` contains all part registers for all stages, packed contiguously:

```
R(C + 0)                             → stage 0 part 0
R(C + 1)                             → stage 0 part 1
...
R(C + partCount[0] - 1)              → stage 0 last part
R(C + partCount[0])                  → stage 1 part 0
R(C + partCount[0] + 1)              → stage 1 part 1
...
R(C + partCount[0] + ... + partCount[B-1] - 1)  → final stage last part
```

**Companion words follow the established VM pattern** (same as `closure`, `call.builtin`). The dispatch loop never reaches the companion word positions — the `PipeChain` handler advances `frame.IP` by B after reading them.

**`BytecodeVerifier` update required:** When the verifier encounters `PipeChain`, it must read B (from the instruction's B field) and skip `ip += B` additional words after the instruction, treating them as metadata rather than instructions.

**`Disassembler` update required:** The disassembler must read and display the B companion words as `stage[i]: partCount=N flags=0x..` lines rather than attempting to decode them as opcodes.

### 4.2 Compiler Changes (`Compiler.Strings.cs`)

`VisitPipeExpr` is completely replaced. The new implementation:

**Step 1 — Flatten the pipe chain.**

`PipeExpr` is left-associative: `a | b | c` parses as `(a | b) | c`. Walk the left side recursively to build a flat `List<CommandExpr>`:

```
private static List<CommandExpr> FlattenPipeChain(PipeExpr root)
{
    var stages = new List<CommandExpr>();
    Expr current = root;
    while (current is PipeExpr pipe)
    {
        if (pipe.Right is not CommandExpr rightCmd)
            throw new ParseError("Pipe stages must be command expressions.", pipe.Right.Span);
        stages.Insert(0, rightCmd);
        current = pipe.Left;
    }
    if (current is not CommandExpr leftCmd)
        throw new ParseError("Pipe stages must be command expressions.", current.Span);
    stages.Insert(0, leftCmd);
    return stages;
}
```

Any non-`CommandExpr` leaf is a compile-time error. This catches things like `$(1 | 2)` or an attempt to pipe a variable.

**Step 2 — Merge interpolation parts per stage.**

For each `CommandExpr` stage, run `MergeInterpolationParts` exactly as `VisitCommandExpr` does. This produces a `List<(Expr? originalExpr, string? folded)>` per stage with constant-folded part strings.

**Step 3 — Allocate a contiguous register block for all parts.**

Compute `totalParts = sum(mergedParts[i].Count for all i)`. Reserve `totalParts` registers starting at `partsBase = _scope.ReserveRegs(totalParts)`.

**Step 4 — Compile all parts into their registers.**

Iterate through all stages, all parts, filling the register block:

```
int regOffset = 0;
foreach (var stage in stages)
{
    foreach (var (originalExpr, folded) in mergedParts[stageIndex])
    {
        byte partReg = (byte)(partsBase + regOffset++);
        if (folded is not null)
        {
            ushort constIdx = _builder.AddConstant(folded);
            _builder.EmitABx(OpCode.LoadK, partReg, constIdx);
        }
        else
        {
            CompileExprTo(originalExpr!, partReg);
        }
    }
}
```

**Step 5 — Emit the `PipeChain` instruction and B companion words.**

```
_builder.AddSourceMapping(expr.Span);
_builder.EmitABC(OpCode.PipeChain, dest, (byte)stages.Count, partsBase);

foreach (var (stage, mergedParts) in zip(stages, allMergedParts))
{
    byte flags = 0;
    if (stage.IsStrict) flags |= 0x01;
    // isPassthrough is validated to be false (see below)
    uint companion = ((uint)mergedParts.Count << 8) | flags;
    _builder.EmitRaw(companion);
}
```

**Passthrough validation:** If any stage's `CommandExpr.IsPassthrough` is true, throw `ParseError("Passthrough commands ($>()) cannot appear in a pipe chain.", stage.Span)` before emitting.

**Step 6 — Free the parts register block.**

```
_scope.FreeTempFrom(partsBase);
if (dest != _destReg)
{
    _builder.EmitAB(OpCode.Move, _destReg, dest);
    _scope.FreeTemp(dest);
}
```

`OpCode.Pipe` is **never emitted** by the new `VisitPipeExpr`. The renamed opcode 65 (`PipeChain`) is what gets emitted.

### 4.3 VM Handler: `ExecutePipeChain` (`VirtualMachine.Strings.cs`)

A new `[MethodImpl(MethodImplOptions.NoInlining)]` method replaces `ExecutePipe`:

```
private void ExecutePipeChain(ref CallFrame frame, uint inst)
{
    byte a = Instruction.GetA(inst);
    byte stageCount = Instruction.GetB(inst);
    byte partsBase = Instruction.GetC(inst);
    int @base = frame.BaseSlot;
    SourceSpan? span = GetCurrentSpan(ref frame);

    // 1. Read B companion words from the instruction stream
    var stageMetas = new (int PartCount, byte Flags)[stageCount];
    for (int i = 0; i < stageCount; i++)
    {
        uint companion = frame.Chunk.Code[frame.IP++];
        stageMetas[i] = ((int)((companion >> 8) & 0xFF), (byte)(companion & 0xFF));
    }

    // 2. Assemble stage descriptors from the register block
    var stages = new List<PipeStage>(stageCount);
    int regOffset = 0;
    for (int i = 0; i < stageCount; i++)
    {
        var (partCount, flags) = stageMetas[i];
        // Merge parts into command string (same logic as ExecuteCommand)
        Span<char> stackBuf = stackalloc char[256];
        var vsb = new ValueStringBuilder(stackBuf);
        for (int p = 0; p < partCount; p++)
            vsb.Append(RuntimeOps.Stringify(_stack[@base + partsBase + regOffset + p]));
        regOffset += partCount;

        string command = _context.ExpandTilde(vsb.AsSpan().Trim().ToString());
        vsb.Dispose();
        if (string.IsNullOrEmpty(command))
            throw new RuntimeError("Command cannot be empty in pipe chain.", span);

        var (program, arguments) = CommandParser.Parse(command);
        ApplyTildeToArguments(arguments);      // existing helper or inline
        ApplyElevationIfActive(ref program, arguments);  // existing helper or inline

        stages.Add(new PipeStage(program, arguments, flags));
    }

    // 3. Execute the streaming pipeline
    var (stdout, stderr, exitCodes) = ExecPipelineStreaming(stages, span, _ct);

    // 4. Strict mode: check last stage
    byte lastFlags = stageMetas[stageCount - 1].Flags;
    bool isStrict = (lastFlags & 0x01) != 0;
    int lastExitCode = exitCodes[^1];
    if (isStrict && lastExitCode != 0)
    {
        throw new RuntimeError(
            $"Command failed with exit code {lastExitCode}.",
            span, "CommandError")
        {
            Properties = new Dictionary<string, object?>
            {
                ["exitCode"] = (long)lastExitCode,
                ["stderr"]   = stderr,
                ["stdout"]   = stdout,
            }
        };
    }

    // 5. Store result
    _stack[@base + a] = StashValue.FromObj(new StashInstance("CommandResult",
        new Dictionary<string, StashValue>
        {
            ["stdout"]   = StashValue.FromObj(stdout),
            ["stderr"]   = StashValue.FromObj(stderr),
            ["exitCode"] = StashValue.FromInt((long)lastExitCode)
        }) { StringifyField = "stdout" });
}
```

The `PipeStage` record (new, defined in `VirtualMachine.Process.cs` or a companion file):

```csharp
internal sealed record PipeStage(string Program, List<string> Arguments, byte Flags);
```

The dispatch case in `VirtualMachine.Dispatch.cs` is updated:

```csharp
case OpCode.PipeChain: ExecutePipeChain(ref frame, inst); break;
```

### 4.4 New Method: `ExecPipelineStreaming` (`VirtualMachine.Process.cs`)

This is the core of the streaming implementation. It is `private static`, synchronous (blocking on `Task.WaitAll`), and accepts a `CancellationToken`.

```csharp
private static (string Stdout, string Stderr, int[] ExitCodes) ExecPipelineStreaming(
    List<PipeStage> stages,
    SourceSpan? span,
    CancellationToken ct)
{
    int n = stages.Count;
    var processes = new Process[n];
    int started = 0;

    try
    {
        // ── Phase 1: Start all processes ────────────────────────────────────
        for (int i = 0; i < n; i++)
        {
            var stage = stages[i];
            var psi = new ProcessStartInfo
            {
                FileName               = stage.Program,
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                RedirectStandardInput  = (i > 0),   // all except the first get piped stdin
                UseShellExecute        = false,
                CreateNoWindow         = true,
            };
            foreach (string arg in stage.Arguments)
                psi.ArgumentList.Add(arg);

            processes[i] = Process.Start(psi)
                ?? throw new RuntimeError($"Failed to start process: {stage.Program}", span);
            started++;
        }

        // ── Phase 2: Start stderr drain tasks for ALL stages ────────────────
        // CRITICAL: Failing to drain stderr causes OS pipe buffer deadlock.
        var stderrTasks = new Task<string>[n];
        for (int i = 0; i < n; i++)
        {
            int idx = i; // capture
            stderrTasks[i] = Task.Run(
                () => processes[idx].StandardError.ReadToEndAsync(ct).GetAwaiter().GetResult(),
                ct);
        }

        // ── Phase 3: Start pump tasks (stdout[i] → stdin[i+1]) ──────────────
        var pumpTasks = new Task[n - 1];
        for (int i = 0; i < n - 1; i++)
        {
            StreamReader from = processes[i].StandardOutput;
            StreamWriter to   = processes[i + 1].StandardInput;
            pumpTasks[i] = Task.Run(() => PumpAsync(from, to, ct).GetAwaiter().GetResult(), ct);
        }

        // ── Phase 4: Collect final stage stdout ──────────────────────────────
        var stdoutTask = Task.Run(
            () => processes[n - 1].StandardOutput.ReadToEndAsync(ct).GetAwaiter().GetResult(),
            ct);

        // ── Phase 5: Wait for all processes to exit ──────────────────────────
        var waitTasks = new Task[n];
        for (int i = 0; i < n; i++)
        {
            int idx = i;
            waitTasks[i] = processes[idx].WaitForExitAsync(ct);
        }

        try
        {
            Task.WaitAll(waitTasks);
        }
        catch (AggregateException ae) when (ae.InnerExceptions.All(e => e is OperationCanceledException))
        {
            // Cancellation: kill all remaining processes and re-throw
            for (int i = 0; i < started; i++)
            {
                try { processes[i].Kill(entireProcessTree: true); } catch { }
            }
            Task.WaitAll(pumpTasks.Append(stdoutTask).Concat(stderrTasks).ToArray());
            ct.ThrowIfCancellationRequested();
        }

        // ── Phase 6: Collect results ─────────────────────────────────────────
        Task.WaitAll(pumpTasks);
        Task.WaitAll(stderrTasks);
        stdoutTask.GetAwaiter().GetResult(); // ensure complete

        var exitCodes = new int[n];
        for (int i = 0; i < n; i++)
            exitCodes[i] = processes[i].ExitCode;

        return (stdoutTask.Result, stderrTasks[n - 1].Result, exitCodes);
    }
    catch (RuntimeError)
    {
        throw;
    }
    catch (Exception ex) when (ex is not OperationCanceledException)
    {
        throw new RuntimeError($"Pipe chain execution failed: {ex.Message}", span);
    }
    finally
    {
        for (int i = 0; i < started; i++)
        {
            try { processes[i].Dispose(); } catch { }
        }
    }
}
```

The `PumpAsync` helper (also in `VirtualMachine.Process.cs`, `private static async Task`):

```csharp
private static async Task PumpAsync(StreamReader from, StreamWriter to, CancellationToken ct)
{
    char[] buffer = new char[8192];
    try
    {
        int read;
        while ((read = await from.ReadAsync(buffer.AsMemory(), ct).ConfigureAwait(false)) > 0)
        {
            await to.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
            await to.FlushAsync(ct).ConfigureAwait(false);
        }
        // Upstream exited naturally — send EOF downstream
        try { to.Close(); } catch { }
    }
    catch (IOException)
    {
        // Downstream exited (broken pipe on write) — close upstream stdout
        // to trigger SIGPIPE on Linux/macOS, or ERROR_BROKEN_PIPE on Windows.
        // This causes the upstream process to exit, propagating the cascade.
        try { from.Close(); } catch { }
        try { to.Close(); } catch { }
    }
    catch (OperationCanceledException)
    {
        try { from.Close(); } catch { }
        try { to.Close(); } catch { }
        throw;
    }
}
```

### 4.5 SIGPIPE Cascade Propagation

For `$(yes | head -5)`:

1. `yes` (stage 0) and `head -5` (stage 1) start concurrently.
2. Pump task reads from `processes[0].StandardOutput`, writes 8 KB chunks to `processes[1].StandardInput`.
3. `head -5` reads 5 lines, writes them to its stdout, exits.
4. `head`'s exit closes the read end of its stdin pipe (from head's side). Our pump's `to.WriteAsync` throws `IOException` (broken pipe on the write end we hold).
5. Pump task: `IOException` handler calls `from.Close()` — closes `processes[0].StandardOutput` (the read end of `yes`'s stdout pipe).
6. `yes`'s next `write()` to its stdout (the write end of the now-reader-closed pipe) receives `EPIPE` → `SIGPIPE` on Linux/macOS. `yes` exits. On Windows, `WriteFile()` returns `ERROR_BROKEN_PIPE` → `yes` exits.
7. `WaitForExitAsync` for both processes completes.
8. Final stdout collect task completes (head's stdout was captured before head exited).
9. `ExecPipelineStreaming` returns `("1\n2\n3\n4\n5\n", "", [143, 0])` (SIGPIPE exit code 141 on Linux for yes; 0 for head).

For `$(tail -f /var/log/syslog | grep ERROR | head -1)` (three stages):

1. All three start concurrently.
2. Two pump tasks run: tail→grep and grep→head.
3. `head -1` reads one matching line, exits.
4. Pump (grep→head) catches `IOException` → closes `processes[1].StandardOutput` (grep's stdout read end).
5. grep gets `SIGPIPE` on its next write → grep exits.
6. Pump (tail→grep): grep's stdin was being written to. grep's exit closes its stdin (read end). Pump's write to grep's stdin (`processes[1].StandardInput`) throws `IOException` → closes `processes[0].StandardOutput` (tail's stdout read end).
7. tail gets `SIGPIPE` → tail exits.
8. All processes exited. Results collected.

### 4.6 Known Limitation: No Terminating Stage = Hangs Forever

`$(tail -f /var/log/syslog | grep ERROR)` will block indefinitely. Both processes run forever. The final stdout collect task never returns (grep never exits). This is correct and expected — the user is capturing an infinite stream.

For infinite-stream use cases without a natural terminating stage, users must add one:

```stash
// Good: head -1 terminates the chain after the first match
let r = $(tail -f /var/log/syslog | grep "CRITICAL" | head -1);
println(r.stdout);
```

This is documented in the language spec (§ below).

---

## 5. Interaction with Existing Features

### String Interpolation in Pipe Stages

Expressions embedded in pipe stage arguments (`${var}`, `${expr}`) are compiled into the parts register block using the same `MergeInterpolationParts` logic as standalone commands. No changes to interpolation handling are needed.

### Redirect (`>`, `>>`, `2>`) After a Pipe

`$(echo "hello" | grep "hello" > output.txt)` — the redirect operator applies to the final `CommandResult`. `ExecuteRedirect` reads from the completed `CommandResult.stdout` string. This is unaffected by the streaming change.

### Elevation (`elevate {}`)

Tilde expansion and elevation prefix are applied per-stage inside `ExecutePipeChain` before calling `ExecPipelineStreaming`, identical to how `ExecuteCommand` handles them. Each stage in a pipe chain independently receives the elevation prefix if elevation is active.

### Error Handling (`try/catch`)

`RuntimeError` thrown by `ExecutePipeChain` (e.g., command not found, strict-mode failure) propagates through the VM's existing exception mechanism. `StashError` wrapping in `try/catch` blocks works without changes.

### `defer`

Pipe stages are synchronous from the VM's perspective — `ExecutePipeChain` blocks until all processes exit and all results are collected. Defer closures captured around a pipe expression behave correctly.

### Static Analysis

`SemanticValidator` has no pipe-specific rules today. Post-implementation, consider adding diagnostic **SA-PIPE-001**: warn if a passthrough command (`$>()`) is used as a pipe stage (currently a compile-time error; could be promoted to a proper diagnostic with a code). Deferred — not required for this implementation.

### LSP / DAP

No changes. Pipe expressions are already parsed and tokenised correctly.

### `BytecodeVerifier`

The verifier at `Stash.Bytecode/Bytecode/BytecodeVerifier.cs` must be updated to handle `PipeChain`: when the verifier encounters opcode 65, read B (the stage count from the instruction's B field) and advance the instruction index by B additional words. Otherwise the verifier will misinterpret companion words as standalone instructions.

---

## 6. Test Plan

### 6.1 Re-enable and Update Five Skipped Tests

All five tests in `Stash.Tests/Interpreting/InterpreterTests.cs` must have their `Skip` attributes removed and, where applicable, their assertions updated to match streaming semantics:

| Test                                   | Streaming behavior                                                                                                                                                                                                                                            |
| -------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `Pipe_BasicChain_StdoutPiped`          | `$(echo "hello world" \| grep "hello")` — stdout contains "hello world". Passes as-is.                                                                                                                                                                        |
| `Pipe_ExitCodeFromLastCommand`         | `$(true \| false)` — exitCode is non-zero. Passes as-is.                                                                                                                                                                                                      |
| `Pipe_ThreeCommands`                   | `$(echo "foo bar baz" \| tr "a-z" "A-Z" \| cut "-d " -f1)` — stdout is "FOO". Passes as-is.                                                                                                                                                                   |
| `Pipe_GrepFilter`                      | `$(echo "line1\nline2\nfoo" \| grep "line")` — stdout contains "line1" and "line2". Passes as-is.                                                                                                                                                             |
| `Pipe_StreamingHeadTerminatesProducer` | `$(seq 1 10000 \| head -5)` — stdout is "1\n2\n3\n4\n5\n". With streaming, `seq` is killed via SIGPIPE after head reads 5 lines. The test name is now **correctly** descriptive. Assertion may need updating if it previously noted the non-streaming caveat. |

### 6.2 New Tests in `PipeTests.cs`

Extract into a dedicated `Stash.Tests/Interpreting/PipeTests.cs`. Suggested cases:

| Test                                           | Scenario                                                                                           |
| ---------------------------------------------- | -------------------------------------------------------------------------------------------------- |
| `Pipe_LeftStdoutPassedAsRightStdin`            | `$(echo "hello" \| cat)` stdout is "hello"                                                         |
| `Pipe_ExitCodeIsLastStage`                     | Last stage exit code wins — `$(true \| false)` is non-zero, `$(false \| true)` is zero             |
| `Pipe_ThreeStages`                             | `$(echo "abc" \| tr 'a-z' 'A-Z' \| rev)` stdout is "CBA"                                           |
| `Pipe_StrictModeLastStage_NonZeroThrows`       | `!$(true \| false)` throws `RuntimeError`                                                          |
| `Pipe_StrictModeLastStage_Zero_DoesNotThrow`   | `!$(false \| true)` does not throw                                                                 |
| `Pipe_InterpolationInStage`                    | Variable substitution works: `let x = "hello"; $(echo ${x} \| cat)`                                |
| `Pipe_CommandNotFoundInFirstStage`             | `$(notacommand \| cat)` throws `RuntimeError`                                                      |
| `Pipe_CommandNotFoundInMiddleStage`            | `$(echo foo \| notacommand \| cat)` throws `RuntimeError`                                          |
| `Pipe_EmptyOutput`                             | `$(echo "" \| cat)` stdout is empty string or newline                                              |
| `Pipe_StreamingTermination`                    | `$(seq 1 1000000 \| head -1)` completes quickly (seq is killed via SIGPIPE, not run to completion) |
| `Pipe_StderrFromIntermediateStage_NotInResult` | Stderr from non-final stages does not appear in `CommandResult.stderr`                             |
| `Pipe_StderrFromFinalStage_IsInResult`         | Stderr from the final stage appears in `CommandResult.stderr`                                      |

> **Platform note on `Pipe_StreamingTermination`:** `seq` is not available on Windows by default. Guard this test with `[SkipOnPlatform(TestPlatforms.Windows)]` or substitute `for /L %i in (1,1,1000000) do echo %i` via a shell. Alternatively, the test can be marked `[Trait("Category", "Unix")]` and skipped in Windows CI runs.

### 6.3 Test Infrastructure Note

Tests that invoke actual shell commands (echo, cat, grep, head, seq) require the test runner to have these commands available. The existing skipped tests already had this assumption. On Linux/macOS CI this is standard. On Windows, WSL or Git Bash provides compatibility. No test infrastructure changes are needed beyond what the skipped tests already required.

---

## 7. Documentation Updates

| Document                                          | Section                   | Change                                                                                                                                                             |
| ------------------------------------------------- | ------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| `docs/Stash — Language Specification.md`          | Command Execution / Pipes | Describe streaming semantics: concurrent process start, stdout→stdin chaining, SIGPIPE termination cascade, stderr last-stage-only policy, passthrough restriction |
| `docs/Stash — Language Specification.md`          | Command Execution / Pipes | Add note: chains without a terminating stage block indefinitely — always include a consuming final stage (`head`, `grep`, `wc`, etc.)                              |
| `docs/Bytecode VM — Instruction Set Reference.md` | §5.17 Shell & Process     | Replace `pipe` entry with `pipe.chain` (opcode 65, renamed). Document ABC format, companion words, parts layout, and streaming execution semantics.                |
| `docs/Bytecode VM — Instruction Set Reference.md` | §5.17 Shell & Process     | Remove any reference to the old `pipe` opcode semantics ("R(A) = pipe(R(B), R(C))").                                                                               |

---

## 8. Risk Register

| Risk                                                                  | Likelihood          | Impact   | Mitigation                                                                                                                                                                                                                              |
| --------------------------------------------------------------------- | ------------------- | -------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Deadlock if stderr is not drained on ALL stages                       | Medium              | Critical | The spec is explicit: start N stderr drain tasks before any pumping begins. Tests covering long stderr output from intermediate stages.                                                                                                 |
| Pump task race: `from.Close()` racing with the pump's own `ReadAsync` | Low                 | High     | The pump task is the sole reader of `from`. Closing `from` from within the pump's own exception handler is safe. No external code closes a process's stdout.                                                                            |
| `yes` / `seq` not terminated by SIGPIPE on Windows                    | Medium              | Low      | On Windows, `ERROR_BROKEN_PIPE` from `WriteFile` causes the same effect for well-behaved programs. Document platform difference. The `Pipe_StreamingTermination` test is guarded to Unix-only or uses a cross-platform equivalent.      |
| Part register count overflow (>255 total parts across all stages)     | Very Low            | Low      | The stage count and per-stage part count are each 8-bit fields. 255 parts per stage is more than enough. If the register allocator hits the 255 limit it will already fail for standalone commands — pipe chains add no new constraint. |
| `BytecodeVerifier` crash on companion words misread as instructions   | High (if forgotten) | Medium   | §5 calls this out explicitly. Must be fixed before the implementation is considered complete.                                                                                                                                           |
| Process start failure in stage i with stages 0..i-1 already running   | Low                 | Medium   | `ExecPipelineStreaming` tracks `started` and kills already-started processes in the `finally` block on exception.                                                                                                                       |
| Users confused by `$(tail -f \| grep)` hanging forever                | Medium              | Low      | Documented in spec and language spec. The behavior is correct — it matches shell semantics.                                                                                                                                             |

---

## 9. Implementation Checklist (for Orchestrator)

When this spec moves to `1-todo/`, the implementing agent must:

**New files:**

- [ ] No new files required. All changes are to existing files.

**`Stash.Bytecode/Bytecode/OpCode.cs`:**

- [ ] Rename `Pipe = 65` to `PipeChain = 65`. Update the XML doc comment to reflect the new ABC format + companion words semantics.

**`Stash.Bytecode/Compilation/Compiler.Strings.cs`:**

- [ ] Replace `VisitPipeExpr` with the flattening + contiguous-parts-block + `PipeChain` + companion-words implementation described in §4.2.
- [ ] Add `FlattenPipeChain(PipeExpr root) → List<CommandExpr>` helper method.
- [ ] Passthrough validation: throw `ParseError` if any stage has `IsPassthrough = true`.

**`Stash.Bytecode/VM/VirtualMachine.Strings.cs`:**

- [ ] Replace `ExecutePipe` with `ExecutePipeChain` per §4.3.
- [ ] Add the `PipeStage` record (or move to a companion file in `Stash.Bytecode/VM/`).
- [ ] Add `ApplyTildeToArguments` and `ApplyElevationIfActive` helpers if they don't already exist as extracted methods (currently this logic is inline in `ExecuteCommand` — extract it so both handlers share it, avoiding duplication).

**`Stash.Bytecode/VM/VirtualMachine.Dispatch.cs`:**

- [ ] Update dispatch case: `case OpCode.PipeChain: ExecutePipeChain(ref frame, inst); break;`

**`Stash.Bytecode/VM/VirtualMachine.Process.cs`:**

- [ ] Add `ExecPipelineStreaming(List<PipeStage>, SourceSpan?, CancellationToken) → (string, string, int[])` per §4.4.
- [ ] Add `PumpAsync(StreamReader, StreamWriter, CancellationToken) → Task` per §4.4.

**`Stash.Bytecode/Bytecode/BytecodeVerifier.cs`:**

- [ ] Add `PipeChain` handling: read stage count B, skip B companion words in the verification pass.

**`Stash.Bytecode/Bytecode/Disassembler.cs`:**

- [ ] Add `PipeChain` disassembly: display companion words as `stage[i]: parts=N flags=0x..` annotations.

**`Stash.Tests/Interpreting/InterpreterTests.cs`:**

- [ ] Remove `Skip` attribute from all 5 pipe tests. Update `Pipe_StreamingHeadTerminatesProducer` assertion to remove any buffering-era caveats.

**`Stash.Tests/Interpreting/PipeTests.cs`** (new file):

- [ ] Add all tests from §6.2.

**`docs/Stash — Language Specification.md`:**

- [ ] Update pipe section per §7.

**`docs/Bytecode VM — Instruction Set Reference.md`:**

- [ ] Replace `pipe` opcode entry with `pipe.chain` per §7.

**Do not touch:**

- `Stash.Core/Parsing/AST/PipeExpr.cs` — AST node is correct, no changes needed.
- `VirtualMachine.Process.cs` — existing `ExecCaptured` and `ExecPassthrough` are unchanged (new `ExecPipelineStreaming` is additive).
- Any LSP, DAP, or analysis files — no changes needed for this implementation.
- `ExecCaptured`'s `stdin string?` parameter — it remains in place for future use but is not the mechanism for streaming. The buffer-based stdin string approach is not used.
