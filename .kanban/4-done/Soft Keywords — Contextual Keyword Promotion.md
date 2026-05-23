# Soft Keywords — Contextual Keyword Promotion

**Status:** Backlog — Design
**Created:** 2026-04-28
**Author:** Architect
**Depends on:** _Timeout — Context-Sensitive Soft Keyword_ spec (currently in `1-todo/` awaiting implementation). Shared infrastructure from that spec must land first.

---

## 1. Overview

The `timeout` soft-keyword spec established the pattern: demote a hard-reserved keyword to an
identifier-level token, and add contextual disambiguation logic in the parser to recognize the
construct only when the surrounding syntax makes it unambiguous.

This spec applies the same treatment to **six additional keywords**:

| Keyword   | Current classification               | Construct it introduces                                 |
| --------- | ------------------------------------ | ------------------------------------------------------- |
| `retry`   | Hard keyword (expression)            | `retry (n, opts...) [onRetry ...] [until ...] { body }` |
| `lock`    | Hard keyword (statement)             | `lock path [(wait: t, stale: t)] { body }`              |
| `defer`   | Hard keyword (statement)             | `defer [await] expr;` / `defer [await] { body }`        |
| `elevate` | Hard keyword (statement)             | `elevate [(elevator)] { body }`                         |
| `async`   | Hard keyword (modifier + expression) | `async fn name() { }` / `async (params) => expr`        |
| `await`   | Hard keyword (unary operator)        | `await expr`                                            |

This spec also **resolves OQ-2** from the timeout spec: it defines the `keyword` semantic token
type addition that enables correct semantic highlighting for all soft keywords in their keyword
usage positions.

---

## 2. Shared Infrastructure

All seven soft keywords (including `timeout` from the companion spec) share the same underlying
mechanisms. The companion spec introduces these first; this spec reuses and extends them.

### 2.1 `SoftKeywords` Constants

A single static class holds all soft keyword string constants, eliminating magic string
repetition across disambiguation methods. The number of occurrences per keyword is low (2–3),
so inline const strings are preferred over a centralized registry:

```csharp
// Stash.Core/Parsing/Parser.cs (private constants, or separate SoftKeywords.cs)
private const string KwTimeout = "timeout";
private const string KwRetry   = "retry";
private const string KwLock    = "lock";
private const string KwDefer   = "defer";
private const string KwElevate = "elevate";
private const string KwAsync   = "async";
private const string KwAwait   = "await";

// Contextual sub-keywords (already treated as soft by retry):
private const string KwOnRetry = "onRetry";
private const string KwUntil   = "until";
```

> **Implementation note:** These already exist implicitly in the parser as inline string
> comparisons (e.g., `Peek().Lexeme == "onRetry"`). Consolidate at the same time.

### 2.2 `IsFollowedByBlockAfterParens(int startPos)` — Bounded Lookahead

Introduced by the timeout spec. Given the index of a `(` token, scans forward tracking nesting
depth; returns `true` if the matching `)` is immediately followed by `{`.

```
function IsFollowedByBlockAfterParens(startPos):
    depth = 0
    pos = startPos  // startPos points to '('
    while pos < tokens.Length:
        if tokens[pos] == '(': depth++
        elif tokens[pos] == ')':
            depth--
            if depth == 0:
                return pos + 1 < tokens.Length && tokens[pos + 1] == '{'
        pos++
    return false
```

This is O(tokens inside the parens) — never O(file size) — and uses the same approach as the
existing `IsLambdaStart()` helper.

### 2.3 `IsFollowedByRetryClauseAfterParens(int startPos)` — Retry Variant

Like `IsFollowedByBlockAfterParens`, but also returns `true` if the matching `)` is followed
by the identifier `onRetry`, `until`, or `{`:

```
function IsFollowedByRetryClauseAfterParens(startPos):
    // same depth-tracking scan as above
    // at depth == 0:
    next = tokens[pos + 1]
    return next.Type == '{'
        || (next.Type == Identifier && (next.Lexeme == "onRetry" || next.Lexeme == "until"))
```

### 2.4 `IsFollowedByLockOptionsOrBlockAfterParens(int startPos)` — Lock Variant

Like `IsFollowedByBlockAfterParens`, but also returns `true` if the matching `)` is followed
by `{` OR by the pattern `( IDENTIFIER :` (the lock named-options list):

```
function IsFollowedByLockOptionsOrBlockAfterParens(startPos):
    // same scan
    // at depth == 0:
    p = pos + 1
    if tokens[p] == '{': return true
    if tokens[p] == '(':
        return p + 1 < tokens.Length && tokens[p + 1].Type == Identifier
            && p + 2 < tokens.Length && tokens[p + 2].Type == Colon
    return false
```

### 2.5 `IsExpressionStarter(TokenType t)` — Helper Predicate

A predicate that returns `true` if the given token type can begin an expression. Used across
multiple disambiguation methods to avoid duplicating the operator/terminator exclusion logic:

```csharp
private static bool IsExpressionStarter(TokenType t) =>
    t is TokenType.Identifier
        or TokenType.Integer or TokenType.Float or TokenType.Duration or TokenType.ByteSize
        or TokenType.SemVer or TokenType.IpAddress
        or TokenType.String or TokenType.InterpolatedString
        or TokenType.True or TokenType.False or TokenType.Null
        or TokenType.LeftParen or TokenType.LeftBracket or TokenType.Bang or TokenType.Minus
        or TokenType.Tilde or TokenType.Increment or TokenType.Decrement;
```

Terminators (`;`, `,`, `)`, `]`, `:`), operators (`+`, `-`, `*`, `=`, …), and keywords that
cannot start an expression are all excluded.

### 2.6 `keyword` Semantic Token Type (Resolves OQ-2)

The existing semantic token taxonomy (`SemanticTokenConstants.cs`) has eleven symbol-identity
types. Keywords like `defer`, `lock`, `retry`, `timeout`, `elevate`, `async`, `await` are
currently highlighted exclusively by the TextMate grammar regex — there is no semantic token
type for them.

