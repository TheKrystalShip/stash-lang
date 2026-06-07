# numeric == coerces but `in` / dict-key lookup do not — deferred SameValueZero unification

**Status:** Backlog — ready to spec
**Created:** 2026-06-06
**Discovery context:** Implementer agent during `language-standard-values` P3 (D2 fix — cross-type numeric
equality). While auditing the blast radius of the `RuntimeOps.IsEqual` change, the agent discovered that
`StashValue.Equals` (used by `Contains` / `in` operator and dict hash key lookup) is a separate, tag-strict
path that does not participate in the D2 numeric-coercion rule.

**Design status (2026-06-07):** The divergence is **sealed honestly** in
`docs/Stash — Language Specification.md` §Equality — "Membership and keying use tag-strict equality"
and pinned by `EqualityNumericConformanceTests` (the tag-strict membership tests). The spec no longer
misrepresents the behavior. This item is now a **ready-to-spec feature** for the SameValueZero
unification — pick it up with `/spec`.

---

## Problem

After the D2 fix (P3 of `language-standard-values`), the `==` and `!=` operators correctly coerce
int/float/byte cross-tag pairs by mathematical value: `1 == 1.0` is `true`, `conv.toByte(0) == 0` is
`true`. However, the `in` operator (for array membership) and dict key lookup use `StashValue.Equals`
— a separate tag-strict equality path that is **not** updated by the D2 fix. As a result:

- `1 in [1.0]` returns `false` even though `1 == 1.0` is `true`.
- A dict populated with integer key `1` does not match a lookup with float key `1.0`.
- `arr.contains([1], 1.0)` returns `false` for an array `[1]`.

This creates a cross-operator inconsistency: the answer to "is 1 equal to 1.0?" depends on which
operator is used. The spec now documents this divergence explicitly (sealed clause, §Equality).

## Reproduction

```bash
dotnet run --project Stash.Cli/ -- -c 'io.println(1 == 1.0);'           # true   (D2 honored)
dotnet run --project Stash.Cli/ -- -c 'io.println(1 in [1.0]);'         # false  (tag-strict)
dotnet run --project Stash.Cli/ -- -c 'io.println(arr.contains([1], 1.0));'  # false  (tag-strict)
dotnet run --project Stash.Cli/ -- -c 'let d = {}; d[1] = "a"; io.println(d[1.0]);'  # null (distinct keys)
```

## Ratified decision

The user evaluated the options and chose: **Option B — make collections coerce via SameValueZero
semantics** (the common path other languages chose: JS `Map`/`Set`/`includes`, Python
membership). The `language-standard-values` unit sealed the current divergence honestly in
the spec; the coercion change is deferred to this dedicated feature because it is a 3-path
foundation-equality change with too large a blast radius for a review-resolve.

## Design contract for the implementing unit

### The three paths that MUST change

1. **`in` operator → `Stash.Bytecode/Runtime/RuntimeOps.cs` `Contains`.**
   Currently: `svList.Any(sv => sv.Equals(left))` — tag-strict, uses `StashValue.Equals`.
   Fix: replace with a collection-scoped SameValueZero comparer (see algorithm below).

2. **`arr.contains` + index/remove family → `Stash.Stdlib/BuiltIns/ArrBuiltIns.cs`.**
   Approximately 6 sites using `RuntimeValues.IsEqual(item.ToObject(), target)` or
   `StashValue.Equals`-based comparisons. All must switch to the SameValueZero comparer.

3. **Dict keys → `Stash.Core/Runtime/Types/StashDictionary.cs`.**
   `_entries` is `Dictionary<object, StashValue>` keyed on boxed primitives (`long` vs
   `double`). Needs a custom `IEqualityComparer<object>` implementing SameValueZero for
   numeric types, falling back to `object.Equals`/`object.GetHashCode` for all others.

### CRITICAL constraint: do NOT touch `StashValue.Equals`/`GetHashCode`

`StashValue.Equals` and `StashValue.GetHashCode` are used by the compiler's constant-pool
interning in `Stash.Bytecode/Bytecode/ChunkBuilder.cs` (`StashValueComparer`). Applying
numeric coercion to them would collapse distinct typed constants (e.g. `1` and `1.0` are
two different constants with different type-tags that must be separately internable) and
corrupt the VM. **All fixes must be collection-scoped comparers, never global.**

