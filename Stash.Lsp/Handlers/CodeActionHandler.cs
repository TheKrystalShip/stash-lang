namespace Stash.Lsp.Handlers;

using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using Stash.Analysis;
using Stash.Common;
using Stash.Lsp.Analysis;
using Stash.Parsing.AST;
using Stash.Stdlib;

/// <summary>
/// Handles LSP <c>textDocument/codeAction</c> requests to provide quick-fix actions
/// for diagnostics reported by the Stash analyser.
/// </summary>
/// <remarks>
/// <para>
/// Currently implements two categories of quick fixes:
/// </para>
/// <list type="bullet">
///   <item>
///     <term>Did-you-mean suggestions</term>
///     <description>
///       When an identifier is flagged as undefined, the handler uses
///       Levenshtein distance against all in-scope symbols to suggest the closest match
///       (within an edit distance of 3).
///     </description>
///   </item>
///   <item>
///     <term>Remove misplaced control-flow keywords</term>
///     <description>
///       Offers to delete <c>break</c>, <c>continue</c>, or <c>return</c> statements that
///       appear outside their valid enclosing scopes.
///     </description>
///   </item>
/// </list>
/// <para>
/// Uses the <see cref="AnalysisEngine"/>'s cached result to query the symbol table via
/// <c>GetVisibleSymbols</c> for fuzzy name matching.
/// </para>
/// </remarks>
public class CodeActionHandler : CodeActionHandlerBase
{
    private readonly AnalysisEngine _analysis;
    private readonly DocumentManager _documents;
    private readonly WorkspaceScanner _scanner;

    private readonly ILogger<CodeActionHandler> _logger;

    /// <summary>
    /// Initialises the handler with the analysis engine used to retrieve cached document results.
    /// </summary>
    /// <param name="analysis">The analysis engine that supplies cached per-document results.</param>
    /// <param name="documents">The document manager used to retrieve current document text.</param>
    public CodeActionHandler(AnalysisEngine analysis, DocumentManager documents, WorkspaceScanner scanner, ILogger<CodeActionHandler> logger)
    {
        _analysis = analysis;
        _documents = documents;
        _scanner = scanner;
        _logger = logger;
    }

    /// <summary>
    /// Creates the registration options advertising <c>QuickFix</c> code action support.
    /// </summary>
    /// <param name="capability">The client's code action capability descriptor.</param>
    /// <param name="clientCapabilities">The full set of client capabilities.</param>
    /// <returns>Registration options scoped to <c>stash</c> language documents with <c>QuickFix</c> kind.</returns>
    protected override CodeActionRegistrationOptions CreateRegistrationOptions(
        CodeActionCapability capability, ClientCapabilities clientCapabilities) =>
        new()
        {
            DocumentSelector = new TextDocumentSelector(TextDocumentFilter.ForLanguage("stash")),
            CodeActionKinds = new Container<CodeActionKind>(
                CodeActionKind.QuickFix,
                CodeActionKind.Source
            )
        };

