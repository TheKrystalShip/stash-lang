![Stash](images/stash-icon.png)

# Stash Language Support for VS Code

Full-featured language support for the Stash scripting language — syntax highlighting, intelligent code editing, debugging, and integrated test discovery.

✦ **LSP** — 21 handlers · **DAP** — full debugging · **Test Explorer** — TAP v14 · **Snippets** — 26 templates · **Semantic Highlighting** — 12 token types

---

## Features Overview

The Stash extension provides a complete development environment for `.stash` files. It bundles a Language Server (LSP) for intelligent editing, a Debug Adapter (DAP) for interactive debugging, a native Test Explorer integration, TextMate syntax highlighting, semantic token highlighting, and a rich set of snippets — all in one package.

---

### Syntax Highlighting

TextMate grammar coverage for all Stash constructs:

- **Keywords** — `let`, `const`, `fn`, `struct`, `enum`, `if`, `else`, `while`, `for`, `in`, `return`, `import`, `as`, `switch`, `case`, `break`, `continue`, `try`, `true`, `false`, `null`
- **Strings** — plain strings, interpolated strings (`$"...${expr}..."`), heredoc multi-line strings (`"""..."""`)
- **Command literals** — `$(...)` shell command invocations
- **Numbers** — integers and floats
- **Operators** — arithmetic, logical, bitwise, comparison, assignment
- **Comments** — line (`//`) and block (`/* */`)
- **Shebang lines** — `#!/usr/bin/env stash`
- **Markdown fenced code blocks** — ` ```stash ` blocks in `.md` files are highlighted via grammar injection

**Semantic Highlighting** — 12 token types for accurate, theme-aware colorization beyond what TextMate grammars can provide:

| Token Type   | Examples                        |
| ------------ | ------------------------------- |
| `namespace`  | Import aliases, module names    |
| `type`       | Struct and enum names           |
| `function`   | Function declarations and calls |
| `parameter`  | Function and lambda parameters  |
| `variable`   | Local and global variables      |
| `property`   | Struct field accesses           |
| `enumMember` | Enum member accesses            |
| `keyword`    | All language keywords           |
| `number`     | Integer and float literals      |
| `string`     | String literals                 |
| `comment`    | Line and block comments         |
| `operator`   | Operators and punctuation       |

---

### Language Server (LSP)

Built on OmniSharp, the Stash LSP provides 21 handlers covering the full editing lifecycle.

#### Diagnostics & Code Actions

**Real-time diagnostics** — squiggles appear as you type for:

- Syntax errors (parse failures)
- Undefined variables and functions
- Constant reassignment
- `break` / `continue` outside loops
- `return` outside functions
- Wrong function call arity
- Duplicate declarations in the same scope
- Invalid `import` paths and unknown imported names

**Quick fixes** — code actions on diagnostics:

- `💡 Did you mean 'X'?` — typo correction using Levenshtein distance ≤ 2
- `💡 Remove statement` — remove misplaced `break`, `continue`, or `return`

#### Navigation

| Feature                 | Shortcut             | Description                                                                        |
| ----------------------- | -------------------- | ---------------------------------------------------------------------------------- |
| **Go to Definition**    | `F12` / `Ctrl+Click` | Jump to the declaration of any symbol — works across files via `import` resolution |
| **Find All References** | `Shift+F12`          | Find every usage of a symbol in the current file and across imports                |
| **Document Highlights** | _(automatic)_        | All occurrences of the symbol under the cursor are highlighted                     |
| **Document Symbols**    | _(outline panel)_    | Hierarchical symbol tree: struct fields, enum members, nested functions            |
| **Workspace Symbols**   | `Ctrl+T`             | Fuzzy-search functions, structs, enums, and variables across open and indexed files |
| **Document Links**      | `Ctrl+Click`         | `import` paths are clickable — opens the referenced file directly                  |
| **Call Hierarchy**      | _(right-click menu)_ | View incoming and outgoing calls for any function                                  |

#### Editing

**Rename Symbol** (`F2`) — renames a variable, function, struct, or enum and updates every reference in the file.

**Autocomplete** (`Ctrl+Space`) — context-aware suggestions:

- Language keywords and built-in functions with documentation
- All symbols visible in the current scope
- After `.` — namespace members, struct fields, enum members
- Cross-file symbols from imported modules (namespace aliases and selective imports)

**Signature Help** (`Ctrl+Shift+Space`) — parameter hints while typing function arguments for both built-in functions and user-defined functions, with the active parameter highlighted.

**Inlay Hints** — parameter name labels shown inline at call sites:

```stash
greet(name: "Alice", greeting: "Hello")
```

**Linked Editing** — simultaneously rename matching identifiers as you type.

#### Formatting

**Document Formatting** (`Shift+Alt+F`) applies consistent style:

- 2-space indentation (respects `editor.tabSize`)
- Correct spacing around operators, keywords, braces, and colons
- Enum and struct members placed one per line
- Struct instantiation kept on one line
- Comments and shebang lines preserved
- Blank lines between top-level declarations

Format triggers:

- `Shift+Alt+F` or Command Palette → "Format Document"
- **Format on save** — opt-in via `editor.formatOnSave`
- **Format on paste** — opt-in via `editor.formatOnPaste`

#### Display

**Hover Information** — hover over any identifier to see its declaration signature, kind (variable, function, struct, enum), type hint if present, and import source for cross-file symbols.

**Code Lens** — reference counts displayed above function, struct, and enum declarations:

```
2 references
fn greet(name) { ... }
```

**Folding Ranges** — collapse:

- Function bodies
- Struct and enum declarations
- `if` / `else` blocks
- `while` and `for` loops
- Consecutive comment blocks
- `// #region` / `// #endregion` markers

