## lsp-completion-providers — Review

> Produced by `/feature-review`. One finding per H2 section.

**Scope reviewed:** commits `99b7f48..0205ba4` on branch `main`
**Brief:** ./brief.md
**Generated:** 2026-05-24

---

## F01 — [IMPORTANT] `extend`-mode completion suggests seven types the runtime rejects

**Status:** open
**Files:** `Stash.Lsp/Completion/Providers/ExtendTypeCompletionProvider.cs:41-53`
**Phase:** P4
**Commit:** 549e234

### Observation

`ExtendTypeCompletionProvider` derives its list of "built-in extendable types" from
`PrimitiveTypes.Names` minus a small structural-exclusion set (`bool`, `null`,
`struct`, `enum`, `function`, `namespace`) and minus typed-array names. With the
current `PrimitiveTypes.Names`, that resolves to:

```
array, byte, bytesize, dict, duration, float, future, int, ipaddress,
range, secret, semver, string
```

The pre-refactor handler emitted only `["string", "array", "dict", "int", "float", "byte"]`.
The seven new entries (`bytesize`, `duration`, `future`, `ipaddress`, `range`,
`secret`, `semver`) are **not extendable**. I confirmed this by running each
through the CLI; every one produces:

```
RuntimeError: Cannot extend '<name>': not a known type.
```

So typing `extend ` in an editor now lists seven completions that will fail at
runtime the moment the user picks one.

### Why this matters

Brief Acceptance Criterion: "exactly the same lists as the pre-refactor handler
at the same cursor positions." The brief frames this refactor as
behavior-preserving; this is a user-visible behavioral regression introduced
inside the refactor. It also widens the surface the snapshot suite locks in —
the new P6 snapshot test (`Snapshot_AfterExtend_EmitsExtendableTypesOnly`) does
not anti-assert the wrong types and so won't catch them.

The architect's brief explicitly cites `["string", "array", "dict", "int", "float", "byte"]`
in the original `BuildExtendTypeCompletionList`; widening the set was a plan
deviation that needed validation against the runtime's actual extend-target check
and didn't get it.

### Suggested fix

Either:

1. Hardcode the canonical extendable set (`string`, `array`, `dict`, `int`,
   `float`, `byte`) in `ExtendTypeCompletionProvider` with a comment pointing at
   the runtime check that enforces it. This is what the monolith did and matches
   "no user-visible behavior change."

2. Or, if `PrimitiveTypes` is meant to be the single source of truth, add a
   `PrimitiveTypes.Extendable` (or `IsExtendable(string)`) authoritative list
   maintained alongside the runtime's actual `extend`-target whitelist, and have
   this provider consume that. Without a single source, the LSP and runtime drift
   exactly as they have here.

Whichever path, extend `Snapshot_AfterExtend_EmitsExtendableTypesOnly` with an
anti-assertion enumerating the non-extendable names so this regression class is
locked out.

### Verify

```
dotnet test --filter "FullyQualifiedName~CompletionSurfaceSnapshotTests.Snapshot_AfterExtend|FullyQualifiedName~ContextModeProvidersTests"
# Sanity-check by hand:
echo 'extend duration { fn x() { return self; } }' > /tmp/t.stash
dotnet run --project Stash.Cli/ -c Release -- /tmp/t.stash   # must still error;
# completion list should no longer suggest 'duration' in the first place.
```

---

## F02 — [MINOR] Dot-strategy gating (strategies 5 and 6 only fire when accumulated empty) is uncovered by tests

**Status:** open
**Files:** `Stash.Lsp/Completion/Providers/DotCompletionProvider.cs:76-99`, `Stash.Tests/Lsp/Completion/DotCompletionProviderTests.cs`
**Phase:** P3 / P6
**Commit:** 93bb7e3, 89087ed

### Observation

