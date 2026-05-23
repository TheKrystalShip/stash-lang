## Syntax and Semantic Highlighting — Full Overhaul

> **Document type:** Design / Architecture
> **Status:** Draft
> **Created:** 2026-04-18
> **Supersedes:** `Syntax Highlighting — Taxonomy, Scope Policy, and Semantic Token Strategy.md`
> **Audience:** Stash core maintainer
> **Purpose:** Define a correct, IDE-agnostic, industry-standard syntax and semantic highlighting architecture for Stash. This spec addresses VS Code (TextMate + LSP semantic tokens), Neovim/Helix/Zed (tree-sitter + LSP semantic tokens), and any editor supporting the Language Server Protocol.

---

## 1. Executive Summary

Stash's highlighting was built incrementally without following established conventions. The result is fragile, non-standard, and VS Code-specific. This spec defines a ground-up plan with three pillars:

1. **Fix the TextMate grammar** — 8 critical scope misclassifications, 13 important structural issues, and 12 minor polish items.
2. **Narrow and correct the semantic token layer** — stop emitting tokens for lexical constructs (strings, numbers, comments, operators); adopt the full LSP standard taxonomy (`struct`, `enum`, `method`, `defaultLibrary`, `async`).
3. **Build a tree-sitter grammar** — required for Neovim, Helix, Zed, and GitHub code navigation. This is the path to IDE-agnostic highlighting.

The guiding principle: **Stash emits portable classifications, not editor-specific colors. Themes decide colors; we decide categories.**

---

## 2. Industry Architecture: The Two-Layer Model

Every well-maintained language uses a two-layer highlighting model:

| Layer                   | Technology                             | Knowledge Level                                                                       | Latency                 |
| ----------------------- | -------------------------------------- | ------------------------------------------------------------------------------------- | ----------------------- |
| **Syntactic** (Layer 1) | TextMate grammar or tree-sitter parser | Lexical/structural — keywords, literals, operators, punctuation, string interpolation | Instant (per-keystroke) |
| **Semantic** (Layer 2)  | LSP `textDocument/semanticTokens`      | Full semantic — resolved types, scopes, symbol kinds, stdlib origin                   | Async (100ms+)          |

**Key rules:**

- Syntactic layer is the baseline — works without a language server, provides immediate feedback.
- Semantic layer overlays and refines — adds information that regex/grammar rules cannot determine.
- When both produce a token for the same span, **semantic wins**.
- Semantic tokens should **never** flatten richer syntactic detail into a more generic category.

### 2.1 What each layer should own

**Syntactic layer (TextMate / tree-sitter):**

- Keywords (`if`, `for`, `fn`, `return`, `struct`, `enum`, `async`, `await`)
- Operators (`+`, `-`, `==`, `&&`, `|>`, `??`)
- String literals (with interpolation boundaries)
- Numeric literals (integer, float, hex, binary, octal, duration, bytesize, semver, IP)
- Boolean and null constants (`true`, `false`, `null`)
- Comments (line, block, doc)
- Punctuation (braces, parens, semicolons, commas, dots)
- Storage type/modifier keywords (`let`, `const`, `async`)
- Command literal `$(...)` boundaries
- Template delimiters and raw template text

**Semantic layer (LSP):**

- Identifier classification (variable vs function vs type vs parameter vs namespace)
- `struct` vs `enum` vs `interface` type distinction
- `method` vs `function` call classification (including UFCS)
- `declaration` modifier at definition sites
- `readonly` for `const` bindings, `self`, `attempt`, enum members
- `defaultLibrary` for built-in namespace functions
- `async` modifier for async functions
- `property` for struct field access
- `enumMember` for enum variants

---

## 3. Current State Audit

### 3.1 TextMate Grammar — Critical Issues

The root cause of most problems is a single catch-all pattern in `constants` that dumps `is|and|or|in|fn|return|self|attempt` into `constant.language.stash`. These 8 tokens span 4 different scope families:

| Token     | Current Scope             | Correct Scope                 | Why                                   |
| --------- | ------------------------- | ----------------------------- | ------------------------------------- |
| `fn`      | `constant.language.stash` | `storage.type.function.stash` | Function declaration keyword          |
| `return`  | `constant.language.stash` | `keyword.control.flow.stash`  | Control flow keyword                  |
| `self`    | `constant.language.stash` | `variable.language.stash`     | Reserved variable (like `this` in JS) |
| `attempt` | `constant.language.stash` | `variable.language.stash`     | Reserved variable in error-handling   |
| `and`     | `constant.language.stash` | `keyword.operator.word.stash` | Word operator                         |
| `or`      | `constant.language.stash` | `keyword.operator.word.stash` | Word operator                         |
| `is`      | `constant.language.stash` | `keyword.operator.word.stash` | Type-checking operator                |
| `in`      | `constant.language.stash` | `keyword.operator.word.stash` | Membership/iteration operator         |

Additionally: `async` and `await` are **completely absent** from the grammar — they receive no highlighting at all in normal code positions.

### 3.2 TextMate Grammar — Important Issues

