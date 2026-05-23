### Opportunity 1 — `ValueStringBuilder` Pattern for String/Interpolated/Command Scanning

**The problem:** 9 instances of `new StringBuilder()` across the lexer. Each one allocates the `StringBuilder` object + an internal `char[16]` buffer on the heap, then resizes with more allocations as content is appended. This happens for _every_ string literal, interpolated string, command literal, and triple-quoted string.

**The pattern:** .NET's own runtime uses an internal `ValueStringBuilder` — a `ref struct` that starts with a `Span<char>` from `stackalloc`, then overflows into `ArrayPool<char>.Shared` if needed. Since it's a `ref struct`, it lives entirely on the stack with zero GC pressure for the common case (strings under ~256 chars).

The structure is roughly:

```csharp
ref struct ValueStringBuilder
{
    private char[]? _arrayToReturnToPool;
    private Span<char> _chars;
    private int _pos;

    public ValueStringBuilder(Span<char> initialBuffer)
    {
        _arrayToReturnToPool = null;
        _chars = initialBuffer;
        _pos = 0;
    }

    public void Append(char c) { /* grow into ArrayPool if needed */ }
    public override string ToString() => _chars[.._pos].ToString();
    public void Dispose() { if (_arrayToReturnToPool != null) ArrayPool<char>.Shared.Return(_arrayToReturnToPool); }
}
```

**Where it applies:**

| Method                                  | Current                                  | Replacement                                                                     |
| --------------------------------------- | ---------------------------------------- | ------------------------------------------------------------------------------- |
| `ScanString()` (L700)                   | `var sb = new StringBuilder();`          | `Span<char> buf = stackalloc char[256]; var vsb = new ValueStringBuilder(buf);` |
| `ScanTripleQuotedString()` (L830)       | `var sb = new StringBuilder();`          | Same pattern                                                                    |
| `ScanTripleQuotedInterpolated()` (L950) | `var textSegment = new StringBuilder();` | Same pattern                                                                    |
| `ScanInterpolatedString()` (L1188)      | `var textSegment = new StringBuilder();` | Same pattern                                                                    |
| `ScanCommandLiteral()` (L1300)          | `var textSegment = new StringBuilder();` | Same pattern                                                                    |
| `StripCommonIndent()` (L1090)           | `var sb = new StringBuilder();`          | Same pattern                                                                    |
| `StripCommonIndentParts()` (L1160)      | `var sb = new StringBuilder();`          | Same pattern                                                                    |

**Impact:** Eliminates 2+ heap allocations per string/command token (StringBuilder + its backing array). For a typical 500-line script with ~50 strings and ~20 commands, that's ~140 fewer GC objects.

**Caveat:** `ValueStringBuilder` is a `ref struct`, so it can't be stored in the `List<object> parts` used for interpolated strings. You'd use it only for the text-segment accumulation, then call `.ToString()` when flushing to the parts list — which is exactly what the current code does with `textSegment.ToString()`.

---

### Opportunity 2 — Span-Based Number Parsing (Eliminate `string.Replace("_", "")`)

**The problem:** Five instances of `lexeme[2..].Replace("_", "")` or `lexeme.Replace("_", "")` in number scanning. Each one:

1. Allocates a substring via `[2..]` (heap)
2. Allocates another string via `.Replace()` (heap) — even if there are no underscores
3. Then parses the result

```csharp
// Current: 3 allocations (slice + replace + the string itself)
string lexeme = _source[_start.._current];
string digits = lexeme[2..].Replace("_", "");
long value = Convert.ToUInt64(digits, 16);
```

**The fix:** Write digits into a `Span<char>` from `stackalloc`, skipping underscores, then parse directly from the span:

```csharp
ReadOnlySpan<char> raw = _source.AsSpan(_start + 2, _current - _start - 2);
Span<char> digits = stackalloc char[raw.Length]; // max possible length
int len = 0;
foreach (char c in raw)
{
    if (c != '_') digits[len++] = c;
}
// long.TryParse and int.Parse both accept ReadOnlySpan<char> on .NET 10
long value = ParseHexSpan(digits[..len]);
```

However, `Convert.ToUInt64(string, int base)` doesn't have a span overload. You'd need a small custom `ParseHexSpan`/`ParseOctalSpan`/`ParseBinarySpan` helper that operates on `ReadOnlySpan<char>`. For decimal, `long.TryParse(ReadOnlySpan<char>, ...)` works out of the box.

**Where it applies:**

