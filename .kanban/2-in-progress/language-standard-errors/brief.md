# RFC: Error-Type Taxonomy — seal §Errors and retype the runtime

> **Status:** Draft
> **Owner:** cristian.moraru@live.com
> **Created:** 2026-06-07
> **Slug:** language-standard-errors
> **Milestone:** language-standard

## Summary

Unit #4 of the `language-standard` milestone seals the **§Errors and Cleanup** section of
`docs/Stash — Language Specification.md` and lands the **cross-cutting error-type taxonomy**
(milestone `coverage.md` workstream #2 — "the single biggest gap"). The §Errors section today
delegates the entire type catalog to a generated, non-normative document; ~196 core
runtime failures throw the **unregistered bare `RuntimeError`** base, surfacing to user code
as `.type == "RuntimeError"` — a name the spec mentions but never defines. After this unit:

1. **`Error` is the normatively-defined, concrete, registered root** — the everyday catch-all
   `catch (Error e)` traps every routinely-handleable failure. The C# class `RuntimeError`
   becomes the **abstract internal base** that never surfaces as a `.type` to user code.
2. **System faults (`CancellationError`, `exit`/`ExitException`) sit OUTSIDE `Error`** as
   siblings under the abstract root — `catch (Error e)` and untyped `catch (e)` do **not**
   trap them; explicit `catch (CancellationError e)` still does. Python-`Exception`-under-
   `BaseException` / Ruby-`StandardError`-vs-`Exception` model.
3. **Every one of the ~351 bare-base throw sites is retyped** to a named, registered
   `[StashError]` type. The core layer's ~196 sites map to a small set of new core types
   normatively enumerated by the §Errors rewrite (`NameError`, `FieldError`,
   `ArithmeticError`/`DivisionByZeroError`, an `ImportError` family, plus reuse of existing
   `TypeError`/`ValueError`/`IndexError`). The stdlib/CLI/host layer's ~155 sites retype to
   contract-compliant named types catalogued in the generated Standard Library Reference,
   per the L1 contract.
4. **§Errors normatively defines** the root type, the catch model (including the system-fault
   split and the `Matches` subtype-walk for any grouping parent introduced), the **payload
   contract** every error obeys, the **`*Error` naming convention** every library error must
   honor, and the **registration requirement** every catchable type satisfies. The generated
   Standard Library Reference becomes a derived view, not the source of truth.

The change makes "what type does this throw?" answerable by reading the spec — every observable
runtime failure has a named, registered, documented type, and uncaught-at-top-level behavior
is law, not an emergent property.

## Motivation

The `error-taxonomy-spec-vs-impl-ledger.md` audit (`.kanban/0-backlog/language/`) confirms
the milestone `coverage.md` thesis empirically:

- **97 spec-clause hits** for "runtime error" / "throws" / "raises" across 11 spec sections;
  in essentially every case outside the already-sealed §Async region and the `ReadOnlyError`
  clauses, the spec says "produces a runtime error" with **no named type**.
- **351 bare `throw new RuntimeError(...)` sites** in the implementation (Stdlib 149,
  Bytecode 144, Core 52, Cli 5, Hosting 1) — every one surfaces to user code as
  `.type == "RuntimeError"`, an undocumented public-by-accident name.
- The **same logical failure class diverges between layers**: a stdlib argument rejected for
  wrong type throws `TypeError`; a VM operand mismatch for the *same* type-mismatch reason
  throws bare `RuntimeError`. `str.substring` OOB throws `IndexError`; `buf` OOB throws bare
  base. `http.download` timeout throws bare base, not `TimeoutError`. The taxonomy is split
  in half and the halves don't agree.
- **The §Errors section itself** delegates: "Stash errors are values. The standard built-in
  error types are specified in the [Standard Library Reference]" (L2555-2556). The reference
  is generated from code metadata, so the spec contains no normative catalog at all.
- **Three thrown types are unregistered.** The bare `RuntimeError` base, the `AssertionError`
  type (spec-named at §Equality L730 but with no `[StashError]`), and `UserRuntimeError` (the
  vehicle for user-`throw`) sit outside the documented public catalog.
- **The catch-all swallows the world.** `catch (Error e)` and untyped `catch (e)` today trap
  `CancellationError`. A naive `try { ... } catch (e) { /* swallow */ }` silently absorbs a
  `task.cancel` / Ctrl-C — the Python/Ruby/PHP designers all chose hierarchy placement
  *specifically* to prevent this; Stash inherited the gap.

Nothing about this is hypothetical for users: a `try/catch` author who wants to recover from
a missing-file `IOError` cannot today distinguish it from a typo-in-variable-name (both bare
`RuntimeError`); a test harness cannot reliably distinguish operator type-mismatch from
arithmetic domain failures; a workflow cannot honor user cancellation while still recovering
from disk failures.

## Goals

- Make `Error` the single, normatively-defined, concrete, registered root every routinely-
  handleable failure surfaces as (`.type == "Error"` for an otherwise-untyped throw).
- Retype every core-VM bare-base throw site to a named, registered, spec-enumerated
  `[StashError]` type — the bare `RuntimeError` base stops being a user-visible `.type`.
- Retype every stdlib/CLI/host bare-base throw site to a named, registered, contract-compliant
  `[StashError]` type — catalogued in the generated reference per the contract clause.
- Define the **contract** in §Errors that every library error type obeys: `*Error` suffix,
  registered with `[StashError]`, structured payload via `Properties`/`PropertyTypes`,
  human-readable `Description`.
- Make `CancellationError` and `exit`/`ExitException` **uncatchable** by `catch (Error e)` /
  `catch (e)` — the one-notch system-vs-everyday split — while still catchable by their
  exact type.
- Register `AssertionError` with `[StashError]` (the §Equality L730 clause names it; close
  the registration gap).
- Settle the §Async sealed-clause amendment for `throw "string"` and "C# exception escape"
  to surface as `Error` (not the now-absent bare `RuntimeError`).
- Settle the §Values sealed-clause amendments for operand mismatch (→ `TypeError`),
  `reveal`-on-non-secret (→ `TypeError`), and division-by-zero (→ a named arithmetic type).
- Specify the **catch model** end-to-end: the `Matches` subtype walk (necessary for the
  system-fault split and for any grouping parent introduced); typed-vs-untyped catch
  semantics; the unknown-catch-name behavior (silently never matches today — ratify).
- Specify **uncaught-at-top-level** behavior as law (exit code, stderr format).
- Specify **`.suppressed`** (deferred-cleanup error accumulation during unwinding) as law.
- Specify **`.cause`** (explicit error chaining; field form `throw E { ..., cause: prev }`)
  as law: a fifth always-present member on every caught error, defaulting to `null`,
  set only by the raising code.
- Ship a static analyzer warning (**SA0170**) for `catch (Name e)` where `Name` is
  statically provable to be neither a registered built-in error type nor an in-scope
  declared type. Runtime semantics remain "silently never matches"; the warning is a
  tooling aid.
- Ship every spec edit backed by `Category=Conformance` tests under
  `Stash.Tests/Conformance/Errors/`. Update cross-unit sealed-test assertions in-place
  (never two tests asserting opposite things).

## Non-Goals

- **Implicit error-context capture** (Python `__context__` / Ruby auto-`cause` —
  the runtime automatically setting `.cause` to the in-flight caught error when a
  `throw` runs inside a `catch`). Explicit `.cause` is in-scope this unit; implicit
  context is deliberately deferred — a future option in Open Questions.
- **Dual accessor / sentinel channel** (Ruby `fetch` / Elixir `!`). Net-new surface; opt-in.
- **`RetryExhaustedError` (D13).** The current "rethrow last error" behavior matches the
  spec and the data; no new type. (If introduced later, it is a single-clause amendment.)
- **HARD raise-vs-sentinel boundary.** Full-retype changes the **type** of throws that
  already exist; it must NOT add a throw where today there is a sentinel. **Dict-key-
  missing-on-read returns `null` today and STAYS returning `null`.** If a `KeyError` leaf
  is created for the lookup group, it is reserved for the null-key-WRITE case and/or a
  future strict accessor — never retrofitted onto today's null-returning read.
- **General hierarchy depth.** The default is flat (root → concrete leaves). A grouping
  parent is introduced **only** when a concrete `catch <group>` handler exists in real
  code; each grouping node is justified by naming the handler that wants it. Speculative
  Python-deep hierarchies are out of scope.
- **Renaming the `Cli*` family.** Surfaced to the user as a forced contradiction (the L1
  contract clause vs the 7-of-9 violators). Default in this unit: rename in-unit (registry
  is pre-release, only the in-repo CLI consumes them); user override available.
- **A new error model for `Stash.Hosting` (host DTO).** `Stash.Hosting.StashError` (the
  C# record surfacing a script error to a C# host) is unchanged. `HostError` (the runtime
  type for CLR exceptions escaping a host delegate) stays registered, gets a §Errors
  contract-citation only.

## Design

### Surface

#### The root type `Error`

A new `Stash.Runtime.Errors.Error` C# class — `[StashError(Description = "Base catchable
runtime error.")]`, concrete, instantiable. It is the **registered root** that surfaces as
`.type == "Error"` for any throw that does not name a more specific type (string throw, dict
throw without `type`, any non-error value throw).

```stash
try {
    throw "boom";        // wraps to Error("boom")
} catch (Error e) {
    io.println(e.type);  // "Error"
}
```

The existing C# `RuntimeError` class becomes the **abstract internal base** every catchable
runtime error inherits from. It is no longer instantiable; `new RuntimeError(...)` in C# fails
to compile (the Construct upgrade in the final phase). It never surfaces as a `.type` to user
code. Any spec or analyzer text naming `RuntimeError` is rewritten to `Error`.

#### The system-fault split

The internal C# base of every Stash-catchable runtime exception is `RuntimeError` (abstract).
But the **registered, Stash-facing root** is `Error`. `CancellationError` and the runtime
exception representing `exit(code)` (`ExitException`) are **siblings** of `Error` — neither
derives from it in the registered hierarchy (they remain catchable by their **exact name**
through the catch-by-name fast path, but `ErrorTypeRegistry.Matches` does **not** treat the
`"Error"` catch-all as matching them).

Concrete consequence:

```stash
try {
    await task.run(async fn() { time.sleep(60); });
} catch (Error e) {           // does NOT trap CancellationError or exit
    io.eprintln(e.message);
} catch (CancellationError e) { // still catches the cancellation explicitly
    io.println("cancelled cleanly");
}
```

`exit(0)` likewise propagates through `catch (Error e)` and untyped `catch (e)` unimpeded;
`defer` blocks still run (the existing dispatch-loop unwinding for `ExitException` is
preserved).

#### Catch grouping (introduced only where earned)

By default the hierarchy is **flat**: every leaf derives directly from the abstract C# base.
Introducing a grouping parent (a Python-style `LookupError`, `ArithmeticError`, ...) requires:

1. naming a real Stash-code handler that would write `catch (<Group> e)` to trap several
   leaves at once;
2. extending `ErrorTypeRegistry.Matches` with a **subtype walk** over a name->parent map
   built from `[StashError(Parent = "...")]` metadata;
3. each grouping parent is itself a concrete `[StashError]` (a user can `throw` it directly)
   unless explicitly noted abstract.

This unit evaluates the candidate grouping types the research surfaced (`LookupError` over
`IndexError`/`KeyError`; `ArithmeticError` over `DivisionByZeroError` and any future numeric
faults) against this bar — see *Forced Decisions* below.

#### The `[StashError]` contract

§Errors normatively states the contract every library error type must obey (the L1 clause):

- The C# class name is the Stash-facing `.type` string (`StashErrorAttribute.Name` override
  is a code smell requiring justification).
