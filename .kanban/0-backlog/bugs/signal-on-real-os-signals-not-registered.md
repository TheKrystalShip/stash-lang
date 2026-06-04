# signal.on(Signal.X, …) never installs a real OS PosixSignalRegistration (member-name mismatch)

**Status:** Backlog — Bug
**Created:** 2026-06-04
**Discovery context:** Surfaced during the `callback-marshaling` feature (embedding phase-3 event-loop
slice), while the `stash-author` agent was writing `examples/event_loop_shutdown.stash` (P3). Writing
a `signal.on`-driven graceful-shutdown example, it found the handler never fires on a real `kill -TERM`
and proved it (process exited 143, handler never printed). **Not caused by callback-marshaling** —
that feature's signal tests use synthetic `SignalImpl.Dispatch(...)`, which bypasses this path. This
is a pre-existing defect in the `signal` namespace's OS-registration path.

---

## Problem

`signal.on(Signal.Term, handler)` (and every other member of the global `Signal` enum) **does not
intercept the corresponding real OS signal.** The handler is registered in the in-process handler map
but no `PosixSignalRegistration` is ever created, so a real `SIGTERM`/`SIGINT`/`SIGHUP`/… takes the
default OS action (terminate the process) and the user's handler never runs. The graceful-shutdown
pattern the `signal` namespace exists to support is silently broken for real signals.

Synthetic dispatch (`SignalImpl.Dispatch("Term")` from C#, used by the test suite) *does* invoke the
handler — which is why every existing signal test is green while the real-OS path is dead.

## Reproduction

```bash
# A script that should ignore the first SIGTERM and print a message instead of dying:
cat > /tmp/sig.stash <<'EOF'
let stop = false;
signal.on(Signal.Term, () => { stop = true; io.println("caught SIGTERM"); });
io.println("pid ready");
let deadline = time.millis() + 10000;
while (!stop && time.millis() < deadline) { time.sleep(0.1); }
io.println(stop ? "graceful exit" : "timed out");
EOF

stash /tmp/sig.stash &   # note the PID it prints
kill -TERM %1
# Expected: "caught SIGTERM" then "graceful exit"
# Actual:   process terminates immediately (exit 143); handler never runs.
```

## Blast radius

- **Live, user-facing.** Any Stash script using `signal.on(Signal.X, …)` for graceful shutdown,
  SIGHUP-reload, or SIGINT-cleanup is silently non-functional against real signals — the handler is a
  no-op and the process dies on the default action. No error, no warning.
- **The documented usage is the broken one.** `SysBuiltIns.cs` doc comments give `Signal.Term` as the
  canonical example, so users following the docs hit this directly.
- **Becomes more load-bearing with callback-marshaling.** That feature makes `signal.on` callbacks
  able to mutate parent state (restoring the graceful-shutdown ergonomic) — which is exactly the use
  case this bug defeats for *real* signals. Synthetic dispatch works; production `kill` does not.
- **Masked indefinitely by the test suite**, which only ever uses synthetic `Dispatch(...)`.

## Root cause

Member-name mismatch between the global `Signal` enum and `SignalImpl.MapToPosixSignal`:

- `GlobalBuiltIns.Signal` (the global `Signal` users reference) = `{ Hup, Int, Quit, Kill, Usr1, Usr2,
  Term }` → `StashEnumValue.MemberName` is `"Hup"`, `"Term"`, etc. (`Stash.Stdlib/BuiltIns/GlobalBuiltIns.cs:246`).
- `SignalImpl.OnSignal` registers the handler under `signal.MemberName` (i.e. `"Term"`) and then calls
  `MapToPosixSignal("Term")` to create the `PosixSignalRegistration`
  (`Stash.Stdlib/BuiltIns/SignalImpl.cs:34,55`).
- `MapToPosixSignal` switches on the **deprecated** `SysBuiltIns.Signal` member names —
  `"SIGHUP"`/`"SIGINT"`/`"SIGQUIT"`/`"SIGTERM"` (`Stash.Stdlib/BuiltIns/SignalImpl.cs:143`,
  `SysBuiltIns.cs:35`). `MapToPosixSignal("Term")` matches nothing → returns `null` → the
  `if (posixSignal is not null)` guard skips registration. Handler stored, OS hook never installed.

(Note: `process.signal()` works because it uses a *separate*, correct map `["Term"] = 15` in
`GlobalBuiltIns.cs:509` — the two paths disagree on the vocabulary.)

## Suggested fix

This is a **bounded-domain / single-source-of-truth** defect: two enums and a third hand-map describe
the same closed set of signals with three different spellings. Per project convention, collapse to one.

- (A) **Teach `MapToPosixSignal` the canonical `Signal` member names** — add `"Hup"`, `"Int"`,
  `"Quit"`, `"Term"`, `"Usr1"`, `"Usr2"` (and decide `"Kill"` — `SIGKILL` is uncatchable, should
  throw/no-op explicitly). Smallest diff; leaves the duplicate-vocabulary smell.
- (B) **Drive registration off the typed enum, not a string** — carry the `Signal` enum value (or a
  single canonical name) end-to-end so `OnSignal`, `Dispatch`, `MapToPosixSignal`, and
  `process.signal()` all read one source of truth. A forgotten signal then fails to compile rather
  than silently no-op. Larger, but kills the class of bug (Construct over Detect).
- (C) Retire the deprecated `SysBuiltIns.Signal` spelling entirely so only `GlobalBuiltIns.Signal`
  exists.

**Recommend (B)** as the durable fix (matches the bounded-domain rule in `CLAUDE.md`), with (A) as the
cheap stopgap if a fix is needed before the larger refactor.

## Verification

- A regression test that installs `signal.on(Signal.Term, …)` and raises a **real** signal
  (`PosixSignalRegistration`-observable, or a child-process `kill -TERM` integration test) — must show
  the handler running. Today it does not.
- Assert `MapToPosixSignal` returns non-null for every `GlobalBuiltIns.Signal` member except the
  uncatchable ones — a small enumeration test that fails today for `"Term"`/`"Hup"`/etc.
- Existing synthetic-dispatch signal tests (`SignalNamespaceTests`, `SignalImplTests`) must stay green.

```bash
# After the fix, a new test class (e.g. SignalRealOsRegistrationTests) passes; before, it fails
# because no PosixSignalRegistration is installed for Signal.Term.
dotnet test --filter "FullyQualifiedName~SignalRealOsRegistrationTests"
```

## Related

- Surfaced by: `callback-marshaling` feature (`.kanban/2-in-progress/callback-marshaling/`), P3 example
  authoring. The feature's own signal tests are correct (synthetic dispatch) and unaffected.
- Same-surface files: `Stash.Stdlib/BuiltIns/SignalImpl.cs` (`OnSignal`, `MapToPosixSignal`, `Dispatch`),
  `Stash.Stdlib/BuiltIns/GlobalBuiltIns.cs` (`Signal` enum + `process.signal` map),
  `Stash.Stdlib/BuiltIns/SysBuiltIns.cs` (deprecated `Signal` enum).
- Convention this violates: root `CLAUDE.md` → "Bounded Domains (No Magic Strings)" / single source of
  truth; the durable fix is a real typed enum carried end-to-end.
