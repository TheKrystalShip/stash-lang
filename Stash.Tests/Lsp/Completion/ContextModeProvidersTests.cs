namespace Stash.Tests.Lsp.Completion;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Extensions.Logging.Abstractions;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using Stash.Analysis;
using Stash.Lsp.Analysis;
using Stash.Lsp.Completion;
using Stash.Lsp.Completion.Providers;
using Stash.Lsp.Completion.Providers.Dot;
using Stash.Common;
using Stash.Stdlib;
using Xunit;
using LspCompletionItemKind = OmniSharp.Extensions.LanguageServer.Protocol.Models.CompletionItemKind;
using LspCompletionContext = OmniSharp.Extensions.LanguageServer.Protocol.Models.CompletionContext;

/// <summary>
/// Unit tests for the three context-mode completion providers:
/// <see cref="ImportPathCompletionProvider"/> (in import-path strings),
/// <see cref="IsTypeCompletionProvider"/> (after the <c>is</c> keyword), and
/// <see cref="ExtendTypeCompletionProvider"/> (after the <c>extend</c> keyword).
/// </summary>
public class ContextModeProvidersTests
{
    // ── ImportPathCompletionProvider — AppliesTo gate ────────────────────────────

    [Fact]
    public void ImportPathProvider_AppliesTo_ImportStringMode()
    {
        var ctx = BuildContextForMode(CompletionMode.ImportString, "");
        Assert.True(new ImportPathCompletionProvider().AppliesTo(ctx));
    }

    [Fact]
    public void ImportPathProvider_DoesNotApply_DefaultMode()
    {
        var ctx = BuildContextForMode(CompletionMode.Default, "");
        Assert.False(new ImportPathCompletionProvider().AppliesTo(ctx));
    }

    [Fact]
    public void ImportPathProvider_DoesNotApply_DotMode()
    {
        var ctx = BuildContextForMode(CompletionMode.Dot, "");
        Assert.False(new ImportPathCompletionProvider().AppliesTo(ctx));
    }

    // ── ImportPathCompletionProvider — IsImportContext logic ─────────────────────

    [Fact]
    public void ImportPathProvider_IsImportContext_From_ReturnsTrue()
    {
        // from "|mypkg"
        Assert.True(ImportPathCompletionProvider.IsImportContext(@"from ""mypkg""", 7));
    }

    [Fact]
    public void ImportPathProvider_IsImportContext_Import_ReturnsTrue()
    {
        // import "|mypkg"
        Assert.True(ImportPathCompletionProvider.IsImportContext(@"import ""mypkg""", 8));
    }

    [Fact]
    public void ImportPathProvider_IsImportContext_PlainString_ReturnsFalse()
    {
        // let x = "|hello"
        Assert.False(ImportPathCompletionProvider.IsImportContext(@"let x = ""hello""", 11));
    }

    [Fact]
    public void ImportPathProvider_IsImportContext_DestructuredImport_ReturnsFalse()
    {
        // import { foo } from "|mypkg" — the 'import {' prefix disqualifies it
        // Actually only the bare "import ..." without braces triggers the import context.
        // With '{', the 'from' portion still triggers. Test the brace guard:
        Assert.False(ImportPathCompletionProvider.IsImportContext(@"import { foo } ""mypkg""", 16));
    }

    [Fact]
    public void ImportPathProvider_IsImportContext_FromWithWordBoundary_ReturnsTrue()
    {
        // "from" is preceded by whitespace — valid
        Assert.True(ImportPathCompletionProvider.IsImportContext(@"  from ""mypkg""", 9));
    }

    [Fact]
    public void ImportPathProvider_IsImportContext_MyfromIsNotFrom_ReturnsFalse()
    {
        // "myfrom" — "from" is NOT a whole word here
        Assert.False(ImportPathCompletionProvider.IsImportContext(@"myfrom ""mypkg""", 9));
    }

    // ── ImportPathCompletionProvider — no candidates for non-import strings ──────

    [Fact]
    public void ImportPathProvider_PlainStringContext_EmitsNoCandidate()
    {
        // Line is a plain string, not an import statement
        var line = @"let x = ""hello""";
        var ctx = BuildImportStringContextForLine(line, col: 11);
        var provider = new ImportPathCompletionProvider();
        var candidates = provider.Provide(ctx).ToList();
        Assert.Empty(candidates);
    }

