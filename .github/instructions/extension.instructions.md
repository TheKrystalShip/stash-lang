---
applyTo: ".vscode/extensions/stash-lang/**"
---

# VS Code Extension Guidelines

TypeScript extension providing LSP client, DAP client, and TAP test explorer for Stash (`.stash`) and Stash Template (`.tpl`) files. See `docs/TAP — Testing Infrastructure.md` for the test framework spec.

## Structure

```
src/extension.ts      → Activation: LSP client, DAP factory, test explorer
src/testing.ts        → Test controller, run/debug handlers, file watcher
src/testDiscovery.ts  → Static (regex) + dynamic (interpreter) test discovery
src/tapParser.ts      → TAP v14 stream parser (state machine)
src/codeLensProvider.ts → Run/Debug code lenses on test/describe/skip calls
```

Output goes to `out/` (CommonJS, ES2020, strict mode). Build with `npm run compile` or `tsc`.

## Conventions

- **Target:** ES2020, CommonJS modules, strict TypeScript
- **Dependency:** `vscode-languageclient` ^9.0.1 (LSP), VS Code API for DAP/testing
- **Binary resolution:** Settings `stash.lspPath`, `stash.dapPath`, `stash.interpreterPath` override defaults; fall back to `stash-lsp`, `stash-dap`, `stash` on PATH at `~/.local/bin/`
- **Configuration namespace:** All settings under `stash.*` (see `package.json` contributes)

## LSP Client

Initialized in `extension.ts` with stdio transport. Document selector: `{ scheme: "file", language: "stash" }`. Syncs `stash.*` configuration changes to the server. Custom command `stash.showReferences` bridges LSP code lens to VS Code's reference viewer.

## DAP Factory

`StashDebugAdapterFactory` resolves the debug adapter binary path, follows symlinks, checks executable bit, and returns a `DebugAdapterExecutable`. Debug type: `"stash"`, required launch property: `program`.

## Test Explorer

**Discovery** uses two strategies:
1. **Static** (`parseTestsFromText`) — regex `\b(describe|test|skip)\s*\(` with brace-depth tracking for nesting
2. **Dynamic** (`discoverTestsDynamic`) — spawns `stash --test --test-list` and parses `# discovered:` TAP comments

**Test item IDs** use ` > ` separator: `file.test.stash > describe > test name`

**Run handler** spawns interpreter with `--test --test-filter`, pipes stdout through `TapParser`, matches results to `TestItem` via `itemMap`.

**Debug handler** launches a debug session with `__testMode` and `__testFilter` config, captures DAP stdout events through a tracker, pipes through `TapParser`.

## TAP Parser

State machine with `State.Normal` and `State.YamlBlock`. Processes:
- `ok N - name` → pass (or skip if `# SKIP`)
- `not ok N - name` → fail (with YAML diagnostic block for expected/actual/location)
- `# discovered: name [file:line:col]` → test discovery
- `1..N` → plan/completion

Streaming API: `feed(chunk)`, `flush()`, `reset()`.

## Code Lens

`StashTestCodeLensProvider` generates Run/Debug lenses for `describe`, `test`, and `skip` calls in `*.test.stash` files. Fires `stash.runTestByName` / `stash.debugTestByName` commands.
