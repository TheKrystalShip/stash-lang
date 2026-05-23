# Stash-Format — Standalone Code Formatter CLI

**Status:** Backlog — Architecture Analysis
**Created:** 2026-04-04
**Related:** [Stash-Check — Standalone Static Analysis CLI](../3-review/Stash-Check%20—%20Standalone%20Static%20Analysis%20CLI.md)

---

## 1. Motivation

The Stash code formatter (`StashFormatter`) currently has two consumers: the LSP (document/range/on-type formatting handlers) and the Playground (browser-based formatting button). There is no way to invoke formatting from the command line.

A standalone `stash-format` binary would enable:

- **CI/CD enforcement** — fail pipelines when code is unformatted (`stash-format --check`)
- **Pre-commit hooks** — format files before commit (`stash-format --write .`)
- **Editor-agnostic usage** — pipe through stdin/stdout for any editor integration
- **Batch formatting** — format entire projects in one command
- **Consistency** — same formatter binary everywhere, no LSP server required

This follows the same pattern as `stash-check` for static analysis.

---

## 2. Architecture Analysis

### 2.1 Current State (What Exists)

| Component        | Location                                                                                             | Dependencies                    |
| ---------------- | ---------------------------------------------------------------------------------------------------- | ------------------------------- |
| `StashFormatter` | `Stash.Analysis/Visitors/StashFormatter.cs`                                                          | Stash.Core (Lexer, Parser, AST) |
| LSP handlers     | `Stash.Lsp/Handlers/FormattingHandler.cs`, `OnTypeFormattingHandler.cs`, `RangeFormattingHandler.cs` | Stash.Analysis                  |
| Playground       | `Stash.Playground/Services/PlaygroundAnalyzer.cs`                                                    | Stash.Analysis                  |
| Tests            | `Stash.Tests/Analysis/FormatterTests.cs`                                                             | Stash.Analysis                  |

**Key finding:** `StashFormatter` is already in `Stash.Analysis`, NOT in the interpreter. It depends only on `Stash.Core` types (Lexer, Parser, AST nodes). No refactoring of the formatter class is required.

### 2.2 `StashFormatter` Public API

```csharp
public class StashFormatter : IStmtVisitor<int>, IExprVisitor<int>
{
    public StashFormatter(int indentSize = 2, bool useTabs = false)
    public string Format(string source)
}
```

- Takes source text, returns formatted source text
- Document-level only (no range support at this layer)
- Lexes with `preserveTrivia: true` to preserve comments
- Separates code tokens from trivia, parses code-only AST, walks with visitor, interleaves trivia
- Uses max-upgrade whitespace model: `None < Space < NewLine < BlankLine`

### 2.3 Dependency Graph for New Project

```
Stash.Format
  └── Stash.Analysis          (StashFormatter)
        ├── Stash.Core         (Lexer, Parser, AST)
        └── Stash.Stdlib       (metadata registry — transitive)
```

This is **identical** to the `Stash.Check` dependency graph. The new project references only `Stash.Analysis`.

### 2.4 Refactoring Assessment

| Question                                                              | Answer                                                                                 |
| --------------------------------------------------------------------- | -------------------------------------------------------------------------------------- |
| Does `StashFormatter` need to be extracted from its current location? | **No.** Already in `Stash.Analysis`, which is the right layer.                         |
| Does it depend on the interpreter?                                    | **No.** Only Core types.                                                               |
| Is it AOT-compatible?                                                 | **Yes.** No reflection, no dynamic dispatch. Uses `StringBuilder` and visitor pattern. |
| Does existing formatter behavior need to change?                      | **No.** The existing `Format(string)` API is exactly what a CLI needs.                 |
| Do LSP/Playground consumers break?                                    | **No.** Nothing moves — the formatter stays where it is.                               |

**Verdict: Zero refactoring needed.** This is a pure "wrap in CLI" task.

---

## 3. CLI Interface Design

