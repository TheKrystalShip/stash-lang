# Stdlib Source Generator — Phase F: Global Namespace Migration

## Goal

Migrate `Stash.Stdlib/BuiltIns/GlobalBuiltIns.cs` from the hand-written `NamespaceBuilder`/`P.cs` form to the attribute-driven source-generator form used by every other namespace. Eliminate the special-case wiring (`StdlibDefinitions.GetGlobalNamespace`, `_globalsCache`, registry/provider carve-outs). The result is a single authoring shape across the entire stdlib — attributes only, with `NamespaceBuilder` retained solely as the underlying API the generator emits calls into.

## Motivation

Phases A–E migrated 47 namespaces. `GlobalBuiltIns` remained on the legacy hand-written path because it has two features no other namespace needs:

1. **Capability-conditional registration of one function** (`exit` only when `Environment` is granted).
2. **Empty namespace name** (`""`) representing globally-visible names.

Both are addressable with small, low-risk additions to the generator. Once they're in place, the "two formats" asymmetry the spec was always trying to remove disappears, the cache (`_globalsCache`) and the special-case provider/registry wiring go away, and `BuiltIns/CLAUDE.md` no longer needs the "Global is special" carve-out.

## Non-Goals

- No change to observable language behavior. Every global function/struct/enum keeps its current Stash name, signature, capability gate, and runtime semantics.
- `NamespaceBuilder` and `P.cs` are **not** removed. They remain the lower-level API that generator-emitted code calls into.
- No reshuffle of struct/enum *content* — names, fields, and field types stay identical.

## Required Generator/Runtime Changes

### F1 — Per-function capability gating

Currently capability is a namespace-level attribute argument (`[StashNamespace(Capability = …)]`). `exit` needs a per-function gate.

**Attribute change:**
```csharp
public sealed class StashFnAttribute : Attribute
{
    // existing: Name, ReturnType, Raw
    public StashCapabilities Capability { get; init; } = StashCapabilities.None;
}
```

**Model change:** add `Capability` to `FunctionModel` (in `Stash.Stdlib.Generators/NamespaceModel.cs`). Parser in `StashNamespaceGenerator.cs` reads it.

**Emission change:** `CodeEmitter.EmitFunction` registers the function unconditionally but tags it with its required capability. `NamespaceBuilder.Function` (in `Stash.Stdlib/Registration/NamespaceBuilder.cs`) accepts an optional `StashCapabilities required = None` and stores it on the resulting `BuiltInFunction`.

**Filter change:** the consumer that today filters whole namespaces by capability must also filter individual functions. Inspect `StashStdlibProvider.cs` — the namespace-include check generalizes to also strip functions whose `RequiredCapability` isn't satisfied.

**Acceptance:** an attributed function with `Capability = StashCapabilities.Environment` is registered iff that capability is in the active set. Verify by:
1. Granting `None` capabilities → `exit` not present in the namespace.
2. Granting `Environment` → `exit` present.
3. Existing `env.exit`, `process.exit` still work unchanged.

### F2 — Empty namespace name

`[StashNamespace(Name = "")]` must be allowed. Today the generator likely defaults to lowercased(class - "BuiltIns") and may reject empty.

- In `StashNamespaceGenerator.cs:91-area`, when `Name = ""` is provided explicitly, accept it.
- In `NamingRules.cs`, ensure no diagnostic fires for empty Stash namespace name when explicitly opted in.
- The generated `Define()` method calls `new NamespaceBuilder("")` — already legal at the runtime layer (`GlobalBuiltIns` does this today).

**Acceptance:** a `[StashNamespace(Name = "")]` class compiles, generates, and registers without diagnostics.

### F3 — Cross-namespace type-label resolution

Several global structs reference types declared elsewhere or peer types in the same partial class:
- `StreamingProcess.signal: Signal?` — `Signal` is a peer enum on the same class. Should already work.
- `ExecOptions.mode: ExecMode?` — peer enum. Should work.
- `ExecOptions.redirect: RedirectSpec?` — peer struct. Should work.
- `PromptContext.git: PromptGit` — peer struct. Should work.

Verify `TypeMarshaller.cs` handles `?`-suffixed Stash type labels for struct/enum field types. If not, add. (This is metadata — generator emits the string label; the runtime is what consumes nullability.)

**Acceptance:** all migrated globals compile with type labels matching the existing strings.

## Migration Body — `GlobalBuiltIns.cs` Conversion

