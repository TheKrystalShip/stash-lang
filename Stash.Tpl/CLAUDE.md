# TPL Templating Engine Guidelines

Stash includes a built-in templating engine with its own lexer/parser pipeline, separate from the main Stash lexer/parser. Templates use `{{ }}` for output, `{% %}` for control flow, and `{# #}` for comments. See `docs/TPL — Templating Engine.md` for the full spec.

## Architecture

```
Stash.Tpl/
├── TemplateAst.cs        → 8 record types (TextNode, OutputNode, IfNode, ForNode, etc.)
├── TemplateLexer.cs      → Two-pointer scanner for {{ }}, {% %}, {# #} delimiters
├── TemplateParser.cs     → Recursive-descent parser, nesting, filter parsing
├── TemplateRenderer.cs   → Tree-walk renderer over the template AST; expressions compile to Stash bytecode via VMTemplateEvaluator
├── TemplateFilters.cs    → 19 built-in filters (static registry)
└── TemplateException.cs  → Template-specific error with line/column
Stash.Stdlib/BuiltIns/
└── TplBuiltIns.cs        → Registers tpl.render, tpl.renderFile, tpl.compile
```

## Pipeline

```
Template String → TemplateLexer (tokenize) → TemplateParser (AST) → TemplateRenderer (walk + evaluate) → String
```

Template expressions are compiled to Stash bytecode via `VMTemplateEvaluator` (Stash.Bytecode/Runtime/VMTemplateEvaluator.cs). This means templates can call built-in functions, use operators, access namespace members, etc.

## tpl Namespace (TplBuiltIns.cs)

| Function         | Signature          | Behavior                                                                                       |
| ---------------- | ------------------ | ---------------------------------------------------------------------------------------------- |
| `tpl.render`     | `(template, data)` | Accepts string or pre-compiled `List<TemplateNode>`. Data must be a `StashDictionary`.         |
| `tpl.renderFile` | `(path, data)`     | Reads `.tpl` file (supports `~` expansion), sets `basePath` for `{% include %}` resolution.    |
| `tpl.compile`    | `(template)`       | Returns `List<TemplateNode>` (opaque to scripts). Reusable across multiple `tpl.render` calls. |

## AST Nodes (TemplateAst.cs)

```csharp
public abstract record TemplateNode;
public record TextNode(string Text) : TemplateNode;
public record OutputNode(string Expression, TemplateFilter[] Filters, bool TrimBefore, bool TrimAfter) : TemplateNode;
public record IfNode(TemplateBranch[] Branches, TemplateNode[]? ElseBody, bool TrimBefore, bool TrimAfter) : TemplateNode;
public record ForNode(string Variable, string Iterable, TemplateNode[] Body, bool TrimBefore, bool TrimAfter) : TemplateNode;
public record IncludeNode(string Path, bool TrimBefore, bool TrimAfter) : TemplateNode;
public record RawNode(string Text) : TemplateNode;
public record TemplateBranch(string Condition, TemplateNode[] Body);
public record TemplateFilter(string Name, string[] Arguments);
```

## Template Syntax

| Delimiter    | Purpose            | Example                                 |
| ------------ | ------------------ | --------------------------------------- |
| `{{ expr }}` | Output expression  | `{{ user.name \| upper }}`              |
| `{% tag %}`  | Control flow       | `{% if active %}...{% endif %}`         |
| `{# ... #}`  | Comment (stripped) | `{# TODO: fix this #}`                  |
| `{%- / -%}`  | Trim whitespace    | `{%- for x in items -%}`                |
| `{% raw %}`  | Literal output     | `{% raw %}{{ not parsed }}{% endraw %}` |

### Control Flow

- **Conditionals:** `{% if %}` / `{% elif %}` / `{% else %}` / `{% endif %}`
- **Loops:** `{% for x in items %}` / `{% endfor %}` — with loop metadata:
  - `loop.index` (1-based), `loop.index0` (0-based), `loop.first`, `loop.last`, `loop.length`
- **Includes:** `{% include "path.tpl" %}` — resolved relative to current template's directory

### Filters

19 built-in filters, applied via pipe syntax and chainable:

| Filter                                          | Args      | Notes                              |
| ----------------------------------------------- | --------- | ---------------------------------- |
| `upper`, `lower`, `trim`, `capitalize`, `title` | —         | String transforms                  |
| `length`                                        | —         | Works on string, array, dict       |
| `reverse`                                       | —         | String or array                    |
| `first`, `last`, `sort`                         | —         | Array operations                   |
| `join(sep)`                                     | separator | Array → string                     |
| `split(sep)`                                    | separator | String → array                     |
| `replace(old, new)`                             | two args  | String replacement                 |
| `default(val)`                                  | fallback  | Returns val if input is null/empty |
| `round`, `abs`                                  | —         | Numeric operations                 |
| `keys`, `values`                                | —         | Dict → array                       |
| `json`                                          | —         | JSON serialization                 |

Filters validate input types and throw `TemplateException` on mismatch.

## Renderer Internals

- `CreateEnvironment(data)` converts `StashDictionary` keys to interpreter environment variables
- Expressions/conditions evaluated via `Interpreter.EvaluateString(expression, env)`
- Loop metadata (`loop.index`, etc.) injected as environment variables per iteration
- Include resolution uses `basePath` from constructor; validates no directory traversal above root
- Truthiness follows Stash semantics: falsy = `null`, `false`, `0`, `0.0`, `""`

## Error Handling

`TemplateException` carries line/column info from the template source. At the script layer, these surface as `RuntimeError`.

## VS Code Support

- `.tpl` files registered as `stash-tpl` language in the extension
- TextMate grammar at `syntaxes/stash-tpl.tmLanguage.json` — highlights delimiters, keywords (`if`/`for`/`include`/`raw`), filters (after `|`), expressions, strings, operators
- Language configuration at `tpl-language-configuration.json` — bracket pairs, comment toggling
- Snippets at `snippets/stash-tpl.json`

## Tests

`Stash.Tests/Interpreting/TemplateTests.cs` (~70 tests) covering:

- Variable interpolation, expressions (arithmetic, ternary, null-coalescing)
- Dot/index access on structs and arrays
- All 19 filters individually + chaining
- Conditionals (if/elif/else, nested, with logical operators)
- Loops (simple, nested, all loop metadata fields, empty arrays)
- Comments and raw blocks (including edge cases with trim markers)
- Pre-compilation and reuse
- Built-in function access from templates
- Error cases (unknown filter, unterminated blocks, wrong data type)
- Lexer and parser unit tests
