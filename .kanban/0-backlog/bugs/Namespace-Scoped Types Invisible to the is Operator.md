# Namespace-Scoped Types Are Invisible to the `is` Operator

**Status:** Backlog — Bug
**Created:** 2026-05-29
**Discovery context:** Surfaced by the Implementer while writing `OsBuiltInsTests` during phase P7 of the `os-namespace` feature. The natural assertion `os.platform() is Platform` returned `false` instead of `true`, forcing the test author to fall back to C#-level `Assert.IsType<StashEnumValue>` + `TypeName` string checks rather than exercising Stash's own `is` operator. Confirmed empirically and traced to `VirtualMachine.TypeOps.cs` during the P7 orchestration turn.

---

## Problem

A struct or enum type declared **inside a namespace** (via `[StashStruct]` / `[StashEnum]` on a built-in namespace, or — by the same mechanism — any type that is not bound directly into global scope) cannot be referenced as the right-hand side of the `is` operator. Both the bare form and the qualified form silently evaluate to `false`:

```stash
os.platform() is Platform      // false — should be true
os.platform() is os.Platform   // false — should be true
```

`typeof(os.platform())` correctly reports `"enum"`, and the value really is a `StashEnumValue` whose `TypeName` is `"Platform"` — but there is no type name a Stash author can write that will make `is` return `true` for it. The same applies to the namespace-scoped struct `PlatformInfo` returned by `os.info()`.

This is compounded by a second, masking defect: the failure is **silent**. The language spec states that unknown type names on the right of `is` produce a runtime error, but the *static* (literal type-name) form returns `false` instead. So instead of an "unknown type `Platform`" error that would point an author straight at the resolution gap, they get a plausible-looking-but-wrong `false`.

## Reproduction

```bash
# Namespace-scoped enum — bare name. Expected: true. Actual: false.
$ dotnet run --project Stash.Cli/ -c Release -- -c 'io.println(os.platform() is Platform);'
false

# Namespace-scoped enum — qualified name. Expected: true. Actual: false.
$ dotnet run --project Stash.Cli/ -c Release -- -c 'io.println(os.platform() is os.Platform);'
false

# Control: a global/built-in type name works correctly.
$ dotnet run --project Stash.Cli/ -c Release -- -c 'io.println(5 is int);'
true

# Masking defect — an entirely unknown static type name returns false,
# but the spec (§ Type Checks) says it must raise a runtime error.
$ dotnet run --project Stash.Cli/ -c Release -- -c 'io.println(5 is Bogus);'
false
```

A top-level `enum`/`struct` declared at global script scope resolves correctly — the bug is specific to types that are **not bound into the globals dictionary**, which is the case for every type declared inside a namespace.

## Blast radius

- **Stash authors using namespaced stdlib types.** Today the live exposure is the `os` namespace's `Platform` enum and `PlatformInfo` struct (this feature). Any future namespace that publishes a struct/enum type — the established stdlib pattern — inherits the same gap. The number of affected types grows with every namespace that exposes a declared type.
- **Type-driven control flow is unavailable for these values.** Authors cannot write `if (v is Platform)` or use `is` in a `switch` guard against a namespaced type; they must compare against `os.name()` / `typeof()` strings instead, which is stringly-typed and loses the type's identity.
- **The masking (silent-`false`) defect has wider, latent reach.** *Every* static `is` against an unresolvable or misspelled type name returns `false` instead of erroring — a typo like `v is Strign` is silently always-false. This is a correctness footgun across all `is` usage, not just namespaced types, and it actively hides this very bug.
- Not a crash or data-corruption issue; it is silently-wrong behavior. No host/embedding-specific dimension.

## Root cause

`Stash.Bytecode/VM/VirtualMachine.TypeOps.cs`, `ExecuteIs` (around lines 91–168).

Every resolution arm that could match an enum/struct *value* to its declared *type* is gated on the type descriptor existing in the globals dictionary:

- String-name path: line 95 (`globals.TryGetValue(typeName, …) && … is StashStruct or StashEnum or StashInterface`), and the value-driven fallbacks at lines 125–134 each re-require `globals.TryGetValue(typeName, …)`.
- Descriptor-object path (`else` branch): lines 160 and 162 each AND in `(!atGlobalScope || globals2.ContainsKey(name))`.

