# Stash Playground — Design Spec

## 1. Executive Summary

This document specifies the design for an interactive playground that lets people **run Stash code in their browser** without installing anything. The playground is a standalone Blazor WebAssembly application hosted on GitHub Pages at `stash-lang.org`. Documentation will be a separate project; this spec focuses exclusively on the playground.

**The interpreter is playground-ready today.** It has full I/O abstraction (`TextWriter`/`TextReader`), a capability gating system (`StashCapabilities.None`) that disables all OS-dependent namespaces, `CancellationToken` support with step limits for execution control, and 22+ pure-computation namespaces that work without any system access. Trimming is safe for all components except the `yaml` namespace (SharpYaml uses reflection), which can be excluded or worked around.

---

## 2. Decisions

| #   | Question                        | Decision                                                                                                                                          |
| --- | ------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------- |
| 1   | Blazor standalone vs. embedded? | **Standalone Blazor WASM app** — playground only. Docs site is a separate project.                                                                |
| 2   | Server-side execution?          | **No.** Client-side only. Users who want `$(...)`, `fs`, `http` can install Stash — it's open source.                                             |
| 3   | Simulated `$(...)` commands?    | **No simulation.** Too much hassle. Show non-interactive code examples for system features instead.                                               |
| 4   | Code sharing?                   | **Out of scope** for now.                                                                                                                         |
| 5   | Example curation?               | Focus on **language strengths vs. competition**: data structures, lambdas, closures, structs, enums, error handling, templates, pattern matching. |
| 6   | Multi-file / imports?           | **No.** Single-file only. Showcase imports via non-interactive examples with an explanation that this is a playground limitation.                 |
| 7   | Threading (`task` namespace)?   | Depends on GitHub Pages COOP/COEP header support — if supported, enable; otherwise disable and note as playground limitation.                     |
| 8   | CancellationToken support?      | **Already fully implemented.** `CancellationToken` at every statement boundary + `StepLimit` for instruction counting. No changes needed.         |
| 9   | IL trimming compatibility?      | **Safe**, except `yaml` namespace (SharpYaml uses reflection). SSH.NET is irrelevant (disabled via capability gating). See §4.3 for details.      |
| 10  | Mobile support?                 | **Desktop only** for now.                                                                                                                         |
| 11  | Domain?                         | `stash-lang.org`                                                                                                                                  |
| 12  | Visual identity?                | **None exists.** Needs to be created from scratch (logo, color palette, typography).                                                              |
| 13  | Docs integration?               | **Separate sites.** Playground at `stash-lang.org`, docs site is a future project.                                                                |
| 14  | Repository?                     | **Same repo** (`stash-lang`). The playground references `Stash.Core` and `Stash.Interpreter` directly.                                            |
| 15  | License?                        | **GPL-3.0** — same as the project.                                                                                                                |

---

## 3. Scope

### What the Playground Is

A single-page application where a user can:

1. Write Stash code in a browser-based editor (Monaco)
2. Run it instantly (client-side, no server)
3. See stdout/stderr output and errors with source locations
4. Pick from curated examples that showcase the language's strengths
5. See non-interactive code showcases for system features that can't run in the browser

### What the Playground Is NOT

- Not a documentation site (separate project)
- Not a package manager or registry UI
- Not a multi-file IDE or project workspace
- Not a mobile-first experience

---

## 4. Technical Architecture

### 4.1 Execution Model: Blazor WebAssembly (Client-Side)

`Stash.Core` (lexer/parser) and `Stash.Interpreter` compile to WebAssembly via Blazor WASM. The entire interpreter runs in the user's browser.

```
Browser
├── Blazor WASM App
│   ├── Monaco Editor (code input)
│   ├── Output Panel (stdout, stderr, errors)
│   ├── Example Selector (curated examples)
│   └── Showcase Panel (non-interactive system feature demos)
├── Stash WASM Runtime
│   ├── Stash.Core.dll (Lexer, Parser)
│   ├── Stash.Interpreter.dll (Interpreter, BuiltIns)
│   └── .NET WASM runtime (~5-15 MB, cached)
└── PlaygroundExecutor (bridge)
```

**Why this works today — no core changes needed:**

