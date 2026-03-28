using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.JSInterop;
using Stash.Analysis;
using Stash.Common;
using Stash.Stdlib;
using Stash.Stdlib.Models;

namespace Stash.Playground.Services;

public record DiagnosticDto(int StartLine, int StartColumn, int EndLine, int EndColumn, string Message, int Severity);

public record CompletionDto(string Label, int Kind, string? Detail, string? Documentation);

public record HoverDto(string Content);

public record ParameterDto(string Label, string? Documentation);

public record SignatureHelpDto(string Label, string? Documentation, ParameterDto[] Parameters, int ActiveParameter);

public static class PlaygroundAnalyzer
{
    private static readonly Uri _playgroundUri = new("file:///playground.stash");
    private static readonly AnalysisEngine _engine = new(NullLogger<AnalysisEngine>.Instance);

    // Monaco CompletionItemKind values
    private const int KindFunction = 1;
    private const int KindClass = 3;
    private const int KindVariable = 5;
    private const int KindProperty = 6;
    private const int KindModule = 8;
    private const int KindEnum = 12;
    private const int KindConstant = 14;
    private const int KindKeyword = 17;
    private const int KindEnumMember = 19;

    // Monaco MarkerSeverity values
    private const int SeverityInfo = 2;
    private const int SeverityWarning = 4;
    private const int SeverityError = 8;

    [JSInvokable]
    public static DiagnosticDto[] GetDiagnostics(string code)
    {
        var result = _engine.Analyze(_playgroundUri, code);
        var diagnostics = new List<DiagnosticDto>();

        foreach (var err in result.StructuredLexErrors)
        {
            diagnostics.Add(SpanToDiagnostic(err.Span, err.Message, SeverityError));
        }

        foreach (var err in result.StructuredParseErrors)
        {
            diagnostics.Add(SpanToDiagnostic(err.Span, err.Message, SeverityError));
        }

        foreach (var diag in result.SemanticDiagnostics)
        {
            int severity = diag.Level switch
            {
                DiagnosticLevel.Error => SeverityError,
                DiagnosticLevel.Warning => SeverityWarning,
                _ => SeverityInfo,
            };
            diagnostics.Add(SpanToDiagnostic(diag.Span, diag.Message, severity));
        }

        return diagnostics.ToArray();
    }

    [JSInvokable]
    public static CompletionDto[] GetCompletions(string code, int line, int column)
    {
        var result = _engine.Analyze(_playgroundUri, code);
        var lines = code.Split('\n');
        if (line < 0 || line >= lines.Length)
        {
            return [];
        }

        var lineText = lines[line];
        var dotPrefix = TextUtilities.FindDotPrefix(lineText, column);

        if (dotPrefix != null)
        {
            return GetMemberCompletions(result, dotPrefix, line, column);
        }

        if (IsAfterIsKeyword(lineText, column))
        {
            return StdlibRegistry.TypeDescriptions
                .Select(kvp => new CompletionDto(kvp.Key, KindClass, kvp.Value.Signature, kvp.Value.Description))
                .ToArray();
        }

        var completions = new List<CompletionDto>();

        foreach (var kw in StdlibRegistry.Keywords)
        {
            completions.Add(new CompletionDto(kw, KindKeyword, null, null));
        }

        foreach (var fn in StdlibRegistry.Functions)
        {
            completions.Add(new CompletionDto(fn.Name, KindFunction, fn.Detail, FormatDocumentation(fn.Documentation)));
        }

        foreach (var ns in StdlibRegistry.NamespaceNames)
        {
            completions.Add(new CompletionDto(ns, KindModule, null, null));
        }

        foreach (var sym in result.Symbols.GetVisibleSymbols(line + 1, column + 1))
        {
            int kind = sym.Kind switch
            {
                SymbolKind.Function or SymbolKind.Method => KindFunction,
                SymbolKind.Constant => KindConstant,
                SymbolKind.Struct => KindClass,
                SymbolKind.Enum => KindEnum,
                SymbolKind.EnumMember => KindEnumMember,
                SymbolKind.Namespace => KindModule,
                _ => KindVariable,
            };
            completions.Add(new CompletionDto(sym.Name, kind, sym.Detail, FormatDocumentation(sym.Documentation)));
        }

        return completions.ToArray();
    }

    [JSInvokable]
    public static HoverDto? GetHover(string code, int line, int column)
    {
        var result = _engine.Analyze(_playgroundUri, code);
        var context = _engine.GetContextAt(_playgroundUri, code, line, column);
        if (context == null)
        {
            return null;
        }

        var word = context.Value.Word;
        var lines = code.Split('\n');
        var lineText = line >= 0 && line < lines.Length ? lines[line] : "";
        var dotPrefix = TextUtilities.FindDotPrefix(lineText, column);

        if (dotPrefix != null && StdlibRegistry.TryGetNamespaceFunction($"{dotPrefix}.{word}", out var nsFn))
        {
            return BuildHover($"```\n{nsFn.Detail}\n```", nsFn.Documentation);
        }

        if (dotPrefix != null && StdlibRegistry.TryGetNamespaceConstant($"{dotPrefix}.{word}", out var nsConst))
        {
            return BuildHover($"```\n{nsConst.Detail}\n```", nsConst.Documentation);
        }

        var sym = result.Symbols.FindDefinition(word, line + 1, column + 1);
        if (sym != null)
        {
            return BuildHover($"```\n{sym.Detail ?? sym.Name}\n```", sym.Documentation);
        }

        if (dotPrefix == null && StdlibRegistry.TryGetFunction(word, out var builtIn))
        {
            return BuildHover($"```\n{builtIn.Detail}\n```", builtIn.Documentation);
        }

        if (dotPrefix == null && StdlibRegistry.TypeDescriptions.TryGetValue(word, out var typeDesc))
        {
            return BuildHover($"```\n{typeDesc.Signature}\n```", typeDesc.Description);
        }

        return null;
    }

