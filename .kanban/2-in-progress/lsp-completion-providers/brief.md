# RFC: LSP Layered Completion Provider Model

> **Status:** Draft
> **Owner:** Cristian Moraru
> **Created:** 2026-05-23
> **Slug:** lsp-completion-providers

## Summary

Replace the monolithic `BuildFullCompletionList` / `HandleDotCompletion` orchestration in
`Stash.Lsp/Handlers/CompletionHandler.cs` with a pluggable `ICompletionProvider` pipeline
fed into a deduplicating `CompletionItemSink`. The handler becomes a thin dispatcher that
chooses a per-mode provider pipeline based on cursor context (default, dot, import-string,
after `is`, after `extend`) and materializes the merged candidates into the OmniSharp
`CompletionList` wire shape. No user-visible behavior change; the deliverable is an
architecture that prevents the two regressions just fixed (stdlib member leakage into
unqualified completions, duplicate stdlib namespaces) from being reintroduced one typo at
a time.

## Motivation

The current handler is ~880 lines that inlines four-to-six independent enumerations per
completion request and merges them with ad-hoc dedup logic. Two user-visible bugs landed
in the last week because of this:

1. **Stdlib member leakage** — `SymbolCollector.RegisterBuiltIns` injects stdlib struct
   fields, methods, and enum members into the global scope so hover / goto-def can resolve
   them. `BuildFullCompletionList` walked `GetVisibleSymbols` without filtering these out,
   and they appeared as unqualified suggestions. Fixed at commit `7c3f098` by re-checking
   `SymbolKind` in the handler itself.
2. **Duplicate stdlib namespaces** — `BuildFullCompletionList` enumerated
   `StdlibRegistry.NamespaceNames` and then walked `GetVisibleSymbols` which also returned
   `Kind.Namespace` entries for the same names. Fixed by a shared `HashSet<string> seen`
   across all passes inside the method.

Both fixes are stable, but they share a root cause: there is no shared contract about who
owns which surface area. Every handler that touches stdlib data is one typo away from a
regression. Two parallel improvements have landed or are landing alongside this work:

- `SymbolInfo.Accessibility` / `SymbolInfo.Origin` tags
  (`SymbolAccessibility.RequiresQualification`, `SymbolOrigin.BuiltinStdlib`). The
  properties are wired in the `SymbolInfo` constructor with auto-derived defaults; this
  RFC's design uses them rather than re-deriving from `Kind`.
- The "completion-surface snapshot tests" referenced in the kickoff brief are **not yet
  on disk** — `Stash.Tests/Lsp/CompletionSurfaceSnapshotTests.cs` does not exist. The
  current regression baseline is `Stash.Tests/Lsp/NamespaceMembersLspTests.cs` from commit
  `7c3f098`. The snapshot suite is treated here as a Phase 6 deliverable, not a
  precondition (see Decision Log).

The cost of doing nothing is continued one-line-fix regressions, an inability to add new
completion sources (AI suggestions, snippets, workspace-wide identifiers) without further
bloating one method, and a 280-line `HandleDotCompletion` with six load-bearing
fall-through branches that nobody can safely reorder.

## Goals

- Decompose completion sources into discrete providers behind one interface.
- Centralize dedup and ordering in one sink so precedence is declared in one place.
- Make stdlib visibility a producer-side concern: providers consume
  `SymbolInfo.Accessibility` / `SymbolInfo.Origin` rather than re-encoding kind tables.
- Keep the OmniSharp wire shape unchanged. The output is still a `CompletionList` of
  `CompletionItem`s, with the same registration options (`.` and `(` triggers, no resolve
  provider).
- Keep the existing dispatcher precedence: import-string → dot → after-`extend` →
  after-`is` → default. Don't move this ordering into a generic provider-self-filtering
  model — keep it explicit in the dispatcher.
- Lock down the post-refactor behavior with a snapshot suite that covers all five modes
  at canonical cursor positions.

## Non-Goals

- **Not** redesigning the resolution layer. Providers consume the existing
  `AnalysisResult` / `ScopeTree` / `StdlibRegistry`; the symbol table is not changing.
- **Not** adding AI / snippet / workspace-identifier providers. The design must obviously
  support them; we are not shipping them in this RFC.
- **Not** changing the LSP wire shape. Same `CompletionItem`, same `CompletionList`, same
  `CompletionRegistrationOptions`.
- **Not** introducing a runtime feature flag in `LspSettings`. This is an internal
  refactor; a private `_useNewPipeline` field across phases 2–4, deleted in phase 5, is
  enough (see Decision Log).
