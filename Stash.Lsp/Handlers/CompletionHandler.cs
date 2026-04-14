namespace Stash.Lsp.Handlers;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using Stash.Common;
using Stash.Analysis;
using Stash.Stdlib;
using Stash.Lsp.Analysis;
using StashSymbolKind = Stash.Analysis.SymbolKind;
using LspCompletionItemKind = OmniSharp.Extensions.LanguageServer.Protocol.Models.CompletionItemKind;

/// <summary>
/// Handles LSP <c>textDocument/completion</c> requests to provide autocompletion suggestions.
/// </summary>
/// <remarks>
/// <para>
/// Provides completions for keywords, built-in functions, built-in namespaces, and
/// user-defined symbols visible at the cursor position (via <see cref="ScopeTree.GetVisibleSymbols"/>).
/// </para>
/// <para>
/// When the cursor follows a <c>.</c>, only members of the preceding identifier are offered:
/// built-in namespace members, import-alias module exports, user-defined struct fields/methods,
/// built-in struct fields, or enum members. Uses <see cref="AnalysisEngine.GetCachedResult"/>
/// to obtain the current <see cref="AnalysisResult"/> and <see cref="ScopeTree"/> for the file.
/// </para>
/// <para>
/// When the cursor is inside a string that follows an <c>import</c> or <c>from</c> keyword,
/// package names from the <c>stashes/</c> directory are suggested.
/// </para>
/// </remarks>
public class CompletionHandler : CompletionHandlerBase
{
    /// <summary>The analysis engine used to obtain cached analysis results and symbol trees.</summary>
    private readonly AnalysisEngine _analysis;

    /// <summary>The document manager used to retrieve the current text of open files.</summary>
    private readonly DocumentManager _documents;

    private readonly ILogger<CompletionHandler> _logger;

    /// <summary>
    /// Initialises a new instance of <see cref="CompletionHandler"/> with the services
    /// needed to resolve completion items.
    /// </summary>
    /// <param name="analysis">Analysis engine providing cached <see cref="AnalysisResult"/> data.</param>
    /// <param name="documents">Document manager for reading open file contents.</param>
    public CompletionHandler(AnalysisEngine analysis, DocumentManager documents, ILogger<CompletionHandler> logger)
    {
        _analysis = analysis;
        _documents = documents;
        _logger = logger;
    }

    /// <summary>
    /// Processes the completion request and returns a list of completion items appropriate
    /// for the cursor context — import path strings, dot-access members, or the full symbol list.
    /// </summary>
    /// <param name="request">The completion request containing the document URI and cursor position.</param>
    /// <param name="cancellationToken">Token to cancel the request.</param>
    /// <returns>A <see cref="CompletionList"/> with matching completion items, or an empty list when inside a non-import string.</returns>
    public override Task<CompletionList> Handle(CompletionParams request, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Completion request at {Uri}:{Line}:{Col}", request.TextDocument.Uri, request.Position.Line, request.Position.Character);
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

        // Import string completions: offer package names when inside import path strings
        if (currentLine != null && IsInsideString(currentLine, col))
        {
            var importItems = GetImportCompletions(currentLine, col, uri);
            if (importItems != null)
            {
                return Task.FromResult(new CompletionList(importItems));
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
                var completionList = HandleDotCompletion(prefix, uri, line, col);
                _logger.LogDebug("Completion: {Count} items for {Uri}", completionList.Items?.Count() ?? 0, request.TextDocument.Uri);
                return Task.FromResult(completionList);
            }
        }

        // Type name completions after `extend` keyword
        if (currentLine != null && IsAfterExtendKeyword(currentLine, col))
        {
            var extendItems = BuildExtendTypeCompletionList(uri, line + 1, col + 1);
            _logger.LogDebug("Completion: {Count} extend type items for {Uri}", extendItems.Items?.Count() ?? 0, request.TextDocument.Uri);
            return Task.FromResult(extendItems);
        }

        // Type name completions after `is` keyword
        if (currentLine != null && IsAfterIsKeyword(currentLine, col))
        {
            var typeItems = BuildTypeCompletionList();
            _logger.LogDebug("Completion: {Count} type items for {Uri}", typeItems.Items?.Count() ?? 0, request.TextDocument.Uri);
            return Task.FromResult(typeItems);
        }

        // Default: full completion list
        var fullList = BuildFullCompletionList(uri, line + 1, col + 1);
        _logger.LogDebug("Completion: {Count} items for {Uri}", fullList.Items?.Count() ?? 0, request.TextDocument.Uri);
        return Task.FromResult(fullList);
    }

