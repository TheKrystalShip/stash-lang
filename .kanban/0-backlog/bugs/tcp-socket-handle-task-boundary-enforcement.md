# TCP/socket handle task-boundary enforcement missing (D5 gap)

**Status:** Backlog — Bug
**Created:** 2026-06-06
**Discovery context:** Surfaced during the `language-standard-async` spec-sealing review (F01).
P6 of that unit wrote a spec paragraph blessing cross-task socket use as "may produce undefined
behavior" — a unilateral narrowing of the user-locked D5 decision. The `language-standard-async`
resolver (F01 fix) reverted the narrowing, documented the gap honestly, and filed this stub.
A spike conducted during the `language-standard-async` review confirmed the corruption is real.

---

## Problem

D5 (user-locked Tier-1 decision from `async-correctness`) states that cross-VM handle use
throws `StateError` instead of producing silent misbehavior — "silent empty is the worst
outcome." The same rationale applies to socket handles (`TcpConnection`, `TcpServer`,
`TcpClient`): passing a socket handle created in one task into a child task and performing
concurrent same-direction I/O silently corrupts the received bytes with no exception or
diagnostic.

The `async-correctness` feature named sockets in D5's scope but only built the enforcement
for `Process` handles. The socket gap is therefore an oversight, not a deliberate design choice.

There are three specific manifestations:

1. **Same-direction concurrent `tcp.send`/`tcp.recv` corrupts data silently.** The underlying
   `NetworkStream.Write` is not thread-safe for same-direction concurrent access; two concurrent
   `tcp.send` calls on the same `TcpConnection` handle held across a `task.run` boundary
   interleave their writes, and the receiver sees garbled bytes with no exception raised in Stash.

2. **Async socket path throws the wrong error type.** When a deep-cloned socket handle is used
   across a `task.run` boundary (the async path), the handle misses the process-global
   `ConditionalWeakTable<StashInstance, *>` lookup, and the impl throws `IOError("invalid or
   closed TcpConnection")` rather than `StateError`. This is both the wrong error type and a
   confusing message that implies the connection itself is closed when the real issue is the
   task-boundary violation.

3. **No per-context socket tracking exists.** The structural primitive that enables Process
   enforcement (`ctx.TrackedProcesses` — a per-context list populated at spawn and consulted
   at every process builtin call) does not exist for sockets. `NetSocketImpl.cs` uses
   process-global `ConditionalWeakTable<StashInstance, *>` containers with no per-context
   association, so there is no way to detect a cross-task use.

## Reproduction

```bash
# Spike confirming silent data corruption (concurrent same-direction send):
dotnet run --project Stash.Cli/ -- -c '
let server = tcp.listenAsync("127.0.0.1", 9901);
let conn = tcp.accept(server);
let f1 = task.run(() => tcp.send(conn, "aaaa"));
let f2 = task.run(() => tcp.send(conn, "bbbb"));
await f1;
await f2;
'
# A receiver on the other side observes garbled bytes — no exception in Stash.

# Wrong error type on async path (deep-clone misses the CWT):
dotnet run --project Stash.Cli/ -- -c '
let conn = tcp.connect("127.0.0.1", 9901);
let f = task.run(() => tcp.send(conn, "hello"));
let r = task.awaitAll([f]);
io.println(r[0].type);  // IOError, not StateError
'
```

## Blast radius

- **Any Stash program that passes a TCP socket handle (`TcpConnection`, `TcpServer`) from one
  task to another** and performs I/O from both the parent and the child. This is a latent
  correctness hazard: no exception is raised, so the program appears to work under light load
  but silently corrupts data under concurrent access.
- **`examples/websockets.stash`** uses a pattern where a `TcpConnection` handle is shared
  across task boundaries (receiver-in-task + sender-on-main). Under approach A1 (see Suggested
  fix), this example would need to be updated when enforcement ships.
- **UDP** (`udp.*`) has no handle object — `udp.send`/`udp.recv` are stateless calls that take
  address+port strings, so there is no handle to enforce task-affinity on. This scope is exempt.
- **WebSocket and SSL** handle objects (if exposed by Stash builtins) would need the same
  per-context tracking.

## Root cause

