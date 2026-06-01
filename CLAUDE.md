# Claude Code Instructions

Read `AGENTS.md` first for shared Stash project architecture, build/test commands, coding conventions, language semantics, and documentation rules.

This file contains only Claude-specific workflow instructions.

## Code Conventions — Bounded Domains (No Magic Strings)

Applies to **every** Stash project, not just the registry. The goal is a self-documenting codebase: a reader — or the compiler — can always see the full set of legal values for a field.

**Rule.** A value drawn from a closed/bounded set (a fixed list of valid values) must be referenced through a named definition — an `enum`, or a `const` / `static readonly` set in a single source-of-truth class — and never inlined as a literal at a use site. Parse external/wire strings into typed values **at the boundary**; downstream code works in enums/constants, not raw strings.

- **Bounded (must be named):** claim names, roles, scopes, policy names, opcode/instruction names, diagnostic codes (`SA0201`…), mode/state flags, namespace names, reserved identifiers, enum-like DB column values.
- **Not bounded — exempt, do NOT over-apply:** free-form text with no closed set — log/exception messages, user-facing prose, format strings, route templates, regex patterns, file paths, test fixtures / expected-output snapshots.
- **Single source of truth.** Define each domain once. A closed set duplicated across files (e.g. the same `["a","b"]` array copied into three places) is the same defect as an inline literal — collapse it.
- **Enforce in the test/CI gate, not just convention.** Agents get no IDE squiggle, so the enforcement that actually bites lives in a meta-test. Prefer **sink-targeted** scans (a Roslyn syntax scan flagging string literals that reach known sinks) over broad literal scans — `Stash.Tests/Registry/Authz/NoMagicAuthStringsMetaTests.cs` is the canonical pattern (it ships a self-test proving the scan has teeth and a floor guard against a vacuous pass). The sink list is append-only: when a new bounded domain gets a named home, add its accessor.
- **When a domain can't be sink-guarded** (bare `==` comparisons), the durable fix is a real `enum` (with an EF value converter for DB columns) so illegal values fail to compile. Centralized string constants are the cheap 80%; types are the 100%.

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
| `bootstrap-feature.stash` | Create `.kanban/2-in-progress/<slug>/` from templates |
| `validate-spec.stash` | Strict structural validation of `plan.yaml`; auto-heals checkpoint |
| `next-phase.stash` | Print next pending phase as a YAML brief |
| `verify-phase.stash` | Run phase verify commands AND enforce file-scope |
| `advance-checkpoint.stash` | Atomic state transitions in `checkpoint.yaml` |
| `status.stash` | Compact text status for `/resume` |
| `promote-done.stash` | Final acceptance + move to `4-done/` |
| `worktree-start.stash` | Create `../stash-<slug>` on a fresh `feature/<slug>` branch (parallel work) |
| `check-parallel-safety.stash` | Warn on subsystem overlap with an in-flight sibling worktree |
| `worktree-finish.stash` | Merge `--no-ff`, re-verify on `main`, remove worktree if green |

### `final_verify` runs the full suite — trust it

The unfiltered `dotnet test` suite is **green** (0 failures). Author `final_verify` to run a full `dotnet test` (optionally a feature-scoped `--filter` for fast per-phase `verify:` steps, but the final gate runs everything). Do **not** add namespace-exclusion filters to "work around flakies" — there are none. If `dotnet test` fails, that is a real regression to investigate, not noise to filter. (A handful of tests are quarantined at the source with `[Fact(Skip = "<reason + backlog link>")]` for genuinely-deep deferred bugs; xUnit skips them automatically, so they never affect a run and need no filter.)

### Workflow agents advance checkpoint state after the code commit

`advance-checkpoint.stash` (called by implementer, reviewer, and resolver flows) rewrites `.kanban/2-in-progress/<slug>/checkpoint.yaml` *after* the corresponding code commit. Reviewer also writes a new `review.md`; resolver also flips finding statuses inside `review.md`. A turn that stops there ends dirty, and the next workflow command refuses on a dirty tree.