| Issue                                                         | Current                          | Correct                                                          |
| ------------------------------------------------------------- | -------------------------------- | ---------------------------------------------------------------- | ----------------------------- |
| `let`/`const` scoped as `keyword.control`                     | `keyword.control.stash`          | `storage.type.stash`                                             |
| Function calls use `entity.name.function.call`                | Non-standard `.call` suffix      | `variable.function.stash` (Sublime standard for call-site)       |
| Struct/enum/interface names nested under `entity.name.type.*` | `entity.name.type.struct.stash`  | `entity.name.struct.stash` (Sublime: avoid nesting under `type`) |
| Doc comments (`///`) not distinguished                        | Same as `//`                     | `comment.line.documentation.stash`                               |
| No `meta.*` containers                                        | Missing                          | `meta.function`, `meta.struct`, `meta.enum`, `meta.interface`    |
| `                                                             | >` pipe operator missing         | Not matched                                                      | `keyword.operator.pipe.stash` |
| `const` variables get `readwrite` scope                       | `variable.other.readwrite.stash` | `variable.other.constant.stash`                                  |
| Interpolation uses `punctuation.definition.*`                 | Non-standard                     | `punctuation.section.interpolation.*`                            |
| Imported names all scoped as functions                        | `entity.name.function.stash`     | Leave unstyled; let semantic tokens handle                       |
| Lambda `fn(x) =>` not handled                                 | Falls to `constant.language`     | Needs dedicated anonymous function pattern                       |

### 3.3 Semantic Token Layer — Issues

| Problem                                                 | Current Behavior                | Correct Behavior                 |
| ------------------------------------------------------- | ------------------------------- | -------------------------------- |
| Built-ins use `readonly`                                | `function.readonly`             | `function.defaultLibrary`        |
| Structs/enums flatten to `type`                         | `type`                          | `struct` / `enum`                |
| Methods flatten to `function`                           | `function`                      | `method`                         |
| UFCS calls classified as `function`                     | `function.readonly`             | `method.defaultLibrary`          |
| `self`/`attempt` silently skipped                       | No token emitted                | `variable.readonly`              |
| No `async` modifier                                     | Missing                         | `function.async.declaration`     |
| Strings/numbers/comments/operators emitted semantically | Overrides richer grammar scopes | Don't emit — let TextMate handle |
| Doc comment tags hardcoded in C#                        | Brittle `@param`/`@return` list | Grammar/injection driven         |
| Lexical tokens dominate legend                          | 6 of 13 types are lexical       | 0 lexical types in legend        |

### 3.4 Editor Support Gap

| Editor           | Syntactic Support                              | Semantic Support                                            |
| ---------------- | ---------------------------------------------- | ----------------------------------------------------------- |
| **VS Code**      | TextMate grammar (exists, buggy)               | LSP semantic tokens (exists, weak taxonomy)                 |
| **Neovim**       | None (TextMate not supported)                  | LSP semantic tokens only (exists but no syntactic baseline) |
| **Helix**        | None                                           | LSP semantic tokens only                                    |
| **Zed**          | None                                           | LSP semantic tokens only                                    |
| **GitHub**       | TextMate grammar (for Linguist, if registered) | None                                                        |
| **Sublime Text** | TextMate grammar (shared)                      | None                                                        |

Neovim, Helix, and Zed all use tree-sitter for syntactic highlighting. Without a tree-sitter grammar, Neovim users get **only** LSP semantic tokens with no syntactic baseline — resulting in a visually poor experience during server startup, during typing pauses, and for any lexical construct the semantic layer doesn't emit.

---

## 4. Design Decisions

### Decision 1: Three-Grammar Architecture

**Chosen.**

Stash will maintain three syntactic grammar representations:

| Grammar     | Editor                                     | Format                                             | Repository                                                    |
| ----------- | ------------------------------------------ | -------------------------------------------------- | ------------------------------------------------------------- |
| TextMate    | VS Code, Sublime Text, GitHub Linguist     | `.tmLanguage.json`                                 | `.vscode/extensions/stash-lang/syntaxes/`                     |
| Tree-sitter | Neovim, Helix, Zed, GitHub code navigation | `grammar.js` → generated C parser + `.scm` queries | `tree-sitter-stash/` (separate repo or monorepo subdirectory) |
| Monarch     | Stash Playground (Monaco editor)           | JavaScript tokenizer                               | `Stash.Playground/wwwroot/js/stash-language.js`               |

All three must cover the same lexical constructs. The tree-sitter grammar is the most important addition — it unlocks the entire non-VS-Code editor ecosystem.

**Alternatives considered:**

- Tree-sitter only, generate TextMate grammar from it. Rejected — tooling for this is immature and lossy.
- TextMate only. Rejected — locks out Neovim, Helix, Zed (the editors most likely to be used by sysadmins who would use Stash).
- Vim regex syntax files. Rejected — legacy, fragile, inferior to tree-sitter in every way.

**Rationale:** This is what every serious language does. Rust has all three. Go has all three. TypeScript has all three. The maintenance cost is real but unavoidable if Stash wants cross-editor support.

**Risks:**

- Three grammars must be kept in sync as the language evolves. Every new keyword or syntax construct must be added to all three.
- Tree-sitter grammar is a significant upfront effort (500-2000 lines of `grammar.js`).

