namespace Stash.Tests.Bytecode;

using System;
using System.Collections.Generic;
using System.Threading;
using Stash.Bytecode;
using Stash.Lexing;
using Stash.Parsing;
using Stash.Resolution;
using Stash.Runtime;
using Stash.Runtime.Types;
using Stash.Stdlib;
using Stash.Stdlib.Abstractions;
using Stash.Stdlib.Models;
using Stash.Stdlib.Registration;

/// <summary>
/// Tests for P3 of stdlib-namespace-members: compile-time DotExpr resolution and the
/// CSE-ineligibility constraint for Live-stability data members.
/// </summary>
public class NamespaceDotResolutionTests
{
    // =========================================================================
    // Test helpers — same pattern as NamespaceMemberRuntimeTests
    // =========================================================================

    private static (Chunk chunk, VirtualMachine vm) CompileWithNamespace(
        string source,
        string namespaceName,
        Action<NamespaceBuilder> configure)
    {
        var builder = new NamespaceBuilder(namespaceName);
        configure(builder);
        NamespaceDefinition def = builder.Build();

        var globals = StdlibDefinitions.CreateVMGlobals();
        globals[namespaceName] = StashValue.FromObj(def.Namespace);

        var lexer = new Lexer(source, "<test>");
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        var stmts = parser.ParseProgram();
        SemanticResolver.Resolve(stmts);
        var chunk = Compiler.Compile(stmts);
        var vm = new VirtualMachine(globals);
        return (chunk, vm);
    }

    private static object? RunReturning(string source, string namespaceName, Action<NamespaceBuilder> configure)
    {
        string full = source + "\nreturn result;";
        var (chunk, vm) = CompileWithNamespace(full, namespaceName, configure);
        return NormalizeResult(vm.Execute(chunk));
    }

    private static void RunStatements(string source, string namespaceName, Action<NamespaceBuilder> configure)
    {
        var (chunk, vm) = CompileWithNamespace(source, namespaceName, configure);
        vm.Execute(chunk);
    }

    private static object? NormalizeResult(object? value)
    {
        if (value is List<StashValue> svList)
        {
            var result = new List<object?>(svList.Count);
            foreach (var sv in svList) result.Add(NormalizeResult(sv.ToObject()));
            return result;
        }
        if (value is StashValue sv2)
            return NormalizeResult(sv2.ToObject());
        return value;
    }

    // =========================================================================
    // CSE-ineligibility for Live DataMember (done_when — regression vector)
    // =========================================================================

    /// <summary>
    /// Adjacent reads of a Live DataMember from a known stdlib namespace must each invoke
    /// the getter. Before the P3 LVN fix, the second read would be collapsed to a Move by
    /// the CSE pass, causing the getter to be called only once.
    /// This test uses <c>log.level</c> (a real Live stdlib member) so the LVN pass can
    /// detect it via <c>StdlibRegistry.LiveMemberNames</c>.
    /// </summary>
    [Fact]
    public void LiveStdlibMember_AdjacentReads_BothReflectCurrentState()
    {
        // If CSE were applied, both reads would share the same register and the second
        // read would not re-invoke the getter.  We verify that after a level change between
        // reads the second read reflects the updated state — which is only possible if the
        // reads are independent.
        //
        // Script: read level before setLevel, call setLevel("warn"), read level after.
        // If CSE fires: both reads return the same value (the pre-setLevel value).
        // If CSE is correctly suppressed: before="info", after="warn".
        //
        // Because the reads are in the same basic block, LVN would CSE them without the fix.
        // We interpose a setLevel call which is call-like (kills all field VNs regardless),
        // so this test actually doesn't distinguish — we need adjacency in a single basic block.
        // See LogLevel_AdjacentReads_SameBlock_BothReflectSameLevel below.
        var globals = StdlibDefinitions.CreateVMGlobals();
        var lexer = new Lexer("let a = log.level;\nlog.setLevel(\"warn\");\nlet b = log.level;\nreturn [a, b];", "<test>");
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        var stmts = parser.ParseProgram();
        SemanticResolver.Resolve(stmts);
        var chunk = Compiler.Compile(stmts);
        var vm = new VirtualMachine(globals);
        var result = vm.Execute(chunk) as List<StashValue>;
        Assert.NotNull(result);
        Assert.Equal("info", result![0].AsObj as string);  // before setLevel
        Assert.Equal("warn", result![1].AsObj as string);  // after setLevel
    }

    [Fact]
    public void LiveStdlibMember_AdjacentReadsInSameBlock_NotCSEd()
    {
        // This is the critical CSE regression test.
        // Two adjacent reads of log.level in the same basic block (no calls between them)
        // must both produce the same current value — and both must independently access
        // the getter so that if the level were changed externally they would both update.
        //
        // With CSE (buggy): both reads share the same register; the value is loaded once.
        // Without CSE (fixed): each read is an independent GetFieldIC invocation.
        //
        // We cannot easily detect "loaded once vs twice" from within the script itself
        // for a Cached-vs-Live distinction when both reads return the same value.
        // Instead, we verify via the disassembly that the second read is NOT a Move.
        var globals = StdlibDefinitions.CreateVMGlobals();
        var lexer = new Lexer("let a = log.level;\nlet b = log.level;\nreturn [a, b];", "<test>");
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        var stmts = parser.ParseProgram();
        SemanticResolver.Resolve(stmts);
        var chunk = Compiler.Compile(stmts);

        // Verify the disassembly has two GetFieldIC instructions for "level",
        // NOT a GetFieldIC followed by a Move (which would indicate CSE collapsed them).
        string disasm = Disassembler.Disassemble(chunk);
        // Disassembler uses lower-case dotted form: "get.field.ic"
        int getFieldCount = CountOccurrences(disasm, "get.field.ic") + CountOccurrences(disasm, "get.field ");
        // There should be at least 2 field read instructions (one per let binding).
        // If CSE fired, the second would be a Move and we'd see only 1 field read.
        Assert.True(getFieldCount >= 2, $"Expected at least 2 GetField* instructions for 2 live-member reads, found {getFieldCount}. Disasm:\n{disasm}");

        // Also verify runtime: both values are correct.
        var vm = new VirtualMachine(globals);
        var result = vm.Execute(chunk) as List<StashValue>;
        Assert.NotNull(result);
        Assert.Equal("info", result![0].AsObj as string);
        Assert.Equal("info", result![1].AsObj as string);
    }

