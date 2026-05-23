## Performance Analysis: Lexer & Parser

### Tier 1 — High-Impact Wins

#### 1. `SourceSpan` is a heap-allocated `record` (class)

This is the single biggest allocation hotspot. Every token creates a `SourceSpan`, every AST node creates one, and `MakeSpan()` creates yet another. For a 500-line file you're looking at thousands of `SourceSpan` heap objects.

**Fix:** Change `record SourceSpan` → `readonly record struct SourceSpan`. Since it's already compared by value (record semantics) and only holds primitives + a string, this is a safe change that eliminates one GC allocation per token and per AST node.

**Risk:** Any code that checks `SourceSpan` for `null` would break. You'd need to audit for `SourceSpan?` usage and null checks.

#### 2. `Declaration()` and `Statement()` dispatch is O(n) sequential Match calls

Every single expression statement (the most common statement type) passes through **9 failed Match calls** in `Declaration()` then **10+ failed Match/Check calls** in `Statement()` before reaching `ExpressionStatement()`. Each `Match()` calls `Check()` → `Peek()` → list index → compare.

**Fix:** Single `Peek().Type` then `switch`:

```csharp
private Stmt? Declaration()
{
    try
    {
        return Peek().Type switch
        {
            TokenType.Let => { Advance(); return VarDeclaration(); },
            TokenType.Const => { Advance(); return ConstDeclaration(); },
            TokenType.Async => { Advance(); return FnDeclaration(isAsync: true, asyncToken: Previous()); },
            // ... etc
            _ => Statement()
        };
    }
    catch (ParseError) { Synchronize(); return null; }
}
```

Same for `Statement()`. This is O(1) dispatch instead of O(n).

#### 3. Whitespace enters `ScanToken()` method for no reason

The `ScanTokens()` loop calls `ScanToken()` for every character, including whitespace. For a typical file, ~30-40% of characters are spaces/tabs/newlines. That's thousands of `ScanToken()` entries that just hit the `case ' ':` branch and return.

**Fix:** Add a fast whitespace skip directly in `ScanTokens()`:

```csharp
while (!IsAtEnd)
{
    // Fast-skip whitespace without entering ScanToken
    char c = _source[_current];
    if (c == ' ' || c == '\t' || c == '\r') { _current++; _column++; continue; }
    if (c == '\n') { _current++; _line++; _column = 1; continue; }

    _start = _current;
    _startLine = _line;
    _startColumn = _column;
    ScanToken();
}
```

Saves method call overhead + the switch dispatch for the most common character class.

#### 4. Static lexeme strings for punctuation/operators

`AddToken(TokenType.LeftParen)` calls `_source[_start.._current]` which allocates a new `"("` string **every time** a `(` is scanned. Same for `)`, `{`, `}`, `[`, `]`, `,`, `;`, `:`, `+`, `-`, etc. These are always the same strings.

**Fix:** Use a static string table for single/double/triple character operator lexemes. The `AddToken(TokenType type)` method could look up from a `TokenType → string` dictionary or just inline the constants:

```csharp
case '(': AddTokenDirect(TokenType.LeftParen, "("); break;
```

Where `AddTokenDirect` skips the substring allocation. For a 1000-line file, this eliminates hundreds of redundant small string allocations.

#### 5. Pre-allocate `_tokens` list capacity

`_tokens` starts empty and grows via doubling. A reasonable heuristic: ~1 token per 4-5 source characters.

**Fix:** In the constructor:
```csharp
_tokens = new List<Token>(source.Length / 4);
```

Avoids multiple list resize + copy operations during lexing.

---

### Tier 2 — Medium-Impact Improvements

#### 6. `ScanString()` double-scans every plain string

The method first does a lookahead scan to check for `${` markers, then if none found, scans again to build the string content. Every non-interpolated string is read twice.

**Fix:** Combine into a single pass: scan character-by-character, and if you hit `${`, either switch to the interpolated path mid-stream or flag it and re-delegate. Alternatively, start building the StringBuilder immediately and only pivot to interpolation scanning when `${` is hit (carrying over the already-scanned text).

