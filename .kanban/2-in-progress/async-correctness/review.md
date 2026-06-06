# async-correctness — Review

> Produced by `/feature-review`. One finding per H2 section.
> Each finding header MUST be parseable: `## Fxx — [SEVERITY] short title`.
> `/resolve <feature> Fxx [Fyy...]` reads the selected section(s) and dispatches a Resolver.

**Scope reviewed:** commits `c2ce0830..cae1dcc0` on branch `feature/async-correctness`
**Brief:** ../brief.md
**Generated:** 2026-06-06

---

## F01 — [IMPORTANT] `process.pid` and `process.detach` metadata declares `<exception cref="StateError">` that the runtime provably cannot throw; the published reference now lies to users

**Status:** open
**Files:** `Stash.Stdlib/BuiltIns/ProcessBuiltIns.cs:458`, `Stash.Stdlib/BuiltIns/ProcessBuiltIns.cs:535-536`, `docs/Stash — Standard Library Reference.md:6859,6863`
**Phase:** P5 (added in P1; P5 added the StateError contract for the consumer subset and should have cleaned these)
**Commit:** `410ef8fe` (P1 metadata), `7d6bde6b` (P5 runtime; opportunity missed)

### Observation

In P1 the StateError `<exception>` tag was added to every `process.*` function that takes a Process handle, including `Pid` and `Detach`. The P5 runtime implementation deliberately keeps these two outside the StateError boundary:

`ProcessBuiltIns.cs:461` `Pid`:
```csharp
private static StashValue Pid(IInterpreterContext ctx, [StashParam(Name = "handle")] StashValue handleVal)
{
    var handle = ExtractProcessHandle(handleVal, "process.pid");
    return handle.GetField("pid", null);
}
```
`Pid` reads the pid field off the StashInstance directly. It never calls `ResolveTrackedProcess`. The only throw is `TypeError` from `ExtractProcessHandle`. **It cannot throw `StateError`.**

`ProcessBuiltIns.cs:539` `Detach` carries the StateError tag, but the code comment immediately below it is explicit:
```csharp
// detach is special: we use FindIndex directly (not ResolveTrackedProcess) because
// detach is an intentional removal operation — cross-VM throws StateError only for
// consumer operations (wait/kill/read/etc.), not for detach itself.
int idx = ctx.TrackedProcesses.FindIndex(e => ReferenceEquals(e.Handle, handle));
if (idx >= 0) { … return true; }
return StashValue.FromBool(false);
```
`Detach` on a cross-VM handle returns `false`; it never throws `StateError`.

The reference (`docs/Stash — Standard Library Reference.md:6859,6863`) now claims both throw `StateError`:
```
| `process.pid`     | `any` | `TypeError`, `StateError` | …
| `process.detach`  | `any` | `TypeError`, `StateError` | …
```

### Why this matters

This is a metadata↔runtime mismatch that **no meta-test catches** — `Wave1ThrowsCoverageTests` checks that every declared throw cref is registered (it is) and that throws are tagged (they are), not that a declared throw is actually reachable.

The blast radius is bigger than the C# XML comment:

1. The published `docs/Stash — Standard Library Reference.md` is generated from this metadata (P1 confirmed). End users reading the reference will write a `try { … } catch (StateError) { … }` around `process.pid(parentHandle)` (or around `process.detach`) and the catch clause is dead code. This is user-facing wrong documentation.
2. The contract is the deliverable of this feature. F01 demonstrates that the metadata-leads-implementation strategy from the brief's decision log ("Front-load all stdlib metadata + reference regen in P1 … meta-tests check metadata↔reference, not metadata↔runtime") has a known gap that needs a runtime alignment pass — and P5 didn't do it for these two functions.

### Suggested fix

Two surgical edits, then regenerate the reference:

1. `ProcessBuiltIns.cs:458` — remove the `<exception cref="StateError">…</exception>` line above `Pid`.
2. `ProcessBuiltIns.cs:535-536` — remove the `<exception cref="StateError">…</exception>` line above `Detach`.
3. Regenerate: `dotnet run --project Stash.Docs/`
4. Verify the reference table at lines 6859,6863 now lists only `TypeError` (and other untouched types) for `process.pid` and `process.detach`.

The alternative (extend the StateError throw to these two functions) is technically possible but contradicts the explicit comment in `Detach`'s body and is unmotivated for `Pid` (reading the pid value off a handle is harmless cross-VM).

