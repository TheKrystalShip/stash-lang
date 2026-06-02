using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Stash.Bytecode;
using Stash.Lexing;
using Stash.Parsing;
using Stash.Parsing.AST;
using Stash.Resolution;
using Stash.Runtime;
using Xunit;

namespace Stash.Tests.Embedding;

/// <summary>
/// Acceptance suite for multi-engine isolation: per-VM cwd, per-VM env overlay,
/// IC-slot correctness under concurrent load (nested chunk), and process.spawn
/// inheriting the VM's view of cwd.
///
/// done_when coverage:
///   #1 — TwoEngines_EnvChdir_DoesNotAffectOtherEngine
///   #2 — TwoEngines_EnvSet_DoesNotLeakAcrossEngines
///   #5 — ICSlot_NestedChunk_ConcurrentDifferentShapes_NoCorruption
///   #7 — ProcessSpawn_InheritsVMCwd
/// </summary>
public class MultiEngineIsolationTests
{
    // ── compile helper ────────────────────────────────────────────────────────

    /// <summary>
    /// Compiles Stash source to a single <see cref="Chunk"/> that can be shared across
    /// multiple <see cref="VirtualMachine"/> instances to exercise per-VM IC-slot isolation.
    /// </summary>
    internal static Chunk CompileSource(string source)
    {
        var lexer = new Lexer(source, "<test>");
        List<Token> tokens = lexer.ScanTokens();
        List<Stmt> stmts = new Parser(tokens).ParseProgram();
        SemanticResolver.Resolve(stmts);
        return Compiler.Compile(stmts);
    }

    // ── test helpers ──────────────────────────────────────────────────────────

    private static string CreateTempDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static (StashEngine engine, StringWriter output) MakeEngine()
    {
        var sw = new StringWriter { NewLine = "\n" };
        var engine = new StashEngine();
        engine.Output = sw;
        return (engine, sw);
    }

    // ── #1: cwd isolation ────────────────────────────────────────────────────

    /// <summary>
    /// Engine A calls env.chdir; engine B's path.abs(".") still reflects the
    /// original process cwd — and the real System.Environment.CurrentDirectory
    /// is also unchanged.
    /// </summary>
    [Fact]
    public void TwoEngines_EnvChdir_DoesNotAffectOtherEngine()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;

