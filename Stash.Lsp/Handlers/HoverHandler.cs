namespace Stash.Lsp.Handlers;

using System.Collections.Generic;
using System.IO;
using System.Linq;
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
        // Skip local scope resolution entirely — the member must be resolved from the namespace/module.
        var textLines = text!.Split('\n');
        var dotPrefix = TextUtilities.FindDotPrefix(textLines[(int)request.Position.Line], (int)request.Position.Character);
        bool afterDot = dotPrefix != null;

        var symbol = afterDot ? null : result.Symbols.FindDefinition(word, line, col);

        // If not found directly, try namespace member access
        if (symbol == null)
        {
            var nsMember = result.ResolveNamespaceMember(text!, (int)request.Position.Line, (int)request.Position.Character, word);
            if (nsMember != null && nsMember.Value.Symbol.Kind != StashSymbolKind.Namespace)
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
                        var throwsSection = ThrowsRenderer.Render(nsFunc.Throws);
                        if (throwsSection != null) markdown += throwsSection;
                        return Task.FromResult<Hover?>(new Hover
                        {
                            Contents = new MarkedStringsOrMarkupContent(new MarkupContent
                            {
                                Kind = MarkupKind.Markdown,
                                Value = markdown
                            })
                        });
                    }

                    // ── UFCS hover fallback ───────────────────────────────────────
                    // If the prefix is a variable with a UFCS-eligible type (string/array),
                    // resolve the function from the corresponding namespace.
                    if (result != null)
                    {
                        var visibleSymbols = result.Symbols.GetVisibleSymbols((int)line, (int)col);
                        var prefixDef = visibleSymbols.FirstOrDefault(s => s.Name == dotPrefix);
                        if (prefixDef != null)
                        {
                            var prefixType = result.Symbols.GetNarrowedTypeHint(dotPrefix, (int)line, (int)col)
                                             ?? prefixDef.TypeHint;
                            var ufcsNs = prefixType != null ? StdlibRegistry.GetUfcsNamespace(prefixType) : null;
                            if (ufcsNs != null && StdlibRegistry.TryGetNamespaceFunction($"{ufcsNs}.{word}", out var ufcsFunc))
                            {
                                var markdown = $"```stash\n{ufcsFunc.Detail}\n```\n**(UFCS)** *built-in function* — `{dotPrefix}.{word}()` is equivalent to `{ufcsNs}.{word}({dotPrefix})`";
                                if (ufcsFunc.Documentation != null)
                                {
                                    markdown += "\n\n---\n\n" + FormatDocumentation(ufcsFunc.Documentation);
                                }
                                var ufcsThrows = ThrowsRenderer.Render(ufcsFunc.Throws);
                                if (ufcsThrows != null) markdown += ufcsThrows;
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
                var builtInThrows = ThrowsRenderer.Render(builtInFn.Throws);
                if (builtInThrows != null) markdown += builtInThrows;
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

        // ── CLI parse-result hover ────────────────────────────────────────────
        // When the hovered word is a variable bound to cli.parse(schema) output
        // (and the schema is a literal cli.schema({...})), render the field listing.
        if (!afterDot && symbol != null &&
            (symbol.Kind is StashSymbolKind.Variable or StashSymbolKind.Constant))
        {
            var cliInfo = result.CliSchema.TryGet(word);
            if (cliInfo != null && cliInfo.Fields.Count > 0)
            {
                var fieldLines = new System.Text.StringBuilder();
                foreach (var f in cliInfo.Fields)
                {
                    var tag = f.TypeTag ?? "any";
                    fieldLines.AppendLine($"  {f.Name}: {tag}");
                }
                var hoverMd =
                    $"```stash\n// cli.parse result — declared fields:\n{fieldLines.ToString().TrimEnd()}\n```\n*cli parse result*";
                return Task.FromResult<Hover?>(new Hover
                {
                    Contents = new MarkedStringsOrMarkupContent(new MarkupContent
                    {
                        Kind = MarkupKind.Markdown,
                        Value = hoverMd
                    })
                });
            }
        }

        // Normal symbol hover — follow re-export chain if applicable
        // When the symbol came from a module that re-exports it, look up the original declaration.
        var (displaySymbol, displaySourceUri) = ResolveReExportChain(_analysis, symbol, word);

        var detail = displaySymbol.Detail ?? displaySymbol.Name;

        // Append effective type (narrowed or inferred) if not already shown in detail
        string? effectiveType = result.Symbols.GetNarrowedTypeHint(word, (int)line, (int)col) ?? symbol.TypeHint;
        if (effectiveType != null &&
            symbol.Kind is StashSymbolKind.Variable or StashSymbolKind.Constant
                or StashSymbolKind.Parameter or StashSymbolKind.LoopVariable &&
            !detail.Contains($": {effectiveType}"))
        {
            detail += $": {effectiveType}";
        }

        var md = $"```stash\n{detail}\n```\n*{displaySymbol.Kind}*";
        var effectiveSourceUri = displaySourceUri ?? symbol.SourceUri;
        if (effectiveSourceUri != null)
        {
            var importedPath = Path.GetFileName(effectiveSourceUri.LocalPath);
            md += $"\n\n*imported from {importedPath}*";
        }

        if (displaySymbol.Documentation != null)
        {
            md += "\n\n---\n\n" + FormatDocumentation(displaySymbol.Documentation);
        }

        // Render @throws section for user-defined functions that carry structured throws metadata.
        if (displaySymbol.Throws != null && displaySymbol.Kind is StashSymbolKind.Function or StashSymbolKind.Method)
        {
            var adapted = AdaptThrows(displaySymbol.Throws);
            var userThrows = ThrowsRenderer.Render(adapted);
            if (userThrows != null) md += userThrows;
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

    /// <summary>
    /// Converts user-code <see cref="Stash.Parsing.AST.ThrowsEntry"/> records into the
    /// <see cref="Stash.Stdlib.Models.ThrowsEntry"/> shape expected by <see cref="ThrowsRenderer"/>.
    /// </summary>
    private static Stash.Stdlib.Models.ThrowsEntry[]? AdaptThrows(
        IReadOnlyList<Stash.Parsing.AST.ThrowsEntry>? throws)
    {
        if (throws == null || throws.Count == 0) return null;
        var result = new Stash.Stdlib.Models.ThrowsEntry[throws.Count];
        for (int i = 0; i < throws.Count; i++)
            result[i] = new Stash.Stdlib.Models.ThrowsEntry(throws[i].ErrorType, throws[i].Description);
        return result;
    }

    /// <summary>
    /// Follows the re-export chain starting from <paramref name="symbol"/> by consulting
    /// <see cref="ExportEntry.OriginPath"/> on intermediate modules.
    /// </summary>
    /// <remarks>
    /// <para>
    /// When a symbol was imported from a module that itself re-exports it via
    /// <c>export { name } from "path";</c>, <see cref="ExportEntry.OriginPath"/> is non-null.
    /// This method walks the chain transitively (up to a depth cap of 16) until it reaches
    /// the module that originally declares the name, or until the chain cannot be followed
    /// (missing module, unresolvable path, or cycle).
    /// </para>
    /// <para>
    /// Cycles are detected by tracking visited absolute paths. A depth cap of 16 provides
    /// an additional safety bound even when cycle detection is not triggered (e.g., very long
    /// valid chains).
    /// </para>
    /// <para>
    /// Modules that are not yet in the import resolver's cache are loaded on demand via
    /// <see cref="AnalysisEngine.EnsureModuleLoaded"/>. This handles transitively-imported modules
    /// that were not loaded during the primary document's analysis pipeline.
    /// </para>
    /// </remarks>
    /// <param name="analysis">The analysis engine used to load modules on demand.</param>
    /// <param name="symbol">The symbol resolved in the current document (may have <see cref="SymbolInfo.SourceUri"/> set).</param>
    /// <param name="name">The symbol name to look up in each intermediate module.</param>
    /// <returns>
    /// A tuple of the final <see cref="SymbolInfo"/> (the original declaration) and the
    /// <see cref="System.Uri"/> of the module that declares it. Both equal the inputs when
    /// no re-export chain is found.
    /// </returns>
    internal static (SymbolInfo Symbol, System.Uri? SourceUri) ResolveReExportChain(
        AnalysisEngine analysis, SymbolInfo symbol, string name)
    {
        const int MaxDepth = 16;

        if (symbol.SourceUri == null)
        {
            return (symbol, null);
        }

        var visited = new System.Collections.Generic.HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
        var currentModulePath = symbol.SourceUri.LocalPath;
        var currentSymbol = symbol;
        System.Uri? currentUri = symbol.SourceUri;

        for (int depth = 0; depth < MaxDepth; depth++)
        {
            if (!visited.Add(currentModulePath))
            {
                // Cycle detected — bail out with current best result
                break;
            }

            var moduleInfo = analysis.EnsureModuleLoaded(currentModulePath);
            if (moduleInfo == null)
            {
                break;
            }

            // Check if this module has an explicit re-export entry with OriginPath
            if (moduleInfo.ExportEntries == null || !moduleInfo.ExportEntries.TryGetValue(name, out var entry))
            {
                // Module doesn't re-export this name (it may declare it locally)
                var localSym = moduleInfo.Symbols.GetTopLevel().FirstOrDefault(s => s.Name == name);
                if (localSym != null)
                {
                    currentSymbol = localSym;
                    currentUri = moduleInfo.Uri;
                }
                break;
            }

            if (entry.OriginPath == null)
            {
                // Locally declared in this module — find the actual symbol
                var localSym = moduleInfo.Symbols.GetTopLevel().FirstOrDefault(s => s.Name == name);
                if (localSym != null)
                {
                    currentSymbol = localSym;
                    currentUri = moduleInfo.Uri;
                }
                break;
            }

            // Resolve OriginPath relative to the current module's directory
            var moduleDir = Path.GetDirectoryName(currentModulePath);
            if (moduleDir == null)
            {
                break;
            }

            var originAbsPath = ResolveOriginPath(entry.OriginPath, moduleDir);
            if (originAbsPath == null)
            {
                break;
            }

            // Load the origin module (may not be in cache yet for transitive imports)
            var originModuleInfo = analysis.EnsureModuleLoaded(originAbsPath);
            if (originModuleInfo == null)
            {
                break;
            }

            var originSym = originModuleInfo.Symbols.GetTopLevel().FirstOrDefault(s => s.Name == name);
            if (originSym != null)
            {
                currentSymbol = originSym;
                currentUri = originModuleInfo.Uri;
            }
            else if (entry.Kind == StashSymbolKind.Namespace)
            {
                // Namespace re-export: the origin module IS the target. Even if there is no
                // symbol named `name` declared inside it (because the alias is the module itself),
                // point the URI at the origin module so hover/go-to-def lands there.
                currentUri = originModuleInfo.Uri;
            }
            currentModulePath = originAbsPath;
        }

        return (currentSymbol, currentUri);
    }

    /// <summary>
    /// Resolves a raw <see cref="ExportEntry.OriginPath"/> string (relative or bare specifier)
    /// to an absolute file-system path. Returns <see langword="null"/> if the file does not exist.
    /// </summary>
    private static string? ResolveOriginPath(string originPath, string moduleDir)
    {
        var candidate = Path.GetFullPath(originPath, moduleDir);
        if (File.Exists(candidate))
        {
            return candidate;
        }

        // Bare-specifier fallback (e.g. package names) — not common for OriginPath but handled
        var packagePath = Stash.Common.ModuleResolver.ResolvePackageImport(originPath, moduleDir);
        return packagePath != null && File.Exists(packagePath) ? packagePath : null;
    }

}
