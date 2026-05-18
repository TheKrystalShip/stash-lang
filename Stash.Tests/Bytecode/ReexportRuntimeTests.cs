using System.Collections.Generic;
using System.Linq;
using Stash.Analysis;
using Stash.Bytecode;
using Stash.Core.Resolution;
using Stash.Lexing;
using Stash.Parsing;
using Stash.Parsing.AST;
using Stash.Resolution;
using Stash.Runtime;

namespace Stash.Tests.Bytecode;

/// <summary>
/// VM end-to-end tests proving the desugar strategy from phases 2C–2D works
/// for all re-export forms without touching the VM.
/// Covers every done_when bullet from Phase 2E.
/// </summary>
public class ReexportRuntimeTests : BytecodeTestBase
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Compiles <paramref name="source"/> and runs <see cref="ModuleExportsBuilder"/>
    /// so that <c>Chunk.Exports</c> is populated from any export annotations.
    /// </summary>
    private static Chunk CompileWithExports(string source)
    {
        var tokens = new Lexer(source, "<test>").ScanTokens();
        var stmts = new Parser(tokens).ParseProgram();
        SemanticResolver.Resolve(stmts);
        var diagnostics = new List<SemanticDiagnostic>();
        var exports = ModuleExportsBuilder.Build(stmts, diagnostics);
        return Compiler.Compile(stmts, exports: exports);
    }

    /// <summary>
    /// Executes <paramref name="mainSource"/> inside a VM that resolves every module
    /// path to <paramref name="moduleChunk"/> (single-module scenario).
    /// </summary>
    private static object? RunWithModule(Chunk moduleChunk, string mainSource)
    {
        Chunk mainChunk = CompileSource(mainSource);
        var vm = new VirtualMachine();
        vm.ModuleLoader = (_, _) => moduleChunk;
        return vm.Execute(mainChunk);
    }

    /// <summary>
    /// Executes <paramref name="mainSource"/> inside a VM that dispatches module paths
    /// by suffix-matching against <paramref name="modules"/> keys.
    /// The keys are plain file names (e.g. "a.stash") used for suffix matching because
    /// <c>LoadModule</c> resolves paths to absolute paths before calling the loader.
    /// </summary>
    private static object? RunWithModules(Dictionary<string, Chunk> modules, string mainSource)
    {
        Chunk mainChunk = CompileSource(mainSource);
        var vm = new VirtualMachine();
        vm.ModuleLoader = (resolvedPath, _) =>
        {
            // Normalize path separators for comparison
            string normalized = resolvedPath.Replace('\\', '/');
            foreach (KeyValuePair<string, Chunk> kv in modules)
            {
                if (normalized.EndsWith(kv.Key))
                    return kv.Value;
            }
            throw new RuntimeError($"Test: no module registered for path '{resolvedPath}'.", null);
        };
        return vm.Execute(mainChunk);
    }

    // ── 1. Single-level namespace re-export ──────────────────────────────────

    [Fact]
    public void NamespaceReexport_ObservableByImporter_ReturnsFooValue()
    {
        // Module B: defines and exports foo.
        Chunk chunkB = CompileWithExports("""
            export fn foo() { return 42; }
            """);

        // Module A: re-exports B as namespace b.
        Chunk chunkA = CompileWithExports("""
            export "b.stash" as b;
            """);

        var modules = new Dictionary<string, Chunk>
        {
            ["a.stash"] = chunkA,
            ["b.stash"] = chunkB,
        };

        // Main: imports A, accesses B's foo through A's re-export namespace.
        string mainSource = """
            let func = null;
            func = () => {
                import "a.stash" as A;
                return A.b.foo();
            };
            return func();
            """;

        object? result = RunWithModules(modules, mainSource);
        Assert.Equal(42L, result);
    }

    // ── 2. Single-level selective re-export ──────────────────────────────────

    [Fact]
    public void SelectiveReexport_ObservableByImporter_ReturnsFooValue()
    {
        // Module B: defines and exports foo.
        Chunk chunkB = CompileWithExports("""
            export fn foo() { return 99; }
            """);

        // Module A: re-exports foo from B selectively.
        Chunk chunkA = CompileWithExports("""
            export { foo } from "b.stash";
            """);

        var modules = new Dictionary<string, Chunk>
        {
            ["a.stash"] = chunkA,
            ["b.stash"] = chunkB,
        };

        // Main: imports foo from A.
        string mainSource = """
            let func = null;
            func = () => {
                import { foo } from "a.stash";
                return foo();
            };
            return func();
            """;

        object? result = RunWithModules(modules, mainSource);
        Assert.Equal(99L, result);
    }

    // ── 3. Three-level transitive re-export chain ─────────────────────────────

    [Fact]
    public void TransitiveReexport_ThreeLevels_ImporiterSeesOriginalValue()
    {
        // Module C: the origin.
        Chunk chunkC = CompileWithExports("""
            export fn foo() { return 7; }
            """);

        // Module B: re-exports C's foo selectively.
        Chunk chunkB = CompileWithExports("""
            export { foo } from "c.stash";
            """);

        // Module A: re-exports B's foo selectively (chains through B → C).
        Chunk chunkA = CompileWithExports("""
            export { foo } from "b.stash";
            """);

        var modules = new Dictionary<string, Chunk>
        {
            ["a.stash"] = chunkA,
            ["b.stash"] = chunkB,
            ["c.stash"] = chunkC,
        };

        // Main: imports foo from A; must receive C's original value.
        string mainSource = """
            let func = null;
            func = () => {
                import { foo } from "a.stash";
                return foo();
            };
            return func();
            """;

        object? result = RunWithModules(modules, mainSource);
        Assert.Equal(7L, result);
    }

    // ── 4. Cache sharing — module body runs exactly once ─────────────────────

    [Fact]
    public void ReexportTwoAliasesSameModule_ModuleBodyRunsOnce()
    {
        // Module X: will be re-exported under two different aliases from A.
        Chunk chunkX = CompileWithExports("""
            export fn value() { return 1; }
            """);

        // Module A: re-exports X under two aliases (both should share the cache entry).
        Chunk chunkA = CompileWithExports("""
            export "x.stash" as x1;
            export "x.stash" as x2;
            """);

        int loadCount = 0;
        Chunk mainChunk = CompileSource("""
            let func = null;
            func = () => {
                import "a.stash" as A;
                return A.x1.value() + A.x2.value();
            };
            return func();
            """);

        var vm = new VirtualMachine();
        vm.ModuleLoader = (resolvedPath, _) =>
        {
            string normalized = resolvedPath.Replace('\\', '/');
            if (normalized.EndsWith("x.stash"))
            {
                loadCount++;
                return chunkX;
            }
            if (normalized.EndsWith("a.stash"))
                return chunkA;
            throw new RuntimeError($"Test: unexpected path '{resolvedPath}'.", null);
        };

        object? result = vm.Execute(mainChunk);

        Assert.Equal(2L, result);
        // x.stash must be loaded exactly once thanks to the ModuleCache.
        Assert.Equal(1, loadCount);
    }

    // ── 5. Re-export of a non-exported name fails at module-load time ─────────

    [Fact]
    public void SelectiveReexport_NameNotInSourceExports_ThrowsDoesNotExportError()
    {
        // Module B: exports foo but not secret.
        Chunk chunkB = CompileWithExports("""
            export fn foo() { return 1; }
            fn secret() { return 2; }
            """);

        // Module A: attempts to re-export 'secret' which is not exported by B.
        // ModuleExportsBuilder in 2C adds 'secret' to A's export set, but at
        // runtime when the importer tries to import 'secret' from the cached env
        // of A, it will be absent (because B filtered it out) and LoadModule will
        // throw "Module does not export 'secret'".
        Chunk chunkA = CompileWithExports("""
            export { secret } from "b.stash";
            """);

        var modules = new Dictionary<string, Chunk>
        {
            ["a.stash"] = chunkA,
            ["b.stash"] = chunkB,
        };

        Chunk mainChunk = CompileSource("""
            let func = null;
            func = () => {
                import { secret } from "a.stash";
                return secret();
            };
            return func();
            """);

        var vm = new VirtualMachine();
        vm.ModuleLoader = (resolvedPath, _) =>
        {
            string normalized = resolvedPath.Replace('\\', '/');
            foreach (KeyValuePair<string, Chunk> kv in modules)
            {
                if (normalized.EndsWith(kv.Key))
                    return kv.Value;
            }
            throw new RuntimeError($"Test: no module registered for path '{resolvedPath}'.", null);
        };

        var ex = Assert.Throws<RuntimeError>(() => vm.Execute(mainChunk));
        Assert.Contains("does not export", ex.Message);
        Assert.Contains("secret", ex.Message);
    }

    // ── 6. Same-module use of namespace alias (D-12) ─────────────────────────

    [Fact]
    public void SameModuleUse_NamespaceAlias_CanCallAliasedFunction()
    {
        // Module x: defines and exports foo.
        Chunk chunkX = CompileWithExports("""
            export fn foo() { return 55; }
            """);

        // Module re-exporter: re-exports x as namespace, and also exports a
        // function that uses x.foo() locally — proving D-12 same-module use.
        // 'getV' is the observable bridge: it calls x.foo() and returns the result.
        Chunk chunkReexporter = CompileWithExports("""
            export "x.stash" as x;
            export fn getV() { return x.foo(); }
            """);

        // Main: imports getV from the re-exporter and calls it.
        Chunk mainChunk = CompileSource("""
            let func = null;
            func = () => {
                import { getV } from "reexporter.stash";
                return getV();
            };
            return func();
            """);

        var vm = new VirtualMachine();
        vm.ModuleLoader = (resolvedPath, _) =>
        {
            string normalized = resolvedPath.Replace('\\', '/');
            if (normalized.EndsWith("reexporter.stash"))
                return chunkReexporter;
            if (normalized.EndsWith("x.stash"))
                return chunkX;
            throw new RuntimeError($"Test: unexpected '{resolvedPath}'.", null);
        };

        object? result = vm.Execute(mainChunk);
        Assert.Equal(55L, result);
    }

    // ── 7. Same-module use of selective names (D-12) ─────────────────────────

    [Fact]
    public void SameModuleUse_SelectiveNames_CanCallImportedFunction()
    {
        // Module x: defines and exports foo.
        Chunk chunkX = CompileWithExports("""
            export fn foo() { return 77; }
            """);

        // Module re-exporter: re-exports foo from x and also exports a function
        // that calls foo() directly (the D-12 same-module local binding).
        Chunk chunkReexporter = CompileWithExports("""
            export { foo } from "x.stash";
            export fn getV() { return foo(); }
            """);

        Chunk mainChunk = CompileSource("""
            let func = null;
            func = () => {
                import { getV } from "reexporter.stash";
                return getV();
            };
            return func();
            """);

        var vm = new VirtualMachine();
        vm.ModuleLoader = (resolvedPath, _) =>
        {
            string normalized = resolvedPath.Replace('\\', '/');
            if (normalized.EndsWith("reexporter.stash"))
                return chunkReexporter;
            if (normalized.EndsWith("x.stash"))
                return chunkX;
            throw new RuntimeError($"Test: unexpected '{resolvedPath}'.", null);
        };

        object? result = vm.Execute(mainChunk);
        Assert.Equal(77L, result);
    }

    // ── 8. Dynamic path expression (D-9) ─────────────────────────────────────

    [Fact]
    public void DynamicPath_ConstReference_LoadsModuleAtRuntimeByEvaluatedPath()
    {
        // Module x: defines and exports foo.
        Chunk chunkX = CompileWithExports("""
            export fn foo() { return 33; }
            """);

        // Re-exporter: uses a const for the path expression.
        Chunk chunkReexporter = CompileWithExports("""
            const P = "x.stash";
            export P as x;
            """);

        Chunk mainChunk = CompileSource("""
            let func = null;
            func = () => {
                import "reexporter.stash" as R;
                return R.x.foo();
            };
            return func();
            """);

        var vm = new VirtualMachine();
        vm.ModuleLoader = (resolvedPath, _) =>
        {
            string normalized = resolvedPath.Replace('\\', '/');
            if (normalized.EndsWith("reexporter.stash"))
                return chunkReexporter;
            if (normalized.EndsWith("x.stash"))
                return chunkX;
            throw new RuntimeError($"Test: unexpected '{resolvedPath}'.", null);
        };

        object? result = vm.Execute(mainChunk);
        Assert.Equal(33L, result);
    }

    // ── 9. Existing ImportExportRuntimeTests still pass (smoke check) ─────────
    // This phase does NOT duplicate them — the verify command covers them via
    // dotnet build Stash.Tests and dotnet test with the full suite filter.
    // The nine tests above together cover all Phase 2E done_when bullets.
}
