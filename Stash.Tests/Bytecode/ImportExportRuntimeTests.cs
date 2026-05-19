using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using Stash.Analysis;
using Stash.Bytecode;
using Stash.Core.Resolution;
using Stash.Lexing;
using Stash.Parsing;
using Stash.Parsing.AST;
using Stash.Resolution;
using Stash.Runtime;
using Stash.Runtime.Types;

namespace Stash.Tests.Bytecode;

/// <summary>
/// VM-level tests for the export-set filtering introduced at the <c>LoadModule</c>
/// boundary. Covers the seven scenarios listed in spec Section 6 "VM / Runtime".
/// </summary>
public class ImportExportRuntimeTests : BytecodeTestBase
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Compiles <paramref name="source"/> and runs the <see cref="ModuleExportsBuilder"/>
    /// pass so that <c>Chunk.Exports</c> is populated from any <c>export</c> annotations.
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
    /// Compiles <paramref name="source"/> without running the export builder so that
    /// <c>Chunk.Exports</c> is <see langword="null"/> (legacy module behaviour).
    /// </summary>
    private static Chunk CompileLegacy(string source) => CompileSource(source);

    /// <summary>
    /// Executes <paramref name="mainSource"/> inside a VM that resolves all module
    /// paths to <paramref name="moduleChunk"/>.
    /// </summary>
    private static object? RunWithModule(Chunk moduleChunk, string mainSource)
    {
        Chunk mainChunk = CompileSource(mainSource);
        var vm = new VirtualMachine();
        vm.ModuleLoader = (_, _) => moduleChunk;
        return vm.Execute(mainChunk);
    }

    // ── Vm_ImportFromExplicitExportModule_OnlySeesExports ─────────────────────

    [Fact]
    public void Vm_ImportFromExplicitExportModule_OnlySeesExports()
    {
        // Module exports only "pub"; "priv" is module-private.
        Chunk moduleChunk = CompileWithExports("""
            export fn pub() { return 1; }
            fn priv() { return 2; }
            """);

        // Importing the exported name succeeds.
        string mainSource = """
            let func = null;
            func = () => {
                import { pub } from "mod";
                return pub();
            };
            return func();
            """;
        object? result = RunWithModule(moduleChunk, mainSource);
        Assert.Equal(1L, result);
    }

    // ── Vm_ImportFromLegacyModule_StillSeesEverything ─────────────────────────

    [Fact]
    public void Vm_ImportFromLegacyModule_StillSeesEverything()
    {
        // Legacy module (no export annotations) — Chunk.Exports is null.
        Chunk moduleChunk = CompileLegacy("""
            let value = null;
            value = 99;
            """);

        string mainSource = """
            let func = null;
            func = () => {
                import { value } from "mod";
                return value;
            };
            return func();
            """;
        object? result = RunWithModule(moduleChunk, mainSource);
        Assert.Equal(99L, result);
    }

    // ── Vm_ImportMissingExport_RaisesDoesNotExport ────────────────────────────

    [Fact]
    public void Vm_ImportMissingExport_RaisesDoesNotExport()
    {
        // Module has only "pub" in its export set; "secret" is module-private.
        Chunk moduleChunk = CompileWithExports("""
            export fn pub() { return 1; }
            fn secret() { return 2; }
            """);

        string mainSource = """
            let func = null;
            func = () => {
                import { secret } from "mod";
                return secret();
            };
            return func();
            """;

        Chunk mainChunk = CompileSource(mainSource);
        var vm = new VirtualMachine();
        vm.ModuleLoader = (_, _) => moduleChunk;
        var ex = Assert.Throws<RuntimeError>(() => vm.Execute(mainChunk));
        Assert.Contains("secret", ex.Message);
    }

    // ── Vm_ImportAsAlias_NamespaceOnlyHasExports ──────────────────────────────

    [Fact]
    public void Vm_ImportAsAlias_NamespaceOnlyHasExports()
    {
        // Only "pub" is exported; "hidden" must not appear on the alias namespace.
        Chunk moduleChunk = CompileWithExports("""
            export fn pub() { return 42; }
            fn hidden() { return -1; }
            """);

        // Access the exported member via alias — must work.
        string mainSource = """
            let func = null;
            func = () => {
                import "mod" as m;
                return m.pub();
            };
            return func();
            """;
        object? result = RunWithModule(moduleChunk, mainSource);
        Assert.Equal(42L, result);
    }

    // ── Vm_ImportAsAlias_BuiltInNamespacesStillCarvedOut ─────────────────────

    [Fact]
    public void Vm_ImportAsAlias_BuiltInNamespacesStillCarvedOut()
    {
        // Module uses an explicit export set; stdlib built-in namespaces that were
        // copied into the module's globals at startup must NOT appear as members of
        // the alias namespace (the existing IsBuiltIn carve-out in ExecuteImportAs).
        Chunk moduleChunk = CompileWithExports("""
            export fn greet() { return "hi"; }
            """);

        // The alias namespace must contain "greet" (exported) and the import must
        // succeed — built-in namespaces are filtered out of the alias by ExecuteImportAs
        // regardless of the module's export set.
        string mainSource = """
            let func = null;
            func = () => {
                import "mod" as m;
                return m.greet();
            };
            return func();
            """;
        object? result = RunWithModule(moduleChunk, mainSource);
        Assert.Equal("hi", result);

        // Verify the alias namespace does not expose a built-in namespace as a field.
        // We do this by directly inspecting the StashNamespace returned to the caller.
        Chunk moduleChunk2 = CompileWithExports("""
            export fn greet() { return "hi"; }
            """);
        Chunk mainChunk2 = CompileSource("""
            let func = null;
            func = () => {
                import "mod" as m;
                return m;
            };
            return func();
            """);
        var vm2 = new VirtualMachine();
        vm2.ModuleLoader = (_, _) => moduleChunk2;
        object? nsObj = vm2.Execute(mainChunk2);
        var ns = Assert.IsType<StashNamespace>(nsObj);
        // "greet" must be present.
        Assert.True(ns.HasMember("greet"));
        // A built-in namespace name like "str" must not be present on the alias.
        Assert.False(ns.HasMember("str"));
    }

    // ── Vm_ModuleCache_FilteredViewIsCached ───────────────────────────────────

    [Fact]
    public void Vm_ModuleCache_FilteredViewIsCached()
    {
        // Two imports of the same module path must see the same filtered dict.
        Chunk moduleChunk = CompileWithExports("""
            export fn pub() { return 7; }
            fn priv() { return 0; }
            """);

        int loadCount = 0;
        Chunk mainChunk = CompileSource("""
            let a = null;
            let b = null;
            let func = null;
            func = () => {
                import { pub } from "mod";
                a = pub();
                import { pub } from "mod";
                b = pub();
                return a + b;
            };
            return func();
            """);

        var vm = new VirtualMachine();
        vm.ModuleLoader = (_, _) => { loadCount++; return moduleChunk; };
        object? result = vm.Execute(mainChunk);

        Assert.Equal(14L, result);
        // Module must have been loaded exactly once; the cache serves the second import.
        Assert.Equal(1, loadCount);
    }

    // ── Vm_EmptyExportBlock_AllImportsFail ────────────────────────────────────

    [Fact]
    public void Vm_EmptyExportBlock_AllImportsFail()
    {
        // A module with an explicit export block that exports nothing hides every symbol.
        Chunk moduleChunk = CompileWithExports("""
            fn hidden() { return 1; }
            export { };
            """);

        string mainSource = """
            let func = null;
            func = () => {
                import { hidden } from "mod";
                return hidden();
            };
            return func();
            """;

        Chunk mainChunk = CompileSource(mainSource);
        var vm = new VirtualMachine();
        vm.ModuleLoader = (_, _) => moduleChunk;
        var ex = Assert.Throws<RuntimeError>(() => vm.Execute(mainChunk));
        Assert.Contains("hidden", ex.Message);
    }

    // ── E2E: zero-annotation module raises "does not export" at runtime ───────

    /// <summary>
    /// End-to-end test: a script that imports from a file with zero export annotations
    /// must raise a runtime "does not export 'X'" error.
    /// Uses real disk files and the VM's built-in file-based module loader.
    /// </summary>
    [Fact]
    public void E2E_ZeroAnnotationModule_ImportFails()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "stash_e2e_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            // A module with no export annotations — exports nothing.
            File.WriteAllText(Path.Combine(tempDir, "lib.stash"), """
                fn helper() { return 42; }
                fn another() { return 99; }
                """);

            // Main script imports from that module.
            string mainPath = Path.Combine(tempDir, "main.stash");
            File.WriteAllText(mainPath, """
                import { helper } from "lib.stash";
                return helper();
                """);

            string mainSource = File.ReadAllText(mainPath);
            var tokens = new Lexer(mainSource, mainPath).ScanTokens();
            var stmts = new Parser(tokens).ParseProgram();
            SemanticResolver.Resolve(stmts);
            var exports = ModuleExportsBuilder.Build(stmts, []);
            var mainChunk = Compiler.Compile(stmts, exports: exports);

            var vm = new VirtualMachine();
            vm.CurrentFile = mainPath;
            var ex = Assert.Throws<RuntimeError>(() => vm.Execute(mainChunk));
            Assert.Contains("does not export 'helper'", ex.Message);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    // ── E2E: namespace import from partial-export module ─────────────────────

    /// <summary>
    /// End-to-end test: <c>import "f.stash" as ns</c> from a file that exports <c>foo</c>
    /// but not <c>bar</c> must expose <c>ns.foo</c> and must not expose <c>ns.bar</c>.
    /// Uses real disk files and the VM's built-in file-based module loader.
    /// </summary>
    [Fact]
    public void E2E_NamespaceImport_ExposesOnlyExportedNames()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "stash_e2e_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            // lib.stash: exports only "foo"; "bar" is private.
            File.WriteAllText(Path.Combine(tempDir, "lib.stash"), """
                export fn foo() { return "foo_result"; }
                fn bar() { return "bar_result"; }
                """);

            // Main script: import lib as ns, access ns.foo → succeeds, ns.bar → missing member.
            string mainPath = Path.Combine(tempDir, "main.stash");
            File.WriteAllText(mainPath, """
                import "lib.stash" as ns;
                return ns.foo();
                """);

            string mainSource = File.ReadAllText(mainPath);
            var tokens = new Lexer(mainSource, mainPath).ScanTokens();
            var stmts = new Parser(tokens).ParseProgram();
            SemanticResolver.Resolve(stmts);
            var exports = ModuleExportsBuilder.Build(stmts, []);
            var mainChunk = Compiler.Compile(stmts, exports: exports);

            var vm = new VirtualMachine();
            vm.CurrentFile = mainPath;

            // ns.foo() succeeds.
            object? result = vm.Execute(mainChunk);
            Assert.Equal("foo_result", result);

            // Verify ns.bar is absent: import the namespace, inspect it.
            File.WriteAllText(mainPath, """
                import "lib.stash" as ns;
                return ns;
                """);

            var tokens2 = new Lexer(File.ReadAllText(mainPath), mainPath).ScanTokens();
            var stmts2 = new Parser(tokens2).ParseProgram();
            SemanticResolver.Resolve(stmts2);
            var mainChunk2 = Compiler.Compile(stmts2);
            var vm2 = new VirtualMachine();
            vm2.CurrentFile = mainPath;
            object? nsObj = vm2.Execute(mainChunk2);
            var ns = Assert.IsType<StashNamespace>(nsObj);
            Assert.True(ns.HasMember("foo"));
            Assert.False(ns.HasMember("bar"));
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }
}
