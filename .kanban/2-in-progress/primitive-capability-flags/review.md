# primitive-capability-flags — Review

> Produced by `/feature-review`. One finding per H2 section.
> Each finding header MUST be parseable: `## Fxx — [SEVERITY] short title`.
> `/resolve <feature> Fxx [Fyy...]` reads the selected section(s) and dispatches a Resolver.

**Scope reviewed:** commits `da4a18c` (P1) and `97337bd` (P2) on branch `main`
**Brief:** ./brief.md
**Generated:** 2026-05-24

---

## Summary

No findings. The feature ships exactly the contract the brief promised:

- `PrimitiveCapability` `[Flags]` enum with `None = 0`, `Extendable = 1 << 0` (Stash.Core/Common/PrimitiveCapability.cs).
- `PrimitiveTypeEntry` positional `sealed record` with `(Name, Description, Caps)` — a true DTO, not a tuple wrapper (Stash.Core/Common/PrimitiveTypeEntry.cs).
- `PrimitiveTypes` migrated to `PrimitiveTypeEntry[]` for both language and runtime arrays; tuples gone. `Names`, `Descriptions` projections preserve content byte-for-byte vs. the pre-refactor file (verified by diffing against `4bcfb3a~1:Stash.Core/Common/PrimitiveTypes.cs`).
- `ExtendableNames` exposed as `FrozenSet<string>` derived from `Caps.HasFlag(Extendable)`. The `Entries` accessor (pulled forward to P1) is an immutable `IReadOnlyCollection<PrimitiveTypeEntry>` backed by `ToArray()` — no leakage; entries themselves are immutable records. The accessor is computed once at type init.
- Compiler.Declarations.cs:298 now consumes `PrimitiveTypes.ExtendableNames.Contains(typeName)`; the literal switch is gone.
- `ExtendTypeCompletionProvider.BuiltInExtendableTypes` derives from `PrimitiveTypes.ExtendableNames.OrderBy(n => n, StringComparer.Ordinal).ToArray()`. Public API shape preserved; ordering verified to match the previously-pinned `["array", "dict", "float", "int", "string"]`.
- `ContextModeProvidersTests.ExtendableBuiltInTypes()` (Stash.Tests/Lsp/Completion/ContextModeProvidersTests.cs:358) reads the same property unchanged. Zero call-site churn confirmed.
- `PrimitiveCapabilityInvariantTests` is data-driven over `PrimitiveTypes.Entries`. The parser-skip mechanism is documented inline (lines 92–107, 122–129) and correctly identifies `null`/`struct`/`enum` as parser-rejected — verified empirically via the CLI (parse error vs. runtime error). The test guards against an over-broad skip via the `testedCount > 0` assertion on line 147.
- F01 supersession claim holds: a hypothetical future `widget` primitive added to `_languagePrimitives` with the wrong `Caps` value would fail one of the two invariant tests deterministically — the positive test if flagged `Extendable` without compiler acceptance, the negative test if unflagged with a parser-OK name. The drift class is structurally closed.

The `grep`-based done_when guardrails both return no matches:

```
grep -rn 'is "string" or "array" or "dict" or "int" or "float"' Stash.Bytecode/   # 0 matches
grep -rn '"array", "dict", "float", "int", "string"' Stash.Lsp/                   # 0 matches
```

Baseline tests: 9151 passed / 40 failed / 112 skipped. All 40 failures are documented flakies per `.claude/repo.md` Known Issues; none touch the feature surface (`PrimitiveCapabilityInvariantTests`, `PrimitiveTypesExtendableNamesTests`, `ContextModeProvidersTests`, `CompletionSurfaceSnapshotTests.Snapshot_AfterExtend` all green).

**Recommendation:** ready for `/done`.
