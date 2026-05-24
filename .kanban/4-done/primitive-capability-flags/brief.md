# RFC: Capability metadata on `PrimitiveTypes`

> **Status:** Draft
> **Owner:** Cristian Moraru
> **Created:** 2026-05-24
> **Slug:** primitive-capability-flags

## Summary

Promote `PrimitiveTypes`' per-entry shape from an anonymous `(string Name, string Description)` tuple to a proper DTO carrying a `[Flags] PrimitiveCapability` field. Expose a derived `FrozenSet<string> ExtendableNames` and migrate both consumers of the "extendable primitives" datum — the bytecode compiler's `extend`-target acceptance check and the LSP's `ExtendTypeCompletionProvider` — to read from this single source. Lock the contract in place with an invariant test that asserts the runtime's actual `extend` acceptance set is exactly the set of primitives flagged `Extendable`.

This feature closes the drift class that produced **F01** in the `lsp-completion-providers` review (fix committed in `4bcfb3a`). F01's conservative fix replaced a buggy `PrimitiveTypes.Names`-derived list with a hardcoded mirror of the runtime's whitelist. The bug is closed, but the *structural condition* that allowed it — two independently-maintained copies of the same datum — remains. This feature removes the condition.

## Motivation

Today, the answer to "which primitives accept `extend`?" lives in two places:

1. **`Stash.Bytecode/Compilation/Compiler.Declarations.cs:298`** — the runtime authority — uses a literal switch expression:
   ```csharp
   bool isBuiltIn = typeName is "string" or "array" or "dict" or "int" or "float";
   ```
2. **`Stash.Lsp/Completion/Providers/ExtendTypeCompletionProvider.cs:41`** — the LSP surface — uses a hardcoded list:
   ```csharp
   public static readonly IReadOnlyList<string> BuiltInExtendableTypes =
       ["array", "dict", "float", "int", "string"];
   ```

Both already carry inline comments warning that they must be kept in sync. That kind of comment is exactly the smell this RFC removes. F01 happened because an earlier refactor tried to derive the LSP list from `PrimitiveTypes.Names` and got the structural-exclusion arithmetic wrong, silently advertising seven types (`byte, bytesize, duration, future, ipaddress, range, secret, semver`) the runtime rejects. The conservative fix re-pinned the list by hand, restoring the duplication.

Doing nothing keeps two consumers latched to a runtime invariant via convention rather than code. The next change to the runtime's extend-acceptance set will eventually forget to update the LSP list (or vice versa) — and there is no test that fails when that happens.

## Goals

- Single source of truth for "extendable primitives" lives on `PrimitiveTypes`.
- Both the bytecode compiler and the LSP completion provider consume that source — neither carries a duplicated list or switch.
- Per-entry capability metadata uses a `[Flags]` enum so future capabilities (e.g. `SupportsArithmetic`, `IsHashable`) can be added without schema growth — but no other capabilities are introduced here.
- A CI-level invariant test makes drift impossible: it fails if a primitive flagged `Extendable` is rejected by the compiler, or if a primitive *not* flagged `Extendable` is accepted.
- The runtime behavior set is preserved exactly: `string, array, dict, int, float` remain accepted; everything else rejected.

## Non-Goals

- **No new capability flags in this feature.** `SupportsArithmetic`, `IsHashable`, `SupportsComparison`, etc. are separate features triggered by separate needs. Scope is the F01 drift class only — `Extendable`.
- **No migration of the `IVMPrimitiveType` static-abstract protocol pattern.** It works correctly for runtime opaque primitives (`StashFuture`, `StashRange`, `StashDuration`, `StashByteSize`, `StashIpAddress`, `StashSemVer`, `StashSecret`). The new `Caps` field is an additional column on each `PrimitiveTypes` entry, written inline for both language-level and runtime primitives. The `Read<T>()` helper continues to feed the runtime rows; it simply produces entries with `Caps = None`.
- **No public-API change to `ExtendTypeCompletionProvider.BuiltInExtendableTypes`.** `Stash.Tests/Lsp/Completion/ContextModeProvidersTests.cs:359` already calls it; the symbol stays. Its initialization changes from a literal to a projection of `PrimitiveTypes.ExtendableNames`.
- **No broadening of the runtime's `extend` acceptance set.** Five types in, five types out. Behavior preservation is the contract.
- **Does not subsume any open work in `lsp-completion-providers`.** This feature is the architectural follow-up; it cannot start until that feature reaches `/done`. F01 is already closed.

