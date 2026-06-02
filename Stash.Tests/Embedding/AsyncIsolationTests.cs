using System;
using System.Text;
using Stash.Bytecode;
using Stash.Lexing;
using Stash.Parsing;
using Stash.Resolution;
using Stash.Runtime;
using Stash.Runtime.Types;
using Stash.Stdlib;
using Xunit;

namespace Stash.Tests.Embedding;

/// <summary>
/// Stress tests for async-child isolation — 100 concurrent tasks each mutating a
/// captured non-frozen (or frozen) dict.
///
/// done_when coverage:
///   #3 — AsyncRaceStress_NonFrozenDictionary_IsCallLocal
///   #4 — AsyncRaceStress_FrozenDictionary_ThrowsOnWrite
/// </summary>
public class AsyncIsolationTests
{
    // ── helpers ───────────────────────────────────────────────────────────────

    private static (Chunk chunk, VirtualMachine vm) CompileToVM(string source)
    {
        var lexer = new Lexer(source, "<test>");
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        var stmts = parser.ParseProgram();
        SemanticResolver.Resolve(stmts);
        var chunk = Compiler.Compile(stmts);
        var vm = new VirtualMachine(StdlibDefinitions.CreateVMGlobals());
        return (chunk, vm);
    }

    // ── #3: non-frozen dict — mutations are call-local ────────────────────────

    /// <summary>
    /// 100 concurrent async tasks each write to a captured non-frozen dict.
    /// After task.all:
    ///   - The parent's dict must be unchanged (writes are call-local to each child).
    ///   - No Dictionary corruption — no InvalidOperationException from concurrent
    ///     modification (which would surface as a RuntimeError wrapping the exception,
    ///     or as a swallowed write visible via a wrong count).
    /// </summary>
    [Fact]
    public void AsyncRaceStress_NonFrozenDictionary_IsCallLocal()
    {
        // Build a script that spawns 100 tasks, each writing a unique key to a cloned dict.
        // Parent dict must remain empty after task.all.
        var sb = new StringBuilder();
        sb.AppendLine("let d = {};");
        sb.AppendLine("async fn writeKey(k) {");
        sb.AppendLine("    d[k] = 1;");
        sb.AppendLine("}");
        sb.AppendLine("let futures = [];");
        sb.AppendLine("let i = 0;");
        sb.AppendLine("while (i < 100) {");
        sb.AppendLine("    futures.push(writeKey(conv.toStr(i)));");
        sb.AppendLine("    i = i + 1;");
        sb.AppendLine("}");
        sb.AppendLine("await task.all(futures);");
        // The parent's dict should still be empty — each child got its own clone.
        sb.AppendLine("let result = len(dict.keys(d));");

        var (chunk, vm) = CompileToVM(sb.ToString() + "\nreturn result;");
        object? result = vm.Execute(chunk);

        // Parent dict must be empty (0 keys written to it).
        Assert.Equal(0L, result);
    }

    /// <summary>
    /// Same as above but we also verify no InvalidOperationException leaks through
    /// (concurrent access to a non-cloned dict would throw this from .NET's Dictionary).
    /// We verify via the parent's dict remaining completely unmodified.
    /// </summary>
    [Fact]
    public void AsyncRaceStress_NonFrozenDictionary_NoCorruption_UnchangedParent()
    {
        // Spawn 100 tasks that each increment a "count" key on their cloned copy.
        // Parent's count must remain 0.
        var sb = new StringBuilder();
        sb.AppendLine("let shared = { count: 0 };");
        sb.AppendLine("async fn increment() {");
        sb.AppendLine("    shared.count = shared.count + 1;");
        sb.AppendLine("}");
        sb.AppendLine("let futures = [];");
        sb.AppendLine("let i = 0;");
        sb.AppendLine("while (i < 100) {");
        sb.AppendLine("    futures.push(increment());");
        sb.AppendLine("    i = i + 1;");
        sb.AppendLine("}");
        sb.AppendLine("await task.all(futures);");
        sb.AppendLine("let result = shared.count;");

        var (chunk, vm) = CompileToVM(sb.ToString() + "\nreturn result;");
        object? result = vm.Execute(chunk);

        // Parent's count must be 0 — all 100 children got clones.
        Assert.Equal(0L, result);
    }

    // ── #4: frozen dict — shared by reference; writes throw ReadOnlyError ─────

    /// <summary>
    /// When the dict is frozen before spawn, children share it by reference.
    /// Their writes throw ReadOnlyError; the parent's view is unchanged.
    /// </summary>
    [Fact]
    public void AsyncRaceStress_FrozenDictionary_ThrowsOnWrite()
    {
        // Spawn 100 tasks that each try to write to a frozen dict.
        // Each child catches the ReadOnlyError and records "ReadOnlyError" in a
        // task-local variable — the parent collects the results via task.all.
        var sb = new StringBuilder();
        sb.AppendLine("readonly const frozen = { v: 0 };");
        sb.AppendLine("async fn tryWrite() {");
        sb.AppendLine("    try {");
        sb.AppendLine("        frozen.v = 1;");
        sb.AppendLine("        return \"no-error\";");
        sb.AppendLine("    } catch (ReadOnlyError e) {");
        sb.AppendLine("        return \"ReadOnlyError\";");
        sb.AppendLine("    }");
        sb.AppendLine("}");
        sb.AppendLine("let futures = [];");
        sb.AppendLine("let i = 0;");
        sb.AppendLine("while (i < 100) {");
        sb.AppendLine("    futures.push(tryWrite());");
        sb.AppendLine("    i = i + 1;");
        sb.AppendLine("}");
        sb.AppendLine("let results = await task.all(futures);");
        // Every result must be "ReadOnlyError" — none should be "no-error".
        sb.AppendLine("let allThrew = true;");
        sb.AppendLine("let j = 0;");
        sb.AppendLine("while (j < len(results)) {");
        sb.AppendLine("    if (results[j] != \"ReadOnlyError\") {");
        sb.AppendLine("        allThrew = false;");
        sb.AppendLine("    }");
        sb.AppendLine("    j = j + 1;");
        sb.AppendLine("}");
        sb.AppendLine("let result = allThrew ? \"ok\" : \"fail\";");

        var (chunk, vm) = CompileToVM(sb.ToString() + "\nreturn result;");
        object? result = vm.Execute(chunk);

        Assert.Equal("ok", result);
    }

    /// <summary>
    /// With a frozen dict, children share the same reference (no clone), so the
    /// parent's view is unchanged after task.all — the dict's value remains 0.
    /// </summary>
    [Fact]
    public void AsyncRaceStress_FrozenDictionary_ParentViewUnchanged()
    {
        // Children try to write; parent's frozen.v stays 0.
        var sb = new StringBuilder();
        sb.AppendLine("readonly const frozen = { v: 0 };");
        sb.AppendLine("async fn tryWrite() {");
        sb.AppendLine("    try { frozen.v = 99; } catch (ReadOnlyError e) {}");
        sb.AppendLine("}");
        sb.AppendLine("let futures = [];");
        sb.AppendLine("let i = 0;");
        sb.AppendLine("while (i < 100) {");
        sb.AppendLine("    futures.push(tryWrite());");
        sb.AppendLine("    i = i + 1;");
        sb.AppendLine("}");
        sb.AppendLine("await task.all(futures);");
        sb.AppendLine("let result = frozen.v;");

        var (chunk, vm) = CompileToVM(sb.ToString() + "\nreturn result;");
        object? result = vm.Execute(chunk);

        // Parent's frozen dict is unchanged.
        Assert.Equal(0L, result);
    }
}
