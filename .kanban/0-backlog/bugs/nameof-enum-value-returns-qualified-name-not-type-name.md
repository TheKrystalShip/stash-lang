# `nameof(enum_value)` returns the fully-qualified member name, not the declared type name

**Status:** Backlog — Bug
**Created:** 2026-06-07
**Discovery context:** Surfaced by the Resolver during the `language-standard-values` feature review fix pass (F03). The reviewer's finding F03 included a fifth conformance test: `nameof(Color.Red) -> "Color"`. Runtime verification with the freshly-built binary produced `"Color.Red"` instead. This revealed a spec/runtime discrepancy before the conformance test was written, blocking F03.

---

## Problem

The language specification (§Values and Types, L632-633) states:

> `typeof(enum_value)` returns `"enum"`. `nameof(enum_value)` returns the declared enum-type name.

The example from §Values and Types L647-648 reinforces this:

> `nameof` returns the user-visible name (e.g. `nameof(Server) == "Server"`).

In practice, `nameof(Color.Red)` returns `"Color.Red"` — the fully-qualified member name — not `"Color"` (the declared type name). This is a spec/runtime asymmetry: `nameof(struct_instance)` correctly returns the type name (e.g. `"P"` for a struct named `P`), but `nameof(enum_value)` returns the qualified member name rather than the type name.

## Reproduction

```bash
# Build from source:
dotnet run --project Stash.Cli/ -- -c 'enum Color { Red, Green } io.println(nameof(Color.Red));'
# Spec says: "Color"  (the declared enum-type name)
# Actual:    "Color.Red"

# Via a variable (confirms it's a value-level behavior, not syntactic):
dotnet run --project Stash.Cli/ -- -c 'enum Color { Red, Green } let v = Color.Red; io.println(nameof(v));'
# Spec says: "Color"
# Actual:    "Color.Red"

# The TYPE IDENTIFIER case is correct:
dotnet run --project Stash.Cli/ -- -c 'enum Color { Red, Green } io.println(nameof(Color));'
# Returns: "Color"  (correct)

# Struct is correct for comparison:
# nameof(struct_instance) -> "P"  (confirmed by existing passing test Nameof_StructInstance_ReturnsStructName_PerSpecValuesTypeModel)
```

## Blast radius

- Any Stash user code that calls `nameof()` on an enum *value* (as opposed to the enum type identifier) and expects to get the type name back.
- The `language-standard-values` conformance test suite (F03) cannot add the `Nameof_EnumValue_PerSpecValuesTypeModel` test until this is resolved — either the runtime is corrected to return `"Color"`, or the spec is corrected to document `"Color.Red"` as the intended behavior.
- Latent today (no known real user code depends on `nameof(enum_value)` returning the type name) but will become a defect report if any user builds enum-dispatch logic using `nameof`.

## Root cause

Unknown precisely. Likely in `nameof` evaluation for enum values in `Stash.Bytecode/Runtime/` or `Stash.Core/`. The `nameof` operator appears to store or return the qualified binding path (`EnumTypeName.MemberName`) for enum member values rather than stripping to just the type name. Struct instances correctly return the type name because they are created via a constructor and carry the type identifier separately from any access path.

Candidate files:
- `Stash.Bytecode/Runtime/` — nameof runtime evaluation
- `Stash.Core/` — enum value representation (does an enum value carry its declaration site / qualified name as a field?)

## Suggested fix

Two resolution paths — the human reviewer should decide which side is law:

- **(A) Fix the runtime** — make `nameof(enum_value)` return the declaring enum type name (e.g. `"Color"` for `Color.Red`), consistent with the spec's "declared enum-type name" language and analogous to how `nameof(struct_instance)` returns the struct type name. This makes the spec authoritative.
- **(B) Correct the spec** — update §Values and Types L632-633 to document that `nameof(enum_value)` returns the fully-qualified member name (e.g. `"Color.Red"`), and update any conformance test to assert that behavior. This makes the runtime authoritative.

The spec's "declared type name" phrasing and the struct analogy both favor **(A)**, but the decision requires a human call.

## Verification

After resolution:

```bash
# If (A) — runtime fixed:
dotnet run --project Stash.Cli/ -- -c 'enum Color { Red, Green } io.println(nameof(Color.Red));'
# Expected: "Color"

# Conformance test to add (currently blocked):
# Nameof_EnumValue_ReturnsEnumTypeName_PerSpecValuesTypeModel in TypeModelConformanceTests.cs

dotnet test --filter "FullyQualifiedName~TypeModelConformanceTests"
dotnet test --filter "Category=Conformance"
```

## Related

- `.kanban/2-in-progress/language-standard-values/review.md` — F03 finding (the conformance test that discovered this)
- Spec §Values and Types L632-633 ("declared enum-type name" clause)
- Existing passing test: `Nameof_StructInstance_ReturnsStructName_PerSpecValuesTypeModel` (struct analogy that works correctly)
