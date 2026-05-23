## Syntax Highlighting — Taxonomy, Scope Policy, and Semantic Token Strategy

> **Document type:** Design / Analysis
> **Status:** Draft
> **Created:** 2026-04-18
> **Audience:** Stash core maintainer
> **Purpose:** Define the correct division of responsibilities between TextMate syntax scopes and LSP semantic tokens for Stash, grounded in VS Code's model and in patterns used by mature language tooling.

---

## 1. Executive Summary

There is no single "correct" color palette for a programming language. The correct approach in VS Code is to emit **portable classifications**, not editor-specific colors:

1. **TextMate grammar owns lexical structure**: comments, strings, interpolations, escape sequences, numeric literal forms, operators, punctuation, doc-comment tags, and embedded-language boundaries.
2. **Semantic tokens own symbol identity**: whether an identifier is a struct, enum, interface, type, function, method, parameter, variable, property, enum member, or standard-library symbol.
3. **Standard token types and modifiers matter more than custom ones** because themes already understand them.
4. **Fine-grained lexical semantic tokens are optional, not foundational**. Tools like `gopls` and `rust-analyzer` only lean into them with explicit escape hatches because semantic tokens override richer grammar styling in VS Code.

Stash currently gets part of this right and part of it backwards:

- The TextMate grammar is already quite rich.
- The semantic layer is comparatively shallow for symbols and overly broad for lexical material.
- Built-ins are modeled as `readonly` instead of `defaultLibrary`.
- Structs and enums collapse to `type`.
- Methods are flattened into `function`.
- Strings, numbers, comments, and operators are emitted semantically in ways that can erase grammar detail.

The recommendation is to make Stash **more conventional, not more clever**:

- Narrow the default semantic layer to identity-sensitive symbols.
- Expand the symbol taxonomy to standard VS Code types and modifiers.
- Preserve the grammar's finer distinctions instead of overriding them.
- Treat any extra semantic coloring for literals/operators/punctuation as experimental and optional.

---

## 2. Current Stash State

### 2.1 Extension and legend shape

The extension currently enables semantic highlighting for `stash` in `.vscode/extensions/stash-lang/package.json`, but not for `stash-tpl`. The semantic legend is defined by `Stash.Lsp/Handlers/SemanticTokensHandler.cs` and mirrored in `Stash.Analysis/Models/SemanticTokenConstants.cs`.

Today Stash exposes 13 semantic token types:

- `namespace`
- `type`
- `function`
- `parameter`
- `variable`
- `property`
- `enumMember`
- `keyword`
- `number`
- `string`
- `comment`
- `operator`
- `interface`

And only 2 modifiers:

- `declaration`
- `readonly`

That is enough to make semantic highlighting work, but it is not an especially good taxonomy.

### 2.2 Symbol classification problems

`Stash.Analysis/Visitors/SemanticTokenWalker.cs` already resolves names from real symbol information, but the final categories are weaker than they should be:

- Built-in functions are emitted as `function.readonly` instead of `function.defaultLibrary`.
- Built-in constants are emitted as `variable.readonly` instead of `variable.readonly.defaultLibrary`.
- Built-in namespaces are emitted as plain `namespace`, not `namespace.defaultLibrary`.
- Structs and enums collapse into generic `type`.
- Member calls are flattened into `function` rather than distinguishing `method`.
- UFCS-style built-ins (`str.*`, `arr.*`) are treated as functions rather than methods when used through dot-call syntax.

This is the central semantic weakness in the current design: Stash has real semantic information, but it is not projecting that information into the standard semantic model precisely enough.

### 2.3 Lexical override problems

`Stash.Lsp/Handlers/SemanticTokensHandler.cs` emits semantic tokens not only for identifiers, but also for:

- strings
- numbers
- operators
- comments
- doc-comment tags
- special literal splits such as IP/CIDR and semver

That creates a mismatch with the grammar, because `.vscode/extensions/stash-lang/syntaxes/stash.tmLanguage.json` already distinguishes far more lexical detail than the semantic legend can preserve, including:

