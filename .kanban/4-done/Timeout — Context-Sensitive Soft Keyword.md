# Timeout — Context-Sensitive Soft Keyword

**Status:** Backlog — Design
**Created:** 2026-04-28
**Author:** Architect

---

## 1. Overview

`timeout` is currently a hard-reserved keyword in Stash. It is lexed unconditionally as
`TokenType.Timeout`, which means it can never appear as an identifier — even in positions where
doing so would be syntactically and semantically unambiguous. This spec designs a solution that
makes `timeout` a **context-sensitive (soft) keyword**: the keyword is only recognized as a
TimeoutExpr when the surrounding syntax requires it. Everywhere else, it is a valid identifier.

---

## 2. Problem Statement

The `timeout` construct has very specific syntax:

```
timeout <duration_expr> { <body> }
```

It always requires a duration expression and a `{ }` block body. This means there are many
positions in the grammar where `timeout` can never be mistaken for a keyword:

```stash
let timeout = 20s;               // ERROR today — 'timeout' cannot be an identifier
fn fetch(timeout, url) { ... }   // ERROR today — 'timeout' not allowed as parameter name
return timeout;                  // ERROR today — 'timeout' as an expression reference

// All of the above are contextually unambiguous — none match the 'timeout EXPR { }' pattern
```

The motivating case from `examples/error_handling.stash` (line ~132) was:

```stash
let _timeout = try conv.toInt(cfg["timeout"] ?? "30");  // forced to use '_timeout'
```

The user had to prefix the variable with `_` solely to avoid collision with the keyword, even
though `let timeout = ...` is completely unambiguous in context.

The complementary case — `timeout` as an expression — must continue to work unchanged:

```stash
let result = timeout 20s {
    io.println("working...");
};

timeout 20s {               // statement form
    io.println("working...");
}
```

### Highlighting is also broken today

The TextMate grammar and the Monarch tokenizer (Playground) unconditionally highlight every
occurrence of `timeout` as a control-flow keyword — including field names in config dictionaries
(`cfg["timeout"]`), struct fields, and parameter names. This is incorrect and misleading.

---

## 3. Prior Art

Several widely-used languages demonstrate that contextual keywords work well in practice:

| Language   | Contextual keyword                                                                             | Resolution strategy                                                                                                                                                                                |
| ---------- | ---------------------------------------------------------------------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| **C#**     | `async`, `await`, `yield`, `var`, `dynamic`, `get`, `set`, `value`, `partial`, `where`, `from` | Soft keywords — recognized as keywords only in specific syntactic positions; otherwise valid identifiers. Roslyn's lexer emits them as identifiers; the parser uses one-token lookahead to decide. |
| **Python** | `match`, `case`, `type`                                                                        | Same pattern: `match` is only special when followed by a subject expression and `:` cases. `type` is only special when followed by a name and `=`.                                                 |
| **Kotlin** | `data`, `sealed`, `value`, `suspend`, `override`, `operator`                                   | Soft modifiers — valid as identifiers outside modifier position.                                                                                                                                   |
| **Swift**  | Backtick escaping — any keyword usable as identifier if wrapped in backticks                   | Different approach; we can learn from this as a fallback escape hatch.                                                                                                                             |

All of these languages chose to demote keywords to contextual status rather than requiring
workarounds (underscore prefixes, backtick escaping, renaming). Stash should do the same.

---

## 4. Current Architecture

### Lexer layer (`Stash.Core/Lexing/Lexer.cs`, line ~116)

```csharp
["timeout"] = TokenType.Timeout,
```

`timeout` is in the frozen keyword dictionary. Any source token with text `"timeout"` is
unconditionally emitted as `TokenType.Timeout`, never as `TokenType.Identifier`.

### Token enum (`Stash.Core/Lexing/TokenType.cs`, line ~324)

```csharp
/// <summary>The <c>timeout</c> keyword. Begins a timeout expression...</summary>
Timeout,
```

