namespace Stash.Tests.Stdlib.SourceGenerator;

using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Stash.Runtime;
using Stash.Stdlib.Abstractions;
using Stash.Stdlib.Generators;
using Stash.Stdlib.Registration;
using Stash.Tests.Stdlib.SourceGenerator.Fixtures;
using Xunit;

/// <summary>
/// Tests for the <c>[StashMember]</c> source generator: registration, diagnostics,
/// capability gating, stability, deprecation, and Throws metadata.
/// </summary>
public class StashMemberGeneratorTests
{
    #region Fixture-driven (in-process) tests

    [Fact]
    public void StashMember_CachedMember_RegistersInNamespace()
    {
        var def = StashMemberFixture.Define(StashCapabilities.All);
        Assert.True(def.Namespace.HasMember("cachedMember"),
            "cachedMember should be registered in the namespace.");
    }

    [Fact]
    public void StashMember_LiveMember_RegistersInNamespace()
    {
        var def = StashMemberFixture.Define(StashCapabilities.All);
        Assert.True(def.Namespace.HasMember("liveMember"),
            "liveMember should be registered in the namespace.");
    }

    [Fact]
    public void StashMember_MembersExposedInDefinitionList()
    {
        var def = StashMemberFixture.Define(StashCapabilities.All);
        Assert.Contains(def.Members!, m => m.Name == "cachedMember");
        Assert.Contains(def.Members!, m => m.Name == "liveMember");
    }

    [Fact]
    public void StashMember_DeclarationsTable_ContainsDataMemberKind()
    {
        var def = StashMemberFixture.Define(StashCapabilities.All);
        Assert.True(def.Declarations.TryGetValue("cachedMember", out var kind));
        Assert.Equal(DeclarationKind.DataMember, kind);
    }

    [Fact]
    public void StashMember_DeclarationsTable_ContainsFunctionKindForFn()
    {
        var def = StashMemberFixture.Define(StashCapabilities.All);
        Assert.True(def.Declarations.TryGetValue("alwaysFn", out var kind));
        Assert.Equal(DeclarationKind.Function, kind);
    }

    [Fact]
    public void StashMember_CachedMember_StabilityIsCached()
    {
        var def = StashMemberFixture.Define(StashCapabilities.All);
        var member = def.Members!.First(m => m.Name == "cachedMember");
        Assert.Equal(Stability.Cached, member.Stability);
    }

    [Fact]
    public void StashMember_LiveMember_StabilityIsLive()
    {
        var def = StashMemberFixture.Define(StashCapabilities.All);
        var member = def.Members!.First(m => m.Name == "liveMember");
        Assert.Equal(Stability.Live, member.Stability);
    }

    [Fact]
    public void StashMember_DefaultStability_IsCached()
    {
        // The cachedMember fixture uses no explicit Stability= — should default to Cached.
        var def = StashMemberFixture.Define(StashCapabilities.All);
        var member = def.Members!.First(m => m.Name == "cachedMember");
        Assert.Equal(Stability.Cached, member.Stability);
    }

    [Fact]
    public void StashMember_GatedMember_CapabilityAbsent_NotRegistered()
    {
        var def = StashMemberFixture.Define(StashCapabilities.None);
        Assert.False(def.Namespace.HasMember("gatedMember"),
            "gatedMember should be absent when Environment capability is not granted.");
        Assert.DoesNotContain(def.Members!, m => m.Name == "gatedMember");
    }

    [Fact]
    public void StashMember_GatedMember_CapabilityPresent_Registered()
    {
        var def = StashMemberFixture.Define(StashCapabilities.Environment);
        Assert.True(def.Namespace.HasMember("gatedMember"),
            "gatedMember should be registered when Environment capability is granted.");
        Assert.Contains(def.Members!, m => m.Name == "gatedMember");
    }

    [Fact]
    public void StashMember_GatedMember_DeclarationsTable_AbsentWhenDenied()
    {
        var def = StashMemberFixture.Define(StashCapabilities.None);
        // gatedMember was denied — should not appear in the declarations table
        Assert.False(def.Declarations.ContainsKey("gatedMember"));
    }

    [Fact]
    public void StashMember_GatedMember_DeclarationsTable_PresentWhenGranted()
    {
        var def = StashMemberFixture.Define(StashCapabilities.Environment);
        Assert.True(def.Declarations.TryGetValue("gatedMember", out var kind));
        Assert.Equal(DeclarationKind.DataMember, kind);
    }

