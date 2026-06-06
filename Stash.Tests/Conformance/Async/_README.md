# Stash.Tests/Conformance/Async — §Async Conformance Tests

This directory proves that the Stash implementation honors the normative clauses of
`docs/Stash — Language Specification.md` **§Async = Async Functions and Await** (L1428–1787).

Populated by the `language-standard-async` milestone unit (P2–P7). Each file covers one
spec clause group; every test carries `[Trait("Category", "Conformance")]` and names the
specific spec claim it proves (see `Stash.Tests/Conformance/_README.md` for the naming
convention, and `Stash.Tests/CLAUDE.md` → "Conformance tests — proving the spec" for the
full protocol).

---

## Clause groups this directory will prove

The following clause groups are the sealed normative surface of §Async that this directory
proves. Each becomes one conformance test class (one `*ConformanceTests.cs` file):

| Clause group | Spec location | File (target) | Phase |
| ------------ | ------------- | ------------- | ----- |
| **D6** — `await` blocks and is uncolored | L1464–1469 | `FuturesCoreConformanceTests.cs` | P2 |
| **D7** — error-type fidelity through `await` | L1471–1479 | `FuturesCoreConformanceTests.cs` | P2 |
| **D8** — settled-Future replay (already-cancelled / already-faulted) | L1487–1498 + Edit 6 | `FuturesCoreConformanceTests.cs` | P2 |
| **D9** — Future is a first-class value | L1487–1498 | `FuturesCoreConformanceTests.cs` | P2 |
| **D2** — `task.awaitAll` collect-all + error-type preservation | L1506–1511 | `CombinatorsConformanceTests.cs` | P3 |
| **D4** — `arr.par*` async-callback flatten | L1523–1524 | `CombinatorsConformanceTests.cs` | P3 |
| **D10** — `arr.par*` order, fail-fast, `maxConcurrency`; `arr.parForEach` returns null | L1513–1524 + Edit 7 | `CombinatorsConformanceTests.cs` | P3 |
| **D3** — cooperative cancellation + `Status.Cancelled` | L1534–1543 | `CancellationConformanceTests.cs` | P4 |
| `task.cancel` return value + idempotency | Edit 4 | `CancellationConformanceTests.cs` | P4 |
| `task.Status` qualified-access + closed-set | Edit 5 | `CancellationConformanceTests.cs` | P4 |
| **D1** — unobserved-fault report at exit | L1559–1573 | `UnobservedExitConformanceTests.cs` | P5 |
| Still-running at exit is dropped (negative space) | L1554 + Edit 8 | `InFlightDropConformanceTests.cs` | P5 |
| Async-child global isolation (frozen-share, deep-clone, cycle ValueError) | L1576–1624 | `IsolationConformanceTests.cs` | P6 |
| **D5** — cross-VM Process/socket handle → `StateError` | L1526–1532 | `TwoSystemsConformanceTests.cs` | P6 |
| **D11** — two-systems model + non-interaction | L1440–1442, L1628–1655 | `TwoSystemsConformanceTests.cs` | P6 |
| `task.resolve(value?)` — already-resolved Future | Edit 2 | `TaskResolveDelayConformanceTests.cs` | P7 |
| `task.delay(seconds)` normative clause + example fix | Edits 1 + 3 | `TaskResolveDelayConformanceTests.cs` | P7 |

### Spec edits that seal previously unwritten clauses

| Edit | Description | Phase that lands the spec edit |
| ---- | ----------- | ------------------------------ |
| Edit 1 | Fix contradicted `task.delay(1s)` example → `task.delay(1)` | P7 |
| Edit 2 | Add `task.resolve(value?)` normative clause | P7 |
| Edit 3 | Add `task.delay(seconds)` normative clause | P7 |
| Edit 4 | `task.cancel` return value + idempotency | P4 |
| Edit 5 | `task.Status` qualified-access + closed-set | P4 |
| Edit 6 | `await` on already-cancelled / already-faulted Future (D8 extended) | P2 |
| Edit 7 | `arr.parForEach` return value (D10 extended) | P3 |
| Edit 8 | Still-running at exit is dropped — promoted to normative negative space | P5 |

### "No-edit" clauses already correct in the spec

D1, D2, D3, D4, D5, D6, D7, D9, D10 (partial), D11, async isolation, event-queue drain
points, file-watch pattern, `event.poll()`/`event.loop()` — these have correct spec prose
already; this directory proves them with conformance tests without editing the spec.

---

## Naming convention (examples)

```
FuturesCoreConformanceTests:
  D6_AwaitIsUncolored_PerSpecAsyncD6()
  D7_AwaitPreservesErrorType_TypeError_PerSpecAsyncD7()
  D8_SettledFaultedFuture_SecondAwait_RethrowsCachedError_PerSpecAsyncD8()
  D9_FutureIsFirstClass_StoredInArray_PerSpecAsyncD9()

CancellationConformanceTests:
  D3_Cancel_StatusBecomesCancelled_PerSpecAsyncD3()
  Cancel_Idempotent_DoubleCancel_NoOp_PerSpecAsyncTaskCancel()
  Status_QualifiedAccess_PerSpecAsyncTaskStatus()

UnobservedExitConformanceTests:
  D1_UnobservedFault_ReportsToStderrAtExit_PerSpecAsyncD1()

InFlightDropConformanceTests:
  StillRunningAtExit_Dropped_NotReported_PerSpecAsyncNegativeSpace()
```
