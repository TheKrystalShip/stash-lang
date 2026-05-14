# TPL - Templating Engine

> **Status:** Stable v1 templating reference
> **Audience:** template authors, package authors, and tool implementers
> **Purpose:** reference for Stash's built-in template language and the `tpl` namespace.

TPL is Stash's Jinja-style text templating engine. It renders template strings or
template files using Stash dictionaries as data, Stash expressions as embedded
logic, and a small set of template-specific control-flow tags.

**Companion documents:**

- [Language Specification](Stash%20%E2%80%94%20Language%20Specification.md) - Stash expression semantics
- [Standard Library Reference](Stash%20%E2%80%94%20Standard%20Library%20Reference.md) - generated `tpl` namespace reference
- [TAP - Testing Infrastructure](TAP%20%E2%80%94%20Testing%20Infrastructure.md) - testing rendered output

---

## Contents

1. [Overview](#overview)
2. [API](#api)
3. [Template Syntax](#template-syntax)
4. [Expressions and Data](#expressions-and-data)
5. [Filters](#filters)
6. [Conditionals](#conditionals)
7. [Loops](#loops)
8. [Includes](#includes)
9. [Whitespace Control](#whitespace-control)
10. [Raw Blocks and Comments](#raw-blocks-and-comments)
11. [Errors](#errors)
12. [Examples](#examples)

---

## Overview

Templates are ordinary text with embedded delimiters:

| Delimiter       | Meaning                                          |
| --------------- | ------------------------------------------------ |
| `{{ expr }}`    | evaluate a Stash expression and write the result |
| `{% tag %}`     | execute template control flow                    |
| `{# comment #}` | comment, removed from output                     |

Example:

```tpl
Hello, {{ user.name | default("guest") }}.

{% if servers | length > 0 %}
Servers:
{% for server in servers %}
- {{ loop.index }}. {{ server.host }}:{{ server.port }}
{% endfor %}
{% else %}
No servers configured.
{% endif %}
```

TPL is intended for configuration generation, reports, scripts, and text
transforms. It does not provide HTML-specific escaping or web-template security
features by default.

Template files conventionally use `.tpl`.

## API

The `tpl` namespace exposes three functions.

### `tpl.render(template, data) -> string`

Renders a template string or compiled template with a data dictionary.

```stash
let output = tpl.render("Hello, {{ name }}!", {
    name: "Alice",
});
```

`template` must be a string or a compiled template returned by `tpl.compile`.
`data` must be a dictionary. Dictionary keys become template variables.

### `tpl.renderFile(path, data) -> string`

Reads a template file and renders it.

```stash
let rendered = tpl.renderFile("templates/nginx.conf.tpl", data);
```

A leading `~` in `path` expands to the current user's home directory. When a file is
rendered this way, include paths resolve relative to the rendered file's directory.

### `tpl.compile(template) -> function`

Compiles a template string for repeated rendering.

```stash
let compiled = tpl.compile("Hello, {{ name }}!");
let a = tpl.render(compiled, { name: "Alice" });
let b = tpl.render(compiled, { name: "Bob" });
```

The compiled value is opaque to Stash code and should be passed back to
`tpl.render`.

## Template Syntax

Everything outside template delimiters is emitted as literal text.

```tpl
literal text
{{ output }}
{% if condition %}conditional text{% endif %}
{# comment #}
```

Delimiter contents are trimmed before parsing, so `{{name}}` and `{{ name }}` are
equivalent.

Supported tags:

| Tag                        | Purpose                       |
| -------------------------- | ----------------------------- |
| `{% if expr %}`            | start conditional block       |
| `{% elif expr %}`          | additional conditional branch |
| `{% else %}`               | fallback branch               |
| `{% endif %}`              | end conditional block         |
| `{% for name in expr %}`   | start loop                    |
| `{% endfor %}`             | end loop                      |
| `{% include "path.tpl" %}` | include another template      |
| `{% raw %}`                | start raw literal block       |
| `{% endraw %}`             | end raw literal block         |

Unknown tags are template errors.

## Expressions and Data

Output expressions and conditional expressions are Stash expressions.

```tpl
{{ name }}
{{ user.name }}
{{ items[0] }}
{{ count + 1 }}
{{ active ? "yes" : "no" }}
{{ name ?? "default" }}
{{ str.upper(name) }}
```

The render data dictionary is the template's primary variable scope. Template
variables may use dot access for struct fields and dictionary keys.

```stash
let data = {
    user: {
        name: "Ada",
        active: true,
    },
};

tpl.render("{{ user.name }}", data); // "Ada"
```

Template evaluation may also access available Stash globals and namespaces exposed
by the active interpreter.

Rendered values are converted to strings with Stash stringification rules.

## Filters

Filters transform output values using single-pipe syntax.

```tpl
{{ name | trim | upper }}
{{ items | length }}
{{ tags | join(", ") }}
```

The expression before the first pipe is evaluated, then filters are applied from
left to right. Filter arguments are parsed from the parenthesized argument list.

The single pipe for filters is distinct from Stash logical OR `||`.

```tpl
{{ enabled || fallback }}  {# logical OR #}
{{ name | upper }}         {# filter #}
```

### Built-in Filters

| Filter              | Input               | Result                                     |
| ------------------- | ------------------- | ------------------------------------------ |
| `upper`             | string              | uppercase string                           |
| `lower`             | string              | lowercase string                           |
| `trim`              | string              | string without leading/trailing whitespace |
| `capitalize`        | string              | string with first character uppercased     |
| `title`             | string              | title-cased string                         |
| `length`            | string, array, dict | length/count as int                        |
| `reverse`           | string, array       | reversed string or array copy              |
| `first`             | array               | first element                              |
| `last`              | array               | last element                               |
| `sort`              | array               | sorted array                               |
| `join(sep)`         | array               | joined string                              |
| `split(sep)`        | string              | array of string parts                      |
| `replace(old, new)` | string              | string with replacements                   |
| `default(value)`    | any                 | fallback when input is `null`              |
| `round`             | number              | rounded number                             |
| `abs`               | number              | absolute value                             |
| `keys`              | dict                | array of keys                              |
| `values`            | dict                | array of values                            |
| `json`              | any                 | JSON-encoded string                        |

Invalid input types or missing filter arguments produce template errors.

## Conditionals

Conditionals choose between template branches.

```tpl
{% if user.isAdmin %}
Welcome, admin.
{% elif user.isActive %}
Welcome back, {{ user.name }}.
{% else %}
Please log in.
{% endif %}
```

`elif` and `else` are optional. Multiple `elif` branches are allowed.

Conditions use Stash truthiness and operators.

```tpl
{% if count > 0 && status == "active" %}
{% if name != null && name != "" %}
{% if "admin" in user.roles %}
```

## Loops

Loops iterate over an expression.

```tpl
{% for server in servers %}
{{ server.host }}: {{ server.port }}
{% endfor %}
```

Supported iterable values follow Stash iteration behavior. Common cases are arrays,
ranges, strings, and dictionaries. Dictionary iteration yields keys.

Inside a loop, the `loop` variable exposes metadata.

| Field         | Meaning                     |
| ------------- | --------------------------- |
| `loop.index`  | 1-based iteration number    |
| `loop.index0` | 0-based iteration number    |
| `loop.first`  | true on the first iteration |
| `loop.last`   | true on the last iteration  |
| `loop.length` | total item count            |

```tpl
{% for item in items %}
{{ loop.index }}. {{ item }}{% if !loop.last %},{% endif %}
{% endfor %}
```

Nested loops each have their own `loop` variable. The inner loop's `loop` refers to
the inner iteration while the inner body is rendering.

## Includes

`include` renders another template using the current data context.

```tpl
{% include "partials/header.tpl" %}
{% include "partials/footer.tpl" %}
```

Include paths must be quoted string literals.

Path resolution:

- In `tpl.renderFile`, includes are resolved relative to the current template file.
- In string-based `tpl.render`, includes are resolved relative to the current working
  directory.

The renderer rejects include paths that escape the template root.

## Whitespace Control

By default, template tags leave surrounding whitespace intact. Add `-` inside a
delimiter to trim adjacent whitespace.

| Marker | Effect                           |
| ------ | -------------------------------- |
| `{%-`  | trim whitespace before a tag     |
| `-%}`  | trim whitespace after a tag      |
| `{{-`  | trim whitespace before output    |
| `-}}`  | trim whitespace after output     |
| `{#-`  | trim whitespace before a comment |
| `-#}`  | trim whitespace after a comment  |

```tpl
{% for item in items -%}
{{ item }}
{%- endfor %}
```

Trim markers remove adjacent spaces, tabs, and newlines. Both sides can be trimmed
at once.

```tpl
{{- name -}}
{%- if active -%}
```

## Raw Blocks and Comments

Raw blocks emit their contents literally without processing delimiters.

```tpl
{% raw %}
This {{ is not }} evaluated.
{% endraw %}
```

Raw blocks are useful for documenting template syntax or generating templates for
another engine.

Comments are removed from output.

```tpl
{# single-line comment #}

{#
  multi-line comment
#}
```

Comments cannot be nested. Whitespace trim markers may be used with comments.

## Errors

Template errors are raised to Stash code as runtime errors. Where available,
messages include template line and column information.

Common errors:

| Error                     | Example                                 |
| ------------------------- | --------------------------------------- | ----------- |
| unterminated output block | `{{ name`                               |
| unterminated tag block    | `{% if active %}` without `{% endif %}` |
| unterminated comment      | `{# note`                               |
| unknown tag               | `{% unless x %}`                        |
| invalid include           | `{% include path %}`                    |
| unknown filter            | `{{ name                                | slugify }}` |
| filter type error         | `{{ 42                                  | upper }}`   |
| runtime expression error  | `{{ 1 / 0 }}`                           |
| file not found            | `tpl.renderFile("missing.tpl", data)`   |
| path traversal            | `{% include "../../../etc/passwd" %}`   |

`tpl.render` requires a template string or compiled template and a dictionary. Wrong
argument types produce runtime type errors.

## Examples

### Configuration File

```stash
let data = {
    generated: time.now(),
    servers: [
        { host: "web1", port: 80 },
        { host: "web2", port: 443 },
    ],
};

let output = tpl.renderFile("templates/nginx.conf.tpl", data);
fs.writeFile("/etc/nginx/sites-enabled/app.conf", output);
```

`templates/nginx.conf.tpl`:

```tpl
# Generated {{ generated }}
{% for srv in servers %}
server {
    listen {{ srv.port }};
    server_name {{ srv.host }};
}
{% endfor %}
```

### Report Template

```stash
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
""", {
    status: "success",
    duration: 42,
    failures: [],
});
```

### Precompiled Template

```stash
let compiled = tpl.compile("Hello, {{ name | default(\"guest\") }}!");

for (let name in ["Ada", "Grace", null]) {
    io.println(tpl.render(compiled, { name: name }));
}
```
