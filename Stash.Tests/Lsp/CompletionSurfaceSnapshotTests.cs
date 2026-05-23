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
using Stash.Stdlib;
using Stash.Lsp.Analysis;
using Stash.Lsp.Handlers;
using Xunit;

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
        var handler = new CompletionHandler(engine, docs, logger);

        var request = new CompletionParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = uri },
            Position = new Position { Line = line, Character = character },
            Context = new CompletionContext { TriggerKind = CompletionTriggerKind.Invoked }
        };

        var result = handler.Handle(request, default).Result;
        return result.Items ?? Enumerable.Empty<CompletionItem>();
    }
}