`DotCompletionProvider.Provide` gates strategies 5 (`CliSchemaDotStrategy`) and 6
(`NamespaceImportEnumDotStrategy`) on `accumulated.Count == 0`, where `accumulated`
is the union of strategy 3 + strategy 4 output. The brief calls this out as
load-bearing precedence (Risk Register entry "Dot-strategy reordering during port
silently changes 'first wins' semantics").

In the test suite, each individual strategy is tested in isolation (direct
`strategy.Apply(...)` calls) and a couple of pipeline ordering scenarios are
covered (`DotCompletionProvider_BuiltInNs_ShortCircuits_BeforeStructCheck`,
`Strategies3And4_RunInParallel`, `UfcsSkipped_ForUserDefinedStructReceiver`).
There is no test that asserts:

- Strategy 5 is **not** invoked when strategies 3+4 produced any output.
- Strategy 6 is **not** invoked when strategies 3+4 produced any output.
- Strategy 6 is invoked when strategy 5 produced nothing and 3+4 produced nothing.

A future edit that flips `accumulated.Count == 0` to `accumulated.Count > 0` (or
removes the gate entirely) would not be caught.

### Why this matters

The brief explicitly identifies this as a Medium-likelihood / Medium-impact risk
and the P3 done_when says the strategies must run in the documented order. The
implementer correctly preserved the gates in the dispatcher code; without a
test that asserts the gate's *effect*, the contract is not actually locked in.

This is Minor rather than Important because the gates are short and visible in
review, but it's a real coverage gap inside a P6 deliverable whose stated goal
is to lock the behavior down.

### Suggested fix

Add two-three tests to `DotCompletionProviderTests` that go through
`DotCompletionProvider.Provide` (not `strategy.Apply` directly) and build the
context so that:

- a CLI-schema entry exists for prefix `p` AND a user struct named `p` also
  exists → assert no CLI-schema-tagged items appear in the result
  (strategy 5 must be suppressed by the strategy-3 output).
- a CLI-schema entry exists for prefix `p` AND no struct/UFCS match → assert
  CLI-schema-tagged items appear (strategy 5 fires when accumulated is empty).
- a namespace-imported enum exists for prefix `E` AND a struct `E` also exists →
  assert no `NamespaceImportEnum`-tagged items appear (strategy 6 suppressed).

### Verify

```
dotnet test --filter "FullyQualifiedName~DotCompletionProviderTests"
```

---

## F03 — [MINOR] `CompletionItemSink.Add` stores `null` `SourceTag` as `null` `Data`, conflicting with the documented OmniSharp round-trip quirk

**Status:** open
**Files:** `Stash.Lsp/Completion/CompletionItemSink.cs:98-102`, `Stash.Lsp/CLAUDE.md` ("OmniSharp quirk: `CompletionItem.Data` null round-trip")
**Phase:** P1
**Commit:** 2dcfe08

### Observation

The sink assigns `Data = candidate.SourceTag`. `SourceTag` is `string?`; the
implicit conversion to `JToken` produces `null` when the tag is `null`. The
project-level `Stash.Lsp/CLAUDE.md` warns that a `null` `CompletionItem.Data`
"comes back as an empty `JToken` after the LSP round-trip, not as `null`" and
recommends `string.IsNullOrEmpty` for "no Data set" assertions.

The snapshot tests use `i.Data?.ToString() ?? ""`, which is consistent with that
guidance and works in-process — but the design comment on the sink
("Null when no tag is set so that callers' null checks remain meaningful") is
incorrect over the real LSP wire, and code in any external consumer (a future
LSP client extension, a snapshot test that uses `== null`) will be wrong.

### Why this matters

The brief's Decision Log Q1 says "always track SourceTag" — and in fact every
provider in the current code emits a non-null `SourceTag`, so the documented
"null Data" branch is unreachable in practice. The issue is the **comment** sets
up a foot-gun for the next provider author who forgets to set `SourceTag`. A
single missing tag would silently produce a `null` Data that round-trips as an
empty `JToken` and breaks any `Data == null` consumer downstream.

### Suggested fix

Either:

1. Make `SourceTag` non-nullable on `CompletionCandidate` (it's effectively
   required already — the sink contract depends on it for diagnostics). Failing
   the compile when a provider forgets it is preferable to a quiet wire-format
   asymmetry.

2. Or, leave `SourceTag` nullable but normalise: when null, omit the `Data`
   assignment entirely (so the OmniSharp Data null/empty quirk no longer
   matters), and update the XML doc comment on `Add` to remove the misleading
   "null checks remain meaningful" line.

### Verify

```
dotnet test --filter "FullyQualifiedName~CompletionItemSinkTests"
```

---

## F04 — [MINOR] `CompletionInterop.MapCompletionKind` maps `SymbolKind.Field` and `SymbolKind.EnumMember` to LSP kinds that the brief says should never reach the wire from `ScopedSymbolCompletionProvider`

**Status:** open
**Files:** `Stash.Lsp/Completion/CompletionInterop.cs:19-32`, `Stash.Lsp/Completion/Providers/ScopedSymbolCompletionProvider.cs:60-69`
**Phase:** P5
**Commit:** 68e9533

### Observation

`MapCompletionKind` handles `Field`, `EnumMember`, and `Method`-like kinds. The
only caller (other than the dot strategies, which legitimately surface members)
is `ScopedSymbolCompletionProvider`, which filters those kinds out via
`Accessibility == RequiresQualification` *before* calling `MapCompletionKind`.

This makes the `Field` / `EnumMember` arms of `MapCompletionKind` dead code in
the Default-mode path that the brief is designed to harden against. The sink's
defence-in-depth filter (`mode == Default && Accessibility == RequiresQualification`)
will also reject anything that slips through. So in practice no harm, but the
function's shape lets a future caller (a new "scoped symbols including members"
provider, or a third party who finds this helper) easily emit candidates that
are *invalid* in Default mode while looking type-safe.