### Parser — expression level (`Stash.Core/Parsing/Parser.cs`, line ~2355)

```csharp
if (Match(TokenType.Timeout))
{
    return ParseTimeoutExpr();
}
```

No lookahead — `Timeout` is immediately dispatched to `ParseTimeoutExpr()`.

### Parser — declaration/parameter level (`Stash.Core/Parsing/Parser.cs`)

```csharp
// VarDeclaration(), ~line 197:
Token name = Consume(TokenType.Identifier, "Expected variable name.");

// Parameter parsing, ~line 360:
parameters.Add(Consume(TokenType.Identifier, "Expected parameter name."));
```

Both use `Consume(TokenType.Identifier, ...)` with no allowances for keyword tokens. Since
`timeout` emits `TokenType.Timeout`, it fails at this point.

### Existing allowances (already contextual)

The parser already allows `timeout` as a property name and dict key:

```csharp
// ConsumePropertyName(), ~line 2847:
Check(TokenType.Timeout) // explicitly allowed after '.'

// IsDictKeyToken(), ~line 2871:
type >= TokenType.Let && type <= TokenType.Timeout  // all keywords allowed as dict keys
```

These allowances only work because `TokenType.Timeout` is checked explicitly. They must be
retained (or become automatic once `timeout` is an identifier).

---

## 5. Design Decision

### Decision: Soft keyword via lexer demotion

**Remove `timeout` from the lexer's keyword table.** The lexer will emit `timeout` as
`TokenType.Identifier` with value `"timeout"` in all positions. The parser is responsible
for recognizing the TimeoutExpr construct by looking at what follows the `"timeout"` identifier.

**Alternatives considered:**

| Alternative                                                | Description                                                                                          | Why rejected                                                                                                                                           |
| ---------------------------------------------------------- | ---------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------ |
| **Keep hard keyword; add keyword-as-identifier overloads** | Add `TokenType.Timeout` to `Consume(TokenType.Identifier, ...)` everywhere an identifier is expected | Requires finding and auditing every `Consume(TokenType.Identifier)` call site (dozens). Brittle. New identifier-consuming grammar rules would miss it. |
| **Backtick escape syntax** (`let \`timeout\` = ...`)       | Allow any keyword inside backticks to be an identifier                                               | Not currently supported. Introduces a separate language mechanism just for one keyword. Ugly.                                                          |
| **Rename the keyword** (`timed`, `timebox`, `within`)      | Change `timeout` to avoid the collision entirely                                                     | Breaking change to all existing Stash code. Loses the most natural name for the construct.                                                             |
| **Require explicit parentheses for identifier use**        | `let (timeout) = 20s;`                                                                               | Bizarre. No language does this.                                                                                                                        |

**Rationale for lexer demotion:**

When `timeout` is removed from the keyword dictionary, _every_ place in the parser that calls
`Consume(TokenType.Identifier, ...)` or checks `Check(TokenType.Identifier)` automatically
starts accepting `timeout`. No surgical editing of declaration/parameter/for-loop parsing is
needed. The only new code is the disambiguation logic in `Primary()` that decides whether a
`"timeout"` identifier starts a TimeoutExpr or is a plain identifier reference.

This is the exact strategy C# Roslyn uses for `async`, `await`, and all other contextual keywords.

**Risks:**

| Risk                                            | Likelihood | Mitigation                                                                     |
| ----------------------------------------------- | ---------- | ------------------------------------------------------------------------------ |
| Disambiguation logic has an uncovered edge case | Low        | Extensive test matrix (see §8)                                                 |
| `IsDictKeyToken` range check breaks             | None       | `TokenType.Timeout` stays in the enum; range is still valid for other keywords |
| `.stashc` deserialization breaks                | None       | Bytecode encodes opcodes, not token types                                      |
| Tooling (LSP, DAP, analysis) breaks             | Low        | Only SemanticTokenWalker needs a targeted fix; all other tooling is AST-based  |

---

## 6. The Disambiguation Algorithm

