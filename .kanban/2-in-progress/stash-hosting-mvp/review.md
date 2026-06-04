# stash-hosting-mvp — Review

> Produced by `/feature-review`. One finding per H2 section.
> Each finding header is parseable: `## Fxx — [SEVERITY] short title`.
> `/resolve <feature> Fxx [Fyy...]` reads the selected section(s) and dispatches a Resolver.

**Scope reviewed:** commits `07ac0e61..33969e3e` on branch `feature/stash-hosting-mvp`
**Brief:** ../brief.md
**Baseline tests:** PASS (failed=0, passed=13421, skipped=6)
**Generated:** 2026-06-04

Verdict in one line: scope is delivered end-to-end, the three VM changes are minimal and
correct, and the marshalling chokepoint holds. The defects below are concentrated in
test-isolation hazard from disposal nulling process-global slots, a brief-design promise
that `CompiledScript` does not actually fulfil (chunk re-compile every run), and a handful
of smaller parity / hygiene gaps.

---

## F01 — [HIGH] Host test classes are not serialized against the process-global hook-owning collections

**Status:** fixed
**Fixed in:** dd465dc6
**Files:** `Stash.Tests/Embedding/StashHostBasicsTests.cs:21`, `Stash.Tests/Embedding/StashHostCallTests.cs:27`, `Stash.Tests/Embedding/StashHostStructuredErrorTests.cs:25`, `Stash.Tests/Embedding/StashHostStatefulnessTests.cs:14-17`, `Stash.Tests/Embedding/StashHostInvokeAsyncTests.cs:20`, `Stash.Tests/Embedding/HostMarshallerTests.cs:26`, `Stash.Tests/Embedding/StashHostDisposalTests.cs:19-31`
**Phase:** P3
**Commit:** 5d5b7acc

### Observation

`StashHost.DisposeAsync` (StashHost.cs:282-302) unconditionally nulls six process-global
static delegate slots and resets a seventh:

```csharp
PromptBuiltIns.ResetPromptFn();
PromptBuiltIns.ResetContinuationFn();
PromptBuiltIns.ResetBootstrapHandler = null;
ProcessBuiltIns.HistoryListProvider  = null;
ProcessBuiltIns.HistoryClearHandler  = null;
ProcessBuiltIns.HistoryAddHandler    = null;
CompleteBuiltIns.ResetAllForTesting();
```

These slots are owned by three other xUnit `[Collection]`s with
`DisableParallelization = true`:

- `PromptTests` — `Stash.Tests/Stdlib/PromptBuiltInsTests.cs:16`,
  `Stash.Tests/Cli/PromptRendererTests.cs:16`,
  `Stash.Tests/Cli/BootstrapLoaderIntegrationTests.cs:19`
- `ProcessHistoryHandlers` — `Stash.Tests/Stdlib/ProcessHistoryTests.cs:21`
- `CompleteTests` — `Stash.Tests/Stdlib/CompleteBuiltInsTests.cs:24`

Only `StashHostDisposalTests` joined the new `[Collection("StashHostStaticSlots")]` (DisableParallelization=true). The other **six** host test classes
(`StashHostBasicsTests`, `StashHostCallTests`, `StashHostStructuredErrorTests`,
`StashHostStatefulnessTests`, `StashHostInvokeAsyncTests`, `HostMarshallerTests`) are
not in any collection — yet every `await using var host = new StashHost()` they perform
triggers `DisposeAsync`, which nulls those slots.

