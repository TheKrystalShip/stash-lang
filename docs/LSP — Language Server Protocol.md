# Stash — LSP (Language Server Protocol)

> **Status:** v1.0 — Complete
> **Created:** March 2026
> **Last updated:** March 2026
> **Purpose:** Source of truth for the Stash language server — architecture, analysis engine, supported features, and editor integration.
>
> **Companion documents:**
>
> - [Language Specification](../Stash%20—%20Language%20Specification.md) — syntax, type system, AST node types, interpreter architecture
> - [Standard Library Reference](../Stash%20—%20Standard%20Library%20Reference.md) — built-in namespace functions (used for completions and signature help)
> - [DAP — Debug Adapter Protocol](DAP%20—%20Debug%20Adapter%20Protocol.md) — debug adapter server
> - [TAP — Testing Infrastructure](TAP%20—%20Testing%20Infrastructure.md) — testing primitives and harness

---

## Table of Contents

1. [Architecture](#1-architecture)
2. [Project Structure](#2-project-structure)
3. [Reusable Infrastructure](#3-reusable-infrastructure)
4. [Implementation Status](#4-implementation-status)
5. [Technical Decisions](#5-technical-decisions)
6. [LSP Feature Reference](#6-lsp-feature-reference)

---

## 1. Architecture

```
VS Code ←→ LSP Client (TypeScript extension) ←→ LSP Server (.NET process)
             (JSON-RPC over stdio)                (Stash.Lsp, references Stash.Core)
```

- **VS Code client:** A small TypeScript extension using `vscode-languageclient` that spawns the LSP server process and communicates via stdio.
- **LSP server:** A .NET console application (`Stash.Lsp`) that receives JSON-RPC requests, uses `Stash.Core` (Lexer, Parser, AST) for analysis, and returns responses.
- **Transport:** stdio (standard input/output) — simplest, universally supported.

---

## 2. Project Structure

```
Stash.Core/                         # Shared class library
├── Common/
│   └── SourceSpan.cs
├── Lexing/
│   ├── Lexer.cs
│   ├── Token.cs
│   └── TokenType.cs
└── Parsing/
    ├── Parser.cs
    └── AST/
        └── (all AST node types)

Stash.Bytecode/                     # Bytecode VM — sole execution engine
├── StashEngine.cs
├── VirtualMachine.cs
├── Compiler.cs
└── ...

Stash.Lsp/                         # LSP server
├── Program.cs                     # Entry point — start server on stdio
├── StashLanguageServer.cs         # Server setup, capability registration
├── Analysis/
│   ├── AnalysisEngine.cs          # Lex → Parse → Scope → Validate pipeline
│   ├── AnalysisResult.cs          # Complete analysis output per document
│   ├── BuiltInRegistry.cs         # Centralized built-in function/struct/namespace definitions
│   ├── DocumentManager.cs         # Track open documents + versions (incremental sync)
│   ├── ImportResolver.cs          # Cross-file import resolution and module caching
│   ├── LspExtensions.cs           # SourceSpan → LSP Range conversion
│   ├── ReferenceInfo.cs           # Reference occurrence tracking (read/write/call/type-use)
│   ├── Scope.cs                   # Single scope node (Global/Function/Block/Loop)
│   ├── ScopeTree.cs               # Hierarchical scope tree (replaces flat symbol table)
│   ├── SemanticValidator.cs       # Semantic error checking (undefined vars, type mismatches, control flow)
│   ├── SemanticTokenConstants.cs  # Shared token type indices and modifier bit flags
│   ├── SemanticTokenWalker.cs     # AST visitor that pre-classifies identifiers for semantic highlighting
│   ├── StashFormatter.cs          # Full document code formatter
│   ├── SymbolCollector.cs         # AST visitor that builds scope tree and reference list
│   ├── SymbolInfo.cs              # Symbol representation (name, kind, span, type hint, parameter types)
│   ├── TextUtilities.cs           # Word-at-position and dot-prefix extraction
    ├── TypeInferenceEngine.cs     # Static type deduction for variables and expressions
    └── WorkspaceScanner.cs        # Background workspace file scanner and indexer
└── Handlers/
    ├── TextDocumentSyncHandler.cs  # didOpen, didChange, didClose + publishes diagnostics
    ├── DocumentSymbolHandler.cs    # Outline view (hierarchical)
    ├── HoverHandler.cs             # Hover info (symbols, namespaces, built-in constants)
    ├── DefinitionHandler.cs        # Go-to-definition (same-file + cross-file imports)
    ├── CompletionHandler.cs        # Autocomplete (keywords, symbols, dot-completion)
    ├── ReferencesHandler.cs        # Find all references (same-file + cross-file)
    ├── DocumentHighlightHandler.cs # Highlight read/write references in current document
    ├── RenameHandler.cs            # Symbol rename (all references in document)
    ├── PrepareRenameHandler.cs     # Validate rename target and return placeholder
    ├── SignatureHelpHandler.cs     # Parameter hints (built-in + user-defined functions)
    ├── SemanticTokensHandler.cs    # Rich semantic highlighting (12 types, 2 modifiers)
    ├── FoldingRangeHandler.cs      # Code folding (blocks + consecutive comment regions)
    ├── SelectionRangeHandler.cs    # Expand/shrink selection (nested ranges)
    ├── DocumentLinkHandler.cs      # Clickable import file paths
    ├── CodeActionHandler.cs        # Quick-fix suggestions (e.g., "Did you mean?")
    ├── WorkspaceSymbolHandler.cs   # Workspace-wide symbol search
    ├── InlayHintHandler.cs         # Inline type and parameter hints
    ├── CodeLensHandler.cs          # Reference counts for functions/structs/enums
    ├── FormattingHandler.cs        # Document formatting (configurable indent)
    ├── CallHierarchyHandler.cs     # Incoming/outgoing call hierarchy
    ├── LinkedEditingRangeHandler.cs # Linked editing for symbol occurrences
    ├── TypeDefinitionHandler.cs    # Go to type definition (struct/enum of a variable)
    ├── ImplementationHandler.cs    # Find all instantiations of a struct/enum
    └── DidChangeWatchedFilesHandler.cs # File watcher for external .stash file changes

.vscode/extensions/stash-lang/        # VS Code extension
├── package.json                      # activationEvents, LSP client deps
├── src/
│   └── extension.ts                  # Start LSP server, create LanguageClient
├── tsconfig.json
├── syntaxes/
│   └── stash.tmLanguage.json         # TextMate grammar
├── snippets/
│   └── stash.json                    # Code snippets
└── stash-language-configuration.json # Brackets, comments, auto-closing pairs

Stash.Tests/                          # Test project (references Stash.Core)
```

---

## 3. Reusable Infrastructure

The following existing components from `Stash.Core` are directly reusable by the LSP server:

| Component                                                  | LSP Feature It Enables                                 |
| ---------------------------------------------------------- | ------------------------------------------------------ |
| `Lexer` (non-fatal error collection, continues on error)   | Diagnostics — syntax errors reported as squiggles      |
| `Parser` (error recovery via `Synchronize()`, partial AST) | Diagnostics + analysis even with incomplete code       |
| `SourceSpan` (1-based line/col)                            | Direct mapping to LSP `Range` (subtract 1 for 0-based) |
| All AST nodes carry `SourceSpan Span`                      | Precise locations for definitions, references, symbols |
| `IExprVisitor<T>` / `IStmtVisitor<T>` (25 + 17 methods)    | Visitor-based AST traversal for analysis passes        |
| `Token` (Type, Lexeme, Span)                               | Semantic tokens, keyword identification                |
| `FnDeclStmt` (Name, Parameters, Body)                      | Document symbols, completion, signature help           |
| `StructDeclStmt` (Name, Fields)                            | Document symbols, completion (field names)             |
| `EnumDeclStmt` (Name, Members)                             | Document symbols, completion (member names)            |
| `VarDeclStmt` / `ConstDeclStmt`                            | Document symbols, hover, go-to-definition              |
| `IdentifierExpr`                                           | Reference resolution, rename                           |
| `ImportStmt`                                               | Cross-file analysis                                    |

### Analysis Components (Built)

All analysis infrastructure has been implemented. Here are the key components:

| Component              | File                                    | Purpose                                                                                               |
| ---------------------- | --------------------------------------- | ----------------------------------------------------------------------------------------------------- |
| **Document Manager**   | `DocumentManager.cs`                    | Tracks open documents with incremental change application and version tracking                        |
| **Analysis Engine**    | `AnalysisEngine.cs`                     | Orchestrates full pipeline: lex → parse → collect symbols → resolve imports → infer types → validate  |
| **Scope Tree**         | `ScopeTree.cs` + `Scope.cs`             | Hierarchical scope tree mirroring block nesting; replaces flat symbol table                           |
| **Symbol Collector**   | `SymbolCollector.cs`                    | AST visitor that builds scope tree and collects all symbol references                                 |
| **Semantic Validator** | `SemanticValidator.cs`                  | AST visitor producing warnings for undefined variables, misplaced control flow, type mismatches       |
| **Import Resolver**    | `ImportResolver.cs`                     | Cross-file import resolution with module caching and dependency tracking                              |
| **Type Inference**     | `TypeInferenceEngine.cs`                | Static type deduction from assignments, struct initialization, function returns                       |
| **Formatter**          | `StashFormatter.cs`                     | Full document code formatter with configurable indent size and tab/space preference                   |
| **Built-In Registry**  | `BuiltInRegistry.cs`                    | Centralized definitions for all built-in functions, structs, namespaces, keywords                     |
| **Position Utilities** | `LspExtensions.cs` + `TextUtilities.cs` | SourceSpan ↔ LSP Range conversion, word-at-position, dot-prefix extraction                            |
| **Symbol Info**        | `SymbolInfo.cs`                         | Symbol representation: name, kind, span, type hint, parameter types, explicit type hint tracking      |
| **Reference Info**     | `ReferenceInfo.cs`                      | Reference occurrence tracking with read/write/call/type-use distinction                               |
| **Analysis Result**    | `AnalysisResult.cs`                     | Complete analysis output per document (tokens, AST, errors, scope tree, imports)                      |
| **Semantic Walker**    | `SemanticTokenWalker.cs`                | AST visitor that pre-classifies identifiers by walking the parsed tree (drives semantic highlighting) |
| **Token Constants**    | `SemanticTokenConstants.cs`             | Shared token type indices and modifier bit flags for the semantic highlighting system                 |
| **Workspace Scanner**  | `WorkspaceScanner.cs`                   | Background file discovery using bounded async channel; feeds `.stash` files through analysis engine   |

### Analysis Pipeline

The analysis engine runs a full pipeline on each document change (debounced at 25ms):

```
Document text
    ↓
Lexer (preserveTrivia=true, non-fatal errors)
    ↓
Parser (error recovery, partial AST)
    ↓
SymbolCollector (AST visitor)
    ├→ ScopeTree built (hierarchical scope nodes)
    └→ ReferenceInfo list (all symbol usages with read/write/call kind)
    ↓
ImportResolver (cross-file)
    ├→ Resolves import paths, loads & caches modules
    └→ Enriches global scope with imported symbols
    ↓
TypeInferenceEngine
    └→ Deduces types from struct init, literals, function returns
    ↓
SemanticValidator (AST visitor)
    ├→ Undefined variables, const reassignment, wrong arity
    ├→ Misplaced break/continue/return
    ├→ Unreachable code detection
    └→ Type hint enforcement (argument types, assignment types, initialization types, field assignments)
    ↓
AnalysisResult (cached per document URI)
    ├→ Tokens, AST statements
    ├→ Lex/parse/semantic errors
    ├→ ScopeTree with symbols
    └→ Namespace imports map
```

### Performance Optimizations

The analysis pipeline is optimized for sub-millisecond response on typical files:

| Component           | Optimization                                                          | Impact                                                                                 |
| ------------------- | --------------------------------------------------------------------- | -------------------------------------------------------------------------------------- |
| **Scope**           | Name-indexed symbol lookup via `Dictionary<string, List<SymbolInfo>>` | `FindDefinition()` scans only symbols matching the name, not all symbols in scope      |
| **ScopeTree**       | Lazy field index `Dictionary<(parentName, fieldName), SymbolInfo>`    | O(1) struct field lookup (was O(n) full global scope scan per dot-access)              |
| **ScopeTree**       | Lazy children-by-parent index for `GetHierarchicalSymbols()`          | O(1) children lookup per struct/enum (was O(n) `.Where()` scan per parent)             |
| **BuiltInRegistry** | Pre-grouped namespace member/constant dictionaries                    | O(1) namespace member lookup for completions (was O(n) scan over ~130 functions)       |
| **AnalysisEngine**  | Manual token filtering with pre-allocated list                        | Avoids LINQ delegate allocation and intermediate list resizing                         |
| **SemanticTokens**  | AST-walker pre-classifies identifiers into position-indexed map       | Context-aware classification via AST node types instead of heuristic token scanning    |
| **SemanticTokens**  | Pre-resolved reference index from `SymbolCollector` references        | O(1) symbol lookup per identifier (avoids re-walking scope chain via `FindDefinition`) |
| **SemanticTokens**  | Delta token support with per-URI document caching                     | Only encodes changed tokens on subsequent requests                                     |
| **Lexer**           | `FrozenDictionary` for keyword lookup                                 | O(1) keyword identification (compile-time optimized)                                   |
| **Document Sync**   | 25ms debounce on incremental changes                                  | Coalesces rapid keystrokes into single analysis pass                                   |

### Benchmark Results

Measured across all 23 example scripts (4,510 lines total) using `AnalysisEngine.Analyze()` with 3 warmup + 20 measured iterations per file. Full benchmark suite in `Stash.Tests/Analysis/AnalysisBenchmarkTests.cs`.

#### Full Pipeline Response Time

| Metric                    | Value          |
| ------------------------- | -------------- |
| **Average analysis time** | **1.24 ms**    |
| **P95 analysis time**     | 2.95 ms        |
| **P99 analysis time**     | 5.13 ms        |
| **Throughput**            | 3,647 lines/ms |
| Smallest file (18 lines)  | 0.11 ms        |
| Largest file (485 lines)  | 3.09 ms        |

#### Pipeline Stage Breakdown (largest file — 485 lines)

| Stage             | Avg Time    | % of Total |
| ----------------- | ----------- | ---------- |
| Parser            | 1.83 ms     | 50.5%      |
| Lexer             | 1.05 ms     | 29.1%      |
| TypeInference     | 0.26 ms     | 7.1%       |
| SymbolCollector   | 0.27 ms     | 7.5%       |
| SemanticValidator | 0.17 ms     | 4.6%       |
| Token Filter      | 0.04 ms     | 1.2%       |
| **Full pipeline** | **4.01 ms** |            |

#### FindDefinition Throughput (semantic token hot path)

| Metric             | Value                 |
| ------------------ | --------------------- |
| Per-lookup average | 0.446 µs              |
| Throughput         | 2,240,000 lookups/sec |

#### JIT Warm-Up Overhead

| Metric             | Value   |
| ------------------ | ------- |
| Cold (first call)  | 6.04 ms |
| Warm (subsequent)  | 1.59 ms |
| JIT overhead ratio | 3.8x    |

---

## 4. Implementation Status

All originally planned phases are complete, plus additional features beyond the original roadmap.

### Phase A: Foundation — Diagnostics + Document Sync ✅

**Status**: Complete

Establishes the full client ↔ server pipeline with real-time diagnostic reporting.

**LSP Methods:**

- `initialize` / `initialized` — capability negotiation
- `textDocument/didOpen` — receive file contents
- `textDocument/didChange` — receive edits (incremental sync supported)
- `textDocument/didClose` — cleanup
- `textDocument/publishDiagnostics` — push lex, parse, and semantic errors to client

**Implementation notes:**

- Diagnostics are published inline by `TextDocumentSyncHandler` with a 25ms debounce
- No separate `DiagnosticsHandler` — publication is integrated into the sync handler
- Three diagnostic stages: lexer errors, parser errors, and semantic diagnostics
- Incremental document sync is supported via `DocumentManager.ApplyIncrementalChanges()`

---

### Phase B: Navigation — Symbols, Go-to-Definition, Hover ✅

**Status**: Complete

**LSP Methods:**

- `textDocument/documentSymbol` — hierarchical outline / breadcrumbs
- `textDocument/definition` — go-to-definition (same-file + cross-file via imports)
- `textDocument/hover` — hover info with Markdown formatting

**Implementation notes:**

- Symbol resolution uses `ScopeTree` (hierarchical scope tree) instead of a flat `SymbolTable`
- `DefinitionHandler` supports cross-file go-to-definition through `ImportResolver`
- `HoverHandler` resolves symbols, namespaces, and built-in constants
- `DocumentSymbolHandler` produces hierarchical symbols; skips built-ins and loop variables

---

### Phase C: Editing Assistance — Completion, Signature Help ✅

**Status**: Complete

**LSP Methods:**

- `textDocument/completion` — autocomplete (keywords, in-scope symbols, dot-completion)
- `textDocument/signatureHelp` — parameter hints (built-in + user-defined functions)

**Implementation notes:**

- `CompletionHandler` supports dot-completion for namespace members and struct fields
- Completions are suppressed inside string literals
- `SignatureHelpHandler` handles both built-in and user-defined function signatures
- Built-in function metadata comes from `BuiltInRegistry`

---

### Phase D: Polish — Semantic Tokens, Rename, Folding, References ✅

**Status**: Complete

**LSP Methods:**

- `textDocument/semanticTokens/full` + `full/delta` — 12 token types + 2 modifiers (declaration, readonly)
- `textDocument/references` — find all references (same-file + cross-file)
- `textDocument/rename` + `textDocument/prepareRename` — rename with validation
- `textDocument/foldingRange` — block and consecutive comment folding

**Implementation notes:**

- Semantic tokens use a two-phase architecture:
  1. **AST walker** (`SemanticTokenWalker`) — implements `IExprVisitor<int>` + `IStmtVisitor<int>` (42 visitor methods) to pre-classify all identifiers by walking the parsed AST. Builds a position-indexed `Dictionary<(line, col), (type, modifiers)>` map. Uses pre-resolved references from `SymbolCollector` for O(1) symbol lookup, `BuiltInRegistry` for namespace/function resolution, and `NamespaceImports` for cross-file member resolution.
  2. **Token stream pass** (`SemanticTokensHandler.Tokenize`) — iterates the lexer token list and looks up each identifier in the walker's classification map. Non-identifier tokens (keywords, literals, operators, comments) are classified directly from their `TokenType`. Compound tokens (interpolated strings, command literals) use position-based sub-token classification.
- Shared constants (`SemanticTokenConstants`) define the 12 token type indices and 2 modifier bit flags used by both the walker and handler.
- `RenameHandler` returns `WorkspaceEdit` across all references
- `PrepareRenameHandler` validates the rename target and returns a placeholder
- References include cross-file references via `AnalysisEngine.FindCrossFileReferences()`

---

### Phase E: Cross-File Intelligence ✅

**Status**: Complete (was originally marked as "Future")

Cross-file analysis is implemented via `ImportResolver`:

- Resolves selective imports: `import { name1, name2 } from "file.stash"`
- Resolves namespace imports: `import "file.stash" as alias`
- Module caching with cache invalidation on file changes
- Cross-file go-to-definition, hover, and references
- Dependency tracking (`GetDependents()` returns files that import a given module)
- Import error diagnostics (missing files, unresolved names)

---

### Phase F: Workspace Indexing ✅

**Status**: Complete

Progressive background discovery of all `.stash` files in the workspace. When enabled, the server builds a complete cross-file reference index without requiring files to be opened.

**LSP Methods:**

- `workspace/didChangeWatchedFiles` — react to external file create/change/delete events
- `codeLens/refresh` — request client to re-fetch code lens (reference counts)
- `semanticTokens/refresh` — request client to re-fetch semantic tokens

**New components:**

| Component                        | File                              | Purpose                                                                                                                                                                                                     |
| -------------------------------- | --------------------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| **WorkspaceScanner**             | `WorkspaceScanner.cs`             | Background file discovery and analysis. Uses `System.Threading.Channels` for bounded async queue (capacity 1000). Respects `.stashignore` patterns. Sends progressive refresh notifications every 10 files. |
| **DidChangeWatchedFilesHandler** | `DidChangeWatchedFilesHandler.cs` | Handles `workspace/didChangeWatchedFiles` to keep the background index current. Registered for `**/*.stash` glob pattern.                                                                                   |

**Configuration:**

- `stash.workspaceIndexing.enabled` (default: `false`) — opt-in toggle in VS Code settings
- Controlled via `LspSettings.WorkspaceIndexingEnabled`, read by `ConfigurationHandler`

**Design decisions:**

- **Opt-in by default.** Background scanning can increase memory usage for large workspaces. Users enable it explicitly when they want workspace-wide reference counts and symbol search.
- **Respects `.stashignore`.** Vendor directories, build output, and other excluded paths are skipped automatically.
- **Skips open files.** Files already open in the editor have fresher analysis from `TextDocumentSyncHandler` — the scanner does not overwrite them.
- **Progressive updates.** Reference counts tick up gradually as files are analyzed, rather than appearing all at once after a long scan. The server sends `codeLens/refresh` and `semanticTokens/refresh` every 10 files.
- **Thread-safe.** Workspace roots protected by lock; channel is bounded with `DropOldest` backpressure; file I/O errors are caught and logged without stopping the scan.

**Impact on existing features:**

- **Code Lens** — reference counts now reflect all workspace files (not just open ones) when indexing is enabled
- **Workspace Symbols** (`Ctrl+T`) — search results include symbols from all indexed files, not just open documents
- **Find All References** — cross-file references discovered through the analysis cache are available for background-indexed files

---

### Beyond Original Roadmap

The following features were implemented beyond the original Phase A–E plan:

| Feature                  | Handler                                             | Description                                                                                                                                                  |
| ------------------------ | --------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| **Document Highlight**   | `DocumentHighlightHandler`                          | Highlights all read/write references of a symbol in the current document                                                                                     |
| **Selection Range**      | `SelectionRangeHandler`                             | Expand/shrink selection through nested AST ranges                                                                                                            |
| **Document Links**       | `DocumentLinkHandler`                               | Makes `import` file paths clickable, resolving to actual file URIs                                                                                           |
| **Code Actions**         | `CodeActionHandler`                                 | Quick-fix suggestions — "Did you mean?" for undefined variables (Levenshtein distance)                                                                       |
| **Workspace Symbols**    | `WorkspaceSymbolHandler`                            | Workspace-wide symbol search with case-insensitive substring matching                                                                                        |
| **Inlay Hints**          | `InlayHintHandler`                                  | Inline type and parameter name hints                                                                                                                         |
| **Code Lens**            | `CodeLensHandler`                                   | Reference counts displayed above functions, structs, and enums                                                                                               |
| **Formatting**           | `FormattingHandler`                                 | Full document formatting with configurable indent size and tab/space preference                                                                              |
| **Call Hierarchy**       | `CallHierarchyHandler`                              | Incoming/outgoing call hierarchy for functions                                                                                                               |
| **Linked Editing Range** | `LinkedEditingRangeHandler`                         | Linked editing for symbol occurrences (renames all when 2+ references exist)                                                                                 |
| **Type Inference**       | `TypeInferenceEngine`                               | Static type deduction from struct init, command expressions, function returns, literals, dot-access field types; used by SemanticValidator for type checking |
| **Semantic Validation**  | `SemanticValidator`                                 | Catches undefined variables, const reassignment, wrong arity, misplaced control flow, type hint violations                                                   |
| **Workspace Indexing**   | `WorkspaceScanner` + `DidChangeWatchedFilesHandler` | Background discovery and indexing of all `.stash` files; opt-in via `stash.workspaceIndexing.enabled`                                                        |

### Type Hint Enforcement

The `SemanticValidator` enforces explicit type hints as warnings. Since Stash is dynamically typed, type hints are advisory — the validator warns when code contradicts the programmer's declared intent, but does not prevent execution.

**Checks performed:**

| Check                                | Location             | Example                                        | Diagnostic                                                                   |
| ------------------------------------ | -------------------- | ---------------------------------------------- | ---------------------------------------------------------------------------- |
| **Argument type mismatch**           | `VisitCallExpr`      | `fn add(a: int) {}` called with `add("hello")` | Warning: Argument 'a' expects type 'int' but got 'string'.                   |
| **Assignment type mismatch**         | `VisitAssignExpr`    | `let x: int = 5; x = "hello"`                  | Warning: Cannot assign value of type 'string' to variable 'x' of type 'int'. |
| **Initialization type mismatch**     | `VisitVarDeclStmt`   | `let x: int = "hello"`                         | Warning: Variable 'x' is declared as 'int' but initialized with 'string'.    |
| **Const initialization mismatch**    | `VisitConstDeclStmt` | `const x: int = "hello"`                       | Warning: Constant 'x' is declared as 'int' but initialized with 'string'.    |
| **Struct field assignment mismatch** | `VisitDotAssignExpr` | `alice.age = "thirty"` (where `age: int`)      | Warning: Cannot assign value of type 'string' to field 'age' of type 'int'.  |

**Design decisions:**

- **Warnings, not errors.** Type hints are advisory since Stash has no static type system. This preserves the dynamic nature of the language while providing early feedback.
- **Explicit hints only.** Assignment checking only triggers for variables where the user wrote an explicit `: type` annotation. Inferred types do not restrict reassignment.
- **Null compatibility.** `null` is compatible with any explicit type — `let x: Config = null` produces no warning.
- **Inference-based.** `TypeInferenceEngine.InferExpressionType()` resolves argument and value types statically from literals, variable definitions, struct initializers, function return types, and dot-access field types (e.g., `alice.age` resolves to the field's declared type).

**Supporting infrastructure:**

- `SymbolInfo.ParameterTypes` — parallel array storing the explicit type hint (or `null`) for each function parameter position
- `SymbolInfo.IsExplicitTypeHint` — distinguishes user-written `: type` annotations from types inferred by `TypeInferenceEngine`, ensuring only explicit hints trigger assignment warnings
- `SymbolCollector` populates both fields for function declarations, variable/const declarations, function parameters, and struct methods
- Struct field type checking resolves the receiver's type (via inference or explicit annotation), then looks up the field's type hint from the struct definition in the scope tree

---

## 5. Technical Decisions

| Decision                    | Choice                                        | Rationale                                                                                                          |
| --------------------------- | --------------------------------------------- | ------------------------------------------------------------------------------------------------------------------ |
| **LSP library**             | `OmniSharp.Extensions.LanguageServer` (NuGet) | De facto .NET LSP library; handles JSON-RPC, serialization, protocol negotiation                                   |
| **Transport**               | stdio                                         | Simplest, universally supported, standard for VS Code extensions                                                   |
| **Analysis strategy**       | Full re-lex + re-parse on every change        | Stash scripts are small; lexer+parser are fast; incremental parsing adds huge complexity for little gain           |
| **Document sync mode**      | Full and incremental document sync            | Both modes supported; incremental changes applied via `DocumentManager.ApplyIncrementalChanges()`                  |
| **Symbol resolution**       | Static AST walk with type inference           | `ScopeTree` for scope-aware resolution; `TypeInferenceEngine` deduces types from assignments; no runtime execution |
| **VS Code client language** | TypeScript                                    | Best practice; `vscode-languageclient` is TypeScript-native                                                        |
| **Multi-file support**      | Implemented                                   | `ImportResolver` provides cross-file definition, references, hover, and module caching                             |

---

## 6. LSP Feature Reference

All features below marked ✅ are fully implemented and tested.

### Implemented Features

| LSP Method                                | Handler                        | Status |
| ----------------------------------------- | ------------------------------ | ------ |
| `textDocument/didOpen/didChange/didClose` | `TextDocumentSyncHandler`      | ✅     |
| `textDocument/publishDiagnostics`         | (inline in sync handler)       | ✅     |
| `textDocument/documentSymbol`             | `DocumentSymbolHandler`        | ✅     |
| `textDocument/definition`                 | `DefinitionHandler`            | ✅     |
| `textDocument/hover`                      | `HoverHandler`                 | ✅     |
| `textDocument/completion`                 | `CompletionHandler`            | ✅     |
| `textDocument/signatureHelp`              | `SignatureHelpHandler`         | ✅     |
| `textDocument/references`                 | `ReferencesHandler`            | ✅     |
| `textDocument/documentHighlight`          | `DocumentHighlightHandler`     | ✅     |
| `textDocument/rename`                     | `RenameHandler`                | ✅     |
| `textDocument/prepareRename`              | `PrepareRenameHandler`         | ✅     |
| `textDocument/semanticTokens/full`        | `SemanticTokensHandler`        | ✅     |
| `textDocument/semanticTokens/full/delta`  | `SemanticTokensHandler`        | ✅     |
| `textDocument/foldingRange`               | `FoldingRangeHandler`          | ✅     |
| `textDocument/selectionRange`             | `SelectionRangeHandler`        | ✅     |
| `textDocument/documentLink`               | `DocumentLinkHandler`          | ✅     |
| `textDocument/codeAction`                 | `CodeActionHandler`            | ✅     |
| `textDocument/formatting`                 | `FormattingHandler`            | ✅     |
| `textDocument/rangeFormatting`            | `RangeFormattingHandler`       | ✅     |
| `textDocument/onTypeFormatting`           | `OnTypeFormattingHandler`      | ✅     |
| `textDocument/inlayHint`                  | `InlayHintHandler`             | ✅     |
| `textDocument/codeLens`                   | `CodeLensHandler`              | ✅     |
| `textDocument/callHierarchy`              | `CallHierarchyHandler`         | ✅     |
| `textDocument/linkedEditingRange`         | `LinkedEditingRangeHandler`    | ✅     |
| `textDocument/typeDefinition`             | `TypeDefinitionHandler`        | ✅     |
| `textDocument/implementation`             | `ImplementationHandler`        | ✅     |
| `workspace/symbol`                        | `WorkspaceSymbolHandler`       | ✅     |
| `workspace/didChangeWatchedFiles`         | `DidChangeWatchedFilesHandler` | ✅     |

### Not Yet Implemented

| LSP Method                       | Notes                                                   |
| -------------------------------- | ------------------------------------------------------- |
| `textDocument/colorPresentation` | Color picker support (not applicable)                   |

---

## Dependencies

### Stash.Lsp (NuGet)

- `OmniSharp.Extensions.LanguageServer` — LSP protocol implementation

### VS Code Client (npm)

- `vscode-languageclient` — LSP client library
- `@types/vscode` — VS Code API types (dev)
- `typescript` — build (dev)

---

## SourceSpan ↔ LSP Range Conversion

Stash `SourceSpan` is **1-based** (line and column). LSP `Position`/`Range` is **0-based**.

```csharp
// SourceSpan → LSP Range
new Range(
    new Position(span.StartLine - 1, span.StartColumn - 1),
    new Position(span.EndLine - 1, span.EndColumn - 1)
);

// LSP Position → SourceSpan lookup
// Add 1 to both line and character
```

---

## Built-in Functions & Keywords

The LSP's completion and signature help providers need awareness of all built-in functions and keywords. Rather than maintaining a separate list here, the LSP reads from `BuiltInRegistry.cs` which mirrors the registrations documented in the [Standard Library Reference](../Stash%20—%20Standard%20Library%20Reference.md).

For the complete list of namespaces, functions, and their signatures, see the [Standard Library Reference](../Stash%20—%20Standard%20Library%20Reference.md). For keywords, see the [Language Specification grammar](../Stash%20—%20Language%20Specification.md#appendix-b--grammar-draft-ebnf).
