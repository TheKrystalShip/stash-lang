# host-object-dispatch — Review

> Produced by `/feature-review`. One finding per H2 section.
> Each finding header MUST be parseable: `## Fxx — [SEVERITY] short title`.
> `/resolve <feature> Fxx [Fyy...]` reads the selected section(s) and dispatches a Resolver.

**Scope reviewed:** commits `116f12066ed3d2dbb4d42dac8143e60286f922b6..1e35a1262c78165ad30fbdf60d97c87efa69385e` on branch `feature/host-object-dispatch`
**Brief:** ../brief.md
**Generated:** 2026-06-05

---

## Summary

The feature is well-executed. The "zero VM/bytecode changes" thesis is **upheld** — the
diff touches `Stash.Bytecode/` zero times and `Stash.Core/` exactly once (the narrow
`HostError.cs` file the brief sanctions). The Construct-lite chokepoint, no-magic-strings
discipline, hermetic per-host state (per-instance `_typeRegistrations` and
`_observedTargets`), in-script `HostError` catchability, OnRelease semantics
(once-per-target, never-for-unobserved, MVP-resets-still-run, host-isolation), and
the `done_when` coverage all map cleanly to evidence in the diff.

**Severity counts:** 0 CRITICAL · 0 IMPORTANT · 3 MINOR

The three MINOR findings are scoped narrowly and **do not block** acceptance:

- **F01** — A real correctness gap in nested-return marshalling: a registered host
  instance returned *inside* a dict/array/anonymous object wraps as a `HostHandle`
  (good) but is **never added to `_observedTargets`**, so its `OnRelease` callback
  never fires. The top-level path is correct and well-tested; this only bites the
  nested case, which is untested.
- **F02** — The `benchmark-results.md` interpretation is internally
  self-contradictory (calls the 15 μs property-read both "low-variance / stable"
  and explains it away as "task-scheduling jitter"); the underlying ShortRun
  (3 iterations) is statistically insufficient to support the verdict's *numbers*,
  even if the verdict ("no specialised opcode warranted") is plausible on first
  principles.
- **F03** — The "Construct-lite chokepoint" guarantee is weaker than the brief's
  text implies: the baked closure (which contains the `DynamicInvoke`) is reachable
  in principle from any future code path that touches a `HostMemberDescriptor.Invoke`
  / `AsyncInvoke` field — the property holds today only by convention, not by
  construction. One header comment + one sealed seam would harden it.

### Brief-parity / `done_when` parity check (quick map)

- **Zero VM/bytecode changes** — confirmed. `git diff --stat 116f1206..1e35a126` shows
  `Stash.Bytecode/` is not in the file list; the only `Stash.Core/` touch is the
  10-line `Stash.Core/Runtime/Errors/HostError.cs` the brief mandates.
- **No-magic-strings** — confirmed. `"HostError"` appears in `Stash.Hosting/` only as
  the value of the `StashError.KindHostError` const (one site) and inside XML
  `<see cref>` doc comments (non-load-bearing). All construction sites reference
  `StashError.KindHostError` or throw a typed `HostError(...)`. The
  `KindHostError_MatchesBuiltInErrorRegistryNameOf` test
  (`HostObjectPropertyTests.cs:216`) pins the const ↔ registry-name agreement.
- **Catchable in Stash** — `HostObjectPropertyTests.Script_TryCatch_HostError_ReturnsErrorType`
  (line 229) compiles `try { let _ = player.bad_prop; } catch (e) { return e.type; }`,
  runs it via `RunAsync<string>`, asserts the returned value is `"HostError"`. Real
  VM catch path, not a host-side assertion. `HostObjectAsyncTests.AsyncMethod_FaultedTask_CatchableInStash`
  (line 178) and `HostObjectMethodTests.Method_InScript_TryCatch_HostError_ReturnsErrorType`
  (line 292) repeat the same for async and sync methods.
- **Cancellation vs fault** — `AsyncMethod_MidAwaitCancellation_SurfacesOCE_AndFiiresDelegateCT`
  (line 203) verifies that a mid-await call-CT cancel surfaces `OperationCanceledException`
  from `CallAsync<T>` and the delegate's linked CT was actually fired CLR-side.
  Faults are covered by `AsyncMethod_FaultedTask_SurfacesHostError_WithInnerMessage` (line 139).
- **OnRelease per-host correctness** — `_observedTargets` is a per-instance
  `ConditionalWeakTable`, not static (`StashHost.cs:48`). The disposal loop runs
  per-callback `try/catch` so one bad release doesn't abort the rest or the MVP
  resets (`StashHost.cs:387-401`). `TwoHosts_HermeticIsolation_NoSharedOnReleaseState`
  (line 238) re-asserts the hermetic property through this surface.