After soft-keyword promotion, the TextMate grammar must stop blindly matching these identifiers.
The semantic token walker must fill the gap by emitting a `keyword` type for each soft keyword
**in its keyword-usage position**.

**Add `TokenTypeKeyword` to `SemanticTokenConstants.cs`:**

```csharp
// New constants:
public const int TokenTypeKeyword = 11;   // add after enumMember (index 10)

// Update TokenTypeNames array:
public static readonly string[] TokenTypeNames = [
    "namespace", "type", "struct", "enum", "interface",
    "function", "method", "parameter", "variable", "property", "enumMember",
    "keyword"   // index 11 — new
];
```

**Update `SemanticTokensHandler.cs`** to include `"keyword"` in the legend:

```csharp
TokenTypes = new Container<string>(SemanticTokenConstants.TokenTypeNames)
```

**Update `SemanticTokenWalker.cs`** — add `EmitKeyword(SourceSpan span)` helper:

```csharp
private void EmitKeyword(SourceSpan span)
    => Emit(span, SemanticTokenConstants.TokenTypeKeyword, 0);
```

**Update each `Visit*` method** in `SemanticTokenWalker.cs` to call `EmitKeyword` for the
keyword token, before walking child nodes. Details per keyword in §3 below.

**Update `package.json`** — add TextMate scope mapping for `"keyword"` semantic token type:

```json
"keyword": {
  "foreground": "keyword.control.stash"
}
```

This means VS Code maps the `keyword` semantic token to the `keyword.control.stash` TextMate
scope, which is already styled by the user's theme. No theme change required.

---

## 3. Per-Keyword Disambiguation Analysis

### 3.1 `await`

**Classification:** Unary prefix operator (like `!` or `not`)
**Currently dispatched in:** `Unary()` (expression precedence level)
**AST node:** `AwaitExpr`

**Grammar:**

```
await_expr := 'await' unary_expr
```

**Disambiguation complexity: LOW**

`await` is a right-recursive unary operator. In expression position, an identifier immediately
followed by another expression-starter is not a valid Stash expression — only a unary operator
or function call can bridge the two. Therefore:

| Next token after `"await"`    | Decision                  | Rationale                                                              |
| ----------------------------- | ------------------------- | ---------------------------------------------------------------------- |
| Identifier                    | AwaitExpr                 | `await someTask` — two adjacent identifiers; `someTask` is the operand |
| Literal (int, duration, etc.) | AwaitExpr                 | Same: adjacent literal                                                 |
| `.`, `[`, `?`                 | Identifier                | Property access / indexing on `await` variable                         |
| `;`, `,`, `)`, `]`, `:`       | Identifier                | Terminator: `return await;`                                            |
| `=`, `+=`, etc.               | Identifier                | Assignment target                                                      |
| `+`, `-`, `*`, `/`, operators | Identifier                | `await + 5`                                                            |
| `!`, `~` (unary)              | AwaitExpr                 | `await !expr` — unusual but valid                                      |
| `(`                           | **AwaitExpr**             | See below                                                              |
| Anything else                 | Identifier (conservative) |                                                                        |

**The `(` case — design decision:**

`await(promise)` is ambiguous: calling a function named `await` vs. awaiting a grouped
expression. Stash adopts the **always-keyword** rule: if `"await"` is followed by `(`, it is
always treated as the await operator applied to a grouped expression. A function named `await`
can never be called using this syntax.

**Rationale:** `await` is a near-universal keyword in async languages. Naming a function `await`
and calling it as `await(args)` is an extreme anti-pattern. The disambiguation overhead of a
paren-scan is not justified.

**Workaround for users with a variable named `await`:**

```stash
let aw = await;    // assign to a different name
let result = aw(); // call it
```

**Algorithm (in `Unary()`):**

```
if Check(Identifier) && Peek().Lexeme == "await":
    peek1 = PeekToken(+1)
    if peek1.Type in { ';', ',', ')', ']', ':' }:
        → VariableExpr (identifier)
    if peek1.Type in { '=', '+=', '-=', ...all assignment ops }:
        → VariableExpr
    if peek1.Type in { '+', '-', '*', '/', ... all binary ops }:
        → VariableExpr
    if peek1.Type in { '.', '[', '?', '??' }:
        → VariableExpr
    if peek1.Type == EOF:
        → VariableExpr
    else:   // identifier, literal, '(', '!', '~', etc.
        Advance()  // consume 'await'
        → parse AwaitExpr (existing ParseAwaitExpr() or inline)
```

**SemanticTokenWalker change:**

```csharp
public int VisitAwaitExpr(AwaitExpr expr)
{
    EmitKeyword(expr.AwaitKeyword.Span);  // NEW: emit 'await' as keyword
    expr.Expression.Accept(this);
    return 0;
}
```

**TextMate grammar change:** Remove `await` from the `async-await` pattern regex.

---

### 3.2 `async`

**Classification:** Modifier keyword (on function declarations) AND expression prefix (lambda)
**Currently dispatched in:** `Declaration()` (modifier form) + `Primary()` (lambda form)
**AST nodes:** `FnDeclStmt` (with `IsAsync` flag) / `LambdaExpr` (with `IsAsync` flag)

**Grammar:**

```
async_fn   := 'async' 'fn' name '(' params ')' [':' type] body
async_lambda := 'async' '(' params ')' '=>' expr_or_block
```

**Disambiguation complexity: LOW-MEDIUM**

`async` has two perfectly deterministic disambiguation rules:

**Form 1 — `async fn` (function declaration):**
`fn` remains a hard keyword after this change. `identifier fn` is never a valid expression
(two adjacent tokens with no operator). So `"async" fn` is always the async function modifier.

In `Declaration()`:

```
if Check(Identifier) && Peek().Lexeme == "async":
    if PeekToken(+1).Type == TokenType.Fn:
        Advance()  // consume 'async'
        → consume 'fn', call FnDeclaration(isAsync: true)
    else:
        → fall through to ExpressionStatement()
```

**Form 2 — `async (params) => body` (lambda):**
The existing `IsLambdaStart()` already performs a bounded lookahead that scans `(params)` and
checks for `=>`. Reuse it:

In `Primary()`:

```
if Check(Identifier) && Peek().Lexeme == "async":
    if PeekToken(+1).Type == '(':
        if IsAsyncLambdaStart():  // variant of IsLambdaStart that expects '(' at current+1
            Advance()  // consume 'async'
            → ParseLambda(isAsync: true)
    else if PeekToken(+1).Type != '=', '.', '[', operators, terminators:
        → treat as identifier (no matching construct)
    else:
        → identifier
```

`IsAsyncLambdaStart()` is essentially `IsLambdaStart()` but saves position at `current + 1`
(the `(` after `"async"`) rather than at the current `(`.

**Edge cases:**

| Expression                        | Decision                                                       |
| --------------------------------- | -------------------------------------------------------------- |
| `async fn myFn() { }`             | async function declaration ✓                                   |
| `async (n) => n * 2`              | async lambda ✓                                                 |
| `let async = 5;`                  | identifier: variable declaration ✓                             |
| `async.method()`                  | identifier: property access ✓                                  |
| `async + 5`                       | identifier: arithmetic ✓                                       |
| `async(fn, x)`                    | identifier: function call (NOT lambda — no `=>`) ✓             |
| `return async;`                   | identifier ✓                                                   |
| `async fn` in struct methods      | async modifier ✓ (same rule applies in `ParseStructMethods()`) |
| `interface { async fn method() }` | async modifier in interface body — check applies there too     |

**Struct and interface method declarations:** These also dispatch through `FnDeclaration()`. The
same `"async"` + `fn` lookahead must be applied wherever method declarations are parsed.

**SemanticTokenWalker change:**

The walker already visits `FnDeclStmt` and `LambdaExpr`. For `async` functions:

```csharp
public object? VisitFnDeclStmt(FnDeclStmt stmt)
{
    if (stmt.IsAsync && stmt.AsyncKeyword.HasValue)
        EmitKeyword(stmt.AsyncKeyword.Value.Span);  // NEW: emit 'async' as keyword
    EmitFunction(stmt.Name.Span, with ModifierDeclaration);
    // ... walk params, return type, body ...
}

public int VisitLambdaExpr(LambdaExpr expr)
{
    if (expr.IsAsync && expr.AsyncKeyword.HasValue)
        EmitKeyword(expr.AsyncKeyword.Value.Span);  // NEW
    // ... walk params, body ...
}
```

> **Implementation note:** Verify that `FnDeclStmt` and `LambdaExpr` already store the async
> keyword token. If they only store an `IsAsync: bool`, add a `Token? AsyncKeyword` field or
> use the span from the first token when `IsAsync` is true.

**TextMate grammar change:** Remove `async` from the `async-await` pattern regex.

---

### 3.3 `defer`

**Classification:** Statement-only keyword
**Currently dispatched in:** `Statement()` → `DeferStatement()`
**AST node:** `DeferStmt`

**Grammar:**

```
defer_stmt := 'defer' '{' body '}'
           | 'defer' 'await' expr ';'
           | 'defer' expr ';'
```

**Disambiguation complexity: MEDIUM — one genuine ambiguity with a principled resolution**

**The `(` ambiguity and its resolution:**

`defer (expr)` is syntactically ambiguous:

- Keyword interpretation: `DeferStmt` where the deferred expression is the grouped `expr`
- Identifier interpretation: function call `defer(expr)` on a callable stored in the `defer` variable

**Resolution:** `defer (expr)` in statement position is treated as a **function call** (identifier
interpretation). Users who want to defer a grouped expression must write `defer expr;` without
the outer parentheses, or use the block form `defer { expr }`.

**Why this resolution is safe in practice:**
Existing `defer` usage invariably writes `defer someCall()` (no outer grouping parens) or
`defer { block }`. The form `defer (someCall())` is stylistically redundant — the outer parens
add nothing since `someCall()` is already a complete expression. Surveying the Stash examples
directory confirms this: no example uses `defer (...)` with outer grouping parens.

The rare user who currently writes `defer (expr)` can trivially migrate to `defer expr;`. **This
is the one mild breaking change** in this spec.

**All other positions are unambiguous:**

| Next token after `"defer"`     | Decision                   | Rationale                                                                                                                               |
| ------------------------------ | -------------------------- | --------------------------------------------------------------------------------------------------------------------------------------- |
| `{`                            | DeferStmt (block)          | `defer { }` — always keyword; `{ }` alone as a separate statement is syntactically valid but `identifier { }` is not a valid expression |
| Identifier (not `"await"`)     | DeferStmt                  | `defer myFn()` — two adjacent identifiers is not a valid expression                                                                     |
| `"await"` identifier           | DeferStmt                  | `defer await expr` — two adjacent identifiers                                                                                           |
| String, int, duration literals | DeferStmt                  | Adjacent literal after identifier is not valid expression                                                                               |
| `(`                            | Identifier (function call) | Per resolution above                                                                                                                    |
| `=`, `.`, `[`, operators       | Identifier                 | Assignment/property/index/expression                                                                                                    |
| `;`, `,`, `)`, `]`             | Identifier                 | Terminator                                                                                                                              |
| EOF                            | Identifier                 |                                                                                                                                         |

> **Note on `defer await` with soft `await`:** If `await` is also demoted (§3.1), then `defer`
> identifier followed by `await` identifier = two adjacent identifiers = cannot be an expression
> = must be DeferStmt. The `"await"` string check serves as a clarifying comment; the two-adjacent-
> identifiers argument already makes it unambiguous.

**Algorithm (in `Statement()`):**

