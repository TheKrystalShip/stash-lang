# RFC: `readonly` modifier — deep value immutability

> **Status:** Draft
> **Owner:** Cristian Moraru
> **Created:** 2026-05-30
> **Slug:** readonly-modifier

## Summary

Introduce a new declaration **modifier**, `readonly`, that composes with the existing
`let` and `const` keywords to mark a reference-typed value as **deeply / transitively
immutable**:

```stash
let D            = {...}   // (today) reassignable name, mutable value
const D          = {...}   // (today) fixed name,        mutable value (JS-style)
readonly let D   = {...}   // (new)   reassignable name, deeply-frozen value
readonly const D = {...}   // (new)   fixed name,        deeply-frozen value (fully locked)
```

Two orthogonal axes — **binding** mutability (governed by `let`/`const`, unchanged)
and **value** mutability (governed by absence/`readonly`, new). Any attempt to
mutate a `readonly` value — directly, through an alias, or anywhere down its
transitive object graph — raises `ReadOnlyError` at runtime. The compiler and
analyzer additionally reject statically-visible direct mutations as a best-effort
early-feedback layer.

This is **phase 1 of a 3-phase embedding roadmap** (readonly → hermetic VM →
host SDK). It delivers the language primitive that the next two phases will
consume; the actual wiring of the resulting frozen flag into async-child
isolation (`SpawnAsyncFunction` deep-copy-vs-share) is **out of scope** here.

## Motivation

Two distinct problems converge on the same primitive.

**1. Concurrency hazard that already exists today.** Stash already runs multiple
VMs concurrently across OS threads via `async fn` →
`VirtualMachine.SpawnAsyncFunction` (`Stash.Bytecode/VM/VirtualMachine.Async.cs`),
which forks a child VM on a thread-pool thread. The fork shallow-copies globals
(`new Dictionary<string, StashValue>(_globals)`) but `StashValue` is a struct
holding an `_obj` reference, so every reference-typed global (dict, array,
struct, `StashError`) is **shared by reference** between parent and child
running on different threads. The runtime value types are not thread-safe;
concurrent mutation of a shared global collection is a **live correctness
hazard today**, independent of any embedding work. The chosen fix is "deep-copy
non-frozen, share-by-reference when frozen" — which requires a real, deep,
author-visible "frozen" notion. (The actual wiring is the *next* phase; what
must land *here* is the primitive.)

**2. `const` cannot express value immutability.** Stash's `const` is
deliberately **JavaScript-style**: it fixes the binding, not the value.
`const D = {a: 1}; D.a = 2;` *succeeds today*. Redefining `const` to also
freeze its value was considered and **rejected** — it is a breaking change to
established, JS-compatible semantics with high blast radius (every script that
mutates a `const`-bound collection — idiomatic today — would start throwing),
and it surprises exactly the audience the `const` design courted. A new,
orthogonal modifier adds the missing value axis without touching `const`.

`readonly` is also independently useful (catching accidental mutation,
expressing intent) — part of why it is worth shipping standalone and first.

## Goals

- Add a `readonly` declaration modifier that composes with both `let` and
  `const` and triggers **deep, transitive** freezing of the initializer (and,
  for `readonly let`, of every subsequent rebind).
- Generalize the existing partial freeze primitives (`StashDictionary._frozen`,
  `StashFrozenArray` wrapper) into a **uniform deep-freeze mechanism** across
  dicts, arrays, structs (`StashInstance`), and `StashError` — without
  changing a value's identity/type from the author's perspective.
- All write paths on every collection / object type honour the frozen flag and
  raise `ReadOnlyError` on attempted mutation.
- Best-effort **compile-time / analyzer diagnostics** for statically-visible
  direct mutations of a known-`readonly` binding (`D.x = …`, `D[i] = …`,
  in-place stdlib mutators on `D`).
