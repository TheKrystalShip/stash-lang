# RFC: Host-Objects-by-Reference — Member Dispatch from Stash

> **Status:** Draft
> **Owner:** Cristian Moraru
> **Created:** 2026-06-05
> **Slug:** host-object-dispatch
> **Milestone:** embedding (phase 3c — the last unshipped DoD §3 item)

## Resolved Design Decisions (user rulings 2026-06-05)

**1. Dispatch mechanism — RESOLVED: Option A (delegate registration).** Option B is rejected on
NAOT grounds. Option C is noted as a possible **future v2 ergonomics layer over A** — *no
follow-up spec is filed now*. The original options table is retained below as historical
rationale; the brief and `plan.yaml` proceed on A.

**2. `HostError` as a `[StashError]`-registered built-in error type — RESOLVED: YES.** Catchable
in Stash via `try/catch e is HostError`, and visible in the generated Standard Library
Reference. Registration lives in `Stash.Core/Runtime/Errors/HostError.cs` (the core error
registry, beside `ReadOnlyError`/`IOError`/etc. — **NOT** in `Stash.Hosting`, which already
carries only the discriminator-side `KindHostError` const). Adding it reactivates a scoped slice
of `.claude/language-changes.md` — see "Reactivated checklist slice" below.

### Original dispatch options (rationale, kept for the record)

| Option | Ergonomics | NAOT (Native AOT) | Trim safety | Outcome |
| --- | --- | --- | --- | --- |
| **A — Delegate registration** (chosen). The host declares each member explicitly via a typed builder: `host.RegisterType<Player>(b => b.Method("attack", (Player p, long dmg) => p.Attack(dmg)).Property("hp", p => p.Hp, (p, v) => p.Hp = (long)v));`. The runtime stores typed delegates per `(Type, memberName)` and dispatches by lookup — no reflection at the call boundary. | Verbose at registration; once registered, **fully ergonomic at the script site** (`p.attack(10); p.hp = 100`). | **Clean.** The CLI ships NAOT; the existing VM dispatch path is reflection-free (Stash.Core protocols + `IStashCallable.CallDirect`); delegate dispatch preserves that. Forgotten members fail loudly (`"no member 'foo' registered for type Player"`), not silently. | Trim-safe — no rooted members beyond what the host explicitly references. | **CHOSEN.** |
| B — Reflection-based dispatch | Zero registration boilerplate (`host.RegisterType<Player>()` and member-name lookup walks `Type.GetMethod` / `Type.GetProperty` lazily). | **NAOT-hostile.** Same reason `AnalysisEngine` / DAP / LSP are not embeddable under NAOT today (design-analysis §1 line 13). Reflection invoke / dynamic method binding is not trim-safe by default; warnings and runtime member-not-found surprises after publish. | Trimmer can drop the very members the script will call. Requires `[DynamicallyAccessedMembers]` on every host-type parameter — leaks NAOT concerns through the SDK signature. | Rejected. Breaks the flagship distribution shape. |
| C — Source generator | Zero per-member boilerplate at registration (`[StashType] public class Player { ... }` → generator emits the delegate table). | Clean. Build-time generation, runtime is delegate dispatch (same shape as A). | Clean. | Future v2 ergonomics layer over A; no follow-up spec filed now. |

### Reactivated checklist slice (HostError registration only)

Because we are adding a new `[StashError]`-attributed built-in error type, a tightly scoped
slice of `.claude/language-changes.md` reactivates in **P2**:

- Author `Stash.Core/Runtime/Errors/HostError.cs` with `[StashError(Description = "...")]`
  describing when it is thrown (CLR exception escaping a host-registered member's delegate).
- `Properties` / `PropertyTypes` on the attribute: keep minimal in v1 — the base `RuntimeError`
  already carries `Message` and `CallStack`, which the `StashError` projection surfaces; no
  extra Stash-visible properties needed for v1.
- Run `dotnet run --project Stash.Docs/` to regenerate
  `docs/Stash — Standard Library Reference.md`. The reference is **generated** — never
  hand-edited.
- `StandardLibraryReferenceTests` must pass on the regenerated output (the test asserts the
  checked-in reference matches the regenerated content; agents that forget the regen step are
  caught by this test).
- This is the **only** `.claude/language-changes.md` slice that reactivates: no new syntax, no
  new stdlib *function*, so the Wave1 / Wave2 throws-coverage meta-tests do **not** apply.
  However, any `<exception cref="HostError">` written from now on references a *registered*
  error type — satisfying `WaveN_TaggedThrows_ReferenceKnownErrorTypes`. (Today's `HostError`
  has no `[StashFn]` tagged-throws consumer, but the registration unblocks tagging in any
  future host-touching stdlib seam.)
- No tooling-compatibility matrix walk (LSP/DAP/Playground/VS Code/Analysis) — adding a
  registered runtime error type does not expand any of those surfaces beyond what
  `BuiltInErrorRegistry` already drives.

### Bounded-domain compliance — the `"HostError"` Kind string

The Kind discriminator string is a bounded value (closed set of `StashError.Kind*` constants on
the MVP). Per the project's bounded-domain rule it must be a named const, never inlined. The
MVP already established the pattern: `StashError.KindCancelled`, `KindStepLimitExceeded`,
`KindParseError` are defined in `Stash.Hosting/StashError.cs`. **P2 adds
`StashError.KindHostError = "HostError"` and uses it everywhere the new kind is referenced.**
The registered `[StashError]` *type name* (`HostError` — derived by `BuiltInErrorRegistry.NameOf`
from the CLR class identity) and the `KindHostError` *const value* must agree on the single
spelling `"HostError"`; a P2 unit test pins the equality
`StashError.KindHostError == BuiltInErrorRegistry.NameOf(new HostError(""))` to fail loudly if
either side drifts.

---

## Summary

Adds **host-objects-by-reference** to `Stash.Hosting`: a C# host passes a live CLR object into a
hermetic `StashEngine` as an opaque by-reference handle, and Stash code dispatches the object's
**members** (`obj.foo()`, `obj.bar`, `obj.bar = x`) across the boundary. The object stays
CLR-side — it is **not** serialized, copied, or marshalled by value. The wire shape is a typed
delegate table per `Type`, registered at `Stash.Hosting`-construction time before any script
runs; the in-VM representation is a small `HostHandle` wrapper that implements existing
`Stash.Core` protocols (`IVMFieldAccessible`, `IVMFieldMutable`, `IVMTyped`,
`IVMStringifiable`).

This is the **last unshipped DoD §3 item** on the `embedding` milestone — the MVP
(`stash-hosting-mvp`, shipped 2026-06-04) deferred it because it was the only genuine
high-unknown design area. The decision above retires that unknown.

## Motivation

The MVP `Stash.Hosting` ships value-only marshalling: anonymous objects → `StashDictionary`,
collections → `List<StashValue>`. That works for command-shaped APIs ("call `add(2, 3)`, get
`5`"), but the milestone's named use case (a .NET web API embedding the same Stash codebase the
CLI runs) needs Stash to act on **live host services** — domain models, repositories, request
contexts — without round-tripping each one through a dict. The user's pattern is `result =
order.markPaid()` where `order` is the live `Order` aggregate, not a frozen snapshot.

Two things make this the right next unit:

1. **The MVP unblocks the rest.** With the facade, marshaller chokepoint, structured errors,
   and `IAsyncDisposable` already shipped, host-objects slot in as a **single new value kind**
   (`HostHandle`) plus a registration surface. No facade redesign.
2. **The dispatch mechanism is the one real fork in the road.** Reflection looks easy and is
   wrong (breaks NAOT — the flagship CLI distribution); delegate registration is the principled
   shape. Locking it down now keeps every future host-object enhancement on a clean rail.

## Goals

- Add **host-objects-by-reference** to `Stash.Hosting`:
  - `IStashHost.RegisterType<T>(Action<HostTypeBuilder<T>> configure)` — registers a type with
    its member dispatch table.
  - `HostTypeBuilder<T>` — typed builder with `Method`, `Property` (read), `Property` (read/write),
    and `AsyncMethod` (returns `Task<TResult>` → `StashFuture`).
  - `IStashHost.SetGlobal(string name, object hostObject)` — binds a registered host object as a
    Stash global so scripts can reference it directly (e.g. `request.path`).
- Member dispatch on a host-handle inside the VM:
  - `obj.member` (read) → reads the registered property delegate; CLR exception → `RuntimeError`
    → structured `StashError`.
  - `obj.member = x` (write) → invokes the registered setter delegate; arg is marshalled via the
    existing `HostMarshaller`.
  - `obj.member(args...)` (call) → returns an `IStashCallable` that, when invoked, marshals args
    in, invokes the delegate, marshals the result out. Same chokepoint discipline as the MVP.
  - **Async member** returning `Task<TResult>` → result is a `StashFuture`; Stash `await` blocks
    the VM thread on it (existing infrastructure).
- Round-trip through `HostMarshaller`:
  - `ToStash`: a CLR instance of a registered host type → `StashValue` wrapping a `HostHandle`
    (the registered table holds the type; the handle holds the live instance).
  - `FromStash<T>`: a `StashValue` whose underlying object is a registered host instance →
    cast back to `T` when `T` matches the registered type, else `InvalidCastException` (same
    contract as the MVP's POCO rejection).
- `typeof(obj)` returns the registered VM type name (already supported via `IVMTyped` and
  `vm.RegisterTypeName<T>`); `obj is HostType` works (already supported via `RegisterTypeCheck`).
- **Lifetime/ownership:** the host handle holds a strong reference to the CLR instance; it is
  released when the wrapping `StashValue` becomes unreachable from the VM (normal GC). Disposal
  is the host's concern — host objects implementing `IDisposable` / `IAsyncDisposable` are
  **not** auto-disposed by the engine; a registered type can opt in via `b.OnRelease(...)` (a
  callback invoked when the engine itself is disposed) for cleanup of host-side resources.
- **Threading:** host-member dispatch runs on the **VM thread** (synchronous calls inside
  `GetField` / `Call` opcode handlers). The lua_State single-thread model is preserved — host
  delegates must be safe to invoke on whatever thread the `Stash.Hosting` host's
  `RunAsync`/`CallAsync` is currently using (which the MVP's `SemaphoreSlim(1, 1)` already
  serialises per host).
- **Freeze:** `DeepFreeze` does **not** traverse into a `HostHandle`'s CLR object. The handle
  itself is opaque to the freeze walker. `freeze(obj)` on a host handle is a no-op returning
  the handle unchanged. Documented in Decision Log.

## Non-Goals

- **POCO/record by-value marshalling.** §14.7 / Gap 5 — a *separate* boundary model
  (copy-in/copy-out). NOT DoD §3. NOT this feature.
- **Event-loop ↔ `InvokeAsync` drain.** Milestone ruling #5 — `InvokeAsync` continues to bridge
  only an already-resolving `StashFuture`. An async host method that schedules a
  callback-marshaled completion still requires the script to be at a drain point.
- **DI / `Stash.Hosting.AspNetCore`.** Out — separate package, separate spec.
- **VM pool.** Out — phase-3 milestone ruling.
- **Snapshot/restore.** Out — milestone-blessed v1 contract is stateful engine.
- **Reflection-based dispatch** (Open Decision Option B). If the user picks B, this entire
  scope changes; see the Decision section.
- **Source-generator dispatch** (Open Decision Option C). Future additive layer over A.
- **New Stash language syntax.** `obj.foo` / `obj.foo()` / `obj.foo = x` are existing surface.
- **New stdlib namespace.** Pure SDK addition.
- **Auto-disposal of registered host instances.** The host is responsible; opt-in `OnRelease`
  callback fires at engine dispose.

### Checklist scoping

Mostly the same posture as the MVP: a C# SDK addition with **no** new Stash-language or stdlib
*function* surface. The user-blessed `HostError` registration reactivates one tightly scoped
slice of `.claude/language-changes.md` (see "Reactivated checklist slice" above).

- **No language-spec edits.** `obj.foo` member syntax is already documented.
- **One docs regen in P2.** `dotnet run --project Stash.Docs/` re-runs
  `Stash.Docs/Program.cs` to regenerate `docs/Stash — Standard Library Reference.md` so the
  new `HostError` appears in the Built-in Errors section. `StandardLibraryReferenceTests`
  gates this regen.
- **No tooling-compatibility matrix walk** (LSP / DAP / Playground / VS Code / Analysis) —
  none of those see a new Stash surface. A registered host type is invisible to LSP
  completion unless we also teach the analyzer about it; explicitly out of v1.
- **No `examples/*.stash` file.** The flagship distribution is the CLI which has no host
  objects. A small in-source narrative test under `Stash.Tests/Embedding/` covers the
  example.
- **No Wave1/Wave2 throws-coverage meta-test changes.** `HostError` is a runtime error type,
  not a `[StashFn]` stdlib function exception, so it does NOT belong in either Wave's
  allow-list. But because it is `[StashError]`-registered, any future `<exception
  cref="HostError">` written from now on satisfies `WaveN_TaggedThrows_ReferenceKnownErrorTypes`.
- **No completion-snapshot regen.**

Tests live in `Stash.Tests/Embedding/` (the existing folder for the multi-engine isolation
suite and the MVP suite). The hermetic-VM acceptance suite **must stay green** — `final_verify`
runs full `dotnet test`.

## Design

### Surface

```csharp
namespace Stash.Hosting;

public interface IStashHost : IAsyncDisposable
{
    // ... existing MVP surface (CompileAsync / RunAsync / CallAsync / TryCallAsync / InvokeAsync) ...

    /// <summary>Register a CLR type for host-object-by-reference dispatch.</summary>
    /// <remarks>
    /// Must be called before the first <c>CompileAsync</c> / <c>RunAsync</c> / <c>CallAsync</c>.
    /// (Underlying constraint: <c>StashEngine.RegisterType</c> only accepts type registrations
    /// before VM creation. The host enforces this by throwing <see cref="InvalidOperationException"/>
    /// if the engine has already been materialised.)
    /// </remarks>
    void RegisterType<T>(Action<HostTypeBuilder<T>> configure) where T : class;

    /// <summary>Bind a registered host instance as a Stash global.</summary>
    void SetGlobal(string name, object hostObject);
}

public sealed class HostTypeBuilder<T> where T : class
{
    /// <summary>VM type name reported by <c>typeof(obj)</c>. Defaults to <c>typeof(T).Name</c>.</summary>
    public HostTypeBuilder<T> Named(string vmTypeName);

    /// <summary>Read-only property. Marshalled out via HostMarshaller.</summary>
    public HostTypeBuilder<T> Property<TValue>(string name, Func<T, TValue> get);

    /// <summary>Read-write property. Marshalled in/out via HostMarshaller.</summary>
    public HostTypeBuilder<T> Property<TValue>(string name, Func<T, TValue> get, Action<T, TValue> set);

    /// <summary>Sync method. Each parameter (and return) marshalled via HostMarshaller.</summary>
    public HostTypeBuilder<T> Method(string name, Delegate handler);

    /// <summary>Async method returning Task / Task&lt;TResult&gt;. Result becomes a StashFuture.</summary>
    public HostTypeBuilder<T> AsyncMethod(string name, Delegate handler);

    /// <summary>Optional cleanup hook fired when the engine is disposed.</summary>
    public HostTypeBuilder<T> OnRelease(Action<T> release);
}
```

Internal-only (illustrative):

```csharp
namespace Stash.Hosting.Internal;

// One per (host, registered type).
internal sealed class HostTypeRegistration
{
    public string VmTypeName { get; }
    public Type ClrType { get; }
    public IReadOnlyDictionary<string, HostMemberDescriptor> Members { get; }
    public Action<object>? OnRelease { get; }
}

internal sealed record HostMemberDescriptor(
    HostMemberKind Kind,                       // Property / Method / AsyncMethod
    Func<object, StashValue>? Getter,          // Property
    Action<object, StashValue>? Setter,        // Property (rw)
    HostInvocation? Invoke);                   // Method / AsyncMethod

// The handle that flows through StashValue, riding existing protocols.
internal sealed class HostHandle : IVMFieldAccessible, IVMFieldMutable, IVMTyped, IVMStringifiable
{
    private readonly object _target;
    private readonly HostTypeRegistration _registration;
    public bool VMTryGetField(string name, out StashValue value, SourceSpan? span) { ... }
    public void VMSetField(string name, StashValue value, SourceSpan? span) { ... }
    public string TypeName => _registration.VmTypeName;
    public string Stringify() => $"<{TypeName}>";
}
```

### Semantics

**One conversion chokepoint.** The MVP's `HostMarshaller` learns one new fact: an unrecognized
CLR object whose `GetType()` matches a registered host type wraps as `HostHandle(registration,
instance)`. **All inbound conversion of CLR instances continues to flow through
`HostMarshaller.ToStash` and no other path.**

**Member dispatch rides the VM's existing protocols.** No new opcode, no IC fast-path change,
no growth in the dispatch switch (the project's NAOT 10–20% perf-cliff hazard is untouched).
The `HostHandle` lives in the `StashValueTag.Obj` slot; reads enter `GetFieldValue`
(`VirtualMachine.TypeOps.cs` line 401), fall through to step 3 `IVMFieldAccessible.VMTryGetField`,
and the handle services them. Writes enter `SetFieldValue` (line 491) and route through step 1
`IVMFieldMutable.VMSetField`. Method calls return an `IStashCallable` (a private
`HostBoundMethod` wrapping the registered `Invoke` closure with the captured target), which the
existing `ExecuteCall` dispatch invokes via `CallDirect(IInterpreterContext,
ReadOnlySpan<StashValue>)`. **Confirmed empirically by orientation:** `ExecuteCall` already
handles any `IStashCallable` callee at `Stash.Bytecode/VM/VirtualMachine.Functions.cs` ~line
549.

**Async members.** `b.AsyncMethod("fetch", async (Player p, string url) => await p.FetchAsync(url))`
registers a delegate whose return is a `Task<TResult>`. The dispatch helper wraps the `Task` in
`new StashFuture(taskAsObjectTask, new CancellationTokenSource())` (the public constructor is
confirmed at `Stash.Core/Runtime/Types/StashFuture.cs` line 16), returns a `StashValue`
wrapping it, and Stash `await` blocks the VM thread. Cancellation flows through the engine's
`CancellationToken` (already wired by the MVP).

**Exception mapping (the cross-cutting single source of truth).** Every host delegate
invocation routes through one private helper:

```csharp
// Internal, the ONLY caller of host delegates anywhere in Stash.Hosting.
internal static StashValue InvokeHostDelegate(HostMemberDescriptor desc, object target,
                                              ReadOnlySpan<StashValue> args, SourceSpan? span);
```

The helper does the try/catch, maps `Exception` → a `HostError` (the `[StashError]`-registered
type added in P2; surfaced kind = `StashError.KindHostError`) with the inner exception preserved. Any host-delegate invocation that did NOT go through this helper
would skip the mapping — so the architecture makes it the **only** function in the SDK that
calls a registered delegate. Construct-lite, exactly mirroring the MVP's marshaller decision.

**typeof / is.** The handle's `IVMTyped.TypeName` returns the registered VM name. The existing
`VirtualMachine.RegisterTypeCheck` flow is reused — `RegisterType<T>(b => ...)` ALSO calls
`engine.RegisterType<T>(vmTypeName, obj => obj is HostHandle h && h.ClrType == typeof(T))`, so
`obj is Player` returns true exactly when the handle wraps a `Player`.

**Freeze.** `IsFrozen` on a `HostHandle` returns `false`; `DeepFreeze` traversing one is a
no-op. Justification: host state lives in opaque CLR memory; the freeze walker cannot enforce
immutability across that boundary. Documented contract: **handles are never frozen.** If a
script needs an immutable view, the host must register a wrapper type whose setters throw.

**Lifetime.** A handle holds a strong reference to its target. When the wrapping `StashValue`
becomes unreachable from VM globals + stack + open closures, normal CLR GC collects the handle
and (transitively) the target — unless the host holds the target elsewhere. Disposal of the
`StashHost` triggers the registered `OnRelease` callbacks in registration order for any handles
the engine has seen, **once** per unique target identity. Implementation: a
`ConditionalWeakTable` of seen targets, iterated on dispose. Decision logged below.

**SetGlobal.** `host.SetGlobal("request", aspNetRequestContext)` is sugar for "wrap the
registered CLR object in a HostHandle and write to `engine.VM.Globals`." The MVP's
`SemaphoreSlim(1, 1)` guarantees no script is in flight when `SetGlobal` runs, so the v1
contract is: `SetGlobal` may be called any time, but the binding takes effect on the next
`RunAsync` / `CallAsync` (the VM observes globals fresh at frame entry — which it already does).

### Implementation Path

Layer:
- `Stash.Hosting/` (the only modified assembly for the surface) — new `HostTypeBuilder<T>`,
  `HostHandle`, internal `HostTypeRegistration` + `HostMemberDescriptor` + `HostInvocation`,
  new `HostMarshaller.ToStash` branch for registered types.
- `Stash.Bytecode/` (zero changes, by design — see Q1 below).
- `Stash.Core/` (zero changes — `IVMFieldAccessible`, `IVMFieldMutable`, `IVMTyped`,
  `IStashCallable`, `StashFuture` are all consumed as-is).

End-to-end path of a call (illustrative):

```
host.CallAsync<long>("processOrder", new object?[] { 42L })
where the script body contains  return orders.find(id).markPaid();
  → MVP path until script reaches `orders.find(id)`:
      - `orders` is a host-registered global, value is a HostHandle(OrderRepository)
      - GetField "find" → HostHandle.VMTryGetField → HostBoundMethod (IStashCallable)
      - ExecuteCall → HostBoundMethod.CallDirect → InvokeHostDelegate
          → marshal args ToStash → invoke delegate → marshal result ToStash
      - Result is a HostHandle(Order)
  → `.markPaid()`:
      - Same path: GetField → HostBoundMethod → CallDirect → InvokeHostDelegate
      - markPaid throws InvalidOperationException("order is refunded")
          → InvokeHostDelegate catches → throws new HostError(InnerException.Message) (surfaced kind = StashError.KindHostError)
          → VM unwinds, host extracts to StashError (MVP P2 path), CallAsync<long> throws
            StashScriptException
```

Phase order:

- **P1.** Internal data structures + builder + host-handle wrapper. `HostTypeBuilder<T>`,
  `HostTypeRegistration`, `HostMemberDescriptor`, `HostHandle` (implementing the three
  Core protocols). `IStashHost.RegisterType<T>(configure)` + the host's per-type
  registration map. `HostMarshaller.ToStash` learns the new "is this a registered host type"
  branch. No member dispatch yet — `VMTryGetField` returns false (unknown-member fall-through
  produces the existing `RuntimeError`). End-to-end: a host registers a type with zero
  members, passes an instance into a script via `SetGlobal`, and `typeof(obj)` returns the
  registered name; `obj is HostType` returns true.

- **P2.** Property dispatch (read + write) + `HostMarshaller.FromStash<T>` mirror. `Property` /
  `Property(get, set)` builder methods populate `HostMemberDescriptor`. `VMTryGetField` and
  `VMSetField` consult the descriptor table. Exception-mapping helper `InvokeHostDelegate`
  introduced here — the SoT chokepoint is established **before** the call path needs it. New
  branch in `HostMarshaller.FromStash<T>`: a `HostHandle` whose `ClrType == typeof(T)` casts
  back to the registered type. End-to-end: `obj.hp` reads the property; `obj.hp = 100` writes
  it; a property getter that throws raises a `HostError` (kind = `StashError.KindHostError`)
  and surfaces as a structured `StashError`.

- **P3.** Sync method dispatch. `Method` builder method; `HostBoundMethod` (private
  `IStashCallable`) returned by `VMTryGetField` for `HostMemberKind.Method`. `CallDirect`
  marshals args ToStash → invokes the registered delegate via `InvokeHostDelegate` → marshals
  result ToStash. Argument arity / type marshalling errors map to a `HostError` with
  `Message="arg N to Player.attack: ..."` (kind = `StashError.KindHostError`). End-to-end: `obj.attack(10)` calls the delegate and
  returns the result; arity mismatch and CLR exception mapping both surface as structured
  `StashError`.

- **P4.** Async method dispatch + `OnRelease` lifetime. `AsyncMethod` builder method; the
  dispatch helper recognises a `Task` / `Task<T>` return and wraps in `StashFuture` (the public
  ctor). Cancellation: forward the engine's `CancellationToken` to the delegate when it accepts
  one (delegate signature inspected at registration time — this reflection runs at host setup,
  not in the VM hot path; NAOT-clean). `OnRelease` hooks invoked from `DisposeAsync` once per
  seen target (`ConditionalWeakTable`). End-to-end: `await obj.fetch(url)` returns the awaited
  result; disposing the host runs all `OnRelease` callbacks; cancelling a long-running async
  member surfaces an `OperationCanceledException` from `CallAsync<T>`.

- **P5.** Hosting-level test pack consolidation + a lightweight micro-benchmark for the
  member-access path. Tests in `Stash.Tests/Embedding/HostObjectDispatchTests.cs` (and siblings
  for property / method / async / lifetime / typeof). The benchmark is one new BenchmarkDotNet
  method in the existing `Stash.Hosting.Benchmarks` project, measuring warm property-read and
  warm method-call against MVP `CallAsync` baseline. **Not** a perf gate — consistent with
  `.claude/performance.md`, the trigger is *VM dispatch changes*, and there are none (Q1
  below). Numbers go in `benchmark-results.md` as a new "Host member access" column with a
  one-line verdict.

### Cross-Cutting Concerns

| Concern | Single source of truth | Omission prevented by |
| --- | --- | --- |
| Every host-delegate invocation must map CLR exceptions to a `HostError` (the `[StashError]`-registered runtime error, surfaced kind = `StashError.KindHostError`) so they reach the embedder as structured `StashError` rather than raw CLR exceptions. Spans every member kind — Property get, Property set, Method, AsyncMethod. | `InvokeHostDelegate` (private static helper in `Stash.Hosting.Internal`). | **Construct-lite.** `InvokeHostDelegate` is the **only** function in `Stash.Hosting` that calls a registered delegate — all four dispatch paths (Getter, Setter, Method, AsyncMethod) route through it. A new dispatch kind added later has no other API to call; the wrapping try/catch cannot be skipped without inventing a parallel invoker. Same Make-It-Right shape as the MVP's `HostMarshaller` chokepoint: a 5-phase one-project SDK feature does not warrant a manufactured Detect scanner; the chokepoint is enforced by being the only callable API. No meta-test. |
| CLR↔Stash value conversion at the host-object boundary (args going in, results coming out, properties read/written). | `HostMarshaller` (existing — extended in P1 with the registered-type → `HostHandle` branch, in P2 with the `HostHandle` → `T` branch). | **Construct-lite — already established in the MVP.** No method in `Stash.Hosting` does inline value conversion; all paths go through `HostMarshaller.ToStash` / `FromStash<T>`. The Host-Object additions extend the existing chokepoint, they do not bypass it. |

The remaining cross-cutting surface — per-engine isolation, callback marshalling, structured
error extraction — is owned by foundations already shipped (`hermetic-vm`, `callback-marshaling`,
`stash-hosting-mvp`); this feature consumes them unchanged.

## Acceptance Criteria

End-to-end (the headline use case):

- A host registers a CLR class `Player { string Name; long Hp; long Attack(long dmg); Task<bool>
  FetchAsync(string); }` with explicit member delegates, binds an instance as a global `player`,
  runs a script `let dmg = player.attack(10); player.hp -= dmg; let ok = await
  player.fetch("/url"); return { name: player.name, hp: player.hp, ok: ok };` and gets the
  expected dictionary back. The same `player` reference observed CLR-side reflects the mutation
  (`hp` decreased, `Attack` was called once).

- A second host in the same process registering the same type and a different `Player` instance
  observes ZERO state bleed (re-asserts the hermetic-VM foundation through the host-object
  surface).

`typeof` / `is`:

- `typeof(player)` returns `"Player"` (the registered name). `player is Player` returns `true`;
  `player is OtherType` returns `false`.

Error behavior:

- A property getter that throws `InvalidOperationException("bad")` produces a `StashError {
  Kind = StashError.KindHostError, Message = "bad", Span = <call site>, CallStack = [<in-script frame>] }`
  routed to `CallAsync` as `StashScriptException` and to `TryCallAsync` in `Errors[0]`.
- Calling an unregistered member (e.g. `player.unknown`) raises the existing
  "cannot access field 'unknown' on <Player>" `RuntimeError` (handle's `VMTryGetField` returns
  false → existing fallback path runs unchanged).
- Calling a method with the wrong arity raises a `HostError` with
  `Message="Player.attack expects 1 argument, got 2"` (surfaced kind = `StashError.KindHostError`).
- Assigning to a read-only property (`Property(get)` only) raises `ReadOnlyError` (the same
  shape `StashNamespace` produces today for `ns.x = 1`).
- **`HostError` is catchable in Stash.** A script `try { obj.bad_prop } catch (e) { return
  e.type; }` returns `"HostError"`; `e is HostError` is true. The kind discriminator surfaced
  to the embedder equals `StashError.KindHostError` (a named const — never an inlined string).
- `dotnet run --project Stash.Docs/` regenerates `docs/Stash — Standard Library Reference.md`
  with `HostError` listed in the Built-in Errors section; `StandardLibraryReferenceTests`
  passes on the regenerated content.

Async:

- A `Task`-returning host method returns a `StashFuture` to Stash; Stash `await` blocks until
  the task completes; the awaited result is marshalled out. A faulted Task surfaces as
  a structured `StashError` with kind = `StashError.KindHostError`.
- Cancelling the host's call token (`CancellationToken` to `CallAsync`) mid-`await` cancels the
  delegate (when it accepts a `CancellationToken`) and surfaces `OperationCanceledException`
  from the throw variant.

Lifetime:

- A registered type with `OnRelease(p => p.Dispose())` calls the callback exactly once per
  unique target seen by the engine, on `DisposeAsync`. Calling the same instance twice does NOT
  fire `OnRelease` twice. Targets the engine never observed do NOT fire (the host owns them).

Cross-entrypoint / foundation:

- The hermetic-VM acceptance suite (`Stash.Tests/Embedding/MultiEngineIsolationTests` and
  siblings) and the MVP test suite stay green. `final_verify` runs the full `dotnet test`.

Benchmark (P5):

- `benchmark-results.md` gains a "Host member access" section with warm property-read and warm
  method-call medians (BenchmarkDotNet), one-line verdict on whether any specialised path
  (e.g. promoted opcode) is justified. Default expectation: comparable to current
  IC-megamorphic fallback property access, no opcode change warranted.

## Phases

See `plan.yaml`. Recap:

- **P1** — Builder + handle wrapper + registration map + `HostMarshaller.ToStash` extension.
  `typeof` / `is` work; no members yet.
- **P2** — Property dispatch (R + RW) + `HostMarshaller.FromStash<T>` extension + the
  `InvokeHostDelegate` chokepoint (used by Getter/Setter).
- **P3** — Sync method dispatch via `HostBoundMethod : IStashCallable`.
- **P4** — Async method dispatch (Task → StashFuture) + `OnRelease` lifetime hook.
- **P5** — Test pack consolidation + a single member-access benchmark column in
  `benchmark-results.md`.

## Scope-boundary Q&A (must remain in the brief)

**Q1: Does this touch the VM member-access path in `Stash.Bytecode`?**
**A: No.** Orientation confirmed (`Stash.Bytecode/VM/VirtualMachine.TypeOps.cs` line 401 +
`VirtualMachine.Collections.cs` lines 233 / 376 + `VirtualMachine.Functions.cs` line 434):
- `GetFieldValue` already dispatches `IVMFieldAccessible` (step 3 of the fall-through).
- `SetFieldValue` already dispatches `IVMFieldMutable`.
- `ExecuteCall` already accepts any `IStashCallable` callee via `CallDirect`.

A `HostHandle` implementing those three protocols rides the existing fall-through with **no new
opcode, no IC fast-path change, no growth in the dispatch switch** (which is at the NAOT 10–20%
perf-cliff threshold). The hot-path trigger in `.claude/performance.md` is therefore **not
met** — a lightweight measurement column is due diligence, not a regression gate.

**Q2: Does any new Stash syntax / semantics need to be introduced?**
**A: No.** `obj.foo`, `obj.foo()`, `obj.foo = x` are existing language surface (member access,
call, member assignment). `typeof(obj)` and `obj is T` are existing operators that already
consult `IVMTyped` + the engine's `_registeredTypeChecks`. Therefore most of
`.claude/language-changes.md` is **N/A**: no spec doc edits, no stdlib regeneration, no
LSP/DAP/Playground/VS Code/Analysis matrix walk, no `examples/*.stash` file. The implementer
must not over-scope doc/tooling work.

## Open Questions

- **`AsyncMethod` delegate signature.** Two viable shapes for the registered async delegate:
  (i) `Func<T, ..., Task<TResult>>` (most natural, but `CancellationToken` plumbing is implicit
  via reflection-of-parameters-at-registration); (ii) `Func<T, ..., CancellationToken, Task<TResult>>`
  (explicit, simpler internals, more verbose at registration). **Lean (i)** — at registration
  time we inspect the delegate signature ONCE (this is allowed reflection; it runs at host
  setup, not in the VM hot path) and remember whether to thread the engine's CT. Pure-function
  reflection at registration is NAOT-clean.

- **Should `SetGlobal` bind unregistered objects too?** I.e. if `T` was never registered,
  should `host.SetGlobal("b", instance)` throw or silently treat the instance as a value
  marshalled through `HostMarshaller.ToStash`? **Lean: throw with a clear "type T not
  registered" error** — silent value-marshalling of a complex object is exactly the trap the
  MVP's `ArgumentException` already prevents. Confirm in P1.

## Decision Log

| Date | Decision | Rationale |
| --- | --- | --- |
| 2026-06-05 | **Host-object member dispatch uses explicit AOT-safe delegate registration (Option A) — RESOLVED.** User ruling. Option B rejected; Option C noted as a possible future v2 ergonomics layer over A with **no follow-up spec filed now**. | The CLI ships Native AOT; the existing VM dispatch path is reflection-free (Stash.Core protocols + `IStashCallable.CallDirect`); reflection-based dispatch (Option B) replicates the NAOT-hostility that already excludes `AnalysisEngine` / DAP / LSP from being NAOT-embeddable (design analysis §1 line 13). Source-gen (C) is a strict-superset ergonomics layer over A and can ship later. |
| 2026-06-05 | **`HostError` registered as a `[StashError]` built-in error type in `Stash.Core/Runtime/Errors/HostError.cs` — RESOLVED.** User ruling. The `"HostError"` discriminator string is named once as `StashError.KindHostError` on the MVP's `StashError` record (beside `KindCancelled`/`KindStepLimitExceeded`/`KindParseError`). `dotnet run --project Stash.Docs/` regenerates the reference; `StandardLibraryReferenceTests` gates the regen. | An embedded application's whole job is to act on fallible live host services. Asymmetry — where built-in errors like `ValueError`/`IOError` are typed and catchable but host-originated exceptions surface as an untyped runtime error — is exactly the rough edge a first-class embedding API removes. Cost is one new error file plus one docs-regen step (already the project's standing pattern). |
| 2026-06-05 | **Zero VM / bytecode changes — host objects ride existing `IVMFieldAccessible` / `IVMFieldMutable` / `IStashCallable` protocols.** | Orientation confirmed all three protocols dispatch in `GetFieldValue` / `SetFieldValue` / `ExecuteCall` today. No new opcode, no IC fast-path change, no growth in the dispatch switch (at the NAOT 10–20% perf-cliff threshold). The hot-path trigger in `.claude/performance.md` is **not met** — a lightweight measurement column is due diligence, not a gate. |
| 2026-06-05 | **No new Stash language syntax; `language-changes.md` largely N/A.** Same posture as the MVP. | `obj.foo` / `obj.foo()` / `obj.foo = x` are existing language surface. No spec edits, no stdlib reference regen, no LSP/DAP/Playground matrix, no `examples/*.stash` (the example lives in `Stash.Tests/Embedding/`). The one possible exception is the `HostError` built-in-error registration in P2; the implementer confirms scope when that decision lands. |
| 2026-06-05 | **Exception mapping single source of truth: `InvokeHostDelegate` is the only caller of host delegates.** No meta-test. | Construct-lite — the same Make-It-Right shape as the MVP's `HostMarshaller`. The wrapper is the only function that can invoke a registered delegate, so the exception mapping cannot be skipped by construction. Adding a Detect scanner for a 5-phase single-project SDK feature is the manufactured-bandaid pattern this project rejects. |
| 2026-06-05 | **Freeze is opaque to host handles.** `DeepFreeze` does not traverse into a `HostHandle`; `IsFrozen` reports false; `freeze(handle)` is a no-op. | Host state lives in opaque CLR memory; the freeze walker cannot enforce immutability across that boundary. Documented contract: handles are never frozen. If a script needs an immutable view, the host registers a wrapper type whose setters throw. |
| 2026-06-05 | **Lifetime: handles hold strong refs; `OnRelease` opt-in fires once per unique target at engine dispose.** Hosts own auto-disposal of their objects; the engine does not. | A pure embedder may share a single repository across many engines; auto-disposing on engine dispose would be wrong. Opt-in `OnRelease` covers the "engine OWNS this object" pattern (e.g. a per-request context). `ConditionalWeakTable` tracks identity without preventing GC of unreachable handles. |
| 2026-06-05 | **Threading: host members run on the VM thread.** No event-loop callback-marshaling interaction. | `Stash.Hosting`'s `SemaphoreSlim(1, 1)` (MVP) already serialises `RunAsync` / `CallAsync` per host, so the VM thread is well-defined per call. Async host members return `Task`, the script `await`s blocking that VM thread (existing infra), and the Task completes on a thread-pool thread — but the Stash-visible resumption happens on the same VM thread that began the `await`. No new threading model. |
