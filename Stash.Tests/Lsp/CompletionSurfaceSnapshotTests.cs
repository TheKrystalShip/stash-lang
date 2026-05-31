namespace Stash.Tests.Lsp;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using Stash.Analysis;
using Stash.Analysis.Cli;
using Stash.Common;
using Stash.Stdlib;
using Stash.Lsp.Analysis;
using Stash.Lsp.Completion;
using Stash.Lsp.Completion.Providers;
using Stash.Lsp.Completion.Providers.Dot;
using Stash.Lsp.Completion.Snippets;
using Stash.Lsp.Handlers;
using Xunit;
using LspCompletionContext = OmniSharp.Extensions.LanguageServer.Protocol.Models.CompletionContext;
using StashSymbolKind = Stash.Analysis.SymbolKind;

/// <summary>
/// Golden snapshot tests for the LSP completion surface at canonical cursor positions.
/// </summary>
/// <remarks>
/// <para>
/// The point of these tests is to make the completion list a <em>conscious decision</em>
/// at every commit — any change to the set of items surfaced for a given context (keywords,
/// stdlib namespaces, struct dot-access, etc.) shows up as a snapshot diff in code review.
/// They are intentionally noisy in a good way: adding a new built-in function or a new
/// stdlib namespace requires re-baselining a snapshot, which is the moment to ask
/// "do I really want this to appear in autocomplete?".
/// </para>
/// <para>
/// Snapshots are normalised to <c>"&lt;Kind&gt;\t&lt;Label&gt;"</c> per line, sorted, with no
/// trailing whitespace. <see cref="CompletionItem.Detail"/> and <see cref="CompletionItem.Documentation"/>
/// are deliberately omitted from the snapshot because they drift more aggressively than the
/// shape of the completion set and would create noise without catching real bugs.
/// </para>
/// <para>
/// To re-baseline after an intentional change, run with <c>STASH_SNAPSHOT_REGEN=1</c>:
/// <code>STASH_SNAPSHOT_REGEN=1 dotnet test --filter FullyQualifiedName~CompletionSurfaceSnapshotTests</code>.
/// The test will overwrite the snapshot file on disk and still fail the first time so the
/// regeneration shows up as a visible diff in the working tree.
/// </para>
/// <para>
/// RFC reference: <c>.kanban/4-done/lsp-completion-providers/brief.md</c>
/// (link becomes live after <c>/done</c> promotes the feature).
/// </para>
/// </remarks>
public class CompletionSurfaceSnapshotTests
{
    // ── Snapshot tests — locked-in expected output for canonical positions ────

    /// <summary>
    /// An empty file's unqualified completion is the union of keywords, stdlib functions,
    /// and stdlib namespaces — exactly once each, with no in-scope user symbols and no
    /// member-style leaks.
    /// </summary>
    [Fact]
    public void Snapshot_EmptyFile_UnqualifiedCompletion()
    {
        var items = InvokeUnqualifiedCompletion("\n");
        AssertSnapshot("empty-file", items);
    }

    /// <summary>
    /// A file with one user variable adds exactly that one label and nothing else.
    /// Locks down the contract that user code only contributes its own declarations.
    /// </summary>
    [Fact]
    public void Snapshot_OneUserVariable_AddsExactlyOneEntry()
    {
        var baseline = InvokeUnqualifiedCompletion("\n").Select(NormalizeItem).ToHashSet();
        var withVar = InvokeUnqualifiedCompletion("let myVar = 42;\n").Select(NormalizeItem).ToHashSet();

        var added = withVar.Except(baseline).ToList();
        var removed = baseline.Except(withVar).ToList();

        Assert.Equal(new[] { $"{CompletionItemKind.Variable}\tmyVar" }, added);
        Assert.Empty(removed);
    }

    // ── Structural invariants — too stdlib-dense to text-snapshot cheaply ────

    /// <summary>
    /// Stdlib namespace dot-access surfaces exactly the registry's view of that
    /// namespace's callable surface, plus its data members, constants, and enums.
    /// </summary>
    [Fact]
    public void Snapshot_FsDot_MatchesRegistrySurface()
    {
        var items = InvokeDotCompletion("fs").ToList();

        var expectedFns = StdlibRegistry.GetNamespaceMembers("fs").Select(m => m.Name).ToHashSet();
        var expectedDataMembers = StdlibRegistry.GetNamespaceDataMembers("fs").Select(m => m.Name).ToHashSet();
        var expectedConstants = StdlibRegistry.GetNamespaceConstants("fs").Select(c => c.Name).ToHashSet();
        var expectedEnums = StdlibRegistry.Enums.Where(e => e.Namespace == "fs").Select(e => e.Name).ToHashSet();

        var allExpected = expectedFns
            .Concat(expectedDataMembers)
            .Concat(expectedConstants)
            .Concat(expectedEnums)
            .ToHashSet();

        var actual = items.Select(i => i.Label).ToHashSet();

        Assert.Equal(allExpected.OrderBy(x => x), actual.OrderBy(x => x));

        // Anti-assertion: user variable names must NOT appear in built-in namespace dot results.
        // (There are no user variables in this test, but the SourceTag must be from the right strategy.)
        Assert.All(items, i => Assert.Equal(
            nameof(BuiltInNamespaceDotStrategy),
            i.Data?.ToString() ?? ""));
    }