        string dirA = CreateTempDir();
        string origCwd = System.Environment.CurrentDirectory;
        try
        {
            var (engineA, _) = MakeEngine();
            var (engineB, _) = MakeEngine();

            // Engine A chdirs to dirA.
            var chdirResult = engineA.Run($"env.chdir(\"{dirA}\");");
            Assert.True(chdirResult.Success,
                $"engineA env.chdir failed: {string.Join(", ", chdirResult.Errors)}");

            // Engine A sees dirA.
            var cwdA = engineA.Evaluate("path.abs(\".\")");
            Assert.True(cwdA.Success,
                $"engineA path.abs failed: {string.Join(", ", cwdA.Errors)}");
            Assert.Equal(Path.GetFullPath(dirA), cwdA.Value as string);

            // Engine B must still see origCwd.
            var cwdB = engineB.Evaluate("path.abs(\".\")");
            Assert.True(cwdB.Success,
                $"engineB path.abs failed: {string.Join(", ", cwdB.Errors)}");
            Assert.Equal(origCwd, cwdB.Value as string);

            // The two engines must see different cwds.
            Assert.NotEqual(cwdA.Value as string, cwdB.Value as string);

            // The real process cwd must be unchanged (no write-through in any mode).
            Assert.Equal(origCwd, System.Environment.CurrentDirectory);
        }
        finally
        {
            if (Directory.Exists(dirA))
                Directory.Delete(dirA, true);
        }
    }

    // ── #2: env overlay isolation ────────────────────────────────────────────

    /// <summary>
    /// Engine A sets env var X=1 in its overlay; engine B's env.get("X") returns null,
    /// and the real System.Environment does not contain X either.
    /// </summary>
    [Fact]
    public void TwoEngines_EnvSet_DoesNotLeakAcrossEngines()
    {
        // Use a unique variable name to avoid collisions with real env vars.
        string varName = "STASH_HERMETIC_TEST_" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        // Ensure the variable isn't accidentally in the real environment.
        Assert.Null(System.Environment.GetEnvironmentVariable(varName));

        var (engineA, _) = MakeEngine();
        var (engineB, _) = MakeEngine();

        // Engine A sets the variable in its overlay.
        var setResult = engineA.Run($"env.set(\"{varName}\", \"1\");");
        Assert.True(setResult.Success,
            $"engineA env.set failed: {string.Join(", ", setResult.Errors)}");

        // Engine A sees the value.
        var getA = engineA.Evaluate($"env.get(\"{varName}\")");
        Assert.True(getA.Success,
            $"engineA env.get failed: {string.Join(", ", getA.Errors)}");
        Assert.Equal("1", getA.Value as string);

        // Engine B must not see the value (overlay is per-VM).
        var getB = engineB.Evaluate($"env.get(\"{varName}\")");
        Assert.True(getB.Success,
            $"engineB env.get failed: {string.Join(", ", getB.Errors)}");
        Assert.Null(getB.Value);

        // The real process environment must also be untouched.
        Assert.Null(System.Environment.GetEnvironmentVariable(varName));
    }

    // ── #5: IC-slot nested-chunk isolation under concurrent load ─────────────

    /// <summary>
    /// The hot path reads obj.field from inside a NESTED function (closure).
    /// Two raw VMs sharing the same compiled Chunk execute concurrently on
    /// different StashStruct shapes; neither sees a wrong-field read.
    ///
    /// This test has teeth only because it:
    ///   1. Uses two raw VMs sharing ONE Chunk (not two engines with separate compiles).
    ///   2. Reads the field from inside a nested function (not top-level).
    ///   3. Verifies correct values — a wrong-field read would return the wrong integer.
    ///
    /// Two structs BoxA { value, tag } and BoxB { tag, value } have "value" at
    /// different field indices. After both VMs execute, both must return [111, 222].
    /// </summary>
    [Fact]
    public async Task ICSlot_NestedChunk_ConcurrentDifferentShapes_NoCorruption()
    {
        // source: defines two structs where "value" is at DIFFERENT offsets.
        // The IC slot for "value" inside the nested "extract" closure will either
        // hit VM-A's monomorphic guard (BoxA) or VM-B's (BoxB). If they share the
        // same IC array the slower-promoting VM may read the wrong field.
        const string source = """
            struct BoxA { value, tag }
            struct BoxB { tag, value }
            let extract = null;
            extract = (bx) => bx.value;
            let a = null;
            a = BoxA { value: 111, tag: "A" };
            let b2 = null;
            b2 = BoxB { tag: "B", value: 222 };
            let rA = null;
            rA = extract(a);
            let rB = null;
            rB = extract(b2);
            return [rA, rB];
            """;

        Chunk chunk = CompileSource(source);

        object? resultA = null, resultB = null;
        var t1 = Task.Run(() => { var vm = new VirtualMachine(); resultA = vm.Execute(chunk); });
        var t2 = Task.Run(() => { var vm = new VirtualMachine(); resultB = vm.Execute(chunk); });
        await Task.WhenAll(t1, t2);

        // Both should return [111, 222] — no wrong-field reads.
        var listA = Assert.IsAssignableFrom<List<StashValue>>(resultA);
        var listB = Assert.IsAssignableFrom<List<StashValue>>(resultB);

        Assert.Equal(111L, listA[0].ToObject());
        Assert.Equal(222L, listA[1].ToObject());
        Assert.Equal(111L, listB[0].ToObject());
        Assert.Equal(222L, listB[1].ToObject());
    }

    // ── #7: process.spawn inherits VM cwd ────────────────────────────────────

    /// <summary>
    /// In engineA, env.chdir("/tmp") then process.spawn("/bin/pwd") must report /tmp.
    /// In engineB (no chdir), process.spawn("/bin/pwd") reports the original cwd.
    /// The two engines run concurrently to prove independence.
    /// </summary>
    [Fact]
    public async Task ProcessSpawn_InheritsVMCwd()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux) &&
            !RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) return;

        string origCwd = System.Environment.CurrentDirectory;
        string dirA = CreateTempDir();
        // Resolve dirA to the real canonical path (/tmp may be a symlink).
        string realDirA = Path.GetFullPath(dirA);
        // On macOS /tmp is a symlink to /private/tmp — use the resolved path for comparison.
        try
        {
            realDirA = new System.IO.DirectoryInfo(dirA).FullName;
        }
        catch { /* keep dirA as-is */ }

        try
        {
            var (engineA, _) = MakeEngine();
            var (engineB, _) = MakeEngine();

            // Engine A changes cwd.
            var chdirResult = engineA.Run($"env.chdir(\"{dirA}\");");
            Assert.True(chdirResult.Success,
                $"engineA env.chdir failed: {string.Join(", ", chdirResult.Errors)}");

            // Run both spawns concurrently.
            // Use Run + GetGlobal because spawn/wait are statements, not a single expression.
            object? stdoutA = null, stdoutB = null;
            const string spawnScript =
                "let h = process.spawn(\"/bin/pwd\");\n" +
                "let r = process.wait(h);\n" +
                "let spawnCwd = str.trim(r.stdout);";

            var tA = Task.Run(() =>
            {
                var r = engineA.Run(spawnScript);
                if (r.Success) stdoutA = engineA.GetGlobal("spawnCwd");
            });
            var tB = Task.Run(() =>
            {
                var r = engineB.Run(spawnScript);
                if (r.Success) stdoutB = engineB.GetGlobal("spawnCwd");
            });
            await Task.WhenAll(tA, tB);

            Assert.NotNull(stdoutA);
            Assert.NotNull(stdoutB);

            // Engine A's spawn reports dirA.
            // On some systems (macOS) /tmp resolves to /private/tmp — canonicalize both sides.
            string actualA = (stdoutA as string)!;
            string actualB = (stdoutB as string)!;

            // dirA canonical check: the reported path must equal dirA's real path.
            Assert.Equal(realDirA, Path.GetFullPath(actualA));

            // Engine B's spawn reports the original cwd (unchanged).
            Assert.Equal(Path.GetFullPath(origCwd), Path.GetFullPath(actualB));
        }
        finally
        {
            if (Directory.Exists(dirA))
                Directory.Delete(dirA, true);
        }
    }
}