- many operator subfamilies
- multiple numeric literal families
- interpolated vs non-interpolated strings
- command literals vs standard strings
- block vs line comments

In VS Code, semantic tokens sit on top of grammar tokens. When a semantic token is too generic, it can flatten a richer grammar scope underneath it.

### 2.4 Contextual and template gaps

There are also two design gaps:

- `self` and `attempt` are explicitly skipped in `SemanticTokenWalker`. That is not a principled design; it is a hole.
- `stash-tpl` has grammar support but no semantic highlighting strategy.

---

## 3. External Research

### 3.1 VS Code's actual model

The official VS Code guidance is unambiguous:

- TextMate grammars are the main tokenization engine.
- Semantic highlighting is an addition on top of grammar highlighting.
- Language authors should build on common TextMate scopes instead of inventing unusual ones.
- Language authors should prefer standard semantic token types and modifiers so themes can style them consistently across languages.

This matters because the right question is not "what colors should Stash use?" The right question is "what categories should Stash emit so themes can color them correctly?"

That distinction is the difference between an editor-friendly language and a fragile one.

### 3.2 What default VS Code themes actually care about

The built-in `Dark+` and `Light+` themes are informative because they show what users are likely to see even when they have not installed a language-specific theme.

Those themes strongly differentiate these broad groups:

- comments
- strings
- numbers
- keywords
- types and namespaces
- functions
- variables and parameters
- constants and enum members

They do **not** depend on a language inventing twenty exotic token families to look good. In fact, most of the built-in theme behavior still flows through familiar TextMate groups and the semantic token scope map.

Important consequence:

- Stash does not need a bespoke color theory.
- Stash needs a stable token taxonomy that lands in the categories themes already understand.

### 3.3 TypeScript / JavaScript practice

The built-in TypeScript semantic token provider in VS Code uses a straightforward standard legend:

- `class`
- `enum`
- `interface`
- `namespace`
- `typeParameter`
- `type`
- `parameter`
- `variable`
- `enumMember`
- `property`
- `function`
- `method`

And modifiers such as:

- `declaration`
- `static`
- `async`
- `readonly`
- `defaultLibrary`
- `local`

That is the relevant lesson. Mature language tooling is not subtle about this: free functions, methods, properties, readonly values, and default-library symbols are separated because themes and users benefit from those distinctions.

### 3.4 Go / gopls practice

The Go extension's documentation is unusually clear about the grammar-vs-semantic tradeoff:

- TextMate provides the baseline syntax highlighting.
- `gopls` semantic tokens are recommended for more accurate symbol coloring.
- Strings and numbers can be deliberately delegated back to VS Code/TextMate when semantic tokens would be counterproductive.

This is a strong precedent for Stash. It says:

- symbol identity belongs in semantic tokens
- literal detail can reasonably remain lexical

That is especially relevant because Stash has more literal forms than Go, not fewer.

### 3.5 Rust Analyzer practice

`rust-analyzer` is the most sophisticated comparison point.

It supports:

- a rich semantic tag/modifier system
- non-standard token types for operator and punctuation specialization
- documentation injection
- many semantic modifiers beyond the standard set

But the critical design choice is not that it has many categories. The critical design choice is that it makes many of them **configurable**, specifically because semantic tokens can override grammar-driven highlighting in editors like VS Code.

That means the advanced model is not "semantic everything by default." The advanced model is:

- standard semantic tokens by default
- specialized semantic tokens only when they are clearly worth the tradeoff
- escape hatches when lexical richness would otherwise be lost

---

## 4. What Other Languages Are Actually Doing

Across VS Code, TypeScript, Go, and Rust, the stable pattern is this:

### 4.1 Broad color families are consistent

Most mainstream editor themes converge on these conceptual color groups:

- **Comments**: muted, often italicized
- **Strings**: warm literal color
- **Numbers**: distinct literal color, separate from strings
- **Keywords**: saturated accent color
- **Types / namespaces**: one family
- **Functions / methods**: one family, often separate from types
- **Variables / parameters**: another family
- **Constants / enum members / readonly properties**: a distinct "constant-like" family

This is not a language invention. It is the baseline editor ecosystem.

### 4.2 Themes care more about categories than spelling

The difference between `function` and `method` matters.

The difference between `variable` and `property` matters.

The difference between `readonly` and mutable matters.

The difference between user code and default library often matters.

But the difference between twelve operator subtypes usually does **not** matter enough to justify overriding a richer TextMate grammar unless the editor ecosystem clearly supports it.

### 4.3 Symbol identity is semantic; literal structure is lexical

This is the most reusable rule from other languages.

Examples:

- A reference to `Foo` being a struct vs enum vs interface is semantic.
- A member access being a property vs method is semantic.
- A token being a triple-quoted interpolated string with escapes and embedded expressions is lexical.
- A token being a duration literal vs hex literal vs semver literal is lexical unless there is a very strong reason otherwise.

---

## 5. Design Decisions For Stash

### Decision 1: TextMate owns lexical syntax; semantic tokens own symbol identity

**Chosen.**

#### TextMate should own, by default

- comments
- doc comments and doc-comment tags
- strings
- interpolations
- command literals
- numbers and all numeric/literal subforms
- operators
- punctuation
- template delimiters and raw template text
- embedded-language boundaries

#### Semantic tokens should own, by default

- namespaces
- structs
- enums
- interfaces
- generic `type` where no better standard type applies
- functions
- methods
- parameters
- variables
- properties / fields
- enum members
- standard-library origin
- readonly / const-ness
- async-ness where applicable
- declaration sites

#### Alternatives considered

- Keep semantically tokenizing lexical material by default.
- Remove semantic highlighting entirely and rely on TextMate only.

#### Rationale

The current grammar is already richer than the current semantic legend. Overriding that detail with generic semantic categories is a net loss.

#### Risks

- Some themes may appear slightly less "busy" after reducing lexical semantic tokens.
- Users who liked semantic coloring of numbers/operators may perceive a visual regression.

#### Reversal trigger

If a later Stash theme or editor integration demonstrates that specific lexical semantic tokens materially improve readability without erasing grammar detail, reintroduce them as opt-in features rather than as baseline behavior.

### Decision 2: Adopt the standard VS Code semantic taxonomy more precisely

**Chosen.**

Stash should converge on the standard semantic token types already recognized by VS Code and used by TypeScript.

#### Recommended default Stash legend

Core symbol token types:

- `namespace`
- `struct`
- `enum`
- `interface`
- `type`
- `function`
- `method`
- `parameter`
- `variable`
- `property`
- `enumMember`

Core modifiers:

- `declaration`
- `readonly`
- `defaultLibrary`
- `async`

Deferred / optional modifiers for later phases:

- `documentation`
- `modification`
- `deprecated`

#### Alternatives considered

- Keep Stash's current 13-type custom-ish legend.
- Invent Stash-only semantic token types for command heads, duration literals, byte sizes, semver, IPs, and similar categories.

#### Rationale

If a standard type already exists, using a custom or weaker type is just making themes work harder for no benefit.

#### Risks

- Token indices will change.
- Existing tests and any hand-written scope mappings will need updates.

#### Reversal trigger

Only if a required Stash construct truly has no standard equivalent and the fallback scope mapping is well-defined.

### Decision 3: Built-ins must use `defaultLibrary`, not `readonly`

**Chosen.**

Origin and mutability are different facts.

- `readonly` answers: can this thing be mutated?
- `defaultLibrary` answers: is this thing part of the language/runtime standard library?

Current Stash conflates those facts for built-ins. That is semantically wrong.

#### Required mapping rules

- built-in namespace: `namespace.defaultLibrary`
- built-in function referenced as free function: `function.defaultLibrary`
- built-in namespace function referenced through namespace access: `function.defaultLibrary`
- built-in UFCS-style member call: `method.defaultLibrary`
- built-in constant: `variable.readonly.defaultLibrary`
- built-in type name in annotations: `type.defaultLibrary`