**Mitigation:** The Monarch tokenizer already exists and is simple. The TextMate grammar already exists and needs fixes, not a rewrite. The tree-sitter grammar is the only net-new work.

### Decision 2: LSP Semantic Tokens Are the Single Source of Semantic Truth

**Chosen.**

The LSP `textDocument/semanticTokens` protocol is the **only** mechanism for semantic highlighting. It works identically in VS Code, Neovim, Helix, and Zed. No editor-specific semantic highlighting logic.

This means:

- The `SemanticTokenWalker` in `Stash.Analysis` produces the token stream.
- The `SemanticTokensHandler` in `Stash.Lsp` serves it over LSP.
- Every editor that speaks LSP gets the same semantic information.

**Alternatives considered:**

- Tree-sitter `locals.scm` for basic semantic info (scope tracking, definition/reference matching). Rejected as primary mechanism — tree-sitter locals are too coarse for Stash's symbol system (UFCS, built-in namespaces, extend methods). But `locals.scm` can be used as a supplement for editors without LSP.

**Rationale:** The LSP semantic token protocol was specifically designed for this. It's the industry standard. Building semantic intelligence into tree-sitter queries would be duplicating work that the LSP already does better.

### Decision 3: Adopt the Full LSP Standard Taxonomy

**Chosen.**

The semantic token legend must align with the LSP 3.17 standard types and modifiers that all editors understand.

**New legend — 11 symbol-identity types:**

| Index | Type         | Stash Constructs                                                       |
| ----- | ------------ | ---------------------------------------------------------------------- |
| 0     | `namespace`  | Built-in namespaces (`arr`, `dict`, `str`), imported module aliases    |
| 1     | `type`       | Generic type references in annotations (future non-struct named types) |
| 2     | `struct`     | Struct declarations and references                                     |
| 3     | `enum`       | Enum declarations and references                                       |
| 4     | `interface`  | Interface declarations and references                                  |
| 5     | `function`   | Free function declarations, free function calls                        |
| 6     | `method`     | Member-call targets, extend methods, UFCS calls                        |
| 7     | `parameter`  | Function parameters                                                    |
| 8     | `variable`   | Mutable variables                                                      |
| 9     | `property`   | Struct fields, dict key access (dot syntax)                            |
| 10    | `enumMember` | Enum variants                                                          |

**Removed from legend:**

| Removed Type | Reason                                    |
| ------------ | ----------------------------------------- |
| `keyword`    | Lexical — TextMate/tree-sitter handles it |
| `number`     | Lexical                                   |
| `string`     | Lexical                                   |
| `comment`    | Lexical                                   |
| `operator`   | Lexical                                   |

**New modifiers — 4 core:**

| Bit | Modifier         | Meaning                                                      |
| --- | ---------------- | ------------------------------------------------------------ |
| 0   | `declaration`    | Definition site                                              |
| 1   | `readonly`       | Immutable binding (`const`, `self`, `attempt`, enum members) |
| 2   | `defaultLibrary` | Built-in / standard library symbol                           |
| 3   | `async`          | Async function or method                                     |

**Deferred modifiers** (future phases):

| Modifier        | When                                                    |
| --------------- | ------------------------------------------------------- |
| `deprecated`    | When static analysis can detect deprecation annotations |
| `documentation` | For symbols referenced inside doc comments              |
| `modification`  | Write-access tracking (assignment targets)              |

**Alternatives considered:**

- Keep the current 13-type legend with lexical types. Rejected — emitting generic `string`/`number`/`operator` semantically erases richer grammar detail.
- Add all 23 LSP standard types. Rejected — Stash doesn't need `class`, `typeParameter`, `event`, `macro`, `decorator`, `regexp`, `label`, or `modifier` (the keyword kind). Only include types Stash actually uses.

**Rationale:** TypeScript, rust-analyzer, and gopls all use this approach. It's battle-tested.

### Decision 4: TextMate Scope Policy — Follow Sublime Text Conventions

**Chosen.**

The TextMate grammar must use scopes from the Sublime Text scope naming standard (the de facto authority for TextMate scopes). This ensures compatibility with every theme, not just VS Code's built-in ones.

**Required scope mapping:**

