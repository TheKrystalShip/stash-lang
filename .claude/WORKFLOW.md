# Stash AI Workflow — Tutorial

This is the canonical guide for working on Stash with the AI agents configured in this repository. Read it once end-to-end before you start your first multi-phase feature.

**TL;DR.** Multi-phase features run through six slash commands: `/spec`, `/next-phase`, `/feature-review`, `/resolve`, `/done`, `/resume`. Each command does one bounded job, leaves the codebase green, and commits to git. You can interrupt at any point and pick up cleanly later — even in a brand-new session.

---

## 1. Why this workflow exists

Claude Code is **token-budget / rate-limit** bound. A long-running "agent loop" — one that plans, implements, tests, and reviews a whole feature in a single session — predictably hits limits halfway through and leaves work unfinished. State held in the conversation evaporates with the session.

This workflow inverts the model:

| Property | Long-running orchestrator | This workflow |
| --- | --- | --- |
| State lives in | The conversation | Files on disk |
| One feature spans | One session | Many sessions, many turns |
| Interruption is | Bad | A non-event |
| The loop is held by | The agent | You (one slash command per step) |
| Cold-start a feature in a new session | Impossible | Trivial |

The key idea: **every handoff is a file**. No agent ever needs to remember what the last agent did — they read it from `.kanban/2-in-progress/<slug>/`.

---

## 2. Mental model: roles, artifacts, slash commands

### Roles

Four agent roles, each a single-turn specialist. None of them loop. None of them spawn each other.

| Role | Model | Job |
| --- | --- | --- |
| **Architect** | Opus | Design the feature. Produce `spec.md`, `plan.yaml`, `context.md`. |
| **Implementer** | Sonnet | Implement *one phase*. Commit. Done. |
| **Reviewer** | Opus | Compare spec to implementation. Produce `review.md`. Never fixes anything. |
| **Resolver** | Sonnet | Fix *one finding* from `review.md`. Commit. Done. |

There is also an `explore` agent (Haiku) used only as a research subagent — you never invoke it directly.

### Artifacts

For each in-flight feature, exactly one directory at `.kanban/2-in-progress/<slug>/`:

```
.kanban/2-in-progress/streaming-pipes/
├── spec.md           ← human-readable design document
├── plan.yaml         ← machine-readable phase plan (strict schema)
├── context.md        ← consolidated explorer findings for implementers to reuse
├── checkpoint.yaml   ← live state (which phase is done, which is pending)
├── review.md         ← reviewer's findings (created at review time)
└── notes/<id>.md     ← optional per-phase notes
```

When the feature ships, the whole directory moves to `.kanban/4-done/<slug>/`.

### Slash commands

Six commands, one per lifecycle step:

| Command | What it does | Invokes |
| --- | --- | --- |
| `/spec <topic>` | Start a new feature: open the Architect. | architect |
| `/next-phase [slug]` | Implement the next pending phase. | implementer |
| `/feature-review [slug]` | All phases done — generate `review.md`. | reviewer |
| `/resolve [slug] <Fxx>` | Fix one review finding. | resolver |
| `/done [slug]` | Run final verify and promote to `4-done/`. | (script only) |
| `/resume [slug]` | Diagnose state, recommend next command. | (script only) |

`[slug]` is optional when exactly one feature is active in `.kanban/2-in-progress/`.

---

## 3. Lifecycle walkthrough

Let's walk through the complete lifecycle of a hypothetical feature, "streaming-pipes", from idea to shipped.

### Step 0 — Prereqs

You're on a clean working tree. You're checked out on a branch (or `main`, but a feature branch is cleaner). You have an idea for a feature.

```bash
git checkout -b feat/streaming-pipes
git status   # clean
```

### Step 1 — Design (turn 1)

You type:

```
/spec streaming pipes that chain commands like $(cmd1 | cmd2)
```

The slash command:
1. Lists any existing in-flight features (in case you meant to refine one).
2. Asks you to confirm the kebab-case slug (`streaming-pipes`).
3. Dispatches the **architect** agent.