**The implementer owns its own chore commit.** Per `.claude/agents/implementer.md` step 7, every implementer phase ends by committing the checkpoint advance (`chore(<slug>): record <id> done state`) so the tree is clean between phases. After an implementer turn the orchestrator's job is to *verify* this — `git status --porcelain` should already be clean. Only as a **fallback**, when the implementer didn't do its job (dirty `checkpoint.yaml`, no chore commit landed), does the orchestrator commit it:

```bash
# fallback only — implementer should have already committed this
git add .kanban/2-in-progress/<slug>/checkpoint.yaml
git commit -m "chore(<slug>): record <id> done state"
```

Reviewer and resolver still rely on an orchestrator follow-up commit (they advance state at the end of their turn):

```bash
# reviewer (after writing review.md)
git add .kanban/2-in-progress/<slug>/review.md .kanban/2-in-progress/<slug>/checkpoint.yaml
git commit -m "chore(<slug>): land review.md (<severity counts>)"

# resolver (after a fix commit)
git add .kanban/2-in-progress/<slug>/review.md .kanban/2-in-progress/<slug>/checkpoint.yaml
git commit -m "chore(<slug>): record <Fxx> fixed"
```

### Subagents can return interrupted mid-edit reports

The `implementer`, `resolver`, and `architect` agents occasionally return their final message mid-action — the tree is partially edited, no commit landed, no verify ran. After every subagent turn, check `git status --porcelain` and `git log -1 --oneline` to confirm the expected commit landed. If the tree is dirty with in-scope files but no commit appears, the orchestrator must finish the work: run the union of the agent's verify commands, commit using the agent's intended message format, write the corresponding `review.md` / `checkpoint.yaml` updates, and chore-commit those.

### Running features in parallel

Default: **one feature on `main` at a time.** Never run two agents against the same working tree — the clean-tree invariant means feature A's in-progress edits block feature B's workflow commands, and same-file edits silently overwrite with no git conflict to flag the loss. When two features must progress concurrently, give each its own git worktree + `feature/<slug>` branch, run the whole lifecycle (including `/spec`) on the branch, and integrate with `git merge --no-ff`. Parallelize only across **disjoint subsystems** (e.g. language + registry); serialize features that share hot files (two language features both touch `TokenType.cs`, `Parser.cs`, and all six visitors). Re-run `final_verify` on `main` after every merge — green-on-branch does not imply green-on-merged-`main`. Full procedure: `.claude/WORKFLOW.md` → "Running Features in Parallel".

**Orchestrating a worktree feature from another checkout (e.g. `main`).** The slash commands and helper scripts resolve paths from their *own* checkout, so a worktree feature's `2-in-progress/<slug>/` is invisible from `main` — drive it from a worktree-rooted session, or orchestrate manually:
- Dispatch `implementer`/`reviewer`/`resolver` via the Agent tool with the **absolute worktree root stated up front** (every Read/Edit/Bash/git op rooted there — they start in the main cwd otherwise). Do **not** pass `isolation: worktree`; the worktree already exists.
- Run `verify-phase.stash` / `advance-checkpoint.stash` / `promote-done.stash` from inside the worktree. `advance-checkpoint.stash` enforces `pending → in_progress → done` (a direct `pending → done` is rejected).
- `promote-done.stash` runs `final_verify` + `mv`s the dir but does **not** commit — the orchestrator commits the move + the `repo.md` Recent-Completed entry.
- Preview a held merge read-only before handoff: `git merge-tree --write-tree main feature/<slug>` (exit 0 = clean). `repo.md` (active + completed lists) is the highest-contention file — catch a conflict before the user hits it at a cold start.

### `/feature-review` diff range on a shared `main`

The skill's `git merge-base HEAD origin/main` base is misleading when `origin/main` is stale and local `main` carries interleaved sibling-feature commits — it pollutes the review diff with unrelated work. Prefer `<parent-of-first-feature-commit>..HEAD` intersected with the plan's `scope` globs as the feature boundary, and don't rely on `--grep="feat(<slug>)"` alone to enumerate the diff: feature-related commits are sometimes tagged `test(<area>)`/`docs(...)`. Hand the reviewer the scope-glob'd diff, not the raw `BASE..HEAD`.

## Specialized agents

This project's agents live in `.claude/agents/`. Under the checkpoint workflow, you typically never invoke an agent directly — the slash commands do it for you.

