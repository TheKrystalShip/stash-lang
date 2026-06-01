# Language spec lists `defer`/`lock` as reserved keywords, but the lexer treats them as soft

**Status:** Backlog — Bug
**Created:** 2026-06-01
**Discovery context:** Surfaced during the `readonly-modifier` design review (keyword reserved-vs-soft investigation). An `explore` agent inventorying every keyword surface found the language specification's keyword lists out of sync with `Stash.Core/Lexing/Keywords.cs`.

---

## Problem

The language specification's "Identifiers and Keywords" section classifies `defer` and `lock` as **reserved (hard) keywords**, but in the implementation they are **soft (contextual) keywords** — `Keywords.SoftKeywords` contains both, and the lexer emits them as `Identifier` tokens, so they remain usable as identifiers. A reader trusting the spec would believe `let defer = 1;` is a syntax error when in fact it compiles. The spec's contextual-keyword list is also incomplete relative to the code's soft set.

This is documentation drift, not a runtime defect: the implementation behaves consistently; the spec describes a different (stricter) language than the one that ships.

## Reproduction

```bash
# Spec says `defer`/`lock` are reserved → this should be a parse error.
# Actual: parses fine, because they are soft keywords.
$ stash -c 'let defer = 1; let lock = 2; io.println(defer + lock);'
# Expected per spec: parse error (reserved word used as identifier)
# Actual: 3
```

Cross-check the two sources:
- `docs/Stash — Language Specification.md`, "Identifiers and Keywords" section — `defer` and `lock` appear in the **reserved** list.
- `Stash.Core/Lexing/Keywords.cs` — `SoftKeywords = { "defer", "async", "await", "retry", "timeout", "elevate", "lock", "export" }`.

## Blast radius

- **Latent / low.** Affects readers of the spec, not running programs. No correctness impact; the implementation is self-consistent.
- The drift compounds slightly each time a new soft keyword is added without a spec update (the `readonly` feature adds `readonly` to the contextual list — the in-scope part — but deliberately does **not** fix the pre-existing `defer`/`lock` rows, to keep that feature's diff scoped).
- Becomes more visible if/when the spec's keyword section is used to drive a conformance test or an external tool's tokenizer.

## Root cause

The spec's keyword lists are hand-maintained and were not updated when `defer`/`lock` moved to (or were authored as) soft keywords in `Keywords.cs`. There is no enforcement tying the spec's reserved/contextual lists to `Keywords.HardKeywords` / `Keywords.SoftKeywords`, so the two can drift silently. (Contrast `LexerTests.cs`, which asserts `Keywords.HardKeywords == Lexer.KeywordNames` to prevent drift *within* the code.)

## Suggested fix

- (A) Doc-only correction — move `defer` and `lock` from the reserved list to the contextual list in the spec, and reconcile the full contextual list against `Keywords.SoftKeywords`. Cheap; fixes the symptom; leaves the drift mechanism in place.
- (B) Doc fix **plus** a meta-test that derives the spec's keyword lists from `Keywords.HardKeywords`/`SoftKeywords` (or asserts the spec section matches them), so future drift fails CI. Durable; a little machinery to parse the spec section.

Recommend **(B)** if the keyword section is worth guarding long-term (it is bounded-domain data, which the project's "no magic strings / single source of truth" doctrine says should be enforced, not just conventional). Otherwise (A) as a stopgap.

## Verification

```bash
# After the fix, the spec's reserved list must NOT contain defer/lock,
# and its contextual list must equal Keywords.SoftKeywords.
# If approach (B): a meta-test enforces this.
dotnet test --filter "FullyQualifiedName~KeywordSpecConsistency"   # (new test, approach B)
# Before the fix: must fail asserting defer/lock are mis-listed.
```

Manual check until a test exists: diff the spec's two keyword lists against `Keywords.HardKeywords` and `Keywords.SoftKeywords`.

## Related

- Surfaced by: `readonly-modifier` feature (`.kanban/2-in-progress/readonly-modifier/`) — that feature adds `readonly` to the contextual list (in scope) but does not touch the `defer`/`lock` rows (out of scope).
- Same surface: `Stash.Core/Lexing/Keywords.cs`, `docs/Stash — Language Specification.md` "Identifiers and Keywords".
- Doctrine: `CLAUDE.md` "Bounded Domains (No Magic Strings)" — keyword sets are a bounded domain with a single source of truth (`Keywords.cs`); the spec should derive from it, not duplicate-and-drift.