- **I/O is fully abstracted.** The interpreter uses `TextWriter Output` / `TextReader Input` properties on `ExecutionContext`. Inject `StringWriter`s to capture output and route to the browser UI.
- **Capability gating exists.** `new Interpreter(StashCapabilities.None)` disables all OS-dependent namespaces (`fs`, `http`, `process`, `env`, `ssh`, `sftp`), leaving 22+ safe namespaces.
- **CancellationToken is fully supported.** The interpreter checks `CancellationToken.IsCancellationRequested` at every statement boundary and throws `ScriptCancelledException`. Infinite loops won't freeze the browser.
- **Step limits exist.** `interpreter.StepLimit = 1_000_000` caps execution at N statements, throwing `StepLimitExceededException`. Perfect for preventing runaway scripts.
- **.NET 10 supports Blazor WASM.** The `wasm-tools` workload compiles .NET IL to WebAssembly. AOT compilation is optional (IL interpreter works out of the box, AOT improves runtime performance).

### 4.2 Available Namespaces in the Playground

**Enabled (22+ namespaces):**

| Category          | Namespaces                                                                  |
| ----------------- | --------------------------------------------------------------------------- |
| Data structures   | `arr`, `dict`, `str`                                                        |
| Math & conversion | `math`, `conv`                                                              |
| Serialization     | `json`, `toml`, `ini`, `config`                                             |
| Crypto & encoding | `crypto`, `encoding`                                                        |
| Templates         | `tpl`                                                                       |
| Utilities         | `io` (print/println), `log`, `store`, `term`, `time`, `path`, `sys`, `args` |
| Testing           | `test` (assert, describe)                                                   |
| Core              | `global` (typeof, len, hash, range, etc.)                                   |
| Concurrency       | `task` — contingent on GitHub Pages COOP/COEP header support (see §4.4)     |

**Disabled (via `StashCapabilities.None`):**

| Namespace                  | Reason                       |
| -------------------------- | ---------------------------- |
| `fs`                       | No filesystem in browser     |
| `http`                     | No server-side fetch         |
| `process`                  | No process spawning          |
| `env`                      | No environment variables     |
| `ssh` / `sftp`             | No SSH in browser            |
| `$(...)` command execution | Requires OS process spawning |

These disabled features are instead showcased via **non-interactive code examples** with output annotations, so users can still see what the language offers. Each showcase includes a note: _"This feature requires a local Stash installation. [Download Stash →]"_

**Special case — `yaml`:** See §4.3.

### 4.3 IL Trimming Compatibility

The .NET IL trimmer removes unused code to reduce WASM download size. Investigation confirms the interpreter is **trimming-safe** with one exception:

| Component                | Status     | Notes                                                                               |
| ------------------------ | ---------- | ----------------------------------------------------------------------------------- |
| Stash.Core               | **Safe**   | Uses source-generated JSON serialization (`[JsonSerializable]`)                     |
| Stash.Interpreter (core) | **Safe**   | No reflection; `GetType()` only in error messages                                   |
| `json` namespace         | **Safe**   | Source-generated serialization via `StashJsonContext`                               |
| `toml` namespace         | **Safe**   | Concrete type deserialization only; trimming suppression attributes already applied |
| `yaml` namespace         | **Unsafe** | SharpYaml v3.5.0 is reflection-based; passes `native?.GetType()` to serializer      |
| SSH.NET                  | **N/A**    | Disabled via capability gating — never loaded                                       |

**Decision for `yaml`:** Three options:

1. **Exclude `yaml` namespace from WASM build** — simplest, minimal impact (users rarely need YAML parsing in a playground)
2. **Replace SharpYaml** with a trimming-safe YAML parser — higher effort, benefits the whole project
3. **Suppress trimming warnings and test** — SharpYaml may work if only Stash-native types (strings, numbers, lists, dicts) are serialized

Recommendation: Option 1 (exclude from WASM) for the initial release; revisit later if demand exists.

### 4.4 Threading in WASM (GitHub Pages)

`task.run()` and `arr.parMap()` use the .NET ThreadPool, which requires `SharedArrayBuffer` in the browser. This in turn requires the hosting server to send:

- `Cross-Origin-Opener-Policy: same-origin`
- `Cross-Origin-Embedder-Policy: require-corp`