    /// <summary>
    /// Builds the default (non-dot) completion list: keywords, built-in functions,
    /// built-in namespaces, and all symbols visible at the given cursor position according
    /// to the <see cref="ScopeTree"/>.
    /// </summary>
    /// <param name="uri">The document URI for which to retrieve analysis results.</param>
    /// <param name="line">1-based line number of the cursor.</param>
    /// <param name="col">1-based column number of the cursor.</param>
    /// <returns>A <see cref="CompletionList"/> containing all applicable items.</returns>
    private CompletionList BuildFullCompletionList(Uri uri, int line, int col)
    {
        var items = new List<CompletionItem>();

        // Keywords
        foreach (var kw in StdlibRegistry.Keywords)
        {
            items.Add(new CompletionItem
            {
                Label = kw,
                Kind = LspCompletionItemKind.Keyword,
                Detail = "keyword"
            });
        }

        // Built-in functions
        foreach (var fn in StdlibRegistry.Functions)
        {
            items.Add(new CompletionItem
            {
                Label = fn.Name,
                Kind = LspCompletionItemKind.Function,
                Detail = fn.Detail,
                Documentation = fn.Documentation != null
                    ? new MarkupContent { Kind = MarkupKind.Markdown, Value = fn.Documentation }
                    : null
            });
        }

        // Built-in namespaces
        foreach (var ns in StdlibRegistry.NamespaceNames)
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

    /// <summary>
    /// Determines whether the cursor at <paramref name="col"/> on <paramref name="line"/>
    /// is inside an unescaped double-quoted string literal, accounting for interpolation
    /// expressions (<c>$"...{expr}..."</c>) where the cursor inside <c>{}</c> is treated
    /// as code, not string text.
    /// </summary>
    /// <param name="line">The source line text.</param>
    /// <param name="col">The 0-based cursor column.</param>
    /// <returns><see langword="true"/> if the cursor is inside string text; <see langword="false"/>
    /// if outside any string or inside an interpolation expression.</returns>
    private static bool IsInsideString(string line, int col)
    {
        bool inString = false;
        bool isInterpolated = false;
        int braceDepth = 0;

        for (int i = 0; i < col && i < line.Length; i++)
        {
            char c = line[i];

            if (!inString)
            {
                if (c == '"' && (i == 0 || line[i - 1] != '\\'))
                {
                    inString = true;
                    isInterpolated = i > 0 && line[i - 1] == '$';
                    braceDepth = 0;
                }
            }
            else
            {
                // Inside a string
                if (isInterpolated && braceDepth > 0)
                {
                    // Inside an interpolation expression — track nested braces
                    if (c == '{')
                    {
                        braceDepth++;
                    }
                    else if (c == '}')
                    {
                        braceDepth--;
                    }
                    // If braceDepth hits 0, we're back in string text
                }
                else if (isInterpolated && c == '{')
                {
                    braceDepth = 1;
                }
                else if (c == '"' && (i == 0 || line[i - 1] != '\\'))
                {
                    inString = false;
                    isInterpolated = false;
                }
            }
        }

        // If we're in a string but inside an interpolation expression, treat as code
        return inString && braceDepth == 0;
    }

    /// <summary>
    /// Detects whether the cursor is immediately after the <c>is</c> keyword, indicating
    /// that type name completions should be offered instead of the full symbol list.
    /// </summary>
    /// <param name="line">The source line text.</param>
    /// <param name="col">The 0-based cursor column.</param>
    /// <returns><see langword="true"/> if the text before the cursor ends with <c>is </c> or the cursor is typing a word preceded by <c>is </c>.</returns>
    private static bool IsAfterIsKeyword(string line, int col)
    {
        // Walk backwards from cursor to find the start of the current partial word
        int pos = col;
        while (pos > 0 && (char.IsLetterOrDigit(line[pos - 1]) || line[pos - 1] == '_'))
        {
            pos--;
        }

        // Skip whitespace before the partial word
        while (pos > 0 && char.IsWhiteSpace(line[pos - 1]))
        {
            pos--;
        }

        // Check if the two characters before are "is" preceded by a non-identifier char (or start of line)
        if (pos >= 2 && line[pos - 1] == 's' && line[pos - 2] == 'i')
        {
            // Ensure "is" is a whole word (not part of "this", "exists", etc.)
            return pos - 2 == 0 || !char.IsLetterOrDigit(line[pos - 3]) && line[pos - 3] != '_';
        }

        return false;
    }

    private static bool IsAfterExtendKeyword(string line, int col)
    {
        // Walk backwards from cursor to find the start of the current partial word
        int pos = col;
        while (pos > 0 && (char.IsLetterOrDigit(line[pos - 1]) || line[pos - 1] == '_'))
        {
            pos--;
        }

        // Skip whitespace before the partial word
        while (pos > 0 && char.IsWhiteSpace(line[pos - 1]))
        {
            pos--;
        }

        // Check if the six characters before are "extend" preceded by a non-identifier char (or start of line)
        if (pos >= 6 &&
            line[pos - 1] == 'd' &&
            line[pos - 2] == 'n' &&
            line[pos - 3] == 'e' &&
            line[pos - 4] == 't' &&
            line[pos - 5] == 'x' &&
            line[pos - 6] == 'e')
        {
            // Ensure "extend" is a whole word (not part of a longer identifier)
            return pos - 6 == 0 || !char.IsLetterOrDigit(line[pos - 7]) && line[pos - 7] != '_';
        }

        return false;
    }

    /// <summary>
    /// Builds a completion list containing all valid type names for <c>is</c> expressions.
    /// </summary>
    /// <returns>A <see cref="CompletionList"/> with type name completion items.</returns>
    private static CompletionList BuildTypeCompletionList()
    {
        var items = new List<CompletionItem>();
        foreach (var (name, desc) in StdlibRegistry.TypeDescriptions)
        {
            items.Add(new CompletionItem
            {
                Label = name,
                Kind = LspCompletionItemKind.TypeParameter,
                Detail = desc.Signature,
                Documentation = new MarkupContent { Kind = MarkupKind.Markdown, Value = desc.Description }
            });
        }
        return new CompletionList(items);
    }

    private CompletionList BuildExtendTypeCompletionList(Uri uri, int line, int col)
    {
        var items = new List<CompletionItem>();

        // Built-in extendable types
        string[] builtInTypes = ["string", "array", "dict", "int", "float", "byte"];
        foreach (var typeName in builtInTypes)
        {
            items.Add(new CompletionItem
            {
                Label = typeName,
                Kind = LspCompletionItemKind.TypeParameter,
                Detail = "built-in type"
            });
        }

        // User-defined struct names from analysis
        var result = _analysis.GetCachedResult(uri);
        if (result != null)
        {
            var seen = new HashSet<string>(builtInTypes);
            foreach (var sym in result.Symbols.All.Where(s => s.Kind == StashSymbolKind.Struct))
            {
                if (!seen.Add(sym.Name)) continue;
                items.Add(new CompletionItem
                {
                    Label = sym.Name,
                    Kind = LspCompletionItemKind.Struct,
                    Detail = sym.Detail
                });
            }
        }

        return new CompletionList(items);
    }

    /// <summary>
    /// If the character immediately before the cursor is a <c>.</c>, walks backwards to
    /// extract the identifier that precedes it (the dot-access prefix).
    /// </summary>
    /// <param name="line">The source line text.</param>
    /// <param name="col">The 0-based cursor column.</param>
    /// <returns>The identifier before the dot, or <see langword="null"/> if no dot context is found.</returns>
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

    /// <summary>
    /// Handles dot-access completion for a given <paramref name="prefix"/> by resolving it as a
    /// built-in namespace, an import alias, a struct instance, a struct type, a built-in struct,
    /// or an enum, and returning only the applicable members.
    /// </summary>
    /// <param name="prefix">The identifier before the dot.</param>
    /// <param name="uri">The document URI used to look up the cached analysis result.</param>
    /// <param name="lspLine">0-based LSP line of the cursor.</param>
    /// <param name="lspCol">0-based LSP column of the cursor.</param>
    /// <returns>A <see cref="CompletionList"/> containing the members of the resolved type or namespace.</returns>
    private CompletionList HandleDotCompletion(string prefix, Uri uri, int lspLine, int lspCol)
    {
        var items = new List<CompletionItem>();

        // Check if it's a known built-in namespace
        if (StdlibRegistry.IsBuiltInNamespace(prefix))
        {
            foreach (var fn in StdlibRegistry.GetNamespaceMembers(prefix))
            {
                items.Add(new CompletionItem
                {
                    Label = fn.Name,
                    Kind = LspCompletionItemKind.Function,
                    Detail = fn.Detail,
                    Documentation = fn.Documentation != null
                        ? new MarkupContent { Kind = MarkupKind.Markdown, Value = fn.Documentation }
                        : null
                });
            }
            foreach (var c in StdlibRegistry.GetNamespaceConstants(prefix))
            {
                items.Add(new CompletionItem
                {
                    Label = c.Name,
                    Kind = LspCompletionItemKind.Constant,
                    Detail = c.Detail
                });
            }
            foreach (var e in StdlibRegistry.Enums.Where(e => e.Namespace == prefix))
            {
                items.Add(new CompletionItem
                {
                    Label = e.Name,
                    Kind = LspCompletionItemKind.Enum,
                    Detail = e.Detail
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
            var symbols = result.Symbols.GetVisibleSymbols(lspLine + 1, lspCol + 1);
            var prefixDef = symbols.FirstOrDefault(s => s.Name == prefix);

            // If prefix is a variable/parameter, resolve to its struct type via narrowing or type hint
            var structName = prefix;
            if (prefixDef != null &&
                (prefixDef.Kind == StashSymbolKind.Variable ||
                 prefixDef.Kind == StashSymbolKind.Constant ||
                 prefixDef.Kind == StashSymbolKind.Parameter ||
                 prefixDef.Kind == StashSymbolKind.LoopVariable))
            {
                var narrowedType = result.Symbols.GetNarrowedTypeHint(prefix, lspLine + 1, lspCol + 1);
                structName = narrowedType ?? prefixDef.TypeHint ?? prefix;
            }

            if (prefixDef == null || prefixDef.Kind != StashSymbolKind.Struct)
            {
                // Check if the resolved structName matches a user-defined struct
                var allSymbols = result.Symbols.All;
                var structDef = allSymbols.FirstOrDefault(s => s.Name == structName && s.Kind == StashSymbolKind.Struct);
                if (structDef != null)
                {
                    foreach (var sym in allSymbols.Where(s => s.ParentName == structName && (s.Kind == StashSymbolKind.Field || s.Kind == StashSymbolKind.Method)))
                    {
                        items.Add(new CompletionItem
                        {
                            Label = sym.Name,
                            Kind = sym.Kind == StashSymbolKind.Method ? LspCompletionItemKind.Method : LspCompletionItemKind.Field,
                            Detail = sym.Detail
                        });
                    }
                }
                else
                {
                    // Fallback: check built-in structs from StdlibRegistry
                    var builtInStruct = StdlibRegistry.Structs.FirstOrDefault(s => s.Name == structName);
                    if (builtInStruct != null)
                    {
                        foreach (var field in builtInStruct.Fields)
                        {
                            var fieldDetail = field.Type != null ? $"field of {structName}: {field.Type}" : $"field of {structName}";
                            items.Add(new CompletionItem
                            {
                                Label = field.Name,
                                Kind = LspCompletionItemKind.Field,
                                Detail = fieldDetail
                            });
                        }
                    }
                }
            }

            // ── UFCS completions ──────────────────────────────────────────────
            // If the resolved type is UFCS-eligible ("string" or "array"),
            // offer namespace functions as method-style completions.
            // Skip if the prefix is a struct type — user-defined structs are not UFCS-eligible.
            var ufcsNamespace = prefixDef?.Kind != StashSymbolKind.Struct
                ? StdlibRegistry.GetUfcsNamespace(structName)
                : null;
            if (ufcsNamespace != null)
            {
                foreach (var fn in StdlibRegistry.GetNamespaceMembers(ufcsNamespace))
                {
                    // Build arity-adjusted signature: remove the first parameter (implicit receiver)
                    var adjustedParams = fn.Parameters.Skip(1).ToArray();
                    var paramParts = adjustedParams.Select(p => p.Type != null ? $"{p.Name}: {p.Type}" : p.Name);
                    string sig = $"{fn.Name}({string.Join(", ", paramParts)})";
                    if (fn.ReturnType != null)
                        sig += $" → {fn.ReturnType}";

                    items.Add(new CompletionItem
                    {
                        Label = fn.Name,
                        Kind = LspCompletionItemKind.Method,
                        Detail = $"{sig}  (UFCS: {ufcsNamespace}.{fn.Name})",
                        Documentation = fn.Documentation != null
                            ? new MarkupContent { Kind = MarkupKind.Markdown, Value = fn.Documentation }
                            : null
                    });
                }
            }

            if (prefixDef != null && prefixDef.Kind == StashSymbolKind.Struct)
            {
                var allSymbols = result.Symbols.All;
                foreach (var sym in allSymbols.Where(s => s.ParentName == prefix && (s.Kind == StashSymbolKind.Field || s.Kind == StashSymbolKind.Method)))
                {
                    items.Add(new CompletionItem
                    {
                        Label = sym.Name,
                        Kind = sym.Kind == StashSymbolKind.Method ? LspCompletionItemKind.Method : LspCompletionItemKind.Field,
                        Detail = sym.Detail
                    });
                }
            }

            var allForEnum = result.Symbols.All;
            var enumDef = allForEnum.FirstOrDefault(s => s.Name == prefix && s.Kind == StashSymbolKind.Enum);
            if (enumDef != null)
            {
                foreach (var sym in allForEnum.Where(s => s.ParentName == prefix && s.Kind == StashSymbolKind.EnumMember))
                {
                    items.Add(new CompletionItem
                    {
                        Label = sym.Name,
                        Kind = LspCompletionItemKind.EnumMember,
                        Detail = sym.Detail
                    });
                }
            }

            // Handle qualified namespace.enum prefix (e.g., task.Status.)
            if (items.Count == 0 && prefix.Contains('.'))
            {
                int dotIndex = prefix.LastIndexOf('.');
                string nsName = prefix.Substring(0, dotIndex);
                string enumName = prefix.Substring(dotIndex + 1);
                if (StdlibRegistry.IsBuiltInNamespace(nsName))
                {
                    var nsEnumDef = allForEnum.FirstOrDefault(s => s.Name == enumName && s.Kind == StashSymbolKind.Enum && s.ParentName == nsName);
                    if (nsEnumDef != null)
                    {
                        foreach (var sym in allForEnum.Where(s => s.ParentName == enumName && s.Kind == StashSymbolKind.EnumMember))
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
            }
        }

        // Check if prefix is an enum from a namespace import (e.g., utils.LogLevel.)
        if (items.Count == 0 && result != null)
        {
            foreach (var (_, modInfo) in result.NamespaceImports)
            {
                var importedEnum = modInfo.Symbols.All
                    .FirstOrDefault(s => s.Name == prefix && s.Kind == StashSymbolKind.Enum);
                if (importedEnum != null)
                {
                    foreach (var member in modInfo.Symbols.All
                        .Where(s => s.ParentName == prefix && s.Kind == StashSymbolKind.EnumMember))
                    {
                        items.Add(new CompletionItem
                        {
                            Label = member.Name,
                            Kind = LspCompletionItemKind.EnumMember,
                            Detail = member.Detail
                        });
                    }
                    break;
                }
            }
        }

        return new CompletionList(items);
    }

    /// <summary>
    /// Handles <c>completionItem/resolve</c> requests. No additional data is resolved;
    /// the item is returned unchanged.
    /// </summary>
    /// <param name="request">The completion item to resolve.</param>
    /// <param name="cancellationToken">Token to cancel the request.</param>
    /// <returns>The same <paramref name="request"/> item unmodified.</returns>
    public override Task<CompletionItem> Handle(CompletionItem request, CancellationToken cancellationToken)
    {
        return Task.FromResult(request);
    }

    /// <summary>
    /// Creates the registration options specifying that this handler applies to <c>stash</c>
    /// language files, triggers on <c>.</c> and <c>(</c>, and does not use a resolve provider.
    /// </summary>
    protected override CompletionRegistrationOptions CreateRegistrationOptions(
        CompletionCapability capability, ClientCapabilities clientCapabilities) =>
        new()
        {
            DocumentSelector = new TextDocumentSelector(TextDocumentFilter.ForLanguage("stash")),
            TriggerCharacters = new Container<string>(".", "("),
            ResolveProvider = false
        };

    /// <summary>
    /// Returns package-name completion items when the cursor is inside an import-path string
    /// (i.e., after <c>from</c> or <c>import</c>). Scans the <c>stashes/</c> directory under
    /// the project root for installable packages, including scoped (<c>@scope/name</c>) entries.
    /// </summary>
    /// <param name="line">The current source line text.</param>
    /// <param name="col">The 0-based cursor column.</param>
    /// <param name="uri">The document URI used to locate the project root.</param>
    /// <returns>
    /// A list of package-name completion items, or <see langword="null"/> if not in an import context
    /// or no packages are found.
    /// </returns>
    private List<CompletionItem>? GetImportCompletions(string line, int col, Uri uri)
    {
        // Find the opening quote before the cursor
        int quoteStart = -1;
        for (int i = col - 1; i >= 0; i--)
        {
            if (line[i] == '"' && (i == 0 || line[i - 1] != '\\'))
            {
                quoteStart = i;
                break;
            }
        }

        if (quoteStart < 0)
        {
            return null;
        }

        // Check if the text before the quote indicates an import context
        string before = line[..quoteStart].TrimEnd();
        bool endsWithFrom = before.EndsWith("from", StringComparison.Ordinal) &&
                            (before.Length == 4 || !char.IsLetterOrDigit(before[before.Length - 5]));
        bool isImportContext = endsWithFrom ||
                               (before.StartsWith("import", StringComparison.Ordinal) && !before.Contains('{'));

        if (!isImportContext)
        {
            return null;
        }

        var items = new List<CompletionItem>();

        // Find project root to list packages from stashes/
        string? documentDir = null;
        if (uri.IsFile)
        {
            documentDir = Path.GetDirectoryName(uri.LocalPath);
        }

        if (documentDir != null)
        {
            string? projectRoot = ModuleResolver.FindProjectRoot(documentDir);
            if (projectRoot != null)
            {
                string stashesDir = Path.Combine(projectRoot, "stashes");
                if (Directory.Exists(stashesDir))
                {
                    // List direct package directories
                    foreach (string dir in Directory.GetDirectories(stashesDir))
                    {
                        string name = Path.GetFileName(dir);
                        if (name.StartsWith('@'))
                        {
                            // Scoped packages: list @scope/name entries
                            foreach (string scopedDir in Directory.GetDirectories(dir))
                            {
                                string scopedName = name + "/" + Path.GetFileName(scopedDir);
                                items.Add(new CompletionItem
                                {
                                    Label = scopedName,
                                    Kind = LspCompletionItemKind.Module,
                                    Detail = "package"
                                });
                            }
                        }
                        else
                        {
                            items.Add(new CompletionItem
                            {
                                Label = name,
                                Kind = LspCompletionItemKind.Module,
                                Detail = "package"
                            });
                        }
                    }
                }
            }
        }

        return items.Count > 0 ? items : null;
    }

    /// <summary>
    /// Maps a Stash <see cref="SymbolKind"/> to the corresponding LSP
    /// <see cref="LspCompletionItemKind"/> for display in the editor's completion UI.
    /// </summary>
    /// <param name="kind">The Stash symbol kind to map.</param>
    /// <returns>The equivalent LSP completion item kind.</returns>
    private static LspCompletionItemKind MapCompletionKind(StashSymbolKind kind) => kind switch
    {
        StashSymbolKind.Function => LspCompletionItemKind.Function,
        StashSymbolKind.Variable => LspCompletionItemKind.Variable,
        StashSymbolKind.Constant => LspCompletionItemKind.Constant,
        StashSymbolKind.Struct => LspCompletionItemKind.Struct,
        StashSymbolKind.Enum => LspCompletionItemKind.Enum,
        StashSymbolKind.EnumMember => LspCompletionItemKind.EnumMember,
        StashSymbolKind.Field => LspCompletionItemKind.Field,
        StashSymbolKind.Parameter => LspCompletionItemKind.Variable,
        StashSymbolKind.LoopVariable => LspCompletionItemKind.Variable,
        StashSymbolKind.Namespace => LspCompletionItemKind.Module,
        _ => LspCompletionItemKind.Text
    };
}
