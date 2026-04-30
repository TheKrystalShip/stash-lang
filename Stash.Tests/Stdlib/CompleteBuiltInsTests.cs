using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Stash.Bytecode;
using Stash.Cli.Completion;
using Stash.Cli.Shell;
using Stash.Runtime;
using Stash.Runtime.Types;
using Stash.Stdlib;
using Stash.Stdlib.BuiltIns;

namespace Stash.Tests.Stdlib;

[CollectionDefinition("CompleteTests", DisableParallelization = true)]
public sealed class CompleteTestsCollection { }

/// <summary>
/// Unit tests for the <c>complete</c> namespace built-in functions in
/// <see cref="CompleteBuiltIns"/> (spec §9, §15.7).
/// Each test resets all static state via <c>ResetAllForTesting()</c> for isolation.
/// </summary>
[Collection("CompleteTests")]
public class CompleteBuiltInsTests : Stash.Tests.Interpreting.StashTestBase
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static (Chunk chunk, VirtualMachine vm) CompileSource(string source)
    {
        var lexer = new Stash.Lexing.Lexer(source, "<test>");
        var tokens = lexer.ScanTokens();
        var parser = new Stash.Parsing.Parser(tokens);
        var stmts = parser.ParseProgram();
        Stash.Resolution.SemanticResolver.Resolve(stmts);
        var chunk = Compiler.Compile(stmts);
        var vm = new VirtualMachine(StdlibDefinitions.CreateVMGlobals());
        vm.Output = TextWriter.Null;
        vm.ErrorOutput = TextWriter.Null;
        vm.EmbeddedMode = true;
        return (chunk, vm);
    }

    private static object? RunScript(string source)
    {
        var (chunk, vm) = CompileSource(source + "\nreturn result;");
        return vm.Execute(chunk);
    }

    private static void RunVoidStatements(string source)
    {
        var (chunk, vm) = CompileSource(source);
        vm.Execute(chunk);
    }

    private static RuntimeError RunExpectingRuntimeError(string source)
    {
        var (chunk, vm) = CompileSource(source);
        return Assert.Throws<RuntimeError>(() => vm.Execute(chunk));
    }

    private static CompletionEngine MakeEngineAndWire(out CustomCompleterRegistry registry, Func<string, bool>? isExec = null)
    {
        var vm = new VirtualMachine(StdlibDefinitions.CreateVMGlobals());
        vm.Output = TextWriter.Null;
        vm.ErrorOutput = TextWriter.Null;
        vm.EmbeddedMode = true;

        var cache = new PathExecutableCache(isExec ?? (_ => false));
        registry = new CustomCompleterRegistry();

        var shellCtx = new ShellContext
        {
            Vm = vm,
            PathCache = cache,
            Keywords = ShellContext.BuildKeywordSet(),
            Namespaces = new HashSet<string>(StdlibRegistry.NamespaceNames, StringComparer.Ordinal),
            ShellBuiltinNames = ShellContext.BuildShellBuiltinSet(),
        };
        var classifier = new ShellLineClassifier(shellCtx);
        var engine = new CompletionEngine(vm, cache, registry, classifier);
        CompletionWiring.Wire(engine, registry, vm);
        return engine;
    }

    // ── complete.register ─────────────────────────────────────────────────────

    [Fact]
    public void Register_ValidCallable_ReturnsNull()
    {
        CompleteBuiltIns.ResetAllForTesting();
        MakeEngineAndWire(out _);

        object? result = RunScript("""
            complete.register("git", (_ctx) => { return []; });
            let result = null;
            """);
        Assert.Null(result);
    }

    [Fact]
    public void Register_ThenRegistered_IncludesName()
    {
        CompleteBuiltIns.ResetAllForTesting();
        MakeEngineAndWire(out _);

        object? result = RunScript("""
            complete.register("git", (_ctx) => { return []; });
            let result = complete.registered();
            """);
        var list = Assert.IsType<List<object?>>(Normalize(result));
        Assert.Contains("git", list.Cast<string>());
    }

    [Fact]
    public void Register_NonCallable_ThrowsTypeError()
    {
        CompleteBuiltIns.ResetAllForTesting();
        MakeEngineAndWire(out _);

        var ex = RunExpectingRuntimeError("""complete.register("git", 42);""");
        Assert.Equal(StashErrorTypes.TypeError, ex.ErrorType);
    }

    // ── complete.unregister ───────────────────────────────────────────────────

    [Fact]
    public void Unregister_ExistingEntry_ReturnsTrue()
    {
        CompleteBuiltIns.ResetAllForTesting();
        MakeEngineAndWire(out _);

        object? result = RunScript("""
            complete.register("git", (_ctx) => { return []; });
            let result = complete.unregister("git");
            """);
        Assert.Equal(true, result);
    }

    [Fact]
    public void Unregister_SecondCall_ReturnsFalse()
    {
        CompleteBuiltIns.ResetAllForTesting();
        MakeEngineAndWire(out _);

        object? result = RunScript("""
            complete.register("git", (_ctx) => { return []; });
            complete.unregister("git");
            let result = complete.unregister("git");
            """);
        Assert.Equal(false, result);
    }

    [Fact]
    public void Unregister_NonExistent_ReturnsFalse()
    {
        CompleteBuiltIns.ResetAllForTesting();
        MakeEngineAndWire(out _);

        object? result = RunScript("""
            let result = complete.unregister("notexist");
            """);
        Assert.Equal(false, result);
    }

    // ── complete.registered ───────────────────────────────────────────────────

    [Fact]
    public void Registered_AfterRegisterAndUnregister_ReturnsCorrectList()
    {
        CompleteBuiltIns.ResetAllForTesting();
        MakeEngineAndWire(out _);

        object? result = RunScript("""
            complete.register("git", (_ctx) => { return []; });
            complete.register("docker", (_ctx) => { return []; });
            complete.unregister("git");
            let result = complete.registered();
            """);
        var list = Assert.IsType<List<object?>>(Normalize(result));
        Assert.Single(list);
        Assert.Equal("docker", list[0]);
    }

    // ── complete.suggest ──────────────────────────────────────────────────────

    [Fact]
    public void Suggest_FsDot_ReturnsNonEmptyCandidates()
    {
        CompleteBuiltIns.ResetAllForTesting();
        MakeEngineAndWire(out _);

        object? result = RunScript("""
            let r = complete.suggest("fs.", -1);
            let result = r;
            """);

        var inst = Assert.IsType<StashInstance>(result);
        Assert.Equal("CompletionResult", inst.TypeName);
        var candidates = inst.GetField("candidates", null).ToObject() as List<StashValue>;
        Assert.NotNull(candidates);
        Assert.True(candidates!.Count > 0, "Expected fs.* completions");
    }

    [Fact]
    public void Suggest_CursorMinusOne_TreatedAsEndOfLine()
    {
        CompleteBuiltIns.ResetAllForTesting();
        MakeEngineAndWire(out _);

        object? result = RunScript("""
            let r = complete.suggest("pri", -1);
            let result = r;
            """);
        var inst = Assert.IsType<StashInstance>(result);
        var candidates = inst.GetField("candidates", null).ToObject() as List<StashValue>;
        Assert.NotNull(candidates);
        Assert.Contains(candidates!, c => c.ToObject() is string s && s == "print");
    }

    [Fact]
    public void Suggest_WithCustomCompleter_ReturnsFilteredCandidates()
    {
        CompleteBuiltIns.ResetAllForTesting();
        MakeEngineAndWire(out _, isExec: name => name == "git");
        // Note: "git" must be on PATH or classified as shell command; we test the completer path
        object? result = RunScript("""
            complete.register("git", (ctx) => { return ["status", "checkout", "commit"]; });
            let r = complete.suggest("git che", -1);
            let result = r;
            """);

        var inst = Assert.IsType<StashInstance>(result);
        var candidates = inst.GetField("candidates", null).ToObject() as List<StashValue>;
        Assert.NotNull(candidates);

        var candidateStrings = candidates!.Select(c => c.ToObject() as string).ToList();
        // "checkout" starts with "che" → should be in results; "status"/"commit" should not
        Assert.Contains("checkout", candidateStrings);
        Assert.DoesNotContain("status", candidateStrings);
        Assert.DoesNotContain("commit", candidateStrings);

        string? prefix = inst.GetField("common_prefix", null).ToObject() as string;
        Assert.Equal("checkout", prefix);
    }

    // ── Script mode (no wiring) ───────────────────────────────────────────────

    [Fact]
    public void ScriptMode_Register_IsNoopNoThrow()
    {
        CompleteBuiltIns.ResetAllForTesting(); // slots = null → script mode

        // Should not throw — just a no-op
        RunVoidStatements("""complete.register("git", (_ctx) => { return []; });""");
    }

    [Fact]
    public void ScriptMode_Registered_ReturnsEmptyArray()
    {
        CompleteBuiltIns.ResetAllForTesting();

        object? result = RunScript("""let result = complete.registered();""");
        var list = Assert.IsType<List<object?>>(Normalize(result));
        Assert.Empty(list);
    }

    [Fact]
    public void ScriptMode_Suggest_ReturnsEmptyCompletionResult()
    {
        CompleteBuiltIns.ResetAllForTesting();

        object? result = RunScript("""
            let r = complete.suggest("fs.", -1);
            let result = r;
            """);
        var inst = Assert.IsType<StashInstance>(result);
        Assert.Equal("CompletionResult", inst.TypeName);
        var candidates = inst.GetField("candidates", null).ToObject() as List<StashValue>;
        Assert.NotNull(candidates);
        Assert.Empty(candidates!);
    }

    [Fact]
    public void ScriptMode_Unregister_ReturnsFalse()
    {
        CompleteBuiltIns.ResetAllForTesting();

        object? result = RunScript("""let result = complete.unregister("git");""");
        Assert.Equal(false, result);
    }
}
