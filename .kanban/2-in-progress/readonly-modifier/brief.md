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
- Full toolchain coverage per `.claude/language-changes.md`: lexer,
  `TokenType`, parser, all six AST visitors, analyzer, LSP semantic tokens /
  hover / completion, playground Monarch tokenizer, VS Code TextMate grammar,
  language-specification update + ToC, an `examples/*.stash` showcase,
  xUnit tests.
- Ship a sibling runtime stdlib function — `freeze(value)` — that is a second
  front-end to the same mechanism, since the frozen-flag plumbing exists
  regardless and `freeze()` is required by the hermetic-VM phase. (Decision
  log records this scope choice.)

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
reachable via two front-ends:

- **Declarative:** `readonly` modifier on a `let` / `const` declaration. The
  initializer (and every rebind value, for `readonly let`) is deep-frozen at
  assignment time.
- **Dynamic:** `freeze(value)` stdlib function. Deep-freezes any reference-typed
  value in place, returns the same value (identity preserved).

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

// Dynamic
let dyn = { a: 1 };
freeze(dyn);
dyn.a = 2;              // throws

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
  This **diverges from C#'s `readonly`** (which is shallow / binding-only);
  the spec explicitly calls out the divergence.
- **Reference types only.** On primitives (`int`, `float`, `bool`, `string`,
  `duration`, `ip_address`, `semver`, `byte_size`, enums, etc.) `readonly` is
  a harmless no-op — primitive values are already immutable; the binding axis
  is `const`'s job there.
- **Identity-preserving.** Freezing does **not** change a value's runtime type
  from the author's perspective: `typeof(freeze(arr)) == "array"`,
  `freeze(arr) is arr` (same reference). This is the central implementation
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

Parser/lexer add the modifier on declaration nodes → `VarDeclStmt`/`ConstDeclStmt`
carry an `IsReadonly` flag → SemanticResolver / SymbolCollector mark the binding
slot as readonly so the analyzer can diagnose direct mutations → Compiler emits
an "after the initializer, deep-freeze" step on the value (for both initial
binding and `readonly let` rebinds) → Runtime collection / object types
(`StashDictionary`, plain array carrier, `StashInstance`, `StashError`) all
carry a uniform `IsFrozen` bit and reject writes with `ReadOnlyError` → a
single `DeepFreeze` walker traverses any value graph, frozen-flagging each
node exactly once (cycle-safe) → stdlib `freeze()` calls the same walker →
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
- `freeze(d); d.a = 1;` raises `ReadOnlyError`; `freeze(d) is d` is true
  (identity preserved); `typeof(freeze(d)) == "dict"`.
- The analyzer surfaces a diagnostic for a statically-visible direct mutation
  of a `readonly` binding (e.g. `D.x = …` where `D` is declared `readonly`)
  *before* runtime.
- `readonly` is rejected on declarations that are not `let` or `const` (parse
  error with a clear message).
- LSP highlights `readonly` as a keyword/modifier; the formatter round-trips
  `readonly let`/`readonly const` declarations unchanged; completions
  for `readonly` appear at statement-start.
- The language specification documents `readonly` (with a section in the ToC),
  including the explicit divergence from C#'s shallow `readonly`.
- An `examples/readonly.stash` script runs under the CLI and exercises every
  decided semantic (deep, alias, rebind, primitive no-op, `freeze()`,
  `ReadOnlyError` catch).
- `dotnet test` (with the documented-flakies filter, but **including**
  `StandardLibraryReferenceTests`, `CompletionSurfaceSnapshotTests`, and
  `Wave1ThrowsCoverageTests`) passes.

## Phases

The phase list lives in `plan.yaml` so scripts can read it. Spine:

1. **P1 — Lexer + parser surface.** `readonly` keyword + modifier flag on
   `VarDeclStmt` / `ConstDeclStmt`; parse errors for bad placement.
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
5. **P5 — `freeze()` stdlib function.** Second front-end onto the P3
   primitive; throws metadata, completion, docs regen.
6. **P6 — Analyzer best-effort diagnostics.** Static detection of direct
   field/index mutation and known in-place mutator stdlib calls on a
   known-`readonly` binding.
7. **P7 — LSP / playground / TextMate / spec / examples.** Hover, completion,
   semantic tokens for `readonly`; Monarch tokenizer; TextMate grammar; spec
   section + ToC entry; `examples/readonly.stash`.

## Open Questions

- **Q1 — Single carrier type for plain arrays?** Today plain arrays are bare
  `List<StashValue>` (`StashValue.Obj`), which cannot carry a flag. P3 must
  choose between (a) introducing a minimal wrapper class for plain arrays to
  hang the flag on (identity preserved on the Stash side, runtime CLR type
  changes once) and (b) extending an existing array-bearing type to cover
  the plain-array case. Decision deferred to the P3 implementer with the
  guidance that identity from the Stash author's perspective is the binding
  constraint.
- **Q2 — `freeze()` capability gate?** The existing `StashCapabilities` set
  is about side effects (filesystem / network / process). `freeze()` is
  pure and should likely require **no** capability — confirm during P5.
- **Q3 — Frozen `StashError` properties.** `StashError` carries a properties
  dict that user code can extend (`e.foo = "bar"`). Settle in P3 by giving
  `StashError` the same `IsFrozen` flag; confirm no existing test relies on
  mutating a caught error after the catch site froze it (none expected — the
  freeze surface is new for `StashError`).

## Decision Log

| Date | Decision | Rationale |
| --- | --- | --- |
| 2026-05-30 | Modifier on top of `let`/`const`, not a standalone keyword. | Two orthogonal axes (binding mutability vs value mutability); a modifier lets authors answer both independently. |
| 2026-05-30 | `readonly` is deep / transitive, **not** shallow like C#. | Shallow freezing does not make a value safe to share across threads, defeating the motivation. Divergence from C# is documented in the spec. |
| 2026-05-30 | Do **not** redefine `const` to be deeply immutable. | Breaking change to established JS-style semantics; high blast radius across existing scripts; surprises exactly the audience the `const` design courted. |
| 2026-05-30 | Compile-time checks are best-effort, not a total static guarantee. | A complete transitive-immutability type system is out of scope. Aliasing escapes any pure compile-time check; the runtime flag is load-bearing. |
| 2026-05-30 | Ship `freeze()` stdlib in the same feature. | The mechanism is built here regardless; `freeze()` is needed by the hermetic-VM phase; shipping both together avoids a partial primitive. |
| 2026-05-30 | Out of scope: actual wiring into `SpawnAsyncFunction`. | Belongs to the next phase (hermetic VM); this spec delivers the primitive that phase consumes. |
| 2026-05-30 | Retire `StashFrozenArray` wrapper in favour of an in-place flag on the unified array carrier. | Identity-preserving freeze is a Goal; two mechanisms (in-place flag for dicts, wrapper for arrays) is the existing inconsistency this feature exists to resolve. |