The architect:
- Spawns parallel `explore` subagents to find prior art, the parser entry points, the visitor pattern, the opcode metadata format.
- Asks you clarifying questions about edge cases (what if a stage exits non-zero? what about timeouts?).
- Writes `spec.md` with the design + decision log.
- Writes `plan.yaml` with phases (1A, 1B, 1C, ...) — each phase declares its files, verify commands, non-goals, and token estimate.
- Writes `context.md` distilling explorer findings into a 10 KB pointer doc.
- Runs `bash scripts/checkpoint/bootstrap-feature.sh streaming-pipes "Streaming Pipes"`.
- Runs `python3 scripts/checkpoint/validate-spec.py streaming-pipes` and fixes any errors.
- Adds a one-line pointer in `.claude/repo.md`.
- Returns: "Spec ready. Slug: `streaming-pipes`. 6 phases. Run `/next-phase streaming-pipes`."

**You can interrupt this turn.** If the architect hits rate limits mid-design, restart `/spec streaming-pipes` later — the agent reads what's already in `.kanban/2-in-progress/streaming-pipes/` and continues.

When you're happy with the spec, **commit it**:

```bash
git add .kanban/2-in-progress/streaming-pipes .claude/repo.md
git commit -m "spec(streaming-pipes): design"
```

### Step 2 — Implement, phase by phase (turns 2..N)

For each phase, type:

```
/next-phase
```

(slug optional if only one feature is active)

The slash command:
1. Runs `validate-spec.py` to confirm everything is in order.
2. Checks `git status` is clean (refuses if not — run `/resume` first).
3. Runs `next-phase.py` to find the next pending phase whose deps are done. Outputs YAML.
4. Refuses to dispatch if `est_tokens > 80000` (architect must split the phase).
5. Marks the phase `in_progress` in `checkpoint.yaml`.
6. Dispatches the **implementer** with a brief that includes the phase YAML, the spec/context paths, the non-goals (verbatim), the file scope, and the verify command.

The implementer:
- Reads the phase brief, `spec.md` (relevant sections), `context.md`, and the files in `phase.files`.
- Writes/edits code restricted to `phase.files`.
- Runs `bash scripts/checkpoint/verify-phase.sh streaming-pipes 1A` — this runs the verify command *and* refuses if any out-of-scope file was modified.
- Commits with: `feat(streaming-pipes): phase 1A — AST nodes + visitors`.
- Runs `advance-checkpoint.py streaming-pipes 1A done --commit <sha> --verified true`.
- Returns: "Phase 1A done. Next: 1B."

You type `/next-phase` again. And again. Once per phase.

**You can stop here for the day.** Come back tomorrow, type `/resume`, see where you are, type `/next-phase` to continue.

### Step 3 — Review (turn N+1)

When all phases are `done`, type:

```
/feature-review
```

The slash command:
1. Refuses if any phase isn't done.
2. Computes the diff range via `git merge-base HEAD origin/main` and `git log --grep "feat(streaming-pipes)"`.
3. Runs `dotnet test` once for a baseline.
4. Dispatches the **reviewer** with spec.md, plan.yaml, the diff range, and the test baseline.

The reviewer:
- Reads `spec.md` fully.
- Maps each spec requirement to its diff hunks. Flags anything missing.
- Reads `plan.yaml` and checks every phase stayed within its declared file scope.
- Spawns `explore` subagents for spot checks ("is `XError` always raised via the registry?").
- Writes `review.md` with structured findings:

```markdown
## F01 — [CRITICAL] Pipeline doesn't kill upstream on downstream exit

**Status:** open
**Files:** Stash.Stdlib/BuiltIns/ProcessBuiltIns.cs:432
**Phase:** 1C
**Commit:** abc1234

### Observation
...

### Suggested fix
...

### Verify
```
dotnet test --filter PipelineCleanupTests
```
```

- Sets `checkpoint.review.status = in_progress` (or `resolved` if no findings).
- Returns: "3 CRITICAL, 2 IMPORTANT, 1 MINOR. Next: `/resolve streaming-pipes F01`."

