# Stash Language Support for VS Code

Full language support for the [Stash](https://github.com/TheKrystalShip/stash-lang) scripting language, powered by a built-in Language Server Protocol (LSP) server with 19 features across 18 registered handlers.

## Features

### Editor

- **Syntax highlighting** for all Stash constructs: keywords, strings, interpolated strings, command literals, numbers, operators, comments, and more
- **Bracket matching** and auto-closing for `{}`, `[]`, `()`, and `""`
- **Comment toggling** with `Ctrl+/` (line) and `Shift+Alt+A` (block)
- **Code snippets** for common patterns (`fn`, `if`, `for`, `struct`, `enum`, `import`, etc.)
- **Shebang detection** — files starting with `#!/usr/bin/env stash` are recognized as Stash

### Language Server (LSP)

#### Diagnostics & Validation

- **Diagnostics** — syntax and semantic errors shown as squiggles in real time (undefined variables, const reassignment, break/continue outside loops, wrong function arity, duplicate declarations, import path/name validation, and more)
- **Code actions** — quick-fix suggestions on diagnostics: "Did you mean 'X'?" for misspelled identifiers (Levenshtein distance ≤ 2), and removal of misplaced `break`, `continue`, or `return` statements

#### Navigation

- **Go-to-definition** — `Ctrl+Click` or `F12` on a variable, function, struct, or enum to jump to its declaration — works across files via `import` resolution
- **Find all references** — `Shift+F12` to find every usage of a symbol across the file
- **Document highlights** — all occurrences of the symbol under the cursor are highlighted
- **Document symbols** — outline view and breadcrumbs with hierarchical structure (structs show fields, enums show members)
- **Workspace symbols** — `Ctrl+T` to search for functions, structs, enums, and variables across all open files
- **Document links** — `import` paths are clickable links; `Ctrl+Click` on a path opens the referenced file

#### Editing

- **Rename symbol** — `F2` to rename a variable, function, or other symbol and update all references
- **Autocomplete** — context-aware suggestions: keywords, built-in functions, scope-visible symbols, and after `.` suggests namespace members, struct fields, or enum members — includes cross-file symbols from imported modules
- **Signature help** — parameter hints shown while typing function arguments for both built-in and user-defined functions
- **Inlay hints** — parameter names shown inline at function call sites (e.g., `greet(name: "Alice", greeting: "Hello")`)

#### Formatting

- **Document formatting** — `Shift+Alt+F` or Command Palette → "Format Document" to reformat your code with consistent style
  - 2-space indentation (configurable via `editor.tabSize`)
  - Correct spacing around operators, keywords, braces, and colons
  - Enum and struct members placed one per line
  - Struct instantiation kept inline
  - Comments and shebang lines preserved
  - Blank lines between top-level declarations
  - Idempotent — formatting already-formatted code produces the same output
- **Format on save** — disabled by default; enable via `editor.formatOnSave` in settings
- **Format on paste** — disabled by default; enable via `editor.formatOnPaste` in settings

#### Display

- **Hover information** — hover over any identifier to see its declaration signature, kind, and import source for cross-file symbols
- **Semantic highlighting** — rich token classification (namespaces, types, functions, parameters, variables, properties, enum members, keywords, numbers, strings) for accurate theme coloring beyond the TextMate grammar
- **Code lens** — reference counts ("N references") shown above function, struct, and enum declarations
- **Folding ranges** — collapse function bodies, struct/enum declarations, if/else blocks, loops, and consecutive comment blocks
- **Selection range** — smart expand/shrink selection (`Shift+Alt+Right/Left`) walks from the innermost expression to enclosing statement, block, and function

### Cross-File Support

The LSP resolves `import` statements to provide a multi-file editing experience:

- Completions, hover, and go-to-definition work for symbols imported from other files
- Namespace imports (`import "path" as alias`) enable dot-completion on the alias
- Selective imports (`import { a, b } from "path"`) inject symbols into the current scope
- Invalid import paths and unknown import names are reported as diagnostics

## Settings

| Setting                    | Default             | Description                                                                            |
| -------------------------- | ------------------- | -------------------------------------------------------------------------------------- |
| `stash.lspPath`            | `""`                | Absolute path to the Stash LSP server binary. If empty, looks for `stash-lsp` on PATH. |
| `stash.formatting.enabled` | `true`              | Enable or disable the document formatter.                                              |
| `editor.formatOnSave`      | `false` (for Stash) | Automatically format on save.                                                          |
| `editor.formatOnPaste`     | `false` (for Stash) | Automatically format pasted code.                                                      |
| `editor.tabSize`           | `2` (for Stash)     | Number of spaces per indent level.                                                     |
| `editor.insertSpaces`      | `true` (for Stash)  | Use spaces instead of tabs.                                                            |

## Prerequisites

The LSP server requires the [.NET 10 SDK](https://dotnet.microsoft.com/download) to be installed.

Before first use, build the LSP server from the repository root:

```bash
dotnet build Stash.Lsp/Stash.Lsp.csproj
```

## Installation

### From source (development)

1. Install the extension from this project's local directory:

   ```bash
   ~/.vscode/extensions/stash-lang
   ```

2. Build the LSP server (see [Prerequisites](#prerequisites))

3. Reload VS Code (`Ctrl+Shift+P` → "Developer: Reload Window")

4. Open any `.stash` file — syntax highlighting and the language server activate automatically

## Keyboard Shortcuts

| Shortcut           | Action                   |
| ------------------ | ------------------------ |
| `Shift+Alt+F`      | Format Document          |
| `F12`              | Go to Definition         |
| `Shift+F12`        | Find All References      |
| `F2`               | Rename Symbol            |
| `Ctrl+T`           | Search Workspace Symbols |
| `Ctrl+Space`       | Trigger Autocomplete     |
| `Ctrl+Shift+Space` | Trigger Signature Help   |
| `Shift+Alt+Right`  | Expand Selection         |
| `Shift+Alt+Left`   | Shrink Selection         |
| `Ctrl+/`           | Toggle Line Comment      |
| `Shift+Alt+A`      | Toggle Block Comment     |

## Snippets

| Prefix    | Description                  |
| --------- | ---------------------------- |
| `fn`      | Function declaration         |
| `let`     | Variable declaration         |
| `const`   | Constant declaration         |
| `struct`  | Struct declaration           |
| `enum`    | Enum declaration             |
| `if`      | If statement                 |
| `ife`     | If-else statement            |
| `while`   | While loop                   |
| `for`     | For-in loop                  |
| `cmd`     | Command execution            |
| `import`  | Import statement             |
| `try`     | Try expression with fallback |
| `pln`     | println()                    |
| `str`     | Interpolated string          |
| `shebang` | Shebang line                 |