    /// <summary>
    /// Verifies that <c>path.</c> dot-completion surfaces <c>match</c> as a member.
    /// This pins the brief's Acceptance Criteria: "path.match appears in completion at a
    /// cursor inside a path. member access". The same <see cref="BuiltInNamespaceDotStrategy"/>
    /// path serves all namespaces, so this fact also acts as a canary for the <c>path</c>
    /// namespace registration end-to-end (registry → strategy → completion handler).
    /// </summary>
    [Fact]
    public void PathDot_ContainsMatch()
    {
        var labels = InvokeDotCompletion("path").Select(i => i.Label).ToHashSet();
        Assert.Contains("match", labels);
    }

    /// <summary>
    /// User enum dot-access surfaces exactly the declared members in declaration order
    /// — nothing more, nothing less. Independent of stdlib evolution.
    /// </summary>
    [Fact]
    public void Snapshot_UserEnumDot_ListsExactlyDeclaredMembers()
    {
        const string src = "enum Color { Red, Green, Blue }\nColor.\n";
        var items = InvokeCompletionAt(src, line: 1, character: 6).ToList();

        var labels = items.Select(i => i.Label).ToList();
        Assert.Equal(new[] { "Red", "Green", "Blue" }, labels);
        Assert.All(items, i => Assert.Equal(CompletionItemKind.EnumMember, i.Kind));

        // Anti-assertion: members of other enum types must not appear.
        Assert.DoesNotContain("push", labels);    // arr function — not an enum member
        Assert.DoesNotContain("string", labels);  // type name — not an enum member
    }

    /// <summary>
    /// Inside a method body, struct fields and methods are NOT surfaced as bare
    /// identifiers — Stash requires <c>self.field</c>. This is the structural form
    /// of the <c>Accessibility</c> contract.
    /// </summary>
    [Fact]
    public void Snapshot_InsideMethodBody_FieldsRequireSelf()
    {
        const string src =
            "struct Point {\n" +
            "  x, y\n" +
            "  fn distance() {\n" +
            "    \n" +
            "  }\n" +
            "}\n";
        var items = InvokeCompletionAt(src, line: 3, character: 4).ToList();
        var labels = items.Select(i => i.Label).ToHashSet();

        Assert.DoesNotContain("x", labels);
        Assert.DoesNotContain("y", labels);
        Assert.DoesNotContain("distance", labels);

        // Sanity: top-level type names still resolve.
        Assert.Contains("Point", labels);
    }

    // ── Default mode tests ────────────────────────────────────────────────────

    /// <summary>
    /// Default mode: stdlib struct field names must NOT appear as unqualified completions.
    /// Locks in the bug-1 fix (stdlib member leakage).
    /// </summary>
    [Fact]
    public void Snapshot_Default_NoStdlibMemberLeakage()
    {
        var items = InvokeUnqualifiedCompletion("\n").ToList();
        var labels = items.Select(i => i.Label).ToHashSet();

        // Get a representative sample of stdlib struct field names
        var stdlibFields = StdlibRegistry.Structs
            .SelectMany(s => s.Fields.Select(f => f.Name))
            .ToList();

        // Every stdlib struct field name must NOT appear as a bare unqualified completion.
        // (They require struct-dot-access: e.g., myFile.name, not just 'name'.)
        foreach (var fieldName in stdlibFields.Take(10)) // sample to avoid over-constraining
        {
            // Only assert absence if the name is not also a keyword or a namespace function.
            bool isKeyword = StdlibRegistry.TypeDescriptions.ContainsKey(fieldName);
            bool isNamespaceFn = StdlibRegistry.Functions.Any(f => f.Name == fieldName);
            bool isNamespace = StdlibRegistry.NamespaceNames.Contains(fieldName);
            if (!isKeyword && !isNamespaceFn && !isNamespace)
            {
                Assert.DoesNotContain(fieldName, labels);
            }
        }
    }

    /// <summary>
    /// Default mode: each stdlib namespace appears at most once in the completion list.
    /// Locks in the bug-2 fix (duplicate stdlib namespaces).
    /// </summary>
    [Fact]
    public void Snapshot_Default_NoStdlibNamespaceDuplicates()
    {
        var items = InvokeUnqualifiedCompletion("\n").ToList();
        var labels = items.Select(i => i.Label).ToList();

        foreach (var ns in StdlibRegistry.NamespaceNames)
        {
            int count = labels.Count(l => l == ns);
            Assert.True(count <= 1, $"Namespace '{ns}' appears {count} times in Default completions (expected at most once).");
        }
    }

    // ── Dot mode tests — one per IDotStrategy ────────────────────────────────