| Stash Construct                           | Correct TextMate Scope                                                |
| ----------------------------------------- | --------------------------------------------------------------------- |
| `fn` (declaration keyword)                | `storage.type.function.stash`                                         |
| `let`                                     | `storage.type.stash`                                                  |
| `const`                                   | `storage.type.stash`                                                  |
| `struct`, `enum`, `interface`, `extend`   | `storage.type.stash`                                                  |
| `async`                                   | `storage.modifier.async.stash`                                        |
| `await`                                   | `keyword.control.flow.stash`                                          |
| `if`, `else`                              | `keyword.control.conditional.stash`                                   |
| `for`, `while`, `do`, `break`, `continue` | `keyword.control.loop.stash`                                          |
| `switch`, `case`, `default`               | `keyword.control.conditional.stash`                                   |
| `try`, `catch`, `finally`, `throw`        | `keyword.control.trycatch.stash`                                      |
| `defer`                                   | `keyword.control.stash`                                               |
| `return`                                  | `keyword.control.flow.stash`                                          |
| `import`, `from`                          | `keyword.control.import.stash`                                        |
| `as`                                      | `keyword.control.stash`                                               |
| `elevate`, `retry`, `timeout`             | `keyword.control.stash`                                               |
| `and`, `or`                               | `keyword.operator.word.stash`                                         |
| `is`                                      | `keyword.operator.word.stash`                                         |
| `in`                                      | `keyword.operator.word.stash`                                         |
| `self`                                    | `variable.language.stash`                                             |
| `attempt`                                 | `variable.language.stash`                                             |
| `true`, `false`                           | `constant.language.boolean.stash`                                     |
| `null`                                    | `constant.language.null.stash`                                        |
| Function name (declaration)               | `entity.name.function.stash`                                          |
| Function name (call-site)                 | `variable.function.stash`                                             |
| Struct name (declaration)                 | `entity.name.struct.stash`                                            |
| Enum name (declaration)                   | `entity.name.enum.stash`                                              |
| Interface name (declaration)              | `entity.name.interface.stash`                                         |
| Parameter                                 | `variable.parameter.stash`                                            |
| `const` variable name                     | `variable.other.constant.stash`                                       |
| `let` variable name                       | `variable.other.readwrite.stash`                                      |
| Struct field (declaration)                | `variable.other.member.stash`                                         |
| Operators                                 | `keyword.operator.{subfamily}.stash`                                  |
| Integers                                  | `constant.numeric.integer.stash`                                      |
| Floats                                    | `constant.numeric.float.stash`                                        |
| Hex                                       | `constant.numeric.hex.stash`                                          |
| Binary                                    | `constant.numeric.binary.stash`                                       |
| Octal                                     | `constant.numeric.octal.stash`                                        |
| Durations                                 | `constant.numeric.duration.stash`                                     |
| Byte sizes                                | `constant.numeric.bytesize.stash`                                     |
| Strings                                   | `string.quoted.double.stash`                                          |
| Triple-quoted strings                     | `string.quoted.triple.stash`                                          |
| Interpolation delimiters                  | `punctuation.section.interpolation.begin/end.stash`                   |
| Escape sequences                          | `constant.character.escape.stash`                                     |
| Line comments (`//`)                      | `comment.line.double-slash.stash`                                     |
| Block comments (`/* */`)                  | `comment.block.stash`                                                 |
| Doc comments (`///`)                      | `comment.line.documentation.stash`                                    |
| Command literal `$(...)`                  | `meta.command.stash` with `string.unquoted.command.stash` for content |
| `${}` / `{}` in interpolation             | `punctuation.section.interpolation.begin/end.stash`                   |

**Meta containers (structural, not colored):**

| Context                     | Meta Scope                       |
| --------------------------- | -------------------------------- |
| Function declaration body   | `meta.function.stash`            |
| Function parameters         | `meta.function.parameters.stash` |
| Struct declaration body     | `meta.struct.stash`              |
| Enum declaration body       | `meta.enum.stash`                |
| Interface declaration body  | `meta.interface.stash`           |
| Function call (name + args) | `meta.function-call.stash`       |
| Import statement            | `meta.import.stash`              |

### Decision 5: `semanticTokenScopes` Fallback Map — LSP Standard Only

**Chosen.**

The `package.json` `semanticTokenScopes` section maps LSP semantic types to TextMate scopes for theme compatibility. Only symbol-identity types need mapping (lexical types are removed from the legend):

```json
{
  "semanticTokenScopes": [
    {
      "language": "stash",
      "scopes": {
        "namespace": ["entity.name.namespace.stash"],
        "type": ["entity.name.type.stash"],
        "struct": ["entity.name.struct.stash"],
        "enum": ["entity.name.enum.stash"],
        "interface": ["entity.name.interface.stash"],
        "function": ["entity.name.function.stash"],
        "method": ["entity.name.function.member.stash"],
        "parameter": ["variable.parameter.stash"],
        "variable": ["variable.other.readwrite.stash"],
        "property": ["variable.other.member.stash"],
        "enumMember": ["variable.other.enummember.stash"],
        "variable.readonly": ["variable.other.constant.stash"],
        "variable.defaultLibrary": ["support.variable.stash"],
        "function.defaultLibrary": ["support.function.stash"],
        "namespace.defaultLibrary": ["support.type.stash"]
      }
    }
  ]
}
```

Note the use of `support.*` for `defaultLibrary`-modified tokens. This follows the Sublime convention: `support.*` scopes are for "framework/library-provided" constructs, which is exactly what `defaultLibrary` means.

### Decision 6: Tree-sitter Grammar Scope — What to Build

**Chosen.**

The tree-sitter grammar (`grammar.js`) must cover all syntactic constructs. The highlight queries (`highlights.scm`) must use the standard Neovim capture names.

**Required Neovim capture mapping:**

