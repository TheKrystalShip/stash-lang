# RFC: Stdlib Namespace Members — Read-only Data Access

> **Status:** Draft (revised)
> **Owner:** Cristian Moraru
> **Created:** 2026-05-22
> **Slug:** stdlib-namespace-members

## Summary

Today every piece of information exposed by a standard-library namespace is a function: `cli.argc()`, `cli.argv()`, `env.cwd()`, `env.user()`. For values that read inherent script/process identity (not observations of changing state), the empty `()` is noise. This RFC introduces a new authoring primitive — a **read-only namespace member** — declared with a new `[StashMember]` attribute on a getter method. The Stash surface becomes a bare property access: `cli.argc`, `cli.argv`, `env.cwd`, `env.user`.

Each entry in a namespace is **statically one of three kinds**: `Function`, `DataMember`, or `Constant`. These kinds are mutually exclusive at registration time. When the compiler sees `ns.x` against a known namespace, it resolves the kind at compile time and emits the appropriate path. Calling a `DataMember` (`ns.x(...)`) is a **compile-time error** — `SA08xx — 'x' is a value member, not a function`. There is no runtime detect-and-invoke, no AST sugar, no deprecation window: Stash is pre-1.0 and the v1 migration set has near-zero `()`-capture in the wild.

Read-only enforcement covers two receiver classes: built-in namespace members, and `const` re-exports from a user module imported with `import "x.stash" as ns`. Assignment is rejected statically (`SA0845`) on a known-namespace receiver; a `ReadOnlyError` runtime fallback covers dynamic-receiver cases.

The brief also pins an undocumented but load-bearing feature that this design depends on: **first-class function references**. `let f = io.println; f("hello")` works today and `typeof(io.println)` is `"function"`. The language spec gains a new subsection documenting it and a regression test guards it. With function references documented, the design is uniform: `ns.x` always means "load the value declared at this slot." For a `Function` slot, that value happens to be callable.

## Motivation

`cli.argv()` and friends look like behavioral operations but are really identity reads. The trailing `()` invites confusion ("Does it re-scan? Allocate? Block?"), bloats scripts, and creates avoidable inconsistency with `[StashConst]`, which already exposes static values (e.g. `math.PI`) as bare member access. `[StashConst]` snapshots once at namespace freeze; what's missing is a way to register a value that depends on `IInterpreterContext` — i.e. a value computed per access.

Doing nothing leaves a permanent split: structured static data (`math.PI`) is bare; structured runtime data (`cli.argv`) requires a parenthesized call. Closing that gap is a small, well-scoped ergonomics win that also unblocks doc-generated tables that list "namespace members" as a single concept.

## Goals

- Add a `[StashMember]` attribute that registers a zero-arg, context-bound getter as a Stash-visible member of its namespace, with `DeclarationKind = DataMember` recorded at registration time.
- Make `ns.member` resolve at compile time when `ns` statically denotes a known namespace, and emit bytecode that loads the current value (re-invoking the getter for `DataMember`).
- Reject `ns.member(...)` (call form against a `DataMember`) at compile time with `SA08xx — 'member' is a value member, not a function`.
- Document **first-class function references** in the Language Specification as a new top-level subsection with at least one worked example, including the rule that `ns.fn` (where `fn` is a `Function`-kind entry) yields a callable value and `ns.x` (where `x` is `DataMember`) yields the underlying value.
- Require every `[StashMember]` to carry a non-empty XML `<summary>`, optional `Throws`, optional `Capability`, optional `Deprecation`, and optional nullable `ReturnType` — symmetric with `[StashFn]`. Enforce the `<summary>` requirement at build time.
- Add `SA0845` and `ReadOnlyError` for assignment to a built-in namespace member or to a `const` re-export through a module alias.
- Migrate a principled v1 set of eight getters to members as a **hard break**: `cli.argc`, `cli.argv`, `env.cwd`, `env.home`, `env.user`, `env.hostname`, `env.os`, `env.arch`. Their `()` call sites become compile-time errors and must be rewritten.
- Cover the full project-mandated change checklist: spec, regenerated stdlib reference, LSP completion + hover + semantic tokens, DAP variable display, playground/VS Code tokenizer, static analysis, example script, xUnit tests, CHANGELOG `Breaking changes` entry.