### Step 4 — Resolve findings one at a time (turns N+2..M)

For each finding, type:

```
/resolve F01
```

(slug optional; finding id required)

The slash command:
1. Extracts the F01 section verbatim from `review.md`.
2. Refuses if `Status:** fixed`.
3. Refuses if the tree is dirty.
4. Dispatches the **resolver** with the finding section and pointers.

The resolver:
- Reads only the files named in the finding.
- Applies the `Suggested fix` exactly. If the suggestion is wrong, stops and reports — doesn't improvise.
- Runs the finding's `Verify` command.
- Commits: `fix(streaming-pipes): F01 — pipeline doesn't kill upstream on downstream exit`.
- Updates `review.md`: `Status:** open` → `Status:** fixed`, adds `Fixed in: <sha>`.
- Returns: "F01 fixed. 2 CRITICAL, 2 IMPORTANT, 1 MINOR remaining. Next: `/resolve F02`."

You type `/resolve F02`, `/resolve F03`, etc. until everything is fixed.

When the last finding is fixed, the slash command sets `checkpoint.review.status = resolved` and tells you `/done` is ready.

### Step 5 — Ship (turn M+1)

Type:

```
/done
```

No agent runs. The slash command:
1. Refuses if any phase isn't `done` or any finding is `open`.
2. Runs `plan.yaml`'s `final_verify` commands (typically `dotnet build` + `dotnet test`).
3. Moves `.kanban/2-in-progress/streaming-pipes/` → `.kanban/4-done/streaming-pipes/`.
4. Prepends a one-line entry to `.claude/repo.md` "Recent Completed Work".
5. Removes the active-features pointer.
6. Commits the kanban move + repo.md update.

Feature shipped. Open a PR in the normal way.

---

## 4. The artifacts in detail

### `spec.md`

The human-readable design doc. Lives in `.kanban/2-in-progress/<slug>/spec.md`. Has sections for motivation, goals/non-goals, design (surface + semantics + cross-platform), implementation surface checklist, decision log, test plan, open questions.

You read this. Reviewers read this. Implementers read the sections relevant to their phase.

Template: `.kanban/_templates/spec-template.md`.

### `plan.yaml`

The machine-readable phase plan. **Every slash command reads this.** Schema:

```yaml
schema_version: 1
feature: streaming-pipes       # must match dir name
title: Streaming Pipes
spec: ./spec.md
context: ./context.md
default_verify:
  - dotnet build
scope:                         # files allowed across all phases (globs)
  - Stash.Core/**
  - Stash.Bytecode/**
phases:
  - id: 1A
    title: AST nodes + visitors
    deps: []                   # ids of phases that must be done first
    files:                     # files this phase may modify
      - Stash.Core/Parsing/AST/StreamingCommand.cs
      - Stash.Analysis/Visitors/*.cs
    verify:                    # commands run after the phase's edits
      - dotnet test --filter ParserTests
    non_goals:                 # embedded verbatim in implementer brief
      - Do not implement bytecode opcodes (Phase 1B)
    est_tokens: 40000          # 30k-60k typical, 80k hard ceiling
    notes: |                   # free-text for the implementer
      Watch the 6-visitor pattern.
  - id: 1B
    title: Bytecode layer
    deps: [1A]
    files: [...]
    verify: [...]
    non_goals: [...]
    est_tokens: 30000
final_verify:                  # runs on /done
  - dotnet build
  - dotnet test
```

The architect writes this. The validate script (`scripts/checkpoint/validate-spec.py`) enforces:
- Required keys
- `feature` matches dir name
- Every phase has `id`/`title`/`deps`/`files`/`verify`/`non_goals`/`est_tokens`
- `deps` reference existing phases
- `est_tokens` between 5000 and 80000

If you edit `plan.yaml` (adding/removing phases), run validate to re-sync `checkpoint.yaml`.

### `context.md`