    /// <summary>
    /// Processes the code action request and returns applicable quick fixes for the diagnostics in the request context.
    /// </summary>
    /// <param name="request">
    /// The request containing the document URI, the edited range, and the active diagnostics
    /// for which code actions are requested.
    /// </param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>
    /// A <see cref="CommandOrCodeActionContainer"/> with zero or more quick-fix actions,
    /// or <see langword="null"/> if no cached analysis is available for the document.
    /// </returns>
    public override Task<CommandOrCodeActionContainer?> Handle(CodeActionParams request,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("CodeAction request for {Uri}", request.TextDocument.Uri);
        var uri = request.TextDocument.Uri.ToUri();
        var result = _analysis.GetCachedResult(uri);
        if (result == null)
        {
            return Task.FromResult<CommandOrCodeActionContainer?>(null);
        }

        var actions = new List<CommandOrCodeAction>();

        // Map CodeFix objects from SemanticDiagnostic to LSP CodeActions.
        foreach (var diagnostic in request.Context.Diagnostics)
        {
            if (diagnostic.Source != "stash")
            {
                continue;
            }

            string diagnosticCode = diagnostic.Code?.String ?? string.Empty;

            // Find the matching SemanticDiagnostic in the cached result.
            int lspLine = diagnostic.Range.Start.Line + 1;
            int lspCol = diagnostic.Range.Start.Character + 1;

            foreach (var semanticDiag in result.SemanticDiagnostics)
            {
                if (semanticDiag.Code != diagnosticCode
                    || semanticDiag.Span.StartLine != lspLine
                    || semanticDiag.Span.StartColumn != lspCol)
                {
                    continue;
                }

                foreach (var fix in semanticDiag.Fixes)
                {
                    var lspEdits = BuildLspEdits(fix, request.TextDocument.Uri);
                    if (lspEdits == null)
                    {
                        continue;
                    }

                    actions.Add(new CommandOrCodeAction(new CodeAction
                    {
                        Title = fix.Title,
                        Kind = CodeActionKind.QuickFix,
                        Diagnostics = new Container<Diagnostic>(diagnostic),
                        IsPreferred = fix.Applicability == FixApplicability.Safe,
                        Edit = new WorkspaceEdit
                        {
                            Changes = new Dictionary<DocumentUri, IEnumerable<TextEdit>>
                            {
                                [request.TextDocument.Uri] = lspEdits
                            }
                        }
                    }));
                }
            }
        }

        // Legacy message-based quick fixes for existing diagnostics without CodeFix objects.
        foreach (var diagnostic in request.Context.Diagnostics)
        {
            if (diagnostic.Source != "stash")
            {
                continue;
            }

            var message = diagnostic.Message;

            // "Did you mean?" for undefined variables
            if (message.EndsWith("' is not defined.") && message.StartsWith("'"))
            {
                var name = message[1..message.IndexOf("' is not defined.")];
                var line = diagnostic.Range.Start.Line + 1;
                var col = diagnostic.Range.Start.Character + 1;

                var visibleSymbols = result.Symbols.GetVisibleSymbols(line, col);
                string? bestMatch = null;
                int bestDistance = 3; // max acceptable distance

                foreach (var sym in visibleSymbols)
                {
                    int dist = LevenshteinDistance(name, sym.Name);
                    if (dist < bestDistance)
                    {
                        bestDistance = dist;
                        bestMatch = sym.Name;
                    }
                }

                if (bestMatch != null)
                {
                    actions.Add(new CommandOrCodeAction(new CodeAction
                    {
                        Title = $"Replace with '{bestMatch}'",
                        Kind = CodeActionKind.QuickFix,
                        Diagnostics = new Container<Diagnostic>(diagnostic),
                        IsPreferred = true,
                        Edit = new WorkspaceEdit
                        {
                            Changes = new Dictionary<DocumentUri, IEnumerable<TextEdit>>
                            {
                                [request.TextDocument.Uri] = new[]
                                {
                                    new TextEdit
                                    {
                                        Range = diagnostic.Range,
                                        NewText = bestMatch
                                    }
                                }
                            }
                        }
                    }));
                }

                // "Add missing import" — search workspace modules that export the undefined name.
                var currentFilePath = uri.IsFile ? uri.LocalPath : null;
                if (currentFilePath != null)
                {
                    var candidates = CollectImportCandidates(_analysis, _scanner, currentFilePath);
                    var importActions = BuildAddMissingImportActions(
                        name, currentFilePath, candidates, request.TextDocument.Uri, result);
                    foreach (var importAction in importActions)
                        actions.Add(new CommandOrCodeAction(importAction));
                }
            }
            // Remove misplaced break/continue/return
            else if (message == "'break' used outside of a loop." ||
                     message == "'continue' used outside of a loop." ||
                     message == "'return' used outside of a function.")
            {
                var keyword = message.StartsWith("'break'") ? "break"
                    : message.StartsWith("'continue'") ? "continue"
                    : "return";

                actions.Add(new CommandOrCodeAction(new CodeAction
                {
                    Title = $"Remove '{keyword}'",
                    Kind = CodeActionKind.QuickFix,
                    Diagnostics = new Container<Diagnostic>(diagnostic),
                    Edit = new WorkspaceEdit
                    {
                        Changes = new Dictionary<DocumentUri, IEnumerable<TextEdit>>
                        {
                            [request.TextDocument.Uri] = new[]
                            {
                                new TextEdit
                                {
                                    Range = diagnostic.Range,
                                    NewText = ""
                                }
                            }
                        }
                    }
                }));
            }
        }

        // Universal suppression quick-fixes for all SA-code diagnostics.
        var suppressionText = _documents.GetText(uri);
        string[] suppressionLines = suppressionText != null ? suppressionText.Split('\n') : Array.Empty<string>();
        int fileInsertLine = suppressionLines.Length > 0 && suppressionLines[0].StartsWith("#!") ? 1 : 0;

        foreach (var diagnostic in request.Context.Diagnostics)
        {
            if (diagnostic.Source != "stash")
            {
                continue;
            }

            var code = diagnostic.Code?.String;
            if (string.IsNullOrEmpty(code))
            {
                continue;
            }

            int diagLine = diagnostic.Range.Start.Line;
            string indent = "";
            if (diagLine >= 0 && diagLine < suppressionLines.Length)
            {
                string diagLineText = suppressionLines[diagLine];
                int i = 0;
                while (i < diagLineText.Length && (diagLineText[i] == ' ' || diagLineText[i] == '\t'))
                    i++;
                indent = diagLineText[..i];
            }

            actions.Add(new CommandOrCodeAction(new CodeAction
            {
                Title = $"Disable {code} for this line",
                Kind = CodeActionKind.QuickFix,
                IsPreferred = false,
                Diagnostics = new Container<Diagnostic>(diagnostic),
                Edit = new WorkspaceEdit
                {
                    Changes = new Dictionary<DocumentUri, IEnumerable<TextEdit>>
                    {
                        [request.TextDocument.Uri] = new[]
                        {
                            new TextEdit
                            {
                                Range = new OmniSharp.Extensions.LanguageServer.Protocol.Models.Range(diagLine, 0, diagLine, 0),
                                NewText = $"{indent}// stash-disable-next-line {code}\n"
                            }
                        }
                    }
                }
            }));

            actions.Add(new CommandOrCodeAction(new CodeAction
            {
                Title = $"Disable {code} for this file",
                Kind = CodeActionKind.QuickFix,
                IsPreferred = false,
                Diagnostics = new Container<Diagnostic>(diagnostic),
                Edit = new WorkspaceEdit
                {
                    Changes = new Dictionary<DocumentUri, IEnumerable<TextEdit>>
                    {
                        [request.TextDocument.Uri] = new[]
                        {
                            new TextEdit
                            {
                                Range = new OmniSharp.Extensions.LanguageServer.Protocol.Models.Range(fileInsertLine, 0, fileInsertLine, 0),
                                NewText = $"// stash-disable {code}\n"
                            }
                        }
                    }
                }
            }));

            var roots = _scanner.GetRoots();
            if (roots.Count > 0)
            {
                string stashcheckPath = Path.Combine(roots[0], ".stashcheck");
                var stashcheckUri = DocumentUri.From(new Uri(stashcheckPath));
                string disableLine = $"disable = {code}\n";

                if (File.Exists(stashcheckPath))
                {
                    string existingContent = File.ReadAllText(stashcheckPath);
                    int lineCount = existingContent.Split('\n').Length;
                    bool endsWithNewline = existingContent.Length > 0 && existingContent[^1] == '\n';
                    int insertLine = endsWithNewline ? lineCount - 1 : lineCount;
                    string insertText = endsWithNewline ? disableLine : "\n" + disableLine;

                    actions.Add(new CommandOrCodeAction(new CodeAction
                    {
                        Title = $"Disable {code} for this project",
                        Kind = CodeActionKind.QuickFix,
                        IsPreferred = false,
                        Diagnostics = new Container<Diagnostic>(diagnostic),
                        Edit = new WorkspaceEdit
                        {
                            Changes = new Dictionary<DocumentUri, IEnumerable<TextEdit>>
                            {
                                [stashcheckUri] = new[]
                                {
                                    new TextEdit
                                    {
                                        Range = new OmniSharp.Extensions.LanguageServer.Protocol.Models.Range(insertLine, 0, insertLine, 0),
                                        NewText = insertText
                                    }
                                }
                            }
                        }
                    }));
                }
                else
                {
                    actions.Add(new CommandOrCodeAction(new CodeAction
                    {
                        Title = $"Disable {code} for this project",
                        Kind = CodeActionKind.QuickFix,
                        IsPreferred = false,
                        Diagnostics = new Container<Diagnostic>(diagnostic),
                        Edit = new WorkspaceEdit
                        {
                            DocumentChanges = new Container<WorkspaceEditDocumentChange>(
                                new WorkspaceEditDocumentChange(new CreateFile
                                {
                                    Uri = stashcheckUri,
                                    Options = new CreateFileOptions { IgnoreIfExists = true }
                                }),
                                new WorkspaceEditDocumentChange(new TextDocumentEdit
                                {
                                    TextDocument = new OptionalVersionedTextDocumentIdentifier { Uri = stashcheckUri },
                                    Edits = new TextEditContainer(
                                        new TextEdit
                                        {
                                            Range = new OmniSharp.Extensions.LanguageServer.Protocol.Models.Range(0, 0, 0, 0),
                                            NewText = disableLine
                                        }
                                    )
                                })
                            )
                        }
                    }));
                }
            }
        }

        // Organize imports action
        var organizeAction = BuildOrganizeImportsAction(uri, result, request.TextDocument.Uri);
        if (organizeAction != null)
        {
            actions.Add(new CommandOrCodeAction(organizeAction));
        }

        // "Surround with try/catch (typed)" — position-based refactoring action.
        // Fires when the cursor is on (or inside) a statement that calls a function
        // with declared throws that are not already caught.
        {
            int cursorLine1 = request.Range.Start.Line + 1; // AST spans are 1-indexed
            var enclosingStmt = FindLeafStatementAt(result.Statements, cursorLine1);
            if (enclosingStmt != null)
            {
                var errorTypes = CollectUncoveredThrows(enclosingStmt, result.Symbols);
                if (errorTypes.Count > 0)
                {
                    var wrapAction = BuildTryCatchWrapAction(
                        enclosingStmt, errorTypes, suppressionLines, request.TextDocument.Uri);
                    if (wrapAction != null)
                        actions.Add(new CommandOrCodeAction(wrapAction));
                }
            }
        }

        _logger.LogDebug("CodeAction: {Count} actions for {Uri}", actions.Count, request.TextDocument.Uri);
        return Task.FromResult<CommandOrCodeActionContainer?>(
            new CommandOrCodeActionContainer(actions));
    }