## Non-Goals

- Making user-defined namespaces mutable in any new way.
- Writable members on stdlib namespaces. Members are read-only by design; there is no `[StashMember(Set = …)]`.
- Migrating **all** zero-arg stdlib functions. The v1 list is fixed (8 entries).
- Keeping `ns.member()` working post-migration. The migration is a hard break — no deprecation window, no `[StashDeprecated]` shim.
- Introducing AST sugar that rewrites `cli.argc()` → `cli.argc`. Source-level call form against a `DataMember` is `SA08xx`, period.
- Runtime polymorphic dispatch that detects-and-invokes a sentinel during `GetField`. The design replaces a sentinel approach with compile-time declaration-kind resolution.
- Cross-module `const` tracking beyond what `ModuleExports` already exposes. If existing infra does not record per-export const-ness, this RFC adds that channel but does not redesign module imports.

## Design

### Declaration kinds

Every entry in a `StashNamespace` carries a static `DeclarationKind`:

| Kind | Storage | `ns.x` yields | `ns.x()` allowed? | Notes |
| --- | --- | --- | --- | --- |
| `Function` | `BuiltInFunction` | the function reference (callable) | yes — normal call | First-class function reference; capture with `let f = ns.x;` |
| `DataMember` | `Func<IInterpreterContext, StashValue>` (the getter) + `Stability` tag | result of `getter(ctx)`; cached or re-invoked per `Stability` | **no — `SA08xx`** | Read-only, context-bound; `Stability.Cached` (default) or `Stability.Live` |
| `Constant` | `StashValue` (snapshot) | the stored value | only if value is itself callable (rare) | Existing `[StashConst]` behavior |

Kinds are determined at registration time by the source generator and are immutable on the frozen namespace. The generator raises a build-time error if a method is annotated with `[StashMember]` together with `[StashFn]` or `[StashConst]`.

#### Stability annotation

`[StashMember]` accepts a `Stability` parameter drawn from a new `Stability` enum:

```csharp
public enum Stability { Cached, Live }

[StashMember(Stability = Stability.Cached)]   // default — may be omitted
[StashMember(Stability = Stability.Live)]
```

Semantics — both flavors are still declaration-kind `DataMember`; only the runtime evaluation strategy differs:

- **`Cached` (default)**: getter invoked on first access; the result is stored in the namespace's frozen dict; subsequent accesses return the same reference. Identity is preserved across the process lifetime.
- **`Live`**: getter invoked on every access; no caching. Used when the underlying source can change during execution.

The source generator emits the stability tag into the registry entry alongside the getter delegate. The runtime read path in `StashNamespace.VMGetField` checks the tag: `Cached` → consult the per-member cache slot, populate-if-empty, return; `Live` → invoke the getter every time.

### Surface

#### Authoring (C# side)

```csharp
[StashNamespace]
public static partial class CliBuiltIns
{
    /// <summary>The raw script argv as supplied by the host.</summary>
    [StashMember(ReturnType = "array")]
    public static List<StashValue> Argv(IInterpreterContext ctx)
    {
        var result = new List<StashValue>();
        foreach (string s in ctx.ScriptArgs ?? Array.Empty<string>())
            result.Add(StashValue.FromObj(s));
        return result;
    }
}
```

Rules enforced by the source generator:

- Exactly one parameter, of type `IInterpreterContext`. Anything else is `STASH_GEN0xx`.
- C# method name with first character lowercased becomes the Stash name (`Argv` → `argv`).
- `[StashMember]` is mutually exclusive with both `[StashFn]` and `[StashConst]`. Violation is a build-time generator diagnostic.
- The generator emits a registry entry with `DeclarationKind = DataMember` alongside the getter delegate.

#### Stash side (user-visible)

```stash
io.println(cli.argc);              // 3
io.println(cli.argv);              // ["--verbose", "input.txt", "output.txt"]
io.println(env.cwd);               // "/home/heisen/project"

cli.argc()                         // SA08xx — 'argc' is a value member, not a function (cli.argc at file:line)
cli.argv = [];                     // SA0845 at analyze time; ReadOnlyError at runtime if dynamic
```

