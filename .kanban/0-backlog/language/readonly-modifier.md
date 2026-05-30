# `readonly` Modifier — Language Feature (Design Rationale & Context)

> **Status:** Ready to spec (phase 1 of a 3-phase embedding roadmap)
> **Created:** 2026-05-30
> **Discovery context:** Surfaced during a from-scratch design discussion on making the Stash
> engine embeddable in C# host applications (Lua `lua_State`-style hosting). The embedding work
> is *not* in scope here — this document exists so the `readonly` spec carries the full reasoning
> chain that produced it. Embedding details are recorded only as motivation.

---

## 1. What this is

A new **`readonly` modifier** that composes with the existing `let` / `const` declaration
keywords to mark a value as **deeply immutable** (transitively frozen):

```stash
let D = {...}            // reassignable name, mutable value      (today, unchanged)
const D = {...}          // fixed name,        mutable value      (today, unchanged)
readonly let D = {...}   // reassignable name, deeply-frozen value
readonly const D = {...} // fixed name,        deeply-frozen value   (fully locked)
```

It is a **modifier**, not a standalone keyword, because immutability has two orthogonal axes
and a modifier lets authors answer them independently:

- **Binding axis** — can the *name* be rebound? Governed by `let` (yes) / `const` (no). *Unchanged.*
- **Value axis** — can the *thing it points at* be mutated? Governed by absence (yes, default) /
  `readonly` (no). *New.*

`readonly` is **orthogonal to `const`** — they govern different axes, so `readonly const` is not
redundant. `const` stays exactly as it is today.

## 2. Why we can't just use `const`

Stash's `const` is **JavaScript-style: it fixes the binding, not the value.** Today
`const D = {a: 1}; D.a = 2;` *succeeds* (the value mutates); only `D = somethingElse` throws.
This was a deliberate JS-inspired choice when `const` was designed.

That means `const` cannot express "this value is immutable." Redefining `const` to be deeply
immutable was considered and **rejected**: it is a breaking change to established, JS-compatible
semantics, with high blast radius (every script that mutates a `const`-bound collection — idiomatic
today — would start throwing) and it surprises exactly the audience the `const` design courted.

`readonly` adds the missing **value axis** without touching `const`.

## 3. Why the language needs this (motivation)

The driving force is making Stash a genuinely embeddable scripting engine — one self-contained,
**hermetic** `StashEngine` per host, in the spirit of Lua's `lua_State`. A precondition for that
is isolation. During the design review we found that **Stash already runs multiple VMs concurrently
across OS threads today**: `async fn` calls route through `VirtualMachine.SpawnAsyncFunction`
(`Stash.Bytecode/VM/VirtualMachine.Async.cs`), which forks a **child VM on a thread-pool thread**
via `Task.Run` and returns a `StashFuture`.

Crucially, that fork **shallow-copies globals** (`new Dictionary<string, StashValue>(_globals)`):
a new dictionary, but `StashValue` is a struct holding an `_obj` reference, so reference-typed
globals (dict / array / struct) are **shared by reference** between parent and child running on
different threads. The runtime value types are not thread-safe, so concurrent mutation of a shared
global collection is a **live correctness hazard today**, independent of any embedding work.

The chosen isolation model for the parent ↔ async-child scope is:

- **Default = deep-copy** every *non-frozen* reference-typed global per async spawn. Correct
  unconditionally.
- **Opt-in = share by reference** any value that is *deeply immutable* — safe precisely because it
  cannot be mutated, therefore safe to hand to another thread without a clone.

To express that opt-in, the language needs a first-class way for authors to declare a value
**deeply immutable**. That is `readonly` (declarative) and a sibling runtime `freeze()` (dynamic).
Both set the same underlying runtime "frozen" flag. The same primitive also underpins the future
VM-pool snapshot model and host-object immutability — one primitive, several consumers.

`readonly` is **independently useful** beyond this motivation (catching accidental mutation,
expressing intent), which is part of why it is worth doing standalone and first.

## 4. Semantics (the spec's job to formalize)

- **Deep / transitive.** `readonly const D = { user: { name: "x" } };` must make `D.user.name = "y"`
  throw. Shallow freezing would not make the value safe to share, defeating the purpose. **This
  diverges from C#'s `readonly`, which is shallow** (C# `readonly` fixes the reference; the pointee
  stays mutable — closer to Stash's current `const`). The divergence must be documented explicitly
  so C#-savvy authors are not surprised.
- **Reference types only.** `readonly` bites on dicts, arrays, structs. On primitives
  (`readonly let x: int = 42`) it is a harmless no-op — primitive values are already immutable; the
  binding axis there is `const`'s job. Docs should set this expectation.
