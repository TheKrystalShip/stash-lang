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
| MEDIUM   | 3     |
| LOW      | 1     |

**Re-review (pass 2, 2026-06-07):** F01–F03 are confirmed fixed. F04 (new,
MEDIUM) is opened: the F01 resolver's new conformance test
`DictKey_ValueTypedKeys_KeyByValue_PerSpecEqualityE1` is vacuous for 3 of 4
types (bytes/duration/ip literals fold to a single constant-pool entry, so
the test cannot distinguish value-keying from reference-identity keying for
those types — only the semver subtest is genuine). Spec sealing and runtime
behavior are both correct; the gap is purely in the test's guarding power.

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

## F04 — [MEDIUM] `DictKey_ValueTypedKeys_KeyByValue_PerSpecEqualityE1` is vacuous for 3 of 4 types — constant-pool interning collapses literal `1KB`/`5s`/`@10.0.0.1` to a single object

**Status:** open
**Files:** `Stash.Tests/Conformance/Equality/DictKeyConformanceTests.cs:309-335`
**Phase:** F01 fix (commit `9756804c`) — regression caught in re-review
**Commit:** 9756804c

### Observation

The F01 resolver added `DictKey_ValueTypedKeys_KeyByValue_PerSpecEqualityE1`,
which checks four value-typed key cohorts:

```csharp
var r1 = Run("let d = {}; d[1KB] = 1; let result = d[1KB];");        // bytes literal
var r2 = Run("let d = {}; d[5s] = 1; let result = d[5s];");          // duration literal
var r3 = Run("let d = {}; d[semver(\"1.2.3\")] = 1; let result = d[semver(\"1.2.3\")];");
var r4 = Run("let d = {}; d[@10.0.0.1] = 1; let result = d[@10.0.0.1];");  // ip literal
```

The intent — per the resolver's docstring and the §Equality keying clause it's
sealing — is to prove that **two distinct constructions of the same value
produce the same dict key** ("Two `5s` duration expressions, two `@10.0.0.1`
ip expressions, and two `semver("1.2.3")` expressions each produce the same
key"). For semver this works: `semver("1.2.3")` is a function call, and the
disassembler shows it compiles to two distinct `call` instructions producing
two distinct `StashSemVer` instances.

But the three literal-form cases (`1KB`, `5s`, `@10.0.0.1`) are
**constant-folded at compile time into a single deduplicated constant-pool
entry**, so both `d[1KB]` references load the same `StashValue` object.
Disassembly of `let d = {}; d[1KB] = 1; let result = d[1KB];`:

```
.const:
  [0] StashByteSize
  [1] 1
…
0003:  load.k              r3, k0                   ; StashByteSize
…
0006:  get.table           r1, r2, r3              ; r1=result
```

`r3` (the key) is loaded once from `k0` and reused for both `set.table` and
`get.table`. Same single-`k0` pattern confirmed for `5s` (`StashDuration` in
`k0`) and `@10.0.0.1` (`StashIpAddress` in `k0`).

A dict lookup with the SAME object reference for set-key and get-key passes
under reference-identity keying, value keying, **and any equality mode that
makes a value equal to itself** — so the test cannot distinguish
"`bytes`/`duration`/`ip` key by value" from "`bytes`/`duration`/`ip` key by
reference identity". If a future implementation regressed `StashByteSize` to
key by reference identity (dropping the `IEquatable`/`GetHashCode`-by-value
delegation), this test would still pass.

This is the same defect class the language-standard milestone exists to close:
a conformance test whose name and docstring assert "key by value" should
fail under "key by reference" — this one does not. The runtime behavior is
correct (a manual probe via the CLI confirms all four return `1` and the
behavior is genuinely value-based; the bytecode is doing the work via
`SameValueZero` → `StashValue.Equals` → the type's value `Equals`), and the
spec prose is correctly sealed in F01's fix. The gap is purely in the
conformance test's *guarding power* for three types.

### Why this matters

The whole charter of `language-standard-*` is "behavior implemented and tested
but absent or thin in the spec is a finding — this is the exact drift that
shipped `async` cooperative-cancellation … undocumented." The mirror image
applies: a conformance test whose only job is to lock in a spec claim must
fail when the implementation diverges from that claim. F01's resolver fix
left the spec sealed but the regression-guard partially open — three of the
four named types are guarded only by the same-reference-trivially-equals path,
not by SameValueZero → value-Equals.

The follow-on cost is real:

- A future implementer who refactors `StashByteSize` to drop value-`Equals`
  (e.g., to make it a struct keyed by ref-equal handle to a pool) will
  flip dict semantics for `bytes` keys without a single conformance test
  going red.
- F02 (sibling fix) explicitly tests `arr.includes([1.0], 1)` and
  `arr.lastIndexOf([1.0, 2, 1.0], 1)` — both genuinely require SameValueZero
  to coerce int↔float, so they cannot pass under reference identity. The
  bar for F01's test is the same.
- The task's hard rule for this re-review names it directly: "a test that
  actually exercises a round-trip (insert under one construction, retrieve
  under a second distinct construction → value found)." That bar is met for
  semver only.

This is a re-review MEDIUM, not HIGH: the spec is sealed and runtime behavior
is correct. The cost is future drift surveillance; nothing ships broken today.

### Suggested fix

For each of `bytes`, `duration`, `ip`, force two distinct constructions of
the same value. The semver subtest is the working model: a constructor-form
call (`semver("1.2.3")` × 2) produces two distinct instances at runtime.

Two construction patterns that defeat constant-pool folding:

1. **Function-returns-fresh.** Wrap the literal in a Stash function that
   returns a fresh instance on every call. The function call site is not
   constant-foldable:

   ```stash
   fn fresh_kb() { return 1KB; }
   let d = {};
   d[fresh_kb()] = 1;
   let result = d[fresh_kb()];
   ```

   A function call to user-defined `fresh_kb` cannot be folded by the
   compiler (it would require inlining + re-interning, which the optimizer
   doesn't do). Confirm with `--disassemble` that two distinct
   instructions produce the key. If two folded literals become two
   `call`-fresh instructions, the test is genuine.

2. **Conv / parse path.** If a stdlib path exists that constructs a fresh
   `StashByteSize` / `StashDuration` / `StashIpAddress` per call (e.g.
   `cli.parse` constructs distinct instances via `TryParse` — see
   `CliBuiltIns.Parse.cs:1331-1358`), thread one input through it twice.
   This is heavier; pattern (1) is the simpler path.

A working test reaches the value-`Equals` path because the two key objects
are distinct references; if `StashByteSize` keyed by reference identity, the
test would return `null`/throw, not `1`.

The resolver should `--disassemble` each subtest before/after the rewrite
to confirm the key is no longer a single `load.k k0` reuse but a
constructor/call producing distinct instances on each side of the round
trip.

### Verify

```bash
dotnet test --filter "FullyQualifiedName~DictKey_ValueTypedKeys_KeyByValue_PerSpecEqualityE1"
dotnet test --filter "Category=Conformance"
```

Plus a disassemble probe per type to confirm the test is no longer vacuous
(two distinct constructions, not a single `load.k`):

```bash
dotnet run --project Stash.Cli/ -- --disassemble -c 'fn fresh_kb() { return 1KB; } let d = {}; d[fresh_kb()] = 1; let result = d[fresh_kb()];'
# expect two distinct `call` instructions producing the keys, not a single `load.k k0` reuse
```

---
