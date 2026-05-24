# Implementation Context — lsp-snippet-completions

Files the implementer should be aware of, grouped by phase concern. Read on demand.

## Provider model (precedent — read first)

- `.kanban/4-done/lsp-completion-providers/brief.md` — the dispatcher/provider/sink model this
  feature plugs into. Key facts: `ICompletionProvider.Provide(CompletionContext) -> IEnumerable<CompletionCandidate>`;
  dispatcher routes per `CompletionMode`; sink dedups by `Label` (first wins) and round-trips
  `SourceTag` through `CompletionItem.Data`.
- `.kanban/4-done/lsp-completion-providers/plan.yaml` — canonical phase shape and the
  `final_verify` flaky-test filter to reuse.

## Files to extend (P1)

- `Stash.Lsp/Completion/CompletionCandidate.cs` — add two optional fields `InsertText` and
  `InsertTextFormat`. Existing call sites use positional construction; new fields go at the end.
- `Stash.Lsp/Completion/CompletionItemSink.cs` — `Add(...)` must write `InsertText` and
  `InsertTextFormat` into the materialized `CompletionItem` when `InsertText` is non-null.

## Files to add (P2)

- `Stash.Lsp/Completion/Snippets/` — new directory housing `Snippet`, `SnippetScope`,
  `SnippetSourceKind`, `ISnippetRegistry`, `BundledSnippetRegistry`, `SnippetValidator`,
  `SnippetLoadError`, `RawSnippet`, `bundled.json`.
- `Stash.Lsp/Completion/Providers/SnippetCompletionProvider.cs` — sibling of the existing
  per-mode providers (see neighbors: `KeywordCompletionProvider.cs`, `ScopedSymbolCompletionProvider.cs`).
- `Stash.Lsp/StashLanguageServer.cs` — DI registration site for both the registry and the
  provider. The existing Default-mode provider list is the insertion point.
- `Stash.Lsp/Stash.Lsp.csproj` — embed `bundled.json` as a resource (`<EmbeddedResource Include="Completion/Snippets/bundled.json" />`).

## Validator dependencies

- `Stash.Core/Lexer.cs` (`Stash.Core.Lexer.Tokenize`) — used by `SnippetValidator` to confirm
  the (placeholder-stripped) snippet body lexes.
- `Stash.Core/Parsing/Parser.cs` (`Stash.Core.Parsing.Parser.Parse`) — used by the validator's
  parse step. Look for the equivalent of `Analyze`/`Parse` entry points in
  `Stash.Lsp.Analysis.AnalysisEngine` for the conventional call shape if direct lexer/parser
  invocation looks awkward.

## Context-detection dependencies (P4)

- `Stash.Analysis/Models/ScopeTree.cs` — `FindScopeAt(int line, int column)` is the entry point
  `SnippetContext.Classify` calls.
- `Stash.Analysis/Models/Scope.cs` — `Parent` chain walk gives the nearest Function/Loop ancestor.
- `Stash.Analysis/Models/ScopeKind.cs` — the four kinds: `Global`, `Function`, `Block`, `Loop`.
  Note: struct/enum/interface bodies are not separate scopes today — see brief Q11.
- `Stash.Lsp/Completion/CompletionContext.cs` — already carries `Analysis` (so `Analysis.ScopeTree`)
  plus `Line`/`Column`.

## Failure-surfacing seam (P3)

- `Stash.Lsp/StashLanguageServer.cs` — `BundledSnippetRegistry` (and `SnippetDiagnosticsReporter`)
  must be constructed in the OmniSharp DI graph with access to `ILanguageServerFacade` and
  `ILogger<BundledSnippetRegistry>`. The facade exposes `Window.ShowMessage(...)`.
- `Stash.Lsp/CLAUDE.md` — "Symbol filtering invariant" and "OmniSharp `CompletionItem.Data` null
  round-trip" sections constrain candidate field semantics; read them before changing the sink.

## Migration source (P5)

- `.vscode/extensions/stash-lang/snippets/stash.json` — the v1 bundled set is migrated from
  this file. 38 entries; most should pass validation as-is. Two entries use `\$` to escape into
  Stash interpolation (`cmd`, `str`, `mli`) — confirm the validator's tabstop stripper treats
  these correctly (covered by a P2 unit test).
- `.vscode/extensions/stash-lang/package.json` — the `contributes.snippets` key (for language
  `stash`) is deleted in P5. Leave the `stash-tpl` entry if present; it's out of scope.
- `.vscode/extensions/stash-lang/snippets/stash-tpl.json` — **untouched** (template language not
  LSP-served).

## Test surfaces

- `Stash.Tests/Lsp/Completion/` — existing test fixtures for the provider model. New tests
  (`SnippetValidatorTests`, `SnippetCompletionProviderTests`, `SnippetFailureSurfacingTests`,
  `SnippetContextTests`) land here.
- `Stash.Tests/Lsp/Completion/CompletionSurfaceSnapshotTests.cs` — surface snapshot harness from
  the precedent feature's P6. P5 adds a `Default_WithSnippets.txt` snapshot to lock the snippet
  set's appearance in Default mode.
