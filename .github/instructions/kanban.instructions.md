---
description: "Use when: interacting with kanban spec files, moving specs between kanban directories, understanding the spec lifecycle, or determining which kanban directory to read/write. Covers the kanban workflow for spec creation, implementation, review, and completion."
applyTo: ".kanban/**"
---

# Kanban Spec Workflow

The `.kanban/` directory tracks the lifecycle of language features and design specs through five stages.

## Directory Stages

| Directory        | Stage          | Who works here                | Description                                                                                                                  |
| ---------------- | -------------- | ----------------------------- | ---------------------------------------------------------------------------------------------------------------------------- |
| `0-backlog/`     | Design         | Architect agent + user only   | Specs under active design. Incomplete, evolving, potentially inaccurate. **No other agents should read or act on these.**    |
| `1-todo/`        | Ready          | Orchestrator (on user prompt) | Approved specs ready for implementation. User moves specs here manually when design is complete.                             |
| `2-in-progress/` | Implementation | Orchestrator + subagents      | Actively being implemented. Stays here until work is complete, then user moves to review.                                    |
| `3-review/`      | Review         | Reviewer agent                | Implementation complete, under review. Issues are fixed in-place without moving the spec. Reviewer moves to done after approval. |
| `4-done/`        | Complete       | Reference only                | Fully designed, implemented, and reviewed. Historical record.                                                                |

## Rules

- **Only the user moves specs between directories**, with one exception: the **Reviewer agent** moves specs from `3-review/` to `4-done/` after a successful review.
- **Architect agent** creates and edits specs in `0-backlog/` during design sessions.
- **Orchestrator agent** reads specs from `1-todo/` and works on them in `2-in-progress/` when prompted by the user.
- **Reviewer agent** reviews specs in `3-review/`, fixes issues via Implementer subagents, and moves the spec to `4-done/` when the review passes.
- **Other agents** (Implementer, Explore) only access kanban specs when they need specific context for a task they're already working on — they don't browse these directories unprompted.
