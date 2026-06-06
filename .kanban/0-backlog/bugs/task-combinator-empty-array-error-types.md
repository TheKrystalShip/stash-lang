# task.race([]) / task.awaitAny([]) / task.awaitAny(non-Future element) throw RuntimeError, contradicting their ValueError/TypeError doc-comment crefs

**Status:** Backlog — Bug
**Created:** 2026-06-06
**Discovery context:** Surfaced as a review audit byproduct during the `language-standard-async` feature review (F06). The reviewer's `RunCapturingError` round-trip on the three combinators exposed the discrepancy between the `<exception>` cref metadata and the actual thrown type. These bugs pre-date `async-correctness` — they are not regressions from this unit.

---

## Problem

Three `task.*` combinator built-ins throw bare `RuntimeError` at runtime, but their XML doc-comment `<exception>` crefs promise `ValueError` or `TypeError`. Because the stdlib reference (`docs/Stash — Standard Library Reference.md`) is generated from that metadata, the published documentation claims the wrong exception types — a reader catching the documented type will miss the actual error.

| Builtin | Doc-comment cref | Actual impl throw | Location |
| ------- | ---------------- | ----------------- | -------- |
| `task.race([])` | `<exception cref="ValueError">` | `throw new RuntimeError("task.race() expects a non-empty array.")` | `Stash.Stdlib/BuiltIns/TaskBuiltIns.cs:279` |
| `task.awaitAny([])` | `<exception cref="ValueError">` | `throw new RuntimeError("task.awaitAny() expects a non-empty list.")` | `Stash.Stdlib/BuiltIns/TaskBuiltIns.cs:148` |
| `task.awaitAny(non-Future element)` | `<exception cref="TypeError">` | `throw new RuntimeError("First argument to 'task.awaitAny' must be a Future.")` | `Stash.Stdlib/BuiltIns/TaskBuiltIns.cs:156` |

The generated `docs/Stash — Standard Library Reference.md` propagates the wrong crefs, so users are misled about what to catch.

## Reproduction

```bash
# task.race([]) — should be ValueError, actually RuntimeError
dotnet run --project Stash.Cli/ -- -c '
try {
    task.race([]);
} catch (e) {
    io.println(e.type);
}
'
# Prints: RuntimeError (expected: ValueError)

# task.awaitAny([]) — should be ValueError, actually RuntimeError
dotnet run --project Stash.Cli/ -- -c '
try {
    task.awaitAny([]);
} catch (e) {
    io.println(e.type);
}
'
# Prints: RuntimeError (expected: ValueError)

# task.awaitAny(non-Future element) — should be TypeError, actually RuntimeError
dotnet run --project Stash.Cli/ -- -c '
try {
    task.awaitAny([42]);
} catch (e) {
    io.println(e.type);
}
'
# Prints: RuntimeError (expected: TypeError)
```

## Blast radius

- Any Stash code that catches `ValueError` or `TypeError` from these combinators will silently miss the exception (the catch block won't fire). This is latent today — no known production use — but becomes load-bearing if a caller follows the generated stdlib reference.
- The generated `docs/Stash — Standard Library Reference.md` documents the wrong error types, misleading users.
- `task.race` and `task.awaitAny` are parallel-task combinators; callers are unlikely to write empty-array calls in practice, so the blast radius is narrow and latent.

## Root cause

The three `throw new RuntimeError(...)` statements were written before (or independently of) the metadata that declares the error types. The `<exception cref="ValueError">` / `<exception cref="TypeError">` doc-comments were added as part of the `async-correctness` unit's metadata sweep but the underlying throw sites were not updated to match. This is a metadata-vs-impl mismatch, not a semantic design question.

## Suggested fix

Mechanical three-line fix in `Stash.Stdlib/BuiltIns/TaskBuiltIns.cs`:

- Line 148: `throw new RuntimeError("task.awaitAny() expects a non-empty list.")` → `throw new ValueError("task.awaitAny() expects a non-empty list.")`
- Line 156: `throw new RuntimeError("First argument to 'task.awaitAny' must be a Future.")` → `throw new TypeError("First argument to 'task.awaitAny' must be a Future.")`
- Line 279: `throw new RuntimeError("task.race() expects a non-empty array.")` → `throw new ValueError("task.race() expects a non-empty array.")`

After the change, regenerate the stdlib reference:

```bash
dotnet run --project Stash.Docs/
```

(A) Apply the mechanical fix — straightforward, low risk.

Recommend (A): the crefs are clearly correct (empty array → ValueError, wrong type → TypeError); the impl is wrong.

## Verification

```bash
# Regression tests that must go from red to green after the fix:
dotnet test --filter "FullyQualifiedName~TaskRace_EmptyArray|FullyQualifiedName~TaskAwaitAny_EmptyArray|FullyQualifiedName~TaskAwaitAny_NonFutureElement"
# (Tests should be added alongside the fix to prevent recurrence.)

# Confirm grep shows no bare RuntimeError at these sites:
grep -n "RuntimeError\|ValueError\|TypeError" Stash.Stdlib/BuiltIns/TaskBuiltIns.cs | grep -E "(awaitAny|race)"
# After fix: lines 148/156/279 must use ValueError/TypeError, not RuntimeError.

# Full suite must stay green:
dotnet test
```

## Related

- Feature `language-standard-async` — review finding F06 that filed this stub.
- `async-correctness` — the unit that added the `<exception>` cref metadata sweep (pre-dates this bug's discovery).
- `Stash.Stdlib/BuiltIns/TaskBuiltIns.cs` — the single file to change.
- `docs/Stash — Standard Library Reference.md` — generated file that propagates the wrong crefs; must be regenerated after the fix.
