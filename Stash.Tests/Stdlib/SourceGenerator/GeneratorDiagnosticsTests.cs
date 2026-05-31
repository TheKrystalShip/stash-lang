namespace Stash.Tests.Stdlib.SourceGenerator;

using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Stash.Stdlib.Abstractions;
using Stash.Stdlib.Generators;
using Xunit;

public class GeneratorDiagnosticsTests
{
    private static (ImmutableArray<Diagnostic> Diagnostics, GeneratorDriverRunResult Result) Run(string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest, DocumentationMode.Parse));

        var refs = new List<MetadataReference>
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(System.Runtime.GCSettings).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(StashNamespaceAttribute).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Stash.Runtime.StashValue).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Stash.Runtime.Types.StashDictionary).Assembly.Location),
        };
        // Add netstandard / private corelibs referenced transitively.
        var trustedAssembliesPaths = ((string)System.AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(System.IO.Path.PathSeparator);
        foreach (var path in trustedAssembliesPaths)
        {
            string name = System.IO.Path.GetFileNameWithoutExtension(path);
            if (name == "netstandard" || name == "System.Runtime" || name == "System.Collections" || name == "System.Memory" || name == "System.Linq")
            {
                refs.Add(MetadataReference.CreateFromFile(path));
            }
        }

        var compilation = CSharpCompilation.Create(
            "GenTest",
            new[] { syntaxTree },
            refs,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        var generator = new StashNamespaceGenerator();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(generator)
            .WithUpdatedParseOptions(new CSharpParseOptions(LanguageVersion.Latest, DocumentationMode.Parse));
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out var diags);
        return (diags, driver.GetRunResult());
    }

    [Fact]
    public void STASH_GEN001_FiresOnUnsupportedParameter()
    {
        const string src = """
            using Stash.Stdlib.Abstractions;
            namespace T;
            [StashNamespace]
            public static partial class C
            {
                /// <summary>x</summary>
                /// <param name="d">date</param>
                [StashFn]
                public static long F(System.DateTime d) => 0;
            }
            """;
        var (diags, _) = Run(src);
        Assert.Contains(diags, d => d.Id == "STASH_GEN001");
    }

    [Fact]
    public void STASH_GEN003_FiresWhenContextIsNotFirst()
    {
        const string src = """
            using Stash.Stdlib.Abstractions;
            using Stash.Runtime;
            namespace T;
            [StashNamespace]
            public static partial class C
            {
                /// <summary>x</summary>
                /// <param name="n">n</param>
                [StashFn]
                public static long F(long n, IInterpreterContext ctx) => n;
            }
            """;
        var (diags, _) = Run(src);
        Assert.Contains(diags, d => d.Id == "STASH_GEN003");
    }

    [Fact]
    public void STASH_GEN005_FiresOnDuplicateFunctionName()
    {
        const string src = """
            using Stash.Stdlib.Abstractions;
            namespace T;
            [StashNamespace]
            public static partial class C
            {
                /// <summary>a</summary>
                /// <param name="n">n</param>
                [StashFn(Name = "dup")]
                public static long A(long n) => n;
                /// <summary>b</summary>
                /// <param name="n">n</param>
                [StashFn(Name = "dup")]
                public static long B(long n) => n;
            }
            """;
        var (diags, _) = Run(src);
        Assert.Contains(diags, d => d.Id == "STASH_GEN005");
    }

    [Fact]
    public void STASH_GEN007_FiresOnConsecutiveUppercase()
    {
        const string src = """
            using Stash.Stdlib.Abstractions;
            namespace T;
            [StashNamespace]
            public static partial class C
            {
                /// <summary>x</summary>
                /// <param name="s">s</param>
                [StashFn]
                public static string URLEncode(string s) => s;
            }
            """;
        var (diags, _) = Run(src);
        Assert.Contains(diags, d => d.Id == "STASH_GEN007");
    }

    [Fact]
    public void STASH_GEN008_FiresWhenClassNotPartialStatic()
    {
        const string src = """
            using Stash.Stdlib.Abstractions;
            namespace T;
            [StashNamespace]
            public class C
            {
                /// <summary>x</summary>
                /// <param name="n">n</param>
                [StashFn]
                public long F(long n) => n;
            }
            """;
        var (diags, _) = Run(src);
        Assert.Contains(diags, d => d.Id == "STASH_GEN008");
    }

    [Fact]
    public void STASH_DOC001_FiresWhenSummaryMissing()
    {
        const string src = """
            using Stash.Stdlib.Abstractions;
            namespace T;
            [StashNamespace]
            public static partial class C
            {
                [StashFn]
                public static long F(long n) => n;
            }
            """;
        var (diags, _) = Run(src);
        Assert.Contains(diags, d => d.Id == "STASH_DOC001");
    }

    [Fact]
    public void NoDiagnostics_ForWellFormedNamespace()
    {
        const string src = """
            using Stash.Stdlib.Abstractions;
            namespace T;
            [StashNamespace]
            public static partial class C
            {
                /// <summary>Returns n.</summary>
                /// <param name="n">The number.</param>
                [StashFn]
                public static long Identity(long n) => n;
            }
            """;
        var (diags, _) = Run(src);
        Assert.DoesNotContain(diags, d => d.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain(diags, d => d.Id.StartsWith("STASH_DOC"));
    }

    // ── STSG014: stray [StashFn]/[StashMember]/[StashConst] on non-[StashNamespace] classes ──

    [Fact]
    public void STSG014_FiresWhenStashFnIsOnNonNamespaceClass()
    {
        const string src = """
            using Stash.Stdlib.Abstractions;
            namespace T;
            // No [StashNamespace] — [StashFn] here is a stray annotation.
            public static partial class C
            {
                /// <summary>Returns n.</summary>
                /// <param name="n">The number.</param>
                [StashFn]
                public static long Identity(long n) => n;
            }
            """;
        var (diags, _) = Run(src);
        Assert.Contains(diags, d => d.Id == "STSG014");
    }

    [Fact]
    public void STSG014_FiresWhenStashMemberIsOnNonNamespaceClass()
    {
        const string src = """
            using Stash.Stdlib.Abstractions;
            using Stash.Runtime;
            namespace T;
            // No [StashNamespace] — [StashMember] here is a stray annotation.
            public static partial class C
            {
                /// <summary>Returns value.</summary>
                [StashMember]
                public static long Len(IInterpreterContext ctx) => 0;
            }
            """;
        var (diags, _) = Run(src);
        Assert.Contains(diags, d => d.Id == "STSG014");
    }

    [Fact]
    public void STSG014_FiresWhenStashConstIsOnNonNamespaceClass()
    {
        const string src = """
            using Stash.Stdlib.Abstractions;
            namespace T;
            // No [StashNamespace] — [StashConst] here is a stray annotation.
            public static partial class C
            {
                [StashConst]
                public const long Pi = 3;
            }
            """;
        var (diags, _) = Run(src);
        Assert.Contains(diags, d => d.Id == "STSG014");
    }

    [Fact]
    public void STSG014_DoesNotFireWhenAnnotationsAreOnStashNamespaceClass()
    {
        const string src = """
            using Stash.Stdlib.Abstractions;
            using Stash.Runtime;
            namespace T;
            [StashNamespace]
            public static partial class C
            {
                /// <summary>Returns n.</summary>
                /// <param name="n">The number.</param>
                [StashFn]
                public static long Identity(long n) => n;

                [StashConst]
                public const long Pi = 3;
            }
            """;
        var (diags, _) = Run(src);
        Assert.DoesNotContain(diags, d => d.Id == "STSG014");
    }
}