`StashHostStatefulnessTests.cs:14-17` explicitly claims the opposite ("These tests are
standalone (no collection) ... does not touch process-global static slots"), which is
demonstrably untrue: disposing a host nulls all seven slots regardless of which test
file created it.

### Why this matters

xUnit runs `StashHostStaticSlots`, `PromptTests`, `ProcessHistoryHandlers`,
`CompleteTests`, and "no collection (default)" in parallel across the four scheduling
domains. A scenario like the following can run concurrently:

- thread A (in `PromptTests`): `PromptBuiltIns._promptFn = myFn; ... assert _promptFn == myFn`
- thread B (in default scheduling, running `StashHostBasicsTests`):
  `await using var host = new StashHost(); ...` → `DisposeAsync` → `ResetPromptFn()`
  → `PromptBuiltIns._promptFn = null`

Result: a non-deterministic cross-collection flake. The full suite passed clean **this**
run (baseline failed=0); xUnit parallel scheduling is non-deterministic, so a clean run
does not refute the hazard — it merely says it did not race today. With the brief's
acceptance criterion that "the hermetic-VM acceptance suite stays green", this is the
class of latent flake the workflow is meant to catch.

The fileable defect is narrow: the host test classes that construct a `StashHost`
need to share a serialized collection with the existing hook-owning collections. The
Decision Log already accepted the disposal-nulls-globals behavior for production
embedders — that is **not** in question here. Only the test-infra serialization is.

### Suggested fix

A test class can be in exactly one xUnit collection, so "put host tests in three
collections" is not implementable. The minimal real fix is to unify all
process-global-touching classes under a single shared collection — e.g. rename
`StashHostStaticSlots` to `ProcessGlobalSlots` (or introduce a new umbrella) and
change `PromptTests`, `ProcessHistoryHandlers`, `CompleteTests`, and every
`StashHost*Tests` (basics/call/structured/statefulness/invokeasync/marshaller) to use
that single collection name. With one shared `DisableParallelization=true` collection,
xUnit serializes all the offending classes against each other.

Update the misleading comment in
`Stash.Tests/Embedding/StashHostStatefulnessTests.cs:14-17` ("does not touch
process-global static slots") to reflect reality (every `await using` host disposal
touches them).

### Verify

```
dotnet test --filter "FullyQualifiedName~Stash.Tests.Embedding|FullyQualifiedName~PromptBuiltInsTests|FullyQualifiedName~ProcessHistoryTests|FullyQualifiedName~CompleteBuiltInsTests|FullyQualifiedName~PromptRendererTests|FullyQualifiedName~BootstrapLoaderIntegrationTests"
dotnet test
```

(A passing run on its own is necessary but not sufficient; the value comes from removing
the race window, not from observing one fewer flake.)

---

## F02 — [MEDIUM] `CompiledScript` does not actually cache the compiled chunk

**Status:** open
**Files:** `Stash.Bytecode/StashEngine.cs:407-419`, `Stash.Bytecode/StashEngine.cs:597-610`, `Stash.Hosting/CompiledScript.cs:5-15`
**Phase:** P1
**Commit:** 26ba7dd0

### Observation

`brief.md` line 195-200 promises:

> **Compile-once, run-many.** `CompileAsync` returns a `CompiledScript` wrapping the existing `StashScript`. The script is parse-resolve-**compiled once**; subsequent `RunAsync(script, ct)` runs **re-use the compiled `Chunk`**.

The `CompiledScript`'s own XML doc (`Stash.Hosting/CompiledScript.cs:5-7`) calls it "an
opaque wrapper around a compiled Stash script".

The implementation does not deliver this. `StashScript` (StashEngine.cs:597-610) holds
only the AST (`Statements`) and an `IsResolved` flag — no `Chunk` field. `RunRaw`
(StashEngine.cs:394-419) does on every invocation:

```csharp
if (!script.IsResolved) { SemanticResolver.Resolve(script.Statements); script.IsResolved = true; }
Chunk chunk = Compiler.Compile(script.Statements);   // ← every call
...
object? result = vm.Execute(chunk);
```

So `RunAsync(script)` parses/resolves once but **re-compiles the AST → bytecode every
time**. The "Compiled" in `CompiledScript` is currently a misnomer.

### Why this matters

Brief-parity divergence on a design promise that the type name advertises. For warm
`RunAsync` callers (the canonical compile-once-run-many pattern the host is sold on),
this hides full AST→bytecode compilation cost in the per-call path — exactly the cost
the brief said would be amortised. The P4 benchmark in `benchmark-results.md` measured
construction and per-call overhead but did NOT measure `RunAsync(script)` repeatedly on
the same `CompiledScript`, so the regression is uncovered by the numbers recorded.

(Pre-existing `Run(StashScript)` has the same recompile behavior, so this is partly
inherited. But the host introduces a new public type whose name explicitly promises
caching that does not happen — the public contract is what's being violated.)

### Suggested fix

Cache the compiled `Chunk` on `StashScript` (or on the host-level `CompiledScript`).
Two viable shapes:

1. Add an internal `Chunk? CompiledChunk` field to `StashScript`; have `RunRaw` compile
   on miss and reuse on hit. Smallest blast radius; the cache lives on the existing
   shared type.
2. Have `CompiledScript` own the `Chunk` directly: `CompileAsync` calls
   `engine.Compile(source)` to produce the `StashScript`, then forces the
   resolve+compile path, and `CompiledScript` wraps both `StashScript` and the
   resulting `Chunk`. `RunAsync` then calls a new `engine.ExecuteChunk(chunk)` overload
   that skips compilation. Cleaner separation from the legacy engine.

Either way, add a `RunAsync_SameScript_DoesNotRecompile` test that monitors a sentinel
(e.g. a `Compiler.CompileCallCount` counter or an indirect check that lex/parse don't
re-run) to lock the invariant in.

### Verify

```
dotnet test --filter "FullyQualifiedName~StashHostBasicsTests"
# plus a new test asserting the chunk is reused across N runs of the same CompiledScript
```

---

## F03 — [MEDIUM] `"StepLimitExceeded"` kind string repeated inline three times instead of named constant

**Status:** open
**Files:** `Stash.Hosting/StashHost.cs:102`, `Stash.Hosting/StashHost.cs:151`, `Stash.Hosting/StashHost.cs:256`
**Phase:** P2
**Commit:** 712a0875

### Observation

The brief and `CLAUDE.md` "Bounded Domains" rule require a closed-set value to come
from one named constant. `StashError.KindCancelled` already follows this in
`Stash.Hosting/StashError.cs:27` ("Defined here so it appears in exactly one place").

The sibling kind `"StepLimitExceeded"` is then inlined as a raw string literal in three
separate `StashError(...)` construction sites: `StashHost.cs:102` (`RunAsync`),
`StashHost.cs:151` (`RunAsync<T>`), and `StashHost.cs:256` (`TryCallAsync<T>`):

```csharp
new StashError("StepLimitExceeded", ex.Message, null, Array.Empty<StackFrameInfo>())
```

### Why this matters

Bounded-domain duplication. `StepLimitExceededException` is not a `RuntimeError`
subclass, so `BuiltInErrorRegistry.NameOf` is the wrong source — but the kind name is
still a closed-set value the host emits. Three copies of the same literal will drift the
moment one site is renamed and the other two are missed. The codebase has bitten on
this exact pattern before (see [[no-magic-strings]] in MEMORY.md and the registry's
`NoMagicAuthStringsMetaTests`); the project rule is to fix it at the named source, not
to add an enforcer test for a four-phase MVP.

### Suggested fix

Add a second public const beside `KindCancelled`:

```csharp
public sealed record StashError(...)
{
    public const string KindCancelled         = "Cancelled";
    public const string KindStepLimitExceeded = "StepLimitExceeded";
}
```

Replace all three inline literals with `StashError.KindStepLimitExceeded`.

### Verify

```
grep -n '"StepLimitExceeded"' Stash.Hosting/        # should be empty
grep -n 'KindStepLimitExceeded'  Stash.Hosting/     # should be ≥4 (declaration + 3 uses)
dotnet test --filter "FullyQualifiedName~StashHost"
```

---

## F04 — [MEDIUM] `CompileAsync` parse/lex errors surface as `InvalidOperationException` instead of the brief's structured `StashError { Kind = "ParseError" }`

**Status:** open
**Files:** `Stash.Hosting/StashHost.cs:58-63`, `Stash.Hosting/IStashHost.cs:29`
**Phase:** P1
**Commit:** 26ba7dd0

### Observation

`brief.md` lines 214-217 specify:

> Parse / lex / compile errors are surfaced as `StashError { Kind = "ParseError", Message = ..., Span = null, CallStack = [] }` — the existing `StashEngine` collects these as strings; the host upgrades them to the structured shape...

The implementation does the opposite — it stringifies and throws an unstructured
exception (`StashHost.cs:58-63`):

```csharp
StashScript? script = _engine!.Compile(source, out IReadOnlyList<string> errors);
if (script is null || errors.Count > 0)
{
    string msg = errors.Count > 0 ? errors[0] : "Compilation failed.";
    throw new InvalidOperationException($"Stash compilation failed: {msg}");
}
```

`StashHostBasicsTests.CompileAsync_InvalidSource_ThrowsInvalidOperationException`
(`Stash.Tests/Embedding/StashHostBasicsTests.cs:158-164`) codifies this current behavior.

### Why this matters

Brief-parity divergence on the structured-error surface. The whole point of upgrading to
a host SDK was structured errors at the boundary — `CompileAsync` is the first surface
the embedder hits, and it falls back to the same stringly shape the bare `StashEngine`
has had for years. Once a downstream consumer (Razor UI, IDE integration, etc.) starts
matching on `ex.Error.Kind == "ParseError"`, retrofitting the structured shape becomes
an API-breaking change. Fixing it now, while the SDK is unconsumed, is cheap.

P1's `done_when` does not explicitly require structured parse errors, so this is not a
phase-gate failure — it's a Design-section parity gap. The Design section is part of the
contract per the architect agent.

### Suggested fix

Two reasonable shapes:

1. **Throw `StashScriptException` with a `StashError { Kind = "ParseError", ... }`.**
   Symmetric with `CallAsync` runtime errors. Update
   `CompileAsync_InvalidSource_ThrowsInvalidOperationException` to assert
   `StashScriptException` with `Error.Kind == "ParseError"`. Add a `KindParseError`
   const to `StashError`.
2. **Return a `Task<StashResult<CompiledScript>>` so parse errors are not exceptional.**
   Larger surface change. The brief's prose ("surfaced as `StashError`") implies the
   error shape, not the throw-vs-result discriminator; shape (1) is the smaller delta.

Either path: drop the `KindParseError` literal into the centralized constants alongside
`KindCancelled` (related to F03's rule).

### Verify

```
dotnet test --filter "FullyQualifiedName~StashHostBasicsTests|FullyQualifiedName~StashHostStructuredErrorTests"
```

Add at minimum: `CompileAsync_InvalidSource_ThrowsStashScriptException_WithParseErrorKind`.

---

## F05 — [LOW] `InvokeAsync` cancellation-race path leaks a `Task.Delay(Infinite, ct)` when the future wins

**Status:** open
**Files:** `Stash.Hosting/StashHost.cs:323-336`
**Phase:** P3
**Commit:** 5d5b7acc

### Observation

`StashHost.InvokeAsync<T>` races the future against a cancellation task:

```csharp
if (ct.CanBeCanceled && !rawTask.IsCompleted)
{
    var cancelTask = Task.Delay(Timeout.Infinite, ct);
    Task winner = await Task.WhenAny(rawTask, cancelTask).ConfigureAwait(false);
    if (winner == cancelTask)
    {
        ct.ThrowIfCancellationRequested();
    }
}
rawResult = await rawTask.ConfigureAwait(false);
```

If `rawTask` wins, `cancelTask` (a `Task.Delay(Timeout.Infinite, ct)`) is left as an
orphan that completes only when `ct` is eventually cancelled or `ct.Dispose()` runs.
On a long-lived application-scope `CancellationToken`, every `InvokeAsync` call where
the future wins registers a callback that lives until the token's lifetime ends.

### Why this matters

For per-call CTSes (the common case) the callback is GC'd shortly after the CTS is
disposed — no observable leak. For an app-lifetime CTS reused across millions of
`InvokeAsync` calls, registrations accumulate. Bound: at typical embedding scale this
is unlikely to bite, hence LOW.

### Suggested fix

Create a linked CTS scoped to the `InvokeAsync` call and dispose it in `finally`, e.g.:

```csharp
using var linked = ct.CanBeCanceled ? CancellationTokenSource.CreateLinkedTokenSource(ct) : null;
var cancelTask = linked is not null
    ? Task.Delay(Timeout.Infinite, linked.Token)
    : null;
// race, await rawTask, then on the success path: linked?.Cancel(); // signals cancelTask to complete
```

Or simpler — register a single one-shot continuation via `ct.Register` and cancel a
local `TaskCompletionSource` instead of `Task.Delay`.

### Verify

```
dotnet test --filter "FullyQualifiedName~StashHostInvokeAsyncTests"
```

A regression test that constructs a long-lived `CancellationTokenSource`, calls
`InvokeAsync` 1000× against resolving futures, and asserts the CTS's registered
callback count stays bounded would lock the fix in (no public counter today; an
indirect proxy is fine).

---

## F06 — [LOW] `CompileAsync` ignores its `CancellationToken` parameter

**Status:** open
**Files:** `Stash.Hosting/StashHost.cs:51-66`, `Stash.Hosting/IStashHost.cs:27`
**Phase:** P1
**Commit:** 26ba7dd0

### Observation

```csharp
public Task<CompiledScript> CompileAsync(string source, CancellationToken ct = default)
{
    if (source is null) throw new ArgumentNullException(nameof(source));
    ThrowIfDisposed();
    // ct never observed
    StashScript? script = _engine!.Compile(source, out IReadOnlyList<string> errors);
    ...
}
```

The XML doc on `IStashHost.CompileAsync` (IStashHost.cs:27) describes `ct` as
"Optional cancellation token" with no caveat. The implementation never calls
`ct.ThrowIfCancellationRequested()` and never threads `ct` into the parse path.

### Why this matters

Standard .NET convention is that a method taking a `CancellationToken` honors at
least the pre-flight check. A caller passing a pre-cancelled token expects an
`OperationCanceledException`; today they get a successfully-returned
`Task<CompiledScript>` even if their token was cancelled before the call. Low because
compile is fast (microseconds), so cancellation is rarely observable in practice.

### Suggested fix

```csharp
public Task<CompiledScript> CompileAsync(string source, CancellationToken ct = default)
{
    if (source is null) throw new ArgumentNullException(nameof(source));
    ThrowIfDisposed();
    ct.ThrowIfCancellationRequested();
    ...
}
```

Optionally also document on `IStashHost.CompileAsync` that the token is only honored
as a pre-flight check (compilation is too fast for mid-operation cancellation to
matter).

### Verify

```
dotnet test --filter "FullyQualifiedName~StashHostBasicsTests"
```

Add `CompileAsync_PreCancelledToken_ThrowsOperationCanceledException`.

---

## F07 — [LOW] `InvokeAsync` uses `StashValue.FromObject` inline — a small leak of the "single chokepoint" property

**Status:** open
**Files:** `Stash.Hosting/StashHost.cs:350`, `Stash.Hosting/Marshalling/HostMarshaller.cs:1-15`
**Phase:** P3
**Commit:** 5d5b7acc

### Observation

The brief's Cross-Cutting Concerns table says `HostMarshaller` is "the **only**
functions in `Stash.Hosting` that perform `object?↔StashValue` translation". P2's
notes (`plan.yaml:107-111`) direct the reviewer to grep for any inline
`object?↔StashValue` conversion outside the marshaller.

One leak remains. `StashHost.InvokeAsync<T>` (line 350) calls:

```csharp
StashValue stashResult = StashValue.FromObject(rawResult);
return HostMarshaller.FromStash<T>(stashResult)!;
```

`StashValue.FromObject` is the Stash.Bytecode-level lifter for `object? → StashValue`;
it is in fact a conversion outside `HostMarshaller`.

### Why this matters

No correctness bug — `StashValue.FromObject` is the canonical VM lifter the rest of the
codebase uses for the same purpose, and this is on the boundary between
`future.DotNetTask` (returns `Task<object?>`) and the marshaller (consumes `StashValue`).
But the brief's "single API for object↔StashValue" invariant is now strictly false in
the host code, and the next person adding a new entry point may follow this example
rather than the rule. The defect is in the chokepoint property, not in behavior.

### Suggested fix

Add a thin overload to `HostMarshaller` so the conversion lives inside it:

```csharp
public static T? FromStashObject<T>(object? raw) => FromStash<T>(StashValue.FromObject(raw));
```

Then `InvokeAsync` becomes `return HostMarshaller.FromStashObject<T>(rawResult)!;`.

### Verify

```
grep -n 'StashValue\.FromObject\|StashValue\.FromObj\|StashTypeConverter\.' Stash.Hosting/
# Expect only matches inside Stash.Hosting/Marshalling/, never in StashHost.cs
dotnet test --filter "FullyQualifiedName~StashHostInvokeAsyncTests"
```

---

## F08 — [LOW] `JsonStashBridge.WriteValue` silently emits `null` for any non-string/list/dict object (including the documented `byte[]` type)

**Status:** open
**Files:** `Stash.Hosting/Marshalling/JsonStashBridge.cs:111-158`
**Phase:** P2
**Commit:** 712a0875

### Observation

`HostMarshaller.ToStash` documents `byte[]` as a supported argument type — it lifts
into a `StashValue` via `StashValue.FromObj(buf)` (HostMarshaller.cs:44-45). On the
return side, `JsonStashBridge.WriteValue` only handles `string`, `List<StashValue>`,
and `StashDictionary` when `value.Tag == Obj`; everything else falls through:

```csharp
default:
    writer.WriteNullValue();
    break;
```

A `byte[]` inside a Stash dict — or any other Obj-tagged value the user reasonably
might have around — is silently serialized as JSON `null`. There is no test that
exercises this case.

### Why this matters

Silent data loss in the JSON round-trip for a documented input type. The two cases
likely to bite an embedder are (a) `byte[]` (e.g. binary blobs) round-tripped through
`T = JsonElement`, and (b) `StashFuture` instances accidentally serialized — both
become `null` with no log line. Low because the documented JSON map in the bridge's
XML doc only promises mappings for null/bool/number/string/array/object, so a strict
reading says `byte[]` is out of scope; but the host advertises both byte[] inputs and
JsonElement outputs as supported, leaving callers to discover this corner empirically.

### Suggested fix

In `WriteValue`'s `Obj` cases, add explicit handling for `byte[]` (base64-encode per
JSON convention, e.g. `writer.WriteBase64StringValue`) and emit a clearly visible
fallback for anything else — at minimum, throw `InvalidOperationException` naming the
runtime type, rather than silently writing `null`. A loud failure on the rare case is
strictly better than a silent one.

### Verify

```
dotnet test --filter "FullyQualifiedName~HostMarshallerTests"
```

Add `JsonStashBridge_ByteArrayInDict_RoundTripsAsBase64_OrThrows`.

---

## Summary

| Severity | Count |
| --- | --- |
| HIGH | 1 |
| MEDIUM | 3 |
| LOW | 4 |

The three VM-core changes (VirtualMachine.Debug.cs, VirtualMachine.cs CancellationToken
property, VirtualMachine.Functions.cs no-frame async early-return) are minimal,
correctly guarded, and re-use existing patterns (`Run()`'s catch-on-null-CallStack
mirror; `_ct` already a non-readonly field; `IStashCallable` is the other no-frame path
that the early-return also covers).

The `StashEngine.RunRaw` / `CallFunction` additions are additive — existing
`Run`/`Evaluate`/`Run(StashScript)` swallowing behavior is unchanged.

The `HostMarshaller` chokepoint is intact for `ToStash` / `FromStash` (`RunAsync`,
`CallAsync`, `TryCallAsync` all route through it); F07 flags the one strict-property
leak in `InvokeAsync` for completeness.

The benchmark harness is properly excluded from `dotnet test` (verified by grep), the
recorded numbers are internally consistent with the OQ#4 verdict's threshold math, and
the deliberate stateful-engine contract is correctly asserted by
`StashHostStatefulnessTests` (per brief Decision Log #2026-06-04 — not a defect).

Stateful-engine v1 contract, two-host isolation, and the cross-frame `RuntimeError.CallStack`
population invariant are all covered by tests that exercise the real paths
(`StashHostStructuredErrorTests.CallAsync_ValueError_HasKindMessageSpanAndCallStack`,
`StashHostStructuredErrorTests.CallAsync_NestedFunctionThrow_CallStackHasMultipleFrames`,
`StashHostBasicsTests.TwoHosts_NoGlobalLeakAcrossHosts`).