    // ── ImportPathCompletionProvider — scoped package handling ───────────────────

    [Fact]
    public void ImportPathProvider_ScopedPackage_ProducesAtScopeSlashName()
    {
        // Create a temp stash directory with a scoped package
        using var tmp = new TempStashesDir(new Dictionary<string, string[]>
        {
            ["@myorg"] = ["mylib"]
        });

        var line = @"from ""|""";
        var ctx = BuildImportStringContextForLine(line, col: 7, root: tmp.Root);
        var provider = new ImportPathCompletionProvider();
        var candidates = provider.Provide(ctx).ToList();

        Assert.Contains(candidates, c => c.Label == "@myorg/mylib");
    }

    [Fact]
    public void ImportPathProvider_RegularPackage_ProducesPackageName()
    {
        using var tmp = new TempStashesDir(new Dictionary<string, string[]>
        {
            ["mypackage"] = []
        });

        var line = @"from ""|""";
        var ctx = BuildImportStringContextForLine(line, col: 7, root: tmp.Root);
        var provider = new ImportPathCompletionProvider();
        var candidates = provider.Provide(ctx).ToList();

        Assert.Contains(candidates, c => c.Label == "mypackage");
    }

    [Fact]
    public void ImportPathProvider_AllCandidates_HaveModuleKind()
    {
        using var tmp = new TempStashesDir(new Dictionary<string, string[]>
        {
            ["pkg1"] = [],
            ["@scope"] = ["pkg2"]
        });

        var line = @"from ""|""";
        var ctx = BuildImportStringContextForLine(line, col: 7, root: tmp.Root);
        var provider = new ImportPathCompletionProvider();
        var candidates = provider.Provide(ctx).ToList();

        Assert.All(candidates, c => Assert.Equal(LspCompletionItemKind.Module, c.Kind));
    }

    [Fact]
    public void ImportPathProvider_SourcePriority_Is10()
    {
        using var tmp = new TempStashesDir(new Dictionary<string, string[]> { ["pkg1"] = [] });

        var line = @"from ""|""";
        var ctx = BuildImportStringContextForLine(line, col: 7, root: tmp.Root);
        var provider = new ImportPathCompletionProvider();
        var candidate = provider.Provide(ctx).First();

        Assert.Equal(10, candidate.SourcePriority);
    }

    [Fact]
    public void ImportPathProvider_SourceTag_IsSet()
    {
        using var tmp = new TempStashesDir(new Dictionary<string, string[]> { ["pkg1"] = [] });

        var line = @"from ""|""";
        var ctx = BuildImportStringContextForLine(line, col: 7, root: tmp.Root);
        var provider = new ImportPathCompletionProvider();
        var candidate = provider.Provide(ctx).First();

        Assert.Equal("ImportPathCompletionProvider", candidate.SourceTag);
    }

    // ── IsTypeCompletionProvider ─────────────────────────────────────────────────

    [Fact]
    public void IsTypeProvider_AppliesTo_AfterIsMode()
    {
        var ctx = BuildContextForMode(CompletionMode.AfterIs, "");
        Assert.True(new IsTypeCompletionProvider().AppliesTo(ctx));
    }

    [Fact]
    public void IsTypeProvider_DoesNotApply_DefaultMode()
    {
        var ctx = BuildContextForMode(CompletionMode.Default, "");
        Assert.False(new IsTypeCompletionProvider().AppliesTo(ctx));
    }

    [Fact]
    public void IsTypeProvider_EmitsOneCandidatePerTypeDescription()
    {
        var ctx = BuildContextForMode(CompletionMode.AfterIs, "");
        var provider = new IsTypeCompletionProvider();
        var candidates = provider.Provide(ctx).ToList();

        Assert.Equal(StdlibRegistry.TypeDescriptions.Count, candidates.Count);
    }

    [Fact]
    public void IsTypeProvider_AllCandidates_HaveTypeParameterKind()
    {
        var ctx = BuildContextForMode(CompletionMode.AfterIs, "");
        var provider = new IsTypeCompletionProvider();
        var candidates = provider.Provide(ctx).ToList();

        Assert.All(candidates, c => Assert.Equal(LspCompletionItemKind.TypeParameter, c.Kind));
    }

