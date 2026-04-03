using System;
using System.Collections.Generic;
using System.IO;
using Stash.Format;

namespace Stash.Tests.Analysis;

public class StashFormatTests
{
    // ───────────────────────────────────────────────────────────────
    // Helpers
    // ───────────────────────────────────────────────────────────────

    private static string CreateTempStashFile(string content, string? dir = null)
    {
        string directory = dir ?? Path.GetTempPath();
        string filePath = Path.Combine(directory, $"test_{Guid.NewGuid():N}.stash");
        File.WriteAllText(filePath, content);
        return filePath;
    }

    private static string CreateTempDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"stash_format_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return dir;
    }

    // ───────────────────────────────────────────────────────────────
    // FormatOptions.Parse() tests
    // ───────────────────────────────────────────────────────────────

    [Fact]
    public void Parse_NoArgs_DefaultsToCurrentDirectory()
    {
        var opts = FormatOptions.Parse(Array.Empty<string>());
        Assert.False(opts.Write);
        Assert.False(opts.Check);
        Assert.False(opts.Diff);
        Assert.Equal(2, opts.IndentSize);
        Assert.False(opts.UseTabs);
        Assert.Empty(opts.ExcludeGlobs);
        Assert.Empty(opts.Paths);
        Assert.False(opts.ShowHelp);
        Assert.False(opts.ShowVersion);
    }

    [Fact]
    public void Parse_WriteFlag_SetsWrite()
    {
        var opts = FormatOptions.Parse(new[] { "--write", "file.stash" });
        Assert.True(opts.Write);
        Assert.Single(opts.Paths);
        Assert.Equal("file.stash", opts.Paths[0]);
    }

    [Fact]
    public void Parse_ShortFlags_Parsed()
    {
        var opts = FormatOptions.Parse(new[] { "-w", "-c", "-d", "-t" });
        Assert.True(opts.Write);
        Assert.True(opts.Check);
        Assert.True(opts.Diff);
        Assert.True(opts.UseTabs);
    }

    [Fact]
    public void Parse_IndentSize_ParsesValue()
    {
        var opts = FormatOptions.Parse(new[] { "--indent-size", "4" });
        Assert.Equal(4, opts.IndentSize);
    }

    [Fact]
    public void Parse_ShortIndentSize_ParsesValue()
    {
        var opts = FormatOptions.Parse(new[] { "-i", "8" });
        Assert.Equal(8, opts.IndentSize);
    }

    [Fact]
    public void Parse_ExcludeGlob_CollectsMultiple()
    {
        var opts = FormatOptions.Parse(new[] { "--exclude", "vendor/*", "-e", "build/*" });
        Assert.Equal(2, opts.ExcludeGlobs.Count);
        Assert.Equal("vendor/*", opts.ExcludeGlobs[0]);
        Assert.Equal("build/*", opts.ExcludeGlobs[1]);
    }

    [Fact]
    public void Parse_HelpFlag_SetsShowHelp()
    {
        var opts = FormatOptions.Parse(new[] { "--help" });
        Assert.True(opts.ShowHelp);
    }

    [Fact]
    public void Parse_ShortHelpFlag_SetsShowHelp()
    {
        var opts = FormatOptions.Parse(new[] { "-h" });
        Assert.True(opts.ShowHelp);
    }

    [Fact]
    public void Parse_VersionFlag_SetsShowVersion()
    {
        var opts = FormatOptions.Parse(new[] { "--version" });
        Assert.True(opts.ShowVersion);
    }

    [Fact]
    public void Parse_ShortVersionFlag_SetsShowVersion()
    {
        var opts = FormatOptions.Parse(new[] { "-v" });
        Assert.True(opts.ShowVersion);
    }

    [Fact]
    public void Parse_MultiplePaths_CollectsAll()
    {
        var opts = FormatOptions.Parse(new[] { "a.stash", "b.stash", "src/" });
        Assert.Equal(3, opts.Paths.Count);
    }

    [Fact]
    public void Parse_CheckAndDiffCombined()
    {
        var opts = FormatOptions.Parse(new[] { "--check", "--diff", "." });
        Assert.True(opts.Check);
        Assert.True(opts.Diff);
    }

    // ───────────────────────────────────────────────────────────────
    // FormatRunner tests
    // ───────────────────────────────────────────────────────────────

    [Fact]
    public void FormatRunner_SingleFile_FormatsCorrectly()
    {
        string filePath = CreateTempStashFile("let   x=5;");
        try
        {
            var options = new FormatOptions { Paths = new List<string> { filePath } };
            var runner = new FormatRunner(options);
            FormatResult result = runner.Run();
            Assert.Single(result.Files);
            Assert.Equal("let x = 5;\n", result.Files[0].Formatted);
            Assert.True(result.Files[0].Changed);
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    [Fact]
    public void FormatRunner_AlreadyFormatted_NotChanged()
    {
        string filePath = CreateTempStashFile("let x = 5;\n");
        try
        {
            var options = new FormatOptions { Paths = new List<string> { filePath } };
            var runner = new FormatRunner(options);
            FormatResult result = runner.Run();
            Assert.Single(result.Files);
            Assert.False(result.Files[0].Changed);
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    [Fact]
    public void FormatRunner_Directory_FindsAllStashFiles()
    {
        string dir = CreateTempDir();
        try
        {
            CreateTempStashFile("let x=1;", dir);
            CreateTempStashFile("let y=2;", dir);
            File.WriteAllText(Path.Combine(dir, "readme.txt"), "not a stash file");

            var options = new FormatOptions { Paths = new List<string> { dir } };
            var runner = new FormatRunner(options);
            FormatResult result = runner.Run();
            Assert.Equal(2, result.TotalFiles);
            Assert.Equal(2, result.ChangedFiles);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void FormatRunner_CustomIndentSize_Applies()
    {
        string filePath = CreateTempStashFile("fn foo(){\nreturn 1;\n}");
        try
        {
            var options = new FormatOptions { Paths = new List<string> { filePath }, IndentSize = 4 };
            var runner = new FormatRunner(options);
            FormatResult result = runner.Run();
            Assert.Contains("    return 1;", result.Files[0].Formatted);
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    [Fact]
    public void FormatRunner_ExcludeGlob_SkipsMatchingFiles()
    {
        string dir = CreateTempDir();
        string vendorDir = Path.Combine(dir, "vendor");
        Directory.CreateDirectory(vendorDir);
        try
        {
            CreateTempStashFile("let x=1;", dir);
            CreateTempStashFile("let y=2;", vendorDir);

            string cwd = Directory.GetCurrentDirectory();
            Directory.SetCurrentDirectory(dir);
            try
            {
                var options = new FormatOptions
                {
                    Paths = new List<string> { "." },
                    ExcludeGlobs = new List<string> { "vendor/*" }
                };
                var runner = new FormatRunner(options);
                List<string> files = runner.DiscoverFiles();
                Assert.Single(files);
                Assert.DoesNotContain(files, f => f.Contains("vendor"));
            }
            finally
            {
                Directory.SetCurrentDirectory(cwd);
            }
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    // ───────────────────────────────────────────────────────────────
    // Program.Main() exit code tests
    // ───────────────────────────────────────────────────────────────

    [Fact]
    public void Main_Help_ReturnsZero()
    {
        int exitCode = Stash.Format.Program.Main(new[] { "--help" });
        Assert.Equal(0, exitCode);
    }

    [Fact]
    public void Main_Version_ReturnsZero()
    {
        int exitCode = Stash.Format.Program.Main(new[] { "--version" });
        Assert.Equal(0, exitCode);
    }

    [Fact]
    public void Main_CheckMode_AllFormatted_ReturnsZero()
    {
        string filePath = CreateTempStashFile("let x = 5;\n");
        try
        {
            int exitCode = Stash.Format.Program.Main(new[] { "--check", filePath });
            Assert.Equal(0, exitCode);
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    [Fact]
    public void Main_CheckMode_NeedsFormatting_ReturnsOne()
    {
        string filePath = CreateTempStashFile("let   x=5;");
        try
        {
            int exitCode = Stash.Format.Program.Main(new[] { "--check", filePath });
            Assert.Equal(1, exitCode);
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    [Fact]
    public void Main_WriteMode_FormatsFileInPlace()
    {
        string filePath = CreateTempStashFile("let   x=5;");
        try
        {
            int exitCode = Stash.Format.Program.Main(new[] { "--write", filePath });
            Assert.Equal(0, exitCode);
            string content = File.ReadAllText(filePath);
            Assert.Equal("let x = 5;\n", content);
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    [Fact]
    public void Main_WriteMode_AlreadyFormatted_DoesNotModifyTimestamp()
    {
        string filePath = CreateTempStashFile("let x = 5;\n");
        try
        {
            DateTime originalTime = File.GetLastWriteTimeUtc(filePath);
            // Small sleep to ensure timestamp would differ if file was rewritten
            System.Threading.Thread.Sleep(50);
            int exitCode = Stash.Format.Program.Main(new[] { "--write", filePath });
            Assert.Equal(0, exitCode);
            DateTime newTime = File.GetLastWriteTimeUtc(filePath);
            Assert.Equal(originalTime, newTime);
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    [Fact]
    public void Main_DiffMode_ShowsDiff()
    {
        string filePath = CreateTempStashFile("let   x=5;");
        try
        {
            int exitCode = Stash.Format.Program.Main(new[] { "--diff", filePath });
            Assert.Equal(0, exitCode);
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    [Fact]
    public void Main_CheckDiffCombined_ReturnsOneWhenUnformatted()
    {
        string filePath = CreateTempStashFile("let   x=5;");
        try
        {
            int exitCode = Stash.Format.Program.Main(new[] { "--check", "--diff", filePath });
            Assert.Equal(1, exitCode);
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    [Fact]
    public void Main_EmptyDirectory_ReturnsZero()
    {
        string dir = CreateTempDir();
        try
        {
            int exitCode = Stash.Format.Program.Main(new[] { "--check", dir });
            Assert.Equal(0, exitCode);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void FormatRunner_UseTabs_AppliesTabIndentation()
    {
        string filePath = CreateTempStashFile("fn foo(){\nreturn 1;\n}");
        try
        {
            var options = new FormatOptions { Paths = new List<string> { filePath }, UseTabs = true };
            var runner = new FormatRunner(options);
            FormatResult result = runner.Run();
            Assert.Contains("\treturn 1;", result.Files[0].Formatted);
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    [Fact]
    public void Main_UnparseableFile_ReturnsTwo()
    {
        string filePath = CreateTempStashFile("fn fn fn {{{ !!!");
        try
        {
            int exitCode = Stash.Format.Program.Main(new[] { "--check", filePath });
            Assert.Equal(2, exitCode);
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    [Fact]
    public void Main_MultipleFiles_OneUnparseable_FormatsOthersAndReturnsTwo()
    {
        string dir = CreateTempDir();
        try
        {
            string validFile = CreateTempStashFile("let x = 5;\n", dir);
            string brokenFile = CreateTempStashFile("fn fn fn {{{ !!!", dir);

            int exitCode = Stash.Format.Program.Main(new[] { "--write", dir });

            Assert.Equal(2, exitCode);
            Assert.Equal("let x = 5;\n", File.ReadAllText(validFile));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
}