### Verify

```bash
dotnet run --project Stash.Docs/
dotnet test --filter "FullyQualifiedName~Wave1ThrowsCoverageTests|FullyQualifiedName~StandardLibraryReferenceTests"
grep -nE "process\.(pid|detach).*StateError" "docs/Stash — Standard Library Reference.md"  # expect zero hits
```

---

## F02 — [IMPORTANT] D1 EmbeddedMode gate is not actually exercised; `EmbeddedMode_UnobservedFault_ZeroStderr` passes vacuously

**Status:** open
**Files:** `Stash.Tests/Interpreting/Async/UnobservedAndExit/EmbeddedModeGateTests.cs:14-31`, `Stash.Cli/Program.cs:1291-1298`, `Stash.Bytecode/StashEngine.cs`
**Phase:** P6
**Commit:** `695e51dd`

### Observation

The done_when for P6 specifies:
> "The same script run via `StashEngine` with `EmbeddedMode = true` writes zero stderr (EmbeddedMode gate)."

The implementing test is `EmbeddedModeGateTests.cs:14`:
```csharp
[Fact]
public void EmbeddedMode_UnobservedFault_ZeroStderr()
{
    var errSw = new StringWriter { NewLine = "\n" };
    var engine = new StashEngine();
    engine.ErrorOutput = errSw;
    engine.Output = TextWriter.Null;
    engine.Run(@"
task.run(() => { throw ValueError { message: ""oops"" }; });
time.sleep(0.3);
");
    Assert.Equal("", errSw.ToString());
}
```

But the `EmbeddedMode` gate **only exists in `Stash.Cli/Program.cs:1291`**:
```csharp
private static void ReportUnobservedFaults(VirtualMachine? vm)
{
    if (vm is null) return;
    if (vm.EmbeddedMode) return;          // ← THE gate
    UnobservedFaultReporter.Report(vm.SpawnedFutures, Console.Error);
}
```

`StashEngine.Run(…)` does not call `ReportUnobservedFaults` at all — there is no call site for it outside `Program.cs`. So `errSw` is empty because the reporter is **never invoked** from the engine path, not because the EmbeddedMode short-circuit fired.

Concretely: delete the `if (vm.EmbeddedMode) return;` line on `Program.cs:1295` and this test would still pass green. The "EmbeddedMode gate" test has zero coverage of the gate.

### Why this matters

Two failure modes:

1. The brief explicitly named "EmbeddedMode hosts should not get surprise stderr writes" as a Goal and a done_when. The test asserting that property doesn't actually test it — a future change that wires `ReportUnobservedFaults` into `StashEngine` without re-checking `EmbeddedMode` would not be caught by this test, because the test predates the wiring.
2. The done_when reads "via `StashEngine` with `EmbeddedMode = true`" — the actual production gate is on `vm.EmbeddedMode` in `Program.cs`, not on the engine. The test does not check that `engine.EmbeddedMode` is true (it never sets it; it defaults), so even if the engine grew a reporter call the test would only catch it if the engine also set `EmbeddedMode = true` by construction, which is implementation-detail-dependent.

### Suggested fix

Replace the script-via-engine test with a direct test of the actual gate. One of:

A. (preferred) Call `Program.ReportUnobservedFaults` via reflection / `InternalsVisibleTo` against two VMs, one with `EmbeddedMode = true` and one with it false, both registered with a pre-faulted future. Assert: false → stderr non-empty; true → stderr empty. This tests the exact gate line.

B. Restructure: hoist the `if (EmbeddedMode) return;` short-circuit into `UnobservedFaultReporter.Report(SpawnedFutureRegistry, TextWriter, bool embeddedMode)` so the gate becomes unit-testable inside `Stash.Bytecode`. Then both CLI and engine call the same function, and the existing test pattern (call `UnobservedFaultReporter.Report` with `embeddedMode = true` vs `false`) becomes the gate test.