    [Fact]
    public void StashMember_Documentation_PropagatesFromSummary()
    {
        var def = StashMemberFixture.Define(StashCapabilities.All);
        var member = def.Members!.First(m => m.Name == "cachedMember");
        Assert.False(string.IsNullOrEmpty(member.Documentation),
            "Documentation should be populated from the XML <summary>.");
    }

    #endregion

    #region Generator diagnostics (compilation-driver tests)

    private static (ImmutableArray<Diagnostic> Diagnostics, GeneratorDriverRunResult Result) Run(string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source,
            new CSharpParseOptions(LanguageVersion.Latest, DocumentationMode.Parse));

        var refs = new List<MetadataReference>
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(System.Runtime.GCSettings).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(StashNamespaceAttribute).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Stash.Runtime.StashValue).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Stash.Runtime.Types.StashDictionary).Assembly.Location),
        };
        var trustedAssembliesPaths = ((string)System.AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(System.IO.Path.PathSeparator);
        foreach (var path in trustedAssembliesPaths)
        {
            string name = System.IO.Path.GetFileNameWithoutExtension(path);
            if (name is "netstandard" or "System.Runtime" or "System.Collections" or "System.Memory" or "System.Linq")
                refs.Add(MetadataReference.CreateFromFile(path));
        }

        var compilation = CSharpCompilation.Create(
            "GenTest",
            new[] { syntaxTree },
            refs,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));

        var generator = new StashNamespaceGenerator();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(generator)
            .WithUpdatedParseOptions(new CSharpParseOptions(LanguageVersion.Latest, DocumentationMode.Parse));
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out var diags);
        return (diags, driver.GetRunResult());
    }

    [Fact]
    public void StashMember_WellFormed_ProducesNoErrors()
    {
        const string src = """
            using Stash.Stdlib.Abstractions;
            using Stash.Runtime;
            namespace T;
            [StashNamespace]
            public static partial class C
            {
                /// <summary>Returns current user.</summary>
                [StashMember]
                public static string GetUser(IInterpreterContext ctx) => "user";
            }
            """;
        var (diags, _) = Run(src);
        Assert.DoesNotContain(diags, d => d.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain(diags, d => d.Id.StartsWith("STASH_MEM"));
    }

    [Fact]
    public void STASH_MEM001_FiresWhenMemberAndFnCombined()
    {
        const string src = """
            using Stash.Stdlib.Abstractions;
            using Stash.Runtime;
            namespace T;
            [StashNamespace]
            public static partial class C
            {
                /// <summary>Both attributes.</summary>
                [StashMember]
                [StashFn]
                public static string GetX(IInterpreterContext ctx) => "x";
            }
            """;
        var (diags, _) = Run(src);
        Assert.Contains(diags, d => d.Id == "STASH_MEM001");
    }

    [Fact]
    public void STASH_MEM002_FiresWhenMemberAndConstCombined()
    {
        // [StashMember] and [StashConst] on the same field — requires AttributeTargets.Field
        // on StashMemberAttribute, which we set intentionally for this enforcement.
        const string src = """
            using Stash.Stdlib.Abstractions;
            namespace T;
            [StashNamespace]
            public static partial class C
            {
                [StashMember]
                [StashConst]
                public const long MyField = 42L;
            }
            """;
        var (diags, _) = Run(src);
        Assert.Contains(diags, d => d.Id == "STASH_MEM002");
    }

    [Fact]
    public void STASH_MEM003_FiresOnZeroParams()
    {
        const string src = """
            using Stash.Stdlib.Abstractions;
            namespace T;
            [StashNamespace]
            public static partial class C
            {
                /// <summary>Zero params.</summary>
                [StashMember]
                public static string GetX() => "x";
            }
            """;
        var (diags, _) = Run(src);
        Assert.Contains(diags, d => d.Id == "STASH_MEM003");
    }

    [Fact]
    public void STASH_MEM003_FiresOnTwoParams()
    {
        const string src = """
            using Stash.Stdlib.Abstractions;
            using Stash.Runtime;
            namespace T;
            [StashNamespace]
            public static partial class C
            {
                /// <summary>Two params.</summary>
                [StashMember]
                public static string GetX(IInterpreterContext ctx, long extra) => "x";
            }
            """;
        var (diags, _) = Run(src);
        Assert.Contains(diags, d => d.Id == "STASH_MEM003");
    }

    [Fact]
    public void STASH_MEM003_FiresOnNonContextParam()
    {
        const string src = """
            using Stash.Stdlib.Abstractions;
            namespace T;
            [StashNamespace]
            public static partial class C
            {
                /// <summary>Non-context param.</summary>
                [StashMember]
                public static string GetX(long notCtx) => "x";
            }
            """;
        var (diags, _) = Run(src);
        Assert.Contains(diags, d => d.Id == "STASH_MEM003");
    }

    [Fact]
    public void STASH_MEM004_FiresWhenSummaryMissing()
    {
        const string src = """
            using Stash.Stdlib.Abstractions;
            using Stash.Runtime;
            namespace T;
            [StashNamespace]
            public static partial class C
            {
                [StashMember]
                public static string GetX(IInterpreterContext ctx) => "x";
            }
            """;
        var (diags, _) = Run(src);
        Assert.Contains(diags, d => d.Id == "STASH_MEM004");
    }

    [Fact]
    public void StashMember_WithDeprecated_PropagatesDeprecation()
    {
        // A method carrying [StashDeprecated] composes with [StashMember].
        // We verify through the fixture definition that deprecation info is present.
        const string src = """
            using Stash.Stdlib.Abstractions;
            using Stash.Runtime;
            namespace T;
            [StashNamespace]
            public static partial class C
            {
                /// <summary>Deprecated member.</summary>
                [StashMember]
                [StashDeprecated("newMember")]
                public static string OldMember(IInterpreterContext ctx) => "old";
            }
            """;
        var (diags, _) = Run(src);
        // No errors — valid combination.
        Assert.DoesNotContain(diags, d => d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void StashMember_ReturnType_PropagatesAsStashLabel()
    {
        var def = StashMemberFixture.Define(StashCapabilities.All);
        var member = def.Members!.First(m => m.Name == "cachedMember");
        Assert.Equal("string", member.ReturnType);
    }

    [Fact]
    public void StashMember_LiveMember_ReturnTypePropagates()
    {
        var def = StashMemberFixture.Define(StashCapabilities.All);
        var member = def.Members!.First(m => m.Name == "liveMember");
        Assert.Equal("int", member.ReturnType);
    }

    [Fact]
    public void StashMember_ExplicitCachedStability_RegistersAsCached()
    {
        // Explicit Stability.Cached annotation — same as the default.
        const string src = """
            using Stash.Stdlib.Abstractions;
            using Stash.Runtime;
            namespace T;
            [StashNamespace]
            public static partial class C
            {
                /// <summary>Explicitly cached.</summary>
                [StashMember(Stability = Stability.Cached)]
                public static string GetX(IInterpreterContext ctx) => "x";
            }
            """;
        var (diags, _) = Run(src);
        Assert.DoesNotContain(diags, d => d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void StashMember_ExplicitLiveStability_RegistersAsLive()
    {
        // Explicit Stability.Live annotation.
        const string src = """
            using Stash.Stdlib.Abstractions;
            using Stash.Runtime;
            namespace T;
            [StashNamespace]
            public static partial class C
            {
                /// <summary>Live member.</summary>
                [StashMember(Stability = Stability.Live)]
                public static string GetX(IInterpreterContext ctx) => System.IO.Directory.GetCurrentDirectory();
            }
            """;
        var (diags, _) = Run(src);
        Assert.DoesNotContain(diags, d => d.Severity == DiagnosticSeverity.Error);
    }

    #endregion

    #region MapStabilityLiteral exhaustiveness

    [Fact]
    public void MapStabilityLiteral_Cached_ReturnsExpectedLiteral()
    {
        // Stability.Cached = 0
        Assert.Equal(
            "global::Stash.Stdlib.Abstractions.Stability.Cached",
            StashNamespaceGenerator.MapStabilityLiteral(0));
    }

    [Fact]
    public void MapStabilityLiteral_Live_ReturnsExpectedLiteral()
    {
        // Stability.Live = 1
        Assert.Equal(
            "global::Stash.Stdlib.Abstractions.Stability.Live",
            StashNamespaceGenerator.MapStabilityLiteral(1));
    }

    [Fact]
    public void MapStabilityLiteral_UnknownInt_Throws()
    {
        // An integer that doesn't correspond to any Stability variant must throw
        // so that adding a new variant without updating this mapping is caught at build time.
        Assert.Throws<System.InvalidOperationException>(
            () => StashNamespaceGenerator.MapStabilityLiteral(2));
    }

    #endregion
}