- **ToStash ordering** — the registered-host-type branch (line 88-93) precedes
  IDictionary (line 96), anonymous (line 106), and IEnumerable (line 116). A
  registered host class that also implements IEnumerable wraps as a handle, as
  required.
- **`VMTryGetField` unknown-name semantics** — returns false for unknown member
  names so the VM's fallback path raises "cannot access field" (RuntimeError, NOT
  HostError). `HostObjectPropertyTests.Script_UnregisteredMember_FallsThrough_ToExistingError`
  (line 250) pins this and `Assert.NotEqual("HostError", result.Errors[0].Kind)`.
- **Phase done_when bullets** — every bullet in P1/P2/P3/P4/P5 maps to at least one
  named test or to a verifiable artifact (docs regen, benchmark file, MVP-reset
  pinning via the existing `StashHostDisposalTests`). Test-file `done_when`
  coverage tables (each file's XML doc lists the bullets it covers) are accurate
  on spot-check.

---

## F01 — [MINOR] OnRelease misses registered host instances returned nested inside a dict/array/anonymous object

**Status:** fixed
**Fixed in:** c592018e
**Files:** `Stash.Hosting/Marshalling/HostMarshaller.cs:100`, `Stash.Hosting/Marshalling/HostMarshaller.cs:110`, `Stash.Hosting/Marshalling/HostMarshaller.cs:120`
**Phase:** P4 (lifetime — though the root cause sits in the P1/P2 marshaller chokepoint)
**Commit:** 4a35888b

### Observation

`HostMarshaller.ToStash(object?, registrations, observedTargets)` correctly threads
`observedTargets` into the top-level `new HostHandle(...)` call at line 92, but its
**recursive** calls for the three collection branches do not:

- Line 100 (IDictionary): `sd.Set(kv.Key, ToStash(kv.Value, registrations));`
- Line 110 (anonymous type): `sd.Set(prop.Name, ToStash(prop.GetValue(arg), registrations));`
- Line 120 (IEnumerable): `list.Add(ToStash(item, registrations));`

`observedTargets` defaults to `null` on those calls, so a registered CLR instance
returned **nested inside** a `Dictionary<string,object?>`, an anonymous object, or any
`IEnumerable` wraps as a `HostHandle` (good — `registrations` IS forwarded so
by-reference dispatch still works), but `HostHandle`'s ctor at line 70 of
`HostHandle.cs` (`observedTargets?.TryAdd(target, registration);`) silently no-ops on
the null. The target is therefore never added to the host's `_observedTargets` table,
and the disposal loop at `StashHost.cs:387-401` skips it.

### Why this matters

Contradicts the brief's lifetime contract ("OnRelease ... fires for every CLR instance
of T that the engine has *observed* — i.e. that was wrapped in a HostHandle and placed
into VM state ... or returned from a host method / property", brief.md line 398-405,
mirrored on `HostTypeBuilder.OnRelease` doc-comment). A handle nested inside a
return-value collection IS placed into VM state — it just wasn't tracked.

Direct returns are fine: `InvokeHostDelegate.InvokeMethod` calls `HostMarshaller.ToStash(rawResult, allRegistrations, observedTargets)` at line 168 with both arguments, so the top-level handle is registered.

Impact is narrow:

- By-reference dispatch on the nested handle still works correctly (it carries
  `registrations`; properties/methods still dispatch through the same chokepoint).
- Only the `OnRelease` opt-in cleanup is skipped. The host-owns-disposal default
  still works.

Currently **untested** — no Embedding test returns a registered host instance nested
inside a collection. `HostObjectMethodTests #8` returns `Self()` at the top level
only.

### Suggested fix

Forward `observedTargets` in all three recursive `ToStash` calls. Minimal patch:

```csharp
// Line 100
sd.Set(kv.Key, ToStash(kv.Value, registrations, observedTargets));

// Line 110
sd.Set(prop.Name, ToStash(prop.GetValue(arg), registrations, observedTargets));

// Line 120
list.Add(ToStash(item, registrations, observedTargets));
```

Add one regression test in `HostObjectLifetimeTests.cs`:

```csharp
[Fact]
public async Task OnRelease_FiresForHostInstance_ReturnedNestedInDict()
{
    var releaseCount = 0;
    var host = new StashHost();
    host.RegisterType<Resource>(b => b
        .Property("id", x => x.Id)
        .Method("nestedSelf", (Resource r) => new Dictionary<string, object?> { ["inner"] = r })
        .OnRelease(_ => releaseCount++));
    var r = new Resource("nested");
    host.SetGlobal("r", r);

    var script = await host.CompileAsync("fn run() { return r.nestedSelf(); }");
    await host.RunAsync(script);
    await host.CallAsync<Dictionary<string, object?>>("run");

    await host.DisposeAsync();
    Assert.Equal(1, releaseCount);  // pre-fix: 0
}
```