### 3.1 Command Syntax

```
stash-format [OPTIONS] [FILES/DIRS...]
```

When no files/dirs are given, reads from stdin and writes to stdout (pipe-friendly).

### 3.2 Options

| Flag                | Short | Default | Description                                 |
| ------------------- | ----- | ------- | ------------------------------------------- |
| `--write`           | `-w`  | off     | Format files in place (overwrite)           |
| `--check`           | `-c`  | off     | Exit 1 if any file is unformatted (CI mode) |
| `--diff`            | `-d`  | off     | Print unified diff of changes to stdout     |
| `--indent-size <N>` | `-i`  | `2`     | Number of spaces per indent level           |
| `--use-tabs`        | `-t`  | off     | Use tabs instead of spaces                  |
| `--exclude <GLOB>`  | `-e`  | none    | Exclude files matching glob (repeatable)    |
| `--help`            | `-h`  | —       | Show usage                                  |
| `--version`         | `-v`  | —       | Show version                                |

### 3.3 Modes of Operation

**Default (stdout):** Format each file, print result to stdout. Useful for piping.

```bash
stash-format script.stash              # print formatted to stdout
cat script.stash | stash-format        # stdin → stdout
```

**Write mode (`--write`):** Overwrite files in place. Skip files that are already correctly formatted (no unnecessary writes/timestamp changes).

```bash
stash-format --write src/              # format all .stash files under src/
stash-format -w *.stash               # format matching files
```

**Check mode (`--check`):** Report which files would change, exit 1 if any. For CI.

```bash
stash-format --check .                 # exit 0 if all formatted, 1 if not
```

**Diff mode (`--diff`):** Show what would change in unified diff format. Can combine with `--check`.

```bash
stash-format --diff src/               # show diffs without modifying
stash-format --check --diff .          # show diffs AND exit 1
```

### 3.4 Exit Codes

| Code | Meaning                                                            |
| ---- | ------------------------------------------------------------------ |
| 0    | Success — all files formatted (or no changes needed in check mode) |
| 1    | Check mode — one or more files need formatting                     |
| 2    | Error — invalid args, file not found, parse failure                |

### 3.5 Output Behavior

- **Default/write mode:** Print summary to stderr: `Formatted 5 files (3 changed)` or `All 5 files already formatted`
- **Check mode:** Print list of unformatted files to stdout (one per line), summary to stderr
- **Diff mode:** Print unified diffs to stdout
- **Errors:** Parse failures are non-fatal per-file — report to stderr, continue with next file, exit 2 at end

### 3.6 Stdin Handling

When no file arguments are provided:

- Read source from stdin
- Format it
- Write to stdout
- `--write` is an error with stdin (can't write back to a pipe)
- `--check` exits 0 or 1 based on whether input changed

---

## 4. Project Structure

```
Stash.Format/
├── Stash.Format.csproj
├── Program.cs                   # Entry point, arg parsing
├── FormatRunner.cs              # File discovery, formatting orchestration
└── Models/
    └── FormatOptions.cs         # CLI argument model
```

### 4.1 `Stash.Format.csproj`

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <AssemblyName>StashFormat</AssemblyName>
    <RootNamespace>Stash.Format</RootNamespace>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <PublishAot>true</PublishAot>
    <InvariantGlobalization>true</InvariantGlobalization>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\Stash.Analysis\Stash.Analysis.csproj" />
  </ItemGroup>

  <ItemGroup>
    <InternalsVisibleTo Include="Stash.Tests" />
  </ItemGroup>
</Project>
```

Mirrors `Stash.Check.csproj` — Native AOT, single project reference.

### 4.2 Key Classes

**`FormatOptions`** — Hand-rolled arg parser (same pattern as `CheckOptions`):

- `Write`, `Check`, `Diff` booleans
- `IndentSize` int (default 2)
- `UseTabs` bool (default false)
- `ExcludeGlobs` list
- `Paths` list
- `ShowHelp`, `ShowVersion`

**`FormatRunner`** — Orchestrates formatting:

- Discovers `.stash` files from paths/directories (reuse same glob logic as `CheckRunner`)
- Reads each file, calls `StashFormatter.Format()`, compares output to input
- Returns per-file results (changed/unchanged/error)

**`Program.Main()`** — Thin entry point:

- Parse args → `FormatOptions`
- Validate (e.g., `--write` + stdin = error)
- Construct `FormatRunner` or handle stdin
- Execute and produce output per mode
- Return exit code

---

## 5. Design Decisions

### Decision 1: Separate project vs. subcommand of `stash`

**Chosen:** Separate `stash-format` binary (new `Stash.Format` project).

**Alternatives considered:**

- `stash format` subcommand in `Stash.Cli` — would require Stash.Cli to reference Stash.Analysis, bloating the main binary with analysis + stdlib metadata it doesn't currently need. Also breaks the clean separation where `stash` = interpreter, standalone tools = specific functions.
- `stash-check format` subcommand — conceptually wrong; checking and formatting are different tools.

**Rationale:** Follows the established `stash-check` precedent. Keeps each binary focused. Allows independent versioning. Minimal binary size since it doesn't pull in the interpreter.

**Risk:** Tool proliferation (stash, stash-check, stash-format, ...). Acceptable — this is standard practice (cf. `rustfmt`, `gofmt`, `prettier`, `black`).

### Decision 2: Default to stdout (not in-place writes)

**Chosen:** Default mode prints to stdout. Explicit `--write` required for in-place modification.

**Alternatives considered:**

- Default to `--write` like `gofmt -w` — too dangerous for a default; silent file modification without explicit opt-in.
- Default to `--check` — not useful; users typically want to see formatted output.

**Rationale:** Principle of least surprise. Stdout is safe, composable, and pipe-friendly. Matches `prettier --write` (explicit), `rustfmt` (writes by default but warns). Since Stash targets sysadmins who are pipeline-oriented, stdout-first is the right default.

### Decision 3: Hand-rolled arg parsing (no library)

**Chosen:** Hand-rolled, same as `Stash.Check`.

**Rationale:** AOT compatibility, zero dependencies, full control. The option set is small enough that a library adds more complexity than it saves.

### Decision 4: Parse errors are non-fatal per file

**Chosen:** If a file fails to parse, report the error to stderr, skip that file, continue processing remaining files. Exit code 2 at the end.

**Rationale:** Batch formatting shouldn't abort on one broken file. Users may want to format the files that ARE parseable while fixing the broken one separately. Same approach as `prettier` and `black`.

### Decision 5: No config file (Phase 1)

**Chosen:** CLI flags only. No `.stashformat`, `stash.toml`, or similar config file in Phase 1.

**Rationale:** Keep initial scope minimal. IndentSize and UseTabs are the only settings. A config file becomes valuable when there are more options (max line length, brace style, etc.). Defer until demand exists.

**Trigger for reversal:** If teams frequently need project-wide formatting settings, add config file support in Phase 2.

---

## 6. Interaction with Existing Features

### 6.1 LSP

No impact. LSP handlers already instantiate `StashFormatter` directly from `Stash.Analysis`. Nothing changes.

### 6.2 Playground

No impact. Same as LSP — direct `StashFormatter` usage.

### 6.3 VS Code Extension

Potential integration point (Phase 2): the extension could invoke `stash-format` as an external formatter via `"editor.formatOnSave"` → external tool, as an alternative to the LSP-based formatting. Low priority since LSP formatting already works.

### 6.4 CI/CD

Primary new integration. GitHub Actions example:

```yaml
- name: Check formatting
  run: stash-format --check .
```

### 6.5 Pre-commit Hooks

```bash
#!/bin/sh
stash-format --check $(git diff --cached --name-only --diff-filter=d -- '*.stash')
```

---

## 7. Cross-Platform Considerations

| Concern         | Resolution                                                                                                    |
| --------------- | ------------------------------------------------------------------------------------------------------------- |
| Line endings    | `StashFormatter` produces `\n`. On Windows, `--write` should write `\n` (not `\r\n`) to maintain consistency. |
| Path separators | File discovery uses `Directory.EnumerateFiles()` which handles platform separators.                           |
| Stdin detection | `Console.IsInputRedirected` works cross-platform on .NET.                                                     |
| Glob patterns   | Same glob matching as `Stash.Check` (hand-rolled or `FileSystemGlobbing`).                                    |

---

## 8. Build & Deployment

### 8.1 Build Scripts

Update `build.sh`, `build.ps1`, and `build.stash` to publish `stash-format` alongside `stash` and `stash-check`:

```bash
dotnet publish Stash.Format/ -c Release -r linux-x64
```

Binary size estimate: comparable to `stash-check` (~40–60% of `stash`) since the dependency graph is identical.

### 8.2 Solution Integration

Add `Stash.Format/Stash.Format.csproj` to `Stash.sln`.

---

## 9. Test Scenarios

### 9.1 CLI Argument Parsing

| #   | Test                                | Expected                                     |
| --- | ----------------------------------- | -------------------------------------------- |
| 1   | No args, stdin has content          | Format stdin → stdout, exit 0                |
| 2   | `--help`                            | Print usage, exit 0                          |
| 3   | `--version`                         | Print version, exit 0                        |
| 4   | `script.stash`                      | Format file → stdout, exit 0                 |
| 5   | `--write script.stash`              | Overwrite file, exit 0                       |
| 6   | `--write` (no files, stdin)         | Error: cannot use --write with stdin, exit 2 |
| 7   | `--check .` — all formatted         | Exit 0                                       |
| 8   | `--check .` — some unformatted      | List unformatted files, exit 1               |
| 9   | `--diff script.stash`               | Print unified diff, exit 0                   |
| 10  | `--check --diff .`                  | Print diffs AND exit 1                       |
| 11  | `--indent-size 4 script.stash`      | Format with 4-space indent                   |
| 12  | `--use-tabs script.stash`           | Format with tabs                             |
| 13  | `--exclude "vendor/*" .`            | Skip vendor directory                        |
| 14  | Non-existent file                   | Error message, exit 2                        |
| 15  | Unparseable file                    | Error to stderr, skip, exit 2                |
| 16  | `--write` on already-formatted file | No write (file timestamp unchanged)          |
| 17  | Multiple files, one fails to parse  | Format others, report error, exit 2          |
| 18  | Empty directory                     | No output, exit 0                            |

### 9.2 Formatting Correctness

Formatting correctness is already covered by `FormatterTests.cs`. The `Stash.Format` tests focus on CLI behavior, not formatter logic.

---

## 10. Deferred Work (Phase 2+)

- **Config file** (`.stashformat.toml` or section in `stash.toml`) for project-wide settings
- **Max line length** and line-wrapping rules (requires `StashFormatter` enhancement)
- **Range formatting** via line number flags (`--lines 10:20`)
- **Watch mode** (`stash-format --watch .`)
- **VS Code "Format on Save" integration** via external tool
- **JSON/machine-readable output** for `--check` mode (file list as JSON)

---

## 11. Implementation Effort Estimate

This is a **small-scope, low-risk** task because:

1. **No formatter refactoring** — `StashFormatter` is already in the right place with the right API
2. **Proven pattern** — `Stash.Check` provides an exact template for project structure, arg parsing, file discovery, build integration, and testing
3. **Minimal surface area** — ~4 files, ~300–400 lines of new code
4. **No new dependencies** — same dependency graph as `Stash.Check`

The file discovery and glob exclusion logic from `CheckRunner` can be largely reused or extracted into a shared utility (though for Phase 1, copying the pattern is acceptable to avoid coupling the two tools).
