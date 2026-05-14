# Claude Code Instructions

Read `AGENTS.md` first for shared Stash project architecture, build/test commands, coding conventions, language semantics, and documentation rules.

This file contains only Claude-specific workflow instructions.

## Specialized Agents

This project has a team of Claude Code agents in `.claude/agents/`. For complex or multi-step work, invoke the appropriate agent rather than working directly.

| Agent | Model | When to use |
| ----- | ----- | ----------- |
| `orchestrator` | Opus | Multi-phase features, large refactors, implementing a kanban spec end-to-end |
| `architect` | Opus | Feature design, spec writing, kanban backlog work, feasibility analysis |
| `reviewer` | Opus | Code review after implementation — reads `.kanban/3-review/` specs autonomously |
| `profiler` | Opus | Performance investigation, benchmarking, dispatch loop analysis |
| `implementer` | Sonnet | Code changes (typically spawned by orchestrator, not invoked directly) |
| `explore` | Haiku | Fast read-only codebase search (spawned by other agents, not invoked directly) |
| `debugger` | Sonnet | Tracing runtime bugs, writing minimal repro cases, inspecting bytecode behavior |

For design work → use `architect`. For a full feature from spec to tested code → use `orchestrator`. For a suspicious test failure or runtime behavior → use `debugger`. For a regression after a change → use `profiler`.

## Project Memory

`.claude/repo.md` is the persistent memory shared across agent sessions. It contains the current build state, active work, enduring architecture decisions, and known gotchas. Read it when starting any architectural or multi-step task.

## Additional Guidelines

@.claude/agent-tools.md
@.claude/language-changes.md
@.claude/performance.md
