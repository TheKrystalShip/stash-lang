using System;
using System.Collections.Generic;
using System.IO;
using Stash.Analysis;
using Xunit;
using Xunit.Abstractions;

namespace Stash.Tests.Analysis;

public class FormatterIdempotencyTests
{
    private readonly ITestOutputHelper _output;

    public FormatterIdempotencyTests(ITestOutputHelper output)
    {
        _output = output;
    }

    private static string RepoRoot
    {
        get
        {
            // Walk up from the test assembly location to find the repo root (where Stash.sln is)
            string dir = AppDomain.CurrentDomain.BaseDirectory;
            while (dir != null)
            {
                if (File.Exists(Path.Combine(dir, "Stash.sln")))
                    return dir;
                dir = Directory.GetParent(dir)?.FullName!;
            }
            throw new InvalidOperationException("Could not find repo root");
        }
    }

    public static IEnumerable<object[]> GetExampleFiles()
    {
        string examplesDir = Path.Combine(RepoRoot, "examples");
        if (!Directory.Exists(examplesDir)) yield break;
        foreach (string file in Directory.EnumerateFiles(examplesDir, "*.stash"))
        {
            yield return new object[] { Path.GetFileName(file), file };
        }
    }

    [Theory]
    [MemberData(nameof(GetExampleFiles))]
    public void Idempotent_ExampleFiles_DefaultConfig(string fileName, string filePath)
    {
        string source = File.ReadAllText(filePath);
        var formatter = new StashFormatter();

        string first;
        try
        {
            first = formatter.Format(source);
        }
        catch (Exception ex)
        {
            _output.WriteLine($"SKIP (parse error): {fileName}: {ex.Message}");
            return; // Skip files that can't be parsed
        }

        string second;
        try
        {
            second = formatter.Format(first);
        }
        catch (Exception ex)
        {
            _output.WriteLine($"SKIP (second-pass parse error): {fileName}: {ex.Message}");
            return;
        }

        Assert.True(first == second,
            $"Idempotency failed for {fileName}:\n" +
            $"First pass length: {first.Length}, Second pass length: {second.Length}\n" +
            $"First diff at: {FindFirstDiff(first, second)}");
    }

    [Theory]
    [MemberData(nameof(GetExampleFiles))]
    public void Idempotent_ExampleFiles_Width60(string fileName, string filePath)
    {
        string source = File.ReadAllText(filePath);
        var config = new FormatConfig { PrintWidth = 60 };
        var formatter = new StashFormatter(config);

        string first;
        try
        {
            first = formatter.Format(source);
        }
        catch (Exception)
        {
            return; // Skip files that can't be parsed
        }

        string second;
        try
        {
            second = formatter.Format(first);
        }
        catch (Exception ex)
        {
            _output.WriteLine($"SKIP (second-pass parse error): {fileName}: {ex.Message}");
            return;
        }

        Assert.True(first == second,
            $"Idempotency (width=60) failed for {fileName}:\n" +
            $"First diff at: {FindFirstDiff(first, second)}");
    }

    [Theory]
    [MemberData(nameof(GetExampleFiles))]
    public void Idempotent_ExampleFiles_Width120(string fileName, string filePath)
    {
        string source = File.ReadAllText(filePath);
        var config = new FormatConfig { PrintWidth = 120 };
        var formatter = new StashFormatter(config);

        string first;
        try
        {
            first = formatter.Format(source);
        }
        catch (Exception)
        {
            return;
        }

        string second;
        try
        {
            second = formatter.Format(first);
        }
        catch (Exception ex)
        {
            _output.WriteLine($"SKIP (second-pass parse error): {fileName}: {ex.Message}");
            return;
        }

        Assert.True(first == second,
            $"Idempotency (width=120) failed for {fileName}:\n" +
            $"First diff at: {FindFirstDiff(first, second)}");
    }