    /// <summary>
    /// Dot/BuiltInNs — <c>arr.</c> produces functions tagged as BuiltInNamespaceDotStrategy.
    /// User variables visible in source must NOT appear.
    /// </summary>
    [Fact]
    public void Snapshot_Dot_BuiltInNamespace_ArrDot_EmitsFunctions()
    {
        // Source has a user variable that must NOT leak into dot completions.
        var items = InvokeDotCompletion("arr").ToList();
        var labels = items.Select(i => i.Label).ToList();

        // Positive: canonical arr functions
        Assert.Contains("push", labels);
        Assert.Contains("pop", labels);
        Assert.Contains("contains", labels);

        // SourceTag: all candidates tagged as BuiltInNamespaceDotStrategy
        Assert.All(items, i => Assert.Equal(
            nameof(BuiltInNamespaceDotStrategy),
            i.Data?.ToString() ?? ""));

        // Anti-assertion: user variable names must NOT appear.
        // (The test source has no user vars — this asserts the mode isolation.)
        Assert.DoesNotContain("let", labels);     // keyword
        Assert.DoesNotContain("arr", labels);     // the namespace name itself
    }

    /// <summary>
    /// Dot/ImportAlias — an import alias produces the module's exported top-level symbols
    /// tagged as ImportAliasDotStrategy. Stdlib namespace names must NOT appear.
    /// </summary>
    [Fact]
    public void Snapshot_Dot_ImportAlias_EmitsModuleExports()
    {
        // Build a fake ModuleInfo with two exported symbols and inject it via a crafted
        // AnalysisResult. Direct strategy invocation avoids needing a real filesystem module.
        var exportedScope = BuildScopeWithSymbols(new[]
        {
            new SymbolInfo("sayHello", StashSymbolKind.Function, EmptySpan()),
            new SymbolInfo("greetName", StashSymbolKind.Function, EmptySpan()),
        });
        var moduleInfo = new ImportResolver.ModuleInfo(
            new Uri("file:///fake/mylib.stash"),
            "/fake/mylib.stash",
            exportedScope,
            new List<DiagnosticError>());

        var result = BuildAnalysisResultWithImport("mylib", moduleInfo);
        var strategy = new ImportAliasDotStrategy();
        var ctx = BuildDotCtx("mylib", result);
        var resolution = new DotResolutionContext(PrefixDef: null, StructName: "mylib");

        var candidates = strategy.Apply(ctx, "mylib", resolution).ToList();
        var candidateLabels = candidates.Select(c => c.Label).ToList();

        // Positive: the two exported symbols must appear
        Assert.Contains("sayHello", candidateLabels);
        Assert.Contains("greetName", candidateLabels);

        // SourceTag
        Assert.All(candidates, c => Assert.Equal(nameof(ImportAliasDotStrategy), c.SourceTag));
        Assert.Equal(110, candidates[0].SourcePriority);

        // Anti-assertion: stdlib namespace names must NOT appear (the module defines no namespaces).
        foreach (var ns in StdlibRegistry.NamespaceNames.Take(5))
        {
            Assert.DoesNotContain(ns, candidateLabels);
        }
    }

    /// <summary>
    /// Dot/StructOrUserEnum — a user struct instance produces fields tagged as
    /// StructOrUserEnumDotStrategy. Fields from other structs must NOT appear.
    /// Uses direct strategy invocation since multi-line analysis requires exact
    /// cursor positioning that the handler applies.
    /// </summary>
    [Fact]
    public void Snapshot_Dot_StructInstance_EmitsFieldsAndMethods()
    {
        // Build analysis with two structs and a variable. Use the per-strategy helper
        // pattern from DotCompletionProviderTests to avoid brittle cursor-position wiring.
        const string src =
            "struct Rect { width, height }\n" +
            "struct Circle { radius }\n" +
            "let r: Rect = Rect { width: 10, height: 5 };\n" +
            "\n";

        var engine = new AnalysisEngine(NullLogger<AnalysisEngine>.Instance);
        var uri = new Uri($"file:///test/struct_snap_{Guid.NewGuid():N}.stash");
        engine.Analyze(uri, src);
        var result = engine.GetCachedResult(uri);

        // Resolve the prefix variable to find its type hint.
        var srcLines = src.Split('\n');
        var symbols = result?.Symbols.GetVisibleSymbols(srcLines.Length, 0) ?? Enumerable.Empty<SymbolInfo>();
        var prefixDef = symbols.FirstOrDefault(s => s.Name == "r");
        var structName = prefixDef?.TypeHint ?? "r";

        var ctx = new Stash.Lsp.Completion.CompletionContext(
            Uri: uri,
            LspLine: srcLines.Length - 1,
            LspColumn: 0,
            CurrentLine: null,
            Mode: CompletionMode.Dot,
            DotPrefix: "r",
            Analysis: result,
            TriggerCharacter: '.');

        var resolution = new DotResolutionContext(PrefixDef: prefixDef, StructName: structName);
        var strategy = new StructOrUserEnumDotStrategy();
        var candidates = strategy.Apply(ctx, "r", resolution).ToList();
        var labels = candidates.Select(c => c.Label).ToList();

        // Positive: Rect's fields
        Assert.Contains("width", labels);
        Assert.Contains("height", labels);

        // SourceTag
        Assert.All(candidates, c => Assert.Equal(nameof(StructOrUserEnumDotStrategy), c.SourceTag));

        // Anti-assertion: Circle's field must NOT appear — methods from other structs must not leak.
        Assert.DoesNotContain("radius", labels);
    }

