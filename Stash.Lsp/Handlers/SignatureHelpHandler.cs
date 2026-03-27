namespace Stash.Lsp.Handlers;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using Stash.Analysis;
using Stash.Lsp.Analysis;
using StashSymbolKind = Stash.Analysis.SymbolKind;

/// <summary>
/// Handles LSP <c>textDocument/signatureHelp</c> requests to display parameter hints
/// when the cursor is inside a function call.
/// </summary>
/// <remarks>
/// <para>
/// Scans backwards from the cursor offset to locate the nearest enclosing open parenthesis
/// via <see cref="FindCallContext"/>, extracts the function name, and counts commas to
/// determine the active parameter index. Built-in functions and namespace functions are
/// looked up from <see cref="BuiltInRegistry"/>; user-defined functions are resolved via
/// <see cref="ScopeTree.FindDefinition"/> from the cached <see cref="AnalysisResult"/>.
/// </para>
/// <para>
/// Parameter labels are extracted from the function's detail string (e.g., <c>fn foo(a: int, b: str)</c>)
/// by <see cref="ExtractParamLabels"/> and attached to the <see cref="SignatureInformation"/>
/// so editors can highlight the active parameter.
/// </para>
/// </remarks>
public class SignatureHelpHandler : SignatureHelpHandlerBase
{
    /// <summary>The analysis engine used to look up user-defined function signatures.</summary>
    private readonly AnalysisEngine _analysis;

    /// <summary>The document manager used to retrieve the current text of open files.</summary>
    private readonly DocumentManager _documents;

    private readonly ILogger<SignatureHelpHandler> _logger;

    /// <summary>
    /// Initialises a new instance of <see cref="SignatureHelpHandler"/> with the services
    /// needed to resolve function signatures.
    /// </summary>
    /// <param name="analysis">Analysis engine providing cached <see cref="AnalysisResult"/> data.</param>
    /// <param name="documents">Document manager for reading open file contents.</param>
    public SignatureHelpHandler(AnalysisEngine analysis, DocumentManager documents, ILogger<SignatureHelpHandler> logger)
    {
        _analysis = analysis;
        _documents = documents;
        _logger = logger;
    }

    /// <summary>
    /// Processes the signature-help request and returns the signature of the enclosing
    /// function call along with the active parameter index.
    /// </summary>
    /// <param name="request">The signature-help request containing the document URI and cursor position.</param>
    /// <param name="cancellationToken">Token to cancel the request.</param>
    /// <returns>
    /// A <see cref="SignatureHelp"/> with the matching signature and active parameter, or
    /// <see langword="null"/> if no enclosing function call can be found.
    /// </returns>
    public override Task<SignatureHelp?> Handle(SignatureHelpParams request, CancellationToken cancellationToken)
    {
        _logger.LogDebug("SignatureHelp request at {Uri}:{Line}:{Col}", request.TextDocument.Uri, request.Position.Line, request.Position.Character);
        var text = _documents.GetText(request.TextDocument.Uri.ToUri());
        if (text == null)
        {
            return Task.FromResult<SignatureHelp?>(null);
        }

        var lines = text.Split('\n');
        var cursorLine = request.Position.Line;
        var cursorChar = request.Position.Character;

        // Calculate absolute offset of cursor
        int offset = 0;
        for (int i = 0; i < cursorLine && i < lines.Length; i++)
        {
            offset += lines[i].Length + 1; // +1 for \n
        }
        offset += Math.Min(cursorChar, cursorLine < lines.Length ? lines[cursorLine].Length : 0);

        // Scan backwards to find enclosing function call
        var (functionName, activeParam) = FindCallContext(text, offset);
        if (functionName == null)
        {
            return Task.FromResult<SignatureHelp?>(null);
        }

        // Try built-in signatures first
        if (BuiltInRegistry.TryGetFunction(functionName, out var builtInFn))
        {
            _logger.LogDebug("SignatureHelp: resolved {FuncName} for {Uri}", functionName, request.TextDocument.Uri);
            return Task.FromResult<SignatureHelp?>(BuildSignatureHelp(builtInFn.Detail, ExtractParamLabels(builtInFn.Detail), activeParam, builtInFn.Documentation));
        }

        if (BuiltInRegistry.TryGetNamespaceFunction(functionName, out var nsFn))
        {
            _logger.LogDebug("SignatureHelp: resolved {FuncName} for {Uri}", functionName, request.TextDocument.Uri);
            return Task.FromResult<SignatureHelp?>(BuildSignatureHelp(nsFn.Detail, ExtractParamLabels(nsFn.Detail), activeParam, nsFn.Documentation));
        }

        // Try user-defined functions
        var result = _analysis.GetCachedResult(request.TextDocument.Uri.ToUri());
        if (result != null)
        {
            // Extract just the function name (without namespace prefix)
            var simpleName = functionName.Contains('.') ? functionName[(functionName.LastIndexOf('.') + 1)..] : functionName;
            var line = request.Position.Line + 1;
            var col = request.Position.Character + 1;
            var definition = result.Symbols.FindDefinition(simpleName, line, col);

            if (definition != null && definition.Kind == StashSymbolKind.Function && definition.Detail != null)
            {
                _logger.LogDebug("SignatureHelp: resolved {FuncName} for {Uri}", functionName, request.TextDocument.Uri);
                var paramLabels = ExtractParamLabels(definition.Detail);
                return Task.FromResult<SignatureHelp?>(BuildSignatureHelp(definition.Detail, paramLabels, activeParam));
            }
        }

        _logger.LogTrace("SignatureHelp: no signature context at {Uri}", request.TextDocument.Uri);
        return Task.FromResult<SignatureHelp?>(null);
    }

