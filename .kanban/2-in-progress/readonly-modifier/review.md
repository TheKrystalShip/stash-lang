# readonly-modifier — Review

> Produced by `/feature-review`. One finding per H2 section.
> Each finding header MUST be parseable: `## Fxx — [SEVERITY] short title`.
> `/resolve <feature> Fxx [Fyy...]` reads the selected section(s) and dispatches a Resolver.

**Scope reviewed:** commits `6c84c290..62fa7b04` on branch `feature/readonly-modifier`
**Brief:** ./brief.md
**Generated:** 2026-06-01 16:48 UTC

**Summary of findings:** 7 open — 3 CRITICAL, 1 IMPORTANT, 3 MINOR.

`final_verify` is currently **red** (CRITICAL F01 fails the `dotnet test` step before
the docs-regen step runs). Promotion will refuse until F01 is fixed.

The lifecycle achievements — soft-keyword + parser + 3-way dispatch + for-init rejection +
`export readonly const` wiring (P1), all six visitors + formatter + symbol metadata + LSP hover
(P2/P6), `StashArray` always-present carrier + cycle-safe `DeepFreeze` + `StashDictionary` /
`StashInstance` write-guards + per-value-kind `ReadOnlyError` (P3), `Freeze` opcode + init-and-rebind
emission + upvalue-descriptor mutability + closure-rebind enforcement that incidentally closes the
const-through-closure hole (P4), SA0847 with parity to runtime mutators (P5), spec section + ToC +
example exercising 7 scenarios + tree-sitter rule + TextMate `storage.modifier.stash` + Monarch
(P6) — are sound and well-tested. The findings below identify completeness gaps in the deep-freeze
guarantee, not architectural problems with the design.

---

## F01 — [CRITICAL] Bytecode instruction reference doc is stale; final_verify fails on `dotnet test`

**Status:** fixed
**Fixed in:** b8c6242
**Files:** `docs/Bytecode VM — Instruction Set Reference.md:17`, `Stash.Bytecode/Bytecode/OpCode.cs:451`
**Phase:** P4
**Commit:** 4df52173

### Observation

P4 added `Freeze = 101` to `OpCode`. The checked-in `docs/Bytecode VM — Instruction Set Reference.md`
still says `| Opcode count | 100 |` and has no `Freeze` entry. The xUnit test
`Stash.Tests.Bytecode.BytecodeInstructionReferenceTests.GeneratedInstructionReference_MatchesCheckedInDoc`
compares the generator output to the file on disk and currently fails with
"Opcode count on disk `100`, expected `101`. Regenerate with: `dotnet run --project Stash.Docs/ --bytecode`."

This test is **not** in `final_verify`'s exclusion filter (correctly — it guards
the language-changes checklist). The feature's `final_verify` runs:

```
1. dotnet build
2. dotnet test --filter "<exclusions>"     ← fails here on this test
3. dotnet run --project Stash.Docs/        ← would regenerate, but step 2 already aborted
4. dotnet run --project Stash.Cli/ -- examples/readonly.stash
5. stash scripts/checkpoint/validate-spec.stash
```

`dotnet test` runs **before** the docs-regen step, so the stale doc on disk blocks the gate.
`promote-done.stash` will refuse.

### Why this matters