## Design

### Surface

#### `Stash.Common.PrimitiveCapability`

A `[Flags]` enum carrying per-primitive capability bits. Initial member is `Extendable`. `None = 0` provides the default for entries that opt into no capabilities.

```csharp
namespace Stash.Common;

[Flags]
public enum PrimitiveCapability
{
    None       = 0,
    Extendable = 1 << 0,
}
```

#### `Stash.Common.PrimitiveTypeEntry`

A positional `record` replacing the anonymous tuple. Three fields, named, with the capability set last so the existing literal-tuple sites in `PrimitiveTypes.cs` migrate by adding one trailing argument per row.

```csharp
namespace Stash.Common;

public sealed record PrimitiveTypeEntry(
    string Name,
    string Description,
    PrimitiveCapability Caps);
```

**Naming rationale (logged below):** `PrimitiveTypeEntry` reads as a row in the primitive-types registry. `PrimitiveTypeInfo` was considered and rejected because `TypeDescription` (already in `Stash.Common`) carries the public "info" connotation for the LSP-facing API; "Entry" disambiguates this as a *registry row*.

#### `Stash.Common.PrimitiveTypes` (post-migration shape)

Both arrays move to the new DTO:

```csharp
private static readonly PrimitiveTypeEntry[] _languagePrimitives =
[
    new("int",       "Integer type. ...",                       PrimitiveCapability.Extendable),
    new("float",     "Floating-point type. ...",                PrimitiveCapability.Extendable),
    new("string",    "String type. ...",                        PrimitiveCapability.Extendable),
    new("bool",      "Boolean type. ...",                       PrimitiveCapability.None),
    new("byte",      "Byte type. ...",                          PrimitiveCapability.None),
    new("null",      "The null type. ...",                      PrimitiveCapability.None),
    new("array",     "Array type. ...",                         PrimitiveCapability.Extendable),
    new("dict",      "Dictionary type. ...",                    PrimitiveCapability.Extendable),
    new("struct",    "Struct type. ...",                        PrimitiveCapability.None),
    new("enum",      "Enum type. ...",                          PrimitiveCapability.None),
    new("function",  "Function type. ...",                      PrimitiveCapability.None),
    new("namespace", "Namespace type. ...",                     PrimitiveCapability.None),
    new("int[]",     "Typed integer array. ...",                PrimitiveCapability.None),
    new("float[]",   "Typed float array. ...",                  PrimitiveCapability.None),
    new("string[]",  "Typed string array. ...",                 PrimitiveCapability.None),
    new("bool[]",    "Typed boolean array. ...",                PrimitiveCapability.None),
    new("byte[]",    "Typed byte array. ...",                   PrimitiveCapability.None),
];

private static readonly PrimitiveTypeEntry[] _runtimePrimitives =
[
    Read<StashFuture>(),
    Read<StashRange>(),
    Read<StashDuration>(),
    Read<StashByteSize>(),
    Read<StashIpAddress>(),
    Read<StashSemVer>(),
    Read<StashSecret>(),
];

private static PrimitiveTypeEntry Read<T>() where T : IVMPrimitiveType
    => new(T.PrimitiveTypeName, T.PrimitiveTypeDescription, PrimitiveCapability.None);
```

Existing public surface stays backward compatible:

```csharp
public static readonly FrozenSet<string> Names = /* same content, projected from PrimitiveTypeEntry.Name */;

public static readonly FrozenDictionary<string, TypeDescription> Descriptions = /* unchanged content */;

/// <summary>
/// Primitives the bytecode compiler accepts as targets of an <c>extend</c> block.
/// Single source of truth — consumed by the compiler's acceptance check and by the
/// LSP's <c>ExtendTypeCompletionProvider</c>. Drift is guarded by
/// <c>PrimitiveCapabilityInvariantTests</c>.
/// </summary>
public static readonly FrozenSet<string> ExtendableNames = /* entries with Caps.HasFlag(Extendable) */;
```

