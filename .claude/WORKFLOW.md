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
| `/next-phase [slug] [count]` | Implement the next ready phase(s) in one implementer turn. |
| `/feature-review [slug]` | Review the completed feature against `brief.md`. |
| `/resolve [slug] <Fxx> [Fyy...]` | Fix exactly the selected review finding(s). |
| `/done [slug]` | Run final verification and move the feature to `.kanban/4-done/`. |
| `/resume [slug]` | Print current state and recommend the next command. |
| `/milestone [slug]` | Print a long-term milestone's derived ledger + the next unit to spec. (See "Milestones" below.) |

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
| `bootstrap-feature.stash <slug> [title]` | Creates the feature directory from templates. |
| `validate-spec.stash [slug]` | Validates `plan.yaml` and syncs missing checkpoint phase entries. |
| `next-phase.stash [slug] [count]` | Prints the next ready phase or ready phase batch as YAML. |
| `verify-phase.stash <slug> <phase-id>` | Prints `done_when`, checks the current plan's scope, and runs verify commands. |
| `advance-checkpoint.stash <slug> <phase-id> <status>` | Performs legal state transitions. |
| `status.stash [slug]` | Prints a compact status report (incl. sibling feature worktrees). |
| `promote-done.stash <slug>` | Runs final verification and moves the feature to done. |
| `worktree-start.stash <slug> [base]` | Creates `../stash-<slug>` on a fresh `feature/<slug>` branch (parallel work). |
| `check-parallel-safety.stash [slug]` | Warns when a feature shares a subsystem with an in-flight sibling worktree. |
| `worktree-finish.stash <slug>` | Merges the branch `--no-ff`, re-verifies on `main`, removes the worktree if green. |

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
stash scripts/checkpoint/validate-spec.stash <slug>
```

passes.

### 2. Implement Phase(s)

Run:

```text
/next-phase [slug]
/next-phase [slug] 3
```

The implementer receives the current phase or selected phase batch, the path to `brief.md`, planned files, verify commands, and `done_when`.

The implementer must:

- start from the planned files and keep any deviations small, documented, and within the phase intent
- run `stash scripts/checkpoint/verify-phase.stash <slug> <phase-id>` for each selected phase
- commit each phase separately
- advance the checkpoint after each phase

Batching affects one agent turn, not phase granularity. A three-phase batch should normally produce three phase commits.

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

## Milestones — long-term work

Some work is too large for one `/spec`→`/done`: a whole-codebase refactor, a multi-project initiative, a sequenced roadmap. That's a **milestone** — a *living charter* spanning many feature cycles, where you build the road as you go (rolling-wave: spec the next unit, sketch the rest, learn, spec the next).

A milestone is a **container, not a work item** — it executes nothing. The work still flows through the normal per-unit `/spec`→`/next-phase`→`/feature-review`→`/done` lifecycle. The milestone only *sequences and aggregates* those units. So there are no milestone agents, no milestone phases, no milestone review — just a doc and a derived view.

### Two layers, two rules

A milestone at `.kanban/milestones/<slug>/MILESTONE.md` (from `_templates/milestone-template.md`) has:

- A **Charter** — hand-maintained, evolves freely, the authority on the *future*: vision, a *finite & checkable* Definition of Done, the next unit or two, and an append-only log of decisions/learnings. The route is emergent; the destination is fixed.
- A **Ledger** — *derived, never hand-written*, the authority on the *past*: which units are done. A living doc drifts, and the first thing to rot is a hand-kept progress claim — so completion is **computed**, not asserted. The doc literally leaves that section empty.

This is the workflow eating its own dog food: per the Construct-over-Detect philosophy (`.claude/agents/architect.md`), "is unit N done" must be a fact that can't drift, so it's read from `4-done/`, not typed into a table.

### How a unit joins a milestone

When you `/spec` a unit of a milestone, give its `plan.yaml` a top-level `milestone: <slug>` tag (and its `brief.md` header a `Milestone: <slug>` line). That tag is the only wiring — the status script groups every tagged feature in `2-in-progress/` and `4-done/` under the milestone.

### Seeing where you are

```text
/milestone [slug]
```

Prints the derived ledger (done / in-flight units) and points at the charter's "Next up". It reports *facts*; the charter holds *intent*. If the two ever disagree, the ledger wins.

A milestone is **done** when its charter's Definition of Done is met and the ledger shows the work landed — then mark the charter `Status: complete`.

---

## Running Features in Parallel

The default model is **one feature on `main` at a time**. When two features genuinely need to progress concurrently, isolate each in its own git worktree on its own branch — do **not** run two agents against the same working tree.

### Why a shared tree can't hold two features

Every workflow command (`/next-phase`, `/feature-review`, `/resolve`) refuses to run on a dirty tree — that refusal is how a turn knows the previous turn finished cleanly. On a shared tree, feature A's in-progress edits *are* a dirty tree to feature B, so B's command refuses through no fault of its own. The two features cannot both be mid-turn. Separately, two agents editing the same file in the same window produce no git conflict at all — one set of edits silently wins and the other is lost, with nothing in history to show it happened. Worktrees convert that invisible loss into a visible, resolvable merge conflict.

### What may run in parallel

Parallelize **across disjoint subsystems**; serialize **within one**.

| Pair | Verdict | Reason |
| --- | --- | --- |
| language feature + registry feature | parallel-safe | near-zero file overlap |
| language feature + stdlib addition | usually parallel-safe | overlap only if the stdlib change adds syntax |
| two language features | serialize | both touch `TokenType.cs`, `Parser.cs`, and all six visitors — guaranteed integration conflict |
| two registry features | serialize | shared registry surface churns the same files |

A language feature is cross-cutting by construction (the six-visitor rule), so two of them in flight will collide on the same fixed set of hot files. The integration tax then eats the parallelism gain.

### Setup — worktree per feature

Deterministic steps are scripted so no agent has to reconstruct them. From the main checkout:

```bash
stash scripts/checkpoint/worktree-start.stash <slug>
```

This creates the worktree at `../stash-<slug>` on a fresh `feature/<slug>` branch (based on the committed `main` ref, so a dirty primary tree doesn't leak in), refusing on any naming collision. Then run the **entire** lifecycle inside that worktree — including `/spec`. Creating the worktree *first* means the feature's `.kanban/2-in-progress/<slug>/` directory, every phase commit, and `/done`'s promotion to `.kanban/4-done/` all live on the branch and arrive on `main` as one coherent unit. The review range stays clean by construction:

```bash
git log main..feature/<slug>   # exactly this feature's commits, no cross-feature noise
```

Once `/spec` has written `plan.yaml`, check the feature won't collide with anything already in flight:

```bash
stash scripts/checkpoint/check-parallel-safety.stash <slug>
```

It reduces every feature's file-globs to top-level subsystems and warns (non-blocking, exit 3) when this feature shares one with an in-flight sibling worktree — the signal that the two branches will fight over the same hot files. `status.stash` (and so `/resume`) also lists sibling feature worktrees so you always know what else is open.

### Integration — `merge --no-ff` per feature

When a feature is green on its branch and `/done` has promoted it, integrate from the main checkout:

```bash
stash scripts/checkpoint/worktree-finish.stash <slug>
```

The script merges `feature/<slug>` with `--no-ff` (one labeled boundary commit, `chore(<slug>): …` commits intact), **re-runs `final_verify` against the merged `main`**, and removes the worktree + branch **only if that verify is green**. It refuses unless run from `main` on a clean tree with the feature already promoted. The three rules it encodes, stated plainly:

1. **Last to merge pays the conflict cost** — merge the less cross-cutting feature first. On a merge conflict the script stops and leaves the tree for you to resolve, then re-run.
2. **Green-on-branch ≠ green-on-merged-`main`.** Two features can each pass `final_verify` in isolation yet break `main` once both land (a semantic conflict with no textual conflict). On a post-merge verify failure the script preserves the merge and tells you to fix forward — it does **not** abort and does **not** clean up.
3. **`.claude/repo.md` "Active Multi-Phase Work" is the one guaranteed collision** — both features append a pointer line. Resolve the trivial conflict, or only update `repo.md` at merge time.

---

## Filing Bugs Discovered During Work

When any agent or human encounters a bug **unrelated to the current feature** during workflow phases (architect design, implementer phase, reviewer review, resolver fix), they MUST:

1. Copy `.kanban/_templates/bug-template.md` to `.kanban/0-backlog/bugs/<descriptive-name>.md`.
2. Fill in every section. Sections that don't apply get a one-line "N/A — reason" entry, not deletion.
3. Do not attempt to fix the bug as part of the current work unless explicitly authorized — file the stub and continue with the current scope.

Reviewers in particular: out-of-scope bugs surfaced during `/feature-review` go in `0-backlog/bugs/` using the template, NOT in `review.md`. `review.md` is exclusively for findings about the feature under review.

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