    /// <summary>
    /// Resolve pass-through: returns the <see cref="CodeAction"/> unchanged (resolve not required).
    /// </summary>
    /// <param name="request">The code action to resolve.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The same <see cref="CodeAction"/> as received.</returns>
    public override Task<CodeAction> Handle(CodeAction request, CancellationToken cancellationToken)
        => Task.FromResult(request);

    /// <summary>
    /// Converts a <see cref="Stash.Analysis.CodeFix"/> to a list of LSP <see cref="TextEdit"/> objects.
    /// Returns <see langword="null"/> if the fix contains no edits.
    /// </summary>
    private static IEnumerable<OmniSharp.Extensions.LanguageServer.Protocol.Models.TextEdit>? BuildLspEdits(Stash.Analysis.CodeFix fix, DocumentUri documentUri)
    {
        if (fix.Edits.Count == 0)
        {
            return null;
        }

        var lspEdits = new List<OmniSharp.Extensions.LanguageServer.Protocol.Models.TextEdit>(fix.Edits.Count);
        foreach (var edit in fix.Edits)
        {
            // SourceSpan is 1-based inclusive; LSP Range is 0-based with exclusive end.
            int startLine = edit.Span.StartLine - 1;
            int startChar = edit.Span.StartColumn - 1;
            int endLine = edit.Span.EndLine - 1;
            int endChar = edit.Span.EndColumn;  // endCol is inclusive, so +0 makes it exclusive

            // For import removal (NewText == ""), extend the range to the next line
            // so the entire line including the newline is deleted by the editor.
            if (edit.NewText == "")
            {
                endLine++;
                endChar = 0;
            }

            lspEdits.Add(new OmniSharp.Extensions.LanguageServer.Protocol.Models.TextEdit
            {
                Range = new OmniSharp.Extensions.LanguageServer.Protocol.Models.Range(
                    new Position(startLine, startChar),
                    new Position(endLine, endChar)),
                NewText = edit.NewText
            });
        }

        return lspEdits;
    }

