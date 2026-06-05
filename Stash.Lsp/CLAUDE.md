# LSP Server Guidelines

The Stash LSP server provides language intelligence for `.stash` files using **OmniSharp** (v0.19.9). It must **never** be built with Native AOT — OmniSharp uses reflection-heavy DryIoc for handler registration. `build.stash` enforces a >20MB binary-size guard to catch accidental AOT builds. See `docs/LSP — Language Server Protocol.md` for feature details.

## Project Structure

```
Stash.Lsp/
├── Program.cs                → Entry point: calls StashLanguageServer.RunAsync()
├── StashLanguageServer.cs    → Server builder: DI registration + handler registrations
├── Handlers/                 → LSP request handlers (one per feature)
└── Analysis/                 → LSP-side analysis: symbol resolution, type-inference glue,
                                AST-walker semantic highlighting (SemanticTokenWalker).
                                The core AnalysisEngine itself lives in Stash.Analysis.
```

**Dependencies:** `OmniSharp.Extensions.LanguageServer` v0.19.9, `Stash.Core` (project reference).

## DI Services

Three singletons registered in `StashLanguageServer.cs`:

| Service           | Role                                                                       |
| ----------------- | -------------------------------------------------------------------------- |
| `LspSettings`     | Configuration (debounce delay, log level, feature flags)                   |
| `DocumentManager` | In-memory document text store (`ConcurrentDictionary<Uri, DocumentState>`) |
| `AnalysisEngine`  | Core analysis pipeline, caches results per URI                             |

## Handler Pattern

All handlers inherit OmniSharp base classes and receive services via constructor injection:

```csharp
public class FooHandler : FooHandlerBase
{
    private readonly AnalysisEngine _analysis;
    private readonly DocumentManager _documents;

    public FooHandler(AnalysisEngine analysis, DocumentManager documents) { ... }

    public override Task<FooResponse> Handle(FooParams request, CancellationToken ct)
    {
        // 1. Extract URI/position from request
        // 2. Get text via _documents.GetText(uri)
        // 3. Get analysis via _analysis.GetContextAt(...) or _analysis.GetCachedResult(...)
        // 4. Build and return LSP response model
    }

    protected override FooRegistrationOptions CreateRegistrationOptions(...)
        => new() { DocumentSelector = TextDocumentFilter.ForLanguage("stash") };
}
```

Register new handlers in `StashLanguageServer.cs` via `.WithHandler<NewHandler>()`.

### All Handlers

| Handler                                  | Feature                                               |
| ---------------------------------------- | ----------------------------------------------------- |
| `TextDocumentSyncHandler`                | Open/change/close/save with debounced re-analysis     |
| `CompletionHandler`                      | Autocompletion (keywords, symbols, namespace members) |
| `HoverHandler`                           | Hover info with type hints, docs, import source       |
| `DefinitionHandler`                      | Go-to-definition                                      |
| `TypeDefinitionHandler`                  | Go-to-type-definition                                 |
| `ImplementationHandler`                  | Go-to-implementation                                  |
| `ReferencesHandler`                      | Find all references (cross-file via imports)          |
| `RenameHandler` / `PrepareRenameHandler` | Rename refactoring with validation                    |
| `DocumentSymbolHandler`                  | Document outline / symbol navigation                  |
| `WorkspaceSymbolHandler`                 | Workspace-wide symbol search                          |
| `SignatureHelpHandler`                   | Parameter hints at call sites                         |
| `SemanticTokensHandler`                  | Semantic syntax highlighting                          |
| `DocumentHighlightHandler`               | Highlight symbol occurrences                          |
| `CodeActionHandler`                      | Quick fixes, organize imports                         |
| `CodeLensHandler`                        | Reference counts above declarations                   |
| `InlayHintHandler`                       | Inline parameter name hints                           |
| `FoldingRangeHandler`                    | Code folding regions                                  |
| `SelectionRangeHandler`                  | Smart selection expansion                             |
| `DocumentLinkHandler`                    | Clickable import paths                                |
| `FormattingHandler`                      | Document formatting                                   |
| `RangeFormattingHandler`                 | Range formatting (format selection)                   |
| `OnTypeFormattingHandler`                | On-type formatting (auto-indent on `}`, `;`, `\n`)    |
| `CallHierarchyHandler`                   | Incoming/outgoing call hierarchy                      |
| `LinkedEditingRangeHandler`              | Linked editing                                        |
| `ConfigurationHandler`                   | Settings change propagation                           |

## Analysis Pipeline