At statement level, when the current token is `TokenType.Identifier` and `Peek().Lexeme == "defer"`:

```
peek1 = PeekToken(+1)
if peek1.Type == '{':
    → DeferStmt (block form)
if peek1.Type == Identifier:
    → DeferStmt (includes 'await' case and any deferred function reference)
if peek1 is a literal (int, string, duration, etc.):
    → DeferStmt
if peek1.Type == '(':
    → ExpressionStatement (function call on 'defer' variable)
else:
    → ExpressionStatement (identifier used in other expression context)
```

**SemanticTokenWalker change:**

```csharp
public object? VisitDeferStmt(DeferStmt stmt)
{
    EmitKeyword(stmt.DeferKeyword.Span);  // NEW: emit 'defer' as keyword
    if (stmt.HasAwait && stmt.AwaitKeyword.HasValue)
        EmitKeyword(stmt.AwaitKeyword.Value.Span);  // NEW: emit 'await' too
    stmt.Body.Accept(this);
    return 0;
}
```

---

### 3.4 `elevate`

**Classification:** Statement-only keyword
**Currently dispatched in:** `Statement()` → `ElevateStatement()`
**AST node:** `ElevateStmt`

**Grammar:**

```
elevate_stmt := 'elevate' '{' body '}'
             | 'elevate' '(' elevator_expr ')' '{' body '}'
```

**Disambiguation complexity: LOW-MEDIUM — only the `(` case needs lookahead**

`elevate` always ends with a `{ }` block. The only ambiguity is when `"elevate"` is followed
by `(`: is this `elevate(elevator_expr) { body }` (ElevateStmt with elevator) or `elevate(args);`
(function call on `elevate` variable)?

The bounded lookahead `IsFollowedByBlockAfterParens` from the shared infrastructure handles
this exactly: look past the `(...)` and check if `{` follows.

| Next token after `"elevate"` | Decision                    | Rationale                                                           |
| ---------------------------- | --------------------------- | ------------------------------------------------------------------- |
| `{`                          | ElevateStmt (no elevator)   | Always unambiguous                                                  |
| `(` + block after `)`        | ElevateStmt (with elevator) | Lookahead confirms `{`                                              |
| `(` + no block after `)`     | Identifier (function call)  | Lookahead shows `;`, `,`, etc.                                      |
| `=`, `.`, `[`, operators     | Identifier                  |                                                                     |
| `;`, `,`, `)`, `]`           | Identifier                  |                                                                     |
| Identifier, literal          | Identifier                  | `elevate myVar` is not an ElevateStmt (grammar requires `(` or `{`) |
| EOF                          | Identifier                  |                                                                     |

**Algorithm (in `Statement()`):**

```
peek1 = PeekToken(+1)
if peek1.Type == '{':
    → ElevateStmt
if peek1.Type == '(':
    if IsFollowedByBlockAfterParens(current + 1):
        → ElevateStmt
    else:
        → ExpressionStatement
else:
    → ExpressionStatement
```

**SemanticTokenWalker change:**

```csharp
public object? VisitElevateStmt(ElevateStmt stmt)
{
    EmitKeyword(stmt.ElevateKeyword.Span);  // NEW
    stmt.Elevator?.Accept(this);
    foreach (var s in stmt.Body.Statements)
        s.Accept(this);
    return 0;
}
```

---

### 3.5 `retry`

**Classification:** Expression keyword (dispatched in Primary(), used in statement position via ExpressionStatement)
**Currently dispatched in:** `Primary()` (expression level)
**AST node:** `RetryExpr`

**Grammar:**

```
retry_expr := 'retry' '(' maxAttempts [',' options] ')'
              ['onRetry' handler]
              ['until' predicate]
              '{' body '}'
```

**Disambiguation complexity: MEDIUM — `(` always follows; needs lookahead for what comes after**

`retry` is always followed by `(` in its keyword form. This is the same shape as `elevate` with
an elevator, but with more possible continuations after the `(...)`:

After the closing `)`:

- `onRetry` identifier — RetryExpr
- `until` identifier — RetryExpr
- `{` — RetryExpr (body block directly)

`IsFollowedByRetryClauseAfterParens` (§2.3) captures all three cases.

| Next token after `"retry"`            | Decision                   | Rationale                       |
| ------------------------------------- | -------------------------- | ------------------------------- |
| `(` + `onRetry`/`until`/`{` after `)` | RetryExpr                  | Lookahead confirms retry clause |
| `(` + anything else after `)`         | Identifier (function call) |                                 |
| `=`, `.`, `[`, operators              | Identifier                 |                                 |
| `;`, `,`, `)`, `]`                    | Identifier                 |                                 |
| Identifier, literal                   | Identifier                 | `retry 5` is not a RetryExpr    |
| EOF                                   | Identifier                 |                                 |

**Algorithm (in `Primary()`):**

```
peek1 = PeekToken(+1)
if peek1.Type == '(':
    if IsFollowedByRetryClauseAfterParens(current + 1):
        Advance()  // consume 'retry'
        → ParseRetryExpr()
    else:
        → VariableExpr (identifier — function call on 'retry')
else:
    → VariableExpr (identifier)
```

**SemanticTokenWalker change:**

```csharp
public int VisitRetryExpr(RetryExpr expr)
{
    EmitKeyword(expr.RetryKeyword.Span);  // NEW: 'retry'
    // 'onRetry' and 'until' are already handled as identifier tokens with no special
    // semantic token type — they remain plain identifiers in the walker. They are styled
    // by the TextMate grammar as contextual sub-keywords via a separate pattern.
    expr.MaxAttempts.Accept(this);
    expr.OptionsExpr?.Accept(this);
    foreach (var (_, val) in expr.NamedOptions ?? [])
        val.Accept(this);
    expr.OnRetryClause?.Accept(this);
    expr.UntilClause?.Accept(this);
    foreach (var s in expr.Body.Statements)
        s.Accept(this);
    return 0;
}
```

