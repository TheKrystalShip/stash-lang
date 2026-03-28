namespace Stash.Lsp.Handlers;

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using Stash.Analysis;
using Stash.Stdlib;
using Stash.Lsp.Analysis;
using StashSymbolKind = Stash.Analysis.SymbolKind;

/// <summary>
/// Handles LSP <c>textDocument/hover</c> requests to display contextual information
/// about the symbol under the cursor.
/// </summary>
/// <remarks>
/// <para>
/// Uses <see cref="AnalysisEngine.GetContextAt"/> to identify the word at the cursor position
/// and then resolves it against the <see cref="ScopeTree"/> via
/// <see cref="ScopeTree.FindDefinition"/>. The result is rendered as a Markdown code block
/// showing the symbol's detail string and kind.
/// </para>
/// <para>
/// Dot-access context is detected via <see cref="TextUtilities.FindDotPrefix"/>: when a word
/// follows a dot, global namespace matches are suppressed to avoid false positives (e.g.,
/// hovering <c>time</c> in <c>e.time</c> must not resolve to the <c>time</c> namespace).
/// </para>
/// <para>
/// Falls back to resolving namespace members (via <see cref="AnalysisResult.ResolveNamespaceMember"/>),
/// built-in namespace constants and functions (via <see cref="StdlibRegistry"/>), and
/// global built-in functions. Documentation strings are formatted with <c>@param</c> and
/// <c>@return</c> tags converted to Markdown.
/// </para>
/// </remarks>
public class HoverHandler : HoverHandlerBase
{
    /// <summary>The analysis engine used to obtain context and resolve symbols.</summary>
    private readonly AnalysisEngine _analysis;

    /// <summary>The document manager used to retrieve the current text of open files.</summary>
    private readonly DocumentManager _documents;

    private readonly ILogger<HoverHandler> _logger;

    /// <summary>
    /// Initialises a new instance of <see cref="HoverHandler"/> with the services needed
    /// to resolve hover information.
    /// </summary>
    /// <param name="analysis">Analysis engine providing <see cref="AnalysisResult"/> data and context resolution.</param>
    /// <param name="documents">Document manager for reading open file contents.</param>
    public HoverHandler(AnalysisEngine analysis, DocumentManager documents, ILogger<HoverHandler> logger)
    {
        _analysis = analysis;
        _documents = documents;
        _logger = logger;
    }