`AnalysisEngine.Analyze(uri, source)` runs this pipeline and caches the result:

1. **Lexer** → tokens (preserves trivia: doc comments, regular comments, shebangs)
2. **Token filter** → removes trivia for parser
3. **Parser** → AST statements
4. **SymbolCollector** → `ScopeTree` (hierarchical symbol table)
5. **ImportResolver** → resolve `import` statements, inject symbols from dependencies
6. **TypeInferenceEngine** → infer types from AST + annotations
7. **DocCommentResolver** → attach `///` doc comments to symbols
8. **SemanticValidator** → produce diagnostics (errors, warnings, info)

**Key public methods on `AnalysisEngine`:**

| Method                                | Purpose                                             |
| ------------------------------------- | --------------------------------------------------- |
| `Analyze(uri, source)`                | Full pipeline, caches result                        |
| `GetCachedResult(uri)`                | Retrieve without re-analysis                        |
| `GetContextAt(uri, text, line, char)` | Returns `(AnalysisResult, word)` for handlers       |
| `InvalidateModule(path)`              | Clear import cache (triggers dependent re-analysis) |
| `GetDependents(path)`                 | URIs that import the given file                     |
| `FindCrossFileReferences(uri, name)`  | Cross-file reference search via imports             |

### Key Analysis Classes

| File                        | Role                                                                          |
| --------------------------- | ----------------------------------------------------------------------------- |
| `AnalysisResult.cs`         | Output record: tokens, statements, symbols, diagnostics                       |
| `ScopeTree.cs`              | Hierarchical scope tracking with `FindDefinition()`                           |
| `SymbolInfo.cs`             | Symbol metadata (name, kind, type, span, documentation)                       |
| `StdlibRegistry` (in `Stash.Stdlib`) | Static registry of keywords, namespace functions, structs, enums, and valid types |
| `TypeInferenceEngine.cs`    | Infers types from assignments, returns, annotations                           |
| `SemanticValidator.cs`      | Validates names, types, arity, produces diagnostics                           |
| `ImportResolver.cs`         | Resolves imports, tracks module dependencies                                  |
| `DocCommentResolver.cs`     | Extracts `///` doc comments and attaches to symbols                           |
| `TextUtilities.cs`          | Dot-prefix detection, word-at-position extraction                             |
| `StashFormatter.cs`         | Code formatting logic                                                         |
| `SemanticTokenWalker.cs`    | AST visitor that pre-classifies identifiers for semantic highlighting         |
| `SemanticTokenConstants.cs` | Shared token type indices and modifier bit flags                              |
| `LspExtensions.cs`          | `SourceSpan` → LSP `Range` conversion utilities                               |
| `LspSettings.cs`            | Config: `DebounceDelayMs`, `LogLevel`, `InlayHintsEnabled`, `CodeLensEnabled` |

## Document Sync & Diagnostics

`TextDocumentSyncHandler` orchestrates the edit → analyze → publish cycle:

1. On **open**: store text, analyze immediately, publish diagnostics
2. On **change**: apply incremental edits, schedule debounced analysis (default 25ms)
3. On analysis complete: publish diagnostics, refresh semantic tokens, **re-analyze dependent files** (files that import this one)
4. On **close**: cancel pending analysis, clear diagnostics

## Symbol filtering invariant

When building unqualified completion or symbol lists from `GetVisibleSymbols`, filter by `SymbolInfo.Accessibility == BareIdentifier` — member-only kinds (`Field`, `Method`, `EnumMember`) auto-derive to `RequiresQualification` and must not appear as bare identifiers. To split user code from stdlib-injected resolution helpers, also check `SymbolInfo.Origin == SymbolOrigin.BuiltinStdlib`. The stdlib symbols `SymbolCollector.RegisterBuiltIns` injects exist for *resolution* (hover, goto-def, rename); presentation handlers must filter them or they'll leak (and duplicate, since stdlib also has registry-driven enumeration paths).

## OmniSharp quirk: `CompletionItem.Data` null round-trip

A `null` `CompletionItem.Data` comes back as an empty `JToken` after the LSP round-trip, not as `null`. Use `string.IsNullOrEmpty` rather than `== null` when asserting "no Data set".

## Tests

LSP analysis tests live in `Stash.Tests/Analysis/`:

- **Helpers:** `Analyze(source)` → `ScopeTree`, `FullAnalyze(source)` → `AnalysisResult`
- **Coverage:** Symbol resolution, references, renaming, type inference, doc comments, import resolution, formatting, semantic validation, incremental sync, call hierarchy
