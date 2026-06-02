namespace Stash.Tests.Stdlib;

using System;
using System.IO;
using Stash.Bytecode;
using Stash.Lexing;
using Stash.Parsing;
using Stash.Resolution;
using Stash.Runtime;
using Stash.Stdlib;
using Xunit;

/// <summary>
/// Tests that <c>env.set</c>, <c>env.get</c>, <c>env.has</c>, <c>env.unset</c>,
/// <c>env.all</c>, <c>env.withPrefix</c>, <c>env.loadFile</c>, and <c>env.saveFile</c>
/// read and write the per-VM overlay only — the real <c>System.Environment</c> is never
/// mutated.  This is the key hermetic-isolation invariant for env-namespace functions
/// (migrated in phase 2B-3).
/// </summary>
public class EnvBuiltInsTests
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

    // =========================================================================
    // env.set — writes to VM overlay only, never to System.Environment
    // =========================================================================

    [Fact]
    public void Set_StoresInVmOverlay_NotRealEnv()
    {
        string varName = "STASH_TEST_HERMETIC_SET_" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        // Ensure not set in real env before test
        System.Environment.SetEnvironmentVariable(varName, null);
        try
        {
            var (chunk, vm) = BuildVM($"""env.set("{varName}", "hermetic_value");""");
            vm.Execute(chunk);

            // VM overlay must have the value
            Assert.Equal("hermetic_value", vm.Context.GetEnv(varName));
            // Real process env must NOT be mutated
            Assert.Null(System.Environment.GetEnvironmentVariable(varName));
        }
        finally
        {
            System.Environment.SetEnvironmentVariable(varName, null);
        }
    }

    // =========================================================================
    // env.get — reads from VM overlay, falling back to real env
    // =========================================================================

    [Fact]
    public void Get_OverlayWinsOverRealEnv()
    {
        string varName = "STASH_TEST_HERMETIC_GET_" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        System.Environment.SetEnvironmentVariable(varName, "real_value");
        try
        {
            var (chunk, vm) = BuildVM($"""
                env.set("{varName}", "overlay_value");
                return env.get("{varName}");
                """);
            var result = vm.Execute(chunk);
            // Overlay wins
            Assert.Equal("overlay_value", result);
        }
        finally
        {
            System.Environment.SetEnvironmentVariable(varName, null);
        }
    }

    [Fact]
    public void Get_FallsBackToRealEnv_WhenNotInOverlay()
    {
        string varName = "STASH_TEST_HERMETIC_FALLBACK_" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        System.Environment.SetEnvironmentVariable(varName, "from_real_env");
        try
        {
            var (chunk, vm) = BuildVM($"return env.get(\"{varName}\");");
            var result = vm.Execute(chunk);
            Assert.Equal("from_real_env", result);
        }
        finally
        {
            System.Environment.SetEnvironmentVariable(varName, null);
        }
    }

    // =========================================================================
    // env.unset — marks as explicitly unset in overlay, never touches real env
    // =========================================================================

    [Fact]
    public void Unset_ShadowsRealEnv_AndDoesNotMutateIt()
    {
        string varName = "STASH_TEST_HERMETIC_UNSET_" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        System.Environment.SetEnvironmentVariable(varName, "real_value");
        try
        {
            var (chunk, vm) = BuildVM($"""
                env.unset("{varName}");
                return env.get("{varName}");
                """);
            var result = vm.Execute(chunk);
            // env.get sees the explicit-unset, returns null
            Assert.Null(result);
            // Real env is unchanged
            Assert.Equal("real_value", System.Environment.GetEnvironmentVariable(varName));
        }
        finally
        {
            System.Environment.SetEnvironmentVariable(varName, null);
        }
    }

    // =========================================================================
    // env.has — reads from overlay
    // =========================================================================

    [Fact]
    public void Has_ReturnsTrueAfterSet()
    {
        var (chunk, vm) = BuildVM("""
            env.set("STASH_HAS_TEST_X", "v");
            return env.has("STASH_HAS_TEST_X");
            """);
        var result = vm.Execute(chunk);
        Assert.Equal(true, result);
    }

    [Fact]
    public void Has_ReturnsFalseAfterUnset_EvenIfRealEnvHasIt()
    {
        string varName = "STASH_TEST_HERMETIC_HAS_" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        System.Environment.SetEnvironmentVariable(varName, "real");
        try
        {
            var (chunk, vm) = BuildVM($"""
                env.unset("{varName}");
                return env.has("{varName}");
                """);
            var result = vm.Execute(chunk);
            // Explicit unset shadows the real env entry
            Assert.Equal(false, result);
        }
        finally
        {
            System.Environment.SetEnvironmentVariable(varName, null);
        }
    }

    // =========================================================================
    // env.all — returns merged overlay+real view; overlay wins
    // =========================================================================

    [Fact]
    public void All_OverlayEntriesAppear()
    {
        var (chunk, vm) = BuildVM("""
            env.set("STASH_ALL_UNIQUE_HERMETIC_1", "v1");
            let d = env.all();
            return d["STASH_ALL_UNIQUE_HERMETIC_1"];
            """);
        var result = vm.Execute(chunk);
        Assert.Equal("v1", result);
    }

    [Fact]
    public void All_ExplicitlyUnsetKeysExcluded()
    {
        string varName = "STASH_TEST_ALL_UNSET_" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        System.Environment.SetEnvironmentVariable(varName, "will_be_unset");
        try
        {
            var (chunk, vm) = BuildVM($"""
                env.unset("{varName}");
                let d = env.all();
                return dict.has(d, "{varName}");
                """);
            var result = vm.Execute(chunk);
            Assert.Equal(false, result);
        }
        finally
        {
            System.Environment.SetEnvironmentVariable(varName, null);
        }
    }

    // =========================================================================
    // env.withPrefix — uses overlay
    // =========================================================================

    [Fact]
    public void WithPrefix_ReturnsOverlayEntries()
    {
        var (chunk, vm) = BuildVM("""
            env.set("STASH_WPX_KEY1", "val1");
            env.set("STASH_WPX_KEY2", "val2");
            env.set("STASH_OTHER_KEY", "nope");
            let d = env.withPrefix("STASH_WPX_");
            return dict.size(d);
            """);
        var result = vm.Execute(chunk);
        // At least 2 entries with the prefix
        Assert.True((long)result! >= 2L);
    }

    // =========================================================================
    // env.loadFile — writes to VM overlay, NOT to System.Environment
    // =========================================================================

    [Fact]
    public void LoadFile_StoresInVmOverlay_NotRealEnv()
    {
        string varName = "STASH_TEST_LF_HERMETIC_" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        System.Environment.SetEnvironmentVariable(varName, null);
        string tmp = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tmp, $"{varName}=loaded_value\n");
            var (chunk, vm) = BuildVM($"env.loadFile(\"{tmp.Replace("\\", "\\\\")}\");");
            vm.Execute(chunk);

            // VM overlay must have the value
            Assert.Equal("loaded_value", vm.Context.GetEnv(varName));
            // Real process env must NOT be mutated
            Assert.Null(System.Environment.GetEnvironmentVariable(varName));
        }
        finally
        {
            System.Environment.SetEnvironmentVariable(varName, null);
            File.Delete(tmp);
        }
    }

    [Fact]
    public void LoadFile_WithPrefix_StoresInVmOverlay()
    {
        string tmp = Path.GetTempFileName();
        string varBase = "STASH_TEST_LF_PFX_" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        try
        {
            File.WriteAllText(tmp, "HOST=myhost\n");
            var (chunk, vm) = BuildVM($"""
                env.loadFile("{tmp.Replace("\\", "\\\\")}", "{varBase}_");
                return env.get("{varBase}_HOST");
                """);
            var result = vm.Execute(chunk);

            Assert.Equal("myhost", result);
            // Real env must not be mutated
            Assert.Null(System.Environment.GetEnvironmentVariable($"{varBase}_HOST"));
        }
        finally
        {
            File.Delete(tmp);
        }
    }

    // =========================================================================
    // env.saveFile — writes the VM's merged env view to disk
    // =========================================================================

    [Fact]
    public void SaveFile_IncludesOverlayEntries()
    {
        string tmp = Path.GetTempFileName();
        string varName = "STASH_SAVE_OVERLAY_" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        try
        {
            var (chunk, vm) = BuildVM($"""
                env.set("{varName}", "overlay_val");
                env.saveFile("{tmp.Replace("\\", "\\\\")}");
                """);
            vm.Execute(chunk);

            string content = File.ReadAllText(tmp);
            Assert.Contains($"{varName}=overlay_val", content);
            // Real env must not be mutated
            Assert.Null(System.Environment.GetEnvironmentVariable(varName));
        }
        finally
        {
            File.Delete(tmp);
        }
    }

    // =========================================================================
    // Two independent VMs have independent overlays
    // =========================================================================

    [Fact]
    public void TwoVms_HaveIndependentEnvOverlays()
    {
        string varName = "STASH_TEST_ISOLATION_" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        System.Environment.SetEnvironmentVariable(varName, null);
        try
        {
            var (chunkA, vmA) = BuildVM($"""env.set("{varName}", "vmA_val");""");
            vmA.Execute(chunkA);

            var (chunkB, vmB) = BuildVM($"return env.get(\"{varName}\");");
            var resultB = vmB.Execute(chunkB);

            // vmA's set must not be visible in vmB
            Assert.Null(resultB);
            // Real env also unaffected
            Assert.Null(System.Environment.GetEnvironmentVariable(varName));
        }
        finally
        {
            System.Environment.SetEnvironmentVariable(varName, null);
        }
    }
}