    private CodeAction? BuildOrganizeImportsAction(Uri uri, AnalysisResult result, DocumentUri documentUri)
    {
        var text = _documents.GetText(uri);
        if (text == null)
        {
            return null;
        }

        // Collect contiguous import statements from the top of the file
        var imports = new List<Stmt>();
        foreach (var stmt in result.Statements)
        {
            if (stmt is ImportStmt or ImportAsStmt)
            {
                imports.Add(stmt);
            }
            else
            {
                break; // Stop at the first non-import statement
            }
        }

        if (imports.Count == 0)
        {
            return null;
        }

        // Check which imported names are used
        var keptImports = new List<string>();

        foreach (var stmt in imports)
        {
            if (stmt is ImportStmt importStmt)
            {
                var usedNames = new List<string>();
                foreach (var nameToken in importStmt.Names)
                {
                    var refs = result.Symbols.FindReferences(
                        nameToken.Lexeme,
                        nameToken.Span.StartLine,
                        nameToken.Span.StartColumn);
                    if (refs.Count > 1)
                    {
                        usedNames.Add(nameToken.Lexeme);
                    }
                }

                if (usedNames.Count > 0)
                {
                    if (importStmt.StaticPathValue is string importStaticPath)
                    {
                        string names = string.Join(", ", usedNames);
                        keptImports.Add($"import {{ {names} }} from \"{importStaticPath}\";");
                    }
                    else
                    {
                        // Dynamic path — preserve original source text verbatim
                        int sl = importStmt.Span.StartColumn - 1;
                        int el = importStmt.Span.EndColumn;
                        string[] srcLines = text.Split('\n');
                        int startIdx = importStmt.Span.StartLine - 1;
                        int endIdx = importStmt.Span.EndLine - 1;
                        var origParts = new List<string>();
                        for (int li = startIdx; li <= endIdx && li < srcLines.Length; li++)
                            origParts.Add(srcLines[li]);
                        keptImports.Add(string.Join("\n", origParts).TrimEnd());
                    }
                }
            }
            else if (stmt is ImportAsStmt importAsStmt)
            {
                var refs = result.Symbols.FindReferences(
                    importAsStmt.Alias.Lexeme,
                    importAsStmt.Alias.Span.StartLine,
                    importAsStmt.Alias.Span.StartColumn);
                if (refs.Count > 1)
                {
                    if (importAsStmt.StaticPathValue is string nsStaticPath)
                    {
                        keptImports.Add($"import \"{nsStaticPath}\" as {importAsStmt.Alias.Lexeme};");
                    }
                    else
                    {
                        // Dynamic path — preserve original source text verbatim
                        string[] srcLines = text.Split('\n');
                        int startIdx = importAsStmt.Span.StartLine - 1;
                        int endIdx = importAsStmt.Span.EndLine - 1;
                        var origParts = new List<string>();
                        for (int li = startIdx; li <= endIdx && li < srcLines.Length; li++)
                            origParts.Add(srcLines[li]);
                        keptImports.Add(string.Join("\n", origParts).TrimEnd());
                    }
                }
            }
        }

        // Sort alphabetically by path (case-insensitive)
        keptImports.Sort(StringComparer.OrdinalIgnoreCase);

        string organizedText = keptImports.Count > 0
            ? string.Join("\n", keptImports) + "\n"
            : "";

        // SourceSpan is 1-based, LSP Range is 0-based
        int startLine = imports[0].Span.StartLine - 1;
        int endLine = imports[imports.Count - 1].Span.EndLine - 1;

        var lines = text.Split('\n');

        // Include the newline after the last import to replace the full block cleanly
        int rangeEndLine;
        int rangeEndChar;
        if (endLine + 1 < lines.Length)
        {
            rangeEndLine = endLine + 1;
            rangeEndChar = 0;
        }
        else
        {
            rangeEndLine = endLine;
            rangeEndChar = endLine < lines.Length ? lines[endLine].Length : 0;
        }

        var currentImportLines = new List<string>();
        for (int i = startLine; i <= endLine && i < lines.Length; i++)
        {
            currentImportLines.Add(lines[i]);
        }
        string currentText = string.Join("\n", currentImportLines) + "\n";

        if (organizedText == currentText)
        {
            return null;
        }

        return new CodeAction
        {
            Title = "Organize Imports",
            Kind = CodeActionKind.Source,
            Edit = new WorkspaceEdit
            {
                Changes = new Dictionary<DocumentUri, IEnumerable<TextEdit>>
                {
                    [documentUri] = new[]
                    {
                        new TextEdit
                        {
                            Range = new OmniSharp.Extensions.LanguageServer.Protocol.Models.Range(startLine, 0, rangeEndLine, rangeEndChar),
                            NewText = organizedText
                        }
                    }
                }
            }
        };
    }