    [JSInvokable]
    public static SignatureHelpDto? GetSignatureHelp(string code, int line, int column)
    {
        var callContext = FindCallContext(code, line, column);
        if (callContext == null)
        {
            return null;
        }

        var (funcName, activeParam, dotPrefix) = callContext.Value;

        BuiltInParam[]? parameters = null;
        string? detail = null;
        string? documentation = null;

        if (dotPrefix != null)
        {
            if (StdlibRegistry.TryGetNamespaceFunction($"{dotPrefix}.{funcName}", out var nsFn))
            {
                parameters = nsFn.Parameters;
                detail = nsFn.Detail;
                documentation = nsFn.Documentation;
            }
        }
        else if (StdlibRegistry.TryGetFunction(funcName, out var builtIn))
        {
            parameters = builtIn.Parameters;
            detail = builtIn.Detail;
            documentation = builtIn.Documentation;
        }

        if (parameters == null)
        {
            var cached = _engine.GetCachedResult(_playgroundUri);
            var sym = cached?.Symbols.FindDefinition(funcName, line + 1, column + 1);
            if (sym?.Kind is SymbolKind.Function or SymbolKind.Method && sym.Detail != null)
            {
                detail = sym.Detail;
                documentation = sym.Documentation;
                parameters = [];
            }
        }

        if (detail == null)
        {
            return null;
        }

        var paramDtos = (parameters ?? [])
            .Select(p => new ParameterDto(p.Type != null ? $"{p.Name}: {p.Type}" : p.Name, null))
            .ToArray();

        return new SignatureHelpDto(detail, FormatDocumentation(documentation), paramDtos, activeParam);
    }

