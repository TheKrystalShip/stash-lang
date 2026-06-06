# language-standard-async — Review

> Produced by `/feature-review`. One finding per H2 section.
> Each finding header MUST be parseable: `## Fxx — [SEVERITY] short title`.
> `/resolve <feature> Fxx [Fyy...]` reads the selected section(s) and dispatches a Resolver.

**Scope reviewed:** commits `478247b31b8b368b5ac216137a5118975ee306f6..d997b9119b38ea3619dc11920e0814adeb1ccc0a` on branch `feature/language-standard-async`
**Brief:** ../brief.md
**Generated:** 2026-06-06 16:30

**Baseline:** `dotnet test` PASS — failed=0 passed=14212 skipped=6; `dotnet test --filter "Category=Conformance"` binds 121 tests, all green. Trait-guard floor `MinScannedParticipants=8` matches the 8 `*ConformanceTests` classes on disk.

**Summary:** This is a spec-sealing unit, and the conformance scaffold, trait-guard, and most of the §Async prose are sound. The trait-guard has teeth (a self-test fixture trips it; the floor is honest; infrastructure exclusion is double-asserted). All 8 Edits land in the spec; all 8 conformance test classes cite their clauses; the dimension suite and conformance suite are correctly complementary. **However**, the seal-discipline failed in three places that matter: (1) **D5 was unilaterally narrowed by the implementer** to legalize an impl gap that the user-locked `async-correctness` decision explicitly existed to prevent (the "silent corruption is the worst outcome" rationale); (2) one verbatim-quoted normative `StateError` message in the spec does **not match** what the impl emits, and the matching conformance test is too loose to catch it (assertion is `Contains("task")` + `Contains("process")`); (3) the §Async D9 prose continues to cite the function `conv.str(future)` which **does not exist** at runtime (it is `conv.toStr`). The seal pass missed these by writing tests that pass against the impl rather than tests that pin the spec's verbatim claims. Two of those gaps were pre-existing in the original async-correctness ship and survived the audit; one was created by P6. The remaining findings are quality items: the coverage map's "Seal-status discriminator" paragraph still claims "zero Conformance tests" across the whole milestone (now contradicted by cross-cutting #1 being marked complete), a clause-completeness gap on `task.await`/`task.status` non-Future validation (parallel to Edit 4's `task.cancel` clause), and stub the spec-vs-impl gaps on `task.race([])` / `task.awaitAny([])` empty-array error types that the audit incidentally surfaced but are out-of-scope.

**Timing assessed:** poll-loop pattern is `i<40` with 50ms sleeps (~2s deterministic budget), `task.delay(5)`/`time.sleep(10)` race margins are generous (3+ orders of magnitude > expected scheduler latency). No flake risk in the conformance suite.

---

## F01 — [HIGH] D5 was unilaterally narrowed past a user-locked safety decision; impl gap remains unenforced

**Status:** fixed
**Fixed in:** b8c6ad11
**Files:** `docs/Stash — Language Specification.md:1573-1579`, `Stash.Tests/Conformance/Async/TwoSystemsConformanceTests.cs:27-31`, `Stash.Stdlib/BuiltIns/NetSocketImpl.cs:29-38` (process-global `ConditionalWeakTable<StashInstance, *>` — no per-context tracking analogous to `ProcessBuiltIns.cs:1054-1071`)
**Phase:** P6
**Commit:** c7e618b4

### Observation

The spec originally claimed (pre-feature L1531–1532) "The same boundary applies to socket / TcpServer / TcpClient handles." That claim came from the `async-correctness` feature's D5 — a **user-locked Tier-1 decision** recorded in `.kanban/4-done/async-correctness/brief.md:484` as:

> 2026-06-05 | D5 → cross-VM handle use throws `StateError` instead of silent empty result | User-locked; **silent empty is the worst outcome**.

P6 of this unit narrowed the spec (docs/Stash — Language Specification.md:1573-1579) to:

> **Socket handle task-affinity.** Socket-creation functions ... return handles that are *ideally* used within the task that created them. Unlike `process.spawn()`, the runtime does **not** currently enforce this ... passing a `TcpConnection` or `TcpServer` handle across a task boundary **may produce undefined behavior** (underlying `TcpClient` state accessed from multiple threads simultaneously) but does not throw a guaranteed `StateError`.

I verified the impl gap is real: `Stash.Stdlib/BuiltIns/NetSocketImpl.cs:29-38` stores all socket handles in **process-global** `ConditionalWeakTable<StashInstance, *>` containers with no per-context tracking — the structural primitive that lets `ProcessBuiltIns.cs:1054-1071` enforce the boundary (`ctx.TrackedProcesses` lookup → `StateError` if not found) does not exist for sockets. The `async-correctness` socket enforcement was never built; the spec claim was aspirational and the dimension suite (`Stash.Tests/Interpreting/Async/TwoSystemsBoundary/CrossVmHandleTests.cs`) only tested process handles.

The new spec language now **blesses "undefined behavior / silent corruption"** as legal — exactly the outcome the locked decision existed to prevent. The checkpoint notes (`checkpoint.yaml:53`) flag this as `architect-ratification pending`, but the spec edit landed *before* ratification.

The accompanying conformance class `TwoSystemsConformanceTests` deliberately omits socket tests and explains in the XML doc (lines 27-31) that socket enforcement is "currently unspecified at the implementation level." So no conformance test proves either the (now-narrowed) negative-space claim about sockets OR the original boundary claim.

### Why this matters

This is the precise drift the milestone Specification-is-the-Law doctrine and this unit exist to prevent. An implementer narrowing a user-locked, safety-rationaled normative decision to make the impl gap legal is **structurally** the same pattern as the original async-gap (behavior shipped without spec; behavior chosen by code, not by design) — except here the doctrine fights it correctly and the narrowing should have been blocked. Seal-first-then-bend means *fix the code to honor the law, or correct the law on purpose* — the second branch is for the architect/user, not the implementer, when the locked decision has a documented safety rationale.

Concretely: a Stash program that hands a `TcpConnection` to a child task will silently mis-behave (concurrent `TcpClient` access; the impl already documents this is data-race UB), with no diagnostic — exactly "silent empty" rendered as "silent corruption." This is the worst-outcome failure mode the original D5 lock rejected.

### Suggested fix

This is a direction decision that requires explicit architect/user ratification — an implementer cannot unilaterally narrow it. Two paths:

1. **Honor the lock — fix the impl.** Restore the original spec wording at L1573–1579 to "the same boundary applies to socket / TcpServer / TcpClient handles" and add per-context tracking to `NetSocketImpl.cs` so socket builtins (`tcp.recv*`, `tcp.send*`, `tcp.accept`, `tcp.close`, etc.) throw `StateError` analogously to `ResolveTrackedProcess`. Land conformance tests asserting the boundary on each socket builtin. Add a UdpSocket path or normatively document that UDP has no handle (D11 already covers UDP as "no handle object" if intended).
2. **Bend the law deliberately.** Get explicit architect/user ratification of the narrowing — log the decision and its rationale in the `coverage.md` "Live spec-vs-impl contradictions" table, link the ratification, and **add a conformance test** that *proves* the narrowed claim ("a `TcpConnection` shared across a task boundary does not throw `StateError`" — pin the negative-space behavior so a future regression to "throws StateError" is caught as a spec change). Note that this branch must explain why "silent UB" is now acceptable when "silent empty" was not.

Whichever direction is chosen, file a `0-backlog/bugs/` stub (using `_templates/bug-template.md`) for the actual socket cross-task enforcement work so the gap is tracked regardless of how ratification lands.

### Verify

After path (1):
```
dotnet test --filter "FullyQualifiedName~TwoSystemsConformanceTests"  # add socket tests
dotnet test  # full suite
```

After path (2):
```
# Add a new conformance test that pins the documented impl behavior, then:
dotnet test --filter "Category=Conformance"
grep -n "socket" "docs/Stash — Language Specification.md"  # confirm narrowed prose
# Confirm coverage.md row for §Async records the spec-vs-impl ratification
```

---

## F02 — [MEDIUM] D5 `StateError` message in the spec does not match what the impl emits; conformance test is too loose to catch it

**Status:** fixed
**Fixed in:** b8c6ad11
**Files:** `docs/Stash — Language Specification.md:1564-1571`, `Stash.Stdlib/BuiltIns/ProcessBuiltIns.cs:1067-1071`, `Stash.Tests/Conformance/Async/TwoSystemsConformanceTests.cs:92-109`
**Phase:** P6
**Commit:** c7e618b4

### Observation

The spec at L1564–1571 quotes a **verbatim** normative message inside backticks (the convention for a fixed user-visible string):

> Using a parent's handle inside a child task (via `task.run` or `async fn`) throws `StateError` with the message
> `"process handle does not cross task boundaries: this Process was created in a different task; pass the result of process.spawn() back to the parent via the task's return value"`.

The impl at `ProcessBuiltIns.cs:1067-1071` actually throws:

```
$"'{funcName}': process handle does not cross task boundaries. " +
"Spawn the process inside the same task that uses it."
```

These are different sentences — the spec promises a sentence containing "this Process was created in a different task; pass the result of process.spawn() back to the parent via the task's return value"; the impl emits "Spawn the process inside the same task that uses it." Neither variant of the impl message contains the substring "this Process was created in a different task" or "pass the result of process.spawn() back to the parent via the task's return value."

The conformance test `D5_ProcessWait_CrossTask_ErrorMessage_NamesBoundary_PerSpecAsyncD5` (TwoSystemsConformanceTests.cs:92-109) asserts only:

```csharp
Assert.Contains("task", msg, StringComparison.OrdinalIgnoreCase);
Assert.Contains("process", msg, StringComparison.OrdinalIgnoreCase);
```

That is weak enough to pass over the mismatch — both substrings are present in the impl's actual message — and the spec's verbatim quote is therefore unsealed by any test that would force it to match.

### Why this matters

Backtick-quoted normative strings in the spec are a stronger claim than ordinary prose — they're the spec's way of saying "this exact text is the contract." A reader writing a `catch` block that pattern-matches the message on the documented sentence, or LSP/diagnostic UI that promotes specific phrasings, would fail to recognize the impl's actual message. This is the same drift class as F03 (`conv.str` vs `conv.toStr`): the seal pass tested the impl, not the spec, so the prose's specific text claims went unverified. For a sealing unit, that inverts the discipline.

This finding is MEDIUM (not HIGH) because the substring-match assertion does pin the message as "task-boundary-naming" (the audit gets *some* of the spec's promise), and the disagreement is about which sentence — no runtime behavior is wrong, only the user-facing wording.

### Suggested fix

Two paths, both clean:

1. **Tighten the spec to the impl.** Replace the backtick block at L1567–1568 with the actual impl message: `"'<funcName>': process handle does not cross task boundaries. Spawn the process inside the same task that uses it."` (parametrizing `<funcName>` as the spec already implicitly does), and update `D5_ProcessWait_CrossTask_ErrorMessage_NamesBoundary` to assert the full new message verbatim.
2. **Tighten the impl to the spec.** Change `ProcessBuiltIns.cs:1067-1071` to emit the spec's quoted sentence (you can keep the `'{funcName}':` prefix if the spec implicitly accepts a function-name prefix); tighten the conformance test to verbatim-`Assert.Contains` the back-half of the quoted sentence (`"this Process was created in a different task"` and `"pass the result of process.spawn() back to the parent"`).

Either way the test must verbatim-pin the chosen sentence so a future drift in either direction fails loud.

### Verify

```
dotnet test --filter "FullyQualifiedName~D5_ProcessWait_CrossTask_ErrorMessage_NamesBoundary"
grep -n "process handle does not cross" "docs/Stash — Language Specification.md" Stash.Stdlib/BuiltIns/ProcessBuiltIns.cs
```

---

## F03 — [MEDIUM] §Async D9 prose cites `conv.str(future)` but the function is `conv.toStr` — spec-vs-impl gap inside the sealed prose

**Status:** fixed
**Fixed in:** f1ba8c7e
**Files:** `docs/Stash — Language Specification.md:1506`, `docs/Stash — Language Specification.md:1681`, `docs/Stash — Language Specification.md:1843`, `Stash.Stdlib/BuiltIns/ConvBuiltIns.cs:19`, `Stash.Tests/Conformance/Async/FuturesCoreConformanceTests.cs:400-442`
**Phase:** P2 (D9 conformance landed without catching this)
**Commit:** 7f88372a

### Observation

The D9 paragraph at L1506 reads:

> `conv.str(future)` (or implicit stringification) produces `"<Future:Running>"`, ...

`conv.str` does **not** exist at runtime. I verified at the CLI:

```
$ stash -c 'io.println(conv.str(42));'
RuntimeError: Namespace 'conv' has no member 'str'.
```

The actual function is `conv.toStr` (`ConvBuiltIns.cs:19` — `public static string ToStr(StashValue value)` which maps to Stash `toStr`). The conformance tests `D9_ConvToStr_CompletedFuture_PerSpecAsyncD9`, `D9_ConvToStr_FailedFuture_PerSpecAsyncD9`, and `D9_ConvToStr_CancelledFuture_PerSpecAsyncD9` (FuturesCoreConformanceTests.cs:400-442) all correctly call `conv.toStr(f)` and assert the bracketed output — but they test the impl, not the spec's literal claim. The spec's claim (`conv.str`) goes untested.

The same name appears at two other places **inside §Async** (L1428–§Async-end ≈L1874 in the post-Edit numbering):
- L1681 — the deep-clone migration example: `return config.host + ":" + conv.str(config.port);`
- L1843 — the file-watch example (in the §`event` Namespace sub-section, still inside §Functions/Closures/Async): `io.println("polled: " + conv.str(polled));   // "polled: true"`

All three are inside §Async footprint and in scope for this sealing unit.

### Why this matters

This is precisely the spec-the-law-vs-code-the-law inversion the milestone exists to fix: the spec says one thing, the impl says another, the conformance test tests the impl. A reader following the §Async D9 stringification clause would write `conv.str(f)` and get a `RuntimeError`. The seal pass had eyes on this exact paragraph (D9 is in scope; FuturesCoreConformanceTests has three tests citing `D9` and exercising the stringification surface) and missed the symbol-name mismatch.

Severity is MEDIUM (not HIGH) because the discriminator for HIGH vs MEDIUM is whether following the spec produces **wrong behavior** vs a **wrong name**: this misleads the reader (they get a RuntimeError, a clear failure) but corrupts no runtime semantics. It is a load-bearing seal-pass miss nonetheless.

### Suggested fix

Edit `docs/Stash — Language Specification.md` to replace `conv.str` with `conv.toStr` at L1506, L1681, and L1843 (three occurrences). Tighten the D9 conformance tests in `FuturesCoreConformanceTests.cs:400-442` to also assert the spec's *implicit stringification* claim with a single string-interpolation test (e.g. `"f=" + f == "f=<Future:Completed>"`) so the negative-space "or implicit stringification" half of the D9 clause has a conformance proof as well.

Alternatively, if `conv.str` is meant to exist (it does not, today), this becomes a stdlib-add — but that is a much larger change and outside the spec-sealing scope; the prose-fix path is the correct one.

### Verify

```
grep -n "conv.str" "docs/Stash — Language Specification.md"    # must return zero matches
dotnet test --filter "FullyQualifiedName~D9_ConvToStr"
dotnet run --project Stash.Cli/ -- -c 'let f = task.resolve(1); io.println("" + f);'  # implicit stringification
```

---

## F04 — [MEDIUM] coverage.md "Seal-status discriminator" still claims zero Conformance tests across all 13 sections; contradicts cross-cutting #1 marked complete

**Status:** fixed
**Fixed in:** f1ba8c7e
**Files:** `.kanban/milestones/language-standard/coverage.md:34-42`, `.kanban/milestones/language-standard/coverage.md:68-72`
**Phase:** P8
**Commit:** 644e3437

### Observation

The "Seal-status discriminator" paragraph (lines 34–42) reads:

> **Seal-status discriminator.** "Zero `Category=Conformance` tests" is *uniform* across all 13 sections (none exist yet) — so it is a **milestone-wide precondition, not a per-section maturity signal**. The operative axis is therefore the prose itself: ...

This was true at audit time. After P8, cross-cutting workstream #1 at lines 68–72 was correctly marked **complete**:

> 1. **Stand up the `Conformance/` suite.** ✅ **Complete** (`language-standard-async`, 2026-06-06). `Stash.Tests/Conformance/Async/` established (8 test classes, 105+ tests) ...

These two paragraphs now contradict each other. Conformance tests exist for §Async (121 bound under the filter, per the baseline). The "discriminator paragraph" was a true statement about an *audit-time* condition and should either be deleted (it has served its purpose) or updated to reflect the new reality (sections with conformance coverage vs. sections without are now actually a maturity signal).

### Why this matters

`coverage.md` is the **single source of truth** for the milestone's roll-up state (per its own header). A document that contradicts itself in two adjacent paragraphs degrades the "single source of truth" guarantee and confuses future implementers (or the architect designing the next unit) about what the milestone has accomplished. This is also the exact item the user flagged as a watch-point — confirmed real.

### Suggested fix

Either:
1. **Delete the paragraph entirely** (lines 34–42) — the framework prose at the top of the file already explains the sealed/partial/unsealed criteria; this paragraph was a historical note about audit time and is no longer needed once one unit is conformance-populated.
2. **Update it to reflect post-async reality** — change the lead from "Zero Category=Conformance tests is uniform" to something like "Until cross-cutting #1 ships, zero Category=Conformance tests was uniform; with §Async sealed, conformance presence is a per-section maturity signal — every later sealed row will have its `Conformance/<Area>/` populated."

Option 1 is cleaner. Option 2 keeps the historical narrative but increases the maintenance load.

### Verify

```
grep -n "Zero .Category=Conformance.\|Category=Conformance.* none exist" .kanban/milestones/language-standard/coverage.md
# After fix, no contradictory paragraph should remain
```

---

## F05 — [LOW] §Async does not normatively seal that `task.await(non-Future)` / `task.status(non-Future)` throws TypeError — parallel-to-Edit-4 clause gap

**Status:** fixed
**Fixed in:** e7110a6d
**Files:** `docs/Stash — Language Specification.md:1588-1591`, `Stash.Stdlib/BuiltIns/TaskBuiltIns.cs:65,188,413-416`
**Phase:** P4
**Commit:** be517bfe

### Observation

Edit 4 sealed `task.cancel(non-Future) → TypeError` as a normative clause at L1588–1591:

> `task.cancel(future)` returns `null`. ... **Cancelling a non-`Future` value throws `TypeError`.**

The P4 impl change (`Stash.Stdlib/BuiltIns/TaskBuiltIns.cs:413-416`) widened `GetFuture`'s exception type from `RuntimeError` to `TypeError`, and `GetFuture` is called by **three** task builtins:

- `task.await` (line 65) — `<exception cref="TypeError">` in the doc-comment
- `task.status` (line 188) — `<exception cref="TypeError">` in the doc-comment
- `task.cancel` (line 201) — `<exception cref="TypeError">` in the doc-comment; **sealed by Edit 4**

`task.cancel` is the only one of the three with a corresponding **normative spec clause**. The conformance suite proves `task.cancel(42) → TypeError` (`Edit4_Cancel_NonFuture_ThrowsTypeError_PerSpecAsyncEdit4`) but there is no normative seal — or corresponding conformance test — for `task.await(42)` or `task.status(42)`. The TypeError fidelity for these two now lives only in the doc-comment metadata (driving the generated reference); a future change to widen them back to `RuntimeError` would be undetected.

### Why this matters

Edit 4 set a precedent for sealing validation-error types at the spec layer. The "negative space" half of the spec law — *what error type a non-Future argument gets* — is now sealed for one of three otherwise-symmetric builtins. Either the symmetry is intentional (and the spec should say so) or the seal is incomplete (and the other two should match). This is exactly the kind of partial-completeness gap the milestone's negative-space discipline is meant to prevent.

LOW because the impl is currently correct (all three throw TypeError) and the doc-comment metadata pins it for tooling; the gap is purely on the spec/normative side.

### Suggested fix

Extend the Edit 4 paragraph (or add a small companion clause) at L1588–1591 with a single sentence:

> All `task.*` builtins that consume a `Future` argument (`task.await`, `task.status`, `task.cancel`) throw `TypeError` when given a non-Future value.

Then add two short conformance tests to `CancellationConformanceTests.cs` (or `FuturesCoreConformanceTests.cs` for `task.await`):

```csharp
[Fact]
public void TaskAwait_NonFuture_ThrowsTypeError_PerSpecAsyncValidation()
{
    var error = RunCapturingError("task.await(42);");
    Assert.Equal("TypeError", error.ErrorType);
}

[Fact]
public void TaskStatus_NonFuture_ThrowsTypeError_PerSpecAsyncValidation()
{
    var error = RunCapturingError("task.status(42);");
    Assert.Equal("TypeError", error.ErrorType);
}
```

Bump `MinScannedParticipants` only if these land in a new class.

### Verify

```
dotnet test --filter "FullyQualifiedName~TaskAwait_NonFuture|FullyQualifiedName~TaskStatus_NonFuture"
grep -n "All .task.\* builtins that consume a .Future." "docs/Stash — Language Specification.md"
```

---

## F06 — [LOW] File a backlog stub for the empty-array combinator error-type bugs surfaced by the audit (out-of-scope but worth tracking)

**Status:** fixed
**Fixed in:** e7110a6d
**Files:** `Stash.Stdlib/BuiltIns/TaskBuiltIns.cs:148,156,279`, `docs/Stash — Standard Library Reference.md` (generated — propagates the wrong cref)
**Phase:** cross-phase (audit byproduct)
**Commit:** -

### Observation

Three pre-existing impl-vs-doc-comment-metadata gaps surfaced while auditing this unit's `GetFuture` fix and the `task.race` / `task.awaitAny` conformance coverage. They are **not** in this unit's spec-sealing scope (the spec does not normatively seal empty-array fail-fast semantics) and they did **not** regress in this feature — they pre-date `async-correctness`. They should be tracked because the wrong error types propagate into the generated `docs/Stash — Standard Library Reference.md`.

| Builtin | Doc-comment cref | Actual impl throw | Location |
| ------- | ---------------- | ----------------- | -------- |
| `task.race([])` | `<exception cref="ValueError">` | `throw new RuntimeError("task.race() expects a non-empty array.")` | `TaskBuiltIns.cs:279` |
| `task.awaitAny([])` | `<exception cref="ValueError">` | `throw new RuntimeError("task.awaitAny() expects a non-empty list.")` | `TaskBuiltIns.cs:148` |
| `task.awaitAny(non-Future-element)` | `<exception cref="TypeError">` | `throw new RuntimeError("First argument to 'task.awaitAny' must be a Future.")` | `TaskBuiltIns.cs:156` |

The generated stdlib reference therefore documents `ValueError`/`TypeError` thrown but the runtime throws bare `RuntimeError`.

### Why this matters

These are not review-md findings (per the kanban convention that pre-existing out-of-scope bugs go to `0-backlog/bugs/`, not `review.md`). They are recorded here as a single LOW pointer so the audit's byproduct isn't lost — the resolver/reviewer should file a `0-backlog/bugs/` stub using `_templates/bug-template.md` and close this finding. Bringing them up to par would be a 3-line follow-up `language-standard-async-`-style mini-fix or a `Stash.Stdlib`-scope task.

### Suggested fix

File `.kanban/0-backlog/bugs/task-combinator-empty-array-error-types.md` (template-compliant: Status / Created / Discovery context / Problem / Reproduction / Blast radius / Root cause / Suggested fix / Verification / Related), naming the three offending `throw new RuntimeError(...)` lines and the doc-comment crefs they contradict. The fix itself is mechanical (`new ValueError(...)` / `new TypeError(...)`) and the regenerated reference falls out.

Mark this finding **`fixed`** when the stub is filed.

### Verify

```
ls .kanban/0-backlog/bugs/task-combinator-empty-array-error-types.md
grep -n "RuntimeError\|ValueError\|TypeError" Stash.Stdlib/BuiltIns/TaskBuiltIns.cs | grep -E "(awaitAny|race)"
```
