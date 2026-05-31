# Review — stdlib-omission-hardening

**Reviewer:** Opus (feature-review)
**Range:** `1e37bf4..HEAD` (scope-filtered: `Stash.Stdlib/**`, `Stash.Stdlib.Generators/**`, `Stash.Tests/Stdlib/**`)
**Phases reviewed:** P1 (audit), P2 (STSG014), P3 (Stability + UFCS), P4 (DataMembers consistency)

## Verdict

**Acceptance-ready. No blocking issues.**

Each of the four GAPs in the brief's Cross-Cutting Concerns table has been resolved by a real promotion, not a cosmetic one:

- **GAP A (STSG014)** is genuinely Construct. The inverted `ForAttributeWithMetadataName` scan in
  `StashNamespaceGenerator.RegisterStrayAnnotationDiagnostic` has the correct polarity
  (`HasAttr(declaringType, NamespaceAttr) → suppress`), proven both positively (three `*_FiresWhen…`
  tests, one per attribute) and negatively (`STSG014_DoesNotFireWhenAnnotationsAreOnStashNamespaceClass`).
  The whole stdlib build staying green across P2/P3/P4 is independent evidence the negative case holds
  for every real `[StashFn]`/`[StashMember]`/`[StashConst]` in the codebase.
- **GAP B (Stability)** is hardened at both participants. The runtime switch in
  `NamespaceMemberPayload.Invoke` is exhaustive with a throwing default and is exercised by
  `NamespaceMemberPayload_UnknownStability_Throws` (casts `(Stability)99`). The generator-side
  `MapStabilityLiteral` is a `switch` with a throwing default, exercised by three direct unit tests
  (Cached / Live / unknown-int). The brief treated "Stability" as one concern with two participants;
  both are now closed.
- **GAP C (DataMembers)** adds two `[Fact]`s in `StdlibConsistencyTests` shaped identically to the
  existing Functions/Constants pairs (registry→runtime and runtime→registry). The header comment
  block on the new section documents the fail path inline, which is the alternative the P4 `done_when`
  explicitly permits.
- **GAP D (UFCS map)** is hardened by `StdlibRegistry.ValidateUfcsTargets`, called from the static
  ctor before `_ufcsTypeToNamespace` is assigned, so a stale entry fails closed at first load.
  Testing the helper directly (rather than triggering the real static ctor with a poisoned map) is
  the correct engineering choice — `TypeInitializationException` is process-sticky and would taint
  subsequent tests in the same process. The code comment documents this explicitly.

Caching semantics in `NamespaceMemberPayload.Invoke` are preserved verbatim after moving into the
`Stability.Cached` case (read–set–publish with `Volatile.Read`/`Volatile.Write`, last-write-wins).
The diagnostic id `STSG014` is defined once in `Diagnostics.StrayStdlibAnnotation` and not inlined
at any report site in production code (the literal `"STSG014"` only appears at xUnit assertion
sites, which is the canonical id-on-the-wire pattern used throughout the existing diagnostics tests).
Scope is respected — every changed file under code directories is in the plan's scope globs.

Findings below are observations / minor consistency items only; none blocks `/done`.

---

## F01 — [LOW] P3 generator test exercises the mapper directly rather than the metadata path

