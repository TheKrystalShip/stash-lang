namespace Stash.Lsp.Analysis;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using Stash.Common;
using Stash.Lexing;
using Stash.Parsing;

public class AnalysisEngine
{
    private readonly ConcurrentDictionary<Uri, AnalysisResult> _cache = new();
    private readonly ImportResolver _importResolver = new();

    public ImportResolver ImportResolver => _importResolver;

    public AnalysisResult Analyze(Uri uri, string source)
    {
        var filePath = uri.IsFile ? uri.LocalPath : uri.ToString();

        var lexer = new Lexer(source, filePath, preserveTrivia: true);
        var tokens = lexer.ScanTokens();
        var lexErrors = new List<string>(lexer.Errors);

        // Filter out trivia tokens for the parser (it doesn't handle comments)
        var parserTokens = new List<Token>(tokens.Count);
        foreach (var t in tokens)
        {
            if (t.Type is not (TokenType.SingleLineComment or TokenType.BlockComment or TokenType.Shebang))
            {
                parserTokens.Add(t);
            }
        }
        var parser = new Parser(parserTokens);
        var statements = parser.ParseProgram();
        var parseErrors = new List<string>(parser.Errors);

        var symbolCollector = new SymbolCollector();
        var symbols = symbolCollector.Collect(statements);

        // Resolve imports and inject enriched symbols before semantic validation
        var importResolution = _importResolver.ResolveImports(uri, statements, ParseModule);

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
                    symbols.GlobalScope.ReplaceSymbol(i, resolvedSym);
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
                    symbols.GlobalScope.ReplaceSymbol(i, new SymbolInfo(
                        alias, SymbolKind.Namespace, globalSymbols[i].Span,
                        detail: globalSymbols[i].Detail,
                        sourceUri: moduleInfo.Uri));
                    break;
                }
            }
        }

        TypeInferenceEngine.InferTypes(symbols, statements);

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

    /// <summary>
    /// Gets the cached analysis result and cursor context for common handler operations.
    /// Returns null if any precondition fails (no cached result, no text, no word at position).
    /// </summary>
    public (AnalysisResult Result, string Word)? GetContextAt(Uri uri, string? text, int lspLine, int lspCharacter)
    {
        var result = GetCachedResult(uri);
        if (result == null || text == null)
        {
            return null;
        }

        var word = TextUtilities.FindWordAtPosition(text, lspLine, lspCharacter);
        if (word == null)
        {
            return null;
        }

        return (result, word);
    }

    public void InvalidateModule(string absolutePath)
    {
        _importResolver.InvalidateCache(absolutePath);
    }

    /// <summary>
    /// Returns document URIs that import the given file and should be re-analyzed.
    /// </summary>
    public IReadOnlyCollection<Uri> GetDependents(string absolutePath)
    {
        return _importResolver.GetDependents(absolutePath);
    }

    /// <summary>
    /// Finds references to a symbol from files that import the given source file.
    /// Handles both namespace imports (alias.member) and selective imports (direct name).
    /// </summary>
    public List<(Uri Uri, SourceSpan Span)> FindCrossFileReferences(Uri sourceUri, string symbolName)
    {
        var results = new List<(Uri, SourceSpan)>();
        if (!sourceUri.IsFile)
        {
            return results;
        }

        var dependents = _importResolver.GetDependents(sourceUri.LocalPath);

        foreach (var depUri in dependents)
        {
            var depResult = GetCachedResult(depUri);
            if (depResult == null)
            {
                continue;
            }

            // Case 1: Namespace import (import "file" as alias)
            // Look for alias.symbolName in tokens
            foreach (var (alias, moduleInfo) in depResult.NamespaceImports)
            {
                if (moduleInfo.AbsolutePath != sourceUri.LocalPath)
                {
                    continue;
                }

                var tokens = depResult.Tokens;
                for (int i = 0; i < tokens.Count - 2; i++)
                {
                    if (tokens[i].Lexeme == alias
                        && tokens[i].Type == TokenType.Identifier
                        && tokens[i + 1].Type == TokenType.Dot
                        && tokens[i + 2].Lexeme == symbolName
                        && tokens[i + 2].Type == TokenType.Identifier)
                    {
                        results.Add((depUri, tokens[i + 2].Span));
                    }
                }
            }

            // Case 2: Selective import (import { name } from "file")
            // The imported symbol has SourceUri pointing to the source module
            foreach (var sym in depResult.Symbols.GlobalScope.Symbols)
            {
                if (sym.Name == symbolName && sym.SourceUri == sourceUri)
                {
                    var refs = depResult.Symbols.FindReferences(symbolName, sym.Span.StartLine, sym.Span.StartColumn);
                    foreach (var r in refs)
                    {
                        // Skip the import statement entry itself
                        if (r.Span == sym.Span)
                        {
                            continue;
                        }

                        results.Add((depUri, r.Span));
                    }
                    break;
                }
            }
        }

        return results;
    }

    private ImportResolver.ModuleInfo ParseModule(string absolutePath)
    {
        var uri = new Uri(absolutePath);
        var source = File.ReadAllText(absolutePath);

        var lexer = new Lexer(source, absolutePath);
        var tokens = lexer.ScanTokens();

        var parser = new Parser(tokens);
        var statements = parser.ParseProgram();

        var collector = new SymbolCollector { IncludeBuiltIns = false };
        var scopeTree = collector.Collect(statements);

        var errors = new List<DiagnosticError>();
        errors.AddRange(lexer.StructuredErrors);
        errors.AddRange(parser.StructuredErrors);

        return new ImportResolver.ModuleInfo(uri, absolutePath, scopeTree, errors);
    }
}