> **Note on `onRetry` and `until`:** These are ALREADY contextual soft keywords (checked by
> string comparison on identifier tokens, not by `TokenType.OnRetry`/`TokenType.Until`). They
> are not in the lexer keyword table and never were. They need no changes to their token
> classification. Their highlighting in the TextMate grammar is handled by a separate named
> pattern for `retry` sub-clauses (which can remain as-is since they are already identifiers
> in the token stream).

---

### 3.6 `lock`

**Classification:** Statement-only keyword
**Currently dispatched in:** `Statement()` → `LockStatement()`
**AST node:** `LockStmt`

**Grammar:**

```
lock_stmt := 'lock' path_expr ['(' options ')'] '{' body '}'
options   := 'wait' ':' duration [',' 'stale' ':' duration]
           | 'stale' ':' duration [',' 'wait' ':' duration]
```

**Disambiguation complexity: MEDIUM-HARD — path is an arbitrary expression; three cases**

`lock` is the most complex of the six because the path expression can be any primary expression
including function calls, member access, and indexing. Only when followed by `{` or the lock
options pattern `(IDENTIFIER :)` is the disambiguation certain.

The key insight from the existing parser: **two adjacent tokens that cannot form a valid binary
expression** signal the keyword. An identifier `"lock"` followed by another identifier, a string
literal, or a duration literal — none of which can be a binary operator — means the two tokens
cannot belong to the same expression. This covers the most common lock paths.

| Next token after `"lock"`        | Decision                   | Rationale                                                               |
| -------------------------------- | -------------------------- | ----------------------------------------------------------------------- |
| Identifier                       | LockStmt                   | `lock myPath` — two adjacent identifiers; not valid expression          |
| String literal                   | LockStmt                   | `lock "/var/run/app.lock"` — identifier + literal; not valid expression |
| Duration, int, float literal     | LockStmt                   | Same                                                                    |
| `(` + block/options after `)`    | LockStmt                   | `lock (path) { }` — bounded lookahead confirms                          |
| `(` + no block/options after `)` | Identifier (function call) | `lock(path)` or `lock(a, b)`                                            |
| `=`, `+=`, etc.                  | Identifier                 | Assignment                                                              |
| `.`, `[`, `?`                    | Identifier                 | Property access / indexing on `lock` variable                           |
| `;`, `,`, `)`, `]`, `:`          | Identifier                 | Terminator                                                              |
| Binary operators                 | Identifier                 | Arithmetic/logical on `lock` variable                                   |
| EOF                              | Identifier                 |                                                                         |

**The `(` disambiguation uses `IsFollowedByLockOptionsOrBlockAfterParens` (§2.3).**

**Algorithm (in `Statement()`):**

```
peek1 = PeekToken(+1)
if peek1.Type == Identifier || peek1.IsNonOperatorLiteral():
    → LockStmt  (two non-operator adjacent tokens)
if peek1.Type == '(':
    if IsFollowedByLockOptionsOrBlockAfterParens(current + 1):
        → LockStmt
    else:
        → ExpressionStatement
else:
    → ExpressionStatement
```

Where `IsNonOperatorLiteral()` returns `true` for `Integer`, `Float`, `String`, `Duration`,
`ByteSize`, `SemVer`, `IpAddress`, `True`, `False`, `Null` — any token that begins a
primary expression but cannot continue a binary expression after an identifier.

**SemanticTokenWalker change:**

```csharp
public object? VisitLockStmt(LockStmt stmt)
{
    EmitKeyword(stmt.LockKeyword.Span);  // NEW: 'lock'
    stmt.Path.Accept(this);
    stmt.WaitOption?.Accept(this);
    stmt.StaleOption?.Accept(this);
    foreach (var s in stmt.Body.Statements)
        s.Accept(this);
    return 0;
}
```

---

## 4. Disambiguation Difficulty Ranking

| Keyword           | Difficulty  | Key ambiguity                        | Resolution                                  |
| ----------------- | ----------- | ------------------------------------ | ------------------------------------------- |
| `async` (fn form) | Trivial     | None — `fn` is still a hard keyword  | Two adjacent tokens rule                    |
| `async` (lambda)  | Low         | `async(args)` vs `async (p) => e`    | Reuse `IsLambdaStart()`                     |
| `await`           | Low         | `await(expr)` vs call                | Always-keyword rule for `(` case            |
| `elevate`         | Low-Medium  | `elevate(e)` vs `elevate(e) { }`     | `IsFollowedByBlockAfterParens`              |
| `defer`           | Medium      | `defer (expr)` vs `defer(expr)` call | Principled resolution: `defer (...)` = call |
| `retry`           | Medium      | `retry(args)` vs `retry(n) { }`      | `IsFollowedByRetryClauseAfterParens`        |
| `lock`            | Medium-Hard | `lock(path)` vs `lock (path) { }`    | Three-case algorithm                        |

All difficulties are "manageable" — none require full backtracking or grammar refactoring.

---

## 5. Lexer Changes

For each keyword, remove from the frozen keyword dictionary in `Stash.Core/Lexing/Lexer.cs`:

```csharp
// Remove these lines:
["retry"]   = TokenType.Retry,
["lock"]    = TokenType.Lock,
["defer"]   = TokenType.Defer,
["elevate"] = TokenType.Elevate,
["async"]   = TokenType.Async,
["await"]   = TokenType.Await,
```

The corresponding `TokenType.{Name}` enum entries **remain in the enum** — removing them would
be a binary-breaking change for external consumers of the library.

`ConsumePropertyName()` already explicitly lists `TokenType.Async` and `TokenType.Await` as
allowed property names (alongside `TokenType.Timeout`). After demotion these tokens are
`TokenType.Identifier`, so those explicit checks become dead code. Remove them during cleanup
(they are harmless if left).