Function references are unchanged:

```stash
let p = io.println;                // first-class function reference
p("hello");                        // "hello"
typeof(io.println);                // "function"
```

### Function References (new spec content)

This RFC explicitly pins behavior that **already works at runtime today but is undocumented in the Language Specification**: `ns.fn` where `fn` is a `Function`-kind entry yields the `BuiltInFunction` value, which is itself callable. Empirically, `let f = io.println; f("hello")` runs and prints `hello`; `typeof(io.println)` is `"function"`. The current spec is silent on both. P7 introduces a dedicated top-level subsection titled **"Function References"** (linked from the ToC, not buried inside an existing paragraph) that documents the behavior with at least one worked example, and a regression test added in P7 freezes the contract.

```stash
let printer = io.println;
printer("hello");
let upper = str.upper;
io.println(upper("abc"));           // "ABC"
```

This is what makes the namespace-members design uniform with the rest of the language: `ns.x` always returns the value declared at the slot. The asymmetry users need to internalize is precisely:

- `Function`-kind entries yield a **callable** when accessed bare (a first-class function reference).
- `DataMember`-kind entries yield the **underlying value** computed by the registered getter — they are not callable.
- `Constant`-kind entries yield the stored snapshot.

The Function References spec subsection states this asymmetry explicitly so users do not learn it by experimentation.

### Cross-language framing (informative)

The spec sections introduced by P7 include a short reference paragraph framing the design for readers coming from other languages: **namespace members are modeled on JS ES module exports and C# properties**. The stability split mirrors the JS ES module precedent directly — `Cached` members behave like `const` exports from a JS ES module (identity is stable for the binding's lifetime), and `Live` members behave like `let` exports that the source module reassigns (re-evaluated each access, no identity guarantee). Like an ES module binding, bare access returns a value and assignment is a static error (SA0845 mirrors a `TypeError: Assignment to constant variable.` on module bindings). Like a C# property, what looks like a field access actually invokes a getter — hover surfaces the kind so the distinction is not hidden. Python (`sys.argv = []` silently succeeds) and Go (relies on convention) are explicitly **not** the model. This paragraph anchors the design as well-precedented rather than Stash-invented and shapes user expectations about read-only semantics.

### Side-effect contract for `DataMember` getters

The "Namespace Members" spec section pins the getter side-effect contract so users do not assume pure O(1) field semantics. The contract is exactly:

- **`Cached` members** return the same reference across accesses for the process lifetime — identity is stable, the getter runs at most once.
- **`Live` members** invoke the getter on every access; returned values may be distinct between accesses if the underlying host state changed.
- **All reference-typed returns are frozen at the boundary**, regardless of stability mode. The runtime applies the same `Freeze()` (or equivalent) call already used elsewhere in the stdlib for frozen-array / frozen-dict semantics, before returning the value to the caller. This applies to arrays, dicts, and any future reference type a getter might return. Primitives (int, string, bool, float) are unaffected — they are value types in Stash semantics. A consequence: `cli.argv[0] = "x"` raises a write-to-frozen error rather than silently mutating a throwaway or shared array.
- **Getters may throw** — the member's documented `Throws` list is the contract.

This sets correct expectations for `cli.argv` (cached, identity stable, frozen — assignment into it errors), for `env.cwd` (live, may return new values after `os.chdir`, may throw `IOError`), and for any future member added by the same mechanism.

### Documentation contract for `[StashMember]`

Every `[StashMember]`-annotated method MUST carry:

- **XML `<summary>`** (non-empty) — same convention as `[StashFn]`. This is the text rendered by LSP hover and the stdlib reference Markdown.
- Optional `Throws = typeof(SomeError)` or `Throws = new[] { typeof(A), typeof(B) }` — same shape as `[StashFn(Throws = …)]`. Surfaced by LSP hover, by the stdlib reference, and by static analysis: a `ns.member` read is treated as a potential throw site for the listed types (folds into the existing `throws`-tracking flow).
- Optional `Capability = StashCapabilities.X` — identical semantics to `[StashFn(Capability = …)]`. The source generator emits the same `if ((__caps & cap) == cap)` registration gate, and accessing a gated-out member from Stash raises the standard capability error (same type/path as a gated function call).
- Optional `ReturnType = "string?"` (nullable) — parsed by the same metadata pipeline as `[StashFn(ReturnType = …)]`. LSP hover renders `member<string?>`; type inference treats the access as potentially-null.
- Optional `[StashDeprecated(Replacement = "…")]` — composes with `[StashMember]` identically to its composition with `[StashFn]`. This becomes load-bearing once members evolve.

