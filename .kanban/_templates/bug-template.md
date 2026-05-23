# <Short, specific title — what's broken, not what to do about it>

**Status:** Backlog — Bug
**Created:** YYYY-MM-DD
**Discovery context:** <Which feature, review, or test surfaced this? Which agent? What were they doing when the bug surfaced?>

---

## Problem

<One or two paragraphs describing what's broken. Lead with the observable symptom, not the internal cause. A future maintainer reading this should be able to confirm the bug exists without prior context.>

## Reproduction

<Minimal, deterministic steps to observe the bug. Prefer a copy-pasteable shell session or Stash snippet over prose. If the bug is timing-dependent or only manifests under specific conditions, say so explicitly and document the conditions (parallel test execution, specific OS, specific build mode, etc.).>

```bash
# Example:
$ stash -c 'io.println(cli.argv[0] = "x")'
# Expected: ReadOnlyError
# Actual: TypeError: ...
```

## Blast radius

<Who/what is affected? Be honest about scope:
- Users of which Stash features?
- Which embedded/host scenarios?
- Latent (no real users today) vs. live (real exposure)?
- Does the bug compound over time (e.g., quietly worsens as more code uses the affected surface)?

If the bug is latent today but becomes load-bearing under a foreseeable change, name the change.>

## Root cause

<If known: cite the file:line where the issue lives and explain the mechanism in 2-3 sentences. If unknown: say so, list the hypotheses that have been considered, and what was ruled in or out.>

## Suggested fix

<One or more approaches, each with a brief sketch and a trade-off. Don't pretend to be definitive — the architect or implementer who picks this up will make the final call. If you have a recommendation, lead with it.

Format when multiple approaches exist:
- (A) Approach name — sketch — trade-off
- (B) Approach name — sketch — trade-off
- (C) ...

Recommend (X): reason.>

## Verification

<How will we know the bug is fixed? At minimum:
- A regression test that fails today and passes after the fix.
- The existing tests that must continue to pass (cross-cutting check).

A reproducible test command is more useful than prose. Examples:

```bash
dotnet test --filter "FullyQualifiedName~ProposedRegressionTestClass"
# After the fix: passes. Before: must fail with <specific assertion>.
```>

## Related

<Bullet list of links and references:
- Feature(s) that surfaced or are blocked by this bug.
- Commit(s) where the bug was introduced (if traceable).
- Other backlog items, briefs, or specs touching the same surface.
- External links (GitHub issues, RFCs) if any.>
