---
name: checkpoint-workflow
description: Greenfield multi-phase feature workflow for Claude Code — flat agents, filesystem-mediated handoffs, user-as-scheduler. Replaces the old single-orchestrator pattern.
skill-for: architect, implementer, reviewer, resolver
---

# Checkpoint Workflow

A flat, interruption-safe workflow for shipping multi-phase features under Claude Code's token-budget / rate-limit model. **No long-running orchestrator.** Each user turn does one bounded job; state lives on disk between turns.

## Core principles

1. **Filesystem is the bus.** Every handoff is a file in `.kanban/2-in-progress/<slug>/`. No agent depends on conversation memory from a prior turn.
2. **One agent per turn.** No agent wraps another agent. Subagents (`Explore`) are fine for research within a turn, but lifecycle steps (design / implement / review / resolve) are flat.
3. **Phases = commits.** Each green phase is one git commit with a structured message. `git log` is the authoritative timeline.
4. **Scripts replace agents wherever possible.** Verification, status, advancement, promotion — all deterministic, no LLM calls.
5. **The user is the scheduler.** Between agent turns, the user types a slash command to load the next step. The agent never tries to drive the loop.
6. **Context is computed once, reused many times.** The architect's `context.md` is consumed by every implementer turn instead of being re-derived.

## Lifecycle

```
turn 1                   turn 2..N                turn N+1               turn N+2..M             turn M+1
┌──────────┐  /spec   ┌──────────────┐ /next-  ┌────────────────┐ /feat-┌──────────────┐ /resolve ┌──────────┐ /done
│ Architect│ ───────► │ Implementer  │ phase   │ Implementer ...│ ureviw│  Reviewer    │ ──────►  │ Resolver │ ────► promote
│ (Opus)   │          │ (Sonnet) ×1  │ ──────► │ (Sonnet) ×1    │ ────► │  (Opus) ×1   │          │ (Sonnet) │       (script)
└──────────┘          └──────────────┘         └────────────────┘       └──────────────┘          └──────────┘
   │                       │                       │                         │                        │
   ▼                       ▼                       ▼                         ▼                        ▼
spec.md             commit + checkpoint     commit + checkpoint        review.md +                commit + review.md
plan.yaml           advance                 advance                    open findings              status: fixed
context.md
```

## Artifacts

For each active feature, exactly one directory `.kanban/2-in-progress/<slug>/`:

| File | Written by | Read by |
| --- | --- | --- |
| `spec.md` | Architect | Implementer, Reviewer, Resolver |
| `plan.yaml` | Architect | Every slash command + every agent |
| `context.md` | Architect | Implementer (most), Reviewer |
| `checkpoint.yaml` | Scripts (`advance-checkpoint.py`) | Every slash command |
| `review.md` | Reviewer | Resolver, `/done` |
| `notes/<phase-id>.md` | Architect or Implementer | Implementer of that phase |

## The five slash commands

| Command | What it does | Agent invoked |
| --- | --- | --- |
| `/spec [topic]` | Opens an Architect session for designing a new feature. Produces `spec.md`, `plan.yaml`, `context.md` and bootstraps `.kanban/2-in-progress/<slug>/`. | `architect` |
| `/next-phase [slug]` | Finds the next pending phase whose deps are done, builds a focused brief, dispatches one Implementer turn. | `implementer` |
| `/feature-review [slug]` | All phases done? Bundles spec + diff, dispatches one Reviewer turn that writes `review.md`. | `reviewer` |
| `/resolve [slug] <Fxx>` | Reads one finding, dispatches one Resolver turn that fixes it. | `resolver` |
| `/done [slug]` | Runs `final_verify`, refuses if any phase isn't `done` or findings are open, then promotes the feature to `.kanban/4-done/`. No agent invocation. | none |
| `/resume [slug]` | Diagnostic-only: prints checkpoint status, git state, last verify result, and suggests the next command. No agent invocation. | none |

If `[slug]` is omitted, the command infers from the single feature in `.kanban/2-in-progress/`. If there are zero or more than one, it errors.

## Scripts

All under `scripts/checkpoint/`:

| Script | Purpose |
| --- | --- |
| `bootstrap-feature.sh <slug> [title]` | Creates the in-progress dir from templates with slug substituted |
| `validate-spec.py <slug?>` | Structural validation of `plan.yaml` + auto-heals missing checkpoint entries |
| `next-phase.py <slug?>` | Prints next-pending phase as YAML with a `_brief` block |
| `verify-phase.sh <slug> <phase-id>` | Runs verify commands AND checks no out-of-scope edits |
| `advance-checkpoint.py <slug> <phase-id> <status>` | Atomic state transitions (pending→in_progress→done/failed) |
| `status.py <slug?>` | Compact text status (used by `/resume`) |
| `promote-done.sh <slug>` | Final acceptance — runs `final_verify`, moves to `4-done/` |

## Phase sizing

**Target:** 30k–60k tokens of total work per phase (implementer reads + edits + verify output).

| Token budget | What it means |
| --- | --- |
| < 5k | Probably too small — combine with neighbor |
| 30k–60k | Sweet spot — one Sonnet turn, comfortable margin |
| 60k–80k | Acceptable, plan for retry on rate-limit interruption |
| > 80k | **Split.** `validate-spec.py` will error. |

Heuristic: *a phase is implementable by one Implementer turn without spawning explorers.* If the implementer must explore, the brief was incomplete or the phase is too big.

## Phase commit format

Every successful phase commits with:

```
feat(<slug>): phase <id> — <phase title>

Checkpoint: <id>/<total> complete. Next: <next-id or 'final review'>.
Spec: .kanban/2-in-progress/<slug>/spec.md

<Optional: 1-3 lines on what was done.>
```

This makes `git log --grep "feat(<slug>)"` an instant per-feature commit list, and `/feature-review` derives the diff range from these commits automatically.

## Interruption safety

Three interruption scenarios, all safe:

1. **Between phases (clean tree).** `/next-phase` just picks the next pending phase from `checkpoint.yaml`. No state recovery needed.
2. **Mid-phase (dirty tree, checkpoint says `in_progress`).** `/resume` detects this. It re-runs the phase's `verify`. If green → "commit and advance?". If red → dispatch a fresh Implementer with the diff summary added to its brief.
3. **Cold-start (new session, hours later).** Same as the above. The agent's conversation has no memory; it doesn't matter. `repo.md` + `plan.yaml` + `checkpoint.yaml` + git tree are sufficient to bootstrap.

## What this replaces

| Old pattern | Why it doesn't fit Claude Code | New pattern |
| --- | --- | --- |
| Orchestrator that loops over phases in one session | Burns context with implementer summaries returning into parent. Rate-limits mid-loop. | One slash command per phase. User holds the loop. |
| Implementer freely exploring the codebase | Cheap in Copilot's request model, expensive in token model | Implementer ONLY reads `phase.files` + `context.md`. Architect pre-computes exploration. |
| Reviewer that fixes issues itself by spawning implementers | Deep nested agent chain returns summaries up the tree | Reviewer writes findings only. `/resolve` dispatches one Resolver per finding. |
| Tracking state in `repo.md` prose | Not machine-readable; drifts; can't cold-start from it | `plan.yaml` + `checkpoint.yaml` in the feature dir |

## When NOT to use this workflow

Small / single-turn work — a bug fix, a single test, a one-file refactor — does **not** need a spec or phases. Dispatch the `implementer` agent directly with file paths and a description. The checkpoint workflow exists for multi-phase features; using it for a one-line fix is overhead.

Heuristic: if the work would have been one phase under this workflow, just do that one phase as a normal commit on `main`. No kanban dir needed.