**Build-time enforcement.** A method annotated `[StashMember]` without a non-empty XML `<summary>` is a **build failure**. This mirrors how `[StashFn]` is treated (or, where the existing infra checks `[StashFn]` summaries via `StandardLibraryReferenceTests`, the analogous check is extended to cover members). Without this, members shipping without docs become a permanent paper cut — LSP hover would render empty bodies and the regenerated reference would have blank cells.

### Per-member manifest (v1)

Each of the 8 v1 members ships with a complete `<summary>` plus explicit `Stability`, `Throws`, and `Capability` decisions recorded in the migration phase:

| Member | Stability | Throws | Capability | Why |
| --- | --- | --- | --- | --- |
| `cli.argc` | `Cached` | (none) | (none) | Set at process start, never changes. |
| `cli.argv` | `Cached` | (none) | (none) | Set at process start, never changes. Frozen at boundary; mutation errors. |
| `env.home` | `Cached` | (none) | `Environment` | Process-lifetime constant. |
| `env.user` | `Cached` | (none) | `Environment` | Process-lifetime constant. |
| `env.hostname` | `Cached` | (none) | `Environment` | Process-lifetime constant. |
| `env.os` | `Cached` | (none) | (none) | Process-lifetime constant. |
| `env.arch` | `Cached` | (none) | (none) | Process-lifetime constant. |
| `env.cwd` | `Live` | `IOError` | `Environment` | Mutated by `os.chdir` and shell commands; must re-read each access. |

`Cached` is the default and may be omitted from the annotation. The capability column is the **target** annotation — P5 cross-checks against the existing `[StashFn]` declarations and preserves whatever the current gating is (if `env.os` / `env.arch` are presently ungated, they stay ungated). Discrepancies surfaced by the survey are recorded in the Decision Log.

### Semantics

#### Compile-time resolution (primary path)

When the parser produces `DotExpr(receiver, name)` and the compiler can statically resolve `receiver` to a known namespace (stdlib namespace, or user-module alias from `import "x.stash" as ns`), the compiler:

1. Looks up the `(name, DeclarationKind)` in the namespace's static registry.
2. **If reading (`DotExpr` outside `CallExpr` head):** emits a load that produces the declared value. For `DataMember`, this is a fused opcode that invokes the registered getter against the current `_vmContext` and pushes the result. For `Function` and `Constant`, it emits the existing field-load that returns the stored value.
3. **If calling (`DotExpr` is the callee of a `CallExpr`):**
   - `Function`: emit the existing fused `CallBuiltIn` opcode.
   - `DataMember`: emit `SA08xx` (compile-time error) — `'<name>' is a value member, not a function. Drop the parentheses.` The diagnostic carries `file:line` of the call site.
   - `Constant`: emit a normal load; the VM call will succeed if the value is callable and raise `TypeError` otherwise (no change to existing behavior).
4. **If assigning (`DotAssignExpr` against a known built-in namespace, or against a `const` export via alias):** emit `SA0845` (compile-time error) — `'<name>' is read-only`. Bytecode emission is suppressed; the analyzer signals fatal.

When `receiver` is dynamic (e.g. `let n = cli; n.argc`), the compiler cannot resolve statically. The emitted code uses the generic `GetField` / `SetField` path. The runtime `StashNamespace.VMGetField` consults the same declaration-kind table and, for `DataMember`, invokes the getter; a dynamic `SetField` against a built-in namespace raises `ReadOnlyError`. The dynamic path is the fallback, not the mechanism.

#### Runtime read path

`StashNamespace` stores each entry as `(StashValue payload, DeclarationKind kind)`. `VMGetField(name, ctx)` switches on `kind`:

