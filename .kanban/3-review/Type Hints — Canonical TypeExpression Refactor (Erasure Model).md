# Type Hints — Canonical TypeExpression Refactor (Erasure Model)

**Status:** Decided — ready for final review before promotion to `1-todo/`
**Created:** 2026-05-16
**Decided:** 2026-05-16 (Cristian)
**Author:** Spec Architect
**Origin:** User concern that type hinting in Stash feels "bolted on" rather than first-class, triggered by commit `5165e9d feat(typehint): support namespace-qualified dotted type annotations`. The original audit (see git history of this file under its previous name *Type Hints — Architectural Audit and Evolution*) identified five distinct in-memory shapes for "a type" and an unresolved enforcement question.
**Scope:** `Stash.Core` (parser + AST), `Stash.Analysis` (resolver, type rules, inference), `Stash.Bytecode` (Compiler, VM, opcodes, serialization), `Stash.Lsp`, `Stash.Stdlib` (type registry), `Stash.Playground`, `.vscode/extensions/stash-lang`, language spec, stdlib reference.

---

## 0. TL;DR

Stash type hints today suffer from two problems:

1. **No canonical type representation.** A "type" exists in the codebase as a `Token`, a `TypeHint` record, a `string`, an `Expr`, or a raw token list — five shapes, none authoritative. The triggering commit (`5165e9d`) exposed this: dotted forms (`diff.Edit`) only reached one of seven type-position productions, and only one of the eight downstream consumers handles them correctly. The compiler at `Stash.Bytecode/Compilation/Compiler.Helpers.cs:342-354` even silently truncates `diff.Edit[]` to `"diff"` in the constant pool.
2. **The spec lies about enforcement.** `docs/Stash — Language Specification.md` §"Type Hints" promises a runtime error on mismatch. In reality only two cases enforce: `byte` narrowing and typed-array element wrapping (both inside `EmitTypeWrapping`). Every other annotation — `let p: Point = 42` included — is silently accepted.

**This spec resolves both, decisively, with the cheapest design that also unblocks future richness:**

- **Decision A (canonicalization):** Introduce a `TypeExpression` AST hierarchy and route every type-position production through one parser entry point. Replace `TypeHint`, `CatchClause.TypeTokens`, `IsExpr.TypeName`/`TypeExpr`, `ExtendStmt.Name`, and `StructInitExpr.Name` with `TypeExpression` references.
- **Decision B (erasure):** Adopt the **full-erasure** model. Type hints are advisory metadata for tooling (LSP, hover, analyzer diagnostics, doc generation). They produce **no runtime checks** and **no value coercion**. The two existing runtime enforcement points (`byte` narrowing and `T[]` element wrapping) are removed; the spec is amended to match.

The combination is small, internally consistent, and matches proven precedent (TypeScript, Python, Luau under `--!nonstrict`). It unblocks generics / unions / nullable / function types as later additions without paying any runtime cost up-front.

**Why now, not later:** Stash has no live users. The cost of walking back the two runtime enforcement points is bounded to a handful of test updates and a spec amendment. Every month this waits, that cost grows and the wrong mental model leaks further into stdlib and idiomatic user code.

---

## 1. Decision Record

### 1.1 Decision A — Canonical `TypeExpression`

**Chosen:** A dedicated `TypeExpression` AST hierarchy is the single source of truth for "a type in source code." Every grammar position that accepts a type name parses through `Parser.ParseTypeExpression()` and stores the result as `TypeExpression?`.

**Alternatives considered:**

- **Keep `TypeHint` as-is, extend per shape.** Rejected. The triggering commit cost 7 files / 283 lines for one shape; generics/unions/nullable would each repeat that. The audit (now folded into §3 of this doc) walked the five-axis explosion.
- **Treat annotations as plain `Expr` nodes (Python model).** Rejected. Stash's grammar is C-style with significant punctuation overlap between expressions and types (`<`, `|`, `[`). Lexer-level mode switching would be required. The dedicated-AST approach (TypeScript, Luau) is the better fit for Stash's grammar shape.

**Rationale:** TypeScript and Luau both prove the dedicated-`TypeNode` model scales through generics, unions, intersections, function types, and qualified imports without per-extension shotgun surgery. The cost is one new file (`TypeExpression.cs`) plus visitor updates — a fixed one-time charge that pays off on every subsequent type-system feature. Erasure (Decision B) makes this even cleaner: there is no `CheckType` opcode to thread through, no runtime type representation to maintain.

**Risks:**