### SameValueZero algorithm (implement as a standalone helper/comparer)

```
bool SameValueZeroEqual(StashValue a, StashValue b):
    if a.IsNumeric && b.IsNumeric:
        // Both integer-typed → compare as long exactly (preserves large int64 distinctions)
        if a.IsInt && b.IsInt:
            return a.LongValue == b.LongValue
        // At least one float → promote both to double
        da = a.ToDouble()
        db = b.ToDouble()
        // NaN self-equal (SameValueZero differs from ==: NaN == NaN)
        return da == db || (IsNaN(da) && IsNaN(db))
    else:
        return a.Equals(b)  // delegate to existing tag-strict path for non-numerics

int SameValueZeroHash(StashValue v):
    if v.IsNumeric:
        d = v.ToDouble()
        if IsNaN(d): return <canonical NaN hash>  // e.g., double.NaN.GetHashCode()
        if d == 0.0: d = 0.0  // normalize -0.0 → +0.0 (they must hash the same)
        return d.GetHashCode()
    else:
        return v.GetHashCode()  // existing hash for non-numerics
```

**Key design decisions:**
- Both integer → exact `long` comparison (preserves `9007199254740993 != 9007199254740992`).
- At least one float → promote to `double`, equal iff `da==db || (IsNaN(da) && IsNaN(db))`.
- `-0.0` and `0.0` hash to the same value (they are `==` under SameValueZero).
- **Deliberate residual:** Collections will have `NaN` self-equal (usable as a dict key,
  findable by `in`), whereas `==` keeps `NaN != NaN`. This is by-design SameValueZero
  semantics. When this feature lands, §Equality must add a sentence stating the residual:
  *"Inside collections (`in`, `arr.contains`, dict key lookup), NaN is self-equal: a NaN
  key stores and retrieves correctly, and `NaN in [NaN]` is `true`. The `==` operator is
  unaffected: `NaN == NaN` remains `false` per IEEE 754."*
- `GetHashCode` large-int collisions (two `long` values that share the same `double`
  representation) are benign — `Equals` separates them.

## Blast radius

- **Scope:** Any Stash script that mixes int and float values in arrays, dict keys, or membership tests.
- **Byte cross-tag:** Same applies for byte, e.g. `conv.toByte(7) in [7]` is currently `false`;
  the fix covers byte ↔ int and byte ↔ float membership as well.
- **Backwards compatibility:** The change makes previously-`false` membership results `true`, which
  is the correct/expected behavior. Existing dict code using integer keys is unaffected (same-tag
  hash/equals are unchanged). Code that *depended on* the tag-strict split would be relying on
  undocumented behavior.

## Conformance test guidance

The existing `EqualityNumericConformanceTests` class contains a `// §Equality — Membership and
keying: tag-strict divergence (sealed)` region with tests that currently assert `false`/`null`.
When this feature ships, flip those assertions to `true`/`"int"` to prove the fix and remove
the sealed-divergence XML doc comment from that region.

## Verification

After implementation:
```bash
dotnet run --project Stash.Cli/ -- -c 'io.println(1 in [1.0]);'          # true (after fix)
dotnet run --project Stash.Cli/ -- -c 'io.println(arr.contains([1], 1.0));'  # true (after fix)
dotnet run --project Stash.Cli/ -- -c 'let d = {}; d[1] = "a"; io.println(d[1.0]);'  # "a" (after fix)
dotnet run --project Stash.Cli/ -- -c 'io.println(1 == 1.0);'            # true (D2 unchanged)
dotnet test --filter "Category=Conformance"   # all green (after flipping membership assertions)
```

## Related

- `language-standard-values` P3 — D2 fix that exposed this inconsistency.
- `language-standard-values` — sealed the divergence honestly; §Equality carve-out is the spec baseline.
- `StashValue.cs` — tag-strict `Equals` path (do NOT modify — see constraint above).
- `RuntimeOps.cs` `Contains` — first fix site.
- `ArrBuiltIns.cs` — second fix site (~6 equality comparison sites).
- `StashDictionary.cs` — third fix site (custom `IEqualityComparer<object>` needed).
- `ChunkBuilder.cs` `StashValueComparer` — reason `StashValue.Equals` must NOT be changed globally.
