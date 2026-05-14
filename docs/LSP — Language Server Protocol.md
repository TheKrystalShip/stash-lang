# LSP - Language Server Protocol

> **Status:** Stable protocol reference
> **Audience:** Editor integrators, language-server maintainers, and contributors changing editor intelligence.
> **Purpose:** Defines the Stash Language Server Protocol surface, analysis model, editor features, settings, and known limits.
>
> **Companion documents:**
>
> - [Language Specification](Stash%20—%20Language%20Specification.md) - authoritative syntax and language semantics.
> - [Standard Library Reference](Stash%20—%20Standard%20Library%20Reference.md) - built-in namespaces, functions, constants, and runtime API documentation.
> - [DAP - Debug Adapter Protocol](DAP%20—%20Debug%20Adapter%20Protocol.md) - debugger protocol reference.
> - [TAP - Testing Infrastructure](TAP%20—%20Testing%20Infrastructure.md) - test discovery and TAP output contract.

The Stash language server exposes the standard [Language Server Protocol](https://microsoft.github.io/language-server-protocol/) over standard input and standard output. Editors use it for diagnostics, completion, hover, navigation, semantic highlighting, formatting, refactoring assistance, workspace search, and related source-code intelligence.

This document defines the public LSP behavior. It is not a project-history document and does not describe implementation phases.

## 1. Scope

The Stash LSP server provides source analysis for `.stash` files. It does not execute Stash programs and does not define language semantics. When this document and the language specification disagree about syntax or runtime behavior, the [Language Specification](Stash%20—%20Language%20Specification.md) is authoritative.

The language server may report diagnostics for code that would still run at runtime. In particular, explicit type hints are advisory in Stash, so type-hint contradictions are reported as warnings rather than parse errors.

## 2. Transport

| Property        | Value                                                              |
| --------------- | ------------------------------------------------------------------ |
| Transport       | JSON-RPC over standard input and standard output                   |
| Server process  | `stash-lsp` or `dotnet run --project Stash.Lsp` during development |
| Session model   | One language-server process per editor client session              |
| Port usage      | None                                                               |
| Supported files | `.stash` source files                                              |

Protocol output must be written to stdout. Logs must not corrupt stdout.

## 3. Lifecycle

A normal client session follows this sequence:

1. The editor starts the language-server process.
2. The editor sends `initialize`.
3. The server registers handlers and returns capabilities through the LSP framework.
4. The editor sends `initialized`.
5. The server records workspace roots from `workspaceFolders`, `rootUri`, or `rootPath`.
6. The editor opens or changes `.stash` documents.
7. The server analyzes documents and publishes diagnostics.
8. The editor sends feature requests such as completion, hover, definition, references, formatting, and rename.

The server supports both open-document analysis and optional background workspace indexing.

## 4. Documents

The server registers incremental text document synchronization.

| Notification             | Behavior                                                                    |
| ------------------------ | --------------------------------------------------------------------------- |
| `textDocument/didOpen`   | Stores the full document text and schedules analysis.                       |
| `textDocument/didChange` | Applies incremental edits and schedules analysis after a debounce interval. |
| `textDocument/didClose`  | Removes the open-document entry and clears diagnostics for that URI.        |

The default analysis debounce is 25 milliseconds. Clients may change the debounce through workspace configuration.

Open documents are the freshest source of truth. When a file is open, server features use the in-memory document text rather than reading the file from disk.

## 5. Analysis Model

Each analysis pass processes a document through the same conceptual pipeline:

1. Lex source text and retain token spans.
2. Parse tokens into a partial AST with error recovery.
3. Build a scope tree and collect symbols.
4. Resolve imports and enrich visible module symbols.
5. Infer expression and symbol types where possible.
6. Validate semantic rules and produce diagnostics.
7. Cache the analysis result for later LSP requests.

The server is designed for fast full-document analysis rather than incremental parsing. A syntax error in one part of a file should not prevent useful diagnostics, completion, or navigation elsewhere in the file when the parser can recover.

## 6. Positions and Ranges

Stash source spans are 1-based. LSP positions and ranges are 0-based.

Conversions must subtract one from source line and column values when returning LSP ranges, and add one when mapping an LSP position back into Stash source coordinates.

The server should return the narrowest useful selection range for identifiers and expressions while preserving valid enclosing ranges for editor expansion features.

## 7. Diagnostics

Diagnostics are published with `textDocument/publishDiagnostics`.

| Diagnostic kind         | Severity                           | Examples                                                      |
| ----------------------- | ---------------------------------- | ------------------------------------------------------------- |
| Lexical error           | Error                              | Invalid token, unterminated string.                           |
| Parse error             | Error                              | Missing delimiter, malformed statement.                       |
| Semantic error          | Error or warning depending on rule | Undefined variable, invalid control flow, const reassignment. |
| Type-hint contradiction | Warning                            | Assigning a string to a variable explicitly hinted as `int`.  |
| Import error            | Error                              | Missing import file, unresolved imported name.                |

The server may still provide completions and navigation when diagnostics are present. Diagnostics are editor assistance, not a substitute for runtime execution.

### 7.1 Type Hints

Type hints are advisory. The server reports contradictions involving explicit hints as warnings.

Examples:

```stash
let count: int = "many";      // warning
fn add(x: int) { return x; }
add("one");                  // warning
```

Inferred types do not restrict later reassignment. `null` is compatible with explicit type hints for diagnostic purposes.

## 8. Symbols

The server exposes symbols through document symbols, workspace symbols, navigation, references, semantic tokens, code lens, and call hierarchy.

| Symbol kind | Examples                                   |
| ----------- | ------------------------------------------ |
| Function    | `fn build() { ... }`                       |
| Method      | Struct method declarations                 |
| Variable    | `let`, loop variables, function parameters |
| Constant    | `const` declarations                       |
| Struct      | `struct User { ... }`                      |
| Enum        | `enum Status { ... }`                      |
| Field       | Struct fields                              |
| Enum member | Enum variants                              |
| Namespace   | Built-in namespaces and imported aliases   |

Built-in namespace APIs are sourced from the standard-library metadata used by the LSP. The public API list belongs in the [Standard Library Reference](Stash%20—%20Standard%20Library%20Reference.md), not in this document.

## 9. Completion

`textDocument/completion` returns context-aware suggestions.

Supported completion sources:

| Context                                  | Completion candidates                                      |
| ---------------------------------------- | ---------------------------------------------------------- |
| General expression or statement position | Keywords, visible symbols, built-ins.                      |
| Dot access on namespace                  | Namespace members.                                         |
| Dot access on struct value               | Struct fields and methods when the receiver type is known. |
| Import position                          | Symbols available from resolved modules where supported.   |

Completions are suppressed inside string literals except where the syntax context explicitly supports structured completion.

Completion items should prefer stable labels and signatures over prose-heavy detail. Function and method items should include enough parameter information for signature help and documentation display.

## 10. Hover

`textDocument/hover` returns Markdown-formatted information for the symbol at the cursor.

Hover may describe:

- User-defined functions, parameters, variables, constants, structs, enums, fields, and methods.
- Built-in namespace functions and constants.
- Imported symbols and namespace aliases.
- Inferred or explicit type information when available.

Hover must not invent runtime values. It is based on static analysis.

## 11. Signature Help

`textDocument/signatureHelp` returns parameter information for calls to user-defined functions, methods, and built-in functions.

The server should identify the active parameter from the call expression and cursor position. If the target cannot be resolved, the server may return no signature help rather than guessing.

## 12. Navigation

The server supports:

| Method                           | Behavior                                                                             |
| -------------------------------- | ------------------------------------------------------------------------------------ |
| `textDocument/definition`        | Goes to the declaration of a symbol, including resolved imports.                     |
| `textDocument/typeDefinition`    | Goes to the struct or enum definition associated with the selected value when known. |
| `textDocument/implementation`    | Finds known construction or implementation-like uses for selected type symbols.      |
| `textDocument/references`        | Finds references in the current analysis graph.                                      |
| `textDocument/documentHighlight` | Highlights read/write occurrences in the current document.                           |
| `workspace/symbol`               | Searches known workspace symbols by query.                                           |

Cross-file results depend on import resolution and available workspace analysis. With background indexing disabled, cross-file knowledge may be limited to open files and files reached through imports.

## 13. Rename and Linked Editing

`textDocument/prepareRename` validates whether the cursor is on a renameable symbol and returns the rename range and placeholder.

`textDocument/rename` returns a `WorkspaceEdit` for known references to the selected symbol. Rename must not rename keywords, literals, built-in namespace members, or unresolved text that is not a symbol.

`textDocument/linkedEditingRange` returns linked ranges for symbol occurrences when the server can identify a safe set of references in the current document.

## 14. Formatting

The server supports:

| Method                          | Behavior                                                        |
| ------------------------------- | --------------------------------------------------------------- |
| `textDocument/formatting`       | Formats a whole document.                                       |
| `textDocument/rangeFormatting`  | Formats a selected range.                                       |
| `textDocument/onTypeFormatting` | Applies formatting edits triggered by configured typing events. |

Formatting uses editor options such as tab size and spaces-vs-tabs where the client provides them.

The formatter should preserve program meaning. It may normalize whitespace, indentation, and layout, but it must not rewrite expressions or reorder executable code.

## 15. Semantic Tokens

The server supports full semantic tokens. Token classification is based on lexer tokens plus AST and symbol context.

The semantic-token legend includes token kinds for language constructs such as:

- Keywords
- Variables and parameters
- Functions and methods
- Structs, enums, fields, and enum members
- Namespaces
- Strings, numbers, comments, and operators

Supported modifiers include declaration and readonly-style classification where applicable.

Clients should treat semantic tokens as presentation data. They do not alter language behavior.

## 16. Folding and Selection

`textDocument/foldingRange` returns foldable ranges for multi-line blocks and consecutive comment regions.

`textDocument/selectionRange` returns nested ranges that allow the editor to expand selection from a token to enclosing expressions, statements, blocks, and declarations.

## 17. Document Links

`textDocument/documentLink` returns links for import path string literals when the target can be resolved to a file URI.

Unresolved import links may still produce a document link with tooltip information when useful, but clients must not assume that every import path resolves.

## 18. Code Actions

`textDocument/codeAction` returns quick fixes and source actions when the server can produce safe edits.

Supported action categories include:

| Category      | Examples                                                     |
| ------------- | ------------------------------------------------------------ |
| Quick fix     | Replace an undefined variable with a close visible symbol.   |
| Quick fix     | Apply semantic diagnostic code fixes when available.         |
| Source action | Organize imports.                                            |
| Refactor      | Wrap throwing code in a typed `try`/`catch` where supported. |

Code actions must be derived from the current analysis result and should avoid edits when the server cannot prove the target range.

## 19. Inlay Hints

`textDocument/inlayHint` returns inline parameter hints and other lightweight annotations.

Parameter hints use labels such as `name:` before arguments when that improves call readability. Inlay hints are controlled by `stash.inlayHints.enabled` in the VS Code extension and by the corresponding server configuration.

## 20. Code Lens

`textDocument/codeLens` returns editor annotations for declarations when enabled. The current implementation is used primarily for reference counts and testing-related editor affordances.

Code lens is controlled by `stash.codeLens.enabled` in the VS Code extension and by the corresponding server configuration.

## 21. Call Hierarchy

The server supports call hierarchy for functions:

| Method                              | Behavior                                                 |
| ----------------------------------- | -------------------------------------------------------- |
| `textDocument/prepareCallHierarchy` | Returns a call hierarchy item for the selected function. |
| `callHierarchy/incomingCalls`       | Returns known callers of the selected function.          |
| `callHierarchy/outgoingCalls`       | Returns known callees called from the selected function. |

Call hierarchy is static. Dynamic calls that cannot be resolved from the AST may be absent.

## 22. Workspace Indexing

Workspace indexing is optional. When enabled, the server scans `.stash` files under workspace roots and analyzes them in the background.

| Setting                           | Default | Meaning                                |
| --------------------------------- | ------- | -------------------------------------- |
| `stash.workspaceIndexing.enabled` | `false` | Enables background workspace scanning. |

The scanner should respect `.stashignore` patterns. It should not replace fresher analysis for open documents. It may publish refresh requests for semantic tokens and code lens as new files are indexed.

Workspace indexing improves:

- Workspace symbol search.
- Cross-file references.
- Reference-count code lens.
- Semantic refresh after external file changes.

## 23. Watched Files

`workspace/didChangeWatchedFiles` keeps the workspace index and import cache aligned with external file changes.

The server watches `.stash` files. File creation, change, and deletion events may invalidate cached analysis and trigger refresh notifications.

## 24. Settings

The VS Code extension and server configuration support these settings:

| Setting                           | Default   | Meaning                                                                                             |
| --------------------------------- | --------- | --------------------------------------------------------------------------------------------------- |
| `stash.lspPath`                   | empty     | Absolute path to the LSP server binary. If empty, the extension searches for `stash-lsp` on `PATH`. |
| `stash.lsp.debounceTime`          | `25`      | Document-change debounce in milliseconds.                                                           |
| `stash.lsp.logLevel`              | `warning` | Minimum server log level forwarded by the LSP logging pipeline.                                     |
| `stash.trace.server`              | `off`     | Client-side protocol trace setting.                                                                 |
| `stash.inlayHints.enabled`        | `true`    | Enables inlay hints.                                                                                |
| `stash.codeLens.enabled`          | `true`    | Enables code lens.                                                                                  |
| `stash.workspaceIndexing.enabled` | `false`   | Enables background workspace indexing.                                                              |
| `stash.formatting.enabled`        | `true`    | Enables extension-side formatting integration.                                                      |

The server handles configuration changes at runtime where the corresponding setting maps to server state.

## 25. Feature Matrix

| LSP feature                   | Method                                                                                            |
| ----------------------------- | ------------------------------------------------------------------------------------------------- |
| Document sync                 | `textDocument/didOpen`, `textDocument/didChange`, `textDocument/didClose`                         |
| Diagnostics                   | `textDocument/publishDiagnostics`                                                                 |
| Document symbols              | `textDocument/documentSymbol`                                                                     |
| Definition                    | `textDocument/definition`                                                                         |
| Type definition               | `textDocument/typeDefinition`                                                                     |
| Implementation-like locations | `textDocument/implementation`                                                                     |
| Hover                         | `textDocument/hover`                                                                              |
| Completion                    | `textDocument/completion`                                                                         |
| Signature help                | `textDocument/signatureHelp`                                                                      |
| References                    | `textDocument/references`                                                                         |
| Document highlight            | `textDocument/documentHighlight`                                                                  |
| Rename                        | `textDocument/prepareRename`, `textDocument/rename`                                               |
| Semantic tokens               | `textDocument/semanticTokens/full`                                                                |
| Folding                       | `textDocument/foldingRange`                                                                       |
| Selection range               | `textDocument/selectionRange`                                                                     |
| Document links                | `textDocument/documentLink`                                                                       |
| Code actions                  | `textDocument/codeAction`                                                                         |
| Formatting                    | `textDocument/formatting`, `textDocument/rangeFormatting`, `textDocument/onTypeFormatting`        |
| Inlay hints                   | `textDocument/inlayHint`                                                                          |
| Code lens                     | `textDocument/codeLens`                                                                           |
| Call hierarchy                | `textDocument/prepareCallHierarchy`, `callHierarchy/incomingCalls`, `callHierarchy/outgoingCalls` |
| Linked editing                | `textDocument/linkedEditingRange`                                                                 |
| Workspace symbols             | `workspace/symbol`                                                                                |
| Watched files                 | `workspace/didChangeWatchedFiles`                                                                 |
| Configuration changes         | `workspace/didChangeConfiguration`                                                                |

## 26. Build and Test

Build the server:

```bash
dotnet build Stash.Lsp/
```

Run LSP-related tests:

```bash
dotnet test --filter "FullyQualifiedName~Lsp"
```

Protocol or analysis changes should be accompanied by tests for the affected handler or analysis component. Formatting, rename, references, import resolution, diagnostics, and semantic tokens should have focused tests because regressions are highly visible in editors.

## 27. Limitations

| Limitation             | Contract                                                                                                      |
| ---------------------- | ------------------------------------------------------------------------------------------------------------- |
| Static analysis only   | The LSP server does not execute programs and cannot know runtime values.                                      |
| Dynamic calls          | Calls or member accesses that cannot be resolved statically may be absent from navigation and call hierarchy. |
| Workspace completeness | Cross-file references depend on open documents, resolved imports, and optional workspace indexing.            |
| Advisory type hints    | Type-hint diagnostics are warnings, not language errors.                                                      |
| Color presentation     | Not implemented because Stash has no dedicated color literal feature.                                         |

## 28. Change Rules

Changes to the LSP server should preserve these rules:

- Any new public editor feature must be added to the feature matrix.
- Any setting consumed by the extension or server must be documented in [Settings](#24-settings).
- Diagnostics must distinguish parse errors, semantic errors, and advisory warnings.
- Built-in API documentation belongs in the standard-library reference; this document should describe how the LSP consumes built-in metadata.
- Language semantics belong in the language specification. This document should not create new language rules.