- Parser ambiguity at `catch (T1 | T2 e)`: when `|` becomes a `TypeExpression` production (future), the existing catch-clause `|` separator must remain unambiguous. Mitigation: in this phase, `UnionType` is **not** introduced, so the catch-clause parser keeps its current `|`-separated list of `TypeExpression`s.
- Migration breadth: ~15 visitor classes touch `TypeHint` today. Mitigation: do the rename and AST shape change as one focused commit; resist the temptation to refactor surrounding code at the same time.

### 1.2 Decision B — Erasure Model

**Chosen:** Type hints are **fully erased** at runtime. They are metadata only — consumed by the analyzer, the LSP, the doc generator, and the bytecode disassembler. The compiler emits **no** `TypedWrap`, **no** `CheckType`, and performs **no** value coercion based on a hint.

**Alternatives considered (per original audit §4.5):**

- **(B) Uniform runtime enforcement.** Emit a check at every typed binding and parameter. Rejected: measurable per-call cost; requires a runtime type representation; doubles down on the "annotations are guarantees" model that TypeScript explicitly walked away from.
- **(C) Boundary enforcement (Typed Racket / Hack).** Enforce at function entry and assignment. Rejected: more complex than A, partially overlapping with what an analyzer pass can prove statically, and still requires runtime type plumbing. It is also approximately where Stash *accidentally* is today, and the user's instinct was right: it's an awkward middle ground.

**Rationale:**

1. **No users to break.** The cost of removing the two existing enforcement points is bounded.
2. **Simpler runtime.** One less opcode to maintain in the dispatch loop (which is at its AOT optimization threshold — see `Stash.Bytecode/CLAUDE.md` "Dispatch Loop Size Limit"). One less concept in the VM.
3. **Proven precedent.** TypeScript, Python (default), Flow, Luau (`--!nonstrict`) — every dynamically-typed language that adopted gradual typing successfully chose erasure. Sorbet's hybrid model is widely cited as the awkward outlier.
4. **Future-friendly.** Generics, unions, intersections, function types, nullables — all cost zero runtime budget under erasure. Under enforcement, each new shape has to teach the VM how to check it.
5. **Honest with users.** The spec stops lying. "Annotations are advisory" is a one-line promise that is easy to verify, easy to test, and never surprises anyone.

**Risks:**

- **Loss of `byte` narrowing convenience.** Today `let b: byte = 200` silently narrows a `long` literal to a `byte`. Under erasure this is no longer automatic. **Resolution: §2.1.**
- **Loss of typed-array element identity.** Today `let arr: int[] = [1, 2, 3]` produces a `StashTypedArray` whose `ElementTypeName` participates in `is int[]`. Under erasure, the array is a plain `array`. **Resolution: §2.2.**
- **Trigger for reversal:** if removing these makes idiomatic system-administration scripts visibly worse (e.g., users repeatedly call `conv.byte()` in tight loops), revisit boundary enforcement (option C) — but only with a real benchmark, not speculation.

---

## 2. User-Visible Semantic Changes Under Erasure

These are the two existing runtime enforcement points and their replacement behavior. Both are user-visible, so they must be documented in the spec and covered by tests.

### 2.1 `byte` narrowing — REMOVED; users call `conv.byte()` explicitly

**Today** (`Stash.Bytecode/Compilation/Compiler.Helpers.cs:347-353`): `let b: byte = 200;` emits `TypedWrap` with `"byte"` in the constant pool; the VM converts the `long` to a `byte` at runtime.

**After:** `let b: byte = 200;` stores the value as a `long` (a literal `200` is a `long`). The `: byte` annotation is recorded in `SymbolInfo` for tooling but has no runtime effect.

**Migration path for users:** explicit conversion via `conv.byte(200)`, which already exists in the stdlib and returns a `byte` value. The analyzer SHOULD warn (new diagnostic `TypeHintLiteralMismatch`) when a literal whose inferred type does not match the annotation is assigned to it — this preserves the "you told me byte, I noticed you wrote a long" feedback without doing anything at runtime.

**Reasoning:** narrowing under the hood is a coercion, not a check. It does **two** things implicitly: it changes the value's CLR type, and it elides the explicit cast a reader would expect. Under erasure we should do neither. `conv.byte()` is one extra token at the call site and is mechanically what the compiler used to do anyway. The new analyzer warning recovers most of the safety value at zero runtime cost.

### 2.2 `T[]` element wrapping — REMOVED; `[...]` always produces a plain array

**Today** (`Compiler.Helpers.cs:344-348`, `VirtualMachine.Collections.cs:544`): `let arr: int[] = [1, 2, 3];` wraps the literal in a `StashTypedArray` with `ElementTypeName = "int"`. Subsequent `is int[]` checks succeed.