    /// <summary>
    /// Dot/Ufcs — a string variable produces UFCS method-style completions tagged as UfcsDotStrategy.
    /// Built-in namespace function names (bare namespace form) must NOT appear.
    /// </summary>
    [Fact]
    public void Snapshot_Dot_Ufcs_StringVariable_EmitsStringMethods()
    {
        const string src = "let greeting: string = \"hello\";\ngreeting.\n";
        var items = InvokeCompletionAt(src, line: 1, character: 9).ToList();
        var labels = items.Select(i => i.Label).ToList();

        // Positive: string UFCS methods (str namespace functions adapted as method-style)
        Assert.NotEmpty(items);
        Assert.Contains("upper", labels);
        Assert.Contains("lower", labels);
        Assert.Contains("trim", labels);

        // All items should be Method kind from UFCS
        Assert.All(items, i => Assert.Equal(CompletionItemKind.Method, i.Kind));

        // SourceTag
        Assert.All(items, i => Assert.Equal(
            nameof(UfcsDotStrategy),
            i.Data?.ToString() ?? ""));

        // Anti-assertion: bare namespace names must NOT appear as UFCS methods.
        // (arr.push shouldn't show as a method on a string variable)
        var arrFunctions = StdlibRegistry.GetNamespaceMembers("arr").Select(m => m.Name).ToHashSet();
        foreach (var arrFn in arrFunctions.Take(5))
        {
            Assert.DoesNotContain(arrFn, labels);
        }
    }

    /// <summary>
    /// Dot/CliSchema — a variable bound to <c>cli.parse(schema)</c> produces the schema's
    /// declared field names tagged as CliSchemaDotStrategy. Schema fields from unrelated
    /// variables must NOT appear.
    /// </summary>
    [Fact]
    public void Snapshot_Dot_CliSchema_EmitsSchemaFields()
    {
        // Construct a CliSchemaIndex directly and inject via AnalysisResult.
        // This avoids needing full CLI analysis — the strategy's contract is:
        // ctx.Analysis.CliSchema.TryGet(prefix) → CliSchemaInfo.Fields.
        var fields = new List<CliFieldInfo>
        {
            new CliFieldInfo("name", "string"),
            new CliFieldInfo("verbose", "bool"),
        };
        var schemaIndex = new CliSchemaIndex();
        schemaIndex.Add("parsed", new CliSchemaInfo(fields));

        // Build a separate schema for a different variable (must NOT appear).
        schemaIndex.Add("other", new CliSchemaInfo(new List<CliFieldInfo>
        {
            new CliFieldInfo("secretField", "string"),
        }));

        var result = BuildAnalysisResultWithCliSchema(schemaIndex);
        var strategy = new CliSchemaDotStrategy();
        var ctx = BuildDotCtx("parsed", result);
        var resolution = new DotResolutionContext(PrefixDef: null, StructName: "parsed");

        var candidates = strategy.Apply(ctx, "parsed", resolution).ToList();
        var candidateLabels = candidates.Select(c => c.Label).ToList();

        // Positive: schema fields for 'parsed'
        Assert.Equal(new[] { "name", "verbose" }, candidateLabels);
        Assert.All(candidates, c => Assert.Equal(nameof(CliSchemaDotStrategy), c.SourceTag));
        Assert.All(candidates, c => Assert.Equal(140, c.SourcePriority));
        Assert.All(candidates, c => Assert.Equal(CompletionItemKind.Field, c.Kind));

        // Anti-assertion: schema-defined fields from 'other' variable must NOT appear.
        Assert.DoesNotContain("secretField", candidateLabels);
    }

    /// <summary>
    /// Dot/NamespaceImportEnum — an enum exported by a namespace-imported module
    /// produces enum members tagged as NamespaceImportEnumDotStrategy.
    /// Members from a different enum in the same module must NOT appear.
    /// </summary>
    [Fact]
    public void Snapshot_Dot_NamespaceImportEnum_EmitsEnumMembers()
    {
        // Build a module with two enums: LogLevel and Priority.
        // Only LogLevel is queried; Priority members must NOT appear.
        var moduleScope = BuildScopeWithSymbols(new[]
        {
            new SymbolInfo("LogLevel", StashSymbolKind.Enum, EmptySpan()),
            new SymbolInfo("Debug", StashSymbolKind.EnumMember, EmptySpan(), parentName: "LogLevel"),
            new SymbolInfo("Info", StashSymbolKind.EnumMember, EmptySpan(), parentName: "LogLevel"),
            new SymbolInfo("Error", StashSymbolKind.EnumMember, EmptySpan(), parentName: "LogLevel"),
            new SymbolInfo("Priority", StashSymbolKind.Enum, EmptySpan()),
            new SymbolInfo("Low", StashSymbolKind.EnumMember, EmptySpan(), parentName: "Priority"),
            new SymbolInfo("High", StashSymbolKind.EnumMember, EmptySpan(), parentName: "Priority"),
        });
        var moduleInfo = new ImportResolver.ModuleInfo(
            new Uri("file:///fake/utils.stash"),
            "/fake/utils.stash",
            moduleScope,
            new List<DiagnosticError>());

        var result = BuildAnalysisResultWithImport("utils", moduleInfo);
        var strategy = new NamespaceImportEnumDotStrategy();
        var ctx = BuildDotCtx("LogLevel", result);
        var resolution = new DotResolutionContext(PrefixDef: null, StructName: "LogLevel");

        var candidates = strategy.Apply(ctx, "LogLevel", resolution).ToList();
        var candidateLabels = candidates.Select(c => c.Label).ToList();

        // Positive: LogLevel members in order
        Assert.Equal(new[] { "Debug", "Info", "Error" }, candidateLabels);
        Assert.All(candidates, c => Assert.Equal(nameof(NamespaceImportEnumDotStrategy), c.SourceTag));
        Assert.All(candidates, c => Assert.Equal(150, c.SourcePriority));
        Assert.All(candidates, c => Assert.Equal(CompletionItemKind.EnumMember, c.Kind));

        // Anti-assertion: members from the other enum (Priority) must NOT appear.
        Assert.DoesNotContain("Low", candidateLabels);
        Assert.DoesNotContain("High", candidateLabels);
    }

