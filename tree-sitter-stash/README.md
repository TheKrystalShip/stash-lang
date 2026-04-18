# tree-sitter-stash

Tree-sitter grammar for the [Stash](https://github.com/TheKrystalShip/stash-lang) scripting language.

## Overview

This grammar provides full syntactic parsing for Stash, enabling syntax highlighting, code folding, indentation, and scope tracking in any editor that supports tree-sitter (Neovim, Helix, Zed, Emacs, etc.).

**Coverage:**

- Declarations: `let`, `const`, `fn`, `struct`, `enum`, `interface`, `import`, `extend`, `elevate`
- Statements: `if`/`else`, `for`/`in`, `while`, `do`/`while`, `switch`/`case`, `try`/`catch`/`finally`, `defer`, `return`, `break`, `continue`, `throw`
- Expressions: binary/unary ops, ternary, null-coalescing (`??`), optional chaining (`?.`), pipe (`|>`), lambda (`fn(x) => expr`), spread (`...`), range (`..`), `is`, `await`, struct init, retry/timeout
- Literals: integers, floats, hex, binary, octal, durations (`5s`, `100ms`), byte sizes (`1kb`, `4mb`), semver (`1.2.3`), IP addresses (`192.168.1.1`), booleans, null
- Strings with interpolation (`"hello ${name}"`)
- Triple-quoted strings (`"""..."""`)
- Command literals (`$(ls -la)`) with balanced parentheses
- Nestable block comments (`/* ... /* ... */ ... */`)
- Doc comments (`///`)

**Included queries:**

| File             | Purpose                               |
| ---------------- | ------------------------------------- |
| `highlights.scm` | Syntax highlighting captures          |
| `locals.scm`     | Scope, definition, reference tracking |
| `folds.scm`      | Foldable regions                      |
| `indents.scm`    | Auto-indentation rules                |
| `injections.scm` | Language injection (placeholder)      |

## Prerequisites

- [Node.js](https://nodejs.org/) >= 18
- A C compiler (gcc or clang)
- [tree-sitter CLI](https://tree-sitter.github.io/tree-sitter/creating-parsers/1-getting-started.html) (installed automatically via npm)

## Building

```bash
cd tree-sitter-stash
npm install
npx tree-sitter generate
```

This generates `src/parser.c` from `grammar.js`. The external scanner (`src/scanner.c`) handles nestable block comments and command literal content.

## Testing

```bash
npx tree-sitter test
```

Runs the corpus tests in `test/corpus/`. All 40 tests should pass.

To parse a Stash file and inspect the syntax tree:

```bash
npx tree-sitter parse /path/to/file.stash
```

## Editor Integration

### Neovim

See [`editors/neovim/README.md`](../editors/neovim/README.md) for a complete Neovim setup guide including NvChad instructions.

**Quick version** — add the parser to your Neovim config:

```lua
-- In your init.lua or plugin config
local parser_config = require("nvim-treesitter.parsers").get_parser_configs()
parser_config.stash = {
  install_info = {
    url = "/absolute/path/to/tree-sitter-stash",  -- local path to this directory
    files = { "src/parser.c", "src/scanner.c" },
    generate_requires_npm = true,
  },
  filetype = "stash",
}
```

Then install the parser: `:TSInstall stash`

### Helix

Add to `~/.config/helix/languages.toml`:

```toml
[[language]]
name = "stash"
scope = "source.stash"
file-types = ["stash"]
comment-tokens = ["//"]
block-comment-tokens = [{ start = "/*", end = "*/" }]
indent = { tab-width = 4, unit = "    " }
language-servers = ["stash-lsp"]

[language-server.stash-lsp]
command = "stash-lsp"

[[grammar]]
name = "stash"
source = { path = "/absolute/path/to/tree-sitter-stash" }
```

Then build the grammar: `hx --grammar build`

Copy the queries to `~/.config/helix/runtime/queries/stash/`:

```bash
mkdir -p ~/.config/helix/runtime/queries/stash
cp tree-sitter-stash/queries/*.scm ~/.config/helix/runtime/queries/stash/
```

### Zed

Zed uses tree-sitter natively. Add a language extension or configure manually following [Zed's language extension docs](https://zed.dev/docs/extensions/languages).

### Emacs

With [emacs-tree-sitter](https://github.com/emacs-tree-sitter/elisp-tree-sitter), build the shared library and register it:

```bash
cd tree-sitter-stash
cc -shared -o stash.so -I src src/parser.c src/scanner.c -O2
```

## Grammar Structure

| File            | Purpose                                          |
| --------------- | ------------------------------------------------ |
| `grammar.js`    | Grammar definition (830 lines)                   |
| `src/scanner.c` | External scanner for block comments and commands |
| `src/parser.c`  | Generated parser (do not edit)                   |
| `queries/*.scm` | Editor query files                               |
| `test/corpus/`  | Corpus tests for `tree-sitter test`              |

## License

MIT
