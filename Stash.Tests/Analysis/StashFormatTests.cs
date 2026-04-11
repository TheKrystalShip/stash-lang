using System;
using System.Collections.Generic;
using System.IO;
using Stash.Analysis;
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

    [Fact]
    public void Parse_RangeStartEnd_ParsesCorrectly()
    {
        var opts = FormatOptions.Parse(new[] { "--range-start", "10", "--range-end", "20", "file.stash" });
        Assert.Equal(10, opts.RangeStart);
        Assert.Equal(20, opts.RangeEnd);
    }

    [Fact]
    public void Parse_RangeStartOnly_EndIsNull()
    {
        var opts = FormatOptions.Parse(new[] { "--range-start", "5", "file.stash" });
        Assert.Equal(5, opts.RangeStart);
        Assert.Null(opts.RangeEnd);
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
            var options = new FormatOptions { Paths = new List<string> { filePath }, IndentSizeOverride = 4 };
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
            var options = new FormatOptions { Paths = new List<string> { filePath }, UseTabsOverride = true };
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

    // ───────────────────────────────────────────────────────────────
    // StashFormatter — Ignore Comments
    // ───────────────────────────────────────────────────────────────

    [Fact]
    public void IgnoreAll_Format_ReturnsSourceUnchanged()
    {
        string source = "// stash-ignore-all format\nlet   x=5;\nlet   y=6;\n";
        var formatter = new StashFormatter();
        string result = formatter.Format(source);
        Assert.Equal(source, result);
    }

    [Fact]
    public void IgnoreNext_Format_PreservesNextStatement()
    {
        string source = "// stash-ignore format\nlet   x=5;\nlet   y=6;\n";
        var formatter = new StashFormatter();
        string result = formatter.Format(source);
        Assert.Contains("let   x=5;", result);
        Assert.Contains("let y = 6;", result);
    }

    [Fact]
    public void IgnoreNext_Format_OnlyAffectsNextStatement()
    {
        string source = "// stash-ignore format\nlet   x=5;\nlet   y=6;\n";
        var formatter = new StashFormatter();
        string result = formatter.Format(source);
        Assert.DoesNotContain("let   y=6;", result);
        Assert.Contains("let y = 6;", result);
    }

    // ───────────────────────────────────────────────────────────────
    // StashFormatter — Trailing Commas
    // ───────────────────────────────────────────────────────────────

    [Fact]
    public void TrailingComma_All_ArrayMultiLine()
    {
        // Use a narrow printWidth to force multi-line expansion (the DocPrinter decides based on width)
        string source = "let arr = [1, 2, 3];\n";
        var formatter = new StashFormatter(new FormatConfig { TrailingComma = TrailingCommaStyle.All, PrintWidth = 10 });
        string result = formatter.Format(source);
        Assert.Contains("3,", result);
    }

    [Fact]
    public void TrailingComma_None_ArrayMultiLine()
    {
        // Use a narrow printWidth to force multi-line expansion; verify no trailing comma is added
        string source = "let arr = [1, 2, 3];\n";
        var formatter = new StashFormatter(new FormatConfig { TrailingComma = TrailingCommaStyle.None, PrintWidth = 10 });
        string result = formatter.Format(source);
        Assert.DoesNotContain("3,", result);
    }

    [Fact]
    public void TrailingComma_All_SingleLine_NoEffect()
    {
        string source = "let arr = [1, 2, 3];\n";
        var formatter = new StashFormatter(new FormatConfig { TrailingComma = TrailingCommaStyle.All });
        string result = formatter.Format(source);
        Assert.DoesNotContain("3,", result);
    }

    // ───────────────────────────────────────────────────────────────
    // StashFormatter — End of Line
    // ───────────────────────────────────────────────────────────────

    [Fact]
    public void EndOfLine_Crlf_OutputHasCrlf()
    {
        string source = "let x = 1;\nlet y = 2;\n";
        var formatter = new StashFormatter(new FormatConfig { EndOfLine = EndOfLineStyle.Crlf });
        string result = formatter.Format(source);
        Assert.Contains("\r\n", result);
        Assert.DoesNotContain("\r\r", result);
    }

    [Fact]
    public void EndOfLine_Lf_OutputHasLf()
    {
        string source = "let x = 1;\nlet y = 2;\n";
        var formatter = new StashFormatter(new FormatConfig { EndOfLine = EndOfLineStyle.Lf });
        string result = formatter.Format(source);
        Assert.DoesNotContain("\r\n", result);
        Assert.Contains("\n", result);
    }

    // ───────────────────────────────────────────────────────────────
    // StashFormatter — Bracket Spacing
    // ───────────────────────────────────────────────────────────────

    [Fact]
    public void BracketSpacing_True_SpacesInsideBraces()
    {
        string source = "let d = {a: 1};\n";
        var formatter = new StashFormatter(new FormatConfig { BracketSpacing = true });
        string result = formatter.Format(source);
        Assert.Contains("{ a: 1 }", result);
    }

    [Fact]
    public void BracketSpacing_False_NoSpacesInsideBraces()
    {
        string source = "let d = {a: 1};\n";
        var formatter = new StashFormatter(new FormatConfig { BracketSpacing = false });
        string result = formatter.Format(source);
        Assert.Contains("{a: 1}", result);
    }

    // ───────────────────────────────────────────────────────────────
    // FormatConfig
    // ───────────────────────────────────────────────────────────────

    [Fact]
    public void FormatConfig_Load_FindsFile()
    {
        string dir = CreateTempDir();
        try
        {
            File.WriteAllText(
                Path.Combine(dir, ".stashformat"),
                "indentSize=4\ntrailingComma=all\nendOfLine=crlf\nbracketSpacing=false\n");
            var config = FormatConfig.Load(dir);
            Assert.Equal(4, config.IndentSize);
            Assert.Equal(TrailingCommaStyle.All, config.TrailingComma);
            Assert.Equal(EndOfLineStyle.Crlf, config.EndOfLine);
            Assert.False(config.BracketSpacing);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void FormatConfig_Default_HasExpectedValues()
    {
        var config = FormatConfig.Default;
        Assert.Equal(2, config.IndentSize);
        Assert.False(config.UseTabs);
        Assert.Equal(TrailingCommaStyle.None, config.TrailingComma);
        Assert.Equal(EndOfLineStyle.Lf, config.EndOfLine);
        Assert.True(config.BracketSpacing);
    }

    // ───────────────────────────────────────────────────────────────
    // EditorConfig support tests
    // ───────────────────────────────────────────────────────────────

    [Fact]
    public void EditorConfig_LoadForFile_ParsesStandardProperties()
    {
        string dir = CreateTempDir();
        try
        {
            File.WriteAllText(Path.Combine(dir, ".editorconfig"),
                "root = true\n\n[*.stash]\nindent_style = tab\nindent_size = 4\nend_of_line = crlf\nmax_line_length = 120\n");
            string filePath = Path.Combine(dir, "test.stash");
            File.WriteAllText(filePath, "");
            var config = FormatConfig.LoadWithEditorConfig(filePath);
            Assert.True(config.UseTabs);
            Assert.Equal(4, config.IndentSize);
            Assert.Equal(EndOfLineStyle.Crlf, config.EndOfLine);
            Assert.Equal(120, config.PrintWidth);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void EditorConfig_LoadForFile_ParsesCustomProperties()
    {
        string dir = CreateTempDir();
        try
        {
            File.WriteAllText(Path.Combine(dir, ".editorconfig"),
                "root = true\n\n[*.stash]\nstash_trailing_comma = all\nstash_bracket_spacing = false\n");
            string filePath = Path.Combine(dir, "test.stash");
            File.WriteAllText(filePath, "");
            var config = FormatConfig.LoadWithEditorConfig(filePath);
            Assert.Equal(TrailingCommaStyle.All, config.TrailingComma);
            Assert.False(config.BracketSpacing);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void EditorConfig_StashFormatTakesPriority()
    {
        string dir = CreateTempDir();
        try
        {
            File.WriteAllText(Path.Combine(dir, ".stashformat"), "indentSize=8\n");
            File.WriteAllText(Path.Combine(dir, ".editorconfig"),
                "root = true\n\n[*.stash]\nindent_size = 4\n");
            string filePath = Path.Combine(dir, "test.stash");
            File.WriteAllText(filePath, "");
            var config = FormatConfig.LoadWithEditorConfig(filePath);
            Assert.Equal(8, config.IndentSize);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void EditorConfig_FallsBackWhenNoStashFormat()
    {
        string dir = CreateTempDir();
        try
        {
            File.WriteAllText(Path.Combine(dir, ".editorconfig"),
                "root = true\n\n[*.stash]\nindent_size = 6\n");
            string filePath = Path.Combine(dir, "test.stash");
            File.WriteAllText(filePath, "");
            var config = FormatConfig.LoadWithEditorConfig(filePath);
            Assert.Equal(6, config.IndentSize);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void EditorConfig_RootStopsHierarchicalSearch()
    {
        string grandparentDir = CreateTempDir();
        try
        {
            string parentDir = Path.Combine(grandparentDir, "parent");
            string childDir = Path.Combine(parentDir, "child");
            Directory.CreateDirectory(childDir);

            File.WriteAllText(Path.Combine(grandparentDir, ".editorconfig"),
                "[*.stash]\nindent_size = 8\n");
            File.WriteAllText(Path.Combine(parentDir, ".editorconfig"),
                "root = true\n\n[*.stash]\nindent_size = 4\n");

            string filePath = Path.Combine(childDir, "test.stash");
            File.WriteAllText(filePath, "");
            var config = FormatConfig.LoadWithEditorConfig(filePath);
            Assert.Equal(4, config.IndentSize);
        }
        finally
        {
            Directory.Delete(grandparentDir, true);
        }
    }

    [Fact]
    public void EditorConfig_StarGlobMatchesStashFiles()
    {
        string dir = CreateTempDir();
        try
        {
            File.WriteAllText(Path.Combine(dir, ".editorconfig"),
                "root = true\n\n[*]\nindent_size = 3\n");
            string filePath = Path.Combine(dir, "test.stash");
            File.WriteAllText(filePath, "");
            var config = FormatConfig.LoadWithEditorConfig(filePath);
            Assert.Equal(3, config.IndentSize);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void EditorConfig_BraceGlobMatchesStashFiles()
    {
        string dir = CreateTempDir();
        try
        {
            File.WriteAllText(Path.Combine(dir, ".editorconfig"),
                "root = true\n\n[*.{stash,js}]\nindent_size = 5\n");
            string filePath = Path.Combine(dir, "test.stash");
            File.WriteAllText(filePath, "");
            var config = FormatConfig.LoadWithEditorConfig(filePath);
            Assert.Equal(5, config.IndentSize);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void EditorConfig_ReturnsDefaultWhenNoEditorConfig()
    {
        string dir = CreateTempDir();
        try
        {
            string filePath = Path.Combine(dir, "test.stash");
            File.WriteAllText(filePath, "");
            var config = FormatConfig.LoadWithEditorConfig(filePath);
            var def = FormatConfig.Default;
            Assert.Equal(def.IndentSize, config.IndentSize);
            Assert.Equal(def.UseTabs, config.UseTabs);
            Assert.Equal(def.TrailingComma, config.TrailingComma);
            Assert.Equal(def.EndOfLine, config.EndOfLine);
            Assert.Equal(def.BracketSpacing, config.BracketSpacing);
            Assert.Equal(def.PrintWidth, config.PrintWidth);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void EditorConfig_NearestFileWinsPerProperty()
    {
        string parentDir = CreateTempDir();
        try
        {
            string childDir = Path.Combine(parentDir, "child");
            Directory.CreateDirectory(childDir);

            File.WriteAllText(Path.Combine(parentDir, ".editorconfig"),
                "root = true\n\n[*.stash]\nindent_size = 4\n");
            File.WriteAllText(Path.Combine(childDir, ".editorconfig"),
                "[*.stash]\nindent_size = 2\n");

            string filePath = Path.Combine(childDir, "test.stash");
            File.WriteAllText(filePath, "");
            var config = FormatConfig.LoadWithEditorConfig(filePath);
            Assert.Equal(2, config.IndentSize);
        }
        finally
        {
            Directory.Delete(parentDir, true);
        }
    }

    [Fact]
    public void EditorConfig_MaxLineLengthOff()
    {
        string dir = CreateTempDir();
        try
        {
            File.WriteAllText(Path.Combine(dir, ".editorconfig"),
                "root = true\n\n[*.stash]\nmax_line_length = off\n");
            string filePath = Path.Combine(dir, "test.stash");
            File.WriteAllText(filePath, "");
            var config = FormatConfig.LoadWithEditorConfig(filePath);
            Assert.Equal(int.MaxValue, config.PrintWidth);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    // ───────────────────────────────────────────────────────────────
    // P0: printWidth-driven collection breaking
    // ───────────────────────────────────────────────────────────────

    [Fact]
    public void PrintWidth_ShortArray_StaysSingleLine()
    {
        string source = "let arr = [1, 2, 3];\n";
        var formatter = new StashFormatter(new FormatConfig { PrintWidth = 80 });
        string result = formatter.Format(source);
        Assert.Equal("let arr = [1, 2, 3];\n", result);
    }

    [Fact]
    public void PrintWidth_LongArray_BreaksToMultiLine()
    {
        string source = "let arr = [1, 2, 3];\n";
        var formatter = new StashFormatter(new FormatConfig { PrintWidth = 10 });
        string result = formatter.Format(source);
        Assert.Contains("\n  1,", result);
        Assert.Contains("\n  2,", result);
    }

    [Fact]
    public void PrintWidth_ShortDict_StaysSingleLine()
    {
        // The old >=3 item heuristic would force this to multi-line; printWidth-based does not.
        string source = "let d = {a: 1, b: 2, c: 3};\n";
        var formatter = new StashFormatter(new FormatConfig { PrintWidth = 80 });
        string result = formatter.Format(source);
        Assert.Equal("let d = { a: 1, b: 2, c: 3 };\n", result);
    }

    [Fact]
    public void PrintWidth_LongDict_BreaksToMultiLine()
    {
        string source = "let d = {alpha: \"longvalue\", beta: \"anotherlongvalue\"};\n";
        var formatter = new StashFormatter(new FormatConfig { PrintWidth = 20 });
        string result = formatter.Format(source);
        Assert.Contains("\n  alpha:", result);
    }

    [Fact]
    public void PrintWidth_ShortStructInit_StaysSingleLine()
    {
        string source = "struct Pt { x, y, z }\nlet p = Pt { x: 1, y: 2, z: 3 };\n";
        var formatter = new StashFormatter(new FormatConfig { PrintWidth = 80 });
        string result = formatter.Format(source);
        Assert.Contains("Pt { x: 1, y: 2, z: 3 }", result);
    }

    // ───────────────────────────────────────────────────────────────
    // P1: sortImports
    // ───────────────────────────────────────────────────────────────

    [Fact]
    public void SortImports_Disabled_PreservesOrder()
    {
        string source =
            "import { readFile } from \"fs\";\n" +
            "import { encrypt } from \"crypto\";\n";
        var formatter = new StashFormatter(new FormatConfig { SortImports = false });
        string result = formatter.Format(source);
        Assert.True(result.IndexOf("\"fs\"", StringComparison.Ordinal)
                   < result.IndexOf("\"crypto\"", StringComparison.Ordinal));
    }

    [Fact]
    public void SortImports_SortsByPath()
    {
        string source =
            "import { readFile } from \"fs\";\n" +
            "import { parse } from \"json\";\n" +
            "import { encrypt } from \"crypto\";\n";
        var formatter = new StashFormatter(new FormatConfig { SortImports = true });
        string result = formatter.Format(source);
        string expected =
            "import { encrypt } from \"crypto\";\n" +
            "import { readFile } from \"fs\";\n" +
            "import { parse } from \"json\";\n";
        Assert.Equal(expected, result);
    }

    [Fact]
    public void SortImports_SortsNamesWithinImport()
    {
        string source = "import { c, a, b } from \"mod\";\n";
        var formatter = new StashFormatter(new FormatConfig { SortImports = true });
        string result = formatter.Format(source);
        Assert.Equal("import { a, b, c } from \"mod\";\n", result);
    }

    [Fact]
    public void SortImports_PreservesGroups()
    {
        // A standalone comment between import groups creates a blank line in the formatted
        // output; SortFormattedImports treats the comment + blank line as a group separator
        // and sorts each group independently.
        string source =
            "import { b_fn } from \"b\";\n" +
            "import { a_fn } from \"a\";\n" +
            "// group 2\n" +
            "import { d_fn } from \"d\";\n" +
            "import { c_fn } from \"c\";\n";
        var formatter = new StashFormatter(new FormatConfig { SortImports = true });
        string result = formatter.Format(source);
        string expected =
            "import { a_fn } from \"a\";\n" +
            "import { b_fn } from \"b\";\n" +
            "// group 2\n" +
            "\n" +
            "import { c_fn } from \"c\";\n" +
            "import { d_fn } from \"d\";\n";
        Assert.Equal(expected, result);
    }

    [Fact]
    public void SortImports_SingleImport_NoChange()
    {
        string source = "import { foo } from \"mod\";\n";
        var formatter = new StashFormatter(new FormatConfig { SortImports = true });
        string result = formatter.Format(source);
        Assert.Equal("import { foo } from \"mod\";\n", result);
    }

    // ───────────────────────────────────────────────────────────────
    // P2: blankLinesBetweenBlocks
    // ───────────────────────────────────────────────────────────────

    [Fact]
    public void BlankLinesBetweenBlocks_Default_OneBlankLine()
    {
        string source = "fn foo() { return 1; }\nfn bar() { return 2; }\n";
        var formatter = new StashFormatter(new FormatConfig());
        string result = formatter.Format(source);
        Assert.Contains("}\n\nfn", result);         // exactly one blank line between functions
        Assert.DoesNotContain("}\n\n\nfn", result); // not two blank lines
    }

    [Fact]
    public void BlankLinesBetweenBlocks_Two_TwoBlankLines()
    {
        string source = "fn foo() { return 1; }\nfn bar() { return 2; }\n";
        var formatter = new StashFormatter(new FormatConfig { BlankLinesBetweenBlocks = 2 });
        string result = formatter.Format(source);
        Assert.Contains("}\n\n\nfn", result); // two blank lines (three newlines) between functions
    }

    [Fact]
    public void BlankLinesBetweenBlocks_Two_AppliesBetweenStructs()
    {
        string source = "struct A { x }\nstruct B { y }\n";
        var formatter = new StashFormatter(new FormatConfig { BlankLinesBetweenBlocks = 2 });
        string result = formatter.Format(source);
        Assert.Contains("}\n\n\nstruct", result); // two blank lines between struct declarations
    }

    [Fact]
    public void BlankLinesBetweenBlocks_NonDeclarations_StillSingleNewline()
    {
        string source = "let x = 1;\nlet y = 2;\n";
        var formatter = new StashFormatter(new FormatConfig { BlankLinesBetweenBlocks = 2 });
        string result = formatter.Format(source);
        Assert.Equal("let x = 1;\nlet y = 2;\n", result);
    }

    // ───────────────────────────────────────────────────────────────
    // P3: singleLineBlocks
    // ───────────────────────────────────────────────────────────────

    [Fact]
    public void SingleLineBlocks_Disabled_AlwaysExpands()
    {
        string source = "fn f() { return 1; }\n";
        var formatter = new StashFormatter(new FormatConfig { SingleLineBlocks = false });
        string result = formatter.Format(source);
        Assert.Contains("\n  return 1;\n", result);
    }

    [Fact]
    public void SingleLineBlocks_ShortFunction_StaysSingleLine()
    {
        string source = "fn f() { return 1; }\n";
        var formatter = new StashFormatter(new FormatConfig { SingleLineBlocks = true, PrintWidth = 80 });
        string result = formatter.Format(source);
        Assert.Equal("fn f() { return 1; }\n", result);
    }

    [Fact]
    public void SingleLineBlocks_LongFunction_BreaksToMultiLine()
    {
        string source = "fn f() { return 1; }\n";
        var formatter = new StashFormatter(new FormatConfig { SingleLineBlocks = true, PrintWidth = 5 });
        string result = formatter.Format(source);
        Assert.Contains("\n  return 1;\n", result);
    }

    [Fact]
    public void SingleLineBlocks_MultiStatement_AlwaysExpands()
    {
        string source = "fn f() { let x = 1; return x; }\n";
        var formatter = new StashFormatter(new FormatConfig { SingleLineBlocks = true, PrintWidth = 80 });
        string result = formatter.Format(source);
        Assert.Contains("\n  let x = 1;\n", result);
    }

    [Fact]
    public void SingleLineBlocks_IfStatement_StaysSingleLine()
    {
        string source = "fn check(x) { if (x > 0) { return x; } }\n";
        var formatter = new StashFormatter(new FormatConfig { SingleLineBlocks = true, PrintWidth = 80 });
        string result = formatter.Format(source);
        Assert.Equal("fn check(x) { if (x > 0) { return x; } }\n", result);
    }

    [Fact]
    public void SingleLineBlocks_WhileStatement_StaysSingleLine()
    {
        string source = "fn run(i) { while (i > 0) { i = i - 1; } }\n";
        var formatter = new StashFormatter(new FormatConfig { SingleLineBlocks = true, PrintWidth = 80 });
        string result = formatter.Format(source);
        Assert.Equal("fn run(i) { while (i > 0) { i = i - 1; } }\n", result);
    }

    // ───────────────────────────────────────────────────────────────
    // Config parsing for new options
    // ───────────────────────────────────────────────────────────────

    [Fact]
    public void FormatConfig_ParseContent_NewOptions()
    {
        string dir = CreateTempDir();
        try
        {
            File.WriteAllText(
                Path.Combine(dir, ".stashformat"),
                "sortImports=true\nblankLinesBetweenBlocks=2\nsingleLineBlocks=true\n");
            var config = FormatConfig.LoadFromFile(Path.Combine(dir, ".stashformat"));
            Assert.True(config.SortImports);
            Assert.Equal(2, config.BlankLinesBetweenBlocks);
            Assert.True(config.SingleLineBlocks);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Parse_SortImportsFlag()
    {
        var opts = FormatOptions.Parse(new[] { "--sort-imports" });
        Assert.True(opts.SortImportsOverride);

        var optsShort = FormatOptions.Parse(new[] { "-si" });
        Assert.True(optsShort.SortImportsOverride);
    }

    [Fact]
    public void Parse_BlankLinesBetweenBlocksFlag()
    {
        var opts = FormatOptions.Parse(new[] { "--blank-lines-between-blocks", "2" });
        Assert.Equal(2, opts.BlankLinesBetweenBlocksOverride);

        var optsShort = FormatOptions.Parse(new[] { "-blb", "2" });
        Assert.Equal(2, optsShort.BlankLinesBetweenBlocksOverride);
    }

    [Fact]
    public void Parse_SingleLineBlocksFlag()
    {
        var opts = FormatOptions.Parse(new[] { "--single-line-blocks" });
        Assert.True(opts.SingleLineBlocksOverride);

        var optsShort = FormatOptions.Parse(new[] { "-slb" });
        Assert.True(optsShort.SingleLineBlocksOverride);
    }
}