Consolidated explorer findings. The architect produces it once; every implementer reads it. Format:

```markdown
## Key file paths
| Concern | File | Notes |
| --- | --- | --- |
| AST visitor pattern | Stash.Core/Parsing/AST/IExprVisitor.cs | 6 implementors |

## Key types
- `StashError` — Stash.Core/Runtime/Types/StashError.cs — built-in error base

## Conventions discovered
- Diagnostics go through DiagnosticDescriptors.cs
- ...

## Prior art
- .kanban/4-done/diff-package/ — similar package structure
```

Keep under ~10 KB. Bigger context = phase-specific notes in `notes/<id>.md`.

### `checkpoint.yaml`

Live state. Updated atomically by `scripts/checkpoint/advance-checkpoint.py`. **Don't hand-edit during normal flow.**

```yaml
schema_version: 1
feature: streaming-pipes
current: 1B                    # null when between phases
updated: 2026-05-16T17:32:00Z
phases:
  1A:
    status: done               # pending | in_progress | done | failed
    commit: abc1234
    verified: true
    started: 2026-05-16T16:00:00Z
    completed: 2026-05-16T16:32:00Z
    attempts: 1
    notes: "AST + 6 visitors landed"
  1B:
    status: in_progress
    attempts: 2                # second try after a rate-limit interruption
    ...
review:
  status: in_progress           # not_started | in_progress | resolved
  findings_open: 2
  findings_fixed: 1
```

### `review.md`

The reviewer's output. **Format is strict** because `/resolve` parses it. Each finding is one H2 section with header `## Fxx — [SEVERITY] title` and required fields `Status`, `Files`, `Phase`, `Commit`, plus body sections `Observation`, `Why this matters`, `Suggested fix`, `Verify`.

`/resolve <Fxx>` extracts exactly that section via `awk` and feeds it to the resolver. The resolver updates `Status:** open` to `Status:** fixed` and adds `Fixed in: <sha>` when done.

---

## 5. Interruption and resumption

Three scenarios, all safe.

### Scenario A — Between phases (clean tree)

You finished phase 1A yesterday. Today, fresh session:

```
/resume
```

Output tells you: "all good, next phase is 1B, run `/next-phase`."

Nothing else needed. Type `/next-phase` and proceed.

### Scenario B — Mid-phase (dirty tree, checkpoint says `in_progress`)

You interrupted phase 1B mid-edit. The implementer made changes but didn't commit. Tomorrow:

```
/resume
```

The slash command notices the dirty tree + `in_progress` checkpoint and tells you to:

1. Run the phase's verify command manually: `bash scripts/checkpoint/verify-phase.sh streaming-pipes 1B`
2. If green → commit and advance manually, then `/next-phase` for the next one.
3. If red → `/next-phase` again will re-dispatch the implementer with the diff visible (the implementer will see uncommitted progress and finish it).

### Scenario C — Cold start (new session, weeks later)

Brand new session. No memory.

```
/resume streaming-pipes
```

The slash command:
- Reads `plan.yaml` and `checkpoint.yaml`
- Reads `git status` and `git log`
- Prints a state report
- Tells you the next command

No agent needs to know what happened before. The filesystem answered.

---

## 6. When NOT to use this workflow

This workflow is overhead for small work. Don't use it for:

- One-line bug fixes
- A test addition
- A single-file refactor
- Documentation typos
- Anything that would be exactly *one phase* under this workflow

For those, just dispatch the `implementer` agent directly (or write the code yourself). No kanban dir, no spec, no plan.yaml. Commit on `main` (or a tiny branch) and move on.

**Rule of thumb:** if you can't think of at least three distinct phases that each leave the codebase green, this isn't a checkpoint-workflow feature.

---

## 7. Helper scripts reference

All under `scripts/checkpoint/`. Run from repo root.

