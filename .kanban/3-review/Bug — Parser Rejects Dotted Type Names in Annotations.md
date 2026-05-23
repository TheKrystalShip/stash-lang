# Bug — Parser Rejects Dotted Type Names in Annotations

> **Status:** Ready
> **Created:** 2026-05-15
> **Purpose:** The parser does not accept namespace-qualified (dotted) type names such as `types.DiffOptions` in any type annotation site. Fix `ParseTypeHint` to consume one-or-more dot-separated identifiers and propagate the full dotted name through the AST and downstream tooling.

---

## 1. Problem Statement

### Repro

File: `/home/heisen/stash-lang/examples/packages/diff/index.stash` (line 22)

```stash
import "lib/types.stash" as types;

fn diffLines(a: str, b: str, options: types.DiffOptions) -> types.DiffResult {
    return lines(a, b, options);
}
```

### Observed LSP errors

- `[index.stash 22:44] Error at '.': Expected ')' after parameters.`
- `[index.stash 24:1] Error at '}': Expected expression.`

### Root cause

`ParseTypeHint` at `Stash.Core/Parsing/Parser.cs:3175` consumes exactly one identifier token and then either matches `[]` or returns. When it sees `types`, it consumes it as the type name and returns; the next token is `.`, which falls back to the caller (parameter parser), which then fails because it expected `,` or `)`.

```csharp
// Stash.Core/Parsing/Parser.cs:3175
private TypeHint ParseTypeHint()
{
    Token typeName = Consume(TokenType.Identifier, "Expected type name after ':'.");
    if (Match(TokenType.LeftBracket)) { /* ... */ }
    return new TypeHint(typeName, false, typeName.Span);
}
```

There is no support for dotted names. The `TypeHint` AST record (`Stash.Core/Parsing/AST/TypeHint.cs`) stores a single `Token Name`.

---

## 2. Scope — All Affected Annotation Sites

Every call to `ParseTypeHint()` is affected. Verified call sites in `Stash.Core/Parsing/Parser.cs`:

| Line | Site                                                                 |
| ---- | -------------------------------------------------------------------- |
| 242  | `let x: T = ...` — variable declaration                              |
| 286  | `const x: T = ...` — constant declaration                            |
| 416  | `fn f(...rest: T)` — rest parameter (named-function form)            |
| 436  | `fn f(p: T)` — regular function parameter                            |
| 458  | `fn f(...) -> T` — function return type                              |
| 512  | `struct S { field: T }` — struct field annotation                    |
| 611  | Anonymous/lambda function parameter type                             |
| 622  | Anonymous/lambda return type                                         |
| 633  | Anonymous struct-literal field type (where applicable)               |
| 1113 | `for i, v: T in iter` — for-in loop variable type                    |
| 2135 | `onRetry (n: T, err)` — retry hook attempt-parameter type            |
| 2140 | `onRetry (n, err: T)` — retry hook error-parameter type              |
| 2886 | Alternate rest-parameter parse path                                  |
| 2906 | Alternate regular-parameter parse path                               |

All of these reject `types.DiffOptions` today. Fixing `ParseTypeHint` fixes every site in one place.

### Sites NOT affected (verified)

- `catch (T1 | T2 e)` — uses a custom identifier-list loop (`Parser.cs:1312–1333`), not `ParseTypeHint`. Out of scope for this bug; tracked separately if needed.
- `is T` / `is T[]` patterns (`Parser.cs:1795–1833`) — already calls `Call()` for the general identifier-expression case, so `x is types.DiffOptions` may already work via the expression path. Confirm during implementation; no spec change required.
- `extend TypeName { ... }` (`Parser.cs:655`) — extends a single declared type, not a type annotation. Out of scope.

---

## 3. Proposed Fix

### 3.1 Grammar update

```
TypeAnnotation := DottedName ( '[' ']' )?
DottedName     := Identifier ( '.' Identifier )*
```

Examples now accepted:

- `int`, `string`, `MyStruct` (unchanged)
- `int[]`, `MyStruct[]` (unchanged)
- `types.DiffOptions` (new)
- `pkg.types.DiffResult` (new — supports re-exported aliases)
- `types.DiffOptions[]` (new — typed array of a qualified type)

### 3.2 AST change — `Stash.Core/Parsing/AST/TypeHint.cs`

`TypeHint` currently stores a single `Token Name`. Extend it to carry the full dotted path while preserving the `Lexeme` contract (downstream code reads `typeHint.Lexeme` heavily — see Section 4).

Recommended shape:

```csharp
public record TypeHint(
    Token Name,                 // first (head) identifier — kept for source-span anchoring
    IReadOnlyList<Token>? Path, // null or empty when not dotted; otherwise [Name, ...subsequent idents]
    bool IsArray,
    SourceSpan Span)
{
    public string Lexeme {
        get {
            string baseName = Path is { Count: > 0 }
                ? string.Join('.', Path.Select(t => t.Lexeme))
                : Name.Lexeme;
            return IsArray ? $"{baseName}[]" : baseName;
        }
    }
}
```

Rationale: the existing `Name` token remains the primary span anchor used by `SemanticTokenWalker.EmitTypeReference(stmt.TypeHint.Name)` (see `Stash.Analysis/Visitors/SemanticTokenWalker.cs:291, 304, 381, 813, 817`). A second collection holds the trailing tokens so semantic highlighting can emit a token range for each segment. `Lexeme` continues to return the full dotted string, which `SymbolCollector` already consumes (`SymbolCollector.cs:603, 619, 810` and parameter/field collection sites).

Backward-compat: when `Path` is null/empty, behavior matches today exactly.

### 3.3 Parser change — `Stash.Core/Parsing/Parser.cs:3175`

```csharp
private TypeHint ParseTypeHint()
{
    Token head = Consume(TokenType.Identifier, "Expected type name after ':'.");
    List<Token>? path = null;
    while (Check(TokenType.Dot))
    {
        Advance(); // consume '.'
        Token next = Consume(TokenType.Identifier, "Expected identifier after '.' in type name.");
        path ??= new List<Token> { head };
        path.Add(next);
    }

    Token last = path is { Count: > 0 } ? path[^1] : head;
    bool isArray = false;
    Token endToken = last;
    if (Match(TokenType.LeftBracket))
    {
        Consume(TokenType.RightBracket, "Expected ']' after '[' in typed array type.");
        isArray = true;
        endToken = Previous();
    }

    var span = new SourceSpan(head.Span.File, head.Span.StartLine, head.Span.StartColumn,
        endToken.Span.EndLine, endToken.Span.EndColumn);
    return new TypeHint(head, path, isArray, span);
}
```

No other parser sites change — every annotation site funnels through `ParseTypeHint`.

### 3.4 Downstream consumers

| File / Line                                                          | Change required                                                                                                                                     |
| -------------------------------------------------------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------- |
| `Stash.Analysis/Visitors/SymbolCollector.cs:158`                     | `RecordTypeReference` records a reference for `typeHint.Name.Lexeme`. Update to record a reference for the **head** segment only (the namespace alias) when `Path` is non-empty. Optionally also record the tail as a member reference — a follow-up if desired. |
| `Stash.Analysis/Visitors/SymbolCollector.cs:603, 619, 810`           | Already read `typeHint.Lexeme` — works unchanged once `Lexeme` returns the dotted string.                                                           |
| `Stash.Analysis/Visitors/SemanticTokenWalker.cs:291, 304, 381, 813, 817` | `EmitTypeReference(stmt.TypeHint.Name)` highlights only the head identifier. Extend `EmitTypeReference` (or call a new `EmitDottedTypeReference`) to also tokenize each `Path[i]` after the head with the `type` semantic kind. |
| `Stash.Analysis/Resolvers/ImportResolver.cs:216, 236`                | Reads stringified type hints — already dotted-safe (strings round-trip).                                                                            |
| `Stash.Lsp/Handlers/{Hover,Completion,TypeDefinition,Implementation}Handler.cs` | All consume `SymbolInfo.TypeHint` (a string). Verify these handlers correctly resolve a dotted name (`types.DiffOptions`) to the imported alias plus member. For this bugfix it is sufficient for them not to crash — full hover/completion on dotted types may need a follow-up. |
| `Stash.Bytecode/` compiler                                           | Type annotations are currently advisory at runtime — the compiler does not enforce them. Verify no compiler path crashes on a `Path != null` hint.  |