#### 7. `ParseInterpolatedString` / `ParseCommandLiteral` copy token lists

```csharp
var tokensWithEof = new List<Token>(innerTokens);  // copies entire list
tokensWithEof.Add(new Token(TokenType.Eof, ...));
```

This happens for every `${expr}` interpolation in every string and command literal. Two allocations (new list + copy) per interpolation.

**Fix:** Append the EOF token during lexing in `ScanInterpolatedExpression` instead of during parsing. Then the parser can use the list directly.

#### 8. `Match(params TokenType[])` allocates an array

In `Assignment()`:
```csharp
if (Match(TokenType.PlusEqual, TokenType.MinusEqual, TokenType.StarEqual,
          TokenType.SlashEqual, TokenType.PercentEqual, TokenType.QuestionQuestionEqual,
          TokenType.AmpersandEqual, TokenType.PipeEqual, TokenType.CaretEqual,
          TokenType.LessLessEqual, TokenType.GreaterGreaterEqual))
```

This allocates an 11-element `TokenType[]` array on **every assignment parse** (which is every expression). You already have 1/2/3-type non-allocating overloads — add a "compound assignment check" that uses a direct approach instead:

**Fix:** Either add more fixed-arity overloads, or replace with a helper like:
```csharp
private bool IsCompoundAssignmentOp() => Peek().Type is
    TokenType.PlusEqual or TokenType.MinusEqual or ... ;
```

#### 9. `IsLambdaStart()` speculative scan on every `(`

Every `(` in expression context triggers `IsLambdaStart()`, which saves position and scans ahead through what might be a full parameter list. For deeply nested function calls like `f(g(h(x)))`, that's 3 speculative scans that all fail.

**Fix:** Add a cheaper early-out. If the first token after `(` is not `)`, `Identifier`, or `DotDotDot`, it cannot be a lambda — bail immediately without the full scan. This handles the common case of `(expr)` grouping with a single token check.

---

### Tier 3 — Small/Niche Improvements

#### 10. `StripCommonIndent` uses `string.Split('\n')`

Allocates a `string[]` for every triple-quoted string. Could use `ReadOnlySpan<char>` line enumeration, but triple-quoted strings are uncommon enough that this rarely matters.

#### 11. `ConsumeWhile` takes a `Func<char, bool>` delegate

Used in `ScanSemVer()`. The `static` keyword avoids capture allocation, but delegate dispatch still has overhead vs. inlined loops. Low frequency (semver literals are rare).

#### 12. `List<Stmt>` in ParseBlock/ParseProgram has no capacity hint

Minor — blocks are typically small. Not worth optimizing unless profiling shows frequent list resizing.

---

### What NOT to change

- **`FrozenDictionary` with `AlternateLookup<ReadOnlySpan<char>>`** — Already optimal for keyword lookup.
- **`string.Intern` for identifiers** — Good for memory.
- **Recursive descent structure** — Correct and clean. Pratt parsing would be faster but the refactor cost is enormous.
- **Exception-based error recovery** — Only hits on malformed input. Zero-cost on the happy path.

---

### Summary — Recommended Priority

| # | Change | Impact | Effort |
|---|--------|--------|--------|
| 1 | `SourceSpan` → `readonly record struct` | Very High | Medium (null audit) |
| 2 | Switch-dispatch in `Declaration()`/`Statement()` | High | Low |
| 3 | Fast whitespace skip in `ScanTokens()` | High | Low |
| 4 | Static lexeme strings for operators | High | Low |
| 5 | Pre-allocate `_tokens` capacity | Medium | Trivial |
| 6 | Single-pass `ScanString()` | Medium | Medium |
| 7 | Avoid token list copy in interpolation parse | Medium | Low |
| 8 | Eliminate `params` array in compound assignment Match | Medium | Low |
| 9 | Cheaper `IsLambdaStart()` early-out | Medium | Low |