**After:** `let arr: int[] = [1, 2, 3];` produces a plain `List<StashValue>`. The annotation is recorded for tooling. `is int[]` evaluated against this value returns `false` (because there is no element-type metadata to check against). `is array` returns `true`.

**Side effects on the VM:**

- `StashTypedArray` itself **stays** as a runtime type for now. It is still produced by other code paths that need to advertise element types. The change is that `let`/`const`/`fn` annotations no longer create one.
- `_knownTypeNames` in `Stash.Bytecode/VM/VirtualMachine.TypeOps.cs:18` no longer needs `"int[]", "float[]", "string[]", "bool[]", "byte[]"`. The general `_ when typeName.EndsWith("[]")` arm at line 66 stays, because any surviving stdlib-produced typed arrays still match it.
- **Decision deferred to the orchestrator implementing this:** grep for all `StashTypedArray` constructors after removing the literal-construction path (`VirtualMachine.Collections.cs:544` and any callers in `Compiler.*`). If no construction sites survive, delete `StashTypedArray` entirely and simplify the line-66 fallback to return `false`. If construction sites survive (e.g., stdlib functions returning typed arrays), keep `StashTypedArray` and the fallback as-is. Document the outcome in the Decision Log.

**Reasoning:** the runtime cost is non-trivial (wrap allocation, dispatch checks at every `arr[i]` access in `VirtualMachine.Collections.cs:135, 193`) and the semantic benefit is small — the typed-array invariant only holds at the *creation site* of the literal; any subsequent `arr.push(some_string)` already poisons it. Real element-type checking needs a static analyzer pass, not a runtime wrapper.

### 2.3 The latent `diff.Edit[]` truncation bug is moot

The audit identified that `EmitTypeWrapping` truncates dotted types to their head segment (`Compiler.Helpers.cs:342-354`: stores `"diff"` instead of `"diff.Edit"` in the constant pool for `let arr: diff.Edit[] = ...`). Under erasure, **`EmitTypeWrapping` is deleted entirely** in Phase 2. The bug ceases to exist by deletion, not by fix. No test for the buggy path needs to be written; the test plan in §5.2 instead asserts that the annotation has no runtime effect at all.

### 2.4 What does NOT change (user-visible)

- `is T` runtime checks continue to work for all primitives, structs, enums, interfaces, and built-in error types. `is` is an *explicit* user-written check; erasure only removes *implicit* compiler-emitted checks.
- `catch (T e)` continues to match against `T` at runtime. This is also explicit, not implicit.
- `extend T { ... }` continues to attach methods to `T`. The type name is resolved at definition time, same as today.
- Struct field initialization (`Point { x: "not an int" }`) — today the `FieldTypeMismatchRule` analyzer rule flags this. That rule continues to run; erasure removes runtime checks, not analyzer rules.

---

## 3. Why the existing design is broken (preserved from the audit)

This section preserves the architectural diagnosis that motivated the decisions in §1. It is intentionally condensed; the original long-form audit lives in this file's git history.

### 3.1 Five inconsistent in-memory shapes for "a type"

| # | Shape | Used by | File:line |
|---|-------|---------|-----------|
| 1 | `Token` | catch-clause type names, `IsExpr.TypeName`, `ExtendStmt.Name`, `StructInitExpr.Name` | `Stash.Core/Parsing/AST/CatchClause.cs:23`, `IsExpr.cs:26`, `Parser.cs:655` |
| 2 | `TypeHint` record | `let`, `const`, `fn` params/return, `for-in`, struct/interface fields, retry hook params, lambda params | `Stash.Core/Parsing/AST/TypeHint.cs` |
| 3 | `string` | the entire analyzer (`SymbolInfo.TypeHint`, `ParameterTypes[]`, `InterfaceField.TypeHint`, all four `*TypeMismatchRule`s, `TypeInferenceEngine` return type), VM constant pool, `.stashc` serialization | `Stash.Analysis/Models/SymbolInfo.cs:97, 126`, `AssignmentTypeMismatchRule.cs:33`, `FieldTypeMismatchRule.cs:39` |
| 4 | `Expr` (any expression) | `IsExpr.TypeExpr` (the value-typed branch of `is`) | `Stash.Core/Parsing/AST/IsExpr.cs:31, 56` |
| 5 | Raw `IReadOnlyList<Token>` | `CatchClause.TypeTokens` for multi-type catches | `Stash.Core/Parsing/AST/CatchClause.cs:23` |

### 3.2 Seven grammar positions, one shared parser

