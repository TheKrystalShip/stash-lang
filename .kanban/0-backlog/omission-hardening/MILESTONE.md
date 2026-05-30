# Milestone: Omission Hardening Across the Codebase

> **Status:** Active program (backlog). Spans many features; never a single `/spec`.
> **Created:** 2026-05-31
> **Doctrine:** `.claude/agents/architect.md` → "Designing Out Cross-Cutting Omission"

## Vision

Apply one philosophy to every project in the codebase, one project at a time:

> A cross-cutting invariant — every participant in a shared concern actually participates — must be enforced by the strongest available level. Prefer **Construct** (a forgotten participant fails to *compile* or fails *closed*) over **Detect** (a meta-test enumerates participants after the fact, and can drift) over **Instruct** (prose/convention, which breaks silently).

The motivating failure was the registry authorization pipeline: one authorizer was built as the single decision point, controllers were wired to it phase by phase, and an entire controller silently never called it — invisible to review because an *omission* is a missing line, not a wrong one. `Stash.Core` is the counter-example: "every visitor handles every AST node" rides the type system, so omission is a compile error. This program asks, for each project: *which invariants are Construct already, and which Detect/Instruct ones can be promoted?*

## Per-project Definition of Done (finite & checkable)

A project is "hardened" when **every cross-cutting concern in it** is:

1. **Classified** — Construct / Detect / Instruct, with the governing file or meta-test named; and
2. **Resolved** — either *promoted to Construct*, or *recorded with an explicit written justification* for remaining Detect/Instruct (e.g. "the type system cannot express this; the meta-test asserts the load-bearing property and ships a fail-path self-test").

This bounds the work: the **audit is the first deliverable**, not an open-ended "make it better." A project is not done because it *feels* hardened — it's done when every concern has a row and a disposition.

## Status is DERIVED, not asserted

Applying our own doctrine to this roadmap: a hand-maintained status table is an Instruct-level artifact that drifts and starts lying. **The authority on "is project X done" is the existence of `.kanban/4-done/<project>-omission-hardening/`** (or the concern being absorbed by another completed feature). The table below is a *narrative convenience for ordering and notes* — if it ever disagrees with `4-done/`, `4-done/` wins.

## How each project is tackled

One project (or one batch) per run — the codebase is too large for a single pass.

1. Run `/spec <project>-omission-hardening` (or a batch slug). The architect's **phase 1 is always "verify & classify"**: confirm the cross-cutting concerns and their current level *by reading the code*, not by trusting a prior audit. Where a candidate-findings file exists (e.g. Stdlib), it is treated as **unverified hypotheses to reproduce**, not facts.
2. Later phases promote the high-value Detect/Instruct concerns to Construct, and record justifications for the rest.
3. Each project's `brief.md` links back to this milestone. On `/done`, its presence in `4-done/` is what marks the milestone row complete.

## Project order (layer-up — foundation first)

Ordering follows `AGENTS.md` "Project Layering." One-line risk/value note each; order can flex on those notes.

| # | Project | Layer | Treatment | Note |
| - | ------- | ----- | --------- | ---- |
| 0 | `Stash.Core` | 0 | **Confirmation pass** (light) | Cited as the reference (compiler-forced visitors). Never actually classified — give it a confirmation pass, not a free pass. "Assumed done" is the unverified-trust move this program kills. |
| 1 | `Stash.Stdlib` | 1 | **Full spec** — NEXT | Branded "single source of truth for all namespaces." Candidate findings seeded (`stdlib-candidate-findings.md`). Key hypothesis: registration is *already* Construct via source-gen — confirm first, it sizes the whole project. |
| 2 | `Stash.Analysis` | 1 | Full spec | Rules, resolvers, visitors. Likely exhaustiveness + rule-registration concerns (does adding a diagnostic rule auto-register, or must it be remembered?). |
| 3 | `Stash.Tpl` | 1 | Assess (likely small spec) | ~1.9k LOC. Classify before deciding spec size. |
| 4 | `Stash.Scheduler` | 1 | Assess (likely small spec) | ~4k LOC. |
| 5 | `Stash.Bytecode` | 2 | Full spec | 101 opcodes + register VM. Classic Construct opportunity: is every opcode's handler/disassembly/verification coverage compiler-forced, or a switch that can silently miss a case? |
| 6 | `Stash.Lsp` + `Stash.Dap` | 3 | Spec (paired) | Handler/capability registration is the cross-cutting surface; the two share shape. |
| 7 | `Stash.Cli` | 3 | Assess | Command/flag dispatch. |
| 8 | `Stash.Check` + `Stash.Format` + `Stash.Docs` + `Stash.Playground` + `Stash.Tap` | 3 | **Batch one pass** | Small projects (180–2.3k LOC). Batching avoids mandating full-spec ceremony for trivial surfaces. |
| — | `Stash.Registry` | 3 | **In-flight — do NOT double-spec** | `registry-authz-filter` (in `2-in-progress/`) is already its omission-hardening pass: `RegistryAuthorizeFilter` + `AuthzDispatchCoverageMetaTests` + imperative-exemption pin. When it lands in `4-done/`, treat this row as covered for the authz surface; re-scan only for non-authz concerns if warranted. |
| — | `Stash.Tests` | n/a | Out of scope | The test project *is* the Detect layer; it is not itself hardened. |

## Anti-goals

- Do **not** create per-project stub files ahead of time — that is premature Instruct-ceremony. A project gets artifacts when it's picked up via `/spec`.
- Do **not** promote Detect → Construct mechanically. A snapshot test whose value *is* the "conscious re-baseline decision" should stay Detect, with that justification recorded.
- Do **not** trust a prior audit (including the seeded Stdlib one) as fact — phase 1 reproduces it.
