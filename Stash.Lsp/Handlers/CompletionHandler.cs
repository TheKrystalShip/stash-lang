namespace Stash.Lsp.Handlers;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using Stash.Lsp.Analysis;
using LspCompletionItemKind = OmniSharp.Extensions.LanguageServer.Protocol.Models.CompletionItemKind;

public class CompletionHandler : CompletionHandlerBase
{
    private readonly AnalysisEngine _analysis;
    private readonly DocumentManager _documents;

    public CompletionHandler(AnalysisEngine analysis, DocumentManager documents)
    {
        _analysis = analysis;
        _documents = documents;
    }

    public override Task<CompletionList> Handle(CompletionParams request, CancellationToken cancellationToken)
    {
        var uri = request.TextDocument.Uri.ToUri();
        var text = _documents.GetText(uri);
        var line = (int)request.Position.Line;
        var col = (int)request.Position.Character;

        string? currentLine = null;
        if (text != null)
        {
            var lines = text.Split('\n');
            if (line < lines.Length)
            {
                currentLine = lines[line];
            }
        }

        // Suppress completions inside strings
        if (currentLine != null && IsInsideString(currentLine, col))
        {
            return Task.FromResult(new CompletionList());
        }

        // Dot completion: suggest only members of the prefix
        if (currentLine != null && col > 0 && col <= currentLine.Length)
        {
            var prefix = GetDotPrefix(currentLine, col);
            if (prefix != null)
            {
                return Task.FromResult(HandleDotCompletion(prefix, uri));
            }
        }

        // Default: full completion list
        return Task.FromResult(BuildFullCompletionList(uri, line + 1, col + 1));
    }

    private CompletionList BuildFullCompletionList(Uri uri, int line, int col)
    {
        var items = new List<CompletionItem>();

        // Keywords
        foreach (var kw in BuiltInRegistry.Keywords)
        {
            items.Add(new CompletionItem
            {
                Label = kw,
                Kind = LspCompletionItemKind.Keyword,
                Detail = "keyword"
            });
        }

        // Built-in functions
        foreach (var fn in BuiltInRegistry.Functions)
        {
            items.Add(new CompletionItem
            {
                Label = fn.Name,
                Kind = LspCompletionItemKind.Function,
                Detail = fn.Detail
            });
        }

        // Built-in namespaces
        foreach (var ns in BuiltInRegistry.NamespaceNames)
        {
            items.Add(new CompletionItem
            {
                Label = ns,
                Kind = LspCompletionItemKind.Module,
                Detail = $"namespace {ns}"
            });
        }

        // Symbols from analysis — scoped to cursor position
        var result = _analysis.GetCachedResult(uri);
        if (result != null)
        {
            var seen = new HashSet<string>();
            foreach (var sym in result.Symbols.GetVisibleSymbols(line, col))
            {
                if (!seen.Add(sym.Name))
                {
                    continue;
                }

                items.Add(new CompletionItem
                {
                    Label = sym.Name,
                    Kind = MapCompletionKind(sym.Kind),
                    Detail = sym.Detail
                });
            }
        }

        return new CompletionList(items);
    }

    private static bool IsInsideString(string line, int col)
    {
        int quoteCount = 0;
        for (int i = 0; i < col && i < line.Length; i++)
        {
            if (line[i] == '"' && (i == 0 || line[i - 1] != '\\'))
            {
                quoteCount++;
            }
        }
        return quoteCount % 2 != 0;
    }

    private static string? GetDotPrefix(string line, int col)
    {
        // col is 0-based cursor position; the dot is at col-1
        if (col < 2 || col - 1 >= line.Length || line[col - 1] != '.')
        {
            return null;
        }

        // Walk backwards from col-2 to find identifier
        int end = col - 2;
        while (end >= 0 && (char.IsLetterOrDigit(line[end]) || line[end] == '_'))
        {
            end--;
        }

        end++;

        if (end >= col - 1)
        {
            return null; // empty prefix
        }

        return line.Substring(end, col - 1 - end);
    }

