using System;
using System.IO;
using System.Threading;
using Stash.Bytecode;
using Stash.Lexing;
using Stash.Parsing;
using Stash.Resolution;
using Stash.Runtime;
using Stash.Stdlib;
using Xunit;

namespace Stash.Tests.Interpreting;

/// <summary>
/// Phase 2A-3: _importStack isolation at cross-thread fork sites.
///
/// done_when coverage:
///   #1 — SpawnAsyncFunction passes the child VM a SNAPSHOT of _importStack (not the
///         parent's live list). Tested indirectly: concurrent async imports don't produce
///         spurious "Circular import detected" errors.
///   #2 — Module-load (VirtualMachine.Modules.cs) STILL shares the parent's _importStack
///         by reference; synchronous circular-import chains still throw correctly.
///   #3 — N async tasks each calling import concurrently do not corrupt the shared module
///         cache and no spurious circular-import errors fire.
/// </summary>
public class ImportStackIsolationTests : StashTestBase
{
    // ── Helper ───────────────────────────────────────────────────────────────

    private static Chunk CompileSource(string source)
    {
        var tokens = new Lexer(source, "<test>").ScanTokens();
        var stmts = new Parser(tokens).ParseProgram();
        SemanticResolver.Resolve(stmts);
        return Compiler.Compile(stmts);
    }

    // ── #2 — Synchronous circular import still throws ─────────────────────────

