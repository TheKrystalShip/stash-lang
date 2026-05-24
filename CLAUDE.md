# Claude Code Instructions

Read `AGENTS.md` first for shared Stash project architecture, build/test commands, coding conventions, language semantics, and documentation rules.

This file contains only Claude-specific workflow instructions.

## Checkpoint Workflow — multi-phase features

Multi-phase work (new language features, large refactors, anything beyond a one-shot fix) goes through the **checkpoint workflow**: a flat, file-mediated pipeline where each user turn does one bounded job and state lives on disk between turns. This fits Claude Code's token-budget / rate-limit model — interruption is safe at any point.

**New here?** Read **`.claude/WORKFLOW.md`** — the canonical end-user tutorial. It walks through a full feature lifecycle with examples and a troubleshooting section.

**Design rationale & internals:** `.claude/skills/checkpoint-workflow.md`.

### Lifecycle (each step is one user turn)

| Slash command | What happens | Agent invoked |
| --- | --- | --- |
| `/spec [topic]` | Architect designs the feature, writes `spec.md` + `plan.yaml` + `context.md`, bootstraps `.kanban/2-in-progress/<slug>/` | `architect` (Opus) |
| `/next-phase [slug]` | Dispatches one Implementer turn for the next pending phase. Commits on green, advances checkpoint. Repeat per phase. | `implementer` (Sonnet) |
| `/feature-review [slug]` | Reviewer reads diff vs spec, writes structured `review.md` with prioritized findings. Does NOT fix anything. | `reviewer` (Opus) |
| `/resolve [slug] <Fxx>` | Resolver fixes exactly one finding, commits, marks it fixed. Repeat per finding. | `resolver` (Sonnet) |
| `/done [slug]` | Runs `final_verify`, refuses if anything is open, promotes feature to `.kanban/4-done/`. Script-only. | none |
| `/resume [slug]` | Diagnostic — prints state, suggests next command. Script-only. | none |

### Helper scripts (`scripts/checkpoint/`)

| Script | Purpose |
| --- | --- |
| `bootstrap-feature.sh` | Create `.kanban/2-in-progress/<slug>/` from templates |
| `validate-spec.py` | Strict structural validation of `plan.yaml`; auto-heals checkpoint |
| `next-phase.py` | Print next pending phase as a YAML brief |
| `verify-phase.sh` | Run phase verify commands AND enforce file-scope |
| `advance-checkpoint.py` | Atomic state transitions in `checkpoint.yaml` |
| `status.py` | Compact text status for `/resume` |
| `promote-done.sh` | Final acceptance + move to `4-done/` |

### `final_verify` must filter documented flakies

When authoring `plan.yaml`, narrow `final_verify`'s `dotnet test` step to exclude documented flaky / environment-dependent classes (see `.claude/repo.md` "Known Issues"). Bare `dotnet test` fails `/done` due to pre-existing `DiffPackageTests`, `Registry*Tests`, `NetBuiltInsTests`, parallel-execution flakies, etc. — these are not feature regressions. Precedent: `exports-private-default` and `stdlib-namespace-members` `plan.yaml` carry the canonical filter shape.

### Every workflow agent leaves the tree dirty after a state advance

`advance-checkpoint.py` (called by implementer, reviewer, and resolver flows) rewrites `.kanban/2-in-progress/<slug>/checkpoint.yaml` *after* the corresponding code commit. Reviewer also writes a new `review.md`; resolver also flips finding statuses inside `review.md`. Every subsequent workflow command refuses on dirty tree. The orchestrator must follow each agent turn with a tiny chore commit:

```bash
# implementer (after a phase commit)
git add .kanban/2-in-progress/<slug>/checkpoint.yaml
git commit -m "chore(<slug>): record <id> done state"

# reviewer (after writing review.md)
git add .kanban/2-in-progress/<slug>/review.md .kanban/2-in-progress/<slug>/checkpoint.yaml
git commit -m "chore(<slug>): land review.md (<severity counts>)"

# resolver (after a fix commit)
git add .kanban/2-in-progress/<slug>/review.md .kanban/2-in-progress/<slug>/checkpoint.yaml
git commit -m "chore(<slug>): record <Fxx> fixed"
```

### Subagents can return interrupted mid-edit reports

The `implementer`, `resolver`, and `architect` agents occasionally return their final message mid-action — the tree is partially edited, no commit landed, no verify ran. After every subagent turn, check `git status --porcelain` and `git log -1 --oneline` to confirm the expected commit landed. If the tree is dirty with in-scope files but no commit appears, the orchestrator must finish the work: run the union of the agent's verify commands, commit using the agent's intended message format, write the corresponding `review.md` / `checkpoint.yaml` updates, and chore-commit those.

## Specialized agents

This project's agents live in `.claude/agents/`. Under the checkpoint workflow, you typically never invoke an agent directly — the slash commands do it for you.

| Agent | Model | When the slash command dispatches it |
| ----- | ----- | ----------- |
| `architect` | Opus | `/spec` — design new feature |
| `implementer` | Sonnet | `/next-phase` — implement one phase |
| `reviewer` | Opus | `/feature-review` — produce review.md |
| `resolver` | Sonnet | `/resolve` — fix one finding |
| `profiler` | Opus | Performance investigation, benchmarking (manual invocation) |
| `debugger` | Sonnet | Tracing runtime bugs, minimal repros (manual invocation) |
| `explore` | Haiku | Spawned by other agents for codebase search (never invoke directly) |
| `orchestrator` | — | **Deprecated.** See `.claude/agents/orchestrator.md` for redirect. |

For a small one-off change (bug fix, single test, one-file refactor), skip the checkpoint workflow and dispatch `implementer` directly with file paths.

## Project Memory

`.claude/repo.md` contains build state, active multi-phase work pointer (one line per feature), architecture decisions, and known gotchas. Read it when starting any multi-step task.

**Live checkpoint state does NOT live in `repo.md`.** It lives in `.kanban/2-in-progress/<slug>/checkpoint.yaml`. `repo.md` only carries a one-line pointer to active features and the historical record of completed ones.

## Additional Guidelines

@.claude/agent-tools.md
@.claude/language-changes.md
@.claude/performance.md