- **Not** adding lazy `CompletionItem/Resolve` handling.

## Design

The handler decomposes into three concrete shapes (`CompletionContext`,
`CompletionCandidate`, `CompletionItemSink`), one interface (`ICompletionProvider`), and a
small dispatcher that owns mode selection and pipeline composition.

### Surface

```csharp
namespace Stash.Lsp.Completion;

/// Inputs every provider gets. Cheap to construct; built once per request.
public sealed record CompletionContext(
    Uri Uri,
    int LspLine,            // 0-based LSP line
    int LspColumn,          // 0-based LSP column
    string? CurrentLine,    // null when the document is unknown
    CompletionMode Mode,    // dispatcher's classification of the cursor situation
    string? DotPrefix,      // identifier before "." in Dot mode, else null
    AnalysisResult? Analysis,  // null when no cached result is available
    char? TriggerCharacter);   // from CompletionParams.Context, if present

public enum CompletionMode
{
    /// Cursor inside an import path string ("from"/"import" + "...|...").
    ImportString,
    /// Cursor immediately after a "." that follows an identifier.
    Dot,
    /// Cursor in the type-name position after the "extend" keyword.
    AfterExtend,
    /// Cursor in the type-name position after the "is" keyword.
    AfterIs,
    /// Anything else — keywords, stdlib globals, in-scope symbols.
    Default
}

/// Pre-LSP shape that carries enough metadata for the sink to dedup and order.
/// Sink converts to OmniSharp CompletionItem on materialization.
public sealed record CompletionCandidate(
    string Label,
    LspCompletionItemKind Kind,
    string? Detail = null,
    string? Documentation = null,
    /// Lower wins. Source priority encodes provider precedence inside a mode.
    int SourcePriority = 100,
    /// Optional tag for diagnostics and snapshot-test attribution.
    string? SourceTag = null,
    /// Optional carry-through from SymbolInfo.Accessibility so the sink can
    /// reject obviously wrong candidates as defence in depth.
    SymbolAccessibility? Accessibility = null);

/// One source of candidates. Stateless; one instance per provider class per server.
public interface ICompletionProvider
{
    /// True if the provider has anything to contribute in this context.
    /// Cheap; avoids running providers that don't apply (e.g., UFCS in Default).
    bool AppliesTo(CompletionContext ctx);

    /// Enumerate the provider's candidates. May be empty.
    IEnumerable<CompletionCandidate> Provide(CompletionContext ctx);
}

/// Collects candidates and produces the final CompletionList.
/// Precedence: first Add for a label wins. Providers are invoked in priority
/// order, so the same label from a higher-priority provider always wins over
/// a lower one.
public sealed class CompletionItemSink
{
    public void Add(CompletionCandidate candidate);   // idempotent on Label
    public CompletionList Materialize();              // → OmniSharp shape
}

public sealed class CompletionDispatcher
{
    private readonly IReadOnlyDictionary<CompletionMode, IReadOnlyList<ICompletionProvider>> _pipelines;

    public CompletionList Run(CompletionContext ctx)
    {
        var sink = new CompletionItemSink();
        foreach (var p in _pipelines[ctx.Mode])
        {
            if (!p.AppliesTo(ctx)) continue;
            foreach (var c in p.Provide(ctx)) sink.Add(c);
        }
        return sink.Materialize();
    }
}
```

Mode classification lives in the dispatcher, not in providers. The current handler's
ordered `if`-chain (`IsInsideString` → dot-prefix → `IsAfterExtendKeyword` →
`IsAfterIsKeyword` → default) becomes a single `ClassifyMode(ctx)` helper that returns one
`CompletionMode`. Each provider's `AppliesTo` is a cheap secondary guard — the dispatcher
already routes only to that mode's pipeline.

### Provider taxonomy

| Provider | Mode(s) | Notes |
| --- | --- | --- |
| `KeywordCompletionProvider` | Default | Enumerates `Keywords.All`. Priority 10. |
| `StdlibFunctionCompletionProvider` | Default | Enumerates `StdlibRegistry.Functions`. Priority 20. |
| `StdlibNamespaceCompletionProvider` | Default | Enumerates `StdlibRegistry.NamespaceNames`. Priority 30. |
| `ScopedSymbolCompletionProvider` | Default | Walks `GetVisibleSymbols`. Filters out `Accessibility == RequiresQualification` and `Origin == BuiltinStdlib` (the latter is already covered by the three providers above with richer detail / docs). Priority 40. |
| `DotCompletionProvider` | Dot | One provider with internal strategy sub-objects (see below). |
| `ImportPathCompletionProvider` | ImportString | Lists `stashes/` directory entries; scoped `@scope/name` handled. |
| `IsTypeCompletionProvider` | AfterIs | Enumerates `StdlibRegistry.TypeDescriptions`. |
| `ExtendTypeCompletionProvider` | AfterExtend | Built-in extendable types + user-defined structs. |

