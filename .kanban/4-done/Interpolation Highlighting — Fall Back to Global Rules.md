# Interpolation Highlighting — Fall Back to Global Rules

**Status:** In Progress
**Created:** 2026-05-01
**Scope:** VS Code TextMate grammar (`stash.tmLanguage.json`); Monaco/Monarch grammar audit (`stash-language.js`); no Stash interpreter or analysis changes.

## 1. Problem

Syntax and semantic highlighting both fail inside string interpolations.

In `examples/highlighting.stash`:

```stash
io.println($"{env.get("HOME")}");        // env, get not highlighted
io.println($"Struct member: {variable.someValue}"); // variable, someValue flat
io.println("variable is Test: ${variable is Test}"); // Test not colored as type
io.println("Bool: ${false}/${true}");    // booleans DO highlight
io.println("Duration: ${24h}");          // rich literals DO highlight
```

Bool/null literals and rich numeric literals work; everything that depends on **identifier classification** (namespace members, struct types, dot-property access, UFCS calls, enum members, custom types) does not light up inside `${...}` or `{...}`.

## 2. Root Cause Analysis

There are two independent root causes; both must be fixed.

### 2.1 TextMate grammar wraps interpolations in `string.*`

In `.vscode/extensions/stash-lang/syntaxes/stash.tmLanguage.json`, every interpolation-bearing string sets `"name"` on its `begin/end` block:

| Repository rule                       | `name`                                    |
| ------------------------------------- | ----------------------------------------- |
| `interpolated-string-prefix` (`$"…"`) | `string.interpolated.stash`               |
| `triple-quoted-interpolated-string`   | `string.quoted.triple.interpolated.stash` |
| `string` (regular `"…"`)              | `string.quoted.double.stash`              |
| `triple-quoted-string`                | `string.quoted.triple.stash`              |

A `name` on a `begin/end` rule applies to **the entire matched range** including everything between the delimiters. So the interpolation content sits inside a `string.*` parent scope.

This breaks two things:

1. **TextMate themes** typically use a low-specificity selector like `string` to color string literals. That selector wins against most inner scopes (e.g. `variable.function`) under standard scope-selector specificity rules.
2. **VS Code semantic highlighting is suppressed inside `string.*` and `comment.*` scopes by default.** This is documented behavior: semantic tokens only override TextMate scopes when the token is _not_ inside a string or comment. Since `SemanticTokenWalker` correctly emits tokens for the inner expressions (verified — see §2.3), the data exists; VS Code just refuses to render it.

### 2.2 The `#expression` rule is an incomplete subset of `$self`

`string-interpolation-bare` (matches `{…}` inside `$"…"`) and `string-interpolation-dollar` (matches `${…}` inside regular `"…"`) both delegate to a private `#expression` repository rule:

```json
"expression": {
  "patterns": [
    { "include": "#comments" },
    { "include": "#constants" },
    { "include": "#triple-quoted-interpolated-string" },
    { "include": "#triple-quoted-string" },
    { "include": "#interpolated-string-prefix" },
    { "include": "#string" },
    { "include": "#numbers" },
    { "include": "#operators" },
    { "include": "#control-keywords" },
    { "include": "#function-call" },
    { "include": "#variable-declaration" },
    { "include": "#identifiers" }
  ]
}
```

It is missing every pattern that the top-level `patterns` array adds: `command-literal`, `async-await`, `return-type-annotation`, `struct-declaration`, `enum-declaration`, `interface-declaration`, `function-declaration`, `import-statement`, `extend-declaration`, `lambda-expression`, `brace-block`, `punctuation`. Every time a new top-level pattern is added, the interpolation context silently drifts further out of sync.

`command-interpolation` (matches `${…}` inside `$(…)` command literals) has a separate, also-divergent inline list of includes with the same problem.

### 2.3 Backend semantic data is correct — no interpreter or analysis change needed

Verified that the full pipeline already handles interpolations:

- **Lexer** ([Stash.Core/Lexing/Lexer.cs L1673](Stash.Core/Lexing/Lexer.cs#L1673)) — `ScanInterpolatedExpression` spawns an inner `Lexer` constructed with `exprStartLine, exprStartColumn`, so inner tokens carry correct source positions back to the original file.
- **Parser** ([Stash.Core/Parsing/Parser.cs L2902](Stash.Core/Parsing/Parser.cs#L2902)) — `ParseInterpolatedString` runs an inner `Parser.Parse()` (full `Expression()`) over those inner tokens and assembles them into `InterpolatedStringExpr.Parts`.
- **SymbolCollector** ([Stash.Analysis/Visitors/SymbolCollector.cs L1201](Stash.Analysis/Visitors/SymbolCollector.cs#L1201)) — recurses into every part.
- **SemanticResolver** ([Stash.Core/Resolution/SemanticResolver.cs L580](Stash.Core/Resolution/SemanticResolver.cs#L580)) — recurses into every part, resolving identifiers like `Test` to their declarations.
- **SemanticTokenWalker** ([Stash.Analysis/Visitors/SemanticTokenWalker.cs L692](Stash.Analysis/Visitors/SemanticTokenWalker.cs#L692)) — recurses into every non-`LiteralExpr` part. `ClassifyDotMember` correctly emits `TokenTypeNamespace` / `TokenTypeFunction` / `TokenTypeProperty` for `env.get`, `variable.someValue`, etc. `VisitIsExpr` emits `TokenTypeStruct` for `Test` via `_resolvedRefs`.

So **the analyzer classifies interpolation contents correctly today.**

> **Correction (2026-05-01):** The original spec asserted "the LSP returns correct semantic tokens for interpolation contents today." This was incorrect. The `SemanticTokenWalker` does emit classifications for inner identifiers, but `Stash.Lsp/Handlers/SemanticTokensHandler.cs::Tokenize` iterates `result.Tokens` linearly and `result.Tokens` contains a single outer `InterpolatedString` token whose inner expression tokens are nested in `token.Literal` as a `List<object>`. The handler never visits those inner tokens, so `builder.Push` was never called for identifiers inside `${…}` / `{…}`. **Fix applied:** added recursive `EmitTokens` helper in the handler that descends into the nested token lists for `InterpolatedString`, `CommandLiteral`, `PassthroughCommandLiteral`, `StrictCommandLiteral`, and `StrictPassthroughCommandLiteral` tokens.

### 2.4 Monaco grammar already does the right thing (mostly)

In `Stash.Playground/wwwroot/js/stash-language.js`, the `interpolationExpr` state is:

```js
interpolationExpr: [
    [/\}/, 'delimiter.bracket', '@pop'],
    { include: 'root' }
],
```

It pops on `}` and falls through to the `root` state. No `string` token wrap on entry. This is the pattern the user wants for the TextMate grammar. Audit required to confirm it produces the desired result for the playground.

## 3. Proposal

User-confirmed direction: anything inside `{…}` / `${…}` falls back to global rules, identical to `$self`. Document and implement the minimal changes.

### 3.1 Decisions (locked)

| Decision                                        | Choice                                       |
| ----------------------------------------------- | -------------------------------------------- |
| Meta scope on interpolation content             | `meta.interpolation.expression.stash`        |
| Apply same fix to `$(…)` command interpolations | Yes                                          |
| Fate of `#expression` repository rule           | Delete                                       |
| Monaco grammar audit                            | Required — verify parity, document any drift |

`meta.interpolation.expression.<lang>` scopes the embedded expression context without triggering VS Code's special embedded-language behavior (`meta.embedded.<lang>` is reserved for cross-language embedding via the `embeddedLanguages` package.json mapping; using it without the mapping caused the inner content to render as plain text in VS Code despite TextMate producing correct scopes).

## 4. Detailed Design

### 4.1 String rules — strip outer `name`, scope only literal content

Each interpolated-string repository rule becomes:

```json
"interpolated-string-prefix": {
  "begin": "\\$\"",
  "beginCaptures": {
    "0": { "name": "punctuation.definition.string.begin.stash string.interpolated.stash" }
  },
  "end": "\"",
  "endCaptures": {
    "0": { "name": "punctuation.definition.string.end.stash string.interpolated.stash" }
  },
  "patterns": [
    { "include": "#string-interpolation-bare" },
    { "include": "#string-escape" },
    {
      "name": "string.interpolated.stash",
      "match": "[^\"\\\\{}]+"
    }
  ]
}
```

Key changes vs. current:

- Remove the top-level `"name": "string.interpolated.stash"` from the `begin/end` rule.
- Move the `string.interpolated.stash` scope onto: (a) the open/close quote captures, (b) the literal-text `match`, (c) `#string-escape` which already names itself `constant.character.escape.stash`.
- The `#string-interpolation-bare` include is **outside** any string scope, so its content doesn't inherit `string.*`.

Apply the same surgery to:

- `triple-quoted-interpolated-string` (uses scope `string.quoted.triple.interpolated.stash`)
- `string` (uses scope `string.quoted.double.stash`)
- `triple-quoted-string` (uses scope `string.quoted.triple.stash`)

For the latter two (regular strings carrying `${…}`), the literal-text fallback patterns already exist (`[^"\\\\$]+` and `\\$(?!\\{)`); they just need the string scope explicit on those matches and the outer `name` removed.

### 4.2 Interpolation entry rules — `meta.embedded.expression.stash` + `$self`

```json
"string-interpolation-bare": {
  "name": "meta.embedded.expression.stash",
  "begin": "\\{",
  "beginCaptures": {
    "0": { "name": "punctuation.section.interpolation.begin.stash" }
  },
  "end": "\\}",
  "endCaptures": {
    "0": { "name": "punctuation.section.interpolation.end.stash" }
  },
  "patterns": [
    { "include": "$self" }
  ]
},

"string-interpolation-dollar": {
  "name": "meta.embedded.expression.stash",
  "begin": "\\$\\{",
  "beginCaptures": {
    "0": { "name": "punctuation.section.interpolation.begin.stash" }
  },
  "end": "\\}",
  "endCaptures": {
    "0": { "name": "punctuation.section.interpolation.end.stash" }
  },
  "patterns": [
    { "include": "$self" }
  ]
}
```

Changes:

- Replace `name: meta.interpolation.stash` with `meta.embedded.expression.stash`.
- Replace `{ "include": "#expression" }` with `{ "include": "$self" }`.

`$self` references the grammar's top-level `patterns` array — the single source of truth. Adding a new top-level pattern (e.g. a future `lock-statement` repo entry) automatically becomes available inside interpolations with no further edits.

### 4.3 Command interpolation — same fix

`command-interpolation` (used inside `$(…)` command literals and inside `command-double-string`/`command-single-string`) becomes:

```json
"command-interpolation": {
  "name": "meta.embedded.expression.stash",
  "begin": "\\$\\{",
  "beginCaptures": {
    "0": { "name": "punctuation.section.interpolation.begin.stash" }
  },
  "end": "\\}",
  "endCaptures": {
    "0": { "name": "punctuation.section.interpolation.end.stash" }
  },
  "patterns": [
    { "include": "$self" }
  ]
}
```

Same scope name as string interpolations — themes that want to distinguish "expression embedded in shell" from "expression embedded in string" can rely on the parent `meta.command.stash` scope.

### 4.4 Delete the `#expression` repository rule

Once §4.2 and §4.3 land, no rule references `#expression`. Remove the entire `"expression": { "patterns": [ ... ] }` entry from the repository.

### 4.5 Brace-nesting safety

`#string-interpolation-bare` begins with `\{` and ends with `\}`. TextMate handles nested begin/end matching — e.g. a struct literal like `Test { someValue: x }` inside `$"{Test { someValue: x }}"` works correctly:

1. `$"` → enter `interpolated-string-prefix`.
2. `{` → enter `string-interpolation-bare` (depth 1).
3. `$self` matches `Test` as identifier.
4. `{` → matched by `brace-block` (top-level `$self` includes it) → enter `brace-block`.
5. `}` → exit `brace-block`.
6. `}` → exit `string-interpolation-bare`.
7. `"` → exit `interpolated-string-prefix`.

`brace-block` is already present in the top-level `patterns` array, so `$self` exposes it. This is the _reason_ `$self` is correct here — `#expression` would have failed on this nesting because it doesn't include `brace-block`.

### 4.6 Monaco/Monarch audit

`Stash.Playground/wwwroot/js/stash-language.js` is reviewed during this work to confirm:

- `interpolatedString` and `tripleInterpolatedString` states do **not** apply a permanent `string` token to interpolation content — they switch to `interpolationExpr` on `{` and that state `include: 'root'`s. Confirmed correct.
- `command` / `string` (helper) states behave correctly when interpolation appears nested.
- Any drift from the TextMate grammar's behavior is documented as a follow-up note inside the spec, not silently fixed (Monaco rendering already works in the playground per current behavior).

If the audit finds the Monaco grammar is correct, no code change there. If it finds drift (e.g. a missed `${…}` path), file a follow-up note in this spec for a separate small task.

**Audit result (2026-05-01):** Monaco's `interpolationExpr` state correctly `include: 'root'` and pops on `}` — primary parity confirmed. One pre-existing minor drift noted (unrelated to this bug, no change made): the `string` helper state used from inside `command` (line 140 in `stash-language.js`) does not match `${…}`, so a regular `"…"` string inside a `$(…)` command would not enter `interpolationExpr`. This is independent of the interpolation-highlighting fix and can be addressed in a separate task if/when reported.

## 5. Interaction Analysis

| Concern                                                                | Outcome                                                                                                                                                                               |
| ---------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Bare-command-execution shell mode                                      | Unaffected — shell-mode classification happens before TM tokenization.                                                                                                                |
| Semantic token modifiers (`defaultLibrary`, `declaration`, `readonly`) | Now actually visible inside interpolations — VS Code will render them.                                                                                                                |
| Theme contributors                                                     | Existing themes that target `string.interpolated.stash` continue to work for delimiters/literal segments. New `meta.embedded.expression.stash` selector available for opt-in styling. |
| `meta.interpolation.stash` legacy scope                                | **Removed.** No themes in this repo target it (verified via grep). External themes that depend on it would lose targeting; documented in CHANGELOG.                                   |
| Triple-quoted strings                                                  | Same fix applies — both regular `"""…"""` (carries `${…}`) and prefixed `$"""…"""` (carries `{…}`).                                                                                   |
| Command literals `$(…)`                                                | Same fix for `command-interpolation`. The `meta.command.stash` parent scope is preserved.                                                                                             |
| Escapes inside interpolations                                          | `#string-escape` already self-scopes; behavior unchanged.                                                                                                                             |
| Performance                                                            | Neutral — `$self` is no more expensive than the old `#expression` (both are pattern-list dispatches in the oniguruma/textmate engine).                                                |

## 6. Cross-Platform Behavior

Pure editor-tooling change. No runtime behavior. No platform differences.

## 7. Test Plan

This is a TextMate-grammar change; .NET unit tests don't cover it. Manual verification via `examples/highlighting.stash` plus a new fixture file.

### 7.1 Manual visual verification

Reload the VS Code extension and open `examples/highlighting.stash`. Confirm:

| Construct                                  | Expected                                                                     |
| ------------------------------------------ | ---------------------------------------------------------------------------- |
| `${env.get("HOME")}`                       | `env` = namespace color, `get` = function color, `"HOME"` = string color     |
| `${variable.someValue}`                    | `variable` = variable color, `someValue` = property color                    |
| `${variable is Test}`                      | `variable` = variable, `is` = keyword.operator, `Test` = struct/type color   |
| `${false}` / `${true}` / `${null}`         | bool/null constant color (still works after fix)                             |
| `${24h}` / `${1.5KB}` / `${@10.0.0.0/24}`  | numeric/literal color (still works after fix)                                |
| `$"prefix {expr} suffix"` literal segments | string color                                                                 |
| `"text ${expr} text"` literal segments     | string color                                                                 |
| `$(echo ${env.get("HOME")})` shell command | `env`/`get` highlighted inside `${…}` even though outer is a command literal |
| Nested struct literal `${Test { x: 1 }}`   | Inner braces don't terminate the interpolation                               |

### 7.2 Add a TM fixture file

Add `examples/highlighting.stash` (or a sibling) coverage for:

- All four interpolated-string forms: `"…${e}…"`, `$"…{e}…"`, `"""…${e}…"""`, `$"""…{e}…"""`.
- All four expression categories that previously failed: namespace member call, dot-property access, struct-type `is` check, struct literal inside interpolation.
- Command literal with embedded interpolation: `$(cmd ${arg})`.

### 7.3 LSP semantic-token snapshot test (optional but recommended)

`Stash.Tests/Lsp/` already has semantic-token tests. Add one that lexes/analyzes a script with interpolations and asserts that the LSP returns tokens at the inner-expression positions. This catches regressions in the SemanticTokenWalker's recursion into `InterpolatedStringExpr.Parts` even though the current bug is purely TextMate-side.

## 8. LSP / DAP Implications

None. The LSP already returns correct semantic tokens for interpolations (verified §2.3). DAP is unaffected.

## 9. Migration / Breaking Changes

- **`meta.interpolation.stash` scope renamed → `meta.embedded.expression.stash`.** No themes inside this repo reference it (verified). External themes targeting it lose their selector. Documented in CHANGELOG.
- The outer string scope (`string.interpolated.stash` etc.) no longer covers interpolation content. External themes that relied on string-color bleeding into interpolations will see them light up in identifier/keyword colors instead. **This is the desired behavior — the bug fix.**

## 10. Files Touched

| File                                                           | Change                                                                   |
| -------------------------------------------------------------- | ------------------------------------------------------------------------ |
| `.vscode/extensions/stash-lang/syntaxes/stash.tmLanguage.json` | Restructure 4 string rules + 3 interpolation rules; delete `#expression` |
| `Stash.Playground/wwwroot/js/stash-language.js`                | Audit only; change only if drift found                                   |
| `CHANGELOG.md`                                                 | Note interpolation-highlighting fix + scope rename                       |
| `examples/highlighting.stash`                                  | Optionally extend to cover new fixture cases                             |

## 11. Decision Log

| Date       | Decision                                                                         | Rationale                                                                                                                                                |
| ---------- | -------------------------------------------------------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------- |
| 2026-05-01 | Use `meta.embedded.expression.stash` rather than keep `meta.interpolation.stash` | VS Code-canonical scope name for embedded expression contexts; communicates intent; avoids semantic-highlighting suppression that string-scopes trigger. |
| 2026-05-01 | Apply the same fix to `command-interpolation`                                    | Parity — identifiers inside `$(cmd ${env.x})` should highlight identically to identifiers in string interpolations.                                      |
| 2026-05-01 | Delete the `#expression` repository rule                                         | Single source of truth (`$self`) avoids future drift between top-level patterns and interpolation patterns.                                              |
| 2026-05-01 | Audit Monaco grammar but don't preemptively change it                            | It already routes interpolations through `root`. Premature edits risk regressing playground rendering that currently works.                              |
| 2026-05-01 | No backend (parser/analyzer/LSP) changes                                         | All four backend stages already produce correct data for interpolation contents (verified §2.3). The bug is exclusively in TM scope assignment.          |

## 12. Risks

| Risk                                                                                                               | Mitigation                                                                                                                                                                                       |
| ------------------------------------------------------------------------------------------------------------------ | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| Removing the outer `name` from string rules breaks themes that selected `string.interpolated.stash` for delimiters | Delimiters and literal-text segments still receive that scope explicitly. Verified `examples/highlighting.stash` retains string color on quote marks and between interpolations.                 |
| `$self` opens recursion through a context that wasn't tested for re-entry into strings                             | TextMate engine handles re-entry naturally; `interpolated-string-prefix` is in the top-level patterns, so recursive interpolations (e.g. `$"{$"{x}"}"`) already work today and continue to work. |
| Some user themes might color `meta.embedded.expression.stash` in unexpected ways                                   | Documented in CHANGELOG with migration note for theme authors.                                                                                                                                   |
| Triple-quoted-string changes interact with `${…}` containing `\"\"\"`                                              | Existing inner pattern `[^"\\\\$]+` plus the `$(?!\\{)` fallback handles this; no change to those matchers.                                                                                      |
