# Error Handling — Architectural Audit and Evolution

**Status:** Architectural audit — no design committed yet
**Created:** 2026-05-10
**Author:** Spec Architect
**Origin:** User request to investigate whether Stash's error handling system is structurally sound or "duct-taped together," with recommendations for evolution (NOT a greenfield rewrite).
**Scope:** `Stash.Core`, `Stash.Bytecode`, `Stash.Stdlib`, `Stash.Analysis`, `Stash.Lsp`, `Stash.Dap`

---

## 0. TL;DR

The user's fear is **partially justified, but more nuanced than "duct tape everywhere."**

The **VM-level mechanism** (try-frame stack, dispatch unwinding, defer integration, finally semantics, registry-based matching) is **Solid to Acceptable**. Two architectural hardenings have already shipped (`ErrorTypeRegistry`, `Error Handling — Architectural Hardening`, and a separate `Error Type System` spec) and they did clean up the worst of the original mess.

The **stdlib surface area** is where the duct tape really lives: 27% of stdlib `throw` sites are untyped (164 of 608), entire namespaces (sftp, scheduler, term, test, tpl) ship zero typed errors, and the conventions for "what error type does X throw" exist nowhere in machine-readable form. Tooling (LSP/DAP/analysis) has no clue what a function can throw, so the user can't get hover-time visibility into what to `catch`.

The **conceptual model** has one significant lump under the rug: errors live in **two parallel universes** — a C# class (`StashError`) and a user-facing struct (`ErrorStruct` and 12 subtype structs registered as `IsBuiltIn = true` in globals). They were unified at the matching layer (Layer 2/3 of the hardening spec) but they remain _two different things at runtime_ — `throw ValueError { message }` constructs a `StashInstance`, which `ExecuteThrow` then unpacks into a `RuntimeError` and discards the instance. This works, but it means the type system has a special case nobody can see from outside.

The user's anti-goal — "Java/C# exception sprawl, where you give up and write `catch(Exception)`" — is **not** a present-day Stash problem because Stash currently has only ~13 built-in error types, all flat, no hierarchy, all with the same shape. But the architecture makes that anti-goal **easy to fall into** as the language grows: with no `throws` annotation, no static analysis of unhandled errors, and no formal hierarchy, the moment Stash crosses ~25 error types, users will start writing `catch (Error e)` defensively because no tool tells them what's actually possible.

**Verdict by dimension** (full audit in §3):

| Dimension                    | Verdict                                                                                                                                                                                                                                   |
| ---------------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| 1. Conceptual coherence      | **Acceptable** — three representations, but the boundary is now documented; one structural redundancy (StashError vs StashInstance for built-in error types).                                                                             |
| 2. Layer integrity           | **Solid** — registry lives in Core, used by both VM ops; no protocol violation.                                                                                                                                                           |
| 3. The catch model           | **Solid** — try-frame stack, defer-aware unwind, three compile shapes for try/catch/finally are correct, finally-on-throw and rethrow paths are tested.                                                                                   |
| 4. Error value structure     | **Duct tape** — the `Properties` bag is stringly-typed and the dual representation (StashError vs StashInstance) is invisible to users. Extensibility is poor.                                                                            |
| 5. Stdlib consistency        | **Duct tape** — 27% of throws untyped; six namespaces have zero typed throws; no machine-readable "throws X, Y" metadata.                                                                                                                 |
| 6. Shell command integration | **Acceptable** — CommandError has the right fields (exitCode, stderr, stdout, command); no signal info; passthrough modes throw on spawn-failure but not on exit-code (by design).                                                        |
| 7. Static analysis hooks     | **Acceptable for what exists** — five rules (SA0160–SA0163, SA0814) plus `assert.throws` checking; **no unhandled-error flow analysis, no exhaustiveness, no `throws` clause checking** because there is no `throws` clause.              |
| 8. Tooling integration       | **Mixed** — DAP has `breakOnAllExceptions` and `OnError` hooks (good); LSP has zero "what can this throw" awareness (poor).                                                                                                               |
| 9. Performance               | **Acceptable for sysadmin scripting** — .NET exception throw is expensive (~10–30 μs) but tolerable in a workload that runs commands; would be poor for a tight numeric loop using try/catch as control flow, but Stash isn't that.       |
| 10. Duct-tape signals        | **Mixed** — only one TODO near error code; AssertionError inheritance is clean; the dual codepath for "compile error vs runtime error" is intentional and fine. The remaining smells are concentrated at the stdlib boundary, not the VM. |

---

## 1. The Three Representations of an Error (current state)

Errors flow through three layers. This is documented in `.kanban/4-done/Error Handling — Architectural Hardening.md` §2.1. In summary:

```
USER SOURCE                INTERNAL C# REPRESENTATION                   USER-VISIBLE VALUE
───────────────────────    ──────────────────────────────               ──────────────────
throw ValueError {...}  →  StashInstance (briefly, in ExecuteThrow)
throw "msg"             →  RuntimeError (Stash.Core/Runtime/RuntimeError.cs:25)
throw {type, message}   →  RuntimeError with .ErrorType and .Properties
                           ─ travels through C# call stack ─
                           caught by VM dispatch loop in
                           VirtualMachine.Dispatch.cs:115
                           converted via                         ──→    StashError (Stash.Core/Runtime/Types/StashError.cs:12)
                           StashError.FromRuntimeError                    .message, .type, .stack, .suppressed, plus Properties
                                                                          implements IVMTyped, IVMFieldAccessible, etc.
```

There are also two satellite C# exception types that bypass the user-visible system:

- `ExitException` (`ExitException.cs:11`) — `process.exit()`. Catch-immune; runs defers then terminates.
- `ScriptCancelledException` (`ScriptCancelledException.cs:6`) — currently unused in dispatch (`OperationCanceledException` is caught instead at `Dispatch.cs:91`).
- `StepLimitExceededException` — debugger limit; not user-handleable.
- `AssertionError` (`AssertionError.cs:9`) — extends `RuntimeError` with `Expected`/`Actual` for the test runner. Clean inheritance, used by the TAP runner via `catch (AssertionError ex)` (`TestBuiltIns.cs:301`).

And a fourth representation that nobody talks about: the **`StashStruct` registered in globals as `IsBuiltIn = true`** (`GlobalBuiltIns.cs:291`–`361`), which is what makes `is ValueError` work and what `throw ValueError { message: "x" }` constructs _for one instruction_ before `ExecuteThrow` (`VirtualMachine.ControlFlow.cs:49`–`67`) tears it down into a `RuntimeError`. So:

```
StashInstance ─(ExecuteThrow)→ RuntimeError ─(Dispatch outer catch)→ StashError
```

The struct definition exists in two places:

1. The C# `record ValueErrorStruct` in `GlobalBuiltIns.cs` (drives the source generator's stdlib registration).
2. The `_subtypes` HashSet in `ErrorTypeRegistry.cs:15` (drives matching).