| Stash Construct                           | Tree-sitter Capture                               |
| ----------------------------------------- | ------------------------------------------------- |
| `fn`                                      | `@keyword.function`                               |
| `let`, `const`                            | `@keyword.modifier`                               |
| `struct`, `enum`, `interface`, `extend`   | `@keyword.type`                                   |
| `async`                                   | `@keyword.coroutine`                              |
| `await`                                   | `@keyword.coroutine`                              |
| `if`, `else`, `switch`, `case`, `default` | `@keyword.conditional`                            |
| `for`, `while`, `do`, `break`, `continue` | `@keyword.repeat`                                 |
| `try`, `catch`, `finally`, `throw`        | `@keyword.exception`                              |
| `defer`                                   | `@keyword`                                        |
| `return`                                  | `@keyword.return`                                 |
| `import`, `from`                          | `@keyword.import`                                 |
| `as`                                      | `@keyword`                                        |
| `and`, `or`, `is`, `in`                   | `@keyword.operator`                               |
| `self`                                    | `@variable.builtin`                               |
| `attempt`                                 | `@variable.builtin`                               |
| `true`, `false`                           | `@boolean`                                        |
| `null`                                    | `@constant.builtin`                               |
| Function name (definition)                | `@function`                                       |
| Function name (call)                      | `@function.call`                                  |
| Struct name                               | `@type`                                           |
| Enum name                                 | `@type`                                           |
| Interface name                            | `@type`                                           |
| Parameter                                 | `@variable.parameter`                             |
| `const` variable                          | `@constant`                                       |
| `let` variable                            | `@variable`                                       |
| Struct field                              | `@property`                                       |
| Enum member                               | `@constant`                                       |
| Operators                                 | `@operator`                                       |
| Integers                                  | `@number`                                         |
| Floats                                    | `@number.float`                                   |
| Strings                                   | `@string`                                         |
| Escape sequences                          | `@string.escape`                                  |
| Interpolation delimiters                  | `@punctuation.special`                            |
| Comments                                  | `@comment`                                        |
| Doc comments                              | `@comment.documentation`                          |
| Command literal                           | `@string.special`                                 |
| Punctuation                               | `@punctuation.delimiter` / `@punctuation.bracket` |

The tree-sitter `locals.scm` should define scopes for:

- Function bodies (scope)
- Block statements (scope)
- For loops (scope)
- Function parameters (definition)
- Variable declarations (definition)
- Identifier references (reference)

### Decision 7: Monarch Tokenizer Must Stay in Sync

**Chosen.**

The Monarch tokenizer in `Stash.Playground/wwwroot/js/stash-language.js` serves the browser-based playground and must use the same classification logic as the TextMate grammar. Since Monaco supports a subset of TextMate-like token names, the Monarch tokenizer should map to the same conceptual categories.

No structural change needed — just ensure it's updated whenever the TextMate grammar changes.

### Decision 8: Doc Comment Tags — Grammar-Driven, Not Semantic Handler

**Chosen.**

Doc comment tags (`@param`, `@return`, `@returns`, `@example`, `@throws`, `@deprecated`, `@since`, `@see`) should be handled by the grammar (TextMate and tree-sitter), not by hardcoded logic in `SemanticTokensHandler.cs`.

**TextMate approach:** Add patterns within the doc comment rule to match `@tagname`:

- `@param`, `@return`, etc. → `storage.type.annotation.stash` or `keyword.other.documentation.stash`
- The parameter name after `@param` → `variable.parameter.stash`

**Tree-sitter approach:** The grammar defines `doc_comment` nodes with `tag` and `tag_name` children. The highlight query maps:

- `(tag_name) @keyword` within doc comments
- `(identifier) @variable.parameter` after `@param`

This removes the brittle hardcoded tag list from `SemanticTokensHandler.cs`.

---

## 5. Implementation Plan

### Phase 1: TextMate Grammar Overhaul

**Goal:** Fix all critical and important scope misclassifications. Make the grammar correct by Sublime Text standards.

**Tasks:**

1. **Break apart the `constants` catch-all pattern.** Remove `is|and|or|in|fn|return|self|attempt` from the single regex. Create separate rules:
   - `self`, `attempt` → `variable.language.stash`
   - `and`, `or`, `is`, `in` → `keyword.operator.word.stash`
   - `return` → move to `control-keywords` as `keyword.control.flow.stash`
   - `fn` → ensure `storage.type.function.stash` everywhere (declaration + lambda)

2. **Add `async` and `await` keywords.** `async` → `storage.modifier.async.stash`, `await` → `keyword.control.flow.stash`.

3. **Fix `let`/`const` scopes.** Change from `keyword.control.stash` to `storage.type.stash`. Split `const` declarations to assign `variable.other.constant.stash` to the variable name.

4. **Fix function call scope.** Change `entity.name.function.call.stash` to `variable.function.stash`.

5. **Fix entity name nesting.** Change `entity.name.type.struct.stash` to `entity.name.struct.stash`, same for enum and interface.

6. **Add doc comment distinction.** `///` → `comment.line.documentation.stash` before the `//` pattern. `/**` → `comment.block.documentation.stash` before `/*`.

7. **Add `meta.*` containers.** Wrap function declarations in `meta.function.stash`, struct bodies in `meta.struct.stash`, etc.

8. **Fix pipe operator.** Add `|>` → `keyword.operator.pipe.stash` before the bitwise `|` pattern.

9. **Fix interpolation punctuation.** Change `punctuation.definition.interpolation.*` to `punctuation.section.interpolation.*`.

10. **Add lambda `fn` pattern.** Handle `fn(x) => x + 1` (anonymous function without a name).