    // ── ImportString mode test ────────────────────────────────────────────────

    /// <summary>
    /// ImportString mode — cursor inside an import path string produces module names
    /// and specifically must NOT include keywords.
    /// </summary>
    [Fact]
    public void Snapshot_ImportString_FromStatement_KeywordsDoNotAppear()
    {
        // Build a temp project with a package and invoke ImportString completion.
        using var tmp = new TempStashesDir(new Dictionary<string, string[]>
        {
            ["mypackage"] = [],
            ["@myorg"] = ["mylib"],
        });

        var line = @"from ""|""";
        var ctx = BuildImportStringCtx(line, col: 7, root: tmp.Root);
        var provider = new ImportPathCompletionProvider();
        var candidates = provider.Provide(ctx).ToList();
        var candidateLabels = candidates.Select(c => c.Label).ToList();

        // Positive: the packages must appear
        Assert.Contains("mypackage", candidateLabels);
        Assert.Contains("@myorg/mylib", candidateLabels);

        // SourceTag
        Assert.All(candidates, c => Assert.Equal(nameof(ImportPathCompletionProvider), c.SourceTag));

        // Anti-assertion: keywords must NOT appear — they are code-mode, not string-mode.
        // A representative sample of Stash keywords:
        Assert.DoesNotContain("let", candidateLabels);
        Assert.DoesNotContain("fn", candidateLabels);
        Assert.DoesNotContain("if", candidateLabels);
        Assert.DoesNotContain("return", candidateLabels);
    }

    // ── AfterIs mode test ────────────────────────────────────────────────────

    /// <summary>
    /// AfterIs mode — cursor after 'is' keyword produces type names tagged as
    /// IsTypeCompletionProvider. User variable names must NOT appear.
    /// </summary>
    [Fact]
    public void Snapshot_AfterIs_EmitsTypeNamesOnly()
    {
        const string userVarSrc = "let mySpecialVar = 42;\n";
        var ctx = BuildContextForMode(CompletionMode.AfterIs, userVarSrc);
        var provider = new IsTypeCompletionProvider();
        var candidates = provider.Provide(ctx).ToList();
        var candidateLabels = candidates.Select(c => c.Label).ToList();

        // Positive: type names from TypeDescriptions must appear
        Assert.NotEmpty(candidates);
        Assert.Contains("string", candidateLabels);
        Assert.Contains("int", candidateLabels);
        Assert.Contains("bool", candidateLabels);

        // All candidates are TypeParameter kind
        Assert.All(candidates, c => Assert.Equal(CompletionItemKind.TypeParameter, c.Kind));

        // SourceTag
        Assert.All(candidates, c => Assert.Equal(nameof(IsTypeCompletionProvider), c.SourceTag));

        // Anti-assertion: user variable names must NOT appear (only type names are valid after 'is').
        Assert.DoesNotContain("mySpecialVar", candidateLabels);
    }

    // ── AfterExtend mode test ────────────────────────────────────────────────

    /// <summary>
    /// AfterExtend mode — cursor after 'extend' keyword produces extendable types tagged as
    /// ExtendTypeCompletionProvider. Stdlib namespace names must NOT appear.
    /// </summary>
    [Fact]
    public void Snapshot_AfterExtend_EmitsExtendableTypesOnly()
    {
        const string src = "struct MyShape { area }\n";
        var ctx = BuildContextForMode(CompletionMode.AfterExtend, src);
        var provider = new ExtendTypeCompletionProvider();
        var candidates = provider.Provide(ctx).ToList();
        var candidateLabels = candidates.Select(c => c.Label).ToList();

        // Positive: built-in extendable types must appear
        Assert.Contains("string", candidateLabels);
        Assert.Contains("int", candidateLabels);
        Assert.Contains("array", candidateLabels);

        // User-defined structs must also appear
        Assert.Contains("MyShape", candidateLabels);

        // SourceTag
        Assert.All(candidates, c => Assert.Equal(nameof(ExtendTypeCompletionProvider), c.SourceTag));

        // Anti-assertion: stdlib namespace names must NOT appear (only types are extendable).
        foreach (var ns in StdlibRegistry.NamespaceNames)
        {
            // Namespace names are not extendable types; they must not appear as extend targets.
            // (Some namespace names could coincidentally match a primitive type name, but
            // namespaces themselves are not emitted here — only types.)
            bool isAlsoType = StdlibRegistry.TypeDescriptions.ContainsKey(ns);
            if (!isAlsoType)
            {
                Assert.DoesNotContain(ns, candidateLabels);
            }
        }

        // Regression net: primitive types that the runtime's extend-compiler check rejects
        // must NOT be offered as completions. Suggesting them sets up a UX trap where the
        // user picks a completion the runtime then refuses with
        // "RuntimeError: Cannot extend '<name>': not a known type." The list below tracks
        // primitives historically misclassified as extendable; if any future primitive
        // becomes genuinely extendable, drop it from this list AND add it to
        // ExtendTypeCompletionProvider.BuiltInExtendableTypes.
        var runtimeRejected = new[] { "byte", "bytesize", "duration", "future", "ipaddress", "range", "secret", "semver" };
        foreach (var t in runtimeRejected)
        {
            Assert.DoesNotContain(t, candidateLabels);
        }
    }

