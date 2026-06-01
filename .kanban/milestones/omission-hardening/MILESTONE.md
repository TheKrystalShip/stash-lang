# Milestone: Omission Hardening Across the Codebase

> **Status:** Active
> **Created:** 2026-05-31
> **Slug:** omission-hardening

A living charter for applying one philosophy to every project, one project (or batch) at a
time. The route is emergent — we spec a project, analyze it, implement, review, learn, then
spec the next. The destination is fixed (below). Doctrine: `.claude/agents/architect.md` →
"Designing Out Cross-Cutting Omission". Run `/milestone omission-hardening` for the derived ledger.

---

## Charter (living — edit freely)

### Vision

Enforce cross-cutting invariants — *every participant in a shared concern actually
participates* — by the strongest available level, project by project:

> Prefer **Construct** (a forgotten participant fails to *compile* or fails *closed*) over
> **Detect** (a meta-test enumerates participants after the fact, and can drift) over
> **Instruct** (prose/convention, which breaks silently).

The motivating failure was the registry authorization pipeline: one authorizer was built as
the single decision point, controllers were wired to it phase by phase, and an entire
controller silently never called it — invisible to review because an *omission* is a missing
line, not a wrong one. `Stash.Core` is the counter-example: "every visitor handles every AST
node" rides the type system, so omission is a compile error. This program asks, per project:
*which invariants are Construct already, and which Detect/Instruct ones can be promoted?*

### Definition of Done (finite & checkable)

Every project in the codebase has had an omission-hardening pass landed in `4-done/` (or had
its concerns absorbed by another completed feature). The program converges — it is not "make
things better" forever.

### Unit Definition of Done

One project (one child `/spec`) is hardened when **every cross-cutting concern in it** is:

1. **Classified** — Construct / Detect / Instruct, with the governing file or meta-test named; and
2. **Resolved** — either *promoted to Construct*, or *recorded with an explicit written
   justification* for staying Detect/Instruct (e.g. "the type system can't express this; the
   meta-test asserts the load-bearing property and ships a fail-path self-test").

The **audit is the first deliverable** (phase 1 of each spec), and it must *verify* concerns
against the code — prior audits (including the seeded Stdlib findings) are unverified
hypotheses, not facts.

### Rough order & next up

Revisable sketch (layer-up from the foundation, per `AGENTS.md` layering), **not a contract** —
analyzing one project may split, merge, or surface units we didn't know existed. Treatment
scales to size: full specs for the big projects, a single batched pass for the tiny tooling ones.

- **Next up:** `stdlib-omission-hardening` — **specced & in-flight** in worktree
  `feature/stdlib-omission-hardening` (4 phases; scope-sizer confirmed — registration is already
  Construct, so the unit is small). **⏸ Milestone paused 2026-05-31** after proving the protocol
  end-to-end through spec. Resume by implementing P1: from the worktree run
  `/resume stdlib-omission-hardening`, or check state with `/milestone omission-hardening`.
- Later (survey-driven + adversarially verified — see `survey.md`): **`analysis`** (strongest —
  hand-list `RuleRegistry`, a new rule silently never fires; CONFIRMED) → **`bytecode`** (verifier
  skips multi-register-range bounds checks; REFINED/real) → **`lsp`+`dap`** (concrete small fixes;
  ceiling mostly Detect) → `tpl` / `cli` / `scheduler` (Tier C, confirmed-MED) →
  `keyword-vocabulary-sync` (LOW/cosmetic — small meta-test). Core / Format / Docs / Tap are
  confirmation-only.
- **Cross-project concerns** (surveying holistically surfaced these; a per-project spec would miss
  them): **visitor exhaustiveness** — the 6-visitor pattern has a *default-throw escape hatch* on 4
  export methods, so the "reference" isn't fully Construct (rides with `analysis`); symbol/value-kind
  →presentation (Lsp+Dap); keyword vocabulary (3 grammars). See `survey.md`.
- **Excluded:** `Stash.Registry` AUTHZ is hardened (PDP + `AuthzDispatchCoverageMetaTests`). Registry
  *non-authz* exception-mapping was a batch-1 finding **refuted** in verification (generic ASP.NET 500,
  not an omission) — dropped. `Stash.Tests` is out of scope — it *is* the Detect layer.

### Decisions & learnings (append as you go)

| Date | Decision / learning | Why it changed the plan |
| --- | --- | --- |
| 2026-05-31 | Milestone created; ordering is layer-up, treatment scales to project size. | Codebase too large for one pass; rolling-wave per project. |
| 2026-05-31 | Unit 1 (`stdlib`) specced in an isolated worktree. Scope-sizer **confirmed**: registration is already Construct (source-gen), so the unit is small (4 phases). | Worktree chosen because `readonly-modifier` is concurrently active; `check-parallel-safety` flagged a coarse `Stash.Stdlib` overlap but file-level dirs are disjoint (BuiltIns/ vs Models/+Registry/+Generators/) → low risk. |
| 2026-05-31 | Phase-1 verification found `Stability` is **one concern with two participants** (runtime `NamespaceMemberPayload.Invoke` + generator `BuildMember`); seed findings named only one. | The doctrine caught a gap in its own audit — exactly the omission shape the milestone exists to prevent. |
| 2026-05-31 | `milestone-status.py` now scans all git worktrees (deduped by slug). | Found by trying it out: a unit built in a worktree was invisible to `/milestone` run from main until merge. |
| 2026-05-31 | Step-1 survey done (10 parallel explorers) → `survey.md`. Order is now survey-driven, not layer-up: `bytecode` and `analysis` lead. Architects deliberately NOT parallelized. | Specs are not independent (rolling-wave): cross-project concerns (keyword vocab, kind→presentation, rule registry) only surfaced by surveying holistically; a per-project architect would miss them. All findings are unverified hypotheses — each spec's phase-1 verifies. |
| 2026-05-31 | Batch-2 ADVERSARIAL pass (skeptics tasked to refute) reconciled the survey. Net: 2 refutations (registry-nonauthz, OpcodeOperands), Dap overstated 8→3, keyword downgraded to cosmetic, +1 new finding (visitor default-throw escape hatch). Order re-led by `analysis`. | A real false-positive rate confirms "finders find work"; the adversarial inversion (default-refute, confirm-with-code) is the cheap antidote. Lead with the finding that survived a skeptic trying to kill it. |

### Open questions

- Is built-in registration truly already Construct (source-generated)? Confirming this sizes
  the whole Stdlib unit. (Verify in `stdlib-omission-hardening` phase 1.)

---

## Ledger (DERIVED — do not edit by hand)

Completion is computed from feature dirs, not asserted here. Each child feature's `plan.yaml`
carries `milestone: omission-hardening`; the status script groups them:

```bash
stash scripts/checkpoint/milestone-status.stash omission-hardening
```

- **Done** = features in `.kanban/4-done/` tagged with this milestone.
- **In-flight** = features in `.kanban/2-in-progress/` tagged with this milestone.

No child unit has been specced yet. If anything written elsewhere in this doc disagrees with
the command above, the command wins.