### Verify

```
dotnet test --filter "FullyQualifiedName~Stash.Tests.Embedding.HostObjectLifetimeTests"
dotnet test --filter "FullyQualifiedName~Stash.Tests.Embedding"
```

---

## F02 — [MINOR] benchmark-results.md interpretation is internally inconsistent; ShortRun (3 iterations) is too thin to support the property-read number

**Status:** fixed
**Fixed in:** acf41c1a
**Files:** `.kanban/2-in-progress/host-object-dispatch/benchmark-results.md:33`, `.kanban/2-in-progress/host-object-dispatch/benchmark-results.md:48-55`, `Stash.Hosting.Benchmarks/HostMemberAccessBenchmarks.cs:30-31`
**Phase:** P5
**Commit:** b64b41a4

### Observation

The `WarmPropertyRead` median (15.488 μs) is **roughly 4×** the `WarmMethodCall`
median (3.879 μs) and **6×** the plain-Stash baseline (2.503 μs). This is
counter-intuitive: property-read goes through `CallAsync` → `HostHandle.VMTryGetField`
→ registered getter → `InvokeHostDelegate.InvokeGetter`. Method-call goes through
the *same* `CallAsync` + `VMTryGetField` (which additionally allocates a
`HostBoundMethod`, validates arity, copies args, marshals one parameter, and calls
through `CallDirect` → `InvokeHostDelegate.InvokeMethod`). The property path does
**strictly less work**, so it should be cheaper or — at worst — comparable.

The interpretation at `benchmark-results.md:48-55` is **internally
self-contradictory**:

> "the ShortRun's 3 iterations for property-read show low variance (StdDev 1.046 μs),
> so the 15 μs is a stable measurement. The property-read overhead versus method-call
> (15 μs vs 3.9 μs) warrants a note: the difference likely reflects task-scheduling
> jitter ..."

"Low variance / stable measurement" and "task-scheduling jitter" are mutually
exclusive explanations. If the measurement is stable, jitter is not the explanation;
if jitter is the explanation, the measurement isn't stable.

`HostMemberAccessBenchmarks.cs:31` uses `[ShortRunJob]` (1 launch, 3 warmup, 3
measured iterations). Three iterations is statistically insufficient to support a
single-digit-microsecond ranking claim, and BDN's own `WarmMethodCall` mean (6.444 μs,
StdDev 5.026 μs) shows the noise floor is comparable to the *baseline itself*.

### Why this matters

The benchmark is **not a correctness gate** (P5 done_when explicitly excludes it from
`dotnet test`, consistent with `.claude/performance.md`), and the verdict ("no
specialised opcode warranted") is plausible on first principles — call infrastructure
(`SemaphoreSlim` + `Task.Run`) dominates either way, so adding a HostBoundMethod alloc
or a getter delegate call is unlikely to materially shift the floor. So the verdict
survives the bad data.

But the *supporting numbers* in the document are unsound and the *narrative* invokes
incompatible explanations side-by-side — readers (humans and future agents) cannot use
the table as a baseline they trust. Either the benchmark is measuring something that
isn't what its docstring claims (e.g. the property-read script `return player.hp;` is
being re-compiled per iteration, or the GC-allocation column hides a one-off
allocation amortised across 3 samples), or the property number is real and the
dispatch path *does* have a hot spot worth at least a sentence diagnosing — neither of
which the doc currently allows the reader to decide between.

### Suggested fix

Two acceptable resolutions:

**Option A (cheapest — fix the narrative, defer re-measurement):** Strike the
"low variance / stable" sentence; flag the 15μs as a known measurement artifact
that the ShortRun's 3-iteration budget cannot resolve. Keep the verdict
("comparable to baseline, no specialised opcode warranted") and explicitly say the
ranking between property-read and method-call is *not* a finding of this benchmark.

**Option B (better — re-run with more iterations):** Bump to a `[SimpleJob]` with
`warmupCount: 10, iterationCount: 30` (or use `[MediumRunJob]`), re-record. The
benchmark file is not in `dotnet test`, so the cost is a single Release-mode `dotnet
run -c Release --project Stash.Hosting.Benchmarks` from the repo root. Replace the
table and interpretation with whatever the larger sample says.

The verdict may not need to change either way, but the **support** for the verdict
needs to be coherent.

### Verify