`Parser.ParseTypeHint()` (`Stash.Core/Parsing/Parser.cs:3189-3214`) is called from twelve sites covering `let`/`const`/`fn`/`for-in`/struct/interface/lambda/retry. The other six positions (`is T`-token form, `is <expr>`, `catch (T | U e)`, `extend T`, `T { ... }` struct init, `throw T(...)`) each roll their own. Commit `5165e9d` made dotted paths work in `ParseTypeHint()` only — none of the six others can parse `diff.Edit` today.

### 3.3 Stringly-typed comparisons and triplicated primitive registries

- Equality checks are raw string compares (`AssignmentTypeMismatchRule.cs:33`, `FieldTypeMismatchRule.cs:39`). No normalization, no subtyping, no nullability, no array-element awareness.
- `UnknownTypeRule.cs:113-119` literally bails on any dotted path (`Path.Count > 1`) with a comment promising "LSP/import resolver covers it." Nothing in fact covers it.
- The canonical primitive type set lives in **three** places:
  - `StdlibRegistry.ValidTypes` (analysis) — referenced from `Stash.Analysis/SemanticValidator.cs:34`
  - `_knownTypeNames` in `Stash.Bytecode/VM/VirtualMachine.TypeOps.cs:18` (runtime)
  - The literal-inference `switch` in `Stash.Analysis/Engines/TypeInferenceEngine.cs:209-219`
- The spec promises runtime enforcement (`docs/Stash — Language Specification.md` §"Type Hints", line 452); only two cases actually enforce. This is the "spec lies" finding, resolved by Decision B + Phase 3.

### 3.4 The shotgun-surgery cost is now visible

Commit `5165e9d` touched 7 files / 283 lines to add one new shape (dotted paths). Every visitor that pattern-matches `TypeHint` had to learn about `Path`. `Stash.Lsp` `SemanticTokenWalker` alone had 11 call-site changes from `EmitTypeReference(typeHint.Name)` to `EmitTypeReference(typeHint)`. Each future shape (generics, unions, nullable, function types) would pay this cost again.

---

## 4. Target Design

### 4.1 `TypeExpression` AST hierarchy

New file: `Stash.Core/Parsing/AST/TypeExpression.cs`.

```csharp
public abstract record TypeExpression(SourceSpan Span)
{
    /// <summary>Canonical source-form string, stable across the compiler.</summary>
    public abstract string ToCanonicalString();
}

public sealed record SimpleType(Token Name, SourceSpan Span) : TypeExpression(Span)
{
    public override string ToCanonicalString() => Name.Lexeme;
}

public sealed record QualifiedType(IReadOnlyList<Token> Segments, SourceSpan Span)
    : TypeExpression(Span)
{
    public override string ToCanonicalString() => string.Join('.', Segments.Select(s => s.Lexeme));
}

public sealed record ArrayType(TypeExpression Element, SourceSpan Span) : TypeExpression(Span)
{
    public override string ToCanonicalString() => Element.ToCanonicalString() + "[]";
}
```

**Reserved for follow-up work (not in scope, do NOT add):** `GenericType`, `UnionType`, `NullableType`, `FunctionType`. These are explicitly out of scope (see §7) but the hierarchy is designed to admit them as additional `record` cases without disturbing existing consumers.

### 4.2 Grammar — single production

```
typeExpression := postfixType
postfixType    := primaryType ('[' ']')*
primaryType    := Identifier ('.' Identifier)*
```

`Parser.ParseTypeExpression()` replaces `Parser.ParseTypeHint()`. Every grammar position that accepts a type calls it: `: T` annotations, `is T`, `catch (T1 | T2 e)`, `extend T`, struct init `T { ... }`.

**Note on `is`**: the existing `is <expr>` predicate form (where the RHS is an arbitrary expression evaluating to a type value) stays — but it is now disambiguated from `is T` (where T is a *type name*) by trying `ParseTypeExpression()` first and falling back to `ParseExpression()` if the result isn't directly consumable. The exact parser strategy here is left to the orchestrator; the constraint is that `is diff.Edit` MUST parse as a `QualifiedType`, not as a member-access expression.

**Note on `catch`**: catch clauses become `IReadOnlyList<TypeExpression>` instead of `IReadOnlyList<Token>`. The `|` between catch types remains a catch-grammar separator, not a `UnionType` constructor — there is no `UnionType` in this scope.

### 4.3 AST changes