    /// <summary>
    /// Processes the hover request and returns Markdown hover content for the symbol
    /// under the cursor, or <see langword="null"/> when no resolvable symbol is found.
    /// </summary>
    /// <param name="request">The hover request containing the document URI and cursor position.</param>
    /// <param name="cancellationToken">Token to cancel the request.</param>
    /// <returns>
    /// A <see cref="Hover"/> with a Markdown <see cref="MarkupContent"/> block, or
    /// <see langword="null"/> if nothing can be resolved at the cursor.
    /// </returns>
    public override Task<Hover?> Handle(HoverParams request, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Hover request at {Uri}:{Line}:{Col}", request.TextDocument.Uri, request.Position.Line, request.Position.Character);
        var uri = request.TextDocument.Uri.ToUri();
        var text = _documents.GetText(uri);
        var ctx = _analysis.GetContextAt(uri, text, (int)request.Position.Line, (int)request.Position.Character);
        if (ctx == null)
        {
            _logger.LogTrace("Hover: no info at {Uri}:{Line}:{Col}", request.TextDocument.Uri, request.Position.Line, request.Position.Character);
            return Task.FromResult<Hover?>(null);
        }

        var (result, word) = ctx.Value;
        var line = request.Position.Line + 1;
        var col = request.Position.Character + 1;

        // Dict literal keys are not symbols — suppress hover resolution
        if (result.IsDictKey((int)line, (int)col))
        {
            return Task.FromResult<Hover?>(null);
        }

        // ── Dot-access context check ──────────────────────────────────────────
        // Detect whether this word follows a dot (e.g., "time" in "e.time").
        // If so, do NOT resolve it as a standalone global symbol.
        var textLines = text!.Split('\n');
        var dotPrefix = TextUtilities.FindDotPrefix(textLines[(int)request.Position.Line], (int)request.Position.Character);
        bool afterDot = dotPrefix != null;

        var symbol = result.Symbols.FindDefinition(word, line, col);

        // After dot: a namespace symbol match is a false positive — e.g., hovering
        // "time" in "e.time" must not resolve to the "time" namespace.
        if (afterDot && symbol != null && symbol.Kind is StashSymbolKind.Namespace)
        {
            symbol = null;
        }

        // If not found directly, try namespace member access
        if (symbol == null)
        {
            var nsMember = result.ResolveNamespaceMember(text!, (int)request.Position.Line, (int)request.Position.Character, word);
            if (nsMember != null)
            {
                var (nsSym, _) = nsMember.Value;
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
                if (dotPrefix != null)
                {
                    var qualifiedName = $"{dotPrefix}.{word}";
                    if (StdlibRegistry.TryGetNamespaceConstant(qualifiedName, out var constant))
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

                    if (StdlibRegistry.TryGetNamespaceFunction(qualifiedName, out var nsFunc))
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

            // Try global built-in functions (e.g., typeof, len) — only for standalone identifiers
            if (!afterDot && StdlibRegistry.TryGetFunction(word, out var builtInFn))
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

            // Built-in type names (used in `is` expressions and type annotations)
            if (!afterDot && StdlibRegistry.TypeDescriptions.TryGetValue(word, out var typeDesc))
            {
                var markdown = $"```stash\n{typeDesc.Signature}\n```\n*built-in type*\n\n---\n\n{typeDesc.Description}";
                return Task.FromResult<Hover?>(new Hover
                {
                    Contents = new MarkedStringsOrMarkupContent(new MarkupContent
                    {
                        Kind = MarkupKind.Markdown,
                        Value = markdown
                    })
                });
            }

            _logger.LogTrace("Hover: no info at {Uri}:{Line}:{Col}", request.TextDocument.Uri, request.Position.Line, request.Position.Character);
            return Task.FromResult<Hover?>(null);
        }

        // Normal symbol hover
        var detail = symbol.Detail ?? symbol.Name;

        // Append effective type (narrowed or inferred) if not already shown in detail
        string? effectiveType = result.Symbols.GetNarrowedTypeHint(word, (int)line, (int)col) ?? symbol.TypeHint;
        if (effectiveType != null &&
            symbol.Kind is StashSymbolKind.Variable or StashSymbolKind.Constant
                or StashSymbolKind.Parameter or StashSymbolKind.LoopVariable &&
            !detail.Contains($": {effectiveType}"))
        {
            detail += $": {effectiveType}";
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

        _logger.LogDebug("Hover: resolved for {Uri}", request.TextDocument.Uri);
        return Task.FromResult<Hover?>(new Hover
        {
            Contents = new MarkedStringsOrMarkupContent(new MarkupContent
            {
                Kind = MarkupKind.Markdown,
                Value = md
            })
        });
    }

    /// <summary>
    /// Creates the registration options specifying that this handler applies to <c>stash</c> language files.
    /// </summary>
    protected override HoverRegistrationOptions CreateRegistrationOptions(
        HoverCapability capability, ClientCapabilities clientCapabilities) =>
        new()
        {
            DocumentSelector = new TextDocumentSelector(TextDocumentFilter.ForLanguage("stash"))
        };

    /// <summary>
    /// Formats a documentation string by converting <c>@param</c> and <c>@return</c>/<c>@returns</c>
    /// tags into Markdown bold sections, and returns the full formatted string.
    /// </summary>
    /// <param name="documentation">The raw documentation string containing optional JSDoc-style tags.</param>
    /// <returns>A Markdown-formatted documentation string with parameter and return sections.</returns>
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

