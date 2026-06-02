using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Stash.Bytecode;
using Stash.Lexing;
using Stash.Parsing;
using Stash.Resolution;
using Stash.Runtime;
using Stash.Runtime.Errors;
using Stash.Stdlib;

namespace Stash.Tests.Stdlib;

/// <summary>
/// Integration tests for the current-process state functions added to the <c>env</c> namespace
/// in Phase A of the Process Namespace Decomposition: <c>env.chdir</c>, <c>env.popDir</c>,
/// <c>env.dirStack</c>, <c>env.dirStackDepth</c>, <c>env.withDir</c>, and <c>env.exit</c>.
/// </summary>
[Collection("SystemCwdTests")]
public class EnvCurrentProcessTests : Stash.Tests.Interpreting.StashTestBase
{
    private static string CreateTempDir()
    {
        string path = Path.Combine(Path.GetTempPath(), $"stash_env_{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

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

    // =========================================================================
    // env.chdir
    // =========================================================================

    [Fact]
    public void Env_Chdir_ChangesDirectory()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;

        string dir = CreateTempDir();
        string resolved = Path.GetFullPath(dir);
        try
        {
            var (chunk, vm) = BuildVM($$"""
                env.chdir("{{dir}}");
                return env.cwd;
                """);
            var result = vm.Execute(chunk);
            Assert.Equal(resolved, result);
            // env.chdir no longer mutates real process cwd
            Assert.NotEqual(resolved, System.Environment.CurrentDirectory);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Env_Chdir_NonexistentDirectory_ThrowsCommandError()
    {
        var (chunk, vm) = BuildVM("""
            env.chdir("/this/path/absolutely/does/not/exist/xyz");
            """);
        var ex = Assert.ThrowsAny<CommandError>(() => vm.Execute(chunk));
        Assert.StartsWith("no such directory: ", ex.Message);
    }

    // =========================================================================
    // env.popDir
    // =========================================================================

    [Fact]
    public void Env_PopDir_RestoresPrevious()
    {
        string dir = CreateTempDir();
        string resolved = Path.GetFullPath(dir);
        string realCwdBefore = System.Environment.CurrentDirectory;
        try
        {
            var (chunk, vm) = BuildVM($$"""
                let before = env.cwd;
                env.chdir("{{dir}}");
                let popped = env.popDir();
                let afterPop = env.cwd;
                return [popped, afterPop == before];
                """);
            var rawResult = vm.Execute(chunk) as List<StashValue>;
            var items = rawResult!.Select(sv => sv.ToObject()).ToList();
            // popped value should be the dir we pushed
            Assert.Equal(resolved, items[0]);
            // env.cwd after popDir should equal the value before chdir
            Assert.Equal(true, items[1]);
            // Real process cwd must be unchanged
            Assert.Equal(realCwdBefore, System.Environment.CurrentDirectory);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Env_PopDir_AtRoot_ThrowsCommandError()
    {
        var (chunk, vm) = BuildVM("""
            env.popDir();
            """);
        var ex = Assert.ThrowsAny<CommandError>(() => vm.Execute(chunk));
        Assert.Contains("root", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    // =========================================================================
    // env.dirStack
    // =========================================================================

    [Fact]
    public void Env_DirStack_ReturnsCopy()
    {
        string dir = CreateTempDir();
        try
        {
            var (chunk, vm) = BuildVM($$"""
                env.chdir("{{dir}}");
                let s = env.dirStack();
                return s.length;
                """);
            var result = vm.Execute(chunk);
            // initial cwd (1) + dir push (2)
            Assert.Equal(2L, result);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    // =========================================================================
    // env.dirStackDepth
    // =========================================================================

    [Fact]
    public void Env_DirStackDepth_ReturnsCount()
    {
        string dir = CreateTempDir();
        try
        {
            var (chunk, vm) = BuildVM($$"""
                let d0 = env.dirStackDepth();
                env.chdir("{{dir}}");
                let d1 = env.dirStackDepth();
                return [d0, d1];
                """);
            var result = Normalize(vm.Execute(chunk)) as System.Collections.Generic.List<object?>;
            Assert.NotNull(result);
            Assert.Equal(2, result!.Count);
            Assert.Equal(1L, result[0]);
            Assert.Equal(2L, result[1]);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    // =========================================================================
    // env.withDir
    // =========================================================================

    [Fact]
    public void Env_WithDir_RestoresOnReturn()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;

        string dir = CreateTempDir();
        try
        {
            var (chunk, vm) = BuildVM($$"""
                let before = env.cwd;
                env.withDir("{{dir}}", () => null);
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
    public void Env_WithDir_RestoresOnException()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;

        string dir = CreateTempDir();
        try
        {
            var (chunk, vm) = BuildVM($$"""
                let before = env.cwd;
                try {
                    env.withDir("{{dir}}", () => {
                        throw "deliberate";
                    });
                }
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

    // =========================================================================
    // env.exit
    // =========================================================================

    [Fact]
    public void Env_Exit_RunsDeferBlocks()
    {
        string markerFile = Path.Combine(Path.GetTempPath(), $"stash_env_exit_{Guid.NewGuid():N}.txt");
        try
        {
            var (chunk, vm) = BuildVM($$"""
                defer fs.writeFile("{{markerFile}}", "deferred");
                env.exit(0);
                """);
            var ex = Assert.Throws<ExitException>(() => vm.Execute(chunk));
            Assert.Equal(0, ex.ExitCode);
            Assert.True(File.Exists(markerFile), "Defer should have written the marker file before exit.");
            Assert.Equal("deferred", File.ReadAllText(markerFile));
        }
        finally
        {
            File.Delete(markerFile);
        }
    }
}