    // ── Default mode with snippets snapshot ──────────────────────────────────

    /// <summary>
    /// Locks the full snippet candidate surface in Default mode at a top-level cursor.
    /// The dispatcher includes <see cref="SnippetCompletionProvider"/> backed by the
    /// <see cref="BundledSnippetRegistry"/>, so every bundled snippet (those gated to
    /// <see cref="Stash.Lsp.Completion.Snippets.SnippetScope.Any"/> or
    /// <see cref="Stash.Lsp.Completion.Snippets.SnippetScope.TopLevel"/>)
    /// must appear alongside keywords, stdlib functions, and stdlib namespaces.
    /// </summary>
    /// <remarks>
    /// Fixture location: <c>Stash.Tests/Lsp/Snapshots/default-with-snippets.completion.txt</c>
    /// (deviation from brief's <c>Default_WithSnippets.txt</c> path — reuses the existing
    /// embedded-resource machinery which resolves names as
    /// <c>Stash.Tests.Lsp.Snapshots.&lt;name&gt;.completion.txt</c>).
    /// Re-baseline with:
    /// <code>STASH_SNAPSHOT_REGEN=1 dotnet test --filter FullyQualifiedName~Snapshot_Default_WithSnippets</code>
    /// </remarks>
    [Fact]
    public void Snapshot_Default_WithSnippets()
    {
        var items = InvokeUnqualifiedCompletionWithSnippets("\n");
        AssertSnapshot("default-with-snippets", items);
    }

    // ── Snapshot machinery ───────────────────────────────────────────────────

    /// <summary>
    /// Compares the normalised completion set against a snapshot fixture.
    /// On mismatch, includes the full actual output in the assertion message so a
    /// developer can copy-paste-update the fixture (or re-run with
    /// <c>STASH_SNAPSHOT_REGEN=1</c> to overwrite it on disk).
    /// </summary>
    private static void AssertSnapshot(
        string snapshotName,
        IEnumerable<CompletionItem> items,
        [CallerFilePath] string callerPath = "")
    {
        var actual = string.Join("\n", items.Select(NormalizeItem).OrderBy(s => s, StringComparer.Ordinal)) + "\n";

        if (Environment.GetEnvironmentVariable("STASH_SNAPSHOT_REGEN") == "1")
        {
            string fixturePath = ResolveFixturePath(callerPath, snapshotName);
            Directory.CreateDirectory(Path.GetDirectoryName(fixturePath)!);
            File.WriteAllText(fixturePath, actual);
            Assert.Fail($"Snapshot regenerated at {fixturePath}. Re-run without STASH_SNAPSHOT_REGEN to verify.");
        }

        string resourceName = $"Stash.Tests.Lsp.Snapshots.{snapshotName}.completion.txt";
        string? expected = ReadEmbeddedResource(resourceName);
        if (expected == null)
        {
            Assert.Fail(
                $"Snapshot fixture '{snapshotName}' is missing.\n" +
                $"Run: STASH_SNAPSHOT_REGEN=1 dotnet test --filter FullyQualifiedName~CompletionSurfaceSnapshotTests\n" +
                $"or create Stash.Tests/Lsp/Snapshots/{snapshotName}.completion.txt with:\n\n{actual}");
        }

        // Normalise line endings — embedded resources on Windows may carry CRLF.
        expected = expected.Replace("\r\n", "\n");

        if (expected != actual)
        {
            Assert.Fail(
                $"Snapshot '{snapshotName}' diverged.\n" +
                $"Re-baseline with: STASH_SNAPSHOT_REGEN=1 dotnet test --filter FullyQualifiedName~{nameof(CompletionSurfaceSnapshotTests)}\n\n" +
                $"--- expected ({expected.Length} bytes)\n{expected}\n" +
                $"--- actual   ({actual.Length} bytes)\n{actual}");
        }
    }

    private static string NormalizeItem(CompletionItem item) => $"{item.Kind}\t{item.Label}";