#### Alternatives considered

- Keep using `readonly` as a proxy for "special runtime thing."

#### Rationale

TypeScript already uses `defaultLibrary`. Themes already understand it. This is solved.

#### Risks

- Some themes do not style `defaultLibrary` differently, so the visual change may be subtle.

#### Reversal trigger

None. The current model is conceptually incorrect.

### Decision 4: Distinguish `struct`, `enum`, `function`, `method`, and `property`

**Chosen.**

Stash has enough structure now that flattening these into `type`, `function`, and `property` is leaving value on the table.

#### Required mapping rules

- struct declarations and references -> `struct`
- enum declarations and references -> `enum`
- interface declarations and references -> `interface`
- user-defined free functions -> `function`
- member-call targets that resolve to methods / extend methods / UFCS methods -> `method`
- fields / stored members / dot-access non-call members -> `property`
- enum variants -> `enumMember.readonly`

#### Alternatives considered

- Keep mapping structs and enums to `type`.
- Keep mapping member-call targets to `function`.

#### Rationale

This is the same direction taken by TypeScript and Rust. Distinguishing these categories improves readability without requiring custom theme support.

#### Risks

- UFCS classification must be deterministic.
- Dot access without resolution may still require fallback behavior.

#### Reversal trigger

If Stash cannot reliably tell methods from properties at semantic-token time, keep the unresolved fallback generic but preserve the stronger classification where semantic information exists.

### Decision 5: Ordinary keywords stay lexical by default

**Chosen.**

Stash does not gain much by semantically tokenizing standard keywords like `if`, `while`, `return`, `throw`, `try`, and `catch`. Those are lexical facts.

If semantic keyword emission remains at all, it should be narrowly reserved for cases where lexical classification is genuinely insufficient.

#### Special case: `self` and `attempt`

These should not be silently skipped. They must be highlighted either:

- lexically, via canonical keyword / language-constant scopes, or
- semantically, if Stash adopts a principled contextual-keyword policy

But the current "ignore them" behavior is not acceptable as a deliberate design.

#### Alternatives considered

- Continue semantically emitting all keywords.
- Remove all keyword handling and accept current holes.

#### Rationale

Keyword coloring is one of the few things TextMate already does well everywhere.

#### Risks

- Minimal. This is mostly a cleanup of responsibilities.

#### Reversal trigger

If Stash later introduces contextual keywords that cannot be correctly scoped lexically.

### Decision 6: Doc-comment tags should move out of the semantic handler's hardcoded list

**Chosen.**

Current behavior hardcodes `@param`, `@return`, and `@returns` in `SemanticTokensHandler`.

That is brittle and not scalable.

#### Preferred direction

- doc-comment tag tokenization should be grammar / injection driven where possible
- symbol references inside documentation can later use semantic information if needed
- if semantic doc support exists, it should use standard modifiers like `documentation`, not a hand-maintained tag whitelist

#### Alternatives considered

- Keep growing the hardcoded tag list in C#.

#### Rationale

Documentation markup is lexical surface syntax first, semantic surface syntax second.

#### Risks

- Grammar-based doc-tag handling may need additional test coverage.

#### Reversal trigger

If later Stash documentation tooling needs semantic resolution inside doc comments, add that as a second layer rather than baking keyword lists into the primary semantic handler.

### Decision 7: `stash-tpl` needs an explicit highlighting strategy

**Chosen.**

The correct long-term answer is not to pretend templates are plain text with braces. Stash needs a stated policy for template files.

#### Phase 1

- keep raw template text and delimiters purely lexical
- ensure embedded Stash expressions use canonical embedded scopes (`meta.embedded.*`)

#### Phase 2

- add semantic highlighting for embedded Stash regions in `stash-tpl`
- do not attempt to semantically classify non-Stash template text

#### Alternatives considered

- Leave `stash-tpl` grammar-only permanently.