    /// <summary>
    /// Scans backwards through <paramref name="text"/> from <paramref name="offset"/> to find
    /// the nearest unmatched open parenthesis that represents a function call, and counts
    /// commas at the outermost call depth to determine the active parameter index.
    /// </summary>
    /// <param name="text">The full document source text.</param>
    /// <param name="offset">The absolute character offset of the cursor.</param>
    /// <returns>
    /// A tuple of the function name (including namespace prefix if present) and the
    /// zero-based active parameter index. Returns <c>(null, 0)</c> when no call context is found.
    /// </returns>
    private static (string? FunctionName, int ActiveParam) FindCallContext(string text, int offset)
    {
        int depth = 0;
        int commaCount = 0;
        int parenPos = -1;

        // Scan backwards from cursor
        for (int i = offset - 1; i >= 0; i--)
        {
            char c = text[i];
            if (c == ')')
            {
                depth++;
            }
            else if (c == '(')
            {
                if (depth > 0)
                {
                    depth--;
                }
                else
                {
                    parenPos = i;
                    break;
                }
            }
            else if (c == ',' && depth == 0)
            {
                commaCount++;
            }
        }

        if (parenPos < 0)
        {
            return (null, 0);
        }

        // Extract function name before the paren
        int end = parenPos - 1;
        while (end >= 0 && text[end] == ' ')
        {
            end--;
        }

        if (end < 0)
        {
            return (null, 0);
        }

        int start = end;
        // Walk back through identifier chars and dots (for namespace.function)
        while (start > 0 && (char.IsLetterOrDigit(text[start - 1]) || text[start - 1] == '_' || text[start - 1] == '.'))
        {
            start--;
        }

        var name = text[start..(end + 1)];
        if (string.IsNullOrWhiteSpace(name))
        {
            return (null, 0);
        }

        return (name, commaCount);
    }

