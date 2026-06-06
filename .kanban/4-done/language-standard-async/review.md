# language-standard-async — Review (pass 2 of 2 — re-review)

> Produced by `/feature-review`. One finding per H2 section.
> Each finding header MUST be parseable: `## Fxx — [SEVERITY] short title`.
> `/resolve <feature> Fxx [Fyy...]` reads the selected section(s) and dispatches a Resolver.
>
> Pass-1 findings (F01H, F02M, F03M, F04M, F05L, F06L) all `fixed` and live in git history
> (commits `b8c6ad11`, `f1ba8c7e`, `e7110a6d`). This is the single re-review pass — there is
> no third pass.

**Scope reviewed:** commits `478247b31b8b368b5ac216137a5118975ee306f6..16b01f71c619ce58d40d859c7bb82cfe753fea6f` on branch `feature/language-standard-async`
**Brief:** ../brief.md
**Generated:** 2026-06-06

---

## Re-review verdict per prior finding

- **F01 (CRITICAL → fixed).** Spec `Socket handle task-affinity (D5 — enforcement pending)` at
  `docs/Stash — Language Specification.md:1573–1586` (a) does NOT bless undefined behavior — it
  states the boundary is "**not yet enforced for socket handles**" and that cross-task socket
  use is "**unsupported and unsafe**"; (b) honestly states D5's intent covers sockets but only
  Process enforcement was built; (c) references the backlog stub
  `.kanban/0-backlog/bugs/tcp-socket-handle-task-boundary-enforcement.md` by path. The whole
  diff range `478247b3..16b01f71` adds **zero** code to `NetSocketImpl.cs` or `TcpBuiltIns.cs`
  (verified by `git diff --stat`) — the fix is spec+backlog only, correctly scoped. The
  backlog stub is comprehensive: spike evidence for both manifestations (silent corruption +
  wrong-error-type), A1/A2 design alternatives, websockets.stash migration noted, all bug-template
  sections present. Brief Decision Log entry records the user-ruled decision with full rationale.
  **Confirmed.**
- **F02 (IMPORTANT → fixed).** Spec message at `docs/Stash — Language Specification.md:1567`
  matches `Stash.Stdlib/BuiltIns/ProcessBuiltIns.cs:1067–1071` verbatim. The conformance test
  `D5_ProcessWait_CrossTask_ErrorMessage_NamesBoundary_PerSpecAsyncD5` was
  **strengthened**, not weakened: it pins two verbatim sentence fragments (`"process handle
  does not cross task boundaries"` and `"Spawn the process inside the same task that uses it"`)
  with `StringComparison.Ordinal`, replacing the prior loose case-insensitive `Contains "task"`
  + `Contains "process"` predicate. **Confirmed.**
- **F03 (IMPORTANT → fixed).** `grep -n "conv\.str" docs/Stash — Language Specification.md`
  returns zero hits; the 3 occurrences are now `conv.toStr` (L1506, L1690, L1852). The
  conformance test `D9_ImplicitStringification_CompletedFuture_PerSpecAsyncD9` proves the
  "(or implicit stringification)" half of the D9 clause via `"" + f` concatenation and
  asserts `"<Future:Completed>"`. **Confirmed.**
- **F04 (IMPORTANT → fixed, with one stale spot remaining — see F07 below).** The
  `Seal-status discriminator` paragraph (`coverage.md:34–38`) was reframed: it acknowledges
  conformance presence as a per-section maturity signal post-§Async-seal AND preserves the
  prose-based discriminator, removing the prior self-contradiction with cross-cutting #1's
  "Complete" mark. **Confirmed for the discriminator paragraph itself.** (A SEPARATE stale
  sentence in coverage.md row #6 — written at P6 and not updated by F01 — is filed as a new
  finding F07 below.)
- **F05 (MINOR → fixed).** New normative sentence at `docs/Stash — Language Specification.md:1598–1600`
  seals: "All `task.*` builtins that consume a `Future` argument (`task.await`, `task.status`,
  `task.cancel`) throw `TypeError` when given a non-Future value." Two conformance tests added in
  `CancellationConformanceTests.cs:212–227` — `TaskAwait_NonFuture_ThrowsTypeError_PerSpecAsyncValidation`
  and `TaskStatus_NonFuture_ThrowsTypeError_PerSpecAsyncValidation` — assert `error.ErrorType
  == "TypeError"` against `task.await(42)` and `task.status(42)`. `task.cancel(non-Future)` was
  already covered by the pre-existing `Edit4_Cancel_StringArg_ThrowsTypeError_PerSpecAsyncEdit4`,
  so all three call sites are now conformance-tested. **Confirmed.**
