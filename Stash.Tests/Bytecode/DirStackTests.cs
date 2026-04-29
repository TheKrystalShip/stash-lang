using System;
using System.IO;
using Stash.Bytecode;
using Stash.Lexing;
using Stash.Parsing;
using Stash.Resolution;
using Stash.Runtime;
using Stash.Stdlib;

namespace Stash.Tests.Bytecode;

/// <summary>
/// Integration tests for the <c>process.chdir</c> directory stack and related functions
/// (<c>process.popDir</c>, <c>process.dirStack</c>, <c>process.dirStackDepth</c>).
/// </summary>
public class DirStackTests : Stash.Tests.Interpreting.StashTestBase
{
    private static string GetInitialCwd() => System.Environment.CurrentDirectory;

    private static string CreateTempDir()
    {
        string path = Path.Combine(Path.GetTempPath(), $"stash_dirstack_{Guid.NewGuid():N}");
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
    // 1. process.chdir — push behaviour
    // =========================================================================

    [Fact]
    public void Chdir_PushesOntoStack()
    {
        string dir1 = CreateTempDir();
        string dir2 = CreateTempDir();
        string originalCwd = GetInitialCwd();
        try
        {
            var (chunk, vm) = BuildVM($$"""
                process.chdir("{{dir1}}");
                process.chdir("{{dir2}}");
                let depth = process.dirStackDepth();
                return depth;
                """);
            var sw = new StringWriter();
            vm.Output = sw;
            var result = vm.Execute(chunk);
            // Initial cwd (1) + dir1 (2) + dir2 (3) = 3
            Assert.Equal(3L, result);
        }
        finally
        {
            System.Environment.CurrentDirectory = originalCwd;
            Directory.Delete(dir1, true);
            Directory.Delete(dir2, true);
        }
    }

    [Fact]
    public void Chdir_AffectsCwd()
    {
        string dir = CreateTempDir();
        string originalCwd = GetInitialCwd();
        // Use resolved path for comparison
        string resolvedDir = Path.GetFullPath(dir);
        try
        {
            var (chunk, vm) = BuildVM($$"""
                process.chdir("{{dir}}");
                return env.cwd();
                """);
            var result = vm.Execute(chunk);
            Assert.Equal(resolvedDir, result);
        }
        finally
        {
            System.Environment.CurrentDirectory = originalCwd;
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Chdir_NonexistentDir_ThrowsIOError()
    {
        string originalCwd = GetInitialCwd();
        try
        {
            var (chunk, vm) = BuildVM("""
                process.chdir("/this/path/does/not/exist/at/all");
                """);
            var ex = Assert.Throws<RuntimeError>(() => vm.Execute(chunk));
            Assert.Equal(StashErrorTypes.IOError, ex.ErrorType);
        }
        finally
        {
            System.Environment.CurrentDirectory = originalCwd;
        }
    }

    // =========================================================================
    // 2. process.popDir
    // =========================================================================

    [Fact]
    public void PopDir_ReturnsAndPopsTop()
    {
        string dir1 = CreateTempDir();
        string dir2 = CreateTempDir();
        string originalCwd = GetInitialCwd();
        string resolvedDir2 = Path.GetFullPath(dir2);
        try
        {
            var (chunk, vm) = BuildVM($$"""
                process.chdir("{{dir1}}");
                process.chdir("{{dir2}}");
                let popped = process.popDir();
                return popped;
                """);
            var result = vm.Execute(chunk);
            Assert.Equal(resolvedDir2, result);
        }
        finally
        {
            System.Environment.CurrentDirectory = originalCwd;
            Directory.Delete(dir1, true);
            Directory.Delete(dir2, true);
        }
    }

    [Fact]
    public void PopDir_RestoresCwdToPreviousLevel()
    {
        string dir1 = CreateTempDir();
        string dir2 = CreateTempDir();
        string originalCwd = GetInitialCwd();
        string resolvedDir1 = Path.GetFullPath(dir1);
        try
        {
            var (chunk, vm) = BuildVM($$"""
                process.chdir("{{dir1}}");
                process.chdir("{{dir2}}");
                process.popDir();
                return env.cwd();
                """);
            var result = vm.Execute(chunk);
            Assert.Equal(resolvedDir1, result);
        }
        finally
        {
            System.Environment.CurrentDirectory = originalCwd;
            Directory.Delete(dir1, true);
            Directory.Delete(dir2, true);
        }
    }

    [Fact]
    public void PopDir_EmptyStack_ThrowsCommandError()
    {
        string originalCwd = GetInitialCwd();
        try
        {
            // Stack starts with 1 entry (initial cwd) — popDir should throw
            var (chunk, vm) = BuildVM("""
                process.popDir();
                """);
            var ex = Assert.Throws<RuntimeError>(() => vm.Execute(chunk));
            Assert.Equal(StashErrorTypes.CommandError, ex.ErrorType);
            Assert.Contains("root", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            System.Environment.CurrentDirectory = originalCwd;
        }
    }

    // =========================================================================
    // 3. process.dirStack
    // =========================================================================

    [Fact]
    public void DirStack_Empty_ReturnsInitialEntry()
    {
        // Stack is never "empty" — it always has the initial cwd entry.
        string originalCwd = GetInitialCwd();
        try
        {
            var (chunk, vm) = BuildVM("""
                let s = process.dirStack();
                return s.length;
                """);
            var result = vm.Execute(chunk);
            Assert.Equal(1L, result);
        }
        finally
        {
            System.Environment.CurrentDirectory = originalCwd;
        }
    }

    [Fact]
    public void DirStack_AfterMultiplePushes_OldestFirst()
    {
        string dir1 = CreateTempDir();
        string dir2 = CreateTempDir();
        string originalCwd = GetInitialCwd();
        string resolvedDir1 = Path.GetFullPath(dir1);
        string resolvedDir2 = Path.GetFullPath(dir2);
        try
        {
            var (chunk, vm) = BuildVM($$"""
                process.chdir("{{dir1}}");
                process.chdir("{{dir2}}");
                let s = process.dirStack();
                // index 0 = original, 1 = dir1, 2 = dir2 (oldest first)
                return s[1];
                """);
            var result = vm.Execute(chunk);
            Assert.Equal(resolvedDir1, result);
        }
        finally
        {
            System.Environment.CurrentDirectory = originalCwd;
            Directory.Delete(dir1, true);
            Directory.Delete(dir2, true);
        }
    }

    // =========================================================================
    // 4. process.dirStackDepth
    // =========================================================================

    [Fact]
    public void DirStackDepth_TracksPushPop()
    {
        string dir = CreateTempDir();
        string originalCwd = GetInitialCwd();
        try
        {
            var (chunk, vm) = BuildVM($$"""
                let d0 = process.dirStackDepth();
                process.chdir("{{dir}}");
                let d1 = process.dirStackDepth();
                process.popDir();
                let d2 = process.dirStackDepth();
                return [d0, d1, d2];
                """);
            var result = Normalize(vm.Execute(chunk)) as System.Collections.Generic.List<object?>;
            Assert.NotNull(result);
            Assert.Equal(3, result!.Count);
            Assert.Equal(1L, result[0]);
            Assert.Equal(2L, result[1]);
            Assert.Equal(1L, result[2]);
        }
        finally
        {
            System.Environment.CurrentDirectory = originalCwd;
            Directory.Delete(dir, true);
        }
    }

    // =========================================================================
    // 5. Cap-256 behaviour
    // =========================================================================

    [Fact]
    public void DirStack_Cap256_DropsEldestOnOverflow()
    {
        string dir = CreateTempDir();
        string originalCwd = GetInitialCwd();
        try
        {
            // Push 256 additional directories by re-chdiring into the same temp dir.
            // After the 256th push the stack is at capacity (256 entries including initial).
            // One more push must drop the eldest and keep depth at 256.
            var sb = new System.Text.StringBuilder();
            // Build a loop: chdir 257 times (initial cwd + 257 pushes = would be 258, capped to 256)
            sb.AppendLine($$"""let d = "{{dir}}";""");
            for (int i = 0; i < 257; i++)
            {
                sb.AppendLine("process.chdir(d);");
            }
            sb.AppendLine("return process.dirStackDepth();");

            var (chunk, vm) = BuildVM(sb.ToString());
            var result = vm.Execute(chunk);
            Assert.Equal(256L, result);
        }
        finally
        {
            System.Environment.CurrentDirectory = originalCwd;
            Directory.Delete(dir, true);
        }
    }
}