`IsDictKeyToken()` uses the range `type >= TokenType.Let && type <= TokenType.Timeout`. All six
demoted keywords fall within this range. After demotion they are `TokenType.Identifier`, handled
by the `type == TokenType.Identifier` branch. The range check remains correct.

---

## 6. Parser Dispatch Changes

### `Statement()` cases to modify

Replace the hard token dispatch with identifier string checks:

```csharp
// BEFORE:
case TokenType.Elevate:
    Advance();
    return ElevateStatement();
case TokenType.Defer:
    Advance();
    return DeferStatement();
case TokenType.Lock:
    Advance();
    return LockStatement();
case TokenType.Retry:
{
    Expr expr = Expression();
    Match(TokenType.Semicolon);
    return new ExprStmt(expr, expr.Span);
}

// AFTER:
// (in the Identifier case, or in a new pre-check before the switch)
// Soft keyword dispatch: checked when token is TokenType.Identifier
if (Check(TokenType.Identifier))
{
    string lexeme = Peek().Lexeme;
    if (lexeme == KwElevate && IsElevateKeyword()) return ParseElevateStatement();
    if (lexeme == KwDefer   && IsDeferKeyword())   return ParseDeferStatement();
    if (lexeme == KwLock    && IsLockKeyword())     return ParseLockStatement();
    // retry is expression-level; falls through to ExpressionStatement()
}
```

Each `Is*Keyword()` method implements the algorithm from §3 for that keyword. `Parse*Statement()`
methods consume the identifier token first (via `Advance()`) then call the existing
`ElevateStatement()`, `DeferStatement()`, `LockStatement()` implementations (which read
`Previous()` for the keyword token).

### `Primary()` cases to modify

```csharp
// BEFORE (in Primary()):
if (Match(TokenType.Retry))
    return ParseRetryExpr();
if (Match(TokenType.Async))  // lambda form
    ...

// AFTER:
if (Check(TokenType.Identifier))
{
    string lexeme = Peek().Lexeme;
    if (lexeme == KwRetry && IsRetryKeyword()) { Advance(); return ParseRetryExpr(); }
    if (lexeme == KwAsync && IsAsyncLambdaStart()) { Advance(); return ParseLambda(isAsync: true); }
    // await is handled in Unary(), not Primary()
}
```

### `Unary()` case to modify

```csharp
// BEFORE:
if (Match(TokenType.Await))
    return ParseAwaitExpr();

// AFTER:
if (Check(TokenType.Identifier) && Peek().Lexeme == KwAwait && IsAwaitKeyword())
{
    Advance();
    return ParseAwaitExpr();
}
```

### `Declaration()` case to modify

```csharp
// BEFORE:
if (Match(TokenType.Async))
{
    Consume(TokenType.Fn, "Expected 'fn' after 'async'.");
    return FnDeclaration(isAsync: true);
}

// AFTER:
if (Check(TokenType.Identifier) && Peek().Lexeme == KwAsync
    && _current + 1 < _tokens.Count && _tokens[_current + 1].Type == TokenType.Fn)
{
    Advance();  // consume 'async'
    // Advance() again for 'fn' is handled by existing FnDeclaration() entry
    return Declaration(isAsync: true);  // or however the async path flows
}
```

---

## 7. TextMate Grammar Changes

**File:** `.vscode/extensions/stash-lang/syntaxes/stash.tmLanguage.json`

### Control keywords pattern

```json
// BEFORE:
"match": "\\b(defer|as|elevate|lock|retry|onRetry|until|timeout)\\b"

// AFTER:
"match": "\\b(as|onRetry|until)\\b"
```

`defer`, `elevate`, `lock`, `retry`, `timeout` are removed — they are now handled by semantic
tokens. `onRetry` and `until` are kept — they are already identifiers and cannot be highlighted
by semantic tokens (no `VisitOnRetry` in the walker); keeping them in TextMate is correct.

### Async/await pattern

```json
// BEFORE (async-await pattern, ~line 15):
"match": "\\b(async|await)\\b"

// AFTER: remove this pattern entirely
// 'async' and 'await' are now handled by semantic token walker
```

> **Alternative:** Keep the TextMate pattern as a fallback for editors without LSP semantic
> token support (e.g., Neovim without the LSP client running). The pattern would incorrectly
> highlight `async` and `await` as identifiers — but since these are common async-language
> keywords, false positives in editors with no LSP are acceptable. The implementer should
> choose based on whether the Neovim/tree-sitter user base is significant.

---

## 8. Monarch Tokenizer Changes (Stash.Playground)

**File:** `Stash.Playground/wwwroot/js/stash-language.js`

Remove from the `keywords` array:

```javascript
// Remove: 'defer', 'lock', 'retry', 'elevate', 'async', 'await'
// 'onRetry' and 'until' may remain (already effectively contextual)
```

The Playground uses Monaco editor, which supports semantic token overlays. After this change,
the keyword highlighting in the Playground falls back to semantic tokens from the LSP connection,
or plain text in offline/embedded mode. This is acceptable.

---

## 9. LSP / DAP Implications

### Hover

The LSP hover handler uses token types to decide whether to show keyword documentation.
After demotion, `async`, `await`, `defer`, etc. all emit `TokenType.Identifier`. The hover
handler must fall back to checking the **semantic token at the cursor position**: if the
semantic token type at the hover position is `keyword`, show keyword documentation; if it's
`variable`/`parameter`, show variable information.

### Completion

The completion provider currently maintains a list of keywords to offer as completions.
Remove the demoted keywords from the hard keyword list. Instead:

- **Context-aware keyword snippet completions:** When the cursor is at the start of a statement
  or expression, AND the typed text matches `"retry"`, `"lock"`, `"defer"`, `"elevate"`,
  `"async"`, or `"await"`, offer the keyword snippet (kind: `Keyword`) alongside any variables
  in scope with the same name (kind: `Variable`).
- **Variable completions:** When a variable named `defer` (etc.) is in scope, offer it as a
  variable completion in all positions.

