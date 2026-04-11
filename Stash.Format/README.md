# Stash Code Formatter

`stash-format` is the official code formatter for the [Stash language](../README.md). It enforces a consistent style across your codebase with minimal configuration, powered by a Wadler-Lindig document IR for intelligent line-breaking decisions.

## Quick Start

```bash
# Format all .stash files in the current directory (prints to stdout)
stash-format .

# Format files in place
stash-format --write src/

# Check if files are formatted (CI mode — exits 1 if changes needed)
stash-format --check .

# Show what would change without modifying files
stash-format --diff .

# Format from stdin
cat script.stash | stash-format
```

## Installation

The formatter ships as part of the Stash CLI toolchain. If you have `stash` installed, you already have `stash-format`.

```bash
# Build from source
dotnet build Stash.Format/

# Run directly
dotnet run --project Stash.Format/ -- [OPTIONS] [FILES...]
```

## Configuration

### Configuration File

Create a `.stashformat` file in your project root. The formatter walks up the directory tree from each file being formatted and uses the first `.stashformat` it finds.

```ini
# .stashformat
indentSize = 4
useTabs = false
printWidth = 100
trailingComma = all
endOfLine = lf
bracketSpacing = true
sortImports = false
blankLinesBetweenBlocks = 1
singleLineBlocks = false
```

See the [example .stashformat](.stashformat) file in this directory for a fully commented reference.

### Available Options

| Option           | Type | Default | Values               | Description                                |
| ---------------- | ---- | ------- | -------------------- | ------------------------------------------ |
| `indentSize`     | int  | `2`     | Any positive integer | Spaces per indent level                    |
| `useTabs`        | bool | `false` | `true`, `false`      | Use tabs instead of spaces                 |
| `printWidth`     | int  | `80`    | Any positive integer | Target line width for wrapping             |
| `trailingComma`  | enum | `none`  | `none`, `all`        | Trailing commas in multi-line collections  |
| `endOfLine`      | enum | `lf`    | `lf`, `crlf`, `auto` | Line ending style                          |
| `bracketSpacing` | bool | `true`  | `true`, `false`      | Spaces inside `{}` in single-line literals |
| `sortImports`             | bool | `false` | `true`, `false`           | Sort import statements alphabetically          |
| `blankLinesBetweenBlocks` | int  | `1`     | `1`, `2`                  | Blank lines between top-level declarations     |
| `singleLineBlocks`        | bool | `false` | `true`, `false`           | Allow single-line function/block bodies        |

### Configuration Priority

Settings are resolved in this order (later wins):

1. **Built-in defaults** — sensible out-of-the-box values
2. **`.stashformat` file** — project-level configuration
3. **CLI flags** — per-invocation overrides

```bash
# .stashformat has indentSize=4, but CLI overrides it
stash-format --indent-size 2 src/
```

## EditorConfig Support

Stash.Format supports `.editorconfig` as a fallback configuration source. If no `.stashformat` file is found while walking up the directory tree, the formatter will look for `.editorconfig` files instead.

### Precedence

Settings are resolved in this order (later wins):

1. **Built-in defaults**
2. **`.editorconfig`** — standard editor config, used when no `.stashformat` is present
3. **`.stashformat`** — project-level Stash-specific config (takes priority over `.editorconfig`)
4. **CLI flags** — per-invocation overrides

### Example `.editorconfig`

```ini
root = true

[*.stash]
indent_style = space
indent_size = 2
end_of_line = lf
max_line_length = 80

# Stash-specific properties
stash_trailing_comma = none
stash_bracket_spacing = true
stash_sort_imports = false
stash_blank_lines_between_blocks = 1
stash_single_line_blocks = false
```

### Property Mapping

| EditorConfig Key         | Stash Property   | Values                              |
| ------------------------ | ---------------- | ----------------------------------- |
| `indent_style`           | `useTabs`        | `space` → `false`, `tab` → `true`  |
| `indent_size`            | `indentSize`     | Any positive integer                |
| `max_line_length`        | `printWidth`     | Positive integer, or `off`          |
| `end_of_line`            | `endOfLine`      | `lf`, `crlf`                        |
| `stash_trailing_comma`   | `trailingComma`  | `none`, `all`                       |
| `stash_bracket_spacing`            | `bracketSpacing`          | `true`, `false`           |
| `stash_sort_imports`               | `sortImports`             | `true`, `false`           |
| `stash_blank_lines_between_blocks` | `blankLinesBetweenBlocks` | `1`, `2`                  |
| `stash_single_line_blocks`         | `singleLineBlocks`        | `true`, `false`           |

### Lookup Behaviour

`.editorconfig` follows standard hierarchical lookup — the formatter walks up directories, stops at `root = true`, and the nearest config wins per property. Glob patterns `[*]`, `[*.stash]`, and `[*.{stash,js}]` are all supported.

## CLI Reference

