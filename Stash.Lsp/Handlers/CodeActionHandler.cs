namespace Stash.Lsp.Handlers;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using Stash.Lsp.Analysis;

public class CodeActionHandler : CodeActionHandlerBase
{
    private readonly AnalysisEngine _analysis;

    public CodeActionHandler(AnalysisEngine analysis)
    {
        _analysis = analysis;
    }

    protected override CodeActionRegistrationOptions CreateRegistrationOptions(
        CodeActionCapability capability, ClientCapabilities clientCapabilities) =>
        new()
        {
            DocumentSelector = new TextDocumentSelector(TextDocumentFilter.ForLanguage("stash")),
            CodeActionKinds = new Container<CodeActionKind>(
                CodeActionKind.QuickFix
            )
        };

    public override Task<CommandOrCodeActionContainer?> Handle(CodeActionParams request,
        CancellationToken cancellationToken)
    {
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

        return Task.FromResult<CommandOrCodeActionContainer?>(
            new CommandOrCodeActionContainer(actions));
    }

    public override Task<CodeAction> Handle(CodeAction request, CancellationToken cancellationToken)
        => Task.FromResult(request);

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
