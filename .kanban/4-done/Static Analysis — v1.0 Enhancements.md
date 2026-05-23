# Static Analysis — v1.0 Enhancements

**Status:** Backlog — Design
**Created:** 2026-04-28
**Author:** Architect
**Derived from:** v1.0 Release Readiness, Section 2.8 — Static Analysis Additions

---

## 1. Context and Motivation

The analysis engine currently ships 77 diagnostic codes across 15 categories. It's broad but several important categories are underdeveloped or entirely absent:

- **Functions**: Only 5 rules. Missing async-correctness checks which are critical now that `async`/`await` is a first-class language feature.
- **Type safety**: No regex validation despite `str.match`/`str.capture` being common patterns — invalid patterns always throw at runtime.
- **Best practices**: No detection of the classic assignment-in-condition bug (`=` vs `==`), and no magic-number detection.
- **Performance**: Only one rule (`SA1201`). String concatenation in loops is the second most common performance mistake after the accumulating-spread problem already caught.
- **Security**: Only two rules. Catastrophic regex backtracking (ReDoS) is a known attack vector for scripts that process untrusted input.
- **Infrastructure**: Suppression directives lack any reason/justification field, making suppression audits difficult. Rule severity cannot be overridden per project.

The three items explicitly listed in Section 2.8 (`assignment-in-condition`, `async without await`, `regex validation`) are the nucleus. This spec expands them into a complete, coherent set of enhancements that collectively raise the quality bar for all three stated goals: correctness, quality-of-life, and developer-focused functionality.

**Current state summary:**

| Category          | Range  | Current codes | Highest |
| ----------------- | ------ | ------------- | ------- |
| Infrastructure    | SA00xx | 3             | SA0003  |
| Control Flow      | SA01xx | 14            | SA0163  |
| Declarations      | SA02xx | 10            | SA0210  |
| Type Safety       | SA03xx | 9             | SA0310  |
| Functions & Calls | SA04xx | 5             | SA0405  |
| Spread / Rest     | SA05xx | 6             | SA0506  |
| Commands          | SA07xx | 10            | SA0710  |
| Imports           | SA08xx | 3             | SA0804  |
| Locks             | SA081x | 5             | SA0814  |
| Style             | SA09xx | 1             | SA0901  |
| Complexity        | SA10xx | 1             | SA1002  |
| Best Practices    | SA11xx | 8             | SA1108  |
| Performance       | SA12xx | 1             | SA1201  |
| Security          | SA13xx | 2             | SA1302  |
| Suggestions       | SA14xx | 2             | SA1402  |

---

## 2. New Diagnostic Rules

Rules are grouped by their target SA category. Each rule includes: code, title, trigger, level, message format, fixability, implementation strategy, and edge cases.

---

### 2.1 Declarations (SA02xx)

#### SA0211 — Function Defined Inside Loop Body

**Trigger:** A named function declaration (`FnDeclStmt`) appears directly inside the body of a `for`, `while`, or `do-while` loop (any nesting depth within the loop, but not inside a nested function within the loop).

**Level:** Information

**Message:** `"Function '{0}' is defined inside a loop body. It will be recreated on every iteration — consider moving it outside the loop."`