11. **Fix imported name scopes.** Remove `entity.name.function.stash` from imported identifiers. Leave them unstyled or use `variable.other.stash` and let semantic tokens refine.

12. **Keyword specificity.** Subdivide `keyword.control.stash` into `conditional`, `loop`, `trycatch`, `flow`, `import` sub-scopes.

13. **Add doc comment tag patterns.** Within doc comment rules, match `@param`, `@return`, etc. and the parameter name after `@param`.

**Files changed:**

- `.vscode/extensions/stash-lang/syntaxes/stash.tmLanguage.json`
- `.vscode/extensions/stash-lang/syntaxes/stash-tpl.tmLanguage.json` (if applicable)
- `Stash.Playground/wwwroot/js/stash-language.js` (keep Monarch in sync)

**Testing:**

- Manually verify with `Developer: Inspect Editor Tokens and Scopes` in VS Code.
- Verify with Dark+, Light+, and one popular third-party theme.
- Snapshot test: create a representative `.stash` file and capture the expected scopes.

### Phase 2: Semantic Token Layer Correction

**Goal:** Adopt the standard 11-type, 4-modifier legend. Stop emitting lexical tokens. Correctly classify all symbol kinds.

**Tasks:**

1. **Update `SemanticTokenConstants.cs`.** New legend: 11 types (namespace, type, struct, enum, interface, function, method, parameter, variable, property, enumMember), 4 modifiers (declaration, readonly, defaultLibrary, async).

2. **Update `SemanticTokenWalker.cs`.**
   - `MapSymbolKind()`: Map `SymbolKind.Struct` → `struct`, `SymbolKind.Enum` → `enum`, `SymbolKind.Method` → `method`.
   - Built-ins: Replace `ModifierReadonly` with `ModifierDefaultLibrary`.
   - UFCS calls: Classify as `method.defaultLibrary`.
   - `self`/`attempt`: Emit `variable.readonly` instead of skipping.
   - Async functions: Emit `function.async` or `method.async`.
   - Remove `ClassifyKeyword()` if present — keywords are not semantic tokens.

3. **Update `SemanticTokensHandler.cs`.**
   - Remove Phase 2 (linear token iteration for strings, numbers, comments, operators, keywords).
   - Remove `ProcessCommandLiteral()`, `ProcessCompoundToken()`, `EmitDocComment()`.
   - Only emit the walker's symbol tokens.
   - Update the legend registration to match the new 11-type, 4-modifier set.

4. **Update `package.json` `semanticTokenScopes`.** Replace the current 13-type mapping with the new 11-type mapping including `support.*` fallbacks for `defaultLibrary`.

5. **Update tests in `LspFeaturesTests.cs`.** Add/update tests for:
   - Struct → `struct` (not `type`)
   - Enum → `enum` (not `type`)
   - Method → `method` (not `function`)
   - Built-in → `*.defaultLibrary` (not `*.readonly`)
   - UFCS → `method.defaultLibrary`
   - `self` → `variable.readonly`
   - `attempt` → `variable.readonly`
   - Async function → `function.async`
   - No semantic tokens for strings, numbers, comments, operators

**Files changed:**

- `Stash.Analysis/Models/SemanticTokenConstants.cs`
- `Stash.Analysis/Visitors/SemanticTokenWalker.cs`
- `Stash.Lsp/Handlers/SemanticTokensHandler.cs`
- `.vscode/extensions/stash-lang/package.json`
- `Stash.Tests/Analysis/LspFeaturesTests.cs`
- `Stash.Tests/Analysis/DocCommentTests.cs`

### Phase 3: Tree-sitter Grammar

**Goal:** Build a tree-sitter parser for Stash that enables highlighting in Neovim, Helix, Zed, and GitHub code navigation.

**Tasks:**

1. **Create `tree-sitter-stash/` directory** (either as a subdirectory of the monorepo or a separate repository — TBD).

2. **Write `grammar.js`.** Define the full Stash syntax:
   - Programs (statement lists)
   - Declarations: `let`, `const`, `fn` (with parameters, return type, body), `struct` (with fields and default values), `enum` (with variants), `interface` (with method signatures)
   - Statements: `if`/`else`, `for`/`in`, `while`, `do`/`while`, `switch`/`case`, `try`/`catch`/`finally`, `defer`, `return`, `break`, `continue`, `throw`, `import`, `extend`, `elevate`
   - Expressions: binary ops, unary ops, call, member access (`.`), index (`[]`), ternary, null-coalescing (`??`), optional chaining (`?.`), pipe (`|>`), lambda (`fn(x) => expr`), spread (`...`), range (`..`), `is`, `as`, `await`, `retry`/`onRetry`/`until`/`timeout`, struct init (`Name { field: value }`)
   - Literals: integers, floats, hex, binary, octal, strings (with interpolation), triple-quoted strings, booleans, null, arrays, dicts, durations, byte sizes, semver, IP addresses
   - Command literals: `$(...)` with interpolation, pipes, redirections
   - Comments: `//`, `/* */`, `///`
   - String interpolation: `${expr}` and `{expr}` (in `$"..."`)