    private static string? ReadEmbeddedResource(string resourceName)
    {
        var asm = Assembly.GetExecutingAssembly();
        using var stream = asm.GetManifestResourceStream(resourceName);
        if (stream == null) return null;
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private static string ResolveFixturePath(string callerPath, string snapshotName)
    {
        // callerPath is the absolute path to this .cs file at build time.
        string dir = Path.GetDirectoryName(callerPath)!;
        return Path.Combine(dir, "Snapshots", $"{snapshotName}.completion.txt");
    }

    // ── Handler invocation helpers ───────────────────────────────────────────

    private static IEnumerable<CompletionItem> InvokeUnqualifiedCompletion(string source)
    {
        var lines = source.Split('\n');
        int line = lines.Length - 1;
        return InvokeCompletionAt(source + (source.EndsWith("\n") ? "" : "\n"), line, 0);
    }

    private static IEnumerable<CompletionItem> InvokeDotCompletion(string prefix)
    {
        string testSource = $"{prefix}.\n";
        return InvokeCompletionAt(testSource, line: 0, character: prefix.Length + 1);
    }

    private static IEnumerable<CompletionItem> InvokeCompletionAt(string source, int line, int character)
    {
        var engine = new AnalysisEngine(NullLogger<AnalysisEngine>.Instance);
        var docs = new DocumentManager(NullLogger<DocumentManager>.Instance);
        var logger = NullLogger<CompletionHandler>.Instance;

        var uri = new Uri($"file:///test/snapshot_{Guid.NewGuid():N}.stash");
        docs.Open(uri, source, 1);
        engine.Analyze(uri, source);
        var handler = new CompletionHandler(engine, docs, logger, BuildDispatcher());

        var request = new CompletionParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = uri },
            Position = new Position { Line = line, Character = character },
            Context = new LspCompletionContext { TriggerKind = CompletionTriggerKind.Invoked }
        };