    // ── "Add missing import" helpers ────────────────────────────────────────────────

    /// <summary>
    /// Collects candidate (absolutePath, ModuleInfo) pairs from the workspace for "add missing
    /// import" suggestions.  Only files already cached in the <see cref="ImportResolver"/> or
    /// discovered via the workspace scanner roots are considered.
    /// </summary>
    private static IEnumerable<(string AbsolutePath, ImportResolver.ModuleInfo ModuleInfo)>
        CollectImportCandidates(AnalysisEngine analysis, WorkspaceScanner scanner, string currentFilePath)
    {
        var roots = scanner.GetRoots();
        if (roots.Count == 0)
            yield break;

        foreach (var root in roots)
        {
            IEnumerable<string> stashFiles;
            try
            {
                stashFiles = Directory.EnumerateFiles(root, "*.stash", SearchOption.AllDirectories);
            }
            catch (Exception)
            {
                continue;
            }

            foreach (var filePath in stashFiles)
            {
                if (string.Equals(filePath, currentFilePath, StringComparison.OrdinalIgnoreCase))
                    continue;

                var moduleInfo = analysis.ImportResolver.GetModule(filePath);
                if (moduleInfo != null)
                    yield return (filePath, moduleInfo);
            }
        }
    }

    /// <summary>
    /// Produces "Add import" <see cref="CodeAction"/> instances for each workspace module that
    /// exports <paramref name="symbolName"/>.  Filters by <see cref="ImportResolver.ModuleInfo.Exports"/>
    /// when the module has explicit exports; falls back to top-level symbol lookup for v3 on-disk chunks (Exports == null fallback).
    /// </summary>
    /// <returns>
    /// One <see cref="CodeAction"/> per matching module, or an empty sequence when no candidates exist.
    /// </returns>
    internal static IEnumerable<CodeAction> BuildAddMissingImportActions(
        string symbolName,
        string currentFilePath,
        IEnumerable<(string AbsolutePath, ImportResolver.ModuleInfo ModuleInfo)> candidates,
        DocumentUri documentUri,
        AnalysisResult currentResult)
    {
        foreach (var (absolutePath, moduleInfo) in candidates)
        {
            if (!ModuleExportsSymbol(moduleInfo, symbolName))
                continue;

            // Compute the import path relative to the current file's directory.
            var currentDir = Path.GetDirectoryName(currentFilePath) ?? string.Empty;
            string relativePath;
            try
            {
                relativePath = Path.GetRelativePath(currentDir, absolutePath)
                    .Replace('\\', '/');
            }
            catch (Exception)
            {
                continue;
            }

            // Ensure the path starts with "./" so the Stash importer treats it as relative.
            if (!relativePath.StartsWith("./", StringComparison.Ordinal) &&
                !relativePath.StartsWith("../", StringComparison.Ordinal))
            {
                relativePath = "./" + relativePath;
            }

            // Determine the insert line: after any existing imports at the top of the file.
            int insertLine = 0;
            foreach (var stmt in currentResult.Statements)
            {
                if (stmt is ImportStmt or ImportAsStmt)
                    insertLine = stmt.Span.EndLine; // 1-based; this advances past the import
                else
                    break;
            }
            // insertLine is now 1-based end line of last import, or 0 — convert to 0-based for LSP.
            // (No adjustment needed when insertLine == 0: insert at line 0.)

            yield return new CodeAction
            {
                Title = $"Add import '{symbolName}' from \"{relativePath}\"",
                Kind = CodeActionKind.QuickFix,
                Edit = new WorkspaceEdit
                {
                    Changes = new Dictionary<DocumentUri, IEnumerable<TextEdit>>
                    {
                        [documentUri] = new[]
                        {
                            new TextEdit
                            {
                                Range = new OmniSharp.Extensions.LanguageServer.Protocol.Models.Range(
                                    insertLine, 0, insertLine, 0),
                                NewText = $"import {{ {symbolName} }} from \"{relativePath}\";\n"
                            }
                        }
                    }
                }
            };
        }
    }

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="moduleInfo"/> exposes
    /// <paramref name="symbolName"/> to importers.
    /// When <see cref="ImportResolver.ModuleInfo.Exports"/> is non-null, only names in
    /// <see cref="Stash.Core.Resolution.ModuleExports.Names"/> are visible.
    /// When <see cref="ImportResolver.ModuleInfo.Exports"/> is <see langword="null"/> (v3
    /// on-disk chunk), every top-level symbol is treated as exported (legacy fallback).
    /// </summary>
    internal static bool ModuleExportsSymbol(ImportResolver.ModuleInfo moduleInfo, string symbolName)
    {
        if (moduleInfo.Exports != null)
        {
            // Module has an export set — only names in that set are visible.
            return moduleInfo.Exports.Names.Contains(symbolName);
        }

        // v3 on-disk chunk (Exports == null): every top-level symbol is exported.
        return moduleInfo.Symbols.GetTopLevel().Any(s => s.Name == symbolName);
    }

