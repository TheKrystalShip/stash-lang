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

    // Built-in function signatures
    private static readonly Dictionary<string, (string Label, string[] Params)> _builtInSignatures = new()
    {
        ["typeof"] = ("fn typeof(value) -> string", new[] { "value" }),
        ["len"] = ("fn len(value) -> int", new[] { "value" }),
        ["lastError"] = ("fn lastError() -> string", Array.Empty<string>()),
        ["parseArgs"] = ("fn parseArgs(tree: ArgTree) -> Args", new[] { "tree" }),
    };

    private static readonly Dictionary<string, (string Label, string[] Params)> _namespacedSignatures = new()
    {
        ["io.println"] = ("fn io.println(value)", new[] { "value" }),
        ["io.print"] = ("fn io.print(value)", new[] { "value" }),
        ["conv.toStr"] = ("fn conv.toStr(value) -> string", new[] { "value" }),
        ["conv.toInt"] = ("fn conv.toInt(value) -> int", new[] { "value" }),
        ["conv.toFloat"] = ("fn conv.toFloat(value) -> float", new[] { "value" }),
        ["env.get"] = ("fn env.get(name: string) -> string", new[] { "name" }),
        ["env.set"] = ("fn env.set(name: string, value: string)", new[] { "name", "value" }),
        ["process.exit"] = ("fn process.exit(code: int)", new[] { "code" }),
        ["fs.readFile"] = ("fn fs.readFile(path: string) -> string", new[] { "path" }),
        ["fs.writeFile"] = ("fn fs.writeFile(path: string, content: string)", new[] { "path", "content" }),
        ["fs.exists"] = ("fn fs.exists(path: string) -> bool", new[] { "path" }),
        ["fs.dirExists"] = ("fn fs.dirExists(path: string) -> bool", new[] { "path" }),
        ["fs.pathExists"] = ("fn fs.pathExists(path: string) -> bool", new[] { "path" }),
        ["fs.createDir"] = ("fn fs.createDir(path: string)", new[] { "path" }),
        ["fs.delete"] = ("fn fs.delete(path: string)", new[] { "path" }),
        ["fs.copy"] = ("fn fs.copy(src: string, dst: string)", new[] { "src", "dst" }),
        ["fs.move"] = ("fn fs.move(src: string, dst: string)", new[] { "src", "dst" }),
        ["fs.size"] = ("fn fs.size(path: string) -> int", new[] { "path" }),
        ["fs.listDir"] = ("fn fs.listDir(path: string) -> array", new[] { "path" }),
        ["fs.appendFile"] = ("fn fs.appendFile(path: string, content: string)", new[] { "path", "content" }),
        ["path.abs"] = ("fn path.abs(path: string) -> string", new[] { "path" }),
        ["path.dir"] = ("fn path.dir(path: string) -> string", new[] { "path" }),
        ["path.base"] = ("fn path.base(path: string) -> string", new[] { "path" }),
        ["path.ext"] = ("fn path.ext(path: string) -> string", new[] { "path" }),
        ["path.join"] = ("fn path.join(a: string, b: string) -> string", new[] { "a", "b" }),
        ["path.name"] = ("fn path.name(path: string) -> string", new[] { "path" }),
    };

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
        if (_builtInSignatures.TryGetValue(functionName, out var builtIn))
        {
            return Task.FromResult<SignatureHelp?>(BuildSignatureHelp(builtIn.Label, builtIn.Params, activeParam));
        }

        if (_namespacedSignatures.TryGetValue(functionName, out var nsBuiltIn))
        {
            return Task.FromResult<SignatureHelp?>(BuildSignatureHelp(nsBuiltIn.Label, nsBuiltIn.Params, activeParam));
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
                var paramNames = ExtractParamNames(definition.Detail);
                return Task.FromResult<SignatureHelp?>(BuildSignatureHelp(definition.Detail, paramNames, activeParam));
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

    private static SignatureHelp BuildSignatureHelp(string label, string[] paramNames, int activeParam)
    {
        var parameters = new List<ParameterInformation>();
        foreach (var p in paramNames)
        {
            parameters.Add(new ParameterInformation { Label = p });
        }

        var signatureInfo = new SignatureInformation
        {
            Label = label,
            Parameters = new Container<ParameterInformation>(parameters)
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
            parts[i] = part;
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
