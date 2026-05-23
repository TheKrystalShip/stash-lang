# stdlib-namespace-members — Review

> Produced by `/feature-review`. One finding per H2 section.

**Scope reviewed:** commits `3d14d1a..092b150` on branch `main`
**Brief:** ../brief.md
**Generated:** 2026-05-23 13:51

## Summary

- CRITICAL: 1
- IMPORTANT: 2
- MINOR: 1

**Test baseline verdict.** The `NamespaceMembersDapTests.NamespaceExpansion_CliNamespace_ShowsMembersWithMemberType` failure in the full-suite baseline is **not a regression**. The test passes in isolation (`dotnet test --filter FullyQualifiedName=...`) and the entire `Stash.Tests.Dap` test class (144 tests) passes cleanly when run as a class. The full-suite failure is a known parallel-test-ordering / shared-state interaction (consistent with the DAP integration pattern, which launches a real engine and inspects scopes). This matches the pre-existing infrastructure-flake pattern documented in `.claude/repo.md`. No finding is filed against the test.

The 35 `DiffPackageTests.*` and 7 registry/`WebApplicationFactory` failures are pre-existing infrastructure failures already documented in `.claude/repo.md`.

The feature does ship a real CRITICAL correctness bug, but it lies in `arr.*` non-mutating helpers, not in the DAP test.

---

## F01 — [CRITICAL] Non-mutating `arr.*` helpers reject frozen `cli.argv` with TypeError

**Status:** fixed
**Fixed in:** 9fe6347
**Files:** `Stash.Stdlib/SvArgs.cs:63`, `Stash.Stdlib/BuiltIns/ArrBuiltIns.cs` (every helper taking `List<StashValue> array`)
**Phase:** P5 (frozen-helper audit) / cross-cutting
**Commit:** e911ff6

### Observation

`SvArgs.StashList(args, index, funcName)` is the canonical extractor that the generator uses to bind a Stash array argument to a `List<StashValue>` parameter. Its body (line 63) handles only `List<StashValue>` and `StashTypedArray`, then throws `TypeError("... must be an array.")`. It does **not** recognize `StashFrozenArray`.

P5 added frozen-input rejection to 8 mutating `arr.*` helpers (push/pop/set/append/insert/remove/reverse/sort), which correctly raises a frozen-write error on a `StashFrozenArray`. However, **every non-mutating helper** that accepts a `List<StashValue>` parameter — `arr.slice`, `arr.concat`, `arr.join`, `arr.map`, `arr.filter`, `arr.forEach`, `arr.find`, `arr.reduce`, `arr.unique`, `arr.any`, `arr.every`, `arr.flat`, `arr.flatMap`, `arr.findIndex`, `arr.indexOf`, `arr.lastIndexOf`, plus the parallel variants — currently fails with `TypeError: First argument to 'arr.X' must be an array.` when handed a frozen array returned by a `DataMember` getter.

Empirical reproduction (Linux):

```stash
fn id(x) { return x; }
io.println(arr.slice(cli.argv, 0, 1));     // expected: [arg1]; actual: TypeError
io.println(arr.map(cli.argv, id));         // expected: [...]; actual: TypeError
io.println(arr.join(cli.argv, ","));       // expected: "a,b,c"; actual: TypeError
```

All three raise `TypeError: First argument to 'arr.X' must be an array.`, even though `cli.argv` is *the* canonical use case for `arr.*` helpers.

### Why this matters

This breaks the central usability promise of the feature for the most prominent v1 member. The brief's stated motivation is "structured runtime data like `cli.argv` becomes a bare member access" — but if every non-trivial use of that array (slicing, mapping, joining, filtering) now raises `TypeError`, scripts must work around the feature with manual copies, defeating the ergonomic win.

The brief's side-effect contract paragraph says reference-typed returns are *frozen* — meaning **read-only**, not "no longer accepted as an array". The CHANGELOG/example demonstrate `cli.argv[0] = "x"` rejection (correct) but the read path through `arr.*` helpers is silently broken.

This is a CRITICAL surface regression introduced by P5 (frozen wrapping at the boundary without symmetric extractor support). It is the highest-priority correctness issue in this feature.

### Suggested fix

