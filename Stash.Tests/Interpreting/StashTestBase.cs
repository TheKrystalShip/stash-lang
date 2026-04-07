using Stash.Bytecode;
using Stash.Lexing;
using Stash.Parsing;
using Stash.Resolution;
using Stash.Runtime;
using Stash.Runtime.Types;
using Stash.Stdlib;
using Stash.Tap;

namespace Stash.Tests.Interpreting;

public abstract class StashTestBase
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

    protected static object? Run(string source)
    {
        string full = source + "\nreturn result;";
        var (chunk, vm) = CompileToVM(full);
        return Normalize(vm.Execute(chunk));
    }

    protected static void RunStatements(string source)
    {
        var (chunk, vm) = CompileToVM(source);
        vm.Execute(chunk);
    }

    protected static object? Eval(string source)
    {
        var lexer = new Lexer(source, "<test>");
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        var expr = parser.Parse();
        var chunk = Compiler.CompileExpression(expr);
        var vm = new VirtualMachine(StdlibDefinitions.CreateVMGlobals());
        return Normalize(vm.Execute(chunk));
    }

    protected static void RunExpectingError(string source)
    {
        var (chunk, vm) = CompileToVM(source);
        Assert.Throws<RuntimeError>(() => vm.Execute(chunk));
    }

    protected static RuntimeError RunCapturingError(string source)
    {
        var (chunk, vm) = CompileToVM(source);
        return Assert.Throws<RuntimeError>(() => vm.Execute(chunk));
    }

    protected static string RunCapturingOutput(string source)
    {
        var (chunk, vm) = CompileToVM(source);
        var sw = new StringWriter();
        vm.Output = sw;
        vm.Execute(chunk);
        return sw.ToString();
    }

    protected static string RunCapturingStderr(string source)
    {
        var (chunk, vm) = CompileToVM(source);
        var sw = new StringWriter();
        vm.ErrorOutput = sw;
        vm.Execute(chunk);
        return sw.ToString();
    }

    protected static object? RunWithArgs(string source, string[] scriptArgs)
    {
        string full = source + "\nreturn result;";
        var (chunk, vm) = CompileToVM(full);
        vm.ScriptArgs = scriptArgs;
        return Normalize(vm.Execute(chunk));
    }

    protected static object? RunWithFile(string source, string filePath)
    {
        string full = source + "\nreturn result;";
        var (chunk, vm) = CompileToVM(full, filePath);
        vm.CurrentFile = filePath;
        return Normalize(vm.Execute(chunk));
    }

    protected static (TapReporter reporter, string output) RunWithHarness(string source, string? currentFile = null)
    {
        var (chunk, vm) = CompileToVM(source);
        if (currentFile is not null)
            vm.CurrentFile = currentFile;
        var sw = new StringWriter();
        var reporter = new TapReporter(sw);
        vm.TestHarness = reporter;
        vm.Execute(chunk);
        reporter.OnRunComplete(reporter.PassedCount, reporter.FailedCount, reporter.SkippedCount);
        return (reporter, sw.ToString());
    }

    private static (Chunk chunk, VirtualMachine vm) CompileToVM(string source, string sourceName = "<test>")
    {
        var lexer = new Lexer(source, sourceName);
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        var stmts = parser.ParseProgram();
        SemanticResolver.Resolve(stmts);
        var chunk = Compiler.Compile(stmts);
        var vm = new VirtualMachine(StdlibDefinitions.CreateVMGlobals());
        return (chunk, vm);
    }
}