    /// <summary>
    /// The module-load code path (VirtualMachine.Modules.cs) deliberately shares
    /// _importStack by reference with the child module VM so that a->b->a import
    /// cycles are detected synchronously. After phase 2A-3 the async fork site is
    /// isolated, but the same-thread module-load path must be UNCHANGED.
    /// </summary>
    [Fact]
    public void SynchronousCircularImport_StillThrowsCircularImportDetected()
    {
        string tmpDir = Path.Combine(Path.GetTempPath(), "stash_importstack_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmpDir);
        try
        {
            // a.stash -> imports b.stash -> imports a.stash  (cycle)
            string aPath = Path.Combine(tmpDir, "a.stash");
            string bPath = Path.Combine(tmpDir, "b.stash");
            File.WriteAllText(aPath, "import { bar } from \"b.stash\"; export fn foo() { return 1; }");
            File.WriteAllText(bPath, "import { foo } from \"a.stash\"; export fn bar() { return 2; }");

            var tokens = new Lexer(File.ReadAllText(aPath), aPath).ScanTokens();
            var stmts = new Parser(tokens).ParseProgram();
            SemanticResolver.Resolve(stmts);
            var chunk = Compiler.Compile(stmts);

            var vm = new VirtualMachine(StdlibDefinitions.CreateVMGlobals());
            vm.CurrentFile = aPath;

            var ex = Assert.ThrowsAny<RuntimeError>(() => vm.Execute(chunk));
            Assert.Contains("Circular import detected", ex.Message);
        }
        finally
        {
            Directory.Delete(tmpDir, true);
        }
    }

    // ── #1 + #3 — Async import: no spurious circular-import, no cache corruption ──

    /// <summary>
    /// Spawning N async tasks each importing the same module must not produce spurious
    /// "Circular import detected" errors. Before phase 2A-3, each child VM shared the
    /// parent's _importStack by reference; one child adding a path before another child
    /// tried to add the same path would trigger the circular-import guard spuriously.
    /// With a per-child snapshot these races cannot occur.
    /// </summary>
    [Fact]
    public void AsyncTasks_ConcurrentImport_NoSpuriousCircularImportError()
    {
        string tmpDir = Path.Combine(Path.GetTempPath(), "stash_asyncimport_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmpDir);
        try
        {
            // A simple module that exports a constant.
            string modPath = Path.Combine(tmpDir, "util.stash");
            File.WriteAllText(modPath, "export fn value() { return 42; }");

            // Each async task imports util.stash and returns value().
            const int N = 20;

            // Build a string path that is safe to embed in Stash source without \ issues.
            string safePath = modPath.Replace("\\", "/");

            // Build the Stash source as a regular C# string (no interpolation needed here
            // because the Stash source contains no dynamic values from C# at this point).
            string mainSource =
                "async fn worker() { " +
                    "import { value } from \"" + safePath + "\"; " +
                    "return value(); " +
                "} " +
                "let futures = []; " +
                "let i = 0; " +
                "while (i < " + N + ") { futures.push(worker()); i = i + 1; } " +
                "let results = await task.all(futures); " +
                "let allOk = true; " +
                "let j = 0; " +
                "while (j < " + N + ") { if (results[j] != 42) { allOk = false; } j = j + 1; } " +
                "let result = allOk;";

            // Add the 'return result' suffix used by Run().
            string full = mainSource + "\nreturn result;";
            var tokens = new Lexer(full, "<test>").ScanTokens();
            var stmts = new Parser(tokens).ParseProgram();
            SemanticResolver.Resolve(stmts);
            var chunk = Compiler.Compile(stmts);
            var vm = new VirtualMachine(StdlibDefinitions.CreateVMGlobals());

            object? result = vm.Execute(chunk);
            Assert.Equal(true, result);
        }
        finally
        {
            Directory.Delete(tmpDir, true);
        }
    }

    /// <summary>
    /// Verify that after many concurrent async imports the module cache contains
    /// exactly one entry for the shared module (the module-loader is called exactly
    /// once despite N concurrent attempts). This confirms that the shared ModuleCache
    /// plus the per-task isolated _importStack is a correct design.
    /// </summary>
    [Fact]
    public void AsyncTasks_ConcurrentImport_ModuleLoadedExactlyOnce()
    {
        int executionCount = 0;

        Chunk moduleChunk;
        {
            string modSrc = "export fn hi() { return 1; }";
            var tokens = new Lexer(modSrc, "<module>").ScanTokens();
            var stmts = new Parser(tokens).ParseProgram();
            SemanticResolver.Resolve(stmts);
            moduleChunk = Compiler.Compile(stmts);
        }

        const int N = 20;
        string mainSrc =
            "async fn worker() { " +
                "import { hi } from \"shared_module\"; " +
                "return hi(); " +
            "} " +
            "let futures = []; " +
            "let i = 0; " +
            "while (i < " + N + ") { futures.push(worker()); i = i + 1; } " +
            "let results = await task.all(futures); " +
            "let result = results[0];" +
            "\nreturn result;";

        var tokens2 = new Lexer(mainSrc, "<test>").ScanTokens();
        var stmts2 = new Parser(tokens2).ParseProgram();
        SemanticResolver.Resolve(stmts2);
        var mainChunk = Compiler.Compile(stmts2);

        var vm = new VirtualMachine(StdlibDefinitions.CreateVMGlobals());
        vm.ModuleLoader = (_, _) =>
        {
            Interlocked.Increment(ref executionCount);
            return moduleChunk;
        };

        object? result = vm.Execute(mainChunk);
        Assert.Equal(1L, result);

        // The module should have been loaded/executed only once (cache hit for the rest).
        Assert.Equal(1, executionCount);
    }

    // ── Direct scenario: async child imports same path that parent already imported ──

    /// <summary>
    /// The parent synchronously imports leaf.stash (which is added to the import stack and
    /// then removed after completion). Then an async child also imports leaf.stash. With a
    /// shared _importStack the async child would see leaf.stash still "in progress" (if the
    /// parent's synchronous import overlapped with child spawn) and throw. With a snapshot
    /// the child starts from its own independent copy.
    ///
    /// Here we test the completed-import case: the parent's sync import is done before spawn,
    /// so the path was already removed. The async child must succeed too.
    /// </summary>
    [Fact]
    public void AsyncChild_ImportSamePathAsParent_NoDuplicateError()
    {
        string tmpDir = Path.Combine(Path.GetTempPath(), "stash_snapshot_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmpDir);
        try
        {
            string aPath = Path.Combine(tmpDir, "leaf.stash");
            File.WriteAllText(aPath, "export fn answer() { return 42; }");

            string safePath = aPath.Replace("\\", "/");

            // Parent imports leaf.stash synchronously first, then spawns async that also imports it.
            string mainSrc =
                "import { answer } from \"" + safePath + "\"; " +
                "async fn asyncImporter() { " +
                    "import { answer } from \"" + safePath + "\"; " +
                    "return answer(); " +
                "} " +
                "let f = asyncImporter(); " +
                "let result = await f;" +
                "\nreturn result;";

            var tokens = new Lexer(mainSrc, "<test>").ScanTokens();
            var stmts = new Parser(tokens).ParseProgram();
            SemanticResolver.Resolve(stmts);
            var chunk = Compiler.Compile(stmts);

            var vm = new VirtualMachine(StdlibDefinitions.CreateVMGlobals());
            object? result = vm.Execute(chunk);
            Assert.Equal(42L, result);
        }
        finally
        {
            Directory.Delete(tmpDir, true);
        }
    }
}