- `Function`: return the stored `BuiltInFunction` value.
- `Constant`: return the stored snapshot.
- `DataMember`: invoke `((Func<IInterpreterContext, StashValue>)payload.AsObj())(ctx)` and return the result.

The GetFieldIC fast path is only valid when the slot's value is stable post-freeze. The IC must check `kind` on first observation and **force megamorphic (`state = 2`)** for `DataMember`, so subsequent reads re-traverse the slow path and re-invoke the getter. This is the same pattern already used elsewhere in the IC.

#### Read-only enforcement (assignment)

Two receiver classes are rejected:

**(a) Built-in namespace member or constant**

- **Static (`SA0845`)**: `Stash.Analysis/Visitors/SemanticValidator.cs:VisitDotAssignExpr`. When the receiver statically resolves to a built-in `StashNamespace`, regardless of the target entry's kind, emit `SA0845`.
- **Runtime (`ReadOnlyError`)**: A new top-level `[StashError]` subclass (NOT a subclass of `TypeError`). Thrown from `VMSetField` / `ExecuteSetField` when the receiver is a `StashNamespace` whose `IsBuiltIn` is true. This replaces the generic "Cannot set field" `TypeError` for this receiver class.

**(b) User-module `const` re-export via alias**

- `import "./mod.stash" as mod` exposes `mod.SOMETHING` for every `export const SOMETHING = …` in the imported file.
- **Static (`SA0845`)**: `SymbolCollector` already builds per-import scopes. Thread a `IsConst` bit through `ModuleExports` and the analyzer's module symbol records. `SemanticValidator.VisitDotAssignExpr` extends the check to "alias points at imported module **and** target export is const".
- **Runtime (`ReadOnlyError`)**: `ExecuteSetField` against a module-alias receiver consults the same const bit; raises `ReadOnlyError` if the export was const.

A new built-in error type `ReadOnlyError` is registered in `Stash.Runtime.Errors` with `[StashError]` metadata so it appears in the regenerated stdlib reference:

```csharp
[StashError(Description = "Thrown when an assignment targets a read-only namespace member, a const re-export, or a built-in namespace value.")]
public sealed class ReadOnlyError : RuntimeError { … }
```

### Surface — v1 Migration Set (hard break)

The members migrated for v1 satisfy the rule "**identifiers that read inherent script/process identity, not observations of changing state**". The migration is a **hard break**: each call site of the form `ns.x()` for an entry below becomes a compile-time `SA08xx` and must be rewritten by hand to `ns.x`. There is no transitional `[StashFn]` shim.

| Namespace | Member | Old call (becomes SA08xx) | New form | C# getter |
| --- | --- | --- | --- | --- |
| `cli` | `argc` | `cli.argc()` | `cli.argc` | `CliBuiltIns.Argc(IInterpreterContext)` |
| `cli` | `argv` | `cli.argv()` | `cli.argv` | `CliBuiltIns.Argv(IInterpreterContext)` |
| `env` | `cwd` | `env.cwd()` | `env.cwd` | `EnvBuiltIns.Cwd(IInterpreterContext)` |
| `env` | `home` | `env.home()` | `env.home` | `EnvBuiltIns.Home(IInterpreterContext)` |
| `env` | `user` | `env.user()` | `env.user` | `EnvBuiltIns.User(IInterpreterContext)` |
| `env` | `hostname` | `env.hostname()` | `env.hostname` | `EnvBuiltIns.Hostname(IInterpreterContext)` |
| `env` | `os` | `env.os()` | `env.os` | `EnvBuiltIns.Os(IInterpreterContext)` |
| `env` | `arch` | `env.arch()` | `env.arch` | `EnvBuiltIns.Arch(IInterpreterContext)` |

Out of scope for v1 (explicit exclusions):

- `time.now()`, `time.millis()`, `time.clock()` — observations of monotonically advancing clock state. Calls clearly imply "ask now."
- `term.cols()`, `term.rows()`, `term.isATerminal()` — observations of dynamic terminal state.
- `env.all()`, `env.timezones()` — return potentially-large collections; calls correctly hint at cost.

The migration phase audits and rewrites every example script, integration test, doctest, and bundled `.stash` artifact that calls one of these eight entries.

### Implementation Path