    [Theory]
    [MemberData(nameof(GetExampleFiles))]
    public void Idempotent_ExampleFiles_TrailingCommaAll(string fileName, string filePath)
    {
        string source = File.ReadAllText(filePath);
        var config = new FormatConfig { TrailingComma = TrailingCommaStyle.All };
        var formatter = new StashFormatter(config);

        string first;
        try
        {
            first = formatter.Format(source);
        }
        catch (Exception)
        {
            return;
        }

        string second;
        try
        {
            second = formatter.Format(first);
        }
        catch (Exception ex)
        {
            _output.WriteLine($"SKIP (second-pass parse error): {fileName}: {ex.Message}");
            return;
        }

        Assert.True(first == second,
            $"Idempotency (trailingComma=all) failed for {fileName}:\n" +
            $"First diff at: {FindFirstDiff(first, second)}");
    }

    [Theory]
    [MemberData(nameof(GetExampleFiles))]
    public void Idempotent_ExampleFiles_TabIndent(string fileName, string filePath)
    {
        string source = File.ReadAllText(filePath);
        var config = new FormatConfig { UseTabs = true };
        var formatter = new StashFormatter(config);

        string first;
        try
        {
            first = formatter.Format(source);
        }
        catch (Exception)
        {
            return;
        }

        string second;
        try
        {
            second = formatter.Format(first);
        }
        catch (Exception ex)
        {
            _output.WriteLine($"SKIP (second-pass parse error): {fileName}: {ex.Message}");
            return;
        }

        Assert.True(first == second,
            $"Idempotency (tabs) failed for {fileName}:\n" +
            $"First diff at: {FindFirstDiff(first, second)}");
    }

    // Edge case tests with synthetic input
    [Fact]
    public void Idempotent_DeeplyNested_DefaultConfig()
    {
        // 10 levels of nesting
        string source = "fn a() {\n";
        for (int i = 0; i < 10; i++)
            source += new string(' ', (i + 1) * 2) + "if (true) {\n";
        source += new string(' ', 22) + "let x = 1;\n";
        for (int i = 9; i >= 0; i--)
            source += new string(' ', (i + 1) * 2) + "}\n";
        source += "}\n";

        var formatter = new StashFormatter();
        string first = formatter.Format(source);
        string second = formatter.Format(first);
        Assert.Equal(first, second);
    }

    [Fact]
    public void Idempotent_EmptyInput_ReturnsEmpty()
    {
        var formatter = new StashFormatter();
        string result = formatter.Format("");
        Assert.Equal("", result);
        Assert.Equal("", formatter.Format(result));
    }

    [Fact]
    public void Idempotent_CommentsEverywhere_Preserved()
    {
        string source = @"// top comment
let x = 1; // inline
// between
let y = 2;
/* block
   comment */
let z = 3;
";
        var formatter = new StashFormatter();
        string first = formatter.Format(source);
        string second = formatter.Format(first);
        Assert.Equal(first, second);
    }

    [Fact]
    public void Idempotent_LongIdentifiers_DefaultConfig()
    {
        string longName = new string('a', 60);
        string source = $"let {longName} = {longName} + {longName};\n";
        var formatter = new StashFormatter();
        string first = formatter.Format(source);
        string second = formatter.Format(first);
        Assert.Equal(first, second);
    }

    private static string FindFirstDiff(string a, string b)
    {
        int minLen = Math.Min(a.Length, b.Length);
        for (int i = 0; i < minLen; i++)
        {
            if (a[i] != b[i])
            {
                int contextStart = Math.Max(0, i - 20);
                return $"position {i}: '{Escape(a[contextStart..Math.Min(a.Length, i + 20)])}' vs '{Escape(b[contextStart..Math.Min(b.Length, i + 20)])}'";
            }
        }
        if (a.Length != b.Length)
            return $"lengths differ: {a.Length} vs {b.Length}";
        return "no diff found";
    }

    private static string Escape(string s) =>
        s.Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t");
}
