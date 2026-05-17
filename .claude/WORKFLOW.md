# Stash AI Workflow

This workflow lets stateless AI agents make incremental progress on larger Stash features without relying on conversation memory. The rule is simple:

**The feature story lives in `brief.md`. The scripts own state and verification.**

Use this workflow for multi-phase work only. Small fixes, one-file refactors, and isolated tests do not need a kanban directory.

---

## Core Model

Each active feature has one directory:

```text
.kanban/2-in-progress/<slug>/
├── brief.md          # RFC-style human source of truth
├── plan.yaml         # small machine-readable phase plan
├── checkpoint.yaml   # live state, updated by scripts
└── review.md         # created by /feature-review
```

Older feature directories may still contain `spec.md`, `context.md`, or `notes/`. New features should not need them.

### Why This Shape

The previous workflow split design context across `spec.md`, `context.md`, `plan.yaml`, phase notes, and checkpoint state. That was resumable, but too easy for agents to optimize locally and lose the feature-level path.

The simplified version keeps fewer sources of truth:

- `brief.md` explains the feature, its motivation, design, acceptance criteria, open questions, and decision log.
- `plan.yaml` contains only what scripts need: phases, file scope, verify commands, and `done_when`.
- `checkpoint.yaml` records state, attempts, commits, and review status.
- `review.md` records findings after implementation.

The plan should be trusted, but it is not sacred. Implementers may make small, documented corrections when the initial discovery is wrong: a path changed, a function is in a neighboring file, a signature differs, or a verify filter is stale. The boundary is the current phase's intent and `done_when`; anything larger goes back to the architect/user.

---

## Slash Commands

| Command | Job |
| --- | --- |
| `/spec <topic>` | Create or revise `brief.md`, `plan.yaml`, and `checkpoint.yaml`. |
| `/next-phase [slug]` | Implement exactly one pending phase. |
| `/feature-review [slug]` | Review the completed feature against `brief.md`. |
| `/resolve [slug] <Fxx> [Fyy...]` | Fix exactly the selected review finding(s). |
| `/done [slug]` | Run final verification and move the feature to `.kanban/4-done/`. |
| `/resume [slug]` | Print current state and recommend the next command. |

`[slug]` is optional only when exactly one feature is active.

---

## Artifact Details

### `brief.md`

`brief.md` is an RFC-style feature brief. It should be readable by a fresh agent without any chat history.

Required shape:

```markdown
# RFC: Feature Name

Status:
Owner:
Created:
Slug:

## Summary
## Motivation
## Goals
## Non-Goals
## Design
### Surface
### Semantics
### Implementation Path
## Acceptance Criteria
## Phases
## Open Questions
## Decision Log
```

The most important sections are `Implementation Path` and `Acceptance Criteria`. They keep the big picture visible across small phases.

Example implementation path:

```text
Parser recognizes export syntax -> analysis builds export set -> compiler attaches it to Chunk -> VM filters imported module environment -> CLI/DAP/engine paths use the same compile path.
```

### `plan.yaml`

`plan.yaml` is script input, not a second design document. It should be accurate enough to guide the implementer, while leaving room for small implementation-time corrections.

Every phase must declare:

- `id`
- `title`
- `deps`
- `files`
- `verify`
- `done_when`

Example:

```yaml
phases:
  - id: 1C
    title: Runtime integration
    deps: [1B]
    files:
      - Stash.Bytecode/**
      - Stash.Cli/**
      - Stash.Tests/**
    verify:
      - dotnet test Stash.Tests --filter "FullyQualifiedName~Export"
    done_when:
      - CLI file-loaded modules reject imports of private names.
      - The behavior is covered through the production module loader path.
```

`done_when` must name observable behavior, not just internal construction. Prefer “CLI rejects private imports from a real module file” over “VM helper exists.”

### `checkpoint.yaml`

`checkpoint.yaml` is state. Scripts update it; agents should not hand-edit it during normal flow.

### `review.md`

`review.md` is created by `/feature-review`. Each finding is one parseable H2 section:

```markdown
## F01 — [CRITICAL] short title

**Status:** open
**Files:** `path:line`
**Phase:** 1C
**Commit:** abc123
```

`/resolve` fixes exactly the selected finding(s). Use one ID for the safest path, or several explicit IDs when the findings are related or small enough to verify together.

---

## Scripts

All deterministic workflow operations live in `scripts/checkpoint/`:

| Script | Purpose |
| --- | --- |
| `bootstrap-feature.sh <slug> [title]` | Creates the feature directory from templates. |
| `validate-spec.py [slug]` | Validates `plan.yaml` and syncs missing checkpoint phase entries. |
| `next-phase.py [slug]` | Prints the next ready phase as YAML. |
| `verify-phase.sh <slug> <phase-id>` | Prints `done_when`, checks the current plan's scope, and runs verify commands. |
| `advance-checkpoint.py <slug> <phase-id> <status>` | Performs legal state transitions. |
| `status.py [slug]` | Prints a compact status report. |
| `promote-done.sh <slug>` | Runs final verification and moves the feature to done. |

The scripts should answer deterministic questions: what phase is next, what files are allowed, what commands prove it, and whether state is legal. They should not become a second planning language.

---

## Lifecycle

### 1. Design

Run:

```text
/spec <topic>
```

The architect creates:

- `brief.md`
- `plan.yaml`
- `checkpoint.yaml`

The feature is ready when:

```bash
python3 scripts/checkpoint/validate-spec.py <slug>
```

passes.

### 2. Implement One Phase

Run:

```text
/next-phase [slug]
```

The implementer receives the current phase, the path to `brief.md`, planned files, verify commands, and `done_when`.

The implementer must:

- start from the planned files and keep any deviations small, documented, and within the phase intent
- run `bash scripts/checkpoint/verify-phase.sh <slug> <phase-id>`
- commit the phase
- advance the checkpoint

### 3. Review

When all phases are done:

```text
/feature-review [slug]
```

The reviewer compares the diff against `brief.md`, especially `Acceptance Criteria` and phase `done_when` items.

### 4. Resolve Findings

Run one selected finding or an explicit small batch:

```text
/resolve [slug] F01
/resolve [slug] F02 F03 F04
```

The resolver fixes only the selected finding(s), verifies the union of their commands, commits once, and marks those findings fixed.

### 5. Finish

Run:

```text
/done [slug]
```

This runs `final_verify`, refuses open findings, promotes the directory to `.kanban/4-done/`, and leaves the feature archived.

---

## Recovery

Use `/resume [slug]` whenever you are unsure.

Common states:

- Clean tree, no current phase: run `/next-phase`.
- Dirty tree, current phase in progress: verify or continue that phase.
- All phases done, review not started: run `/feature-review`.
- Review has open findings: run `/resolve <slug> Fxx [Fyy...]`.

---

## Quality Bar

A feature brief is good enough when a fresh agent can answer:

- What user-visible behavior are we building?
- What are we explicitly not building?
- What is the end-to-end implementation path?
- What observable acceptance criteria prove the feature works?
- What phase is next, what files may it touch, and how is it verified?

If those answers require chat history, the brief is not ready.