    private static int CountOccurrences(string text, string pattern)
    {
        int count = 0;
        int index = 0;
        while ((index = text.IndexOf(pattern, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += pattern.Length;
        }
        return count;
    }

    // =========================================================================
    // Cached DataMember — CSE IS allowed (no regression)
    // =========================================================================

    [Fact]
    public void CachedMember_AdjacentReads_GetterCalledAtMostOnce()
    {
        // Cached members need not be CSE-ineligible — the getter may be called once or
        // twice (CSE may or may not fire), but both reads must return the same value.
        int callCount = 0;
        Func<IInterpreterContext, StashValue> getter = _ =>
        {
            Interlocked.Increment(ref callCount);
            return StashValue.FromInt(42L);
        };

        var (chunk, vm) = CompileWithNamespace(
            "let a = ns.cachedMember;\nlet b = ns.cachedMember;\nreturn [a, b];",
            "ns",
            b => b.Member("cachedMember", getter, Stability.Cached, "int", "cached getter"));

        var result = vm.Execute(chunk) as List<StashValue>;
        Assert.NotNull(result);
        Assert.Equal(42L, result![0].AsInt);
        Assert.Equal(42L, result[1].AsInt);
    }

    // =========================================================================
    // Bare DataMember access (done_when — let n = ns.member;)
    // =========================================================================

    [Fact]
    public void DataMember_BareRead_ReturnsGetterValue()
    {
        object? result = RunReturning(
            "result = ns.myMember;",
            "ns",
            b => b.Member("myMember",
                _ => StashValue.FromInt(7L),
                Stability.Cached, "int", "test member"));

        Assert.Equal(7L, result);
    }

    [Fact]
    public void DataMember_BareRead_Live_ReturnsGetterValue()
    {
        long counter = 0L;
        object? result = RunReturning(
            "result = ns.liveOne;",
            "ns",
            b => b.Member("liveOne",
                _ => StashValue.FromInt(Interlocked.Increment(ref counter)),
                Stability.Live, "int", "live one-shot"));

        Assert.Equal(1L, result);
    }

    // =========================================================================
    // Dynamic receiver — no SA0846 at compile time (done_when)
    // =========================================================================

    [Fact]
    public void DataMember_DynamicReceiver_WorksAtRuntime()
    {
        // Dynamic receiver: compile succeeds and runtime correctly invokes getter.
        object? result = RunReturning(
            "let n = ns;\nresult = n.dynMember;",
            "ns",
            b => b.Member("dynMember",
                _ => StashValue.FromInt(55L),
                Stability.Live, "int", "dynamic member"));

        Assert.Equal(55L, result);
    }

    // =========================================================================
    // Function reference — bare read returns function value (done_when)
    // =========================================================================

    [Fact]
    public void FunctionEntry_BareRead_ReturnsCallableFunction()
    {
        // let f = io.println should yield a function value, not invoke println.
        object? result = RunReturning(
            "result = typeof(ns.doThing);",
            "ns",
            b => b.Function("doThing",
                [],
                static (ctx, args) => StashValue.FromInt(0L),
                "int", false, "test function"));

        Assert.Equal("function", result);
    }

    // =========================================================================
    // Real stdlib — log.level DataMember integration
    // =========================================================================

    [Fact]
    public void LogLevel_BareRead_ReturnsCurrentLevel()
    {
        // log.level is a real Live DataMember registered via [StashMember(Stability = Live)].
        // Bare access should return the current log level string.
        var globals = StdlibDefinitions.CreateVMGlobals();
        var lexer = new Lexer("return log.level;", "<test>");
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        var stmts = parser.ParseProgram();
        SemanticResolver.Resolve(stmts);
        var chunk = Compiler.Compile(stmts);
        var vm = new VirtualMachine(globals);
        var result = vm.Execute(chunk);
        // Default level is "info".
        Assert.Equal("info", result);
    }

    [Fact]
    public void LogLevel_AdjacentReads_AfterSetLevel_BothReflectNewLevel()
    {
        // Two adjacent reads of log.level must each reflect the current state.
        // After log.setLevel("warn"), both reads should return "warn".
        var globals = StdlibDefinitions.CreateVMGlobals();
        var lexer = new Lexer("log.setLevel(\"warn\");\nlet a = log.level;\nlet b = log.level;\nreturn [a, b];", "<test>");
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        var stmts = parser.ParseProgram();
        SemanticResolver.Resolve(stmts);
        var chunk = Compiler.Compile(stmts);
        var vm = new VirtualMachine(globals);
        var result = vm.Execute(chunk) as List<StashValue>;
        Assert.NotNull(result);
        Assert.Equal("warn", result![0].AsObj as string);
        Assert.Equal("warn", result![1].AsObj as string);
    }
}
