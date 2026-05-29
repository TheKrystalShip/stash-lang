namespace Stash.Tests.Stdlib;

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Stash.Bytecode;
using Stash.Lexing;
using Stash.Parsing;
using Stash.Resolution;
using Stash.Runtime;
using Stash.Runtime.Errors;
using Stash.Stdlib;
using Stash.Tests.Interpreting;
using Xunit;

/// <summary>
/// Tests for the P5 v1 migration of env.cwd, env.home, env.user, env.hostname
/// as [StashMember] data members. env.os and env.arch were removed in the
/// os-namespace change (P6); use os.name() / os.arch() instead.
/// </summary>
public class EnvMembersTests : StashTestBase
{
    // =========================================================================
    // typeof returns "string" (not "function") for env members
    // =========================================================================

    [Fact]
    public void Cwd_TypeofReturnsString()
    {
        var result = Run("let result = typeof(env.cwd);");
        Assert.Equal("string", result);
    }

    [Fact]
    public void Home_TypeofReturnsString()
    {
        var result = Run("let result = typeof(env.home);");
        Assert.Equal("string", result);
    }

    [Fact]
    public void User_TypeofReturnsString()
    {
        var result = Run("let result = typeof(env.user);");
        Assert.Equal("string", result);
    }

    [Fact]
    public void Hostname_TypeofReturnsString()
    {
        var result = Run("let result = typeof(env.hostname);");
        Assert.Equal("string", result);
    }

    // =========================================================================
    // All env members return non-empty strings
    // =========================================================================

    [Fact]
    public void Cwd_ReturnsNonEmptyString()
    {
        var result = Run("let result = env.cwd;");
        Assert.IsType<string>(result);
        Assert.NotEmpty((string)result!);
    }

    [Fact]
    public void Home_ReturnsNonEmptyString()
    {
        var result = Run("let result = env.home;");
        Assert.IsType<string>(result);
        Assert.NotEmpty((string)result!);
    }

    [Fact]
    public void User_ReturnsNonEmptyString()
    {
        var result = Run("let result = env.user;");
        Assert.IsType<string>(result);
        Assert.NotEmpty((string)result!);
    }

    [Fact]
    public void Hostname_ReturnsNonEmptyString()
    {
        var result = Run("let result = env.hostname;");
        Assert.IsType<string>(result);
        Assert.NotEmpty((string)result!);
    }

    // =========================================================================
    // env.cwd is Live: env.chdir changes what env.cwd returns
    // =========================================================================

    [Fact]
    public void Cwd_Live_ReflectsChangedDirectory()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;

        string originalCwd = System.Environment.CurrentDirectory;
        try
        {
            var result = Run("""
                let before = env.cwd;
                env.chdir("/tmp");
                let after = env.cwd;
                let result = after;
                """);
            Assert.Equal("/tmp", result);
        }
        finally
        {
            System.Environment.CurrentDirectory = originalCwd;
        }
    }

    [Fact]
    public void Cwd_Live_BeforeAndAfterChdirAreDifferent()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;

        string originalCwd = System.Environment.CurrentDirectory;
        try
        {
            var result = Run("""
                let before = env.cwd;
                env.chdir("/tmp");
                let after = env.cwd;
                let result = before != after;
                """);
            // If originalCwd was already /tmp this could be equal; skip rather than fail.
            if (originalCwd != "/tmp")
                Assert.Equal(true, result);
        }
        finally
        {
            System.Environment.CurrentDirectory = originalCwd;
        }
    }

    // =========================================================================
    // env.user / env.hostname capability-gated: absent when Environment denied
    // =========================================================================

    [Fact]
    public void EnvNamespace_UnavailableWithoutEnvironmentCapability_RaisesError()
    {
        // When StashCapabilities.Environment is excluded, the env namespace is not
        // registered. Accessing env.user should raise a RuntimeError about a missing
        // namespace or undefined name — the "standard capability error" path.
        var source = "let result = env.user;";
        var (chunk, vm) = CompileAndBuildVmWithCapabilities(
            source,
            StashCapabilities.All & ~StashCapabilities.Environment);

        Assert.ThrowsAny<RuntimeError>(() => vm.Execute(chunk));
    }

    // =========================================================================
    // SA0846: old call forms are compile-time errors
    // =========================================================================

    [Fact]
    public void Cwd_CallForm_RaisesCompileTimeSA0846()
    {
        var diagnostics = GetCompileDiagnostics("env.cwd();");
        Assert.Contains(diagnostics, d => d.Code == "SA0846");
    }

    [Fact]
    public void Home_CallForm_RaisesCompileTimeSA0846()
    {
        var diagnostics = GetCompileDiagnostics("env.home();");
        Assert.Contains(diagnostics, d => d.Code == "SA0846");
    }

    [Fact]
    public void User_CallForm_RaisesCompileTimeSA0846()
    {
        var diagnostics = GetCompileDiagnostics("env.user();");
        Assert.Contains(diagnostics, d => d.Code == "SA0846");
    }

    [Fact]
    public void Hostname_CallForm_RaisesCompileTimeSA0846()
    {
        var diagnostics = GetCompileDiagnostics("env.hostname();");
        Assert.Contains(diagnostics, d => d.Code == "SA0846");
    }

    // =========================================================================
    // Helpers
    // =========================================================================

    private static (Chunk chunk, VirtualMachine vm) CompileAndBuildVmWithCapabilities(
        string source, StashCapabilities capabilities)
    {
        var lexer = new Lexer(source, "<test>");
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        var stmts = parser.ParseProgram();
        SemanticResolver.Resolve(stmts);
        var chunk = Compiler.Compile(stmts);
        var vm = new VirtualMachine(StdlibDefinitions.CreateVMGlobals(capabilities));
        return (chunk, vm);
    }

    private static List<Stash.Analysis.SemanticDiagnostic> GetCompileDiagnostics(string source)
    {
        var lexer = new Lexer(source, "<test>");
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        var stmts = parser.ParseProgram();
        var collector = new Stash.Analysis.SymbolCollector();
        var scopeTree = collector.Collect(stmts);
        var validator = new Stash.Analysis.SemanticValidator(scopeTree);
        return validator.Validate(stmts);
    }
}
