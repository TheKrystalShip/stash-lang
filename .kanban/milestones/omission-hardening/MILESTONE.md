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

- **Next up:** `stdlib-omission-hardening` — `Stash.Stdlib` is branded "single source of truth
  for all namespaces"; candidate findings seeded in `stdlib-candidate-findings.md`. Verify the
  scope-sizer first (registration looks already-Construct via source-gen — if true, the project
  is small).
- Later (rough): `Stash.Core` (confirmation pass — cited as reference but never actually
  classified; don't give it a free pass) · `Stash.Analysis` · `Stash.Tpl` · `Stash.Scheduler` ·
  `Stash.Bytecode` (101 opcodes — is handler/disassembly/verify coverage compiler-forced?) ·
  `Stash.Lsp`+`Stash.Dap` (paired) · `Stash.Cli` · small-tooling batch
  (`Check`+`Format`+`Docs`+`Playground`+`Tap`).
- **Excluded:** `Stash.Registry` is in-flight via `registry-authz-filter` (its
  `AuthzDispatchCoverageMetaTests` + imperative-exemption pin *is* this pass for the authz
  surface — don't double-spec). `Stash.Tests` is out of scope — it *is* the Detect layer.

### Decisions & learnings (append as you go)

| Date | Decision / learning | Why it changed the plan |
| --- | --- | --- |
| 2026-05-31 | Milestone created; ordering is layer-up, treatment scales to project size. | Codebase too large for one pass; rolling-wave per project. |

### Open questions

- Is built-in registration truly already Construct (source-generated)? Confirming this sizes
  the whole Stdlib unit. (Verify in `stdlib-omission-hardening` phase 1.)

---

## Ledger (DERIVED — do not edit by hand)

Completion is computed from feature dirs, not asserted here. Each child feature's `plan.yaml`
carries `milestone: omission-hardening`; the status script groups them:

```bash
python3 scripts/checkpoint/milestone-status.py omission-hardening
```

- **Done** = features in `.kanban/4-done/` tagged with this milestone.
- **In-flight** = features in `.kanban/2-in-progress/` tagged with this milestone.

No child unit has been specced yet. If anything written elsewhere in this doc disagrees with
the command above, the command wins.
