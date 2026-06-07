# `nameof(enum_value)` returns the fully-qualified member name, not the declared type name

**Status:** Backlog — Bug
**Created:** 2026-06-07
**Discovery context:** Surfaced by the Resolver during the `language-standard-values` feature review fix pass (F03). The reviewer's finding F03 included a fifth conformance test: `nameof(Color.Red) -> "Color"`. Runtime verification with the freshly-built binary produced `"Color.Red"` instead. This revealed a spec/runtime discrepancy, triggering a human decision on the resolution path.

**Option B ratified (2026-06-07):** The user chose to correct the spec to match the runtime ("Color.Red"), so the spec and runtime now agree. This item is no longer a spec/impl contradiction — it is a tracked **design-improvement** item for a future follow-up.

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

**Option B has been ratified** — the spec now documents `"Color.Red"` as the correct behavior. The spec and runtime agree; this is no longer a contradiction.

**Recommended eventual fix (follow-up):** Change the runtime to return the **bare member name** `"Red"` — the common path taken by most languages:
- C#: `nameof(Color.Red) == "Red"`
- Python: `Color.RED.name == "RED"`

This is the most intuitive and useful return value for dispatch, serialization, and display.

**Alternative (option A):** Change the runtime to return the declaring type name `"Color"`, consistent with the struct-instance analogy (`nameof(struct_instance) == "P"`). This is the spec-consistency path — both instances return only the type name.

Either follow-up requires a runtime change to `nameof` evaluation for enum values, a spec update, and a conformance test flip. The current conformance test (`Nameof_EnumValue_ReturnsQualifiedMemberName_PerSpecValuesTypeModel`) will flip red when the fix lands, serving as the change-detector.

The original two resolution paths for context:
- **(A) Fix the runtime to return `"Color"`** — analogous to struct instances; makes the spec's original "declared enum-type name" phrasing accurate.
- **(B) Correct the spec to `"Color.Red"`** ← **CHOSEN** — the spec now documents the runtime's qualified-path behavior. See commit listed in Verification below.

## Verification

**Option B is done** — confirmed green as of the F03 fix commit in `language-standard-values`:

```bash
# Current behavior (spec now matches runtime):
dotnet run --project Stash.Cli/ -- -c 'enum Color { Red, Green } io.println(nameof(Color.Red));'
# Returns: "Color.Red"  ← both spec and runtime agree (option B sealed)

# Conformance test that pins this behavior (added in F03 fix):
# Nameof_EnumValue_ReturnsQualifiedMemberName_PerSpecValuesTypeModel in TypeModelConformanceTests.cs

dotnet test --filter "FullyQualifiedName~TypeModelConformanceTests"
dotnet test --filter "Category=Conformance"
```

When the eventual follow-up fix lands (changing the runtime to return `"Red"`), the conformance test above will flip red — that is the intended change-detector signal to update the assertion and the spec.

## Related

- `.kanban/2-in-progress/language-standard-values/review.md` — F03 finding (the conformance test that discovered this)
- Spec §Values and Types L632-633 ("declared enum-type name" clause)
- Existing passing test: `Nameof_StructInstance_ReturnsStructName_PerSpecValuesTypeModel` (struct analogy that works correctly)