---

## 4. Tooling Impact (per `.claude/language-changes.md`)

| Component        | Change?         | Detail                                                                                                                                                  |
| ---------------- | --------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------- |
| **LSP — semantic tokens** | Yes  | `SemanticTokenWalker.EmitTypeReference` only emits the head token. Add emission for each path segment so `types.DiffOptions` gets `type` highlighting on both parts (or `namespace` + `type`, designer's choice — recommend `type` for the head when it's part of an annotation since the user's intent is a type reference). |
| **LSP — completion**      | Verify | After typing `: types.`, completion should offer struct/enum names exported from the `types` alias. May already work via the dot-expression completion path — verify against `Stash.Lsp/Handlers/CompletionHandler.cs:517`. |
| **LSP — hover**           | Verify | `HoverHandler` reads `SymbolInfo.TypeHint` (a string). Hovering a parameter typed `types.DiffOptions` should show the dotted string. No code change expected — just verify. |
| **LSP — diagnostics**     | No    | `SymbolCollector.RecordTypeReference` currently filters via `StdlibRegistry.ValidTypes`; for dotted types, recording the head-only reference avoids spurious "unknown type" warnings on the tail. |
| **VS Code TextMate grammar** | Verify | `.vscode/extensions/stash-lang/syntaxes/stash.tmLanguage.json` — confirm the type-annotation pattern (after `:` / `->`) accepts `\b[A-Za-z_][\w]*(\.[A-Za-z_][\w]*)*\b`. If the existing pattern only matches a single identifier, broaden it. |
| **Playground Monarch tokenizer** | Verify | `Stash.Playground/wwwroot/js/stash-language.js` — same check. The current rule set (`typeKeywords` + `@namespaces`) does not enforce single-identifier-only after `:`, so this is likely already fine. |
| **Static analysis (Resolver/SemanticValidator)** | Yes | `SemanticValidator` should not flag `types.DiffOptions` as an undefined type — see SymbolCollector change above. Also confirm `SemanticValidator.cs:444, 483` (which checks specific lexemes like `RuntimeError`) is unaffected by dotted paths. |
| **DAP**                   | No    | Type annotations are not evaluated at runtime; DAP variable display uses runtime types, not annotations. |
| **Formatter (`StashFormatter`)** | Yes | `StashFormatter` must emit the full dotted name. If it currently uses `typeHint.Name.Lexeme`, switch to `typeHint.Lexeme`. Verify against `Stash.Lsp/Analysis/StashFormatter.cs`. |

---

## 5. Tests

Add unit tests in `Stash.Tests/Parsing/` (or wherever existing `ParseTypeHint` tests live):

1. `ParseTypeHint_DottedType_InFunctionParameter` — `fn f(x: a.B) {}` parses; resulting `TypeHint.Lexeme == "a.B"`.
2. `ParseTypeHint_DottedType_InReturnType` — `fn f() -> a.B {}` parses.
3. `ParseTypeHint_DottedType_InLetDeclaration` — `let x: a.B = 1;` parses.
4. `ParseTypeHint_DottedType_InConstDeclaration` — `const x: a.B = 1;` parses.
5. `ParseTypeHint_DottedType_InStructField` — `struct S { x: a.B }` parses.
6. `ParseTypeHint_DottedType_InForLoopVariable` — `for i, v: a.B in xs { }` parses.
7. `ParseTypeHint_DottedType_InRestParameter` — `fn f(...xs: a.B) {}` parses.
8. `ParseTypeHint_DottedType_InLambdaParameter` — `let f = fn(x: a.B) { };` parses.
9. `ParseTypeHint_DottedType_InLambdaReturnType` — `let f = fn() -> a.B { };` parses.
10. `ParseTypeHint_DottedType_InOnRetryParameter` — both attempt and error parameters.
11. `ParseTypeHint_MultiSegmentDotted` — `x: a.b.c` parses; lexeme is `"a.b.c"`.
12. `ParseTypeHint_DottedArray` — `x: a.B[]` parses; `IsArray == true`, lexeme `"a.B[]"`.
13. `ParseTypeHint_TrailingDot_Errors` — `x: a.` produces a parse error with message `"Expected identifier after '.' in type name."`.
14. `ParseTypeHint_DotKeyword_Errors` — `x: a.if` fails (keywords are not accepted as path segments).
15. `ParseTypeHint_SourceSpanCoversFullDottedName` — span end column matches the last segment (or `]` for arrays).

End-to-end:

16. `Example_DiffPackage_ParsesWithoutError` — load `examples/packages/diff/index.stash` and confirm zero parse diagnostics.
17. `SymbolCollector_DottedTypeHint_StoresFullLexeme` — confirm a parameter typed `types.DiffOptions` has `SymbolInfo.TypeHint == "types.DiffOptions"`.

---

## 6. Documentation Updates

Per `.claude/language-changes.md`:

- **`docs/Stash — Language Specification.md`** — update the "Type Annotations" section (and any subsection enumerating supported syntax: variables, parameters, return types, struct fields, for-in loop vars). Add a paragraph stating that type annotations may be **namespace-qualified** via a dotted path (`alias.TypeName`) and may end with `[]` for typed arrays of qualified types. Include an example using an imported alias.
- **`docs/Stash — Standard Library Reference.md`** — no change (generated; no stdlib metadata changes).
- No `[StashFn]` / `[StashError]` metadata changes required.

---

## 7. Decision Log

### Decision: Extend `TypeHint` with an optional `Path` list rather than introducing a new `DottedTypeHint` node

- **Alternatives considered:**
  - (A) Add a new `DottedTypeHint` AST node alongside `TypeHint`. Rejected: every annotation site (and every visitor) would need to branch on two types; the six-visitor invariant in `Stash.Core/CLAUDE.md` makes this expensive.
  - (B) Store only the joined string `"types.DiffOptions"` on a single `Name` token. Rejected: loses per-segment spans needed for semantic highlighting and go-to-definition on the alias.
  - (C) Store first token + raw joined lexeme string. Rejected: weaker than (B) on tooling — no spans for trailing segments — and only marginally simpler than the chosen design.
- **Rationale:** Keeping `TypeHint` as the single annotation type means downstream visitors and `SymbolInfo.TypeHint` plumbing changes are minimal. `Lexeme` already returns a string and is the contract every consumer uses; preserving that contract is what makes the fix cheap.
- **Risks:** If a future feature wants generic type parameters (`Result<a.B, Error>`) or arbitrary type expressions, this design will need to evolve into a full `TypeExpr` tree. Trigger for revisit: any proposal for generics or union types in annotations.

---

## 8. Out of Scope

- **`catch` clause type lists** — uses its own identifier-only parser (`Parser.cs:1312`). If dotted catch types are needed, file a follow-up.
- **Generic type parameters** — `a.B<T>` is not part of this fix.
- **Runtime type enforcement** — annotations remain advisory; the compiler still does not validate that the actual value matches.
- **Full LSP UX on dotted types** (completion after `: types.`, go-to-definition jumping to the resolved struct) — verify they don't crash; deeper UX is a follow-up if gaps are found.