These must agree. **There is no test that asserts they do.** Adding a new error type to one but not the other would silently break in subtle ways (matching works but `is` doesn't, or vice versa).

---

## 2. The Decision History (briefly)

Three relevant historical specs in `.kanban/4-done/`:

1. **`Error Type System — Built-in Error Types and Struct Throw Semantics.md`** (2025-07-24)
   Fixed a critical bug: `throw ValueError { message: "x" }` previously produced an untyped RuntimeError because `ExecuteThrow` had no case for `StashInstance`. The fix added the StashInstance branch (`VirtualMachine.ControlFlow.cs:49`–`67`) and registered the 12 error structs in `GlobalBuiltIns`.

2. **`Error Handling — Architectural Hardening.md`** (2026-04-28)
   Found that error type knowledge lived in 6 separate locations. Introduced `ErrorTypeRegistry` in Core, wired it into `ExecuteIs` and `ExecuteCatchMatch`, removed dead code in CatchMatch, replaced inline StashError construction with the factory.

3. **`Runtime Errors — Stack Traces, Source Context, and Typed Catch.md`** — added the call-stack capture and stack lines.

These specs **did real work** and the code today reflects them. The result is far better than 2025-07. The remaining issues are largely in stdlib uniformity and tooling, not in the VM.

---

## 3. Audit by Dimension

### 3.1 Conceptual Coherence — **Acceptable**

There is a single, documented model: `StashError` is the user-visible value, `RuntimeError` is the internal C# unwinder. The boundary is clean — every `RuntimeError` that escapes a try is converted via `StashError.FromRuntimeError` (`Dispatch.cs:168`). No leakage observed.

**The wart:** the four-layer representation chain (`StashInstance → RuntimeError → StashError`, with parallel struct registration in globals) is not invisible to users when things go wrong. If a user does:

```stash
let e = ValueError { message: "x" };  // a StashInstance
throw e;                              // becomes RuntimeError, then StashError on catch
catch (ValueError caught) {
    typeof(caught) == "Error"         // returns "Error", not "ValueError"
}
```

The runtime type of a caught error is `"Error"` (because `StashError.VMTypeName = "Error"`, `StashError.cs:91`), but the `.type` field is `"ValueError"`. This is two different "type" concepts living on the same object. It works because users have learned to use `.type` and `is`, but it is a smell.

**Evidence:**

- `StashError.cs:91` — `VMTypeName => "Error"` (overrides `typeof()`)
- `StashError.cs:100`–`102` — `.type` field returns the actual error type string
- `VirtualMachine.TypeOps.cs:96`–`98` — `is ValueError` works against StashError via the registry

### 3.2 Layer Integrity — **Solid**

`ErrorTypeRegistry` lives in `Stash.Core/Runtime/ErrorTypeRegistry.cs:10` (Layer 0). Both VM call sites (`ExecuteIs` at `VirtualMachine.TypeOps.cs:98`, `ExecuteCatchMatch` at `VirtualMachine.ControlFlow.cs:725`) delegate to it. `CatchClause.IsCatchAll` (Stash.Core) consumes it (Layer 4 of the hardening spec was implemented).

`StashError` itself implements four IVM\* protocols (`StashError.cs:12`): `IVMTyped`, `IVMFieldAccessible`, `IVMTruthiness`, `IVMStringifiable`. No hardcoded type cascades for StashError exist in dispatch. The `is StashError` checks I found are all in the right places: `ExecuteThrow` (unpacking a Stash-side `throw e`), `ExecuteCatchMatch` (matching), `ExecuteRethrow` (rethrow), and one in `RuntimeValues` (stringify). None violates the protocol pattern.

**No layer violations found.**

### 3.3 The Catch Model — **Solid**

The unwinding strategy:

- **Try frame stack:** `_exceptionHandlers` is a `List<ExceptionHandler>` on the VM (`Dispatch.cs:20`). `TryBegin` pushes (saving `CatchIP`, `StackLevel`, `FrameIndex`, `ErrorReg`), `TryEnd` pops, the outer `catch (RuntimeError)` filter at `Dispatch.cs:115` peels back to the innermost handler.
- **Stack walk:** uses .NET exception throw, which **is** the expensive path. The dispatch loop runs `RunInner<T>` inside a `while(true)` and the catch resumes execution at the handler's bytecode offset. This means a single try/catch crosses _one_ .NET unwind regardless of how many Stash function frames are between throw and catch.
- **Defer integration:** unwound frames have their defers run before the handler restores (`Dispatch.cs:127`–`135`), errors-during-defer are accumulated as `suppressed`. Multiple defers throwing → all collected.
- **Finally semantics:** `Compiler.Exceptions.cs:201`–`297` synthesizes try/catch/finally as nested try-begin/try-end with explicit error-path duplication of the finally body. Both success and error paths run the finally, which is correct.
- **Error in catch handler:** the catch body is plain bytecode; if it throws, the next outer `_exceptionHandlers` entry catches it (or unhandled boundary). Tested at `InterpreterTests.cs:3688` (`TryFinally_ErrorPropagatesAfterFinally`).
- **Bare rethrow:** `ExecuteRethrow` (`VirtualMachine.ControlFlow.cs:736`) re-throws the **original** `RuntimeError` saved on `StashError.OriginalException`, preserving span and call stack. Falls back to reconstruction if the original is gone (e.g., the user constructs and rethrows a new `StashError` — rare).

**Risks:**

- Iterator disposal during unwind is best-effort with swallowed errors (`VirtualMachine.ControlFlow.cs:766`, `795`). This is correct (you don't want to mask the original throw with a Dispose error), but a defer-error-style accumulation would be more honest.
- The `_exceptionHandlers.Count == 0 && ex.CallStack is null` filter at `Dispatch.cs:41` is to avoid double-capturing the call stack. Subtle — relies on null vs non-null state. A flag would be clearer.

**No bugs found** in the model itself.

### 3.4 Error Value Structure — **Duct tape**

`StashError` has: `Message` (string), `Type` (string), `Stack` (`List<string>?` of pre-formatted lines), `Suppressed` (`List<StashError>?`), `Properties` (`Dictionary<string, object?>?`), and a side-channel `OriginalException` (`RuntimeError?`) used for rethrow.

Problems:

- **`Properties` is stringly-typed.** When `CommandError` carries `exitCode`, `stderr`, `stdout`, `command`, those go into the dictionary at `StashStreamingProcess.cs:533`. Field access at `StashError.cs:122` falls through to `Properties.TryGetValue`. So `err.exitCode` works, but the LSP doesn't know it works (no per-error-type schema is consulted). Errors are heterogeneous bags.
- **The struct-side definition in `GlobalBuiltIns.cs`** declares the fields (e.g., `CommandErrorStruct` has `ExitCode`, `Stderr`, `Stdout`, `Command`) but **the StashError instance doesn't validate against it.** A user who writes `throw CommandError { message: "x" }` (omitting required fields) gets a CommandError with only `message` — undetected.
- **`Stack` is pre-formatted strings**, not structured frames. `[" at f (file:1:2)", ...]`. You can't programmatically inspect "which function did this come from" from Stash code without parsing the string. For a sysadmin scripting language, this is probably fine. For richer error introspection (e.g. masking secrets in logs by frame), it's a wall.
- **Extensibility for users:** a user can `throw { type: "MyAppError", message: "..." }` and `catch (MyAppError e)` works (string equality in `ErrorTypeRegistry.Matches`). But `is MyAppError` returns **false** because the global lookup finds no struct (`VirtualMachine.TypeOps.cs:90`). This asymmetry is documented as deferred work in §5.2 of the hardening spec but is real today.

### 3.5 Stdlib Consistency — **Duct tape (the worst dimension)**

I counted every `throw new RuntimeError` in `Stash.Stdlib/BuiltIns/`:

```
Total throws:           608
Untyped (no errorType): 164  (27%)
```

Per-namespace ratio of typed/total:

| Namespace             | Typed/Total | Verdict         |
| --------------------- | ----------- | --------------- |
| ProcessBuiltIns       | 52/52       | clean           |
| FsBuiltIns            | 47/48       | clean           |
| ConfigBuiltIns        | 8/8         | clean           |
| SshBuiltIns           | 22/22       | clean           |
| ReBuiltIns            | 12/12       | clean           |
| **SftpBuiltIns**      | **0/27**    | **all untyped** |
| **SchedulerBuiltIns** | **0/7**     | **all untyped** |
| **TermBuiltIns**      | **0/6**     | **all untyped** |
| **TestBuiltIns**      | **0/4**     | **all untyped** |
| **TplBuiltIns**       | **0/2**     | **all untyped** |
| MathBuiltIns          | 2/9         | mostly untyped  |
| IoBuiltIns            | 3/7         | mostly untyped  |
| TaskBuiltIns          | 4/10        | mostly untyped  |

What this means for the user: doing `catch (IOError e)` correctly catches fs failures but **silently misses sftp failures** because sftp throws bare RuntimeError. Same for scheduler operations. This is exactly the "exception sprawl hassle" anti-goal — except inverted. The user _wants_ to write specific catches but the stdlib gives them no choice but `catch (Error e)` if they want to be safe.

There is also **no per-function metadata declaring what errors a function can throw.** XML doc comments mention errors prose-prose ("throws CommandError"), but neither the source generator (`Stash.Stdlib.Generators`) nor `BuiltInFunction` carry a structured `Throws: [IOError, ValueError]` field. LSP hover therefore cannot tell a user "this function can throw IOError or ValueError."

**Concrete inconsistencies observed across stdlib (from grep):**

| Inconsistency                                                              | Example sites                                                                                                                |
| -------------------------------------------------------------------------- | ---------------------------------------------------------------------------------------------------------------------------- |
| Same kind of failure typed in some places, untyped in others               | `fs.readFile` → IOError; `tpl.load` → untyped (TplBuiltIns.cs:69)                                                            |
| "Bad argument count" — sometimes ValueError, sometimes untyped             | `IoBuiltIns.cs:21` untyped vs `EnvBuiltIns.cs:24` untyped vs `ConvBuiltIns.cs:31` untyped vs `BufBuiltIns.cs` (mostly typed) |
| "Type mismatch" — sometimes TypeError, sometimes untyped                   | `FsBuiltIns.cs:760` TypeError, vs `ConvBuiltIns.cs:38` untyped                                                               |
| Network failures sometimes IOError, sometimes untyped (especially in sftp) | sftp: 0/27 typed                                                                                                             |

These are not bugs; the code works. They are **architectural debt** that the analyzer can't see.

### 3.6 Shell Command Integration — **Acceptable**

`$(cmd)` (non-strict) returns `{ stdout, stderr, exitCode }` and never throws on non-zero exit. `$!(cmd)` (strict) throws `CommandError` on non-zero exit. This is the right design — the user explicitly opts into exception flow with `!`.

`CommandError.Properties` (`StashStreamingProcess.cs:533`) carries `exitCode`, `stderr`, `stdout`, `command` — accessible as `err.exitCode`, `err.stderr`, etc.

**Gaps:**

- **No signal information.** If a command was killed by SIGTERM (exit 143 on Unix) or SIGKILL (137), users only see `exitCode == 143`. No `signal: "SIGTERM"` field. This matters for sysadmin scripts (e.g., distinguishing "command crashed" from "we killed it via timeout").
- **No timing data.** `DurationMs` would be cheap.
- **Spawn failure** (file not found, no permissions) is `CommandError` with the same shape but no `exitCode` semantics ("Failed to start process: ..."). Indistinguishable from "process started, exited 1" unless the user reads the message.
- **Pipeline stages:** stage failure is collapsed to one CommandError with the offending stage's info. No `stages: [...]` array showing exit codes per stage.

These are _future improvements_, not duct tape. The existing design is intentional and decent.

### 3.7 Static Analysis — **Acceptable for what exists**

`Stash.Analysis/Models/DiagnosticDescriptors.cs:29`–`32` defines four error-related rules:

- **SA0160** Bare rethrow outside catch (Error)
- **SA0161** Unreachable catch clause after catch-all (Error)
- **SA0162** Duplicate catch-all (Warning)
- **SA0163** Catching generic RuntimeError (Warning)

Plus **SA0814** Non-blocking lock without try (Warning), and a few more around `defer`/control-flow.

What's **missing** that other languages have:

- **No "function may throw X" inference.** Because there's no source-of-truth for what functions throw, this can't exist.
- **No unhandled-error-flow analysis.** Java has it (checked exceptions); Swift has it (`throws` keyword). Stash has nothing.
- **No exhaustiveness on typed catches.** `try { stat() } catch (TypeError e) {}` doesn't warn that `stat` can throw `IOError`.
- **No `throw` flow analysis.** A function that always throws at the top doesn't get unreachable-code warnings on subsequent statements (SA0106 covers some cases via "unreachable after throw" but not the chained-call case).

**Verdict:** what's there is well-designed; the absences are large because the substrate (per-function `Throws:` metadata) doesn't exist.

### 3.8 Tooling Integration — **Mixed**

**DAP — Solid.** `DebugSession.cs:82` defines `_breakOnAllExceptions`; the VM hook at `VirtualMachine.Debug.cs:46` calls `ShouldBreakOnException(ex)` and then `OnError(ex, callStack, threadId)`. The user can break on every uncaught error via DAP's standard exceptionBreakpoints request. This is correct and matches what any debugger user expects.

**LSP — Poor.** I grepped `Stash.Lsp/` for `StashError`, `Throws`, `errorType` — zero hits. The LSP knows the _struct definitions_ of error types (because they go through the standard `StashStruct` registry path), so completion of `ValueError {` field-prompts works. But:

- No "this function can throw X" hover.
- No quick-fix to add a catch clause.
- No completion of error type names inside `catch (...)` parens (it falls back to general identifier completion).

**Playground/VS Code grammar/etc.** — tokenizes `throw`, `try`, `catch`, `finally` as keywords. No semantic awareness beyond that.

### 3.9 Performance — **Acceptable for sysadmin scripting**

The implementation uses .NET exceptions for unwinding. .NET exception throw on a modern x64 with frames-to-unwind is ~10–50 microseconds depending on stack depth. This is **catastrophic** for a tight loop using try/catch as control flow (e.g. parsing a number per iteration via `try { conv.toInt(s) } catch { 0 }`). It is **fine** for a sysadmin script that runs commands at human-scale.

What I did NOT find:

- No try-frame side-table for pre-allocated handlers.
- No "fast unwind" path that bypasses .NET exception machinery for small jumps.
- No exception-free sentinel return path for hot stdlib functions like `conv.toInt`.

These are not surprising — the VM was designed for sysadmin workloads. The user already has `try (expr)` for swallowing errors-as-values which avoids the cost when the _typical_ path is success but the user wants an inline default. The structural concern is whether stdlib functions like `conv.toInt(s)` (parse failure) should provide a `tryParseInt(s) → int?` non-throwing variant in addition. Today they don't.

### 3.10 Duct-Tape Signals — **Mixed**

| Signal                                                      | Found?                                                                                                                                                                                                                                  | Where  |
| ----------------------------------------------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ------ |
| TODO/HACK/FIXME near error code                             | One only — `VirtualMachine.ControlFlow.cs:203` ("TODO: dry mode — skip actual acquisition") which is unrelated to errors per se.                                                                                                        | clean  |
| `if (e is XError)` cascades in dispatch                     | None. All matching goes through `ErrorTypeRegistry.Matches`.                                                                                                                                                                            | clean  |
| String-based error matching (`if msg.contains(...)`)        | Not found in user-facing paths. Stdlib's own error catches do `catch (RuntimeError) { throw; } catch (Exception ex) { throw new RuntimeError(...) }` — that's a normal "wrap unfamiliar exceptions" pattern, not error-string matching. | clean  |
| Catch-all that just rethrows                                | Many: `VirtualMachine.Process.cs:246`, `349`, `393`, etc. **Intentional pattern** — they rethrow Stash RuntimeErrors and wrap arbitrary .NET exceptions into RuntimeErrors. Fine.                                                       | clean  |
| Inconsistent throw points (some throw, some return null)    | Yes, but not really duct tape — this is the language's design. There is no `Result<T>` type. The inconsistency is **between functions that should throw and don't tag types** (covered in §3.5).                                        | medium |
| AssertionError extending RuntimeError                       | Clean inheritance, used coherently by the test harness.                                                                                                                                                                                 | clean  |
| Two parallel codepaths for "compile error vs runtime error" | Yes (CompileError vs RuntimeError) — this is **intentional and correct** since compile errors can never be caught with try/catch, while runtime errors can. Different lifecycle, different needs.                                       | clean  |

The biggest residual smell is the **dual representation of error types in C#**: the `record ValueErrorStruct` in GlobalBuiltIns.cs (used at runtime as a struct registration) AND the `_subtypes` HashSet in ErrorTypeRegistry.cs (used for matching). They MUST stay in sync. No test enforces it.

---

## 4. Inspiration from Other Languages (filtered through the user's anti-goal)

The user's stated anti-goal: "Java/C# exception sprawl, where every function call has 3-5 possible exceptions and you give up and write `catch(Exception)`." So the question is **not** "how do other languages design exceptions" but **"what specifically did each language do to avoid exception sprawl and reduce the cognitive cost of handling failures?"**

### 4.1 Lua — pcall/xpcall

**Model:** No `try/catch`. `error(value)` raises; `pcall(f, ...)` calls `f` and returns `(true, results...)` or `(false, errVal)`. Errors are _first-class values_ (any type).

**Strength for Stash:** Maximally lightweight. A single function call boundary collapses success and failure into a tuple. No type hierarchy to design; the user passes whatever they want as the error value. Fits scripting.

**Weakness:** No structured types means tooling cannot help. Scaling to 100+ error sites means everyone re-invents conventions.

**What to steal:** Stash's `try (expr)` is already morally pcall — `try (jsonParse(s)) ?? {}` already does the right thing. **Continue investing in this form.** Lua's success suggests `try (expr)` should be the _default_ idiom for "convert error to fallback value," with `try { ... } catch` reserved for the few cases needing structured handling.

### 4.2 Powershell — terminating vs non-terminating errors, ErrorAction, $Error

**Model:** Two categories: _terminating_ errors halt the pipeline (treat like exceptions); _non-terminating_ errors are reported but the pipeline continues. Cmdlets accept `-ErrorAction` to convert behaviour. `$Error` is an array of recent errors. Try/catch exists for terminating errors.

**Strength:** Closest to Stash domain (sysadmin on .NET). Recognises that for many sysadmin operations, you want to "keep going" rather than throw. The `ErrorAction` parameter is a brilliant ergonomic — the same Get-ChildItem behaves differently per call site without changing the cmdlet.

**Weakness:** The dual category is genuinely confusing. Users guess wrong about which errors are terminating. `$Error` as a global side-effect bucket is a debugging nightmare.

**What to steal:** **The ErrorAction concept** — but per-call, not global. Imagine:

```stash
fs.readFile("/etc/passwd", on_error: "default" => "")    // returns "" on failure
fs.readFile("/etc/passwd", on_error: "warn")              // logs and returns null
fs.readFile("/etc/passwd")                                // throws (default)
```

Stash already has `try (fs.readFile(...))` ?? `""` for the first form. The proposal would be to formalise it as a cross-cutting convention for stdlib — but doing it via `try (expr) ?? default` is more orthogonal and Stash-native than per-function options. Powershell's pain ("which functions support -ErrorAction?") suggests we should NOT add a per-function knob; keep it at the language level.

**AVOID:** the global `$Error` bucket. Not idempotent, not thread-safe, scales badly.

### 4.3 Go — multi-return errors, errors.Is/As, error wrapping

**Model:** No exceptions for normal errors. Functions return `(value, error)`. `if err != nil { return err }` is the defensive ritual. `errors.Is(err, target)` and `errors.As(err, &target)` for type checks. `fmt.Errorf("doing X: %w", err)` for wrapping.

**Strength:** Errors are values, fully visible in signatures, statically checkable. No "surprise unwind." Wrapping (`%w`) lets you build a causal chain that is queryable via `errors.Is`.

**Weakness:** The `if err != nil { return err }` ritual is the most-criticised part of Go. Cognitive overhead is high. For a _scripting_ language, multi-return per call is too verbose.

**What to steal:** **Error wrapping with cause chains.** Today Stash has no concept of "this IOError was caused by that PermissionError." The `errors.Is/As` model is great prior art:

- StashError gains an optional `cause: Error?` field.
- `is` recurses through the cause chain: `err is PermissionError` is true if any link in the chain is a PermissionError.
- A new `errors.cause(e)` returns the next link.

This is a **small, additive change** and addresses a real gap.

**AVOID:** the multi-return convention. Stash is a scripting language; `(val, err)` tuples everywhere would be a regression.

### 4.4 Swift — `throws` clause, `try` / `try?` / `try!`

**Model:** Functions declare `throws` in the signature. Callers must `try` at the call site. `try?` swallows to optional. `try!` asserts no-throw (crashes if it does). Exhaustive catch is required. Recently: typed throws (`throws SomeError`) lets you specify exactly which type.

**Strength:** **The `try` mark at the call site.** This is the single best idea in error handling for scripting languages. It tells the _reader_ (not just the type checker) that this expression can fail. No surprise unwinds. Combined with `try?` (Swift's swallow-to-optional), it provides three calibrated handling modes inline.

Stash already has a `try` expression that morally matches `try?`. Stash does NOT have:

- A `throws` annotation on functions.
- A `try!` form that asserts non-failure (and panics if it does).
- A requirement to mark fallible call sites.

**Weakness:** Mandatory marking is heavy for a scripting language. Java's checked exceptions are the well-known cautionary tale of "compile-time errors-must-be-handled" turning into developer pain. Swift mitigates this with `try?` (cheap escape) and `try!` (cheap escape with bang), but it's still a non-trivial cognitive load.

**What to steal:**

1. **`try!` as an explicit assertion** — `try! conv.toInt(s)` says "I know this won't fail; if it does, that's a bug." Compiler can encode this as a normal call that unwraps to a panic on error. This documents intent at the call site.
2. **OPTIONAL: `throws T` declarations on user functions** — purely as documentation/LSP-hover signal initially, with optional warnings (not errors) when callers don't handle. Avoid the Java trap by NOT making it required.

**AVOID:** mandatory `try` marking on every fallible call. The user explicitly does NOT want this — it's the Java pain in a different coat.

### 4.5 Zig — error union types `!T`, error sets

**Model:** Error types are _part of the return type_. `fn parse(s: []const u8) !i32` returns either an `i32` or an error. Errors are members of _error sets_: `error{ParseError, OverflowError}`. No data on errors (just a tag) — for richer info, you use a separate channel. Catch with `catch |err| switch (err) { ... }`. No stack traces by default (opt-in).

**Strength:** Pure data flow — no separate exception machinery, no .NET-style 30μs unwinds, just a tagged union on the return path. Error sets compose: a function that calls two fallible functions has the union of their errors in its signature, computed automatically.

**Weakness:** Errors carry no data (Zig design choice). For a sysadmin language where you need `exitCode` and `stderr`, this is too austere. Would require a sidecar mechanism for payload.

**What to steal:** **The mental model of "errors as part of the return type."** `try (expr)` is already this. The Zig insight is that this model _scales_: if every function consistently uses the convention, the language can statically check it without invasive annotations. Stash's `try (expr) ?? default` could become more idiomatic if stdlib functions returned `Result<T>` for "this call commonly fails" cases (e.g. `conv.toInt(s)` could be both `toInt` (throws) and `tryToInt` (returns null/Result)).

**AVOID:** the dataless-error constraint and the requirement to declare error sets in signatures.

### 4.6 Erlang/Elixir — let-it-crash, supervisor trees

**Model:** Don't catch errors in business code. Crash the process; a supervisor restarts it. Failures isolate.

**Strength:** Maps surprisingly well to **fan-out sysadmin work**: "ssh into 50 hosts and run X; some will fail; collect results, report failures, continue." Stash has `task.parallel(...)` — the Erlang principle says "let one task crash; others run; the harness reports."

**Weakness:** Doesn't help inside a single script for routine failures. Supervisor trees are an OS concept Erlang baked in; Stash doesn't have processes.

**What to steal:** For `task.parallel` and similar fan-out APIs — make it **idiomatic to return per-task `Result`-style outcomes** rather than throwing through. Today `task.parallel` returns results; an error in one task likely throws. Reframe: `task.parallel` always returns `[Result<T>, ...]` and the user folds explicitly. This is a stdlib-level idea, not a language-level one.

### 4.7 Rust (briefly, for completeness) — Result<T, E>, the `?` operator

**Model:** No exceptions. `Result<T, E>` is a sum type. `?` propagates errors up: `let x = parse(s)?;` desugars to `match parse(s) { Ok(v) => v, Err(e) => return Err(e.into()) }`.

**What to steal:** The `?` operator is **the single cleanest piece of error-propagation syntax in any language.** It makes "do this thing, return the error if it fails" a single character. Stash could add a `?` postfix for "if this is an Error, throw it (or, in a function that returns `Error?`, return it)":

```stash
let data = parse(input)?;   // throws if parse returned an error
```

This is more interesting than it sounds — it would give Stash a way to thread errors as values through code without ceremony. It's **additive** (existing throw/catch unchanged).

---

## 5. Top Architectural Weaknesses (prioritised)

### Weakness 1 — No machine-readable "what does this function throw" metadata

**Severity:** High. **Effort to fix:** Medium.

What's wrong: the stdlib has 608 throw sites; LSP/analysis cannot tell a user what errors a call can produce. There's no field on `BuiltInFunction` or the source-generator output recording the throw set. XML doc comments mention errors prose-style but aren't structured.

Why it matters: directly causes the user's anti-goal. Without this, users can't write `catch (IOError)` confidently because they don't know which calls throw IOError. They default to `catch (Error e)`. This is **the Java sprawl trap** without even Java's tooling support.

What fixing requires:

- Add a `Throws: string[]` (error type names) to `BuiltInFunction` (or `[StashFn(Throws = ["IOError"])]` attribute on stdlib functions).
- Extend the stdlib source generator to surface this in the LSP metadata.
- Audit the 608 throw sites and tag each function. (Mostly mechanical: most functions throw 1–2 known types.)
- LSP hover surfaces "throws: IOError, ValueError."
- Analysis gets a new diagnostic SA0164 "function may throw X but no catch clause matches."

### Weakness 2 — Untyped throws in stdlib (27%)

**Severity:** High. **Effort to fix:** Low–medium (mechanical).

What's wrong: 164 `throw new RuntimeError(...)` calls without an `errorType:` argument. Six namespaces (sftp, scheduler, term, test, tpl, plus parts of math/io) have **zero** typed throws.

Why it matters: silently breaks `catch (IOError e)` for the affected operations. Users who rely on the type system get false negatives.

What fixing requires:

- Per-namespace audit pass: for each untyped throw, decide the right StashErrorType.
- Most map cleanly: argument-arity errors → ValueError, type mismatches → TypeError, sftp failures → IOError, etc.
- Tests: for each newly-typed throw, add a test that catches by type.
- Coordinate with Weakness 1 — once functions know what they throw, untagged throws become a CI lint.

### Weakness 3 — Dual representation (StashError vs StashStruct registration) with no consistency test

**Severity:** Medium. **Effort to fix:** Low.

What's wrong: built-in error types live in TWO places that must agree: the C# `record XErrorStruct` in `GlobalBuiltIns.cs` (drives runtime struct registration), and the `_subtypes` HashSet in `ErrorTypeRegistry.cs` (drives matching). No mechanism enforces synchronisation.

Why it matters: drift bugs are silent. Adding "DnsError" to one place but not the other means `is DnsError` works one way but not the other (or vice versa), depending on which path was used.

What fixing requires:

- Have `ErrorTypeRegistry` derive its set from the registered structs at runtime (single source of truth: the StashStruct registry, which is itself derived from generator-emitted records). OR keep them separate but add a startup assertion / unit test that the two sets agree.
- Even simpler: a unit test `RegistrySubtypes_MatchGlobalErrorStructs()` that scans `StashErrorTypes` constants vs the global StashStruct registrations.

### Weakness 4 — No error cause chain

**Severity:** Medium (will become high as the language matures). **Effort to fix:** Low (additive).

What's wrong: when stdlib catches a .NET `IOException` and wraps it as a Stash IOError, the original C# exception's cause is discarded. There's no "this IOError was caused by..." linkage.

Why it matters: in real-world sysadmin scripts, the _root cause_ is what users want to log. "Permission denied opening config file" loses information when wrapped to "IOError: failed to read config." Logging libraries traditionally walk a cause chain.

What fixing requires:

- Add `Cause: StashError?` to StashError (and the corresponding `cause: StashError?` field via VMTryGetField).
- Add `errors.cause(e)` and optionally `errors.root(e)` stdlib helpers.
- Update the wrap sites (notably `process.cs` and `fs.cs`) to set the cause when wrapping.
- `is` walks the cause chain when checking against an error type (Go's `errors.Is`).

### Weakness 5 — Shell command errors lack signal/timing info

**Severity:** Low–medium. **Effort to fix:** Low.

What's wrong: `CommandError.Properties` carries `exitCode`, `stderr`, `stdout`, `command` but no `signal` (when killed by a signal) and no `durationMs`.

Why it matters: sysadmin scripts that retry timeouts can't easily distinguish "command crashed" from "we killed it via timeout." The signal information is in `Process.ExitCode` and `WaitForExitAsync`'s sister APIs but not surfaced.

What fixing requires:

- Process.ExitCode + signal detection (Unix: exit > 128 implies signal; Windows: Process.HasExited/ExitCode).
- Add `signal: string?` and `durationMs: int` to CommandErrorStruct + populate at throw sites.

---

## 6. Recommended Evolution Path (NOT a rewrite)

Each phase is independently shippable. They build on the existing code; nothing is thrown away.

### Phase A — Plug the consistency holes (sprint-sized)

Goal: eliminate the silent inconsistencies that already exist, before adding new features.

A1. Audit and tag all 164 untyped stdlib throws. Mechanical pass per namespace.
A2. Add a unit test that asserts `ErrorTypeRegistry._subtypes` and `GlobalBuiltIns` registered error structs agree.
A3. Add a `Throws: string[]` field to `BuiltInFunction` and to the source generator (`[StashFn(Throws = "IOError, ValueError")]`). Initially purely advisory — populated for high-traffic namespaces (fs, process, http, conv, json) first. Surfaced in LSP hover.
A4. Add SA0164 "Catch may not match any throw site" when a typed catch references a type the try body cannot produce (requires A3 metadata; falls back to silent if metadata absent).

**Value delivered:** users can trust `catch (IOError)` to actually catch all IO failures. LSP hover starts showing `throws:`. Analysis warns on dead catch clauses.

### Phase B — Cause chain (small, additive)

B1. Add `Cause: StashError?` to StashError. Field-accessible as `err.cause`.
B2. Add `errors.cause(e)`, `errors.root(e)` stdlib helpers.
B3. Update high-traffic wrap sites in `Stash.Bytecode` and `Stash.Stdlib` to set the cause when re-throwing.
B4. Extend `ErrorTypeRegistry.Matches` with a "match-along-chain" variant. `catch (IOError e)` matches if `e` OR any `e.cause...` is IOError. (Optional; could be opt-in via `is` operator semantics.)

**Value:** scripts that wrap errors keep root information for logging and decision-making.

### Phase C — Steal Swift's `try!` and Rust's `?`

C1. `try! expr` — explicit assertion of no-failure. Compiles to TryBegin/TryEnd with a panic-on-throw branch (pre-formatted "unexpected error" message, source span included).
C2. `expr?` — postfix propagation operator. In a function whose declared return type is `Error?` (or whose body is wrapped in a try/catch), `?` short-circuits and returns the error. Less ambitious initial form: only valid inside `try { ... }` blocks, where `?` rethrows.
C3. (Decision needed.) Should Stash add a `Result<T>`-equivalent type? Recommendation: NO. The existing `try (expr) ?? default` covers it without complicating the type system.

**Value:** call-site marking documents intent (matches user concern about hidden control flow). Inline error propagation reduces the temptation to write `catch (Error e) { throw e; }`.

### Phase D — Static throws inference (ambitious, optional)

D1. Per-function throw inference: the analyser walks function bodies, collects throw sites, propagates through calls (using the BuiltInFunction `Throws` metadata from A3 plus user-function inference). Optional `throws X, Y` annotation on user fn declarations as documentation/override.
D2. Optional warning SA0165 "function declares `throws X` but body throws Y." Initially info-level, never error.
D3. LSP code action: "Add catch clause for X" when a try block's body can throw a type not in the catch.

**Value:** the user gets opt-in checked-exception-style guidance without the Java pain. Warnings, never errors. Always escapable via `try!` or `catch (Error)`.

### Phase E — Shell command enrichment

E1. Add `signal: string?` and `durationMs: int` to CommandError.
E2. Add `stages: Array<{ command, exitCode, signal? }>` for pipeline failures.
E3. Optional: add `process.execWith(opts)` form that lets users opt into richer error data (e.g., capture stack of the spawn command for debugging).

---

## 7. What to Steal (concrete summary)

| Idea                                                               | From                                                           | Where it lands in Stash                                                                                                 |
| ------------------------------------------------------------------ | -------------------------------------------------------------- | ----------------------------------------------------------------------------------------------------------------------- |
| Errors-as-values is the default; throw only when truly exceptional | Lua, Zig                                                       | Continue investing in `try (expr)`. Add `try!` for assertion form. Make `try (expr) ?? default` the idiomatic recovery. |
| The `try` mark at the call site documents intent                   | Swift                                                          | Phase C — `try! expr` and possibly `expr?`. NOT mandatory.                                                              |
| Cause chain via `cause` field; `is` walks the chain                | Go (`errors.Is`/`%w`)                                          | Phase B.                                                                                                                |
| Postfix `?` for error propagation                                  | Rust                                                           | Phase C2.                                                                                                               |
| Per-function "what does this throw" metadata                       | Swift typed throws, Java checked exceptions (without the pain) | Phase A3 — advisory only, populated incrementally.                                                                      |
| Per-task Result-style outcomes for fan-out                         | Erlang/Elixir                                                  | Stdlib evolution — `task.parallel` returns `[Result, ...]`.                                                             |
| Rich shell command error context                                   | Powershell ($Error has cmdlet/category/etc.)                   | Phase E.                                                                                                                |

| What to AVOID                                                                                        |
| ---------------------------------------------------------------------------------------------------- |
| Java's mandatory checked exceptions — produces sprawl, exhaustion. (Phase D is opt-in for a reason.) |
| Powershell's global `$Error` bucket — non-thread-safe, debugging pain.                               |
| Zig's data-less errors — too austere for sysadmin needs.                                             |
| Go's `(val, err)` multi-return convention — too verbose for scripting.                               |
| Swift's mandatory `try` keyword on every fallible call — the user explicitly does not want this.     |
| Common Lisp's conditions/restarts — too exotic, no cultural fit, would confuse 99% of users.         |

---

## 8. Decision Log

### D1: This is an audit + evolution proposal, NOT a redesign

**Chosen:** evolve the existing code in independently shippable phases (A → E).
**Alternative:** the prior `Error Handling — Greenfield Architecture Vision.md` (left in backlog as historical reference).
**Rationale:** the user's clarified requirement is to assess and harden, not rewrite. The existing VM mechanism is solid; the duct tape is at the stdlib edge and in tooling.

### D2: Don't unify StashError and StashInstance into one type (yet)

**Chosen:** keep StashError as a separate runtime type that implements IVMTyped/IVMFieldAccessible. Continue to convert StashInstance → RuntimeError → StashError on throw.
**Alternative:** make errors first-class structs implementing an `Error` interface, retire StashError.
**Rationale:** unification is a major VM rearchitecting (the `Error Handling — Architectural Hardening` spec marks it as Future Work 5.3). The current dual representation is invisible to users in practice; the cost of change is high; the benefit is mostly conceptual cleanliness. Defer.

### D3: Don't add mandatory `try` marking

**Chosen:** call sites remain unmarked; `try!` is optional documentation.
**Alternative:** Swift-style mandatory `try` on every fallible call.
**Rationale:** the user's anti-goal is exception sprawl. Mandatory marking is a different sprawl ("noise on every call") that scripting languages historically reject for good reason.

### D4: Use additive features, not breaking changes

**Chosen:** every phase adds capability without changing existing behaviour. Existing user code keeps working.
**Alternative:** breaking changes (e.g., make `catch (Error)` more strict).
**Rationale:** Stash has users; the migration cost of breaking changes is unjustified for what is mostly a polish-and-tooling problem.

### D5: Phase A is non-negotiable; Phases B–E are optional / sequential

**Chosen:** ship A first (consistency), then prioritise as needed.
**Rationale:** Phase A is where the actual user-visible bugs live (silent miss-catches because of untyped throws, drift between registry and globals). Everything else is enhancement.

---

## 9. Open Questions for the User

Before promoting any phase to `1-todo`, please confirm:

1. **Is Weakness 1 (no `Throws` metadata) the right priority-1 target?** It has the largest leverage but requires touching every stdlib namespace. Worth coordinating with a deprecation-style migration.
2. **Phase C `try!` + `?`** — is this stylistically welcome, or does it feel like Rust/Swift cargo-culting? Stash's syntax is currently very C-family-clean; punctuation operators are a meaningful aesthetic shift.
3. **Phase B cause chain** — should `is IOError` walk the cause chain by default, or only when an explicit `errors.is(e, "IOError")` helper is used? Default-walking is more ergonomic, less surprising; explicit is purer. Go went default-walking.
4. **`try!` panic semantics** — should `try!` on failure produce an unhandled error (same as if no try existed) or a distinguishable "TryBangFailedError" type? Proposal: same unhandled error, with a source-pointer to the `try!` site for the message. Less type proliferation.
5. **Stdlib error tagging migration** — accept it as a multi-PR mechanical pass (preferred), or gate behind a feature flag and run it as one big PR? Recommendation: per-namespace PRs, one file at a time, easy to review.

When these are answered, the phases can be split into concrete `1-todo` specs.