#### Rationale

Go's template support in `gopls` is a relevant precedent: embedded language regions benefit from semantic tokens once the range mapping is worth the complexity.

#### Risks

- Embedded range mapping in templates is non-trivial.

#### Reversal trigger

If implementation complexity is too high for now, Phase 1 alone is still acceptable, but that should be a conscious deferral, not an accidental omission.

---

## 6. Recommended Stash Taxonomy

### 6.1 Semantic token mapping table

| Stash construct                                                | Recommended semantic token                                 |
| -------------------------------------------------------------- | ---------------------------------------------------------- |
| Built-in namespace `arr`                                       | `namespace.defaultLibrary`                                 |
| Imported module alias                                          | `namespace` or `namespace.declaration` at declaration site |
| Struct declaration/reference                                   | `struct` / `struct.declaration`                            |
| Enum declaration/reference                                     | `enum` / `enum.declaration`                                |
| Interface declaration/reference                                | `interface` / `interface.declaration`                      |
| Primitive / built-in type in type hint                         | `type.defaultLibrary`                                      |
| User-defined type alias or future non-struct named type        | `type`                                                     |
| Free function                                                  | `function` / `function.declaration`                        |
| Async free function                                            | `function.async` / `function.async.declaration`            |
| Extend method / member-call target / UFCS built-in member call | `method` or `method.defaultLibrary`                        |
| Parameter                                                      | `parameter` / `parameter.declaration`                      |
| Mutable variable                                               | `variable` / `variable.declaration`                        |
| Constant                                                       | `variable.readonly` / `variable.readonly.declaration`      |
| Built-in constant                                              | `variable.readonly.defaultLibrary`                         |
| Struct field                                                   | `property` / `property.declaration`                        |
| Enum variant                                                   | `enumMember.readonly` / `enumMember.readonly.declaration`  |

### 6.2 TextMate scope policy

The grammar should use **canonical parent scopes first**, with `.stash` only as the language suffix.

Recommended families:

- comments -> `comment.*.stash`
- control keywords -> `keyword.control.stash`
- declaration keywords -> `storage.type.stash`
- modifiers -> `storage.modifier.stash`
- booleans / null -> `constant.language.*.stash`
- types -> `entity.name.type.*.stash`
- functions -> `entity.name.function.*.stash`
- parameters -> `variable.parameter.stash`
- variables -> `variable.other.stash`
- properties / fields -> `variable.other.property.stash`
- enum members -> `variable.other.enummember.stash`
- operators -> `keyword.operator.*.stash`
- numeric families -> `constant.numeric.*.stash`
- strings -> `string.*.stash`

Where Stash needs language-specific specialization, the specialization should still inherit from a well-known family.

Example:

- good: `constant.numeric.duration.stash`
- good: `keyword.operator.null-coalescing.stash`
- risky: a completely bespoke top-level family with no common parent

### 6.3 Command literal and embedded-language policy

Command literals and template embeddings should remain grammar-led.

Required properties:

- the command / template container should be a `meta.*` scope
- embedded Stash source should live under `meta.embedded.*`
- if a non-Stash embedded language is ever introduced, it must use `embeddedLanguages` correctly so editor behaviors follow that language inside the embedded region

---

## 7. What This Means Visually

Stash should align with the color roles users already expect from mainstream editor themes:

- **Comments**: subdued
- **Strings**: literal color
- **Numbers**: numeric literal color
- **Keywords**: control/declaration accent color
- **Types / namespaces**: one family
- **Functions / methods**: one family, potentially with method-specific differentiation
- **Variables / parameters**: another family
- **Constants / enum members / readonly properties**: constant-like family

This is the important point: Stash should not try to dictate these colors. It should emit the categories that allow themes to do this naturally.

---

## 8. Implementation Impact

No parser or runtime changes are required. This is an editor-tooling change.

### 8.1 Files likely to change