| Method                | Line  | Current                                                         |
| --------------------- | ----- | --------------------------------------------------------------- |
| `ScanHexLiteral()`    | ~2034 | `lexeme[2..].Replace("_", "")` + `Convert.ToUInt64(digits, 16)` |
| `ScanOctalLiteral()`  | ~2105 | `lexeme[2..].Replace("_", "")` + `Convert.ToUInt64(digits, 8)`  |
| `ScanBinaryLiteral()` | ~2176 | `lexeme[2..].Replace("_", "")` + `Convert.ToUInt64(digits, 2)`  |
| `ScanDecimalNumber()` | ~2286 | `floatLexeme.Replace("_", "")` + `double.TryParse`              |
| `ScanDecimalNumber()` | ~2301 | `lexeme.Replace("_", "")` + `long.TryParse`                     |

**Impact:** Eliminates 2-3 string allocations per numeric literal. In code-heavy scripts with many numbers, this adds up. The decimal cases (float/int) are the most valuable since `double.TryParse` and `long.TryParse` both accept `ReadOnlySpan<char>` natively on .NET 10.

---

### Opportunity 3 — Span-Based Line Enumeration in `StripCommonIndent`

**The problem:** `text.Split('\n')` allocates a `string[]` plus individual `string` objects for every line. For a 20-line triple-quoted string, that's 21 allocations just to compute indentation.

```csharp
// Current: allocates string[] + n string objects
string[] lines = text.Split('\n');
```

**The fix:** Use `MemoryExtensions.Split()` (available in .NET 9+) or manual `IndexOf`-based span iteration:

```csharp
ReadOnlySpan<char> remaining = text.AsSpan();
int minIndent = int.MaxValue;
while (!remaining.IsEmpty)
{
    int nl = remaining.IndexOf('\n');
    ReadOnlySpan<char> line = nl >= 0 ? remaining[..nl] : remaining;

    if (!line.IsEmpty && !line.IsWhiteSpace())
    {
        int indent = 0;
        foreach (char c in line)
        {
            if (c is ' ' or '\t') indent++;
            else break;
        }
        minIndent = Math.Min(minIndent, indent);
    }

    remaining = nl >= 0 ? remaining[(nl + 1)..] : default;
}
```

**Complication:** The current `StripCommonIndent` does two passes — one to find min indentation, one to build the stripped result. The second pass (building the result string) still needs to produce a `string`. But you can use `string.Create()` to write directly into the result string after computing the needed length, skipping intermediate allocations entirely.

For `StripCommonIndentParts()`, the same span-based line iteration can replace the `Split` and the auxiliary `StringBuilder` for each text segment.

**Impact:** Eliminates N+1 allocations per triple-quoted string (where N = number of lines). Triple-quoted strings are less common than regular strings, so this is medium-priority, but it's a clean win.

---

### Opportunity 4 — `ArrayPool<Token>` for Interpolated Expression Token Lists

**The problem:** Every `${expr}` interpolation in a string or command creates:

1. A nested `Lexer` that allocates a new `List<Token>` internally
2. The tokens get added to a `List<object> parts`
3. During parsing, the token list is **copied** into a new list just to append an EOF token:

```csharp
// Parser.cs, lines ~2370 and ~2410
var tokensWithEof = new List<Token>(innerTokens);  // COPIES entire list
tokensWithEof.Add(new Token(TokenType.Eof, "", null, token.Span));
var innerParser = new Parser(tokensWithEof);
```

**The fix (low-hanging fruit):** Append the EOF token during lexing in `ScanInterpolatedExpression` instead of during parsing, eliminating the copy entirely:

```csharp
// In ScanInterpolatedExpression:
innerTokens.Add(new Token(TokenType.Eof, "", null,
    new SourceSpan(_file, _line, _column, _line, _column)));
// Now the parser can use innerTokens directly — no copy needed
```

**The fix (aggressive):** For the inner lexer itself, use `ArrayPool<Token>.Shared.Rent()` as the backing store instead of a `List<Token>`. The inner expressions are typically tiny (1-5 tokens), so even a minimum-size rented array (which the pool will round up to 16) is more than enough. Return it after parsing completes.

**Impact:** Eliminates a list copy + allocation per interpolation expression. In template-heavy code with many `${}` markers, this is significant.

---

### Opportunity 5 — Static Lexeme Strings for Fixed-Length Tokens

**The problem:** `AddToken(TokenType)` always does `_source[_start.._current]` which allocates a new string for every operator/punctuation token:

```csharp
private void AddToken(TokenType type)
{
    string lexeme = _source[_start.._current]; // allocates "(" every time
    _tokens.Add(new Token(type, lexeme, null, ...));
}
```

**The fix:** This isn't strictly Span/ArrayPool, but it's allocation-related. Create a lookup from `TokenType` → canonical lexeme string for all fixed tokens:

```csharp
private static readonly string[] s_canonicalLexemes = new string[(int)TokenType.MaxValue];
static Lexer()
{
    s_canonicalLexemes[(int)TokenType.LeftParen] = "(";
    s_canonicalLexemes[(int)TokenType.RightParen] = ")";
    s_canonicalLexemes[(int)TokenType.EqualEqual] = "==";
    // ... etc for all ~50 fixed-lexeme token types
}

private void AddToken(TokenType type)
{
    string lexeme = s_canonicalLexemes[(int)type] ?? _source[_start.._current];
    _tokens.Add(new Token(type, lexeme, null, ...));
}
```

Alternatively, use `_source.AsSpan(_start, _current - _start)` to compare and only allocate when needed — but since these strings are stored in `Token.Lexeme` (a `string` field), you ultimately need a string. The canonical string table avoids the allocation entirely.

**Impact:** Eliminates one string allocation per operator/punctuation token. For a typical file, that's hundreds of small strings that are all identical copies of `"("`, `")"`, `"{"`, etc.

---

### Opportunity 6 — `ReadOnlySpan<char>` in `ScanString()` Lookahead

**The problem:** `ScanString()` does a pre-scan to check for `${` interpolation markers. This scan iterates character-by-character through the source, which is already zero-alloc. BUT — if interpolation is found, it delegates to `ScanInterpolatedString`. The two methods share no work, so the characters before the `${` are scanned twice.

**The fix:** Rather than a separate lookahead, use the `ReadOnlySpan<char>` APIs to search efficiently:

```csharp
// Fast check: does the string contain ${ before the closing quote?
ReadOnlySpan<char> ahead = _source.AsSpan(_current);
// Use IndexOf for the common non-interpolated case
int quotePos = ahead.IndexOf('"');
if (quotePos >= 0)
{
    ReadOnlySpan<char> candidate = ahead[..quotePos];
    if (candidate.Contains("${".AsSpan(), StringComparison.Ordinal))
    {
        ScanInterpolatedString(prefixed: false);
        return;
    }
}
```

This is faster than the char-by-char loop and is a single vectorized scan on .NET 10 (SIMD-accelerated `IndexOf`). But note this simplified version doesn't account for escape sequences (`\$`), so you'd need a bit more care — the existing lookahead approach is correct because it skips `\\`. Still, the escape case could be handled with a fast-path: if the span doesn't contain `\` at all, the simple check suffices. Only fall back to the careful scan when backslashes are present.

**Impact:** Small — the lookahead is already O(n) and doesn't allocate. The win is in using SIMD-accelerated span search vs. char-by-char branching.

---

### Opportunity 7 — `_source.Substring` → `_source.AsSpan` in `TryMatchByteSizeUnit`

**The problem:**

```csharp
string unit = _source.Substring(_current, 2); // Allocates "KB", "MB", "GB", "TB"
```

**The fix:** These are always the same 4 strings. Use a constant:

```csharp
string unit = _source[_current] switch {
    'K' => "KB", 'M' => "MB", 'G' => "GB", 'T' => "TB",
    _ => throw new InvalidOperationException()
};
_current += 2;
_column += 2;
```

**Impact:** Tiny — byte-size literals are rare. But it's a trivial fix.

---

### Summary — Priority Ranking for Span/ArrayPool Work

| #   | Optimization                           | API Used                               | Allocations Eliminated | Frequency | Effort  |
| --- | -------------------------------------- | -------------------------------------- | ---------------------- | --------- | ------- |
| 1   | ValueStringBuilder for string scanning | `stackalloc` + `ArrayPool<char>`       | 2 per string/cmd token | Very High | Medium  |
| 2   | Span-based number parsing              | `ReadOnlySpan<char>` + `stackalloc`    | 2-3 per number literal | High      | Low     |
| 3   | Append EOF during lexing, not parsing  | Removes `new List<Token>(inner)` copy  | 1 list copy per `${}`  | Medium    | Trivial |
| 4   | Static lexeme table for operators      | Static `string[]`                      | 1 per operator token   | Very High | Low     |
| 5   | Span-based `StripCommonIndent`         | `ReadOnlySpan<char>` + `string.Create` | N+1 per `"""` string   | Low       | Medium  |
| 6   | SIMD string interpolation check        | `Span.IndexOf` / `Span.Contains`       | 0 (throughput win)     | High      | Low     |
| 7   | Static byte-size unit strings          | Constants                              | 1 per byte literal     | Very Low  | Trivial |

Items 1-4 are the highest value. Item 1 (ValueStringBuilder) alone would eliminate the largest category of GC allocation in the lexer — every string literal, every command literal, every interpolated string.
