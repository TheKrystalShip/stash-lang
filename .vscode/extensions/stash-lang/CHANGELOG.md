# Changelog

All notable changes to the **Stash Language** extension will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [0.1.0] — 2026-03-27

### Added

#### Language Support
- TextMate grammar for Stash (`.stash`) files covering keywords, strings, interpolation, command literals (`$(...)`), numbers, operators, comments, and shebangs
- TextMate grammar for Stash Templates (`.tpl`) files covering Jinja2-style expressions (`{{ }}`), tags (`{% %}`), comments (`{# #}`), and filters
- Markdown fenced code block injection for ` ```stash ` blocks
- Semantic highlighting with 12 token types: `namespace`, `type`, `function`, `parameter`, `variable`, `property`, `enumMember`, `keyword`, `number`, `string`, `comment`, `operator`

#### Language Server (LSP)
- 27 LSP handlers including:
  - Diagnostics with real-time error reporting
  - Code actions: typo correction and remove unused statement
  - Go to definition and type definition
  - Find references and document highlights
  - Document symbols and workspace symbols
  - Document links
  - Call hierarchy (incoming and outgoing)
  - Rename symbol
  - Autocomplete with context-aware suggestions
  - Signature help
  - Inlay hints
  - Linked editing ranges
  - Hover documentation
  - Code lens with reference counts
  - Folding ranges and selection range
  - Document formatting, range formatting, and on-type formatting
  - Implementation lookup
- Cross-file support via import resolution
- Optional background workspace indexing with `.stashignore` support

#### Debugger (DAP)
- 18 DAP handlers for full debugging support
- Breakpoint types: standard, conditional, hit count, and log points
- Execution control: continue, step over, step into, step out, pause
- Variable inspection with structured value display
- Debug console with expression evaluation
- Stop on entry support

#### Test Explorer
- TAP v14 protocol support
- Static discovery (regex-based) and dynamic discovery (interpreter-based)
- Hierarchical test tree with `describe`, `test`, and `skip` blocks
- Run and debug individual tests or entire suites from the Test Explorer panel
- Code lens with **Run** and **Debug** buttons on test functions

#### Snippets
- 34 Stash code snippets: `fn`, `let`, `const`, `struct`, `enum`, `if`, `while`, `for`, `switch`, `try`, `test.it`, `test.describe`, `aeq`, and more
- 12 template snippets for `.tpl` files: expressions, filters, control flow, includes, and more

#### Configuration
- Settings for LSP server, DAP adapter, and interpreter binary paths
- Cross-platform binary resolution with PATH search and `~/.local/bin/` fallback
- Settings for formatting, inlay hints, code lens, background workspace indexing, and test discovery mode

#### File Icon Theme
- Custom Stash file icon for `.stash` and `.tpl` files

[0.1.0]: https://github.com/TheKrystalShip/stash-lang/releases/tag/vscode-ext-v0.1.0