`DotCompletionProvider` keeps the six existing fall-through branches as an ordered list of
internal `IDotStrategy` objects. The ordering is load-bearing and is documented inside that
provider:

1. `BuiltInNamespaceDotStrategy` — `StdlibRegistry.IsBuiltInNamespace(prefix)` → functions
   + data members + constants + nested enums.
2. `ImportAliasDotStrategy` — `result.NamespaceImports[prefix]` → exported top-level
   symbols.
3. `StructOrUserEnumDotStrategy` — variable / parameter / loop-var → narrowed type → user
   struct fields / methods OR built-in struct fields OR user enum members.
4. `UfcsDotStrategy` — runs in parallel with (3): `StdlibRegistry.GetUfcsNamespace(...)`
   returns a namespace whose functions become method-style completions on the receiver.
   Skipped for user-defined struct receivers.
5. `CliSchemaDotStrategy` — `result.CliSchema.TryGet(prefix)` → declared CLI field names.
6. `NamespaceImportEnumDotStrategy` — qualified `module.Enum.` pattern.

Each strategy is short (current branches are 10–40 lines apiece). We deliberately do **not**
hoist these to top-level providers because their order matters and they must all see the
same prefix-resolution work; making them peers would either duplicate that work or force a
shared mutable resolution state.

### Semantics

- **Dedup is label-based and first-wins.** Sink's `Add` is idempotent on `Label`; the
  iteration order of providers within a pipeline encodes priority. Within a provider,
  iteration order is the provider's responsibility (it sees its own enumerations).
- **Accessibility is defence in depth.** `ScopedSymbolCompletionProvider` filters on
  `Accessibility == RequiresQualification` and `Origin == BuiltinStdlib`. The sink also
  rejects any candidate whose `Accessibility == RequiresQualification` arrives in Default
  mode — belt-and-braces against future providers that forget.
- **Mode is single-valued, dispatcher-assigned.** No provider sees a different mode than
  the dispatcher chose. `AppliesTo` returning false is a no-op skip, not a fallback to
  another mode.
- **No resolver step.** `Handle(CompletionItem, …)` continues to return the request
  unchanged.
- **Concurrency.** Providers are stateless and the sink is per-request. No shared mutable
  state; safe under OmniSharp's request-parallel dispatch.

### Implementation Path

```
New types (Stash.Lsp/Completion/)
  ICompletionProvider, CompletionContext, CompletionCandidate,
  CompletionItemSink, CompletionDispatcher, CompletionMode
        │
        ▼
Default-mode providers ported from BuildFullCompletionList
  → KeywordCompletionProvider, StdlibFunctionCompletionProvider,
    StdlibNamespaceCompletionProvider, ScopedSymbolCompletionProvider
        │
        ▼
Dot-mode provider ported from HandleDotCompletion
  → DotCompletionProvider + 6 IDotStrategy sub-objects
        │
        ▼
Context-mode providers ported from import-string / extend / is paths
  → ImportPathCompletionProvider, ExtendTypeCompletionProvider,
    IsTypeCompletionProvider
        │
        ▼
Handler cutover
  CompletionHandler.Handle delegates to CompletionDispatcher;
  old private methods deleted.
        │
        ▼
Snapshot lockdown
  Stash.Tests/Lsp/CompletionSurfaceSnapshotTests.cs covering:
    Default, Dot (built-in ns, user struct, import alias, UFCS, CLI schema, ns-imported enum),
    ImportString, AfterIs, AfterExtend
```

Existing user-visible behavior must remain identical at every cutover boundary. Phases 2–4
run parallel-path: `CompletionHandler` retains a private `_useNewPipeline` boolean so the
new dispatcher can be invoked from unit tests without taking over the live request path.
Phase 5 flips the field and deletes the old code in one commit.

## Surface

(See "Surface" block in Design above — C# signatures for `CompletionContext`,
`CompletionCandidate`, `ICompletionProvider`, `CompletionItemSink`,
`CompletionDispatcher`, `CompletionMode`.)

## Semantics