**GitHub Pages does NOT support custom response headers.** Options:

1. **Cloudflare proxy** in front of GitHub Pages — can inject headers via Cloudflare Workers or Transform Rules. Adds slight complexity but is free-tier compatible.
2. **Disable `task` namespace** in the playground and note it as a limitation.
3. **Use a different host** that supports custom headers (Cloudflare Pages, Netlify, Vercel all support this natively).

**Recommendation:** If sticking strictly to GitHub Pages, disable `task` and list it as a playground limitation. If willing to put Cloudflare in front (or use Cloudflare Pages directly), threading can work. This is a minor decision that can be deferred to implementation time.

### 4.5 Estimated Download Size

| Component                     | Approx. Size   |
| ----------------------------- | -------------- |
| .NET WASM runtime (trimmed)   | ~5–8 MB        |
| Stash.Core.dll                | ~200–400 KB    |
| Stash.Interpreter.dll         | ~800 KB–1.5 MB |
| Blazor framework              | ~500 KB        |
| Monaco Editor                 | ~2–3 MB        |
| **Total (compressed/brotli)** | **~4–7 MB**    |

With Brotli compression and aggressive trimming, the initial download is comparable to loading a complex web app (~4–7 MB). Subsequent visits use the browser cache.

**Tradeoffs:**

- First load: 2–5 seconds (download + WASM init)
- Subsequent loads: near-instant (cached)
- WASM AOT compilation (optional): improves runtime speed ~2-3x but increases download size ~2x. Can be tested empirically.

---

## 5. Technology Stack

| Layer                   | Technology                                             | Rationale                                                           |
| ----------------------- | ------------------------------------------------------ | ------------------------------------------------------------------- |
| **Application**         | Blazor WASM (standalone)                               | Single C#/.NET stack, same ecosystem as the interpreter             |
| **Code editor**         | BlazorMonaco (Monaco wrapper)                          | Full-featured editor; TextMate grammar reuse from VS Code extension |
| **Syntax highlighting** | Existing `stash.tmLanguage.json`                       | Already defined in `.vscode/extensions/stash-lang/`                 |
| **Styling**             | Tailwind CSS or a Blazor component library (MudBlazor) | Modern, responsive, minimal effort                                  |
| **Hosting**             | GitHub Pages (`stash-lang.org`)                        | Free, CDN-backed, zero ops                                          |
| **Build**               | `dotnet publish` with WASM tooling                     | Integrated into existing build infrastructure                       |

---

## 6. Project Structure

The playground lives in the main repo as a new project:

```
Stash.Playground/
├── Stash.Playground.csproj          (Blazor WASM standalone app)
├── Program.cs                       (app entry point)
├── wwwroot/
│   ├── index.html                   (SPA shell)
│   ├── css/
│   │   └── app.css                  (playground styles)
│   └── examples/                    (curated .stash files loaded at runtime)
│       ├── 01-hello-world.stash
│       ├── 02-data-structures.stash
│       ├── ...
│       └── showcases/               (non-interactive system feature demos)
│           ├── commands.stash
│           ├── filesystem.stash
│           └── http.stash
├── Layout/
│   └── MainLayout.razor             (app shell, header, footer)
├── Pages/
│   └── Playground.razor             (main playground page)
├── Components/
│   ├── Editor.razor                 (Monaco editor wrapper)
│   ├── OutputPanel.razor            (execution results: stdout, stderr, errors)
│   ├── ExampleSelector.razor        (sidebar/dropdown to pick examples)
│   ├── ShowcasePanel.razor          (non-interactive code + annotated output)
│   └── PlaygroundLimitations.razor  (info box: what's disabled and why)
└── Services/
    └── PlaygroundExecutor.cs        (sandboxed interpreter bridge)
```

### 6.1 PlaygroundExecutor

The bridge between the UI and the interpreter:

```csharp
public class PlaygroundExecutor
{
    private const long DefaultStepLimit = 5_000_000;
    private static readonly TimeSpan ExecutionTimeout = TimeSpan.FromSeconds(10);

    public PlaygroundResult Execute(string code)
    {
        var output = new StringWriter();
        var errors = new StringWriter();

        using var cts = new CancellationTokenSource(ExecutionTimeout);

        var interpreter = new Interpreter(StashCapabilities.None);
        interpreter.Output = output;
        interpreter.ErrorOutput = errors;
        interpreter.CancellationToken = cts.Token;
        interpreter.StepLimit = DefaultStepLimit;

        try
        {
            var lexer = new Lexer(code, "<playground>");
            List<Token> tokens = lexer.ScanTokens();

            if (lexer.Errors.Count > 0)
                return PlaygroundResult.LexerErrors(lexer.Errors);

            var parser = new Parser(tokens);
            List<Stmt> statements = parser.Parse();

            if (parser.Errors.Count > 0)
                return PlaygroundResult.ParserErrors(parser.Errors);

            interpreter.Interpret(statements);

            return new PlaygroundResult
            {
                Output = output.ToString(),
                Errors = errors.ToString(),
                StepCount = interpreter.StepCount,
            };
        }
        catch (ScriptCancelledException)
        {
            return PlaygroundResult.Timeout(output.ToString());
        }
        catch (StepLimitExceededException)
        {
            return PlaygroundResult.StepLimitExceeded(output.ToString());
        }
        catch (RuntimeError ex)
        {
            return PlaygroundResult.RuntimeError(output.ToString(), ex);
        }
    }
}
```

---

## 7. UI Design

### 7.1 Layout

Desktop-only, single page:

```
┌──────────────────────────────────────────────────────────────┐
│  Stash Playground                    [Examples ▼]  [Run ▶]  │
├────────────────────────────────┬─────────────────────────────┤
│                                │                             │
│                                │  Output                     │
│  Monaco Editor                 │  ─────────                  │
│  (code input)                  │  Hello, World!              │
│                                │  Sum: 55                    │
│                                │                             │
│                                ├─────────────────────────────┤
│                                │  ℹ Execution: 0.3ms         │
│                                │    142 steps                │
├────────────────────────────────┴─────────────────────────────┤
│  ⚠ Playground limitations: $(cmd), fs, http, ssh, env,      │
│    and import are not available. Install Stash to try them → │
└──────────────────────────────────────────────────────────────┘
```

### 7.2 Features

| Feature                | Description                                                                |
| ---------------------- | -------------------------------------------------------------------------- |
| **Run button**         | Executes code via `PlaygroundExecutor`, displays output                    |
| **Keyboard shortcut**  | Ctrl+Enter / Cmd+Enter to run                                              |
| **Example selector**   | Dropdown loads curated examples into the editor                            |
| **Output panel**       | Shows stdout, stderr, error messages with line/column info                 |
| **Execution stats**    | Time elapsed, step count                                                   |
| **Error highlighting** | Errors reference source spans — highlight the offending line in the editor |
| **Limitations banner** | Persistent but dismissible info about what's disabled                      |
| **Showcase tab**       | Non-interactive examples showing system features with annotated output     |
| **Dark/light theme**   | Toggle; store preference in localStorage                                   |
| **Loading state**      | Spinner during WASM initialization on first visit                          |

### 7.3 Non-Interactive Showcases

For features that can't run in the browser (`$(...)`, `fs`, `http`), display read-only code with pre-rendered output:

```
┌─ Showcase: Shell Command Execution ──────────────────────────┐
│                                                              │
│  // Stash has first-class shell integration                  │
│  let result = $(ls -la /tmp);                                │
│  println(result.stdout);                                     │
│                                                              │
│  // Pipe commands naturally                                  │
│  let count = $(ps aux) | $(grep nginx) | $(wc -l);           │
│  println($"Nginx processes: {count.stdout}");                │
│                                                              │
│  ─── Example Output ───                                      │
│  drwxrwxrwt 12 root root 4096 Mar 27 10:00 .                 │
│  -rw-r--r--  1 user user  512 Mar 27 09:55 script.stash      │
│  Nginx processes: 3                                          │
│                                                              │
│  ⓘ This feature requires a local installation.              │
│    [Download Stash →]                                        │
└──────────────────────────────────────────────────────────────┘
```

---

## 8. Curated Examples Strategy

The playground's examples should highlight **what makes Stash different** — the selling points vs. the competition. The existing 30 examples in `examples/` mostly rely on system features. New playground-specific examples are needed.

