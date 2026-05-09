<!--
Thanks for contributing to Stash. Please fill out the relevant sections below.
For language and stdlib changes, the checklist is mandatory — see .claude/language-changes.md
for the rationale behind each item.
-->

## Summary

<!-- What does this PR do, and why? One or two sentences. -->

## Type of change

- [ ] Bug fix
- [ ] New feature (language or stdlib)
- [ ] Refactor / internal cleanup
- [ ] Documentation
- [ ] Tooling (LSP / DAP / Format / Check / VS Code extension)
- [ ] Performance
- [ ] Build / CI / release

## Linked issue or spec

<!-- e.g. Fixes #123, or links to a .kanban/ spec file. -->

## Language & stdlib change checklist

If this PR changes language semantics or adds/removes/modifies stdlib functions, ALL of the
following must be addressed. Skip the section entirely if it doesn't apply.

### Documentation

- [ ] Updated `docs/Stash — Language Specification.md` (syntax, types, operators, scoping, control flow)
- [ ] Updated `docs/Stash — Standard Library Reference.md` (namespaces, signatures, return types)
- [ ] Updated Table of Contents if a new section was added
- [ ] Removed sections for deleted functionality

### Tooling compatibility (verify each, not all need changes)

- [ ] **LSP** — semantic tokens, completions, hover, diagnostics, signature help
- [ ] **DAP** — variable display, expression evaluation, watch expressions
- [ ] **Playground** — Monaco tokenizer keywords, sandbox capability gates
- [ ] **VS Code extension** — TextMate grammar, language configuration
- [ ] **Static analysis** — resolver visitors, type inference, diagnostic rules

### Visitor pattern

- [ ] If a new AST node was added, ALL six visitors are updated:
  - [ ] `Compiler` (across its partials)
  - [ ] `SemanticResolver`
  - [ ] `SemanticValidator`
  - [ ] `SymbolCollector`
  - [ ] `SemanticTokenWalker`
  - [ ] `StashFormatter`

### Examples

- [ ] Added or updated a `.stash` file in `examples/` that exercises the change

### Tests

- [ ] xUnit tests added in `Stash.Tests/` covering happy paths, edges, and error conditions
- [ ] `dotnet test` passes locally with zero failures

## VM / Bytecode changes

- [ ] If opcodes were added, removed, or renumbered, the bytecode serialiser version was bumped
- [ ] `--disassemble` output reviewed for the affected scripts
- [ ] Fused / specialized opcodes still emit correctly under `--optimize` and `--no-optimize`

## Risk / blast radius

<!-- Anything reviewers should pay extra attention to? Performance regressions you've measured?
     Cross-platform behaviour you can't test locally? -->

## Screenshots / output

<!-- For UI changes (Playground, VS Code extension), REPL behaviour, formatter output, or
     diagnostic messages, paste a before/after sample. -->