(See "Semantics" block in Design above. Externally observable behavior is unchanged.)

## Acceptance Criteria

- Typing in an empty file produces keywords + stdlib globals + stdlib namespaces, exactly
  once each, no member leakage. Reproduces the fix in commit `7c3f098`.
- Typing `arr.` produces the namespace's functions and data members; typing `"hello".`
  produces UFCS string methods; both lists equal those produced by the pre-refactor
  handler at the same cursor positions.
- The `Stash.Tests/Lsp/NamespaceMembersLspTests.cs` regression suite (current baseline)
  continues to pass without modification.
- A new `CompletionSurfaceSnapshotTests` suite covers the five modes at canonical cursor
  positions and asserts the deduped, ordered candidate list.
- `BuildFullCompletionList`, `HandleDotCompletion`, `BuildTypeCompletionList`,
  `BuildExtendTypeCompletionList`, `GetImportCompletions`, `IsInsideString`,
  `IsAfterIsKeyword`, `IsAfterExtendKeyword` are no longer private methods on
  `CompletionHandler` after Phase 5. Either deleted or relocated under
  `Stash.Lsp/Completion/`.
- `dotnet build` and the filtered `dotnet test` in `final_verify` pass green.

## Phases

The phase list lives in `plan.yaml`. Summary:

- **P1 — Types and sink.** Introduce `Stash.Lsp/Completion/` with the interface and data
  shapes. No handler change yet. Unit-test the sink's dedup precedence in isolation.
- **P2 — Default-mode providers.** Port four providers behind parallel path. Old handler
  still serves requests; new pipeline exercised by unit tests.
- **P3 — Dot-mode provider.** Port `HandleDotCompletion` plus six strategies under
  `DotCompletionProvider`. Parallel-path unit-tested.
- **P4 — Context-mode providers.** Port import-string, after-`is`, after-`extend`.
  Parallel-path unit-tested.
- **P5 — Cutover.** `CompletionHandler.Handle` becomes a 30-line dispatcher call. Old
  private methods deleted. All existing LSP tests still green.
- **P6 — Snapshot lockdown.** Add `CompletionSurfaceSnapshotTests.cs` covering all five
  modes at canonical positions.

## Open Questions

- **Q1.** Should `CompletionItemSink` always track `SourceTag`, or only in `DEBUG`?
  Answer: always — it costs a single string per candidate and is invaluable when a
  user files a "wrong completion appeared" bug.
- **Q2.** Should `ImportPathCompletionProvider` get its own `Stash.Lsp.IO` abstraction
  over `Directory.GetDirectories`, or is direct FS access acceptable as today?
  Answer: keep direct FS access; it matches every other handler.
- **Q3.** When a stdlib namespace and an in-scope user symbol share a name (user shadows
  `fs` with `let fs = …`), which wins? Current handler: first-pass wins → stdlib
  namespace wins because it's enumerated before scope symbols. Recommendation: preserve
  that ordering. Settle in P2.

## Decision Log

| Date | Decision | Rationale |
| --- | --- | --- |
| 2026-05-23 | Per-mode pipeline registration, not provider self-filtering on `Mode` | Existing dispatcher's `if`-chain already encodes a single-mode-per-request decision; provider self-gating would move precedence into N providers instead of one dispatcher. Per-mode pipelines keep ordering explicit. |
| 2026-05-23 | Dot strategies are sub-objects inside `DotCompletionProvider`, not top-level providers | The six dot branches have load-bearing ordering and share prefix-resolution work. Top-level peers would duplicate that work or force shared mutable resolution state. |
| 2026-05-23 | No persistent `LspSettings` feature flag for cutover | Internal refactor with no user-visible toggle; a private `_useNewPipeline` field across phases 2–4, deleted in phase 5, is sufficient. |
| 2026-05-23 | Snapshot suite is a Phase 6 deliverable, not a precondition | `CompletionSurfaceSnapshotTests.cs` does not yet exist. The current regression baseline is `NamespaceMembersLspTests.cs` (commit `7c3f098`). The brief states this plainly so the implementer doesn't hunt for a missing file. |
| 2026-05-23 | Rely on `SymbolInfo.Accessibility` / `Origin` rather than re-deriving from `Kind` | Tags are populated in the `SymbolInfo` constructor (auto-derived default per kind, verified line 251 of `Stash.Analysis/Models/SymbolInfo.cs`). `ScopedSymbolCompletionProvider` filters on them. If they regress, snapshot tests fail loudly. |
| 2026-05-23 | Keep `final_verify` filter narrow per `stdlib-namespace-members` precedent | This feature touches no DAP / stdlib metadata; drop the DAP-only `NamespaceExpansion_CliNamespace_ShowsMembersWithMemberType` exclusion and the `dotnet run --project Stash.Docs/` step. |