When `Primary()` encounters `TokenType.Identifier` with value `"timeout"`, it must decide
whether to parse a `TimeoutExpr` or return a plain `VariableExpr`.

### Core insight

A `TimeoutExpr` always follows the grammar:

```
timeout_expr := 'timeout' duration_expr '{' ... '}'
```

The `duration_expr` is parsed via `Call()`, which can start only with:

- A literal (integer, float, duration, bytesize)
- An identifier
- A `(` (for grouped expressions)

Conversely, a plain identifier reference is always **continued** by:

- An operator: `+`, `-`, `*`, `/`, `%`, `**`, `||`, `&&`, `|`, `&`, `^`, `!`, `==`, `!=`,
  `<`, `>`, `<=`, `>=`, `??`
- A postfix/accessor: `.`, `[`, `?`
- A terminator: `;`, `,`, `)`, `]`, `:`
- A control keyword: `in`, `is`, `as`, `and`, `or`, `not`
- End of file

None of these can be the start of a `duration_expr`. The sets are disjoint — except for `(`.

### The `(` ambiguity

`timeout(args)` — calling a callable stored in `timeout` — vs.
`timeout (expr) { ... }` — TimeoutExpr with a grouped duration.

Both start with `(`. The disambiguator resolves this with bounded lookahead: scan forward past
the balanced `(...)` and check if `{` follows:

```
timeout (30s) { ... }    →  after ')': '{' found  →  TimeoutExpr  ✓
timeout(fn, x)           →  after ')': ';' or ',' found  →  identifier  ✓
timeout(a + b)           →  after ')': ';' found  →  identifier  ✓
```

This scan is O(nesting depth) — never O(file length) — and the parser already has this
infrastructure from `IsLambdaStart()` (lines ~2391 in `Parser.cs`).

### Full algorithm (pseudocode)

```
function IsTimeoutKeyword():
    peek = PeekToken(1)  // token after 'timeout'

    // ── Clear identifier continuations ──────────────────────────────────────
    if peek.Type in { EOF, ';', ',', ')', ']', ':' }:
        return false  // identifier

    if peek.Type in { '=', '+=', '-=', '*=', '/=', '%=', '**=', '??=' }:
        return false  // assignment target

    if peek.Type in { '+', '-', '*', '/', '%', '**', '|', '&', '^', '~' }:
        return false  // arithmetic / bitwise

    if peek.Type in { '||', '&&', '!', '==', '!=', '<', '>', '<=', '>=' }:
        return false  // logical / comparison

    if peek.Type in { '.', '[', '?', '??' }:
        return false  // property access, indexing, null-conditional

    if peek.Type in { 'in', 'is', 'as', 'and', 'or', 'not' }:
        return false  // keyword operators

    // ── Clear TimeoutExpr starters ───────────────────────────────────────────
    if peek.Type in { IntLit, FloatLit, DurationLit, ByteSizeLit, StringLit }:
        return true   // duration literal

    if peek.Type == Identifier:
        return true   // duration variable

    // ── Ambiguous case: '(' ─────────────────────────────────────────────────
    if peek.Type == '(':
        return IsFollowedByBlockAfterParens()  // bounded paren scan

    // ── Anything else: treat as identifier (conservative) ───────────────────
    return false
```

### `IsFollowedByBlockAfterParens()` (bounded scan)

Starting at the `(` token after `timeout`, track paren depth. When depth returns to 0,
check if the next token is `{`. Return true iff it is.

```
function IsFollowedByBlockAfterParens():
    depth = 0
    pos = current + 1  // index of '(' token
    while pos < tokens.Length:
        t = tokens[pos]
        if t.Type == '(': depth++
        else if t.Type == ')':
            depth--
            if depth == 0:
                return pos + 1 < tokens.Length && tokens[pos + 1].Type == '{'
        pos++
    return false
```

### Edge cases