This is a **medium-complexity** LSP change. A simpler approach: always offer both the keyword
snippet and the variable completion; let the user pick.

### Semantic highlighting correctness

The semantic token walker changes in §3 ensure that:

- `defer { }` → `defer` keyword token emitted by walker → styled as `keyword.control.stash`
- `let defer = 5;` → `defer` variable token emitted by walker → styled as `variable`
- `fn f(defer)` → `defer` parameter token emitted by walker → styled as `parameter`

No additional LSP handler changes are needed for semantic highlighting.

---

## 10. Breaking Changes

### Source compatibility

**Minor break: `defer (expr)` form**

The one genuine ambiguity resolution in this spec is that `defer (expr)` in statement position
is treated as a function call on a `defer` variable, not as a defer statement. This changes the
behavior for code that writes:

```stash
// Old (valid, parses as: defer statement with grouped expr):
defer (someCall());

// New (parses as: call expression on 'defer' variable — likely a runtime error if 'defer' is undefined):
defer (someCall());
```

**Migration:** Replace `defer (someCall())` with `defer someCall()`.

This is the only source-level breaking change. All other demotions are non-breaking because the
keywords could not previously appear as identifiers.

**No breaking changes for:**

- `retry`, `lock`, `elevate`, `async`, `await` — all were hard-reserved and could not appear as identifiers; no existing code uses them as identifiers.
- Any code using these keywords in their keyword form continues to parse and execute identically.
- All construct semantics (TimeoutExpr, RetryExpr, LockStmt, etc.) are unchanged.

### Bytecode compatibility

No bytecode changes. All disambiguation is parse-time. `OpCode` values are unchanged.
Pre-compiled `.stashc` files are fully compatible.

### Analysis compatibility

Static analysis rules (`SA*`) are all AST-based. No changes needed.

---

## 11. Implementation Order

These demotions are independent of each other (except all depend on the timeout spec's
infrastructure being in place first). Recommended order, from simplest to most complex:

1. **Semantic token taxonomy** — add `TokenTypeKeyword` and `EmitKeyword()` helper (prerequisite for all)
2. **`async` and `await`** — simplest disambiguation; high user impact (common in async code)
3. **`elevate`** — simple lookahead; low frequency of use as identifier in practice
4. **`defer`** — medium complexity; document the `defer (expr)` convention
5. **`retry`** — medium complexity; `IsFollowedByRetryClauseAfterParens` is the main new piece
6. **`lock`** — most complex; three-case algorithm; implement last

Each keyword can be shipped independently or all together. The shared infrastructure
(`IsFollowedByBlockAfterParens`, `SoftKeywords`, `IsExpressionStarter`) should be added in step 1
so all subsequent steps can reuse it.

---

## 12. Test Cases

### Execution tests (representative sample)

```stash
// async as identifier
let async = fn(x) { return x * 2; };
io.println(async(5));  // → 10

// await as identifier
let await = fn(task) { return task.result; };
io.println(await({ result: 42 }));  // → 42

// defer as identifier (variable holding a function)
let defer = fn() { io.println("deferred!"); };
defer();  // → "deferred!"

// lock as identifier
let lock = "/var/run/my.lock";
io.println(lock);  // → "/var/run/my.lock"

// retry as identifier
let retry = 3;
for (let i = 0; i < retry; i++) { io.println(i); }  // → 0 1 2

// elevate as identifier
let elevate = fn(level) { return level + 1; };
io.println(elevate(5));  // → 6

// Keywords still work as keywords:
let result = retry (3) { io.println("try"); "done" };
defer fn() { io.println("cleanup"); }();
timeout 5s { io.println("ok"); }
```

### Parser tests

| Scenario                      | Input                                  | Expected AST                       |
| ----------------------------- | -------------------------------------- | ---------------------------------- |
| `async` as variable           | `let async = 5;`                       | `LetStmt` name=`async`             |
| `await` as variable           | `let await = promise;`                 | `LetStmt` name=`await`             |
| `defer` as variable           | `let defer = fn() {};`                 | `LetStmt` name=`defer`             |
| `lock` as variable            | `let lock = path;`                     | `LetStmt` name=`lock`              |
| `retry` as variable           | `let retry = 3;`                       | `LetStmt` name=`retry`             |
| `elevate` as variable         | `let elevate = 5;`                     | `LetStmt` name=`elevate`           |
| `async fn` still works        | `async fn f() { }`                     | `FnDeclStmt` isAsync=true          |
| `async` lambda                | `async (n) => n + 1`                   | `LambdaExpr` isAsync=true          |
| `await` as unary              | `await someTask()`                     | `AwaitExpr`                        |
| `defer` block                 | `defer { io.println("hi"); }`          | `DeferStmt` block=true             |
| `defer` expr                  | `defer fn()`                           | `DeferStmt` expr                   |
| `defer` call (identifier)     | `defer(fn)`                            | `CallExpr` on identifier `defer`   |
| `lock` path                   | `lock myPath { }`                      | `LockStmt` path=identifier         |
| `lock` call (identifier)      | `lock(path);`                          | `CallExpr` on identifier `lock`    |
| `lock` with options           | `lock myPath (wait: 5s) { }`           | `LockStmt` with wait               |
| `elevate` no elevator         | `elevate { }`                          | `ElevateStmt` elevator=null        |
| `elevate` with elevator       | `elevate (admin) { }`                  | `ElevateStmt` elevator=ident       |
| `elevate` call (identifier)   | `elevate(admin);`                      | `CallExpr` on identifier `elevate` |
| `retry` expr                  | `retry (3) { }`                        | `RetryExpr`                        |
| `retry` with onRetry          | `retry (3) onRetry fn { }`             | `RetryExpr` with onRetry           |
| `retry` call (identifier)     | `retry(3, x);`                         | `CallExpr` on identifier `retry`   |
| `await (expr)` always keyword | `await (promise)`                      | `AwaitExpr` (grouped operand)      |
| for-loop variable             | `for (let defer in arr) { }`           | loop var=`defer`                   |
| parameter name                | `fn f(async, await) { }`               | params named `async`, `await`      |
| struct field                  | `struct S { async: bool, defer: int }` | struct with fields                 |

