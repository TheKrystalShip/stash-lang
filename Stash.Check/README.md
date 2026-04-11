# stash-check

Static analysis CLI for the Stash programming language.

## Overview

`stash-check` is a standalone Native AOT CLI tool that performs static analysis on Stash source files. It ships 63 diagnostic rules across 14 categories and is designed for use in editors, CI pipelines, and local development workflows.

Key capabilities:

- **63 diagnostic rules** across 14 categories (control flow, declarations, type safety, functions, security, performance, and more)
- **Autofix support** — safe and unsafe fixes with diff preview
- **5 output formats** — `text`, `grouped`, `json`, `github`, `sarif`
- **Hierarchical `.stashcheck` config** — walks up the directory tree and merges configs root-first
- **CI/CD integration** — GitHub Actions annotations, SARIF upload, structured exit codes
- **Watch mode** — re-analyzes on file changes with 300ms debounce

## Build

Requires the .NET 10 SDK.

```bash
# Build (development)
dotnet build Stash.Check/

# Publish native binary (release)
dotnet publish Stash.Check/ -c Release
```

The publish command produces a self-contained native binary via Native AOT — no .NET runtime is required on the target machine.

## Usage

```
Usage: stash-check [OPTIONS] [FILES/DIRS...]

Runs Stash static analysis and outputs diagnostics.

Arguments:
  FILES/DIRS...              One or more .stash files or directories (default: .)
  -                          Read source from stdin (requires --stdin-filename)

Options:
  --format <fmt>             Output format: text, sarif, json, github, grouped (default: text)
  --output <path>            Write output to a file instead of stdout
  --exclude <glob>           Glob pattern to exclude (repeatable)
  --severity <level>         Minimum severity: error, warning, information (default: information)
  --no-imports               Disable cross-file import resolution
  --fix                      Apply safe fixes in-place
  --unsafe-fixes             Apply safe and unsafe fixes in-place (implies --fix)
  --diff                     Show fixes as unified diff without applying
  --statistics               Show summary of diagnostics by rule
  --show-files               List files that would be analyzed
  --select <codes>           Only report these codes/prefixes (comma-separated, e.g. SA0201,SA03)
  --ignore <codes>           Suppress these codes/prefixes (comma-separated, e.g. SA0201,SA03)
  --add-suppress             Insert suppression comments for all current diagnostics in-place
  --reason <text>            Reason text appended to auto-inserted suppression comments
  --stdin-filename <file>    Virtual filename for stdin diagnostics (used with -)
  --watch                    Watch for file changes and re-analyze
  --timing                   Print pass timing breakdown
  --generate-docs <dir>      Generate rule documentation pages into <dir> and exit
  --version                  Print version and exit
  --help, -h                 Print this help and exit
```

## Output Formats

### `text` (default)

One diagnostic per line, suitable for terminal output and editor integration:

```
src/main.stash:10:5: SA0201 [Information] Unused declaration 'x'
src/main.stash:24:1: SA0802 [Warning] Unused import 'fs'
src/lib.stash:8:3: SA0401 [Error] Function 'greet' expects 2 arguments, got 1
```

### `grouped`

Groups diagnostics by file with decorative headers — useful for human review:

```
src/main.stash
  10:5  SA0201 [Information] Unused declaration 'x'
  24:1  SA0802 [Warning] Unused import 'fs'

src/lib.stash
  8:3   SA0401 [Error] Function 'greet' expects 2 arguments, got 1
```

### `json`

JSON array of diagnostic objects, suitable for tool integration:

```json
[
  {
    "file": "src/main.stash",
    "line": 10,
    "column": 5,
    "code": "SA0201",
    "level": "information",
    "message": "Unused declaration 'x'"
  }
]
```

### `github`

GitHub Actions workflow command format — produces inline annotations in pull request diffs:

```
::warning file=src/main.stash,line=10,col=5::SA0201: Unused declaration 'x'
::warning file=src/main.stash,line=24,col=1::SA0802: Unused import 'fs'
::error file=src/lib.stash,line=8,col=3::SA0401: Function 'greet' expects 2 arguments, got 1
```

### `sarif`

SARIF v2.1.0 — industry-standard format for static analysis results. Compatible with GitHub Code Scanning, Azure DevOps, and any SARIF-aware tool.

## Configuration

Place a `.stashcheck` file in your project root (or any directory). `stash-check` walks up the directory tree from each analyzed file and merges all `.stashcheck` files it encounters. The root-most file is applied first; child configs override parents.

### Key Directives