| Expression                      | Next token after `timeout` | Result        | Notes                                              |
| ------------------------------- | -------------------------- | ------------- | -------------------------------------------------- |
| `let timeout = 20s`             | `=`                        | Identifier    | Covered by assignment set                          |
| `fn f(timeout)`                 | `)`                        | Identifier    | Covered by terminator set                          |
| `timeout.field`                 | `.`                        | Identifier    | Property access; duration can't start with `.`     |
| `timeout[0]`                    | `[`                        | Identifier    | Index; duration can't start with `[`               |
| `timeout + 5`                   | `+`                        | Identifier    | Arithmetic                                         |
| `timeout in arr`                | `in`                       | Identifier    | For-loop; `in` is a keyword operator               |
| `timeout is Error`              | `is`                       | Identifier    | Type guard                                         |
| `return timeout`                | `;` or EOF after           | Identifier    | Terminator                                         |
| `timeout 30s { ... }`           | `DurationLit`              | TimeoutExpr ✓ | Clear duration literal                             |
| `timeout myVar { ... }`         | `Identifier`               | TimeoutExpr ✓ | Duration is a variable                             |
| `timeout getDuration() { ... }` | `Identifier`               | TimeoutExpr ✓ | Duration is a function call; `Call()` consumes it  |
| `timeout (30s) { ... }`         | `(` → `{` after `)`        | TimeoutExpr ✓ | Grouped duration                                   |
| `timeout(fn, x)`                | `(` → `;` after `)`        | Identifier    | Function call                                      |
| `timeout timeout { ... }`       | `Identifier("timeout")`    | TimeoutExpr ✓ | Outer = keyword; inner = duration variable         |
| `timeout { ... }` (no duration) | `{`                        | Identifier    | Conservative fallback — invalid TimeoutExpr anyway |

**The `timeout timeout { ... }` case is valid and correct:** the outer `timeout` is parsed as
the keyword start of a TimeoutExpr; the `duration_expr` is the identifier `timeout` which
resolves to whatever value `timeout` holds at runtime. The body is the `{ ... }` block. This
reads as "run the block with a timeout equal to the current value of the `timeout` variable."

---

## 7. Implementation Plan

### Files requiring changes

| File                                                           | Change                                                                                                          | Complexity |
| -------------------------------------------------------------- | --------------------------------------------------------------------------------------------------------------- | ---------- |
| `Stash.Core/Lexing/Lexer.cs`                                   | Remove `["timeout"] = TokenType.Timeout` from keyword dict                                                      | Trivial    |
| `Stash.Core/Parsing/Parser.cs`                                 | In `Primary()`: replace `Match(TokenType.Timeout)` with `IsTimeoutKeyword()` + `ParseTimeoutExprOrIdentifier()` | Low        |
| `Stash.Core/Parsing/Parser.cs`                                 | Add `IsTimeoutKeyword()` and `IsFollowedByBlockAfterParens()` helpers                                           | Low        |
| `Stash.Analysis/Visitors/SemanticTokenWalker.cs`               | In `VisitTimeoutExpr`, emit a keyword semantic token for `TimeoutKeyword.Span`                                  | Trivial    |
| `.vscode/extensions/stash-lang/syntaxes/stash.tmLanguage.json` | Remove `timeout` from the `control-keywords` match pattern                                                      | Trivial    |
| `Stash.Playground/wwwroot/js/stash-language.js`                | Remove `timeout` from the `keywords` array                                                                      | Trivial    |

### Files that do NOT need changes