    private CompletionList HandleDotCompletion(string prefix, Uri uri)
    {
        var items = new List<CompletionItem>();

        // Check if it's a known built-in namespace
        if (BuiltInRegistry.IsBuiltInNamespace(prefix))
        {
            foreach (var fn in BuiltInRegistry.GetNamespaceMembers(prefix))
            {
                items.Add(new CompletionItem
                {
                    Label = fn.Name,
                    Kind = LspCompletionItemKind.Function,
                    Detail = fn.Detail
                });
            }
            return new CompletionList(items);
        }

        // Check if prefix is a namespace import alias
        var result = _analysis.GetCachedResult(uri);
        if (result != null && result.NamespaceImports.TryGetValue(prefix, out var moduleInfo))
        {
            foreach (var sym in moduleInfo.Symbols.GetTopLevel())
            {
                items.Add(new CompletionItem
                {
                    Label = sym.Name,
                    Kind = MapCompletionKind(sym.Kind),
                    Detail = sym.Detail
                });
            }
            return new CompletionList(items);
        }

        // Check if prefix is a struct or enum — look up its type via ScopeTree
        if (result != null)
        {
            var symbols = result.Symbols.GetVisibleSymbols(1, 1);
            var prefixDef = symbols.FirstOrDefault(s => s.Name == prefix);

            // If prefix is a variable/parameter with a type hint, resolve to that struct's fields
            var structName = prefix;
            if (prefixDef != null && prefixDef.TypeHint != null &&
                (prefixDef.Kind == Analysis.SymbolKind.Variable ||
                 prefixDef.Kind == Analysis.SymbolKind.Constant ||
                 prefixDef.Kind == Analysis.SymbolKind.Parameter ||
                 prefixDef.Kind == Analysis.SymbolKind.LoopVariable))
            {
                structName = prefixDef.TypeHint;
            }

            if (prefixDef == null || prefixDef.Kind != Analysis.SymbolKind.Struct)
            {
                // Check if the resolved structName matches a struct definition
                var structDef = symbols.FirstOrDefault(s => s.Name == structName && s.Kind == Analysis.SymbolKind.Struct);
                if (structDef != null)
                {
                    foreach (var sym in symbols.Where(s => s.ParentName == structName && s.Kind == Analysis.SymbolKind.Field))
                    {
                        items.Add(new CompletionItem
                        {
                            Label = sym.Name,
                            Kind = LspCompletionItemKind.Field,
                            Detail = sym.Detail
                        });
                    }
                }
            }

            if (prefixDef != null && prefixDef.Kind == Analysis.SymbolKind.Struct)
            {
                foreach (var sym in symbols.Where(s => s.ParentName == prefix && s.Kind == Analysis.SymbolKind.Field))
                {
                    items.Add(new CompletionItem
                    {
                        Label = sym.Name,
                        Kind = LspCompletionItemKind.Field,
                        Detail = sym.Detail
                    });
                }
            }

            var enumDef = symbols.FirstOrDefault(s => s.Name == prefix && s.Kind == Analysis.SymbolKind.Enum);
            if (enumDef != null)
            {
                foreach (var sym in symbols.Where(s => s.ParentName == prefix && s.Kind == Analysis.SymbolKind.EnumMember))
                {
                    items.Add(new CompletionItem
                    {
                        Label = sym.Name,
                        Kind = LspCompletionItemKind.EnumMember,
                        Detail = sym.Detail
                    });
                }
            }
        }

        return new CompletionList(items);
    }

    public override Task<CompletionItem> Handle(CompletionItem request, CancellationToken cancellationToken)
    {
        return Task.FromResult(request);
    }

    protected override CompletionRegistrationOptions CreateRegistrationOptions(
        CompletionCapability capability, ClientCapabilities clientCapabilities) =>
        new()
        {
            DocumentSelector = new TextDocumentSelector(TextDocumentFilter.ForLanguage("stash")),
            TriggerCharacters = new Container<string>(".", "("),
            ResolveProvider = false
        };

    private static LspCompletionItemKind MapCompletionKind(Analysis.SymbolKind kind) => kind switch
    {
        Analysis.SymbolKind.Function => LspCompletionItemKind.Function,
        Analysis.SymbolKind.Variable => LspCompletionItemKind.Variable,
        Analysis.SymbolKind.Constant => LspCompletionItemKind.Constant,
        Analysis.SymbolKind.Struct => LspCompletionItemKind.Struct,
        Analysis.SymbolKind.Enum => LspCompletionItemKind.Enum,
        Analysis.SymbolKind.EnumMember => LspCompletionItemKind.EnumMember,
        Analysis.SymbolKind.Field => LspCompletionItemKind.Field,
        Analysis.SymbolKind.Parameter => LspCompletionItemKind.Variable,
        Analysis.SymbolKind.LoopVariable => LspCompletionItemKind.Variable,
        Analysis.SymbolKind.Namespace => LspCompletionItemKind.Module,
        _ => LspCompletionItemKind.Text
    };
}
