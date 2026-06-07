# RFC: Language Standard — Unify Equality Behind One Named Chokepoint

> **Status:** Draft
> **Owner:** cristian.moraru@live.com
> **Created:** 2026-06-07
> **Slug:** language-standard-equality
> **Milestone:** language-standard  <!-- unit #3; follows language-standard-values; seals §Equality sub-clause + collapses the 5-path divergence -->

## Summary

Stash has **five separately-maintained equality implementations** that disagree on
int/float, NaN, and ±0 — five answers to "is `x == y`?", chosen by *which syntax*
reaches the comparison. This unit collapses them onto **one named source of truth**
(`Stash.Core/Runtime/StashEquality.cs`) exposing a small bounded-domain closed set of
**three named modes** — `OperatorEquals`, `SameValueZero`, `StrictEquals` — over a
private `NumericCoercingEquals(a, b, bool nanSelfEqual)` core. Every observable equality
call site routes through this module declaring which mode it wants. As part of that
consolidation, **collections (`in`, `arr.contains`, dict keys) adopt SameValueZero
semantics** (so `1 in [1.0]` becomes `true`, `NaN in [NaN]` becomes `true`, `±0`
collapses to one key), matching the common path (Python / JavaScript / Lua / Perl
4-to-1 over Ruby — see seed §"Cross-language comparison").

This is **unit #3** of the `language-standard` milestone (after
`language-standard-async` (§Async) and `language-standard-values` (§Values & Types)).
It seals the *one remaining unsealed clause* of §Equality — the "Membership and
keying use tag-strict equality" carve-out that `language-standard-values` parked at
L730–L750 under the doc-honest "sealed divergence" framing — and turns the seam into
a single, named, multi-mode source-of-truth that every later seal unit consumes
unchanged.

## Motivation

The §Values & Types unit sealed the equality *operator* per D2 (numeric coercion for
`==`/`!=`) and parked the membership/dict-key divergence behind a deliberate "sealed
honestly" carve-out, with the §Equality spec text explicitly pointing at the
backlog stub that triggered this unit. The carve-out is not the long-run answer:
five separately-maintained equality implementations is the same anti-pattern the
no-magic-strings doctrine names "a closed set duplicated across files," applied to
runtime semantics rather than literals — five answers to one question, none of
them named.

The five paths today (post-D2, confirmed by direct line-grep 2026-06-07):

| # | Implementation | Location | `1` vs `1.0` | `NaN==NaN` | ±0 unified | Consumers |
|---|---|---|---|---|---|---|
| 1 | `RuntimeOps.IsEqual` / `IsEqualCrossTag` | `Stash.Bytecode/Runtime/RuntimeOps.cs:49,67` | **equal** (D2) | no (IEEE) | yes | `==` / `!=` operator |
| 2 | `RuntimeValues.IsEqual(object,object)` | `Stash.Core/Runtime/RuntimeValues.cs:205` | unequal (`GetType()!=`) | (`object.Equals` → `Double.Equals` → **yes**) | (`Double.Equals` → **yes**) | `arr.contains`/index/remove (~6 sites in `ArrBuiltIns.cs`), `DictBuiltIns.cs:247`, **`AssertBuiltIns.cs:26,42`** |
| 3 | `StashValue.Equals` (IEquatable) | `Stash.Core/Runtime/StashValue.cs:145` | unequal | **yes** (`_data == other._data` bit-level) | **no** (bit-level) | **only** `RuntimeOps.Contains` line 335 (the `in` operator) |
| 4 | `StashValueComparer` (in `ChunkBuilder`) | `Stash.Bytecode/Bytecode/ChunkBuilder.cs:1220` | unequal | **no** (`x.AsFloat == y.AsFloat` value-`==`) | **yes** (value-`==`) | constant-pool interning (`_constantMap`) |
| 5 | `Dictionary<object,…>` default comparer | `Stash.Core/Runtime/Types/StashDictionary.cs:16` | unequal | (`Double.Equals` → yes) | (`Double.Equals` → yes) | dict key lookup `d[k]` |

**Correction to the backlog seed:** the seed claimed "#4 is a hand-copied duplicate of
#3." It is not — they disagree on NaN and ±0. `StashValue.Equals` (#3) uses bit-level
`_data == other._data` for Float (NaN-self-equal, ±0 distinct, with a comment "for
collection ops"); `StashValueComparer` (#4) uses value-`==` `x.AsFloat == y.AsFloat`
(NaN-not-self-equal, ±0 unified). #4 is the latent bug for interning (it could
merge `+0.0` and `-0.0` into one constant, which is fine for value but loses
bit-fidelity in disassembly). When we fold #4 into #3, interning moves from value-`==`
to bit-level: a **safe correction** in the structural-identity direction.

**Additional user-visible inconsistencies the seed surfaced (re-probed 2026-06-07):**

```bash
stash -c 'io.println(1 == 1.0);'                         # true   (D2 honored)
stash -c 'io.println(1 in [1.0]);'                       # false  (tag-strict #3)
stash -c 'io.println(arr.contains([1], 1.0));'           # false  (tag-strict #2)
stash -c 'let d = {}; d[1] = "a"; io.println(d[1.0]);'   # null   (distinct keys, #5)
# user-visible contradiction:
stash -c 'try { assert.equal(1, 1.0); } catch (e) { io.println("THROWS"); }'
# THROWS — assert (#2) disagrees with == operator (#1) on numeric cross-type
```

Plus the **assert NaN/±0 profile probed 2026-06-07** (decisive for the Open Questions):

```bash
stash -c 'try { assert.equal(0.0, -0.0); io.println("PASS"); } catch (e) { io.println("THROWS"); }'
# PASS — assert unifies ±0 (Double.Equals)
stash -c 'let big = conv.toFloat("1e308") * 10.0; let n = big - big; try { assert.equal(n, n); io.println("PASS"); } catch (e) { io.println("THROWS"); }'
# PASS — assert treats NaN==NaN (Double.Equals)
```

Without this unit, every later language-standard seal pass either re-derives one of
these five rules locally or has to spec around the membership/dict-key gap; the
substrate stays multi-source-of-truth.

## Goals

- **Build `Stash.Core/Runtime/StashEquality.cs`** as the *single source of truth* for
  every runtime equality decision, exposing three *named* modes — `OperatorEquals`,
  `SameValueZero` (`: IEqualityComparer<StashValue>`), `StrictEquals` — over a
  private `NumericCoercingEquals(StashValue a, StashValue b, bool nanSelfEqual)`
  core. `OperatorEquals` passes `nanSelfEqual:false`; `SameValueZero` passes
  `true`. `StrictEquals` is tag-strict value equality (the **assert** mode);
  preserves existing assert behavior (ratified DE3 + the probed NaN/±0 profile).