| File                                                     | Reason                                                                                                                                                                           |
| -------------------------------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `Stash.Core/Lexing/TokenType.cs`                         | Keep `TokenType.Timeout` in the enum — removes nothing, avoids downstream breakage                                                                                               |
| `Stash.Core/Parsing/Parser.cs` — `ConsumePropertyName()` | The explicit `Check(TokenType.Timeout)` becomes dead code (covered by `Check(TokenType.Identifier)`) but is harmless. Can be cleaned up separately.                              |
| `Stash.Core/Parsing/Parser.cs` — `IsDictKeyToken()`      | The range check `type >= TokenType.Let && type <= TokenType.Timeout` remains valid. `timeout` tokens are now `Identifier`, handled by the `type == TokenType.Identifier` branch. |
| `Stash.Core/Parsing/AST/TimeoutExpr.cs`                  | `TimeoutKeyword` will now hold a token with `Type = Identifier` and `Value = "timeout"`. The span is still correct. No field changes.                                            |
| `Stash.Bytecode/` — all files                            | Bytecode encodes opcodes, not token types. No change.                                                                                                                            |
| `Stash.Analysis/Visitors/SemanticValidator.cs`           | AST-based; no change.                                                                                                                                                            |
| `Stash.Analysis/Visitors/SymbolCollector.cs`             | AST-based; no change.                                                                                                                                                            |
| `Stash.Analysis/Visitors/StashFormatter.cs`              | `ExpressionPrinter.PrintTimeout` calls `ctx.EmitToken()` which emits the source text of the token, not its type. Emits `"timeout"` correctly. No change.                         |
| `Stash.Bytecode/Compilation/Compiler.ComplexExprs.cs`    | AST-based; no change.                                                                                                                                                            |
| `Stash.Bytecode/VM/VirtualMachine.ControlFlow.cs`        | Runtime; no change.                                                                                                                                                              |

### Detail: Parser changes

**Before:**

```csharp
// Primary(), ~line 2355:
if (Match(TokenType.Timeout))
{
    return ParseTimeoutExpr();
}
```

**After:**

```csharp
// Primary(), ~line 2355:
if (Check(TokenType.Identifier) && Peek().Value == "timeout" && IsTimeoutKeyword())
{
    Advance(); // consume 'timeout'
    return ParseTimeoutExpr();
}
```

Where `IsTimeoutKeyword()` and `IsFollowedByBlockAfterParens()` implement the disambiguation
algorithm from §6. `ParseTimeoutExpr()` itself is unchanged — it reads `Previous()` as the
timeout token and then calls `Call()` for the duration.

> **Note:** `Peek().Value` references the string value stored in the identifier token.
> The existing `Token` type already carries a `Value` (or equivalent) field for identifiers.
> Verify the exact property name in `Stash.Core/Lexing/Token.cs` during implementation.

### Detail: SemanticTokenWalker change

**Before (`Stash.Analysis/Visitors/SemanticTokenWalker.cs`, ~line 752):**

```csharp
public int VisitTimeoutExpr(TimeoutExpr expr)
{
    expr.Duration.Accept(this);
    foreach (var stmt in expr.Body.Statements)
        stmt.Accept(this);
    return 0;
}
```

**After:**

```csharp
public int VisitTimeoutExpr(TimeoutExpr expr)
{
    EmitKeyword(expr.TimeoutKeyword.Span);  // emit 'timeout' as keyword token
    expr.Duration.Accept(this);
    foreach (var stmt in expr.Body.Statements)
        stmt.Accept(this);
    return 0;
}
```

> **Implementation note:** Verify that the walker has (or needs) a helper like `EmitKeyword(SourceSpan)`
> that emits a `keyword` (or `namespace`?) token at the given position. Look at how other keyword
> constructs (`defer`, `retry`, `lock`) emit tokens in the walker to use the same pattern.

---

## 8. Highlighting Behavior

### Current (broken) behavior

The TextMate grammar regex `\b(defer|as|elevate|lock|retry|onRetry|until|timeout)\b` matches
every occurrence of `timeout` — including:

- `cfg["timeout"]` — string key access (the string itself doesn't get highlighted, but `timeout` in code does)
- `let _timeout = 30;` — was impossible, but `timeout` in variable names was just the keyword color
- `fn setTimeout(timeout, ms)` — `timeout` parameter highlighted as keyword (wrong)

### Target (correct) behavior after this change

| Position                                 | Highlighting              | Mechanism                                                      |
| ---------------------------------------- | ------------------------- | -------------------------------------------------------------- |
| `timeout 30s { }` — the keyword          | `keyword.control.stash`   | Semantic token (walker emits keyword at `TimeoutKeyword.Span`) |
| `timeout 30s { }` — the duration `30s`   | `constant.numeric`        | Normal literal highlighting                                    |
| `let timeout = 30s`                      | `variable`                | Normal identifier semantic token                               |
| `fn f(timeout)` — parameter              | `parameter`               | Normal parameter semantic token                                |
| `return timeout`                         | `variable` (or parameter) | Normal semantic token                                          |
| `timeout timeout { }` — inner `timeout`  | `variable`                | Normal identifier semantic token (duration position)           |
| `struct S { timeout: int }` — field name | `property`                | Normal property semantic token                                 |

### TextMate grammar change

Remove `timeout` from the control-keywords match in `stash.tmLanguage.json`:

```json
// Before:
"match": "\\b(defer|as|elevate|lock|retry|onRetry|until|timeout)\\b"

// After:
"match": "\\b(defer|as|elevate|lock|retry|onRetry|until)\\b"
```

`timeout` highlighting in TimeoutExpr context is now handled exclusively by the semantic token
walker, which correctly distinguishes the two usages.

### Monarch tokenizer change (Stash.Playground)

Remove `'timeout'` from the keywords array in `stash-language.js`. The Monarch tokenizer
is regex-based and cannot do context-sensitive matching — relying on the semantic overlay in
the Monaco editor is the right approach for the Playground, or a fallback that leaves `timeout`
unstyled in plain mode.

---

## 9. LSP / DAP Implications

### Completion

When the user types `timeout`:

- The completion provider should offer the TimeoutExpr snippet (`timeout <duration> { }`) as a
  completion item (keyword kind)
- If `timeout` is declared as a variable in scope, it should also offer that variable as a
  completion item (variable kind)
- Context (what follows the cursor) determines which is most relevant

This is a **medium-complexity LSP change** that requires the completion handler to be aware of
whether the cursor is in a position where a TimeoutExpr could begin (start of statement, start
of expression) versus in a position where only an identifier makes sense (declaration LHS,
parameter list, property name).

If the completion handler is currently driven by the keyword token list, `timeout` must be
removed from that list and handled specially.

### Hover

- Hovering `timeout` in a TimeoutExpr → show keyword documentation (current behavior, from
  the language server's keyword hover map)
- Hovering `timeout` as a variable → show variable type/value (current behavior for identifiers)

The hover handler currently uses the token type to decide. Since `timeout` tokens will now be
`TokenType.Identifier`, the LSP must check the parent AST node (or semantic token type) to
decide whether to show keyword docs vs. variable docs.

### Go-to-definition

- `timeout` as identifier → navigate to its declaration (same as any variable)
- `timeout` in TimeoutExpr → no definition (keyword); or open language docs

### Diagnostics (static analysis)

No new SA rules are needed for this change. The existing SA rules around `timeout` operate on
`TimeoutExpr` AST nodes and are unaffected by the token type change.

> **Note:** The existing `TimeoutError` analysis rules (if any) are AST-based and unaffected.

---

## 10. Test Cases

### Parser tests

| Scenario                   | Input                                                   | Expected                                             |
| -------------------------- | ------------------------------------------------------- | ---------------------------------------------------- |
| Variable declaration       | `let timeout = 30s;`                                    | Parses as `LetStmt` with name `"timeout"`            |
| Variable assignment        | `timeout = 20s;`                                        | Parses as assignment to identifier `timeout`         |
| Parameter name             | `fn f(timeout) { return timeout; }`                     | Parses parameter named `timeout`; body reads it      |
| For-loop variable          | `for (let timeout in arr) { }`                          | Parses `timeout` as the loop variable                |
| Property access            | `obj.timeout`                                           | Parses as member access; `timeout` is the field name |
| Dict key                   | `{ timeout: 30s }`                                      | Parses as dict literal with key `"timeout"`          |
| Keyword in expression      | `timeout 30s { io.println("ok"); }`                     | Parses as `TimeoutExpr`                              |
| Duration variable          | `let t = 30s; timeout t { }`                            | Parses as `TimeoutExpr` with duration identifier `t` |
| Grouped duration           | `timeout (5s + 5s) { }`                                 | Parses as `TimeoutExpr` with grouped duration        |
| Function call duration     | `timeout getDuration() { }`                             | Parses as `TimeoutExpr` with function call duration  |
| Ambiguous paren — call     | `timeout(fn, x);`                                       | Parses as call expression on identifier `timeout`    |
| Identifier in return       | `return timeout;`                                       | Parses as `return` of identifier `timeout`           |
| Identifier in expression   | `let x = timeout + 5;`                                  | Parses as addition with identifier `timeout`         |
| Self-referencing           | `timeout timeout { }`                                   | Outer=TimeoutExpr; inner=identifier as duration      |
| Nested in timeout          | `timeout 30s { let timeout = 5s; timeout timeout { } }` | Inner declaration + inner TimeoutExpr                |
| Struct field named timeout | `struct Config { timeout: int }`                        | Struct definition with `timeout` field               |
| Is-check                   | `timeout is Error`                                      | Identifier `timeout`, `is` check                     |
| In-check                   | `timeout in arr`                                        | Identifier `timeout`, `in` check                     |

### Execution tests

| Scenario                     | Input                                                | Expected                          |
| ---------------------------- | ---------------------------------------------------- | --------------------------------- |
| Variable stores duration     | `let timeout = 30s; timeout`                         | Evaluates to `30s` duration value |
| Timeout expr executes        | `timeout 30s { 42 }`                                 | Returns `42`                      |
| Variable as timeout duration | `let timeout = 30s; let r = timeout timeout { 99 };` | Returns `99` within 30s           |
| Closure captures `timeout`   | `let timeout = 5; fn f() { return timeout; } f()`    | Returns `5`                       |

### Semantic token tests

| Scenario            | Expected token at `timeout` span             |
| ------------------- | -------------------------------------------- |
| `timeout 30s { }`   | `keyword` type                               |
| `let timeout = 30s` | `variable` type with `declaration` modifier  |
| `return timeout;`   | `variable` type                              |
| `fn f(timeout)`     | `parameter` type with `declaration` modifier |

### Formatting tests

| Scenario              | Expected formatted output         |
| --------------------- | --------------------------------- |
| `let timeout = 20s;`  | `let timeout = 20s;` (no change)  |
| `timeout   30s   { }` | `timeout 30s { }` (single spaces) |

### Error tests

| Scenario                    | Expected error                                               |
| --------------------------- | ------------------------------------------------------------ |
| `timeout { }` (no duration) | Parse error — missing duration expression (no regression)    |
| `timeout = 5s` (no `let`)   | Parse error — assignment in invalid position (no regression) |

---

## 11. Breaking Changes

**There are no breaking changes to Stash source code.**

- Any code that used `timeout` as a TimeoutExpr continues to parse and execute identically.
- No existing valid Stash code uses `timeout` as an identifier (it was a parse error before).
  Therefore, no existing code breaks.

**Bytecode (`.stashc`) compatibility:**

- No bytecode changes. The disambiguation happens at parse time. `OpCode.Timeout` and the
  compiled instruction encoding are unchanged.
- Pre-compiled `.stashc` files are fully compatible.

**Tooling (LSP, DAP, static analysis):**

- The `SemanticTokenWalker` change is additive (emitting a new token where none was emitted
  before). Clients that don't handle the new token will simply not highlight the keyword — an
  acceptable degradation.
- Static analysis rules are AST-based and unaffected.

---

## 12. Scope Boundary: `timeout` Only

This spec addresses `timeout` specifically. Other Stash keywords that might benefit from the
same treatment — `retry`, `lock`, `defer`, `elevate`, `async`, `await` — are out of scope.

If this feature proves successful, a follow-on spec should evaluate each remaining keyword for
soft-keyword promotion. The infrastructure added here (the disambiguation helper pattern) will
be directly reusable.

> **Future consideration:** A general `IsContextualKeyword(string name)` mechanism could
> systematically handle a set of soft keywords, rather than per-keyword ad-hoc logic. Not
> needed now.

---

## 13. Open Questions

### OQ-1: Token type for `TimeoutKeyword` field

With the soft keyword approach, `TimeoutExpr.TimeoutKeyword` will hold a `Token` with
`Type = TokenType.Identifier` and `Value = "timeout"`. The `TokenType.Timeout` enum entry
becomes unreachable from new code. Should it be deprecated? Or is it acceptable to leave it
as unused dead code in the enum?

**Recommendation:** Leave it. Removing an enum entry is a binary-breaking change for any
external consumer of the Stash library. Document it as "reserved, unused after v1.x."

### OQ-2: SemanticTokenWalker — what token type for the keyword?

The walker must emit a semantic token for `timeout` in TimeoutExpr. What type?

Looking at the existing taxonomy (§ Semantic Token Taxonomy spec):

- `keyword` is not in the current semantic token type set (removed in the Phase 1 & 2 refactoring)
- The taxonomy uses `namespace`, `type`, `struct`, `enum`, `interface`, `function`, `method`,
  `parameter`, `variable`, `property`, `enumMember`

This means there is **no `keyword` semantic token type** in the current walker. The `timeout`
keyword in TimeoutExpr is currently highlighted by the TextMate grammar, not by semantic tokens.

After this change, the TextMate grammar no longer highlights `timeout`. The walker must fill
the gap. Options:

**A)** Add a `keyword` (or `controlKeyword`) token type to the semantic token taxonomy.
**B)** Leave `timeout` in the TextMate grammar specifically when followed by the TimeoutExpr
pattern (requires regex lookahead, which `.tmLanguage.json` supports via `begin`/`end` pairs).
**C)** Accept that `timeout` in TimeoutExpr has no highlighting until the LSP provides semantic tokens
(i.e., in editors without semantic token support, `timeout` appears as plain text).

