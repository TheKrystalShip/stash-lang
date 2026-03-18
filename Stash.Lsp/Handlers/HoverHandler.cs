namespace Stash.Lsp.Handlers;

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using Stash.Lsp.Analysis;

public class HoverHandler : HoverHandlerBase
{
    private readonly AnalysisEngine _analysis;
    private readonly DocumentManager _documents;

    public HoverHandler(AnalysisEngine analysis, DocumentManager documents)
    {
        _analysis = analysis;
        _documents = documents;
    }

    public override Task<Hover?> Handle(HoverParams request, CancellationToken cancellationToken)
    {
        var uri = request.TextDocument.Uri.ToUri();
        var text = _documents.GetText(uri);
        var ctx = _analysis.GetContextAt(uri, text, (int)request.Position.Line, (int)request.Position.Character);
        if (ctx == null)
        {
            return Task.FromResult<Hover?>(null);
        }

        var (result, word) = ctx.Value;
        var line = request.Position.Line + 1;
        var col = request.Position.Character + 1;

        var symbol = result.Symbols.FindDefinition(word, line, col);

        // If not found directly, try namespace member access
        if (symbol == null)
        {
            var nsMember = result.ResolveNamespaceMember(text!, (int)request.Position.Line, (int)request.Position.Character, word);
            if (nsMember != null)
            {
                var (nsSym, _) = nsMember.Value;
                var lines2 = text!.Split('\n');
                var dotPrefix = TextUtilities.FindDotPrefix(lines2[(int)request.Position.Line], (int)request.Position.Character);
                var markdown = $"```stash\n{nsSym.Detail ?? nsSym.Name}\n```\n*{nsSym.Kind}* — from `{dotPrefix}`";
                return Task.FromResult<Hover?>(new Hover
                {
                    Contents = new MarkedStringsOrMarkupContent(new MarkupContent
                    {
                        Kind = MarkupKind.Markdown,
                        Value = markdown
                    })
                });
            }
            // Try built-in namespace constants (e.g., process.SIGTERM) and functions (e.g., arr.map)
            {
                var lines2 = text!.Split('\n');
                var dotPrefix = TextUtilities.FindDotPrefix(lines2[(int)request.Position.Line], (int)request.Position.Character);
                if (dotPrefix != null)
                {
                    var qualifiedName = $"{dotPrefix}.{word}";
                    if (BuiltInRegistry.TryGetNamespaceConstant(qualifiedName, out var constant))
                    {
                        var markdown = $"```stash\n{constant.Detail}\n```\n*constant* — from `{dotPrefix}`";
                        if (constant.Documentation != null)
                        {
                            markdown += "\n\n---\n\n" + constant.Documentation;
                        }
                        return Task.FromResult<Hover?>(new Hover
                        {
                            Contents = new MarkedStringsOrMarkupContent(new MarkupContent
                            {
                                Kind = MarkupKind.Markdown,
                                Value = markdown
                            })
                        });
                    }

                    if (BuiltInRegistry.TryGetNamespaceFunction(qualifiedName, out var nsFunc))
                    {
                        var markdown = $"```stash\n{nsFunc.Detail}\n```\n*built-in function* — from `{dotPrefix}`";
                        if (nsFunc.Documentation != null)
                        {
                            markdown += "\n\n---\n\n" + FormatDocumentation(nsFunc.Documentation);
                        }
                        return Task.FromResult<Hover?>(new Hover
                        {
                            Contents = new MarkedStringsOrMarkupContent(new MarkupContent
                            {
                                Kind = MarkupKind.Markdown,
                                Value = markdown
                            })
                        });
                    }
                }
            }

            // Try global built-in functions (e.g., typeof, len)
            if (BuiltInRegistry.TryGetFunction(word, out var builtInFn))
            {
                var markdown = $"```stash\n{builtInFn.Detail}\n```\n*built-in function*";
                if (builtInFn.Documentation != null)
                {
                    markdown += "\n\n---\n\n" + FormatDocumentation(builtInFn.Documentation);
                }
                return Task.FromResult<Hover?>(new Hover
                {
                    Contents = new MarkedStringsOrMarkupContent(new MarkupContent
                    {
                        Kind = MarkupKind.Markdown,
                        Value = markdown
                    })
                });
            }

            return Task.FromResult<Hover?>(null);
        }

        // Normal symbol hover
        var detail = symbol.Detail ?? symbol.Name;

        // Append inferred type if the symbol has a TypeHint not already shown in detail
        if (symbol.TypeHint != null &&
            symbol.Kind is Analysis.SymbolKind.Variable or Analysis.SymbolKind.Constant
                or Analysis.SymbolKind.Parameter or Analysis.SymbolKind.LoopVariable &&
            !detail.Contains($": {symbol.TypeHint}"))
        {
            detail += $": {symbol.TypeHint}";
        }

        var md = $"```stash\n{detail}\n```\n*{symbol.Kind}*";
        if (symbol.SourceUri != null)
        {
            var importedPath = System.IO.Path.GetFileName(symbol.SourceUri.LocalPath);
            md += $"\n\n*imported from {importedPath}*";
        }

        if (symbol.Documentation != null)
        {
            md += "\n\n---\n\n" + FormatDocumentation(symbol.Documentation);
        }

        return Task.FromResult<Hover?>(new Hover
        {
            Contents = new MarkedStringsOrMarkupContent(new MarkupContent
            {
                Kind = MarkupKind.Markdown,
                Value = md
            })
        });
    }

    protected override HoverRegistrationOptions CreateRegistrationOptions(
        HoverCapability capability, ClientCapabilities clientCapabilities) =>
        new()
        {
            DocumentSelector = new TextDocumentSelector(TextDocumentFilter.ForLanguage("stash"))
        };

    /// <summary>
    /// Formats documentation text with @param and @return tags rendered as markdown.
    /// </summary>
    private static string FormatDocumentation(string documentation)
    {
        var lines = documentation.Split('\n');
        var description = new List<string>();
        var paramDocs = new List<(string Name, string Desc)>();
        string? returnDoc = null;

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("@param "))
            {
                var rest = trimmed.Substring(7).Trim();
                var spaceIdx = rest.IndexOf(' ');
                if (spaceIdx > 0)
                {
                    paramDocs.Add((rest.Substring(0, spaceIdx), rest.Substring(spaceIdx + 1).Trim()));
                }
                else
                {
                    paramDocs.Add((rest, ""));
                }
            }
            else if (trimmed.StartsWith("@return ") || trimmed.StartsWith("@returns "))
            {
                var marker = trimmed.StartsWith("@returns ") ? "@returns " : "@return ";
                returnDoc = trimmed.Substring(marker.Length).Trim();
            }
            else
            {
                description.Add(line);
            }
        }

        var result = string.Join("\n", description).Trim();

        if (paramDocs.Count > 0)
        {
            result += "\n\n**Parameters:**\n";
            foreach ((string name, string desc) in paramDocs)
            {
                result += $"\n- `{name}` — {desc}";
            }
        }

        if (returnDoc != null)
        {
            result += $"\n\n**Returns:** {returnDoc}";
        }

        return result;
    }

}

