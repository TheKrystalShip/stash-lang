# Self-Registering Runtime Types

## Goal

Eliminate hand-maintained name/description tables for opaque runtime types (`StashFuture`, `StashRange`, `StashDuration`, `StashByteSize`, `StashIpAddress`, `StashSemVer`, `StashSecret`, etc.) by having each runtime type declare its Stash-visible name and description on itself. Mirror the principle used for stdlib structs/enums after Phase F: definition and description live next to the type, the registry derives.

## Motivation

After Phase F, stdlib structs/enums self-describe via `[StashStruct]` + `<summary>` doc comments, and `TypeDescriptions` derives from them. Runtime primitive types (in `Stash.Core/Runtime/Types/`) still have their names duplicated across multiple sites:

- `StashFuture.VMTypeName => "Future"` — used by `typeof()` dispatch.
- `PrimitiveTypes.Names` — hardcoded set including `"Future"`, `"range"`, `"duration"`, etc.
- `PrimitiveTypes.Descriptions` — hardcoded dictionary with description strings.
- `GlobalBuiltIns.typeof` — hardcoded `switch` on runtime type → name string.
- `GlobalBuiltIns.nameof` — hardcoded `switch` returning name strings.

Adding a new runtime type (e.g. `StashUuid`) requires touching at least four files. Drift between them is silent: a type can be added to one without the others, with no test failing until a user-facing surface (LSP hover, completion, `typeof`) returns inconsistent data.

## Non-Goals

- No change to observable Stash behavior. Every `typeof` result, every LSP hover, every type-hint accepted today must remain identical.
- No change to which types exist as primitives — the seven existing opaque runtime types stay primitives; `Future` does not become a struct.
- No change to `[StashStruct]`/`[StashEnum]` metadata flow — Phase F's stdlib pipeline is untouched.
- No new runtime type added by this work — it's a refactor, not a feature.

## Scope — affected runtime types

The "primitive opaque" runtime types in `Stash.Core/Runtime/Types/`:

| Type            | Stash name | Construction              | Lives in `PrimitiveTypes.Descriptions` |
| --------------- | ---------- | ------------------------- | -------------------------------------- |
| `StashFuture`   | `Future`   | `async fn` / `task.*`     | yes                                    |
| `StashRange`    | `range`    | `1..10` literal           | yes                                    |
| `StashDuration` | `duration` | `5s` / `1h30m` literals   | yes                                    |
| `StashByteSize` | `bytes`    | `1kb` / `4mb` literals    | yes                                    |
| `StashIpAddress`| `ip`       | `ip("1.2.3.4")` ctor      | yes                                    |
| `StashSemVer`   | `semver`   | `semver("1.2.3")` ctor    | yes                                    |
| `StashSecret`   | `secret`   | `secret(value)` ctor      | yes                                    |

Other runtime types (`StashDictionary`, `StashError`, `StashInstance`, `StashStruct`, `StashEnum`, `StashEnumValue`, `StashNamespace`, `StashTypedArray`) are also in `Runtime/Types/` but are categorically different: they back a built-in container/abstraction rather than a primitive. They keep their existing `VMTypeName` and stay out of scope here.

## Design

### A. Runtime-type metadata interface

New interface in `Stash.Core/Runtime/Protocols/IVMPrimitiveType.cs`:

```csharp
namespace Stash.Runtime.Protocols;

/// <summary>
/// Marks a runtime type as a Stash-visible opaque primitive whose name and
/// description should appear in PrimitiveTypes.Names and PrimitiveTypes.Descriptions.
/// Implemented as a static abstract pair so the registry can read them without
/// instantiating the type.
/// </summary>
public interface IVMPrimitiveType
{
    static abstract string PrimitiveTypeName { get; }
    static abstract string PrimitiveTypeDescription { get; }
}
```

Each in-scope runtime type adds the implementation:

```csharp
public class StashFuture : IVMTyped, IVMStringifiable, IVMPrimitiveType
{
    public static string PrimitiveTypeName => "Future";
    public static string PrimitiveTypeDescription =>
        "Represents an asynchronous computation that may not have completed yet. " +
        "Returned by async functions. Use `await` to get the resolved value.";
    // ...existing members...
    public string VMTypeName => PrimitiveTypeName; // collapses the duplicate
}
```

`VMTypeName` (instance) becomes a one-liner forwarder to `PrimitiveTypeName` (static), so `typeof()` dispatch via `IVMTyped` and metadata via `IVMPrimitiveType` share one string.

### B. PrimitiveTypes derivation

`Stash.Core/Common/PrimitiveTypes.cs` partitions into:

- **Language-level primitives** that aren't backed by a `Stash*` runtime type: `int`, `float`, `string`, `bool`, `byte`, `null`, `array`, `dict`, `struct`, `enum`, `function`, `namespace`, `int[]`, `float[]`, `string[]`, `bool[]`, `byte[]`. These stay literal — there's no class to attach an attribute to.
- **Runtime-type primitives**: `Future`, `range`, `duration`, `bytes`, `ip`, `semver`, `secret`. These derive from `IVMPrimitiveType` implementations.

```csharp
public static class PrimitiveTypes
{
    private static readonly (string Name, string Description)[] _languagePrimitives = new[]
    {
        ("int",       "Integer type. Whole numbers like `42`, `-7`, `0`."),
        ("float",     "Floating-point type. Decimal numbers like `3.14`, `-0.5`."),
        // …rest…
    };

    private static readonly IReadOnlyList<(string Name, string Description)> _runtimePrimitives =
        DiscoverRuntimePrimitives();

    public static readonly FrozenSet<string> Names = _languagePrimitives
        .Concat(_runtimePrimitives)
        .Select(p => p.Name)
        .ToFrozenSet();

    public static readonly FrozenDictionary<string, TypeDescription> Descriptions =
        _languagePrimitives
            .Concat(_runtimePrimitives)
            .ToFrozenDictionary(p => p.Name, p => new TypeDescription(p.Name, p.Description));

    private static IReadOnlyList<(string, string)> DiscoverRuntimePrimitives()
    {
        // Reflect over the assembly for IVMPrimitiveType implementers.
        // Read PrimitiveTypeName/PrimitiveTypeDescription via static abstract dispatch.
    }
}
```

Reflection runs once at static-init. The Core assembly is small and AOT-compatible (Core is referenced by the AOT-published CLI). If reflection becomes a problem under AOT trimming, switch to a generated registry (a small source generator in `Stash.Core.Generators` or hand-maintained `RuntimePrimitiveRegistry.cs` listing each `typeof(StashFuture)` etc. — same pattern as `GeneratedStdlibRegistry`).

### C. typeof / nameof dispatch

`GlobalBuiltIns.typeof` and `nameof` currently `switch` on runtime types and return literal strings. After this change, the switch arms for the seven primitive runtime types delegate to the static interface:

```csharp
StashFuture       => StashFuture.PrimitiveTypeName,
StashRange        => StashRange.PrimitiveTypeName,
StashDuration     => StashDuration.PrimitiveTypeName,
// …
```

Or, more idiomatically, use the existing `IVMTyped.VMTypeName` (which now forwards to `PrimitiveTypeName`) — collapsing the switch:

```csharp
IVMTyped t => t.VMTypeName,
```

This must come *after* the more specific arms (e.g. `StashEnumValue ev => $"{ev.TypeName}.{ev.MemberName}"` in `nameof`) so the specialized name shapes still win.

### D. Invariant test

In `Stash.Tests/Core/`:

```csharp
[Fact]
public void PrimitiveTypes_Names_IncludesEveryIVMPrimitiveType()
{
    var implementers = typeof(StashValue).Assembly
        .GetTypes()
        .Where(t => typeof(IVMPrimitiveType).IsAssignableFrom(t) && !t.IsInterface);

    foreach (var type in implementers)
    {
        var name = (string)type.GetProperty(nameof(IVMPrimitiveType.PrimitiveTypeName))!
            .GetGetMethod()!.Invoke(null, null)!;
        Assert.True(PrimitiveTypes.Names.Contains(name),
            $"{type.Name} declares PrimitiveTypeName={name} but PrimitiveTypes.Names is missing it");
        Assert.True(PrimitiveTypes.Descriptions.ContainsKey(name),
            $"{type.Name} declares PrimitiveTypeName={name} but PrimitiveTypes.Descriptions is missing it");
    }
}

[Fact]
public void PrimitiveTypes_VMTypeName_MatchesPrimitiveTypeName()
{
    // Every IVMPrimitiveType that also implements IVMTyped must agree on the name —
    // catches drift if someone overrides VMTypeName separately.
    foreach (var type in implementersImplementingBoth)
    {
        var instance = TryConstructForTest(type); // a tiny helper, may skip if no test ctor
        Assert.Equal(type.PrimitiveTypeName, ((IVMTyped)instance).VMTypeName);
    }
}
```

Adding a new runtime type without registering its description fails CI.

## Implementation Sequence (4 commits)

Each commit ends with `dotnet build` clean and `dotnet test` green.

### Commit 1 — `IVMPrimitiveType` interface + opt-in implementations

