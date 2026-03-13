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

        var validator = new SemanticValidator(symbols);
        var semanticDiagnostics = validator.Validate(statements);

        var result = new AnalysisResult(tokens, statements, lexErrors, parseErrors, symbols, semanticDiagnostics);
        _cache[uri] = result;
        return result;
    }

    public AnalysisResult? GetCachedResult(Uri uri)
    {
        return _cache.TryGetValue(uri, out var result) ? result : null;
    }
}
