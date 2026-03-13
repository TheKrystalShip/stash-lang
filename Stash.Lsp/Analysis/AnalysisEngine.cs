namespace Stash.Lsp.Analysis;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Stash.Lexing;
using Stash.Parsing;
using Stash.Parsing.AST;

public class AnalysisEngine
{
    private readonly ConcurrentDictionary<Uri, AnalysisResult> _cache = new();
    private readonly ImportResolver _importResolver = new();

    public ImportResolver ImportResolver => _importResolver;

    public AnalysisResult Analyze(Uri uri, string source)
    {
        var filePath = uri.IsFile ? uri.LocalPath : uri.ToString();

        var lexer = new Lexer(source, filePath);
        var tokens = lexer.ScanTokens();
        var lexErrors = new List<string>(lexer.Errors);

        var parser = new Parser(tokens);
        var statements = parser.ParseProgram();
        var parseErrors = new List<string>(parser.Errors);

        var symbolCollector = new SymbolCollector();
        var symbols = symbolCollector.Collect(statements);

        // Resolve imports and inject enriched symbols before semantic validation
        var importResolution = _importResolver.ResolveImports(uri, statements);

        foreach (var resolvedSym in importResolution.ResolvedSymbols)
        {
            var globalSymbols = symbols.GlobalScope.Symbols;
            bool replaced = false;
            for (int i = 0; i < globalSymbols.Count; i++)
            {
                if (globalSymbols[i].Name == resolvedSym.Name && globalSymbols[i].SourceUri == null)
                {
                    if (resolvedSym.Kind == SymbolKind.Field || resolvedSym.Kind == SymbolKind.EnumMember)
                    {
                        break;
                    }
                    globalSymbols[i] = resolvedSym;
                    replaced = true;
                    break;
                }
            }
            if (!replaced)
            {
                symbols.GlobalScope.AddSymbol(resolvedSym);
            }
        }

        foreach (var (alias, moduleInfo) in importResolution.NamespaceImports)
        {
            var globalSymbols = symbols.GlobalScope.Symbols;
            for (int i = 0; i < globalSymbols.Count; i++)
            {
                if (globalSymbols[i].Name == alias && globalSymbols[i].Kind == SymbolKind.Namespace)
                {
                    globalSymbols[i] = new SymbolInfo(
                        alias, SymbolKind.Namespace, globalSymbols[i].Span,
                        detail: globalSymbols[i].Detail,
                        sourceUri: moduleInfo.Uri);
                    break;
                }
            }
        }

        var validator = new SemanticValidator(symbols);
        var semanticDiagnostics = validator.Validate(statements);
        semanticDiagnostics.AddRange(importResolution.Diagnostics);

        var result = new AnalysisResult(tokens, statements, lexErrors, parseErrors,
            lexer.StructuredErrors, parser.StructuredErrors, symbols, semanticDiagnostics,
            importResolution.NamespaceImports);
        _cache[uri] = result;
        return result;
    }

    public AnalysisResult? GetCachedResult(Uri uri)
    {
        return _cache.TryGetValue(uri, out var result) ? result : null;
    }

    public void InvalidateModule(string absolutePath)
    {
        _importResolver.InvalidateCache(absolutePath);
    }
}
