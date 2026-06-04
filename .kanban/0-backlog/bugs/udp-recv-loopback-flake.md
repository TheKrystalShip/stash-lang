# `udp.recv` loopback receive returns null instead of the sent payload

**Status:** Backlog — Bug
**Created:** 2026-06-04
**Discovery context:** Surfaced as a baseline-test failure during `/feature-review` for the
`callback-marshaling` feature (embedding phase-3 event-loop slice). Two tests in
`Stash.Tests/Interpreting/NetBuiltInsTests.cs` were red on the baseline `dotnet test` run that
gated the review. Reproduction in isolation (single-test runs) reproduces the failure, ruling out a
xUnit parallelism / port-collision flake. `git log 0aa00c5b..HEAD` shows **zero commits** touching
either `NetBuiltInsTests.cs`, `NetBuiltIns.cs`, or `NetSocketImpl.cs`, so this is not caused by the
callback-marshaling feature — it is a pre-existing defect (or environment-dependent flake) that the
green-up effort missed.

---

## Problem

Two UDP-loopback tests fail in isolation against the current `main` (and the
`feature/callback-marshaling` branch tip):

1. `NetBuiltInsTests.UdpRecv_ReturnsUdpMessageStruct` — `udp.recv(port)` returns `null` instead of
   a `UdpMessage` struct (`Cannot access field 'data' on null`).
2. `NetBuiltInsTests.UdpSendRecv_Loopback_ReturnsData` — `udp.recv(port)` after a `udp.send(...)`
   to the same loopback port returns a `UdpMessage` whose `.data` is the literal string `"null"`
   instead of `"hello udp"` (assertion: `Expected: "hello udp"`, `Actual: "null"`).

## Reproduction

```bash
dotnet test --filter \
  "FullyQualifiedName~NetBuiltInsTests.UdpRecv_ReturnsUdpMessageStruct|FullyQualifiedName~NetBuiltInsTests.UdpSendRecv_Loopback_ReturnsData"
```

Expected: both pass. Actual (Linux 7.0.10-arch1-1, .NET 10.0, current `main`):

```
Failed Stash.Tests.Interpreting.NetBuiltInsTests.UdpRecv_ReturnsUdpMessageStruct
  Error Message: Stash.Runtime.RuntimeError : Cannot access field 'data' on null.
Failed Stash.Tests.Interpreting.NetBuiltInsTests.UdpSendRecv_Loopback_ReturnsData
  Error Message: Assert.Equal() Failure: Strings differ
    Expected: "hello udp"
    Actual:   "null"
```

## Blast radius

- Direct: `udp.recv` is the receive primitive for the entire `udp` namespace. Any Stash script that
  uses UDP receive is affected.
- Tests: every CI run on this machine class (Linux loopback) shows the regression. The full-suite
  baseline is RED until this is fixed or quarantined.
- The bug is environment-sensitive enough that it didn't surface during the `test-suite-green-up`
  milestone, so a clean machine may pass. Worth confirming under macOS / Windows / a clean container
  before deciding on a fix strategy.

## Root cause

Unknown. Hypotheses to rule in/out:

- `udp.recv`'s underlying `udp.ReceiveAsync(cts.Token).AsTask().GetAwaiter().GetResult()` may be
  timing out (5000 ms default) before the send arrives. The Loopback test sends and then receives,
  but the order / timing inside the script may race. If the receive blocks, then the send happens
  *after* the timeout, the receive returns null, and the test reads the null struct.
- `Encoding.UTF8.GetString(result.Buffer)` is the actual data extraction — if `result` is the
  default `UdpReceiveResult` (uninitialized when the receive cancels via timeout), `result.Buffer`
  could be `null` and `GetString(null)` would `NullReferenceException` — but the test shows the
  string `"null"` being returned, so the path through the catch and the StashValue marshaling needs
  to be traced.
- Port-reuse / `SO_REUSEADDR` semantics on Linux loopback when the test fixture binds and rebinds
  the same port across tests.

## Suggested fix

(A) Trace through `NetSocketImpl.UdpRecv` (`Stash.Stdlib/BuiltIns/NetSocketImpl.cs:608-639`) with a
   minimal repro — most likely the receive is timing out (`OperationCanceledException` path
   returning a stale / null result) on this Linux build.

(B) Raise the test timeout if (A) confirms it's tight, or rework the test to start the receive
   before the send (e.g. spawn the receive on a separate task and then send).

(C) Quarantine with `[Fact(Skip = "<backlog link>")]` only as a last resort, and only after (A)
   confirms the root cause is environmental rather than a real Stash bug.

Recommend (A) → (B): fix the underlying timing rather than mask the failure.

## Verification

```bash
dotnet test --filter \
  "FullyQualifiedName~NetBuiltInsTests.UdpRecv_ReturnsUdpMessageStruct|FullyQualifiedName~NetBuiltInsTests.UdpSendRecv_Loopback_ReturnsData"
# Before fix: both fail (see error output above).
# After fix: both pass.
```

Also confirm the full `dotnet test` baseline returns 0 failures.

## Related

- Surfaced during `/feature-review` for `.kanban/2-in-progress/callback-marshaling/`, but **not caused**
  by it — diff range `0aa00c5b..c6bb62dd` touches no networking files.
- `Stash.Stdlib/BuiltIns/NetSocketImpl.cs:608-639` — the `UdpRecv` implementation.
- `Stash.Tests/Interpreting/NetBuiltInsTests.cs:552, 562` — the two failing tests.
- `[no-useless-skips]` memory: this is precisely the kind of pre-existing flake the green-up milestone
  is meant to eliminate; do not silently `[Skip]` without filing here first.