    // ── "Surround with try/catch (typed)" helpers ────────────────────────────────────

    /// <summary>
    /// Finds the innermost leaf statement (non-compound) that contains <paramref name="cursorLine"/>.
    /// Returns <c>null</c> if the cursor is inside a <c>try</c> block (already protected)
    /// or no matching statement is found.
    /// </summary>
    internal static Stmt? FindLeafStatementAt(IReadOnlyList<Stmt> stmts, int cursorLine)
    {
        foreach (var stmt in stmts)
        {
            if (stmt.Span.StartLine > cursorLine || stmt.Span.EndLine < cursorLine)
                continue;

            switch (stmt)
            {
                case TryCatchStmt:
                    // Inside a try block → already protected; suppress action.
                    return null;

                case FnDeclStmt fn:
                    return FindLeafStatementAt(fn.Body.Statements, cursorLine);

                case BlockStmt blk:
                    return FindLeafStatementAt(blk.Statements, cursorLine);

                case IfStmt ifStmt:
                {
                    Stmt? found = null;
                    if (ifStmt.ThenBranch is BlockStmt tb)
                        found = FindLeafStatementAt(tb.Statements, cursorLine);
                    else if (ifStmt.ThenBranch.Span.StartLine <= cursorLine && ifStmt.ThenBranch.Span.EndLine >= cursorLine)
                        found = FindLeafStatementAt(new[] { ifStmt.ThenBranch }, cursorLine);

                    if (found != null) return found;

                    if (ifStmt.ElseBranch is BlockStmt eb)
                        found = FindLeafStatementAt(eb.Statements, cursorLine);
                    else if (ifStmt.ElseBranch != null && ifStmt.ElseBranch.Span.StartLine <= cursorLine && ifStmt.ElseBranch.Span.EndLine >= cursorLine)
                        found = FindLeafStatementAt(new[] { ifStmt.ElseBranch }, cursorLine);

                    return found;
                }

                case WhileStmt w:
                    return FindLeafStatementAt(w.Body.Statements, cursorLine);

                case DoWhileStmt dw:
                    return FindLeafStatementAt(dw.Body.Statements, cursorLine);

                case ForInStmt fi:
                    return FindLeafStatementAt(fi.Body.Statements, cursorLine);

                case ForStmt f:
                    return FindLeafStatementAt(f.Body.Statements, cursorLine);

                case LockStmt lk:
                    return FindLeafStatementAt(lk.Body.Statements, cursorLine);

                default:
                    return stmt; // Leaf statement (ExprStmt, VarDeclStmt, ReturnStmt, etc.)
            }
        }

        return null;
    }