- The name ends in **`Error`**.
- The class is registered with `[StashError]` and appears in `BuiltInErrorRegistry`.
- The class derives from the abstract `RuntimeError` base (so `BuiltInErrorRegistry.NameOf`
  resolves it; so the catch-by-name fast path finds it).
- Structured payload is declared via `StashErrorAttribute.Properties` + `PropertyTypes`;
  the C# class exposes those fields as public properties surfaced by `GetProperties()`.
- A human-readable `Description` is required (consumed by the generated reference).

The contract is a normative §Errors clause; the **catalog** of contract-compliant types
remains in the generated Standard Library Reference (the derived view) for stdlib/CLI/host
types. Only the **core** types are spec-enumerated in §Errors itself.

#### `Error` payload contract (sealed)

Every caught error value exposes exactly:

- `.type` — `string`, the registered type name (always present);
- `.message` — `string`, the human-readable message (always present, may be empty);
- `.stack` — `array<dict>` of stack frames captured at throw or catch (always present;
  may be empty when no source location is available);
- `.suppressed` — `array<Error>` of errors collected from deferred-cleanup actions that
  threw during the propagation of this error (always present; usually empty);
- `.cause` — `Error?` (an `Error` value or `null`), the underlying error this error was
  raised in response to. **Explicit-only**: `.cause` is `null` unless the raising code
  set it via the dict / struct literal's `cause` field (e.g.
  `throw IOError { message: "config load failed", cause: prev };`). The runtime does
  **not** automatically chain — there is no implicit "this error was raised while
  handling another" capture (see *Negative Space* below).

Plus the per-type payload declared by each type's `[StashError(Properties, PropertyTypes)]`.

The "where supported" hedge at today's §Errors L2558 is removed: the five fields are
**always present** (`cause` is always *present* as a member, with value `null` when
unset), and the per-type payload is documented per-type in the generated reference.

**Negative space — no implicit chaining.** A `throw` inside a `catch` clause does NOT
automatically copy the caught error into the new error's `.cause`. The raising code
must opt in by passing `cause: <caught>` in the throw expression. (Python's `__context__`
and Ruby's auto-`cause` are deferred future options — see Open Questions.) This keeps
the chain explicit and predictable: a non-null `.cause` always reflects deliberate
authoring intent, never runtime happenstance.

#### Throw

- `throw <value>;` where `<value>` is a built-in error (e.g. `TypeError { message: "..." }`)
  raises that value with its registered type intact.
- `throw <value>;` where `<value>` is a struct instance whose type is **not** a registered
  error type raises a `UserRuntimeError` whose `.type` is the struct's type name (today's
  `ExecuteThrow` behavior, retained).
- `throw { type: "MyError", message: "...", ... };` (dict literal) raises a
  `UserRuntimeError` whose `.type` is the dict's `type` field. **Negative space:** a dict
  literal **without** a `type` field today defaults to `"RuntimeError"` — that string is
  rewritten in `VirtualMachine.ControlFlow.cs:39` to `"Error"`. A `type` field whose value
  collides with a registered type name still produces a `UserRuntimeError` (the user's
  intent overrides; the catch-by-name fast path still finds it).
- `throw <string>;`, `throw <number>;`, `throw <any non-error value>;` all wrap into a
  new `Error(<stringified-value>)` instance and surface as `.type == "Error"`. (Today's
  `ExecuteThrow` :31 string path constructs `new RuntimeError(msg, span)` directly; that
  becomes `new Error(msg, span)`.)
- `throw;` inside a `catch` rethrows the current error preserving span and stack
  (unchanged).
- `throw;` outside a `catch` clause raises an `Error` with the message `"Rethrow used
  outside of a catch block."` (the message stays; the **type** flips from bare base to
  `Error`).

#### `Matches` subtype walk

The `Stash.Core.Runtime.ErrorTypeRegistry.Matches(errorType, targetType)` predicate gains
a **bounded subtype walk** over a name->parent map. The map is built once from the
`StashErrorAttribute.Parent` metadata of every registered type:

- Walk from `errorType` toward the root; match if `targetType` is encountered.
- `"Error"` matches every type whose chain reaches `"Error"` (the registered root) — does
  **not** match `CancellationError` / `exit` (system faults — not under `Error`).
- The walk is bounded by the number of registered types (no cycles by construction —
  source-generator validation: see *Cross-Cutting Concerns*).
- The CLR-identity fast path in `ExecuteCatchMatch` is preserved for the no-grouping common
  case; the subtype walk runs only when the fast path misses.

#### Empty-`typeNames` catch (the untyped `catch (e)` form)

Today, `ExecuteCatchMatch` :705-709 with an empty `typeNames` array matches unconditionally
(any error). After this unit, that fast path is **gated on the registered hierarchy**: an
untyped `catch (e)` matches **only** errors whose registered type reaches `Error`. System
faults (`CancellationError`, `exit`) are *not* trapped by `catch (e)`. The compiler emits an
empty-`typeNames` array for `catch (e)` and `catch (Error e)` indistinguishably (semantically
equivalent: both are the everyday catch-all).

#### Uncaught-at-top-level

A runtime error that propagates past the top-level frame:

- prints **one line to stderr** of the form `"<type>: <message>"` followed by the captured
  stack (one frame per line, indented two spaces, each frame as
  `"at <function> (<file>:<line>:<col>)"`);
- prints the `.suppressed` list, if any, indented under `"suppressed errors:"`;
- exits the process with **exit code 1** (`Error` and any subtype).

`exit(code)` propagates through to the CLI runtime as today: prints **nothing**, sets the
exit code to `code`, runs every pending `defer`. `CancellationError` at top level prints
`"cancelled"` to stderr (one line, no stack) and exits with **exit code 130**
(SIGINT-cancelled convention). These are CLI behaviors; a host that embeds Stash through the
hosting SDK receives the error via its own surface and chooses its own action.

#### `.suppressed` (deferred-cleanup error accumulation)

When an error `E` is propagating and a `defer` block on the unwound stack throws an error
`Ed`:

- The primary error `E` continues to propagate (the original error wins).
- `Ed` is appended to `E.SuppressedErrors`.
- `defer` blocks continue to run; further suppressed errors append to the list (LIFO order
  of defer execution, FIFO order of suppression — i.e. first-suppressed appears first in
  `E.suppressed`).
- A caught `E` exposes the list as `.suppressed` (an array of `Error` values, possibly
  empty).