#### `Stash.Bytecode/Compilation/Compiler.Declarations.cs`

```csharp
// Before:
bool isBuiltIn = typeName is "string" or "array" or "dict" or "int" or "float";

// After:
bool isBuiltIn = PrimitiveTypes.ExtendableNames.Contains(typeName);
```

#### `Stash.Lsp/Completion/Providers/ExtendTypeCompletionProvider.cs`

```csharp
// Before:
public static readonly IReadOnlyList<string> BuiltInExtendableTypes =
    ["array", "dict", "float", "int", "string"];

// After (public API preserved):
public static readonly IReadOnlyList<string> BuiltInExtendableTypes =
    PrimitiveTypes.ExtendableNames.OrderBy(n => n, StringComparer.Ordinal).ToArray();
```

The `OrderBy` preserves the previously-visible alphabetical ordering used by the test fixtures (`["array", "dict", "float", "int", "string"]`) and the snapshot suite. `FrozenSet<string>` enumeration order is not stable, so the projection must impose order explicitly.

### Semantics

- The runtime's acceptance behavior does not change. Inputs that compiled and ran today continue to compile and run; inputs that errored continue to error with the same `Cannot extend '<name>': not a known type.` message.
- LSP completion in `AfterExtend` mode emits the same five built-in suggestions in the same order as today.
- Adding a new primitive to `PrimitiveTypes` requires choosing its `PrimitiveCapability` value. Marking it `Extendable` without making the compiler accept it (or vice versa) fails the invariant test.

### Implementation Path

`Stash.Common/PrimitiveTypes` gets a per-entry DTO and a derived `ExtendableNames` set -> the bytecode compiler's `extend`-target acceptance reads from it -> the LSP's `ExtendTypeCompletionProvider.BuiltInExtendableTypes` derives from it -> an invariant test asserts that, for every primitive, the `Extendable` flag and the compiler's actual acceptance agree.

The change ripples outward from `Stash.Common` (the leaf project) without altering its API surface, so neither `Stash.Bytecode` nor `Stash.Lsp` see a breaking change.

### Alternatives Considered

- **Extend the `IVMPrimitiveType` static-abstract protocol with an `IsExtendable` member.** Rejected: language-level primitives (`int`, `float`, `string`, `array`, `dict`, `bool`, `byte`, `null`, `struct`, `enum`, `function`, `namespace`) are tagged-union variants of `StashValue` with no backing class to hang static-abstract members on. There is nothing to register. The protocol pattern is the right tool only for the seven runtime opaque types and would leave the five language-level extendable types unaddressed.
- **Keep the LSP-side list and add a runtime cross-check test, leaving the duplication.** Rejected: a test would catch *future* drift, but the goal here is to eliminate the second copy entirely. The test is part of the solution; it is not the whole solution.
- **Use bool fields per capability on the DTO (`Extendable`, `Hashable`, ...).** Rejected: every new capability would require a DTO field addition. A `[Flags]` enum is the standard C# shape for "small set of orthogonal yes/no facets" and is open-ended without schema churn.

### Risk Register

- **Frozen set enumeration order.** `FrozenSet<string>` does not guarantee iteration order; the snapshot test and `ContextModeProvidersTests` assume alphabetical. Mitigation: explicitly `OrderBy(n => n, StringComparer.Ordinal)` when projecting to `BuiltInExtendableTypes`.
- **`PrimitiveTypes` is in `Stash.Common`, used by everything.** A bug in the static initializer would cascade. Mitigation: P1 ships strictly additive — `Names` and `Descriptions` keep the same observable content. The P1 unit test pins `ExtendableNames` content before any consumer is touched.
- **Invariant test transitively compiles every primitive.** Cost is low (17 primitives, two compile attempts each), but the test must use the bytecode compiler entry point only, not require a full VM execution, so the negative cases produce a `CompileError`/`RuntimeError` at the documented point and not somewhere unrelated. Check `Stash.Tests/Bytecode/` test base for the right helper; `BytecodeTestBase.RunExpectingError` or a `Compile`-only path are both acceptable.