Namespace-scoped types live on the `StashNamespace` object stored under the namespace key (e.g. `os`), **not** as their own entry in `globals`. So `globals.TryGetValue("Platform", …)` / `globals.ContainsKey("Platform")` always fail, every arm short-circuits, and control reaches the final `else { result = false; }` at line 147 (static form) — or the `atGlobalScope` gate forces `false` in the descriptor path.

The global-gating is intentional and has a real purpose: it makes `unset S` cause `v is S` to return `false` (the comment at lines 152–154 documents this). The defect is that the check conflates *"the type is a live global binding"* with *"the type exists at all"* — there is no path that consults namespace-resident type descriptors.

The masking secondary defect: the static-form fallthrough (lines 145–148) returns `false`, while the spec-mandated runtime error for unknown type names is only thrown on the *dynamic* form (lines 139–144). The static path therefore violates `docs/Stash — Language Specification.md` § Type Checks ("Unknown type names produce a runtime error.").

## Suggested fix

Two related but separable changes — the architect can fix them independently.

**For the primary (namespace-scoped visibility):**

- (A) **Resolve qualified namespace types in the descriptor path.** Make `os.Platform` evaluate to the `StashEnum`/`StashStruct` descriptor (member access on the namespace returning the type), and in the `else` branch drop/relax the `atGlobalScope && ContainsKey` gate when the descriptor came from a namespace rather than a global slot. Cleanest for the *qualified* form; requires teaching member-access to surface namespace-resident type descriptors.
- (B) **Consult namespaces during string-name resolution.** When `typeName` isn't a global or known built-in, fall back to scanning visible namespaces for a struct/enum whose `Name == typeName` before giving up. Makes the *bare* form (`is Platform`) work, matching how the implementer instinctively wrote it. Trade-off: bare names could collide across namespaces; needs a defined precedence (or restrict to unambiguous matches).
- (C) **Bind namespace type descriptors into a side table** keyed by name that `is` consults alongside globals, kept distinct from the `unset`-sensitive globals so the `unset S` semantics are preserved.

Recommend **(A) + the unblocking of the gate**: it keeps the `unset`-driven `false` semantics for true globals intact (the gate only relaxes for namespace-sourced descriptors) and gives an unambiguous, explicitly-qualified spelling (`os.Platform`) that mirrors how the type is declared. (B) can be layered on later as ergonomic sugar if bare names are desired.

**For the masking secondary (silent `false`):**

- Make the static-form fallthrough (line 147) raise the same "Right-hand side of 'is' must be a type" `RuntimeError` the dynamic form already throws at lines 139–144, bringing the static path in line with the spec. **Do this together with the primary fix** — otherwise turning on the error first would convert today's silent-wrong `os.platform() is Platform` into a hard runtime error, which is stricter but still wrong. Fixing the primary first (so the name resolves) and then tightening the fallthrough yields the correct end state: resolvable names match, genuinely unknown names error.

## Verification

A regression test that fails today and passes after the fix:

```bash
# New assertions in the os namespace suite:
#   os.platform() is Platform        => true
#   os.info()     is PlatformInfo     => true
#   <namespaced value> is <misspelled name> => raises RuntimeError (after secondary fix)
dotnet test --filter "FullyQualifiedName~OsBuiltInsTests"

# Cross-cutting checks that must continue to pass (unset semantics, global structs,
# built-in type names, typed arrays):
dotnet test --filter "FullyQualifiedName~BytecodeVmTests|FullyQualifiedName~CompilerTests"
```

Before the fix, the `is Platform` / `is PlatformInfo` assertions fail with `false`. After: they pass, and existing `unset S` → `v is S == false` behavior is unchanged.

## Related

- Surfaced during `os-namespace` phase P7 (test phase). The P7 tests (`Stash.Tests/Stdlib/OsBuiltInsTests.cs`, commit `b18aabb`) currently work around this with C#-level `Assert.IsType<StashEnumValue>` + `TypeName` checks; they should be upgraded to use `is` once this is fixed.
- Implementation: `Stash.Bytecode/VM/VirtualMachine.TypeOps.cs` — `ExecuteIs` and `CheckIsType`.
- Spec: `docs/Stash — Language Specification.md` § Type Checks (the "Unknown type names produce a runtime error" sentence is the contract the secondary defect violates).
- The masking silent-`false` defect could be split into its own backlog item if the architect prefers to scope the two fixes separately; they are filed together here because they share one code path and the secondary actively hides the primary.
