---
description: Drive a specced feature to completion autonomously — all phases, review, fixes — in a dedicated worktree by default. Stops at /done on the branch (merge to main stays human/opt-in).
argument-hint: [slug] [--here] [--merge]
---

You are about to **autonomously drive an already-specced feature to completion**: every
implementation phase, the review, and the fixes — then stop with the feature promoted on its
own branch, ready for a human to merge.

This command is a **thin driver**. It does **not** reimplement any dispatch logic. It *composes*
the existing slash commands — `/next-phase`, `/feature-review`, `/resolve`, `/done` — by invoking
them in sequence (via the Skill tool, exactly as if a user typed them), and owns **only** the
parts they don't: the worktree wrapper, the loop + termination, the retry budget, the orchestrator
chore-commits between steps, and the hard-stop conditions. Treat the four sub-commands as the
single source of truth for *how* each step dispatches; never duplicate their bodies here.

## Args from the user

$ARGUMENTS

Parse: the first non-flag token is `<slug>` (required). Flags:

- `--here` — **opt out of the worktree.** Run the whole pipeline in the current checkout instead
  of a sibling worktree. (Default is worktree-per-feature so concurrent work isn't blocked.)
- `--merge` — **opt in to auto-merge.** After `/done`, also merge the branch into `main`. Default
  is to **stop before merging** and hand that judgment to a human.

If no slug is given and exactly one feature is in `.kanban/2-in-progress/`, infer it; otherwise
list the active features and stop.

---

## How this command behaves (read before running)

- **Resumable.** Every step leaves a clean tree and an advanced `checkpoint.yaml`. Re-invoking
  `/autopilot <slug>` after any interruption re-enters the worktree and picks up from checkpoint
  state — it never restarts work already committed.
- **Cost.** A full run executes the entire `dotnet test` suite **at least 3×** (both
  `/feature-review` passes + `/done`'s `final_verify`), on top of each phase's `verify-phase`. On a
  large feature that is real wall-clock time — expected, not a hang.
- **Two hard rules that override everything below:**
  1. **Hard gates are never retried or forced past.** A red `verify-phase`, a red `final_verify`,
     a merge conflict, or a post-merge-red → **STOP immediately, leave the tree exactly as it is,
     and write a handoff** (what failed, the exact command to resume). Never edit code to force a
     gate green outside the normal phase/resolve flow.
  2. **Soft failures get 2 attempts, then stop.** A phase or a finding that the sub-agent cannot
     resolve after **2 total attempts** → STOP + handoff. Do not loop a third time.

---

## Step 1 — Preconditions (run in the **main checkout**, before any worktree)

Stop on the first failure and tell the user how to fix it.

1. **Spec exists and is committed on `main`.** `worktree-start` branches from the *committed*
   `main` ref, so an uncommitted spec won't carry into the worktree.
   ```bash
   git -C . rev-parse --abbrev-ref HEAD          # expect: main  (or run --here)
   [ -d ".kanban/2-in-progress/$SLUG" ] || { echo "no spec for $SLUG — run /spec first"; exit 1; }
   [ -z "$(git status --porcelain .kanban/2-in-progress/$SLUG)" ] \
     || { echo "spec for $SLUG is uncommitted — commit it on main first"; exit 1; }
   ```
2. **Spec validates.**
   ```bash
   stash scripts/checkpoint/validate-spec.stash "$SLUG"
   ```
   If this fails, the brief/plan needs the architect — stop and say so (`/spec`).

## Step 2 — Enter the worktree (default) / resume / `--here`

- **`--here`:** skip this step entirely. Work in the current checkout. (No merge step at the end —
  the work simply lands wherever you are.)
- **Worktree already exists** (`../stash-<slug>` + `feature/<slug>` present — an interrupted prior
  run): do **not** call `worktree-start` (it refuses on collision). Just re-enter it:
  ```
  EnterWorktree  path: ../stash-<slug>
  ```
- **Otherwise — create it:**
  ```bash
  stash scripts/checkpoint/worktree-start.stash "$SLUG"
  ```
  then `EnterWorktree path: ../stash-<slug>`. Once inside (cwd is now the worktree, with its own
  `.kanban/` and `scripts/`), run the parallel-safety heads-up and **surface its warning but do
  not stop** (it is non-blocking, exit 3):
  ```bash
  stash scripts/checkpoint/check-parallel-safety.stash "$SLUG" || true
  ```

Everything from here runs **inside the worktree** (or the current checkout under `--here`), so the
sub-commands resolve all paths locally — no cross-checkout orchestration.

## Step 3 — Phase loop (until all phases `done`)

Repeat until `stash scripts/checkpoint/status.stash "$SLUG"` shows every phase `done`:

1. Invoke **`/next-phase <slug>`** (Skill tool). One implementer, one phase. `/next-phase` already
   runs its own pre-flight (validate, clean-tree check, get-next-phase) and its own
   post-dispatch verification + implementer chore-commit fallback — let it.
2. After it returns, confirm the phase advanced (`status.stash`) and the tree is clean.
3. **If a phase reaches `failed`** (verify never went green after the implementer's bounded
   corrections): that is the retry boundary. Re-invoke `/next-phase <slug>` **once** more (attempt
   2). If it fails again → **STOP + handoff** (phase id, failure reason, `/resume <slug>` to continue).

When `/next-phase` reports "all phases done," continue to Step 4.

## Step 4 — Review pass 1

1. Invoke **`/feature-review <slug>`** (Skill tool). It refuses unless all phases are done, runs the
   `dotnet test` baseline, and the reviewer writes `review.md` + sets `--review-status`.
2. **Commit the review (orchestrator's job — the sub-command leaves it dirty):**
   ```bash
   git add .kanban/2-in-progress/$SLUG/review.md .kanban/2-in-progress/$SLUG/checkpoint.yaml
   git commit -m "chore($SLUG): land review.md"
   ```
   This chore-commit is **mandatory before any `/resolve`** — `/resolve` refuses on a dirty tree,
   so skipping it deadlocks the pipeline.
3. **Zero findings →** skip Steps 5–6, go straight to Step 7 (`/done`).
   **Findings exist →** go to Step 5.

## Step 5 — Resolve all findings (of the current review)

Loop until `grep '\*\*Status:\*\* open' .kanban/2-in-progress/$SLUG/review.md` is empty:

1. Invoke **`/resolve <slug> Fxx`** (Skill tool) — one finding, or a small batch of clearly
   related/small findings. The resolver fixes only those, runs their Verify union, commits the fix,
   and flips their status to `fixed`.
2. **Commit the status flip (orchestrator's job):**
   ```bash
   git add .kanban/2-in-progress/$SLUG/review.md .kanban/2-in-progress/$SLUG/checkpoint.yaml
   git commit -m "chore($SLUG): record <Fxx[ Fyy...]> fixed"
   ```
3. **2-attempt rule:** if `/resolve` reports a finding it cannot cleanly fix (conflicting batch,
   needs design judgment, verify won't pass), re-attempt it once alone. If it still fails →
   **STOP + handoff** (which finding, why, the remaining open findings).

## Step 6 — Review pass 2 (the single re-review)

This is the **one** re-review (per the locked design: exactly two review passes max). It catches
regressions the fixes introduced.

1. Invoke **`/feature-review <slug>`** again, then chore-commit `land review.md` as in Step 4.2.
2. **Zero findings →** Step 7. **Findings exist →** resolve them all (Step 5 mechanics), then go to
   Step 7 with **no third review** — `/done`'s `final_verify` is the backstop for the pass-2 fixes.

## Step 7 — Finish (promote on the branch)

1. Confirm the tree is clean, then invoke **`/done <slug>`** (Skill tool). It runs `promote-done`
   (all-phases-done + all-findings-fixed gate + `final_verify`), moves the dir to `.kanban/4-done/`
   **on the branch**, archives into `repo.md`, and commits the promotion.
2. **`final_verify` red → HARD STOP + handoff.** Never forced past. (This is where an
   un-re-reviewed pass-2 regression would surface — by design.)

## Step 8 — Merge handoff (default) or `--merge` (opt-in)

- **Default (no `--merge`): return to `main`, then STOP and report.** The feature is promoted on
  `feature/<slug>`, not yet on `main`. If you entered a worktree (i.e. not `--here`), first
  `ExitWorktree action: keep` so the session lands back in the **main checkout** — `worktree-finish`
  must run from `main`, and `keep` leaves the worktree in place for that merge. Then tell the user
  to integrate from there:
  ```bash
  stash scripts/checkpoint/worktree-finish.stash <slug>
  ```
  Merging is left to a human because it needs judgment the script can't make: *last-to-merge-pays*
  ordering against sibling features, green-on-branch ≠ green-on-merged-`main`, and the guaranteed
  `repo.md` "Active Multi-Phase Work" collision.
- **`--merge` (opt-in): do the merge.** `worktree-finish` must run from `main`, not the worktree:
  1. `ExitWorktree action: keep` (return to the main checkout; do **not** remove — the script
     removes the worktree itself only if the post-merge verify is green).
  2. ```bash
     stash scripts/checkpoint/worktree-finish.stash <slug>
     ```
     It merges `--no-ff`, re-runs `final_verify` on the merged `main`, and cleans up only if green.
  3. **Merge conflict or post-merge `final_verify` red → HARD STOP + handoff.** The script
     preserves the merge and leaves the tree; report that the user must resolve and re-run.

## Hard gates (never retried, never forced — immediate STOP + leave tree + handoff)

| Gate | Where |
| --- | --- |
| `verify-phase` red after 2 attempts | Step 3 |
| A finding unfixable after 2 attempts | Step 5 / 6 |
| `final_verify` red | Step 7 (`/done`) |
| Merge conflict | Step 8 (`--merge`) |
| Post-merge `final_verify` red | Step 8 (`--merge`) |

## Reporting

Narrate each transition in one line as you go ("phase P3 done (abc1234) → P4…", "review pass 1: 3
findings → resolving", "F02 fixed (def5678)"). End with a summary:

- phases completed (+ commit SHAs), findings fixed across both passes,
- where it stopped (`/done` promoted on branch, or the hard gate that halted it),
- the **exact next command** for the human (the `worktree-finish` line, or the resume command).

## What this command does NOT do

- It never runs `/spec` — the spec must already exist (this drives an *existing* spec).
- It never merges to `main` unless `--merge` is passed.
- It never forces a red gate green, and never chases out-of-scope bugs an agent discovers — those
  are filed to `.kanban/0-backlog/bugs/` by the agent that found them (existing workflow rule) and
  the run continues.
