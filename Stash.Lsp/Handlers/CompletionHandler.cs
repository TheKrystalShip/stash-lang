namespace Stash.Lsp.Handlers;

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

    private static readonly string[] _keywords =
    {
        "let", "const", "fn", "struct", "enum", "if", "else",
        "for", "in", "while", "return", "break", "continue",
        "true", "false", "null", "try", "import", "from", "as", "args"
    };

    private static readonly (string Name, string Detail)[] _builtIns =
    {
        ("typeof", "typeof(value) → string"),
        ("len", "len(value) → int"),
        ("lastError", "lastError() → string | null"),
        ("parseArgs", "parseArgs(argTree) → args"),
    };

    private static readonly (string Namespace, string Name, string Detail)[] _namespacedBuiltIns =
    {
        ("io", "println", "io.println(value)"),
        ("io", "print", "io.print(value)"),
        ("conv", "toStr", "conv.toStr(value) → string"),
        ("conv", "toInt", "conv.toInt(value) → int"),
        ("conv", "toFloat", "conv.toFloat(value) → float"),
        ("env", "get", "env.get(name) → string | null"),
        ("env", "set", "env.set(name, value)"),
        ("process", "exit", "process.exit(code)"),
        ("fs", "readFile", "fs.readFile(path) → string"),
        ("fs", "writeFile", "fs.writeFile(path, content)"),
        ("fs", "exists", "fs.exists(path) → bool"),
        ("fs", "dirExists", "fs.dirExists(path) → bool"),
        ("fs", "pathExists", "fs.pathExists(path) → bool"),
        ("fs", "createDir", "fs.createDir(path)"),
        ("fs", "delete", "fs.delete(path)"),
        ("fs", "copy", "fs.copy(src, dst)"),
        ("fs", "move", "fs.move(src, dst)"),
        ("fs", "size", "fs.size(path) → int"),
        ("fs", "listDir", "fs.listDir(path) → array"),
        ("fs", "appendFile", "fs.appendFile(path, content)"),
        ("path", "abs", "path.abs(path) → string"),
        ("path", "dir", "path.dir(path) → string"),
        ("path", "base", "path.base(path) → string"),
        ("path", "ext", "path.ext(path) → string"),
        ("path", "join", "path.join(a, b) → string"),
        ("path", "name", "path.name(path) → string"),
    };

    public CompletionHandler(AnalysisEngine analysis)
    {
        _analysis = analysis;
    }

    public override Task<CompletionList> Handle(CompletionParams request, CancellationToken cancellationToken)
    {
        var items = new List<CompletionItem>();

        // Keywords
        foreach (var kw in _keywords)
        {
            items.Add(new CompletionItem
            {
                Label = kw,
                Kind = LspCompletionItemKind.Keyword,
                Detail = "keyword"
            });
        }

        // Built-in functions
        foreach (var (name, detail) in _builtIns)
        {
            items.Add(new CompletionItem
            {
                Label = name,
                Kind = LspCompletionItemKind.Function,
                Detail = detail
            });
        }

        // Built-in namespaces
        var seenNamespaces = new HashSet<string>();
        foreach (var (ns, name, detail) in _namespacedBuiltIns)
        {
            if (seenNamespaces.Add(ns))
            {
                items.Add(new CompletionItem
                {
                    Label = ns,
                    Kind = LspCompletionItemKind.Module,
                    Detail = $"namespace {ns}"
                });
            }
        }

        // Symbols from analysis
        var result = _analysis.GetCachedResult(request.TextDocument.Uri.ToUri());
        if (result != null)
        {
            var seen = new HashSet<string>();
            foreach (var sym in result.Symbols.All)
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

        return Task.FromResult(new CompletionList(items));
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
