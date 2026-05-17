# Module Exports — explicit export keyword — Context

> Consolidated explorer findings produced by the architect.
> **Purpose:** give each implementer turn the exact code-pointers it needs without re-exploring.

## Key file paths

| Concern | File | Notes |
| --- | --- | --- |
| Lexer soft-keyword registry | `Stash.Core/Lexing/Keywords.cs` | Frozen sets `HardKeywords` and `SoftKeywords`. Add `"export"` to `SoftKeywords`. |
| Lexer hard-keyword table | `Stash.Core/Lexing/Lexer.cs:89` | Map of literal → `TokenType`. Do **not** add an entry for `export`. |
| Token types | `Stash.Core/Lexing/TokenType.cs:226` | Keywords are a contiguous range; `Import` at 295, `Extend` at 325. Adding a new TokenType is unnecessary for export (soft keyword). |
| Statement dispatch in parser | `Stash.Core/Parsing/Parser.cs:200–215` | `case TokenType.Import:` etc. The export soft-keyword check goes here, mirroring `async fn` at Parser.cs:171–178. |
| Existing import parser | `Stash.Core/Parsing/Parser.cs:686–730` | `ImportDeclaration()` — pattern to mirror for `export { name, name }`. |
| AST node directory | `Stash.Core/Parsing/AST/` | One file per node. Existing import nodes: `ImportStmt.cs`, `ImportAsStmt.cs`. |
| Statement visitor interface | `Stash.Core/Parsing/AST/IStmtVisitor.cs` | Add `VisitExportDeclStmt` and `VisitExportBlockStmt`. |
| Statement-type enum | `Stash.Core/Parsing/AST/StmtType.cs` | Add `ExportDecl` and `ExportBlock`. |
| Six-visitor implementations | `Stash.Core/Resolution/SemanticResolver.cs:383`, `Stash.Analysis/Visitors/SemanticValidator.cs:423`, `Stash.Analysis/Visitors/SymbolCollector.cs:959`, `Stash.Analysis/Visitors/SemanticTokenWalker.cs:606`, `Stash.Analysis/Visitors/StashFormatter.cs:438`, `Stash.Bytecode/Compilation/Compiler.Declarations.cs:335` | The six existing `VisitImportStmt` implementations show exactly where the new export visits go. |
| Top-level chunk | `Stash.Bytecode/Bytecode/Chunk.cs` | Add `ModuleExports? Exports { get; init; }`. Nested function chunks always have `null`. |
| Constant-pool metadata records | `Stash.Bytecode/Bytecode/Metadata.cs:33–36` | `ImportMetadata` / `ImportAsMetadata` shape to mirror for `ExportSetMetadata`. |
| Bytecode serialization tags | `Stash.Bytecode/Serialization/BytecodeWriter.cs:328`, `Stash.Bytecode/Serialization/BytecodeReader.cs:334` | New kind tag for `ExportSetMetadata`. Bump the format version. |
| Compiler import emission | `Stash.Bytecode/Compilation/Compiler.Declarations.cs:335–388` | Reference shape for export visit stubs in 1B; no opcode emission for export. |
| VM module loading | `Stash.Bytecode/VM/VirtualMachine.Modules.cs:20–150` | `LoadModule` is the chokepoint. Filter happens after `moduleVM.Execute` at line 140. Cache key is `ModuleCache[resolvedPath]`. |
| VM import opcodes | `Stash.Bytecode/VM/VirtualMachine.Modules.cs:300–351` | `ExecuteImport` and `ExecuteImportAs`. Existing "Module does not export 'X'" error message at line 321 is reused verbatim. |
| Built-in namespace filter | `Stash.Bytecode/VM/VirtualMachine.Modules.cs:344` | `if (val is StashNamespace sn && sn.IsBuiltIn) continue;` — preserve this carve-out in the new filter. |
| Analyzer import resolver | `Stash.Analysis/Resolvers/ImportResolver.cs:193–230` | `ResolveSelectiveImport` — the analyzer-side equivalent of `ExecuteImport`. |
| Analyzer ModuleInfo | `Stash.Analysis/Resolvers/ImportResolver.cs:57–95` | Holds `Symbols` (ScopeTree of all top-level symbols). Add `Exports` field. |
| Analysis engine entry | `Stash.Analysis/Engines/AnalysisEngine.cs` | Owns the `ModuleParser` delegate that populates `ModuleInfo`. Runs `ModuleExports.Build` here. |
| Diagnostic codes | `Stash.Analysis/Models/SemanticDiagnostics.cs` | Register SX001–SX005 and SX-W001 here. |
| LSP code action | `Stash.Lsp/Handlers/CodeActionHandler.cs:486–545` | "Add missing import" action — must filter by `Exports`. |
| Playground tokenizer | `Stash.Playground/wwwroot/js/stash-language.js:10` | `keywords:` array. |
| VS Code tmLanguage | `.vscode/extensions/stash-lang/syntaxes/stash.tmLanguage.json` | Add an `export-declaration` rule and extend `keyword.control` alternation. |
| Language spec | `docs/Stash — Language Specification.md` | Imports at line 298; reserved/contextual keywords at line 139/147; BNF `importDecl` at line 1464. |

## Key types

- `Token` — `Stash.Core/Lexing/Token.cs` — carries lexeme; soft keywords are compared by lexeme (`Peek().Lexeme == "export"`).
- `Stmt` — `Stash.Core/Parsing/AST/Stmt.cs` — base class for the new nodes.
- `Chunk` — `Stash.Bytecode/Bytecode/Chunk.cs` — module unit; gains `Exports`.
- `StashValue` / `StashNamespace` — `Stash.Core/Runtime/StashValue.cs`, `Stash.Core/Runtime/Types/StashNamespace.cs` — the built-in flag `IsBuiltIn` distinguishes stdlib namespaces from user-defined.
- `SourceSpan` — `Stash.Common/SourceSpan.cs` — required on every AST node.
- `ImportResolver.ModuleInfo` — `Stash.Analysis/Resolvers/ImportResolver.cs:57` — the analyzer's per-module record. Gains `Exports`.

