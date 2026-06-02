namespace Stash.Tests.Stdlib;

using System;
using System.IO;
using System.Runtime.InteropServices;
using Stash.Bytecode;
using Stash.Lexing;
using Stash.Parsing;
using Stash.Resolution;
using Stash.Runtime;
using Stash.Runtime.Errors;
using Stash.Stdlib;
using Xunit;

/// <summary>
/// Tests that <c>env.chdir</c>, <c>env.popDir</c>, and <c>env.withDir</c> write to
/// the per-VM <c>WorkingDirectory</c> overlay only.  <c>System.Environment.CurrentDirectory</c>
/// must remain unchanged after any of these calls — the key hermetic isolation invariant
/// for the cwd migration (phase 2B-3, Decision Log "no general write-through in any mode").
/// </summary>
public class CurrentProcessImplTests
{
    private static (Chunk chunk, VirtualMachine vm) BuildVM(string source)
    {
        var lexer = new Lexer(source, "<test>");
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        var stmts = parser.ParseProgram();
        SemanticResolver.Resolve(stmts);
        var chunk = Compiler.Compile(stmts);
        var vm = new VirtualMachine(StdlibDefinitions.CreateVMGlobals());
        vm.EmbeddedMode = true;
        return (chunk, vm);
    }

    private static string CreateTempDir()
    {
        string path = Path.Combine(Path.GetTempPath(), $"stash_cpi_{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    // =========================================================================
    // env.chdir — updates ctx.WorkingDirectory, never System.Environment
    // =========================================================================

    [Fact]
    public void Chdir_UpdatesVmWorkingDirectory()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;

        string dir = CreateTempDir();
        string resolved = Path.GetFullPath(dir);
        try
        {
            var (chunk, vm) = BuildVM($"""env.chdir("{dir}");""");
            vm.Execute(chunk);

            Assert.Equal(resolved, vm.Context.WorkingDirectory);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Chdir_DoesNotMutateRealProcessCwd()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;

        string dir = CreateTempDir();
        string realCwdBefore = System.Environment.CurrentDirectory;
        try
        {
            var (chunk, vm) = BuildVM($"""env.chdir("{dir}");""");
            vm.Execute(chunk);

            // Real process cwd must be unchanged
            Assert.Equal(realCwdBefore, System.Environment.CurrentDirectory);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Chdir_EnvCwdMember_ReturnsVmWorkingDirectory()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;

        string dir = CreateTempDir();
        string resolved = Path.GetFullPath(dir);
        try
        {
            var (chunk, vm) = BuildVM($"""
                env.chdir("{dir}");
                return env.cwd;
                """);
            var result = vm.Execute(chunk);
            Assert.Equal(resolved, result);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Chdir_TwoVms_HaveIndependentWorkingDirectories()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;

        string dir1 = CreateTempDir();
        string dir2 = CreateTempDir();
        try
        {
            var (chunkA, vmA) = BuildVM($"""env.chdir("{dir1}");""");
            vmA.Execute(chunkA);

            var (chunkB, vmB) = BuildVM($"""env.chdir("{dir2}");""");
            vmB.Execute(chunkB);

            // Each VM has its own cwd
            Assert.Equal(Path.GetFullPath(dir1), vmA.Context.WorkingDirectory);
            Assert.Equal(Path.GetFullPath(dir2), vmB.Context.WorkingDirectory);
        }
        finally
        {
            Directory.Delete(dir1, true);
            Directory.Delete(dir2, true);
        }
    }

    // =========================================================================
    // env.popDir — updates ctx.WorkingDirectory, never System.Environment
    // =========================================================================

    [Fact]
    public void PopDir_UpdatesVmWorkingDirectory_NotRealCwd()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;

        string dir = CreateTempDir();
        string realCwdBefore = System.Environment.CurrentDirectory;
        try
        {
            var (chunk, vm) = BuildVM($"""
                env.chdir("{dir}");
                env.popDir();
                """);
            vm.Execute(chunk);

            // Real process cwd must be unchanged
            Assert.Equal(realCwdBefore, System.Environment.CurrentDirectory);
            // VM's cwd should be back to the initial cwd (= realCwdBefore at VM construction)
            Assert.Equal(Path.GetFullPath(realCwdBefore), vm.Context.WorkingDirectory);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    // =========================================================================
    // env.withDir — temporarily changes ctx.WorkingDirectory, restores on exit
    // =========================================================================

    [Fact]
    public void WithDir_DoesNotMutateRealProcessCwd()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;

        string dir = CreateTempDir();
        string realCwdBefore = System.Environment.CurrentDirectory;
        try
        {
            var (chunk, vm) = BuildVM($"""
                env.withDir("{dir}", () => null);
                """);
            vm.Execute(chunk);

            // Real process cwd must be unchanged throughout and after
            Assert.Equal(realCwdBefore, System.Environment.CurrentDirectory);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void WithDir_RestoresVmCwdAfterCallback()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;

        string dir = CreateTempDir();
        try
        {
            var (chunk, vm) = BuildVM($"""
                let before = env.cwd;
                env.withDir("{dir}", () => null);
                return env.cwd == before;
                """);
            var result = vm.Execute(chunk);
            Assert.Equal(true, result);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void WithDir_RestoresVmCwdAfterException()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;

        string dir = CreateTempDir();
        string realCwdBefore = System.Environment.CurrentDirectory;
        try
        {
            var (chunk, vm) = BuildVM($$"""
                let before = env.cwd;
                try {
                    env.withDir("{{dir}}", () => { throw "deliberate"; });
                }
                return env.cwd == before;
                """);
            var result = vm.Execute(chunk);
            Assert.Equal(true, result);
            // Real env also unaffected
            Assert.Equal(realCwdBefore, System.Environment.CurrentDirectory);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    // =========================================================================
    // StashEngine path (CLI-mode EmbeddedMode=true) — done_when #2 second half
    // =========================================================================

    /// <summary>
    /// Verifies the "no general write-through" invariant via the StashEngine API path
    /// (done_when #2 second bullet: "verified in BOTH the default-constructed-VM path AND
    /// the CLI-mode StashEngine path").  <c>StashEngine</c> constructs the VM with
    /// <c>EmbeddedMode=true</c> and shares one VM across successive Execute/Evaluate calls.
    /// </summary>
    [Fact]
    public void StashEngine_Chdir_UpdatesVmCwd_DoesNotMutateRealCwd()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;

        string dir = CreateTempDir();
        string realCwdBefore = System.Environment.CurrentDirectory;
        try
        {
            var engine = new StashEngine();
            // Run env.chdir via StashEngine (same VM persists across calls)
            var runResult = engine.Run($"env.chdir(\"{dir}\");");
            Assert.True(runResult.Success, $"env.chdir failed: {string.Join(", ", runResult.Errors)}");

            // Evaluate env.cwd — must reflect the VM's new cwd
            var evalResult = engine.Evaluate("env.cwd");
            Assert.True(evalResult.Success, $"env.cwd eval failed: {string.Join(", ", evalResult.Errors)}");
            Assert.Equal(Path.GetFullPath(dir), evalResult.Value);

            // Real process cwd must be unchanged
            Assert.Equal(realCwdBefore, System.Environment.CurrentDirectory);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    // =========================================================================
    // Two-engine cwd isolation — done_when 2B-4 item 4
    // =========================================================================

    /// <summary>
    /// Two independent <see cref="StashEngine"/> instances must have independent per-VM
    /// cwds.  Calling <c>env.chdir</c> in engine A must not affect engine B's cwd,
    /// and <c>path.abs(".")</c> (which resolves via <c>ctx.ResolveAgainstCwd</c>)
    /// must return different directories in each engine after the chdir.
    /// The real <c>System.Environment.CurrentDirectory</c> must also remain unchanged
    /// (no write-through in any mode).
    /// </summary>
    [Fact]
    public void TwoEngines_Chdir_InEngineA_DoesNotAffectEngineB()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;

        string dirA = CreateTempDir();
        string realCwdBefore = System.Environment.CurrentDirectory;
        try
        {
            var engineA = new StashEngine();
            var engineB = new StashEngine();

            // Engine A changes its VM cwd to dirA
            var chdirResult = engineA.Run($"env.chdir(\"{dirA}\");");
            Assert.True(chdirResult.Success, $"engineA env.chdir failed: {string.Join(", ", chdirResult.Errors)}");

            // Engine A should see dirA via path.abs(".")
            var absA = engineA.Evaluate("path.abs(\".\")");
            Assert.True(absA.Success, $"engineA path.abs failed: {string.Join(", ", absA.Errors)}");
            Assert.Equal(Path.GetFullPath(dirA), absA.Value as string);

            // Engine B should still see the original process cwd (not dirA)
            var absB = engineB.Evaluate("path.abs(\".\")");
            Assert.True(absB.Success, $"engineB path.abs failed: {string.Join(", ", absB.Errors)}");
            Assert.Equal(realCwdBefore, absB.Value as string);

            // The two engines must see different cwds
            Assert.NotEqual(absA.Value as string, absB.Value as string);

            // The real process cwd must be unchanged
            Assert.Equal(realCwdBefore, System.Environment.CurrentDirectory);
        }
        finally
        {
            Directory.Delete(dirA, true);
        }
    }
}