3. **Write `queries/highlights.scm`.** Map all AST nodes to standard Neovim captures (see Decision 6 table).

4. **Write `queries/locals.scm`.** Define scopes and definitions for basic scope tracking.

5. **Write `queries/folds.scm`.** Define foldable regions (blocks, function bodies, struct bodies).

6. **Write `queries/indents.scm`.** Define indentation rules.

7. **Write `queries/injections.scm`.** If command literals can contain shell code, define injection rules.

8. **Add `tree-sitter.json` metadata.** File types, scope, highlighting queries path.

9. **Generate the C parser.** Run `tree-sitter generate` and include the generated `src/parser.c`.

10. **Write tree-sitter tests.** Use `tree-sitter test` with corpus files to verify parse correctness.

11. **Write highlight tests.** Use tree-sitter's highlight test format.

**External scanner considerations:** Stash has several constructs that may need an external scanner (C code for context-sensitive lexing):

- String interpolation (matching `${` with balanced `}`)
- Triple-quoted strings (matching `"""`)
- Command literals (`$(...)` with nested parens)
- Block comments (nestable `/* ... /* ... */ ... */`)

**Files created:**

- `tree-sitter-stash/grammar.js`
- `tree-sitter-stash/queries/highlights.scm`
- `tree-sitter-stash/queries/locals.scm`
- `tree-sitter-stash/queries/folds.scm`
- `tree-sitter-stash/queries/indents.scm`
- `tree-sitter-stash/tree-sitter.json`
- `tree-sitter-stash/src/parser.c` (generated)
- `tree-sitter-stash/src/scanner.c` (external scanner if needed)
- `tree-sitter-stash/test/corpus/*.txt`

### Phase 4: Neovim Plugin / Distribution

**Goal:** Package the tree-sitter grammar and LSP config for easy Neovim installation.

**Tasks:**

1. **Create a Neovim plugin directory** or standalone repo with:
   - `ftdetect/stash.lua` — filetype detection for `.stash` files
   - `ftplugin/stash.lua` — LSP client configuration, comment settings, indentation settings
   - `syntax/stash.vim` — minimal Vim syntax fallback for users without tree-sitter (just keywords + string/number/comment basics)
   - `queries/stash/highlights.scm` — symlink or copy of tree-sitter queries (Neovim can pick these up automatically)

2. **Document Neovim setup** in README or docs.

3. **Register with nvim-treesitter** (if still accepting new parsers) or document manual parser installation via `vim.treesitter.language.add()`.

**Files created:**

- `editors/neovim/ftdetect/stash.lua`
- `editors/neovim/ftplugin/stash.lua`
- `editors/neovim/syntax/stash.vim`
- Docs update

### Phase 5: Template Highlighting

**Goal:** Proper highlighting for `.tpl` template files.

**Tasks:**

1. **Update `stash-tpl.tmLanguage.json`** — ensure embedded Stash regions use `meta.embedded.stash` scopes with correct `embeddedLanguages` configuration.

2. **Add tree-sitter injection** for Stash expressions within template delimiters.

3. **Optionally add semantic tokens** for embedded Stash regions in templates (deferred — only if the range mapping work is justified).

---

## 6. Phase Dependencies and Ordering

```
Phase 1 (TextMate)  ─────────────────────────────────┐
Phase 2 (Semantic)  ─────────────────────────────────┤──→ Phase 5 (Templates)
Phase 3 (Tree-sitter) ──→ Phase 4 (Neovim plugin)  ─┘
```

Phases 1 and 2 can be done in parallel. Phase 3 is independent of Phases 1-2 (different grammar format). Phase 4 depends on Phase 3. Phase 5 depends on Phases 1-2 being stable.

**Recommended execution order:** Phase 1 → Phase 2 → Phase 3 → Phase 4 → Phase 5.

Phase 1 has the highest immediate impact (fixes wrong colors for existing VS Code users). Phase 2 is closely related and should follow immediately. Phase 3 is the largest effort but unlocks the most editors.

---

## 7. Testing Strategy

### 7.1 TextMate Grammar Tests

**Manual verification checklist** (use `Developer: Inspect Editor Tokens and Scopes`):

| Construct          | Expected Scope                                                                 |
| ------------------ | ------------------------------------------------------------------------------ |
| `fn main()`        | `fn` → `storage.type.function.stash`, `main` → `entity.name.function.stash`    |
| `let x = 5`        | `let` → `storage.type.stash`, `x` → `variable.other.readwrite.stash`           |
| `const PI = 3.14`  | `const` → `storage.type.stash`, `PI` → `variable.other.constant.stash`         |
| `if x > 0`         | `if` → `keyword.control.conditional.stash`                                     |
| `for item in list` | `for` → `keyword.control.loop.stash`, `in` → `keyword.operator.word.stash`     |
| `x and y`          | `and` → `keyword.operator.word.stash`                                          |
| `self.name`        | `self` → `variable.language.stash`                                             |
| `async fn fetch()` | `async` → `storage.modifier.async.stash`, `fn` → `storage.type.function.stash` |
| `await result`     | `await` → `keyword.control.flow.stash`                                         |
| `/// @param x`     | `///` → `comment.line.documentation.stash`                                     |
| `struct Foo {`     | `struct` → `storage.type.stash`, `Foo` → `entity.name.struct.stash`            |