| Agent | Model | When the slash command dispatches it |
| ----- | ----- | ----------- |
| `architect` | Opus | `/spec` — design new feature |
| `implementer` | Sonnet | `/next-phase` — implement one phase |
| `reviewer` | Opus | `/feature-review` — produce review.md |
| `resolver` | Sonnet | `/resolve` — fix one finding |
| `stash-author` | Sonnet | Writing/non-trivially editing `.stash` code — the SOLE author of Stash; other agents delegate to it (see below) |
| `profiler` | Opus | Performance investigation, benchmarking (manual invocation) |
| `debugger` | Sonnet | Tracing runtime bugs, minimal repros (manual invocation) |
| `explore` | Haiku | Spawned by other agents for codebase search (never invoke directly) |
| `orchestrator` | — | **Deprecated.** See `.claude/agents/orchestrator.md` for redirect. |

For a small one-off change (bug fix, single test, one-file refactor), skip the checkpoint workflow and dispatch `implementer` directly with file paths.

### Writing Stash code: delegate to `stash-author`

Stash is dogfooded across this repo (`examples/`, `scripts/checkpoint/*`, fixtures), and the recurring failure is agents writing Stash from memory instead of the docs — producing plausible-but-wrong code (it has caused real bugs more than once). The fix is a single docs-first specialist:

- **All `.stash` authoring goes through the `stash-author` agent — and this binds the orchestrator too, not just sub-agents.** The most common real path is the orchestrator writing Stash inline; if you (the orchestrator) write `.stash` directly instead of delegating, you must follow the **identical docs-first protocol** in `.claude/agents/stash-author.md` (read the relevant `docs/Stash — Language Specification.md` + `Standard Library Reference.md` sections first, emit the API plan, then write). The protocol that only sub-agents follow is the protocol that gets walked around.
- **Exempt:** trivial mechanical edits (rename, whitespace, a path/command flip). New Stash logic or non-trivial edits are not exempt.
- **Gotcha memory:** `stash-author` maintains `.claude/agents/stash-author.gotchas.md` (doc/reality mismatches), each entry backed by a `Category=Gotcha` xUnit test in `Stash.Tests/Interpreting/GotchaTests.cs`.
- **`Category=Gotcha` tests are change-detectors — never exclude them from a gate.** Each asserts *current buggy* behavior, so it is green today and flips **red when the bug is fixed**; that red is the signal to flip the assertion to correct behavior and prune the gotcha entry. Excluding them defeats the entire mechanism.

### Exploration: find then refute

Explorer findings are **hypotheses, not facts**, and an explorer asked to *find* problems or opportunities is biased to find them — a finder sent to find work finds work. When an exploration's result will drive a **consequential decision** (a spec, a refactor, a prioritization) *and* the prompt is open-ended ("find X that could benefit from…"), pair the finder pass with an **adversarial refute pass**:

- **Invert the incentive — don't re-run the prompt.** A blind re-run just reproduces the bias (both passes are finders; their false agreement reads as confirmation). Hand the second pass the first pass's *specific claims* and instruct: default verdict REFUTED; confirm a claim ONLY by quoting the exact code that proves it; call out any guard the finder missed; downgrade or refute freely. Also ask "what did the finder miss?" to catch false negatives.
- **Scale to the work:** one finder → one skeptic; a batch → a batch.
- **Skip it** for cheap factual lookups ("where is X defined?", "does this exist?") and one-shot orientation — there the bias is absent and a refute pass is pure waste.

A finding that survives a skeptic *trying* to kill it is robust — lead with those; treat refuted/over-stated ones as the noise they are. This is the same adversarial-verification discipline the Workflow tool ("spawn N skeptics prompted to REFUTE") and the omission-hardening milestone already use; this rule just makes it the default for consequential ad-hoc `explore` dispatches.

## Project Memory

`.claude/repo.md` contains build state, active multi-phase work pointer (one line per feature), architecture decisions, and known gotchas. Read it when starting any multi-step task.

**Live checkpoint state does NOT live in `repo.md`.** It lives in `.kanban/2-in-progress/<slug>/checkpoint.yaml`. `repo.md` only carries a one-line pointer to active features and the historical record of completed ones.

## Additional Guidelines

@.claude/agent-tools.md
@.claude/language-changes.md
@.claude/performance.md