### Why this matters

The whole architectural premise of the refactor is "presentation is
provider-side; member-kind symbols never escape to bare-identifier surfaces."
A shared helper that silently produces member-style `CompletionItemKind` values
without any mode or accessibility guard quietly invites the next regression
class the brief was written to prevent.

### Suggested fix

Move `MapCompletionKind` to a place that signals the caller's intent — e.g., a
small `BareIdentifierKind(SymbolKind)` returning `CompletionItemKind?` (null
for member-only kinds) for the Default-mode providers, and a separate
`MemberKind(SymbolKind)` for the dot strategies. Alternately, leave the function
but add an `AssertBareSafe` overload used by Default-mode callers that throws on
member kinds.

This is the kind of seam the brief's "Risk: snapshot suite proves too brittle"
register entry depends on — a stricter helper would make the snapshot
anti-assertions redundant rather than load-bearing.

### Verify

```
dotnet build Stash.Lsp
dotnet test --filter "FullyQualifiedName~CompletionItemSinkTests|FullyQualifiedName~ScopedSymbolCompletionProvider|FullyQualifiedName~CompletionSurfaceSnapshotTests"
```

---

## F05 — [NIT] P5 done_when says "CompletionDispatcher and its providers are registered as singletons"; only the dispatcher is DI-registered

**Status:** open
**Files:** `Stash.Lsp/StashLanguageServer.cs:49-78`
**Phase:** P5
**Commit:** 68e9533

### Observation

Plan P5 done_when item: "CompletionDispatcher and its providers are registered as
singletons in StashLanguageServer.cs."

Actual: `CompletionDispatcher` is registered as a singleton via a factory; the
providers are instantiated inline inside that factory (so effectively also
singletons by virtue of the dispatcher being one). They are not registered with
the DI container as `ICompletionProvider` services.

Functionally equivalent for today (no provider is injected elsewhere), but it
means a future test that wants to override one provider via DI can't — it has
to rebuild the whole pipeline dictionary. The brief's stated "obviously
support" goal for "AI suggestions, snippets, workspace-wide identifiers" implies
extensibility; today's wiring is closed.

### Why this matters

NIT, not blocking. Either update the plan to reflect the chosen wiring or
register each provider with DI and resolve them inside the factory. Worth
mentioning so the documentation and code don't drift further.

### Suggested fix

Either:

1. Add `services.AddSingleton<KeywordCompletionProvider>()` etc., and
   `services.AddSingleton<DotCompletionProvider>()`, then resolve them inside
   the factory. Six lines.

2. Or amend the brief / a follow-up note saying "providers are owned by the
   dispatcher; we deliberately don't surface them as separate DI services."

### Verify

```
dotnet build Stash.Lsp
dotnet test --filter "FullyQualifiedName~Lsp"
```