- Full toolchain coverage per `.claude/language-changes.md`: lexer +
  soft-keyword table, parser, all six AST visitors, analyzer, LSP semantic
  tokens / hover / completion, playground Monarch tokenizer, VS Code TextMate
  grammar, **tree-sitter grammar** (`tree-sitter-stash/grammar.js`),
  language-specification update + ToC, an `examples/*.stash` showcase,
  xUnit tests.
- Build the deep-freeze mechanism (cycle-safe `DeepFreeze` walker + per-value
  `IsFrozen` flag) as an **internal-only** primitive, surfaced through the
  single `readonly` front-end. **No Stash-callable `freeze()` is exposed**
  (decision 2026-06-01): the hermetic-VM phase consumes the internal
  `IsFrozen`/`DeepFreeze`, not a stdlib function, and a `freeze()` name would
  collide with JavaScript's deliberately *shallow* `Object.freeze`.

## Non-Goals

- **No wiring into `SpawnAsyncFunction`.** The deep-copy-non-frozen /
  share-frozen logic in async-child globals belongs to the next phase
  (hermetic VM). This spec only delivers the frozen primitive it will consume.
- **No total static immutability guarantee.** A complete transitive-immutability
  type system (tracking aliasing through `let` rebinds and function
  parameters) is out of scope. The runtime flag remains load-bearing;
  compile-time diagnostics are best-effort.
- **No change to `const` semantics.** `const` continues to fix only the
  binding, JS-style. Authors who want a deeply-immutable constant write
  `readonly const`.
- **No new shallow-freeze surface.** The existing internal shallow uses
  (`cli.argv` etc.) are migrated to the unified deep mechanism; we do not
  expose a separate shallow option.
- **No host-SDK / embedding work.** That is phase 3.
- **No `unfreeze` / thaw.** Freezing is one-way (matches the existing
  `StashDictionary.Freeze()` contract).

## Design

The end state is a single runtime concept — **"this value is frozen"** —
reachable through **one** author-facing front-end:

- **Declarative:** the `readonly` modifier on a `let` / `const` declaration.
  The initializer (and every rebind value, for `readonly let`) is deep-frozen
  at assignment time.

The underlying mechanism — a cycle-safe `DeepFreeze` walker plus a per-value
`IsFrozen` flag — is built as an **internal** primitive. It is *not* exposed as
a Stash-callable `freeze()` function (decision 2026-06-01): the name would
collide with JavaScript's deliberately *shallow* `Object.freeze`, and the only
downstream consumer (the hermetic-VM phase) binds to the internal
`IsFrozen`/`DeepFreeze`, not to a stdlib surface. Keeping `readonly` the sole
trigger also shrinks the aliasing footgun (see Semantics) — every deep-freeze
now originates at a syntactically visible `readonly` site, never from an
arbitrary function call.

Frozenness is **carried by the value, not the binding** — aliasing
(`let a = readonlyD; a.x = 2`) must still throw, which only a value-side flag
can catch.

### Surface

```stash
// readonly + const — fully locked
readonly const Config = { host: "localhost", ports: [80, 443] };
Config = {};            // throws — binding fixed by const
Config.host = "x";      // throws — value frozen by readonly (ReadOnlyError)
Config.ports.push(22);  // throws — deep: nested array is frozen too

// readonly + let — value frozen, binding rebindable
readonly let Snapshot = makeSnapshot();
Snapshot.x = 1;            // throws — value frozen
Snapshot = makeSnapshot(); // ok — binding mutable; new value re-frozen on assignment
Snapshot.x = 1;            // throws — re-frozen

// Aliasing — runtime flag catches it
readonly const D = { a: 1 };
let alias = D;
alias.a = 2;            // throws — alias points at the same frozen value

// Primitives — harmless no-op
readonly let n = 42;    // ok; ints are already immutable

// Soft keyword — `readonly` is still a legal identifier elsewhere
let readonly = true;    // ok; only special immediately before let/const

// Aliasing footgun — deep freeze reaches a PRE-EXISTING nested value
let shared = { count: 0 };
readonly const snap = { data: shared };
shared.count = 1;       // throws — `shared` was frozen as collateral,
                        // even though it carries no `readonly` keyword

// Catching
try {
    Config.host = "x";
} catch (ReadOnlyError e) {
    io.println("cannot mutate: ", e.message);
}
```

