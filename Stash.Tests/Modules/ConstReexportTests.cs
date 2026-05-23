namespace Stash.Tests.Modules;

using System.Collections.Generic;
using System.Linq;
using Stash.Analysis;
using Stash.Bytecode;
using Stash.Core.Resolution;
using Stash.Lexing;
using Stash.Parsing;
using Stash.Resolution;
using Stash.Runtime;
using Stash.Runtime.Errors;
using Stash.Runtime.Types;
using Stash.Stdlib;
using Stash.Tests.Analysis;

/// <summary>
/// Tests for P4 — user-module const enforcement via SA0845 (static) and ReadOnlyError (runtime).
/// Verifies that assignment to a module-aliased export is rejected statically and at runtime,
/// while assignment to a non-namespace variable's field is not affected.
/// </summary>
public class ConstReexportTests : AnalysisTestBase
{
    // =========================================================================
    // Helpers
    // =========================================================================

    /// <summary>
    /// Compiles <paramref name="moduleSource"/> with full export metadata so the
    /// module chunk carries a populated <see cref="ModuleExports"/> record.
    /// </summary>
    private static Chunk CompileModule(string moduleSource)
    {
        var tokens = new Lexer(moduleSource, "<mod>").ScanTokens();
        var stmts = new Parser(tokens).ParseProgram();
        SemanticResolver.Resolve(stmts);
        var diagnostics = new List<SemanticDiagnostic>();
        var exports = ModuleExportsBuilder.Build(stmts, diagnostics);
        return Compiler.Compile(stmts, exports: exports);
    }

    /// <summary>
    /// Compiles <paramref name="mainSource"/> and executes it inside a VM that resolves
    /// any module import to <paramref name="moduleChunk"/>.
    /// </summary>
    private static RuntimeError RunWithModuleExpectingError(Chunk moduleChunk, string mainSource)
    {
        var mainTokens = new Lexer(mainSource, "<main>").ScanTokens();
        var mainStmts = new Parser(mainTokens).ParseProgram();
        SemanticResolver.Resolve(mainStmts);
        var mainChunk = Compiler.Compile(mainStmts);

        var globals = StdlibDefinitions.CreateVMGlobals();
        var vm = new VirtualMachine(globals);
        vm.ModuleLoader = (_, _) => moduleChunk;

        return Assert.ThrowsAny<RuntimeError>(() => vm.Execute(mainChunk));
    }

    // =========================================================================
    // Static SA0845 — user-module alias assignment
    // =========================================================================

    [Fact]
    public void ImportAs_AssignToAliasedField_EmitsSA0845()
    {
        // `import "./mod.stash" as mod; mod.PI = 0;` must produce SA0845
        // because `mod` is an ImportAsStmt alias.
        var source = """
            import "./mod.stash" as mod;
            mod.PI = 0;
            """;
        var diagnostics = Validate(source);
        Assert.Contains(diagnostics, d => d.Code == "SA0845");
    }

    [Fact]
    public void ImportAs_SA0845_MessageContainsQualifiedName()
    {
        var source = """
            import "./mod.stash" as mod;
            mod.PI = 0;
            """;
        var diagnostics = Validate(source);
        var d = diagnostics.FirstOrDefault(d => d.Code == "SA0845");
        Assert.NotNull(d);
        Assert.Contains("mod.PI", d!.Message);
    }

    // =========================================================================
    // Negative case — non-namespace variable dot-assignment must not fire
    // =========================================================================

    [Fact]
    public void NonImportAlias_DotAssign_DoesNotEmitSA0845()
    {
        // A locally-declared variable's field assignment is valid.
        var source = """
            struct Config { value: int; }
            let cfg = Config { value: 1 };
            cfg.value = 2;
            """;
        var diagnostics = Validate(source);
        Assert.DoesNotContain(diagnostics, d => d.Code == "SA0845");
    }

    [Fact]
    public void ImportAs_ReadingExport_DoesNotEmitSA0845()
    {
        // Reading a module alias field is fine (not an assignment).
        var source = """
            import "./mod.stash" as mod;
            let x = mod.PI;
            """;
        var diagnostics = Validate(source);
        Assert.DoesNotContain(diagnostics, d => d.Code == "SA0845");
    }

    // =========================================================================
    // Runtime enforcement — dynamic receiver path raises ReadOnlyError
    // =========================================================================

    [Fact]
    public void DynamicAssignToModuleAlias_RaisesReadOnlyError()
    {
        // Module exports a const PI.
        Chunk moduleChunk = CompileModule("""
            export const PI = 3.14;
            """);

        // The main script uses a dynamic receiver to escape static SA0845 detection.
        // At runtime, the VM must raise ReadOnlyError when setting a field on the
        // namespace-alias StashNamespace.
        string mainSource = """
            import "./mod.stash" as mod;
            let alias = mod;
            alias.PI = 0;
            """;

        RuntimeError error = RunWithModuleExpectingError(moduleChunk, mainSource);
        Assert.IsType<ReadOnlyError>(error);
    }

    [Fact]
    public void DynamicAssignToModuleAlias_ErrorMessage_MentionsNamespace()
    {
        Chunk moduleChunk = CompileModule("""
            export const PI = 3.14;
            """);

        string mainSource = """
            import "./mod.stash" as mod;
            let alias = mod;
            alias.PI = 0;
            """;

        RuntimeError error = RunWithModuleExpectingError(moduleChunk, mainSource);
        Assert.IsType<ReadOnlyError>(error);
        // The error message should mention the namespace name.
        Assert.Contains("mod", error.Message);
    }
}
