# RFC: Language Standard — Seal §Values & Types

> **Status:** Draft
> **Owner:** cristian.moraru@live.com
> **Created:** 2026-06-06
> **Slug:** language-standard-values
> **Milestone:** language-standard  <!-- unit #2; follows language-standard-async; reuses Conformance/<Area>/ scaffold -->

## Summary

Seal `docs/Stash — Language Specification.md` **§Values and Types** (L570–L664) to the
milestone's Unit-DoD: the full observable behavior of the type model, `typeof`/`nameof`,
truthiness, equality, type coercion, and secret values — including their negative space —
is written into the spec, every normative clause is backed by a `Category=Conformance`
test under `Stash.Tests/Conformance/Values/`, and any code that contradicted the now-
written law is fixed (seal-first-then-bend).

This is **unit #2** of the `language-standard` milestone (after
`language-standard-async`). §Values & Types is the **substrate cross-cutting workstream
#3** in `coverage.md`: every conditional in every section (every `if`/`while`/ternary,
every retry predicate, every `&&`/`||`), and every `==`/`!=` in every section, derive
their meaning here. Sealing it unblocks Lexical, Bindings, Expressions, Statements,
Aggregate, Errors, Modules, and Shell to be sealed without re-deriving these primitives.

## Motivation

The spec's §Values & Types section is the **most foundational unsealed row** in
`coverage.md` (row #2). It contains:

1. **A direct shipped-vs-law contradiction** (audit-confirmed): spec L629 lists empty
   array as falsey; the impl (`RuntimeOps.IsFalsy` + the absence of an `IVMTruthiness`
   implementation on `List<StashValue>`) treats `[]` as **truthy**.
   `Truthiness_EmptyArrayIsTruthy` (`Stash.Tests/Interpreting/InterpreterTests.cs:4204`)
   pins the shipped behavior.
2. **A live spec-vs-impl-vs-itself trilemma on cross-type `==`.** Spec L645 forbids
   cross-type coercion for equality. The runtime path (`RuntimeOps.IsEqual`) honors
   this for cross-tag pairs — `let i = 1; let f = 1.0; i == f` returns **false**. But
   the constant folder in `Compiler.Expressions.cs:874–893` (`TryFoldBinary`) promotes
   int→double for equality on literal operands — so `1 == 1.0` (both literals) returns
   **true** at compile time. The language disagrees with itself depending on whether
   the operands are literals or variables. This was unknown before this unit's
   empirical sweep. **Ratified resolution (D2):** the folder is the law-in-practice
   and the user-preferred mainstream answer; the **runtime path is the bug**. Numeric
   categories (`int`, `float`, `byte`) **coerce by mathematical value** for `==`/`!=`
   (`1 == 1.0` → `true`, `0 == 0.0` → `true`, `-0.0 == 0` → `true`); non-numeric
   cross-category pairs (`bool`, `null`, `string`, …) remain unequal. `byte` also
   participates in numeric-equality coercion to stay consistent with the byte→int
   arithmetic promotion rule (Edit 5 case 2). Probed today: `byte 0 == 0` returns
   `false` (runtime tag-strict) — that is a coercion-consistency bug that this unit
   fixes alongside the int↔float fix.
3. **Explicit prose punts to "the implementation"** at L642 and L650 — equality and
   type coercion are deferred to "whatever the runtime does", which the *Specification
   is the Law* doctrine forbids: the spec is the law; "ask the code" is not normative.
4. **A typeof vocabulary contradiction.** The type table at L603 names `bytesize`, but
   the runtime returns `"bytes"` (`StashByteSize.PrimitiveTypeName => "bytes"`,
   confirmed by `typeof(1KB)` on the built CLI). One side is wrong.
5. **The `range` type is absent from the type table** at L582–L604, yet `typeof(1..3)`
   returns `"range"` and `range` is a documented value category used throughout the
   language. The audit row #2 flagged this; confirmed by probe.
6. **Equality edges are unspecified.** `null == null`, `0 == -0.0`, `0.0 == 0` (mixed
   type at runtime), NaN reachability and equality, identity vs by-value comparison for
   arrays / dicts / structs / enums / errors / futures / namespaces / functions /
   ranges / secrets / IPs / durations / bytesizes / semvers — each must be pinned
   positively (the rule) and negatively (what is not guaranteed).
7. **Secret-value semantics are under-specified.** The spec covers redacted display but
   says nothing about equality (probed: `secret("x") == secret("x")` returns **true** —
   a potential information leak that should be either ratified or fixed), the
   `+`-concatenation taint behavior (probed: any `+` involving a secret produces a
   secret), `secret(secret(x))` (probed: auto-unwraps so secrets never nest), or what
   `typeof(secret(x))` returns (probed: `"secret"`).
8. **An undisclosed contradiction in the falsey list.** `StashError.VMIsFalsy => true`
   means a caught Error is **falsey** in a conditional — `try { ... } catch (e) { if
   (e) { ... } }` never enters the `if` body. The spec lists nothing about errors being
   falsey. **Ratified resolution (D5):** errors are **truthy** (fix the code). This is
   the correct mainstream rule — a caught error is a real value, not an absence-marker,
   and `if (e)` is a natural "did I catch one?" idiom. Code change in P2: flip
   `StashError.VMIsFalsy` from `true` to `false`; blast-radius audit before commit
   (no `if (caught_error)` pattern should regress).
9. **`StashLiteralArg.VMIsFalsy => string.IsNullOrEmpty(Text)`** affects command-line
   argument literal values. Confirm whether it is user-observable; if so, spec it; if
   not, document the internal-only invariant.

Without sealing §Values & Types, every later unit has to either inherit unwritten
substrate (the §Async unit had to lean on the working-but-unspecified Future identity
`==` semantics) or spec its own local rules and create drift. Sealing this unit pays
the substrate cost once.

## Goals

- **Seal the type model section.** The category list (L582–L604) lists every runtime
  category that `typeof` can return, including `range`, in alphabetical order; every
  name in the list matches the actual string `typeof` returns at runtime
  (resolves the `bytesize`/`bytes` contradiction in the chosen direction). The list is
  declared closed: a future runtime category is a breaking change to the §Values & Types
  surface.
- **Seal `typeof` and `nameof`.** Pin the closed vocabulary `typeof` returns; document
  `typeof(null) == "null"` (not raise), `typeof(future)`, `typeof(error)`,
  `typeof(secret)`, `typeof(range)`, `typeof(enum_value)` vs `typeof(enum_type)`,
  `typeof(struct_instance)` vs `typeof(struct_type)`, and `typeof(namespace)`. Pin the
  type-name registry as the single source of truth.
- **Seal truthiness (the substrate).** Rule the empty-array / empty-dict contradiction
  per D1 (RATIFIED: empty `[]` and `{}` are **truthy** — correct the law). Rule the
  caught-error contradiction per D5 (RATIFIED: caught errors are **truthy** — fix the
  code; flip `StashError.VMIsFalsy` from `true` to `false`). Document the full closed
  falsey set explicitly (`null`, `false`, integer `0`, float `0.0` / `-0.0`, byte `0`,
  empty string `""`) and pin "every other value, including all aggregate, container,
  opaque-handle, error, and user-defined values, is truthy" as the closed negative
  space.
- **Seal equality.**
  - Pin cross-type `==` per D2 (RATIFIED: **numeric coercion**). Numeric categories
    (`int`, `float`, `byte`) compare by mathematical value for `==`/`!=` (`1 == 1.0`
    is `true`; `0 == 0.0` is `true`; `-0.0 == 0` is `true`; `byte 0 == 0` is `true`;
    `byte 7 == 7.0` is `true`). Non-numeric cross-category pairs remain unequal
    (`1 == true` is `false`, `0 == null` is `false`, `0 == ""` is `false`). The
    constant folder already folds `1 == 1.0` to `true` and is correct; the
    **runtime path** (`RuntimeOps.IsEqual`/`IsEqualSlow` + the byte tag-strictness)
    is the bug — fix it so cross-tag numeric equality coerces, matching the folder.
    The literal-vs-variable pair pattern stays as a regression guard; after the
    fix it asserts both forms return the **same** boolean (e.g. `1 == 1.0` =
    `true` in both forms).
  - Pin equality semantics per category, replacing the L640–L646 punt with normative
    prose: primitives `null`, `bool`, `int`, `float`, `byte`, `string` compare by
    value (`int`/`float`/`byte` additionally coerce cross-category by mathematical
    value per D2); `duration`, `bytes`, `ip`, `semver` compare by value (semver per
    semver precedence); **`secret` compares by reference identity per D4 (RATIFIED:
    safety-by-default)** — to compare contents, `reveal()` first and use
    `crypto.constantTimeEquals` on the revealed values; arrays / dicts / structs /
    namespaces / functions / enums / futures / ranges / errors compare by reference
    identity.
  - Pin NaN: `NaN != NaN` (IEEE 754 default; reachable only via overflow arithmetic,
    e.g. `let big = conv.toFloat("1e308") * 10.0; let n = big - big;`).
  - Pin `-0.0 == 0.0` (true; IEEE 754 default) and `-0.0 == 0` (`true` per D2
    cross-numeric coercion).
- **Seal type coercion.** Replace the L648–L652 sketch with a positive rule
  (numeric int↔float promotion in arithmetic and relational operators; byte→int
  promotion in arithmetic; string concatenation with `+` where one operand is a
  string; no other implicit coercion) and a negative rule (no bool↔int, no
  null↔anything, no string↔number, no equality coercion).
- **Seal secret values.** Replace L654–L664 with normative prose covering: redacted
  display (println, interpolation, conv.toStr, concat), **equality by reference
  identity per D4 (RATIFIED: safety-by-default)** — to compare contents `reveal()`
  first and use `crypto.constantTimeEquals` on the revealed values for
  security-sensitive checks; `+`-concatenation taint propagation; automatic unwrap of
  nested secrets; `reveal()`'s capability gate (if any); `typeof(secret(x)) ==
  "secret"`; the impossibility of escaping the redaction through `conv.toStr` (the
  explicit escape is `reveal()`); the consequence that a secret used as a dict key
  keys by identity (two distinct `secret("x")` literals collide on neither lookup nor
  insertion).
- **Range is a first-class value.** Add `range` to the type table; document range
  identity equality (no by-value comparison); document `typeof(1..3) == "range"`,
  range iterability, range truthiness (always truthy per the empty-collection
  ruling), range `in` membership (per §Expressions; cross-reference).
- **Stand up `Stash.Tests/Conformance/Values/`** (mirrors `Conformance/Async/`):
  one test class per clause group, every test `[Trait("Category","Conformance")]`,
  every test cites its spec clause via name (`PerSpecValuesTruthiness`,
  `PerSpecValuesEqualityNumeric`, etc.) and class-level XML doc.
- **Bump `MinScannedParticipants`** in
  `Stash.Tests/Conformance/ConformanceTraitMetaTests.cs` as each test class lands;
  reuse — do not duplicate — the trait guard.
- **Update `coverage.md` row #2** to `sealed` with the populated `Conformance/Values/`
  pointer and the cross-cutting workstream #3 marked complete.

## Non-Goals

- **The Bindings & Scope section** (coverage.md row #3) is its own unit
  (`language-standard-bindings`). This unit touches §Values & Types' four
  subsections only.
- **The Expressions section** (row #4). `==` semantics live in §Values; relational
  comparison precedence and arithmetic semantics live in §Expressions. This unit
  pins the cross-type numeric rule for equality and references the §Expressions unit
  for the relational rule (where numeric int↔float promotion is honored — confirmed
  by probe).
- **The error-type taxonomy** (cross-cutting workstream #2). §Values has no fallible
  operations today (`typeof`, `==`, `secret()`, `reveal()` are all infallible at the
  language level; equality with a non-equatable value returns a defined boolean, not
  a throw). If sealing this unit surfaces a new failure mode that needs a typed error,
  cite the existing `TypeError` / `ValueError` registered types and forward-reference
  the upcoming `language-standard-errors` unit's catalogue. Do not invent error types
  here.
- **Typed-array element-type coercion** (e.g. `byte[]` rejecting a `string`). The
  typed-array surface is §Aggregate Types territory (row #7); this unit pins
  `typeof(byte_array) == "byte[]"` and similar VMTypeName strings as part of the
  type vocabulary, but leaves the runtime construction/mutation semantics to its
  owning section.
- **Lexer-level literal handling.** Whether `1.0` lexes as `FloatLiteral` is §Lexical;
  this unit takes the *value* the lexer produces as given and pins its runtime tag
  semantics. A spec edit for the floating-point literal grammar (e.g. allowing
  `1e308` scientific notation, which currently does NOT parse — probed) is out of
  scope.
- **A general structural equality option for aggregates.** Pin reference identity for
  arrays / dicts / structs; defer a structural equality option to §Aggregate Types.
- **`secret()` capability-gating of `reveal()`.** If the impl requires a capability to
  reveal, pin that. If it does not, pin that. Do not add a new capability gate.
- **Implementation changes outside the seal-first-then-bend rulings.** The truthiness
  fix (empty-array/dict), the const-folder fix (cross-type `==`), the type-name fix
  (`bytesize` vs `bytes`), and the optional secret-equality fix are in-scope. Any
  other behavior change is out-of-scope — file a backlog stub.

## Design

The shape is **build out the seal pass clause-by-clause, landing each spec edit and
its conformance test in the same phase, with the in-code seal-first rulings landing
in their phase too** (not deferred). Every phase is independently green: an
implementation-side ruling lands its code change + the conformance test asserting the
*newly-sealed* (not the legacy) behavior in the same phase.

### Surface

No new user-facing language surface. The deliverables are:

- A new test directory: `Stash.Tests/Conformance/Values/` with `*ConformanceTests.cs`
  files, each `[Trait("Category","Conformance")]`-marked, citing the spec clauses
  it proves.
- An updated `Stash.Tests/Conformance/_README.md` (single line in the dir table:
  `Values/` — §Values & Types).
- Updated `Stash.Tests/Conformance/ConformanceTraitMetaTests.cs` —
  `MinScannedParticipants` floor raised as each phase lands a participant class.
- Spec edits under `docs/Stash — Language Specification.md` §Values & Types
  (L570–L664), enumerated in *Specification Delta* below.
- Updated coverage roll-up at `.kanban/milestones/language-standard/coverage.md` —
  row #2 `unsealed → sealed`; cross-cutting workstream #3 marked complete.
- Targeted impl changes for the four seal-first rulings (per D1–D4 ratification):
  - Truthiness of empty array / empty dict (if law-side: none; if code-side: edit
    `RuntimeOps.IsFalsy`).
  - Runtime cross-type `==` fix (`RuntimeOps.IsEqual`) so int/float/byte
    coerce by mathematical value, matching the existing folder.
  - Secret equality flip to reference identity (`StashSecret.Equals`).
  - Caught-error truthiness flip (`StashError.VMIsFalsy => false`).
  - **No** code change for `bytesize`/`bytes` (D3 spec-side only).
- A `Truthiness_EmptyArrayIsTruthy` / `Truthiness_StructInstanceIsTruthy` test
  reclassification (`Category=Conformance` after the ruling, OR deleted/flipped if
  the ruling flips the behavior). Mirror the §Async drop-test reclassification
  pattern.

### Semantics

The §Values & Types substrate is defined here once and consumed everywhere. The
sealed prose pins:

- **The type-category vocabulary** (`typeof` return strings) is a closed set listed in
  the §Values & Types type table. Adding a new category is a breaking change.
- **Truthiness** is a per-tag decision (for primitives) plus a per-protocol decision
  (for `IVMTruthiness` implementers). The §Values prose enumerates the full closed
  falsey set; every other value is truthy.
- **Equality** decomposes into (a) the cross-type rule (`Tag != Tag → false`,
  exceptions enumerated), (b) the per-category rule (by value vs by reference), and
  (c) the NaN/`-0.0` IEEE-754 floating-point edges.
- **Type coercion** is a fixed list of cases (numeric int↔float, byte→int, string
  concatenation with `+`); the spec calls out everything else as *not* implicit.
- **Secret values** are a redaction wrapper with a defined display form, an explicit
  `reveal()` escape, taint-propagating `+`, automatic unwrap of nested secrets, and a
  declared equality semantics.

### Specification Delta

Spec-first per `AGENTS.md` → *The Specification is the Law*. The exact normative
prose each phase will add to or alter in `docs/Stash — Language Specification.md`
§Values and Types (L570–L664) is enumerated here. Every clause is backed by a
`Category=Conformance` test (the per-phase `done_when` names the spec edit AND the
test). **Specific phrasing below is the proposal; the Decision Log ratifications
may alter it before the spec edit lands.**

#### Edit 1 — Type table: add `range`, rule `bytesize`/`bytes`

The type table at L582–L604 is extended and corrected. **Direction depends on
ratification of D3** (see Decision Log). Proposed: keep the runtime string `"bytes"`
(it is shorter and already shipped on stack traces, `conv.toStr`, error messages, and
`typeof`), and change the spec table from `bytesize` → `bytes`. Insert `range`
between `dict` and `struct`.

New / modified rows:

```
| `range`     | half-open integer range (a..b)               |
| `bytes`     | byte quantity (renamed from `bytesize`)      |
```

Plus a closing normative sentence:

> The type names above are the complete closed set returned by `typeof`. A future
> runtime value category is a breaking change to §Values and Types; tooling and
> libraries may rely on this set being closed.

#### Edit 2 — `typeof` and `nameof` — pin the vocabulary

Replace L608–L619. The updated prose names:

> `typeof(value)` returns one of the strings enumerated in the type table above —
> a closed vocabulary. It is total: it never raises for any value, including
> `null`. For `int`, `float`, `bool`, `null`, `byte`, `string`, `array`, `dict`,
> `range`, `function`, `namespace`, `secret`, `duration`, `bytes`, `ip`, and
> `semver`, the string names the runtime category directly.
>
> For aggregate, opaque, and user-defined values:
>
> - `typeof(struct_instance)` returns `"struct"`. To recover the user-visible
>   struct name, use `nameof(instance)`.
> - `typeof(enum_value)` returns `"enum"`. `nameof(enum_value)` returns the
>   declared enum-type name.
> - `typeof(struct_type)` / `typeof(enum_type)` (the type *identifier*) returns
>   `"struct"` / `"enum"` respectively; the *type* and its *instance* are
>   indistinguishable by `typeof`.
> - `typeof(future)` returns `"Future"` (capitalized; Future is a runtime category,
>   not a user-defined type — the capitalization is normative).
> - `typeof(error)` returns `"Error"` for any first-class Error value caught by
>   `try/catch`. The user-visible error type (e.g. `"TypeError"`) is recoverable
>   via `nameof(err)` or the `err.type` property.
> - `typeof(typed_array)` returns the element-type string suffixed with `[]`
>   (e.g. `"byte[]"`, `"int[]"`, `"float[]"`, `"string[]"`).
>
> `nameof(value)` returns the declared type or binding name where one exists,
> and falls back to the same string `typeof` would return for values with no
> named declaration. For user-defined struct, enum, and interface types,
> `nameof` returns the user-visible name (e.g. `nameof(Server) == "Server"`).

#### Edit 3 — Truthiness — close the falsey set, rule the empty-collection contradiction, rule the caught-error contradiction

Replace L621–L633. **Ratified per D1 and D5.** D1: empty arrays and empty dicts are
**truthy** (correct the law; no code change). D5: caught errors are **truthy** (fix
the code; flip `StashError.VMIsFalsy` from `true` to `false`).

> The following values, and no others, are **falsey**:
>
> - `null`
> - `false`
> - the integer `0`
> - the float `0.0` (and the float `-0.0`)
> - the byte `0`
> - the empty string `""`
>
> **Every other value is truthy.** This explicitly includes:
>
> - The empty array `[]` and the empty dictionary `{}`. Use `len(value) == 0` or
>   `arr.isEmpty(value)` / `dict.isEmpty(value)` for emptiness checks.
> - Every caught Error value. `try { ... } catch (e) { if (e) { ... } }` enters
>   the `if` body — `e` is a real value, not an absence-marker.
> - Every struct instance, enum value, function, namespace, future, range, secret,
>   `ip`, `duration`, `bytes`, and `semver` value, regardless of internal state.
> - `NaN` (the float). Use `value != value` to detect NaN.
> - The string `" "` (a single space).
>
> Conditions in `if`, `while`, `do while`, ternary expressions, logical operators
> (`&&`, `||`, `!`), and retry predicates (`when`) use these rules. The same rules
> apply uniformly in CLI, embedded host, LSP runtime, and DAP-driven execution
> contexts.

#### Edit 4 — Equality — replace the L640–L646 punt with normative prose

Replace L635–L646 with a structured rule set. **Ratified per D2 (numeric coercion
for `==`/`!=`) and D4 (secret equality by reference identity).**

> `==` evaluates to `true` when its operands are equal. `!=` evaluates to the
> logical negation of `==`. Equality is **total**: it returns a boolean for any
> two operands and never raises.
>
> **Numeric-coercion rule.** The three numeric categories — `int`, `float`,
> `byte` — form a single equivalence class for `==` and `!=`. Operands from any
> two of these categories are compared by **mathematical value**: `1 == 1.0` is
> `true`, `0 == 0.0` is `true`, `-0.0 == 0` is `true`, `byte 0 == 0` is `true`,
> `byte 7 == 7.0` is `true`. This matches the byte→int and int↔float promotion
> in arithmetic and relational operators (see *Type Coercion*). NaN follows
> IEEE 754: `NaN != NaN` even with itself.
>
> **Non-numeric cross-category rule.** Outside the numeric equivalence class,
> two values of different runtime categories (per `typeof`) are never equal:
> `1 == true` is `false`, `0 == null` is `false`, `0 == ""` is `false`,
> `null == false` is `false`, `[] == null` is `false`. No other cross-category
> coercion is performed.
>
> **Per-category rule (within a category, or within the numeric equivalence class).**
>
> | Category | `==` semantics |
> | -------- | -------------- |
> | `null`, `bool`, `string` | by value |
> | `int`, `float`, `byte` | by **mathematical value across the three categories** (IEEE 754 for `float`: `0.0 == -0.0`, `NaN != NaN`) |
> | `duration`, `bytes`, `ip`, `semver` | by value (semver per semver precedence) |
> | `secret`, `array`, `dict`, `struct`, `enum`, `function`, `namespace`, `future`, `range`, `Error` | by **reference identity** (same value handle ↔ equal) |
>
> The reference-identity rule for `function` and `namespace` implies
> `io.println == io.println` is `true` (the same registered function handle), and
> `io == io` is `true` (the same namespace singleton). Two arrays with the same
> elements but distinct constructions are **not** equal (`[1] == [1]` is `false`);
> use `arr.equals` / `dict.equals` for structural comparison. Two distinct
> `secret("x")` constructions are **not** equal — to compare contents, `reveal()`
> first; for security-sensitive comparison use `crypto.constantTimeEquals` on the
> revealed values (see §Secret Values).
>
> **Floating-point edges.** `0.0 == -0.0` is `true`. `NaN != NaN` (and is the only
> value not equal to itself). `NaN` is reachable from a Stash script only via
> overflow arithmetic (`conv.toFloat("1e308") * 10.0` produces `Infinity`;
> `Infinity - Infinity` produces `NaN`); literal `0.0 / 0.0` raises
> `RuntimeError("Division by zero.")`.
>
> **The `==` operator's compile-time evaluation agrees with its runtime
> evaluation.** The compiler folds a literal equality only when the folded
> result equals the runtime result. In particular, `1 == 1.0` is `true` at both
> compile time and at runtime — the same boolean as `let i = 1; let f = 1.0; i
> == f`. A folder regression that diverges from the runtime path is a defect.

#### Edit 5 — Type coercion — replace the L648–L652 sketch with the closed list

Replace L648–L652.

> Stash performs implicit conversion only in the following narrow, enumerated
> cases. No other operator or operation performs implicit conversion between
> types:
>
> 1. **Numeric promotion in arithmetic, relational, AND equality operators.**
>    When `+`, `-`, `*`, `/`, `%`, `**`, `<`, `<=`, `>`, `>=`, `==`, `!=` are
>    applied to two numeric operands of different categories (any pair drawn
>    from `int`, `float`, `byte`), the operands are promoted to a common
>    category by mathematical value. For arithmetic and relational operators,
>    the result category is the wider of the two (`int`/`byte` → `int`;
>    anything paired with `float` → `float`). For equality (`==`/`!=`), the
>    result is a `bool`; see *Equality* for the full rule.
> 2. **Byte promotion to int in arithmetic and relational operators.** A `byte`
>    operand to `+`, `-`, `*`, `/`, `%`, `**`, `<`, `<=`, `>`, `>=` is promoted
>    to `int` before the operation. The result category is `int` or `float`
>    (per rule 1), never `byte`. Byte equality is covered by rule 1 (the three
>    numeric categories form a single equivalence class for `==`/`!=`).
> 3. **String concatenation with `+`.** When one operand of `+` is a `string`,
>    the other operand is stringified via the same rule as `conv.toStr` and the
>    result is a `string`. A `secret` operand of `+` produces a `secret` result
>    (see *Secret Values*).
>
> Outside these three cases, every operand mismatch raises a `RuntimeError` with
> the operand types in the message. Use the `conv` namespace for explicit
> conversions.

#### Edit 6 — Secret values — extend L654–L664 with the negative space

Extend L654–L664 with the equality, taint, and unwrap rules. **Ratified per D4:
secret equality is by reference identity — safety-by-default.**

> `secret(value)` wraps `value` for redaction. The wrapped value is stored
> unmodified inside the secret. A `secret` value displays as the redacted form
> `"******"` (six asterisks) in every stringification context — `io.println`,
> string interpolation (`"...${secret}..."`), `conv.toStr`, and the `+`
> string-concatenation operator (which propagates taint — see below). `reveal(s)`
> returns the wrapped value and is the only way to escape redaction; there is no
> capability gate on `reveal`.
>
> **Idempotent wrap.** `secret(secret(x))` is equivalent to `secret(x)`. Secrets
> do not nest; the inner secret's value is unwrapped before wrapping.
>
> **Taint-propagating `+`.** When `+` is applied with a `secret` operand on
> either side, the result is a `secret` wrapping the concatenation. The non-
> secret operand is stringified through `conv.toStr` before concatenation. This
> applies whether the other operand is a `string`, a number, or any other value.
>
> **Equality — reference identity.** `secret(a) == secret(b)` is `true` only
> when both operands are the **same secret handle** (`let t = secret("x"); t
> == t` is `true`); two distinct `secret("x")` constructions are **not** equal
> (`secret("x") == secret("x")` is `false`). The wrapped value never participates
> in equality. To compare contents, `reveal()` both secrets first and compare
> the revealed values; for security-sensitive comparison use
> `crypto.constantTimeEquals` on the revealed bytes (a constant-time check that
> resists timing side-channels). This is safety-by-default: `==` cannot be used
> as an oracle for the wrapped value.
>
> A consequence: a `secret` value used as a dict key keys by **identity**. Two
> distinct `secret("x")` literals are distinct keys; the same handle is the
> same key. To key by content, use the revealed value (with the security
> caveat in mind).
>
> `typeof(s) == "secret"` for any secret `s`. `nameof(s)` returns `"secret"`. A
> `secret` is truthy regardless of inner value.

#### Edit 7 — `range` as a first-class value (cross-reference)

Add a brief subsection (≈ 6 lines) at the end of the type model, before the
`### typeof and nameof` heading:

> ### Ranges
>
> A `range` value (constructed by the `..` operator; see §Expressions) is a
> first-class value with `typeof` string `"range"`. Ranges compare by reference
> identity (`1..3 == 1..3` is `false` for two distinct constructions; the same
> range value compared to itself is `true`). A range is always truthy,
> regardless of whether the range is empty (`0..0`). Iteration semantics, `in`
> membership, and arithmetic interactions are defined in §Expressions.

### Implementation Path

The spec is the spine. Each phase lands the spec edit AND the conformance test
asserting the sealed law. Phases that ratify a seal-first-then-bend code change
also land the code change.

1. **Spec (`docs/Stash — Language Specification.md` §Values and Types,
   L570–L664)** — Edits 1–7 land per phase, each pinned by a `done_when` quoting
   a distinctive phrase.
2. **Conformance suite (`Stash.Tests/Conformance/Values/`)** — directory exists
   with `_README.md`; one `*ConformanceTests.cs` per clause group; trait
   discipline inherited from `ConformanceTraitMetaTests` (just bump
   `MinScannedParticipants`).
3. **Implementation rulings (seal-first-then-bend; all five user-ratified
   2026-06-06 — see Decision Log).**
   - **D1 (empty `[]`/`{}` → truthy):** no code change. The legacy
     `Truthiness_EmptyArrayIsTruthy` test reclassifies to `Category=Conformance`
     in P2 (moved to `Conformance/Values/TruthinessConformanceTests.cs` or
     trait-added in place — pick the cleaner placement).
   - **D2 (cross-type numeric `==` → coerce by mathematical value):** the
     **constant folder is correct and stays untouched**. The **runtime path is
     the bug**: `RuntimeOps.IsEqual`/`IsEqualSlow` returns `false` for cross-tag
     numeric pairs (`int`↔`float`, `byte`↔`int`, `byte`↔`float`) and is fixed
     to coerce by mathematical value. The likely shape: add an `IsNumeric &&
     IsNumeric` branch into `IsEqual` that promotes to `double` and compares,
     parallel to the existing `ExecuteLtSlow` numeric-promotion branch. Verify
     `byte` participates (probed today `byte 0 == 0` is `false` — that flips
     to `true`). NaN must still be handled IEEE-754 (`NaN != NaN`). Non-numeric
     cross-category pairs continue to return `false` unchanged.
   - **D3 (`bytes` not `bytesize`):** keep the runtime string `"bytes"`. Change
     the spec table only — no code change, no stdlib reference regen, no
     metadata edits.
   - **D4 (secret `==` → reference identity):** change `StashSecret.Equals(StashSecret? other)`
     to `ReferenceEquals(this, other)`; `GetHashCode()` switches to
     `RuntimeHelpers.GetHashCode(this)` (identity hash). The `IEquatable<StashSecret>`
     interface implementation stays but the contract narrows to identity. The
     route through `RuntimeOps.IsEqualSlow` → `IVMEquatable.VMEquals` (if any)
     or `RuntimeValues.IsEqual` must reach the new identity check; verify
     there is no second equality path (e.g. a dict-key path) that uses
     `Equals(object)` and bypasses identity.
   - **D5 (caught `Error` → truthy):** flip `StashError.VMIsFalsy` from `true`
     to `false`. Blast-radius audit in P2 before commit: grep the codebase
     (`StashError`-typed conditions, `IsFalsy` callers) for any code that
     relies on caught-error-falsey; none expected (the user idiom is
     `if (e != null)`, not `if (e)`), but verify.
4. **Coverage map** — row #2 `unsealed → sealed`; cross-cutting workstream #3
   marked complete.
5. **Stdlib reference regeneration** — not required for any ratified direction
   (D3 ratified code-side: no code change; D4 ratified code-side but
   `StashSecret` metadata does not appear in the reference; D5 flips a falsey
   flag, no docs surface). P7 still runs `dotnet run --project Stash.Docs/`
   as a no-op confirmation; if the reference changes for any reason, that
   change is committed.

### Cross-Cutting Concerns

The truthiness / equality / coercion / typeof substrate is the textbook
cross-cutting concern of this milestone — its consumers are every later
unit. The single sources of truth and omission-prevention mechanisms:

| Concern | Single source of truth | Omission prevented by |
| --- | --- | --- |
| Every `Conformance/Values/` test class carries `[Trait("Category","Conformance")]` | `Stash.Tests/Conformance/ConformanceTraitMetaTests.cs` (the unit-#1 trait guard; **reused unchanged**) | **Detect with teeth (inherited from unit #1)** — reflection scan over `Stash.Tests.Conformance.*`; participant filter (simple-name ends with `ConformanceTests` OR has a `[Fact]`/`[Theory]`) automatically picks up the new `Values/` classes. `MinScannedParticipants` floor is bumped per phase. The fail-path self-test and infrastructure-exclusion self-test still cover this unit. No code duplication; the guard was built to amortize. |
| The runtime typeof-string vocabulary (e.g. `"bytes"` vs `"bytesize"`) is one closed set, defined in one place | `IVMTyped.VMTypeName` implementations + the small set of primitive-tag literals inside `RuntimeOps.GetTypeName` and `Stringify` — together with the §Values & Types type table in the spec | **Detect + Conformance** — a `TypeModelConformanceTests` class enumerates every `typeof` form covered by the spec table and asserts each returns the expected string. A new participant added to the type system without a matching `typeof` clause + Conformance assertion is caught by Review-Priority-2 (Instruct). Pure Construct (e.g. an enum-typed `VMTypeName`) is the right long-run shape but is outside this unit's scope — file as a backlog optimization. |
| The cross-type `==` rule is honored uniformly by both the constant folder and the runtime path | `RuntimeOps.IsEqual` (runtime) + `Compiler.Expressions.TryFoldBinary` (compile-time) | **Conformance with a literal-vs-variable pair** — every cross-type `==` conformance test runs the assertion in BOTH forms (`1 == 1.0` for the folder path, `let i = 1; let f = 1.0; i == f` for the runtime path) and asserts the **same** boolean (both `true` per D2). A divergence between folder and runtime path — in either direction — is the bug that motivated this guard; the assertion catches it at phase verify, not in shipped code. The pair pattern is documented in the test class's XML doc. (Collapsing the two paths into one shared helper is a future Construct option — logged as a backlog optimization.) |
| The truthiness rule is honored uniformly across every truthiness-consuming construct | `RuntimeOps.IsFalsy` (the single source) used by every conditional opcode | **Construct (already present in code)** — there is exactly one `IsFalsy` function and every conditional opcode (`JmpFalse`, `JmpTrue`, `LogAnd`, `LogOr`, `Not`, ternary lowering, retry-predicate lowering) calls it. A new conditional construct that reimplements truthiness is a code-review reject. The Conformance tests assert the rule from each consumer surface (`if`, `while`, `do while`, `?:`, `&&`, `||`, `!`, `when`) to confirm the single-source rule is reached uniformly. |

## Acceptance Criteria

End-to-end behavior that proves the unit is done:

1. `Stash.Tests/Conformance/Values/` exists with at least the test classes listed
   in the phase table (Type model + typeof + range, Truthiness, Equality
   Numeric/Cross-Type/NaN, Equality Per-Category, Coercion, Secrets — 6 classes
   covering Edits 1–7).
2. Every Values conformance class is `[Trait("Category","Conformance")]`-marked
   and cites its spec clauses via name and XML doc.
3. `dotnet test --filter "Category=Conformance"` runs the full conformance
   surface (Async + Values), binds non-zero tests in each suite, and is green.
4. `Stash.Tests/Conformance/ConformanceTraitMetaTests.cs`'s
   `MinScannedParticipants` floor is raised to include the Values classes; the
   fail-path self-test and the infrastructure-exclusion self-test still pass.
5. `docs/Stash — Language Specification.md` §Values and Types contains Edits 1–7.
   Each is reachable by line-grep for a distinctive phrase named in the phase's
   `done_when`.
6. The verified contradiction `Truthiness_EmptyArrayIsTruthy` is resolved per
   **D1 ratification (law-side)**: the test reclassifies to `Category=Conformance`
   (moved to `Conformance/Values/TruthinessConformanceTests.cs` or trait-added
   in place) and remains green; the spec lists `[]`/`{}` as truthy.
7. The cross-type numeric `==` trilemma is resolved per **D2 ratification
   (coercion)**. Both forms of the literal-vs-variable pair return `true` for
   numeric pairs (`1 == 1.0`, `byte 0 == 0`, `byte 7 == 7.0`, `-0.0 == 0`); both
   forms return `false` for non-numeric cross-category pairs (`1 == true`,
   `0 == null`, `0 == ""`). The conformance test pair returns the **same**
   boolean per pair.
8. The typeof vocabulary contradiction `bytesize` vs `bytes` is resolved per
   **D3 ratification (code-side: keep `"bytes"`)**. `typeof(1KB)` returns
   `"bytes"`; the spec table row matches. No stdlib reference regeneration
   was required.
9. Secret equality is sealed per **D4 ratification (reference identity)**.
   The conformance test asserts `secret("x") == secret("x")` is `false` (two
   distinct constructions) AND `let t = secret("x"); t == t` is `true` (same
   handle). The class XML doc names the safety-by-default trade-off and points
   at `reveal()` + `crypto.constantTimeEquals` as the content-comparison
   pattern.
10. Caught-error truthiness is sealed per **D5 ratification (errors are
    truthy)**. `try { throw "x"; } catch (e) { if (e) { ... } }` enters the
    `if` body; `StashError.VMIsFalsy` returns `false`. The pre-change
    blast-radius audit found no in-tree caller relying on the old
    error-falsey behavior.
11. `coverage.md` row #2 reads `sealed`; cross-cutting workstream #3 is marked
    complete.
12. Full `dotnet test` is green; `dotnet test --filter "Category=Conformance"`
    is green and binds non-zero tests; `Wave1ThrowsCoverageTests`,
    `Wave2ThrowsCoverageTests`, `CompletionSurfaceSnapshotTests`, and
    `StandardLibraryReferenceTests` are all green.
13. `stash scripts/checkpoint/checkpoint.stash validate-spec language-standard-values`
    passes.

Error behavior that proves the failure paths work:

- The cross-type-`==` regression guard is the literal-vs-variable pair test.
  Today both literal and variable forms must return the **same** boolean per
  numeric pair; a future divergence — in either direction — flips one form
  and trips the pair assertion.
- The trait guard's `MinScannedParticipants` floor catches an accidental delete
  of any Values conformance class.
- The empty-array truthiness fix's regression guard is the conformance test
  itself — flipping the impl back violates the spec clause directly.

Cross-entrypoint behavior:

- §Values & Types prose applies uniformly across `stash`, the LSP runtime, the
  embedded host (`Stash.Hosting`), and the DAP. Conformance tests run inside the
  test VM, which is the same `Stash.Bytecode` runtime as the other entrypoints,
  so the proof is uniform. No CLI-only or LSP-only conformance tests are needed.

## Phases

The phase list lives in `plan.yaml`. Each phase has a concrete `done_when` and
verify commands. Each phase that introduces a new normative spec clause also
lands the matching conformance test in the same phase — the architect doctrine
forbids a phase shipping a test for a clause that does not yet exist in the
spec, and vice versa.

| Phase | Spec edits landed | Code change | Conformance tests landed |
| --- | --- | --- | --- |
| P1 — Type model + typeof + range | **Edits 1 + 2 + 7** + **D3 ratified** | D3 spec-side only — no code change | `TypeModelConformanceTests` (covers `typeof`, `nameof`, the closed type table, and §Ranges) |
| P2 — Truthiness | **Edit 3** + **D1 + D5 ratified** | D5 code change: flip `StashError.VMIsFalsy` from `true` to `false`; reclassify the legacy `Truthiness_EmptyArrayIsTruthy` test to `Category=Conformance` (D1 law-side, no code change) | `TruthinessConformanceTests` (covers full closed falsey set + caught-error truthy + empty-collection truthy) |
| P3 — Equality (numeric / cross-type / NaN) | **Edit 4** (numeric coercion + non-numeric cross-category) + **D2 ratified** | D2 code change: extend `RuntimeOps.IsEqual`/`IsEqualSlow` to coerce cross-tag `int`/`float`/`byte` operands by mathematical value for `==`/`!=` (matching the folder). Folder untouched. | `EqualityNumericConformanceTests` (literal-vs-variable pair pattern — both forms now return the **same** boolean) |
| P4 — Equality (per-category, identity vs by-value) | **Edit 4** (per-category table) | — | `EqualityPerCategoryConformanceTests` |
| P5 — Type coercion | **Edit 5** | — | `CoercionConformanceTests` |
| P6 — Secret values | **Edit 6** + **D4 ratified** | D4 code change: `StashSecret.Equals` → reference identity; `GetHashCode` → identity hash | `SecretConformanceTests` (asserts distinct constructions are not equal; aliased handles are equal; dict-key identity consequence) |
| P7 — Closing — no-op confirmation + update coverage.md | — | `dotnet run --project Stash.Docs/` runs as a confirmation no-op (no metadata edits expected in this unit) | — (no new participant; final gate) |

## Open Questions

- **The `StashLiteralArg.VMIsFalsy` rule** (an internal-only `IVMTruthiness`
  implementation). Is `StashLiteralArg` ever reachable from a Stash user script
  as a value? If yes, its truthiness rule must be spec'd. If no (the suspected
  case — it appears to be an internal command-lowering shim), document the
  internal-only invariant in code comment but leave it out of the spec.
  **Investigation in P2.**
- ~~**`StashError.VMIsFalsy => true`**~~ **RESOLVED** by D5 ratification —
  errors are truthy; code change in P2 flips the flag.
- ~~**`-0.0 == 0` (mixed-tag)**~~ **RESOLVED** by D2 ratification — under the
  numeric-coercion rule, `-0.0 == 0` is `true`. Asserted in
  `EqualityNumericConformanceTests` (P3).
- **Will the Values seal pass surface a hidden contradiction that motivates a
  D6?** The audit's contradictions are addressed by Edits 1–7 + D1–D5. If a
  P-phase discovers a new one, file it as a Decision Log addition in the same
  phase and proceed seal-first-then-bend.

## Decision Log

| Date | Decision | Rationale |
| --- | --- | --- |
| 2026-06-06 | **Unit scope: the whole §Values & Types section (L570–L664).** Includes type model, `typeof`/`nameof`, truthiness, equality, type coercion, secret values, AND a new `Ranges` subsection. Does NOT include §Bindings, §Expressions, §Aggregate, or the cross-cutting error-type taxonomy. | Natural section boundary in the spec; the substrate cross-cutting workstream #3 in `coverage.md`. Smaller slicing leaves the equality vs truthiness vs coercion threesome scattered across multiple units, which is exactly the seam this milestone exists to close. |
| 2026-06-06 | **Reuse the unit-#1 trait guard unchanged.** Just bump `MinScannedParticipants` per phase. | The guard was built to amortize across 13 units. Adding `Values/` directory triggers the participant scanner automatically because the namespace prefix matches. |
| 2026-06-06 | **D1 (empty-array/dict truthiness fork) — RATIFIED 2026-06-06: correct the law (Ruby/JS/Lua-camp).** Empty arrays and empty dicts become **truthy** in the spec; the impl stays as-is. `Truthiness_EmptyArrayIsTruthy` reclassifies to `Category=Conformance`. (Recommendation accepted — code is correct, spec drifted.) | Empirical: the impl has shipped with empty-collection-truthy since at least the §Async dimension suite era, and downstream code implicitly relies on it. Doctrinally: the JS/Ruby/Lua camp distinguishes "the value `null` / `false` / `0` / `""`" from "an empty container", which matches the impl. Practically: `arr.isEmpty(v)` / `len(v) == 0` is the explicit way to test emptiness. **Make-It-Right: correcting the law is the right move because the code is correct; the spec was written without an empirical check.** |
| 2026-06-06 | **D2 (cross-type numeric `==` trilemma) — RATIFIED 2026-06-06: NUMERIC COERCION (user OVERRODE the recommendation).** Numeric categories (`int`, `float`, `byte`) coerce by mathematical value for `==`/`!=`. `1 == 1.0` is `true` consistently (folder and runtime agree). Non-numeric cross-category pairs (`bool`, `null`, `string`, …) remain unequal. The **constant folder is correct and stays untouched**; the **runtime path** (`RuntimeOps.IsEqual`/`IsEqualSlow`) is the bug and is fixed to coerce. The byte tag-strict miss (`byte 0 == 0` → `false` today) is also fixed by this change. **User one-line reason: mainstream `1 == 1.0` is `true`.** | The architect's original recommendation was "runtime wins, fix the folder" on doctrinal grounds (existing spec L645 + Stash's no-cross-coercion patterns for `bool != int`, `null != 0`). The user chose the mainstream semantics (JS, Python, Lua, Ruby, C — all coerce int↔float for `==`) on usability grounds: a user writing `if (count == 0)` should not have their behavior depend on whether `count` is `int` or `float`. The override is recorded; the architect's doctrinal point is preserved by the non-numeric clause (no `bool`/`null`/`string` coercion). |
| 2026-06-06 | **D3 (typeof vocabulary `bytesize` vs `bytes`) — RATIFIED 2026-06-06: keep `bytes`, change the spec.** (Recommendation accepted — code-side.) `typeof(1KB)` returns `"bytes"`; spec table row corrected from `bytesize` to `bytes`. No code change, no stdlib reference regen. | Empirical: `typeof(1KB) == "bytes"` (confirmed). `"bytes"` is the shipped string in every observable surface. **Make-It-Right: the code is the law-in-practice; the spec drifted at write time.** |
| 2026-06-06 | **D4 (secret equality fork) — RATIFIED 2026-06-06: REFERENCE IDENTITY (user OVERRODE the recommendation; safety-by-default).** `secret(a) == secret(b)` is `false` unless both operands are the same handle. To compare contents, `reveal()` first; for security-sensitive comparison use `crypto.constantTimeEquals` on the revealed bytes. `StashSecret.Equals` changes to reference identity; the `IEquatable<StashSecret>` contract narrows. Consequence: a secret used as a dict key keys by identity. **User one-line reason: safety-by-default outweighs usability for the secret type.** | The architect's original recommendation was "keep by-inner-value, document the information-leak window" on usability grounds (dedup, dict-key-by-content) with a pointer to `crypto.constantTimeEquals`. The user chose safety: a secret type should not leak its contents through `==`, period. The override is recorded; the spec edit removes the "ratified information-leak window" framing entirely and states the safety reasoning instead. |
| 2026-06-06 | **D5 (caught-error truthiness) — RATIFIED 2026-06-06: errors are truthy (fix the code).** `StashError.VMIsFalsy` flips from `true` to `false`. `try { throw "x"; } catch (e) { if (e) { ... } }` enters the `if` body. (Originally framed as a P2-investigation Open Question; now an explicit ratified decision.) | Empirical: today `try{throw "x"}catch(e){if(e){…}}` never enters the `if`, and `typeof(e)=="Error"` for both thrown strings and thrown error types. The current code makes errors falsey, which contradicts every mainstream language (Python, JS, Ruby, C# — all treat caught errors as truthy values). A caught error is a real value, not an absence-marker. P2 includes a blast-radius audit before the flip; no in-tree caller is expected to rely on the old falsey behavior. |
| 2026-06-06 | **Seal-first-then-bend: ratifications land in their phase, not at the end.** D1 + D5 land in P2; D2 in P3; D3 in P1; D4 in P6. Each phase commits the spec edit + code change + conformance test as one atomic unit. | The §Async unit's late-D5 ratification taught us that ratifications at the end (after the seal) re-open the spec for re-edits. Ratification-then-seal in one phase keeps the spec and code aligned at every checkpoint. |
| 2026-06-06 | **Conformance test naming: `_PerSpecValues<Subarea>` suffix** (e.g. `_PerSpecValuesTruthiness`, `_PerSpecValuesEqualityNumeric`, `_PerSpecValuesEqualityPerCategory`, `_PerSpecValuesCoercion`, `_PerSpecValuesSecret`, `_PerSpecValuesTypeModel`, `_PerSpecValuesRange`). | Matches the unit-#1 pattern (`_PerSpecAsyncD6`, etc.). The subarea names mirror the spec subsection headings, not the runtime impl details. |
| 2026-06-06 | **The literal-vs-variable pair pattern is the documented `EqualityNumericConformanceTests` shape.** Every cross-type `==` clause is asserted twice — once with two literal operands (folder path) and once with two `let`-bound variables (runtime path) — and the test asserts they produce the same boolean. | The const-folder/runtime trilemma was a real silent defect for the lifetime of the language. Future similar defects (a folder that diverges from the runtime path) get caught by the same pattern. Documented in the test class XML doc so future units inherit the precedent. |
| 2026-06-06 | **NaN reachability is via overflow arithmetic, not a stdlib constant.** Spec the reachability path explicitly (`conv.toFloat("1e308") * 10.0` produces `Infinity`; `Infinity - Infinity` produces `NaN`). | Probed: `math.nan` / `math.inf` do not exist; `0.0 / 0.0` raises `RuntimeError("Division by zero.")` rather than returning `NaN`. The only reachable NaN is the overflow path. Without spec'ing it, conformance tests would be authoring undocumented incantations. |
| 2026-06-06 | **No `examples/` script changes.** | No new user-facing behavior ships. `.claude/language-changes.md` requires an example for *new* functionality; the §Values seal is documentation-of-existing + small code corrections. |
| 2026-06-06 | **`final_verify` runs `Category=Conformance` as a discrete step in addition to full `dotnet test`.** | Matches the unit-#1 vacuous-pass guard. If the filter ever binds zero conformance tests, the discrete step fails loud rather than passing silently inside the full suite's green sea. |
| 2026-06-06 | **Coverage.md row #2 flips `unsealed → sealed` at P7.** Workstream #3 in the cross-cutting list is marked complete at P7. | Matches the unit-#1 closing-phase convention. |
