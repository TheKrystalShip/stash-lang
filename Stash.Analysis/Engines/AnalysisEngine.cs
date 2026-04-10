namespace Stash.Analysis;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Extensions.Logging;
using Stash.Common;
using Stash.Lexing;
using Stash.Parsing;

/// <summary>
/// Core analysis engine for the Stash language server that lexes, parses, and semantically
/// validates source documents and caches the results for handler consumption.
/// </summary>
/// <remarks>
/// Each call to <see cref="Analyze"/> performs a full pipeline run (lex → parse → symbol
/// collection → import resolution → type inference → semantic validation) and stores the
/// <see cref="AnalysisResult"/> in an in-memory cache keyed by document <see cref="Uri"/>.
/// Handlers retrieve cached results via <see cref="GetCachedResult"/> or the convenience
/// method <see cref="GetContextAt"/>.
/// </remarks>
public class AnalysisEngine
{
    private readonly ILogger<AnalysisEngine> _logger;

    /// <summary>Thread-safe cache mapping document URIs to their most recent analysis results.</summary>
    private readonly ConcurrentDictionary<Uri, AnalysisResult> _cache = new();

    /// <summary>Content hash cache: maps URI → (hash, result) for skipping unchanged files.</summary>
    private readonly ConcurrentDictionary<Uri, (int ContentHash, AnalysisResult Result)> _contentHashCache = new();

    /// <summary>Resolver responsible for locating and parsing imported Stash modules.</summary>
    private readonly ImportResolver _importResolver = new();

    public AnalysisEngine(ILogger<AnalysisEngine> logger)
    {
        _logger = logger;
    }

    /// <summary>Gets the <see cref="ImportResolver"/> used to resolve cross-file imports.</summary>
    public ImportResolver ImportResolver => _importResolver;