**Selection Range** (`Shift+Alt+Right` / `Shift+Alt+Left`) — smart expand/shrink selection walks from the innermost expression outward through statements, blocks, and function bodies.

#### Cross-File Support

The LSP resolves `import` statements to provide a seamless multi-file editing experience:

- Completions, hover, and go-to-definition work for imported symbols
- Namespace imports (`import "path" as alias`) enable dot-completion on the alias
- Selective imports (`import { a, b } from "path"`) inject symbols into the current scope
- Invalid import paths and unknown import names are reported as diagnostics

#### Workspace Indexing

When `stash.workspaceIndexing.enabled` is set to `true`, the LSP server scans all `.stash` files in the workspace in the background to build a complete cross-file reference index.

**What changes:**

- **Code Lens** — reference counts above functions, structs, and enums reflect all workspace files, not just open ones
- **Workspace Symbols** (`Ctrl+T`) — search results include symbols from every `.stash` file in the workspace
- **Progressive updates** — reference counts tick up gradually as files are analyzed, rather than appearing all at once

**How it works:**

- Scans all workspace folders for `.stash` files on server startup
- Respects `.stashignore` patterns — vendor directories, build output, and other excluded paths are skipped
- Skips files currently open in the editor (they already have fresher analysis)
- Watches for external file changes (create, modify, delete) to keep the index current

> **Note:** Background indexing increases memory usage proportional to the number of `.stash` files in the workspace. It is disabled by default and recommended for projects where workspace-wide reference counts are valuable.

---

### Debugger (DAP)

Full Debug Adapter Protocol support via the Stash DAP server.

#### Breakpoints

| Type        | Description                                       |
| ----------- | ------------------------------------------------- |
| Standard    | Break on a specific line                          |
| Conditional | Break only when an expression evaluates to truthy |
| Hit Count   | Break after the line is hit N times               |
| Log Point   | Print a message without pausing execution         |

#### Execution Control

- **Continue** (`F5`) — resume until the next breakpoint
- **Step Over** (`F10`) — execute the next statement
- **Step Into** (`F11`) — step into a function call
- **Step Out** (`Shift+F11`) — run until the current function returns
- **Pause** — suspend execution at the current position

#### Inspection

- **Variables panel** — inspect locals, globals, and closure variables at any breakpoint
- **Debug Console** — evaluate arbitrary Stash expressions in the current scope
- **Stop on Entry** — optionally pause on the first line of the script

#### Launch Configuration

Add to `.vscode/launch.json`:

```json
{
  "version": "0.2.0",
  "configurations": [
    {
      "type": "stash",
      "request": "launch",
      "name": "Run Stash Script",
      "program": "${file}",
      "stopOnEntry": false
    }
  ]
}
```

Auto-generated launch configurations and snippets are available via the Run & Debug panel.

---

### Test Explorer

Native VS Code Test Explorer integration built on the TAP v14 protocol.

#### Discovery

- Automatically scans for test files matching the configured glob pattern (default: `**/*.test.stash`)
- Parses `describe` and `test` blocks to build a hierarchical test tree
- File watcher triggers automatic re-discovery when test files are created or modified

#### Running Tests

- **Run All** — execute every discovered test
- **Run Suite** — run a single `describe` block and its children
- **Run Test** — run a single `it` / `test` case
- Fully qualified test names are used for precise filtering
- TAP v14 output is parsed to determine pass/fail status and display failure messages

#### Debugging Tests

Any test can be debugged: set breakpoints in your test file, then right-click → "Debug Test" to launch the DAP server with that test isolated.

#### Test File Example

```stash
describe("math utils") {
    it("adds two numbers") {
        let result = add(2, 3);
        assert(result == 5, "expected 5");
    }

    it("handles zero") {
        assert(add(0, 0) == 0, "expected 0");
    }
}
```