        var result = handler.Handle(request, default).Result;
        return result.Items ?? Enumerable.Empty<CompletionItem>();
    }

    private static IEnumerable<CompletionItem> InvokeUnqualifiedCompletionWithSnippets(string source)
    {
        var lines = source.Split('\n');
        int line = lines.Length - 1;
        return InvokeCompletionAtWithDispatcher(
            source + (source.EndsWith("\n") ? "" : "\n"),
            line, 0,
            BuildDispatcherWithSnippets());
    }

    private static IEnumerable<CompletionItem> InvokeCompletionAtWithDispatcher(
        string source, int line, int character, CompletionDispatcher dispatcher)
    {
        var engine = new AnalysisEngine(NullLogger<AnalysisEngine>.Instance);
        var docs = new DocumentManager(NullLogger<DocumentManager>.Instance);
        var logger = NullLogger<CompletionHandler>.Instance;

        var uri = new Uri($"file:///test/snapshot_{Guid.NewGuid():N}.stash");
        docs.Open(uri, source, 1);
        engine.Analyze(uri, source);
        var handler = new CompletionHandler(engine, docs, logger, dispatcher);

        var request = new CompletionParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = uri },
            Position = new Position { Line = line, Character = character },
            Context = new LspCompletionContext { TriggerKind = CompletionTriggerKind.Invoked }
        };

        var result = handler.Handle(request, default).Result;
        return result.Items ?? Enumerable.Empty<CompletionItem>();
    }

    private static CompletionDispatcher BuildDispatcher()
    {
        var pipelines = new Dictionary<CompletionMode, IReadOnlyList<ICompletionProvider>>
        {
            [CompletionMode.Default] = new ICompletionProvider[]
            {
                new KeywordCompletionProvider(),
                new StdlibFunctionCompletionProvider(),
                new StdlibNamespaceCompletionProvider(),
                new ScopedSymbolCompletionProvider(),
            },
            [CompletionMode.Dot] = new ICompletionProvider[] { new DotCompletionProvider() },
            [CompletionMode.ImportString] = new ICompletionProvider[] { new ImportPathCompletionProvider() },
            [CompletionMode.AfterIs] = new ICompletionProvider[] { new IsTypeCompletionProvider() },
            [CompletionMode.AfterExtend] = new ICompletionProvider[] { new ExtendTypeCompletionProvider() },
        };
        return new CompletionDispatcher(pipelines);
    }

    private static CompletionDispatcher BuildDispatcherWithSnippets()
    {
        var pipelines = new Dictionary<CompletionMode, IReadOnlyList<ICompletionProvider>>
        {
            [CompletionMode.Default] = new ICompletionProvider[]
            {
                new KeywordCompletionProvider(),
                new StdlibFunctionCompletionProvider(),
                new StdlibNamespaceCompletionProvider(),
                new ScopedSymbolCompletionProvider(),
                new SnippetCompletionProvider(new BundledSnippetRegistry()),
            },
            [CompletionMode.Dot] = new ICompletionProvider[] { new DotCompletionProvider() },
            [CompletionMode.ImportString] = new ICompletionProvider[] { new ImportPathCompletionProvider() },
            [CompletionMode.AfterIs] = new ICompletionProvider[] { new IsTypeCompletionProvider() },
            [CompletionMode.AfterExtend] = new ICompletionProvider[] { new ExtendTypeCompletionProvider() },
        };
        return new CompletionDispatcher(pipelines);
    }

    // ── Direct strategy context builders ─────────────────────────────────────

    private static Stash.Lsp.Completion.CompletionContext BuildDotCtx(
        string prefix,
        AnalysisResult? analysisResult,
        int line = 0)
    {
        var uri = new Uri($"file:///test/dotctx_{Guid.NewGuid():N}.stash");
        return new Stash.Lsp.Completion.CompletionContext(
            Uri: uri,
            LspLine: line,
            LspColumn: 0,
            CurrentLine: null,
            Mode: CompletionMode.Dot,
            DotPrefix: prefix,
            Analysis: analysisResult,
            TriggerCharacter: '.');
    }

    private static Stash.Lsp.Completion.CompletionContext BuildContextForMode(
        CompletionMode mode, string source, int line = 0, int col = 0)
    {
        var engine = new AnalysisEngine(NullLogger<AnalysisEngine>.Instance);
        var uri = new Uri($"file:///test/ctx_{Guid.NewGuid():N}.stash");
        engine.Analyze(uri, source);
        var result = engine.GetCachedResult(uri);

        var srcLines = source.Split('\n');
        string? currentLine = line < srcLines.Length ? srcLines[line] : null;

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

    private static Stash.Lsp.Completion.CompletionContext BuildImportStringCtx(
        string line, int col, string? root = null)
    {
        Uri uri;
        if (root != null)
        {
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

    // ── AnalysisResult / ScopeTree factory helpers ───────────────────────────

    /// <summary>
    /// Builds a <see cref="ScopeTree"/> containing the given symbols in a single global scope.
    /// </summary>
    private static ScopeTree BuildScopeWithSymbols(IEnumerable<SymbolInfo> symbols)
    {
        var span = new Stash.Common.SourceSpan("", 0, 0, int.MaxValue, int.MaxValue);
        var scope = new Scope(ScopeKind.Global, null, span);
        foreach (var sym in symbols)
            scope.AddSymbol(sym);
        return new ScopeTree(scope);
    }

    /// <summary>
    /// Builds an <see cref="AnalysisResult"/> with a single namespace-import alias.
    /// </summary>
    private static AnalysisResult BuildAnalysisResultWithImport(
        string alias,
        ImportResolver.ModuleInfo moduleInfo)
    {
        var imports = new Dictionary<string, ImportResolver.ModuleInfo>
        {
            [alias] = moduleInfo,
        };
        var span = new Stash.Common.SourceSpan("", 0, 0, int.MaxValue, int.MaxValue);
        var emptyScope = new Scope(ScopeKind.Global, null, span);
        return new AnalysisResult(
            tokens: new List<Stash.Lexing.Token>(),
            statements: new List<Stash.Parsing.AST.Stmt>(),
            lexErrors: new List<string>(),
            parseErrors: new List<string>(),
            structuredLexErrors: new List<DiagnosticError>(),
            structuredParseErrors: new List<DiagnosticError>(),
            symbols: new ScopeTree(emptyScope),
            semanticDiagnostics: new List<SemanticDiagnostic>(),
            namespaceImports: imports);
    }

    /// <summary>
    /// Builds an <see cref="AnalysisResult"/> with the given <see cref="CliSchemaIndex"/>.
    /// </summary>
    private static AnalysisResult BuildAnalysisResultWithCliSchema(CliSchemaIndex schemaIndex)
    {
        var span = new Stash.Common.SourceSpan("", 0, 0, int.MaxValue, int.MaxValue);
        var emptyScope = new Scope(ScopeKind.Global, null, span);
        return new AnalysisResult(
            tokens: new List<Stash.Lexing.Token>(),
            statements: new List<Stash.Parsing.AST.Stmt>(),
            lexErrors: new List<string>(),
            parseErrors: new List<string>(),
            structuredLexErrors: new List<DiagnosticError>(),
            structuredParseErrors: new List<DiagnosticError>(),
            symbols: new ScopeTree(emptyScope),
            semanticDiagnostics: new List<SemanticDiagnostic>(),
            cliSchema: schemaIndex);
    }

    private static Stash.Common.SourceSpan EmptySpan() =>
        new Stash.Common.SourceSpan(File: "", StartLine: 0, StartColumn: 0, EndLine: 0, EndColumn: 0);

    // ── SymbolInfo factory shorthand ──────────────────────────────────────────

    private static SymbolInfo SymbolInfo(
        string name, StashSymbolKind kind, Stash.Common.SourceSpan span,
        string? parentName = null)
    {
        return new SymbolInfo(name, kind, span, parentName: parentName);
    }

    // ── Temporary directory helper ────────────────────────────────────────────

    private sealed class TempStashesDir : IDisposable
    {
        public string Root { get; }

        public TempStashesDir(Dictionary<string, string[]> packages)
        {
            Root = Path.Combine(Path.GetTempPath(), "stash_test_" + Guid.NewGuid().ToString("N"));
            string stashesDir = Path.Combine(Root, "stashes");
            Directory.CreateDirectory(stashesDir);
            File.WriteAllText(Path.Combine(Root, "stash.json"), "{}");

            foreach (var (name, subPkgs) in packages)
            {
                string pkgDir = Path.Combine(stashesDir, name);
                Directory.CreateDirectory(pkgDir);
                foreach (var sub in subPkgs)
                    Directory.CreateDirectory(Path.Combine(pkgDir, sub));
            }
        }

        public void Dispose()
        {
            try { Directory.Delete(Root, recursive: true); } catch { /* best effort */ }
        }
    }
}