## New types this feature introduces

- `ExportDeclStmt(Token ExportKeyword, Stmt Inner, SourceSpan Span)` in `Stash.Core/Parsing/AST/`.
- `ExportBlockStmt(Token ExportKeyword, List<Token> Names, SourceSpan Span)` in `Stash.Core/Parsing/AST/`.
- `ModuleExports` record + `ExportEntry` — Phase 1C creates them in `Stash.Analysis/Models/`. Phase 1D **moves them into `Stash.Core/Resolution/`** so `Stash.Bytecode` (which cannot depend on `Stash.Analysis`) can reference them on `Chunk`.
- `ExportSetMetadata` in `Stash.Bytecode/Bytecode/Metadata.cs` — the serializable mirror used in the constant pool.

## Conventions discovered

- **Soft keyword pattern**: keyword is registered in `Stash.Lexing.Keywords.SoftKeywords` but **not** added to the lexer's keyword map and **no** `TokenType` is created. The parser checks `Check(TokenType.Identifier) && Peek().Lexeme == "kw"`. See `Parser.cs:171–178` for `async fn`.
- **Six-visitor pattern**: every new `Stmt` subclass must add a `VisitXyzStmt` method to `IStmtVisitor<T>` and implement it in all six implementations (Compiler, SemanticResolver, SemanticValidator, SymbolCollector, SemanticTokenWalker, StashFormatter). Compile error if any is missed. See `Stash.Core/CLAUDE.md`.
- **Module loading**: `LoadModule` returns `moduleVM.Globals` directly today. The cache key is the resolved absolute file path. The cache value is the entire globals dict. Filtering at this boundary cleanly satisfies every downstream consumer (`ExecuteImport`, `ExecuteImportAs`) without per-call-site changes.
- **No new opcodes**: `Stash.Bytecode/CLAUDE.md` warns that the dispatch switch is near the .NET AOT optimization threshold. This feature does not add any opcode.
- **Diagnostic codes** live in `Stash.Analysis/Models/SemanticDiagnostics.cs`. Codes use the SXnnn / SX-Wnnn convention; messages are emitted via the standard sink.
- **Bytecode format versioning**: changes to the serialized `Chunk` shape must bump the format-version byte in `BytecodeWriter.cs` (header comment documents the contract).
- **The generated `docs/Stash — Standard Library Reference.md` must not be edited by hand.** This feature does not touch stdlib, so this is informational only.

## Prior art / similar features

- **`.kanban/4-done/Import System — Namespace Re-Export Filtering.md`** — most directly relevant. Established the `StashNamespace.IsBuiltIn` flag and the carve-out at `VirtualMachine.Modules.cs:344`. The new filter must continue to respect this carve-out.
- **`.kanban/4-done/Soft Keywords — Contextual Keyword Promotion.md`** — established the soft-keyword machinery (`Keywords.SoftKeywords`) and the lexeme-comparison pattern in the parser. `export` follows the same recipe.
- **`.kanban/4-done/Unset Statement — Removing Top-Level Bindings.md`** — recent example of adding a new top-level `Stmt` node, the six-visitor update, and parser integration. Similar size and shape to phase 1A here.
- **`.kanban/4-done/Bytecode Serialization — Compile-Once Run-Later.md`** — explains the `.stashc` format-version contract and how new metadata kinds get added.

## Gotchas surfaced during exploration

- **`fn export(...)` exists in the tree** (`examples/packages/docker/lib/containers.stash:86`). The soft-keyword design preserves this. A hard-keyword promotion would silently break it. Phase 1A tests **must** include `Parse_FnExport_AsName_StillWorks`.
- **`example/packages/diff/lib/constants.stash:46`** contains the comment "Internal sentinels (do not export beyond the package)". The intent maps directly to this feature — once landed, that comment can be replaced by `export { ... }` in `index.stash`. Optional follow-up; not part of the phase plan.
- **`Stash.Bytecode` cannot depend on `Stash.Analysis`** per `Stash.Core/CLAUDE.md`. `ModuleExports` is therefore placed in `Stash.Core/Resolution/` (move performed in Phase 1D); both layers reference it from there. The `ExportSetBuilder.Build` algorithm reads only `Stmt`/AST types from `Stash.Core`, so this is mechanically clean.
- **`ImportResolver` caches `ModuleInfo` by absolute path** and tracks reverse dependencies. When a module's `export { ... }` list changes (text edit), the existing `InvalidateCache` + `GetDependents` flow already re-analyzes importers — confirm tests cover this.
- **The built-in namespace carve-out is value-based, not name-based.** The filter checks `val is StashNamespace ns && ns.IsBuiltIn`. Do **not** introduce a hard-coded set of built-in namespace names in the new filter.
- **`StashNamespace.Freeze()` is called after population** in `ExecuteImportAs` (`Stash.Bytecode/VM/VirtualMachine.Modules.cs:348`). The filtered environment for `ExecuteImportAs` must be built before this call, which already happens — the filter at `LoadModule` is upstream of `Freeze()`.
- **Tests live in `Stash.Tests/`** and run via `dotnet test Stash.Tests --filter "FullyQualifiedName~..."`. New per-phase tests should follow that naming pattern; the verify commands in `plan.yaml` rely on it.