| Script | Use |
| --- | --- |
| `bootstrap-feature.sh <slug> [title]` | Create `.kanban/2-in-progress/<slug>/` from templates. The architect runs this. |
| `validate-spec.py [slug]` | Validate `plan.yaml` and auto-heal `checkpoint.yaml`. Safe to run any time. |
| `next-phase.py [slug]` | Print next pending phase as YAML. Exit 2 if none ready. |
| `verify-phase.sh <slug> <phase-id>` | Run verify commands and check no out-of-scope edits. |
| `advance-checkpoint.py <slug> <phase-id> <status>` | Atomic state transition. Enforces legal transitions. |
| `status.py [slug]` | Compact text status report. |
| `promote-done.sh <slug>` | Final verify + move to `4-done/`. |

You normally don't run these by hand — the slash commands do. But knowing they exist helps when something is off.

---

## 8. Troubleshooting

### `/next-phase` says "tree dirty"

Either:
- A previous phase didn't commit. Run `/resume` to diagnose.
- You have unrelated uncommitted work. Stash or commit it on a separate branch first.

### `/next-phase` says "no phase ready"

Possible causes:
- All phases are `done` — run `/feature-review`.
- A phase is `in_progress` — run `/resume`.
- A phase is `failed` — investigate, fix manually or have the architect amend the plan, then mark it pending again with `advance-checkpoint.py <slug> <id> pending` (illegal transition — you'll need to edit `checkpoint.yaml` directly; document why).

### `verify-phase.sh` says "out-of-scope file modifications detected"

The implementer touched a file not declared in `phase.files`. This is a hard error — do not "fix" by adding the file to `plan.yaml` from the implementer's seat. Stop, mark the phase failed, and ask the architect to amend the plan:

```bash
python3 scripts/checkpoint/advance-checkpoint.py <slug> <id> failed \
    --notes "scope-mismatch: needs <path> added to phase.files"
```

Then in a `/spec <slug>` follow-up turn, the architect edits `plan.yaml`, re-validates, and you can `/next-phase` again.

### Validate says "est_tokens=95000 exceeds 80k"

The phase is too big. Split it. The architect goes back to `plan.yaml`, splits the phase into two or three, re-validates. The checkpoint auto-heals.

### Reviewer found a pre-existing bug unrelated to this feature

It belongs in `.claude/repo.md` "Known Issues" or as a new spec stub in `.kanban/0-backlog/`. **Not in `review.md`.** `review.md` is only for issues introduced by *this* feature.

### I want to fix the reviewer's suggested fix because it's wrong

Don't have the resolver improvise. The right answer is one of:

- The user (you) manually edits `review.md` to correct the suggestion, then `/resolve <Fxx>`.
- Run `/feature-review` again to get a fresh reviewer pass with corrected context (overwrites `review.md`).

The resolver is intentionally low-judgment — it follows the suggestion.

### I want to do multiple phases in one go

Don't. That's exactly the long-running-orchestrator anti-pattern this workflow exists to avoid. Each `/next-phase` is one Sonnet turn. If you want to burn through them quickly, just type the command back-to-back; the loop is yours to drive.

### I edited a phase's `files` list mid-feature; now what?

Run `validate-spec.py <slug>` — it'll re-sync the checkpoint and warn if you broke any dependencies. If you removed a phase entirely, you may need to edit `checkpoint.yaml` to remove its entry (validate will flag it as "unknown phase").

### Where is the "active task" being tracked?

`.kanban/2-in-progress/<slug>/checkpoint.yaml`. **Not** `.claude/repo.md` (which only has a one-line pointer). **Not** the agent's conversation memory. Always one place. Always machine-readable.

---

## 9. Quick reference card

```
NEW FEATURE
  /spec <topic>                     ← architect designs

IMPLEMENT (repeat per phase)
  /next-phase [slug]                ← implementer does one phase

REVIEW
  /feature-review [slug]            ← reviewer writes review.md

RESOLVE (repeat per finding)
  /resolve [slug] <Fxx>             ← resolver fixes one finding

SHIP
  /done [slug]                      ← script promotes to 4-done/

ANY TIME
  /resume [slug]                    ← diagnose state, get next command
```

That's the whole loop. Welcome to Stash.
