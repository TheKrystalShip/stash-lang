namespace Stash.Bytecode;

using System.IO;
using System.Collections.Generic;
using Stash.Common;
using Stash.Lexing;
using Stash.Parsing;
using Stash.Parsing.AST;
using Stash.Resolution;
using Stash.Runtime;

/// <summary>
/// Lex → Parse → Resolve → Compile pipeline helpers used by <see cref="StashEngine"/>.
/// </summary>
internal static class StashCompilationPipeline
{
    /// <summary>Lexes and parses Stash source code into a list of statements, collecting any errors.</summary>
    internal static (List<Stmt> Statements, List<string> Errors) ParseStatements(string source)
    {
        var lexer = new Lexer(source);
        var tokens = lexer.ScanTokens();

        if (lexer.Errors.Count > 0)
        {
            return ([], lexer.Errors);
        }

        var parser = new Parser(tokens);
        var statements = parser.ParseProgram();

        if (parser.Errors.Count > 0)
        {
            return ([], parser.Errors);
        }

        return (statements, []);
    }

    /// <summary>Module loading callback — resolves, lexes, parses, resolves, and compiles a module.</summary>
    internal static Chunk LoadModule(string modulePath, string? currentFile)
    {
        string? basePath = currentFile is not null ? Path.GetDirectoryName(currentFile) : null;
        string fullPath;

        if (Path.IsPathRooted(modulePath))
        {
            fullPath = modulePath;
        }
        else if (basePath is not null)
        {
            fullPath = Path.GetFullPath(Path.Combine(basePath, modulePath));
        }
        else
        {
            fullPath = Path.GetFullPath(modulePath);
        }

        string? resolvedPath = ModuleResolver.ResolveFilePath(fullPath);
        if (resolvedPath is null && basePath is not null && ModuleResolver.IsBareSpecifier(modulePath))
        {
            resolvedPath = ModuleResolver.ResolvePackageImport(modulePath, basePath);
        }

        if (resolvedPath is null)
        {
            throw new RuntimeError($"Cannot find module '{modulePath}'.", null);
        }

        string source = File.ReadAllText(resolvedPath);
        var lexer = new Lexer(source, resolvedPath);
        var tokens = lexer.ScanTokens();
        if (lexer.Errors.Count > 0)
        {
            throw new RuntimeError($"Lex errors in module '{resolvedPath}': {lexer.Errors[0]}", null);
        }

        var parser = new Parser(tokens);
        var stmts = parser.ParseProgram();
        if (parser.Errors.Count > 0)
        {
            throw new RuntimeError($"Parse errors in module '{resolvedPath}': {parser.Errors[0]}", null);
        }

        SemanticResolver.Resolve(stmts);
        return Compiler.Compile(stmts);
    }
}
