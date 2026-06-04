# RFC: Stash.Hosting Host SDK MVP

> **Status:** Draft
> **Owner:** Cristian Moraru
> **Created:** 2026-06-04
> **Slug:** stash-hosting-mvp
> **Milestone:** embedding (phase 3b — the host SDK proper)

## Summary

Ship the **measurement-grounding MVP** of `Stash.Hosting`: a new C# assembly exposing an async
`IStashHost` / `StashHost` facade that wraps the existing synchronous `Stash.Bytecode/StashEngine`
in a Lua-`lua_State`-shaped envelope. One host owns one engine; constructing two hosts yields two
universes that share nothing observable (the isolation foundation already shipped in the
`hermetic-vm` feature — this MVP **consumes** it, it does not rebuild it).

The MVP is deliberately small. It delivers an async surface (`CompileAsync` / `RunAsync` /
`CallAsync` / `TryCallAsync`), structured `StashError` extraction (walking
`RuntimeError.CallStack`), a single internal CLR↔Stash marshalling bridge, `IAsyncDisposable`
cleanup, and an `InvokeAsync` that bridges an already-resolving `StashFuture` to a CLR `Task`.
It does **not** ship a VM pool, a snapshot/restore model, host-objects-by-reference, DI
integration, or a CLI rebuild — each of those is an additive follow-up.

The point of shipping this slice now is the **fourth and final phase**: a Release-mode
BenchmarkDotNet + Stopwatch harness that measures cold/warm engine construction, full
create→run-trivial→dispose lifecycle, and warm per-call overhead, and commits the recorded
numbers under this feature dir. Those numbers answer milestone open-question #4 ("how cheap can
a fresh engine be?") and become the empirical gate on whether any future VM pool is justified.

## Motivation

The `embedding` milestone has shipped its isolation foundation (phases 1 and 2 — `readonly`
modifier + hermetic VM) and its event-loop foundation (phase 3a — callback marshaling). Phase 3
is the destination: a host SDK that lets a .NET application embed Stash as a scripting / config
/ policy language. Without it, embedders still face the painful surface documented in the design
analysis §3 (no call-a-function API, blocking-only execution, stringly-typed errors, no
marshalling helpers, no structured disposal).

Two things make this slice the right next unit of work:

1. **The unknowns are gone.** Host-objects-by-reference (the one genuine design unknown) is
   explicitly deferred to v2. The DI integration (`Stash.Hosting.AspNetCore`) is also v2. What
   remains is mostly a thin async layer over `StashEngine` plus a marshalling bridge — small,
   reviewable, and load-bearing on the milestone's "converges" criterion.
2. **The benchmark exists to ground future architectural decisions.** Until we have empirical
   numbers for `new StashHost() ... await using` cost, every conversation about "do we need a
   VM pool?" / "do we need a snapshot model?" is unfalsifiable. This MVP costs little **and**
   produces the measurement that retires open-question #4.

## Goals

- Add a `Stash.Hosting` project (NuGet-shaped, no ASP.NET dep) layered over `Stash.Bytecode` and
  `Stash.Stdlib`. Honors §14.10: ASP.NET-specific DI lives in a **separate, not-yet-created**
  `Stash.Hosting.AspNetCore` package; `Stash.Hosting` stays clean.
- Public surface (the only public types added by this MVP):
  - `IStashHost` interface + `StashHost` implementation (sealed class, `IAsyncDisposable`).
  - `StashHostOptions` (capabilities, step limit, optional `TextWriter` Output/ErrorOutput).
  - `CompiledScript` (opaque wrapper around the existing `StashScript`; `IDisposable` is not
    required — the underlying type holds no native resources).
  - `StashResult` / `StashResult<T>` (the throw-vs-result discriminator output type).
  - `StashError` (structured: `Kind`, `Message`, `Span`, `CallStack` as `IReadOnlyList<StackFrameInfo>`).
  - `StackFrameInfo` (file/line/column/function name).
  - `StashScriptException` (thrown by the `*Async<T>` "throw on failure" variants).
