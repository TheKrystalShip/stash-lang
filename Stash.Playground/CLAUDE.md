# Playground Guidelines

The Stash Playground is a standalone Blazor WebAssembly app that runs the Stash interpreter entirely in the browser. Users write code in a Monaco editor with syntax highlighting and see output instantly — no server, no installation. See `docs/specs/Website & Playground — Feasibility Analysis.md` for the full design spec.

## Project Structure

```
Stash.Playground/
├── Program.cs                       → Entry point: registers PlaygroundExecutor as scoped service
├── App.razor                        → Router shell
├── _Imports.razor                   → Global usings (BlazorMonaco, Services, Layout)
├── Layout/
│   └── MainLayout.razor             → Minimal layout wrapper
├── Pages/
│   └── Playground.razor             → Main UI: Monaco editor, examples, output, showcases, theme toggle
├── Services/
│   ├── PlaygroundExecutor.cs        → Sandboxed interpreter bridge
│   ├── PlaygroundResult.cs          → Execution result DTO
│   └── CappedStringWriter.cs        → TextWriter with 512 KB output cap
└── wwwroot/
    ├── index.html                   → SPA shell + loading spinner + Monaco/BlazorMonaco script loading
    ├── css/app.css                  → Dual-theme CSS (Catppuccin Mocha dark / Latte light)
    └── js/stash-language.js         → Monarch tokenizer, themes, JS interop helpers
```

**Dependencies:** `BlazorMonaco` 3.4.0 (Monaco Editor wrapper), `Stash.Analysis`, `Stash.Bytecode`, `Stash.Core`, `Stash.Stdlib` project references.

## Sandbox Model

The playground uses `StashEngine(StashCapabilities.None)` to disable all OS-dependent features:

| Guard                | Value     | Purpose                                              |
| -------------------- | --------- | ---------------------------------------------------- |
| `StashCapabilities`  | `None`    | Disables fs, http, env, process, ssh, shell commands |
| `StepLimit`          | 5,000,000 | Prevents infinite loops                              |
| `CappedStringWriter` | 512 KB    | Prevents output memory exhaustion                    |

**CancellationToken does NOT work** in single-threaded WASM — JS `setTimeout` can't fire during synchronous .NET execution. `StepLimit` is the sole runaway-prevention guard.

**Available namespaces:** arr, dict, str, math, conv, json, toml, ini, config, crypto, encoding, tpl, io, log, store, term, time, path, sys, args, test/assert, and global builtins.

**Disabled:** fs, http, env, process, ssh, `$(...)` commands, `import`.

## Key Patterns

### PlaygroundExecutor

Creates a fresh `StashEngine` per execution. Never reuse engine instances across runs.

```csharp
var engine = new StashEngine(StashCapabilities.None);
engine.Output = new CappedStringWriter(MaxOutputLength);
engine.ErrorOutput = errors;
engine.StepLimit = DefaultStepLimit;
ExecutionResult result = engine.Run(code);
```

Catch these exception types: `StepLimitExceededException`, `ScriptCancelledException`, `OperationCanceledException`, `RuntimeError`.

### Monaco Editor (BlazorMonaco)

- Component: `<StandaloneCodeEditor>` with `ConstructionOptions` callback
- Language: `"stash"` (registered via Monarch tokenizer in `stash-language.js`)
- Themes: `"stash-dark"` (Catppuccin Mocha), `"stash-light"` (Catppuccin Latte)
- Switch theme at runtime: `await BlazorMonaco.Editor.Global.SetTheme(JSRuntime, themeName)`
- Get code: `await _editor.GetValue()`
- Set code: `await _editor.SetValue(newCode)`

### Keyboard Shortcut (Ctrl+Enter)

Registered via Monaco's `addCommand` API (not `OnKeyUp` — that fires after Monaco processes the key, causing unwanted line insertions). The JS bridge calls a `[JSInvokable]` C# method:

```javascript
// In stash-language.js
function addRunCommand(editorInstance, dotnetHelper) {
  editorInstance.addCommand(
    monaco.KeyMod.CtrlCmd | monaco.KeyCode.Enter,
    function () {
      dotnetHelper.invokeMethodAsync("RunFromKeyboard");
    },
  );
}
```

### Theme System

Dual-theme via CSS classes on `<body>`:

- `body.theme-dark` — Catppuccin Mocha colors
- `body.theme-light` — Catppuccin Latte colors
- Toggle via: `await JSRuntime.InvokeVoidAsync("setPlaygroundTheme", isDark)` + Monaco `SetTheme`
- Default body has no class initially; `OnAfterRenderAsync` syncs it on first render

### Script Loading Order (index.html)

Monaco scripts must load BEFORE `blazor.webassembly.js`:

```html
<script src="_content/BlazorMonaco/jsInterop.js"></script>
<script>
  var require = { paths: { vs: "..." } };
</script>
<script src="_content/BlazorMonaco/lib/monaco-editor/min/vs/loader.js"></script>
<script src="_content/BlazorMonaco/lib/monaco-editor/min/vs/editor/editor.main.js"></script>
<script src="js/stash-language.js"></script>
<script>
  registerStashLanguage();
</script>
<script src="_framework/blazor.webassembly.js"></script>
```

## Syntax Highlighting (Monarch Tokenizer)

`stash-language.js` contains the Monarch tokenizer ported from the TextMate grammar at `.vscode/extensions/stash-lang/syntaxes/stash.tmLanguage.json`. When adding new Stash language features:

1. Update the TextMate grammar in the VS Code extension (source of truth)
2. Port the changes to the Monarch tokenizer in `stash-language.js`

**Important:** Regular strings (`"..."`) and plain triple strings (`"""..."""`) are NOT interpolated — only `$"..."` and `$"""..."""` support `{expr}` interpolation. The tokenizer states `string` and `tripleString` must NOT include `$\{` rules.

## Curated Examples

Examples are defined inline in `Playground.razor` as `record Example(string Name, string Code)`. All examples must:

1. **Work with `StashCapabilities.None`** — no `fs.*`, `http.*`, `env.*`, `process.*`, `$(...)`, `import`
2. **Produce visible output** via `println()` / `print()`
3. **Run within the step limit** (5M steps)
4. **Showcase language strengths** — see design spec §8 for strategy

When adding examples, test them locally: `dotnet run --project Stash.Cli/ -- example.stash`

## Non-Interactive Showcases

Showcases display features that can't run in the browser sandbox (shell commands, fs, http, process) as read-only `<pre><code>` blocks with pre-rendered output. Defined in `Playground.razor` as `record Showcase(string Title, string Code, string Output)`.

**Important:** Showcase code must use **correct Stash syntax** even though it won't execute in the playground:
- `for` loops require `for (let x in arr)` — parentheses and `let` are mandatory
- `if` statements require `if (condition)` — parentheses are mandatory
- `process.spawn` takes a single string, `process.isAlive`/`process.kill` take process handles (not PIDs)
- `fs.listDir` (not `fs.list`) returns an array of path strings, not objects
- `time.sleep(seconds)` — namespaced, not global `sleep()`
- `http.post(url, body)` takes 2 args — for custom headers use `http.request(opts)`
- Dict literal keys must be identifiers: `{key: value}` — use `dict.set()` for string keys with special characters

## CSS Conventions

- Both themes define all CSS custom properties: `--bg`, `--surface`, `--border`, `--text`, `--text-muted`, `--accent`, `--error`, `--success`, etc.
- Monaco container requires explicit height (invisible at 0px otherwise): use flex layout with `flex: 1; min-height: 0`
- `AutomaticLayout: true` on the editor handles resize events

## Build & Publish

```bash
dotnet build                                             # Build (debug)
dotnet publish Stash.Playground/ -c Release              # WASM publish (~4.4 MB Brotli)
dotnet run --project Stash.Playground/                   # Dev server (hot reload)
```

**WASM constraints:**

- `InvariantGlobalization=true` — no ICU data (reduces size)
- `SuppressTrimAnalysisWarnings=true` — SharpYaml (yaml namespace) uses reflection
- BouncyCastle (crypto namespace): 2.1 MB / 48% of payload — investigate trimming later

## Common Pitfalls

- **`isRunning` stuck state:** Always wrap `Task.Run` in `try/finally` that sets `isRunning = false`
- **Race condition:** Guard `RunCode()` with `if (isRunning) return;` — the button `disabled` attribute doesn't prevent keyboard shortcuts
- **Monaco invisible:** If the editor doesn't render, check CSS height — the container needs explicit height via flex or fixed values
- **DotNetObjectReference leak:** Dispose `_dotNetRef` in `Dispose()` when using `[JSInvokable]` callbacks