- A `defer` block that throws when there is **no** propagating error becomes the primary
  error (today's behavior; unchanged).

### Semantics

#### Core failure → type (normative, enumerated in §Errors)

This is the surface §Errors itself enumerates (the L1 *core* taxonomy). Spec-enumerated
core types and the situations they own:

| Core type | Throws on | Replaces today's |
| --------- | --------- | ---------------- |
| `Error` (concrete root) | String throw, dict throw without `type`, non-error value throw, bare `throw;` outside catch | bare `RuntimeError` for these specific cases |
| `TypeError` | Operand type mismatch (`+`, `-`, `*`, `/`, `%`, `**`, `<`, `<=`, `>`, `>=`, `++`, `--`, `in` with non-iterable, `for-in` non-iterable, call non-callable, indexing non-indexable, accessing fields on non-struct, `reveal` on non-secret); stdlib argument type mismatch (unchanged from today) | bare `RuntimeError` for operand mismatch; preserves `TypeError` for stdlib args |
| `ValueError` | Value out of domain (shift count, byte element out of `[0,255]`, typed-array wrong element type; stdlib value-domain checks (unchanged) | bare `RuntimeError` for these VM cases; preserves `ValueError` for stdlib |
| `IndexError` | Array / string / byte-array index out of bounds (VM); non-integer index | bare `RuntimeError` in VM; aligns with `IndexError` in stdlib (`str.substring`) |
| `NameError` (new) | Reading an undefined variable; assigning to an undeclared name | bare `RuntimeError` in VM (`Variables.cs:44,53`) |
| `FieldError` (new) | Reading an undefined struct field; accessing a field on a non-struct receiver; assigning to an unknown struct field; missing required field in struct construction | bare `RuntimeError` in VM (`StashInstance.cs:78,87`; `TypeOps.cs`) |
| `ArithmeticError` (new, grouping parent) | (concrete, instantiable; the common catch-supertype for arithmetic faults) | n/a |
| `DivisionByZeroError` (new, under `ArithmeticError`) | `int / 0`; literal `0.0 / 0.0` | bare `RuntimeError` (`Arithmetic.cs:165,172`; `RuntimeOps.cs:275,283`) |
| `ImportError` (new, grouping parent) | (concrete; the common catch-supertype for the import family) | n/a |
| `ModuleNotFoundError` (new, under `ImportError`) | Import path resolves to no module | bare `RuntimeError` (`Modules.cs:102,272`) |
| `ImportCycleError` (new, under `ImportError`) | Circular import detected | bare `RuntimeError` (`Modules.cs:79`) |
| `ExportError` (new, under `ImportError`) | Imported name not exported by the module; package has no entry point | bare `RuntimeError` (`Modules.cs:316,340`) |
| `ReadOnlyError` (existing) | Mutation of frozen value / readonly namespace member. **Does NOT cover `const`-assign** — see `ConstAssignError` below. | preserves frozen-value/namespace `ReadOnlyError` (existing 28 sites) |
| `ConstAssignError` (new) | Assignment to a `const` binding | bare `RuntimeError` for `const`-assign (`Variables.cs:77,90`) |
| `LockError` (existing) | Lock acquisition failure (no change to the type; §Statements amendment names it) | already correct |
| `StateError` (existing) | `for in` / iterator-on-closed; SFTP-on-closed-connection; `return` outside function; using `process.spawn` handle across task boundary; using `test.*` outside a `test.describe` block | bare `RuntimeError` in VM (`ControlFlow.cs` return-outside-fn); bare `RuntimeError` in stdlib (sftp, test) |
| `NotSupportedError` (existing) | Restricted side effect in embedded mode (§Runtime); platform / capability gates | bare `RuntimeError` in VM (`ControlFlow.cs:119` elevate-denied) |
| `AssertionError` (existing, now registered) | `assert.*` failures (no behavior change; gains `[StashError(Properties = ["expected", "actual"], ...)]`) | unregistered -> registered |

**System faults (registered but OUTSIDE `Error`):**

| Type | Throws on | Caught by `catch (Error e)`? |
| ---- | --------- | ----------------------------- |
| `CancellationError` | `task.cancel` / Ctrl-C cooperative cancellation | **No** — by exact name only. **This is the live breaking change** (today `catch (e)` and `catch (Error e)` trap a `CancellationError`; after this unit they do not). |
| (the `exit(code)` propagation, represented internally as `ExitException`) | `process.exit(code)` | **No** — propagates uncaught. `ExitException` derives from `System.Exception` (not `RuntimeError`), so the StashError catch machinery already bypasses it; the spec entry here DOCUMENTS existing behavior, it does not change anything. `ScriptCancelledException` and `StepLimitExceededException` are likewise `System.Exception`-derived and outside the catch surface. |

**Contract-layer types (registered, contract-compliant, NOT spec-enumerated):**

All existing stdlib/CLI/host types — `IOError`, `CommandError`, `TimeoutError`, `AliasError`,
runtime `ParseError`, `HostError`, plus the renamed `Cli*` family (see Forced Decisions). New
stdlib types introduced by the retype (e.g. potential `ArgumentError` for stdlib arity guards
— see Forced Decisions) are also contract-layer. They are catalogued in the generated
reference, not enumerated in §Errors itself; they all obey the L1 contract.

#### Cross-section amendments (every section the ledger §3 names)

The §Errors rewrite is the load-bearing edit, but every section that today says "produces a
runtime error" gets a parallel amendment naming the type:

- **§Bindings & Scope** (L905, L915): "produces a runtime error" -> "raises `NameError`"
  (undefined read / undeclared assign); "raises `ConstAssignError`" (const-assign — a
  new binding-specific type, distinct from `ReadOnlyError` which stays scoped to
  frozen-value / readonly-namespace-member mutation per D3 user override).
- **§Expressions** (L1198, L1280, L1313, L1360, L1379, L1392, L1405, L1421, L1437): each
  unnamed clause -> named (`TypeError` for non-callable / non-indexable / invalid operand /
  unsupported `in` / `++`/`--`; `IndexError` for OOB; `FieldError` for missing-field;
  `NameError` for unknown name; `TypeError` for non-assignable target).
- **§Statements & Control Flow** (L1514, L1525, L1563, L1582): `TypeError` for non-iterable
  in `for-in`; `StateError` for `return` outside a function; **`LockError` named** for lock
  acquisition; `NotSupportedError` for elevation-denied in embedded mode.
- **§Aggregate Types** (L2149): "raises `FieldError`" for missing-required / unknown-field
  (NB: the milestone `coverage.md` flags L2149 as a false clause — `TypeOps.cs:347` does
  *not* check today; this unit makes the check real or honestly down-codes the clause; the
  decision is recorded in *Forced Decisions* below).
- **§Source Files & Modules** (L320): unnamed -> named (`ImportCycleError`,
  `ModuleNotFoundError`, `ExportError`, `TypeError` for non-string path).
- **§Shell Integration** (L2432-2438): unnamed runtime error -> **`CommandError`** named.
  Field contract already specified; the type name is now spec-normative.
- **§Namespace Members** (L2379): "may throw `IOError`" stays (already named).
- **§Errors and Cleanup** (the rewrite itself): see *Specification Delta* for the full
  normative prose.

#### Sealed-section amendments (cross-unit discipline)

These touch already-sealed clauses; each existing `Category=Conformance` test moves rather
than a new contradicting test landing:

- **§Values L793 (sealed unit #2):** `RuntimeError("Division by zero.")` ->
  `DivisionByZeroError("Division by zero.")`. The conformance test in
  `Conformance/Values/EqualityNumericConformanceTests.cs` or `CoercionConformanceTests.cs`
  that asserts the bare-base on literal `0.0 / 0.0` is updated.
- **§Values L824 (sealed unit #2):** "every operand mismatch raises a `RuntimeError`" ->
  "every operand mismatch raises a `TypeError`". `Conformance/Values/CoercionConformanceTests.cs`
  L302/L319-region tests update from `Assert.ThrowsAny<RuntimeError>` to
  `Assert.ThrowsAny<TypeError>` (or equivalent) and the XML doc comments rewrite.
- **§Values L844 (sealed unit #2):** "raises a `RuntimeError`" (reveal-non-secret) ->
  "raises a `TypeError`". `Conformance/Values/SecretConformanceTests.cs:341,351`
  (`Reveal_NonSecret_ThrowsRuntimeError_*` / `Reveal_PlainString_ThrowsRuntimeError_*`)
  is renamed + retyped.
- **§Async L1708 (sealed unit #1):** "wraps to a generic `RuntimeError`" -> "wraps to a
  generic `Error`". `Conformance/Async/CombinatorsConformanceTests.cs:150` and any
  D7-error-wrap assertion update.
- **§Async L1710 (sealed unit #1):** "`throw \"string\"` inside a task wraps to
  `RuntimeError(\"string\")`" -> "wraps to `Error(\"string\")`". The conformance test
  `D7_ThrowString_WrapsToRuntimeError_PerSpecAsyncD7` in
  `Conformance/Async/FuturesCoreConformanceTests.cs:184` is renamed +
  `Assert.Equal("RuntimeError", error.ErrorType)` becomes `Assert.Equal("Error", ...)`.
- **§Equality L730 (sealed unit #3):** "raises `AssertionError`" — no prose change; the
  type registration is name-preserving. No test moves.

### Specification Delta

The exact normative prose. The §Errors and Cleanup section is **rewritten** (the old text is
delegate-only and contains the false "Standard Library Reference" delegation); the smaller
cross-section amendments and sealed-clause amendments are listed individually.

#### §Errors and Cleanup — full rewritten section

```
## Errors and Cleanup

### Error Values

Stash errors are first-class values. Every Stash-catchable runtime error is an instance of
a *registered* error type. The registered root is **`Error`**: a concrete, instantiable
type that surfaces as the `.type` of any throw not naming a more specific type. Every
runtime error type whose values are catchable by `catch (Error e)` or `catch (e)` derives
(directly or transitively) from `Error`. Two registered types — `CancellationError` and
the exit propagation type — are *registered* but sit **outside** the `Error` subtree;
see *System Faults* below.

Every caught error value exposes the following members:

- `.type`: `string`. The registered type name (e.g. `"TypeError"`, `"IOError"`, `"Error"`).
  Always present.
- `.message`: `string`. The human-readable message. Always present; may be empty.
- `.stack`: `array<dict>`. The captured call stack at throw or catch, most-recent frame
  first; each frame is a dict with members `function: string`, `file: string`, `line: int`,
  `column: int`. Always present; may be empty when no source location is available.
- `.suppressed`: `array<Error>`. Errors collected from deferred cleanup that threw during
  this error's propagation (see *Defer and Suppressed Errors* below). Always present; usually empty.
- `.cause`: `Error?`. The underlying error this error was raised in response to, or `null`.
  Set **only** by the raising code through the `cause` field of a throw expression (see
  *Throw* and *Error Chaining* below). The runtime does not chain automatically: a
  `throw` inside a `catch` does **not** copy the caught error into the new error's
  `.cause` unless the throw expression names it explicitly.

In addition, each registered type may declare *typed properties* (named, documented fields
beyond the five above). Typed properties for a built-in type are documented in the
[Standard Library Reference](Stash%20%E2%80%94%20Standard%20Library%20Reference.md). For
example, `CommandError` exposes `exitCode: int`, `stderr: string`, `stdout: string`, and
`command: string`.

### Built-in Error Types

The following error types are *core* to the language — they are raised by language
constructs and the virtual machine. Every catchable language-level failure surfaces as one
of these types or as a more specific registered subtype:

| Type | Parent | Situation |
| ---- | ------ | --------- |
| `Error` | (root) | Throwing a string, number, or any non-error value; bare `throw;` outside a catch clause; dict throw without a `type` field. |
| `TypeError` | `Error` | Operand type mismatch in any operator (`+`, `-`, `*`, `/`, `%`, `**`, `<`, `<=`, `>`, `>=`, `==` operand validity, `++`, `--`); calling a non-callable value; indexing a non-indexable value; iterating a non-iterable value with `for-in`; `in` with an unsupported right operand; accessing or writing a field on a non-struct receiver; `reveal` on a non-secret value; non-integer index; stdlib argument type mismatch. |
| `ValueError` | `Error` | Value outside the domain expected by an operation (shift count out of range, byte element out of `[0, 255]`, typed-array wrong element type, stdlib value-domain rejection). |
| `IndexError` | `Error` | Array, string, or byte-array index out of bounds. |
| `NameError` | `Error` | Reading an undefined variable; assigning to an undeclared name. |
| `FieldError` | `Error` | Reading an undefined struct field; assigning to an unknown struct field; structurally-required field missing from a struct construction. |
| `ArithmeticError` | `Error` | Catch-supertype for arithmetic faults (`DivisionByZeroError` and any future numeric domain types). |
| `DivisionByZeroError` | `ArithmeticError` | `int / 0`, `int % 0`; literal `0.0 / 0.0` (non-literal float division by zero produces `Infinity` or `NaN`, not an error — see §Values and Types). |
| `ImportError` | `Error` | Catch-supertype for import/module faults. |
| `ModuleNotFoundError` | `ImportError` | Import path resolves to no module. |
| `ImportCycleError` | `ImportError` | A circular import is detected. |
| `ExportError` | `ImportError` | Imported member is not exported by the module; package has no entry point. |
| `ReadOnlyError` | `Error` | Mutating a frozen value or a readonly namespace member. (`const`-binding reassignment raises `ConstAssignError`, not `ReadOnlyError`.) |
| `ConstAssignError` | `Error` | Assigning to a `const` binding. |
| `LockError` | `Error` | `lock` acquisition or release failure. |
| `StateError` | `Error` | An operation requires a state the receiver is not in (closed connection, iterator after exhaustion, `return` outside a function, using a `process.spawn` handle across a task boundary, `test.*` outside a `test.describe`). |
| `NotSupportedError` | `Error` | An operation is restricted by the host (embedded-mode capability denial, `elevate` denied) or unsupported on the platform. |
| `AssertionError` | `Error` | An `assert.*` built-in's predicate failed. Exposes `.expected` and `.actual`. |

Additional registered error types exist in the standard library, the CLI, and the hosting
SDK. They obey the same contract (see *Error Type Contract* below) and are catalogued in
the [Standard Library Reference](Stash%20%E2%80%94%20Standard%20Library%20Reference.md).
The catalog there is *derived*, not normative: this section is the law for what catchable
runtime types exist; the reference is the law for their per-type payload.

### System Faults

Two registered error types sit **outside** the `Error` subtree:

- **`CancellationError`** — raised when a task is cancelled cooperatively (see §Async).
- The propagation form of `process.exit(code)` — represented internally as a runtime
  exception, not as a Stash-catchable value.

Neither is matched by `catch (Error e)` or by untyped `catch (e)`. `CancellationError` is
catchable only by its **exact name**: `catch (CancellationError e)`. The exit propagation
is not catchable by any Stash `catch` clause; it always propagates to the top level (where
`defer` blocks still run; see §Defer).

This split prevents a routine `catch (e) { /* swallow */ }` from accidentally absorbing a
user's Ctrl-C or a script's deliberate `process.exit`. A handler that genuinely needs to
intercept either fault must name it explicitly.

### Error Type Contract

Every Stash-catchable runtime error type — built-in, standard-library, CLI, host — obeys
the following normative contract:

- The type has a single, stable, **registered** name (the `.type` string surfaced to user
  code). The name ends in **`Error`** (e.g. `IOError`, `CommandError`, `ParseError`).
- The type is registered with the runtime such that:
  - `BuiltInErrorRegistry.NameOf(<instance>)` returns the registered name.
  - `catch (<Name> e)` matches instances of the type, via either the CLR fast-path or the
    name-based fallback.
- The type derives, directly or transitively, from `Error` (except the System Faults
  named above).
- The type documents its typed properties (their names and Stash-facing types) and a
  description of when it is raised. These appear in the
  [Standard Library Reference](Stash%20%E2%80%94%20Standard%20Library%20Reference.md).
- The type's name and parent are stable: adding or removing a type, renaming a type, or
  re-parenting a type is a breaking change to this section.
- Every instance of the type exposes the five always-present members `.type`, `.message`,
  `.stack`, `.suppressed`, and `.cause` (with `.cause` defaulting to `null`) in addition
  to any typed properties the type declares. No registered type may shadow these names
  with a property of the same name.

A new core type added to the table in *Built-in Error Types* is a breaking change to this
section and must land alongside a normative clause naming the situation it owns.

### Throw

`throw expr;` raises `expr` as a Stash error.

- If `expr` is a value of a registered error type (e.g. `TypeError { message: "..." }`),
  the value is raised with its registered type intact.
- If `expr` is a struct instance whose type is **not** a registered error type, the value
  is raised as a user-defined error whose `.type` is the struct's type name.
- If `expr` is a dict literal with a `type` field, the value is raised as a user-defined
  error whose `.type` is the dict's `type` field (a `string`). Other dict fields populate
  the error's typed properties. A dict literal *without* a `type` field is raised as an
  `Error` whose `.message` is the dict's `message` field or the stringified dict.
- If `expr` is a string, number, or any other non-error value, the value's stringification
  becomes an `Error`'s message and the `Error` is raised. `(throw "boom").type == "Error"`.

`throw;` inside a `catch` clause **rethrows** the current error, preserving the original
type, message, span, stack, `.suppressed` list, and `.cause`.

`throw;` outside a `catch` clause raises an `Error` with the message `"Rethrow used
outside of a catch block."`.

### Error Chaining

Any throw expression may set the new error's `.cause` field by naming `cause` in the
struct or dict literal:

```stash
try {
    parseConfig(text);
} catch (e) {
    throw IOError { message: "config load failed", cause: e };
}
```

The caught error becomes `.cause` on the new error. A reader can walk the chain:

```stash
try { loadApp(); } catch (e) {
    io.eprintln(e.message);
    let c = e.cause;
    while (c != null) {
        io.eprintln("  caused by: " + c.message);
        c = c.cause;
    }
}
```

Normative behavior of `.cause`:

- The value of `cause` in the throw expression must be either an `Error` value or
  `null`. Any other value raises `TypeError` at the throw site (the throw itself
  fails before the new error is raised).
- The chain may be of any depth and may include any registered error type. The
  runtime does **not** detect or break cycles in the chain; a chain that loops back
  to itself is a programming error and observable by a chain walker (a defensive
  walker should bound its iteration count).
- `.cause` defaults to `null` and is never set by the runtime automatically:
  there is no implicit "this error was raised while handling another" capture.
  All chaining is explicit.
- Setting `cause` to a non-`Error` value via a dict throw — e.g.
  `throw { type: "MyError", cause: 42 }` — raises `TypeError` at the throw site
  with the message `"'cause' must be an Error value or null, got <typeof>"`.

### Try Expressions

`try expr` evaluates `expr`. If evaluation succeeds, the expression evaluates to the
result. If evaluation raises an error that is matched by `Error` (i.e. any error except the
*System Faults* above), the expression evaluates to the caught error value instead of
propagating. A System Fault propagates through `try expr` unchanged.

```stash
let result = try fs.readFile("missing.txt");
if (result is Error) {
    io.eprintln(result.message);
}
```

### Try / Catch / Finally Statements

`try` statements handle thrown errors.

```stash
try {
    deploy();
} catch (CommandError e) {
    io.eprintln(e.stderr);
} catch (e) {
    io.eprintln(e.message);
} finally {
    cleanup();
}
```

Catch clauses are tested in source order:

- A **typed** catch `catch (T e)` matches an error whose registered type is `T` or a
  registered subtype of `T`. The subtype relation is the parent chain declared in the
  *Built-in Error Types* table (and by any contract-layer `[StashError]` registration).
- An **untyped** catch `catch (e)` matches any error whose registered type derives from
  `Error` — that is, any error except a System Fault. To trap a System Fault, name it
  explicitly: `catch (CancellationError e)`.
- A catch clause naming an **unregistered type** — including a typo such as
  `catch (TpyeError e)` — never matches and is unreachable; control falls through to the
  next clause or out of the `try`. (The runtime rule is silent-no-match; the static
  analyzer additionally emits a warning, **SA0170**, when the named type is *statically
  provable* to be neither a registered built-in error type nor a struct/error type
  declared in scope. The warning is a tooling aid; the runtime semantics it describes
  are unchanged. A name whose binding is only knowable at runtime — for instance, the
  `type` field of a `throw { type: "..." }` user error whose value is computed
  dynamically — is not flagged.)

The `finally` block executes whenever control leaves the `try` statement — on normal
completion, on a caught error, on an uncaught error (before the error continues to
propagate), and on `return` / `break` / `continue` exiting the protected block.

### Retry Expressions

(...existing prose at L2605-L2622 unchanged in shape...)

If all attempts fail, evaluation produces the last error, with its original type and
message preserved.

### Timeout Expressions

`timeout duration { block }` bounds execution time. If the body completes before the
duration, the expression evaluates to the body's value. If time expires, evaluation raises
a `TimeoutError`. (`TimeoutError` derives from `Error`; the deadline cancellation does
**not** surface as `CancellationError` — the two are deliberately distinct: a `timeout`
that elapses raises `TimeoutError` synchronously inside the protected block.)

### Defer and Suppressed Errors

`defer` registers cleanup code to run when the current function scope exits.

```stash
fn useFile(path) {
    let handle = fs.open(path);
    defer handle.close();
    return handle.readAll();
}
```

Deferred actions run in last-in, first-out (LIFO) order. Deferred actions run on normal
return and on error unwinding — including on `CancellationError`, `exit`, and any
uncaught error.

If a deferred action **itself raises an error** while a *different* error is propagating:

- The originally-propagating error continues to propagate (it is not replaced).
- The deferred action's error is appended to the propagating error's `.suppressed` array.
  Multiple suppressed errors appear in the order they were raised (first-suppressed first).
- Subsequent deferred actions on the stack still run; each that raises adds to the same
  `.suppressed` list.

If a deferred action raises an error while *no* error is propagating, that error becomes
the primary error and propagates as if `throw`n at the deferred action's call site.

### Uncaught Errors

An error that propagates past the top-level frame is *uncaught*. The CLI runtime:

- prints one line to stderr of the form `<type>: <message>`;
- prints the captured stack on subsequent lines (one frame per line, indented two spaces,
  each frame as `at <function> (<file>:<line>:<column>)`);
- prints any `.suppressed` errors under the heading `suppressed errors:` (each formatted
  the same way, indented two spaces further);
- terminates the process with exit code **1** for any error derived from `Error`;
- terminates the process with exit code **130** for `CancellationError` (SIGINT
  convention), printing only `cancelled` to stderr with no stack;
- terminates the process with the requested exit code for the exit propagation, printing
  nothing.

An embedding host (see §Runtime Behavior -> *Embedded Mode and Side Effects*) does not
inherit these CLI defaults; it receives the error through its own host surface and
chooses its own report and exit behavior.
```

#### §Bindings & Scope — amendments (sealed status preserved)

- **L905** replace `"Assignment to an undeclared name produces a runtime error."`
  with `"Assignment to an undeclared name raises `NameError`."`.
- **L905** insert "Reading an undefined variable raises `NameError`."
- **L915** replace `"Assigning to a `const` binding produces a runtime error."`
  with `"Assigning to a `const` binding raises `ConstAssignError`."` (a new core type
  distinct from `ReadOnlyError`; the latter remains scoped to frozen-value /
  readonly-namespace-member mutation).

#### §Expressions — amendments

Each unnamed clause names the type per the table above. Verbatim replacements:

- **L1198**: `"produces a runtime error"` (indexing non-indexable) -> `"raises `TypeError`"`.
- **L1280**: `"produces a runtime error"` (calling non-callable) -> `"raises `TypeError`"`.
- **L1313**: `"produces a runtime error"` (invalid operand types) -> `"raises `TypeError`"`.
- **L1360**: `"runtime error"` (index out of bounds) -> `"`IndexError`"`.
- **L1379**: `"produces a runtime error"` (`in` unsupported right operand) ->
  `"raises `TypeError`"`.
- **L1392**: `"produces a runtime error"` (unknown type names) -> `"raises `NameError`"`.
- **L1421**: `"runtime error"` (`++`/`--` on non-numeric) -> `"`TypeError`"`.
- **L1437**: `"produces a runtime error"` (missing field) -> `"raises `FieldError`"`.

#### §Statements & Control Flow — amendments

- **L1514**: `"produces a runtime error"` (`for in` non-iterable) -> `"raises `TypeError`"`.
- **L1525**: `"produces a runtime error"` (`return` outside fn) -> `"raises `StateError`"`.
- **L1563**: `"produces a runtime error"` (lock failure) -> `"raises `LockError`"`.
- **L1582**: `"produces a runtime error"` (elevation denied) -> `"raises `NotSupportedError`"`.

#### §Aggregate Types & Members — amendments

- **L2149**: `"Missing required fields or unknown fields produce a runtime error"` ->
  `"Missing required fields or unknown fields raise `FieldError`"`. (The
  `coverage.md`-flagged contradiction at `TypeOps.cs:347` is resolved by making the check
  real; this is a Forced Decision below.)

#### §Source Files & Modules — amendments

- **L320**: `"Import cycles produce a runtime error"` -> `"Import cycles raise `ImportCycleError`."`
- Add a new normative paragraph after the import-cycle clause: `"Resolving an import path
  to no module raises `ModuleNotFoundError`. Importing a name not exported by the
  resolved module raises `ExportError`. Importing a package with no entry point raises
  `ExportError`. An import path that is not a string raises `TypeError`."`

#### §Shell Integration — amendments

- **L2432-2438**: replace the unnamed runtime-error prose with: `"A strict command failure
  raises `CommandError`. The error exposes `command: string`, `exitCode: int`,
  `stdout: string`, and `stderr: string`."`

#### §Values & Types — sealed-clause amendments (cross-unit)

- **L793**: `RuntimeError("Division by zero.")` -> `DivisionByZeroError("Division by zero.")`.
- **L824**: `"every operand mismatch raises a `RuntimeError`"` ->
  `"every operand mismatch raises a `TypeError`"`.
- **L844**: `"raises a `RuntimeError`"` (reveal-non-secret) -> `"raises a `TypeError`"`.

#### §Functions, Closures, and Async — sealed-clause amendments (cross-unit)

- **L1708**: `"wraps to a generic `RuntimeError` with the message ..."` ->
  `"wraps to a generic `Error` with the message ..."`.
- **L1710**: `"`throw \"string\"` inside a task wraps to `RuntimeError(\"string\")`"` ->
  `"`throw \"string\"` inside a task wraps to `Error(\"string\")`"`.

### Implementation Path

Parser/lexer: unchanged. Analyzer/SemanticResolver: unchanged (the catch-typename-validation
opt-in is deferred). Source generator (`StashErrorRegistryGenerator`): extended once to
emit the **parent map** and to validate naming/parent/cycle invariants at compile time.
Catch dispatcher (`ErrorTypeRegistry.Matches` + `ExecuteCatchMatch`): subtype-walk added;
empty-`typeNames` fast path gated on the `Error` subtree. Throw dispatcher (`ExecuteThrow`):
`new RuntimeError(...)` -> `new Error(...)` for string/non-error/dict-no-type paths. Every
core throw site retypes to a named type from the table above. Every stdlib/CLI/host throw
site retypes to a contract-compliant named type. CLI top-level handler:
`Stash.Cli/Program.cs` (or equivalent) implements the spec's uncaught-error output rules.

The end-to-end shape that must stay intact across phases:

`Catch dispatcher gains subtype walk + system-fault split` -> `Throw dispatcher routes
string/non-error/dict-no-type to the new Error concrete root` -> `bucketed core retypes
land bucket-by-bucket through the chokepoint` -> `bucketed stdlib/CLI/host retypes land
through the same chokepoint` -> `the §Errors rewrite + cross-section + sealed-clause spec
edits land alongside the behavior they describe` -> `the bare-base RuntimeError C# class
is made abstract` -> `CLI top-level handler implements the spec'd uncaught output`.

### Cross-Cutting Concerns

The shared concern: **every catchable runtime failure routes through a named, registered,
spec'd `[StashError]` type; a future core failure must not silently throw the bare base
again.** The prevention is staged Detect-then-Construct, with the Detect guard built in
phase 1 so every later bucket-phase migrates through it.

| Concern | Single source of truth | Omission prevented by |
| --- | --- | --- |
| Every core throw site routes through a named, registered, spec-enumerated `[StashError]` type (no bare-`RuntimeError` in `Stash.Bytecode/**` or `Stash.Core/**`, and zero bare-base in any project after the final phase). | `Stash.Tests/Conformance/Errors/BareBaseErrorChokepointMetaTests.cs` — Roslyn sink-scan flagging any `throw new RuntimeError(` outside an explicit exemption list. **In P-final**, the bare base `RuntimeError` is itself made `abstract` (Construct upgrade) so a new `new RuntimeError(...)` site fails to compile. | **Detect (P1) -> Construct (P-final).** Detect goes up GREEN with a P1 exemption list naming every not-yet-migrated bucket; each later phase removes its bucket's entries. The list never goes empty until the final phase, when it shrinks to the **single sanctioned use** (the `Error` concrete root's own `: RuntimeError` derivation) and the abstract flip lands. Meta-test pattern copies `EqualityChokepointMetaTests` exactly: `TRUSTED_PLATFORM_ASSEMBLIES`-based MetadataReferences (CLAUDE.md Roslyn-determinism rule); `MinScannedFiles` floor; **binding-floor** asserting `Stash.Runtime.Errors.Error` resolves to a non-error `INamedTypeSymbol`; fail-path teeth self-test. |
| Every catchable type is registered AND derives from `RuntimeError`. A new C# subclass of `RuntimeError` without `[StashError]` (or without a `*Error` suffix) is a contract violation. | `Stash.Tests/Conformance/Errors/ErrorTypeContractConformanceTests.cs` — reflection scan: every `class X : RuntimeError` outside the abstract bases (`RuntimeError`, `UserRuntimeError`) carries `[StashError]`; the name ends in `Error`; the name in `[StashError(Name=...)]` (if overridden) ends in `Error`; the type appears in `BuiltInErrorRegistry`. | **Detect.** Goes up green at first introduction in P1 (catches the unregistered `AssertionError` immediately — it must be registered in P1 or land on the exemption list with a removal phase). Fail-path teeth + bound floor. |
| Every `catch (Error e)` / untyped `catch (e)` must trap an `Error` and NOT trap a System Fault. | `Stash.Tests/Conformance/Errors/SystemFaultCatchModelConformanceTests.cs` — conformance tests assert (a) `catch (Error e)` and `catch (e)` trap a `TypeError`, `NameError`, etc.; (b) `catch (Error e)` and `catch (e)` do NOT trap a `CancellationError`; (c) explicit `catch (CancellationError e)` traps it. Plus a meta-test enumerating every registered type and asserting each is *either* under `Error` *or* in the (closed, hard-coded) `SystemFaults` list. | **Construct + Detect.** Source-generator validation in `StashErrorRegistryGenerator` ensures every registered type's parent chain terminates in either `Error` or the `SystemFaults` registered root — a third root fails to generate (compile error). Conformance meta-test backs it for run-time visibility. The closed `SystemFaults` list is a single named constant (no string literals at use sites — `NoMagicAuthStringsMetaTests` precedent). |
| Every spec edit lands with the behavior it describes. | The per-phase `done_when` lines that name the literal spec phrase added/changed. | **Instruct + verify.** Each phase's `done_when` pins the spec edit at the prose level (distinctive phrase that grep can find). |
| Cross-unit sealed-test moves are deliberate (no two tests asserting opposite things). | The Decision Log entry that names each moved test, plus each phase's `done_when` listing the moved tests by file:method. | **Instruct + Decision Log audit.** Reviewer Priority-2 finding if any sealed unit has a duplicated-and-contradicting conformance test post-merge. |

The Construct upgrade (abstract-flip) is the **culminating** move, not the guard. The Detect
meta-test is the guard, built in P1 and shrinking through every later phase. State this
explicitly so a reviewer doesn't misread the deferred Construct as a deferred guard.

## Acceptance Criteria

- End-to-end: a script that throws a string (`throw "boom"`), catches with `catch (Error e)`,
  reads `e.type` -> `"Error"`. The same script catches with `catch (e)` (untyped) and
  succeeds identically.
- End-to-end: a script that accesses an undefined variable (`io.println(undefined_name);`)
  catches with `catch (NameError e)` and recovers; the same script with the catch removed
  prints `NameError: Undefined variable 'undefined_name'.` to stderr followed by a stack
  trace and exits with code 1.
- End-to-end (system-fault split): `try { await task.run(async fn() { time.sleep(60); }); }
  catch (e) { io.println("trapped"); }` with `task.cancel(future)` called from the main
  task — the cancellation propagates through `catch (e)` (NOT trapped). Adding
  `catch (CancellationError e) { io.println("cancelled"); }` traps it explicitly.
- End-to-end (`.suppressed`): `try { defer { throw "cleanup-boom"; }; throw "primary"; }
  catch (e) { io.println(e.message); io.println(arr.len(e.suppressed)); io.println(e.suppressed[0].message); }`
  prints `primary`, `1`, `cleanup-boom`.
- End-to-end: division-by-zero (`let x = 1 / 0;`) catches with `catch (DivisionByZeroError e)`
  and also with `catch (ArithmeticError e)` (the grouping parent) and also with
  `catch (Error e)`.
- Operator type-mismatch: `try { let x = 1 + "a"; } catch (TypeError e) { io.println(e.type); }`
  prints `TypeError`. (Today: bare `RuntimeError`.)
- `reveal` on non-secret: `try { reveal("not-a-secret"); } catch (TypeError e) { io.println("ok"); }`
  prints `ok`. (Today: bare `RuntimeError`; cross-unit amendment to §Values L844.)
- `AssertionError` is registered and surfaces correctly: `try { assert.equal(1, 2); }
  catch (AssertionError e) { io.println(e.expected); io.println(e.actual); }` prints `1`
  then `2`.
- Uncaught error CLI behavior: a script `throw "boom";` exits with code 1 and prints
  `Error: boom` to stderr followed by a stack trace.
- Uncaught cancellation: a script cancelled via Ctrl-C prints `cancelled` to stderr and
  exits with code 130.
- Import: `import "./missing.stash";` raises `ModuleNotFoundError` (catchable by
  `ImportError`).
- Cross-entrypoint: the host SDK (`Stash.Hosting`) receives the same `.type` strings via
  `Stash.Hosting.StashError.Kind` — the host integration tests for error reporting still pass.
- Every section the ledger §3 names has its "produces a runtime error" prose replaced
  with a named type (verifiable by a `grep -n "produces a runtime error" docs/Stash —
  Language Specification.md` that returns no lines naming a runtime-failure outside
  generic Conformance/Terminology definitions).
- The Detect meta-test (`BareBaseErrorChokepointMetaTests`) ends the unit with its
  exemption list pinned to the sanctioned single use (the `Error` class's own
  `: RuntimeError` derivation in C#).
- The Construct upgrade (`RuntimeError` becomes `abstract`) lands; `new RuntimeError(...)`
  in C# fails to compile.
- Every cross-unit sealed-test that moves is moved (not duplicated); a `grep -rn
  "ThrowsRuntimeError\|ThrowsAny<RuntimeError>" Stash.Tests/Conformance/` returns only
  the sites whose behavior is *spec'd* to remain on the abstract base (none — every site
  retypes).
- Full `dotnet test` green; `dotnet test --filter "Category=Conformance"` green and binds
  non-zero tests across `Async/`, `Values/`, `Equality/`, AND `Errors/`.
- End-to-end (`.cause` chaining, D22): `try { try { throw IOError { message: "inner" }; }
  catch (e) { throw ValueError { message: "outer", cause: e }; } } catch (e) {
  io.println(e.message); io.println(e.cause.type); io.println(e.cause.message); }`
  prints `outer`, `IOError`, `inner`.
- End-to-end (`.cause` defaults to null): `try { throw "boom"; } catch (e) {
  io.println(e.cause == null); }` prints `true`.
- End-to-end (`.cause` type-check): `try { throw ValueError { message: "x", cause: 42 }; }
  catch (TypeError e) { io.println("ok"); }` prints `ok` (the throw itself fails before
  the new error is raised, because `cause` must be `Error` or `null`).
- End-to-end (catch-name validation, D23, SA0170): `stash-check` over a fixture containing
  `try { ... } catch (TpyeError e) { ... }` emits an `SA0170` warning naming `TpyeError`;
  the same fixture with `catch (TypeError e)` emits no warning; the same fixture with
  `catch (MyUserError e)` where `MyUserError` is only known via dynamic dict-throws emits
  no warning.

## Phases

The phase list lives in `plan.yaml`. Bucket boundaries mirror the ledger §2b failure
counts: phase 1 = chokepoint + guard; phases 2-9 = bucketed retypes (each phase migrates
one bucket of related throw sites through the chokepoint, shrinks the exemption list,
and lands its slice of the spec amendments + conformance tests); phases 10-11 = the new
explicit `.cause` chaining surface (D22) — payload/raise mechanics + conformance, then
spec prose touches; phase 12 = the new `SA0170` catch-name validation (D23) — analyzer
rule + tests + spec note; phase 13 = the §Errors section rewrite (now including
`.cause` and the `SA0170` reconciliation) + uncaught-at-top-level CLI implementation +
final Construct upgrade (abstract-flip on `RuntimeError`) + tooling sweep + coverage
roll-up. Net: **13 phases** (was 10 before the D22/D23 pull-ins).

## Open Questions

These items remain DEFERRED to a future unit. The user-pulled-in opt-ins (explicit
`.cause` chaining and `SA0170` catch-name validation) are now in scope and tracked in
the Decision Log / phase plan.

1. **Implicit error-context capture (Python `__context__` / Ruby auto-`cause`).** The
   runtime automatically setting `.cause` to the in-flight caught error when a `throw`
   runs inside a `catch` (in addition to the explicit field-form already in scope). Cost:
   runtime state for "currently-handling error"; a display-suppression escape hatch
   (Python `raise X from None`); ~1 phase. **Deferred to a future unit** so the explicit
   `.cause` mechanism settles first.
2. **Dual-accessor / sentinel channel for current always-raising operations.** E.g.
   `arr.fetch(i)` raises vs `arr[i]` returns `null` for OOB (Ruby/Elixir model, research
   §4.G/iv). Cost: net-new stdlib functions per channel, no language change, ~1 phase per
   pair. Deferred.
3. **`RetryExhaustedError` wrapper.** Wrap the last error in a typed wrapper on `retry`
   exhaustion. Cost: 1 new type, behavior change (callers catch the wrapper not the
   inner type), ~1 phase. **Today's behavior is already spec-aligned (D13);** this is a
   forward design question only.
4. **`Cli*` naming sweep direction (forced contradiction, see Decision Log).** The L1
   contract clause requires `*Error` suffix; 7 of 9 `Cli*` types violate it
   (`CliAmbiguousOption`, `CliInvalidValue`, `CliMissingRequired`, `CliMissingValue`,
   `CliUnexpectedPositional`, `CliUnknownCommand`, `CliUnknownOption` lack the suffix).
   **User confirmed (D17, 2026-06-07): rename in-unit (Make It Right).** Tracked as a P9
   work item; this Open Question entry is closed.

## Decision Log

| Date | Decision | Rationale |
| --- | --- | --- |
| 2026-06-07 | **L1 (user-locked): Core + contract.** §Errors normatively defines the root, the catch model, and the core types only. Stdlib/CLI/host types stay in the generated reference per the contract clause. | User direction. Avoids a ~25-row catalog in the language spec while keeping the spec the law for the *contract* every library error type must obey. |
| 2026-06-07 | **L2 (user-locked): Flat + a few groups.** Default flat; introduce a grouping parent only where a real `catch <group>` handler exists. | User direction. `ImportError` and `ArithmeticError` clear the bar (the import family has 4 leaves a handler would naturally trap together; `ArithmeticError` parents `DivisionByZeroError` + makes future arithmetic types catch-supertype-able). `LookupError` does NOT clear the bar — the hard raise-vs-sentinel boundary means dict-key-missing-on-read returns `null` (no `KeyError` leaf to group with `IndexError`). |
| 2026-06-07 | **L3 (user-locked): Full retype now.** All 351 bare-base sites get a named type in this unit. | User direction. The cross-cutting concern is whole-codebase; a partial retype leaves a porous edge that re-grows. |
| 2026-06-07 | **L4 (user-locked): System faults outside `Error`.** `CancellationError` and `exit` propagate through `catch (Error e)` / `catch (e)` unimpeded. | User direction. Python `Exception`-under-`BaseException` / Ruby `StandardError`-vs-`Exception` precedent prevents accidental cancellation/exit swallowing. |
| 2026-06-07 | **D1 (forced): Root name = `Error`.** Concrete, registered. Surfaces as `.type == "Error"`. The abstract C# base keeps the name `RuntimeError` (internal-only). | Resolves §Values L600 + §Errors L2580 (`is Error`, `catch (e)`) consistently with L3. Forced by L1 + L3 + L4 jointly: with system faults outside the catch-all and bare-base sites all retyped, the only role the existing `Error` *string sentinel* still plays is as the catch-all; promoting it to a real concrete registered type closes the gap with one named entity. |
| 2026-06-07 | **D2 (forced): Bare-`RuntimeError` C# class -> abstract.** Lands in the FINAL phase (the Construct upgrade), once the Detect exemption list has shrunk to the single sanctioned use (the `Error` class's own `: RuntimeError`). | The Construct upgrade is the strongest possible prevention (compile error); deferring it until the migration finishes avoids cascading build breaks during bucket phases. Detect guard built in P1 covers the interim. |
| 2026-06-07 | **D3 (user override): `const`-assign -> `ConstAssignError` (new dedicated core type).** Distinct from `ReadOnlyError`, which stays scoped to frozen-value / readonly-namespace-member mutation. `catch (ConstAssignError e)` and `catch (ReadOnlyError e)` are separately catchable; a handler that wants both writes both clauses or `catch (Error e)`. | User override of the prior architect default. Keeps binding-readonly distinct from value-readonly — the two situations share an English label ("read-only") but are operationally different (binding identity vs value identity / namespace membership). Users who genuinely want to handle both can; users who want to react only to a `const`-binding programming error get a precise type without a false-positive on frozen-value writes. |
| 2026-06-07 | **D4 (forced): Operand type-mismatch -> `TypeError`** (cross-unit §Values L824/L844 amendment). | Aligns the VM operand-mismatch path with the stdlib argument-mismatch path (~62 sites). Same logical failure class, same type. `Conformance/Values/CoercionConformanceTests.cs` L302/L319-region tests update from `ThrowsAny<RuntimeError>` to `ThrowsAny<TypeError>` in-place; their XML doc comments update; one `.type` assertion (`Reveal_NonSecret_ThrowsRuntimeError_PerSpecValuesSecret` in `Conformance/Values/SecretConformanceTests.cs:341,351`) renames + retypes. |
| 2026-06-07 | **D5 (forced): Division-by-zero -> `DivisionByZeroError` under `ArithmeticError`** (cross-unit §Values L793 amendment). | Names the situation; the grouping parent unlocks `catch (ArithmeticError e)` for future numeric faults. The §Values L793 spec edit is name-level (the existing prose stays; only the type name changes). |
| 2026-06-07 | **D6 (forced): `IndexError` for VM index-out-of-bounds.** Resolves ledger D4. | The VM and stdlib `str.substring` now agree (~32 + 6 sites unified). `buf` OOB (`BufBuiltIns.cs:183`) migrates with them. Non-integer index -> `TypeError`. |
| 2026-06-07 | **D7 (forced): Undefined variable -> `NameError` (new core type).** | The Python/Ruby precedent matches user expectation; the existing bare-`RuntimeError` site is unique enough to deserve a dedicated type. |
| 2026-06-07 | **D8 (forced): Missing-field / field-on-non-struct -> `FieldError` (new core type).** | Distinguishes "this struct lacks the field" from "this isn't a struct" with one type the user catches once. The `coverage.md`-flagged false clause at §Aggregate L2149 ("missing required fields produce a runtime error") is resolved by **making the check real** — the V1 retype of `TypeOps.cs:347` lands the missing-fields check at the same time it raises `FieldError`. |
| 2026-06-07 | **D9 (forced): Import family -> `ImportError` parent + `ModuleNotFoundError`, `ImportCycleError`, `ExportError` leaves.** Non-string import path -> `TypeError` (per D4). | The four bucket-failures (~14 sites) form a natural `catch (ImportError e)` group: a module loader writes one handler to retry / fallback / report regardless of which import leaf failed. Earns the grouping parent. |
| 2026-06-07 | **D10 (forced): Call-non-callable, for-in-non-iterable, in-unsupported-operand -> `TypeError`.** | All "this operand isn't a callable thing / isn't an iterable thing / isn't an `in`-acceptable thing" — one type the user catches once. ~5-8 sites. |
| 2026-06-07 | **D11 (forced): Stdlib arity / argument-count guards (one of the largest stdlib slices, ~50+ sites) -> `TypeError`** (signature mismatch is a type-of-call mismatch). | Reuses an existing type rather than introducing a new `ArgumentError`. (`ArgumentError` would be a candidate if argument *value* domain checks were also under it — but those are `ValueError` today and stay that way; the arity guards collapse cleanly into `TypeError`.) |
| 2026-06-07 | **D12 (forced): Typed-array wrong-element -> `TypeError` (wrong type), byte-range -> `ValueError` (out-of-domain).** | Splits cleanly on the existing two types. Shift-count domain -> `ValueError`. |
| 2026-06-07 | **D13 (forced): `AssertionError` registered with `[StashError(Properties = ["expected","actual"], PropertyTypes = ["any","any"], Description = "Assertion failed.")]`.** Resolves ledger D6. | Already spec-named at §Equality L730; registration is name-preserving (no §Equality prose change). The C# fields `Expected`/`Actual` surface as Stash properties `.expected`/`.actual`. |
| 2026-06-07 | **D14 (forced): String-throw / non-error-throw / dict-no-type-throw -> `Error`** (cross-unit §Async L1708/L1710 amendment). | Resolves the phase-1 chokepoint hazard: `ExecuteThrow` :31 string-path and :70 fallback path construct `new Error(...)` (not bare base). The cross-unit conformance test at `Conformance/Async/FuturesCoreConformanceTests.cs:184` (`D7_ThrowString_WrapsToRuntimeError_PerSpecAsyncD7`) is renamed + the `Assert.Equal("RuntimeError", ...)` flips to `Assert.Equal("Error", ...)`. `Conformance/Async/CombinatorsConformanceTests.cs:150` (the C#-escape D7 mention) updates likewise. |
| 2026-06-07 | **D15 (forced): `LockError`, `NotSupportedError`, `StateError`, `IOError`, `TimeoutError`, `CommandError`, `ReadOnlyError`, `AliasError`, `ParseError`, `HostError` — spec-named at their respective sites.** | Each is already registered; the §Errors contract clause (L1) covers them; their use-site spec sections (§Statements, §Modules, §Shell, §Namespace, §Bindings) gain the type-name. |
| 2026-06-07 | **D16 (forced): Hierarchy depth = flat-with-two-groups.** Only `ArithmeticError` (parents `DivisionByZeroError`) and `ImportError` (parents `ModuleNotFoundError`, `ImportCycleError`, `ExportError`) clear the L2 bar in this unit. | `LookupError` does NOT (dict-key-read stays `null`). The arithmetic and import groups have concrete `catch <group>` handlers (a math worker retrying on any arithmetic fault; a module loader fallback). All other leaves stay direct children of `Error`. |
| 2026-06-07 | **D17 (forced): Cli\* family — rename in-unit (Make It Right).** Each of the 7 contract-violators renames to a `*Error` form: `CliAmbiguousOption -> CliAmbiguousOptionError`, `CliInvalidValue -> CliInvalidValueError`, `CliMissingRequired -> CliMissingRequiredError`, `CliMissingValue -> CliMissingValueError`, `CliUnexpectedPositional -> CliUnexpectedPositionalError`, `CliUnknownCommand -> CliUnknownCommandError`, `CliUnknownOption -> CliUnknownOptionError`. | The L1 contract clause requires `*Error`; not renaming would ship a contradicting catalog day-one. Registry is pre-release (no external consumers); the only consumer is the in-repo CLI; the rename is mechanical. Surfaced to user as Open Question 5 for confirmation; default is rename. |
| 2026-06-07 | **D18 (forced): `Stash.Hosting.HostError` — kept, contract-cited only.** | Already registered; only thrown inside `Stash.Hosting` (9 sites). §Errors L1 contract covers it; the generated reference catalogs it; no spec-section change needed. |
| 2026-06-07 | **D19 (forced): Uncaught-at-top-level prose — exit code 1 for `Error`, 130 for `CancellationError`, requested code for `exit`.** | 130 is the SIGINT convention. The exit-1 default matches every surveyed language. The `exit(code)` path preserves the existing CLI behavior. |
| 2026-06-07 | **D20 (forced): `.suppressed` semantics — primary error wins, deferred-error appended.** Resolves ledger D12. | The implementation already collects suppressed errors (`RuntimeError.SuppressedErrors`); this unit lifts it to law. Ordering: first-suppressed first in the `.suppressed` array (the order they were raised during unwinding). |
| 2026-06-07 | **D21 (RECONCILED — runtime "never matches" + SA0170 warning).** **Runtime semantics (normative, unchanged):** a catch clause naming an unregistered type silently never matches; control falls through. **Static tooling (NEW, pulled in from Open Question 2):** the analyzer emits warning **SA0170** when the name is *statically provable* to be neither a registered built-in error type (via `BuiltInErrorRegistry`) nor a struct/error type declared in the analyzer's scope. The warning is suppressed for names whose binding can only be known dynamically (e.g. a user-error `type` field computed at runtime), so a `catch (MyUserError e)` where `MyUserError` is the `.type` string of a `throw { type: "MyUserError", ... }` is not flagged unless `MyUserError` is also declared as something *else* statically. The two surfaces are consistent: the warning is a tooling aid over an unchanged runtime rule, not a behavior change. | User direction (Open Question 2 pulled in). The runtime keeps its forgiving semantics so dynamic-name throws remain catchable by string; the static surface adds the typo-detection most authors want. |
| 2026-06-07 | **Test-helper blast radius assessed.** `StashTestBase.RunExpectingError` uses `Assert.ThrowsAny<RuntimeError>` (base catches subclasses — safe). 66 *exact-type* `Assert.Throws<RuntimeError>` sites exist scattered across ~20 test files; each phase's bucket-retype updates the exact-type asserts the bucket invalidates (in-scope phase work, not a separate phase). | Verified by `grep -c 'Assert\.Throws<RuntimeError>'` (66) and `grep -c 'Assert\.ThrowsAny<RuntimeError>'` (46). |
| 2026-06-07 | **Cancellation-catch blast radius (the L4 parallel of the Flag-1 finding) assessed.** Today `CancellationError : RuntimeError` is `[StashError]`-registered; `catch (e)` and `catch (Error e)` BOTH trap it; existing tests rely on it (e.g. `Stash.Tests/Interpreting/Async/CancellationAndTimeout/CancellationTests.cs:100`, `Stash.Tests/Conformance/Async/CancellationConformanceTests.cs:353` both use `catch (e) { result = e.type; }` to inspect `"CancellationError"`). P1's gating of the empty-`typeNames` fast path on the `Error` subtree IS a semantic change to every untyped catch of a cancellation. Each such test MUST be updated in P1 to use `catch (CancellationError e)` instead — same in-scope treatment as the exact-type `Assert.Throws<RuntimeError>` sites. P1's "MINIMAL behavior change" notes are explicitly rewritten to name this break. | Surfaced as a blast-radius survey item to the user per the advisor finding; the count is ~10-15 tests touching cancellation+`catch (e)` (full enumeration in P1). |
| 2026-06-07 | **System Faults closed set negative-space verified.** `ScriptCancelledException` and `StepLimitExceededException` both derive from `System.Exception` (NOT `RuntimeError`), so they bypass the entire StashError catch machinery today (no change needed; they were never user-catchable via `catch (e)`). `ExitException` likewise derives from `System.Exception` (the brief over-claimed by implying P1 "establishes" exit-outside-Error; it documents existing behavior — only `CancellationError`'s position changes). The closed `SystemFaults` registered set this unit ships is exactly `{ CancellationError }` — the only `[StashError]`-registered type with `Parent = null`. The `SystemFaultCatchModel` meta-test asserts this set is exactly that single name + the `Error` chain root, no third option. | Verified by `grep 'class \w+\s*:\s*(RuntimeError\|System.Exception)' Stash.Core/` — `ExitException`, `ScriptCancelledException`, `StepLimitExceededException` are all `System.Exception`-derived. |
| 2026-06-07 | **`Error` C# name-collision pre-flight.** No file in the repo defines a `class Error` or `enum Error` (only `ErrorTypeRegistry` and the `BaseTypeName = "Error"` constant). Three `Stash.Stdlib/BuiltIns/*.cs` files `using Stash.Runtime.Errors;` — after the new `Error` class lands, an unqualified `Error` token in those files would bind to it. Pre-flight grep added to P1's done_when: confirm no surprising binding exists before the class lands. | Verified by `grep -rnE 'enum Error\b\|class Error\b' --include="*.cs"` returning zero collision matches. |
| 2026-06-07 | **D22 (NEW — user pull-in): explicit `.cause` error chaining, field-form, no new keyword.** `.cause` becomes a fifth always-present payload member on every caught error (`Error?`, defaults to `null`). Set only by the raising code via the dict / struct literal's `cause` field: `throw IOError { message: "...", cause: prev };`. Lifted to a first-class typed accessor on the error value (alongside `.type`/`.message`/`.stack`/`.suppressed`), not just a raw property. **Explicit-only — no implicit auto-chaining.** A non-`Error`, non-`null` value in the `cause` field raises `TypeError` at the throw site. The chain may be of any depth; the runtime does not detect or break cycles (a defensive walker should bound iteration). | User pull-in from Open Question 1. The field form keeps the syntax surface zero: `ExecuteThrow` already copies struct/dict fields into `Properties`; lifting `cause` to a typed first-class accessor is a payload-contract change, not a grammar change. A `caused_by` keyword would only be justified if the field form couldn't carry — it can. The explicit-only choice mirrors JS / PHP `getPrevious` / Rust `source` and avoids the display-suppression complexity Python's `__context__` introduces. |
| 2026-06-07 | **D23 (NEW — user pull-in): `SA0170` catch-name validation (analyzer, Warning severity).** New `SA0170` warns on `catch (Name e)` where `Name` is statically provable to be (a) NOT a member of `BuiltInErrorRegistry.ByName` AND (b) NOT a struct/error type declared in the analyzer's resolver scope. The rule uses Warning, not Error, because static knowledge is incomplete (dynamic user-error type names exist via `throw { type: <expr> }`). The rule does NOT fire when the catch-name *could* match a dynamically-named user error (i.e. when the name is otherwise unbound in the scope but plausibly the `.type` field value of a user dict-throw the analyzer cannot statically prove absent). Runtime behavior remains "silently never matches" (D21 unchanged). | User pull-in from Open Question 2. Most catch typos a user would actually write (`catch (TpyeError e)`) are caught by the static rule; legitimately dynamic user-error names (`throw { type: "MyError" }; ... catch (MyError e)`) remain valid. Runtime forgiveness preserved for the dynamic case. |
