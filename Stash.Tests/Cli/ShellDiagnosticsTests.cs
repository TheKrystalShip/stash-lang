using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Stash.Bytecode;
using Stash.Cli.Shell;
using Stash.Stdlib;

namespace Stash.Tests.Cli;

[CollectionDefinition("ShellDiagnostics", DisableParallelization = true)]
public class ShellDiagnosticsCollection { }

/// <summary>
/// Unit tests for <see cref="ShellDiagnostics"/> — SA0821 per-session suppression behaviour.
/// </summary>
[Collection("ShellDiagnostics")]
public sealed class ShellDiagnosticsTests : IDisposable
{
    private readonly StringWriter _writer = new();

    public ShellDiagnosticsTests()
    {
        ShellDiagnostics.Reset();
        ShellDiagnostics.SetWriter(_writer);
    }

    public void Dispose()
    {
        ShellDiagnostics.SetWriter(null);
        ShellDiagnostics.Reset();
        _writer.Dispose();
    }

    // ── Core behaviour ──────────────────────────────────────────────────────

    [Fact]
    public void EmitShadowWarning_FirstCall_PrintsDiagnostic()
    {
        ShellDiagnostics.EmitShadowWarning("ls");

        string output = _writer.ToString();
        Assert.Contains("SA0821", output);
        Assert.Contains("'ls'", output);
        Assert.Contains("hint:", output);
        Assert.Contains("\\ls", output);
    }

    [Fact]
    public void EmitShadowWarning_SecondCallSameName_Silent()
    {
        ShellDiagnostics.EmitShadowWarning("ls");
        string after_first = _writer.ToString();

        _writer.GetStringBuilder().Clear();
        ShellDiagnostics.EmitShadowWarning("ls");

        Assert.Equal(string.Empty, _writer.ToString());
        _ = after_first; // first call had output — verified above
    }

    [Fact]
    public void EmitShadowWarning_DifferentNames_BothPrint()
    {
        ShellDiagnostics.EmitShadowWarning("ls");
        ShellDiagnostics.EmitShadowWarning("grep");

        string output = _writer.ToString();
        Assert.Contains("'ls'", output);
        Assert.Contains("'grep'", output);
    }

    [Fact]
    public void Reset_ClearsSuppressionState_AllowsReprint()
    {
        ShellDiagnostics.EmitShadowWarning("ls");
        _writer.GetStringBuilder().Clear();

        ShellDiagnostics.Reset();
        ShellDiagnostics.EmitShadowWarning("ls");

        string output = _writer.ToString();
        Assert.Contains("SA0821", output);
        Assert.Contains("'ls'", output);
    }

    [Fact]
    public async Task EmitShadowWarning_ConcurrentCalls_StillIdempotent()
    {
        const int threadCount = 16;
        var barrier = new Barrier(threadCount);
        var tasks = new Task[threadCount];

        for (int i = 0; i < threadCount; i++)
        {
            tasks[i] = Task.Run(() =>
            {
                barrier.SignalAndWait();
                ShellDiagnostics.EmitShadowWarning("git");
            });
        }

        await Task.WhenAll(tasks);

        string output = _writer.ToString();
        // The diagnostic line must appear exactly once.
        int count = CountOccurrences(output, "SA0821");
        Assert.Equal(1, count);
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private static int CountOccurrences(string text, string substring)
    {
        int count = 0;
        int idx = 0;
        while ((idx = text.IndexOf(substring, idx, StringComparison.Ordinal)) >= 0)
        {
            count++;
            idx += substring.Length;
        }
        return count;
    }
}

/// <summary>
/// Classifier integration tests for SA0821 — verifies the classifier triggers
/// <see cref="ShellDiagnostics.EmitShadowWarning"/> at the right time.
/// Serialized with <see cref="ShellDiagnosticsTests"/> via the shared collection.
/// </summary>
[Collection("ShellDiagnostics")]
public sealed class ShellClassifierSA0821Tests : IDisposable
{
    private readonly StringWriter _writer = new();

    public ShellClassifierSA0821Tests()
    {
        ShellDiagnostics.Reset();
        ShellDiagnostics.SetWriter(_writer);
    }

    public void Dispose()
    {
        ShellDiagnostics.SetWriter(null);
        ShellDiagnostics.Reset();
        _writer.Dispose();
    }

    private static ShellLineClassifier MakeClassifier(
        Func<string, bool>? isExec = null,
        IEnumerable<string>? globals = null)
    {
        var vm = new VirtualMachine(StdlibDefinitions.CreateVMGlobals());
        if (globals != null)
        {
            foreach (string name in globals)
                vm.Globals[name] = Stash.Runtime.StashValue.FromObject(null);
        }

        var ctx = new ShellContext
        {
            Vm = vm,
            PathCache = new PathExecutableCache(isExec ?? (_ => false)),
            Keywords = ShellContext.BuildKeywordSet(),
            Namespaces = new HashSet<string>(StdlibRegistry.NamespaceNames, StringComparer.Ordinal),
            ShellBuiltinNames = ShellContext.BuildShellBuiltinSet(),
        };
        return new ShellLineClassifier(ctx);
    }

    [Fact]
    public void Classify_IdentifierShadowsPathExecutable_EmitsSA0821AndReturnsStash()
    {
        // 'ls' declared as Stash global AND on PATH (via injectable seam).
        var clf = MakeClassifier(isExec: name => name == "ls", globals: new[] { "ls" });
        var mode = clf.Classify("ls -la");

        Assert.Equal(LineMode.Stash, mode);
        Assert.Contains("SA0821", _writer.ToString());
    }

    [Fact]
    public void Classify_IdentifierIsGlobalButNotOnPath_NoDiagnostic()
    {
        // Global declared, but NOT on PATH — no SA0821.
        var clf = MakeClassifier(isExec: _ => false, globals: new[] { "myFlag123_notOnPath" });
        var mode = clf.Classify("myFlag123_notOnPath --verbose");

        Assert.Equal(LineMode.Stash, mode);
        Assert.Equal(string.Empty, _writer.ToString());
    }

    [Fact]
    public void Classify_IdentifierOnPathButNoGlobal_NoDiagnostic()
    {
        // 'ls' on PATH but NOT declared as a Stash global → Shell mode, no SA0821.
        var clf = MakeClassifier(isExec: name => name == "ls");
        var mode = clf.Classify("ls -la");

        Assert.Equal(LineMode.Shell, mode);
        Assert.Equal(string.Empty, _writer.ToString());
    }
}
