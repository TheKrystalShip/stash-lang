# Stash for Neovim

Neovim plugin providing syntax highlighting (tree-sitter + Vim fallback), LSP integration, and editor settings for the [Stash](https://github.com/TheKrystalShip/stash-lang) scripting language.

## What You Get

- **Syntax highlighting** via tree-sitter (rich, accurate) or Vim regex (fallback)
- **Semantic highlighting** via the Stash LSP (type-aware: structs, enums, methods, built-ins, async)
- **Auto-completion**, **hover docs**, **go-to-definition**, **diagnostics** — all from the LSP
- **Comment toggling** (`gc` / `gcc`) configured for `//`

## Prerequisites

| Dependency     | Purpose                        | Install                                                    |
| -------------- | ------------------------------ | ---------------------------------------------------------- |
| Neovim >= 0.10 | Editor                         | [neovim.io](https://neovim.io)                             |
| `stash-lsp`    | Language server                | Build from `Stash.Lsp/` — see [below](#building-stash-lsp) |
| tree-sitter    | Syntax highlighting (optional) | Ships with Neovim; parser needs to be compiled             |
| C compiler     | Build the tree-sitter parser   | `gcc` or `clang`                                           |

## Setup

### Step 1: Install the Neovim Plugin

The plugin lives at `editors/neovim/` in the Stash monorepo. You need to add this directory to Neovim's runtime path.

#### Option A: NvChad / lazy.nvim

Add to your lazy.nvim plugin spec (e.g. `lua/plugins/stash.lua` in your NvChad custom config):

```lua
return {
  {
    dir = "/path/to/stash-lang/editors/neovim",
    ft = "stash",
    config = function() end,
  },
}
```

> Replace `/path/to/stash-lang` with the actual absolute path to your Stash repo clone.

#### Option B: Manual runtimepath

Add to your `init.lua`:

```lua
vim.opt.rtp:prepend("/path/to/stash-lang/editors/neovim")
```

#### Option C: Symlinks

```bash
mkdir -p ~/.config/nvim/ftdetect ~/.config/nvim/ftplugin ~/.config/nvim/syntax
ln -s /path/to/stash-lang/editors/neovim/ftdetect/stash.lua ~/.config/nvim/ftdetect/stash.lua
ln -s /path/to/stash-lang/editors/neovim/ftplugin/stash.lua ~/.config/nvim/ftplugin/stash.lua
ln -s /path/to/stash-lang/editors/neovim/syntax/stash.vim   ~/.config/nvim/syntax/stash.vim
```

### Step 2: Set Up Tree-sitter Highlighting

Tree-sitter provides fast, accurate syntax highlighting that works without the LSP. This is what colorizes your code instantly while typing.

#### For NvChad / nvim-treesitter Users

NvChad ships with [nvim-treesitter](https://github.com/nvim-treesitter/nvim-treesitter). You need to register the Stash parser so nvim-treesitter can compile and load it.

Add to your NvChad custom config (e.g. `lua/configs/treesitter.lua` or wherever you configure nvim-treesitter):

```lua
local parser_config = require("nvim-treesitter.parsers").get_parser_configs()

parser_config.stash = {
  install_info = {
    url = "/path/to/stash-lang/tree-sitter-stash",
    files = { "src/parser.c", "src/scanner.c" },
    generate_requires_npm = true,
  },
  filetype = "stash",
}
```

Then install the parser:

```vim
:TSInstall stash
```

Verify it's working:

```vim
:TSInstallInfo
```

You should see `stash` listed with a checkmark.

#### Installing the Highlight Queries

nvim-treesitter needs query files to know _how_ to highlight the parsed tree. Copy them to the nvim-treesitter runtime queries directory:

```bash
# Find where nvim-treesitter is installed
# For lazy.nvim / NvChad, it's typically:
TSDIR=~/.local/share/nvim/lazy/nvim-treesitter

# Create the queries directory and copy
mkdir -p "$TSDIR/queries/stash"
cp /path/to/stash-lang/editors/neovim/queries/stash/highlights.scm "$TSDIR/queries/stash/"
cp /path/to/stash-lang/tree-sitter-stash/queries/locals.scm "$TSDIR/queries/stash/"
cp /path/to/stash-lang/tree-sitter-stash/queries/folds.scm "$TSDIR/queries/stash/"
cp /path/to/stash-lang/tree-sitter-stash/queries/indents.scm "$TSDIR/queries/stash/"
```

Alternatively, symlink the entire queries directory:

```bash
ln -s /path/to/stash-lang/editors/neovim/queries/stash "$TSDIR/queries/stash"
```

> **Note:** If you installed the plugin via runtimepath (Option A or B), Neovim may find the queries from `editors/neovim/queries/stash/` automatically. Try opening a `.stash` file first — if highlighting works, you can skip the copy step.

#### For Plain Neovim (No nvim-treesitter Plugin)

If you're using Neovim's built-in tree-sitter support without the nvim-treesitter plugin:

```lua
-- Compile the parser manually first:
-- cd /path/to/stash-lang/tree-sitter-stash
-- cc -shared -o stash.so -I src src/parser.c src/scanner.c -O2

-- Then register it:
vim.treesitter.language.add("stash", {
  path = "/path/to/stash-lang/tree-sitter-stash/stash.so",
})
```

### Step 3: Set Up the LSP (Semantic Highlighting + IDE Features)

The Stash language server provides semantic highlighting on top of tree-sitter's syntax highlighting. Semantic tokens add type-aware coloring — distinguishing structs from enums, marking built-in functions, identifying async functions, etc. It also provides completions, hover, go-to-definition, diagnostics, and more.

#### Building stash-lsp

```bash
cd /path/to/stash-lang
dotnet publish Stash.Lsp/ -c Release -o ~/.local/bin/stash-lsp-dist
ln -s ~/.local/bin/stash-lsp-dist/Stash.Lsp ~/.local/bin/stash-lsp
```

Verify it's accessible:

```bash
stash-lsp --version
```

> **Important:** The LSP must NOT be built with Native AOT — it uses reflection (OmniSharp/DryIoc). Use `dotnet publish` without AOT flags.

#### LSP Auto-Start

The `ftplugin/stash.lua` file included in this plugin automatically starts the LSP when you open a `.stash` file. It uses `vim.lsp.start()` with:

- **Command:** `stash-lsp` (must be on your `$PATH`)
- **Root detection:** looks for `.git` or `stash.toml` in parent directories
- **Filetype:** `stash`

If `stash-lsp` is not on your `$PATH`, edit `ftplugin/stash.lua` and change the `cmd` to an absolute path:

```lua
cmd = { "/path/to/stash-lsp" },
```

#### NvChad: Using lspconfig Instead

If you prefer to use `nvim-lspconfig` (common in NvChad setups), you can configure the LSP there instead and remove the `vim.lsp.start()` call from `ftplugin/stash.lua`.

Add to your lspconfig setup (e.g. `lua/configs/lspconfig.lua`):

```lua
local lspconfig = require("lspconfig")
local configs = require("lspconfig.configs")

if not configs.stash_lsp then
  configs.stash_lsp = {
    default_config = {
      cmd = { "stash-lsp" },
      filetypes = { "stash" },
      root_dir = lspconfig.util.root_pattern(".git", "stash.toml"),
      settings = {},
    },
  }
end

lspconfig.stash_lsp.setup({
  -- Add your on_attach, capabilities, etc. here
  -- on_attach = your_on_attach_function,
  -- capabilities = your_capabilities,
})
```

Then comment out or remove the `vim.lsp.start(...)` block in `editors/neovim/ftplugin/stash.lua` to avoid starting the LSP twice.

#### Enabling Semantic Highlighting

Neovim supports LSP semantic tokens out of the box (Neovim >= 0.9). The Stash LSP sends semantic tokens automatically. To verify it's working:

1. Open a `.stash` file
2. Run `:lua vim.print(vim.lsp.get_clients())` — you should see `stash-lsp` listed
3. Run `:Inspect` on a symbol — you should see both tree-sitter and LSP highlight groups

Semantic highlighting layers on top of tree-sitter. Tree-sitter provides the instant baseline (keywords, strings, numbers, operators), and the LSP refines identifiers with semantic info (this variable is a struct, that function is async, this call is a built-in).

If semantic tokens don't seem active, ensure your colorscheme supports them. Most modern themes do. You can check with:

```vim
:hi @lsp.type.function
:hi @lsp.type.struct
:hi @lsp.mod.defaultLibrary
```

## Verifying Your Setup

Open a `.stash` file and check each layer:

| Check                    | Command / Action                                      | Expected                       |
| ------------------------ | ----------------------------------------------------- | ------------------------------ |
| Filetype detected        | `:set ft?`                                            | `filetype=stash`               |
| Tree-sitter active       | `:InspectTree`                                        | Syntax tree panel opens        |
| Tree-sitter highlighting | `:Inspect` on a keyword                               | Shows `@keyword.*` capture     |
| LSP running              | `:LspInfo` or `:lua vim.print(vim.lsp.get_clients())` | `stash-lsp` listed             |
| Semantic tokens          | `:Inspect` on a function name                         | Shows `@lsp.type.function`     |
| Comment toggling         | `gcc` on a line                                       | Toggles `//` comment           |
| Completions              | Type `arr.` and trigger completion                    | Built-in function list appears |

## Without Tree-sitter (Vim Syntax Fallback)

If you don't want to compile a tree-sitter parser, the plugin includes `syntax/stash.vim` — a Vim regex-based syntax file that provides basic highlighting for keywords, strings, numbers, comments, and operators. It loads automatically when tree-sitter highlighting is not active for the buffer.

## Troubleshooting

**"stash-lsp" not found:**
Make sure the binary is on your `$PATH`, or use an absolute path in the `cmd` configuration.

**No syntax highlighting:**

1. Check filetype: `:set ft?` should show `stash`
2. Check tree-sitter: `:TSInstallInfo` should show `stash` installed
3. Check queries exist: `:echo nvim_get_runtime_file("queries/stash/highlights.scm", v:true)`

**Semantic highlighting not working:**

1. Verify LSP is running: `:LspInfo`
2. Check Neovim version: semantic tokens require >= 0.9
3. Check your colorscheme supports `@lsp.*` highlights

**Tree-sitter parser won't compile:**
Make sure you have a C compiler installed (`gcc --version` or `clang --version`). The Stash grammar requires compiling both `src/parser.c` and `src/scanner.c`.

**NvChad: queries not found after `:TSInstall stash`:**
nvim-treesitter installs the parser but not queries for custom parsers. You need to copy the query files manually — see [Step 2](#step-2-set-up-tree-sitter-highlighting).

## File Reference

```
editors/neovim/
├── ftdetect/
│   └── stash.lua           # Filetype detection (.stash → stash)
├── ftplugin/
│   └── stash.lua           # LSP config, comment/indent settings
├── queries/
│   └── stash/
│       └── highlights.scm  # Tree-sitter highlight captures
├── syntax/
│   └── stash.vim           # Vim regex syntax fallback
└── README.md               # This file
```

## License

MIT
