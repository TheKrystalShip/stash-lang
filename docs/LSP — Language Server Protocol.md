# Stash — LSP (Language Server Protocol)

> **Status:** Draft v0.1
> **Created:** March 2026
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
4. [Implementation Phases](#4-implementation-phases)
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

Stash.Interpreter/                  # CLI: REPL + script runner
├── Program.cs
├── Debugging/
│   ├── CallFrame.cs
│   ├── CliDebugger.cs
│   └── IDebugger.cs
└── Interpreting/
    ├── Environment.cs
    ├── Interpreter.cs
    ├── RuntimeError.cs
    ├── StashFunction.cs
    ├── StashStruct.cs
    ├── StashInstance.cs
    ├── StashEnum.cs
    ├── StashEnumValue.cs
    ├── IStashCallable.cs
    ├── ReturnException.cs
    ├── BreakException.cs
    └── ContinueException.cs

Stash.Lsp/                         # LSP server
├── Program.cs                     # Entry point — start server on stdio
├── StashLanguageServer.cs         # Server setup, capability registration
├── Analysis/
│   ├── DocumentManager.cs         # Track open documents + versions
│   ├── AnalysisEngine.cs          # Lex → Parse → Build symbols per document
│   └── SymbolTable.cs             # Scope-aware symbol map
└── Handlers/
    ├── TextDocumentSyncHandler.cs  # didOpen, didChange, didClose
    ├── DiagnosticsHandler.cs      # Publish lex/parse errors
    ├── DocumentSymbolHandler.cs   # Outline view
    ├── CompletionHandler.cs       # Autocomplete
    ├── HoverHandler.cs            # Hover info
    ├── DefinitionHandler.cs       # Go-to-definition
    ├── ReferencesHandler.cs       # Find all references
    ├── SignatureHelpHandler.cs    # Parameter hints
    ├── SemanticTokensHandler.cs   # Rich semantic highlighting
    ├── RenameHandler.cs           # Symbol rename
    └── FoldingRangeHandler.cs     # Code folding

editors/vscode/stash-lang/          # VS Code extension (extended)
├── package.json                    # Updated: activationEvents, LSP client deps
├── src/
│   └── extension.ts               # Start LSP server, create LanguageClient
├── tsconfig.json
├── syntaxes/
│   └── stash.tmLanguage.json       # Existing grammar
├── snippets/
│   └── stash.json                  # Existing snippets
└── language-configuration.json     # Existing

Stash.Tests/                        # Test project (references Stash.Core)
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
| `IExprVisitor<T>` / `IStmtVisitor<T>` (19 + 14 methods)    | Visitor-based AST traversal for analysis passes        |
| `Token` (Type, Lexeme, Span)                               | Semantic tokens, keyword identification                |
| `FnDeclStmt` (Name, Parameters, Body)                      | Document symbols, completion, signature help           |
| `StructDeclStmt` (Name, Fields)                            | Document symbols, completion (field names)             |
| `EnumDeclStmt` (Name, Members)                             | Document symbols, completion (member names)            |
| `VarDeclStmt` / `ConstDeclStmt`                            | Document symbols, hover, go-to-definition              |
| `IdentifierExpr`                                           | Reference resolution, rename                           |
| `ImportStmt`                                               | Cross-file analysis                                    |

### What Must Be Built New

| Component               | Purpose                                                                | Notes                                                                                                                                       |
| ----------------------- | ---------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------- |
| **Document Manager**    | Track open file contents, versions, dirty state                        | Dictionary keyed by URI, updated on didChange                                                                                               |
| **Analysis Engine**     | Re-lex + re-parse on each change, cache results                        | Full re-analysis per edit (sufficient for script-sized files)                                                                               |
| **Symbol Table**        | Static scope-aware map of declarations                                 | Walk AST, record: name, kind (fn/var/const/struct/enum/param), span, scope depth. Unlike `Environment`, this is static (no runtime values). |
| **Position Utilities**  | Convert between LSP positions (0-based) and Stash SourceSpan (1-based) | Simple offset arithmetic                                                                                                                    |
| **AST Query Utilities** | "Find the node at position X", "Find all references to name Y"         | Walk AST with visitors, filter by span containment                                                                                          |

---

## 4. Implementation Phases

### Phase A: Foundation — Diagnostics + Document Sync

**Goal:** Red squiggles for syntax errors as you type. Establishes the full client ↔ server pipeline.

**LSP Methods:**

- `initialize` / `initialized` — capability negotiation
- `textDocument/didOpen` — receive file contents
- `textDocument/didChange` — receive edits (full document sync)
- `textDocument/didClose` — cleanup
- `textDocument/publishDiagnostics` — push errors to client

**Implementation:**

1. Set up `Stash.Lsp` project with `OmniSharp.Extensions.LanguageServer` NuGet package
2. Create `DocumentManager` to store document text by URI
3. On each `didOpen`/`didChange`: re-run `Lexer.ScanTokens()` + `Parser.ParseProgram()`
4. Map `Lexer.Errors` and `Parser.Errors` to LSP `Diagnostic` objects using `SourceSpan`
5. Publish diagnostics back to client

**VS Code client:**

1. Add `vscode-languageclient` npm dependency
2. Write `extension.ts` that spawns the LSP server process
3. Register `onLanguage:stash` activation event

**Success Criteria:** Opening a `.stash` file with a syntax error shows a red squiggle with error message.

---

### Phase B: Navigation — Symbols, Go-to-Definition, Hover

**Goal:** Outline view, click-to-jump to definitions, hover info.

**LSP Methods:**

- `textDocument/documentSymbol` — outline / breadcrumbs
- `textDocument/definition` — go-to-definition
- `textDocument/hover` — hover info popup

**Implementation:**

1. Build `SymbolTable` — an AST visitor that collects all declarations:
   - Functions: name, parameter names, span
   - Structs: name, field names, span
   - Enums: name, member names, span
   - Variables/Constants: name, span, const flag
   - Parameters: name, span, owning function
2. `DocumentSymbolHandler`: Walk AST, emit `SymbolInformation` for each top-level declaration
3. `DefinitionHandler`: At cursor position, find the `IdentifierExpr` under cursor; look up its declaration in `SymbolTable`
4. `HoverHandler`: Same lookup; format declaration info as Markdown

**Key challenge:** Building a scope-aware symbol resolver that handles:

- Block scoping (`{ let x = ...; }` — `x` not visible outside)
- Function closures
- Shadowing (inner scope redefines outer name)
- Struct field access (`srv.host` — need to know `srv` is a `Server` to suggest `host`)

**Success Criteria:** Outline shows all functions/structs/enums. Ctrl+click on a variable jumps to its `let`/`const` declaration. Hover shows declaration signature.

---

### Phase C: Editing Assistance — Completion, Signature Help

**Goal:** Autocomplete and parameter hints.

**LSP Methods:**

- `textDocument/completion` — autocomplete suggestions
- `textDocument/signatureHelp` — parameter hints inside function calls

**Implementation:**

1. `CompletionHandler`: Based on cursor context, offer:
   - **Keywords:** `let`, `const`, `fn`, `struct`, `enum`, `if`, `else`, `while`, `for`, `return`, `break`, `continue`, `try`, `import`, `from`, `true`, `false`, `null`
   - **In-scope variables:** Walk symbol table from cursor scope outward
   - **Built-in functions:** `println`, `print`, `typeof`, `len`, `toStr`, `toInt`, `toFloat`, `readFile`, `writeFile`, `exit`, `lastError`, `env`, `setEnv`
   - **Struct fields:** After `.` on a struct instance, suggest field names
   - **Enum members:** After `.` on an enum type, suggest member names
2. `SignatureHelpHandler`: When cursor is inside `name(` ... `)`, look up `name` as function, show parameter list with active parameter highlighted

**Key challenge:** Dot-completion requires type inference — knowing that `srv` is a `Server` instance so you can suggest `.host`, `.port`, `.status`. In a dynamically typed language, this requires tracking assignments: `let srv = Server { ... }` → `srv` has type `Server`.

**Success Criteria:** Typing `pr` suggests `println`, `print`. Typing `srv.` after `let srv = Server {...}` suggests `host`, `port`, `status`. Typing inside `println(` shows signature hint.

---

### Phase D: Polish — Semantic Tokens, Rename, Folding, References

**Goal:** Richer highlighting, rename refactoring, code folding.

**LSP Methods:**

- `textDocument/semanticTokens/full` — semantic token data
- `textDocument/references` — find all references
- `textDocument/rename` — rename symbol everywhere
- `textDocument/foldingRange` — collapsible regions

**Implementation:**

1. `SemanticTokensHandler`: Walk AST, emit semantic token for each node with type+modifiers:
   - `variable` (with `declaration` modifier for `let`/`const`)
   - `function` (with `declaration` modifier for `fn`)
   - `struct`, `enum`, `parameter`, `property`, `enumMember`
2. `ReferencesHandler`: Walk entire AST, collect all `IdentifierExpr` nodes matching target name in compatible scope
3. `RenameHandler`: Same as references + generate `TextEdit` for each location
4. `FoldingRangeHandler`: Emit folding ranges for `BlockStmt`, multi-line comments, multi-line arrays

**Success Criteria:** Variables, functions, types get distinct colors even within same keyword category. F2 rename works across file. Blocks can be collapsed.

---

### Phase E (Future): Cross-File Intelligence

**Goal:** Go-to-definition and completions across `import` boundaries.

**Implementation:**

- Parse `ImportStmt` to resolve file paths
- Analyze imported files, build combined symbol table
- Cross-file go-to-definition, hover, references

---

## 5. Technical Decisions

| Decision                    | Choice                                        | Rationale                                                                                                |
| --------------------------- | --------------------------------------------- | -------------------------------------------------------------------------------------------------------- |
| **LSP library**             | `OmniSharp.Extensions.LanguageServer` (NuGet) | De facto .NET LSP library; handles JSON-RPC, serialization, protocol negotiation                         |
| **Transport**               | stdio                                         | Simplest, universally supported, standard for VS Code extensions                                         |
| **Analysis strategy**       | Full re-lex + re-parse on every change        | Stash scripts are small; lexer+parser are fast; incremental parsing adds huge complexity for little gain |
| **Document sync mode**      | Full document sync (not incremental)          | Simpler; re-receive entire document on each change                                                       |
| **Symbol resolution**       | Static AST walk (no execution)                | LSP must work without running code; build scope tree from AST declarations                               |
| **VS Code client language** | TypeScript                                    | Best practice; `vscode-languageclient` is TypeScript-native                                              |
| **Multi-file support**      | Deferred to Phase E                           | Single-file analysis first; cross-file adds significant complexity                                       |

---

## 6. LSP Feature Reference

Quick reference of all LSP methods and their Stash applicability:

| LSP Method                                | Phase  | Priority  |
| ----------------------------------------- | ------ | --------- |
| `textDocument/publishDiagnostics`         | A      | Must-have |
| `textDocument/didOpen/didChange/didClose` | A      | Must-have |
| `textDocument/documentSymbol`             | B      | High      |
| `textDocument/definition`                 | B      | High      |
| `textDocument/hover`                      | B      | High      |
| `textDocument/completion`                 | C      | High      |
| `textDocument/signatureHelp`              | C      | Medium    |
| `textDocument/references`                 | D      | Medium    |
| `textDocument/semanticTokens`             | D      | Medium    |
| `textDocument/rename`                     | D      | Medium    |
| `textDocument/foldingRange`               | D      | Low       |
| `textDocument/formatting`                 | Future | Low       |
| `textDocument/codeAction`                 | Future | Low       |
| `workspace/symbol`                        | Future | Low       |

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
