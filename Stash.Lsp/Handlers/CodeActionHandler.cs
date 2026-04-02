namespace Stash.Lsp.Handlers;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using Stash.Analysis;
using Stash.Lsp.Analysis;
using Stash.Parsing.AST;

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

    private readonly ILogger<CodeActionHandler> _logger;

    /// <summary>
    /// Initialises the handler with the analysis engine used to retrieve cached document results.
    /// </summary>
    /// <param name="analysis">The analysis engine that supplies cached per-document results.</param>
    /// <param name="documents">The document manager used to retrieve current document text.</param>
    public CodeActionHandler(AnalysisEngine analysis, DocumentManager documents, ILogger<CodeActionHandler> logger)
    {
        _analysis = analysis;
        _documents = documents;
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

        // Organize imports action
        var organizeAction = BuildOrganizeImportsAction(uri, result, request.TextDocument.Uri);
        if (organizeAction != null)
        {
            actions.Add(new CommandOrCodeAction(organizeAction));
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