| Node | Today | After Phase 1 |
|------|-------|---------------|
| `VarDeclStmt.TypeHint` | `TypeHint?` | `TypeExpression?` |
| `ConstDeclStmt.TypeHint` | `TypeHint?` | `TypeExpression?` |
| `FnDeclStmt.Parameters[].TypeHint` | `TypeHint?` | `TypeExpression?` |
| `FnDeclStmt.ReturnType` | `TypeHint?` | `TypeExpression?` |
| `ForInStmt.TypeHint` | `TypeHint?` | `TypeExpression?` |
| `StructDeclStmt.Fields[].TypeHint` | `TypeHint?` | `TypeExpression?` |
| `InterfaceDeclStmt.Fields[].TypeHint` and method param/return types | `TypeHint?` | `TypeExpression?` |
| `LambdaExpr.Parameters[].TypeHint` | `TypeHint?` | `TypeExpression?` |
| `RetryExpr` hook parameter type | `TypeHint?` | `TypeExpression?` |
| `CatchClause.TypeTokens` | `IReadOnlyList<Token>` | `IReadOnlyList<TypeExpression>` (rename field to `CatchTypes`) |
| `IsExpr.TypeName` + `IsExpr.TypeExpr` | exclusive-or `Token?` / `Expr?` | `TypeExpression? Type` + `Expr? TypeExpr` (still exclusive-or but one side is now structured) |
| `ExtendStmt.Name` | `Token` | `TypeExpression` (always `SimpleType` or `QualifiedType`) |
| `StructInitExpr.Name` | `Token` | `TypeExpression` (always `SimpleType` or `QualifiedType`) |

The `TypeHint` record is **deleted** at the end of Phase 1. No compatibility wrapper is shipped; this is a pre-1.0 internal refactor.

### 4.4 Analyzer changes

- `SymbolInfo.TypeHint` (`Stash.Analysis/Models/SymbolInfo.cs:97`) keeps its string shape for now — it is still consumed by string-comparing rules and the LSP. The string source-of-truth is updated from `TypeHint.Lexeme` to `TypeExpression.ToCanonicalString()`. **No new `ResolvedType` field is introduced in this scope** (deferred — see §7).
- `UnknownTypeRule.cs:113-119`'s bail-out is removed. The rule walks `QualifiedType.Segments`, asks the import resolver for the alias, and resolves the tail segment in that module's exports. If unresolved, emit `UnknownType` diagnostic as for simple types.
- A new analyzer diagnostic `TypeHintLiteralMismatch` is added to recover the `byte` warning value from §2.1. It fires when a `let`/`const` initializer is a literal whose inferred type does not match the annotation. Severity: `Warning`. Suggested code action: insert `conv.<targetType>(...)` wrapper.

### 4.5 Compiler / VM changes (Phase 2)

- `Compiler.Helpers.cs:342-354` `EmitTypeWrapping` is **deleted**.
- The two callers `Stash.Bytecode/Compilation/Compiler.Declarations.cs:49` and `:91` lose the call.
- `OpCode.TypedWrap` is **deleted** from `Stash.Bytecode/Bytecode/OpCode.cs`. Its dispatch case in `Stash.Bytecode/VM/VirtualMachine.Dispatch.cs` is deleted. Its disassembler entry is deleted. (Net effect: dispatch loop shrinks by one opcode — small win for AOT codegen.)
- `_knownTypeNames` in `VirtualMachine.TypeOps.cs:18`: remove `"int[]", "float[]", "string[]", "bool[]", "byte[]"` from the set. The fallback `_ when typeName.EndsWith("[]")` at line 66 stays for any surviving stdlib-produced `StashTypedArray` instances; if §2.2's grep shows none remain, simplify the arm to return `false`.
- `StashTypedArray` literal-construction path in `VirtualMachine.Collections.cs:544` — drop the `elementType` wrapping; produce a plain `List<StashValue>`. The `StashTypedArray` consumers at `VirtualMachine.Collections.cs:18, 74, 135, 193, 401, 549` continue to handle the type defensively in case any survive.
- **Bytecode format version:** the `OpCode.TypedWrap` removal invalidates the OpCode table hash, which auto-invalidates `.stashc` files (per `Stash.Bytecode/CLAUDE.md` serialization rules). No explicit format-version bump needed for that reason. `BytecodeWriter.cs:309`'s `WriteNullableString(writer, field.TypeHint)` for interface fields is unchanged — interface field type hints still serialize as canonical strings (now sourced from `TypeExpression.ToCanonicalString()`).

### 4.6 What does NOT change

- LSP hover, completion, go-to-type, semantic tokens — all keep reading `SymbolInfo.TypeHint` as a string. They become *more correct* (because dotted types now reach them consistently) but the API surface is unchanged. `SemanticTokenWalker` gains a single `EmitTypeReference(TypeExpression)` overload, replacing the per-shape ad-hoc dispatch added in `5165e9d`.
- `is T` runtime semantics. `catch (T e)` runtime semantics. `extend T` resolution. Struct init field-type analyzer checks.
- Doc-comment pipeline (`Stash.Docs`). `StashFn` / `StashParam` attribute metadata.
- The runtime types `StashError`, `StashInstance`, `StashEnumValue`, `StashInterface` and all their machinery.