| Directive                           | Description                                                          |
| ----------------------------------- | -------------------------------------------------------------------- |
| `preset = <name>`                   | Base rule preset: `recommended`, `strict`, or `minimal`              |
| `extend = <path>`                   | Inherit from another `.stashcheck` file (path relative to this file) |
| `disable = <codes>`                 | Comma-separated codes or prefixes to silence                         |
| `enable = <codes>`                  | Comma-separated codes or prefixes to enable                          |
| `require-suppression-reason = true` | Require a reason on all inline suppression comments                  |
| `severity.<CODE> = <level>`         | Override severity for a rule: `error`, `warning`, `info`, `off`      |
| `options.<CODE>.<key> = <value>`    | Rule-specific option (e.g., complexity thresholds)                   |

### Per-File Overrides

Disable specific rules for files matching a glob pattern:

```
[per-file-overrides]
"tests/**/*.stash" = disable SA0201, SA0206
"*.test.stash" = disable ALL
"generated/**" = disable ALL
```

### Domains

Assign analysis profiles (`recommended`, `strict`, `off`) to named rule groups:

```
[domains]
security = strict
performance = recommended
style = off
```

See [.stashcheck.example](.stashcheck.example) for a fully commented example.

## Diagnostic Rules

63 rules across 14 categories. In the **Fix** column: **Safe** = auto-applicable without semantic risk; **Unsafe** = fix may change semantics; **—** = no autofix available.

| Code   | Title                                              | Level       | Category       | Fix    |
| ------ | -------------------------------------------------- | ----------- | -------------- | ------ |
| SA0001 | Unknown diagnostic code in suppression directive   | Warning     | Infrastructure | —      |
| SA0002 | Malformed diagnostic code in suppression directive | Warning     | Infrastructure | —      |
| SA0003 | Unused suppression directive                       | Warning     | Infrastructure | —      |
| SA0101 | Break outside loop                                 | Error       | Control Flow   | —      |
| SA0102 | Continue outside loop                              | Error       | Control Flow   | —      |
| SA0103 | Return outside function                            | Error       | Control Flow   | —      |
| SA0104 | Unreachable code                                   | Information | Control Flow   | —      |
| SA0105 | Empty block body                                   | Information | Control Flow   | —      |
| SA0106 | Unreachable code after terminating branches        | Information | Control Flow   | —      |
| SA0109 | Cyclomatic complexity too high                     | Information | Control Flow   | —      |
| SA0201 | Unused declaration                                 | Information | Declarations   | —      |
| SA0202 | Undefined identifier                               | Warning     | Declarations   | —      |
| SA0203 | Constant reassignment                              | Error       | Declarations   | Unsafe |
| SA0205 | Variable could be constant                         | Information | Declarations   | Safe   |
| SA0206 | Unused parameter                                   | Information | Declarations   | —      |
| SA0207 | Shadow variable                                    | Warning     | Declarations   | —      |
| SA0208 | Dead store                                         | Information | Declarations   | —      |
| SA0209 | Naming convention violation                        | Information | Declarations   | —      |
| SA0210 | Variable used before assignment on all paths       | Warning     | Declarations   | —      |
| SA0301 | Variable type mismatch                             | Warning     | Type Safety    | —      |
| SA0302 | Constant type mismatch                             | Warning     | Type Safety    | —      |
| SA0303 | Unknown type                                       | Warning     | Type Safety    | —      |
| SA0304 | Field type mismatch                                | Warning     | Type Safety    | —      |
| SA0305 | Variable assignment type mismatch                  | Warning     | Type Safety    | —      |
| SA0308 | Possible null access                               | Warning     | Type Safety    | —      |
| SA0309 | Null access on unguarded path                      | Warning     | Type Safety    | —      |
| SA0310 | Non-exhaustive switch on enum                      | Warning     | Type Safety    | —      |
| SA0401 | User function arity mismatch                       | Error       | Functions      | —      |
| SA0402 | Built-in function arity mismatch                   | Error       | Functions      | —      |
| SA0403 | Argument type mismatch                             | Warning     | Functions      | —      |
| SA0404 | Missing return                                     | Warning     | Functions      | —      |
| SA0405 | Too many parameters                                | Information | Functions      | —      |
| SA0501 | Spread type mismatch (array context)               | Warning     | Spread / Rest  | —      |
| SA0502 | Spread type mismatch (dict context)                | Warning     | Spread / Rest  | —      |
| SA0503 | Spreading null literal                             | Warning     | Spread / Rest  | —      |
| SA0504 | Unnecessary spread of array literal                | Information | Spread / Rest  | —      |
| SA0505 | Empty spread                                       | Information | Spread / Rest  | —      |
| SA0506 | Too many arguments with spread                     | Error       | Spread / Rest  | —      |
| SA0701 | Nested elevate                                     | Warning     | Commands       | —      |
| SA0702 | Retry shell commands only                          | Warning     | Commands       | —      |
| SA0703 | Retry zero attempts                                | Warning     | Commands       | —      |
| SA0704 | Retry single attempt                               | Information | Commands       | —      |
| SA0705 | Invalid on filter                                  | Warning     | Commands       | —      |
| SA0706 | Invalid on option                                  | Warning     | Commands       | —      |
| SA0707 | Invalid until clause                               | Warning     | Commands       | —      |
| SA0708 | Backoff without delay                              | Information | Commands       | —      |
| SA0709 | Retry no throwable operations                      | Information | Commands       | —      |
| SA0801 | Dynamic import path                                | Information | Imports        | —      |
| SA0802 | Unused import                                      | Warning     | Imports        | Safe   |
| SA0804 | Import statements not in canonical order           | Information | Imports        | Safe   |
| SA0901 | Unnecessary else after return                      | Information | Style          | —      |
| SA1002 | Nesting depth too high                             | Information | Complexity     | —      |
| SA1102 | Self-assignment                                    | Warning     | Best Practices | Safe   |
| SA1103 | Duplicate case value                               | Warning     | Best Practices | —      |
| SA1105 | Unnecessary block statement                        | Information | Best Practices | —      |
| SA1106 | Self-comparison                                    | Warning     | Best Practices | —      |
| SA1107 | Constant condition                                 | Warning     | Best Practices | —      |
| SA1108 | Unreachable loop                                   | Warning     | Best Practices | —      |
| SA1201 | Accumulating spread in loop                        | Warning     | Performance    | —      |
| SA1301 | Hardcoded credentials                              | Warning     | Security       | —      |
| SA1302 | Unsafe command interpolation                       | Warning     | Security       | —      |
| SA1401 | Use optional chaining                              | Information | Suggestions    | Unsafe |
| SA1402 | Use null coalescing                                | Information | Suggestions    | Unsafe |