### 8.1 Proposed Example Categories

**Data Structures & Types** (Stash advantage: real data structures unlike Bash)

- Structs with methods
- Enums with pattern matching
- Dictionaries: creation, merging, nested access
- Arrays: map, filter, reduce, sort, parMap (if threading works)

**Functions & Closures** (Stash advantage: first-class functions unlike Bash)

- Lambdas and higher-order functions
- Closures capturing variables
- Default parameters
- Spread operator

**Error Handling** (Stash advantage: `try` expressions, `??` operator, typed errors)

- `try` expression capturing errors
- `??` null coalescing chains
- Error types with `.type`, `.message`, `.stack`
- `throw` with custom error types

**String Interpolation & Templates** (Stash advantage: built-in Jinja2-style templating)

- String interpolation with expressions
- Template rendering with filters
- Template composition

**Pattern Matching** (Stash advantage: switch expressions)

- Switch expressions with pattern matching
- Enum matching

**Serialization** (Stash advantage: built-in JSON, TOML, INI parsing)

- JSON encode/decode round-trip
- TOML parsing
- INI file parsing
- Config auto-detection

**Testing** (Stash advantage: built-in TAP framework)

- `test.it()` and `test.describe()` blocks
- Assertion functions
- Test organization

**Crypto & Encoding** (Stash advantage: built-in, no external deps)

- Hashing (MD5, SHA256)
- Base64, hex encoding
- HMAC

**Algorithms** (General language demonstration)

- Fibonacci, factorial
- Sorting algorithms
- Data processing pipelines

### 8.2 Non-Interactive Showcases

| Feature            | Why it matters                 | Example focus                      |
| ------------------ | ------------------------------ | ---------------------------------- |
| `$(...)` commands  | Core differentiator vs. Python | Shell piping, capture, exit codes  |
| `fs` namespace     | Sysadmin utility               | File read/write, directory listing |
| `http` namespace   | API automation                 | REST calls, JSON response handling |
| `process` spawning | Service management             | Process control, signal handling   |
| Imports / modules  | Code organization              | Multi-file project structure       |

---

## 9. Visual Identity

**Current state:** No logo, no color palette, no typography guidelines. Everything needs to be created from scratch.

**What's needed:**

| Asset            | Purpose                                  | Notes                                                             |
| ---------------- | ---------------------------------------- | ----------------------------------------------------------------- |
| Logo             | Brand identity, favicon, social previews | Should convey "programming language for sysadmin"                 |
| Color palette    | UI theming, dark/light modes             | Consider terminal-inspired aesthetics given the sysadmin audience |
| Typography       | Headings, body, code                     | Monospace for code (already handled by Monaco); sans-serif for UI |
| Favicon          | Browser tab icon                         | Derived from logo                                                 |
| Open Graph image | Social media link previews               | Logo + tagline                                                    |

**Recommendations:**

- Keep it minimal and professional — sysadmins value function over flash
- Terminal/CLI aesthetic could work well (think dark background, green/amber accents)
- The logo could incorporate terminal brackets `$()`, the `>` prompt symbol, or curly braces — tying back to the language's identity as a shell-meets-programming-language

This is a creative task that should probably be iterated on separately before baking it into the playground UI.

---

## 10. Hosting & Deployment

| Requirement    | Solution                              | Cost          |
| -------------- | ------------------------------------- | ------------- |
| Static hosting | GitHub Pages                          | Free          |
| Custom domain  | `stash-lang.org`                      | ~$12/year     |
| CDN / SSL      | Included with GitHub Pages            | Free          |
| CI/CD          | GitHub Actions (build + publish WASM) | Free          |
| **Total**      |                                       | **~$1/month** |

### 10.1 GitHub Actions Workflow

```yaml
# .github/workflows/playground.yml
name: Deploy Playground
on:
  push:
    branches: [main]
    paths: ["Stash.Playground/**", "Stash.Core/**", "Stash.Interpreter/**"]

jobs:
  deploy:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: "10.0.x"
      - run: dotnet workload install wasm-tools
      - run: dotnet publish Stash.Playground/ -c Release -o release
      - uses: peaceiris/actions-gh-pages@v4
        with:
          github_token: ${{ secrets.GITHUB_TOKEN }}
          publish_dir: ./release/wwwroot
```

