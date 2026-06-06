# numeric == coerces but `in` / dict-key lookup do not — cross-operator inconsistency

**Status:** Backlog — Bug
**Created:** 2026-06-06
**Discovery context:** Implementer agent during `language-standard-values` P3 (D2 fix — cross-type numeric
equality). While auditing the blast radius of the `RuntimeOps.IsEqual` change, the agent discovered that
`StashValue.Equals` (used by `Contains` / `in` operator and dict hash key lookup) is a separate, tag-strict
path that does not participate in the D2 numeric-coercion rule.

---

## Problem

After the D2 fix (P3 of `language-standard-values`), the `==` and `!=` operators correctly coerce
int/float/byte cross-tag pairs by mathematical value: `1 == 1.0` is `true`, `conv.toByte(0) == 0` is
`true`. However, the `in` operator (for array membership) and dict key lookup use `StashValue.Equals`
— a separate tag-strict equality path that is **not** updated by the D2 fix. As a result:

- `1 in [1.0]` returns `false` even though `1 == 1.0` is `true`.
- A dict populated with integer key `1` does not match a lookup with float key `1.0`.
- `arr.contains(arr, 1.0)` returns `false` for an array `[1]`.

This creates a new cross-operator inconsistency of the same genus as the original D2 trilemma: the
answer to "is 1 equal to 1.0?" now depends on which operator is used.

## Reproduction

```bash
# After the D2 fix (language-standard-values P3):

# == operator: correct per D2
$ stash -c 'io.println(1 == 1.0);'          # true
$ stash -c 'io.println(1 != 1.0);'          # false

# in operator: tag-strict (inconsistent with ==)
$ stash -c 'io.println(1 in [1.0]);'        # false  — inconsistency
$ stash -c 'io.println(1.0 in [1]);'        # false  — inconsistency

# dict key: tag-strict (inconsistent with ==)
$ stash -c 'let d = {1: "a"}; io.println(d[1.0]);'   # likely null or KeyError

# arr.contains: routes through StashValue.Equals — tag-strict
$ stash -c 'io.println(arr.contains([1], 1.0));'     # false — inconsistency
```

## Blast radius

- **Scope:** Any Stash script that mixes int and float values in arrays, dict keys, or membership tests.
- **Severity:** Medium-high. The inconsistency is now observable and surprising — a user who knows
  `1 == 1.0` is `true` (D2 rule) will expect `1 in [1.0]` to also be `true`. The divergence is a
  correctness hazard for numeric membership checks.
- **Latency:** This is a pre-existing bug exposed/worsened by D2. Before D2, `==` was also tag-strict,
  so no inconsistency existed — the language was consistently tag-strict. D2 fixed `==` but not `in`/dict.
- **Byte:** Same applies for byte cross-tag membership, e.g. `conv.toByte(7) in [7]` is likely `false`.

## Root cause

`StashValue.Equals` (in `Stash.Core/Runtime/StashValue.cs`) is a separate structural equality path
used by:
- `RuntimeOps.Contains` → `svList.Any(sv => sv.Equals(left))` (L314 in RuntimeOps.cs)
- `StashDictionary` hash table key lookup (uses `StashValue.GetHashCode` + `Equals`)

`StashValue.Equals` has `if (Tag != other.Tag) return false;` as an early-out (line 147) and uses
bit-level `_data == other._data` within a tag, so it treats `int 1` and `float 1.0` as unequal
regardless of mathematical value. This path is deliberately separate from `RuntimeOps.IsEqual`
(the `==` operator path) and was not updated in D2.

The same issue affects `GetHashCode`: `int 1` and `float 1.0` hash differently (they have distinct
tags), so even if `Equals` were fixed, the dict hash contract (`a == b → same hash`) would be
violated without also fixing `GetHashCode` — a non-trivial change to the core value type.

## Suggested fix

Two approaches:

- **(A) Fix `StashValue.Equals` + `GetHashCode` for cross-numeric pairs.** Promote byte→int then
  int/float to a canonical comparison. `GetHashCode` must hash `int 1`, `float 1.0`, and `byte 1`
  to the same value (e.g. `HashCode.Combine(Tag.Numeric, BitConverter.DoubleToInt64Bits((double)value))`).
  **Trade-off:** Changes a core value-type contract; likely affects dict key semantics for every
  numeric key (behavioral change to dict). High blast radius; requires audit of all dict consumers.

- **(B) Introduce a `IsEqualNumericCoerce` helper used only by `RuntimeOps.Contains`; leave
  `StashValue.Equals` + dict-key hash tag-strict; document the split.** This narrows the blast
  radius: `1 in [1.0]` becomes `true` (matching `==`) but `d[1]` still does not match `d[1.0]`
  (dict keys stay tag-strict for hash-contract reasons). The spec would need to explicitly document
  that `in` for arrays coerces numerically but dict lookup does not.

- **(C) Pin the current behavior as-is in the spec** — `in` and dict keys are tag-strict; `==` is
  numeric-coercing. Document the split clearly in §Expressions (for `in`) and §Aggregate (for dicts).
  This is the least-risk option: no code change, just spec clarity.

Recommend **(C)** as the immediate resolution, with **(B)** as a follow-up if the `in`-inconsistency
proves user-visible in practice. **(A)** requires a careful audit of the whole dict subsystem and
is §Aggregate territory per the brief.

## Verification

A regression test that confirms `in` is tag-strict (current behavior post-D2):
```bash
dotnet test --filter "FullyQualifiedName~EqualityNumericConformanceTests"
# (the existing tests already verify == coercion; a future test for `in` tag-strictness would go here)
```

After any fix, the following must hold:
- `1 in [1.0]` produces the expected boolean per whichever rule is ratified.
- `1 == 1.0` still returns `true` (D2 regression guard).
- Existing dict tests still pass.

## Related

- `language-standard-values` P3 — D2 fix that exposed this inconsistency.
- `language-standard-values` P4 — per-category equality table (§Equality); the spec there should
  cross-reference the `in`/dict-key behavior when addressing array/dict equality semantics.
- `language-standard-values` brief §Non-Goals: "A general structural equality option for aggregates.
  Pin reference identity for arrays / dicts / structs; defer a structural equality option to §Aggregate
  Types." — This bug's fix (if chosen to be a fix) belongs to §Aggregate.
- `StashValue.cs` L145-L156 — the tag-strict `Equals` path.
- `RuntimeOps.cs` L309-L332 — `Contains` using `StashValue.Equals`.
