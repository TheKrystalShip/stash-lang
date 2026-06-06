# Static analyzer does not recognize the `event` namespace (SA0202 false positive)

**Status:** Backlog — Bug
**Created:** 2026-06-06
**Discovery context:** Surfaced during the `/feature-review` of `async-correctness` (F05). The feature's P1 spec rewrite added `event.poll` / `event.loop` to the canonical System B surface in the two-systems table of `docs/Stash — Language Specification.md`. The reviewer probed `stash-check` with a one-liner `event.poll();` to verify the analyzer would not flag the example script, and discovered the SA0202 false positive on the `event` namespace itself.

---

## Problem

The static analyzer (`stash-check`, also run inside the LSP server) does not recognize the `event` namespace as a defined stdlib symbol. Any use of `event.poll()`, `event.loop()`, etc. in a Stash source file emits diagnostic `SA0202 [warning] 'event' is not defined.` — a false positive, because `event` is a registered stdlib namespace and the runtime executes the call correctly.

This bug is **pre-existing** (the `event` namespace and the analyzer's namespace-table predate the async-correctness feature), but it is now user-facing in a way it wasn't before: the new Language Specification §Async explicitly names `event.poll` / `event.loop` as the canonical System B surface in the two-systems contract diagram. Users following the spec to write System B code will see the spurious SA0202 in their IDE.

## Reproduction

```bash
$ echo 'event.poll();' > /tmp/event_test.stash
$ stash-check /tmp/event_test.stash
# Actual:
#   /tmp/event_test.stash:1:1: SA0202 [warning] 'event' is not defined.
# Expected:
#   (no SA0202 diagnostic — event is a registered stdlib namespace with a poll function)
```

Contrast: `task.run(() => {});` does not trigger SA0202 — `task` is recognized.

The bug is deterministic and platform-agnostic; it appears on every analyzer invocation.

## Blast radius

- **Users:** any Stash author writing System B code (`event.*`, also worth auditing whether sibling namespaces are similarly missing). The two-systems model is the documented contract path for the event-queue surface.
- **IDE:** the LSP server consumes the same diagnostic stream — users get a spurious squiggle on every `event.X` call in their editor.
- **Examples / dogfooding:** `examples/async_correctness.stash` (the verified-example deliverable of `async-correctness`) carefully avoids `event.*` to sidestep the warning; that workaround is fragile and silently caps how illustrative the example can be. The dimension tests `NonInteractionTests.cs` embed `event.poll()` in Stash source strings that bypass the analyzer.
- **Compounding risk:** every future stdlib namespace that's added but not registered in the analyzer will reproduce this bug. An audit of the registration is appropriate (not just an `event` patch).

## Root cause

Unknown precisely — needs investigation. Hypothesis: the analyzer's namespace-resolution table in `Stash.Analysis/` is hand-maintained or generated from a different source than the runtime stdlib registry (`Stash.Stdlib.Generators` → `GeneratedStdlibRegistry.g.cs`), and the two have drifted. The runtime knows about `event` (`Stash.Stdlib/BuiltIns/EventBuiltIns.cs` declares `[StashNamespace] public static partial class EventBuiltIns` and the source generator picks it up); the analyzer apparently does not. Compare with how the analyzer recognizes `task` and `arr` to see what the registration shape is supposed to be.

## Suggested fix

- (A) **Single-namespace patch** — find the analyzer's namespace registration site, add `event`. Cheap. Doesn't catch other gaps.
- (B) **Shared registry** — refactor the analyzer to read its known-namespace set from the same source the runtime uses (`GeneratedStdlibRegistry.g.cs` or its source). Eliminates the class of bug (Construct, not Detect, per the omission-hardening doctrine in CLAUDE.md). More expensive; touches the analyzer's symbol-collection.
- (C) **Audit + patch** — audit every `[StashNamespace]`-attributed class in `Stash.Stdlib/BuiltIns/` against the analyzer's known set; patch each gap. Same cost as (A) per gap; produces a list of what's broken now without preventing future drift.

Recommend **(B)** to align with the project's Construct-over-Detect doctrine — this is exactly the kind of cross-cutting omission that should be made impossible by construction. (A) is acceptable as a stopgap if (B) is out of budget; (C) should be done as a discovery pass regardless to scope the work.

## Verification

```bash
# Regression test (negative — bug present today):
echo 'event.poll();' > /tmp/event_test.stash
stash-check /tmp/event_test.stash
# Before fix: emits SA0202 on 'event'.
# After fix: no SA0202 diagnostic.

# Targeted suite test (after the fix lands):
dotnet test --filter "FullyQualifiedName~Stash.Tests.Analysis" # must stay green

# If (B) is taken, the audit pass test should enumerate every [StashNamespace] and assert
# the analyzer resolves each — a Construct-level floor test.
```

## Related

- Surfaced during `/feature-review` of `async-correctness` — see `.kanban/2-in-progress/async-correctness/review.md` F05.
- The Language Specification rewrite that names `event.*` as System B: `docs/Stash — Language Specification.md` §Async (commit `410ef8fe`).
- `Stash.Stdlib/BuiltIns/EventBuiltIns.cs` — the runtime registration that the analyzer disagrees with.
- `language-changes.md` — the toolchain-compat checklist that explicitly lists static analysis as a mandatory check for every language/stdlib change.
- `CLAUDE.md` "Prevent omission, don't detect it" — the doctrine recommendation B follows.
