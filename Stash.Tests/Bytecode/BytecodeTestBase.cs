using Stash.Bytecode;
using Stash.Lexing;
using Stash.Parsing;
using Stash.Parsing.AST;
using Stash.Resolution;
using Stash.Runtime;
using Stash.Runtime.Types;

namespace Stash.Tests.Bytecode;

public abstract class BytecodeTestBase
{
    /// <summary>
    /// Recursively normalizes VM results: converts List&lt;StashValue&gt; to List&lt;object?&gt;
    /// so test assertions that check types/values work without changes.
    /// </summary>
    protected static object? Normalize(object? value)
    {
        if (value is List<StashValue> svList)
        {
            var result = new List<object?>(svList.Count);
            foreach (var sv in svList)
                result.Add(Normalize(sv.ToObject()));
            return result;
        }
        if (value is List<object?> objList)
        {
            var result = new List<object?>(objList.Count);
            foreach (var item in objList)
                result.Add(Normalize(item));
            return result;
        }
        if (value is StashDictionary dict)
        {
            foreach (object key in dict.RawKeys())
            {
                StashValue sv = dict.Get(key);
                object? val = sv.ToObject();
                object? normalized = Normalize(val);
                if (!ReferenceEquals(normalized, val))
                    dict.Set(key, StashValue.FromObject(normalized));
            }
            return dict;
        }
        return value;
    }

    protected static Chunk CompileSource(string source, bool optimize = true)
    {
        var lexer = new Lexer(source, "<test>");
        List<Token> tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        List<Stmt> stmts = parser.ParseProgram();
        SemanticResolver.Resolve(stmts);
        return Compiler.Compile(stmts, optimize);
    }

    protected static object? Execute(string source)
    {
        Chunk chunk = CompileSource(source);
        var vm = new VirtualMachine();
        return Normalize(vm.Execute(chunk));
    }
}