---

## 5. Implementation Phasing

Each phase is a self-contained chunk of work that compiles and passes tests on its own. The orchestrator agent SHOULD land each as a separate commit (or PR) and run the full test suite between phases.

### Phase 1 — Introduce `TypeExpression`, route all type-position parsing through it

**Goal:** every type in the AST is a `TypeExpression`. No runtime semantic changes yet; `EmitTypeWrapping` still runs (but is now fed the structured form, fixing the `diff.Edit[]` truncation as a side effect for the duration of Phase 1). Tooling sees dotted types consistently in all positions.

**Files added:**

- `Stash.Core/Parsing/AST/TypeExpression.cs` — the new hierarchy per §4.1.

**Files modified:**

- `Stash.Core/Parsing/Parser.cs` — rename `ParseTypeHint()` to `ParseTypeExpression()`. Update callers at lines 242, 286, 416, 436, 458, 512, 611, 622, 633, 1126, 2148, 2153, 2919. Add calls at the six non-`TypeHint` positions: catch clause parsing, `is` (token form, lines 1809, 1839), `extend` (line 655), struct init (`StructInitExpr` parsing site), and any others surfaced during work.
- `Stash.Core/Parsing/Parser.cs:1080-1092` — remove the `IsForInLoop` lookahead dotted-segment special case (now handled uniformly).
- `Stash.Core/Parsing/AST/` — update node definitions per the §4.3 table. Delete `TypeHint.cs` at the end of the phase.
- All visitors implementing the visitor interfaces — six visitors per `.claude/repo.md` "Visitor completeness". The mechanical change is `TypeHint` → `TypeExpression`, and `.Lexeme` → `.ToCanonicalString()`. The `Stash.Lsp` `SemanticTokenWalker` change merits its own focused review (collapse the 11 `5165e9d`-era call sites to one `EmitTypeReference(TypeExpression)`).
- `Stash.Analysis/Visitors/SymbolCollector.cs:158, 603, 619, 810` — update `RecordTypeReference` and the `typeStr = stmt.TypeHint?.Lexeme` assignments.
- `Stash.Analysis/Rules/UnknownTypeRule.cs:106-135` — remove the `Path.Count > 1` bail-out; implement `QualifiedType` resolution via the import resolver.
- `Stash.Bytecode/Compilation/Compiler.Helpers.cs:342-354` `EmitTypeWrapping` — temporarily updated to call `TypeExpression.ToCanonicalString()` instead of `typeHint.Name.Lexeme`. (This fixes the `diff.Edit[]` truncation bug for the duration of Phase 1 only; Phase 2 deletes the method entirely.)

**Tests added:**

- `Stash.Tests/Parsing/TypeExpressionParserTests.cs` — parses each grammar position with simple, qualified, and array forms; asserts the resulting AST node type and `ToCanonicalString()`.
- Update existing parser tests for `let`, `const`, `fn`, etc. to assert `TypeExpression`, not `TypeHint`.
- New test: `catch (diff.ParseError e)` parses and produces a `QualifiedType` in the catch types list.
- New test: `extend diff.DiffOptions { ... }` parses.
- New test: `if x is diff.Edit { ... }` parses as `QualifiedType`, not as member access.
- New test: `Point { x: 1 }` keeps working; `mod.Point { x: 1 }` also parses (orchestrator: confirm with user whether qualified struct-init is intended at language level — likely yes for symmetry, but flag in PR description).

**Verification at end of phase:** full `dotnet test` green; `EmitTypeWrapping` still runs (so existing byte/T[] tests still pass); LSP regression suite green.

### Phase 2 — Erase runtime enforcement

**Goal:** delete `EmitTypeWrapping`, `OpCode.TypedWrap`, and the associated VM dispatch. Annotations become metadata-only.

**Files modified:**