- **F06 (MINOR → fixed).** Backlog stub
  `.kanban/0-backlog/bugs/task-combinator-empty-array-error-types.md` exists, follows the
  mandatory bug-template shape, and pins the 3 offending sites by exact line:
  `TaskBuiltIns.cs:148` (`awaitAny([])`), `TaskBuiltIns.cs:156` (`awaitAny(non-Future
  element)`), `TaskBuiltIns.cs:279` (`race([])`). `git diff 478247b3..16b01f71 -- Stash.Stdlib/BuiltIns/TaskBuiltIns.cs`
  shows the 3 sites are **unchanged** — correctly NOT fixed in-unit (out-of-scope discipline
  preserved). **Confirmed.**

**Composition / regression scan.** The F01/F02/F05 edits all touched the §Async D5/Edit-4
region (L1564–1612). They compose coherently: §Async now reads `Process handle boundary
(D5)` (L1564) → `Socket handle task-affinity (D5 — enforcement pending)` (L1573) →
`Cancellation, timeout, and task status` (L1588) → `task.cancel(future) returns null. … [+
new sentence covering await/status/cancel]` (L1598). No earlier sealed clause is garbled, no
numbering or cross-reference is broken, and the new clauses preserve the prior section
structure. The 3 new conformance tests assert spec law (error.ErrorType / message-fragment)
without timing dependence — sound, non-flaky. `Category=Conformance` filter run: **124 passed
/ 0 failed.**

---

## F07 — [MINOR] coverage.md row #6 D5 note still reads the P6-narrowed state, opposite of post-F01 law

**Status:** fixed
**Fixed in:** 15a8bd51
**Files:** `.kanban/milestones/language-standard/coverage.md:55`
**Phase:** cross-phase (F04 resolve loop carry-over)
**Commit:** -

### Observation

The §Async row in the milestone scoreboard (`coverage.md:55`, row #6 "Functions, Closures,
Async") still carries the P6-era D5 note verbatim:

> **D5 note (P6):** D5 cross-task handle enforcement narrowed to process handles only; socket
> task-affinity is a documented impl limitation (not enforced at runtime — spec prose updated
> to reflect gap; architect-ratification pending).

The F01 fix (`b8c6ad11`, ratified by the user-locked Decision Log entry in `brief.md:380`)
**reverted** that P6 narrowing — D5 is no longer "narrowed to process handles only." After
F01, D5's intent covers sockets (per the rewritten spec paragraph at
`docs/Stash — Language Specification.md:1573–1586`), and the gap is a *tracked enforcement
backlog item*, not a "documented impl limitation" / narrowed clause.

The scoreboard row therefore documents the OPPOSITE of what the spec now says. The trailing
"architect-ratification pending" caveat is also stale — the user ruling on 2026-06-06 in the
Decision Log IS the ratification.

### Why this matters

The milestone scoreboard is the architect's source-of-truth for "what state is each spec
section in?" — a future unit (e.g. `language-standard-functions`, which inherits row #6) will
read this note and mis-spec the D5 surface, either by re-narrowing what F01 just un-narrowed,
or by skipping the socket-enforcement backlog handoff because the row implies the narrowing
is final law. This is the exact drift-via-stale-pointer failure mode the milestone scoreboard
exists to prevent.

The fix is a one-line edit to the same row, no spec change. F04 reframed the discriminator
paragraph at the top of `coverage.md`, but the row's own note text was not updated in the
same resolve — it slipped through. Filed as `MINOR` because the spec itself is correct and
the brief Decision Log captures the ratified state; the scoreboard staleness is a
documentation-pointer issue, not a law-level defect.

### Suggested fix

Edit `.kanban/milestones/language-standard/coverage.md:55` D5 note to reflect the
F01-ratified state. Suggested replacement (preserving the column structure — single
sentence inside the existing pipe-cell):

> **D5 socket gap (post-F01):** D5's cross-task handle boundary is intended to cover socket
> handles, but only Process enforcement is built today. Spec prose (`Socket handle
> task-affinity (D5 — enforcement pending)`) calls this an unsupported-and-unsafe tracked
> gap; the enforcement work is filed at
> `.kanban/0-backlog/bugs/tcp-socket-handle-task-boundary-enforcement.md` (user-ruled
> 2026-06-06; brief Decision Log entry).

### Verify

```
grep -n "narrowed to process\|architect-ratification pending" .kanban/milestones/language-standard/coverage.md
# Expect: zero matches (the stale phrases removed).

grep -n "tcp-socket-handle-task-boundary-enforcement" .kanban/milestones/language-standard/coverage.md
# Expect: row #6 references the backlog stub.
```
