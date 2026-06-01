---
name: checkpoint-workflow
description: Multi-phase feature workflow for Claude Code — RFC-style brief, small machine plan, filesystem-mediated handoffs, deterministic scripts.
skill-for: architect, implementer, reviewer, resolver
---

# Checkpoint Workflow

This workflow ships multi-phase Stash features across stateless agent turns.

Core rule:

**`brief.md` tells the story. `plan.yaml` tells scripts what to do. `checkpoint.yaml` records state.**

## Artifacts

Each active feature lives in `.kanban/2-in-progress/<slug>/`:

| File | Purpose |
| --- | --- |
| `brief.md` | RFC-style human source of truth: summary, motivation, design, acceptance criteria, decisions. |
| `plan.yaml` | Small machine-readable phase plan: files, verify commands, deps, `done_when`. |
| `checkpoint.yaml` | Live state, updated only by scripts during normal flow. |
| `review.md` | Structured review findings, created after implementation. |

Older feature directories may contain `spec.md`, `context.md`, or `notes/`. New features should prefer the smaller shape above.

## Commands

| Command | What it does |
| --- | --- |
| `/spec [topic]` | Create or revise `brief.md`, `plan.yaml`, and `checkpoint.yaml`. |
| `/next-phase [slug] [count]` | Dispatch one Implementer turn for the next ready phase(s). |
| `/feature-review [slug]` | Dispatch one Reviewer turn after all phases are done. |
| `/resolve [slug] <Fxx> [Fyy...]` | Dispatch one Resolver turn for exactly the selected finding(s). |
| `/done [slug]` | Run final verify and promote to `.kanban/4-done/`. |
| `/resume [slug]` | Print deterministic status and next action. |

## Phase Rules

Every phase in `plan.yaml` must have:

- `id`
- `title`
- `deps`
- `files`
- `verify`
- `done_when`

`done_when` must describe observable behavior. It is the lightweight guard against agents completing only local mechanics while missing the end-to-end feature.

Good:

```yaml
done_when:
  - CLI file-loaded modules reject imports of private names.
```

Weak:

```yaml
done_when:
  - Export helper method exists.
```

The phase plan is expected to be mostly right, not perfect. Implementers may make bounded corrections when reality differs from the initial discovery: stale paths, moved functions, slightly wrong signatures, or a verify filter that names the wrong test. They must keep the correction within the current phase's intent, update `plan.yaml` if needed, and report the deviation.

## Scripts

Use scripts for deterministic work:

| Script | Purpose |
| --- | --- |
| `bootstrap-feature.sh <slug> [title]` | Create feature files from templates. |
| `validate-spec.stash [slug]` | Validate plan/checkpoint shape and heal missing checkpoint phase entries. |
| `next-phase.stash [slug] [count]` | Print the next ready phase or ready phase batch as YAML. |
| `verify-phase.sh <slug> <phase-id>` | Print `done_when`, enforce the current plan's file scope, and run verify commands. |
| `advance-checkpoint.stash <slug> <phase-id> <status>` | Advance state legally. |
| `status.stash [slug]` | Print compact current state. |
| `promote-done.sh <slug>` | Run final verification and move feature to done. |

## Lifecycle

1. `/spec <topic>` writes the RFC-style brief and phase plan.
2. `/next-phase` implements one phase by default, or an explicit small batch when the user passes a count.
3. Repeat `/next-phase` until all phases are done.
4. `/feature-review` compares the diff to `brief.md` and writes `review.md`.
5. `/resolve <Fxx> [Fyy...]` fixes an explicit selected finding or small selected batch.
6. `/done` runs final verification and archives the feature.

## When Not To Use This

Do not use this workflow for single-turn work: tiny bug fixes, one-file refactors, small tests, docs typos, or changes that would naturally be one phase.
