using Stash.Bytecode;
using Stash.Lexing;
using Stash.Parsing;
using Stash.Parsing.AST;
using Stash.Resolution;

namespace Stash.Tests.Bytecode;

public abstract class BytecodeTestBase
{
    protected static Chunk CompileSource(string source)
    {
        var lexer = new Lexer(source, "<test>");
        List<Token> tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        List<Stmt> stmts = parser.ParseProgram();
        SemanticResolver.Resolve(stmts);
        return Compiler.Compile(stmts);
    }

    protected static object? Execute(string source)
    {
        Chunk chunk = CompileSource(source);
        var vm = new VirtualMachine();
        return vm.Execute(chunk);
    }
}