```
Usage: stash-format [OPTIONS] [FILES/DIRS...]

Arguments:
  FILES/DIRS...              One or more .stash files or directories (default: .)

Output Modes:
  -w, --write                Format files in place (overwrite)
  -c, --check                Exit 1 if any file needs formatting (CI mode)
  -d, --diff                 Print unified diff of changes

Style Options:
  -i, --indent-size <N>      Spaces per indent level (default: 2)
  -t, --use-tabs             Use tabs instead of spaces
  -pw, --print-width <N>     Target line width (default: 80)
  -tc, --trailing-comma <S>  Trailing commas: none|all (default: none)
  -eol, --end-of-line <S>    Line endings: lf|crlf|auto (default: lf)
  -bs, --bracket-spacing <B> Spaces inside {}: true|false (default: true)
  -si, --sort-imports        Sort import statements alphabetically
  -blb, --blank-lines-between-blocks <N>  Blank lines between declarations: 1|2 (default: 1)
  -slb, --single-line-blocks Allow single-line function/block bodies

File Selection:
  -e, --exclude <GLOB>       Exclude files matching glob (repeatable)
  -cfg, --config <FILE>      Path to explicit .stashformat config file

Range Formatting:
  --range-start <N>          Format only from line N (1-based)
  --range-end <N>            Format only up to line N (1-based)

Other:
  -h, --help                 Print help and exit
  -v, --version              Print version and exit
```

### Exit Codes

| Code | Meaning                                                |
| ---- | ------------------------------------------------------ |
| `0`  | Success — no errors, no changes needed                 |
| `1`  | `--check` mode: one or more files need formatting      |
| `2`  | Error — invalid arguments, parse failure, or I/O error |

## Output Modes

### Default (stdout)

Without flags, formatted output is printed to stdout. Useful for piping or preview:

```bash
stash-format script.stash          # Print formatted output
stash-format script.stash | less   # Preview with pager
```

### Write (`--write`)

Formats files in place, overwriting them. Reports a summary:

```bash
$ stash-format --write src/
Formatted 42 files (3 changed)
```

### Check (`--check`)

Returns exit code 1 if any file differs from formatted output. Useful in CI:

```bash
# In CI pipeline
stash-format --check . || echo "Code is not formatted"
```

### Diff (`--diff`)

Shows what would change as a unified diff:

```bash
$ stash-format --diff script.stash
--- script.stash
+++ script.stash (formatted)
@@ -3,4 +3,4 @@
-let x=1
+let x = 1
```

## Ignore Directives

### Skip a Line

Add `// stash-ignore format` to any line to preserve it exactly as-is:

```stash
// This line stays exactly as written:
let   aligned_a   = 1   // stash-ignore format
let   aligned_b   = 2   // stash-ignore format
let   aligned_c   = 3   // stash-ignore format
```

### Skip an Entire File

Add `// stash-ignore-all format` anywhere in the file to skip formatting entirely:

```stash
// stash-ignore-all format
// This is a generated file — do not format

let data = { very:    "specific",   formatting:    "here" }
```

### Exclude Files via CLI

Use `--exclude` with glob patterns to skip files:

```bash
# Exclude generated files and vendor code
stash-format --write --exclude "**/*.generated.stash" --exclude "vendor/**" .
```

## Range Formatting

Format only a specific line range within a file, leaving the rest untouched:

```bash
# Format lines 10 through 25
stash-format --range-start 10 --range-end 25 script.stash
```

This is used by the LSP for editor "Format Selection" support.

## Stdin Support

Pipe source code directly to the formatter:

```bash
# Format from stdin, output to stdout
cat script.stash | stash-format

# Check formatting via stdin
echo 'let x=1' | stash-format --check
```

> Note: `--write` cannot be used with stdin (there is no file to write to).

## Architecture

### Wadler-Lindig Document IR

The formatter uses an industry-standard intermediate representation (IR) based on the Wadler-Lindig pretty-printing algorithm — the same model used by Prettier, Biome, and rustfmt. The process:

1. **Parse** — Stash source is lexed and parsed into an AST
2. **Emit** — The AST visitor emits a tree of `Doc` nodes (Text, Line, Indent, Group, Fill, etc.)
3. **Render** — The `DocPrinter` performs fit-checking against the `printWidth` and decides where to break lines

This architecture enables intelligent decisions like "keep this function call on one line if it fits, otherwise break after each argument."

### Doc IR Node Types

| Node         | Purpose                                                    |
| ------------ | ---------------------------------------------------------- |
| `Text`       | Literal text (tokens, operators)                           |
| `Line`       | Line break or space (depending on flat/break mode)         |
| `SoftLine`   | Line break or nothing (in flat mode)                       |
| `HardLine`   | Always a line break                                        |
| `Indent`     | Increase indent level for enclosed content                 |
| `Dedent`     | Decrease indent level for enclosed content                 |
| `Group`      | Try to fit contents on one line; break if it doesn't fit   |
| `Fill`       | Pack as many items per line as possible (greedy)           |
| `IfBreak`    | Choose between two alternatives based on break mode        |
| `LineSuffix` | Append content after the line (used for trailing comments) |
| `Concat`     | Concatenate multiple documents                             |

### Trivia Preservation

Comments and whitespace (trivia) are preserved through formatting:

- Single-line comments (`//`) are emitted at their original positions
- Block comments (`/* */`) are preserved verbatim
- Doc comments (`///`) are kept attached to their declarations
- Blank lines between statements are preserved (up to one)

## Integration

### VS Code Extension

The Stash VS Code extension integrates the formatter via the Language Server Protocol:

- **Format Document** (`Shift+Alt+F`) — formats the entire file
- **Format Selection** — formats only the selected range
- **Format on Save** — auto-formats when saving (if enabled)

### CI Pipeline

```yaml
# GitHub Actions example
- name: Check formatting
  run: stash-format --check .
```

```yaml
# GitLab CI example
format:
  script:
    - stash-format --check .
```

### Pre-commit Hook

```bash
#!/bin/sh
# .git/hooks/pre-commit
stash-format --check $(git diff --cached --name-only --diff-filter=ACM -- '*.stash')
```
