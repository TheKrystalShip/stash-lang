# Stash has five uncoordinated equality paths — unify them behind one named chokepoint

**Status:** Backlog — ready to spec
**Created:** 2026-06-06
**Updated:** 2026-06-07 (cross-language comparison + corrected path map + `assert.equal` decision folded in)
**Discovery context:** Implementer agent during `language-standard-values` P3 (D2 fix — cross-type numeric
equality). While auditing the blast radius of the `RuntimeOps.IsEqual` change, the agent discovered that
the `in` operator and dict-key lookup use a *separate, tag-strict* equality path that does not participate
in the D2 numeric-coercion rule. A follow-up investigation (2026-06-07) found the divergence is wider than
first reported: there are **five** distinct equality implementations, not three, and they disagree with
each other on NaN and ±0 as well as int/float.

**Design status (2026-06-07):** The divergence is **sealed honestly** in
`docs/Stash — Language Specification.md` §Equality — "Membership and keying use tag-strict equality" —
and pinned by `EqualityNumericConformanceTests`. The spec no longer misrepresents the behavior. This item
is now a **ready-to-spec feature**: not merely "make collections coerce," but "**route all equality through
one named module**" — the SameValueZero collection fix becomes one sub-goal of that consolidation.

---

## Problem

"Is `x` equal to `y`?" has up to **five different answers** in Stash depending on which syntax reaches the
comparison, because there are five separately-maintained equality implementations:

| # | Implementation | Location | `1` vs `1.0` | `NaN==NaN` | ±0 unified | Consumers |
|---|---|---|---|---|---|---|
| 1 | `RuntimeOps.IsEqual` / `IsEqualCrossTag` | `Stash.Bytecode/Runtime/RuntimeOps.cs` | **equal** (D2 coercion) | no (IEEE) | yes | `==` / `!=` operator |
| 2 | `RuntimeValues.IsEqual(object,object)` | `Stash.Core/Runtime/RuntimeValues.cs` | unequal (`GetType()!=`) | (`object.Equals`) | (`object.Equals`) | `arr.contains`/index/remove (~6 sites), `dict.*`, **`assert.equal`** |
| 3 | `StashValue.Equals` (IEquatable) | `Stash.Core/Runtime/StashValue.cs` | unequal | **yes** (bit-level) | no (bit-level) | **only** the `in` operator (`RuntimeOps.Contains` line 335, `sv.Equals(left)`) |
| 4 | `StashValueComparer` (hand-copied dup of #3) | `Stash.Bytecode/Bytecode/ChunkBuilder.cs` | unequal | yes | no | constant-pool interning (`_constantMap`) |
| 5 | `Dictionary<object,…>` default comparer | `Stash.Core/Runtime/Types/StashDictionary.cs` | unequal | (`object`) | (`object`) | dict key lookup `d[k]` |

Two of these are **user-visible contradictions** the original report missed:

- `assert.equal(1, 1.0)` **throws** (path #2, tag-strict) even though `1 == 1.0` is `true` (path #1). The test
  helper disagrees with the operator.
- `1 in [1.0]` is `false`, but `NaN in [NaN]` is `true` (path #3 deliberately bit-compares floats so NaN
  self-equals — `StashValue.cs:152`), while `NaN == NaN` is `false`. The `in` operator already carries its
  own third NaN rule.

Path #4 is a **hand-copied duplicate** of path #3's logic, separately maintained — the "a closed set
duplicated across files is the same defect as an inline literal" anti-pattern (CLAUDE.md no-magic doctrine),
applied to equality semantics. Five implementations of one concept, **none of them named**, is the root
defect — not any single rule.

### Correction to the original report (path attribution)

The first version of this stub said "do NOT touch `StashValue.Equals` because constant-pool interning
depends on it." **That is mis-attributed.** Interning binds to path **#4** (`ChunkBuilder` constructs
`_constantMap = new(StashValueComparer.Instance)`), *not* path #3. A 2026-06-07 sweep confirmed:

- The **only** `Dictionary<StashValue,…>`/`HashSet<StashValue>` in the entire codebase is `_constantMap`,
  and it explicitly names the #4 comparer.
- There are **zero** `== StashValue.X` / `!= StashValue.X` idioms anywhere — the struct's C# `==`/`!=`
  operators (`StashValue.cs:169-170`) have no runtime callers.
- Path #3's *sole* runtime consumer is the `in` operator's `Contains`.

So #3 is barely load-bearing: once `in` moves to SameValueZero, `StashValue.Equals` has no runtime callers
and serves *only* as the structural identity the interning key needs (which #4 currently duplicates).

## Reproduction

```bash
dotnet run --project Stash.Cli/ -- -c 'io.println(1 == 1.0);'                         # true   (D2 honored)
dotnet run --project Stash.Cli/ -- -c 'io.println(1 in [1.0]);'                       # false  (tag-strict)
dotnet run --project Stash.Cli/ -- -c 'io.println(arr.contains([1], 1.0));'           # false  (tag-strict)
dotnet run --project Stash.Cli/ -- -c 'let d = {}; d[1] = "a"; io.println(d[1.0]);'   # null   (distinct keys)
# user-visible contradiction:
dotnet run --project Stash.Cli/ -- -c 'assert.equal(1, 1.0);'                         # throws (assert disagrees with ==)
```

## Cross-language comparison (empirical, 2026-06-07)

Probed against real interpreters to ground the decision in "the common path other languages chose" rather
than folklore. Every cell was run, not recalled.

| Decision | Python 3.14 | Node 26 | Ruby 3.4 | Lua 5.5 | Perl | **Stash today** | **Stash target** |
|---|---|---|---|---|---|---|---|
| `1 == 1.0` (operator) | `True` | `true` | `true` | `true` | `true` | **`true`** (D2) | keep |
| `1 in [1.0]` (membership) | `True` | `true` | `true` | n/a¹ | — | **`false`** | `true` |
| `{1:…}[1.0]` (key lookup) | hit | hit | **`nil`** | hit¹ | hit² | **miss** | hit |
| `{1, 1.0}` (set size) | `1` | `1` | **`2`** | — | — | — | `1` |
| **operator `==` agrees with collections?** | **YES** | **YES** | **NO** | YES | YES | **NO (accidental)** | YES |
| `NaN == NaN` (operator) | `False` | `false` | `false` | `false` | `false` | **`false`** | keep |
| `NaN` findable in a collection? | by identity³ | **by value** | by identity³ | **banned (error)** | — | `false` | by value |
| `+0.0 == -0.0` (operator) | `True` | `true` | `true` | `true` | `true` | `true` | keep |
| `+0` / `−0` same in collections? | same (1) | same (1) | same | same¹ | — | **distinct** | same |
| # of **named** equality predicates | 2 (`==`, `is`) | **4** | 3 (`==`,`eql?`,`equal?`) | 1 + norm | 2 (`==`,`eq`) | 5 (tangled) | 3 (named) |

¹ Lua has no value-`in`; integer-valued float keys are *normalized* to integer keys (`t[1.0]` → `t[1]`), and
a NaN key is a hard runtime error (`table index is NaN`). ² Perl hash keys are all strings (`1.0` →
`"1"`) — moot. ³ **Decisive for Stash:** Python and Ruby make `NaN in [NaN]` work *only by object identity*
(distinct NaN instances are not found); JS SameValueZero makes it work *by value*. A Stash `NaN` is an
unboxed `double` inside a `StashValue` struct — **no object identity** — so Python/Ruby's trick is
physically unavailable. Stash's only coherent options are JS-style (value-based, all NaNs equal in
collections) or Lua-style (ban NaN keys). SameValueZero is the value-based choice.

**What the data says:**

- `1 == 1.0` is **universal** — D2 already matches the common path. No language disagrees.
- The real dividing line — *does `==` agree with collection membership/keys?* — splits **4-to-1 in favor of
  YES** (Python/JS/Lua/Perl), including the two most-used scripting languages. Ruby is the lone splitter,
  and it does so **deliberately and with a name** (`eql?` = "same value AND same type" for hashing). **Stash
  today is accidentally Ruby** — same split, but unnamed and uncoordinated. That is the defect.
- The careful languages all expose a **small, named, closed set** of equality predicates (JS has 4; Ruby 3;
  C#'s `==`/`Equals`/`IEqualityComparer` triad). Stash's three proposed modes map almost 1:1 onto three of
  JavaScript's four: `OperatorEquals` ≈ `===`; the collection comparer ≈ **`SameValueZero`** (literally);
  the interning key ≈ a finer-than-`Object.is` structural identity. JS even keeps a strict outlier next to
  the coercing one in the *same* collection family — `[NaN].indexOf(NaN)` is `-1` (`===`) while
  `[NaN].includes(NaN)` is `true` (SameValueZero) — proof that mature languages route different operations
  through *named* modes on purpose.
- Statically-typed systems peers (for context, not reachable here): **Go/Rust** refuse cross-type `==` at
  compile time and refuse/ban NaN keys at the type level; **C#/Java** keep `==` IEEE but make `.Equals`/hash
  a total order (NaN==NaN, ±0 *distinct* — the opposite of SameValueZero on ±0). Neither is open to a
  dynamically-typed language; both reinforce that "operator ≠ collection equality" is respectable *only when
  named and consistent*.

## Ratified decisions

| ID | Decision | Source |
|---|---|---|
| **DE1** | `==`/`!=` operator: keep D2 — coerce int/float/byte by value, `NaN != NaN`, `+0 == -0`. | locked (D2) |
| **DE2** | Membership (`in`, `arr.contains`) and dict keys: **SameValueZero** (coerce int/float/byte, NaN self-equal *by value*, ±0 unified). The common path (Python/JS); the only path mechanically open to Stash. | user, Option B |
| **DE3** | **`assert.equal` / `assert.notEqual` stay type-strict** — `assert.equal(1, 1.0)` must keep throwing. Assert is its own *named* strict mode, NOT operator-loose and NOT SameValueZero. Precedent: Ruby `eql?`, C#/Java `Object.Equals`, JS would use `Object.is`/`===` for strict test assertions. | user, 2026-06-07 |
| **DE4** | secret `==` stays **reference identity** (D4, USER OVERRIDE from `language-standard-values`). Must survive the refactor — see constraint below. | locked (D4) |

## Design contract for the implementing unit

### The chokepoint: one `StashEquality` module, three *named* modes

A literal single function is **impossible** — interning genuinely needs a semantics distinct from runtime
equality (it must keep `1` and `1.0` *separate* constants or the VM corrupts). The correct realization of
"single chokepoint" is the bounded-domain doctrine applied to equality: **one source-of-truth module that
defines the entire closed set of equality modes; every call site routes through it declaring which mode.**

```
Stash.Core/Runtime/StashEquality.cs   ← NEW single source of truth
                                         (lives in Core so Bytecode + Stdlib both consume it)
  ├─ OperatorEquals(StashValue, StashValue) -> bool        [DE1]  NumericCoercingEquals(…, nanSelfEqual:false)
  ├─ SameValueZero : IEqualityComparer<StashValue>         [DE2]  NumericCoercingEquals(…, nanSelfEqual:true)
  │     (+ an IEqualityComparer<object> adapter for the boxed dict-key path, #5)
  └─ StrictEquals(StashValue, StashValue) -> bool          [DE3]  tag-strict value equality for assert
```

The two *runtime-value* modes differ in **exactly one rule** — NaN self-equality. Both coerce int/float/byte;
both unify ±0. So the heart of the module is one private `NumericCoercingEquals(a, b, bool nanSelfEqual)`
over an `IVMEquatable` dispatch (for struct/enum custom `==`) with the non-numeric branch delegating to
`object.Equals` (preserving DE4 secret identity). `OperatorEquals` passes `false`; `SameValueZero` passes
`true`.

Interning is **not** a third equality mode — it answers "are these the same literal constant?", whose only
requirement is being *at least as fine as* the finest runtime equality (over-splitting a constant is
harmless; over-merging corrupts). Name it accordingly (`StructuralIdentity` / `ConstantInterningKey`), not
"strict equality," to kill the standing temptation to "unify it too."

### What each of the five paths becomes

1. **#1 `RuntimeOps.IsEqual`** → thin forwarder to `StashEquality.OperatorEquals` (logic moves out of the VM).
2. **#2 `RuntimeValues.IsEqual`** → **retired.** `arr.contains`/index/remove + `dict.*` callers → `SameValueZero`;
   `assert.equal`/`assert.notEqual` → `StashEquality.StrictEquals` (DE3).
3. **#3 `in` operator (`RuntimeOps.Contains`, line 335)** → `StashEquality.SameValueZero.Equals` (replaces
   `sv.Equals(left)`). After this, `StashValue.Equals` has no runtime callers.
4. **#4 `StashValueComparer`** → **deleted**, folded into #3: interning uses `StashValue`'s own `IEquatable`
   (now purely the structural-identity key). Collapses the duplicate.
5. **#5 `StashDictionary._entries`** → constructed with the SameValueZero `object` adapter. **Trickiest
   mechanical piece**: keys are boxed `object` (`long`/`double`/`string`), not `StashValue`, so the adapter
   must operate on boxed primitives (or the dict migrates to key on `StashValue` — a larger change the
   architect should weigh).

### Constraints the spec MUST pin (conformance, not implicit)

1. **DE4 — secret membership / secret dict-keys stay reference-identity.** `StashSecret.Equals` is
   `ReferenceEquals(this, other)` with an identity hash. The SameValueZero comparer's non-numeric branch must
   delegate to `object.Equals` (never a structural deep-equal) so secrets don't silently regress. **Assert it.**
2. **Interning stays at-least-as-fine.** `1` and `1.0` must remain distinct constants (different type-tag,
   different `typeof`, different stringification). This — not "interning uses `StashValue.Equals`" — is the
   real reason the structural-identity key stays tag-strict.
3. **NaN / ±0 residual documented in §Equality.** When this lands, the sealed-divergence clause is replaced
   by: *"Inside collections (`in`, `arr.contains`, dict key lookup), equality is SameValueZero: int/float/byte
   coerce by value, NaN is self-equal (a NaN key stores and retrieves; `NaN in [NaN]` is `true`), and `+0.0`
   and `-0.0` are the same key. The `==` operator is unaffected: `NaN == NaN` remains `false` and `+0.0 ==
   -0.0` remains `true` per IEEE 754. `assert.equal` is type-strict and is not SameValueZero."*

### SameValueZero algorithm (the `nanSelfEqual:true` instantiation)

```
bool SameValueZeroEqual(StashValue a, StashValue b):
    if a.IsNumeric && b.IsNumeric:
        if a.IsInt && b.IsInt:                 // both integer → exact long (preserves large int64)
            return a.LongValue == b.LongValue
        da = a.ToDouble(); db = b.ToDouble()   // at least one float → promote both
        return da == db || (IsNaN(da) && IsNaN(db))   // NaN self-equal (the one delta vs ==)
    else:
        return a.Equals(b)                     // delegate to structural identity for non-numerics (DE4 secret = ref)

int SameValueZeroHash(StashValue v):
    if v.IsNumeric:
        d = v.ToDouble()
        if IsNaN(d): return double.NaN.GetHashCode()   // canonical NaN hash
        if d == 0.0: d = 0.0                            // normalize -0.0 → +0.0 (must hash equal)
        return d.GetHashCode()
    else:
        return v.GetHashCode()
```

- Both integer → exact `long` comparison (preserves `9007199254740993 != 9007199254740992`).
- At least one float → promote to `double`, equal iff `da==db || (IsNaN(da) && IsNaN(db))`.
- `-0.0` and `0.0` hash to the same value (they are `==` under SameValueZero).
- `GetHashCode` large-int/double collisions are benign — `Equals` separates them.

## Blast radius

- **Scope:** Any Stash script mixing int and float in arrays, dict keys, or membership tests; plus byte
  cross-tag (`conv.toByte(7) in [7]` is currently `false` — the fix covers byte↔int and byte↔float too).
- **`assert.equal` is intentionally NOT changed** (DE3) — it remains type-strict, so no existing assertion
  flips.
- **Backwards compatibility:** the change makes previously-`false` membership results `true` (the
  correct/expected behavior). Same-tag dict code is unaffected. Code that *depended on* the tag-strict
  collection split was relying on undocumented behavior.

## Enforcement (Detect, not Construct — honest about feasibility)

Full "Construct" (delete the tempting `IEquatable<StashValue>`) is **infeasible** — the struct's `IEquatable`
is load-bearing for interning. So the realistic guard is a **Detect** meta-test in the
`NoMagicAuthStringsMetaTests` mold: a Roslyn sink-scan flagging any *new* `.Equals(`/`IsEqual` on runtime
values outside `StashEquality`, with the sanctioned module as the only allowed home (append-only allow-list
for genuinely-internal structural uses like interning). Pair it with a binding-floor so it can't pass
vacuously (CLAUDE.md Roslyn-determinism rule).

## Conformance test guidance

- `EqualityNumericConformanceTests` has a `// §Equality — Membership and keying: tag-strict divergence
  (sealed)` region asserting `false`/`null`. **Flip** those to `true`/`"int"` and remove the
  sealed-divergence XML doc comment.
- Add: NaN-in-collection by value (`NaN in [NaN]` → `true`; distinct-instance, not identity), NaN dict key
  round-trips, ±0 unified as a key, byte cross-tag membership.
- **DE3 conformance:** `assert.equal(1, 1.0)` throws; `assert.equal(1, 1)` passes — assert is strict.
- **DE4 conformance:** two distinct secrets wrapping equal bytes are NOT the same dict key / NOT `in` each
  other (reference identity preserved).

## Verification

```bash
dotnet run --project Stash.Cli/ -- -c 'io.println(1 in [1.0]);'                       # true (after fix)
dotnet run --project Stash.Cli/ -- -c 'io.println(arr.contains([1], 1.0));'           # true (after fix)
dotnet run --project Stash.Cli/ -- -c 'let d = {}; d[1] = "a"; io.println(d[1.0]);'   # "a" (after fix)
dotnet run --project Stash.Cli/ -- -c 'io.println(1 == 1.0);'                         # true (DE1 unchanged)
dotnet run --project Stash.Cli/ -- -c 'assert.equal(1, 1);'                           # ok   (DE3 strict)
dotnet test --filter "Category=Conformance"   # all green (after flipping membership assertions)
```

## Related

- `language-standard-values` P3 — D2/DE1 fix that exposed this inconsistency.
- `language-standard-values` — sealed the divergence honestly; §Equality carve-out is the spec baseline.
- `RuntimeOps.cs` `IsEqual`/`Contains` — paths #1 and #3 (operator + `in`).
- `RuntimeValues.cs` `IsEqual` — path #2 (to be retired; assert → DE3 strict).
- `StashValue.cs` `Equals`/`GetHashCode` — path #3; becomes the structural-identity key (do NOT make it coerce).
- `ChunkBuilder.cs` `StashValueComparer` — path #4 duplicate (to be deleted, folded into #3).
- `StashDictionary.cs` — path #5 (boxed-key SameValueZero adapter — trickiest site).
- `StashSecret.cs` — DE4 reference-identity (must survive).
- `AssertBuiltIns.cs` — DE3 strict equality.