### 10.2 Custom Domain Setup

1. Register `stash-lang.org`
2. Add CNAME record pointing to `<user>.github.io`
3. Add `CNAME` file to `wwwroot/` with `stash-lang.org`
4. Enable HTTPS in GitHub Pages settings

---

## 11. Comparable Playgrounds (Reference)

| Language       | URL                     | Approach           | Download Size | Notes                                |
| -------------- | ----------------------- | ------------------ | ------------- | ------------------------------------ |
| **TypeScript** | typescriptlang.org/play | Client-side        | ~4 MB         | Closest model to what we're building |
| **Lua**        | lua.org/demo.html       | Client-side (WASM) | ~200 KB       | Very small runtime                   |
| **Go**         | go.dev/play             | Server-side        | N/A           | Google Cloud infrastructure          |
| **Rust**       | play.rust-lang.org      | Server-side        | N/A           | Docker containers                    |
| **Kotlin**     | play.kotlinlang.org     | Server-side        | N/A           | JetBrains-hosted                     |
| **C#**         | sharplab.io             | Server-side        | N/A           | Full .NET compilation                |

The TypeScript Playground is the closest analog: a client-side compiler/runtime running in the browser with Monaco Editor. Our approach is architecturally identical.

---

## 12. Remaining Open Questions

Most questions from the initial analysis have been answered (see §2). These remain for future investigation:

| #   | Question                         | Status                | Context                                                                                           |
| --- | -------------------------------- | --------------------- | ------------------------------------------------------------------------------------------------- |
| 1   | **WASM AOT vs. IL interpreter?** | 🔍 Investigate later  | AOT improves runtime speed but doubles download size. Needs empirical testing after Phase 2.       |
| 2   | **Exact step limit value?**      | 🔍 Investigate later  | 5,000,000 is set. Calibrate against real examples once curated examples exist.                     |
| 3   | **`yaml` namespace handling?**   | 🔍 Investigate later  | SharpYaml survives publish (not excluded yet). Evaluate if trimming breaks it at runtime.          |
| 4   | **Threading headers?**           | 🔍 Investigate later  | CancellationToken removed in Phase 1 (non-functional in single-threaded WASM). Revisit if threading is needed. |
| 5   | **Visual identity**              | 🔍 Investigate later  | No logo/colors/typography yet. Design in parallel with or after Phase 2.                          |
| 6   | **BouncyCastle size (2.1 MB)**   | 🔍 Investigate later  | `crypto` namespace pulls BouncyCastle (48% of 4.3 MB payload). Consider excluding or lazy-loading. |

---

## 13. Implementation Plan

```
Phase 1 — Proof of Concept ✅
├── ✅ Create Stash.Playground project (Blazor WASM)
├── ✅ Reference Stash.Core + Stash.Interpreter
├── ✅ Build PlaygroundExecutor with StepLimit (5M steps)
├── ✅ Verify WASM build succeeds (4.3 MB compressed)
├── ✅ CappedStringWriter (512 KB output limit)
├── ✅ Minimal UI: textarea + output div (no Monaco yet)
├── ✅ Review: isRunning stuck-state fix, OperationCanceledException catch
└── ✅ Full test suite passes (3,208 tests, 0 regressions)

Phase 2 — Core Playground ✅
├── ✅ Integrate Monaco Editor (BlazorMonaco)
├── ✅ Port stash.tmLanguage.json for syntax highlighting
├── ✅ Build example selector + load curated examples
├── ✅ Build output panel (stdout, stderr, errors with spans)
├── ✅ Add execution stats (time, steps)
├── ✅ Add dark/light theme toggle
└── ✅ Keyboard shortcut (Ctrl+Enter to run)

Phase 3 — Polish ✅
├── ✅ Non-interactive showcases for system features (4 showcases: shell, fs, http, process)
├── ✅ Playground limitations banner (dismissible info banner)
├── ✅ Loading state / WASM initialization spinner (CSS-only animated spinner)
├── GitHub Actions deployment pipeline (deferred, will handle separately)
├── Custom domain setup (stash-lang.org) (deferred, will handle separately)
└── ✅ README + contributing guide for the playground
```
