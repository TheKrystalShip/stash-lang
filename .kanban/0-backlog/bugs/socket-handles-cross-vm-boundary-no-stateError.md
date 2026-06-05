# Socket handles silently work (or fail) across task boundaries without throwing StateError

**Status:** Backlog — Bug
**Created:** 2026-06-06
**Discovery context:** `async-correctness` feature, P5 implementation (D5 cross-VM handle boundary). The audit in `.kanban/0-backlog/language/Async Correctness — Contract Audit and Test Suite Plan.md` verified the cross-VM boundary issue for process handles (D5). Sockets were scoped to this phase but require new subsystem design, so process was shipped and this was deferred.

---

## Problem

`tcp.listen`, `tcp.connect`, and `udp.bind` return socket handles (TcpServer, TcpClient, UdpSocket) that are bound to the OS socket in the creating VM context. If a socket handle is captured by a `task.run(() => ...)` lambda and consumed inside the child task (e.g. via `tcp.recvBytesAsync`, `tcp.send`, `tcp.close`), the child runs in an isolated forked `VMContext` with no socket tracking. Today there is no cross-VM boundary check — the child either silently succeeds (mutating shared OS state, violating task isolation) or silently fails, depending on whether the OS socket is still valid.

The contract established by D5 is: **resource handles do not cross task boundaries**. Sockets are the same class of resource as process handles and should throw `StateError` with an explicit message when a consumer function is called from a different task context than the one that created the handle.

## Reproduction

```stash
let server = tcp.listen(0);
let f = task.run(() => {
    let conn = tcp.accept(server);   // or tcp.send, tcp.close, etc.
    // Should throw StateError — currently may silently proceed or fail
});
let results = task.awaitAll([f]);
// results[0].type should be "StateError" — today it is something else or succeeds
```

Requires a running test environment with `tcp`/`udp` capability enabled.

## Blast radius

- Users of `tcp.*`, `udp.*`, `ws.*` who capture a socket handle across a `task.run` / `arr.parMap` boundary.
- Latent today: concurrent socket programs are rare and the silent behavior is confusing but not crashing.
- Becomes load-bearing if more async socket patterns are documented or the embedding API exposes socket handles to host code that also uses `task.run`.

## Root cause

`VMContext` has `TrackedProcesses` and `TrackedWatchers` but no `TrackedSockets` collection. The `Fork()` method (called by `task.run` child creation) does not propagate any socket state — correctly so, since sockets must not be shared. But without per-context tracking, there is no lookup-site check to throw `StateError`. `NetSocketImpl.cs`, `TcpBuiltIns.cs`, and `UdpBuiltIns.cs` all access socket objects directly (stored on `StashInstance` fields or via internal registry) without consulting a per-VM ownership collection.

## Suggested fix

- **(A) Add `TrackedSockets` to `VMContext` and `IProcessContext` (or a new `ISocketContext`):** Register each socket handle at creation (`tcp.listen`/`tcp.connect`/`udp.bind`). Add a `ResolveTrackedSocket` helper (mirroring `ResolveTrackedProcess` from P5) at every consumer site. Cleanly parallels the process fix.
- **(B) Store per-context identity on each socket handle instance:** At creation, tag the handle with the creating `VMContext` identity (e.g. a `Guid` field). At consumer sites, compare the tag to `ctx`'s identity. Simpler than a collection; avoids adding a new list to `VMContext`.

Recommend **(A)**: consistent with the process-handle pattern, enables per-VM cleanup at exit, and is the single-responsibility path the architecture already favors.

## Verification

```bash
# After the fix: the following test must pass:
dotnet test --filter "FullyQualifiedName~CrossVmHandleTests"
# A new CrossVmHandleTests test for tcp/udp should throw StateError.

# Regression: existing tcp/udp tests must stay green:
dotnet test --filter "FullyQualifiedName~TcpBuiltInsTests|FullyQualifiedName~UdpBuiltInsTests"
dotnet test   # full suite
```

## Related

- `async-correctness` feature, P5 (D5 process fix): `.kanban/2-in-progress/async-correctness/brief.md`
- Contract audit: `.kanban/0-backlog/language/Async Correctness — Contract Audit and Test Suite Plan.md`
- Process cross-VM fix (the parallel case): `Stash.Stdlib/BuiltIns/ProcessBuiltIns.cs` — `ResolveTrackedProcess` helper introduced in P5.
- `VMContext.Fork()`: `Stash.Bytecode/Runtime/VMContext.cs` — the fork site where sockets should NOT be propagated (already correct; tracking is the missing piece).
