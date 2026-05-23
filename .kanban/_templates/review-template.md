# {{Feature Name}} — Review

> Produced by `/feature-review`. One finding per H2 section.
> Each finding header MUST be parseable: `## Fxx — [SEVERITY] short title`.
> `/resolve <feature> Fxx [Fyy...]` reads the selected section(s) and dispatches a Resolver.

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
