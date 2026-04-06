# Stash — Browser Playground

> **Status:** v1.0
> **Created:** March 2026
> **Purpose:** Interactive browser-based environment for writing, running, and sharing Stash code — no installation required.
>
> **Companion documents:**
>
> - [Language Specification](Stash%20—%20Language%20Specification.md) — language syntax, type system, interpreter architecture
> - [Standard Library Reference](Stash%20—%20Standard%20Library%20Reference.md) — built-in namespace functions
> - [LSP — Language Server Protocol](LSP%20—%20Language%20Server%20Protocol.md) — language server
> - [TAP — Testing Infrastructure](TAP%20—%20Testing%20Infrastructure.md) — built-in test runner
> - [TPL — Templating Engine](TPL%20—%20Templating%20Engine.md) — templating engine

---

## Table of Contents

1. [Overview](#1-overview)
2. [Getting Started](#2-getting-started)
3. [Editor Features](#3-editor-features)
4. [Curated Examples](#4-curated-examples)
5. [Feature Showcases](#5-feature-showcases)
6. [Toolbar & Actions](#6-toolbar--actions)
7. [Sharing & Persistence](#7-sharing--persistence)
8. [Sandbox Model](#8-sandbox-model)
9. [Theme System](#9-theme-system)
10. [Architecture](#10-architecture)
11. [Development](#11-development)

---

## 1. Overview

The Stash Playground is an interactive browser application that lets users write, edit, and execute Stash code directly in the browser. It runs the full Stash interpreter compiled to WebAssembly — no server round-trips, no installation, no sign-up. Code executes entirely client-side.

The playground is designed for learning the language, experimenting with ideas, and sharing code snippets. It provides the same editing experience as a desktop IDE — syntax highlighting, autocomplete, hover documentation, signature help, real-time error diagnostics, and code formatting — all powered by the same analysis engine used by the VS Code extension.

**Key capabilities:**

| Feature                 | Description                                                         |
| ----------------------- | ------------------------------------------------------------------- |
| **Code execution**      | Run Stash scripts instantly with Ctrl+Enter or the Run button       |
| **IntelliSense**        | Autocomplete, hover info, signature help, and real-time diagnostics |
| **Syntax highlighting** | Full Monarch tokenizer with Catppuccin color themes                 |
| **10 curated examples** | Pre-built scripts covering core language features                   |
| **Feature showcases**   | Read-only demonstrations of platform-dependent features             |
| **Share via URL**       | Generate a shareable link that encodes your code in the URL         |
| **Autosave**            | Code is automatically saved to browser storage                      |
| **Format code**         | One-click code formatting using the built-in Stash formatter        |
| **Download**            | Export your code as a `.stash` file                                 |
| **Dark / light themes** | Toggle between Catppuccin Mocha (dark) and Catppuccin Latte (light) |

---

## 2. Getting Started

Open the playground in any modern browser. The editor loads with a "Hello World" example that demonstrates basic Stash features — variables, structs, arrays, and output.

**Running code:**

- Press **Ctrl+Enter** (or **Cmd+Enter** on macOS) to execute the current code
- Or click the **▶ Run** button in the toolbar

Output appears in the panel on the side of the editor, along with execution statistics (elapsed time and step count).

**Exploring the language:**

Use the **Examples** dropdown in the toolbar to load any of the 10 curated examples. Each example is self-contained and demonstrates a different aspect of Stash — from basic types to functional programming to algorithms.

**Writing your own code:**

Simply clear the editor and start typing. The editor provides autocomplete suggestions as you type — press `.` after a namespace name (like `arr`, `str`, `math`) to see all available functions with documentation.

---

## 3. Editor Features

The playground uses the Monaco editor (the same editor that powers VS Code) with full Stash language support.

### Syntax Highlighting

The Monarch tokenizer provides accurate highlighting for all Stash syntax:

- **Keywords** — `let`, `const`, `fn`, `struct`, `enum`, `if`, `else`, `for`, `while`, `return`, `match`, etc.
- **Namespaces** — All 24 built-in namespaces (`arr`, `dict`, `str`, `math`, `time`, `json`, `fs`, `http`, etc.)
- **Types** — `int`, `float`, `string`, `bool`, `array`, `dict`, `Error`
- **Strings** — Regular strings, interpolated strings (`"Hello, ${name}!"`), and triple-quoted strings
- **Comments** — Single-line (`//`) and block comments (`/* */`)
- **Operators** — Null-coalescing (`??`), optional chaining (`?.`), pipeline (`|>`), spread (`..`), arrow (`=>`)

### Autocomplete

Intelligent code completion is triggered automatically as you type:

- **Namespace members** — Type `arr.` to see all array functions with type signatures and documentation
- **Keywords and built-ins** — Suggestions for language keywords, built-in functions, and type names
- **Type completions** — After the `is` keyword, suggests available types for type checking
- **User symbols** — Variables, functions, structs, and enums defined in your code

### Hover Information

Hover over any identifier to see its type, signature, and documentation. Works for namespace functions, built-in types, and user-defined symbols.

### Signature Help

When calling a function, parameter hints appear showing the function's full signature with the current parameter highlighted. Triggered automatically when you type `(` or `,`.

### Real-Time Diagnostics

As you type, the editor runs continuous analysis and underlines errors:

- **Red underlines** — Syntax errors and parse failures
- **Yellow underlines** — Semantic warnings (e.g., referencing an undefined variable)

Diagnostics update in real-time with a short debounce (300ms) so they don't flicker during active typing.

---

## 4. Curated Examples

The playground ships with 10 examples that progressively introduce Stash features:

| #   | Example                 | Concepts                                             |
| --- | ----------------------- | ---------------------------------------------------- |
| 1   | **Hello World**         | Variables, structs, array operations, `io.println`   |
| 2   | **Variables & Types**   | Type system, `typeof`, nullish coalescing (`??`)     |
| 3   | **Structs & Enums**     | Struct definitions, field access, enum values        |
| 4   | **Arrays & Functional** | `filter`, `map`, `reduce`, `find`, `flatten`         |
| 5   | **Dictionaries**        | Key access, `dict.merge`, traversal                  |
| 6   | **Lambdas & Closures**  | Higher-order functions, closures, currying           |
| 7   | **String Processing**   | `trim`, `upper`, `lower`, `split`, `join`, `replace` |
| 8   | **Error Handling**      | Try expressions, error types, safe defaults          |
| 9   | **Pattern Matching**    | Switch expressions, ternary chains, FizzBuzz         |
| 10  | **Algorithms**          | Quicksort, Fibonacci, statistics                     |

Select any example from the dropdown in the toolbar. Each example replaces the current editor content and can be run immediately.

---

## 5. Feature Showcases

Some Stash features require operating system access and cannot run in a browser sandbox — shell commands, file system operations, HTTP requests, and process management. The playground includes **read-only showcase cards** for these features, each displaying representative code alongside pre-rendered output.

Showcases are collapsible and appear below the main editor. They give users a preview of what Stash can do on a real system without pretending these features work in the browser.

| Showcase                    | What it demonstrates                                  |
| --------------------------- | ----------------------------------------------------- |
| **Shell Command Execution** | `$(...)` syntax for running system commands           |
| **File System Operations**  | `fs.readFile`, `fs.writeFile`, directory operations   |
| **HTTP Requests**           | `http.get`, `http.post`, API integration              |
| **Process Management**      | `process.exec`, `process.spawn`, background processes |

---

## 6. Toolbar & Actions

The toolbar sits at the top, on the right hand side of the screen, providing quick access to common actions:

| Button         | Shortcut   | Description                                                |
| -------------- | ---------- | ---------------------------------------------------------- |
| **⚡ Format**  | —          | Formats the editor code using the built-in Stash formatter |
| **⬇ Download** | —          | Downloads the current code as `script.stash`               |
| **🔗 Share**   | —          | Copies a shareable URL to the clipboard                    |
| **▶ Run**      | Ctrl+Enter | Executes the code and displays the output                  |

The Share button generates a URL with the code encoded in the hash fragment. When someone opens the link, the playground automatically loads the shared code.

The output panel includes a **📋 Copy** button for copying execution results to the clipboard.

---

## 7. Sharing & Persistence

### Share via URL

Click **🔗 Share** to generate a link like:

```
https://playground.stash-lang.org/#code=bGV0IHggPSA0MjsKaW8ucHJpbnRsbih4KTs=
```

The code is Base64-encoded in the URL hash fragment. When someone opens this link, the playground decodes the hash and loads the code into the editor. This approach keeps everything client-side — no server storage, no expiration, no accounts.

URL hash takes priority: if a share link is opened, the shared code loads regardless of what was previously autosaved.

### Autosave

The playground automatically saves your code to the browser's `localStorage` every time the content changes (with a 300ms debounce). When you return to the playground later, your last code is restored automatically.

**Priority order on page load:**

1. URL hash code (from a share link) — highest priority
2. localStorage saved code — restored if no hash is present
3. Default "Hello World" example — used on first visit

---

## 8. Sandbox Model

Code runs in a restricted sandbox to ensure safety and responsiveness in the browser environment.

| Guard            | Limit                    | Purpose                                                  |
| ---------------- | ------------------------ | -------------------------------------------------------- |
| **Capabilities** | `StashCapabilities.None` | Disables shell, file system, network, and process access |
| **Step limit**   | 5,000,000 steps          | Prevents infinite loops from freezing the browser tab    |
| **Output cap**   | 512 KB                   | Prevents memory exhaustion from excessive output         |

When the step limit is exceeded, execution stops and an error message is displayed. The output cap silently truncates output beyond 512 KB.

**What works in the sandbox:**

All pure computation — variables, functions, structs, enums, arrays, dictionaries, string operations, math, pattern matching, closures, error handling, type checking, and all functional programming features.

**What doesn't work in the sandbox:**

Anything requiring OS access — shell commands (`$(...)`), file I/O (`fs.*`), HTTP requests (`http.*`), process management (`process.*`), and environment variables (`env.*`). These features are demonstrated in the [Feature Showcases](#5-feature-showcases) instead.

---

## 9. Theme System

The playground supports two color themes, toggled via the button in the top bar:

| Theme                | Background | Style       |
| -------------------- | ---------- | ----------- |
| **Catppuccin Mocha** | `#1e1e2e`  | Dark theme  |
| **Catppuccin Latte** | `#eff1f5`  | Light theme |

Both themes apply consistently to the entire UI — editor, output panel, toolbar, showcases, and all controls. The Monaco editor uses custom theme definitions that map Stash token types to Catppuccin palette colors (Mauve for keywords, Green for strings, Sky for operators, Peach for numbers, etc.).

---

## 10. Architecture

The playground is a **Blazor WebAssembly** application that compiles the Stash interpreter to WebAssembly and runs it entirely in the browser.

### Technology Stack

| Layer                | Technology                                                 |
| -------------------- | ---------------------------------------------------------- |
| **UI framework**     | Blazor WebAssembly (.NET 10)                               |
| **Code editor**      | Monaco Editor via BlazorMonaco 3.4.0                       |
| **Language support** | Monarch tokenizer (syntax) + Stash.Analysis (IntelliSense) |
| **Execution**        | Stash.Core (lexer/parser) + Stash.Bytecode (bytecode VM)   |
| **Formatter**        | StashFormatter from Stash.Analysis                         |

### How It Works

The application compiles three Stash projects to WebAssembly:

1. **Stash.Core** — The lexer and recursive-descent parser, producing AST nodes
2. **Stash.Bytecode** — The bytecode VM that compiles and executes AST nodes
3. **Stash.Analysis** — The analysis engine that provides diagnostics, completions, hover info, signature help, and formatting

When the user clicks Run, the Blazor component calls `PlaygroundExecutor`, which creates a fresh `StashEngine` instance with sandbox restrictions (`StashCapabilities.None`, step limit, output cap), compiles and executes the code, and returns the captured output.

IntelliSense features work through `PlaygroundAnalyzer` — a static C# service with `[JSInvokable]` methods that the Monaco editor's JavaScript providers call via .NET interop. The analysis engine parses the code, resolves symbols, and returns structured results (diagnostics, completions, hover info, signatures) that the JavaScript layer transforms into Monaco-compatible objects.

### Project Structure

```
Stash.Playground/
├── Pages/
│   └── Playground.razor          # Main UI — editor, toolbar, output, examples, showcases
├── Services/
│   ├── PlaygroundExecutor.cs     # Sandboxed script execution
│   ├── PlaygroundAnalyzer.cs     # IntelliSense bridge (C# ↔ JS interop)
│   ├── PlaygroundResult.cs       # Execution result model
│   └── CappedStringWriter.cs    # Output size limiter
├── wwwroot/
│   ├── index.html                # Host page with WASM loader
│   ├── css/app.css               # Dual-theme styles (Catppuccin Mocha/Latte)
│   └── js/stash-language.js      # Monarch tokenizer, Monaco providers, QoL helpers
├── Layout/
│   └── MainLayout.razor          # App shell
├── Program.cs                    # Blazor WASM entry point
└── Stash.Playground.csproj       # Project config (BlazorWebAssembly SDK)
```

### Data Flow

```
User types code
    │
    ├──→ Monaco onChange (300ms debounce)
    │       ├──→ JS calls PlaygroundAnalyzer.GetDiagnostics() via DotNet.invokeMethodAsync
    │       │       └──→ Returns markers → Monaco renders squiggles
    │       └──→ Code saved to localStorage
    │
    ├──→ User triggers autocomplete (typing or '.')
    │       └──→ JS calls PlaygroundAnalyzer.GetCompletions()
    │               └──→ Returns suggestions → Monaco shows completion menu
    │
    ├──→ User hovers over identifier
    │       └──→ JS calls PlaygroundAnalyzer.GetHover()
    │               └──→ Returns docs → Monaco shows tooltip
    │
    └──→ User clicks Run (or Ctrl+Enter)
            └──→ Blazor calls PlaygroundExecutor.Execute()
                    ├──→ Lexer → Parser → Interpreter (sandboxed)
                    └──→ Returns output + stats → rendered in output panel
```

---

## 11. Development

### Build & Run

```bash
# Build the playground (and all dependencies)
dotnet build Stash.Playground/

# Run the development server (hot reload enabled)
dotnet watch run --project Stash.Playground/

# Publish for deployment
dotnet publish Stash.Playground/ -c Release
```

The dev server starts at `http://localhost:5111` by default.

### Adding Examples

Examples are defined in `Playground.razor` as a list of `PlaygroundExample` records. Each example has a name, description, and code string. To add a new example:

1. Add a new entry to the `Examples` list in `Playground.razor`
2. Ensure the code runs successfully in the sandbox (no OS-dependent features)
3. Keep examples focused — each should demonstrate one concept clearly

### Adding Showcases

Showcases are defined in the same file as collapsible cards. Each has a title, description, code string, and pre-rendered output string. Showcases are for features that **cannot** run in the browser sandbox — they display code and expected output side by side without execution.

### Theme Customization

Theme colors are defined as CSS custom properties in `app.css` using the Catppuccin palette. The Monaco editor themes are defined in `stash-language.js`. Both must be updated together when changing the color scheme.