    /// <summary>
    /// Constructs a <see cref="SignatureHelp"/> object from the given function detail string,
    /// parameter labels, and active parameter index.
    /// </summary>
    /// <param name="label">The full signature label string (e.g., <c>fn foo(a: int, b: str)</c>).</param>
    /// <param name="paramNames">Array of individual parameter label strings to highlight.</param>
    /// <param name="activeParam">Zero-based index of the parameter at the cursor.</param>
    /// <param name="documentation">Optional Markdown documentation string for the function.</param>
    /// <returns>A populated <see cref="SignatureHelp"/> ready to send to the client.</returns>
    private static SignatureHelp BuildSignatureHelp(string label, string[] paramNames, int activeParam, string? documentation = null)
    {
        var parameters = new List<ParameterInformation>();
        foreach (var p in paramNames)
        {
            parameters.Add(new ParameterInformation { Label = p });
        }

        var signatureInfo = new SignatureInformation
        {
            Label = label,
            Parameters = new Container<ParameterInformation>(parameters),
            Documentation = documentation != null
                ? new MarkupContent { Kind = MarkupKind.Markdown, Value = documentation }
                : null
        };

        return new SignatureHelp
        {
            Signatures = new Container<SignatureInformation>(signatureInfo),
            ActiveSignature = 0,
            ActiveParameter = Math.Min(activeParam, Math.Max(0, paramNames.Length - 1))
        };
    }

    /// <summary>
    /// Extracts bare parameter names from a function detail string by stripping type
    /// annotations (<c>name: type</c>) and default values (<c>name = default</c>).
    /// </summary>
    /// <param name="detail">The function detail string, e.g. <c>fn foo(a: int, b: str = "x")</c>.</param>
    /// <returns>An array of bare parameter names, or an empty array if none are found.</returns>
    private static string[] ExtractParamNames(string detail)
    {
        // detail format: "fn name(a, b)" or "fn name(a: int, b: int) -> int"
        var openParen = detail.IndexOf('(');
        var closeParen = detail.IndexOf(')');
        if (openParen < 0 || closeParen < 0 || closeParen <= openParen + 1)
        {
            return Array.Empty<string>();
        }

        var inside = detail[(openParen + 1)..closeParen].Trim();
        if (string.IsNullOrEmpty(inside))
        {
            return Array.Empty<string>();
        }

        var parts = inside.Split(',');
        for (int i = 0; i < parts.Length; i++)
        {
            var part = parts[i].Trim();
            // Strip type annotation (e.g., "a: int" → "a")
            var colonIdx = part.IndexOf(':');
            if (colonIdx >= 0)
            {
                part = part[..colonIdx].Trim();
            }
            else
            {
                // Strip default value if no type annotation (e.g., "b = 5" → "b")
                var equalsIdx = part.IndexOf('=');
                if (equalsIdx >= 0)
                {
                    part = part[..equalsIdx].Trim();
                }
            }
            parts[i] = part;
        }

        return parts;
    }

    /// <summary>
    /// Extracts the full parameter label strings from a function detail string, preserving
    /// type annotations (e.g., <c>a: int</c>) for display in the signature-help UI.
    /// </summary>
    /// <param name="detail">The function detail string, e.g. <c>fn foo(a: int, b: str)</c>.</param>
    /// <returns>An array of trimmed parameter label strings, or an empty array if none are found.</returns>
    private static string[] ExtractParamLabels(string detail)
    {
        var openParen = detail.IndexOf('(');
        var closeParen = detail.IndexOf(')');
        if (openParen < 0 || closeParen < 0 || closeParen <= openParen + 1)
        {
            return Array.Empty<string>();
        }

        var inside = detail[(openParen + 1)..closeParen].Trim();
        if (string.IsNullOrEmpty(inside))
        {
            return Array.Empty<string>();
        }

        var parts = inside.Split(',');
        for (int i = 0; i < parts.Length; i++)
        {
            parts[i] = parts[i].Trim();
        }

        return parts;
    }

    /// <summary>
    /// Creates the registration options specifying that this handler applies to <c>stash</c>
    /// language files and triggers on <c>(</c> and <c>,</c> characters.
    /// </summary>
    protected override SignatureHelpRegistrationOptions CreateRegistrationOptions(
        SignatureHelpCapability capability, ClientCapabilities clientCapabilities) =>
        new()
        {
            DocumentSelector = new TextDocumentSelector(TextDocumentFilter.ForLanguage("stash")),
            TriggerCharacters = new Container<string>("(", ",")
        };
}
