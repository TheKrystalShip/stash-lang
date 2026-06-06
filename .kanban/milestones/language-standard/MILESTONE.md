# Milestone: Language Standard — Seal the Specification

> **Status:** Active
> **Created:** 2026-06-06
> **Slug:** language-standard

A milestone is a **living charter for long-term work that spans many feature cycles**. It holds the
*destination* and the *current thinking*; the road is built as you go (rolling-wave). The **Charter**
below is hand-maintained; the **Ledger** is derived from `4-done/` via
`scripts/checkpoint/checkpoint.stash milestone-status` — run `/milestone language-standard`.

This milestone is the standing program behind the **`AGENTS.md` → *The Specification is the Law***
doctrine. It does not add language features; it makes the existing language *honest* — every
observable behavior written down as normative prose, every normative claim proven by a conformance
test.

---

## Charter (living — edit freely)

### Vision

`docs/Stash — Language Specification.md` must become an **airtight, normative standard** — the C++-ISO
of Stash. A competent reader should be able to predict the language's behavior in any situation the
spec addresses, and know which situations it deliberately leaves open. Today the spec is good but not
sealed: real behavior lives only in the code and the tests (the `async` cooperative-cancellation /
`task.status` lifecycle / unobserved-task reporting gap that motivated this milestone is the
archetype). That inverts the intended dependency — the tests become the de-facto law and the spec a
stale shadow. We flip it back: the spec is the law; the code and tests exist to honor and prove it.

The work is a deliberate, subsystem-by-subsystem **audit and seal**: read each area's spec section
against the implementation and the test suite, find the gaps and the *unwritten rules we follow but
never wrote down*, write them into the spec (positive behavior **and** negative space), back each
normative clause with a `Category=Conformance` test, and — *seal first, then bend* — fix any code that
violates the now-written law.

### Definition of Done (finite & checkable)

Every observable language/runtime subsystem is **sealed**:

1. Its spec section states the full observable behavior — happy path, errors, edges, lifecycle,
   ordering, concurrency, isolation, resource cleanup, exit semantics — **and** its negative space
   (what is not guaranteed / dropped / unspecified).
2. Each normative clause in that section is backed by at least one `Category=Conformance` test under
   `Stash.Tests/Conformance/<Area>/` that proves the implementation honors it (positive and negative).
3. No *known* undocumented observable behavior remains in that area (the audit pass surfaced and
   closed them, or filed an explicit backlog stub with rationale).
4. Any implementation that violated the sealed law has been corrected (or the law corrected, on
   purpose, if the code was right).

The milestone converges when every area in *Rough order* below is sealed to this bar. New language
features added after an area is sealed inherit the spec-first + conformance obligations
(`.claude/language-changes.md`), so the standard stays sealed rather than re-drifting.

### Unit Definition of Done

One **unit** = one subsystem audit-and-seal pass (a child `/spec` feature tagged
`milestone: language-standard`). It is done when, for that subsystem:

- the spec section is read against the implementation **and** the existing tests;
- every gap and unwritten rule is written into `docs/Stash — Language Specification.md`, including
  negative space, with the spec edits pinned by phase `done_when`;
- each normative clause has a `Category=Conformance` test under `Stash.Tests/Conformance/<Area>/`,
  organized by spec section and citing the clause it proves;
- any code that contradicted the sealed law is fixed (seal-first-then-bend), or a deliberate,
  documented backlog stub records a chosen deviation;
- full `dotnet test` is green and the conformance filter (`Category=Conformance`) passes.

### Rough order & next up

A revisable sketch, **not** a contract. Detail only the next unit; redraw the rest as each pass
teaches us where the real gaps are.

- **Next up:** `language-standard-async` — finish sealing **§Async**. The prose is now substantially
  complete (the two-systems model, D1–D11, and the just-landed cancellation + unobserved-task
  clauses), but it has **no dedicated `Category=Conformance` suite** under `Conformance/Async/` and no
  final gap-audit. This unit converts the existing behavior tests' intent into clause-citing
  conformance tests, sweeps §Async for remaining unwritten rules (e.g. `task.delay`/`task.resolve`
  semantics, `await` on an already-cancelled future, `arr.parForEach` return value, isolation cycle
  errors), and seals it. It also establishes the `Conformance/` directory + the clause-citing pattern
  the later units reuse.
- Later (rough, reorder freely as audits teach us): error model & exceptions (`throw`/`try`/`catch`/
  `defer`/`elevate`/`retry`, the `[StashError]` hierarchy, error propagation) · types, truthiness &
  coercion · scoping, closures & upvalue capture · modules & imports (resolution, circular-import,
  dynamic paths) · control flow & pattern matching · operators, literals & precedence · collections &
  iteration protocols · strings, interpolation & templates · the command/pipe/shell surface ·
  numbers (int/float, overflow, division) · structs/enums/interfaces & dispatch.

### Decisions & learnings (append as you go)

| Date | Decision / learning | Why it changed the plan |
| --- | --- | --- |
| 2026-06-06 | Milestone created after the `async-correctness` feature shipped cancellation + unobserved-task behavior **working and tested but undocumented**; the spec gap was caught only by a manual read, not by any gate. | Established the *Specification is the Law* doctrine + spec-first hooks (architect `Specification Delta`, reviewer Review-Priority-2, `language-changes.md` reorder, `Category=Conformance` convention). The spec can't be a Construct, so the standing audit milestone is how we seal it. |
| 2026-06-06 | Locked: **formal milestone** (subsystem-by-subsystem passes) over incremental-on-touch; **marked + organized conformance tests** (`Category=Conformance` + `Conformance/<Area>/`) over an unmarked discipline. | User rulings — the stronger, more durable options; matches how the project runs cross-cutting programs and its `Category=Gotcha` precedent. |

### Open questions

- Should there be a single roll-up conformance "spec coverage" report (e.g. a doc listing each spec
  section → its conformance test file), to make remaining gaps visible at a glance — or is the
  `Conformance/<Area>/` directory structure mirroring the spec enough?
- Granularity of a unit: one spec `##` section per `/spec`, or batch closely-related sections (e.g.
  all of §Async, or all of the error model) into one pass? Decide per area by size.
- Do any *generated* surfaces (the stdlib reference) carry normative claims that belong in the
  hand-written spec instead? Audit the boundary as part of the relevant area passes.

---