## Autofixes

Use `--fix` to apply safe fixes in-place. Use `--unsafe-fixes` to also apply fixes that may change semantics (implies `--fix`). Use `--diff` to preview any fixes as a unified diff without modifying files.

| Code   | Fix Kind | Description                        |
| ------ | -------- | ---------------------------------- |
| SA0205 | Safe     | Convert `let` to `const`           |
| SA0802 | Safe     | Remove unused import               |
| SA0804 | Safe     | Reorder imports to canonical order |
| SA1102 | Safe     | Remove self-assignment             |
| SA0203 | Unsafe   | Convert `const` to `let`           |
| SA1401 | Unsafe   | Rewrite to optional chaining       |
| SA1402 | Unsafe   | Rewrite to null coalescing         |

## Inline Suppressions

Suppress diagnostics on a per-line basis using comments directly in source:

```stash
// stash-disable-next-line SA0201
let x = compute()

// stash-disable-next-line SA0201, SA0206
fn process(unused) { }

// stash-disable-next-line SA0201 — not used yet, reserved for Phase 2
let placeholder = null
```

Use `--add-suppress` to automatically insert suppression comments for every current diagnostic in a file. Combine with `--reason` to include a reason in each comment:

```bash
stash-check --add-suppress --reason "legacy code, tracked in issue #42" src/
```

Set `require-suppression-reason = true` in `.stashcheck` to enforce reasons on all suppression comments across the project.

## CI/CD Integration

### GitHub Actions — Annotations

```yaml
- name: Run stash-check
  run: stash-check --format github --severity warning .
```

Diagnostics appear as inline pull request annotations.

### GitHub Actions — SARIF Upload

```yaml
- name: Run stash-check
  run: stash-check --format sarif --output results.sarif .

- name: Upload SARIF
  uses: github/codeql-action/upload-sarif@v3
  with:
    sarif_file: results.sarif
```

Results appear in the repository's Security > Code Scanning tab.

### Generic CI

```bash
stash-check --severity warning .
echo "Exit code: $?"
```

Exit codes:

| Code | Meaning                                            |
| ---- | -------------------------------------------------- |
| 0    | No diagnostics at or above the severity threshold  |
| 1    | One or more diagnostics found                      |
| 2    | Runtime error (parse failure, missing files, etc.) |

## Watch Mode

```bash
stash-check --watch src/
```

Watches for `.stash` file changes and re-analyzes automatically with a 300ms debounce. Press Ctrl+C to stop.

## Exit Codes

| Code | Meaning                                                                 |
| ---- | ----------------------------------------------------------------------- |
| 0    | No diagnostics at or above the severity threshold                       |
| 1    | One or more diagnostics found                                           |
| 2    | Runtime error (parse failure, missing files, configuration error, etc.) |

## Architecture

`stash-check` is a thin CLI wrapper over the `Stash.Analysis` engine. Rules are implemented in `Stash.Analysis/Rules/` using a visitor-based architecture with `IPerNodeRule` and `IPerFileRule` interfaces. The CLI handles argument parsing, config loading, output formatting, fix application, and watch mode; all diagnostic logic lives in `Stash.Analysis`.