- `Stash.Bytecode/Compilation/Compiler.Helpers.cs:342-354` — delete `EmitTypeWrapping`.
- `Stash.Bytecode/Compilation/Compiler.Declarations.cs:49, 91` — remove the calls.
- `Stash.Bytecode/Bytecode/OpCode.cs` — remove the `TypedWrap` variant.
- `Stash.Bytecode/VM/VirtualMachine.Dispatch.cs` — remove the `TypedWrap` dispatch case.
- `Stash.Bytecode/Bytecode/Disassembler.cs` — remove the `TypedWrap` entry.
- `Stash.Bytecode/VM/VirtualMachine.TypeOps.cs:18` — remove `"int[]", "float[]", "string[]", "bool[]", "byte[]"` from `_knownTypeNames`. Verify the line-66 fallback still handles any stdlib-produced typed arrays; if grep shows none remain (see next bullet), simplify the arm to return `false`.
- `Stash.Bytecode/VM/VirtualMachine.Collections.cs:544` — drop the `StashTypedArray.Create(elementType, list)` wrapping in the literal-construction path; produce a plain `List<StashValue>`.
- **Decision in this phase:** grep `StashTypedArray` across `Stash.Bytecode/`, `Stash.Runtime/`, `Stash.Stdlib/` for remaining construction sites. If only the deleted literal-construction path produced them, delete `StashTypedArray` and its consumers at `VirtualMachine.Collections.cs:18, 74, 135, 193, 401, 549` and the `_ when typeName.EndsWith("[]")` fallback at `TypeOps.cs:66`. If construction sites survive, keep `StashTypedArray` and its consumers. Document the outcome in this file's Decision Log.
- `Stash.Analysis/` — add `TypeHintLiteralMismatchRule.cs` (new analyzer rule per §4.4). Register in the rule list. Suggested code action: insert `conv.<targetType>(...)` wrapper around the literal.

**Tests added/updated:**

- **Delete** tests in `Stash.Tests/` that asserted automatic `long → byte` narrowing on annotated `let`. (Search for `: byte` annotation tests; convert to "after rewrite with `conv.byte()`, behavior matches.")
- **Delete** tests that asserted `is int[]` after a literal `let arr: int[] = [...]`.
- Add `Stash.Tests/Analysis/TypeHintLiteralMismatchTests.cs` — exercises the new warning (positive and negative cases; verifies code action text).
- Add `Stash.Tests/Runtime/AnnotationsAreErasedTests.cs` — assertion suite confirming:
  - `let b: byte = 200;` then `b is long` returns `true` (erasure).
  - `let arr: int[] = [1, "two", 3.0];` runs without runtime error; the array contains mixed types; `arr is array` is `true` and `arr is int[]` is `false`.
  - `let p: Point = "not a point";` runs without runtime error (analyzer still warns via a separate rule path).

**Verification at end of phase:** full `dotnet test` green. Bytecode disassembly of the benchmark suite shows no `TypedWrap` opcodes (use `--disassemble` per `.claude/performance.md`). `bench_*.stash` perf within noise of pre-phase baseline (expect a tiny positive: one less dispatch case, one less per-typed-binding instruction).

### Phase 3 — Documentation, stdlib reference, tooling alignment

**Goal:** every doc and every tool agrees that annotations are advisory; every tool accepts dotted types in every type position.

**Files modified:**

- `docs/Stash — Language Specification.md` §"Type Hints" — rewrite to say:
  > "Type annotations are advisory metadata. They are surfaced by editor tooling (hover, completion, diagnostics) and consumed by the static analyzer, but they are erased at compile time and have no effect on runtime behavior. A value that does not match its annotation will not raise a runtime error; for explicit runtime checks use `is` or `as`. For value conversion (e.g., narrowing a number to a `byte`) use the `conv` namespace."
  - Update the grammar production for type annotations to use the unified `typeExpression` form (per §4.2) at every position. Show dotted forms (`catch (diff.ParseError e)`, `extend diff.DiffOptions`, `is diff.Edit`) as supported.
  - Remove any prose claiming runtime checking. Search for "runtime error", "runtime check", "checked at runtime" in §"Type Hints" and adjacent sections; replace or delete.
- `docs/Stash — Standard Library Reference.md` — generated; do not edit by hand. If `conv.byte()` and friends need new metadata to support the §2.1 migration path, update `Stash.Stdlib/BuiltIns/ConvBuiltIns.cs` `[StashFn]` attributes and regenerate per `.claude/language-changes.md` (`dotnet run --project Stash.Docs/`).
- `Stash.Lsp/Handlers/SemanticTokensHandler.cs`, `CompletionHandler.cs`, `HoverHandler.cs` — verify each handles `TypeExpression` cleanly for all positions. The `5165e9d`-era 11 special cases in `SemanticTokenWalker` collapse to one (this happens in Phase 1 already; Phase 3 just confirms no regressions remain).
- `Stash.Playground/wwwroot/js/stash-language.js` — Monaco/Monarch tokenizer: verify that type-position highlighting works for dotted forms in `catch`, `extend`, `is`. Update tokenization rules if a position currently misclassifies.
- `.vscode/extensions/stash-lang/syntaxes/stash.tmLanguage.json` — same audit as the Playground grammar. The TextMate grammar likely needs new captures or pattern adjustments at the six previously-non-uniform positions.
- `Stash.Analysis/` — ensure `UnknownTypeRule` and the other type-position-touching rules work for all positions, not just `: T` annotations (the audit found `catch`, `extend`, struct init were never reached by it).

