---
description: Open an Architect session to design a new feature. Produces brief.md, plan.yaml, and checkpoint.yaml under .kanban/2-in-progress/<slug>/.
argument-hint: [topic or feature description]
---

You are about to dispatch the **architect** agent to design a new feature.

## Topic from the user

$ARGUMENTS

## Pre-flight

Before invoking the architect, do this in the main conversation (not inside the architect):

1. Confirm the user actually wants to start a new design session (vs. resuming an existing one). If `.kanban/2-in-progress/` already has features, list them with:

   ```bash
   ls -1 .kanban/2-in-progress/ 2>/dev/null || true
   ```

   If one of them might be what the user means, ask whether they want to refine that brief instead of starting fresh.

2. Read `.claude/skills/checkpoint-workflow.md` and `.claude/agents/architect.md` so you can pass the architect a brief that's anchored in the current workflow.

3. Propose a kebab-case slug to the user based on the topic (e.g., `streaming-pipes`, `typed-catch`). Get confirmation before bootstrapping — slugs are immutable once a feature is created.

## Dispatch

Once the slug is confirmed, invoke the `architect` agent via the `Agent` tool with `subagent_type: "architect"`. The prompt must include:

- The user's topic (verbatim)
- The agreed slug
- An instruction to start by reading `.claude/skills/checkpoint-workflow.md` and `.kanban/_templates/` before doing anything
- An instruction to run `stash scripts/checkpoint/checkpoint.stash bootstrap-feature <slug> "<title>"` only AFTER initial design discussion has converged enough to commit to a slug and title
- An instruction to run `stash scripts/checkpoint/checkpoint.stash validate-spec <slug>` before declaring the spec ready, and fix every reported problem
- An instruction to add a one-line pointer to `.claude/repo.md` "Active Multi-Phase Work" when done
- An instruction to NOT write implementation code — only `brief.md`, `plan.yaml`, and checkpoint artifacts

Tell the architect to return the slug and a short summary so you can report back to the user with the next command: `/next-phase <slug>`.
