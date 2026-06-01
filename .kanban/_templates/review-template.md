# {{Feature Name}} — Review

> Produced by `/feature-review`. One finding per H2 section.
> Each finding header MUST be parseable: `## Fxx — [SEVERITY] short title`.
> `/resolve <feature> Fxx [Fyy...]` reads the selected section(s) and dispatches a Resolver.
>
> **Finding `**Status:**` lifecycle** (the promotion gate enforces this — see `promote-gate.stash`):
> - `open` — not yet addressed. **Blocks `/done`.**
> - `fixed` — resolved in code; carries a `**Fixed in:** <sha>` line. Set by `/resolve`.
> - `accepted` — a deliberate, human-recorded decision to ship without fixing. Set ONLY by a human
>   via `/accept <feature> <Fxx> <reason>`. Requires an `**Accepted because:** <reason>` line, and a
>   backlog stub for any deferred work. **CRITICAL findings can NEVER be `accepted`** — they must be
>   fixed or the run stops. The autopilot never self-accepts.
> - Any other value (typos, `wontfix`, …) is rejected by the gate — it fails closed.

**Scope reviewed:** commits `{{base}}..{{head}}` on branch `{{branch}}`
**Brief:** ../brief.md
**Generated:** {{YYYY-MM-DD HH:MM}}

---

## F01 — [CRITICAL] short title

**Status:** open
**Files:** `path/to/file.cs:123`, `path/to/other.cs:45`
**Phase:** 1B
**Commit:** abc1234

### Observation

What is wrong and where.

### Why this matters

Impact on correctness, performance, maintainability, or brief parity.

### Suggested fix

Concrete, minimal change. The Resolver agent will read selected section(s) verbatim.

### Verify

Command(s) to run after the fix to confirm:

```
dotnet test --filter ...
```

---

## F02 — [IMPORTANT] another title

**Status:** open
**Files:** ...
**Phase:** 1C
**Commit:** def5678

...