Extend `SvArgs.StashList` (`Stash.Stdlib/SvArgs.cs:63`) to unwrap `StashFrozenArray.Items` and return the underlying list. The 8 mutating helpers that received explicit `StashFrozenArray` guards in P5 already short-circuit before reaching the extractor, so unwrapping in `StashList` is safe for them too. Verify by adding regression tests that call each non-mutating `arr.*` helper on `cli.argv` and assert the expected return (rather than `TypeError`).

Alternative: have `StashFrozenArray` implement an explicit "read-only list view" protocol the extractor consults, so future frozen-collection types compose. Either fix works; the extractor unwrap is the minimal change.

### Verify

```bash
dotnet test --filter "FullyQualifiedName~ArrBuiltInsTests|FullyQualifiedName~CliMembersTests"
# Plus the one-liner below should print [arg1] not TypeError:
echo 'io.println(arr.slice(cli.argv, 0, 1));' > /tmp/v.stash
dotnet run --project Stash.Cli/ -- /tmp/v.stash a b c
```

---

## F02 — [IMPORTANT] `dict.*` mutating helpers were not audited for frozen-input rejection

**Status:** fixed
**Fixed in:** 6d23225
**Files:** `Stash.Stdlib/BuiltIns/DictBuiltIns.cs:47` (`Set`), `:71` (`Remove`), `:80` (`Clear`); add any other in-place mutators in the same file
**Phase:** P5
**Commit:** e911ff6

### Observation

P5's `done_when` (plan.yaml line 176) literally enumerates: *"Every mutating helper in the `arr.*` and `dict.*` namespaces rejects frozen-collection inputs upfront with the standard frozen-write error: a regression test invokes `arr.push(cli.argv, "x")` (and equivalents for `arr.pop`, `arr.set`, `arr.append`, `dict.set`, `dict.remove`, `dict.clear`, plus any other in-place mutators present in the current stdlib)..."*

P5 audited `arr.*` (8 helpers gained `StashFrozenArray` rejection). It did **not** audit `dict.*`. `DictBuiltIns.Set`, `Remove`, `Clear`, and any other in-place mutators do not check `StashDictionary.IsFrozen` and will silently mutate a frozen dictionary if one ever reaches them.

`StashDictionary` does carry an `IsFrozen` flag (`Stash.Core/Runtime/Types/StashDictionary.cs:21`), so the protocol is in place — only the audit is missing.

### Why this matters

- The done_when contract is unmet — promised audit is incomplete.
- No `[StashMember(ReturnType = "dict")]` exists in v1, so the gap is latent (no frozen dict can reach these helpers via a member today). However:
  - The brief's "side-effect contract" paragraph applies to **all** reference-typed returns regardless of type, and authors of future dict-returning members (e.g. an `env.vars` member proposed in spec future-work) would step on this silently.
  - `StashDictionary.Freeze()` is invoked by `ExecuteImportAs` on user-module namespace aliases — meaning a user import alias dict (if one is ever exposed via dict-typed re-export) is already affected.
- This is the kind of forward-looking correctness gap the brief specifically pinned in the Decision Log for P5.

### Suggested fix

Audit `Stash.Stdlib/BuiltIns/DictBuiltIns.cs` for every helper that calls `dict.Set / Remove / Clear / Update / MergeInto / SetIfAbsent / ...` (any in-place mutator). For each, add an early-exit:

```csharp
if (dict.IsFrozen)
    throw new RuntimeError("Cannot mutate a read-only dict returned by a namespace member.");
```

Add a regression test class `DictFrozenInputTests` that literally enumerates the audited helpers and uses a `StashDictionary` with `Freeze()` invoked, asserting the frozen-write error path. This mirrors the audited-helpers enumeration that the brief mandates for the symmetric `arr.*` test.

### Verify

```bash
dotnet test --filter "FullyQualifiedName~DictFrozenInputTests"
```

---

## F03 — [IMPORTANT] CSE-ineligibility name check is broader than the brief acknowledges; `log.level` user-shadowing is silent

**Status:** fixed
**Fixed in:** 970fe84
**Files:** `Stash.Bytecode/Optimization/LocalValueNumberingPass.cs:226`, `Stash.Stdlib/Registry/StdlibRegistry.cs:91`
**Phase:** P3
**Commit:** 01c0737

### Observation

The CSE-ineligibility check at `LocalValueNumberingPass.cs:226` is a pure name match on the `GetField` constant string against `StdlibRegistry.LiveMemberNames`. Today that set contains `{"cwd", "level"}` (env.cwd Live and log.level Live).