Either way: the test that calls `engine.Run(...)` and asserts empty stderr is fine to *keep* (it documents that the engine doesn't surprise-report), but it must not be the *only* test bearing the "EmbeddedMode gate" name.

### Verify

```bash
dotnet test --filter "FullyQualifiedName~EmbeddedModeGateTests"
# Then mutation-test by hand: remove `if (vm.EmbeddedMode) return;` from Program.cs:1295
# and re-run — the gate test must go RED.
```

---

## F03 — [IMPORTANT] D1 CLI driver wiring (RunFile/RunSource exit hook) has no test exercising the production path

**Status:** open
**Files:** `Stash.Cli/Program.cs:587,594,600,683,690,696`, `Stash.Tests/Interpreting/Async/UnobservedAndExit/UnobservedReportTests.cs`
**Phase:** P6
**Commit:** `695e51dd`

### Observation

The brief done_when #1 specifies the CLI invocation:
> "A Stash script `task.run(() => { throw ValueError(\"oops\"); });` (no await) run via the CLI writes exactly one `warning: 1 unobserved async error(s):` block to `stderr` with `ValueError: oops`; `stdout` is unchanged."

And done_when #7:
> "When the script itself threw a top-level error, the unobserved-task report still fires after the primary error message (a primary error does not excuse a silently-dropped one)."

The `UnobservedFaultReporter.Report(…)` mechanism is well-tested in isolation (`UnobservedReportTests`, `ConsumerEnumerationTests`, `InFlightDropGotchaTests`). What is **not** tested is the CLI driver wiring itself — the six `ReportUnobservedFaults(_activeVM)` calls in `Program.cs` at lines 587, 594, 600 (`RunSource`) and 683, 690, 696 (`RunFile`):

- The `finally`-block ordering (after `CleanupTrackedProcesses`? before? does the after-primary-error path actually fire before `Environment.Exit(70)` returns?).
- The interaction between the `try-catch` for `RuntimeError`/`OperationCanceledException` and the `finally`'s `if (!primaryErrored)` gate.
- The done_when #7 "primary error does not excuse a silently-dropped one" claim — the implementation calls `ReportUnobservedFaults` from inside the `catch` before `Environment.Exit(70)`, but no test asserts this ordering and the wire-up.

`UnobservedReportTests.RealPath_TaskRunThrows_RegistrationAndReport` exercises a real `task.run` (Chokepoint 2 — `ctx.RegisterFuture` invocation), but it calls `UnobservedFaultReporter.Report` directly on the VM's registry — never through `Program.RunFile` / `Program.RunSource`. That's a mechanism test, not a wiring test.

### Why this matters

Review priority #2: "End-to-end behavior: production paths are wired, not only isolated helpers." The single production wire (CLI driver `finally` block) is the entire reason D1 is observable to users — that wire is the load-bearing piece for a script run from the command line. A future refactor that:

- moves `_activeVM = null` above `ReportUnobservedFaults`, or
- adds an early-return path that skips `finally` (e.g., `Environment.FailFast`), or
- removes the call from the `catch` branch (breaking done_when #7),

would not be caught by any existing test. The mechanism would still pass green; the contract would silently regress.

The done_when checklist treats #1 ("run via the CLI") as satisfied by the registry/reporter-mechanism tests, but those tests do not run `dotnet run --project Stash.Cli/`.

### Suggested fix

Add at least one end-to-end CLI test under `Stash.Tests/Cli/` that:

1. Writes a small temporary `.stash` file containing `task.run(() => { throw ValueError { message: "oops" }; });` and a `time.sleep(0.3);` line.
2. Launches the CLI as a subprocess: `dotnet run --project Stash.Cli/ -- <tmpfile>` (or builds the AOT binary once and reuses it).
3. Captures stdout + stderr + exit code.
4. Asserts: exit code is 0, stdout is empty, stderr contains exactly one `warning: 1 unobserved async error(s):` line and the `ValueError: oops` body line.

A second test for done_when #7: a script whose top-level throws AND has an unobserved fault — assert the primary error appears AND the unobserved warning appears AND exit code is 70.

Subprocess invocation is the right level for a "wired into the CLI driver" test — calling `Program.Main` directly is unsafe (it calls `Environment.Exit`).

### Verify

```bash
dotnet build Stash.sln
dotnet test --filter "FullyQualifiedName~Stash.Tests.Cli.UnobservedAsync"   # new test name
```

---

## F04 — [MINOR] Cross-cutting "Construct" claim is actually Detect — `SpawnedFutureRegistry` is nullable and `RegisterFuture` silently no-ops on null

**Status:** open
**Files:** `Stash.Bytecode/Runtime/VMContext.cs:437,720`, `Stash.Core/Runtime/IInterpreterContext.cs:114`, brief §Cross-Cutting Concerns row 2
**Phase:** P6
**Commit:** `695e51dd`

### Observation

The brief's cross-cutting table promises Construct-level prevention:

> "**Construct** — the registry is a required ctor / shared-field parameter on the child VM construction path. A child VM without a registry cannot exist (the field is non-nullable; the C# compiler enforces a value at every construction site)."

The actual implementation:

- `VMContext.cs:437`: `internal SpawnedFutureRegistry? SpawnedFutures { get; set; }` — **nullable**.
- `VMContext.cs:720`: `public void RegisterFuture(StashFuture future) => SpawnedFutures?.Register(future);` — null-conditional, silently no-ops when null.
- `IInterpreterContext.cs:114`: the interface default is `{ }` — silent no-op.
- `VirtualMachine.cs:81`: the VM has `private SpawnedFutureRegistry _spawnedFutures = new SpawnedFutureRegistry();` — every root VM gets its own; child VMs receive it via `SpawnedFutures = SpawnedFutures` assignments at construction time.

The four propagation sites (`Async.cs:101`, `VMContext.Fork:384`, `InvokeCallback:685`, `Modules.cs:114`) all do thread the registry through correctly today, so the behavior is correct — but the enforcement model is **Detect, not Construct**. If a future PR adds a fifth child-VM site and forgets to thread `SpawnedFutures =`, the code still compiles (because the field is nullable) and silently drops registrations on that path. The brief's `Unobserved_AwaitedByEveryConsumer_NotReported` floor test (cited as the safety net) is about consumer enumeration, not propagation — it would not catch a missing propagation site.

### Why this matters

This is brief-parity, not a functional defect. The brief sold a Construct-level guarantee; the runtime delivers a Detect one. That's worth recording so:

- The next contributor reading the brief and looking for the compiler-enforced invariant doesn't waste time looking for it.
- The next cross-cutting design knows that "field on `VMContext`" with a nullable type was chosen for a reason (likely: avoid threading `SpawnedFutureRegistry` through the `IInterpreterContext` parameter list and every test ctor), and that the Construct option is still available but not taken.

The brief itself explicitly distinguishes Construct from Detect in §Cross-Cutting Concerns and elsewhere (`.claude/agents/architect.md` doctrine, recorded as "Construct > Detect > Instruct"). This finding is the gap between the doctrine and the delivered implementation.

### Suggested fix

Either:

A. (cheapest) **Reword the brief table** to record that propagation is Detect-not-Construct, citing the consumer-enumeration test as the safety net. This is what shipped; honest documentation is the bar.

B. (most aligned with the doctrine) Make `IInterpreterContext.RegisterFuture` a required-implementation method (no default), and make `SpawnedFutures` non-nullable on `VMContext` with mandatory construction. Every test that instantiates `VMContext` directly already passes `Globals = …` etc.; a registry constructor parameter is the same shape. Then the field becomes compile-time-enforced for every child-VM site.

C. Add a meta-test that scans for `VirtualMachine` construction expressions and asserts `SpawnedFutures =` appears in each ctor-initializer of a child-creation site. This is a Detect, but a tighter one than the consumer-enumeration test.

Severity is MINOR because the four current sites are correct; the risk is solely future regressions on a new child-VM creation site.

### Verify

If A: re-read the brief table row and confirm it now states Detect.
If B: `dotnet build Stash.sln` — every child-VM construction site must compile.
If C: introduce the meta-test, then mutate one propagation site to remove `SpawnedFutures =`, confirm the meta-test goes RED.

---

## F05 — [MINOR] Static analyzer does not recognize the `event` namespace (SA0202 false positive) — pre-existing scope gap surfaced by this feature's spec

**Status:** open
**Files:** `Stash.Analysis/` (analyzer), `docs/Stash — Language Specification.md:1437` (the spec that names `event.*` as System B)
**Phase:** out-of-scope (pre-existing; uncovered by P7)
**Commit:** —

### Observation

`stash-check` on a file containing `event.poll()` emits:
```
SA0202 [warning] 'event' is not defined.
```

`event` is a real stdlib namespace (`Stash.Stdlib/BuiltIns/EventBuiltIns.cs`, declared `[StashNamespace] public static partial class EventBuiltIns`) with a registered `poll` function. So the static analyzer's name-resolution does not see the `event` namespace.

This feature's spec rewrite (P1) names `event.*` as the canonical System B surface in the two-systems table:
> "Surface | `async fn`, `await`, `task.*`, `arr.par*` | `fs.watch`, `signal.on`, `event.poll`, `event.loop`"

So users who follow the spec and write `event.poll()` will see a spurious `SA0202` warning from the analyzer (including in their IDE's LSP).

The example file `examples/async_correctness.stash` avoids this by not using `event.*`. The dimension tests (`NonInteractionTests.cs`) embed `event.poll()` in Stash source strings, which run through the interpreter (correct) — not through the static analyzer (where the warning would surface).

### Why this matters

`language-changes.md` mandates a static-analysis check for every language/stdlib change:

> "Every language or stdlib change must be checked against the full toolchain. … **Static analysis** | Resolver visitors, type inference, diagnostic rules | `Stash.Analysis/`"

The contract spec (P1) re-baselined `event.*` as a first-class System-B surface that users will reach for. The analyzer's existing failure to know about `event` is a pre-existing defect, but it becomes user-visible in the new spec's name. This review does not assert that the feature should have fixed the analyzer (it's out-of-scope per the brief's Non-Goals on "Bridging the two concurrency systems"), only that the gap should be **filed as a backlog stub** during this review, per `.kanban/CLAUDE.md` bug-template rules.

### Suggested fix

File `.kanban/0-backlog/bugs/analyzer-event-namespace-unknown-sa0202.md` from the bug template:

- Problem: `event.poll()` / `event.loop()` triggers SA0202 in the analyzer.
- Reproduction: `echo 'event.poll();' > /tmp/event.stash && stash-check /tmp/event.stash`.
- Root cause: the analyzer's namespace registry is missing `event`. Likely a registration-table omission in `Stash.Analysis/` parallel to the source-generator's `GeneratedStdlibRegistry.g.cs` — investigate.
- Blast radius: any user-facing or `examples/`-listed script that uses `event.*` will see a spurious warning in the IDE.
- Suggested fix: add `event` to the analyzer's namespace registry (audit other stdlib namespaces while at it — there may be siblings).

This is a separate fix, not part of this feature. Severity MINOR because (a) the warning is non-fatal (the script runs fine), (b) the feature's example does not trigger it.

### Verify

```bash
echo 'event.poll();' > /tmp/event.stash
stash-check /tmp/event.stash
# should NOT emit SA0202 after the fix
rm /tmp/event.stash
```

---

## Summary

Five findings; zero CRITICAL.

**By severity:**
- CRITICAL: 0
- IMPORTANT: 3 (F01, F02, F03)
- MINOR: 2 (F04, F05)

**Overall assessment.** The locked async contract (D1–D5) is functionally delivered. D2 correctly preserves the original error type through `TaskBuiltIns.AwaitAll` by routing each constituent through `GetResult()` + `StashError.FromRuntimeError`. D3's genuine cancellation works via the `IsAsyncChild` discriminator in `VirtualMachine.Dispatch.cs:92,114` and its propagation to all relevant child-VM creation sites (`Async.cs:99`, `VMContext.cs:682`); main-VM Ctrl-C / event.loop cancellation still converts OCE → `CancellationError`, no regression. D4's flatten is implemented via the single `FlattenAsyncCallbackResult` helper reused by `ExecuteParMap`/`ExecuteParFilter`/`ExecuteParForEach` — DRY, with the truthiness check correctly running on the unwrapped value. D5 throws `StateError` from `ResolveTrackedProcess` for the consumer subset of process handles (sockets correctly deferred to a filed backlog stub). D1's `MarkObserved()` chokepoint is wired through every outcome-consuming combinator including `awaitAny` losers and `all`/`race` per-constituent; the registry is propagated to all four documented child-VM creation sites. The Language Specification §Async correctly presents the two-systems model and documents D6–D11 (verified in the doc diff); the Standard Library Reference is regenerated. The dimension suite asserts non-vacuous contracts including the consumer-enumeration `[Theory]` floor test and the row-10 in-flight `[Trait("Category", "Gotcha")]` change-detector. The findings cluster on (i) a metadata↔runtime mismatch that leaks into user-facing reference docs (F01), (ii) two coverage gaps where the production-path wire is not exercised end-to-end (F02 EmbeddedMode gate is vacuous; F03 CLI driver wiring is not subprocess-tested), and (iii) two brief-parity/scope notes (F04 Construct-vs-Detect framing; F05 pre-existing analyzer gap surfaced by the new spec). None of these block the contract being deliverable; F01 and F02 are the most cost-effective to fix (small surgical edits that close real holes the feature itself created or claimed to close).
