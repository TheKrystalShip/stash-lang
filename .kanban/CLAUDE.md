# Kanban Directory

The `.kanban/` directory holds Stash's design and implementation lifecycle for multi-phase features. **Canonical workflow:** [`/.claude/WORKFLOW.md`](/.claude/WORKFLOW.md). This file describes the directory layout only; the workflow itself lives there.

## Directory Layout

| Directory        | Purpose                                                                                       |
| ---------------- | --------------------------------------------------------------------------------------------- |
| `0-backlog/`     | Design notes, bug stubs, optimization ideas. Authored freely by anyone. Not actionable yet.   |
| `1-todo/`        | Approved specs ready to start. User moves a backlog item here when they want it picked up.    |
| `2-in-progress/` | Active features. Each has `brief.md` + `plan.yaml` + `checkpoint.yaml`, `review.md` post-review. |
| `4-done/`        | Completed features. Historical record. Reference-only — never re-edit.                        |
| `milestones/`    | Long-term programs spanning many feature cycles. Each has `<slug>/MILESTONE.md` (a living charter; completion is *derived* from `4-done/`, never asserted). See `/milestone` and `.claude/WORKFLOW.md`. |
| `_templates/`    | `brief-template.md`, `plan-template.yaml`, `checkpoint-template.yaml`, `review-template.md`, `milestone-template.md`. |

`3-review/` exists for legacy specs from the prior workflow. New features do **not** move there — review happens in-place at `2-in-progress/<slug>/review.md` and `/done` promotes directly to `4-done/`.

## Authoring Rules

- **Architect** (`/spec`) creates and edits artifacts in `2-in-progress/<slug>/` after bootstrapping. Design notes that aren't tied to an active feature live in `0-backlog/`.
- **Implementer** (`/next-phase`) reads `brief.md` and the current phase's YAML; edits source code, not kanban files (except as the workflow scripts dictate).
- **Reviewer** (`/feature-review`) writes `review.md` in-place. Does not move directories.
- **Resolver** (`/resolve`) fixes selected findings and updates `review.md` status fields. Does not move directories.
- **User** moves specs between `0-backlog/` → `1-todo/` manually. `/done` handles the `2-in-progress/` → `4-done/` move.

## Backlog Stubs

Bugs and follow-up ideas discovered during work go under `0-backlog/bugs/`, `0-backlog/optimizations/`, etc.

### Bug reports — mandatory template

**Any agent (or human) filing a bug stub under `0-backlog/bugs/` MUST start from `_templates/bug-template.md`.** Copy it, fill in every section, save under a descriptive filename. The template's sections are not optional — if a section genuinely doesn't apply, write a one-line explanation in that section rather than deleting it. Required sections: Status / Created / Discovery context headers, Problem, Reproduction, Blast radius, Root cause, Suggested fix, Verification, Related.

This rule covers bugs surfaced during any workflow phase: architect design sessions, implementer phase work, reviewer feature reviews, resolver finding fixes, and ad-hoc investigation. Reviewers in particular must use the template when filing out-of-scope bugs surfaced during a feature review — those go in `0-backlog/bugs/`, not `review.md`.

### Other backlog stubs

Optimization ideas, design sketches, and other non-bug notes use the same general shape (`Status:`, `Created:`, `Discovery context:`, narrative) but are not bound to the bug template. Existing files in `0-backlog/{optimizations,language,stdlib,...}/` are the convention.

### Fixed bugs → promote to `4-done/`

When a backlog bug is fixed: append a `## Resolution (<date>)` section (fix commit, what changed, verification performed) to its stub, set `**Status:** Fixed — <date> (commit <hash>)`, and move it to `4-done/` as a flat **`Bug — <Title-Case>.md`** file — there is **no** `4-done/bugs/` subfolder; fixed-bug stubs sit alongside completed features. A bug filed *and* fixed in the same session may go straight to `4-done/` "born resolved."
