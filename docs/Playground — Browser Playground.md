# Playground - Browser Playground

> **Status:** Stable product and maintainer reference
> **Audience:** Playground users, website maintainers, and contributors changing browser execution or editor intelligence.
> **Purpose:** Defines the Stash browser playground experience, sandbox contract, editor features, sharing model, and maintenance rules.
>
> **Companion documents:**
>
> - [Language Specification](Stash%20%E2%80%94%20Language%20Specification.md) - syntax and language semantics.
> - [Standard Library Reference](Stash%20%E2%80%94%20Standard%20Library%20Reference.md) - built-in namespace API surface.
> - [LSP - Language Server Protocol](LSP%20%E2%80%94%20Language%20Server%20Protocol.md) - editor-intelligence behavior mirrored in the browser.
> - [TAP - Testing Infrastructure](TAP%20%E2%80%94%20Testing%20Infrastructure.md) - test framework behavior.
> - [TPL - Templating Engine](TPL%20%E2%80%94%20Templating%20Engine.md) - templating language behavior.

The Stash Playground is a Blazor WebAssembly application for writing, analyzing, formatting, running, and sharing Stash code in the browser. Code runs client-side; the playground does not send user code to a server for execution.

## 1. Scope

The playground is for learning, experimentation, demos, and small shareable snippets. It is not a replacement for the local CLI when code needs operating-system access.

The browser runtime supports pure Stash computation and disables capability-gated host features such as shell execution, file I/O, networking, process control, and environment mutation.

## 2. User Experience

The first screen is the editor, toolbar, and output panel.

| Area               | Behavior                                                                |
| ------------------ | ----------------------------------------------------------------------- |
| Editor             | Monaco editor configured for Stash syntax.                              |
| Toolbar            | Example loader, format, download, share, theme toggle, and run command. |
| Output panel       | Program output, runtime errors, elapsed time, and step count.           |
| Limitations banner | Explains which OS features are unavailable in the browser.              |
| Showcases          | Read-only examples for non-sandboxed features.                          |

Run code with the Run button or `Ctrl+Enter` / `Cmd+Enter`.

## 3. Editor Features

The Monaco editor integrates with Stash analysis services compiled into WebAssembly.

| Feature             | Source                                                  |
| ------------------- | ------------------------------------------------------- |
| Syntax highlighting | Monarch tokenizer in `wwwroot/js/stash-language.js`.    |
| Diagnostics         | `PlaygroundAnalyzer.GetDiagnostics`.                    |
| Completion          | `PlaygroundAnalyzer.GetCompletions`.                    |
| Hover               | `PlaygroundAnalyzer.GetHover`.                          |
| Signature help      | `PlaygroundAnalyzer.GetSignatureHelp`.                  |
| Formatting          | `PlaygroundAnalyzer.FormatCode` using `StashFormatter`. |

Diagnostics include lexer errors, parse errors, and semantic diagnostics. Completion and hover use the same analysis engine and standard-library metadata used by the rest of the tooling.

The editor uses `TabSize = 4`, spaces, automatic layout, no minimap, and a monospace font stack suitable for code.

## 4. Execution Model

Each run creates a fresh `StashEngine` and executes the current editor contents.

| Guard           | Value                    | Contract                                                       |
| --------------- | ------------------------ | -------------------------------------------------------------- |
| Capabilities    | `StashCapabilities.None` | Disables capability-gated host namespaces and shell execution. |
| Step limit      | `5,000,000`              | Stops runaway execution.                                       |
| Output cap      | `512 KB`                 | Captures output up to the cap and appends a truncation note.   |
| Engine lifetime | One engine per run       | Runtime globals do not persist between runs.                   |

The executor captures stdout and stderr separately. Runtime errors are displayed in the output panel with line and column information when available.

Step-limit failures are reported as execution failures. Browser cancellation is best-effort; the step limit is the primary loop guard in WebAssembly.

## 5. What Works

The sandbox supports language and standard-library features that do not require external host capabilities, including:

- Variables, constants, functions, lambdas, closures, and recursion.
- Structs, enums, interfaces, extend blocks, methods, and field access.
- Arrays, dictionaries, ranges, loops, switch expressions, and pattern-style dispatch.
- String processing, math, JSON-like in-memory data shaping, and pure transformations.
- Error handling, `try` expressions, and first-class error values.
- Static diagnostics, formatting, completion, hover, and signature help.

## 6. What Does Not Run

The following are not executable in the browser sandbox:

| Feature                                  | Reason                                                              |
| ---------------------------------------- | ------------------------------------------------------------------- |
| `$(...)` shell commands                  | Requires process execution.                                         |
| `fs.*`                                   | Requires local file-system access.                                  |
| `http.*`, sockets, SSH, SFTP, WebSockets | Requires network capabilities outside the browser sandbox contract. |
| `process.*`                              | Requires process management.                                        |
| `env.*` mutation                         | Requires host environment access.                                   |
| Scheduler/service management             | Requires OS service APIs.                                           |

The playground includes read-only showcases for representative OS-backed features instead of pretending they work in the browser.

## 7. Examples

Examples are defined in `Stash.Playground/Pages/Playground.razor`. The current curated example set contains 12 runnable examples:

| Example             | Concepts                                                  |
| ------------------- | --------------------------------------------------------- |
| Hello World         | Variables, structs, arrays, and output.                   |
| Variables & Types   | Types, `typeof`, nullish coalescing, ternary expressions. |
| Structs & Enums     | Struct construction, field access, enum values.           |
| Interfaces          | Interface contracts, methods, and type checks.            |
| Arrays & Functional | Sorting, filtering, mapping, reducing, and flattening.    |
| Dictionaries        | Dynamic keys, merge, traversal, remove.                   |
| Lambdas & Closures  | Higher-order functions, closures, currying.               |
| String Processing   | Trim, case conversion, split, join, search, replace.      |
| Error Handling      | Throwing, `try`, error checks, safe defaults.             |
| Pattern Matching    | Switch expressions, ternary chains, FizzBuzz.             |
| Algorithms          | Quicksort, Fibonacci, statistics.                         |
| Extend Blocks       | Methods added to built-in and user-defined types.         |

Runnable examples must work with `StashCapabilities.None`, produce visible output, and complete within the step limit.

## 8. Showcases

Showcases are collapsible, read-only examples with pre-rendered output. They document features that require a local Stash installation.

| Showcase                | Demonstrates                               |
| ----------------------- | ------------------------------------------ |
| Shell Command Execution | Command literals and shell-style output.   |
| File System Operations  | Reading, writing, and listing files.       |
| HTTP Requests           | HTTP client calls and response handling.   |
| Process Management      | Spawning, checking, and killing processes. |

Showcases must not be wired to execute in the browser.

## 9. Sharing

The Share action encodes the current editor text into the URL hash:

```text
https://playground.stash-lang.dev/#code=<base64>
```

The code is stored in the fragment, not sent to a server. Opening a share URL decodes the hash and loads it into the editor.

Load priority:

1. `#code=` hash fragment.
2. Browser `localStorage`.
3. Default example.

Large snippets can produce long URLs. The playground does not provide server-side snippet storage, expiration, accounts, or privacy controls.

## 10. Persistence

Editor contents are saved to browser `localStorage` under `stash-playground-code`. Saving is triggered from the JavaScript editor integration. A later visit restores the saved code when no share hash is present.

Clearing browser storage clears the autosaved snippet.

## 11. Actions

| Action       | Behavior                                                                   |
| ------------ | -------------------------------------------------------------------------- |
| Run          | Executes current editor contents and updates the output panel.             |
| Format       | Runs the Stash formatter and replaces editor contents with formatted code. |
| Download     | Downloads the current editor contents as `playground.stash`.               |
| Share        | Copies a URL hash link to the clipboard.                                   |
| Copy output  | Copies current stdout text to the clipboard.                               |
| Theme toggle | Switches between dark and light playground themes.                         |

Clipboard actions depend on browser clipboard permissions and may fail.

## 12. Themes

The playground ships with two UI/editor themes:

| Theme         | Mode                                   |
| ------------- | -------------------------------------- |
| `stash-dark`  | Catppuccin Mocha-inspired dark theme.  |
| `stash-light` | Catppuccin Latte-inspired light theme. |

CSS variables live in `wwwroot/css/app.css`. Monaco theme definitions and token styling live in `wwwroot/js/stash-language.js`. Theme changes should update both layers together.

## 13. Architecture

The playground is a `net10.0` Blazor WebAssembly app.

| Layer              | Implementation                                   |
| ------------------ | ------------------------------------------------ |
| UI                 | `Pages/Playground.razor`                         |
| Editor integration | BlazorMonaco plus `wwwroot/js/stash-language.js` |
| Execution          | `Services/PlaygroundExecutor.cs`                 |
| Analysis bridge    | `Services/PlaygroundAnalyzer.cs`                 |
| Output cap         | `Services/CappedStringWriter.cs`                 |
| Result DTO         | `Services/PlaygroundResult.cs`                   |

Project references:

| Project          | Purpose                                                    |
| ---------------- | ---------------------------------------------------------- |
| `Stash.Core`     | Lexer, parser, AST, common runtime contracts.              |
| `Stash.Bytecode` | Compiler and VM execution.                                 |
| `Stash.Analysis` | Diagnostics, completion, hover, signature help, formatter. |
| `Stash.Stdlib`   | Built-in metadata and capability-gated runtime APIs.       |

## 14. Data Flow

Editor analysis flow:

1. Monaco detects text changes or editor requests.
2. JavaScript calls `[JSInvokable]` methods on `PlaygroundAnalyzer`.
3. `AnalysisEngine` analyzes `file:///playground.stash`.
4. JavaScript converts DTOs into Monaco diagnostics, completions, hovers, and signature help.

Run flow:

1. User clicks Run or presses `Ctrl+Enter` / `Cmd+Enter`.
2. `Playground.razor` reads editor text.
3. `PlaygroundExecutor` creates a fresh `StashEngine(StashCapabilities.None)`.
4. Output is captured through `CappedStringWriter`.
5. Result, errors, elapsed milliseconds, and step count are rendered in the output panel.

## 15. Development

Build:

```bash
dotnet build Stash.Playground/
```

Run locally:

```bash
dotnet run --project Stash.Playground/
```

Publish:

```bash
dotnet publish Stash.Playground/ -c Release
```

Use the URL printed by the development server. Do not hard-code a port in docs or tooling unless the project file pins one.

## 16. Maintenance Rules

When changing the playground:

- Keep runnable examples pure and sandbox-compatible.
- Keep showcases read-only when they require OS, network, file-system, or process access.
- Update this document when example counts, sandbox limits, share format, storage keys, or editor features change.
- Update `Stash.Playground/README.md` when contributor workflow changes.
- Keep Monaco tokenization and analysis behavior aligned with language syntax and standard-library metadata.
- Test generated share links, autosave restore, formatting, diagnostics, and at least one successful and one failing run before release.