**Example script (per `.claude/language-changes.md`):**

- Add `examples/type_hints.stash` showing: simple, dotted, and array annotations across `let`, `fn`, `for-in`, `catch`, `extend`, struct init; the analyzer warning for `let b: byte = 200`; the migration to `conv.byte(200)`; and a comment block stating "annotations are advisory — they document intent and inform tooling but do not enforce at runtime."

**Tests added:**

- `Stash.Tests/Docs/StandardLibraryReferenceTests` — auto-runs the generator; should pass.
- A new doctest or script test that runs `examples/type_hints.stash` and asserts expected `io.println` output.

**Verification at end of phase:** docs regenerated and committed. Playground and VS Code grammar visually correct for all six previously-broken positions. All `.claude/language-changes.md` checklist items (Documentation, Tooling Compatibility, Example Script, Tests) checked off.

---

## 6. Spec quality checklist

- [x] **Syntax is unambiguous** — single grammar production in §4.2.
- [x] **Semantics are explicit** — §2 covers user-visible runtime changes; §4 covers internal semantics; §4.6 enumerates what is unchanged.
- [x] **Interaction with existing features analyzed** — `is`, `catch`, `extend`, struct init, error handling, import resolver, doc generator all addressed.
- [x] **Cross-platform behavior** — N/A; this is a frontend/analyzer/VM refactor with no OS-level dependencies.
- [x] **Parser, interpreter, analysis impacts enumerated** — §4 and §5 list specific files and changes.
- [x] **LSP/DAP implications noted** — §4.6 (no API surface change), §5 Phase 3 (verify tooling consistency).
- [x] **Test scenarios outlined** — each phase in §5 lists tests to add/delete/update.
- [x] **Migration/breaking changes called out** — §2.1 (`byte` narrowing removed), §2.2 (`T[]` element wrapping removed). User-impact justified by "no live users yet."

---

## 7. What's NOT in scope

Explicitly **out of scope** for this work, even though the `TypeExpression` hierarchy is designed to admit them later:

- **Generics** (`List<int>`, `Result<T, E>`). Reserved as `GenericType` in the AST hierarchy but not implemented.
- **Union types** (`int | string`). Reserved as `UnionType` but not implemented. The `|` in catch clauses remains a catch-grammar separator, not a union constructor.
- **Nullable types** (`T?`). Reserved as `NullableType` but not implemented.
- **Function types** (`fn(int) -> string`). Reserved as `FunctionType` but not implemented.
- **Intersection types** (`A & B`).
- **A separate `stash check` command** for ahead-of-time type checking. The existing analyzer pass continues to run during normal compilation; no new CLI entry point.
- **A `ResolvedType` analyzer field** distinct from the source-form string. `SymbolInfo.TypeHint` stays as a string in this scope. Adding `ResolvedType` is a sensible follow-up but interacts with generics and import resolution in ways that should be designed as one piece.
- **Subtyping** (`int` ⊆ `float`?), **interface satisfaction checks at runtime**, **structural typing**. The four `*TypeMismatchRule` analyzer rules continue to do raw string equality.
- **Unifying the three primitive-type lists** (`StdlibRegistry.ValidTypes`, `_knownTypeNames`, the `TypeInferenceEngine` literal switch). The audit identified this; it is its own focused refactor and benefits from `TypeExpression` having landed first.

These are all follow-ups *enabled* by the work in this spec, not part of it.

---

## Decision Log

- **2026-05-16** — Original audit created in response to user concern triggered by `5165e9d` (under previous filename *Type Hints — Architectural Audit and Evolution*). No design committed at that point.
- **2026-05-16** — User (Cristian) decided: Option A (full erasure). Spec rewritten in place as a decided, phased plan. File renamed to *Type Hints — Canonical TypeExpression Refactor (Erasure Model)*. The original exploratory audit content is preserved in this file's git history. Key consequences recorded:
  - `byte` narrowing removed in favor of explicit `conv.byte()` + new `TypeHintLiteralMismatch` analyzer warning (§2.1).
  - `T[]` element wrapping removed; literal arrays always produce plain `List<StashValue>` (§2.2).
  - `EmitTypeWrapping` and `OpCode.TypedWrap` deleted (Phase 2); the latent `diff.Edit[]` truncation bug ceases to exist by deletion (§2.3).
  - `StashTypedArray` survives if any stdlib path still produces one; otherwise deleted in Phase 2. Decision deferred to the implementing orchestrator after a grep.
  - Language specification §"Type Hints" rewritten to honestly say "annotations are advisory" (Phase 3).