Convert `GlobalBuiltIns.cs` to a `[StashNamespace(Name = "")]` attributed `static partial class`. Members translate as follows:

### Functions

| Current | After migration | Notes |
|---|---|---|
| `typeof(value)` | `[StashFn(Raw = true, ReturnType = "string")]` static method | Switches on `StashValue` runtime types; cannot be auto-marshalled |
| `nameof(value)` | `[StashFn(Raw = true, ReturnType = "string")]` | Same reason |
| `range(...)` | `[StashFn(Raw = true, ReturnType = "array")]` | Variadic with positional re-interpretation |
| `len(value)` | `[StashFn(Raw = true, ReturnType = "int")]` | Switches on runtime types of `StashValue.AsObj` |
| `lastError()` | `[StashFn(ReturnType = "Error")]` taking `IInterpreterContext` | Auto-marshallable |
| `hash(value)` | `[StashFn(ReturnType = "int")]` taking `StashValue` | Auto-marshallable (passthrough) |
| `secret(value)` | `[StashFn(ReturnType = "secret")]` returning `StashValue` | Auto-marshallable |
| `reveal(value)` | `[StashFn(ReturnType = "any")]` returning `StashValue` | Auto-marshallable; param type label `secret` via `[StashParam(Type = "secret")]` |
| `semver(value)` | `[StashFn(ReturnType = "semver")]` returning `StashValue` | Auto-marshallable |
| `exit(code = 0)` | `[StashFn(Capability = StashCapabilities.Environment, ReturnType = "never")]` | Uses F1 |

XML `<summary>`/`<param>` doc comments replace the literal `documentation: "..."` strings — content is mechanically preserved.

### Enums

```csharp
[StashEnum] public enum Backoff { Fixed, Linear, Exponential }
[StashEnum] public enum Signal { Hup, Int, Quit, Kill, Usr1, Usr2, Term }
[StashEnum] public enum ExecMode { Capture, Passthrough, Stream }
```

### Structs

Each `b.Struct(name, fields)` becomes a `[StashStruct]` C# record. Field types preserve current Stash type labels using `[StashField(Type = "...")]` where the C# type can't directly express the label (e.g., nullable references, named struct/enum references).

Error structs use the constants from `StashErrorTypes`:

```csharp
[StashStruct(Name = StashErrorTypes.ValueError)]
public sealed record ValueError { public string Message { get; init; } = ""; }
```

…and analogously for `TypeError`, `ParseError`, `IndexError`, `IOError`, `NotSupportedError`, `TimeoutError`, `CommandError`, `LockError`, `AliasError`, `StateError`, `CancellationError`.

Other structs: `ExecOptions`, `RedirectSpec`, `PipelineStage`, `StreamingProcess`, `SourceLoc`, `ParamInfo`, `AliasOptions`, `AliasInfo`, `PromptGit`, `PromptContext`, `RetryOptions`, `RetryContext`, `CompletionContext`, `CompletionResult`.

### Class-level non-Stash members

Stay on the partial class as plain static members — the generator only consumes attributed members:

- `public static readonly Dictionary<string, long> SignalNumbers` — referenced by `ProcessBuiltIns`.
- `internal static void EmitExitImpl(IInterpreterContext, long)` — referenced by `ProcessBuiltIns`, `EnvBuiltIns`, and the new `Exit` method itself.

## Wiring Removal

After `GlobalBuiltIns` is registered through `GeneratedStdlibRegistry.All()`, the following carve-outs are deleted (every callsite):

1. `Stash.Stdlib/StdlibDefinitions.cs`:
   - `_globalsCache` field
   - `GetGlobalNamespace(StashCapabilities)` method
2. `Stash.Stdlib/StashStdlibProvider.cs`: lines around 52 — replace the explicit `GetGlobalNamespace` call with the same path used for every other namespace.
3. `Stash.Stdlib/Registry/StdlibRegistry.cs:30` and `Registry/StdlibRegistry.Types.cs` (lines ~13, ~41, ~48): remove the `Concat(GetGlobalNamespace(...).Structs/Enums)` calls. The generated registry already includes them via the empty-named namespace.
4. `Stash.Stdlib/BuiltIns/CLAUDE.md`: remove the special-case row for `GlobalBuiltIns.cs` from the File Layout table; collapse the table to two rows (`*BuiltIns.cs` and `*Impl.cs` + `StashJsonContext.cs`).

## Invariants (must hold throughout the work)

