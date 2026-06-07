# language-standard-equality — Review

> Produced by `/feature-review`. One finding per H2 section.
> Each finding header MUST be parseable: `## Fxx — [SEVERITY] short title`.

**Scope reviewed:** commits `0378bd9a..91750c8b` on branch `feature/language-standard-equality`
**Brief:** ../brief.md
**Generated:** 2026-06-07

## Summary

The feature successfully unifies five uncoordinated equality implementations behind one
named chokepoint (`Stash.Runtime.StashEquality`) with the three normative modes
(`OperatorEquals`, `SameValueZero`, `StrictEquals`) and seals §Equality (Edits E1–E4).
The implementation is sound: 14470/0/6 baseline; Conformance suite 375/0/0; the chokepoint
is well-documented, the Detect meta-test has teeth (binding-floor + fail-path self-test),
the vocabulary closure test is intact, hash/equals consistency for `SameValueZero` is
correct (NaN canonicalized via `Double.GetHashCode`, ±0 normalized to `+0.0`, numeric
equivalence class hashes uniformly), and the interning fold from value-`==` to bit-level
is the documented safe-correction direction. The two intentional behavior expansions
(arrays-and-secrets-as-dict-keys-throws → now legal by reference identity) are explicitly
authorized by the brief (Edit E1) and sanctioned by the §Secret Values L867 update.

Two Review-Priority-2 (spec-is-Law) findings remain — both **spec drift**, not behavior
defects — plus one LOW maintenance note. None are blocking the unit; all are about
sealing negative space the §Equality / §Values keying surface left thin.

| Severity | Count |
| -------- | ----- |
| CRITICAL | 0     |
| HIGH     | 0     |
| MEDIUM   | 2     |
| LOW      | 1     |

