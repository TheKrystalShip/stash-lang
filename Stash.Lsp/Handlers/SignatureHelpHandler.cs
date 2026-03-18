namespace Stash.Lsp.Handlers;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using Stash.Lsp.Analysis;

public class SignatureHelpHandler : SignatureHelpHandlerBase
{
    private readonly AnalysisEngine _analysis;
    private readonly DocumentManager _documents;

    public SignatureHelpHandler(AnalysisEngine analysis, DocumentManager documents)
    {
        _analysis = analysis;
        _documents = documents;
    }

    public override Task<SignatureHelp?> Handle(SignatureHelpParams request, CancellationToken cancellationToken)
    {
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
            return Task.FromResult<SignatureHelp?>(BuildSignatureHelp(builtInFn.Detail, ExtractParamLabels(builtInFn.Detail), activeParam, builtInFn.Documentation));
        }

        if (BuiltInRegistry.TryGetNamespaceFunction(functionName, out var nsFn))
        {
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

            if (definition != null && definition.Kind == Analysis.SymbolKind.Function && definition.Detail != null)
            {
                var paramLabels = ExtractParamLabels(definition.Detail);
                return Task.FromResult<SignatureHelp?>(BuildSignatureHelp(definition.Detail, paramLabels, activeParam));
            }
        }

        return Task.FromResult<SignatureHelp?>(null);
    }

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

    protected override SignatureHelpRegistrationOptions CreateRegistrationOptions(
        SignatureHelpCapability capability, ClientCapabilities clientCapabilities) =>
        new()
        {
            DocumentSelector = new TextDocumentSelector(TextDocumentFilter.ForLanguage("stash")),
            TriggerCharacters = new Container<string>("(", ",")
        };
}