> **Status:** This is a decision point requiring input. Options A and B are both viable.
> Option A is the cleanest long-term solution (the taxonomy gap is real — keywords like `defer`,
> `lock`, `retry` also rely on TextMate for their highlighting). Option B is a targeted workaround.
> This question should be resolved before implementation begins.

**Answer:** Option A is the cleanest and provides benefits in the long run, this is the correct choice to implement.

### OQ-3: `timeout` string value check in parser

The implementation check `Peek().Value == "timeout"` depends on the `Token` struct having a
readable string value for identifier tokens. Verify the exact field name in
`Stash.Core/Lexing/Token.cs` and `Stash.Core/Lexing/Lexer.cs`.

Alternatively, the implementation could define a `const string TimeoutKeyword = "timeout"` in
a shared location to avoid magic string repetition.

**Answer:** If the "timeout" keyword only needs to be checked one or two times then it can be inlined, otherwise extract to a const string and reference.

---

## Decision Log

| Date       | Decision                                                                 | Notes                                                                                                                                                             |
| ---------- | ------------------------------------------------------------------------ | ----------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| 2026-04-28 | Chose lexer-demotion (soft keyword) over parser-level contextual keyword | Lexer demotion propagates automatically to all identifier-consuming grammar rules. Parser-level approach requires auditing every `Consume(Identifier)` call site. |
| 2026-04-28 | Kept `TokenType.Timeout` in the enum                                     | Removing an enum entry is a binary-breaking change. Marked as unused, not deleted.                                                                                |
| 2026-04-28 | Scope limited to `timeout` only                                          | `retry`, `lock`, `defer` etc. may follow in a separate spec.                                                                                                      |
| 2026-04-28 | OQ-2 (keyword semantic token type) flagged as open                       | The Phase 1/2 taxonomy refactoring removed `keyword` from semantic types. This must be resolved before implementation.                                            |