## Acceptance Criteria

- `PrimitiveTypes.ExtendableNames` exists and contains exactly `{string, array, dict, int, float}`.
- `Compiler.Declarations.cs` no longer carries a literal switch over type names for the `extend` acceptance check; the check goes through `PrimitiveTypes.ExtendableNames.Contains`.
- `ExtendTypeCompletionProvider.BuiltInExtendableTypes` is no longer a literal collection; it is derived from `PrimitiveTypes.ExtendableNames`.
- A new test class — `PrimitiveCapabilityInvariantTests`, sibling to `IVMPrimitiveTypeInvariantTests` in `Stash.Tests/Core/` — verifies for every primitive in `PrimitiveTypes`:
  - If flagged `Extendable`: compiling `extend <name> { fn foo() { return 1; } }` produces no error.
  - If not flagged `Extendable`: compiling/running the same shape raises a `RuntimeError` whose message contains `Cannot extend '<name>': not a known type.`.
- The existing `IVMPrimitiveTypeInvariantTests`, `ContextModeProvidersTests.ExtendableBuiltInTypes()`, `CompletionSurfaceSnapshotTests.Snapshot_AfterExtend`, and bytecode `extend` tests all pass without snapshot regeneration.

## Phases

The phase list lives in `plan.yaml`. Two phases:

- **P1** — Introduce `PrimitiveCapability`, `PrimitiveTypeEntry`, migrate `PrimitiveTypes` internals, expose `ExtendableNames`. Additive; no consumer changes.
- **P2** — Migrate the two consumers, add `PrimitiveCapabilityInvariantTests`.

## Open Questions

- None. The five extendable types are fixed by runtime behavior; the DTO/enum shapes are dictated by the requirements; the invariant test pattern is templated from `IVMPrimitiveTypeInvariantTests`.

## Decision Log

| Date | Decision | Rationale |
| --- | --- | --- |
| 2026-05-24 | `PrimitiveTypeEntry` over `PrimitiveTypeInfo` | "Info" overlaps with `TypeDescription`'s LSP-facing role; "Entry" reads as a registry row, matching the file's purpose. |
| 2026-05-24 | Positional `record` DTO over anonymous tuple | Tuple shape was explicitly rejected for adding a third field; the user wants named members. A record gives equality/printing semantics for free and is the canonical C# shape for an immutable named tuple. |
| 2026-05-24 | `[Flags] PrimitiveCapability` enum over per-capability booleans | The whole point is to admit future capabilities (`SupportsArithmetic`, `IsHashable`) without schema growth. A bool-per-capability would force a DTO field addition every time; a flags enum adds one member. |
| 2026-05-24 | Integration via `PrimitiveTypes`, not via `IVMPrimitiveType` protocol | `Stash.Core/CLAUDE.md` prohibits hardcoded `if (value is X)` type switches and prefers protocol additions, but that pattern only fits runtime opaque primitives (boxed types with backing classes). Language-level primitives (`int`, `float`, `string`, `array`, `dict`, etc.) are tagged-union variants of `StashValue` with no backing class to hang a static-abstract member on. `PrimitiveTypes` is already the registry that handles both kinds; capability metadata belongs there. |
| 2026-05-24 | Runtime behavior is authoritative | If the compiler accepts X, X must be flagged `Extendable`. If it rejects X, X must not be. The invariant test cross-checks the flag against actual compiler behavior, so the source of truth is the compiler — `PrimitiveTypes` simply mirrors it in a queryable form. |
| 2026-05-24 | Sequenced after `lsp-completion-providers` | F01 is the trigger for this work and was closed in `4bcfb3a`. Folding this into that feature would expand its scope past the review-resolve contract. Treating it as a separate feature keeps each scope tight. |