```
Generator: [StashMember] attribute + mutual-exclusion validation
  -> emits registry entries tagged DeclarationKind.DataMember
  -> NamespaceBuilder.Member(name, getter, returnType, doc) records DataMember kind
  -> StashNamespace stores (payload, kind) per entry; freeze preserves both
  -> VM read path (StashNamespace.VMGetField) switches on kind; DataMember invokes getter(ctx)
  -> GetFieldIC slot forced megamorphic on first DataMember observation
  -> Compiler.Expressions.cs: DotExpr against statically-known namespace resolves by kind
       Function (in CallExpr head): existing fused CallBuiltIn
       Function (bare): load BuiltInFunction value (function reference)
       DataMember (bare): emit GetMember-of-kind opcode (invoke getter on VM)
       DataMember (in CallExpr head): SA08xx compile-time error
       Constant: existing const-load
  -> SemanticValidator.VisitDotAssignExpr against built-in namespace or const-aliased member: SA0845
  -> ExecuteSetField fallback path: ReadOnlyError for the same receiver classes
  -> Migrate 8 v1 members; audit & rewrite all existing `ns.x()` call sites in repo
  -> Generator: build-time check rejects [StashMember] without XML <summary>
  -> Generator: propagates Throws / Capability / Deprecation / nullable ReturnType into the registry entry
  -> LSP CompletionHandler/HoverHandler distinguish member vs function (hover renders `member<array<string>>` plus summary + throws list)
  -> DAP DebugSession variable display auto-invokes member getters on namespace expansion (labeled distinctly from functions)
  -> Stash.Docs/ReferenceGenerator: separate "Members" subsection per namespace with summary + throws + capability columns
  -> Spec: new "Namespace Members" section + new "Function References" section + cross-language framing paragraph + side-effect contract paragraph, all linked from ToC
  -> Example: examples/namespace_members.stash demonstrates bare access, function-reference capture, and ReadOnlyError catch
  -> CHANGELOG: dedicated "Breaking changes" entry listing the 8 migrated members
```

## Acceptance Criteria

End-to-end (observable from a Stash script):

- A script that prints `cli.argc`, `cli.argv`, `env.cwd`, `env.home`, `env.user`, `env.hostname`, `env.os`, `env.arch` produces the values supplied by the host (argv, cwd, env-derived identity), with each access re-reading the host state.
- A `.stash` source that contains `cli.argc()` fails to compile with `SA08xx`; the diagnostic message names `argc`, calls it "a value member, not a function", and includes the source `file:line` of the call site.
- `cli.argv = []` is rejected at analyze time with `SA0845`.
- A dynamic-receiver assignment that reaches a built-in `StashNamespace` raises `ReadOnlyError` (not generic `TypeError`).
- For a user module `mod.stash` that contains `export const PI = 3.14;`, the script `import "./mod.stash" as mod; mod.PI = 0;` is rejected at analyze time with `SA0845` and at runtime (dynamic path) with `ReadOnlyError`.
- `let f = io.println; f("hello");` runs and prints `hello`. `typeof(io.println)` is `"function"`. A spec-pinned regression test enforces this.

Cross-entrypoint:

- LSP completion at `cli.` lists `argc` and `argv` as member items, distinguished from function items by completion kind and hover. Hover on `cli.argv` renders as `member<array<string>>` plus the XML `<summary>` text plus the documented `Throws` list (if any), not as `fn cli.argv() -> array`.
- The DAP variable view, when inspecting the `cli` namespace, shows `argc` and `argv` with their resolved current values, labeled distinctly from function entries.
- The regenerated `docs/Stash — Standard Library Reference.md` contains a "Members" section (separate from "Functions") under each namespace that has at least one member, with summary + throws + capability columns populated per member.
- The Language Specification has a new top-level "Namespace Members" section **and** a new top-level "Function References" section with at least one worked example, both linked from the Table of Contents.
- The Language Specification "Namespace Members" section contains both the cross-language framing paragraph (ES modules + C# properties) and the side-effect contract paragraph (getter may throw, allocations not preserved, identity not stable).
- `examples/namespace_members.stash` runs to completion under `dotnet run --project Stash.Cli/ -- examples/namespace_members.stash arg1 arg2 arg3`.
- `CHANGELOG.md` carries a `Breaking changes` heading whose entries list every one of the eight migrated members by their old `()` form and new bare form, and references the new SA08xx diagnostic code.

Documentation / build-time:

- Every `[StashMember]`-annotated method has a non-empty XML `<summary>`. A method annotated `[StashMember]` without `<summary>` fails the build (generator diagnostic or the analogous `StandardLibraryReferenceTests` check), symmetric to the existing `[StashFn]` enforcement.
- A `[StashMember(Capability = StashCapabilities.Environment)]` registered under a capability the embedder denied raises the standard capability error on access, identical to a gated `[StashFn]` call.

Final verification:

- `dotnet build` passes with zero warnings introduced by the feature.
- `dotnet test` passes — including new xUnit coverage for the generator mutual-exclusion check, the runtime resolution path, the compile-time `DotExpr` resolution, both `ReadOnlyError` paths, and the function-references regression.
- `python3 scripts/checkpoint/validate-spec.py stdlib-namespace-members` reports clean.

## Phases

See `plan.yaml`. Every phase's `done_when` describes user-observable behavior or a verifiable artifact, not a local mechanism.

## Open Questions

- **Getter exceptions.** May a `[StashMember]` getter throw? If yes, how does propagation through the GetFieldIC slow path interact with the existing IC contract? Recommended default: `RuntimeError` flows up, anything else is wrapped. Confirm in P2.
- **DAP rendering of getter members.** Auto-invoke on namespace expansion (consistent with showing the value) or display opaquely? Recommended: auto-invoke. Confirm in P5.
- **LSP completion item kind.** `Property` vs `Field`. Recommended: `Property` for `DataMember`, `Constant` for `[StashConst]`, `Function` for callables. Resolved in P5.
- **`SA08xx` ID for the call-form-against-member error.** Picked from the next free SA08xx slot during P3 and recorded in the Decision Log.
- **Cross-module const tracking — scope creep.** Confirm during P3/P4 whether `ModuleExports` already carries per-export const-ness; if not, decide whether to thread the bit through or to slip user-module enforcement to a follow-up.

## Decision Log

| Date | Decision | Rationale |
| --- | --- | --- |
| 2026-05-22 | Use a runtime `StashNamespaceMember` sentinel value + detect-and-invoke in `GetFieldIC`. | (Superseded.) Originally favored to localize the change, but conflates bare access with call-of-access and hides the type error behind a runtime check. |
| 2026-05-22 (revised) | Replace sentinel with **static `DeclarationKind` per entry**, resolved at compile time when the receiver statically denotes a known namespace. Dynamic-receiver case falls back to a uniform VM read path that branches on the same kind. | Honest static semantics. `ns.member()` against a `DataMember` is a type error at compile time, not a runtime polymorphic dispatch trick. Makes the language uniform with first-class function references (`ns.fn` returns the value stored at the slot — callable for `Function`, freshly computed for `DataMember`). |
| 2026-05-22 (revised) | **Hard-break migration**: `cli.argc()` etc. become compile-time `SA08xx`. No `[StashDeprecated]` shim, no AST sugar that rewrites `()` away, no deprecation window. | Stash is pre-1.0; the v1 set is 8 entries of inherent-identity reads with near-zero real-world `()` capture. A deprecation window for a coherence break that has no clean IR is more work than the rewrite it would defer. |
| 2026-05-22 (revised) | Source generator must reject a method annotated with both `[StashMember]` and `[StashFn]` (or `[StashMember]` and `[StashConst]`) at build time. | Kinds are mutually exclusive by definition; enforcing at registration time eliminates a class of runtime ambiguity. |
| 2026-05-22 (revised) | Document **first-class function references** in the Language Specification. Pin with a regression test. | The design's coherence depends on `ns.fn` (bare) returning a callable. This is already true at runtime but undocumented — making it spec-load-bearing is necessary and free. |
| 2026-05-22 | `ReadOnlyError` is a new `[StashError]`, not a subclass of `TypeError`. | "Read-only" and "wrong type" are distinct semantics. Promotes the error to the regenerated stdlib reference under its own entry; lets user `catch` blocks discriminate. |
| 2026-05-22 | v1 migration set is 8 members, scoped by "inherent script/process identity, not observations of changing state". | Bounded, defensible, easy to review. Excludes `time.*` (clock observations) and `term.*` (dynamic terminal state). |
| 2026-05-22 | User-module const enforcement is in v1 scope **iff** `ModuleExports` already carries const-ness. | If not, threading the bit is its own multi-phase effort; the P3/P4 phases close this in the Decision Log when reached. |
| 2026-05-22 (revised) | `[StashMember]` requires non-empty XML `<summary>`; the source generator (or analogous build-time test) fails the build otherwise. Optional `Throws`, `Capability`, `Deprecation`, and nullable `ReturnType` are supported, each mirroring the `[StashFn]` shape and feeding LSP hover, the stdlib reference, and static-analysis throws-tracking. | Shipping a member without docs leaves a permanent paper cut. Symmetry with `[StashFn]` keeps the authoring surface uniform. |
| 2026-05-22 (revised) | The spec frames namespace members as ES module live bindings + C# properties; pins the side-effect contract (getter may throw; allocations and identity not preserved). | Anchors the design as well-precedented; prevents users from assuming pure O(1) field semantics. |
| 2026-05-23 | P2 surfaced two cross-phase correctness risks (CSE-defeats-Live and frozen-helper-bypass) that have been pinned into P3 and P5 done_when respectively. | CSE would silently collapse repeated `Live`-member reads into one getter invocation, breaking the live contract; mutating `arr.*`/`dict.*` helpers extract underlying lists directly and would silently mutate frozen Cached members like `cli.argv`. Both fixes belong in the phases that introduce the relevant surface (P3 compile-time resolution; P5 v1 migration), not in P2's runtime layer. |
| 2026-05-22 (revised) | Replace the prior "fresh allocation per access" rule with a **stability-annotation model** (`Stability.Cached` default, `Stability.Live` opt-in) plus **freeze reference-typed return values at the boundary**. v1 assignment: `env.cwd` is `Live`; the other 7 are `Cached`. Cross-language framing cites JS ES module `const`-export (cached/identity-stable) vs `let`-export (live/re-read) as the explicit precedent. | The previous contract created a v1 footgun: `cli.argv[0] = "x"` would silently mutate a throwaway array on every read. Freezing returned references at the boundary turns that into a write-to-frozen error. The Cached/Live split makes identity stability a per-member design decision rather than a global property, aligned with a well-known JS precedent and giving us a place to land semantics for future host-mutable observations (e.g. `env.cwd` after `os.chdir`). |
| 2026-05-23 (P3) | SA0846 is the chosen diagnostic ID for "call of namespace data member" (`ns.member()`). SA0845 is reserved for P4 (read-only assignment enforcement). SA0846 follows the next available slot in the SA084x–SA085x gap. | The SA084x–SA085x range has SA0840–SA0844 (Bindings) and SA0850–SA0851 (Aliases). SA0845 is intentionally left for P4 (assignment to read-only) per spec; SA0846 is the next free code. |
| 2026-05-23 (P3) | LVN CSE-ineligibility for Live members is implemented as a name-based check in `LocalValueNumberingPass` using `StdlibRegistry.LiveMemberNames` (a `FrozenSet<string>` of unqualified member names with `Stability.Live`). This is over-conservative (user struct fields with the same name also lose CSE) but correct and minimal — the unsoundness produces no incorrect behavior, only a missed optimisation. | A per-chunk metadata approach (tagging the instruction or constant pool) would be more precise but requires threaded state from the compiler through the optimizer pass. The name-based check needs no new inter-pass contracts and is trivially sound. For real stdlib DataMembers added in P3+ the named set covers the actual regression vectors. |
| 2026-05-23 (P3) | `log.level` was added as the first `[StashMember(Stability = Live)]` DataMember to `LogBuiltIns` to make `StdlibRegistry.LiveMemberNames` non-empty. This enables the SA0846 analysis tests to exercise the rule against a real stdlib namespace without requiring any P5 migration. | P3 needs at least one Live DataMember in the registry to test both the CSE fix and the SA0846 rule. `log.level` (the current minimum log level string) is semantically appropriate: it can change at runtime via `log.setLevel`. |