## Alternatives Considered

- **Do nothing.** Continue patching regressions inline. Rejected: the two recent bugs
  share a root cause that more inline checks won't address. Each new completion source
  makes the monolith worse.
- **Extract helpers, keep monolith.** Pull each enumeration block into a private method on
  the handler. Rejected: same dedup logic, same shared `HashSet`, same regression
  surface — just spread across more methods. Doesn't enable new sources.
- **Generic provider list, providers self-gate on mode.** Single ordered provider list;
  each provider checks `ctx.Mode`. Rejected: precedence becomes implicit in provider
  iteration order across modes; harder to reason about than a per-mode pipeline table.
- **Lazy resolution via `CompletionItem/Resolve`.** Move documentation / throws rendering
  into the resolve callback. Out of scope; orthogonal optimization.

## Migration Story

- Phases 1–4 are additive. The old `CompletionHandler.Handle` path is untouched; new
  types live under `Stash.Lsp/Completion/`. Unit tests exercise the new pipeline directly.
- Phase 5 (cutover) is the only behavior-affecting commit. It must keep all existing LSP
  integration tests (`Stash.Tests/Lsp/*`) green, including the namespace-members
  regression suite. If a test diverges in phase 5, the divergence is the bug.
- Phase 6 locks the new behavior in with the snapshot suite. If a future change breaks a
  snapshot, the author must either justify the change in a Decision Log entry under the
  affected snapshot's owning RFC or fix the regression.

## Risk Register

| Risk | Likelihood | Impact | Mitigation |
| --- | --- | --- | --- |
| `Accessibility` / `Origin` not actually populated on some symbol path (e.g., imported modules) | Medium | Member-style symbols leak again | Phase 2 includes an audit and unit test that `RequiresQualification` covers Field / Method / EnumMember from every collector path. |
| Dot-strategy reordering during port silently changes "first wins" semantics | Medium | Wrong completions for ambiguous prefixes | Phase 3 ports strategies in the exact order the current method evaluates them; review checklist in plan.yaml notes. |
| Snapshot suite proves too brittle (changes whenever stdlib gains a function) | Medium | Future PRs blocked by unrelated snapshot diffs | Snapshots assert presence and ordering of a curated set of labels, not the full list. Documented in P6 done_when. |
| OmniSharp request parallelism reveals shared state in the sink | Low | Race conditions, mangled completions | Sink is per-request; providers are stateless. Verified by an explicit P1 unit test. |
| Performance regression from extra allocations (candidate records, sink dictionary) | Low | Slow completion in large files | Not a hot path. Phase 5 done_when includes a manual LSP smoke against a large example file. |

## Context for the Implementer

Files and concepts to study before phase 1:

- `Stash.Lsp/Handlers/CompletionHandler.cs` — the current monolith. ~880 lines. Five
  top-level branches in `Handle`, two large private methods (`BuildFullCompletionList`,
  `HandleDotCompletion`), plus helpers.
- `Stash.Lsp/Analysis/AnalysisEngine.cs` — entry to cached `AnalysisResult` via
  `GetCachedResult(uri)`.
- `Stash.Analysis/Models/SymbolInfo.cs` — `Accessibility`, `Origin`,
  `SymbolAccessibility`, `SymbolOrigin`. Constructor auto-derives `Accessibility` from
  `Kind`. **Do not re-derive in providers.**
- `Stash.Analysis/Models/ScopeTree.cs` — `GetVisibleSymbols(line, col)`,
  `NamespaceImports`, `All`.
- `Stash.Stdlib/Registry/StdlibRegistry.cs` — `Functions`, `NamespaceNames`,
  `GetNamespaceMembers`, `GetNamespaceDataMembers`, `GetNamespaceConstants`, `Enums`,
  `Structs`, `IsBuiltInNamespace`, `GetUfcsNamespace`, `TypeDescriptions`.
- `Stash.Tests/Lsp/NamespaceMembersLspTests.cs` — current regression baseline. Must pass
  unmodified through every phase.
- `Stash.Lsp/CLAUDE.md` — handler conventions, DI services, registration patterns.
- `.kanban/4-done/stdlib-namespace-members/plan.yaml` — canonical shape for the
  `final_verify` filter; copy with the adjustments noted in the Decision Log.
