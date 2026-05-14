namespace Stash.Tests.Stdlib;

using System;
using System.IO;
using Stash.Docs;
using Xunit;

/// <summary>
/// CI guardrail: the checked-in standard library reference must match the
/// output of <see cref="ReferenceGenerator"/>. Adding a <c>[StashFn]</c> or
/// touching stdlib metadata without regenerating the doc fails this test
/// with a unified diff showing exactly which generated rows are stale.
/// </summary>
public class StandardLibraryReferenceTests
{
    [Fact]
    public void GeneratedReference_MatchesCheckedInDoc()
    {
        string repoRoot = FindRepoRoot();
        string docPath = Path.Combine(repoRoot, ReferenceGenerator.DefaultRelativePath);

        Assert.True(File.Exists(docPath), $"Reference file not found at {docPath}.");

        string onDisk = File.ReadAllText(docPath).Replace("\r\n", "\n");
        string fresh = ReferenceGenerator.Generate();

        if (onDisk == fresh) return;

        // Build a focused diff so CI failures are actionable.
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("Standard library reference is stale.");
        sb.AppendLine("Regenerate with: dotnet run --project Stash.Docs/");
        sb.AppendLine();
        sb.AppendLine(BuildDiffSummary(onDisk, fresh));

        throw new Xunit.Sdk.XunitException(sb.ToString());
    }

    private static string BuildDiffSummary(string onDisk, string fresh)
    {
        var a = onDisk.Split('\n');
        var b = fresh.Split('\n');
        int max = Math.Min(a.Length, b.Length);
        var sb = new System.Text.StringBuilder();
        int firstDiff = -1;
        for (int i = 0; i < max; i++)
        {
            if (a[i] != b[i]) { firstDiff = i; break; }
        }
        if (firstDiff < 0) firstDiff = max;

        sb.AppendLine($"First difference at line {firstDiff + 1}:");
        sb.AppendLine($"  on disk : {(firstDiff < a.Length ? a[firstDiff] : "<eof>")}");
        sb.AppendLine($"  expected: {(firstDiff < b.Length ? b[firstDiff] : "<eof>")}");
        sb.AppendLine();
        sb.AppendLine($"On-disk lines: {a.Length}, generated lines: {b.Length}");
        return sb.ToString();
    }

    private static string FindRepoRoot()
    {
        string current = AppContext.BaseDirectory;
        for (int i = 0; i < 12 && current != null; i++)
        {
            if (File.Exists(Path.Combine(current, "Stash.sln")))
                return current;
            current = Path.GetDirectoryName(current)!;
        }
        throw new InvalidOperationException(
            "Could not locate repo root (Stash.sln) from " + AppContext.BaseDirectory);
    }
}