- `dotnet build` is clean after every commit. Bisectable history.
- `dotnet test` passes after every commit. (Two known pre-existing flakies — `FuzzCorpus_PipelineOnAndOff_IdenticalOutput`, `UdpSendRecv_Loopback_ReturnsData` — may need re-run if they fail standalone.)
- No observable Stash language behavior change. The `StdlibBehaviorTests` suite is the canary.
- `StashStdlibProvider`'s capability filtering still produces the same set of registered names per capability set as before.

## Suggested Commit Sequence

Each step ends with a clean build + green tests; if the work stops mid-flight the codebase is shippable.

1. **F1a — `[StashFn(Capability = …)]` attribute + model + parser.** Generator + abstractions only. No usage yet. Verifies via existing tests.
2. **F1b — Runtime/builder support for per-function capability filter.** `NamespaceBuilder.Function` overload, `BuiltInFunction.RequiredCapability`, `StashStdlibProvider` filter logic. Still no caller. Add a unit test in `Stash.Tests` that registers a fake namespace with a gated function and asserts the filter behavior.
3. **F2 — Allow `[StashNamespace(Name = "")]`.** Generator parsing/validation only. No caller yet.
4. **F3 — Verify or extend `TypeMarshaller`** for cross-type `?`-suffixed labels. May be a no-op commit if everything already works.
5. **F4 — Author new `GlobalBuiltIns.cs` (attribute form), keep old code in place behind a compile-time toggle OR keep both files temporarily.** Cleanest path: introduce `GlobalBuiltIns` *generated* registration alongside the legacy hand-written one, asserting they produce the same `NamespaceDefinition` structure, then in the next commit delete the legacy path. (Equivalent of a strangler step.)
6. **F5 — Delete legacy wiring:** `_globalsCache`, `GetGlobalNamespace`, the three special-case callers, the legacy registration code in `GlobalBuiltIns.cs`. Update `BuiltIns/CLAUDE.md`.
7. **F6 — Documentation** — confirm `docs/Stash — Standard Library Reference.md` is unchanged (no user-visible surface change), confirm `BuiltIns/CLAUDE.md` and the parent spec are accurate, add an example of `[StashFn(Capability = …)]` to the authoring guide.

## Acceptance Criteria

- [ ] `GlobalBuiltIns.cs` declares `[StashNamespace(Name = "")]` and contains no `NamespaceBuilder` calls in its registration body.
- [ ] `GlobalBuiltIns` appears as a row in `GeneratedStdlibRegistry.g.cs`.
- [ ] `StdlibDefinitions.cs` no longer has `_globalsCache` or `GetGlobalNamespace`.
- [ ] `StashStdlibProvider` and `StdlibRegistry`/`StdlibRegistry.Types` no longer reference `GetGlobalNamespace`.
- [ ] `[StashFn(Capability = X)]` is supported and used by `exit`.
- [ ] `dotnet build` clean, `dotnet test` green.
- [ ] `BuiltIns/CLAUDE.md` no longer documents `GlobalBuiltIns.cs` as a special case.
- [ ] `examples/` and `docs/Stash — Standard Library Reference.md` unchanged (zero behavior diff).
- [ ] All current globals — `typeof`, `nameof`, `len`, `range`, `lastError`, `hash`, `secret`, `reveal`, `semver`, `exit`, plus all enums and structs — produce identical Stash-visible behavior pre- and post-migration. Spot-check via tests.

## Risks & Open Questions

- **Per-function capability filter site** — need to confirm whether the filter happens once at registry build (in `StashStdlibProvider`) or per-call (during dispatch). Today it's at registry build via namespace inclusion; per-function gating extends this naturally but the filter must run *after* the namespace is included, on each function it contains. Verify before F1b.
- **`StashErrorTypes` constants vs. attribute args** — these are `const string`, so `[StashStruct(Name = StashErrorTypes.ValueError)]` is legal at the C# level. Generator must emit the resolved string, not the symbolic reference. Confirm Roslyn's symbol API exposes the constant value during generation.
- **Cross-namespace type labels for nullable enum/struct fields** — if `TypeMarshaller` doesn't already support `?` on named-type labels in struct fields, F3 grows. Worth a 5-minute prototype before committing to the plan.
- **VS Code DAP/LSP semantic-token snapshots** — none of these test fixtures should change because Stash-visible names/signatures are identical, but `Stash.Tests` has goldens that include namespace ordering. The empty-named global namespace might land at a different alphabetical slot than its current special-case wiring produced. Audit `*Tests/*goldens` after F4.