### Semantic token tests

| Scenario                          | Span      | Expected token type                |
| --------------------------------- | --------- | ---------------------------------- |
| `defer { }` — keyword span        | `defer`   | `keyword`                          |
| `let defer = 5` — identifier span | `defer`   | `variable`, `declaration` modifier |
| `async fn f()` — `async` span     | `async`   | `keyword`                          |
| `async fn f()` — `f` span         | `f`       | `function`, `declaration` modifier |
| `await someTask()` — `await` span | `await`   | `keyword`                          |
| `return await;` — `await` span    | `await`   | `variable`                         |
| `lock path { }` — `lock` span     | `lock`    | `keyword`                          |
| `let lock = x` — `lock` span      | `lock`    | `variable`, `declaration` modifier |
| `retry (3) { }` — `retry` span    | `retry`   | `keyword`                          |
| `elevate { }` — `elevate` span    | `elevate` | `keyword`                          |

### Formatting tests

| Input                   | Expected output    |
| ----------------------- | ------------------ |
| `let async = 5;`        | `let async = 5;`   |
| `async   fn   f()  { }` | `async fn f() { }` |
| `let defer   = 5;`      | `let defer = 5;`   |
| `defer   {  }`          | `defer { }`        |
| `lock   myPath   {  }`  | `lock myPath { }`  |

---

## 13. Open Questions

### OQ-1: `async` keyword token storage in AST

If `FnDeclStmt` and `LambdaExpr` currently store `IsAsync: bool` but no token reference, the
walker cannot emit a keyword token for `async` without knowing its span. Options:

- Add `Token? AsyncKeyword` to both AST nodes (requires AST + all visitor updates)
- Approximate the span using the first token of the declaration minus one position (brittle)
- Reconstruct the span from the function keyword token span, offset by the length of `"async "` (fragile)

**Recommendation:** Add `Token? AsyncKeyword` to both AST nodes. This is a clean, correct
solution. It requires updating the constructor, all six visitors, and serialization if tokens
are serialized (unlikely). The implementation spec should verify whether tokens are serialized
in `.stashc` and handle accordingly.

### OQ-2: `defer await` — double soft keyword interaction

When both `defer` and `await` are demoted, `defer await expr` in statement position is:

- `defer` (identifier) followed by `await` (identifier) followed by `expr`
- The disambiguation for `defer` sees identifier `"await"` as the next token → DeferStmt rule
- Then `DeferStatement()` checks if the next token is the `await` keyword... but it's now an identifier!

`DeferStatement()` currently uses `Match(TokenType.Await)`. After `await` is demoted:

```csharp
// Change:
bool hasAwait = Match(TokenType.Await);
// To:
bool hasAwait = Check(TokenType.Identifier) && Peek().Lexeme == KwAwait && Advance() != null;
```

This interaction must be explicitly handled in the implementation.

### OQ-3: `async` in struct/interface method declarations

Method declarations inside `struct` bodies and `interface` definitions also support `async fn`.
The `"async"` + `fn` lookahead must be applied wherever method declarations are parsed, not
only at the top-level `Declaration()`. Audit all call sites of `FnDeclaration(isAsync: bool)`.

---

## 14. Relationship to Timeout Spec

This spec depends on the timeout soft-keyword spec being implemented first because:

1. The shared infrastructure (`IsFollowedByBlockAfterParens`, `SoftKeywords`, `IsExpressionStarter`)
   is introduced there and reused here.
2. The `keyword` semantic token type (§2.6) resolves OQ-2 from the timeout spec. The timeout
   implementation should **leave OQ-2 unimplemented** and wait for this spec to define the
   taxonomy change, so both are shipped together.
3. `TokenTypeKeyword` and `EmitKeyword()` should be added in a single commit alongside the
   timeout change (not separately) to avoid a state where `timeout` has no keyword highlighting.

Alternatively, both specs can be implemented in a single pass — the Orchestrator should
read both before starting implementation. Key shared artifacts introduced in the _Timeout_ spec
and reused here:

- `IsFollowedByBlockAfterParens(int startPos)` bounded lookahead helper
- `IsExpressionStarter(TokenType t)` predicate
- `SoftKeywords` string constants
- `TokenTypeKeyword` semantic token type and `EmitKeyword()` walker helper

> **Coordination note:** The _Timeout_ spec (already in `1-todo/`) left OQ-2 (semantic token
> type for the keyword) answered as Option A but not yet in the implementation checklist.
> This spec defines exactly what Option A means (§2.6). The Orchestrator implementing the
> timeout spec should read §2.6 of this spec before implementing the SemanticTokenWalker
> change, so the `keyword` type is added once and shared.

---

## Decision Log

| Date       | Decision                                                         | Notes                                                                                                              |
| ---------- | ---------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------ |
| 2026-04-28 | Include all six keywords in scope                                | User confirmed they want the full set, not just `timeout`                                                          |
| 2026-04-28 | `defer (expr)` → function call rule                              | The one genuine ambiguity; principled resolution that avoids backward-compatibility pain for common usage patterns |
| 2026-04-28 | `await (expr)` → always AwaitExpr                                | Calling a function named `await` is sufficiently unusual that the lookahead cost is not justified                  |
| 2026-04-28 | Add `keyword` semantic token type (Option A)                     | Resolves OQ-2 from timeout spec; cleanest long-term solution; benefits all soft keywords                           |
| 2026-04-28 | `onRetry` / `until` not included                                 | Already soft (string-comparison contextual keywords, never in the lexer keyword table); no changes needed          |
| 2026-04-28 | Implement in order: async/await → elevate → defer → retry → lock | Complexity-ordered; each builds on shared infrastructure                                                           |