`/done` is currently impossible. This is a mandatory step of the
`.claude/language-changes.md` checklist that was missed in P4 ("After updating
metadata, regenerate and commit"). It cannot be deferred and is not environmental.

### Suggested fix

Run `dotnet run --project Stash.Docs/` from the worktree root and commit the regenerated
`docs/Bytecode VM — Instruction Set Reference.md` (and `docs/Stash — Standard Library Reference.md`
if it also drifted). Verify the test passes locally before re-running `final_verify`.

### Verify

```
dotnet run --project Stash.Docs/
git diff -- "docs/Bytecode VM — Instruction Set Reference.md"   # expect: "Opcode count | 101" + new Freeze row
dotnet test --filter "FullyQualifiedName~BytecodeInstructionReferenceTests"   # expect: PASS
```

---

## F02 — [CRITICAL] Deep-freeze is non-transitive through any stdlib-produced array (every `arr.*` / `dict.keys/values/pairs` etc.)

**Status:** fixed
**Fixed in:** 45a99c0
**Files:** `Stash.Core/Runtime/RuntimeValues.cs:316-376`, `Stash.Stdlib/BuiltIns/ArrBuiltIns.cs` (~30 sites returning bare `List<StashValue>`), `Stash.Stdlib/BuiltIns/DictBuiltIns.cs:97-131`
**Phase:** P3
**Commit:** 6b302819

### Observation

`RuntimeValues.DeepFreezeObject` has a `case StashArray arr: arr.Freeze(); … recurse` branch but
no case for bare `List<StashValue>`. Such bare lists fall through `default: break` — they are
neither frozen nor traversed. Stdlib producers across the codebase still return bare
`List<StashValue>`:

- `arr.slice`, `arr.concat`, `arr.map`, `arr.filter`, `arr.flat`, `arr.flatMap`, `arr.unique`,
  `arr.partition`, `arr.chunk`, `arr.zip`, `arr.sort`, `arr.sortBy`, `arr.take`, `arr.drop`,
  `arr.parMap`, `arr.parFilter`, `arr.parForEach`, `arr.groupBy`-values, etc.
- `dict.keys`, `dict.values`, `dict.pairs`, and many other dict/format-namespace producers.

Concrete user-visible breakage (does NOT throw today, contrary to acceptance criterion
"deep, transitive"):

```stash
// 1. Direct: arr.slice produces a bare List → freeze walker skips it
readonly const D = { items: arr.slice([1,2,3], 0, 2) };
D.items[0] = 99;          // SILENTLY SUCCEEDS — should throw ReadOnlyError
arr.push(D.items, 99);    // SILENTLY SUCCEEDS — same

// 2. dict.keys/values are bare lists too
readonly const D = { a: 1, b: 2 };
let ks = dict.keys(D);    // ks is a bare List<StashValue>
                          // — not the brief's bug, but the same hole

// 3. Nested in a struct
struct S { items: array; }
readonly let s = S { items: arr.map([1,2,3], fn(x) { return x*2; }) };
s.items.push(99);         // SILENTLY SUCCEEDS — items is a bare List
```

The reason the headline-example `readonly const D = { ports: [80, 443] }; D.ports.push(22);` works
is that the `[80, 443]` literal is constructed by `ExecuteNewArray` as a `StashArray`. The hole
opens the instant any stdlib transform is in the path. A backlog stub already exists at
`.kanban/0-backlog/bugs/DeepFreeze-skips-stdlib-produced-bare-lists.md` and was deliberately
deferred — the recommendation in that stub is option (A), migrate producers.

### Why this matters

Violates Goal 3 ("every collection type honours the frozen flag") and Acceptance Criterion 1
("deep freeze through nested array"). Worse, the failure is **silent** — exactly the property
the brief calls out as the bug class immutability exists to prevent. Once users start writing
`readonly`, any value flowing through `arr.slice`/`map`/`filter`/`concat` punches a hole; and
those are core idiomatic transforms, not edge cases. It also undermines the hermetic-VM next
phase: `IsFrozen` must be reliable for the "share-when-frozen, deep-copy otherwise" decision.

A backlog stub does not justify shipping this — it was introduced by this feature, it violates
the feature's own acceptance criteria, and the test coverage in `FreezeTests.cs` /
`ReadonlyTests.cs` has zero cases that exercise a stdlib producer. The "already filed" framing
is for unrelated pre-existing bugs.

### Suggested fix

Migrate stdlib producers to `StashArray` (option A in the backlog stub) — wide but mechanical:

1. Change return type / wrapper in `ArrBuiltIns.cs` for every `arr.*` that returns
   `List<StashValue>` (about 30 sites: `Slice`, `Concat`, `Map`, `Filter`, `ForEach` (n/a — void),
   `Sort`, `SortBy`, `Take`, `Drop`, `Flat`, `FlatMap`, `Unique`, `Partition`, `Chunk`, `Zip`,
   `ParMap`, `ParFilter`, `GroupBy`-inner-lists).
2. Mirror in `DictBuiltIns.cs` (`Keys`, `Values`, `Pairs`).
3. Audit the source-generator stdlib `[StashFn]` return-marshalling — if `List<StashValue>` returns
   are wrapped automatically at the marshal boundary, the fix collapses to a one-line generator
   change in `Stash.Stdlib.Generators` (preferable: single point of control).
4. Add coverage to `Stash.Tests/Runtime/FreezeTests.cs`: `DeepFreeze_TraversesStdlibProducedArray`
   for each producer family.
5. Add an end-to-end `ReadonlyTests.cs` case:
   `readonly const D = { items: arr.slice([1,2,3], 0, 2) }; arr.push(D.items, 99);` → expect
   `ReadOnlyError`.
6. After the migration, update or close
   `.kanban/0-backlog/bugs/DeepFreeze-skips-stdlib-produced-bare-lists.md`.

### Verify

```
dotnet test --filter "FullyQualifiedName~FreezeTests|FullyQualifiedName~ReadonlyTests"
dotnet test --filter "FullyQualifiedName~ArrBuiltInsTests|FullyQualifiedName~DictBuiltInsTests"
dotnet test                            # ensure no broad regressions from the producer change
```

---

## F03 — [CRITICAL] Typed arrays (`StashTypedArray` and all five subclasses) are neither frozen nor write-guarded

**Status:** fixed
**Fixed in:** 0b353ae (follow-up: 05fe9f4)
**Files:** `Stash.Core/Runtime/Types/StashTypedArray.cs:100-107`, `Stash.Core/Runtime/Types/StashIntArray.cs`, `StashStringArray.cs`, `StashFloatArray.cs`, `StashBoolArray.cs`, `StashByteArray.cs`, `Stash.Core/Runtime/RuntimeValues.cs:316-376` (`DeepFreezeObject` switch)
**Phase:** P3
**Commit:** 6b302819

### Observation

`StashTypedArray` (the abstract base) and its five concrete subclasses
(`StashIntArray`, `StashByteArray`, `StashFloatArray`, `StashBoolArray`, `StashStringArray`)
have **no** `IsFrozen` property, **no** `Freeze()` method, **no** case in
`RuntimeValues.DeepFreezeObject`, and **no** check in `StashTypedArray.VMSetIndex` (line 100).
The `DeepFreezeObject` switch falls to `default: break` for these values, and the in-place
mutators (`Set`, `Add`, `RemoveAt`, `Clear` on the typed-array surface — all abstract on the
base, implemented by each subclass) are similarly unguarded.

User-observable failure modes:

```stash
// Byte buffers — used heavily by net/crypto/encoding namespaces
readonly let buf = encoding.utf8.encode("hello");
buf[0] = 0;             // SILENTLY SUCCEEDS — should throw ReadOnlyError

// Typed-array constructors
readonly let xs = arr.typed([1, 2, 3], "int");
xs[0] = 99;             // SILENTLY SUCCEEDS

// Typed arrays returned by stdlib (ip.parse(...).octets etc., depending on shape)
readonly let ip = ip.parse("1.2.3.4");
// ... if any nested value is a typed array, mutation through it escapes the freeze.
```

### Why this matters

Same severity class as F02 — silent escape of the deep-freeze guarantee for a different
collection kind. Typed arrays are the canonical representation for `byte[]`/`int[]`/`string[]`
results returned by `encoding.*`, `net.*`, `crypto.*`, `arr.typed`/`arr.new`, IP-octet
projections, etc. The brief's Goal 3 ("every reference-typed runtime value … carries a uniform
`IsFrozen` bit and rejects writes with `ReadOnlyError`") and the P3 done_when
("Every write path on dict / array carrier / struct … throws `ReadOnlyError` when the carrier
is frozen") explicitly cover this — typed arrays are reference-typed runtime values.

The Q3 design-review note explicitly resolved StashError as "already write-blocked, no new
guard needed." Typed arrays got no analogous resolution — they were simply missed.

### Suggested fix

Add the uniform flag pattern used for `StashDictionary` / `StashInstance` / `StashArray`:

1. On the `StashTypedArray` base class, add `public bool IsFrozen { get; private set; }` and
   `public void Freeze() => IsFrozen = true;` (same shape as `StashArray`).
2. In `StashTypedArray.VMSetIndex` (line 100), prepend
   `if (IsFrozen) throw new ReadOnlyError("Cannot mutate a frozen <element>[] array.", span);`
   with the per-element-kind wording (the brief's "generalized message per value kind" rule).
3. Add abstract-or-virtual guards to `Set`/`Add`/`RemoveAt`/`Clear` on the base, propagated to
   every subclass — or check `IsFrozen` once in the public-facing `VMSetIndex` (low-cost path)
   and audit every C# caller of the abstract `Set/Add/RemoveAt/Clear` for whether it can be
   reached from user code (stdlib producers).
4. Add a case to `RuntimeValues.DeepFreezeObject`:
   `case StashTypedArray ta: ta.Freeze(); /* element types are primitive — no recursion needed */ break;`
5. Add `Stash.Tests/Runtime/FreezeTests.cs` cases: `DeepFreeze_OnStashIntArray_FreezesAndBlocksWrites`,
   …`_OnStashByteArray_…`, …`_OnStashStringArray_…`.
6. Add `ReadonlyTests.cs` end-to-end:
   `readonly let buf = encoding.utf8.encode("hi"); buf[0] = 0;` → expect `ReadOnlyError`.
7. Once the runtime guard lands, audit `arr.shuffle`-style stdlib mutators that branch into
   `StashTypedArray` (e.g. `Shuffle` line 920) and ensure those paths also see the guard.

### Verify

```
dotnet test --filter "FullyQualifiedName~FreezeTests|FullyQualifiedName~ReadonlyTests"
dotnet test --filter "FullyQualifiedName~ArrBuiltInsTests"    # regression check on typed-array mutators
```

---

## F04 — [IMPORTANT] `arr.shuffle` is missing the frozen guard and absent from SA0847's `KnownInPlaceMutators`

**Status:** open
**Files:** `Stash.Stdlib/BuiltIns/ArrBuiltIns.cs:918-943`, `Stash.Analysis/Rules/Declarations/ReadOnlyMutationRule.cs:50-64`
**Phase:** P5
**Commit:** 36febd30 (analyzer half); P3/runtime half pre-existing

### Observation

Every in-place `arr.*` mutator (`push`, `pop`, `insert`, `removeAt`, `remove`, `clear`, `reverse`,
`sort`) starts with `if (array.IsObj && IsArrayFrozen(array.AsObj)) throw new ReadOnlyError(...)`.
`Shuffle` (line 918) does not. It mutates both the `StashTypedArray` and bare-List branches
without a frozen check. `arr.shuffle(readonlyArr)` therefore silently mutates a frozen array.

`KnownInPlaceMutators` in `ReadOnlyMutationRule.cs` enumerates 11 mutators (8 arr + 3 dict).
`"arr.shuffle"` is also absent there — so the analyzer doesn't statically flag
`arr.shuffle(D)` for a readonly `D` either.

This is documented in `.kanban/0-backlog/bugs/arr-shuffle-missing-frozen-guard.md` (filed during
P5). Listing it here because the parity gap was introduced *by the bounded `KnownInPlaceMutators`
set* this feature shipped, and the brief's "parity with the runtime" criterion is violated.

### Why this matters

Narrow blast radius (one function), but it's a direct violation of the brief's
"every write path … honours the frozen flag" goal, and exposes an inconsistency in the
`ReadOnlyMutationRule` parity-with-runtime claim. The fix is small and best done alongside F02/F03.

### Suggested fix

1. In `ArrBuiltIns.Shuffle` (line 918) add the standard guard:
   ```csharp
   if (array.IsObj && IsArrayFrozen(array.AsObj))
       throw new ReadOnlyError("Cannot mutate a frozen array.", null);
   ```
   (or whatever span the surrounding code provides).
2. Add `"arr.shuffle"` to `ReadOnlyMutationRule.KnownInPlaceMutators`.
3. Add unit tests in `ReadonlyMutationAnalyzerTests` (`ReadonlyConst_ArrShuffle_EmitsSA0847`)
   and in `FreezeTests`/`ReadonlyTests` (`ArrShuffle_OnFrozenArray_ThrowsReadOnlyError`).
4. Close the backlog stub with a Resolution section.

### Verify

```
dotnet test --filter "FullyQualifiedName~ReadOnlyMutationRule|FullyQualifiedName~ReadonlyMutationAnalyzerTests|FullyQualifiedName~FreezeTests"
```

---

## F05 — [MINOR] Brief's acceptance criterion uses `is` for reference-identity, but Stash `is` is a type check (would throw `RuntimeError`)

**Status:** open
**Files:** `.kanban/2-in-progress/readonly-modifier/brief.md:212`, `brief.md:276-277` (acceptance criterion)
**Phase:** P3 (Q1 design log)
**Commit:** - (brief, pre-implementation)

### Observation

The brief states (Goal "Identity-preserving") and Acceptance Criterion:

> Identity is preserved by freezing: `readonly let a = D; a is D` is true and
> `typeof(a) == "dict"` — the value's reference and author-visible type are
> unchanged by the in-place freeze.

But Stash's `is` operator is a **type check** (`ExecuteIs` in
`Stash.Bytecode/VM/VirtualMachine.TypeOps.cs:77`). It dispatches on the right-hand operand: when
it's a string name or a registered type, it returns a bool; when it's a runtime value that is
**not** a struct/enum/interface/known-type (i.e. a dict, in this case `D`), it throws
`RuntimeError("Right-hand side of 'is' must be a type, got ...")`.

So `a is D` (where `D` holds a dict) does not return `true` — it raises. The *intent* of the
acceptance criterion (reference identity is preserved) is satisfied by the implementation: the
P3 `StashArray` is an always-present subclass of `List<StashValue>`, so the C# reference is
preserved across freeze and `typeof(a) == "dict"` holds (verified via the dispatch in
`VirtualMachine.TypeOps.cs:384,451`). The acceptance criterion as worded is just wrong.

No shipped artifact (spec, example, test) repeats the wrong `a is D` claim — only the brief
does. The example uses behavioral aliasing-throws to demonstrate identity, and the spec
section doesn't mention `is D` at all.

### Why this matters

Documentation hygiene only. The brief is the long-term record of design intent and will be
consulted by the embedding-phase team; leaving an incorrect operator claim invites later
confusion ("did P3 weaken `is` to identity?"). Cheap to correct.

### Suggested fix

Update the brief's "Identity-preserving" bullet (line 211–214) and the corresponding
acceptance criterion to use language that reflects actual Stash semantics, for example:

> Identity is preserved by freezing: `readonly let a = D` does not alter the underlying
> reference, so a mutation through `a` reaches the same frozen graph that `D` sees
> (the aliasing-throws test in `FreezeTests` proves this), and `typeof(a) == "dict"` /
> `typeof(a) == "array"` unchanged.

Alternatively keep the spirit and use `RuntimeValues.IsEqual(a, D)` / a structural-identity
predicate that actually exists. Whichever you pick, drop the `a is D` example.

### Verify

```
git diff -- .kanban/2-in-progress/readonly-modifier/brief.md
# Confirm `a is D` claim is removed/replaced.
```

---

## F06 — [MINOR] `StashFrozenArray` is not retired or shimmed; two parallel frozen-array mechanisms now coexist

**Status:** open
**Files:** `Stash.Core/Runtime/Types/StashFrozenArray.cs` (full implementation, unchanged), `Stash.Bytecode/VM/VirtualMachine.Collections.cs:20-29,86-89`, `Stash.Stdlib/SvArgs.cs:70`, `Stash.Stdlib/BuiltIns/ArrBuiltIns.cs:1295-1296` (`IsArrayFrozen`), `Stash.Stdlib/BuiltIns/GlobalBuiltIns.cs:135`
**Phase:** P3
**Commit:** 6b302819

### Observation

P3's done_when says:

> The existing `StashFrozenArray` wrapper is deleted or reduced to a thin shim over the
> in-place flag …

`StashFrozenArray.cs` is unchanged from pre-feature state — it still implements
`IVMTyped/IVMIndexable/IVMIterable/IVMSized/IVMStringifiable` with its own iterator, its own
`VMSetIndex` ReadOnlyError, its own ToString. Boundary callers (DataMember `FreezeMemberValue`
in `VirtualMachine.Collections.cs:638-662`) still produce `StashArray` (the new shape) — so
incoming DataMember reads ARE migrated. But `GetIndexValue` (line 20), `SetIndexValue` (line 86),
`SvArgs.StashList` (line 70), `IsArrayFrozen` (line 1295), and `len()` global all still special-case
`StashFrozenArray`. So two frozen-array shapes coexist in the runtime today:

- `StashArray { IsFrozen = true }` — produced by `readonly` and updated DataMember boundary
- `StashFrozenArray` — produced by older / not-yet-migrated boundary callers and tests

This is functionally safe (every relevant write-guard checks both), but it leaves the design's
"identity-preserving uniform carrier" goal partially achieved and adds carrying cost for every
future maintainer (every guard, every `is List<StashValue>` switch needs to remember both shapes).

### Why this matters

Brief-parity gap, not a correctness bug. Worth a follow-up to retire the legacy class entirely
(or reduce it to a single 5-line `[Obsolete]` shim that constructs a `StashArray.Freeze()`).

### Suggested fix

Either:
- (A) Migrate the remaining boundary producers to construct `StashArray.Freeze()` and delete
  `StashFrozenArray.cs` entirely; update the dual-check `IsArrayFrozen` to single-check; remove
  the four `obj is StashFrozenArray` early-paths in `VirtualMachine.Collections.cs` /
  `SvArgs.cs` / `GlobalBuiltIns.cs`. Verify nothing externally allocates `StashFrozenArray`
  (grep already shows the producer sites are all in this repo).
- (B) Keep `StashFrozenArray` as a sealed `[Obsolete]` factory wrapper:
  ```csharp
  public static class StashFrozenArray
  {
      public static StashArray Wrap(List<StashValue> items) {
          var sa = items as StashArray ?? new StashArray(items);
          sa.Freeze();
          return sa;
      }
  }
  ```
  and migrate call sites.

(A) is cleaner and aligns with the brief; (B) is a smaller diff.

### Verify

```
grep -rn "StashFrozenArray" --include="*.cs" /home/heisen/stash-readonly-modifier/
# Expect: zero references after (A), or only the obsolete factory after (B).
dotnet test
```

---

## F07 — [MINOR] `readonly let` binding doesn't get the `readonly` semantic-token modifier; tree-sitter accepts `for (readonly let ...)`; non-top-level `readonly const` local doesn't set `IsLocalReadonly`

**Status:** open
**Files:** `Stash.Analysis/Visitors/SemanticTokenWalker.cs:328-355`, `tree-sitter-stash/grammar.js:81,92`, `Stash.Bytecode/Compilation/Compiler.Declarations.cs:86`
**Phase:** P2 / P6
**Commit:** 2878912d (semantic tokens), 9f79eea0 (tree-sitter)

### Observation

Three small inconsistencies collected as one finding because each is genuinely minor:

1. **Semantic token modifier asymmetry.** `VisitConstDeclStmt` emits
   `EmitFromToken(stmt.Name, TokenTypeVariable, ModifierDeclaration | ModifierReadonly)` for the
   binding name — but `VisitVarDeclStmt` (the `readonly let` path) emits only
   `ModifierDeclaration`, never `ModifierReadonly`, even when `stmt.IsReadonly` is true. Cosmetic
   LSP gap: a `readonly let X = …` shows X without the readonly modifier color, while
   `readonly const X` shows it. Both `readonly`-prefixed forms should arguably tag the binding
   identically.

2. **Tree-sitter accepts what the parser rejects.** `tree-sitter-stash/grammar.js:81` makes
   `optional('readonly')` part of `variable_declaration`, and `for_statement` (line 328) includes
   `$.variable_declaration` in its init slot — so `for (readonly let x = 0; …)` parses
   successfully in tree-sitter editors (Neovim, IntelliJ tree-sitter front-ends) while the Stash
   parser throws `'readonly' is not allowed in a 'for' loop initializer.`. Pure-IDE
   discrepancy, no runtime effect.

3. **`readonly const` local doesn't propagate `IsReadonly` to `CompilerScope`.**
   `VisitConstDeclStmt` calls `_scope.DeclareLocal(stmt.Name.Lexeme, isConst: true)` — never
   passes `isReadonly: stmt.IsReadonly`. The `Freeze` opcode IS emitted (line 103-104), so
   initial freeze works. But `_scope.IsLocalReadonly` returns `false` for `readonly const`
   locals, which means any future code that conditions on `IsLocalReadonly` (e.g. an analyzer
   refactor that walks the compiler scope) sees the wrong answer. Today benign because const
   rebinds are statically rejected anyway and the upvalue capture path correctly uses
   `IsLocalConst`. Worth fixing for principle-of-least-surprise.

### Why this matters

None of these are user-visible correctness bugs on shipped Stash code. They're polish/parity
issues that should be cleaned up while the context is fresh, and trivial to fix.

### Suggested fix

1. In `SemanticTokenWalker.VisitVarDeclStmt` (line 328-341), add the same
   `ModifierReadonly` conditionally when `stmt.IsReadonly`:
   ```csharp
   int mods = ModifierDeclaration | (stmt.IsReadonly ? ModifierReadonly : 0);
   EmitFromToken(stmt.Name, TokenTypeVariable, mods);
   ```
2. In `tree-sitter-stash/grammar.js`, factor the `optional('readonly')` out of
   `variable_declaration` into a wrapper rule that the *statement* slot uses but the
   *for-init* slot does not (or duplicate `variable_declaration` into a `_for_var_init`
   variant without the `readonly` prefix). Regenerate the parser.
3. In `VisitConstDeclStmt` (line 86), thread the modifier:
   `byte reg = _scope.DeclareLocal(stmt.Name.Lexeme, isConst: true, isReadonly: stmt.IsReadonly);`.

### Verify

```
dotnet test --filter "FullyQualifiedName~SemanticTokenTests"
# Tree-sitter: cd tree-sitter-stash && npx tree-sitter test (after regen).
```

---

## Out-of-scope (noted, not findings)

- **Baseline test failure 2** — `IntegrityVerificationTests.DownloadAndCache_HeaderMatches_Succeeds`
  with `Address already in use` — is environmental, port-bind, and the test class IS in
  `final_verify`'s exclusion filter (`!~IntegrityVerification`). Not feature-related, no action
  needed here.
- **`StashFrozenArray` ↔ `StashArray` dual checks in `SvArgs.StashList`** — once F06 lands, the
  dual unwrap collapses. No separate finding.
