# Diagnostic Codes & Suppression — Infrastructure Spec

> **Status:** Design (backlog)
> **Created:** April 2026
> **Purpose:** Define the protocol for assigning, identifying, and suppressing static analysis diagnostics in Stash. This is foundational infrastructure — every future diagnostic (spread/rest, pattern matching, etc.) must follow the conventions established here.
> **Blocks:** [Spread/Rest Parameters](Spread-Rest%20Parameters%20—%20Language%20Feature.md) §7.6 diagnostics

---

## Table of Contents

1. [Motivation](#1-motivation)
2. [Prior Art](#2-prior-art)
3. [Diagnostic Code Protocol](#3-diagnostic-code-protocol)
   - 3.1 [Code Format](#31-code-format)
   - 3.2 [Category Ranges](#32-category-ranges)
   - 3.3 [Severity Levels](#33-severity-levels)
   - 3.4 [Code Registry](#34-code-registry)
4. [Suppression Mechanism](#4-suppression-mechanism)
   - 4.1 [Inline Suppression — Next Line](#41-inline-suppression--next-line)
   - 4.2 [Inline Suppression — Same Line](#42-inline-suppression--same-line)
   - 4.3 [Block Suppression — Disable/Restore](#43-block-suppression--disablerestore)
   - 4.4 [File-Level Suppression](#44-file-level-suppression)
   - 4.5 [Project-Level Suppression](#45-project-level-suppression)
5. [Suppression Parsing & Resolution](#5-suppression-parsing--resolution)
6. [Requiring a Reason](#6-requiring-a-reason)
7. [Implementation Impact](#7-implementation-impact)
8. [LSP Integration](#8-lsp-integration)
9. [Migration Plan — Existing Diagnostics](#9-migration-plan--existing-diagnostics)
10. [Edge Cases & Error Conditions](#10-edge-cases--error-conditions)
11. [Test Scenarios](#11-test-scenarios)
12. [Decision Log](#12-decision-log)

---

## 1. Motivation

Stash's static analysis engine (`SemanticValidator`) currently produces diagnostics with human-readable messages, a severity level, and a source span — but **no stable identifier**. This creates three problems:

1. **No suppression.** If the analyzer warns about something the user intentionally wrote, there is no way to silence it. The warning persists forever, trains users to ignore the diagnostics pane, and erodes trust.

2. **No searchability.** Users can't Google `SA0042` for an explanation, documentation page, or Stack Overflow answers. Messages are the only identifier, and those strings are unstable — a rewording silently breaks any tooling built on message matching.

3. **No protocol for new diagnostics.** As we add spread/rest (8 new diagnostics), pattern matching, and other features, we need a consistent scheme for naming, numbering, and documenting each diagnostic. Without a protocol, codes will be assigned ad hoc and the system will be incoherent within months.

Every mature analysis toolchain solves this:

| Tool          | Code Format               | Suppression Mechanism                        |
| ------------- | ------------------------- | -------------------------------------------- |
| C# / Roslyn   | `CS0168`, `CA1234`        | `#pragma warning disable CS0168`             |
| ShellCheck    | `SC2034`                  | `# shellcheck disable=SC2034`                |
| ESLint        | `no-unused-vars`          | `// eslint-disable-next-line no-unused-vars` |
| Pylint        | `C0114`, `W0612`          | `# pylint: disable=W0612`                    |
| Clippy (Rust) | `clippy::needless_return` | `#[allow(clippy::needless_return)]`          |
| Go vet        | Named checks              | `//nolint:errcheck` (via golangci-lint)      |

Stash should join this list.

---

## 2. Prior Art

### ShellCheck — Comment directives

ShellCheck uses inline comments scoped to the **next statement**:

```bash
# shellcheck disable=SC2059
printf "\x$1"
```

- Codes are 4-digit numbers with `SC` prefix.
- Multiple codes comma-separated: `disable=SC2034,SC2059`.
- File-level suppression by placing the directive after the shebang.
- Project-level via `.shellcheckrc` file with `disable=SC2034`.
- Supports `enable=` to opt in to optional checks.

**What Stash borrows:** Comment-based suppression, scoping to next statement, comma-separated codes, file-level via top-of-file comment, project-level via config file.

**What Stash skips:** ShellCheck's `source=`, `shell=`, and `source-path=` directives are shell-specific and don't apply.

### C# — Pragma directives

C# uses preprocessor-style region-based suppression:

```csharp
#pragma warning disable CS0168
int x;
#pragma warning restore CS0168
```

- Codes are 4-digit numbers with `CS` prefix (compiler) or `CA` prefix (code analysis).
- `disable` / `restore` create regions — everything between is suppressed.
- Project-level via `.editorconfig` or MSBuild `<NoWarn>` property.
- `SuppressMessageAttribute` for method/class-level suppression.

**What Stash borrows:** The disable/restore region model for multi-line suppression. The concept of severity overrides in project config.

**What Stash skips:** `#pragma` requires a preprocessor — Stash doesn't have one. Attribute-based suppression requires decorators — Stash doesn't have those.

### ESLint — Comment directives

ESLint uses structured comments with the most flexible scoping model:

```javascript
// eslint-disable-next-line no-unused-vars
const x = 5;

/* eslint-disable no-unused-vars */
const a = 1;
const b = 2;
/* eslint-enable no-unused-vars */
```

- Named checks (not numeric codes).
- `disable-next-line` scopes to one line.
- `disable`/`enable` create regions.
- File-level by placing `/* eslint-disable */` at top.
- Project-level via `.eslintrc` config file.

**What Stash borrows:** The `disable-next-line` naming (clearer than just `disable` for single-statement scope). The concept that a bare `disable` without a code suppresses ALL diagnostics in scope (useful but dangerous — see §4).

---

## 3. Diagnostic Code Protocol

### 3.1 Code Format

Every diagnostic MUST have a unique code following this format:

```
SA{CCNN}
```

Where:

- `SA` — fixed prefix. **S**tash **A**nalysis. Two letters, uppercase.
- `CC` — category number (2 digits, `00`–`99`).
- `NN` — sequential number within category (2 digits, `00`–`99`).

This gives 100 categories × 100 codes per category = **10,000 possible codes**, which is more than sufficient. If a category fills up, allocate a second range.

Examples: `SA0101`, `SA0302`, `SA0501`.

The `SA` prefix is:

- Short enough to type in suppression comments.
- Distinctive enough to search for.
- Analogous to `SC` (ShellCheck), `CS` (C#), `CA` (Code Analysis).

### 3.2 Category Ranges

| Range             | Category          | Description                                                                 |
| ----------------- | ----------------- | --------------------------------------------------------------------------- |
| `SA00xx`          | Reserved          | Reserved for infrastructure diagnostics (e.g., invalid suppression comment) |
| `SA01xx`          | Control flow      | `break`/`continue`/`return` outside valid context, unreachable code         |
| `SA02xx`          | Declarations      | Unused variables, constants, parameters, imports; shadowing                 |
| `SA03xx`          | Type safety       | Type annotation mismatches, unknown types, assignment type conflicts        |
| `SA04xx`          | Functions & calls | Arity mismatches, argument type mismatches, variadic violations             |
| `SA05xx`          | Spread/rest       | Spread type mismatches, invalid null spread, redundant spread, empty spread |
| `SA06xx`          | Patterns          | Destructuring issues, pattern validation (future)                           |
| `SA07xx`          | Commands          | Command-related analysis (strict mode, retry, elevate)                      |
| `SA08xx`          | Imports           | Dynamic import paths, unresolved imports, circular imports                  |
| `SA09xx`          | Structs & enums   | Struct field type mismatches, unknown struct fields                         |
| `SA10xx`–`SA99xx` | Future            | Reserved for future categories                                              |

Categories are allocated in blocks of 100. A given code stays in its category permanently — codes are never renumbered or reassigned.

### 3.3 Severity Levels

Diagnostic code format carries no inherent severity. Severity is a property defined per-diagnostic and surfaced through the `DiagnosticLevel` enum:

| Level         | LSP Mapping                      | Editor Rendering        | When to use                                                                              |
| ------------- | -------------------------------- | ----------------------- | ---------------------------------------------------------------------------------------- |
| `Error`       | `DiagnosticSeverity.Error`       | Red underline           | The code is definitely wrong (e.g., `break` outside loop, arity mismatch with no spread) |
| `Warning`     | `DiagnosticSeverity.Warning`     | Yellow underline        | The code is likely wrong but we can't be certain (e.g., type mismatch on dynamic value)  |
| `Information` | `DiagnosticSeverity.Information` | Blue underline or faded | Style suggestions, unused symbols, redundant code                                        |

Severity can be overridden per-diagnostic at the project level (§4.5). Users can promote an `Information` to `Warning`, demote a `Warning` to `Information`, or suppress entirely.

### 3.4 Code Registry

Every diagnostic code MUST be registered in a central catalog. This catalog is the single source of truth and serves three audiences:

1. **Implementors** — know which codes are assigned, which are available.
2. **LSP/tooling** — populate the `Code` field on LSP diagnostics, generate documentation links.
3. **Users** — look up what a code means, decide whether to suppress it.

The catalog lives as a static data structure in `Stash.Analysis` (not in a human-edited markdown file that drifts). Each entry contains:

```
Code:         SA0501
Title:        Spreading non-array in array context
Category:     Spread/rest
Default Level: Warning
Message:      "Spread argument has type '{type}', expected 'array'."
Explanation:  URL or inline text explaining why this diagnostic exists
              and what to do about it.
```

Docs automation can generate a reference page from this data structure. This avoids the common problem of code registries that exist in docs but not in source (or vice versa).

---

## 4. Suppression Mechanism

Stash uses **comment-based directives** for suppression. No preprocessor, no attributes — just comments. This is consistent with Stash being an interpreted scripting language (comments are cheap, preprocessors are overhead).

### 4.1 Inline Suppression — Next Line

Suppress a diagnostic on the **next statement** (the line immediately following the comment):

```stash
// stash-disable-next-line SA0501
let result = [...nonArrayValue];
```

Multiple codes, comma-separated:

```stash
// stash-disable-next-line SA0501, SA0503
let result = [...null, ...nonArray];
```

Suppress ALL diagnostics on the next line (use sparingly):

```stash
// stash-disable-next-line
let result = [...unknownThing];
```

**Scoping:** The suppression applies only to diagnostics whose `SourceSpan` starts on the next non-empty, non-comment line. It does NOT bleed past that line.

### 4.2 Inline Suppression — Same Line

Suppress a diagnostic on the **same line** as the comment (trailing comment):

```stash
let result = [...nonArray]; // stash-disable-line SA0501
```

This is convenient for one-liners where a preceding comment would be noisy.

**Scoping:** Applies only to diagnostics whose `SourceSpan` starts on the same line as the comment.

### 4.3 Block Suppression — Disable/Restore

Suppress diagnostics across a **region** of code:

```stash
// stash-disable SA0501
let a = [...x];
let b = [...y];
let c = [...z];
// stash-restore SA0501
```

If `restore` is omitted, suppression extends to the end of the file (same semantics as C#'s `#pragma warning disable` without a matching `restore`).

Suppress all diagnostics in a region:

```stash
// stash-disable
// ... code with many diagnostics ...
// stash-restore
```

Restore a specific code while keeping others suppressed:

```stash
// stash-disable SA0501, SA0502
let a = [...x];       // SA0501 and SA0502 suppressed
// stash-restore SA0501
let b = [...y];       // Only SA0502 still suppressed
// stash-restore SA0502
```

### 4.4 File-Level Suppression

Place a `stash-disable` directive at the top of the file (after the shebang, if present, and before any code):

```stash
#!/usr/bin/env stash
// stash-disable SA0201
// stash-disable SA0501, SA0502

let unused = 5;          // SA0201 suppressed file-wide
let result = [...bad];   // SA0501 suppressed file-wide
```

This is syntactically identical to block suppression — the convention is just "at the top of the file." No special parsing needed.

### 4.5 Project-Level Suppression

A `.stashcheck` file in the project root (or any ancestor directory) configures diagnostics project-wide. The analyzer walks up from the script's directory looking for this file (like `.editorconfig` and `.shellcheckrc`).

```ini
# .stashcheck — project-level diagnostic configuration

# Suppress specific diagnostics globally
disable = SA0201, SA0202

# Change severity of a diagnostic
severity.SA0501 = error     # Promote spread-type-mismatch to error
severity.SA0301 = off       # Completely disable type mismatch warnings

# Severity values: error, warning, info, off
```

The file format is deliberately simple — key-value pairs, one per line, `#` comments. Not JSON, not TOML — this file must be trivially readable and editable. It mirrors `.shellcheckrc` simplicity.

**Resolution order** (last wins):

1. **Built-in defaults** — each diagnostic has a default severity baked into the code registry.
2. **`.stashcheck` file** — overrides defaults for the project.
3. **Inline directives** — override everything for their scope.

---

## 5. Suppression Parsing & Resolution

### 5.1 Directive Syntax (Formal)

```
Directive       ::= "//" WHITESPACE? DirectiveBody
DirectiveBody   ::= "stash-disable-next-line" CodeList?
                   | "stash-disable-line" CodeList?
                   | "stash-disable" CodeList?
                   | "stash-restore" CodeList?
CodeList        ::= Code ("," WHITESPACE? Code)*
Code            ::= "SA" DIGIT DIGIT DIGIT DIGIT
```

The directive keywords are case-sensitive (lowercase). Leading/trailing whitespace around codes is tolerated. Anything after the code list on the same line is ignored (allows trailing explanatory comments):

```stash
// stash-disable-next-line SA0501 — we know this is a dict, the type inference is wrong
```

### 5.2 Where Directives Are Parsed

Directives are parsed from **trivia tokens**. The `AnalysisEngine` already lexes with `preserveTrivia: true` and collects `SingleLineComment` tokens. The new step:

1. After lexing, scan all `SingleLineComment` tokens.
2. For each, check if the text (after `//`) matches a directive pattern.
3. Build a `SuppressionMap` data structure that records which codes are suppressed at which line ranges.
4. Pass the `SuppressionMap` to `SemanticValidator`.
5. After validation, filter the diagnostic list: remove any diagnostic whose code is suppressed at its `SourceSpan` location.

### 5.3 SuppressionMap Data Structure

```
SuppressionMap {
    // Line-level suppressions: line → set of suppressed codes (empty set = all)
    LineSuppressions: Dict<int, Set<string>>

    // Range suppressions: each entry has start line, optional end line, set of codes
    RangeSuppressions: List<{ StartLine, EndLine?, Codes: Set<string> }>

    // Query method
    IsSuppressed(code: string, line: int) -> bool
}
```

### 5.4 Resolution Rules

For a given diagnostic with code `SA0501` at line 42:

1. Check `LineSuppressions[42]` — if it contains `SA0501` or is empty (suppress-all), suppress.
2. Check each `RangeSuppressions` — if any range contains line 42 and its `Codes` includes `SA0501` (or is empty), suppress.
3. If not suppressed by inline directives, check the `.stashcheck` file for `disable` or `severity.SA0501 = off`.
4. If still not suppressed, emit the diagnostic.

### 5.5 Directive Validation

Invalid directives should produce their own diagnostic:

```stash
// stash-disable-next-line SA9999
let x = 5;
```

| Code     | Level     | Message                                                        |
| -------- | --------- | -------------------------------------------------------------- |
| `SA0001` | `Warning` | `"Unknown diagnostic code 'SA9999' in suppression directive."` |

```stash
// stash-disable-next-line SAXXXX
let x = 5;
```

| Code     | Level     | Message                                                                                                    |
| -------- | --------- | ---------------------------------------------------------------------------------------------------------- |
| `SA0002` | `Warning` | `"Malformed diagnostic code 'SAXXXX' in suppression directive. Expected format: SA followed by 4 digits."` |

```stash
// stash-disable-next-line SA0501
let x = 5;  // SA0501 doesn't fire here — no spread at all
```

| Code     | Level         | Message                                                                 |
| -------- | ------------- | ----------------------------------------------------------------------- |
| `SA0003` | `Information` | `"Unnecessary suppression: 'SA0501' does not fire on the target line."` |

> **Decision:** SA0003 (unnecessary suppression) is **deferred** to a follow-up. It requires a two-pass approach (validate, then check which suppressions were actually used) and adds complexity. Nice to have but not blocking.

---

## 6. Requiring a Reason

Some teams want to enforce that every suppression includes a reason. Stash supports this as an **opt-in project-level setting**, not a default:

```ini
# .stashcheck
require-suppression-reason = true
```

When enabled, a bare suppression without trailing text produces a diagnostic:

```stash
// stash-disable-next-line SA0501
let x = [...bad];
// ⚠ SA0004: Suppression directive should include a reason.

// stash-disable-next-line SA0501 — type inference is wrong here, x is actually an array
let x = [...bad];
// ✓ No diagnostic — reason is present
```

The "reason" is any non-whitespace text after the code list. It's not parsed or validated — it's for humans.

Default: `false`. Most users won't want this. Teams with strict code review processes can opt in.

---

## 7. Implementation Impact

### 7.1 `SemanticDiagnostic` Model Change

Add a `Code` field to the diagnostic model:

```
SemanticDiagnostic {
    Code: string           // NEW — e.g., "SA0501"
    Message: string
    Level: DiagnosticLevel
    Span: SourceSpan
    IsUnnecessary: bool
}
```

All existing diagnostic construction sites (every `new SemanticDiagnostic(...)` in `SemanticValidator.cs`) must be updated to include a code. For the initial migration, codes are assigned to all existing diagnostics (see §9).

### 7.2 Diagnostic Registry (`Stash.Analysis`)

New class: `DiagnosticDescriptors` — static catalog of all known diagnostics.

```
DiagnosticDescriptor {
    Code: string            // "SA0101"
    Title: string           // Short human-readable title
    DefaultLevel: DiagnosticLevel
    Category: string        // "Control flow", "Spread/rest", etc.
    MessageFormat: string   // Template with {0}, {1} placeholders
}
```

Every diagnostic is defined as a `static readonly` field on `DiagnosticDescriptors`. The `SemanticValidator` references these descriptors instead of constructing messages inline. This:

- Ensures every diagnostic has a code.
- Centralizes message strings (no typo drift across 50+ diagnostic sites).
- Enables documentation generation.
- Enables severity overrides (look up the descriptor, apply project config).

### 7.3 Suppression Infrastructure (`Stash.Analysis`)

New classes:

- `SuppressionDirectiveParser` — scans trivia tokens for `stash-disable` / `stash-restore` comments, builds the `SuppressionMap`.
- `SuppressionMap` — queryable data structure for "is code X suppressed at line Y?"
- `ProjectConfig` — reads `.stashcheck` files, provides project-level disable list and severity overrides.

### 7.4 Analysis Engine Pipeline Change

The `AnalysisEngine.Analyze()` pipeline gains two new steps:

```
1. Lex (with trivia)
2. Parse
3. Collect symbols
4. Resolve imports
5. Infer types
6. Resolve doc comments
7. *** Parse suppression directives from trivia tokens ***
8. Semantic validation (produces diagnostics with codes)
9. *** Filter diagnostics through SuppressionMap ***
10. Cache and return
```

Steps 7 and 9 are new.

### 7.5 Lexer — No Changes

No lexer changes needed. Suppression comments are regular `//` comments. The lexer already preserves them as `SingleLineComment` tokens when `preserveTrivia: true`. The parsing happens downstream in the analysis engine.

### 7.6 Parser — No Changes

The parser never sees trivia tokens (they're filtered out before parsing). No parser changes needed.

---

## 8. LSP Integration

### 8.1 Diagnostic `Code` Field

The `DiagnosticBuilder` must populate the LSP `Diagnostic.Code` field:

```
Before:
    Diagnostic {
        Message: "Spread argument has type 'int', expected 'array'."
        Severity: Warning
        Source: "stash"
        Range: ...
    }

After:
    Diagnostic {
        Code: "SA0501"
        Message: "Spread argument has type 'int', expected 'array'."
        Severity: Warning
        Source: "stash"
        Range: ...
        CodeDescription: { Href: "https://stash-lang.dev/diagnostics/SA0501" }
    }
```

The `CodeDescription.Href` field makes the code a clickable link in VS Code, taking the user to documentation. Initially this can point to a generated docs page or the GitHub wiki. The URL scheme should be decided now even if the docs site doesn't exist yet.

### 8.2 Code Actions — Quick Suppress

The LSP server should offer **code actions** (lightbulb menu) on any diagnostic to insert a suppression comment:

| Code Action Label               | Result                                                                |
| ------------------------------- | --------------------------------------------------------------------- |
| `Suppress SA0501 for this line` | Inserts `// stash-disable-next-line SA0501` above the diagnostic line |
| `Suppress SA0501 for this file` | Inserts `// stash-disable SA0501` at the top of the file              |

This is a follow-up feature — it requires implementing a `CodeActionHandler` in the LSP server. Document it here so it's on the roadmap, but it is not blocking.

### 8.3 Suppressed Diagnostics — Faded Directive

When a suppression directive is active and correctly suppresses a diagnostic, the suppression comment itself should NOT be flagged. When a suppression is unnecessary (SA0003), the directive comment should be faded (rendered with `IsUnnecessary: true`). This is deferred per §5.5.

---

## 9. Migration Plan — Existing Diagnostics

All existing diagnostics in `SemanticValidator` must be assigned codes. This is the initial registry. Categories follow §3.2.

### SA00xx — Infrastructure

| Code     | Level   | Current Message                                                  | Notes                   |
| -------- | ------- | ---------------------------------------------------------------- | ----------------------- |
| `SA0001` | Warning | `"Unknown diagnostic code '{code}' in suppression directive."`   | New (suppression infra) |
| `SA0002` | Warning | `"Malformed diagnostic code '{code}' in suppression directive."` | New (suppression infra) |

### SA01xx — Control Flow

| Code     | Level       | Current Message                          | Notes                           |
| -------- | ----------- | ---------------------------------------- | ------------------------------- |
| `SA0101` | Error       | `"'break' used outside of a loop."`      | Existing                        |
| `SA0102` | Error       | `"'continue' used outside of a loop."`   | Existing                        |
| `SA0103` | Error       | `"'return' used outside of a function."` | Existing                        |
| `SA0104` | Information | `"Unreachable code detected."`           | Existing, `IsUnnecessary: true` |

### SA02xx — Declarations

| Code     | Level       | Current Message                                 | Notes                           |
| -------- | ----------- | ----------------------------------------------- | ------------------------------- |
| `SA0201` | Information | `"{Kind} '{name}' is declared but never used."` | Existing, `IsUnnecessary: true` |
| `SA0202` | Warning     | `"'{name}' is not defined."`                    | Existing                        |
| `SA0203` | Error       | `"Cannot reassign constant '{name}'."`          | Existing                        |

### SA03xx — Type Safety

| Code     | Level   | Current Message                                                                     | Notes    |
| -------- | ------- | ----------------------------------------------------------------------------------- | -------- |
| `SA0301` | Warning | `"Variable '{name}' is declared as '{expected}' but initialized with '{actual}'."`  | Existing |
| `SA0302` | Warning | `"Constant '{name}' is declared as '{expected}' but initialized with '{actual}'."`  | Existing |
| `SA0303` | Warning | `"Unknown type '{typeName}'."`                                                      | Existing |
| `SA0304` | Warning | `"Cannot assign value of type '{actual}' to field '{field}' of type '{expected}'."` | Existing |

### SA04xx — Functions & Calls

| Code     | Level   | Current Message                                                      | Notes                             |
| -------- | ------- | -------------------------------------------------------------------- | --------------------------------- |
| `SA0401` | Error   | `"Expected {expected} arguments but got {actual}."`                  | Existing (user-defined functions) |
| `SA0402` | Error   | `"Expected {expected} arguments but got {actual}."`                  | Existing (built-in functions)     |
| `SA0403` | Warning | `"Argument '{param}' expects type '{expected}' but got '{actual}'."` | Existing                          |

### SA05xx — Spread/Rest (New — from spread/rest spec)

| Code     | Level       | Message                                                                    | Notes              |
| -------- | ----------- | -------------------------------------------------------------------------- | ------------------ |
| `SA0501` | Warning     | `"Spread argument has type '{type}', expected 'array'."`                   | New                |
| `SA0502` | Warning     | `"Spread argument has type '{type}', expected 'dict' or struct instance."` | New                |
| `SA0503` | Warning     | `"Spreading 'null' will always fail at runtime."`                          | New                |
| `SA0504` | Error       | `"At least {min} arguments provided but '{fn}' expects at most {max}."`    | New (spread arity) |
| `SA0505` | Information | `"Unnecessary spread of array literal in function call."`                  | New                |
| `SA0506` | Information | `"Spreading an empty {type} literal has no effect."`                       | New                |

### SA07xx — Commands

| Code     | Level   | Current Message                                          | Notes    |
| -------- | ------- | -------------------------------------------------------- | -------- |
| `SA0701` | Warning | `"Nested 'elevate' has no effect..."`                    | Existing |
| `SA0702` | Warning | `"Retry body contains only shell commands..."`           | Existing |
| `SA0703` | Warning | `"'retry' with 0 attempts will never execute the body."` | Existing |

### SA08xx — Imports

| Code     | Level       | Current Message                                          | Notes    |
| -------- | ----------- | -------------------------------------------------------- | -------- |
| `SA0801` | Information | `"Dynamic import path cannot be resolved statically..."` | Existing |

> **Note:** The existing informal `SA-SPREAD-01` through `SA-SPREAD-05` names used in the spread/rest spec should be updated to use the canonical codes (`SA0501`–`SA0506`) once this infrastructure is in place.

---

## 10. Edge Cases & Error Conditions

### 10.1 Block comments as suppression directives

Only `//` single-line comments are recognized as suppression directives. Block comments (`/* */`) and doc comments (`///`) are NOT parsed for suppression. This keeps the parsing simple and unambiguous.

```stash
/* stash-disable SA0501 */    // NOT recognized — block comment
/// stash-disable SA0501      // NOT recognized — doc comment
// stash-disable SA0501       // ✓ Recognized
```

**Rationale:** Block comments can span multiple lines with complex structure. Doc comments have their own semantics (attached to declarations). Suppression should be a simple, predictable `//` comment — easy to add, easy to find, easy to grep for.

### 10.2 Suppression of parser errors

Parser errors (syntax errors) **cannot be suppressed**. Suppression directives only apply to semantic diagnostics (`SA####` codes). If the parser can't parse the file, no analysis runs, so no suppression can apply.

### 10.3 Suppression in REPL

The REPL evaluates line-by-line. Suppression directives work in the REPL, but:

- `stash-disable-next-line` applies to the next REPL input line.
- `stash-disable` without `restore` suppresses for the remainder of the REPL session.
- File-level and project-level suppression are not applicable in the REPL.

### 10.4 Multiple directive comments on consecutive lines

```stash
// stash-disable-next-line SA0501
// stash-disable-next-line SA0502
let result = [...bad, ...alsoBad];
```

Both directives apply to the same target line. SA0501 and SA0502 are both suppressed. This is equivalent to:

```stash
// stash-disable-next-line SA0501, SA0502
let result = [...bad, ...alsoBad];
```

### 10.5 Nested disable/restore regions

```stash
// stash-disable SA0501
// stash-disable SA0502
let x = [...a];      // SA0501 and SA0502 suppressed
// stash-restore SA0501
let y = [...b];      // Only SA0502 suppressed
// stash-restore SA0502
let z = [...c];      // Nothing suppressed
```

Restore affects only the specified code. Other codes in the active set remain suppressed. This matches C# `#pragma warning` semantics.

### 10.6 `.stashcheck` file discovery

The analysis engine searches for `.stashcheck` starting from the script's directory, walking up to the filesystem root. The first file found is used (no merging). This matches `.shellcheckrc` and `.editorconfig` behavior.

If no `.stashcheck` file is found, all diagnostics use their built-in defaults.

---

## 11. Test Scenarios

### Suppression Directive Parsing

| #   | Test                                                                     | Expected                                |
| --- | ------------------------------------------------------------------------ | --------------------------------------- |
| 1   | `// stash-disable-next-line SA0201` followed by unused variable          | SA0201 suppressed                       |
| 2   | `// stash-disable-next-line SA0201, SA0301` — two codes                  | Both suppressed on next line            |
| 3   | `// stash-disable-next-line` (no code) — suppress all                    | All diagnostics suppressed on next line |
| 4   | Same-line: `let x = 5; // stash-disable-line SA0201`                     | SA0201 suppressed on that line          |
| 5   | Block: `// stash-disable SA0201` ... `// stash-restore SA0201`           | Suppressed in range                     |
| 6   | Block without restore — suppression extends to file end                  | All subsequent lines suppressed         |
| 7   | File-level: directive at top of file before any code                     | Entire file suppressed                  |
| 8   | Directive after shebang: `#!/usr/bin/env stash\n// stash-disable SA0201` | Works                                   |

### Directive Validation

| #   | Test                                         | Expected                                 |
| --- | -------------------------------------------- | ---------------------------------------- |
| 9   | `// stash-disable-next-line SA9999`          | SA0001 warning: unknown code             |
| 10  | `// stash-disable-next-line SAXXXX`          | SA0002 warning: malformed code           |
| 11  | `// stash-disable-next-line FOOBAR`          | SA0002 warning: not SA format            |
| 12  | `/* stash-disable SA0201 */` (block comment) | NOT parsed as directive — no suppression |
| 13  | `/// stash-disable SA0201` (doc comment)     | NOT parsed as directive — no suppression |

### Scoping

| #   | Test                                                                                                      | Expected                                                |
| --- | --------------------------------------------------------------------------------------------------------- | ------------------------------------------------------- |
| 14  | `next-line` does not bleed to line after target                                                           | Only target line suppressed                             |
| 15  | Blank line between directive and target: `// stash-disable-next-line SA0201\n\nlet x = 5;`                | Suppression applies to next non-empty, non-comment line |
| 16  | Comment between directive and target: `// stash-disable-next-line SA0201\n// regular comment\nlet x = 5;` | Suppression applies to `let x = 5;`                     |
| 17  | Nested disable/restore regions                                                                            | Inner restore doesn't affect outer codes                |
| 18  | Restore without prior disable                                                                             | No effect (no error, silently ignored)                  |

### Integration with Existing Diagnostics

| #   | Test                                                            | Expected                      |
| --- | --------------------------------------------------------------- | ----------------------------- |
| 19  | `// stash-disable-next-line SA0101` above `break;` outside loop | Error suppressed              |
| 20  | `// stash-disable-next-line SA0104` above unreachable code      | Unreachable fading suppressed |
| 21  | Diagnostic code appears in LSP `Diagnostic.Code` field          | Code present and correct      |

### Project-Level Config

| #   | Test                                         | Expected                              |
| --- | -------------------------------------------- | ------------------------------------- |
| 22  | `.stashcheck` with `disable = SA0201`        | SA0201 suppressed in all files        |
| 23  | `.stashcheck` with `severity.SA0301 = error` | SA0301 promoted from Warning to Error |
| 24  | `.stashcheck` with `severity.SA0301 = off`   | SA0301 completely disabled            |
| 25  | Inline directive overrides `.stashcheck`     | Inline wins                           |

---

## 12. Decision Log

### D1: Code format — `SA` prefix + 4 digits

- **Decision:** `SA{CCNN}` — 2-letter prefix, 2-digit category, 2-digit sequence.
- **Alternatives:**
  - Named codes like ESLint (`no-unused-vars`) — rejected. More readable but harder to type in suppression comments, harder to number/sequence, and Stash's diagnostics don't map neatly to hyphenated-phrase names.
  - Longer codes like Roslyn (`CA1234`) with separate prefixes for different subsystems — rejected. Stash has one analysis engine, not multiple. One prefix is enough.
  - 3-digit category + 3-digit sequence (6 digits total) — rejected. Over-engineered for current scale. 4 digits (10K codes) is sufficient for the foreseeable future.
- **Rationale:** `SA` is short, distinctive, and follows the `SC`/`CS`/`CA` convention. 4 digits give 10K codes — well beyond what Stash will need for years.

### D2: Comment-based suppression (not preprocessor)

- **Decision:** `// stash-disable-next-line SA0501` as a regular comment.
- **Alternatives:**
  - Preprocessor directives (`#pragma`, `#stash`) — rejected. Stash has no preprocessor and adding one for this alone would be over-engineering. Preprocessor directives also need special lexer support.
  - Attribute/decorator syntax (`@suppress(SA0501)`) — rejected. Stash has no decorators and shouldn't add them just for suppression.
- **Rationale:** Comment-based directives require zero language changes — the lexer already preserves comments as trivia. Parsing happens in the analysis layer only. ShellCheck and ESLint prove this approach works well in practice.

### D3: `stash-` prefix on directives

- **Decision:** Directives use `stash-disable`, not just `disable` or `sa-disable`.
- **Alternatives:**
  - `// @stash-disable` (decorator-style) — rejected. The `@` is unnecessary noise.
  - `// disable SA0501` (no prefix) — rejected. Too generic, could conflict with human comments that happen to start with "disable."
  - `// sa:disable` (colon syntax) — rejected. Slightly less readable.
- **Rationale:** `stash-` as a namespace prefix is unambiguous, easy to grep for (`grep stash-disable`), and clearly distinct from regular comments. Follows ESLint's `eslint-disable` convention.

### D4: Severity override in `.stashcheck`, not in source

- **Decision:** Severity changes (promote warning to error, demote to info) are project-level only, via `.stashcheck`. Inline directives can only suppress (not change severity).
- **Alternatives:**
  - Allow inline severity changes: `// stash-severity SA0501 error` — rejected. Adds complexity for a rare use case. Severity is a project policy decision, not a per-line decision.
- **Rationale:** Keep the inline syntax simple: suppress or don't. Severity is a team/project concern, configured once in `.stashcheck`, not scattered through source files.

### D5: Only `//` comments, not `/* */`

- **Decision:** Only single-line `//` comments are parsed for suppression directives. Block comments and doc comments are ignored.
- **Alternatives:**
  - Allow `/* stash-disable SA0501 */` on the same line — would enable same-line suppression without a separate `disable-line` keyword.
- **Rationale:** One comment style for directives is simpler to implement, document, and grep for. Block comments have nesting complexity. Doc comments have their own attachment semantics. One style, one rule.