### Semantics

- **Deep / transitive.** Freezing a dict freezes every nested dict, array,
  struct, and `StashError` reachable through it. Cycle-safe (visited set).
  Depth follows from **purpose**: `readonly` is *value immutability for safe
  sharing* (the Rust / Swift-value-type / Clojure / Java-immutability-guidance
  camp), **not** the *binding/annotation* immutability that C# `readonly`,
  JS `const`, TS `readonly`, Java `final`, and Kotlin `val` provide — those are
  shallow because they fix a name or type, not a value. A shallowly-frozen
  value with a mutable nested collection is still unsafe to share across the
  `async`-child thread boundary, which would defeat the motivation. The
  languages whose freeze-like primitive is shallow (JS `Object.freeze`, Python
  `frozen`) never hit this problem because they forbid sharing mutable objects
  across threads at all (structured-clone copy / the GIL) — an architectural
  choice Stash already declined. So the spec frames the C# contrast as
  *"different category, not bolder."*
- **The genuine novelty is retroactive in-place freeze, not depth.**
  Rust/Clojure/Swift make values immutable *from birth*; Stash freezes
  *post-hoc*, because the modifier decorates an initializer that may reference
  pre-existing values — so a deep-freeze can reach a nested value that some
  other (non-`readonly`) binding still aliases. Two properties keep this safe:
  it fails **loud** (`ReadOnlyError` at the offending write — never silent data
  skew), and every freeze originates at a visible `readonly` declaration. A
  *clone-on-freeze* alternative (deep-copy the initializer, Rust/Swift-style
  value semantics) was **considered and rejected**: it would convert the loud
  throw into a *silent* divergence between the frozen copy and the still-mutable
  original — exactly the bug class immutability exists to prevent — and would
  carve a surprising island of copy-semantics into an otherwise
  reference-semantic language.
- **Reference types only.** On primitives (`int`, `float`, `bool`, `string`,
  `duration`, `ip_address`, `semver`, `byte_size`, enums, etc.) `readonly` is
  a harmless no-op — primitive values are already immutable; the binding axis
  is `const`'s job there.
- **`readonly` is a soft (contextual) keyword.** It is special only immediately
  before `let`/`const`; everywhere else it stays a legal identifier
  (`let readonly = 1;` parses). This matches Stash's existing declaration
  modifiers `async` and `export` (also soft) and never breaks code that uses
  `readonly` as a name. Because `let`/`const` are hard keywords, `readonly let`
  / `readonly const` is unambiguous on a two-token lookahead.