    [Fact]
    public void IsTypeProvider_AllCandidates_MatchTypeDescriptionLabels()
    {
        var ctx = BuildContextForMode(CompletionMode.AfterIs, "");
        var provider = new IsTypeCompletionProvider();
        var candidateLabels = provider.Provide(ctx).Select(c => c.Label).ToHashSet();

        foreach (var name in StdlibRegistry.TypeDescriptions.Keys)
        {
            Assert.Contains(name, candidateLabels);
        }
    }

    [Fact]
    public void IsTypeProvider_SourcePriority_Is10()
    {
        var ctx = BuildContextForMode(CompletionMode.AfterIs, "");
        var candidate = new IsTypeCompletionProvider().Provide(ctx).First();
        Assert.Equal(10, candidate.SourcePriority);
    }

    [Fact]
    public void IsTypeProvider_SourceTag_IsSet()
    {
        var ctx = BuildContextForMode(CompletionMode.AfterIs, "");
        var candidate = new IsTypeCompletionProvider().Provide(ctx).First();
        Assert.Equal("IsTypeCompletionProvider", candidate.SourceTag);
    }

    // ── ExtendTypeCompletionProvider ─────────────────────────────────────────────

    [Fact]
    public void ExtendTypeProvider_AppliesTo_AfterExtendMode()
    {
        var ctx = BuildContextForMode(CompletionMode.AfterExtend, "");
        Assert.True(new ExtendTypeCompletionProvider().AppliesTo(ctx));
    }

    [Fact]
    public void ExtendTypeProvider_DoesNotApply_DefaultMode()
    {
        var ctx = BuildContextForMode(CompletionMode.Default, "");
        Assert.False(new ExtendTypeCompletionProvider().AppliesTo(ctx));
    }

    [Fact]
    public void ExtendTypeProvider_EmitsBuiltInTypes()
    {
        var ctx = BuildContextForMode(CompletionMode.AfterExtend, "");
        var provider = new ExtendTypeCompletionProvider();
        var labels = provider.Provide(ctx).Select(c => c.Label).ToHashSet();

        // All extendable primitive types (derived from PrimitiveTypes) must appear.
        // Canonical extendable set: non-meta, non-structural, non-typed-array primitives.
        foreach (var t in ExtendableBuiltInTypes())
        {
            Assert.Contains(t, labels);
        }
    }

    [Fact]
    public void ExtendTypeProvider_BuiltInTypes_AreEmittedFirst()
    {
        var ctx = BuildContextForMode(CompletionMode.AfterExtend, "struct MyStruct { }\n");
        var candidates = new ExtendTypeCompletionProvider().Provide(ctx).ToList();

        // The built-ins must appear before any user struct
        var builtInNames = ExtendableBuiltInTypes().ToHashSet();
        int lastBuiltIn = -1;
        int firstStruct = int.MaxValue;

        for (int i = 0; i < candidates.Count; i++)
        {
            if (builtInNames.Contains(candidates[i].Label)) lastBuiltIn = i;
            else if (candidates[i].Kind == LspCompletionItemKind.Struct) firstStruct = Math.Min(firstStruct, i);
        }

        if (firstStruct != int.MaxValue)
        {
            Assert.True(lastBuiltIn < firstStruct, "Built-in types must precede user-defined structs.");
        }
    }

    [Fact]
    public void ExtendTypeProvider_IncludesUserDefinedStructs()
    {
        const string src = "struct MyPoint { x: int, y: int }\n";
        var ctx = BuildContextForMode(CompletionMode.AfterExtend, src);
        var provider = new ExtendTypeCompletionProvider();
        var labels = provider.Provide(ctx).Select(c => c.Label).ToList();

        Assert.Contains("MyPoint", labels);
    }

    [Fact]
    public void ExtendTypeProvider_UserStructNamedAfterBuiltIn_DeduplicatesBuiltIn()
    {
        // If a user struct has the same name as a built-in type, only one entry appears.
        // The built-in wins (seeded into the dedup set before structs are enumerated).
        // We can't easily register a user struct named "string" via the analysis engine,
        // so this test verifies that built-in type names appear exactly once in the output.
        var ctx = BuildContextForMode(CompletionMode.AfterExtend, "");
        var provider = new ExtendTypeCompletionProvider();
        var labels = provider.Provide(ctx).Select(c => c.Label).ToList();

        // No duplicates among built-in extendable names (derived from PrimitiveTypes)
        foreach (var name in ExtendableBuiltInTypes())
        {
            Assert.Single(labels, l => l == name);
        }
    }