The Decision Log (brief.md 2026-05-23 (P3)) acknowledges this is "over-conservative — user struct fields with the same name also lose CSE — but correct". That framing is right for *Live* members. But the check fires on **any** `GetField` whose constant key is `"cwd"` or `"level"`, regardless of whether the static receiver is a namespace at all. Examples that lose CSE pessimistically:

- `let s: SomeStruct = ...; let a = s.level; let b = s.level;` — both reads marked opaque, even though `s` is a plain struct.
- `dict["level"]` is unaffected (it isn't a `GetField`), but `obj.level` for any object is.

That's the documented trade-off. **The undocumented hazard** is the inverse: a user who legitimately uses `level` or `cwd` as a struct field is silently denied a routine optimization, with no way to discover why. There is no diagnostic, no comment in the user-visible source, and the Decision Log is in a kanban brief the user never sees.

A subtler concern: future stdlib additions that add a Live member with a common identifier name (e.g. `time.now`, `term.cols`) would silently widen the deopt blast radius on user code. There is no test that the LiveMemberNames set stays narrow.

### Why this matters

- A perf footgun grows quietly over time as more Live members are added.
- The "name based, over-conservative" trade-off is fine *if* the set stays small AND the names are unlikely to collide with idiomatic user identifiers. `level` (logging) and `cwd` (working directory) are both moderately common as struct fields in user code.

### Suggested fix

Either:

1. **Tighten the check** to require the static receiver be a known namespace. The compiler already has this information at the emit site (the brief notes "the compiler has the type info"). Thread one bit through to LVN (a per-instruction flag in the chunk metadata, or a new `GetNamespaceMember` opcode pair) so the LVN check is namespace-receiver-only.
2. Or, **document the constraint and add a contract test**: a test that asserts `LiveMemberNames` only contains identifiers chosen with care (perhaps prefix-namespaced, or reviewed in a fixture list), and a CHANGELOG/spec note that struct fields with the same name as a Live stdlib member won't be CSE'd. Lock the contract.

Option 1 is the principled fix; option 2 is the docs-only patch.

### Verify

```bash
dotnet test --filter "FullyQualifiedName~NamespaceDotResolutionTests|FullyQualifiedName~LocalValueNumberingTests"
# A new test should assert that a user struct field named "level" or "cwd"
# is CSE-eligible (two reads collapse to one).
```

---

## F04 — [MINOR] Function-reference behavior for user-module aliases is not pinned by tests

**Status:** fixed
**Fixed in:** 970fe84
**Files:** `Stash.Tests/Language/FunctionReferenceTests.cs`, `Stash.Tests/Modules/ConstReexportTests.cs`
**Phase:** P7
**Commit:** 092b150

### Observation

The brief (§ Function References) and `FunctionReferenceTests` pin the contract that `Function`-kind namespace entries yield a callable when accessed bare. The test class covers stdlib namespaces (`io`, `str`, `math`, etc., per the P7 done_when "at least three namespaces"). It does not exercise user-module aliases:

```stash
// f.stash
export fn helper(x) { return x + 1; }

// main.stash
import "./f.stash" as f;
let h = f.helper;
io.println(h(41));     // expected: 42
```

This is the brief's stated promise that `ns.x` is uniform across stdlib and user-module receivers ("Stash side: ... function references are unchanged" + the "uniform with first-class function references" Decision Log). The unit test does not verify the user-module half.

### Why this matters

- Coverage gap, not a known break — the runtime path is uniform via `StashNamespace.VMGetField` and almost certainly works. But the brief's design coherence claim depends on it.
- If a future change to module-load freezing or alias handling breaks bare function-reference capture from a user module, no test catches it.

### Suggested fix

Add one test to `FunctionReferenceTests.cs` (or `ConstReexportTests.cs`) that:

1. Writes a minimal module file with `export fn` and an `export const`.
2. Imports as alias and captures the function reference into a local.
3. Calls the captured reference, asserts the return value.
4. Optionally: asserts `typeof(alias.fn) == "function"` for symmetry with the stdlib test.

### Verify

```bash
dotnet test --filter "FullyQualifiedName~FunctionReferenceTests|FullyQualifiedName~ConstReexportTests"
```

---