    /// <summary>
    /// Performs a full analysis pass on the given source text and caches the result.
    /// </summary>
    /// <param name="uri">The document URI that identifies the source file.</param>
    /// <param name="source">The raw source text to analyze.</param>
    /// <returns>
    /// An <see cref="AnalysisResult"/> containing tokens, AST statements, symbol table,
    /// diagnostics, and import information.
    /// </returns>
    public AnalysisResult Analyze(Uri uri, string source, bool noImports = false, ProjectConfig? configOverride = null)
    {
        _logger.LogDebug("Analyzing {Uri}", uri);

        // Content-hash short-circuit: skip full re-analysis if source hasn't changed
        int sourceHash = source.GetHashCode();
        if (_contentHashCache.TryGetValue(uri, out var cached) && cached.ContentHash == sourceHash)
        {
            _logger.LogDebug("Content hash match for {Uri}, returning cached result", uri);
            _cache[uri] = cached.Result;
            return cached.Result;
        }

        var filePath = uri.IsFile ? uri.LocalPath : uri.ToString();

        var lexer = new Lexer(source, filePath, preserveTrivia: true);
        var tokens = lexer.ScanTokens();
        var lexErrors = new List<string>(lexer.Errors);

        // Filter out trivia tokens for the parser (it doesn't handle comments)
        var parserTokens = new List<Token>(tokens.Count);
        foreach (var t in tokens)
        {
            if (t.Type is not (TokenType.DocComment or TokenType.SingleLineComment or TokenType.BlockComment or TokenType.Shebang))
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
        List<SemanticDiagnostic> importDiagnostics = new();
        Dictionary<string, ImportResolver.ModuleInfo> namespaceImportsResult = new();

        if (!noImports)
        {
            var importResolution = _importResolver.ResolveImports(uri, statements, ParseModule);
            importDiagnostics = importResolution.Diagnostics;
            namespaceImportsResult = importResolution.NamespaceImports;

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
                        var oldSymbol = globalSymbols[i];
                        symbols.GlobalScope.ReplaceSymbol(i, resolvedSym);
                        replaced = true;

                        // Update references that still point to the old placeholder
                        foreach (var reference in symbols.References)
                        {
                            if (reference.ResolvedSymbol == oldSymbol)
                            {
                                reference.ResolvedSymbol = resolvedSym;
                            }
                        }
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
                        var oldSymbol = globalSymbols[i];
                        var newSymbol = new SymbolInfo(
                            alias, SymbolKind.Namespace, oldSymbol.Span,
                            detail: oldSymbol.Detail,
                            sourceUri: moduleInfo.Uri);
                        symbols.GlobalScope.ReplaceSymbol(i, newSymbol);

                        // Patch references that still point to the old placeholder
                        foreach (var reference in symbols.References)
                        {
                            if (reference.ResolvedSymbol == oldSymbol)
                            {
                                reference.ResolvedSymbol = newSymbol;
                            }
                        }
                        break;
                    }
                }
            }
        }

        TypeInferenceEngine.InferTypes(symbols, statements);

        DocCommentResolver.Resolve(tokens, symbols);

        // Load project config early so disabled rules can be pre-filtered
        var scriptDir = uri.IsFile ? Path.GetDirectoryName(uri.LocalPath) : null;
        var projectConfig = configOverride ?? ProjectConfig.Load(scriptDir);

        var allRules = Rules.RuleRegistry.GetAllRules();
        var enabledRules = new System.Collections.Generic.List<Rules.IAnalysisRule>(allRules.Count);
        foreach (var rule in allRules)
        {
            if (!projectConfig.IsCodeDisabled(rule.Descriptor.Code))
            {
                enabledRules.Add(rule);
            }
        }

        var validator = new SemanticValidator(symbols, enabledRules);
        var semanticDiagnostics = validator.Validate(statements);
        semanticDiagnostics.AddRange(importDiagnostics);

        // Parse suppression directives from trivia tokens
        var suppressionMap = SuppressionDirectiveParser.Parse(tokens);
        semanticDiagnostics = suppressionMap.Filter(semanticDiagnostics);
        semanticDiagnostics = projectConfig.Apply(semanticDiagnostics, uri.IsFile ? filePath : null);

        var result = new AnalysisResult(tokens, statements, lexErrors, parseErrors,
            lexer.StructuredErrors, parser.StructuredErrors, symbols, semanticDiagnostics,
            namespaceImportsResult);
        _logger.LogDebug("Analysis complete: {Uri} — {DiagCount} diagnostics, {SymbolCount} symbols", uri, result.SemanticDiagnostics.Count, result.Symbols.All.Count);
        _contentHashCache[uri] = (sourceHash, result);
        _cache[uri] = result;
        return result;
    }

    /// <summary>
    /// Returns the most recently cached <see cref="AnalysisResult"/> for the given URI, or
    /// <see langword="null"/> if the document has not been analyzed yet.
    /// </summary>
    /// <param name="uri">The document URI to look up.</param>
    /// <returns>The cached <see cref="AnalysisResult"/>, or <see langword="null"/> if not present.</returns>
    public AnalysisResult? GetCachedResult(Uri uri)
    {
        return _cache.TryGetValue(uri, out var result) ? result : null;
    }

    /// <summary>
    /// Returns all URIs currently in the analysis cache, including both open documents
    /// and background-indexed files.
    /// </summary>
    public IReadOnlyCollection<Uri> GetAllCachedUris()
    {
        return _cache.Keys.ToArray();
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

    /// <summary>
    /// Invalidates the import cache for the given file so that the next analysis picks up changes.
    /// </summary>
    /// <param name="absolutePath">The absolute file system path of the module to invalidate.</param>
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
        _logger.LogTrace("FindCrossFileReferences: {Symbol} in {Uri}", symbolName, sourceUri);
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

    /// <summary>
    /// Parses the Stash source file at the given path and returns a <see cref="ImportResolver.ModuleInfo"/>
    /// containing its symbol table and any lex/parse diagnostics.
    /// </summary>
    /// <param name="absolutePath">The absolute file system path of the module to parse.</param>
    /// <returns>
    /// An <see cref="ImportResolver.ModuleInfo"/> describing the module's URI, path, scope tree,
    /// and structured errors.
    /// </returns>
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