`Stash.Stdlib/BuiltIns/NetSocketImpl.cs:29-38` stores all socket handles in **process-global**
`ConditionalWeakTable<StashInstance, *>` containers with no per-context (task-context) tracking.
Contrast `ProcessBuiltIns.cs:1054-1071`, where `ResolveTrackedProcess` does a
`ctx.TrackedProcesses.Find(e => ReferenceEquals(e.Handle, handle))` lookup — if the handle is
not in the current context's tracked list, it throws `StateError`. The equivalent
`ctx.TrackedSockets` per-context list does not exist.

The gap is an oversight from `async-correctness`: D5 was written to cover sockets, but only
the Process enforcement was implemented. The spec claim was aspirational; the conformance suite
(`CrossVmHandleTests.cs`) only tested process handles.

Async path: `task.run` deep-clones the task's captured environment via `RuntimeValues.cs`
`DeepClone` (`_ => value` for opaque reference types — handles are passed by reference, not
cloned). However, the deep-cloned handle reference does not appear in the child task context's
tracked-processes list (because `TrackedSockets` doesn't exist), so any builtin that tries to
resolve it via a CWT lookup gets a miss and throws `IOError("invalid or closed TcpConnection")`
rather than the correct `StateError`.

## Suggested fix

Two approaches, which differ on how much of the `examples/websockets.stash` pattern they break:

- **(A1) Full prohibition — cross-task socket use always throws `StateError`.** Add
  `ctx.TrackedSockets` (a per-context list analogous to `ctx.TrackedProcesses`) populated at
  `tcp.connect` / `tcp.accept` and consulted in every socket builtin that takes a handle
  argument (`tcp.send`, `tcp.recv`, `tcp.recvAsync`, `tcp.recvBytesAsync`, `tcp.close`, etc.).
  A cross-task access throws `StateError` with a message analogous to the Process message.
  **Trade-off:** simplest to implement; matches Process semantics exactly. BUT it breaks the
  `examples/websockets.stash` pattern of sharing a connection handle between a receiver task
  and the sender on the main task (a legitimate different-direction usage). The example would
  need updating.

- **(A2) Direction-aware enforcement — prohibit same-direction concurrent access, allow
  different-direction.** Track which task "owns" a socket for sending and which for receiving,
  and throw `StateError` only when two tasks compete on the same direction. **Trade-off:**
  preserves the `examples/websockets.stash` pattern; architecturally harder (requires per-handle
  direction ownership metadata and atomic update). Risk: harder to specify correctly and the
  direction concept may not generalize cleanly to WebSocket/SSL.

**Recommend (A1):** simplest, safest, matches D5's Process precedent. Update
`examples/websockets.stash` to spawn a process-owned connection (or restructure to avoid the
cross-task share) as part of the enforcement commit.

## Verification

After the fix, the following should hold:

```bash
# Cross-task socket use throws StateError (not IOError, not silence):
dotnet test --filter "FullyQualifiedName~TwoSystemsConformanceTests"
# New socket boundary conformance tests must be added to TwoSystemsConformanceTests.cs
# asserting: task.run(() => tcp.send(conn, "x")) → StateError.

# Full suite remains green:
dotnet test
```

Pre-fix, the async path test returns `IOError`; post-fix it must return `StateError`.
The same-direction corruption test has no Stash-level failure today — post-fix it must
throw `StateError`.

The conformance test class XML-doc note about socket enforcement pending should be removed
when this bug is fixed and the tests added.

## Related

- `async-correctness` feature (`D5` decision) — the original user-locked Tier-1 decision that
  scoped sockets in but only built Process enforcement.
- `.kanban/2-in-progress/language-standard-async/review.md` — F01 finding that surfaced this gap.
- `Stash.Stdlib/BuiltIns/NetSocketImpl.cs:29-38` — the process-global CWT containers.
- `Stash.Stdlib/BuiltIns/ProcessBuiltIns.cs:1054-1071` — `ResolveTrackedProcess`, the model for
  socket enforcement.
- `examples/websockets.stash` — uses cross-task socket sharing (would need updating under A1).
- `Stash.Tests/Conformance/Async/TwoSystemsConformanceTests.cs` — when this bug is fixed, add
  socket boundary conformance tests here and remove the "enforcement pending" XML-doc note.