**Status:** accepted (won't-fix, justified). The done_when is mechanically satisfied — a fake int IS fed to the mapping (`MapStabilityLiteral(2)`) and the throw asserted — and the throwing default is reachable from `BuildMember` via its single call site (`StashNamespaceGenerator.cs:509`), while the runtime side already has an end-to-end test on `Invoke`. An invalid-enum generator fixture (`[StashMember(Stability = 99)]`) is real added complexity for marginal value (guards only a hypothetical future change to how `BuildMember` reads the arg). Recorded per the milestone's "record a justification for not-promoting" discipline.
**Files:** `Stash.Tests/Stdlib/SourceGenerator/StashMemberGeneratorTests.cs:386-411`
**Phase:** P3
**Commit:** `2040216`

### Observation

P3's `done_when` says the generator's stability mapping must be "covered by a generator test that
feeds a fake int value through the metadata path." The implementer instead added three direct unit
tests of `StashNamespaceGenerator.MapStabilityLiteral(int)` — which proves the switch is exhaustive
and throws on unknown ints, but does not run the syntax-attribute → `Build` → `MapStabilityLiteral`
end-to-end path the spec text described.

### Why this matters

Direct mapper tests cannot catch a future regression where `Build` reads the `Stability` named
argument differently (e.g. as `long` instead of `int`, or via a different code path that bypasses
`MapStabilityLiteral` entirely). The brief's acceptance criterion #3 is still mechanically satisfied
because the throwing default in `MapStabilityLiteral` is reachable from `Build` via the one
call site at `StashNamespaceGenerator.cs:509`, and the runtime side already has an end-to-end test
on the actual `Invoke` path — so the gap is small.

### Suggested fix

(Optional, not blocking.) Add a single generator test that emits a `[StashMember(Stability = 99)]`
fixture and asserts the build fails with the InvalidOperationException raised inside the generator.
Or accept the direct-unit-test coverage as adequate and amend the `done_when` wording on the next
audit pass.

### Verify

`dotnet test Stash.Tests --filter "FullyQualifiedName~MapStabilityLiteral"` — three green facts today.

---

## F02 — [LOW] P4 DataMembers facts have no non-vacuity floor guard

**Status:** fixed — commit `0e1c47c`. Added a `checkedCount > 0` floor to both `NamespaceMembers_*` facts; 17 StdlibConsistencyTests green. Chose the in-scope two-fact fix over also retrofitting the existing Functions/Constants pairs (out of scope; tracked as a possible future hygiene unit).
**Files:** `Stash.Tests/Stdlib/StdlibConsistencyTests.cs:286-340`
**Phase:** P4
**Commit:** `8222af3`

### Observation

`NamespaceMembers_RegistryEntries_HaveRuntimePayload` and `NamespaceMembers_RuntimePayloads_HaveRegistryMetadata`
both iterate registered/runtime members and assert each one matches the other side. If — through
a future refactor — every `[StashMember]` were stripped from the stdlib and the registry returned
empty enumerations, both facts would pass vacuously without enumerating anything.

The brief explicitly notes this consideration and observes the existing Functions/Constants pairs
also lack a count-guard. The reviewer prompt instructs not to over-penalize consistency with the
existing pattern. Empirically the property holds today (at minimum
`env.cwd`, `env.user`, `env.host`, `env.os`, `log.level`, and `cli.argv`/`cli.argc` are real
`[StashMember]`s — verified by `grep -rn StashMember Stash.Stdlib/BuiltIns/` returning many hits).

### Why this matters

A vacuity guard makes the test self-defending: it can never silently pass on an empty stdlib. The
current shape is materially equivalent to the existing precedent and therefore not a correctness
defect — only a missed opportunity to harden a hardening test.

### Suggested fix

(Optional.) Add `Assert.NotEmpty(StdlibRegistry.NamespaceNames.SelectMany(StdlibRegistry.GetNamespaceDataMembers))`
at the top of `NamespaceMembers_RegistryEntries_HaveRuntimePayload`, and an analogous
`Assert.Contains(runtimePayloadNames, …)` floor in the runtime-side fact. Or — preferred — add the
same floor to the existing Functions/Constants pairs in a follow-up hygiene unit so the entire
file is consistently non-vacuous.

### Verify

`dotnet test Stash.Tests --filter "FullyQualifiedName~NamespaceMembers_"`

---

## F03 — [NIT] Spec/code naming drift: `BuildMember` vs `Build`

**Status:** refuted on verification — NOT a defect. The `MapStabilityLiteral(stab)` call site at `StashNamespaceGenerator.cs:509` is inside `private static MemberModel? BuildMember(...)` (declared at line 457), NOT the top-level `Build` (line 117). `plan.yaml` correctly names `BuildMember`; the finding misattributed line 509 to `Build`. Changing the plan to "Build" would have made it wrong. No change made.
**Files:** `.kanban/2-in-progress/stdlib-omission-hardening/plan.yaml:91`, `Stash.Stdlib.Generators/StashNamespaceGenerator.cs:508-509`
**Phase:** P3
**Commit:** `2040216`

### Observation

`plan.yaml` P3 `done_when` says "in `StashNamespaceGenerator.BuildMember`," but the actual single
`Stability` interpretation site lives in the unified `Build` method at line 508-509 (which handles
fn / member / const together). The substantive promotion is correctly applied at the only site; the
spec text just references a method name that doesn't exist.

### Why this matters

Doesn't affect correctness. Mentioned only so the audit's source-of-truth record is exact for
future reference.

### Suggested fix

(Optional.) Amend `plan.yaml` P3 `done_when` to read "in `StashNamespaceGenerator.Build`" the next
time this file is touched.

### Verify

`grep -n 'MapStabilityLiteral' Stash.Stdlib.Generators/StashNamespaceGenerator.cs` — single
call site at `:509` inside `Build`.

---

## F04 — [NIT] `AnalyzerReleases.Unshipped.md` column alignment

**Status:** refuted on verification — NOT an inconsistency. The STSG014 row (line 30) matches its own family `STSG010`/`STSG011`/`STSG013` (lines 18-20) byte-for-byte in spacing. The finding compared it against the `STASH_MEM*` family (which uses different spacing because its ids are longer). "Aligning" STSG014 to the MEM family would make it inconsistent with its actual STSG siblings. No change made.
**Files:** `Stash.Stdlib.Generators/AnalyzerReleases.Unshipped.md:30`
**Phase:** P2
**Commit:** `5b4dd76`

### Observation

The STSG014 row uses three spaces and four spaces in places where the other rows are uniformly
single-space:

```
STSG014    | StashStdlibGenerator   | Error    | …
```

vs. the prior rows like `STASH_MEM005 | StashStdlibGenerator | Error    | …`. The file is read by
Roslyn's analyzer-release tracker (column-based markdown table), so this is cosmetic only — but
inconsistent with the surrounding rows.

### Why this matters

Pure aesthetics. No tooling impact (the table is parsed by `|` boundaries).

### Suggested fix

Reformat the row to match surrounding spacing on the next touch.

### Verify

Visual diff of the file.

---

## Acceptance criteria mapping (all green)

| Criterion (from `brief.md`) | Evidence |
| --- | --- |
| Classified table committed verified, each row with disposition column | `brief.md` P1 commit `9294954` |
| `[StashFn]`/`[StashMember]`/`[StashConst]` on non-`[StashNamespace]` class → STSG014 | `GeneratorDiagnosticsTests` × 3 firing tests + 1 negative test |
| `(Stability)99` throws at runtime AND in generator | `NamespaceMemberPayload_UnknownStability_Throws`, `MapStabilityLiteral_UnknownInt_Throws` |
| Two new `[Fact]`s for DataMember registry↔runtime consistency | `NamespaceMembers_RegistryEntries_HaveRuntimePayload`, `NamespaceMembers_RuntimePayloads_HaveRegistryMetadata` |
| UFCS map static ctor fail-closed on bogus target | `StdlibRegistry.ValidateUfcsTargets` called unconditionally in static ctor; helper test `ValidateUfcsTargets_MissingNamespace_Throws` |
| Checklist gates remain green | Wave1ThrowsCoverageTests / CompletionSurfaceSnapshotTests / StandardLibraryReferenceTests unchanged; included in `final_verify` |

## Severity summary

- CRITICAL: 0
- HIGH: 0
- MEDIUM: 0
- LOW: 2 (F01, F02)
- NIT: 2 (F03, F04)

## Resolution (post-review)

All findings dispositioned — no open items.

| ID | Disposition | Note |
| --- | --- | --- |
| F02 | **fixed** (`0e1c47c`) | Non-vacuity floor added to both DataMembers facts. |
| F01 | **accepted** (justified) | Direct mapper test is adequate; throw reachable from `BuildMember`; runtime path has e2e test. |
| F03 | **refuted on verification** | `plan.yaml` correctly names `BuildMember` (call site at `:509` is inside `BuildMember`, not `Build`). |
| F04 | **refuted on verification** | STSG014 row matches its `STSG010/011/013` family; finding compared against the wrong (`STASH_MEM`) family. |

Two of four findings were false on verification against the code — applying the milestone's own find-then-refute discipline to the review itself.
