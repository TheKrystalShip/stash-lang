# Stash.Tests/Interpreting/Async — Contract Dimension Test Suite

This directory holds the dimension-organized correctness suite for the Stash async system,
introduced by the `async-correctness` feature (2026-06-05). Each subdirectory corresponds
to one contract dimension from the verified-behavior audit in
`.kanban/0-backlog/language/Async Correctness — Contract Audit and Test Suite Plan.md`.

Tests are organized by contract dimension rather than by API (e.g. not "TaskBuiltInsTests"
but "ErrorPropagation") so that cross-function contract properties — which are the things
most likely to be missed by a per-function suite — each have a named home.

## Dimension directories

| Directory | Contents |
| --------- | -------- |
| `Basics/` | `async fn` / lambda returns Future; `await` joins; nested async calls; async-fn-ref vs lambda; top-level `await`; `await` in non-async fn. |
| `ErrorPropagation/` | Every await path × every error type: `await`, `task.await`, `task.run`, `task.timeout`, `task.awaitAll`, `task.awaitAny`, `task.all`, `task.race`, `arr.par*` × {StashError type preserved, `throw string` → RuntimeError, foreign error wrap}. Also: `task.awaitAll` collect-all — element error type preservation (D2). |
| `CancellationAndTimeout/` | Cooperative stop verified by side-effect-after-sleep probe; `task.cancel` → `task.status == "Cancelled"` (D3); `await` after cancel → `CancellationError`; timeout-vs-cancel distinction (`task.timeout` still throws `TimeoutError`, not `CancellationError`); status transitions; `event.loop()` cancellation. |
| `LifecycleEdges/` | Double-`await` idempotent (D8); `await` on non-Future passthrough; `await` does not flatten Future-of-Future (one level only); Future-as-value (typeof, identity-`==`, stringify `<Future:Status>`, storable in arrays/dicts/struct fields). |
| `Isolation/` | Freeze-or-clone for `async fn` / `task.run` / `arr.par*`; frozen share-by-ref; cycle → ValueError; call-local mutation; Future handle shared not cloned across isolation boundary; `readonly` freeze sharing. |
| `Combinators/` | `arr.parMap` / `parFilter` / `parForEach` order preservation; async-callback flattening (D4 — parMap of async fn returns values not Futures); fail-fast first-error; `maxConcurrency` effect; `task.awaitAll` collect-all vs `task.all` fail-fast; `task.awaitAny` / `task.race` winner; empty-list edges. |
| `UnobservedAndExit/` | Fire-and-forget error reporting to stderr at script exit (D1); EmbeddedMode suppression; consumer-enumeration no-false-positive test (every observer — `await`, `task.await`, `task.awaitAll`, `task.awaitAny`, `task.all`, `task.race` — marks futures observed so D1 does not false-report); still-running task at exit dropped without stderr (row-10 in-flight-drop — `Category=Gotcha` change-detector). |
| `TwoSystemsBoundary/` | `event.poll()` does not advance a Future; `await` does not drain the event queue; cross-VM Process/socket handle use throws `StateError` (D5); two-system non-interaction is a positive contract. |

## Phases

Each dimension directory is populated by the phase that implements or stabilizes the
corresponding contract dimension:

- **P2** (D3 — genuine cancellation) → `CancellationAndTimeout/`
- **P3** (D2 — awaitAll error-type preservation) → `ErrorPropagation/`
- **P4** (D4 — arr.par* async flatten) → `Combinators/`
- **P5** (D5 — cross-VM handle throws StateError) → `TwoSystemsBoundary/`
- **P6** (D1 — observation registry + exit-hook report) → `UnobservedAndExit/`
- **P7** (cross-cutting dimensions: two-systems model, isolation matrix, basics, lifecycle edges, Future-as-value) → `Basics/`, `LifecycleEdges/`, `Isolation/`, remaining `TwoSystemsBoundary/`

## Naming convention

Tests use the standard convention from `Stash.Tests/CLAUDE.md`:

```
{Dimension}_{Scenario}_{Expected}()
```

Examples:
- `ErrorPropagation_AwaitAll_PreservesOriginalErrorType()`
- `CancellationAndTimeout_Cancel_StatusBecomesСancelled()`
- `Combinators_ParMapAsyncCallback_ReturnsValuesNotFutures()`
- `UnobservedAndExit_FaultedNeverAwaited_ReportsToStderr()`
- `TwoSystemsBoundary_CrossTaskProcessHandle_ThrowsStateError()`
