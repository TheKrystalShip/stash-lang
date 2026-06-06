# Stash.Tests/Conformance/Values — §Values & Types Conformance Tests

This directory proves that the Stash implementation honors the normative clauses of
`docs/Stash — Language Specification.md` **§Values and Types** (L570–L664).

Populated by the `language-standard-values` milestone unit (P1–P7). Each file covers one
spec clause group; every test carries `[Trait("Category", "Conformance")]` and names the
specific spec claim it proves (see `Stash.Tests/Conformance/_README.md` for the naming
convention, and `Stash.Tests/CLAUDE.md` → "Conformance tests — proving the spec" for the
full protocol).

---

## Clause groups this directory will prove

The following clause groups are the sealed normative surface of §Values & Types that this
directory proves. Each becomes one conformance test class (one `*ConformanceTests.cs` file):

| Clause group | Spec location | File (target) | Phase |
| ------------ | ------------- | ------------- | ----- |
| **Type model + typeof + range** (Edits 1 + 2 + 7, D3) | L582–L664 | `TypeModelConformanceTests.cs` | P1 |
| **Truthiness** (Edit 3, D1 + D5) | L621–L633 | `TruthinessConformanceTests.cs` | P2 |
| **Equality — numeric / cross-type / NaN** (Edit 4, D2) | L635–L646 | `EqualityNumericConformanceTests.cs` | P3 |
| **Equality — per-category, identity vs by-value** (Edit 4) | L635–L646 | `EqualityPerCategoryConformanceTests.cs` | P4 |
| **Type coercion** (Edit 5) | L648–L652 | `CoercionConformanceTests.cs` | P5 |
| **Secret values** (Edit 6, D4) | L654–L664 | `SecretConformanceTests.cs` | P6 |

---

## Naming convention (examples)

```
TypeModelConformanceTests:
  TypeOf_Int_ReturnsInt_PerSpecValuesTypeModel()
  TypeOf_ByteQuantity_ReturnsBytesNotBytesize_PerSpecValuesTypeModel()
  TypeOf_Range_ReturnsRange_PerSpecValuesTypeModel()
  TypeOf_StructInstance_ReturnsStruct_PerSpecValuesTypeModel()

TruthinessConformanceTests:
  Truthiness_EmptyArray_IsTruthy_PerSpecValuesTruthiness()
  Truthiness_CaughtError_IsTruthy_PerSpecValuesTruthiness()

EqualityNumericConformanceTests:
  Equality_IntEqualsFloat_LiteralForm_PerSpecValuesEqualityNumeric()
  Equality_IntEqualsFloat_VariableForm_PerSpecValuesEqualityNumeric()
```