- **`readonly let` re-freezes on rebind.** For the mutable-binding / frozen-value corner, every
  value assigned to the binding is frozen at assignment time (not just the first).
- **Runtime flag is the load-bearing mechanism.** `readonly` is the *declaration-site trigger*; the
  actual protection is a frozen bit carried by the value at runtime, because aliasing escapes any
  pure compile-time binding check (`readonly const D; let a = D; a.x = 2;` must throw — only a
  runtime flag on the *value* catches that). `readonly` and `freeze()` are two front-ends to the
  same mechanism.
- **Compile-time diagnostics (best-effort, not a total guarantee).** The compiler / analyzer should
  reject *statically visible, direct* mutations of a `readonly` binding (`D.a = 2`, `arr.push(D, …)`
  where `D` is known `readonly`) for early feedback. A *total* static guarantee would require a full
  transitive immutability type system (out of scope); aliasing through `let` or function parameters
  is caught at runtime by the frozen flag.

## 5. Existing primitives to generalize (implementation starting points)

Stash already has a **partial, internal** freeze mechanism — used today to protect cached/shared
stdlib returns (e.g. `cli.argv`), per the spec rule *"reference-typed returns are frozen at the
boundary."* It is shallow and type-inconsistent, and must be made **deep and uniform**:

- `StashDictionary` (`Stash.Core/Runtime/Types/StashDictionary.cs`): in-place `_frozen` flag,
  `IsFrozen`, `Freeze()`, write ops throw `ReadOnlyError`. **Shallow** — only the dict itself.
- `StashFrozenArray` (`Stash.Core/Runtime/Types/StashFrozenArray.cs`): a read-only **view wrapper**
  over `List<StashValue>` (does not copy). Used at the DataMember getter boundary.
- **Inconsistency to resolve:** dicts freeze via an in-place flag; arrays freeze via a *separate
  wrapper type*. A plain Stash array is a raw `List<StashValue>` (`StashValue.Obj`) with nowhere to
  hang a flag — hence the wrapper. A general author-facing `readonly`/`freeze` should **not change a
  value's identity or type** (`freeze(arr)` should still *be* that array, just immutable). Reconciling
  the in-place-flag vs wrapper approaches across dict / array / struct is the central implementation
  question.
- **Structs (`StashInstance`):** verify whether a freeze flag exists; deep-freeze must cover structs
  too (and `StashError`, which carries a mutable properties dict).

## 6. Scope note — this is a full language change

Per `.claude/language-changes.md`, a new modifier touches the whole toolchain and every item must
be addressed in-phase: lexer / `TokenType`, parser, **all six AST visitors** (Compiler,
SemanticResolver, SemanticValidator, SymbolCollector, SemanticTokenWalker, StashFormatter), the
analyzer's diagnostic rules, LSP (semantic tokens / hover / completion), the playground Monarch
tokenizer, the VS Code TextMate grammar, the language specification + ToC, an `examples/*.stash`
showcase, and xUnit tests (happy path, deep/nested, aliasing-throws-at-runtime, primitive no-op,
rebind-refreezes, compile-time-diagnostic cases). Also: **verify `readonly` is not already in use as
an identifier** — introducing a reserved word has a backward-compat cost.

The runtime "frozen" semantics needed by async-child sharing should land with this feature; the
**actual wiring of deep-copy-vs-share into `SpawnAsyncFunction`** belongs to the *next* phase
(hermetic VM), not here. This spec delivers the language primitive; the hermetic-VM spec consumes it.

## 7. Roadmap (sequencing — decided)

1. **`readonly` modifier** (this spec) — the language primitive, standalone.
2. **Hermetically seal the VM** — virtualize process-global state (cwd / env / signals), per-VM IC-slot
   cloning on shared `Chunk`s, and wire parent ↔ async-child isolation (deep-copy non-frozen globals,
   share `readonly`/frozen ones) using the primitive from step 1.
3. **Embedding Host SDK** (`Stash.Hosting`) — `StashEngine` as a hermetic `lua_State`; host objects
   by reference via a reflection-free core interface with a JIT-only reflection proxy (CLI AOT path
   preserved); marshalling hybrid (primitives/data copy, real objects reference, collections copy
   with an `AsHostObject` opt-in); `InvokeAsync` bridging to `StashFuture`'s real `Task`;
   `IAsyncDisposable`; typed `StashException` out / host exceptions catchable-in-Stash. See the older
   `0-backlog/tools/Stash Embedding API — Host SDK Design Analysis.md` for prior analysis (superseded
   in part by the decisions above).

Steps 2 and 3 are **not** to be specced until step 1 ships.