- **Identity-preserving.** Freezing does **not** change a value's runtime type
  from the author's perspective: after `readonly let a = arr`, `typeof(a) ==
  "array"` and `a is arr` (same reference). This is the central implementation
  constraint — see Implementation Path.
- **`readonly let` re-freezes on every rebind.** Every value assigned to a
  `readonly let` binding is deep-frozen at assignment time, not only the
  initializer.
- **Runtime flag is load-bearing.** `readonly` is the declaration-site
  trigger; the actual mutation block is enforced by a per-value frozen flag
  honoured by every write path (collection mutators, field assignment, index
  assignment, in-place stdlib mutators).
- **`ReadOnlyError` is the single error type.** Already exists
  (`StashDictionary` uses it); generalized message wording per value kind.
- **Compile-time diagnostics — best-effort.** The analyzer rejects statically
  visible direct mutations of a known-`readonly` binding: field/index
  assignment (`D.x = …`, `D[i] = …`) and known in-place stdlib mutators
  (`arr.push(D, …)`). Mutation through aliasing or function-parameter
  passing is **not** statically diagnosed — runtime catches it.

### Implementation Path

`readonly` joins `Keywords.SoftKeywords` (the lexer emits it as an `Identifier`;
it stays usable as a name). The parser's `Declaration()` statement entry — which
also parses declarations inside every block and function body via `ParseBlock` —
gains a 3-way branch on `Identifier("readonly")`: followed by `let`/`const` →
modifier (sets `IsReadonly`); followed by `fn`/`struct`/`enum`/`interface` →
targeted "readonly only modifies let/const" diagnostic; otherwise → fall through
to a plain identifier. Two declaration sites bypass `Declaration()` and need
explicit handling: the `for`-init clause (`readonly` **rejected** there — a
rebound loop variable can't be meaningfully frozen) and `export` (which
**allows** `export readonly const`, single canonical order). `VarDeclStmt`/`ConstDeclStmt`
carry an `IsReadonly` flag → SemanticResolver / SymbolCollector mark the binding
slot as readonly so the analyzer can diagnose direct mutations → Compiler emits
an "after the initializer, deep-freeze" step on the value (for both initial
binding and `readonly let` rebinds) → Runtime collection / object types
(`StashDictionary`, plain array carrier, `StashInstance`, `StashError`) all
carry a uniform `IsFrozen` bit and reject writes with `ReadOnlyError` → a
single `DeepFreeze` walker traverses any value graph, frozen-flagging each
node exactly once (cycle-safe) →
the existing internal shallow uses (`cli.argv` etc.) and the
`StashFrozenArray` wrapper are migrated to the in-place flag → all six AST
visitors, the formatter, the LSP semantic-token walker, completion, hover,
the playground tokenizer, the TextMate grammar, the spec, and the example all
show or honour the modifier → end-to-end tests prove that direct, aliased,
and nested writes all throw, primitives are a no-op, and `readonly let`
rebinds re-freeze.

The central implementation question — *reconcile the dict in-place flag vs the
`StashFrozenArray` wrapper into a uniform identity-preserving deep-freeze* —
is resolved by giving the plain-array carrier an `IsFrozen` flag and retiring
`StashFrozenArray` in favour of the in-place flag on the unified carrier.
Whether plain Stash arrays need a small wrapper class to hang the flag on, or
whether an existing wrapper already exists, is settled during P3 (Runtime flag
plumbing) — both options preserve value identity from the Stash side.

## Acceptance Criteria

- `readonly const D = { ports: [80] }; D.ports.push(22);` raises `ReadOnlyError`
  at runtime (deep-freeze through nested array).
- `readonly const D = {...}; let a = D; a.x = 2;` raises `ReadOnlyError` at
  runtime (aliasing is caught by the value-side flag, not the binding).
- `readonly let S = {...}; S = {...}; S.x = 1;` raises `ReadOnlyError` (rebind
  re-freezes).
- `readonly let n = 42;` is accepted, and `n = 99` works (primitive no-op,
  binding axis remains `let`).
- Identity is preserved by freezing: `readonly let a = D; a is D` is true and
  `typeof(a) == "dict"` — the value's reference and author-visible type are
  unchanged by the in-place freeze.
- `let shared = { count: 0 }; readonly const snap = { data: shared };
  shared.count = 1;` raises `ReadOnlyError` — deep freeze reaches a pre-existing
  alias (the headline footgun; a **loud** failure, never silent data skew).
- The analyzer surfaces a diagnostic for a statically-visible direct mutation
  of a `readonly` binding (e.g. `D.x = …` where `D` is declared `readonly`)
  *before* runtime.
- `readonly` is rejected on declarations that are not `let` or `const` (parse
  error with a clear message), and is rejected in a `for`-init clause.
- `readonly` remains usable as a plain identifier (`let readonly = 1;` parses) —
  confirming the soft-keyword treatment.
- `export readonly const Config = {...};` parses and produces an exported,
  binding-fixed, deeply-frozen constant.
- LSP highlights `readonly` as a keyword/modifier; the formatter round-trips
  `readonly let`/`readonly const` declarations unchanged; completions
  for `readonly` appear at statement-start.
- The language specification documents `readonly` (with a section in the ToC),
  including the explicit divergence from C#'s shallow `readonly`.
- An `examples/readonly.stash` script runs under the CLI and exercises every
  decided semantic (deep, alias, rebind, primitive no-op, the aliasing footgun,
  `ReadOnlyError` catch).
- `dotnet test` (with the documented-flakies filter, but **including**
  `StandardLibraryReferenceTests`, `CompletionSurfaceSnapshotTests`, and
  `Wave1ThrowsCoverageTests`) passes.

## Phases

The phase list lives in `plan.yaml` so scripts can read it. Spine:

1. **P1 — Lexer + parser surface.** `readonly` as a **soft keyword**
   (`Keywords.SoftKeywords`) + `IsReadonly` flag on `VarDeclStmt` /
   `ConstDeclStmt`; 3-way dispatch in `Declaration()`; `for`-init rejection;
   `export readonly const` wiring; parse errors for bad placement.
2. **P2 — All six AST visitors compile + formatter round-trips.** Resolver,
   Validator, SymbolCollector, SemanticTokenWalker, StashFormatter, Compiler
   accept the new flag; formatter prints it; semantic token walker tags it.
3. **P3 — Uniform runtime deep-freeze primitive.** Add `IsFrozen` to every
   reference-typed runtime value (dict / array carrier / `StashInstance` /
   `StashError`); every write path throws `ReadOnlyError`; cycle-safe
   `DeepFreeze` walker; retire `StashFrozenArray` wrapper in favour of the
   uniform flag (preserving the `cli.argv`-style boundary behavior).
4. **P4 — Compiler wiring.** After initializer (and after each `readonly let`
   rebind), emit a deep-freeze of the value. End-to-end CLI script shows
   direct/aliased/nested writes all throwing.
5. **P5 — Analyzer best-effort diagnostics.** Static detection of direct
   field/index mutation and known in-place mutator stdlib calls on a
   known-`readonly` binding.
6. **P6 — LSP / playground / TextMate / tree-sitter / spec / examples.** Hover,
   completion, semantic tokens for `readonly` (AST-driven, so precise); Monarch
   tokenizer; TextMate `storage.modifier` scope (contextual, before
   `let`/`const`); tree-sitter grammar rule; spec section + ToC entry carrying
   the deep-vs-shallow rationale and the aliasing-footgun example;
   `examples/readonly.stash`.

## Open Questions

*All design-review questions are resolved as of 2026-06-01 (see below). Q2 —
the `freeze()` capability gate — is moot: no Stash-callable `freeze()` is
exposed.*

### Resolved during design review (2026-06-01)

- **Q1 — Single carrier type for plain arrays — RESOLVED: always-wrap.**
  Verified `is`/`==` on reference types is C# reference identity
  (`RuntimeOps.IsEqualSlow` → `RuntimeValues.IsEqual` → `object.Equals`).
  This rules out "wrap only when frozen" (swapping the bare `List` for a
  wrapper at freeze time would make a frozen array fail `is`-identity with its
  pre-freeze reference — `readonly let a = arr; a is arr` would be **false**).
  **Decision:**
  every plain array is an always-present wrapper carrier (uniform with
  `StashDictionary`'s in-place `_frozen` model), carrying an `IsFrozen` bit
  from creation. Because this touches the VM's hottest path, P3/P4 carry a
  perf gate (see Decision Log).
- **Q3 — Frozen `StashError` — premise corrected; near-zero work.** The brief
  assumed user code can extend a caught error's properties (`e.foo = "bar"`).
  Verified false: `StashError` does **not** implement `IVMFieldMutable`, so
  `VirtualMachine.TypeOps.cs` already throws on any field write today.
  `StashError` is therefore already write-blocked. **Decision:** P3 does *not*
  add an `IsFrozen` write-guard to `StashError`; `DeepFreeze` only needs to
  **traverse into** its properties dict to freeze nested reachable values.
- **Q4 — `readonly let` rebind through a closure — RESOLVED: full enforcement.**
  Verified the compiler knows declared mutability for **locals**
  (`CompilerScope.IsLocalConst`, used by `Compiler.Helpers.cs` to reject const
  reassignment), but `UpvalueDescriptor` carries **no** mutability flag and
  `ExecuteSetUpval` is unguarded — so a closure can today rebind an outer
  `readonly let` (and, separately, an outer `const`) without a check.
  **Decision:** thread the readonly bit into `UpvalueDescriptor` and guard the
  upvalue store path so closure rebinds also re-freeze. This incidentally
  closes a **pre-existing `const`-through-closure enforcement hole** (filed as
  a backlog bug; the fix lands here).
- **Q5 — Reserved vs soft keyword — RESOLVED: soft (contextual).** Verified
  Stash's existing declaration modifiers `async`/`export` are soft keywords
  (`Keywords.SoftKeywords`, recognized by the parser via lexeme comparison);
  `readonly` is the same shape and joins them. Unambiguous because it is always
  followed by the hard keywords `let`/`const`. Keeps `readonly` usable as an
  identifier and matches the sibling precedent. The hook lives in
  `Declaration()` (which `ParseBlock` re-enters, covering blocks + fn bodies);
  the `for`-init and `export` sites bypass it and are handled explicitly.
- **Q6 — `freeze()` stdlib — RESOLVED: not exposed.** The internal `DeepFreeze`
  walker is built in P3 and surfaced only through `readonly`. A Stash-callable
  `freeze()` is dropped (collides with JS's shallow `Object.freeze`; the
  hermetic-VM phase depends on the internal `IsFrozen`/`DeepFreeze`, not a
  stdlib function; a sole `readonly` trigger shrinks the aliasing footgun). The
  old `freeze()` phase (P5) is removed and the phases renumbered.

## Decision Log

| Date | Decision | Rationale |
| --- | --- | --- |
| 2026-05-30 | Modifier on top of `let`/`const`, not a standalone keyword. | Two orthogonal axes (binding mutability vs value mutability); a modifier lets authors answer both independently. |
| 2026-05-30 | `readonly` is deep / transitive, **not** shallow like C#. | `readonly` is *value immutability for safe sharing* (Rust / Swift value types / Clojure / Java-guidance camp), not *binding/annotation* immutability (C# `readonly`, JS `const`, TS `readonly`, Java `final`, Kotlin `val` — all shallow). Shallow freezing leaves nested collections mutable and unsafe to share across the `async`-child boundary, defeating the motivation. The shallow-`freeze` languages (JS/Python) dodge the problem by forbidding cross-thread mutable sharing — a choice Stash already declined. |
| 2026-05-30 | Do **not** redefine `const` to be deeply immutable. | Breaking change to established JS-style semantics; high blast radius across existing scripts; surprises exactly the audience the `const` design courted. |
| 2026-05-30 | Compile-time checks are best-effort, not a total static guarantee. | A complete transitive-immutability type system is out of scope. Aliasing escapes any pure compile-time check; the runtime flag is load-bearing. |
| 2026-06-01 | **Reversed:** do **not** expose a Stash-callable `freeze()`; surface the deep-freeze mechanism only through the `readonly` modifier. | The `DeepFreeze` walker is internal and built regardless; the hermetic-VM phase binds to the internal `IsFrozen`/`DeepFreeze`, not a stdlib function; `freeze()` would collide with JS's deliberately *shallow* `Object.freeze`; and a single `readonly` trigger keeps every deep-freeze at a visible site, shrinking the aliasing footgun. Supersedes the 2026-05-30 "ship `freeze()`" decision. |
| 2026-05-30 | Out of scope: actual wiring into `SpawnAsyncFunction`. | Belongs to the next phase (hermetic VM); this spec delivers the primitive that phase consumes. |
| 2026-05-30 | Retire `StashFrozenArray` wrapper in favour of an in-place flag on the unified array carrier. | Identity-preserving freeze is a Goal; two mechanisms (in-place flag for dicts, wrapper for arrays) is the existing inconsistency this feature exists to resolve. |
| 2026-06-01 | Q1: plain arrays become an **always-present wrapper carrier** with an in-place `IsFrozen` bit (not wrap-on-freeze). | `is`/`==` is C# reference identity; swapping the carrier at freeze time would break the identity criterion (`readonly let a = arr; a is arr`). Always-present carrier preserves identity and unifies with the `StashDictionary` model. |
| 2026-06-01 | Q4: thread the readonly/mutability bit into `UpvalueDescriptor` and guard the upvalue store so closure rebinds of `readonly let` re-freeze. | The runtime-flag-is-load-bearing doctrine still protects the original graph, but full enforcement closes the rebind escape hatch *and* a pre-existing `const`-through-closure hole surfaced during review. |
| 2026-06-01 | Add a before/after **perf gate** (`bench_algorithms` / `bench_numeric`) to P3 and P4. | The always-present array carrier touches the VM's hottest path; the project's mandatory perf doctrine should catch a carrier-choice regression in-phase, not post-merge. |
| 2026-06-01 | Q3 corrected: `StashError` is already write-blocked (no `IVMFieldMutable`); P3 only needs `DeepFreeze` to **traverse into** its properties, not a new write-guard. | Verified against `VirtualMachine.TypeOps.cs`; the brief's "user can extend `e.foo`" premise was false. |
| 2026-06-01 | `DeepFreeze` treats **function/closure values as opaque** (traversal skips them; they are not frozen). | A complete capture-graph freeze is out of scope and matches the "functions aren't frozen" mental model; recorded as a case in the `DeepFreeze` type switch. |
| 2026-06-01 | Spec must call out two sharp edges: transitive freeze reaches **pre-existing aliases** of nested values, and `ReadOnlyError`'s message is **generalized per value kind** (the current text is namespace-member-specific). | Both are author-visible behaviors the original examples don't show; documenting them prevents surprise. |
| 2026-06-01 | Q5: `readonly` is a **soft (contextual) keyword**, not reserved. | Matches the existing soft declaration modifiers `async`/`export`; never breaks `readonly` used as an identifier; unambiguous since it is always followed by the hard keywords `let`/`const`. |
| 2026-06-01 | Parser hook lives in `Declaration()` with a 3-way branch; `for`-init **rejects** `readonly`; `export readonly const` is **allowed** (single canonical order). | `Declaration()` covers top-level + blocks + fn bodies (via `ParseBlock`); the `for`-init and `export` construction sites bypass it and are handled explicitly, so `for (readonly let …)` and `export readonly const` don't yield cryptic errors. |
| 2026-06-01 | `tree-sitter-stash/grammar.js` added to the toolchain surface. | It carries keywords inlined per-rule and was absent from the original toolchain list; the modifier needs a grammar rule there too. Scope glob `tree-sitter-stash/**` added to `plan.yaml`. |
| 2026-06-01 | Clone-on-freeze (value-semantics deep copy) **considered and rejected** in favour of in-place deep freeze. | In-place fails *loud* (`ReadOnlyError` at the write); clone-on-freeze would fail *silent* (the frozen copy and the still-mutable original diverge) — the bug class immutability exists to prevent — and would island copy-semantics into a reference-semantic language. Preserves Q1's always-present in-place carrier. |