---

### Snippets

26 code snippets for common patterns. Trigger via `Ctrl+Space` or by typing the prefix.

| Prefix    | Description                    |
| --------- | ------------------------------ |
| `fn`      | Function declaration           |
| `fnt`     | Typed function declaration     |
| `let`     | Variable declaration           |
| `lett`    | Typed variable declaration     |
| `const`   | Constant declaration           |
| `constt`  | Typed constant declaration     |
| `struct`  | Struct declaration             |
| `structt` | Typed struct declaration       |
| `enum`    | Enum declaration               |
| `if`      | If statement                   |
| `ife`     | If-else statement              |
| `while`   | While loop                     |
| `for`     | For-in loop                    |
| `forr`    | For-in range loop              |
| `switch`  | Switch expression              |
| `cmd`     | Command execution              |
| `import`  | Import statement               |
| `try`     | Try expression with fallback   |
| `pln`     | `println()`                    |
| `str`     | Interpolated string            |
| `ml`      | Multi-line string              |
| `mli`     | Interpolated multi-line string |
| `letd`    | Array destructuring            |
| `letdd`   | Dict destructuring             |
| `shebang` | Shebang line                   |

---

## Configuration

### Extension Settings

| Setting                      | Default           | Description                                                                            |
| ---------------------------- | ----------------- | -------------------------------------------------------------------------------------- |
| `stash.lspPath`              | `""`              | Path to the Stash LSP server binary. If empty, looks for `stash-lsp` on `PATH`.        |
| `stash.dapPath`              | `""`              | Path to the Stash DAP debug adapter binary. If empty, looks for `stash-dap` on `PATH`. |
| `stash.interpreterPath`      | `""`              | Path to the Stash interpreter binary. If empty, looks for `stash` on `PATH`.           |
| `stash.formatting.enabled`   | `true`            | Enable or disable the document formatter.                                              |
| `stash.workspaceIndexing.enabled` | `false`      | Enable background scanning of all `.stash` files for workspace-wide reference counts and symbol search. Respects `.stashignore`. |
| `stash.testing.filePattern`  | `**/*.test.stash` | Glob pattern for test file discovery.                                                  |
| `stash.testing.autoDiscover` | `true`            | Automatically discover tests in matching files.                                        |

### Editor Defaults (pre-configured for Stash)

| Setting                               | Value                       | Description                                |
| ------------------------------------- | --------------------------- | ------------------------------------------ |
| `editor.tabSize`                      | `2`                         | Two spaces per indent level                |
| `editor.insertSpaces`                 | `true`                      | Spaces instead of tabs                     |
| `editor.formatOnSave`                 | `false`                     | Opt-in: format automatically on save       |
| `editor.formatOnPaste`                | `false`                     | Opt-in: format automatically on paste      |
| `editor.semanticHighlighting.enabled` | `true`                      | Enable semantic token coloring             |
| `editor.defaultFormatter`             | `TheKrystalShip.stash-lang` | Use the Stash formatter for `.stash` files |

---

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
| `F5`               | Start Debugging          |
| `F9`               | Toggle Breakpoint        |

---

## File Icon Theme

The extension includes a **Stash File Icons** theme that displays the Stash icon on `.stash` files in the Explorer panel.

To enable: **File → Preferences → File Icon Theme → Stash File Icons**

---

## Prerequisites

The LSP server, DAP adapter, and interpreter all require the [.NET 10 SDK](https://dotnet.microsoft.com/download).

Build all components from the repository root:

```bash
dotnet build Stash.Lsp/
dotnet build Stash.Dap/
dotnet build Stash.Interpreter/
```

---

## Installation

### From Source (Development)

1. Clone the repository:

   ```bash
   git clone https://github.com/TheKrystalShip/stash-lang.git
   cd stash-lang
   ```

2. Build the .NET projects:

   ```bash
   dotnet build Stash.Lsp/
   dotnet build Stash.Dap/
   dotnet build Stash.Interpreter/
   ```

3. Symlink or copy the extension directory to `~/.vscode/extensions/`:

   ```bash
   ln -s "$PWD/.vscode/extensions/stash-lang" ~/.vscode/extensions/stash-lang
   ```

4. Reload VS Code (`Ctrl+Shift+P` → **Developer: Reload Window**)

5. Open any `.stash` file — syntax highlighting and the language server activate automatically.

---

## Links

- [Repository](https://github.com/TheKrystalShip/stash-lang)
- [Language Specification](https://github.com/TheKrystalShip/stash-lang/blob/main/docs/Stash%20—%20Language%20Specification.md)
- [Standard Library Reference](https://github.com/TheKrystalShip/stash-lang/blob/main/docs/Stash%20—%20Standard%20Library%20Reference.md)
