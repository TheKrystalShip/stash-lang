# LSP Agent Tools

Expose language server capabilities as **Language Model Tools** for AI coding agents in VS Code.

When an AI agent needs to understand your code — what type a variable is, what functions a file contains, who calls a method — it typically reads entire files or runs text searches. Meanwhile, the language server already knows the answer. This extension bridges that gap by exposing 7 LSP features as tools that agents can call directly.

**Works with any language server** — TypeScript, Python, C#, Rust, Stash, and any other language with a VS Code extension that provides LSP support.

---

## Tools

### `lsp_hover` — Type Information & Documentation

Get type signatures, doc comments, and documentation for any symbol.

```
Agent: "What type is `config`?"
→ lsp_hover(file, "config")
→ const config: DeployConfig
```

Instead of reading 200 lines to find a return type, the agent gets the answer in one call.

---

### `lsp_documentSymbols` — File Structure Outline

Get a structured, hierarchical outline of all symbols in a file — functions, classes, structs, enums, methods, variables — with line ranges. Optionally filter by symbol kind.

```
Agent: "What's in deploy.stash?"
→ lsp_documentSymbols("deploy.stash")
→ fn main() — line 1-45
  fn loadConfig(path: string) — line 47-62
  struct DeployTarget — line 91-97
    property host: string — line 92
    property port: int — line 93
  enum DeployStatus — line 144-149
```

Replaces reading entire files just to understand their structure.

---

### `lsp_typeDefinition` — Navigate to Type Definitions

Navigate from a variable to the definition of its **type**. Given `let target: DeployTarget = ...`, this finds where `DeployTarget` is defined — not where `target` is declared.

Falls back to hover information if the language server doesn't support type definition.

---

### `lsp_callHierarchy` — Incoming & Outgoing Calls

Get directed call relationships for impact analysis:

- **Incoming** — who calls this function?
- **Outgoing** — what does this function call?

Supports traversal up to 3 levels deep. Unlike reference search, this provides _directed_ call relationships.

---

### `lsp_workspaceSymbols` — Search Symbols Across the Workspace

Semantic symbol search across all files — finds functions, structs, classes, enums, interfaces, and more by name. Supports partial matching and kind filtering.

Unlike text search, this understands re-exports, aliases, and symbol semantics.

---

### `lsp_codeActions` — Query & Apply Quick Fixes

Discover available code actions at a specific line — quick fixes, refactorings, and source actions. Optionally apply an action by title.

When a diagnostic exists, the language server often already has the fix computed (add import, fix typo, extract method). The agent can use it instead of inventing its own.

---

### `lsp_signatureHelp` — Function Parameter Information

Get parameter names, types, order, and documentation for function calls. Includes overload information where supported.

---

## How It Works

Each tool accepts **symbol names** and **file paths** (not cursor positions) and resolves them internally using word-boundary matching. This matches how AI agents think about code.

All tools (except `lsp_codeActions` in apply mode) are **read-only** — they query information without modifying files.

### Position Resolution

Tools locate symbols using a 3-tier strategy:

1. **`lineContent`** — scan for a line containing this substring, then find the symbol within it
2. **`line`** — go to this 1-based line number and find the symbol
3. **Fallback** — scan the entire file for the first word-boundary match

### Output Format

All tools return compact **Markdown** optimized for token efficiency:

- File locations use workspace-relative paths
- Large results are truncated with `...(+N more)` counts
- Hierarchical data preserves structure through indentation

---

## Requirements

- VS Code 1.99.0 or later
- A language extension that provides LSP support for your language (e.g., the built-in TypeScript extension, Pylance for Python, the Stash Language extension for Stash)
- GitHub Copilot or another AI agent that supports Language Model Tools

---

## Extension Settings

This extension has no configurable settings. It automatically works with whatever language servers are active in your workspace.

---

## FAQ

**Q: Does this replace `grep_search` or `read_file`?**
No. These tools complement existing agent capabilities. Use `grep_search` for exact text matching, `read_file` for understanding logic flow, and LSP tools for type-aware and structure-aware queries.

**Q: What happens if there's no language server for a file?**
The tool returns a clear message: _"No language server provides [feature] for [language] files."_

**Q: Does this work with any language?**
Yes. It works with any language server — TypeScript, Python, C#, Rust, Go, Java, and any other language with VS Code LSP support.

---

## License

[GPL-3.0](LICENSE)