    /// <summary>
    /// Collects the unique set of error type names declared as thrown by any function call
    /// reachable in <paramref name="stmt"/> that are not already wrapped in a nested try/catch.
    /// Excludes error types covered by a parent catch-all (none at this level — that's
    /// handled by <see cref="FindLeafStatementAt"/> returning null for TryCatchStmt).
    /// </summary>
    internal static IReadOnlyList<string> CollectUncoveredThrows(Stmt stmt, ScopeTree scopeTree)
    {
        var rawResults = new List<(string FnName, string ErrorType, SourceSpan Span)>();
        CollectThrowsInStmt(stmt, scopeTree, rawResults);

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var unique = new List<string>();
        foreach (var (_, errorType, _) in rawResults)
            if (seen.Add(errorType))
                unique.Add(errorType);

        return unique;
    }

    private static void CollectThrowsInStmt(
        Stmt stmt,
        ScopeTree scopeTree,
        List<(string, string, SourceSpan)> results)
    {
        switch (stmt)
        {
            case ExprStmt exprStmt:
                CollectThrowsInExpr(exprStmt.Expression, scopeTree, results);
                break;
            case VarDeclStmt varDecl when varDecl.Initializer != null:
                CollectThrowsInExpr(varDecl.Initializer, scopeTree, results);
                break;
            case ConstDeclStmt constDecl:
                CollectThrowsInExpr(constDecl.Initializer, scopeTree, results);
                break;
            case ReturnStmt ret when ret.Value != null:
                CollectThrowsInExpr(ret.Value, scopeTree, results);
                break;
        }
    }

    private static void CollectThrowsInExpr(
        Expr expr,
        ScopeTree scopeTree,
        List<(string, string, SourceSpan)> results)
    {
        if (expr is TryExpr)
            return; // universal catch-all — skip

        if (expr is CallExpr call)
        {
            ResolveCallThrowsForWrap(call, scopeTree, results);
            foreach (var arg in call.Arguments)
                CollectThrowsInExpr(arg, scopeTree, results);
        }
        else
        {
            switch (expr)
            {
                case BinaryExpr bin:
                    CollectThrowsInExpr(bin.Left, scopeTree, results);
                    CollectThrowsInExpr(bin.Right, scopeTree, results);
                    break;
                case UnaryExpr un:
                    CollectThrowsInExpr(un.Right, scopeTree, results);
                    break;
                case GroupingExpr group:
                    CollectThrowsInExpr(group.Expression, scopeTree, results);
                    break;
                case TernaryExpr tern:
                    CollectThrowsInExpr(tern.Condition, scopeTree, results);
                    CollectThrowsInExpr(tern.ThenBranch, scopeTree, results);
                    CollectThrowsInExpr(tern.ElseBranch, scopeTree, results);
                    break;
                case DotExpr dotExpr:
                    CollectThrowsInExpr(dotExpr.Object, scopeTree, results);
                    break;
                case NullCoalesceExpr coalesce:
                    CollectThrowsInExpr(coalesce.Left, scopeTree, results);
                    CollectThrowsInExpr(coalesce.Right, scopeTree, results);
                    break;
                case ArrayExpr arr:
                    foreach (var elem in arr.Elements)
                        CollectThrowsInExpr(elem, scopeTree, results);
                    break;
                case InterpolatedStringExpr interp:
                    foreach (var part in interp.Parts)
                        CollectThrowsInExpr(part, scopeTree, results);
                    break;
            }
        }
    }