- **Route all five call-site families through `StashEquality`:**
  - **#1 operator** → `RuntimeOps.IsEqual` becomes a thin forwarder to
    `StashEquality.OperatorEquals` (logic migrates out of the VM module).
  - **#2 `RuntimeValues.IsEqual`** → retired; arr/dict callers move to
    `StashEquality.SameValueZero`, assert callers move to
    `StashEquality.StrictEquals`.
  - **#3 `in` operator (`RuntimeOps.Contains`, line 335 `sv.Equals(left)`)** →
    `StashEquality.SameValueZero.Equals`. After this, `StashValue.Equals`'s sole
    runtime caller is the constant-pool key (path #4 folds in).
  - **#4 `ChunkBuilder.StashValueComparer`** → deleted, folded into #3.
    `_constantMap` constructs against `StashValue`'s own `IEquatable`. The
    interning key is the **`StructuralIdentity`** name; it is **not** an equality
    mode (it is the constant-pool comparator, not a runtime semantics) and
    therefore lives on `StashValue` itself, not on `StashEquality`. The "for
    collection ops" comment on `StashValue.Equals` (`StashValue.cs:152`) is
    updated to "for the constant-pool key — see `StashEquality` for runtime
    equality."
  - **#5 `StashDictionary._entries`** → migrated to `Dictionary<StashValue,
    StashValue>` with `StashEquality.SameValueZero` as the comparer. Boxed-key
    blast-radius (the ~3 `StashValue.FromObject(kvp.Key)` / `index.ToObject()!`
    sites) is rewritten in the same phase; no boxed-`object` adapter ships.
- **Adopt SameValueZero in collections (DE2).** `1 in [1.0]`, `NaN in [NaN]`,
  `arr.contains([0], -0.0)`, `d[1]=...; d[1.0]` all return the **same-key /
  found** answer. The §Equality membership/dict-key clause is rewritten to
  state SameValueZero explicitly (positive + negative space).
- **Preserve assert's user-visible behavior, but name it (DE3).** `assert.equal(1,
  1.0)` keeps throwing; `assert.equal(0.0, -0.0)` keeps passing (probed today);
  `assert.equal(NaN, NaN)` keeps passing (probed today). All three become
  normative clauses backed by `Category=Conformance` tests.
- **Preserve secret reference identity (DE4).** Two distinct `secret("x")`
  constructions remain not-`in` each other and remain distinct dict keys after
  the `in` and dict-key migrations. The SameValueZero non-numeric branch
  delegates to `StashValue.Equals` → `object.Equals` → `StashSecret.Equals` →
  `ReferenceEquals` (the chain D4 ratified in unit #2). Backed by conformance
  tests **in both the membership phase and the dict-key phase** (the two
  migrations are the riskiest secret-equality regressions).
- **Update §Equality in `docs/Stash — Language Specification.md`.** Replace the
  current sealed-divergence carve-out (L730–L750) with the SameValueZero
  membership/dict-key clause; pin the assert-strict clause as a new sentence
  under the operator section; pin the `StashEquality` module's three-mode
  vocabulary as the closed bounded domain of equality semantics (the
  bounded-domain doctrine applied to equality, with the *names* being the
  spec's normative vocabulary).
- **Add `Stash.Tests/Conformance/Equality/`** as a new conformance area
  (siblings: `Async/`, `Values/`). Each clause group gets one
  `*ConformanceTests.cs` class with `[Trait("Category","Conformance")]`,
  inheriting the unit-#1 trait guard (`ConformanceTraitMetaTests`) by name-prefix
  scan. **Flip** the existing sealed-divergence assertions inside
  `Stash.Tests/Conformance/Values/EqualityNumericConformanceTests.cs` (the region
  beginning `// §Equality — Membership and keying: tag-strict divergence
  (sealed)` at ~L351) — `false`/`null` → `true`/`"int"` — and remove the sealed
  doc comments. (The values-unit assertions are now the bridge between this
  unit's spec edits and the unit's conformance suite.)
- **Ship a Detect meta-test** —
  `Stash.Tests/Conformance/Equality/EqualityChokepointMetaTests.cs` — a Roslyn
  sink-scan flagging any `.Equals(`/`IsEqual(` call on a runtime value (`StashValue`,
  arr/dict/secret/struct/enum object representations) **outside** the
  `Stash.Core.Runtime.StashEquality` namespace, with an append-only exemption
  list. The meta-test goes up **green-with-an-explicit-exemption-list** in P1
  (chokepoint-build phase) enumerating every not-yet-migrated site; each later
  phase **shrinks the list** as it migrates its sites; at unit close the list
  contains only the sanctioned interning use (`ChunkBuilder._constantMap` /
  `StashValue.IEquatable`) and the `StashEquality` module's own internal
  delegations. Copies the `NoMagicAuthStringsMetaTests` pattern: MetadataReferences
  from `TRUSTED_PLATFORM_ASSEMBLIES` (load-order-deterministic), file-count floor,
  binding-floor (assert `StashEquality` resolves to a non-error symbol), and a
  fail-path teeth self-test.
- **Update `coverage.md`** — §Values & Types row #2's `Highest-value open items`
  column drops the membership/keying carve-out note; the live spec-vs-impl
  contradictions table strikes through (or removes) the membership-keying entry
  if present; cross-cutting workstream #3 (truthiness/equality/coercion
  substrate) note is extended to confirm membership/dict-keys are now part of
  the sealed substrate. The §Equality clause is no longer an open seam.
- **Author `examples/equality.stash`** — by `stash-author` agent, called for from
  a phase but not authored in this brief. Demonstrates the three named modes via
  observable behavior (`==` cross-type numeric, `in` SameValueZero,
  `assert.equal` strict).

## Non-Goals

- **No new equality predicate at the language level.** This unit names the three
  *existing* equality semantics (operator, collection, assert). It does **not**
  add a `===` operator, an `is` keyword, an `arr.equalsSVZ` overload, or any
  user-facing equality syntax. The names live in *C# code* (the module's three
  members) and in the *spec prose* (the bounded domain); the Stash surface is
  unchanged.
- **No structural-deep-equality on arrays/dicts/structs.** `[1] == [1]` stays
  `false` (reference identity per §Values D4-locked semantics); `arr.equals` /
  `dict.equals` remain the structural helpers. SameValueZero only changes the
  *numeric + NaN + ±0* rules; the non-numeric branch delegates to
  `StashValue.Equals` (reference identity for aggregates), preserving every
  existing identity equality.
- **No change to `==` operator semantics on primitives.** DE1 keeps D2 exactly:
  int/float/byte coerce, `NaN != NaN`, `+0.0 == -0.0`. Existing operator tests
  must stay green.
- **No change to secret equality (DE4-locked).** SameValueZero's non-numeric
  branch delegates to existing `StashValue.Equals` → `object.Equals` →
  `StashSecret.ReferenceEquals`. Conformance tests pin this in both migration
  phases.
- **No new opcode, no dispatch loop edit, no AST change.** The chokepoint lives
  in `Stash.Core/Runtime/StashEquality.cs`; the existing operator opcode
  (`OpCode.Equal`/`NotEqual`) still routes through `RuntimeOps.IsEqual`, which
  becomes a thin forwarder. The Dispatch-Loop-Size-Limit invariant
  (`Stash.Bytecode/CLAUDE.md`) is not touched.
- **No `examples/` script changes besides `examples/equality.stash`** (and that
  one is authored by `stash-author`, called for from a phase, not by the
  architect). The §Async drop-test pattern is the precedent — examples
  illustrate, conformance proves.
- **No change to the constant-folder.** D2 ratified that the folder was right;
  the runtime path was the bug (fixed in unit #2). This unit does not touch
  `Compiler.Expressions.cs:TryFoldBinary`. The interning *key* changes (from #4
  value-`==` to #3 bit-level via StashValue) — `1.0` vs `0.0` vs `-0.0` are
  *more* distinct, not less; folder/interning correctness is preserved.
- **No backfill of pre-existing `Category=Conformance` tests under
  `Conformance/Values/Equality*`.** Those tests are authoritative for §Values
  cross-type operator equality. This unit *flips* the sealed-divergence region
  inside `EqualityNumericConformanceTests.cs` (~L351) but does not duplicate
  the unflipped tests elsewhere.
- **No data migration.** The registry is pre-release per the project memory;
  no shipped behavior depends on the old membership/dict-key answers. The
  change is a strict semantic upgrade.

## Design

The shape is **build the chokepoint first, then migrate the five call-site
families through it phase-by-phase, with each spec clause landing in the same
phase as the behavior that makes it true** (per §Async / §Values precedent).
Every phase is independently green: a phase landing a spec clause has already
landed the code change that makes the clause true; a phase landing a
behavior-preserving forwarder lands no spec clause. The Detect meta-test goes
up *green with a pinned exemption list* in P1 (it is not a final-phase guard —
that would mean every prior phase landed un-guarded), and shrinks to the
sanctioned interning entry by P5.

### Surface

No user-facing language or stdlib surface changes. The deliverables are:

- A new module: `Stash.Core/Runtime/StashEquality.cs` (static class with three
  members + one private core).
- An updated `Stash.Bytecode/Runtime/RuntimeOps.cs:IsEqual`/`IsEqualCrossTag`/
  `IsEqualSlow` — thin forwarders to `StashEquality.OperatorEquals`. The
  existing `IVMEquatable.VMEquals` dispatch is preserved (user struct/enum `==`
  routes through it unchanged).
- An updated `Stash.Bytecode/Runtime/RuntimeOps.cs:Contains` (line 335) — `in`
  uses `StashEquality.SameValueZero.Equals`.
- Updated `Stash.Stdlib/BuiltIns/ArrBuiltIns.cs` (6 sites) and
  `Stash.Stdlib/BuiltIns/DictBuiltIns.cs:247` — `arr.contains` / index / remove
  / dict.filter etc. use `StashEquality.SameValueZero`.
- Updated `Stash.Stdlib/BuiltIns/AssertBuiltIns.cs:26,42` —
  `assert.equal`/`assert.notEqual` use `StashEquality.StrictEquals`.
- A migrated `Stash.Core/Runtime/Types/StashDictionary.cs:_entries` —
  `Dictionary<StashValue, StashValue>` constructed with
  `StashEquality.SameValueZero`. Boxed-key sites (`StashValue.FromObject(kvp.Key)`
  ~L68/L99, `index.ToObject()!` L189/L196) rewritten to pass `StashValue`
  directly.
- A deleted `Stash.Bytecode/Bytecode/ChunkBuilder.cs:StashValueComparer`
  (L1220–L1247). `_constantMap` becomes `new Dictionary<StashValue, ushort>()`
  using `StashValue`'s own `IEquatable`. The `StashValue.cs:152` comment is
  updated.
- A retired `Stash.Core/Runtime/RuntimeValues.cs:IsEqual`
  (`RuntimeValues.IsNumeric` / `ToDouble` stay — they are reused by
  `StashEquality`'s numeric branch and by other arithmetic call sites).
- A new test directory: `Stash.Tests/Conformance/Equality/` with
  `_README.md`, one `*ConformanceTests.cs` per clause group, every test
  `[Trait("Category","Conformance")]`-marked, citing spec clauses.
- A new Detect meta-test:
  `Stash.Tests/Conformance/Equality/EqualityChokepointMetaTests.cs` (Roslyn
  sink-scan + binding-floor + teeth self-test + append-only exemption list).
- Updated `Stash.Tests/Conformance/_README.md` (directory layout adds an
  `Equality/` row).
- Updated `Stash.Tests/Conformance/ConformanceTraitMetaTests.cs` —
  `MinScannedParticipants` floor raised per phase as participants land.
- Updated `Stash.Tests/Conformance/Values/EqualityNumericConformanceTests.cs`
  — sealed-divergence region flipped to assert SameValueZero, doc comments
  removed.
- Updated `docs/Stash — Language Specification.md` §Equality (L730–L750
  carve-out replaced; assert-strict clause inserted; named-modes bounded
  domain sentence added).
- Updated `.kanban/milestones/language-standard/coverage.md` (membership /
  keying carve-out note retired; cross-cutting workstream #3 expanded;
  contradictions table updated if any §Equality entry remains).
- A `stash-author`-authored `examples/equality.stash` (called for from a
  phase; not authored in this brief).

### Semantics

The §Equality clause becomes a closed-vocabulary, three-mode contract:

- **`OperatorEquals` (DE1, unchanged):** `==`/`!=`. Int/float/byte coerce by
  mathematical value; `NaN != NaN`; `+0.0 == -0.0`. Non-numeric cross-category
  pairs are unequal. Per-category rule unchanged from §Values & Types Edit 4.
- **`SameValueZero` (DE2, new in collections):** `in`, `arr.contains`,
  `arr.indexOf`, `arr.remove`, `arr.findFirst`-and-friends, dict keys
  (`d[k]=v`, `d[k]`, `k in d`, `dict.has`, `dict.keys` round-trip),
  `dict.filter`, `dict.merge` dedup, `arr.unique`. Same coercion rules as
  `OperatorEquals` **with one delta**: NaN is self-equal by value. `+0.0` and
  `-0.0` are the same key. The non-numeric branch delegates to
  `StashValue.Equals` (preserving secret reference identity, array identity,
  etc.).
- **`StrictEquals` (DE3, the assert mode):** `assert.equal`/`assert.notEqual`.
  Tag-strict — operands of different `typeof` are not equal even within the
  numeric class (so `assert.equal(1, 1.0)` keeps throwing). Within a tag, value
  equality through `Double.Equals` for float (which unifies ±0 and treats
  NaN==NaN — the probed behavior), `==` for primitives, and reference identity
  for aggregates/secrets via `StashValue.Equals`.

The interning constant-pool key is **not** a fourth equality mode. It is
`StashValue`'s own `IEquatable<StashValue>` — bit-level value equality on the
Float tag, used solely to dedupe constant-pool entries. Its only requirement is
being *at least as fine as* the finest runtime equality; over-splitting is
harmless (a doubled `1.0` constant is fine), over-merging corrupts. The name
**`StructuralIdentity` / "constant-pool key"** keeps it doctrinally separate
from runtime equality.

The user-observable changes are:
- `1 in [1.0]` flips `false` → `true`.
- `arr.contains([1], 1.0)` flips `false` → `true`.
- `NaN in [NaN]` is `true` (was already `true` via path #3's bit-level rule,
  but for the wrong reason — now it is `true` for the SameValueZero reason
  the spec states).
- Dict keys: `d[1] = "a"; d[1.0]` returns `"a"` (was `null`); a NaN-keyed
  entry round-trips (was previously broken via `Double.Equals` which is
  reflexive for `Double.NaN`, so the change is more about *intent* than
  *observed behavior*).
- ±0 keys: `d[0.0] = "a"; d[-0.0]` returns `"a"` (was already `"a"` via
  `Double.Equals`, but the spec now states it normatively).
- `assert.equal(1, 1.0)`: keeps throwing (unchanged, but now spec'd).
- `assert.equal(0.0, -0.0)`: keeps passing (unchanged, but now spec'd —
  probed today).
- `assert.equal(NaN, NaN)`: keeps passing (unchanged, but now spec'd —
  probed today).
- Two distinct `secret("x")`: still not `in` each other; still distinct dict
  keys (DE4-locked).

### Specification Delta

Spec-first per `AGENTS.md` → *The Specification is the Law*. The exact normative
prose this unit adds to or changes in `docs/Stash — Language Specification.md`
§Equality. Every clause is backed by a `Category=Conformance` test (the
per-phase `done_when` names the spec edit AND the test). The current §Equality
section (L691–L761) has six paragraphs after the sealed-divergence carve-out is
retired; this unit edits two of them and adds two new ones.

#### Edit E1 — Replace the membership/keying carve-out (L730–L750)

The current prose `**Membership and keying use tag-strict equality — not the
numeric equivalence class.**` (L730) through `arr.contains(arr.map(xs, (x) =>
conv.toFloat(x)), 1.0)` (L749) is **replaced** by:

> **Membership and keying use SameValueZero — a value-equality comparator
> aligned with the numeric-coercion rule, with one floating-point delta.** The
> `in` operator (array element membership and dict key lookup), `arr.contains`,
> `arr.indexOf`, `arr.remove`, related array-search built-ins, and dictionary
> key storage all use **SameValueZero equality**: int/float/byte operands
> compare by mathematical value (the numeric-coercion rule above), the floats
> `+0.0` and `-0.0` are the same value, and `NaN` is **self-equal by value**.
> Two `NaN`s in the same collection are considered equal even though `NaN !=
> NaN` under `==`.
>
> Concretely:
>
> - `1 in [1.0]` is `true`. `arr.contains([1], 1.0)` is `true`.
>   `arr.contains([0], -0.0)` is `true`.
> - `NaN in [NaN]` is `true` (`NaN`-self-equal by value, not by identity).
> - Integer key `1` and float key `1.0` are the **same** dictionary key: a
>   dictionary populated with `d[1] = "a"` returns `"a"` for `d[1.0]`. The
>   floats `+0.0` and `-0.0` are the **same** key.
> - A `NaN` key round-trips: `let d = {}; d[NaN] = 1; d[NaN]` returns `1`.
>
> This is the **collection equivalent of `==`** with the single delta that NaN
> is treated as self-equal by value (a key collection users universally
> expect — a value put in is a value found back). The `==` operator is
> unaffected: `NaN == NaN` remains `false` and `+0.0 == -0.0` remains `true`
> per IEEE 754. The non-numeric branch of SameValueZero delegates to the
> per-category equality rule above: two distinct `secret("x")` constructions
> are **not** the same dict key and are **not** `in` each other (reference
> identity per §Secret Values); two arrays with the same elements but distinct
> constructions are **not** the same key (reference identity); enums, structs,
> functions, namespaces, futures, ranges, and `Error` values are keyed by
> reference identity. To key by content for aggregates, use a stringified or
> hashed key.

#### Edit E2 — Add the assert-strict clause as a new paragraph after the per-category table (after L729)

> **Strict assert equality.** The `assert.equal` and `assert.notEqual` built-ins
> use a **type-strict** equality that is neither `==` nor SameValueZero. Operands
> of different `typeof` are not equal even within the numeric class — so
> `assert.equal(1, 1.0)` raises `AssertionError` even though `1 == 1.0` is
> `true`. Within a single category, comparison is by value (so
> `assert.equal(0.0, -0.0)` passes and `assert.equal(NaN, NaN)` passes — the
> floating-point reflexive cases an assertion test author expects), and
> aggregate/secret values compare by reference identity (per the per-category
> table above; `assert.equal(secret("x"), secret("x"))` raises). Test authors
> who want operator-`==` semantics in an assertion write `assert.isTrue(a ==
> b);` instead.

#### Edit E3 — Add the named-modes closing sentence (before *Floating-point edges* at L751)

> **Three named modes — one source of truth.** Stash exposes a small, closed
> set of equality semantics, named for the use site that consumes each: the
> `==`/`!=` operator (the numeric-coercion rule above), collection
> membership and dict keys (SameValueZero), and `assert.equal`/`assert.notEqual`
> (strict). These three are the complete vocabulary; no other observable
> equality semantics exist in the language. The names are normative — a
> future addition to this set is a breaking change to §Equality.

#### Edit E4 — Retire the backlog-stub pointer (L744–L749)

The closing sentence of the current carve-out — `The hash-contract
requirement ... a coordinated change reserved for a dedicated future unit (see
\`.kanban/0-backlog/bugs/numeric-equality-inconsistency-in-operator-dict-keys.md\`)`
through the `arr.contains(arr.map(xs, (x) => conv.toFloat(x)), 1.0)` workaround
— is **deleted**. The backlog-stub pointer is no longer needed; the unit it
pointed at is this one.

### Implementation Path

The spec is the spine. Each phase lands either (a) a behavior-preserving
forwarder + the meta-test exemption-list shrink, or (b) a behavior-changing
migration + the matching spec edit + the conformance test asserting the
sealed law. Phases never ship a spec clause without the code change that
makes it true, and never ship a code change without the conformance test
asserting the sealed behavior.

1. **`Stash.Core/Runtime/StashEquality.cs`** lands in P1 — the bounded-domain
   chokepoint. Three named modes over `NumericCoercingEquals(a, b, bool
   nanSelfEqual)`. The `==` operator's `RuntimeOps.IsEqual` becomes a thin
   forwarder; behavior is preserved byte-for-byte (DE1). No spec edit
   yet — the migration is internal.
2. **Membership** (`in`, `arr.contains`/index/remove etc.) lands in P2 with
   spec Edit E1 (membership half of the clause). User-visible behavior
   flips for `1 in [1.0]` and friends; secret reference identity is
   preserved (DE4 conformance proof lands in the same phase).
3. **Dict keys** (`StashDictionary._entries` migration) lands in P3 with
   spec Edit E1 (keying half). **The whole §Equality clause is sealed at
   P3 close** — P2 ships the membership-half sentences and P3 ships the
   keying-half sentences, so each phase's prose matches shipped behavior.
   Secret + DE4 conformance proven again in P3 for the dict-key path.
4. **Assert** (`assert.equal`/`assert.notEqual`) lands in P4 with spec Edit
   E2. Behavior is preserved (cross-type still throws; ±0 / NaN profile
   probed today is now spec'd normatively). `RuntimeValues.IsEqual` is
   retired.
5. **Interning fold** (`ChunkBuilder.StashValueComparer` deletion +
   `StashValue.cs:152` comment update) lands in P5. No spec edit (the
   constant-pool key is not a normative equality mode). A small
   constant-pool regression test confirms `0.0`/`-0.0`/`NaN`/literal-1.0
   intern correctly.
6. **Closing** — spec Edit E3 (the named-modes closing sentence), Edit E4
   (retire backlog-pointer paragraph), `examples/equality.stash` (authored
   by `stash-author`), tooling checklist verification, coverage.md
   updates, final gate. The Detect meta-test exemption list is at its
   minimal sanctioned-uses-only state.

### Cross-Cutting Concerns

The "all equality routes through one definition" property is the textbook
cross-cutting concern this unit is built to solve. Full Construct — delete
`IEquatable<StashValue>` so a forgotten participant fails to compile — is
**infeasible** because the interning key is load-bearing for the bytecode
serialization format (the constant pool needs *some* `StashValue`-keyed
hashing semantics, distinct from any runtime equality mode). The realistic
guard is **Detect**, built per the architect's playbook: green with a
pinned exemption list in the chokepoint-build phase, shrinking each phase
as sites migrate, ending at the sanctioned interning use.

| Concern | Single source of truth | Omission prevented by |
| --- | --- | --- |
| Every runtime equality decision routes through one of three named modes | `Stash.Core/Runtime/StashEquality.cs` (three public modes over private `NumericCoercingEquals`) | **Detect with teeth** — `Stash.Tests/Conformance/Equality/EqualityChokepointMetaTests.cs` is a Roslyn sink-scan flagging any `.Equals(` / `IsEqual(` invocation targeting a runtime value (`StashValue` and the aggregate/secret/struct/enum object reps) *outside* `Stash.Core.Runtime.StashEquality`. **Goes up green with a pinned exemption list in P1**, shrinks each phase, ends at the sanctioned interning use (`StashValue`'s own `IEquatable` consumed by `ChunkBuilder._constantMap`) at P5. Copies `NoMagicAuthStringsMetaTests`: load-order-deterministic MetadataReferences (`TRUSTED_PLATFORM_ASSEMBLIES`), `MinScannedFiles` floor, **binding-floor** (asserts `StashEquality` resolves to a non-error symbol), **fail-path teeth self-test** (a fixture that should trip the scan, and does). The exemption list is append-only post-unit-close. |
| The three normative equality modes are the closed vocabulary of equality semantics — no fourth mode appears in the runtime | `StashEquality`'s three public members (`OperatorEquals`, `SameValueZero`, `StrictEquals`) | **Detect (cross-checked with spec)** — `EqualityModesVocabularyConformanceTests` (P5) enumerates the `public static` / `public sealed class` members of `StashEquality` via reflection and asserts the set equals the spec-named three. A future addition to the type is the breaking change the spec calls out (Edit E3); the conformance test enforces the closure. |
| SameValueZero's non-numeric branch preserves secret reference identity (DE4 locked) and aggregate reference identity | `StashEquality.SameValueZero`'s delegation chain → `StashValue.Equals` → `object.Equals` → `StashSecret.ReferenceEquals` (existing chain from §Values D4) | **Conformance proven from both consumer surfaces** — every secret-identity assertion runs once through `in`/`arr.contains` (P2 conformance) and once through dict-key lookup (P3 conformance), each proving two distinct `secret("x")` constructions are not `in` each other and are distinct keys, and an aliased handle is `in` itself and is the same key. The cross-surface duplication is intentional: each migration phase's conformance class re-proves DE4 from its own consumer surface, preventing a regression that one surface but not the other might mask. |
| The constant-pool interning key stays at-least-as-fine as the finest runtime equality | `StashValue.Equals` (`IEquatable<StashValue>`, bit-level on Float) — the sole *non-equality* surface left after the fold | **Construct + interning regression test** — interning's correctness is "at-least-as-fine," achievable as **Construct** because the key type is `StashValue` and its `IEquatable` is the only equality available without going through `StashEquality`. A P5 interning regression test pins the not-collapsed cases (`1` vs `1.0` distinct constants; `0.0` vs `-0.0` distinct constants; `NaN` literal preserved; small int vs same-value byte distinct). |

This is the second milestone unit to ship a Detect meta-test (the first was
the `ConformanceTraitMetaTests` trait guard in unit #1). The pattern is
identical: build the meta-test alongside the structure it guards, ship
fail-path self-test + binding-floor + load-order-deterministic
MetadataReferences, and never schedule it as the final phase.

## Acceptance Criteria

End-to-end behavior that proves the unit is done:

1. **`Stash.Core/Runtime/StashEquality.cs` exists** with three public members
   (`OperatorEquals`, `SameValueZero`, `StrictEquals`) over a private
   `NumericCoercingEquals(StashValue a, StashValue b, bool nanSelfEqual)`
   core, in `namespace Stash.Core.Runtime`.
2. **`Stash.Tests/Conformance/Equality/` exists** with at least six
   `*ConformanceTests.cs` files (one per clause group), each
   `[Trait("Category","Conformance")]`-marked, plus the
   `EqualityChokepointMetaTests.cs` Detect meta-test.
3. `dotnet test --filter "Category=Conformance"` runs the full conformance
   surface (Async + Values + Equality), binds non-zero tests in each suite,
   and is green.
4. `Stash.Tests/Conformance/ConformanceTraitMetaTests.cs`'s
   `MinScannedParticipants` floor is raised to include the Equality
   participants; the fail-path self-test still passes.
5. `Stash.Tests/Conformance/Equality/EqualityChokepointMetaTests.cs` is green
   with the **minimal sanctioned exemption list** (`StashValue`'s own
   `IEquatable` consumed by `ChunkBuilder._constantMap`, plus internal
   `StashEquality` delegations); the **fail-path teeth self-test** is green
   (a fixture with a banned `.Equals(` outside `StashEquality` makes the
   scan flag it); the **binding-floor** is green (asserts `StashEquality`
   resolves to a non-error symbol so the scan can't pass vacuously).
6. **`1 in [1.0]` is `true`** in a `stash -c` probe. `arr.contains([1], 1.0)`
   is `true`. `arr.contains([0], -0.0)` is `true`. `NaN in [NaN]` is `true`.
7. **Dict keys merge int/float/byte and unify ±0:** `let d = {}; d[1] = "a";
   d[1.0]` returns `"a"`. `let d = {}; d[0.0] = "a"; d[-0.0]` returns `"a"`.
   `let d = {}; d[1] = "a"; d[conv.toByte(1)]` returns `"a"`.
8. **NaN-key round-trip works:** `let big = conv.toFloat("1e308") * 10.0; let
   n = big - big; let d = {}; d[n] = "x"; d[n]` returns `"x"`.
9. **`assert.equal(1, 1.0)` keeps throwing** (DE3 — type-strict).
   `assert.equal(0.0, -0.0)` keeps passing (DE3 — Double.Equals).
   `assert.equal(NaN, NaN)` keeps passing (DE3 — Double.Equals).
10. **`1 == 1.0` is `true`** (DE1 unchanged); `NaN == NaN` is `false` (DE1
    unchanged); the `==` operator's full §Values & Types Edit 4 prose is
    unaffected.
11. **Secret reference identity preserved (DE4):** `let a = secret("x");
    let b = secret("x"); a in [b]` is `false`; `let d = {}; d[secret("x")]
    = 1; d[secret("x")]` is `null`; `let t = secret("x"); d[t] = 1; d[t]`
    is `1`. Re-asserted through both `in` (P2) and dict-key (P3)
    conformance.
12. **§Equality clause sealed:** `docs/Stash — Language Specification.md`
    §Equality contains spec Edits E1 + E2 + E3 + E4. The legacy
    sealed-divergence prose at L730–L750 is replaced by the SameValueZero
    membership/keying clause; the assert-strict clause is present; the
    three-named-modes closing sentence is present; the backlog-pointer
    paragraph is deleted.
13. **`Stash.Tests/Conformance/Values/EqualityNumericConformanceTests.cs`
    sealed-divergence region is flipped:** the `// §Equality — Membership
    and keying: tag-strict divergence (sealed)` region (~L351) is gone
    or rewritten; its three `Assert.False(...)`/`Assert.Null(...)`
    assertions are `Assert.True(...)`/`Assert.Equal("int", ...)` or have
    moved to `Stash.Tests/Conformance/Equality/` with the flipped
    assertions; the doc comments are updated.
14. **`coverage.md` updated:** the §Values & Types row's
    `Highest-value open items` column drops the membership/keying
    carve-out note; cross-cutting workstream #3 note is extended to
    include the membership/keying surface; the contradictions table
    has no §Equality entry.
15. **Tooling checklist (mandatory per `.claude/language-changes.md`):**
    each of LSP / DAP / Playground / VS Code extension / Static analysis
    is explicitly checked and recorded in P6's commit. No new syntax →
    most are "no change needed," but each is stated rather than skipped.
    No new stdlib function → `Wave1ThrowsCoverageTests`,
    `Wave2ThrowsCoverageTests`, and `CompletionSurfaceSnapshotTests`
    confirmed green with no churn (also stated rather than skipped).
16. **`examples/equality.stash` exists** (authored by `stash-author` in
    P6), demonstrating the three named modes with observable behavior
    (`==`, `in`, `assert.equal`) and verified with the freshly-built
    binary per `.claude/language-changes.md` (`dotnet run --project
    Stash.Cli/ -- examples/equality.stash`).
17. **Full `dotnet test` is green;**
    `dotnet test --filter "Category=Conformance"` is green and binds
    non-zero tests across `Async/`, `Values/`, and `Equality/`.
18. `stash scripts/checkpoint/checkpoint.stash validate-spec
    language-standard-equality` passes.

Error behavior that proves the failure paths work:

- The Detect meta-test's **fail-path teeth self-test** catches a fixture
  with a banned `.Equals(` outside `StashEquality` — proves the scan has
  teeth and can never pass vacuously.
- The meta-test's **binding-floor** assertion catches a hypothetical
  rename of `StashEquality` (the scan would bind zero target symbols
  and the floor flips the test red).
- The trait guard's `MinScannedParticipants` floor catches an
  accidental delete of any Equality conformance class.
- The interning regression test catches a hypothetical fold of `1` and
  `1.0` into one constant.

Cross-entrypoint behavior:

- The chokepoint module lives in `Stash.Core/Runtime/` so all
  consumers — `Stash.Bytecode` (operator, `in`, dict), `Stash.Stdlib`
  (arr, dict, assert builtins), `Stash.Hosting` (embedding), the LSP
  runtime, the DAP — share a single equality semantics by
  construction. Conformance tests run inside the test VM, which is the
  same `Stash.Bytecode` runtime as the other entrypoints, so the proof
  is uniform.

## Phases

The phase list lives in `plan.yaml`. Each phase has a concrete `done_when`
and verify commands. Spec edits land in the same phase as the behavior
that makes them true, per the §Async/§Values precedent.

| Phase | Spec edits landed | Code change | Conformance / meta-tests landed |
| --- | --- | --- | --- |
| P1 — Build the chokepoint + route operator | — (no observable change) | `StashEquality.cs` lands with three modes + private `NumericCoercingEquals`. `RuntimeOps.IsEqual`/`IsEqualCrossTag`/`IsEqualSlow` become thin forwarders to `StashEquality.OperatorEquals` (preserving `IVMEquatable` dispatch). Operator tests stay green. | `OperatorChokepointConformanceTests` (asserts every operator equality path reaches `StashEquality.OperatorEquals` via observable behavior — e.g. `IVMEquatable.VMEquals` precedence preserved). `EqualityChokepointMetaTests` lands **green with the explicit P1 exemption list** naming every not-yet-migrated site (#2, #3-via-Contains, #5, ~6 arr/dict/assert callers). Fail-path self-test + binding-floor lands here. |
| P2 — Membership: `in` + `arr.contains` + array search | **Edit E1 — membership half** (the `in`/`arr.contains`/index/remove sentences) | `RuntimeOps.Contains` (RuntimeOps.cs:335) uses `StashEquality.SameValueZero.Equals`. `ArrBuiltIns.cs` 6 sites move to `StashEquality.SameValueZero`. Meta-test exemption list shrinks (removes these sites). | `MembershipConformanceTests` (`1 in [1.0]`, `arr.contains([1], 1.0)`, `NaN in [NaN]`, `±0` membership, secret-DE4 from the `in` surface, cross-byte/int/float). Flips the sealed-divergence assertions in `Stash.Tests/Conformance/Values/EqualityNumericConformanceTests.cs` ~L351 region. |
| P3 — Dict keys: migrate `_entries` to `Dictionary<StashValue, StashValue>` | **Edit E1 — keying half** (the dict-key sentences + the §Equality clause fully closed) | `StashDictionary._entries` becomes `Dictionary<StashValue, StashValue>` with `StashEquality.SameValueZero` comparer. Boxed-key sites (`FromObject(kvp.Key)` ~L68/L99, `ToObject()!` L189/L196) rewritten. `DictBuiltIns.cs:247` site migrated. Meta-test exemption list shrinks again. | `DictKeyConformanceTests` (cross-type key, ±0 key, NaN-key round-trip, secret-DE4 from the dict-key surface, byte/int/float keys, hash-equals consistency: equal keys hash equal). |
| P4 — Assert: `StrictEquals` mode + retire `RuntimeValues.IsEqual` | **Edit E2** (assert-strict clause) | `AssertBuiltIns.cs:26,42` routes through `StashEquality.StrictEquals`. `RuntimeValues.IsEqual` is deleted (callers all migrated). `RuntimeValues.IsNumeric`/`ToDouble` stay. Meta-test exemption list shrinks again. | `StrictAssertConformanceTests` (the probed behavior is now normative: `assert.equal(1, 1.0)` throws; `assert.equal(0.0, -0.0)` passes; `assert.equal(NaN, NaN)` passes; `assert.equal(secret("x"), secret("x"))` throws — DE4 from the assert surface). |
| P5 — Interning fold: delete `ChunkBuilder.StashValueComparer` | — (the interning key is not a normative equality mode) | `ChunkBuilder.StashValueComparer` (L1220–L1247) deleted; `_constantMap` uses `new Dictionary<StashValue, ushort>()` (default `IEquatable` from `StashValue`). `StashValue.cs:152` comment updated from "for collection ops" to "for the constant-pool key — see `StashEquality` for runtime equality." Meta-test exemption list is at minimal sanctioned-uses-only state. | `InterningRegressionTests` (constant pool: `1` vs `1.0` distinct; `0.0` vs `-0.0` distinct; literal-`NaN` interning preserved; small int vs same-value byte distinct). Plus the vocabulary closure test (`EqualityModesVocabularyConformanceTests` enumerates `StashEquality`'s public members and asserts the set equals `{OperatorEquals, SameValueZero, StrictEquals}`). |
| P6 — Closing: closing-sentence spec, examples, tooling sweep, coverage roll-up | **Edit E3** (three-named-modes closing sentence) + **Edit E4** (retire backlog-pointer paragraph) | `examples/equality.stash` authored by `stash-author` (called for from this phase). LSP / DAP / Playground / VS Code / Static analysis tooling components explicitly verified (each stated, not skipped) per `.claude/language-changes.md`. `Wave1ThrowsCoverageTests`/`Wave2ThrowsCoverageTests`/`CompletionSurfaceSnapshotTests` confirmed green (no churn). `coverage.md` updated. | — (no new participant; the closing phase is the documentation roll-up + final gate per §Async/§Values precedent). |

## Open Questions

- ~~**StrictEquals vs StructuralIdentity — same or distinct mode?**~~ **RESOLVED
  2026-06-07 by direct CLI probe:** `assert.equal(0.0, -0.0)` PASSES (assert
  unifies ±0 via `Double.Equals`); `assert.equal(NaN, NaN)` PASSES (assert
  treats NaN==NaN via `Double.Equals`). The interning key is bit-level
  (`_data == other._data` → ±0 distinct). They are **distinct modes**;
  `StrictEquals` is a named runtime mode, `StructuralIdentity` is the
  constant-pool key, and they have different ±0 / NaN profiles. The
  assert clause (Edit E2) states the ±0-unified + NaN-reflexive profile
  as normative; the interning key stays bit-level by `StashValue.Equals`.
- ~~**Dict-key migration: `Dictionary<object,_>` wrap-with-adapter vs
  migrate to `Dictionary<StashValue, _>`?**~~ **RESOLVED 2026-06-07 in the
  Make-It-Right direction:** migrate `_entries` to `Dictionary<StashValue,
  StashValue>` (P3). The decisive argument: one comparer (the
  `StashEquality.SameValueZero` instance), no boxed-`object` adapter, no
  "two comparers must stay in lockstep" hazard. The blast-radius is small
  (3 boxing sites — `FromObject(kvp.Key)` at L68/L99 and `ToObject()!`
  at L189/L196) and rewritten in the same phase. Falls back to the
  boxed-`object` adapter only if a P3 investigation surfaces a
  C#-public-surface key-type leak (none expected — `StashDictionary` is
  an internal runtime type with all access through its own methods).
- **`RuntimeValues.IsEqual` retirement timing.** Retired in P4 once
  arr/dict callers (P2/P3) and assert callers (P4) are all migrated.
  If a P4 investigation surfaces an unexpected internal caller (none
  expected per the line-grep), it routes through `StashEquality` or
  files a backlog stub before retirement.
- **Will a P-phase surface a hidden contradiction motivating a DE5?**
  The audit's DE1–DE4 are addressed by Edits E1–E4. If a P-phase
  discovers a new one (e.g. an undocumented `in`-on-`StashRange`
  behavior intersecting SameValueZero), file as a Decision Log
  addition in the same phase and proceed seal-first-then-bend per
  §Async/§Values precedent.

## Decision Log

| Date | Decision | Rationale |
| --- | --- | --- |
| 2026-06-07 | **Unit scope: the whole §Equality membership/keying carve-out + the named-modes vocabulary + the assert clause** (replaces L730–L750 in the spec; adds two new clauses + a closing sentence). Does NOT include §Values & Types' operator equality (already sealed by unit #2 D2), the per-category identity-vs-by-value rule (sealed by unit #2 Edit 4), or secret equality (sealed by unit #2 D4). | The §Equality clause is the one remaining unsealed seam in the otherwise-sealed §Values & Types section; the membership/keying carve-out explicitly pointed at this unit as its dedicated future. Smaller slicing leaves the SameValueZero collection rule scattered across `in`/`arr.contains`/dict-key as three separate units. |
| 2026-06-07 | **Reuse the unit-#1 trait guard unchanged.** Just bump `MinScannedParticipants` per phase. | The guard was built to amortize across all milestone units (Async, Values, Equality, and the 10 remaining sections). Adding `Equality/` directory triggers the participant scanner automatically because the namespace prefix matches `Stash.Tests.Conformance.*`. |
| 2026-06-07 | **DE1 (`==`/`!=` operator) — LOCKED unchanged from §Values D2.** Numeric-coercion rule for int/float/byte; `NaN != NaN`; `+0.0 == -0.0`. | Already sealed and shipped. This unit's operator-side change is a pure forwarder (`RuntimeOps.IsEqual` → `StashEquality.OperatorEquals`); no observable behavior change. |
| 2026-06-07 | **DE2 (collections — SameValueZero) — LOCKED per the seed.** Membership (`in`, `arr.contains`) and dict keys coerce int/float/byte by mathematical value, NaN is self-equal by value, ±0 is unified. | The common path (Python/JS/Lua/Perl 4-to-1 over Ruby); the only mechanically-available path for a NaN-by-value language (Stash NaN is an unboxed `double` with no object identity, so Python/Ruby's identity-based NaN-in-collection is *physically unavailable* — the seed's decisive cross-language finding). |
| 2026-06-07 | **DE3 (`assert.equal`/`notEqual` — strict, NOT SameValueZero) — LOCKED per the user instruction.** `assert.equal(1, 1.0)` keeps throwing. Within a category, ±0 is unified and NaN is reflexive (the probed `Double.Equals` profile). | The user instruction: assert is its own *named* strict mode, NOT operator-loose and NOT SameValueZero. Test-helper precedent (Ruby `eql?`, JS `Object.is`/`===` for tests, C#/Java `Object.Equals`). The ±0/NaN profile is `Double.Equals`'s, which today's assert already uses (probed 2026-06-07); sealing the profile rather than changing it. |
| 2026-06-07 | **DE4 (secret `==` — reference identity) — LOCKED from §Values D4-USER-OVERRIDE.** Two distinct `secret("x")` constructions remain not-`in` each other and remain distinct dict keys. SameValueZero's non-numeric branch delegates to `StashValue.Equals` → `object.Equals` → `StashSecret.ReferenceEquals`. | The user override from §Values D4: safety-by-default. Must survive the SameValueZero migration. Conformance proof lives in **both** P2 (membership phase) and P3 (dict-key phase) — each migration's class re-proves DE4 from its own consumer surface. |
| 2026-06-07 | **The chokepoint is `Stash.Core/Runtime/StashEquality.cs` (Core layer, not Bytecode).** | `Stash.Bytecode`, `Stash.Stdlib`, `Stash.Hosting`, and the LSP/DAP runtimes all depend on `Stash.Core`; placing the chokepoint in Core makes it reachable from every consumer without inverted dependencies. Matches the project's "Core has zero dependencies on any other Stash project" invariant (`Stash.Core/CLAUDE.md`). |
| 2026-06-07 | **The three names are normative spec vocabulary** (`OperatorEquals`, `SameValueZero`, `StrictEquals`), pinned by Edit E3's closing sentence and by `EqualityModesVocabularyConformanceTests` (P5). A future fourth mode is a breaking change. | The bounded-domain doctrine applied to equality: the closed set has a *named* source of truth. This is the architecture-level analogue of `Visibilities` enum / `enum` over `const` rule from `CLAUDE.md`. The vocabulary test enforces closure. |
| 2026-06-07 | **The Detect meta-test goes up green-with-an-exemption-list in P1, NOT red, NOT as a final phase.** | Per architect.md "Designing Out Cross-Cutting Omission" → "go up red with an exemption list when the shared component is built, and shrink to empty as phases migrate" — the corollary is that *red* there means "constraints exist that the not-yet-migrated sites *should* eventually satisfy"; the *test* is green-passing because the exemption list permits the not-yet-migrated sites. Going up actually-red would mean the meta-test fails until the final phase, which violates the per-phase-green invariant. Going up as the final phase would mean every prior phase landed unguarded. The exemption-list pattern is the architect's specified shape. |
| 2026-06-07 | **#4 (`ChunkBuilder.StashValueComparer`) is folded into `StashValue.Equals` (bit-level), NOT into the SameValueZero comparer.** Interning's only requirement is *at-least-as-fine-as* runtime equality; switching from #4's value-`==` to #3's bit-level is **finer** (over-splits ±0 into distinct constants — safe). | Advisor correction: the seed mis-stated "identical duplicate." They disagree on NaN and ±0 — #3 is bit-level (NaN-self, ±0-distinct), #4 is value-`==` (NaN-not-self, ±0-unified). Folding #4 → #3 corrects a latent #4 over-merge bug. The "for collection ops" comment on `StashValue.Equals:152` loses its justification once `in` moves off this path; update the comment in P5. |
| 2026-06-07 | **The interning key is not a fourth named equality mode.** It is `StashValue`'s own `IEquatable<StashValue>`, named `StructuralIdentity` informally but **not** exposed on `StashEquality`. | Doctrinally distinct: equality modes answer "are these values equal at runtime?"; the interning key answers "are these the same literal constant?" The architect's seed already made this distinction; this unit preserves it explicitly so a future reader doesn't try to unify them. |
| 2026-06-07 | **`StashDictionary._entries` migrates to `Dictionary<StashValue, StashValue>`** (the Make-It-Right direction), NOT wrapped with a boxed-`object` adapter. | Make-It-Right: one comparer (`StashEquality.SameValueZero`), no adapter, no two-comparers-in-lockstep hazard. Blast-radius is 3 boxing sites in `StashDictionary.cs` (L68, L99, L189/L196), rewritten in P3 alongside the migration. Fall back to the adapter only if P3 surfaces an unexpected key-type leak across a public surface (none expected — `StashDictionary` is internal). |
| 2026-06-07 | **DE3's NaN/±0 profile is sealed at today's `Double.Equals` behavior (probed PASS/PASS), NOT changed.** | Probed today: `assert.equal(0.0, -0.0)` PASSES; `assert.equal(NaN, NaN)` PASSES. These are the `Double.Equals` reflexive cases. Test authors expect them. Sealing the profile rather than changing it preserves every existing assertion-using test. |
| 2026-06-07 | **Examples script is authored by `stash-author` in P6 (NOT by the architect).** The plan *calls for* `examples/equality.stash`; the agent writes it docs-first per `.claude/agents/stash-author.md`. | Architect doctrine forbids `.stash` authoring in the spec phase. The `stash-author` agent reads the §Equality spec + §Values & Types + the relevant `Standard Library Reference` sections before writing, then emits the API plan, then writes — preventing "plausible-but-wrong" Stash code (the precedent failure the agent exists to prevent). |
| 2026-06-07 | **`final_verify` runs `Category=Conformance` as a discrete step in addition to full `dotnet test`.** | Matches the unit-#1 / unit-#2 vacuous-pass guard. If the filter ever binds zero conformance tests, the discrete step fails loud rather than passing silently inside the full suite's green sea. |
| 2026-06-07 | **`coverage.md` update lives in P6** (the closing phase), not in earlier phases. | Matches the unit-#1 / unit-#2 closing-phase convention. Per-phase updates would churn the file 6 times; one consolidated edit at close is cleaner. |