- Methods on `IStashHost`:
  - `Task<CompiledScript> CompileAsync(string source, CancellationToken ct = default)`
  - `Task<StashResult> RunAsync(CompiledScript script, CancellationToken ct = default)`
  - `Task<StashResult<T>> RunAsync<T>(CompiledScript script, CancellationToken ct = default)`
  - `Task<T> CallAsync<T>(string fnName, object? args = null, CancellationToken ct = default)` —
    throws `StashScriptException` on failure (host opts in).
  - `Task<StashResult<T>> TryCallAsync<T>(string fnName, object? args = null, CancellationToken ct = default)` —
    never throws on script-level failure (host opts in to the result-shape).
  - `Task<T> InvokeAsync<T>(StashFuture future, CancellationToken ct = default)` — bridges an
    already-resolving future via `future.DotNetTask`. Does **not** pump the event-loop callback
    queue (that drain is phase-3b's v2 work).
  - `ValueTask DisposeAsync()` — calls into the static-state cleanup hooks (see Semantics).
- Stateful lua_State semantics, **deliberate and blessed v1 contract**: sequential calls on the
  same host accumulate global state; the only reset mechanism is dispose-and-create-new.
- Single internal marshalling chokepoint (`HostMarshaller`) used by both argument-conversion and
  return-conversion paths — see Cross-Cutting Concerns.
- A `Stash.Hosting.Benchmarks` console project (Release-only, NOT part of `dotnet test`) using
  **BenchmarkDotNet** for construction micro-measurements and Stopwatch median-of-3 for coarse
  lifecycle measurements. Numbers committed to `benchmark-results.md` under the feature dir.

## Non-Goals

The list below is the load-bearing one — implementer must not pull these in to make the diff
"feel complete":

- **No VM pool.** A `StashHost` owns exactly one engine, period. (Demoted from §14.1 by the
  milestone's 2026-06-04 ruling.)
- **No snapshot / restore model.** §14.5 only existed to make a *reused pool VM* look fresh.
  With no pool, snapshot is moot; the deliberate v1 contract is **stateful engine**, calls
  accumulate global state, dispose-to-reset.
- **No host-objects-by-reference.** `RegisterType<T>` gives type identity only (already on
  `StashEngine`); member dispatch on host objects from Stash code is the one genuine design
  unknown and is **explicitly deferred to v2**.
- **No DI integration.** `services.AddStash(...)` and the `Stash.Hosting.AspNetCore` package are
  out — neither is built here.
- **No CLI rebuild.** §14.9 / §16 — `Stash.Cli` continues to use `StashEngine` directly; the
  migration is a separate spec.
- **No event-loop drain integration.** `InvokeAsync` only awaits an already-resolving
  `StashFuture`; it does not pump the per-VM callback queue shipped in 3a. (That drain is the
  natural v2 enhancement.)
- **No reflection-based POCO marshalling.** §14.7 — only the liberal-marshalling subset
  (primitives, `IDictionary<string,object?>`, `IEnumerable`, anonymous types→dict) plus the
  `T=StashValue` / `T=JsonElement` short-circuits. Source-generator marshalling stays v2.
- **No new module loader abstraction.** `IStashModuleLoader` and friends (§4.4 of the design
  doc) stay deferred. The host transparently uses the engine's existing
  `StashCompilationPipeline.LoadModule` path.
- **No new memory budget / path-jail / sub-namespace capability gating** (§14.8 extended
  knobs).
- **No new Stash language syntax, no new stdlib namespace, no new global keyword.** This is a
  pure C# SDK addition — see "Checklist scoping" below.
- **No `docs/Embedding — Hosting Stash in .NET.md`.** That is §15 Increment-3 polish, out of
  this MVP. (XML doc comments on the public types are still required so IntelliSense works.)

### Checklist scoping

Because this MVP adds **no** Stash-language or stdlib surface, most of
`.claude/language-changes.md` does not apply:

- No spec-doc edits.
- No `dotnet run --project Stash.Docs/` regeneration.
- No tooling-compatibility matrix walk (LSP / DAP / Playground / VS Code / Analysis) — none of
  those see a host SDK they don't already see.
- No `examples/*.stash` file.
- No throws-coverage meta-test changes, no completion-snapshot regen.

Tests live in `Stash.Tests/Embedding/` (the existing folder for the multi-engine isolation
suite) using xUnit, alongside the existing `MultiEngineIsolationTests`. The hermetic-VM
acceptance suite **must stay green** — `final_verify` runs the full `dotnet test`.

## Design

### Surface

```csharp
namespace Stash.Hosting;

public sealed class StashHostOptions
{
    public StashCapabilities Capabilities { get; init; } = StashCapabilities.All;
    public long StepLimit { get; init; }                          // 0 = unlimited
    public TextWriter? Output { get; init; }                      // null → discard
    public TextWriter? ErrorOutput { get; init; }                 // null → discard
}

public interface IStashHost : IAsyncDisposable
{
    Task<CompiledScript> CompileAsync(string source, CancellationToken ct = default);

    Task<StashResult>    RunAsync(CompiledScript script,                     CancellationToken ct = default);
    Task<StashResult<T>> RunAsync<T>(CompiledScript script,                  CancellationToken ct = default);

    Task<T>              CallAsync<T>(string fn, object? args = null,        CancellationToken ct = default);
    Task<StashResult<T>> TryCallAsync<T>(string fn, object? args = null,     CancellationToken ct = default);

    Task<T>              InvokeAsync<T>(StashFuture future,                  CancellationToken ct = default);
}

public sealed class StashHost : IStashHost { /* ctors take StashHostOptions */ }

public sealed record StashResult(  bool Success, StashValue?       Value, IReadOnlyList<StashError> Errors);
public sealed record StashResult<T>(bool Success, T?               Value, IReadOnlyList<StashError> Errors);

public sealed record StashError(string Kind, string Message, SourceSpan? Span, IReadOnlyList<StackFrameInfo> CallStack);
public sealed record StackFrameInfo(string? File, int Line, int Column, string? FunctionName);

public sealed class StashScriptException : Exception { public StashError Error { get; } }

public sealed class CompiledScript { /* opaque wrapper around StashScript */ }
```

### Semantics

**One host = one engine.** `new StashHost(...)` lazily owns one `StashEngine`. Two
`StashHost`s in the same process do not share state — this is enforced by the hermetic-VM work
that already shipped (`final_verify` runs `Stash.Tests/Embedding/MultiEngineIsolationTests`).

**Stateful — the deliberate lua_State contract.** Sequential calls on the same `StashHost`
accumulate global state. If `RunAsync(scriptA)` defines `let n = 0; fn inc() { n += 1; return n; }`,
then `CallAsync<long>("inc")` returns `1`, the next returns `2`, and so on. **There is no
snapshot, no checkpoint, no reset short of disposing the host.** This is the documented contract,
not an omission. (Design analysis §14.5's `const`/`let` snapshot was a tool for pool-VM reuse;
with no pool the snapshot is moot.)

**Async = `Task.Run` wrapper.** The underlying `StashEngine` is synchronous. Each method
delegates the sync work to `Task.Run`, threading the per-call `CancellationToken` through to
`StashEngine.CancellationToken` (set on the property — already supported). The result of the
sync work is then surfaced. This is what the design analysis Gap 3 names; the underlying VM
already runs single-threaded, so the host enforces "one in-flight call at a time" via a
`SemaphoreSlim(1, 1)` per host. Reentrant calls (a second `CallAsync` issued while a first is
still running) wait on the semaphore; this preserves the lua_State single-thread model.

**Compile-once, run-many.** `CompileAsync` returns a `CompiledScript` wrapping the existing
`StashScript`. The script is parse-resolve-compiled once; subsequent `RunAsync(script, ct)` runs
re-use the compiled `Chunk`. `CompiledScript` is **not bound to a specific host** (it carries
no engine reference) — though running it through a different host is unspecified and may fail
on resolver state in the wrapped `StashScript`. (v1 contract: use a `CompiledScript` with the
host that created it.)

**Structured errors.** When the VM raises a `RuntimeError`, the host extracts:

- `Kind` = `BuiltInErrorRegistry.NameOf(error)` (e.g. `"ValueError"`, `"TypeError"`,
  `"IOError"`) — or for user `throw { type: "..." }`, the user-supplied type.
- `Message` = `error.Message`.
- `Span` = `error.Span` (may be null).
- `CallStack` = projection of `error.CallStack` (a `List<StackFrame>?`) into the public
  `StackFrameInfo` shape. **This depends on the VM populating `CallStack` on the path that
  escapes to embedding callers.** The implementer must verify this on a real test case before
  P2 closes; if the field is null at the boundary, the cause must be fixed in `Stash.Bytecode`,
  not papered over in the host.

Parse / lex / compile errors are surfaced as `StashError { Kind = "ParseError", Message = ...,
Span = null, CallStack = [] }` — the existing `StashEngine` collects these as strings; the host
upgrades them to the structured shape by going through the lexer/parser directly when
`CompileAsync` is called (re-using the same path the engine uses internally).

**`CallAsync` vs `TryCallAsync`.** `CallAsync<T>` throws `StashScriptException` on script-level
failure (and propagates `OperationCanceledException` for cancellation, `StepLimitExceededException`
for step-limit hit). `TryCallAsync<T>` returns a `StashResult<T>` with `Success = false` and
populated `Errors`, swallowing nothing — both shapes report cancellation and step-limit through
`Errors` rather than throwing. (`OperationCanceledException` is the standard exception for
honoring a cancellation token, so the throw-variant lets it through.)

**Marshalling (the single chokepoint — see Cross-Cutting Concerns).** Arguments to `CallAsync`
go through `HostMarshaller.ToStash(object?)`:

- C# primitives → matching `StashValue` tag (long/double/string/bool/null/byte).
- `byte[]` → buffer.
- `IDictionary<string, object?>` → `StashDictionary` (recursive).
- Anonymous objects → `StashDictionary` (property name → marshalled value, recursive).
- `IEnumerable` (other than `string`) → `List<StashValue>` (recursive).
- `StashValue` → passthrough.
- `JsonElement` → JSON→StashValue bridge (recursive).
- Anything else → `ArgumentException("no marshaller registered for type X")`.

Return values come back through `HostMarshaller.FromStash<T>(StashValue)`:

- `T = StashValue` → passthrough (zero conversion).
- `T = JsonElement` → StashValue→JSON bridge (System.Text.Json).
- `T = string/long/double/bool/byte/byte[]` → unbox the underlying CLR value.
- `T = Dictionary<string, object?>` → flatten (re-uses `StashTypeConverter.ToDictionary`).
- `T = List<StashValue>` → re-uses `StashTypeConverter.ToList`.
- Anything else (including POCO types) → `InvalidCastException` with a clear message that
  reflection-POCO marshalling is v2.

When the wrapped function returns a `StashFuture`, the host **does not** auto-await it here —
callers use `InvokeAsync<T>(future)` to bridge it. This matches the milestone ruling that
"`InvokeAsync` only bridges an already-resolving `StashFuture`; it does not pump the queue."

**`InvokeAsync<T>(StashFuture future, ct)`.** Implementation is `await future.DotNetTask` (the
property already exists on `StashFuture`) wrapped with `ct`-honoring `Task.WhenAny`, then
`HostMarshaller.FromStash<T>` on the result. If `future.IsFaulted` produces a `RuntimeError`,
extract the structured error and throw `StashScriptException`.

**`InvokeAsync` does not own the event loop.** Phase 3a shipped the per-VM `ConcurrentQueue` and
the `time.sleep` / `event.poll()` / `event.loop()` drain points. The host does NOT drain that
queue while waiting on a future — if the future depends on a callback being delivered, the
script that scheduled it must already be at a drain point. This is the v2 enhancement noted in
Non-Goals.

**`DisposeAsync` semantics.**

- **Engine-state side:** the wrapped `StashEngine` has no `Dispose` (its mutable state — globals,
  IC slots, stack — is held by `VirtualMachine`, which is itself non-disposable). The host
  releases its reference and lets the GC collect; the lua_State "this universe is gone" promise
  is satisfied by `_engine = null` plus the semaphore being disposed.
- **Process-global cleanup-hooks side:** this is the *test-isolation* concern, **not** the
  per-engine isolation concern (per-engine isolation already shipped in hermetic-VM via
  per-VM IC clones and `VMContext` cwd/env overlays). On `DisposeAsync`, the host nulls
  the following static delegate slots so subsequent test fixtures see a clean slate:
  - `Stash.Stdlib.BuiltIns.PromptBuiltIns.ResetPromptFn()`
  - `Stash.Stdlib.BuiltIns.PromptBuiltIns.ResetContinuationFn()`
  - `Stash.Stdlib.BuiltIns.PromptBuiltIns.ResetBootstrapHandler = null`
  - `Stash.Stdlib.BuiltIns.ProcessBuiltIns.HistoryListProvider = null`
  - `Stash.Stdlib.BuiltIns.ProcessBuiltIns.HistoryClearHandler = null`
  - `Stash.Stdlib.BuiltIns.ProcessBuiltIns.HistoryAddHandler = null`
  - `Stash.Stdlib.BuiltIns.CompleteBuiltIns.ResetAllForTesting()`

  These are CLI-only hooks (only `Stash.Cli` ever sets them). Resetting them on dispose is mildly
  cross-purpose to per-engine isolation in the abstract (a host that *did* set them would lose
  them on dispose), but a pure embedder never touches them, so the practical effect is
  test-isolation friendliness. **Decision logged** below.

  **Implementer must verify these symbol names compile** before P3 closes — if any has been
  renamed since this brief was written, use the real name and update this list.

### Implementation Path

The wrapped primitive (`StashEngine`) cannot today provide three of this MVP's deliverables, so
the path involves **small, scoped additions to `Stash.Bytecode/StashEngine.cs`** as well as the
new `Stash.Hosting` project. Ruling #1 ("keep `StashEngine` as the low-level primitive") binds
the host to layer on top — it does NOT forbid extending the primitive. The additions are:

1. **A non-swallowing execution entry on `StashEngine`** (P1) — the existing `Run` /
   `Evaluate` / `Run(StashScript)` each catch `RuntimeError` and stringify the message,
   throwing away `Span` / `CallStack` / `ErrorType`. The host needs the raw `RuntimeError`
   propagated to its boundary so it can build a `StashError`. Add a sibling method (e.g.
   `RunRaw(StashScript)` returning the raw `StashValue` and letting `RuntimeError` /
   `OperationCanceledException` / `StepLimitExceededException` escape). Existing public methods
   keep their current swallowing behavior — additive, not breaking.
2. **A call-a-function primitive on `StashEngine`** (P2) — there is no
   `StashEngine.Call(name, args)` today; embedders must fish the callable out of `vm.Globals`,
   cast, and call `IStashCallable.Call(VMContext, args)` by hand. Add a
   `CallFunction(string name, ReadOnlySpan<StashValue> args)` that performs the lookup
   (clear error if not found / not callable), invokes through the existing context, and returns
   the raw `StashValue`. Errors propagate as in (1).
3. **A `StashFuture` accessor on `StashEngine`** (P3 — if needed) — the call primitive in (2)
   already returns a `StashValue`, which wraps the `StashFuture` when the function is `async fn`.
   No extra accessor is needed — the host extracts the future from the result and feeds it to
   `InvokeAsync`.

The path through the codebase, end to end:

```
Host call (CallAsync<T>("greet", new { name = "Alice" }, ct))
  → SemaphoreSlim wait (single in-flight per host)
  → HostMarshaller.ToStash(args) [single chokepoint]
  → Task.Run(() => engine.CallFunction("greet", marshalledArgs))   [new in P2]
       → engine.EnsureVM() (lazy stdlib registration)
       → look up callable, invoke via VMContext
       → RuntimeError? propagate (no message-stringification) [new in P1]
  → result is a StashValue
       → if StashFuture and T isn't Task-wrapped:
             InvokeAsync flow (P3)
       → else HostMarshaller.FromStash<T>(result) [single chokepoint]
  → return T
```

Phase order:

- **P1.** New `Stash.Hosting` project + `Stash.sln` entry + `IStashHost`/`StashHost`/`CompiledScript`
  + `StashHostOptions` + `CompileAsync` + `RunAsync(script)` / `RunAsync<T>(script)` (string
  errors still — `StashError` shape lands in P2). Adds the non-swallowing `RunRaw` to
  `StashEngine`. End-to-end: a host compiles a top-level script and runs it.
- **P2.** `CallAsync<T>` + `TryCallAsync<T>` + the `HostMarshaller` chokepoint + structured
  `StashError` / `StackFrameInfo` / `StashScriptException`. Adds the `CallFunction` primitive
  to `StashEngine`. All result/error paths now route through the marshaller. End-to-end: a
  host calls a named Stash function with anonymous-object args, gets a typed return, and a
  failing call produces a `StashError` with `Kind` / `Span` / `CallStack`.
- **P3.** `IAsyncDisposable` + cleanup hooks + `InvokeAsync<T>(StashFuture, ct)`. End-to-end:
  a script defines `async fn delayed() -> int { ... }`, `CallAsync<StashFuture>` returns the
  future, `InvokeAsync<long>` awaits the result; disposing the host nulls the static hook
  slots.
- **P4.** `Stash.Hosting.Benchmarks` console project (BenchmarkDotNet + Stopwatch), produced
  numbers committed to `benchmark-results.md`, OQ#4 verdict recorded.

### Cross-Cutting Concerns

| Concern | Single source of truth | Omission prevented by |
| --- | --- | --- |
| CLR↔Stash conversion (both directions, used by `RunAsync<T>`, `CallAsync<T>`, `TryCallAsync<T>`, `InvokeAsync<T>`) | `HostMarshaller` (new, internal to `Stash.Hosting`) | **Construct-lite** — the facade methods hold zero inline conversion logic; `HostMarshaller.ToStash` / `FromStash<T>` are the **only** functions in `Stash.Hosting` that perform `object?`↔`StashValue` translation. A new method added later that wants to convert has nothing else to call, so it must route through the chokepoint. No meta-test (per Make-It-Right doctrine: a 4-phase one-project feature does not warrant a manufactured Detect scanner; the chokepoint is enforced by being the only callable API). |

The rest of the cross-cutting surface is either already handled by foundations (per-engine
isolation lives in hermetic-VM; the event-loop callback queue lives in callback-marshaling) or
genuinely single-subsystem and small enough that no shared invariant exists.

## Acceptance Criteria

End-to-end:

- A host compiles a script, runs it, calls a function in it by name, gets a typed return value.
  `async () => { await using var host = new StashHost(); var s = await host.CompileAsync("fn add(a,b) { return a + b; }"); await host.RunAsync(s); return await host.CallAsync<long>("add", new[] { 2L, 3L }); }` yields `5`.
- A second host constructed in the same process does not observe the first host's globals
  (covered by the `MultiEngineIsolationTests` foundation, re-asserted in a Hosting-level test).
- The stateful-engine contract is observable: two sequential `RunAsync` calls on the same host
  see each other's global mutations. The disposal-resets-state test proves the only
  reset mechanism is dispose-and-new-host.
- The marshalling round-trip works on `int → long`, `string`, `bool`, `null`, anonymous-object →
  dict, `List<int> → List<StashValue>`, and `JsonElement` in both directions.

Error behavior:

- A failing `CallAsync<T>` throws `StashScriptException`; `ex.Error.Kind` is the
  `BuiltInErrorRegistry` name (e.g. `"ValueError"`), `ex.Error.Span` is non-null on errors
  raised from a token-attributable site, and `ex.Error.CallStack` has at least one frame for an
  in-script `throw`. The same call routed through `TryCallAsync<T>` returns `Success = false`
  with the same structured `StashError` in `Errors[0]`, no throw.
- Cancellation: a long-running call honored a `CancellationToken`-fired-after-100ms produces
  `OperationCanceledException` from the throw variant and an `Errors[0].Kind == "Cancelled"`
  from the try variant.

Cross-entrypoint / foundation:

- The hermetic-VM acceptance suite (`Stash.Tests/Embedding/MultiEngineIsolationTests` and
  siblings) stays green — `final_verify` runs the full `dotnet test`.

Benchmark / measurement (P4 closes only when this lands):

- `benchmark-results.md` exists under `.kanban/2-in-progress/stash-hosting-mvp/`, has medians
  for: cold `new StashHost()`, warm `new StashHost()`, full
  create→`RunAsync("0")`→`DisposeAsync` lifecycle, and warm `CallAsync<long>("noop")` per-call
  overhead. The file states a verdict on open-question #4 ("a VM pool is / is not justified at
  these numbers, and here's the threshold").

## Phases

See `plan.yaml` for the script-input form. Recap (numeric only; details in YAML):

- **P1** — `Stash.Hosting` project skeleton + facade + `RunAsync`/`CompileAsync` (strings-for-errors
  ok in P1).
- **P2** — `CallAsync`/`TryCallAsync` + marshalling bridge + structured `StashError`.
- **P3** — `IAsyncDisposable` + cleanup hooks + `InvokeAsync` future-bridge.
- **P4** — `Stash.Hosting.Benchmarks` harness + recorded results + OQ#4 verdict.

## Open Questions

- **Does the VM populate `RuntimeError.CallStack` on the path that escapes to an embedding
  caller?** The field comment says "set by the VM at the catch boundary or unhandled boundary."
  The implementer must confirm this on a real test in P2 before closing the phase. If `null` at
  the host boundary, the fix is in `Stash.Bytecode` (small), not in the host (which can't
  reconstruct it).
- **BenchmarkDotNet adds a NuGet dependency to the repo.** Other repo projects don't use it
  today. The MVP's view: the construction figure is exactly the kind of micro-benchmark where
  naive Stopwatch lies (JIT warmup, DCE, GC noise), and BDN is the right tool — but if a
  reviewer prefers an all-Stopwatch harness, P4 can swap to one and lose some construction-cost
  precision. Default = BDN for construction, Stopwatch median-of-3 for the coarse
  create→run-trivial→dispose lifecycle (consistent with `.claude/performance.md`).
- Should `Stash.Hosting.Benchmarks` live under `benchmarks/` (alongside the existing `.stash`
  microbenchmarks) or as a sibling at repo root? Existing `benchmarks/` is a flat folder of
  per-language `.stash`/`.py`/etc. files, no csproj. **Default: place the new csproj at
  `Stash.Hosting.Benchmarks/` at repo root** to follow the existing `Stash.*` project shape;
  implementer may relocate if a stronger convention turns up.

## Decision Log

| Date | Decision | Rationale |
| --- | --- | --- |
| 2026-06-04 | **Stateful-engine semantics are the blessed v1 contract.** Sequential calls accumulate globals; the only reset is dispose-and-new-host. No snapshot, no checkpoint. | Milestone ruling 2026-06-04 #2. The §14.5 snapshot model existed only to make a *reused pool VM* look fresh; with no pool the snapshot is moot. Stateful is the opposite of what §14.5 engineered — chosen on purpose, not by omission. |
| 2026-06-04 | **No VM pool.** | Milestone ruling 2026-06-04 #3. Cheap engine construction may make a pool unnecessary; phase 4's benchmark is the empirical gate. |
| 2026-06-04 | **Host-objects-by-reference deferred to v2.** | Milestone ruling 2026-06-04 #4. The one genuine high-unknown design area is cut from the MVP; `RegisterType<T>` gives type identity only, member dispatch is v2. |
| 2026-06-04 | **Event-loop ↔ `InvokeAsync` drain deferred to v2.** | Milestone ruling 2026-06-04 #5. `InvokeAsync` only bridges an already-resolving `StashFuture`. The 3a callback queue and `event.poll()` drain stand on their own; integrating them with `InvokeAsync` is additive. |
| 2026-06-04 | **`Stash.Bytecode/StashEngine.cs` is in scope** for primitive additions (a non-swallowing execute path and a call-a-function-by-name primitive). | The wrapped primitive cannot today provide structured errors or call-by-name without these. Ruling #1 says "keep `StashEngine` as the low-level primitive" — extending it stays consistent; reaching past it from the host into `VirtualMachine` would violate the layering. |
| 2026-06-04 | **Marshalling is a single internal chokepoint, no meta-test.** | The Construct-lite shape — `HostMarshaller` is the only API in `Stash.Hosting` that does `object?↔StashValue` — is the right level under the Make-It-Right doctrine. A 4-phase, single-project MVP does not warrant a manufactured Detect scanner. |
| 2026-06-04 | **Disposal nulls process-global static hooks (Prompt/Process/Complete).** | Test-isolation friendliness. These are CLI-only static slots; a pure embedder never sets them. Slightly cross-purpose to per-engine isolation in the abstract (a host that *did* set them would lose them on dispose), accepted because no current embedder does. The Definition of Hosts will not promise to preserve them. |
| 2026-06-04 | **This is a C# SDK addition, NOT a Stash language/stdlib change.** The `.claude/language-changes.md` checklist largely does not apply — no spec edits, no stdlib reference regen, no LSP/DAP/Playground matrix, no `examples/*.stash`, no throws-coverage/completion-snapshot changes. | The MVP adds no Stash-visible syntax or namespace. Said explicitly so the implementer does not over-scope. |
| 2026-06-04 | **Benchmark = BenchmarkDotNet (construction micro) + Stopwatch median-of-3 (lifecycle).** Lives in `Stash.Hosting.Benchmarks` console project, Release-only, **NOT** in `dotnet test`. | BDN handles JIT warmup / DCE / GC noise that defeat naive Stopwatch on micro-construction figures; Stopwatch is fine for coarse lifecycle measurement and matches `.claude/performance.md`. Putting it in `dotnet test` would be flaky (per the AOT-enum-self-test precedent that broke the registry suite). |