### 7.2 Semantic Token Tests

Extend `LspFeaturesTests.cs` with cases for:

1. `struct Point { x: int }` → `Point` classified as `struct.declaration`
2. `let p = Point { x: 1 }` → `Point` classified as `struct`
3. `enum Color { Red, Blue }` → `Color` classified as `enum.declaration`, `Red`/`Blue` as `enumMember.readonly`
4. `arr.map(list, fn(x) => x)` → `arr` as `namespace.defaultLibrary`, `map` as `function.defaultLibrary`
5. `list.map(fn(x) => x)` (UFCS) → `map` as `method.defaultLibrary`
6. `extend Foo { fn bar() {} }` → `bar` as `method.declaration`
7. `self.name` → `self` as `variable.readonly`
8. `async fn fetch() {}` → `fetch` as `function.async.declaration`
9. No semantic token exists for string literals, numeric literals, comments, or operators

### 7.3 Tree-sitter Tests

Use tree-sitter's built-in test framework (`tree-sitter test`):

```
==================
Function declaration
==================
fn greet(name: string) -> string {
  return "hello " + name
}
---
(program
  (function_declaration
    name: (identifier)
    parameters: (parameter_list
      (parameter name: (identifier) type: (type_identifier)))
    return_type: (type_identifier)
    body: (block
      (return_statement
        (binary_expression
          left: (string)
          operator: "+"
          right: (identifier))))))
```

### 7.4 Theme Verification

Verify with at minimum:

- VS Code: Dark+, Light+, One Dark Pro or Catppuccin
- Neovim: default, Catppuccin or tokyonight

Goal: Stash should look "native" in each theme — keywords one color, functions another, types another, strings another. No broken/uncolored tokens.

---

## 8. Risks and Mitigations

| Risk                                                   | Impact                             | Mitigation                                                                                                                                            |
| ------------------------------------------------------ | ---------------------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------- |
| Three grammars diverge over time                       | Wrong highlighting in some editors | Add a CI check that verifies all three cover the same keyword/operator set                                                                            |
| Tree-sitter grammar is large upfront effort            | Delays Neovim support              | Start with core syntax (declarations, control flow, expressions, literals). Advanced features (command literals, template injection) can be Phase 3.5 |
| Visual regression for existing VS Code users           | Surprise/complaints                | Document changes. The "before" colors were wrong by standard conventions.                                                                             |
| Semantic token removal makes editor feel "less active" | Perceived regression               | In reality, the grammar provides richer detail. Educate in changelog.                                                                                 |
| External scanner complexity for tree-sitter            | Parser bugs                        | Start without external scanner; add only when needed (string interpolation, nestable comments)                                                        |

---

## 9. Non-Goals

- This spec does **not** define a Stash-specific color theme. Themes decide colors.
- This spec does **not** change language semantics or the parser/runtime.
- This spec does **not** require semantic token deltas or performance optimization.
- This spec does **not** add new semantic capabilities (e.g., go-to-definition, rename). It only fixes and standardizes the highlighting layer.
- This spec does **not** address syntax highlighting for embedded DSLs beyond `stash-tpl` templates.

---

## 10. Open Questions

1. **Tree-sitter repository location.** Should `tree-sitter-stash` live as a subdirectory of the monorepo or as a separate GitHub repository? Separate repos are the tree-sitter convention, but monorepo simplifies CI.
   Answer: Stash and all of its toolkit is a monorepo, we'll make tree-sitter be a part of it.

2. **Neovim plugin distribution.** Standalone repo? Subdirectory under `editors/neovim/`? Or rely solely on tree-sitter parser + LSP with no dedicated plugin?
   Answer: We'll rely on tree-sitter and the LSP for now, do dedicated plugin.

3. **Monarch tokenizer maintenance.** The playground's Monarch tokenizer is a third grammar to maintain. Should we consider generating it from the TextMate grammar to reduce drift?
   Answer: If it's possible, then yes, generate it dynamically from the TextMate grammar.

4. **`from` as a keyword.** `from` is a contextual keyword used in imports (`import { foo } from "bar"`). Should the TextMate grammar highlight it as `keyword.control.import.stash`? Currently it's not highlighted. The lexer parses it contextually, not as a reserved keyword.
   Answer: Yes, highlight it when it's used in the import context, otherwise it's a normal identifier users can use in the code.

5. **Phase 2 semantic token removal — gradual or all-at-once?** Should we remove lexical semantic tokens all at once, or add a user setting like `stash.semanticHighlighting.lexicalTokens: true` for a transition period?
   Answer: Nuke it all, we start fresh and we do it right. No point in keeping unmainteinable code with us.

---

## 11. Decision Log

| Date       | Decision                                                                                           | Revision                                                                              |
| ---------- | -------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------- |
| 2026-04-18 | Initial draft incorporating three-grammar architecture, full grammar audit, tree-sitter plan       | —                                                                                     |
| 2026-04-18 | Supersedes previous `Syntax Highlighting — Taxonomy, Scope Policy, and Semantic Token Strategy.md` | Previous spec was VS Code-only, lacked tree-sitter plan, lacked grammar audit details |