Highest-priority concern: **F01 — value-typed keying (`duration`/`bytes`/`ip`/`semver`) is
unspecified negative space in the §Equality keying clause.** A reader of L755–L768 cannot
tell whether `let d = {}; d[bytes(1024)] = 1; d[bytes(1024)]` returns `1` or `null` —
under the shipped implementation it returns `1` (these types implement
`IEquatable`+`GetHashCode` by value, and `SameValueZero`'s non-numeric branch delegates
to `StashValue.Equals` → `object.Equals` → the type's value `Equals`), but the keying
prose enumerates only strings (by value) and the seven reference-identity types,
omitting these four. No `Conformance/Equality/DictKeyConformanceTests` proves the actual
behavior either.

---

## F01 — [MEDIUM] Value-typed keying (`duration`/`bytes`/`ip`/`semver`) is unspecified

**Status:** fixed
**Fixed in:** 9756804c
**Files:** `docs/Stash — Language Specification.md:755-768`, `Stash.Tests/Conformance/Equality/DictKeyConformanceTests.cs`
**Phase:** P3 / P6 (spec-prose + conformance gap)
**Commit:** 9476aadd (P3 keying spec), 531d14b7 (P6 closing)

### Observation

The §Equality keying clause (L755–L768) enumerates two keying cohorts under
`SameValueZero`:

> For non-numeric types, the per-category equality rule governs keying:
> - Strings key by value (two `"foo"` expressions are the same key).
> - Secrets, arrays, dicts, struct instances, functions, namespaces, futures, ranges,
>   and Error values key by reference identity — …

The four value-typed Stash types `duration`, `bytes`, `ip`, `semver` are silently
absent. Per the per-category table at L716 they `==` by value (operator), and they
implement `IEquatable<T>` + `GetHashCode` by their value payload
(`StashByteSize.cs:125+132`, `StashDuration.cs:178`, `StashSemVer.cs:271`,
`StashIpAddress.cs:168`). Under `SameValueZero`, the non-numeric branch delegates to
`StashValue.Equals` → `object.Equals(_obj, other._obj)` → these types' value-`Equals`,
so:

- `let d = {}; d[bytes("1KB")] = 1; d[bytes("1KB")]` returns **`1`** (value keys
  identically; the operator behavior at L716 transfers).
- Same for two `duration("5s")` constructions, two `ip("10.0.0.1")` constructions,
  two `semver("1.2.3")` constructions.

This is the spec-is-Law gap from `AGENTS.md` → *The Specification is the Law* (positive
behavior **and** negative space) and CLAUDE.md → *normative claims need a conformance
test*. A reader of the keying clause cannot predict the behavior, and no
`Category=Conformance` test pins it (`DictKeyConformanceTests` covers
int/float/byte/string/secret/array/dict/struct/range/namespace/function — all the
enumerated cohorts — but not `duration`/`bytes`/`ip`/`semver`).

The omission is *not* a code defect — the shipped behavior is consistent with the per-
category table at L716. It is a clean Review-Priority-2 (spec-is-Law) drift: a behavior
exists and is reachable from a user script, but the spec prose covering keying does
not document it.

### Why this matters

This is the exact drift pattern that shipped `async` cooperative-cancellation,
`task.status` lifecycle, and unobserved-task reporting undocumented — sealed
behavior with a thin keying clause that omits a class of types. The
`language-standard` milestone's whole charter is to seal section-by-section, including
negative space. Leaving `duration`/`bytes`/`ip`/`semver` keying behavior live but
unspecified means the next-unit reviewer (e.g. `language-standard-functions`) has to
re-discover it, and a future implementation change (e.g. moving `bytes` to a
different keying mode) would not be caught by a conformance test.

### Suggested fix

Two coordinated edits, both small:

1. Extend the L755–L768 keying clause's "by value" bullet to enumerate the value-
   typed cohort explicitly. Suggested addition after the strings bullet:

   > - `duration`, `bytes`, `ip`, and `semver` key by value (per the per-category
   >   equality rule at L716): `let d = {}; d[bytes("1KB")] = 1; d[bytes("1KB")]`
   >   returns `1`.

2. Add a `DictKey_ValueTypedKeys_KeyByValue_PerSpecEqualityE1` `[Fact]` to
   `Stash.Tests/Conformance/Equality/DictKeyConformanceTests.cs` covering at minimum
   one round-trip per type:

   ```csharp
   Assert.Equal(1L, Run("let d = {}; d[duration(\"5s\")] = 1; let result = d[duration(\"5s\")];"));
   Assert.Equal(1L, Run("let d = {}; d[bytes(\"1KB\")] = 1; let result = d[bytes(\"1KB\")];"));
   Assert.Equal(1L, Run("let d = {}; d[semver(\"1.2.3\")] = 1; let result = d[semver(\"1.2.3\")];"));
   ```

### Verify

```bash
dotnet test --filter "FullyQualifiedName~DictKeyConformanceTests"
dotnet test --filter "Category=Conformance"
```

Plus a docs-grep that the new bullet survives a regeneration:

```bash
grep -n "duration\|bytes\|semver" "docs/Stash — Language Specification.md" | grep -i "key"
```

---

## F02 — [MEDIUM] `arr.unique`'s SameValueZero dedup direction is sealed in conformance but absent from spec prose

**Status:** fixed
**Fixed in:** 9756804c
**Files:** `docs/Stash — Language Specification.md:734-738`, `Stash.Stdlib/BuiltIns/ArrBuiltIns.cs:627-655`
**Phase:** P2 (membership flip)
**Commit:** 53240e57 (P2)

### Observation

The P2 implementer migrated `arr.unique` (both branches at `ArrBuiltIns.cs:627` and
`:654`) from `RuntimeValues.IsEqual` to `StashEquality.SameValueZeroEquals`. The
P2 phase notes called this out as a brief-discretionary decision: "Migrating it to
SameValueZero is the consistent choice (one `arr.unique([1, 1.0])` returns `[1]`),
but pin the chosen direction in the commit message + a conformance test."

The conformance side is covered (`MembershipConformanceTests.cs:310`
`ArrUnique_IntAndFloatAreDeduplicated_PerSpecEqualityMembership` asserts `arr.unique([1,
1.0])` returns `[1]`). The **spec side** is thinly covered: §Equality L734-738
enumerates "the `in` operator (when the right-hand side is an array), `arr.contains`,
`arr.indexOf`, `arr.remove`, and **related array-search built-ins**." `arr.unique`
is a *dedup*, not a search — the implicit umbrella "related array-search built-ins"
is a stretch.

This is also the omission to verify for `arr.includes` and `arr.lastIndexOf`:
both were migrated to `SameValueZero` (lines 224, 270 of the same diff), neither
is explicitly named in the spec, and neither has a conformance test (vs `unique`,
which does). The membership prose at L1369–L1372 also says only "Array element
membership uses SameValueZero equality" with no enumeration of the affected
builtins.

### Why this matters

Same Review-Priority-2 issue as F01: observable behavior changed (`arr.unique([1,
1.0])` was previously `[1, 1.0]` under `RuntimeValues.IsEqual`; now `[1]`),
covered by conformance for one of three flipped functions, only implicitly covered
by spec prose for any of them. A future reader of §Equality cannot predict whether
`arr.unique([1, 1.0])` returns one or two elements, and no spec clause locks the
answer.

### Suggested fix

Two minimal edits:

1. Expand the L734-738 enumeration to be a closed set. Suggested replacement:

   > The `in` operator (when the right-hand side is an array), `arr.contains`,
   > `arr.includes`, `arr.indexOf`, `arr.lastIndexOf`, `arr.remove`, **and**
   > `arr.unique` (de-duplication compares elements pairwise under SameValueZero)
   > all use SameValueZero equality.

2. Add two `Category=Conformance` tests to `MembershipConformanceTests.cs`
   covering `arr.includes` and `arr.lastIndexOf` for the int/float coercion case
   (one each), to match the existing `arr.unique` test at line 310.

### Verify

```bash
dotnet test --filter "FullyQualifiedName~MembershipConformanceTests"
dotnet test --filter "Category=Conformance"
grep -n "arr.unique\|arr.includes\|arr.lastIndexOf" "docs/Stash — Language Specification.md"
```

---

## F03 — [LOW] Stale comment in `EqualityChokepointMetaTests` references non-existent exclusion entry

**Status:** fixed
**Fixed in:** 1c6f8e40
**Files:** `Stash.Tests/Conformance/Equality/EqualityChokepointMetaTests.cs:91-92`
**Phase:** P1 (meta-test landed) / P5 (KnownExemptions emptied)
**Commit:** 2fc3ca5c (initial), 98460fb8 (P5)

### Observation

The doc comment on `ScanDirectories` says:

> Excludes `Stash.Core/Common/` (non-runtime utility classes) and
> `Stash.Bytecode/VM/` (VM dispatch — covered by `ExcludedRelPaths` entry for
> `VirtualMachine.Arithmetic.cs`).

But `VirtualMachine.Arithmetic.cs` is NOT in the `ExcludedRelPaths` set (lines
109–125 contain only `StashEquality.cs`, `StashValue.cs`, and four value-type
`IEquatable` implementations). `Stash.Bytecode/VM/` is excluded from the scan
because it is not in `ScanDirectories` — there is no `ExcludedRelPaths`
mechanism in play for it.

Functionally this is benign today (a direct grep of `Stash.Bytecode/VM/*.cs`
for `.Equals(` confirms no live runtime-equality call sites remain — VM
dispatch routes through `RuntimeOps.IsEqual` → the chokepoint), so the omission
does not let a real violation through. But the comment misleads a future
maintainer about where the scan boundary lives.

### Why this matters

A LOW-severity hygiene issue. The meta-test ships green and has working teeth
(binding-floor + fail-path self-test verified). The risk is small but real:
if a future VM-dispatch refactor adds an `sv.Equals(other)` call site in
`Stash.Bytecode/VM/VirtualMachine.*.cs`, the scan silently won't flag it
because the directory isn't scanned, and the misleading comment doesn't
warn the maintainer that VM/ is an unscanned blind spot.

### Suggested fix

Update the comment to accurately describe the scan boundary. Either:

(a) Add `"Stash.Bytecode/VM"` to `ScanDirectories` and verify zero new
    violations land (the scan would then cover the dispatch layer too); OR

(b) Rewrite the comment to:

    > Excludes `Stash.Core/Common/` (non-runtime utility classes) and
    > `Stash.Bytecode/VM/` (VM dispatch — by-design unscanned because all
    > equality decisions route through `Stash.Bytecode/Runtime/RuntimeOps.cs:IsEqual`,
    > which is itself scanned and forwards to `StashEquality.OperatorEquals`).

Option (a) is the more defensive choice (the scan covers the path); option (b)
is the simpler honest-edit if the unit considers VM/ permanently out-of-scope.

### Verify

```bash
dotnet test --filter "FullyQualifiedName~EqualityChokepointMetaTests"
```

Plus, if option (a) is taken, scan-add-no-violations confirmation that the new
directory doesn't introduce findings.

---