- `.vscode/extensions/stash-lang/package.json`
- `.vscode/extensions/stash-lang/syntaxes/stash.tmLanguage.json`
- `.vscode/extensions/stash-lang/syntaxes/stash-tpl.tmLanguage.json`
- `Stash.Analysis/Models/SemanticTokenConstants.cs`
- `Stash.Analysis/Visitors/SemanticTokenWalker.cs`
- `Stash.Lsp/Handlers/SemanticTokensHandler.cs`
- potentially docs for the extension and LSP features

### 8.2 Required implementation tasks

#### Phase 1: taxonomy correction

1. Replace the current semantic legend with a standard-symbol-focused legend.
2. Add `struct`, `enum`, `method`, and `defaultLibrary`.
3. Add `async` modifier where semantic information exists.
4. Reclassify built-ins away from `readonly`-as-origin and into `defaultLibrary`.
5. Distinguish `function` vs `method` where possible.
6. Distinguish `struct` and `enum` from generic `type`.

#### Phase 2: lexical responsibility cleanup

1. Stop emitting semantic tokens for strings, numbers, comments, and operators by default.
2. Move doc-comment tag handling toward grammar/injection ownership.
3. Ensure `self` and `attempt` are highlighted via an explicit policy.

#### Phase 3: template strategy

1. Formalize the `stash-tpl` grammar's embedded Stash ranges.
2. Add semantic support for embedded Stash regions if the mapping work is justified.

---

## 9. Testing Strategy

### 9.1 Automated tests

Existing semantic-token tests already live in `Stash.Tests/Analysis/LspFeaturesTests.cs`, and doc-comment tokenization tests live in `Stash.Tests/Analysis/DocCommentTests.cs`.

Required additions or updates:

1. Struct declarations and references classify as `struct`, not `type`.
2. Enum declarations and references classify as `enum`, not `type`.
3. UFCS member calls classify as `method.defaultLibrary` where appropriate.
4. Built-in namespaces/functions/constants/types carry `defaultLibrary`.
5. Async functions carry `async`.
6. Constant declarations and enum members carry `readonly`.
7. Lexical tokens that are no longer semantically emitted do not regress identifier classification.
8. `self` and `attempt` are highlighted by whichever policy is chosen.

### 9.2 Manual editor verification

Use VS Code's `Developer: Inspect Editor Tokens and Scopes` on a representative sample covering:

- declarations vs references
- built-ins vs user symbols
- property vs method access
- command literals
- interpolated strings
- doc comments
- template files

### 9.3 Theme verification

At minimum, verify with:

- `Dark+`
- `Light+`
- one popular third-party theme with semantic highlighting enabled

The goal is not pixel-perfect identical output. The goal is that Stash lands in the expected category families cleanly.

---

## 10. Risks and Non-Goals

### 10.1 Risks

- Visual changes may surprise existing users even if they are objectively more correct.
- Some themes will not distinguish every new semantic modifier.
- Reducing semantic emission can make the editor appear "less active" even when it is actually preserving better grammar detail.

### 10.2 Non-goals

- This spec does **not** define a Stash-specific color palette.
- This spec does **not** require a bundled theme.
- This spec does **not** change language semantics.
- This spec does **not** require semantic token deltas or performance work beyond what is necessary to keep the current LSP behavior correct.

---

## 11. Final Recommendation

If the question is "what is the correct way to handle syntax highlighting for a programming language in VS Code?", the answer is:

1. **Use TextMate for lexical syntax.**
2. **Use semantic tokens for symbol identity.**
3. **Prefer standard token types and modifiers over clever custom ones.**
4. **Do not semantically override richer grammar detail unless you are certain the tradeoff is worth it.**
5. **Model standard-library origin explicitly with `defaultLibrary`.**

For Stash specifically, the highest-value path is:

- keep the grammar rich
- make the semantic layer narrower but more precise
- align the taxonomy with TypeScript-style standard symbol categories
- treat advanced lexical semantic tokens as optional future work, not as the foundation

That is the most conventional, most theme-friendly, and least flimsy version of Stash highlighting.