    private static void ResolveCallThrowsForWrap(
        CallExpr call,
        ScopeTree scopeTree,
        List<(string, string, SourceSpan)> results)
    {
        // Stdlib call: ns.fn(…)
        if (call.Callee is DotExpr dot &&
            dot.Object is IdentifierExpr nsId &&
            StdlibRegistry.IsBuiltInNamespace(nsId.Name.Lexeme))
        {
            var qualName = $"{nsId.Name.Lexeme}.{dot.Name.Lexeme}";
            if (StdlibRegistry.TryGetNamespaceFunction(qualName, out var nsFn) && nsFn.Throws != null)
            {
                foreach (var t in nsFn.Throws)
                    results.Add((qualName, t.ErrorType, call.Span));
            }
            return;
        }

        // User function call: fn(…)
        if (call.Callee is IdentifierExpr fnId)
        {
            var def = scopeTree.FindDefinition(
                fnId.Name.Lexeme, fnId.Span.StartLine, fnId.Span.StartColumn);
            if (def?.Throws != null)
            {
                foreach (var t in def.Throws)
                    results.Add((fnId.Name.Lexeme, t.ErrorType, call.Span));
            }
        }
    }

    /// <summary>
    /// Builds a <see cref="CodeAction"/> that wraps <paramref name="stmt"/> in a typed try/catch.
    /// </summary>
    internal static CodeAction? BuildTryCatchWrapAction(
        Stmt stmt,
        IReadOnlyList<string> errorTypes,
        string[] lines,
        DocumentUri documentUri)
    {
        int startLine0 = stmt.Span.StartLine - 1; // convert to 0-indexed
        int endLine0 = stmt.Span.EndLine - 1;

        if (startLine0 < 0 || endLine0 >= lines.Length)
            return null;

        // Extract indentation from the statement's first line.
        string firstLine = lines[startLine0];
        int k = 0;
        while (k < firstLine.Length && (firstLine[k] == ' ' || firstLine[k] == '\t'))
            k++;
        string indent = firstLine[..k];
        string innerIndent = indent + "    ";

        // Build the replacement text.
        var sb = new System.Text.StringBuilder();
        sb.Append(indent);
        sb.AppendLine("try {");

        for (int i = startLine0; i <= endLine0; i++)
        {
            string rawLine = lines[i];
            // Re-indent: strip existing base indent and add inner indent.
            string content = rawLine.StartsWith(indent, StringComparison.Ordinal)
                ? rawLine[indent.Length..]
                : rawLine;
            sb.Append(innerIndent);
            sb.AppendLine(content);
        }

        sb.Append(indent);
        sb.AppendLine("}");

        foreach (var errorType in errorTypes)
        {
            sb.Append(indent);
            sb.AppendLine($"catch ({errorType} e) {{");
            sb.Append(innerIndent);
            sb.AppendLine($"// handle {errorType}");
            sb.Append(indent);
            sb.AppendLine("}");
        }

        // Remove trailing newline added by the last AppendLine.
        string newText = sb.ToString().TrimEnd('\r', '\n');

        string catchLabel = errorTypes.Count == 1
            ? $"({errorTypes[0]})"
            : $"({string.Join(", ", errorTypes)})";

        return new CodeAction
        {
            Title = $"Surround with try/catch {catchLabel}",
            Kind = CodeActionKind.Refactor,
            Edit = new WorkspaceEdit
            {
                Changes = new Dictionary<DocumentUri, IEnumerable<TextEdit>>
                {
                    [documentUri] = new[]
                    {
                        new TextEdit
                        {
                            Range = new OmniSharp.Extensions.LanguageServer.Protocol.Models.Range(
                                startLine0, 0, endLine0, lines[endLine0].Length),
                            NewText = newText
                        }
                    }
                }
            }
        };
    }

    /// <summary>
    /// Computes the Levenshtein (edit) distance between two strings using a case-insensitive
    /// dynamic-programming approach.
    /// </summary>
    /// <param name="s">The source string.</param>
    /// <param name="t">The target string.</param>
    /// <returns>The minimum number of single-character edits required to transform <paramref name="s"/> into <paramref name="t"/>.</returns>
    private static int LevenshteinDistance(string s, string t)
    {
        int n = s.Length, m = t.Length;
        if (n == 0)
        {
            return m;
        }

        if (m == 0)
        {
            return n;
        }

        var d = new int[n + 1, m + 1];
        for (int i = 0; i <= n; i++)
        {
            d[i, 0] = i;
        }

        for (int j = 0; j <= m; j++)
        {
            d[0, j] = j;
        }

        for (int i = 1; i <= n; i++)
        {
            for (int j = 1; j <= m; j++)
            {
                int cost = char.ToLowerInvariant(s[i - 1]) == char.ToLowerInvariant(t[j - 1]) ? 0 : 1;
                d[i, j] = Math.Min(
                    Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                    d[i - 1, j - 1] + cost);
            }
        }

        return d[n, m];
    }
}
