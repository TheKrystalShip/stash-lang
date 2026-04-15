# Changelog

All notable changes to the **LSP Agent Tools** extension will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [0.1.0] — 2026-04-16

### Added

- **`lsp_hover`** — Get type information, documentation, and signatures for any symbol via the language server's hover provider
- **`lsp_documentSymbols`** — Get a structured, hierarchical outline of all symbols in a file with optional kind filtering
- **`lsp_typeDefinition`** — Navigate from a variable to the definition of its type, with hover fallback
- **`lsp_callHierarchy`** — Get incoming callers and outgoing callees for a function, with depth up to 3 levels
- **`lsp_workspaceSymbols`** — Search for symbols by name across the entire workspace with partial matching and kind filtering
- **`lsp_codeActions`** — Query and apply quick fixes, refactorings, and source actions at a specific location
- **`lsp_signatureHelp`** — Get function parameter information including names, types, and documentation
- Position resolver with 3-tier resolution: lineContent → line number → full document scan
- Word-boundary matching to avoid false positives (e.g., `config` won't match `loadConfig`)
- Token-efficient Markdown output with truncation budgets
- Graceful degradation when language servers are unavailable or still starting