    [Fact]
    public void ExtendTypeProvider_SourcePriority_Is10()
    {
        var ctx = BuildContextForMode(CompletionMode.AfterExtend, "");
        var candidate = new ExtendTypeCompletionProvider().Provide(ctx).First();
        Assert.Equal(10, candidate.SourcePriority);
    }

    [Fact]
    public void ExtendTypeProvider_SourceTag_IsSet()
    {
        var ctx = BuildContextForMode(CompletionMode.AfterExtend, "");
        var candidate = new ExtendTypeCompletionProvider().Provide(ctx).First();
        Assert.Equal("ExtendTypeCompletionProvider", candidate.SourceTag);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the canonical set of built-in types accepted by the runtime's
    /// <c>extend</c> compiler check. Production code is the single source of truth.
    /// </summary>
    private static IEnumerable<string> ExtendableBuiltInTypes()
        => ExtendTypeCompletionProvider.BuiltInExtendableTypes;

    private static Stash.Lsp.Completion.CompletionContext BuildContextForMode(CompletionMode mode, string source, int line = 0, int col = 0)
    {
        var engine = new AnalysisEngine(NullLogger<AnalysisEngine>.Instance);
        var uri = new Uri($"file:///test/ctx_{Guid.NewGuid():N}.stash");
        engine.Analyze(uri, source);
        var result = engine.GetCachedResult(uri);

        var lines = source.Split('\n');
        string? currentLine = line < lines.Length ? lines[line] : null;

        return new Stash.Lsp.Completion.CompletionContext(
            Uri: uri,
            LspLine: line,
            LspColumn: col,
            CurrentLine: currentLine,
            Mode: mode,
            DotPrefix: null,
            Analysis: result,
            TriggerCharacter: null);
    }

    /// <summary>
    /// Builds an ImportString-mode context with a given <paramref name="line"/> text
    /// and cursor column. Optionally uses a temp project root for filesystem-based tests.
    /// </summary>
    private static Stash.Lsp.Completion.CompletionContext BuildImportStringContextForLine(
        string line, int col, string? root = null)
    {
        // When a real project root is needed, we create a fake file inside it.
        Uri uri;
        if (root != null)
        {
            // Place the fake file inside the project root so ModuleResolver.FindProjectRoot can locate it
            string fakePath = Path.Combine(root, "test.stash");
            uri = new Uri("file://" + fakePath);
        }
        else
        {
            uri = new Uri($"file:///test/ctx_{Guid.NewGuid():N}.stash");
        }

        return new Stash.Lsp.Completion.CompletionContext(
            Uri: uri,
            LspLine: 0,
            LspColumn: col,
            CurrentLine: line,
            Mode: CompletionMode.ImportString,
            DotPrefix: null,
            Analysis: null,
            TriggerCharacter: null);
    }

    // ── Temporary directory helper ────────────────────────────────────────────────

    /// <summary>
    /// Creates a temporary project structure with a <c>stashes/</c> directory.
    /// Scoped packages are under <c>@scope/name</c> sub-directories.
    /// The key is either a plain package name or a <c>@scope</c> directory;
    /// the value array holds sub-package names (only meaningful for scoped entries).
    /// Dispose removes the directory tree.
    /// </summary>
    private sealed class TempStashesDir : IDisposable
    {
        public string Root { get; }

        public TempStashesDir(Dictionary<string, string[]> packages)
        {
            Root = Path.Combine(Path.GetTempPath(), "stash_test_" + Guid.NewGuid().ToString("N"));
            string stashesDir = Path.Combine(Root, "stashes");
            Directory.CreateDirectory(stashesDir);

            // ModuleResolver.FindProjectRoot walks up looking for stash.json.
            File.WriteAllText(Path.Combine(Root, "stash.json"), "{}");

            foreach (var (name, subPkgs) in packages)
            {
                string pkgDir = Path.Combine(stashesDir, name);
                Directory.CreateDirectory(pkgDir);
                foreach (var sub in subPkgs)
                {
                    Directory.CreateDirectory(Path.Combine(pkgDir, sub));
                }
            }
        }

        public void Dispose()
        {
            try { Directory.Delete(Root, recursive: true); } catch { /* best effort */ }
        }
    }

}
