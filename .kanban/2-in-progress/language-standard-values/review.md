# language-standard-values — Review

> Produced by `/feature-review`. One finding per H2 section.
> Each finding header is parseable: `## Fxx — [SEVERITY] short title`.
> `/resolve language-standard-values Fxx [Fyy...]` selects one or more findings.

**Scope reviewed:** commits `f4a518e8..e9356bb3` on branch `feature/language-standard-values`
**Brief:** ../brief.md
**Generated:** 2026-06-07 01:13

## Summary verdict

**The seal is sound.** All five ratified rulings (D1 truthy-empty-collection, D2 numeric `==` coercion fix, D3 `bytesize`→`bytes`, D4 secret reference identity — user override, D5 caught-error truthy) are implemented correctly in code, asserted by `Category=Conformance` tests under `Stash.Tests/Conformance/Values/`, and stated in `docs/Stash — Language Specification.md` §Values and Types. The two USER OVERRIDES (D2, D4) ratified directions match in spec ↔ code ↔ tests. The brief's distinctive phrases land at the named locations. 159 Conformance/Values tests pass; the baseline registry-metrics failure is a known flake on a disjoint subsystem.

What the findings below capture is **seal-completeness drift** of three kinds, none CRITICAL: (1) the spec normatively recommends a non-existent API (`crypto.constantTimeEquals`); (2) the §Equality "single equivalence class" claim is silently contradicted by `in`/dict-key/`arr.contains` (the divergence noted in the architect's MUST-2 prompt — confirmed observable and not addressed in the sealed prose); (3) coverage gaps where the spec table claims completeness but tests omit `ip`/`interface`/`enum_type`/`struct_type`/`nameof(enum)` and the prose overstates reachable typed-array surface (`int[]`/`float[]`/`string[]` examples that no script can actually construct).

The single MUST-evaluate item I'm ruling **acceptable with a one-line cleanup** (F05) is the §Secret idempotent-wrap / dict-key prose: the "equivalent to" phrasing is fine when paired with the immediately-following reference-identity clause (the test correctly pins non-nesting via `reveal`, not via `==`), and the "where permitted as a dict key" hypothetical sits next to an explicit prohibition so it's harmless.

**By severity:** 0 CRITICAL, 2 HIGH (F01 dangling-API recommendation, F02 `in`/dict-key equality divergence as unsealed negative space), 2 MEDIUM (F03 typeof coverage gap on closed-set members, F04 typed-array spec example overstates reachable surface), 1 LOW (F05 secret prose tightening).

The seal can be declared sound and promoted on resolution of F01 + F02 at minimum; F03/F04 are real Priority-2 gaps a milestone-disciplined `/done` would expect closed; F05 is editorial.

---

## F01 — [HIGH] Spec normatively recommends `crypto.constantTimeEquals` — the function does not exist

**Status:** fixed
**Fixed in:** 04701792
**Files:** `docs/Stash — Language Specification.md:711-713`, `docs/Stash — Language Specification.md:799-801`
**Phase:** P4, P6
**Commit:** 69f93ebb, c32fcfec

### Observation

§Equality (L711-L713) and §Secret Values (L799-L801) both normatively recommend `crypto.constantTimeEquals(reveal(a), reveal(b))` as the way to compare secret contents safely. The function does not exist:

```
$ stash -c 'io.println(crypto.constantTimeEquals(buf.from("a"), buf.from("a")));'
RuntimeError: Namespace 'crypto' has no member 'constantTimeEquals'.
  at <main> (<command>:1:53)
```

`Stash.Stdlib/BuiltIns/CryptoBuiltIns.cs` contains no constant-time comparator. The function the spec is reaching for lives in **`buf`**: `Stash.Stdlib/BuiltIns/BufBuiltIns.cs:249` `buf.equals(a, b)` calls `System.Security.Cryptography.CryptographicOperations.FixedTimeEquals` and is the actual constant-time comparator. Its signature requires `byte[]` operands.

### Why this matters

The spec is the law (`AGENTS.md` → *The Specification is the Law*). A normative recommendation that resolves to a `RuntimeError` is a defect the same kind as a doc-comment example that doesn't compile — it ships in two places, in the most security-sensitive paragraph of the entire §Values & Types seal (the secret-equality safety-by-default rationale that justified the D4 override), and a user following the spec verbatim hits an unrecoverable error. Worse, the false function name is presented as the *better* alternative to `==`, so a security-conscious reader is **specifically steered into the broken path**.

The brief's *Goals* (line 142) and Edit 4 / Edit 6 prose both call out this API; the design appears to have assumed an unbuilt namespace move (`buf.equals → crypto.constantTimeEquals`) without verifying it exists.

### Suggested fix

Replace both occurrences with the existing API. The two paragraphs need:

- §Equality L711-L713: `... use \`crypto.constantTimeEquals\` on the revealed values ...` → `... use \`buf.equals(buf.from(reveal(a)), buf.from(reveal(b)))\` on the revealed values (a constant-time comparator backed by \`CryptographicOperations.FixedTimeEquals\`) ...` (or just `buf.equals` if both operands are already `byte[]`).
- §Secret Values L799-L801: same substitution; preserve the "safety-by-default" / "== cannot be used as an oracle" rationale verbatim.

Also update the class-level XML doc in `Stash.Tests/Conformance/Values/SecretConformanceTests.cs` (lines 12-14, 200) which echoes the same wrong API name.

Optional secondary: if the user actually wants `crypto.constantTimeEquals` to exist (it is a reasonable namespace for it), the alternative is to **add** the function to `CryptoBuiltIns.cs` as a thin wrapper, then the spec prose stays as-is and a new conformance test pins the call shape. That is out-of-scope for this seal but lower friction for spec stability.

### Verify

```
grep -n 'crypto.constantTimeEquals' "docs/Stash — Language Specification.md"   # must return nothing, OR every hit must resolve at runtime
dotnet run --project Stash.Cli/ -- -c 'io.println(buf.equals(buf.from("a"), buf.from("a")));'   # true
dotnet test --filter "Category=Conformance"
```

---

## F02 — [HIGH] §Equality "single equivalence class" claim silently contradicted by `in`/dict-key/`arr.contains` — unsealed negative space

**Status:** open
**Files:** `docs/Stash — Language Specification.md:677-725`, `Stash.Core/Runtime/StashValue.cs:145-167`, `Stash.Bytecode/Runtime/RuntimeOps.cs:49-76`
**Phase:** P3, P4
**Commit:** f03c186a, 69f93ebb

### Observation

D2 fixed `RuntimeOps.IsEqual` so cross-tag numeric pairs (int/float/byte) compare by mathematical value. The §Equality seal at L683-L689 elevates this to a normative claim: *"The three numeric categories — `int`, `float`, `byte` — form a **single equivalence class for `==` and `!=`**. Operands from any two of these categories are compared by **mathematical value**"*, and the section opens with *"Equality is **total**: it returns a boolean for any two operands and never raises."*

However, `StashValue.Equals(StashValue)` (`Stash.Core/Runtime/StashValue.cs:145`) — the equality path used by **`in`**, **dict-key lookup**, and **`arr.contains`** — is unchanged and tag-strict:

```csharp
public bool Equals(StashValue other)
{
    if (Tag != other.Tag) return false;       // ← cross-tag short-circuit, no D2 coercion
    return Tag switch { ... };
}
```

This produces an observable language-level contradiction with the sealed §Equality clause:

```
$ stash -c 'io.println(1 == 1.0);'                                  # true   (D2 honored — RuntimeOps.IsEqual)
$ stash -c 'io.println(1 in [1.0]);'                                # false  (D2 NOT honored — StashValue.Equals)
$ stash -c 'let d = {}; d[1] = "a"; io.println(d[1.0]);'            # null   (D2 NOT honored — dict hash + StashValue.Equals)
$ stash -c 'io.println(arr.contains([1], 1.0));'                    # false  (D2 NOT honored)
```

A reader who reads "single equivalence class for `==` and `!=`" then sees `1 in [1.0]` is `false` learns by experiment that the spec means *literally only `==`/`!=`* — but the spec does not say that. The negative space is silent.

The brief flags this exact divergence (Architect MUST-2 prompt). The implementer filed `.kanban/0-backlog/bugs/numeric-equality-inconsistency-in-operator-dict-keys.md` with three remediation options (A: fix `Equals`+`GetHashCode`, B: helper for `Contains` only, C: pin tag-strict in spec). The bug is logged; the **seal is not informed**. The sealed §Equality prose claims an equivalence class without naming where the equivalence stops — that is the textbook negative-space gap the milestone Unit-DoD exists to close.

This is not a code regression introduced by this unit — both paths existed before. What this unit changed is the *observability* of the inconsistency: pre-D2 the language was uniformly tag-strict; post-D2 `==` coerces and the consumers of `Equals` do not. The seal needs a normative decision here that the user did not make when ratifying D2.

### Why this matters

The seal-first doctrine ("can a reader of the sealed §Values & Types *predict* the observable behavior AND its negative space") fails here: a reader cannot predict `1 in [1.0]` from the §Equality prose. The "equivalence class" framing is the user's preferred mainstream answer (their one-line reason in the D2 ratification was "mainstream `1 == 1.0` is `true`"), but mainstream languages where `1 == 1.0` is `true` (JS, Python, Lua) **also** have `1 in [1.0]` be `true` (or `1.0 in [1]`, by analogous numeric membership). Stash uniquely splits the rule. That split is normative content that belongs in the spec, not buried in a backlog stub.

Per the coverage map's own discriminator (a *false clause* is "unsealed", *missing negative space* is "partial"), the current §Equality seal sits in "partial-with-prose" territory while being flagged "sealed" — exactly the drift the milestone is designed to prevent. Either the spec gets a carve-out, or the runtime `StashValue.Equals` gets fixed; either way the seal is *not* the gap.

### Suggested fix

**Two acceptable resolutions; user must pick.** This finding is asking for a *decision*, not a single fix:

**Option C (recommended for this seal — narrowest blast radius):** add a one-sentence carve-out to §Equality (immediately after the per-category table at L703, or as a new bolded paragraph after L713):

> **Membership uses tag-strict equality.** The `in` operator (array element membership, dict key lookup), `arr.contains`, and dict-key hash lookup use **tag-strict structural equality**, NOT the numeric-coercion rule above. `1 in [1.0]` is `false`; a dict populated with integer key `1` does not match a lookup with float key `1.0`. This is observable and deliberate — the dict hash contract requires `a == b → same hash`, which the numeric coercion cannot satisfy without changing the hash function for every numeric key. To test mathematical membership, normalize first: `arr.contains(arr.map(xs, x => conv.toFloat(x)), 1.0)`. See `.kanban/0-backlog/bugs/numeric-equality-inconsistency-in-operator-dict-keys.md` for the design history.

Cross-reference §Expressions → Membership (L1283-L1295) to add the matching forward pointer. Land a new conformance test class (`EqualityMembershipConformanceTests` or extend `EqualityNumericConformanceTests`) that pins `1 in [1.0]` is `false`, `1.0 in [1]` is `false`, `d[1] != d[1.0]`, and `arr.contains([1], 1.0)` is `false`, with the assertion message "`in`/dict-key tag-strict (sealed in §Equality)" so the regression guard is obvious.

**Option A** (fix `StashValue.Equals` + `GetHashCode` for numeric coercion) is the user-friendlier mainstream answer but is §Aggregate territory per the brief's Non-Goals and requires a careful dict-subsystem audit. If the user prefers this, defer to a future unit (§Aggregate / §Expressions) and adopt Option C *as the interim seal* so the spec is not lying to the reader during the interregnum.

### Verify

```
grep -n 'Membership uses tag-strict' "docs/Stash — Language Specification.md"   # must find the new clause
dotnet test --filter "FullyQualifiedName~EqualityNumericConformanceTests"
dotnet test --filter "Category=Conformance"
# Manual probe (sealed-behavior regression guard):
dotnet run --project Stash.Cli/ -- -c 'io.println(1 == 1.0); io.println(1 in [1.0]);'
# Expected: true, false  — with the carve-out, the spec now PREDICTS both.
```

---

## F03 — [MEDIUM] TypeModel conformance class misses `typeof(ip)`, `typeof(interface)`, `typeof(struct_type)`, `typeof(enum_type)`, `nameof(enum_value)` — closed-set claim under-tested

**Status:** open
**Files:** `Stash.Tests/Conformance/Values/TypeModelConformanceTests.cs`, `docs/Stash — Language Specification.md:582-609`, `docs/Stash — Language Specification.md:620-648`
**Phase:** P1
**Commit:** c292e810

### Observation

`TypeModelConformanceTests` documents itself as the *vocabulary registry test*: "every distinct typeof string the spec table names is asserted here. A new runtime category without a matching clause in the spec table is caught by Review Priority 2." (Class XML doc L34-L38.)

Spec table at L582-L605 lists **20** type-name rows. The conformance class asserts `typeof` for `int`, `float`, `bool`, `null`, `string`, `array`, `dict`, `range`, `duration`, `bytes`, `namespace`, `function`, `byte`, `semver`, `secret`, `struct`, `enum`, `Future`, `Error`, `byte[]` — **missing assertions for** `ip` and `interface`. Both return correctly (verified manually):

```
$ stash -c 'io.println(typeof(@127.0.0.1));'       # ip
$ stash -c 'interface Runnable { fn run() } io.println(typeof(Runnable));'   # interface
```

Spec Edit 2 prose (L634-L636) also makes a normative claim that *`typeof(struct_type)` / `typeof(enum_type)` (the type identifier) returns `"struct"` / `"enum"` respectively; the type and its instance are indistinguishable by typeof*. The class tests `typeof(struct_instance)` and `typeof(enum_value)` but NOT the type-identifier cases. Manually verified `typeof(P)` returns `"struct"` for `struct P { x }`, so the law is honored — but un-asserted.

Spec L632-L633 says `nameof(enum_value)` returns "the declared enum-type name". `Nameof_StructInstance_ReturnsStructName` covers structs but no equivalent for enums.

### Why this matters

The class's own self-description states the discipline ("vocabulary registry test"). Two rows of a 20-row closed table are unobserved, two prose-named identifier cases (`typeof(struct_type)`, `typeof(enum_type)`) are unobserved, and a paired `nameof` is missing. The closed-set sentence in the spec ("the complete closed set returned by typeof") is the strongest *Construct-shaped* commitment in this seal — its conformance proof must enumerate every member or the closure claim is on the honor system.

The omission also defeats the unit's intended "Detect with teeth" mechanism: per the brief's *Cross-Cutting Concerns* table (line 546), "A new participant added to the type system without a matching `typeof` clause + Conformance assertion is caught by Review-Priority-2 (Instruct)." For a future `ip`/`interface`/typed-array change to be caught, the baseline must enumerate them today.

### Suggested fix

Add five tests to `Stash.Tests/Conformance/Values/TypeModelConformanceTests.cs`:

```csharp
[Fact] public void TypeOf_IpLiteral_ReturnsIp_PerSpecValuesTypeModel()
    => Assert.Equal("ip", Run("let result = typeof(@127.0.0.1);"));

[Fact] public void TypeOf_InterfaceType_ReturnsInterface_PerSpecValuesTypeModel()
    => Assert.Equal("interface",
        Run("interface Runnable { fn run() } let result = typeof(Runnable);"));

[Fact] public void TypeOf_StructTypeIdentifier_ReturnsStruct_PerSpecValuesTypeModel()
    => Assert.Equal("struct",
        Run("struct P { x } let result = typeof(P);"));

[Fact] public void TypeOf_EnumTypeIdentifier_ReturnsEnum_PerSpecValuesTypeModel()
    => Assert.Equal("enum",
        Run("enum Color { Red } let result = typeof(Color);"));

[Fact] public void Nameof_EnumValue_ReturnsEnumTypeName_PerSpecValuesTypeModel()
    => Assert.Equal("Color",
        Run("enum Color { Red } let result = nameof(Color.Red);"));
```

No spec edit needed — every claim is already stated; this just closes the proof loop. No `MinScannedParticipants` change (still one class).

### Verify

```
dotnet test --filter "FullyQualifiedName~TypeModelConformanceTests"
# Expect: 5 new tests, all green.
dotnet test --filter "Category=Conformance"
```

---

## F04 — [MEDIUM] Spec Edit 2 lists `int[]`/`float[]`/`string[]` as typed-array examples — only `byte[]` is reachable from a Stash script

**Status:** fixed
**Fixed in:** 6597c945
**Files:** `docs/Stash — Language Specification.md:642-643`, `Stash.Core/Runtime/Types/StashByteArray.cs`
**Phase:** P1
**Commit:** c292e810

### Observation

Spec Edit 2 at L642-L643 states:

> `typeof(typed_array)` returns the element-type string suffixed with `[]` (e.g. `"byte[]"`, `"int[]"`, `"float[]"`, `"string[]"`).

The example list implies four reachable typed-array categories. In practice, the runtime ships exactly one (`StashByteArray`), and a type-annotated `int[]` / `float[]` / `string[]` literal evaluates to a plain `array`:

```
$ stash -c 'let xs: int[] = [1,2,3]; io.println(typeof(xs));'        # array  (NOT "int[]")
$ stash -c 'let s: string[] = ["a"]; io.println(typeof(s));'         # array  (NOT "string[]")
$ stash -c 'io.println(typeof(buf.from("Hello")));'                  # byte[]  (the only reachable typed array)
```

Type annotations are erased at compile time per §Type Hints (L838+, also reaffirmed at L575-L577 of the sealed type-model intro). There is no runtime construction path that yields an `int[]`/`float[]`/`string[]` typed-array category — those typeof strings can never be observed from a Stash script.

`TypeModelConformanceTests` honors this — only `byte[]` (via `buf.from`) is tested. But that asymmetry between the spec example list and the test surface is itself the signal: the example list overstates the closed set.

### Why this matters

The sealed prose is the law a tooling author or library consumer will read. Distributing example `typeof` strings that no script can ever produce is a quiet drift from the substrate's own discriminator ("can a reader of the spec *predict* the observable behavior"). A library author writing `if (typeof(x) == "int[]")` will write dead code. A future addition of `int[]`/`float[]`/`string[]` as real typed-array categories is a real language change — having the spec already imply they exist makes it impossible to tell the planned-vs-shipped state from the prose.

This is the same shape of overstatement the omission-hardening doctrine watches for: the spec asserts a closed set, but one row's contents are aspirational.

### Suggested fix

Two minimal edits in §Values and Types:

1. **L642-L643** — replace the four-example parenthetical with the reality:

   > `typeof(typed_array)` returns the element-type string suffixed with `[]`. The only reachable typed array in the current runtime is `byte[]` (constructed via `buf.from(...)`, `buf.alloc(...)`, or a `process.read*` byte buffer). Adding a new typed-array category (e.g. `int[]`, `float[]`, `string[]`) is a breaking change to §Values and Types per the closed-set sentence at the end of the type table.

2. **L591** (type table row "typed array") — narrow the description from `primitive homogeneous array such as \`byte[]\`` to `primitive homogeneous byte array (\`byte[]\`)` to match. (If the user *intends* to land `int[]`/`float[]`/`string[]` as a future unit, leave row L591 as-is and instead reword L642-L643 to say "the only example currently reachable in the runtime is `byte[]`; the language reserves the suffix form for future typed-array categories.")

### Verify

```
grep -n 'int\[\]\|float\[\]\|string\[\]' "docs/Stash — Language Specification.md" | grep -v 'docs/'   # only intended references remain
# Manual probe:
dotnet run --project Stash.Cli/ -- -c 'io.println(typeof(buf.from("x")));'   # byte[]
dotnet run --project Stash.Cli/ -- -c 'let xs: int[] = [1]; io.println(typeof(xs));'   # array (per the corrected spec)
```

---

## F05 — [LOW] §Secret Values prose: "Idempotent wrap" + dict-key hypothetical can be tightened

**Status:** fixed
**Fixed in:** 04701792
**Files:** `docs/Stash — Language Specification.md:772-779`, `docs/Stash — Language Specification.md:803-807`
**Phase:** P6
**Commit:** c32fcfec

### Observation (architect MUST-evaluate item #1)

Two prose-quality calls the architect asked me to rule on, judged together:

**A. "Idempotent wrap" (L772-779).** The clause says *`secret(secret(x))` is **equivalent to** `secret(x)`*. Immediately below is the *Equality — reference identity* clause: two distinct constructions are NOT `==`. Under D4, `let inner = secret("x"); let outer = secret(inner); outer == inner` is `false` (verified by `SecretConformanceTests.IdempotentWrap_OuterAndInner_AreDistinctHandles_PerSpecValuesSecret`). A reader who takes "equivalent" to imply `==` will be wrong.

**B. "Where permitted as a dict key" hypothetical (L803-807).** The clause says *a `secret` value, **where permitted as a dict key**, keys by identity*. The very next sentence says the current implementation **prohibits** secrets as dict keys (`RuntimeError`). The hypothetical is harmless (no observable behavior contradicts it — you can't get a secret into a dict to see it), but it documents an impossible code path.

### Why this matters

Ruling: **acceptable, with a one-paragraph tightening.** Neither is a sealed-prose defect — the conformance tests pin the right invariants — but both add reader-load that the surrounding D4 safety-by-default rationale doesn't need. The MUST-evaluate prompt explicitly defers to the reviewer's prose-quality call; my call is that the two prose nits are LOW-priority cleanup, not blockers.

### Suggested fix

Minimal one-line edits, no code touch:

- **L772**: change *`secret(secret(x))` is **equivalent to** `secret(x)`* → *`secret(secret(x))` wraps `x` with no inner-secret nesting; `reveal(secret(secret(x))) == reveal(secret(x))`. The outer and inner handles are distinct objects (per Equality below), so `outer == inner` is `false`.*

- **L803-807** prose: collapse the hypothetical-then-prohibition into one prohibition sentence:
  *A `secret` value cannot be used as a dict key. The current implementation restricts dict keys to `string`, `int`, `float`, and `bool`; passing a `secret` raises a `RuntimeError`. (If a future runtime permits secret dict-keys, they will key by reference identity per the equality rule above.)*

The existing conformance tests cover both behaviors unchanged.

### Verify

```
grep -n 'Idempotent wrap' "docs/Stash — Language Specification.md"   # still present, prose updated
dotnet test --filter "FullyQualifiedName~SecretConformanceTests"
```
