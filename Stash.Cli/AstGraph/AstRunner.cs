using System;
using System.IO;
using Stash.Cli.AstGraph.Models;
using Stash.Cli.AstGraph.Visitors;
using Stash.Lexing;
using Stash.Parsing;
using Stash.Resolution;

namespace Stash.Cli.AstGraph;

/// <summary>
/// Orchestrates lexing, parsing, optional semantic resolution, and DOT graph generation.
/// </summary>
internal sealed class AstRunner
{
    private readonly AstOptions _options;

    public AstRunner(AstOptions options)
    {
        _options = options;
    }

    /// <summary>
    /// Runs the full pipeline: read source, lex, parse, optionally resolve, and generate DOT.
    /// </summary>
    public AstResult Run()
    {
        string source;
        try
        {
            source = File.ReadAllText(_options.FilePath!);
        }
        catch (Exception ex)
        {
            return AstResult.FatalError($"Cannot read file: {ex.Message}");
        }

        var lexer = new Lexer(source, _options.FilePath!);
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        var statements = parser.ParseProgram();

        if (parser.Errors.Count > 0)
            return AstResult.ParseError(string.Join(Environment.NewLine, parser.Errors));

        if (_options.Semantic)
            SemanticResolver.Resolve(statements);

        var visitor = new AstDotVisitor(_options.Semantic);
        var dot = visitor.Generate(statements, Path.GetFileName(_options.FilePath)!);
        return AstResult.Success(dot);
    }
}