    [JSInvokable]
    public static string FormatCode(string code)
    {
        try
        {
            var formatter = new StashFormatter();
            return formatter.Format(code);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"FormatCode error: {ex.Message}");
            return code; // Return original code if formatting fails
        }
    }

    // ── Helpers ──

    private static CompletionDto[] GetMemberCompletions(AnalysisResult result, string prefix, int line, int column)
    {
        var completions = new List<CompletionDto>();

        if (StdlibRegistry.IsBuiltInNamespace(prefix))
        {
            foreach (var fn in StdlibRegistry.GetNamespaceMembers(prefix))
            {
                completions.Add(new CompletionDto(fn.Name, KindFunction, fn.Detail, FormatDocumentation(fn.Documentation)));
            }

            foreach (var c in StdlibRegistry.GetNamespaceConstants(prefix))
            {
                completions.Add(new CompletionDto(c.Name, KindConstant, c.Detail, FormatDocumentation(c.Documentation)));
            }

            foreach (var e in StdlibRegistry.Enums.Where(e => e.Namespace == prefix))
            {
                completions.Add(new CompletionDto(e.Name, KindEnum, e.Detail, null));
            }

            return completions.ToArray();
        }

        var allSymbols = result.Symbols.All;

        // Enum type accessed directly (e.g. Color.Red)
        var enumDef = allSymbols.FirstOrDefault(s => s.Name == prefix && s.Kind == SymbolKind.Enum);
        if (enumDef != null)
        {
            foreach (var sym in allSymbols.Where(s => s.ParentName == prefix && s.Kind == SymbolKind.EnumMember))
            {
                completions.Add(new CompletionDto(sym.Name, KindEnumMember, sym.Detail, null));
            }

            return completions.ToArray();
        }

        // Struct type accessed directly or via instance — resolve type hint
        var visibleSyms = result.Symbols.GetVisibleSymbols(line + 1, column + 1);
        var prefixDef = visibleSyms.FirstOrDefault(s => s.Name == prefix);

        var structName = prefix;
        if (prefixDef != null &&
            prefixDef.Kind is SymbolKind.Variable or SymbolKind.Constant or SymbolKind.Parameter or SymbolKind.LoopVariable)
        {
            var narrowed = result.Symbols.GetNarrowedTypeHint(prefix, line + 1, column + 1);
            structName = narrowed ?? prefixDef.TypeHint ?? prefix;
        }

        var structDef = allSymbols.FirstOrDefault(s => s.Name == structName && s.Kind == SymbolKind.Struct);
        if (structDef != null)
        {
            foreach (var sym in allSymbols.Where(s => s.ParentName == structName &&
                (s.Kind == SymbolKind.Field || s.Kind == SymbolKind.Method)))
            {
                int kind = sym.Kind == SymbolKind.Method ? KindFunction : KindProperty;
                completions.Add(new CompletionDto(sym.Name, kind, sym.Detail, null));
            }
            return completions.ToArray();
        }

        // Built-in struct fields
        var builtInStruct = StdlibRegistry.Structs.FirstOrDefault(s => s.Name == structName);
        if (builtInStruct != null)
        {
            foreach (var field in builtInStruct.Fields)
            {
                var fieldDetail = field.Type != null ? $"{field.Name}: {field.Type}" : field.Name;
                completions.Add(new CompletionDto(field.Name, KindProperty, fieldDetail, null));
            }
        }

        return completions.ToArray();
    }

    private static bool IsAfterIsKeyword(string lineText, int column)
    {
        int wordStart = Math.Min(column, lineText.Length);
        while (wordStart > 0 && (char.IsLetterOrDigit(lineText[wordStart - 1]) || lineText[wordStart - 1] == '_'))
        {
            wordStart--;
        }

        int i = wordStart - 1;
        while (i >= 0 && lineText[i] == ' ')
        {
            i--;
        }

        if (i < 1)
        {
            return false;
        }

        // Check that text[i-1..i] == "is" and is not part of a longer word
        return lineText[i] == 's' && lineText[i - 1] == 'i' &&
               (i - 2 < 0 || (!char.IsLetterOrDigit(lineText[i - 2]) && lineText[i - 2] != '_'));
    }

    private static (string FuncName, int ActiveParam, string? DotPrefix)? FindCallContext(string code, int line, int column)
    {
        var lines = code.Split('\n');
        if (line < 0 || line >= lines.Length)
        {
            return null;
        }

        int offset = 0;
        for (int i = 0; i < line; i++)
        {
            offset += lines[i].Length + 1;
        }

        offset += Math.Min(column, lines[line].Length);

        int depth = 0;
        int commas = 0;

        for (int i = offset - 1; i >= 0; i--)
        {
            char c = code[i];
            if (c == ')' || c == ']' || c == '}')
            {
                depth++;
            }
            else if (c == '(' || c == '[' || c == '{')
            {
                if (depth > 0)
                {
                    depth--;
                }
                else if (c == '(')
                {
                    int nameEnd = i - 1;
                    while (nameEnd >= 0 && code[nameEnd] == ' ')
                    {
                        nameEnd--;
                    }

                    if (nameEnd < 0 || (!char.IsLetterOrDigit(code[nameEnd]) && code[nameEnd] != '_'))
                    {
                        return null;
                    }

                    int nameStart = nameEnd;
                    while (nameStart > 0 && (char.IsLetterOrDigit(code[nameStart - 1]) || code[nameStart - 1] == '_'))
                    {
                        nameStart--;
                    }

                    var funcName = code[nameStart..(nameEnd + 1)];

                    string? dotPrefix = null;
                    if (nameStart > 0 && code[nameStart - 1] == '.')
                    {
                        int prefixEnd = nameStart - 2;
                        int prefixStart = prefixEnd;
                        while (prefixStart > 0 && (char.IsLetterOrDigit(code[prefixStart - 1]) || code[prefixStart - 1] == '_'))
                        {
                            prefixStart--;
                        }

                        if (prefixStart <= prefixEnd)
                        {
                            dotPrefix = code[prefixStart..(prefixEnd + 1)];
                        }
                    }

                    return (funcName, commas, dotPrefix);
                }
                else
                {
                    return null;
                }
            }
            else if (c == ',' && depth == 0)
            {
                commas++;
            }
        }

        return null;
    }

    private static HoverDto BuildHover(string codeBlock, string? documentation)
    {
        var parts = new List<string> { codeBlock };
        if (!string.IsNullOrEmpty(documentation))
        {
            parts.Add(FormatDocumentation(documentation)!);
        }

        return new HoverDto(string.Join("\n\n", parts));
    }

    private static string? FormatDocumentation(string? doc)
    {
        if (doc == null)
        {
            return null;
        }

        var docLines = doc.Split('\n');
        var result = new List<string>();
        foreach (var l in docLines)
        {
            var trimmed = l.Trim();
            if (trimmed.StartsWith("@param "))
            {
                var rest = trimmed["@param ".Length..];
                var spaceIdx = rest.IndexOf(' ');
                if (spaceIdx >= 0)
                {
                    result.Add($"**{rest[..spaceIdx]}** — {rest[(spaceIdx + 1)..]}");
                }
                else
                {
                    result.Add($"**{rest}**");
                }
            }
            else if (trimmed.StartsWith("@return "))
            {
                result.Add($"**Returns:** {trimmed["@return ".Length..]}");
            }
            else
            {
                result.Add(l);
            }
        }
        return string.Join("\n", result);
    }

    private static DiagnosticDto SpanToDiagnostic(SourceSpan span, string message, int severity)
    {
        // SourceSpan is 1-based; Monaco IMarkerData also uses 1-based line/column
        return new DiagnosticDto(span.StartLine, span.StartColumn, span.EndLine, span.EndColumn, message, severity);
    }
}
