# Stash-Check — Standalone Static Analysis CLI

> **Status:** Draft
> **Created:** April 2026
> **Purpose:** Ship a standalone `stash-check` binary that runs the existing `AnalysisEngine` from the command line, outputting diagnostics in SARIF v2.1.0 format for CI/CD integration.

---

## Table of Contents

1. [Motivation](#1-motivation)
2. [Architecture](#2-architecture)
3. [CLI Interface](#3-cli-interface)
4. [SARIF Output Mapping](#4-sarif-output-mapping)
5. [Project Structure & Build](#5-project-structure--build)
6. [Cross-Platform Behavior](#6-cross-platform-behavior)
7. [Implementation Strategy](#7-implementation-strategy)
8. [Test Scenarios](#8-test-scenarios)
9. [Deferred Work](#9-deferred-work)
10. [Decision Log](#10-decision-log)

---

## 1. Motivation

### 1.1 The Gap

The `AnalysisEngine` in `Stash.Analysis` provides 31 diagnostic rules across 7 categories (control flow, declarations, type safety, functions & calls, spread/rest, commands, imports). Today it is only accessible through the LSP — there is no way to run static analysis from the command line.

This means:

- **CI/CD pipelines** cannot lint Stash code without starting a language server
- **Pre-commit hooks** have no lightweight way to check for issues
- **Headless environments** (Docker builds, SSH sessions without VS Code) have no analysis tooling
- **GitHub Code Scanning** cannot receive Stash diagnostics (it consumes SARIF)

### 1.2 Why a Separate Binary

The analysis engine has **zero coupling** to the interpreter:

| Project             | References                                |
| ------------------- | ----------------------------------------- |
| `Stash.Analysis`    | `Stash.Core`, `Stash.Stdlib`              |
| `Stash.Interpreter` | `Stash.Core`, `Stash.Stdlib`, `Stash.Tpl` |

They share upstream dependencies but never reference each other. This means `stash-check` can ship without the interpreter, producing a smaller binary focused on a single task.

### 1.3 Why Not Embed in the CLI

The CLI (`Stash.Cli`) references `Stash.Interpreter` and `Stash.Tap` — it is an execution environment. Adding analysis to it would pull `Stash.Analysis` and `Stash.Stdlib` into the CLI's dependency graph, increasing binary size for a feature that most script executions don't need. A separate binary follows the Unix philosophy: do one thing well.

**Decision:** Standalone binary. Not embedded in the CLI.
**Alternatives rejected:** Flag on `stash` CLI (`stash --check`), analysis-only mode in LSP (`stash-lsp --check`).
**Rationale:** Smallest binary, cleanest dependency graph, no runtime baggage from the interpreter or OmniSharp.

---

## 2. Architecture

### 2.1 Dependency Graph

```
Stash.Check (new)
  ├── Stash.Analysis
  │     ├── Stash.Core       (Lexer, Parser, AST, SourceSpan)
  │     └── Stash.Stdlib     (StdlibRegistry — metadata only)
  └── (no Interpreter, no OmniSharp, no Tap)
```

### 2.2 Component Responsibilities

```
┌──────────────────────────────────────────────────┐
│                  Stash.Check                     │
│                                                  │
│  Program.cs          — CLI entry point, arg      │
│                        parsing, exit codes       │
│                                                  │
│  CheckRunner.cs      — Orchestrates analysis     │
│                        across files/directories  │
│                                                  │
│  IOutputFormatter    — Formatter interface       │
│  SarifFormatter.cs   — SARIF v2.1.0 output       │
│                                                  │
└──────────────────────┬───────────────────────────┘
                       │ calls
                       ▼
              AnalysisEngine.Analyze(uri, source)
                       │ returns
                       ▼
                 AnalysisResult
                   ├── SemanticDiagnostics
                   ├── StructuredLexErrors
                   └── StructuredParseErrors
```

### 2.3 Output Formatter Interface

The binary ships with SARIF only, but the architecture uses an interface to allow future formats:

```csharp
public interface IOutputFormatter
{
    string Format { get; }           // e.g. "sarif"
    void Write(CheckResult result, Stream output);
}
```

`CheckResult` is a thin wrapper aggregating results across all analyzed files:

```csharp
public record CheckResult(
    IReadOnlyList<FileResult> Files,
    int TotalErrors,
    int TotalWarnings,
    int TotalInformation
);

public record FileResult(
    Uri Uri,
    AnalysisResult Analysis
);
```

**Decision:** Interface-based formatter from day one.
**Alternatives rejected:** Direct SARIF writing in `Program.cs` (no extension point), plugin system (over-engineered for now).
**Rationale:** Minimal cost to add the interface; future formats (text, JSON, GitHub Actions annotations) just implement `IOutputFormatter`.

---

## 3. CLI Interface

### 3.1 Usage

```
stash-check [OPTIONS] [FILES/DIRS...]
```

### 3.2 Arguments

| Argument        | Description                                                                                                                                           |
| --------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------- |
| `FILES/DIRS...` | One or more `.stash` files or directories. Directories are searched recursively for `*.stash` files. If omitted, defaults to current directory (`.`). |

### 3.3 Options

| Option               | Type     | Default       | Description                                                                                  |
| -------------------- | -------- | ------------- | -------------------------------------------------------------------------------------------- |
| `--format <fmt>`     | string   | `sarif`       | Output format. Only `sarif` is supported initially.                                          |
| `--output <path>`    | string   | stdout        | Write output to a file instead of stdout.                                                    |
| `--exclude <glob>`   | string[] | —             | Glob patterns for files/directories to exclude (e.g. `**/node_modules/**`). Can be repeated. |
| `--severity <level>` | string   | `information` | Minimum severity to report: `error`, `warning`, or `information`.                            |
| `--no-imports`       | flag     | false         | Disable cross-file import resolution (analyze each file in isolation).                       |
| `--version`          | flag     | —             | Print version and exit.                                                                      |
| `--help`             | flag     | —             | Print usage and exit.                                                                        |

### 3.4 Exit Codes

| Code | Meaning                                                        |
| ---- | -------------------------------------------------------------- |
| `0`  | No diagnostics at or above the minimum severity                |
| `1`  | One or more diagnostics found at or above the minimum severity |
| `2`  | Invalid arguments, file not found, or internal error           |

### 3.5 Examples

```bash
# Analyze all .stash files in the current directory, SARIF to stdout
stash-check

# Analyze a specific file, write SARIF to a file
stash-check --output results.sarif src/deploy.stash

# Analyze a directory, only report errors and warnings
stash-check --severity warning src/

# Analyze everything except test files
stash-check --exclude "**/*.test.stash" .

# CI/CD: Analyze and upload to GitHub Code Scanning
stash-check --output results.sarif src/
# Then: github/codeql-action/upload-sarif@v3
```

---

## 4. SARIF Output Mapping

### 4.1 SARIF Version

Output conforms to **SARIF v2.1.0** (OASIS Standard, JSON schema `https://raw.githubusercontent.com/oasis-tcs/sarif-spec/main/sarif-2.1/schema/sarif-schema-2.1.0.json`).

### 4.2 Top-Level Structure

```json
{
  "$schema": "https://raw.githubusercontent.com/oasis-tcs/sarif-spec/main/sarif-2.1/schema/sarif-schema-2.1.0.json",
  "version": "2.1.0",
  "runs": [
    {
      "tool": { ... },
      "results": [ ... ],
      "invocations": [ ... ]
    }
  ]
}
```

One `run` per invocation of `stash-check`. Multiple files produce multiple `result` objects within a single run.

### 4.3 Tool Object

```json
{
  "tool": {
    "driver": {
      "name": "stash-check",
      "semanticVersion": "<version>",
      "informationUri": "https://stash-lang.dev",
      "rules": [ ... ]
    }
  }
}
```

### 4.4 Rules Mapping (`DiagnosticDescriptor` → SARIF `reportingDescriptor`)

Each `DiagnosticDescriptor` from `DiagnosticDescriptors.cs` maps to a SARIF rule:

| DiagnosticDescriptor     | SARIF reportingDescriptor            |
| ------------------------ | ------------------------------------ |
| `Code` (e.g. `"SA0101"`) | `id`                                 |
| `Title`                  | `shortDescription.text`              |
| `MessageFormat`          | `messageStrings.default.text`        |
| `Category`               | `properties.category` (property bag) |
| `DefaultLevel`           | `defaultConfiguration.level`         |

**Level mapping:**

| `DiagnosticLevel` | SARIF `level` |
| ----------------- | ------------- |
| `Error`           | `"error"`     |
| `Warning`         | `"warning"`   |
| `Information`     | `"note"`      |

Example rule:

```json
{
  "id": "SA0101",
  "shortDescription": { "text": "Break outside loop" },
  "messageStrings": {
    "default": { "text": "'break' can only be used inside a loop." }
  },
  "defaultConfiguration": { "level": "error" },
  "properties": { "category": "Control flow" }
}
```

### 4.5 Results Mapping (`SemanticDiagnostic` → SARIF `result`)

| SemanticDiagnostic | SARIF result                                         |
| ------------------ | ---------------------------------------------------- |
| `Code`             | `ruleId`                                             |
| `Message`          | `message.text`                                       |
| `Level`            | `level` (same mapping as above)                      |
| `Span.File`        | `locations[0].physicalLocation.artifactLocation.uri` |
| `Span.StartLine`   | `locations[0].physicalLocation.region.startLine`     |
| `Span.StartColumn` | `locations[0].physicalLocation.region.startColumn`   |
| `Span.EndLine`     | `locations[0].physicalLocation.region.endLine`       |
| `Span.EndColumn`   | `locations[0].physicalLocation.region.endColumn`     |
| `IsUnnecessary`    | `properties.tags: ["unnecessary"]` (property bag)    |

**Note on line numbers:** `SourceSpan` uses 1-based line/column numbers. SARIF also uses 1-based. No conversion needed.

**Diagnostics without codes:** Legacy `SemanticDiagnostic` instances created without a code (using the constructor that takes only `message, level, span`) will use `ruleId: "SA0000"` with `ruleIndex` omitted — they won't reference a rule in the `rules` array.

### 4.6 Lex/Parse Errors

`AnalysisResult` also contains `StructuredLexErrors` and `StructuredParseErrors` (both `List<DiagnosticError>`). These are syntax-level errors, not semantic diagnostics. They map to SARIF results with:

- `ruleId`: `"STASH001"` for lex errors, `"STASH002"` for parse errors
- `level`: `"error"` (syntax errors are always errors)
- `kind`: `"fail"`

Two additional rules (`STASH001`, `STASH002`) are added to the driver's `rules` array for these.

### 4.7 Invocations

```json
{
  "invocations": [
    {
      "executionSuccessful": true,
      "commandLine": "stash-check --severity warning src/",
      "arguments": ["--severity", "warning", "src/"],
      "startTimeUtc": "2026-04-03T12:00:00.000Z",
      "endTimeUtc": "2026-04-03T12:00:01.234Z"
    }
  ]
}
```

`executionSuccessful` is `true` if stash-check ran without internal errors (even if it found diagnostics). It is `false` only if stash-check encountered an internal fault (e.g., OOM, unhandled exception).

### 4.8 Artifact URIs

File paths in `artifactLocation.uri` are emitted as relative URIs when the file is under the current working directory, and absolute `file://` URIs otherwise. Relative URIs use forward slashes on all platforms.

The run includes `originalUriBaseIds` with a `%SRCROOT%` entry pointing to the working directory, so consumers can resolve relative paths:

```json
{
  "originalUriBaseIds": {
    "%SRCROOT%": {
      "uri": "file:///home/user/project/"
    }
  }
}
```

Artifact URIs reference this base: `"uri": "src/deploy.stash", "uriBaseId": "%SRCROOT%"`.

---

## 5. Project Structure & Build

### 5.1 Project File: `Stash.Check/Stash.Check.csproj`

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>disable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <AssemblyName>StashCheck</AssemblyName>
    <OptimizationPreference>Speed</OptimizationPreference>
    <PublishSingleFile>true</PublishSingleFile>
    <IlcInstructionSet>native</IlcInstructionSet>
    <InvariantGlobalization>true</InvariantGlobalization>
    <EventSourceSupport>false</EventSourceSupport>
    <DebugType>embedded</DebugType>
    <PublishAot>true</PublishAot>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\Stash.Analysis\Stash.Analysis.csproj" />
  </ItemGroup>

  <ItemGroup>
    <InternalsVisibleTo Include="Stash.Tests" />
  </ItemGroup>

</Project>
```

**Key points:**

- **Native AOT** — same flags as `Stash.Cli`. Analysis + Core + Stdlib are all AOT-compatible (no reflection).
- **Only references `Stash.Analysis`** — which transitively brings in `Core` and `Stdlib`. No interpreter, no OmniSharp.
- **Assembly name `StashCheck`** — produces `StashCheck` binary (renamed to `stash-check` in build scripts).
- `ILogger<AnalysisEngine>` dependency — use `Microsoft.Extensions.Logging.Abstractions` `NullLoggerFactory` or a minimal console logger. The `Logging.Abstractions` package is already AOT-safe.

### 5.2 File Layout

```
Stash.Check/
  Stash.Check.csproj
  Program.cs              — Entry point, argument parsing
  CheckRunner.cs          — File discovery, analysis orchestration
  Formatters/
    IOutputFormatter.cs   — Formatter interface
    SarifFormatter.cs     — SARIF v2.1.0 output
  Models/
    CheckResult.cs        — Aggregated results across files
    CheckOptions.cs       — Parsed CLI options
```

### 5.3 Assembly Name → Binary Name

The .NET assembly is `StashCheck` (PascalCase, matching Stash conventions: `StashLsp`, `StashDap`). The build scripts rename it to `stash-check` when copying to the install directory. This follows the existing pattern:

| AssemblyName | Installed binary |
| ------------ | ---------------- |
| `Stash`      | `stash`          |
| `StashLsp`   | `stash-lsp`      |
| `StashDap`   | `stash-dap`      |
| `StashCheck` | `stash-check`    |

### 5.4 Build Script Changes

Add `stash-check` to `build.stash`, `build.sh`, and `build.ps1`:

```bash
# New constant in build.stash / build.sh / build.ps1
CHECK_SOURCE="./Stash.Check/bin/Release/net10.0/${RUNTIME}/publish/StashCheck"

# Copy alongside other binaries
cp "$CHECK_SOURCE" "$INSTALL_DIR/stash-check"
```

The binary participates in the existing `dotnet publish -c Release -r $RUNTIME --self-contained` solution-level build.

### 5.5 Solution File

Add `Stash.Check/Stash.Check.csproj` to `Stash.sln`.

### 5.6 Expected Binary Size

`stash-check` should be significantly smaller than `stash` (the CLI/interpreter):

- `stash` includes: Core + Stdlib + Interpreter + Tpl + Tap → full runtime
- `stash-check` includes: Core + Stdlib + Analysis → lexer, parser, analysis only (no tree-walk interpreter, no built-in function implementations, no template engine, no test framework)

Estimated ~40-60% of `stash` binary size. Exact numbers to be verified after first build.

---

## 6. Cross-Platform Behavior

### 6.1 Runtime Identifiers

Same six RIDs as other Stash binaries:

| Platform      | RID           |
| ------------- | ------------- |
| Linux x64     | `linux-x64`   |
| Linux ARM64   | `linux-arm64` |
| macOS x64     | `osx-x64`     |
| macOS ARM64   | `osx-arm64`   |
| Windows x64   | `win-x64`     |
| Windows ARM64 | `win-arm64`   |

### 6.2 Path Handling

- File paths in SARIF output always use forward slashes (SARIF convention)
- URI encoding for paths with spaces
- `SourceSpan.File` from the parser may contain OS-native separators — the SARIF formatter normalizes these

### 6.3 Glob Patterns

The `--exclude` option uses standard glob syntax. Implementation should use `Microsoft.Extensions.FileSystemGlobbing` (already AOT-compatible) or a vendored matcher. A `FileSystemGlobbing` package reference may be needed — verify AOT compatibility before adding.

### 6.4 Standard I/O

- SARIF output goes to **stdout** (or `--output` file)
- Errors and progress messages go to **stderr**
- No interactive prompts — the tool is designed for piping and scripting

---

## 7. Implementation Strategy

### 7.1 Phased Approach

**Phase 1 — Core CLI (this spec)**

1. Create `Stash.Check` project with AOT configuration
2. Implement `Program.cs` with argument parsing (no external library — keep it simple for AOT)
3. Implement `CheckRunner` — file discovery, analysis orchestration
4. Implement `SarifFormatter` — full SARIF v2.1.0 output
5. Wire into build scripts
6. Add tests

**Phase 2 — Deferred (not in this spec)**

- Text formatter (human-readable terminal output)
- JSON formatter (simplified, non-SARIF)
- GitHub Actions annotations formatter (`::error file=...`)
- VS Code extension integration (`stash.checkPath` setting)
- Configuration file (`.stash-check.json` for per-project settings)
- Suppression comment support (already exists in Analysis: `// stash-ignore SA0201`)

### 7.2 Argument Parsing

Use manual argument parsing (no third-party library). The CLI has few options and Native AOT compatibility is critical. Pattern:

```csharp
// Pseudocode
var options = CheckOptions.Parse(args);  // returns CheckOptions or prints help/error and exits
```

This avoids `System.CommandLine` (historically unstable AOT support) and keeps dependencies minimal.

**Decision:** Hand-rolled argument parser.
**Alternatives rejected:** `System.CommandLine` (AOT fragility, heavy dependency for ~6 options), `Spectre.Console.Cli` (unnecessary dependency).
**Rationale:** The CLI surface is small enough that a manual parser is simpler and more reliable than any library.

### 7.3 File Discovery

`CheckRunner` handles:

1. Expand directory arguments to `*.stash` files (recursive)
2. Apply `--exclude` globs
3. Read each file's content
4. Call `AnalysisEngine.Analyze(uri, source)` for each file
5. Collect results into `CheckResult`

Import resolution: `AnalysisEngine` already handles `import` statements via `ImportResolver`. When analyzing a file that imports another, the resolver reads the imported file from disk. This works without changes — the same code path that the LSP uses.

With `--no-imports`, the import resolver would be disabled (files analyzed in isolation). This requires a configuration flag on `AnalysisEngine` or `ImportResolver`. Check if this already exists; if not, add a minimal opt-in flag.

### 7.4 Logging

`AnalysisEngine` requires `ILogger<AnalysisEngine>`. Use `NullLoggerFactory.Instance` for production (silent). A `--verbose` flag could enable console logging to stderr in the future, but is not in scope for Phase 1.

### 7.5 SARIF Serialization

Use `System.Text.Json` (included in the .NET runtime, fully AOT-compatible). No SARIF-specific NuGet package — the format is straightforward enough to serialize directly. This avoids the `Microsoft.CodeAnalysis.Sarif` package which has complex Roslyn dependencies unsuitable for AOT.

Define a minimal set of C# records/classes mirroring the SARIF schema:

```csharp
// Stash.Check/Formatters/Sarif/
SarifLog.cs          — Root object ($schema, version, runs)
SarifRun.cs          — Run (tool, results, invocations, originalUriBaseIds)
SarifTool.cs         — Tool + ToolComponent (driver with rules)
SarifResult.cs       — Result (ruleId, level, message, locations)
SarifRule.cs         — ReportingDescriptor (id, shortDescription, defaultConfiguration)
SarifLocation.cs     — Location, PhysicalLocation, ArtifactLocation, Region
SarifInvocation.cs   — Invocation metadata
```

Use `[JsonPropertyName]` attributes and `JsonSerializerOptions { WriteIndented = true, DefaultIgnoreCondition = WhenWritingNull }`.

**Decision:** Hand-rolled SARIF model + `System.Text.Json`.
**Alternatives rejected:** `Microsoft.CodeAnalysis.Sarif` NuGet (Roslyn dependency, not AOT-safe), `Sarif.Sdk` (unmaintained).
**Rationale:** SARIF is a well-defined JSON schema. A handful of C# records is simpler and safer than pulling in a heavy SDK.

---

## 8. Test Scenarios

### 8.1 Test Location

Tests go in `Stash.Tests/` following existing convention. File: `StashCheckTests.cs` (or `CheckRunnerTests.cs` + `SarifFormatterTests.cs`).

### 8.2 Happy Path

| Test                                              | Description                                                                                    |
| ------------------------------------------------- | ---------------------------------------------------------------------------------------------- |
| `CheckRunner_SingleFile_ReturnsAnalysisResult`    | Analyze a single `.stash` file and verify `CheckResult` has correct file count and diagnostics |
| `CheckRunner_Directory_FindsAllStashFiles`        | Pass a directory, verify all `.stash` files are analyzed                                       |
| `CheckRunner_EmptyFile_NoDiagnostics`             | Empty file produces no diagnostics                                                             |
| `CheckRunner_CleanFile_NoDiagnostics`             | Well-formed file produces no diagnostics                                                       |
| `SarifFormatter_ValidSarif_MatchesSchema`         | Output validates against SARIF v2.1.0 JSON schema                                              |
| `SarifFormatter_DiagnosticsPresent_MapsCorrectly` | Verify `SemanticDiagnostic` → SARIF `result` field mapping                                     |
| `SarifFormatter_AllRulesIncluded_InToolDriver`    | All 31+ `DiagnosticDescriptor`s appear in `tool.driver.rules`                                  |
| `SarifFormatter_LexErrors_MappedAsSTASH001`       | Lex errors produce results with `ruleId: "STASH001"`                                           |
| `SarifFormatter_ParseErrors_MappedAsSTASH002`     | Parse errors produce results with `ruleId: "STASH002"`                                         |

### 8.3 Edge Cases

| Test                                                    | Description                                                       |
| ------------------------------------------------------- | ----------------------------------------------------------------- |
| `CheckRunner_ExcludeGlob_FiltersFiles`                  | `--exclude "**/*.test.stash"` skips test files                    |
| `CheckRunner_SeverityFilter_OmitsLower`                 | `--severity warning` omits `Information` diagnostics              |
| `CheckRunner_NonexistentFile_ExitCode2`                 | Non-existent file path returns exit code 2                        |
| `CheckRunner_NoStashFiles_ExitCode0`                    | Directory with no `.stash` files returns exit code 0, empty SARIF |
| `CheckRunner_MixedSeverities_ExitCodeReflectsThreshold` | Exit code 1 only if diagnostics meet severity threshold           |
| `SarifFormatter_RelativePaths_ForwardSlashes`           | Paths use `/` even on Windows                                     |
| `SarifFormatter_DiagnosticWithoutCode_SA0000`           | Legacy diagnostics get `ruleId: "SA0000"`                         |
| `SarifFormatter_UnnecessaryFlag_PropertyTag`            | `IsUnnecessary` maps to `properties.tags: ["unnecessary"]`        |

### 8.4 Error Cases

| Test                                          | Description                                          |
| --------------------------------------------- | ---------------------------------------------------- |
| `Program_InvalidFormat_ExitCode2`             | `--format xml` prints error to stderr, exit code 2   |
| `Program_NoArgs_DefaultsToCurrentDir`         | No arguments defaults to analyzing `.`               |
| `Program_Help_PrintsUsage`                    | `--help` prints usage to stdout, exit code 0         |
| `Program_Version_PrintsVersion`               | `--version` prints version to stdout, exit code 0    |
| `CheckRunner_UnreadableFile_SkipsWithWarning` | Permission-denied file is skipped, warning to stderr |

---

## 9. Deferred Work

These are explicitly **out of scope** for Phase 1 but documented here for future reference:

### 9.1 Additional Output Formats

| Format                     | Use Case                                                                        | Priority                      |
| -------------------------- | ------------------------------------------------------------------------------- | ----------------------------- |
| Text (human-readable)      | Terminal output for developers                                                  | High — first Phase 2 addition |
| JSON (simplified)          | Lightweight machine-readable output                                             | Medium                        |
| GitHub Actions annotations | `::error file=F,line=L::message` for inline PR annotations without SARIF upload | Low                           |

### 9.2 VS Code Extension Integration

Add settings to the VS Code extension:

```jsonc
"stash.checkPath": { "type": "string", "default": "" },
"stash.check.enabled": { "type": "boolean", "default": false },
"stash.check.runOn": { "type": "string", "enum": ["save", "type"], "default": "save" }
```

The extension would spawn `stash-check`, parse SARIF output, and convert to VS Code `Diagnostic` objects via `DiagnosticCollection`. This supplements (not replaces) the LSP's real-time diagnostics.

### 9.3 Configuration File

A `.stash-check.json` or `.stash-check.yaml` project-level config file for persistent options:

```json
{
  "exclude": ["**/vendor/**", "**/*.test.stash"],
  "severity": "warning",
  "rules": {
    "SA0201": "off",
    "SA0104": "warning"
  }
}
```

### 9.4 Rule-Level Configuration

Allow enabling/disabling individual diagnostic rules and overriding severity. This requires changes to `AnalysisEngine` to accept a rule configuration object.

### 9.5 Watch Mode

```bash
stash-check --watch src/
```

Re-analyze files on change. Lower priority — CI/CD doesn't need it, and the LSP already provides real-time feedback.

---

## 10. Decision Log

| #   | Decision                                  | Alternatives Rejected                       | Rationale                                                                          | Risk                                                                               |
| --- | ----------------------------------------- | ------------------------------------------- | ---------------------------------------------------------------------------------- | ---------------------------------------------------------------------------------- |
| D1  | Standalone binary, not embedded in CLI    | `stash --check` flag, LSP `--check` mode    | Smallest binary, cleanest deps, no interpreter/OmniSharp baggage                   | Another binary to distribute and version                                           |
| D2  | Native AOT (same as CLI)                  | Self-contained trimmed (like LSP/DAP)       | Analysis engine is AOT-safe (no reflection), fast startup critical for CI          | If future Analysis features need reflection, would need to switch to trimmed       |
| D3  | SARIF v2.1.0 only for Phase 1             | Text + SARIF, all formats at once           | Ship fast, validate the SARIF mapping, add formats later via `IOutputFormatter`    | Users who want human-readable output must wait for Phase 2                         |
| D4  | Hand-rolled SARIF serialization           | `Microsoft.CodeAnalysis.Sarif` NuGet        | AOT-safe, no Roslyn dependency, schema is simple enough                            | Must manually track SARIF spec compliance                                          |
| D5  | Hand-rolled argument parser               | `System.CommandLine`, `Spectre.Console.Cli` | AOT reliability, minimal deps, small CLI surface (~6 options)                      | Slightly more code to maintain, no auto-generated help formatting                  |
| D6  | Analysis stays in LSP (no removal)        | Move analysis exclusively to stash-check    | LSP real-time diagnostics are too valuable to lose; stash-check is for CI/headless | Two analysis entry points to keep in sync (same `AnalysisEngine`, so minimal risk) |
| D7  | `IOutputFormatter` interface from day one | Direct SARIF writes, add interface later    | Near-zero cost, prevents refactor when adding formats                              | Slight over-abstraction for a single formatter, but trivial                        |

---

## Appendix A: SARIF Output Example

Given a file `src/deploy.stash`:

```stash
fn deploy() {
    break;
    let x: int = "hello";
}
```

Expected SARIF output:

```json
{
  "$schema": "https://raw.githubusercontent.com/oasis-tcs/sarif-spec/main/sarif-2.1/schema/sarif-schema-2.1.0.json",
  "version": "2.1.0",
  "runs": [
    {
      "tool": {
        "driver": {
          "name": "stash-check",
          "semanticVersion": "0.1.0",
          "informationUri": "https://stash-lang.dev",
          "rules": [
            {
              "id": "SA0101",
              "shortDescription": { "text": "Break outside loop" },
              "defaultConfiguration": { "level": "error" },
              "properties": { "category": "Control flow" }
            },
            {
              "id": "SA0301",
              "shortDescription": { "text": "Variable type mismatch" },
              "defaultConfiguration": { "level": "warning" },
              "properties": { "category": "Type safety" }
            }
          ]
        }
      },
      "originalUriBaseIds": {
        "%SRCROOT%": {
          "uri": "file:///home/user/project/"
        }
      },
      "results": [
        {
          "ruleId": "SA0101",
          "ruleIndex": 0,
          "level": "error",
          "message": { "text": "'break' can only be used inside a loop." },
          "locations": [
            {
              "physicalLocation": {
                "artifactLocation": {
                  "uri": "src/deploy.stash",
                  "uriBaseId": "%SRCROOT%"
                },
                "region": {
                  "startLine": 2,
                  "startColumn": 5,
                  "endLine": 2,
                  "endColumn": 10
                }
              }
            }
          ]
        },
        {
          "ruleId": "SA0301",
          "ruleIndex": 1,
          "level": "warning",
          "message": {
            "text": "Variable 'x' declared as 'int' but assigned a value of type 'string'."
          },
          "locations": [
            {
              "physicalLocation": {
                "artifactLocation": {
                  "uri": "src/deploy.stash",
                  "uriBaseId": "%SRCROOT%"
                },
                "region": {
                  "startLine": 3,
                  "startColumn": 5,
                  "endLine": 3,
                  "endColumn": 28
                }
              }
            }
          ]
        }
      ],
      "invocations": [
        {
          "executionSuccessful": true,
          "commandLine": "stash-check src/deploy.stash",
          "startTimeUtc": "2026-04-03T12:00:00.000Z",
          "endTimeUtc": "2026-04-03T12:00:00.045Z"
        }
      ]
    }
  ]
}
```

---

## Appendix B: GitHub Actions Integration

```yaml
name: Stash Lint
on: [push, pull_request]

jobs:
  lint:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4

      - name: Run stash-check
        run: stash-check --output results.sarif .

      - name: Upload SARIF
        if: always()
        uses: github/codeql-action/upload-sarif@v3
        with:
          sarif_file: results.sarif
          category: stash-check
```

This produces inline annotations on pull requests via GitHub Code Scanning.