**Fixable:** No (restructuring required — can't be automated safely)

**Rationale:** A named `fn` declaration inside a loop doesn't benefit from closure semantics in any useful way, and it rebuilds the function object (and any captured upvalues) on every pass. This is almost always unintentional. If the author wants a loop-scoped closure, a `let f = fn() { ... }` lambda is the correct idiom.

**Not triggered by:**

- Lambda expressions assigned to a variable inside a loop (`let handler = fn() { ... }`) — lambdas in loops are intentional.
- Named functions inside nested functions that are inside a loop — only the innermost enclosing function matters.

**Implementation:** Per-node rule on `FnDeclStmt`. Check `context.LoopDepth > 0 && context.FunctionDepth == outerFunctionDepthAtRule invocation`. Actually, since `RuleContext.LoopDepth` is passed by the validator, this is straightforward: if `context.LoopDepth > 0` when visiting a `FnDeclStmt` that is not inside a nested function (requires `context.FunctionDepth` check — the loop must be at the same function scope as the declaration). This needs careful depth accounting: the declaration itself increments `FunctionDepth` when entered, so the check fires before entry. The validator already tracks `_loopDepth` and `_functionDepth` separately. **RuleContext must expose `FunctionDepth`** (it does not currently — see Section 5.1).

**Edge cases:**

- Recursive named functions inside loops — still fire, still unintentional.
- `for ... in` loops — also trigger.

---

#### SA0212 — Declaration Shadows Built-in Namespace

**Trigger:** A variable or constant declaration (`VarDeclStmt`, `ConstDeclStmt`, parameter declaration) uses a name that exactly matches a built-in namespace identifier (`arr`, `str`, `dict`, `math`, `time`, `json`, `fs`, `path`, `env`, `sys`, `http`, `crypto`, `io`, `conv`, `process`, `term`, `encoding`, `ini`, `config`, `args`, `tpl`, `test`, `net`, `log`, `csv`, `archive`, `xml`, `yaml`, `toml`, `scheduler`).

**Level:** Warning

**Message:** `"'{0}' shadows the built-in namespace '{0}'. All '{0}.*' functions will be inaccessible in this scope."`

**Fixable:** No

**Rationale:** SA0207 catches variable-shadows-variable, but built-in namespaces do not appear in the scope tree as declared symbols — they are injected by the VM. If a user writes `let str = "hello"`, the `str` namespace becomes inaccessible for the rest of that scope, which silently breaks any `str.split()` calls. This is both confusing and hard to debug.

The existing `BuiltInNames` set in `RuleContext` contains namespace names. This rule simply cross-references the declared name against that set.

**Not triggered by:**

- Names that partially match (e.g., `strings`, `strUtil`) — only exact matches.
- Destructuring patterns (the destructured fields are not namespace names).
- Import aliases (handled separately; intentional re-binding).

**Implementation:** Per-node rule on `VarDeclStmt`, `ConstDeclStmt`, and the parameter list within `FnDeclStmt` and `LambdaExpr`. Check `context.BuiltInNames.Contains(declaredName)`.

---

### 2.2 Type Safety (SA03xx)

#### SA0311 — Invalid Regex Pattern

**Trigger:** A call to any of the six regex-using `str` functions (`str.match`, `str.matchAll`, `str.isMatch`, `str.capture`, `str.captureAll`, `str.replaceRegex`) where the pattern argument (position 1, zero-indexed) is a `LiteralExpr` of string type, and the string value fails to compile as a .NET regular expression.

**Level:** Error (an invalid pattern always throws `ParseError` at runtime; there is no benign case)

**Message:** `"Invalid regex pattern '{0}': {1}"` — where `{1}` is the `.NET` `ArgumentException.Message` from the failed compilation (trimmed to the first sentence to avoid noise).

**Fixable:** No

**Rationale:** All six regex functions pass the pattern string directly to .NET's `Regex` constructor. An invalid pattern is not a runtime-recoverable condition; the script will always fail. Detecting this at analysis time turns a runtime crash into a compile-time error visible in the editor. The analysis engine runs on .NET regardless of platform, so the compilation is always available.

**Implementation:**

```
Per-node rule on CallExpr.
1. Check callee is DotExpr with left = IdentifierExpr("str") and right = one of the six names.
2. Check args[1] is LiteralExpr<string>.
3. Attempt new Regex(pattern, RegexOptions.None) inside try/catch(ArgumentException).
4. If catch: report SA0311 with pattern value (truncated to 80 chars) and exception message (first sentence only).
```

The rule lives in `Stash.Analysis/Rules/TypeSafety/InvalidRegexPatternRule.cs`.

**Edge cases:**

- Named capture groups (`(?<name>...)`) — valid .NET syntax, must not trigger.
- Escaped backslashes in Stash strings — the `LiteralExpr` already holds the processed string value (post-escape), so the pattern passed to `Regex` is correct.
- Empty string pattern `""` — valid regex, must not trigger.
- Regex patterns built from string interpolation (`"prefix{var}suffix"`) — the pattern is not a pure literal; skip silently (SA0801-style "cannot resolve statically").

---

#### SA0312 — Regex Pattern With Potentially Catastrophic Backtracking

**Trigger:** A call to one of the six `str` regex functions where the pattern literal compiles successfully (not caught by SA0311) but contains a structural pattern known to exhibit exponential worst-case backtracking: nested quantifiers (`(a+)+`, `(a*)+`, `(a+)*`, `(a*)*`), or alternation with overlapping prefixes under a quantifier (`(a|ab)+`, `(foo|foobar)*`).

**Level:** Warning

**Message:** `"Regex pattern may have catastrophic backtracking. Nested quantifier at position {0}: '{1}'. Use atomic groups or possessive quantifiers, or restructure the pattern."`

**Fixable:** No

**Rationale:** Although Stash applies a 5-second timeout to regex operations, that timeout is per-call. A script processing many lines of untrusted input that matches a ReDoS pattern will time out on every call, producing `TimeoutError` storms. This check catches the most common ReDoS patterns structurally. It is not a complete ReDoS detector (that is NP-hard), but it catches the textbook cases.

**Detection heuristic (must implement):**

After successful `Regex` compilation, walk the pattern string with a simple state machine looking for:

- `(X+)+`, `(X*)+`, `(X+)*`, `(X*)*` — nested quantifiers on any group that contains a quantifier inside.
- `(A|B)+` where A is a prefix of B or vice versa — overlapping alternation under outer quantifier.

This does not require full regex parsing — a recursive descent over the pattern string at the character level is sufficient for the heuristic cases. False positive rate must be kept low; only flag patterns that are textbook examples.

**Implementation:** Same rule file as SA0311, or a companion `CatastrophicBacktrackingRule.cs`. Post-SA0311 check in the same `Analyze()` method using a helper `IsCatastrophicPattern(string pattern)`.

**Edge cases:**

- `.NET` added non-backtracking `(?>[...])` atomic groups — these are safe and must not trigger even if they contain nested quantifiers.
- `.NET` 7+ added source-generated regex and `RegexOptions.NonBacktracking` — but the Stash stdlib does not use these, so all patterns are potentially backtracking.
- Patterns that use possessive quantifiers (not supported in .NET) — these will already fail SA0311.

---

### 2.3 Functions & Calls (SA04xx)

#### SA0406 — Async Call Result Not Awaited

**Trigger:** A `CallExpr` at statement level (i.e., wrapped in `ExprStmt`) where the callee resolves to a known `async fn` declaration, and the call expression itself is not wrapped in an `AwaitExpr`.

**Level:** Warning

**Message:** `"Return value of async function '{0}' is not awaited. The operation will run in the background — use 'await' to wait for completion or explicitly discard with 'let _ = await {0}(...)'."`

**Fixable:** Unsafe (adding `await` changes control flow)

**Rationale:** Calling an `async fn` without `await` is legal in Stash (the task is dispatched and the caller continues), but it is almost always a mistake in scripting contexts. Sysadmin scripts are typically sequential, and silently dropping an async operation's completion/error is a correctness bug. Unlike application programming where fire-and-forget is common, a sysadmin script that uploads a file, sends an alert, or modifies a server state needs to know the operation succeeded.

**Implementation requirements:** This rule requires knowing whether the resolved function symbol is `async`. The `ScopeTree` stores function declarations; those declarations have `FnDeclStmt.IsAsync`. A lookup by name against the scope tree at the call site yields the `SymbolInfo`, which must surface `IsAsync` if available. **See Section 5.2 for the required `SymbolInfo` extension.**

If the callee is not a simple `IdentifierExpr` (e.g., it's a method on a struct, or a lambda stored in a variable), the rule fires only when the callee can be statically resolved. If resolution fails (dynamic dispatch, dict lookup, function stored in variable), the rule is suppressed for that call site.

**Not triggered by:**

- `let result = foo()` — result is captured; the caller may process it later.
- `await foo()` — the call is awaited.
- `task.all([foo(), bar()])` — the calls are intentionally non-awaited to allow parallel dispatch.
- Calls inside `task.all`, `task.race`, `task.any` argument arrays — these are intentional parallel dispatches.

**Edge cases:**

- `let _ = foo()` — result is explicitly discarded. **Still fires.** To suppress intentionally, the user must write `let _ = await foo()`. The explicit discard of an un-awaited task is suspicious enough to flag.
- UFCS calls on async functions: `data.asyncTransform()` — harder to resolve; lower priority. May need to be limited to direct name calls in v1.

---

#### SA0407 — Async Function Without Await

**Trigger:** A `FnDeclStmt` or `LambdaExpr` where `IsAsync = true` and the function body contains no `AwaitExpr` at any depth that is not inside a nested `async` function.

**Level:** Warning

**Message (FnDeclStmt):** `"Async function '{0}' has no 'await' expressions. The 'async' modifier is unnecessary unless the function is part of an interface contract."`

**Message (LambdaExpr):** `"Async lambda has no 'await' expressions. The 'async' modifier is unnecessary."`

**Fixable:** Unsafe (removing `async` changes the external contract)

**Rationale:** An `async fn` with no `await` still returns a `Task`-wrapped value, making callers await it unnecessarily. In most cases this is an oversight — the developer forgot to add `await` to an internal call, or copy-pasted `async` out of habit. The "unless part of an interface contract" qualifier handles the legitimate case: a function that must satisfy an `async`-typed interface method signature even if its current implementation happens to be synchronous.

**Implementation:** Per-node rule on `FnDeclStmt` and `LambdaExpr`. The rule walks the function body internally using a recursive helper that traverses all statements and expressions, looking for `AwaitExpr`, but stops recursion when it hits another `FnDeclStmt` or `LambdaExpr` (nested async functions don't count toward the outer function's await inventory).

```csharp
private static bool ContainsAwait(IEnumerable<Stmt> stmts)
{
    foreach (var stmt in stmts)
    {
        if (StmtContainsAwait(stmt)) return true;
    }
    return false;
}

private static bool StmtContainsAwait(Stmt stmt) => stmt switch
{
    // Stop at nested function boundaries
    FnDeclStmt => false,
    ExprStmt es => ExprContainsAwait(es.Expression),
    // ... recurse into blocks, if/else, loops, try, etc.
    _ => false
};

private static bool ExprContainsAwait(Expr expr) => expr switch
{
    AwaitExpr => true,
    LambdaExpr => false,  // Nested lambda — stop
    // ... recurse into binary, unary, call, dot, etc.
    _ => false
};
```

**Not triggered by:**

- `async fn poll() { return false; }` — if `poll` is implementing an interface with an `async`-typed method signature. The rule fires but can be suppressed with `// suppress SA0407 "implements IPoller.poll async contract"`.
- Functions declared `async` in a struct's `extend` block for interface conformance — same.

**Edge cases:**

- A function that calls another `async fn` but without `await` — the body has a `CallExpr` to an async function, but no `AwaitExpr`. SA0406 and SA0407 may both fire. The combination is intentional: SA0406 says "you're not awaiting this", SA0407 says "you don't await anything at all". Both are separately actionable.
- Async lambda in a non-async function that calls `task.all([...])` — the lambda might legitimately be `async fn() { await x }`. The outer function has an async lambda but no direct await. SA0407 should not fire for the outer function in this case because `task.all` is the mechanism. However, since the outer function is not async itself, SA0407 doesn't apply to it. Only fires on the lambda or outer async fn.

---

### 2.4 Style (SA09xx)

#### SA0902 — Function Body Too Long

**Trigger:** A `FnDeclStmt` or top-level `LambdaExpr` whose body spans more than a configurable threshold of lines (default: **60 lines**, configurable via `.stashcheck` as `max_function_lines`).

**Level:** Information

**Message:** `"Function '{0}' is {1} lines long, exceeding the threshold of {2}. Consider breaking it into smaller functions."`

**Fixable:** No (restructuring cannot be automated)

**Rationale:** Long functions are a readability and maintainability smell. The cyclomatic complexity rule (SA0109) catches structurally complex functions; SA0902 catches functions that are long even if structurally simple (e.g., a 200-line switch-case factory). Both rules are complementary.

The default threshold of 60 lines is deliberately generous — Stash is a scripting language where a 40-line function is routine. Teams with stricter standards can lower it in `.stashcheck`.

**Implementation:** Per-node rule on `FnDeclStmt`. Count lines from `fn.Body.OpenBrace.Line` to `fn.Body.CloseBrace.Line`. The `SourceSpan` on each node carries line/column information.

**Configurable:** Yes — `max_function_lines` key in `.stashcheck` `[rules.SA0902]` section. Default 60.

**Not triggered by:** Built-in function declarations (these are C# methods, not Stash AST nodes).

---

### 2.5 Best Practices (SA11xx)

#### SA1109 — Assignment Used as Condition

**Trigger:** The condition expression of an `IfStmt`, `WhileStmt`, `DoWhileStmt`, or the middle expression of a `ForStmt` (when present) is, or contains at its top level after unwrapping any `GroupingExpr`, an `AssignExpr`.

**Level:** Warning

**Message:** `"Assignment used as condition. Did you mean '==' instead of '='? If the assignment is intentional, suppress this warning with a comment."`

**Fixable:** Unsafe (changing `=` to `==` is the common fix, but not always correct)

**Rationale:** This is one of the oldest and most common bugs in C-style languages. Stash allows assignment as an expression (the `AssignExpr` node can appear anywhere an expression is valid), which means `if (x = 5)` is syntactically legal but almost certainly a typo for `if (x == 5)`. Every major linter (ESLint `no-cond-assign`, C# CS0665, clang `-Wparentheses`) catches this.

The only legitimate intentional case in Stash-like scripting is `while (line = io.readLine())` — reading until null. This is rare and suppressable.

**Detection:**

```
Unwrap condition:
  condition = StripGrouping(condition)  // remove any wrapping GroupingExpr

if (condition is AssignExpr):
    fire SA1109
```

`StripGrouping` recursively unwraps `GroupingExpr` wrappers (parentheses in the source). This fires on both `if (x = 5)` and `if ((x = 5))`.

**Not triggered by:**

- `for (let i = 0; i < n; i++)` — the `let i = 0` initializer is not a condition.
- `let x = if (cond) a else b` — this is a ternary/switch expression, not a control flow condition.
- Compound expressions: `if (a && (b = c))` — only the top-level condition is checked; sub-expressions are **not** checked (too many false positives for patterns like short-circuit assignment).

**Edge cases:**

- `if (x = null)` — still fires; the user probably meant `if (x == null)`.
- Ternary condition: `cond ? a : b` where cond is an assign — not checked (ternary conditions are intentional expressions).

---

#### SA1110 — Magic Number

**Trigger:** A numeric `LiteralExpr` (integer or float) whose value is not in the set `{-1, 0, 1, 2, 100}` appears more than once in the **same file** outside of named constant declarations (`ConstDeclStmt`) and default parameter values.

**Level:** Information (lowest severity — this is advisory only)

**Message:** `"Magic number {0} appears {1} times. Consider extracting it as a named constant for clarity and maintainability."`

**Fixable:** No (requires naming the constant, which is a semantic decision)

**Rationale:** Repeated magic numbers are a readability and maintenance smell. When `3600` appears six times in a script, it's unclear whether all six mean "one hour". Extracting it to `const SECONDS_PER_HOUR = 3600` makes the intent clear and ensures all uses are updated together.

The exemption set `{-1, 0, 1, 2, 100}` covers universally understood constants that don't benefit from naming (`-1` for "not found", `0` for "none", `1` for "increment", etc.).

**Implementation:** Post-walk rule. Collect all `LiteralExpr` with numeric values; group by value; report any value with count ≥ 2 that is not in the exemption set. Report on the **second and subsequent occurrences** (span pointing at each repeated use, not just the first).

**Configurable:** `magic_number_threshold` (default 2) and `magic_number_exemptions` (default `[-1, 0, 1, 2, 100]`) in `.stashcheck`.

**Not triggered by:**

- Values inside `const` declarations — that is the fix we're recommending.
- Values in default parameter values (`fn foo(n = 10)`) — default parameters are named at their use site.
- Values that appear only once — no magic at one occurrence.

---

### 2.6 Performance (SA12xx)

#### SA1202 — String Concatenation in Loop

**Trigger:** Inside the body of any loop (`for`, `for-in`, `while`, `do-while`), a statement of the form `s = s + expr`, `s = expr + s`, or `s += expr` where `s` can be inferred to hold a string value (either declared with a string literal initializer, or typed as `string` in the scope).

**Level:** Warning

**Message:** `"String concatenation in a loop creates a new string object on each iteration (O(n²) allocations). Build an array of parts and join them: use 'arr.push()' then 'arr.join(\"\")' outside the loop."`

**Fixable:** No (restructuring required)

**Rationale:** This is the string-building anti-pattern. In Stash (as in most runtimes), strings are immutable. `s = s + item` creates a new string every iteration, allocating O(n²) total memory for n iterations. The idiomatic solution is to collect parts in an array and join once. SA1201 already catches the equivalent accumulating-spread pattern on arrays; SA1202 closes the gap for strings.

**Implementation:** Per-node rule on `AssignExpr` (for `s = s + expr`) and `UpdateExpr` with `+=` (for `s += expr`). Must:

1. Check `context.LoopDepth > 0`.
2. For `s = s + expr`: both sides reference `s` and the `+` operator is string concatenation.
3. For `s += expr`: the target `s` must be a string.
4. Infer string type via `ScopeTree`: look up `s`'s symbol and check its inferred type, or fall back to "initialized with string literal".

**Not triggered by:**

- One-time concatenation at loop exit: `result = parts.join("") + suffix` — not in loop body.
- String interpolation: `s = "prefix {expr} suffix"` — interpolation is compiled differently and is not the same pattern.
- Loop bodies that concatenate two non-loop-varying strings (both constants) — SA1107 (constant condition) would handle similar cases.

**Edge cases:**

- `s = s + sep + item` — still triggers (multiple concatenations in one statement).
- `other = s + item` (assignment to a different variable) — does not trigger (no accumulation on `s`).

---

#### SA1203 — Repeated Function Call in Loop Condition

**Trigger:** The condition expression of a `WhileStmt` or `DoWhileStmt`, or the condition portion of a `ForStmt`, contains a `CallExpr` that is a known pure/read-only function call (specifically: `arr.len()`, `str.len()`, `dict.len()`, `dict.keys()`, or any explicit `*.len()` dot call) where the receiver is an identifier that is not modified inside the loop body.

**Level:** Information

**Message:** `"'{0}.{1}()' is called on every loop iteration in the condition. If '{0}' does not change inside the loop, cache the result before the loop: 'let n = {0}.{1}()'"`

**Fixable:** Unsafe (caching changes semantics if the collection is modified inside the loop)

**Rationale:** `for (let i = 0; i < items.len(); i++)` recalculates `items.len()` on every iteration. When the collection is not modified inside the loop body, this is pure overhead. The fix is `let n = items.len()` before the loop. This is a micro-optimization but is a very common pattern in Stash scripts that process large arrays.

**Implementation:** Per-node rule on `ForStmt`, `WhileStmt`, `DoWhileStmt`. Walk the condition for `DotExpr { right: "len" | "size", left: IdentifierExpr }` calls. Then check the loop body: does any statement assign to or mutate the identifier? (Look for `AssignExpr { Name = receiverName }` and `IndexAssignExpr` on the receiver, and calls that mutate — `arr.push(arr)` etc.). If no mutation is found, fire SA1203.

Keep the mutation check conservative: if any method is called on the receiver, assume potential mutation (e.g., `items.sort(fn(a,b) { ... })`). Only fire confidently when the receiver is read-only within the loop.

**Edge cases:**

- `while (items.len() > 0 && items.first() != null)` — fires only once per condition (not once per `len()` call, deduplicate on receiver+method).
- The receiver `items` is reassigned inside the loop — do not fire.

---

### 2.7 Security (SA13xx)

#### SA1303 — Catastrophic Regex Backtracking Risk

This rule is the security-focused counterpart to SA0312 (which is the type-safety framing). The distinction:

- **SA0312** (Type Safety): fires when a regex pattern structurally contains nested quantifiers — a correctness/behavior concern. Severity: Warning.
- **SA1303** (Security): fires when the input that will be matched against the regex is derived from an external source (command output, `io.readLine()`, HTTP response, file content, function parameter) and the pattern has nested quantifiers. Severity: Warning, higher signal-to-noise than SA0312 alone.

**Full spec for SA1303:**

**Trigger:** Same structural pattern detection as SA0312 (nested quantifiers / overlapping alternation), AND the first argument to the regex call (`str.match`, etc.) is:

- A function parameter (user-provided input)
- A result of `io.readLine()`, `io.read()`, `fs.readText()`, `http.*` responses
- A command output expression `$(...)`

**Level:** Warning

**Message:** `"Regex pattern with potential catastrophic backtracking is applied to externally-sourced input '{0}'. An attacker could craft input to cause a denial-of-service. Restructure the pattern or use atomic groups."`

**Rationale:** ReDoS becomes an actual attack vector only when the input is attacker-controlled. A regex applied to a hardcoded internal string is a performance issue; the same regex applied to HTTP request data is a security vulnerability. SA1303 makes this taint-based distinction explicit.

**Implementation:** Requires taint tracking — a simplified version where "tainted" sources are known function calls and parameter references, not a full taint analysis. The rule is a post-walk rule that first identifies tainted variables (those assigned from known external sources) and then checks whether those variables are used as the input to regex calls with dangerous patterns.

This is **the most complex rule in this spec**. It should be implemented after SA0312 is stable.

---

### 2.8 Suggestions (SA14xx)

#### SA1403 — Prefer String Interpolation Over Concatenation

**Trigger:** A binary `+` expression where both operands are strings (one is a `LiteralExpr` with string type or an `InterpolatedStringExpr`, and the other is an `IdentifierExpr` or any non-literal expression), appearing **outside a loop body** (to avoid conflicting with SA1202).

**Level:** Information

**Message:** `"String concatenation can be simplified using string interpolation. Consider: `\"{0}{1}\"`instead of`\"{2}\" + {3}`."`

**Fixable:** Safe (the auto-fix can mechanically convert `"prefix" + expr` to `"prefix{expr}"`, and `expr + "suffix"` to `"{expr}suffix"`)

**Rationale:** String interpolation in Stash (`"Hello {name}!"`) is more readable than concatenation (`"Hello " + name + "!"`). It also produces fewer intermediate string objects. This is purely a style/readability suggestion.

**Implementation:** Per-node rule on `BinaryExpr` with `+` operator. Check:

1. Both operands involve at least one string literal or interpolated string.
2. The expression is not inside a loop body (`context.LoopDepth == 0`).
3. The expression is not already the argument to `arr.join()` or `str.concat()`.

**Not triggered by:**

- `str1 + str2` where both are non-literal — unclear intent, don't suggest interpolation.
- String concatenation inside format strings themselves — would create deeply nested interpolation.
- Cases where the string literal contains `{` or `}` characters that would need escaping — the fix would introduce interpolation syntax errors; skip these.

**Edge cases:**

- Multi-part chain: `"a" + b + "c" + d` — fire once per BinaryExpr node (multiple diagnostics on a chain). The auto-fix for chains would need to collapse all into a single interpolation; mark as Unsafe in that case.

---

## 3. Infrastructure and Quality-of-Life Improvements

These are not new diagnostic rules but improvements to the analysis infrastructure that affect the developer experience across all rules.

---

### 3.1 Suppression Directive Reason Field

**Current syntax:**

```stash
// suppress SA0201
let _unused = computeExpensiveThing();
```

**Proposed syntax:**

```stash
// suppress SA0201 "kept for side-effect: computeExpensiveThing() registers a global"
let _unused = computeExpensiveThing();
```

**Behavior changes:**

- The reason string (everything after the code, inside quotes) is parsed by `SuppressionDirectiveParser` and stored alongside the suppression.
- LSP hover on a suppression directive shows the reason string: `SA0201 suppressed — "kept for side-effect: computeExpensiveThing() registers a global"`.
- The reason is **optional** — existing directives without a reason continue to work.
- SA0003 (Unused suppression directive) hover text includes the reason, making audits easier.
- A new configuration option (`require_suppression_reason: true` in `.stashcheck`) makes the reason **required**. When required, a suppression directive without a reason is reported as SA0003 with a different message: `"Suppression directive for '{0}' has no reason. Add a justification in quotes."`.

**Implementation files:** `SuppressionDirectiveParser.cs`, `SuppressionMap.cs`, LSP `HoverHandler.cs`.

---

### 3.2 Rule Severity Override in `.stashcheck`

**Current state:** Every rule has a `DefaultLevel` (Error/Warning/Information) that cannot be changed per project.

**Proposed `.stashcheck` addition:**

```toml
[rules]
SA0109.level = "error"     # Promote cyclomatic complexity from info → error
SA0207.level = "none"      # Disable shadow variable warning entirely
SA0902.max_function_lines = 40   # Override configurable threshold
SA1110.level = "none"      # Disable magic number (too noisy for this project)
```

**Behavior:**

- `level` accepts: `"error"`, `"warning"`, `"info"` (or `"information"`), `"none"` (disabled).
- `"none"` fully disables the rule — no diagnostics emitted, no suppression needed.
- Per-rule threshold overrides (for rules that already support `IConfigurableRule`) are consolidated under the same `[rules]` section key using dot notation.
- Unknown rule codes in `[rules]` emit SA0001 (unknown diagnostic code) just as with suppression directives.

**Implementation files:** `ProjectConfig.cs` / `DomainConfig.cs`, `AnalysisEngine.cs` (apply override before emitting), `DiagnosticDescriptor.cs` (add `EffectiveLevel` that accounts for project override), `EditorConfigParser.cs` (or `.stashcheck` parser — determine which owns project config).

---

### 3.3 Severity Presets

Allow teams to select a named severity profile in `.stashcheck`:

```toml
preset = "strict"   # or "default", "relaxed", "pedantic"
```

**Profiles:**

| Rule                         | relaxed | default | strict  | pedantic |
| ---------------------------- | ------- | ------- | ------- | -------- |
| SA0109 Cyclomatic complexity | none    | info    | warning | error    |
| SA0201 Unused declaration    | none    | info    | warning | warning  |
| SA0205 Let could be const    | none    | info    | info    | warning  |
| SA0207 Shadow variable       | none    | warning | warning | error    |
| SA0404 Missing return        | warning | warning | error   | error    |
| SA0902 Function too long     | none    | info    | warning | error    |
| SA1110 Magic number          | none    | none    | info    | warning  |
| SA1202 String concat in loop | none    | warning | warning | error    |
| SA1403 Prefer interpolation  | none    | none    | info    | info     |

The preset is a **baseline** — individual `[rules]` overrides apply on top.

**Default preset:** `"default"` — behaves identically to the current behavior with no `.stashcheck` present.

---

## 4. RuleContext and SymbolInfo Extensions

Several new rules require data not currently available in `RuleContext` or `SymbolInfo`. These are prerequisite changes.

---

### 4.1 Add `FunctionDepth` to `RuleContext`

**Required by:** SA0211 (function defined in loop body)

**Current state:** `RuleContext` exposes `LoopDepth` and `FunctionDepth` — actually wait, checking against the current code, `RuleContext` currently exposes:

- `LoopDepth` ✅
- `FunctionDepth` — **not currently exposed**. SemanticValidator tracks `_functionDepth` privately but does not pass it to `RuleContext`.

**Change:** Add `int FunctionDepth { get; init; }` to `RuleContext`. Pass `_functionDepth` (before incrementing for the current function) when building the context for `FnDeclStmt`.

**Impact:** Zero impact on existing rules (additive field).

---

### 4.2 Add `AsyncDepth` to `RuleContext`

**Required by:** SA0407 (async function without await) and potentially SA0406

**Current state:** `_asyncDepth` is private to `SemanticValidator`. Rules for async-correctness need to know if the current context is inside an async function.

**Change:** Add `int AsyncDepth { get; init; }` to `RuleContext`. Pass `_asyncDepth` when building rule contexts.

**Impact:** Zero impact on existing rules. SA0153 (defer await without async) is currently baked into the validator; it could optionally be migrated to a proper rule using `AsyncDepth`.

---

### 4.3 Extend `SymbolInfo` with `IsAsync`

**Required by:** SA0406 (async call not awaited)

**Current state:** `SymbolInfo` is the model for a declared symbol in the scope tree. It likely has fields for `Name`, `Kind` (function/variable/etc.), and parameter count (to support SA0401). It does not expose `IsAsync`.

**Change:** Add `bool IsAsync { get; init; }` to `SymbolInfo`. Populate it in `SymbolCollector.cs` when visiting `FnDeclStmt.IsAsync`.

**Impact:** Additive. The LSP `HoverHandler` may also want to surface `async` in hover text for function symbols — this extension enables that as a free side effect.

---

## 5. Code Fix Inventory

New rules that get auto-fix support:

| Rule                           | Fix Type                              | Description                                       |
| ------------------------------ | ------------------------------------- | ------------------------------------------------- |
| SA0407 Async without await     | Unsafe                                | Remove `async` modifier from function declaration |
| SA1109 Assignment in condition | Unsafe                                | Replace `=` with `==` in condition                |
| SA1403 Prefer interpolation    | Safe (single concat) / Unsafe (chain) | Convert `"a" + b` to `"a{b}"`                     |
| SA0212 Shadows built-in        | None                                  | Cannot be automated — rename is semantic          |
| SA0311 Invalid regex           | None                                  | Pattern must be fixed manually                    |
| SA0902 Function too long       | None                                  | Structural refactor                               |

**Existing fixable rules gaining improvements:**

- SA0205 (Let could be const) — already Safe fixable; consider adding "Fix All in file" support via the LSP `CodeActionHandler`.
- SA0802 (Unused import) — already Safe fixable; add "Fix All in file" support.
- SA0804 (Import ordering) — already Safe fixable; add "Fix All in file" support.

"Fix All in file" is a new LSP code action kind (`source.fixAll`) that applies all safe fixes for a given rule across the entire file in a single action. Implementation in `CodeActionHandler.cs`.

---

## 6. Implementation Notes by Rule

### Implementation Ordering (suggested)

The rules fall into three tiers by implementation complexity:

**Tier A — Simple, implement first:**

1. SA1109 (Assignment in condition) — per-node, 15-20 lines
2. SA0212 (Shadows built-in) — per-node, 10 lines
3. SA0407 (Async without await) — per-node with body walk, 40 lines; requires `AsyncDepth` in `RuleContext`
4. SA0311 (Invalid regex) — per-node, 20 lines + try/catch
5. SA0211 (Function in loop) — per-node, 10 lines; requires `FunctionDepth` in `RuleContext`
6. SA1403 (Prefer interpolation) — per-node, 25 lines

**Tier B — Medium complexity:** 7. SA0902 (Function too long) — per-node with span line counting, 15 lines 8. SA1202 (String concat in loop) — per-node, 30 lines + type inference 9. SA1110 (Magic number) — post-walk, 30 lines 10. SA0312 (Catastrophic backtracking) — per-node, 50 lines for heuristic pattern walker 11. SA1203 (Loop condition repeated call) — per-node, 40 lines + mutation check

**Tier C — Complex, implement last:** 12. SA0406 (Async call not awaited) — per-node; requires `SymbolInfo.IsAsync` extension 13. SA1303 (Tainted regex ReDoS) — post-walk; requires simplified taint tracking

---

### File Placement

All new rule files follow the existing convention of one file per rule in the appropriate subdirectory:

```
Stash.Analysis/Rules/
  BestPractices/
    NoAssignmentInConditionRule.cs    → SA1109
    NoMagicNumbersRule.cs             → SA1110
  Declarations/
    NoFunctionInLoopRule.cs           → SA0211
    NoBuiltInShadowRule.cs            → SA0212
  Functions/
    NoDiscardedAsyncCallRule.cs       → SA0406
    NoAsyncWithoutAwaitRule.cs        → SA0407
  Performance/
    NoStringConcatInLoopRule.cs       → SA1202
    NoRepeatedLoopConditionCallRule.cs → SA1203
  Security/
    NoCatastrophicBacktrackingRule.cs → SA1303
  Style/
    NoLongFunctionRule.cs             → SA0902
  Suggestions/
    PreferStringInterpolationRule.cs  → SA1403
  TypeSafety/
    InvalidRegexPatternRule.cs        → SA0311, SA0312 (companion methods)
```

---

### Descriptor Registrations

All new descriptors must be added to `DiagnosticDescriptors.cs` and registered in `BuildCodeLookup()`. New codes in sequence:

| Code   | Category          | Next available after |
| ------ | ----------------- | -------------------- |
| SA0211 | Declarations      | SA0210               |
| SA0212 | Declarations      | SA0211 (this spec)   |
| SA0311 | Type Safety       | SA0310               |
| SA0312 | Type Safety       | SA0311 (this spec)   |
| SA0406 | Functions & Calls | SA0405               |
| SA0407 | Functions & Calls | SA0406 (this spec)   |
| SA0902 | Style             | SA0901               |
| SA1109 | Best Practices    | SA1108               |
| SA1110 | Best Practices    | SA1109 (this spec)   |
| SA1202 | Performance       | SA1201               |
| SA1203 | Performance       | SA1202 (this spec)   |
| SA1303 | Security          | SA1302               |
| SA1403 | Suggestions       | SA1402               |

---

## 7. Test Scenarios

Each rule requires a dedicated test class in `Stash.Tests/Analysis/`. Tests follow the `{Feature}_{Scenario}_{Expected}()` naming convention.

### SA1109 — Assignment in Condition

```
NoAssignmentInCondition_IfStatement_ReportsWarning
NoAssignmentInCondition_WhileStatement_ReportsWarning
NoAssignmentInCondition_DoWhile_ReportsWarning
NoAssignmentInCondition_ForCondition_ReportsWarning
NoAssignmentInCondition_Parenthesized_ReportsWarning          // if ((x = 5))
NoAssignmentInCondition_CompoundAnd_DoesNotFire               // if (a && (b = c)) — not top-level
NoAssignmentInCondition_ForInitializer_DoesNotFire            // for (let i = 0; ...)
NoAssignmentInCondition_TernaryCondition_DoesNotFire
NoAssignmentInCondition_Suppressed_DoesNotFire
```

### SA0407 — Async Without Await

```
AsyncWithoutAwait_NamedFunction_ReportsWarning
AsyncWithoutAwait_Lambda_ReportsWarning
AsyncWithoutAwait_FunctionWithNestedAsyncLambda_DoesNotFire   // outer has no await but inner does (different scope)
AsyncWithoutAwait_FunctionWithDirectAwait_DoesNotFire
AsyncWithoutAwait_FunctionWithAwaitInsideIf_DoesNotFire
AsyncWithoutAwait_FunctionWithAwaitInsideLoop_DoesNotFire
AsyncWithoutAwait_EmptyBody_ReportsWarning
AsyncWithoutAwait_Suppressed_DoesNotFire
```

### SA0311 — Invalid Regex Pattern

```
InvalidRegex_StrMatch_InvalidPattern_ReportsError
InvalidRegex_StrIsMatch_InvalidPattern_ReportsError
InvalidRegex_StrCapture_InvalidPattern_ReportsError
InvalidRegex_StrReplaceRegex_InvalidPattern_ReportsError
InvalidRegex_ValidPattern_DoesNotFire
InvalidRegex_InterpolatedPattern_DoesNotFire                  // not a pure literal
InvalidRegex_EmptyPattern_DoesNotFire                         // empty string is valid
InvalidRegex_NamedCapture_ValidPattern_DoesNotFire
InvalidRegex_UnclosedGroup_ReportsError                       // "(abc"
InvalidRegex_InvalidQuantifier_ReportsError                   // "a{2,1}"
```

### SA0312 — Catastrophic Backtracking

```
CatastrophicBacktracking_NestedQuantifier_ReportsWarning       // (a+)+
CatastrophicBacktracking_NestedStarStar_ReportsWarning         // (a*)*
CatastrophicBacktracking_OverlappingAlternation_ReportsWarning // (a|aa)+
CatastrophicBacktracking_AtomicGroup_DoesNotFire               // (?>a+)+ is safe
CatastrophicBacktracking_SimplePattern_DoesNotFire             // \d+\.\d+
```

### SA0211 — Function in Loop

```
FunctionInLoop_ForLoop_ReportsInfo
FunctionInLoop_WhileLoop_ReportsInfo
FunctionInLoop_ForInLoop_ReportsInfo
FunctionInLoop_LambdaInLoop_DoesNotFire                        // lambdas are intentional
FunctionInLoop_FunctionOutsideLoop_DoesNotFire
FunctionInLoop_NestedFunctionInsideLoop_ReportsInfo            // fn inside fn inside loop — fires for inner fn
FunctionInLoop_FunctionInLoopBody_Suppressed_DoesNotFire
```

### SA0212 — Shadows Built-in

```
ShadowsBuiltin_LetStr_ReportsWarning
ShadowsBuiltin_LetArr_ReportsWarning
ShadowsBuiltin_Parameter_ReportsWarning
ShadowsBuiltin_PartialName_DoesNotFire                        // "strings" is fine
ShadowsBuiltin_NonBuiltinName_DoesNotFire
ShadowsBuiltin_Suppressed_DoesNotFire
```

### SA1202 — String Concat in Loop

```
StringConcatInLoop_PlusEquals_ReportsWarning
StringConcatInLoop_AssignSelf_ReportsWarning                   // s = s + x
StringConcatInLoop_NonStringVariable_DoesNotFire
StringConcatInLoop_OutsideLoop_DoesNotFire
StringConcatInLoop_Interpolation_DoesNotFire                   // s = "prefix{item}" is fine
StringConcatInLoop_TwoDifferentVariables_DoesNotFire           // other = s + item
```

### SA1110 — Magic Number

```
MagicNumber_RepeatedLiteral_ReportsInfo
MagicNumber_ZeroOneMinusOne_DoesNotFire                        // exempted constants
MagicNumber_AppearingOnce_DoesNotFire
MagicNumber_InsideConst_DoesNotFire
MagicNumber_DefaultParameter_DoesNotFire
MagicNumber_Suppressed_DoesNotFire
```

### SA0902 — Function Too Long

```
LongFunction_ExceedsDefault_ReportsInfo
LongFunction_AtThreshold_DoesNotFire
LongFunction_ConfiguredThreshold_RespectsOverride
LongFunction_ShortFunction_DoesNotFire
```

### SA1403 — Prefer Interpolation

```
PreferInterpolation_LiteralPlusIdent_ReportsInfo
PreferInterpolation_IdentPlusLiteral_ReportsInfo
PreferInterpolation_InsideLoop_DoesNotFire                     // SA1202 takes priority
PreferInterpolation_TwoNonLiterals_DoesNotFire
PreferInterpolation_LiteralContainsBrace_DoesNotFire
PreferInterpolation_Suppressed_DoesNotFire
```

### SA0406 — Async Call Not Awaited

```
AsyncCallNotAwaited_DirectCall_ReportsWarning
AsyncCallNotAwaited_AwaitedCall_DoesNotFire
AsyncCallNotAwaited_InsideTaskAll_DoesNotFire
AsyncCallNotAwaited_SyncFunction_DoesNotFire
AsyncCallNotAwaited_ResultCaptured_DoesNotFire                 // let r = foo() — result captured
```

---

## 8. Cross-Cutting Concerns

### Cross-Platform Behavior

All new rules are purely syntactic/semantic — they analyze AST structure. There are no platform-specific code paths. The regex compilation in SA0311/SA0312 uses .NET's `System.Text.RegularExpressions` which is identical across all platforms.

### LSP Impact

- New rules automatically appear in LSP diagnostics (pushed via `PublishDiagnosticsHandler`) — no additional LSP work required.
- New fixable rules (SA0407, SA1109, SA1403) need `CodeFix` objects returned from `CodeActionHandler`. Each fix corresponds to a text edit at the diagnostic span.
- Suppression reason field (Section 3.1) requires `HoverHandler` changes to display the reason in suppression directive hover text.
- "Fix All in file" (Section 5) requires `CodeActionHandler` changes to aggregate all safe fixes per rule.

### Formatter Impact

None. New rules are diagnostic-only. The formatter does not need to know about new rule codes.

### `.stashcheck` Config Format

The `.stashcheck` config additions (severity override, presets, per-rule thresholds) affect `ProjectConfig.cs` and `DomainConfig.cs`. The config format extension must be documented in the project's docs (wherever `.stashcheck` is documented — likely `docs/Stash — Language Specification.md` or a dedicated check config doc).

---

## 9. Decision Log

| Date       | Decision                                                                                             | Rationale                                                                                                                                                                                           |
| ---------- | ---------------------------------------------------------------------------------------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| 2026-04-28 | Split regex into SA0311 (invalid) and SA0312/SA1303 (ReDoS)                                          | They have different severity, different detection strategies, and different audiences (SA0311 is always-wrong, ReDoS is context-dependent)                                                          |
| 2026-04-28 | SA0407 fires on LambdaExpr too, not just FnDeclStmt                                                  | `async fn() { return 5 }` as a lambda is equally suspicious                                                                                                                                         |
| 2026-04-28 | SA1109 does not check sub-expressions of compound conditions                                         | `if (a && (b = c))` has too many legitimate patterns (e.g., short-circuit assignment). Only top-level condition checked.                                                                            |
| 2026-04-28 | SA1202 does not fire for string interpolation                                                        | Interpolation (`"prefix{item}"`) compiles to a `StringBuilder` internally, not string concatenation — it's already the recommended form                                                             |
| 2026-04-28 | SA1303 (tainted regex) deferred to Tier C                                                            | Requires simplified taint analysis infrastructure that doesn't exist yet. SA0312 covers the structural detection without taint.                                                                     |
| 2026-04-28 | Magic number exemption set: {-1, 0, 1, 2, 100}                                                       | -1 (not found), 0 (zero/none), 1 (unit), 2 (pair), 100 (percentage) are universally understood without names. Adding 10 and 1000 would create too many false positives for loop bounds and timeouts |
| 2026-04-28 | Severity presets use "default" as the no-config baseline                                             | Ensures zero behavioral change for existing users with no `.stashcheck`                                                                                                                             |
| 2026-04-28 | `FunctionDepth` and `AsyncDepth` added to RuleContext instead of baking rules into SemanticValidator | Keeps rules composable and testable in isolation. The validator's depth tracking is already correct; just needs exposure.                                                                           |
| 2026-04-28 | SA0406 limited to direct name resolution in v1                                                       | Resolving async-ness through UFCS, struct method calls, and function-in-variable patterns is too complex for v1. Limit to direct `CallExpr { Callee: IdentifierExpr }` calls.                       |
