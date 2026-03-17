# Stash — Templating Engine

> **Status:** v1.0
> **Created:** June 2025
> **Purpose:** Source of truth for the `tpl` namespace — Stash's built-in Jinja2-style templating engine.
>
> **Companion documents:**
>
> - [Language Specification](Stash%20—%20Language%20Specification.md) — language syntax, type system, interpreter architecture
> - [Standard Library Reference](Stash%20—%20Standard%20Library%20Reference.md) — built-in namespace functions
> - [DAP — Debug Adapter Protocol](DAP%20—%20Debug%20Adapter%20Protocol.md) — debug adapter server
> - [LSP — Language Server Protocol](LSP%20—%20Language%20Server%20Protocol.md) — language server
> - [TAP — Testing Infrastructure](TAP%20—%20Testing%20Infrastructure.md) — built-in test runner

---

## Table of Contents

1. [Overview](#1-overview)
2. [API — `tpl` Namespace](#2-api--tpl-namespace)
3. [Template Syntax](#3-template-syntax)
4. [Variable Output](#4-variable-output)
5. [Filters](#5-filters)
6. [Conditionals](#6-conditionals)
7. [Loops](#7-loops)
8. [Includes](#8-includes)
9. [Whitespace Control](#9-whitespace-control)
10. [Raw Blocks](#10-raw-blocks)
11. [Comments](#11-comments)
12. [Architecture](#12-architecture)
13. [Error Handling](#13-error-handling)

---

## 1. Overview

Stash provides a built-in Jinja2-style templating engine via the `tpl` namespace. Templates use `{{ }}` for output, `{% %}` for logic, and `{# #}` for comments.

Expressions inside templates are evaluated by Stash's own interpreter, giving templates access to the full expression language — arithmetic, ternary, null-coalescing, function calls, namespace access, and more. The engine is designed for config file generation, report rendering, and text transformation, not web/HTML templating.

**File extension:** Template files use the `.tpl` extension by convention (e.g., `report.tpl`, `nginx.conf.tpl`). The Stash VS Code extension provides syntax highlighting for `.tpl` files with template-aware coloring of delimiters, tags, filters, and embedded Stash expressions.

---

## 2. API — `tpl` Namespace

### `tpl.render(template, data)`

Render a template string with a data dictionary. `template` can be a string or a pre-compiled template object (from `tpl.compile`). `data` must be a Stash dictionary — its keys become template variables.

```stash
let data = dict.new();
data["name"] = "Alice";
let result = tpl.render("Hello, {{ name }}!", data);
// result: "Hello, Alice!"
```

### `tpl.renderFile(path, data)`

Render a template file (conventionally with a `.tpl` extension). Path supports `~` expansion. The file's directory becomes the base path for `{% include %}` resolution.

```stash
let output = tpl.renderFile("templates/report.tpl", data);
```

### `tpl.compile(template)`

Pre-compile a template string for repeated use. Returns a compiled template object that can be passed to `tpl.render()` instead of a string. Useful when the same template is rendered many times with different data, avoiding repeated lexing and parsing.

```stash
let compiled = tpl.compile("Hello, {{ name }}!");
let r1 = tpl.render(compiled, data1);
let r2 = tpl.render(compiled, data2);
```

---

## 3. Template Syntax

Templates mix literal text with three types of delimiters:

| Delimiter       | Purpose                        | Example           |
| --------------- | ------------------------------ | ----------------- |
| `{{ expr }}`    | Output expression              | `{{ user.name }}` |
| `{% tag %}`     | Logic/control flow             | `{% if active %}` |
| `{# comment #}` | Comment (stripped from output) | `{# TODO #}`      |

Everything outside a delimiter is emitted as literal text. Delimiters can appear anywhere in a template — inline, on their own line, or mixed with text.

---

## 4. Variable Output

The `{{ }}` delimiter evaluates a Stash expression and emits its string representation:

```
{{ name }}                   — simple variable
{{ user.name }}              — dot access (struct fields, dict keys)
{{ items[0] }}               — index access
{{ count + 1 }}              — arithmetic expressions
{{ active ? "yes" : "no" }}  — ternary operator
{{ name ?? "default" }}      — null-coalescing
{{ str.upper(name) }}        — function calls
```

Template expressions are full Stash expressions — any valid Stash expression can appear inside `{{ }}`. The data dictionary's keys are bound as variables in the expression scope.

---

## 5. Filters

Filters transform values using pipe syntax. The value on the left becomes the input to the filter on the right:

```
{{ name | upper }}
{{ items | length }}
{{ name | trim | upper }}
```

### Built-in Filters

| Filter              | Arguments | Description                       | Example                           |
| ------------------- | --------- | --------------------------------- | --------------------------------- |
| `upper`             | —         | Uppercase string                  | `{{ name \| upper }}`             |
| `lower`             | —         | Lowercase string                  | `{{ name \| lower }}`             |
| `trim`              | —         | Strip leading/trailing whitespace | `{{ name \| trim }}`              |
| `capitalize`        | —         | Capitalize first letter           | `{{ name \| capitalize }}`        |
| `title`             | —         | Title Case each word              | `{{ name \| title }}`             |
| `length`            | —         | Length of string or array         | `{{ items \| length }}`           |
| `reverse`           | —         | Reverse string or array           | `{{ name \| reverse }}`           |
| `first`             | —         | First element of array            | `{{ items \| first }}`            |
| `last`              | —         | Last element of array             | `{{ items \| last }}`             |
| `sort`              | —         | Sort array                        | `{{ items \| sort }}`             |
| `join(sep)`         | separator | Join array elements to string     | `{{ items \| join(", ") }}`       |
| `split(sep)`        | separator | Split string into array           | `{{ csv \| split(",") }}`         |
| `replace(old, new)` | old, new  | Replace substring                 | `{{ name \| replace("_", " ") }}` |
| `default(val)`      | fallback  | Fallback value for null           | `{{ name \| default("N/A") }}`    |
| `round`             | —         | Round to nearest integer          | `{{ price \| round }}`            |
| `abs`               | —         | Absolute value                    | `{{ diff \| abs }}`               |
| `keys`              | —         | Dictionary keys as array          | `{{ config \| keys }}`            |
| `values`            | —         | Dictionary values as array        | `{{ config \| values }}`          |
| `json`              | —         | JSON-encode value                 | `{{ data \| json }}`              |

### Filter Chaining

Filters can be chained — each filter receives the output of the previous one:

```
{{ name | trim | upper | default("N/A") }}
```

### Filters vs. Logical OR

The pipe `|` for filters is distinct from the logical OR operator `||`. The engine disambiguates based on whether a filter name follows the single `|`:

- `{{ x || y }}` — logical OR, evaluates as a boolean expression
- `{{ x | upper }}` — filter application, transforms `x`

---

## 6. Conditionals

```
{% if user.isAdmin %}
  Welcome, admin!
{% elif user.isActive %}
  Welcome back, {{ user.name }}.
{% else %}
  Please log in.
{% endif %}
```

`{% elif %}` and `{% else %}` are optional. Multiple `{% elif %}` branches are allowed.

### Condition Expressions

Conditions support all Stash logical operators:

```
{% if count > 0 && status == "active" %}
{% if items | length > 0 %}
{% if name != null && name != "" %}
```

Supported operators: `&&`, `||`, `!`, `==`, `!=`, `>`, `<`, `>=`, `<=`, `in`.

**Truthiness rules:** `false`, `null`, `0`, `0.0`, and `""` are falsy. Everything else — including empty arrays and empty dicts — is truthy.

---

## 7. Loops

```
{% for server in servers %}
  {{ server.host }}: {{ server.status }}
{% endfor %}
```

### Loop Metadata

Inside a `{% for %}` block, a `loop` variable is automatically available:

| Variable      | Type | Description                   |
| ------------- | ---- | ----------------------------- |
| `loop.index`  | int  | Current iteration (1-based)   |
| `loop.index0` | int  | Current iteration (0-based)   |
| `loop.first`  | bool | `true` on the first iteration |
| `loop.last`   | bool | `true` on the last iteration  |
| `loop.length` | int  | Total number of items         |

```
{% for item in items %}
  {{ loop.index }}. {{ item.name }}{% if !loop.last %},{% endif %}
{% endfor %}
```

### Iterable Types

| Type           | Behavior                    |
| -------------- | --------------------------- |
| Array          | Iterates elements           |
| Range (`1..5`) | Iterates integers inclusive |
| String         | Iterates characters         |
| Dictionary     | Iterates keys               |

### Nested Loops

Each loop has its own independent `loop` variable. Inner loops do not shadow the outer `loop`:

```
{% for group in groups %}
  Group {{ loop.index }}: {{ group.name }}
  {% for item in group.items %}
    {{ loop.index }}. {{ item }}
  {% endfor %}
{% endfor %}
```

---

## 8. Includes

```
{% include "header.tpl" %}
{% include "partials/nav.tpl" %}
```

Included templates receive the same data context as the parent template. All variables bound in the parent are accessible in the included file.

**Path resolution:** When using `tpl.renderFile`, include paths are resolved relative to the directory of the current template file. When using `tpl.render` (string-based), include paths are resolved relative to the current working directory.

**Security:** Path traversal outside the template root directory is blocked. Paths such as `../../../etc/passwd` are rejected at render time with a `RuntimeError`.

---

## 9. Whitespace Control

By default, a tag on its own line produces a blank line in the output. Add `-` to delimiters to trim surrounding whitespace (including newlines):

| Marker | Effect                                       |
| ------ | -------------------------------------------- |
| `{%-`  | Trim whitespace **before** the tag           |
| `-%}`  | Trim whitespace **after** the tag            |
| `{{-`  | Trim whitespace before the output expression |
| `-}}`  | Trim whitespace after the output expression  |
| `{#-`  | Trim whitespace before the comment           |
| `-#}`  | Trim whitespace after the comment            |

```
{% for item in items -%}
  {{ item }}
{%- endfor %}
```

Trim markers remove all adjacent whitespace — spaces, tabs, and newlines — up to and including the trim boundary. Both sides can be trimmed simultaneously: `{%- tag -%}`.

---

## 10. Raw Blocks

```
{% raw %}
  This {{ is not }} parsed.
{% endraw %}
```

Content inside `{% raw %}...{% endraw %}` is emitted literally — no delimiter processing occurs. Useful for:

- Outputting template syntax as documentation or example text
- Generating templates that will be processed by another engine (e.g., a Jinja2 template that Stash generates)
- Escaping sequences that would otherwise be interpreted

Raw blocks correctly preserve all delimiter variants including whitespace-trimming markers (`{{-`, `-%}`, `{#-`, etc.).

---

## 11. Comments

```
{# This is stripped from output #}

{#
   Multi-line comments
   are also supported
#}
```

Comments are completely removed from the rendered output, including any surrounding whitespace if whitespace-trim markers are used (`{#-` / `-#}`). Comments cannot be nested.

---

## 12. Architecture

```
Template String
    → Template Lexer   (scans for {{ }}, {% %}, {# #}, literal text)
    → Template Parser  (builds template AST)
    → Template Renderer (walks AST, evaluates expressions via Stash Interpreter)
    → Output String
```

### Key Design Decisions

**1. Reuses the Stash expression evaluator.**
Expressions inside `{{ }}` and `{% if %}` / `{% for %}` are parsed and evaluated by the Stash interpreter. Templates get all Stash operators, function calls, and namespace access for free — without duplicating expression logic in the template engine.

**2. Data context is a Stash dictionary.**
Variable lookups resolve against the data dictionary first, then fall back to the interpreter's global scope. This makes built-in functions like `len()` and namespaces like `str.*` accessible inside templates without explicit import.

**3. Template AST is separate from the language AST.**
The template engine has its own lexer, parser, and AST node types, keeping the core language pipeline clean. Template nodes (`TextNode`, `OutputNode`, `IfNode`, `ForNode`, `IncludeNode`, `RawNode`) are distinct from `Expr` and `Stmt` nodes.

**4. `tpl.compile()` enables pre-compilation.**
Templates can be parsed once and rendered multiple times with different data, avoiding repeated lexing and parsing overhead. The compiled template object is opaque to Stash scripts.

### Implementation Files

```
Stash.Interpreter/Interpreting/
├── BuiltIns/TplBuiltIns.cs           — tpl namespace registration
└── Templating/
    ├── TemplateAst.cs                — AST node types
    ├── TemplateException.cs          — template-specific errors
    ├── TemplateLexer.cs              — template tokenizer
    ├── TemplateParser.cs             — template parser
    ├── TemplateFilters.cs            — filter registry
    └── TemplateRenderer.cs           — AST walker / renderer
```

---

## 13. Error Handling

Template errors include source location (line and column within the template string or file):

| Error                    | Example                               | When        |
| ------------------------ | ------------------------------------- | ----------- |
| Unterminated expression  | `{{ name` without `}}`                | Parse time  |
| Unterminated block       | `{% if x %}` without `{% endif %}`    | Parse time  |
| Unknown filter           | `{{ x \| foo }}`                      | Render time |
| Runtime expression error | `{{ 1 / 0 }}`                         | Render time |
| File not found           | `tpl.renderFile("missing.tpl", data)` | Render time |
| Type error               | `tpl.render(123, data)`               | Render time |
| Path traversal           | `{% include "../../../etc/passwd" %}` | Render time |

All errors are raised as `RuntimeError` at the Stash script level, with a message that includes the template source location. The underlying `TemplateException` is wrapped and its location information is preserved.

---

## Examples

### Config File Generation

```stash
let servers = [
    { host: "web1", port: 80 },
    { host: "web2", port: 443 }
];

let data = dict.new();
data["servers"] = servers;
data["generated"] = time.now();

let nginx = tpl.renderFile("templates/nginx.conf.tpl", data);
fs.writeFile("/etc/nginx/sites-enabled/app.conf", nginx);
```

**templates/nginx.conf.tpl:**

```
# Generated {{ generated }}
{% for srv in servers %}
server {
    listen {{ srv.port }};
    server_name {{ srv.host }};
}
{% endfor %}
```

### Report Generation

```stash
let data = dict.new();
data["status"] = "success";
data["duration"] = 42;
data["failures"] = [];

let report = tpl.render("""
=== Build Report ===
Status: {{ status | upper }}
Duration: {{ duration }}s

{% if failures | length > 0 -%}
Failures ({{ failures | length }}):
{% for f in failures %}
  {{ loop.index }}. {{ f.test }}: {{ f.message }}
{% endfor %}
{%- else -%}
All tests passed!
{%- endif %}
""", data);
```
