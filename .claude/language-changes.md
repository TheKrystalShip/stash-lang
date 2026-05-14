# Language & Standard Library Change Checklist

Every change to the Stash language or its standard library MUST complete **all** applicable steps below. Do not consider a feature done until each item is addressed.

## 1. Documentation (MANDATORY)

| What changed                                                        | How to update                                                                                       |
| ------------------------------------------------------------------- | --------------------------------------------------------------------------------------------------- |
| Syntax, types, operators, literals, keywords, control flow, scoping | Edit `docs/Stash — Language Specification.md` directly                                              |
| Namespace functions, signatures, return types, new namespaces       | Update metadata (see below), then run `dotnet run --project Stash.Docs/` to regenerate the reference |
| Built-in error types (`[StashError]`)                               | Update `Description`, `Properties`, `PropertyTypes` on the attribute, then regenerate               |

**`docs/Stash — Standard Library Reference.md` is generated — never edit it by hand.**
Its API inventory is produced from `StdlibDefinitions` and `BuiltInErrorRegistry` metadata by `Stash.Docs`.
Editing the Markdown directly will cause `StandardLibraryReferenceTests` to fail on the next run.

### Updating stdlib metadata

For namespace functions and signatures:
- Add or update `[StashFn]`, `[StashParam]`, `[StashDeprecated]` attributes on the built-in method.
- Add or update XML `<summary>`, `<param>`, `<returns>`, `<exception>` comments on the method.
- Register the function in the namespace builder (`Stash.Stdlib/BuiltIns/<Namespace>BuiltIns.cs`).

For built-in error types:
- Add `Description = "..."` to `[StashError]` for the "when thrown" text.
- Add `Properties` and `PropertyTypes` for any extra Stash-accessible fields.

After updating metadata, regenerate and commit both the metadata change and the updated reference:

```bash
dotnet run --project Stash.Docs/
```

- Add the feature to the language specification with full syntax, semantics, and examples.
- If a new section is needed in the spec, add it and update the Table of Contents.
- Removals or breaking changes must be reflected in both the spec and the metadata.

## 2. Tooling Compatibility (MANDATORY — verify each)

Every language or stdlib change must be checked against the full toolchain. For each component, determine whether it needs modifications and apply them.

| Component             | What to check                                                    | Key files                                                             |
| --------------------- | ---------------------------------------------------------------- | --------------------------------------------------------------------- |
| **LSP**               | Semantic tokens, completions, hover, diagnostics, signature help | `Stash.Lsp/Handlers/SemanticTokensHandler.cs`, `CompletionHandler.cs` |
| **DAP**               | Variable display, expression evaluation, watch expressions       | `Stash.Dap/DebugSession.cs`                                           |
| **Playground**        | Monarch tokenizer keywords/patterns, sandbox capability gates    | `Stash.Playground/wwwroot/js/stash-language.js`                       |
| **VS Code extension** | TextMate grammar patterns, language configuration                | `.vscode/extensions/stash-lang/syntaxes/stash.tmLanguage.json`        |
| **Static analysis**   | Resolver visitors, type inference, diagnostic rules              | `Stash.Analysis/`                                                     |

Not every component needs changes for every feature — but each must be **explicitly verified**.

## 3. Example Script (MANDATORY)

Create or update a `.stash` file in `examples/` that showcases the new functionality.

- File name should clearly describe the feature (e.g., `durations.stash`, `ip_addresses.stash`).
- Demonstrate the feature's key capabilities: core syntax, property access, operators, practical use cases.
- Follow existing example style — use `io.println()` to show results, include comments explaining what's happening.

## 4. Tests (MANDATORY)

- Add xUnit tests in `Stash.Tests/` covering happy paths, edge cases, and error conditions.
- Follow naming: `{Feature}_{Scenario}_{Expected}()`.
- Run `dotnet test` and confirm zero failures before considering the feature complete.