```
# Option A: just edit the markdown.
# Option B (preferred):
dotnet run -c Release --project Stash.Hosting.Benchmarks
# Then update the table and interpretation in benchmark-results.md.
```

---

## F03 — [MINOR] "Construct-lite chokepoint" guarantee is weaker than the brief claims — the baked-closure DynamicInvoke is structurally reachable

**Status:** fixed
**Fixed in:** 982383dc
**Files:** `Stash.Hosting/HostTypeBuilder.cs:209`, `Stash.Hosting/HostTypeBuilder.cs:338`, `Stash.Hosting/Internal/InvokeHostDelegate.cs:20-26`, `Stash.Hosting/Internal/HostMemberDescriptor.cs:63-69`
**Phase:** P3 (sync invoker) / P4 (async invoker)
**Commit:** b6db63ab, 4a35888b

### Observation

The brief and `InvokeHostDelegate.cs:20-26` claim a Make-It-Right Construct-lite
chokepoint: *"No other code in `Stash.Hosting` may invoke a registered host delegate
directly. ... the wrapping try/catch cannot be skipped because there is no other API
to call a delegate through."*

What actually exists:

- The user-supplied `Delegate handler` is **not** what the chokepoint guards. The
  literal `handler.DynamicInvoke(clrArgs)` calls live in **baked closures inside
  `HostTypeBuilder.Method` (line 209) and `HostTypeBuilder.AsyncMethod` (line 338)**.
- Those closures are stored on the descriptor as `HostMemberDescriptor.Invoke`
  / `HostMemberDescriptor.AsyncInvoke` (`HostMemberDescriptor.cs:63-69`) — internal
  but not sealed.
- The chokepoint property holds **only** because `desc.Invoke` is, today, dereferenced
  only inside `InvokeHostDelegate.InvokeMethod` (line 137: `rawResult = invoker(target, stashArgs);`)
  and `desc.AsyncInvoke` only inside `InvokeHostDelegate.InvokeAsyncMethod` (line 236).
  Nothing structurally prevents a future patch from reaching into a descriptor and
  invoking the closure directly, bypassing the try/catch → `HostError` mapping. A
  future maintainer who reads the brief's claim ("no other API to call a delegate
  through") will not necessarily verify the convention at the descriptor-access site.

Contrast the MVP's `HostMarshaller`: there really IS no other API — the conversion
is type-erased through `ToStash`/`FromStash` and any caller has to go through it.
Here, the descriptor's `Invoke` and `AsyncInvoke` fields hand out a live callable.

### Why this matters

Brief parity: the brief invokes this as one of two "Make-It-Right" cross-cutting
guarantees. The actual guarantee is weaker (convention-only, not structural). A
single block-comment in `HostMemberDescriptor.cs` + a one-line invariant on the
descriptor fields is enough to make the convention legible without changing any
runtime behavior.

This is not a correctness defect today — every reachable path goes through
`InvokeHostDelegate`. It is a maintainability / brief-parity finding: the documented
property is not quite what the structure delivers.

### Suggested fix

Either (a) document the convention loudly at the descriptor, or (b) harden it
structurally. Concrete options:

**(a) Doc-only (cheapest):** Add a `<remarks>` block on `HostMemberDescriptor.Invoke`
and `AsyncInvoke` stating: *"This field must be dereferenced ONLY by
`InvokeHostDelegate.InvokeMethod` / `InvokeAsyncMethod`. Calling these closures
directly bypasses the CLR-exception → `HostError` mapping that the brief documents
as a Construct-lite chokepoint."* Also soften the brief's "no other API to call a
delegate through" claim to "no API in `Stash.Hosting` reachable today other than
through `InvokeHostDelegate`."

**(b) Structural (medium effort, optional):** Move the `DynamicInvoke` call out of
the baked closure and into `InvokeHostDelegate` itself: keep only the per-arg
marshalling (which legitimately needs the captured `argTypes`) in the closure, and
have it return a tuple `(object?[] clrArgs, Delegate handler)` to the chokepoint
which then performs the actual `handler.DynamicInvoke(clrArgs)` inside its try/catch.
This makes the chokepoint the *only* call site of `DynamicInvoke` on a registered
handler, matching the brief literally.

(b) is nicer; (a) is acceptable given the feature is a 5-phase SDK addition.

### Verify

```
dotnet build Stash.Hosting
dotnet test --filter "FullyQualifiedName~Stash.Tests.Embedding"
```

A docs-only change has no test impact; the existing Embedding suite stays green.
Option (b) requires re-asserting the existing `MethodArgTypeMismatch_ThrowsHostError_PerArgMessage`
(`HostObjectMethodTests.cs:163`) and the async equivalent stay green.

---
