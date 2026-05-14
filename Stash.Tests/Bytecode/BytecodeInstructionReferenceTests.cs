namespace Stash.Tests.Bytecode;

using System;
using System.IO;
using Stash.Docs;
using Xunit;

/// <summary>
/// CI guardrail: the checked-in bytecode instruction-set reference must match
/// opcode metadata in <c>Stash.Bytecode/Bytecode/OpCode.cs</c>.
/// </summary>
public class BytecodeInstructionReferenceTests
{
    [Fact]
    public void GeneratedInstructionReference_MatchesCheckedInDoc()
    {
        string repoRoot = FindRepoRoot();
        string docPath = Path.Combine(repoRoot, BytecodeInstructionReferenceGenerator.DefaultRelativePath);
        string sourcePath = Path.Combine(repoRoot, BytecodeInstructionReferenceGenerator.DefaultSourceRelativePath);

        Assert.True(File.Exists(docPath), $"Instruction reference file not found at {docPath}.");
        Assert.True(File.Exists(sourcePath), $"Opcode source file not found at {sourcePath}.");

        string onDisk = File.ReadAllText(docPath).Replace("\r\n", "\n");
        string fresh = BytecodeInstructionReferenceGenerator.Generate(sourcePath);

        if (onDisk == fresh) return;

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("Bytecode instruction reference is stale.");
        sb.AppendLine("Regenerate with: dotnet run --project Stash.Docs/ --bytecode");
        sb.AppendLine();
        sb.AppendLine(BuildDiffSummary(onDisk, fresh));

        throw new Xunit.Sdk.XunitException(sb.ToString());
    }

    private static string BuildDiffSummary(string onDisk, string fresh)
    {
        var a = onDisk.Split('\n');
        var b = fresh.Split('\n');
        int max = Math.Min(a.Length, b.Length);
        int firstDiff = -1;
        for (int i = 0; i < max; i++)
        {
            if (a[i] != b[i]) { firstDiff = i; break; }
        }
        if (firstDiff < 0) firstDiff = max;

        var sb = new System.Text.StringBuilder();
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
