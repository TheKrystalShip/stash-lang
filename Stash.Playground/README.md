# Stash Playground

Browser-based playground for the [Stash scripting language](https://stash-lang.org). Runs the full interpreter via Blazor WebAssembly — no server required.

**Features:** Monaco editor with syntax highlighting · 10 curated examples · dark/light themes · Ctrl+Enter to run

## Project Structure

```
Stash.Playground/
├── Program.cs                       → Entry point
├── App.razor                        → Router shell
├── Layout/MainLayout.razor          → Minimal layout wrapper
├── Pages/Playground.razor           → Main UI (editor, examples, output, showcases)
├── Services/
│   ├── PlaygroundExecutor.cs        → Sandboxed interpreter (StashCapabilities.None)
│   ├── PlaygroundResult.cs          → Execution result DTO
│   └── CappedStringWriter.cs        → Output cap (512 KB)
└── wwwroot/
    ├── index.html                   → SPA shell + loading screen
    ├── css/app.css                  → Dual-theme CSS (Catppuccin)
    └── js/stash-language.js         → Monarch tokenizer + JS helpers
```

## Development

```bash
# Build
dotnet build Stash.Playground/

# Run dev server (hot reload)
dotnet run --project Stash.Playground/

# Publish (release)
dotnet publish Stash.Playground/ -c Release
```

The dev server runs at `https://localhost:5001` (or the port shown in terminal output).

## Sandbox Model

Three guards keep browser execution safe:

| Guard | Value | Purpose |
|---|---|---|
| `StashCapabilities.None` | — | Disables OS features: fs, http, env, process, shell commands |
| Step limit | 5,000,000 | Prevents infinite loops |
| `CappedStringWriter` | 512 KB | Caps total output size |

> **Note:** `CancellationToken` does not work in single-threaded WASM — the step limit is the only loop guard.

## Adding Examples

Examples are defined in `Playground.razor` as `record Example(string Name, string Code)`.

Rules for new examples:
- Must work under `StashCapabilities.None` (no shell, fs, http, or process calls)
- Must produce output via `io.println()` — silent examples are confusing in a playground
- Must complete within the 5,000,000 step limit

Test locally before adding:

```bash
dotnet run --project Stash.Cli/ -- example.stash
```

## Theme System

The playground uses [Catppuccin](https://github.com/catppuccin/catppuccin) — Mocha (dark) and Latte (light).

- CSS custom properties are set on `body.theme-dark` and `body.theme-light` in `app.css`
- Monaco editor themes (`stash-dark` / `stash-light`) are defined in `stash-language.js`

## Non-Interactive Showcases

Some Stash features (shell commands, fs, http, process) can't run in the browser sandbox. These are presented as read-only `<pre><code>` blocks with pre-rendered output so users can see what they look like.

Showcases are defined in `Playground.razor` alongside examples, also as a typed record list.