- Add `Stash.Core/Runtime/Protocols/IVMPrimitiveType.cs`.
- Implement on `StashFuture`, `StashRange`, `StashDuration`, `StashByteSize`, `StashIpAddress`, `StashSemVer`, `StashSecret`. Static name + description match what's currently in `PrimitiveTypes.Descriptions` verbatim.
- Update `VMTypeName` instance properties to forward to `PrimitiveTypeName` where they currently return a literal.
- No registry changes. No callsite changes.

Verify by inspection: every type's `VMTypeName` returns the same string before and after.

### Commit 2 — Runtime-primitive discovery in `PrimitiveTypes`

- Split `_primitiveTypeDescriptions` into `_languagePrimitives` (literal tuples for the non-runtime-backed ones) and `_runtimePrimitives` (discovered via reflection over `IVMPrimitiveType` implementers).
- `Names` and `Descriptions` derive from the union.
- Description text matches existing entries byte-for-byte.

Verify by adding a test that asserts `Names` and `Descriptions` are unchanged from a frozen snapshot of the previous values.

### Commit 3 — Collapse `typeof` / `nameof` cascades

- In `GlobalBuiltIns.cs`, replace the seven literal arms (`StashFuture => "Future"`, etc.) with either an `IVMTyped` fall-through or per-type delegation to `PrimitiveTypeName`. Choose the one that preserves arm ordering (specialized cases first, generic fall-through last).
- Existing tests for `typeof` / `nameof` (`StdlibBehaviorTests` covers most) must pass unchanged.

### Commit 4 — Invariant tests

- Add the two tests in section D.
- Update `Stash.Core/CLAUDE.md` to mention `IVMPrimitiveType` next to the other VM protocols.
- Update `Stash.Stdlib/BuiltIns/CLAUDE.md` if it documents the primitive list (mostly it doesn't).

## Open Questions

1. **AOT trimming** — reflection on `typeof(StashValue).Assembly.GetTypes()` may not survive `--published-aot`. The CLI is the only AOT target. If the test pass detects this (or the CLI build size guards in `build.stash` flag it), switch Commit 2 to a generated registry. Cheap to convert.

2. **Static abstract interfaces require .NET 7+** — confirm the project targets net7.0+. Check `Stash.Core.csproj`. If targeting older, drop to plain interface with instance properties (every implementer would need a singleton instance to read from — uglier but compatible).

3. **`StashSecret` doesn't currently implement `IVMTyped`** — verify before commit 1 by grepping for `IVMTyped` implementers. If absent, the `VMTypeName` collapse step doesn't apply for `StashSecret`; only `IVMPrimitiveType` matters.

4. **Should `StashError`, `StashDictionary`, `StashTypedArray`, etc. also implement `IVMPrimitiveType`?** — out of scope. They are not in `PrimitiveTypes.Descriptions` today (they have entries via `TypeDescriptions` for `Error` / `dict` / `array` etc., handled by other paths). Including them is a follow-up if the maintainer wants total uniformity.

5. **Do struct types like `StreamingProcess` need this?** — no. Stdlib structs already self-register via `[StashStruct]` + `<summary>` after Phase F. `IVMPrimitiveType` is for *runtime* types in `Stash.Core/Runtime/Types/` — types the language has built-in awareness of, with no user-facing fields.

## Acceptance Criteria

- [ ] `IVMPrimitiveType` exists in `Stash.Core/Runtime/Protocols/`.
- [ ] All seven in-scope runtime types implement it with names and descriptions matching the current `PrimitiveTypes.Descriptions` entries verbatim.
- [ ] `PrimitiveTypes.Names` and `PrimitiveTypes.Descriptions` derive their runtime-primitive entries from `IVMPrimitiveType` implementers.
- [ ] The seven literal arms in `GlobalBuiltIns.typeof` and `nameof` are replaced by static-property delegation or an `IVMTyped` fall-through.
- [ ] Two invariant tests fail CI when a runtime type implements `IVMPrimitiveType` without a `PrimitiveTypes` entry, or disagrees with `VMTypeName`.
- [ ] `dotnet build` clean, `dotnet test` green.
- [ ] No observable Stash behavior change — `typeof`, `nameof`, LSP hover, type-hint validation, completion all return identical results pre/post.

## Risks

- **Reflection at static-init** — adds a one-time scan of the Core assembly's types at startup. Measured cost should be sub-millisecond on modern hardware. If profiling flags it, replace with a generated registry (one source-gen file listing the seven types). Low risk, easy fallback.
- **Static abstract interface dispatch** — relatively new feature, but well-supported in current .NET. Unit-tested by the invariant tests themselves.
- **Adding `IVMPrimitiveType` to `StashFuture` etc. could conflict with existing `IVMTyped`** — they're orthogonal interfaces; a class can implement both without ambiguity.
