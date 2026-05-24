# Static Analysis — Diagnostic Descriptor Rules

`DiagnosticDescriptors` is the **single source of truth** for every diagnostic the analysis engine produces. Never create a `SemanticDiagnostic` by hand-coding its code, message, or level.

## Adding a New Diagnostic

1. **Define the descriptor** in `Stash.Analysis/Models/DiagnosticDescriptors.cs`:
   - Pick the next available code in the correct category range (SA00xx Infrastructure, SA01xx Control flow, SA02xx Declarations, SA03xx Type safety, SA04xx Functions & calls, SA07xx Commands, SA08xx Imports).
   - Set the `Title`, `DefaultLevel`, `Category`, and `MessageFormat` (use `{0}`, `{1}`, … placeholders for dynamic parts).
   - Register the new descriptor in `BuildCodeLookup()`.

2. **Emit via factory method** at the diagnostic site:

   ```csharp
   // Parameterless message:
   _diagnostics.Add(DiagnosticDescriptors.SA0101.CreateDiagnostic(span));

   // Templated message:
   _diagnostics.Add(DiagnosticDescriptors.SA0301.CreateDiagnostic(span, varName, expectedType, actualType));

   // Faded/unnecessary (unreachable code, unused symbols):
   _diagnostics.Add(DiagnosticDescriptors.SA0104.CreateUnnecessaryDiagnostic(span));
   ```

3. **Never do this:**

   ```csharp
   // WRONG — duplicates message/level, drifts from descriptor
   _diagnostics.Add(new SemanticDiagnostic(
       DiagnosticDescriptors.SA0301.Code,
       $"Variable '{name}' is declared as '{expected}' but initialized with '{actual}'.",
       DiagnosticLevel.Warning,
       span));

   // WRONG — no diagnostic code at all
   _diagnostics.Add(new SemanticDiagnostic(
       "Something went wrong.",
       DiagnosticLevel.Error,
       span));
   ```

## Modifying an Existing Diagnostic

- Change the **message text or severity** only in the descriptor definition in `DiagnosticDescriptors.cs` — all emission sites inherit automatically.
- Never change a diagnostic's code once assigned; codes are stable identifiers used in suppression directives and `.stashcheck` configs.

## Key Files

| File                                             | Role                                                                                   |
| ------------------------------------------------ | -------------------------------------------------------------------------------------- |
| `Stash.Analysis/Models/DiagnosticDescriptor.cs`  | Descriptor record + `CreateDiagnostic` / `CreateUnnecessaryDiagnostic` factory methods |
| `Stash.Analysis/Models/DiagnosticDescriptors.cs` | Central registry of all SA-codes (single source of truth)                              |
| `Stash.Analysis/Models/SemanticDiagnostics.cs`   | Diagnostic data class (do not construct directly for coded diagnostics)                |

## Coordinate convention — 1-based vs 0-based

`ScopeTree.FindScopeAt(line, col)`, `ScopeTree.GetVisibleSymbols(line, col)`, and most other position-taking methods in `Stash.Analysis` use **1-based** coordinates (matching analyzer internals). LSP requests carry **0-based** coordinates. Bridge by adding `+1` to both axes at the LSP/analysis boundary: `analysis.Symbols.FindScopeAt(ctx.LspLine + 1, ctx.LspColumn + 1)`. Forgetting the `+1` is a real footgun — symptoms include "scope classifier returns the wrong ancestor" or "GetVisibleSymbols returns nothing at the cursor". Existing precedent in `Stash.Lsp/Completion/Providers/ScopedSymbolCompletionProvider.cs` and `Stash.Lsp/Completion/Snippets/SnippetContext.cs`.
