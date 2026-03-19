using System;
using System.IO;
using Stash.Lexing;
using Stash.Parsing;
using Stash.Interpreting;
using Stash.Interpreting.Types;

namespace Stash.Tests.Interpreting;

public class FsBuiltInsTests : IDisposable
{
    private readonly string _testDir;

    public FsBuiltInsTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "stash_fs_test_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_testDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_testDir, true); } catch { }
    }

    private void Execute(string source)
    {
        var lexer = new Lexer(source);
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        var statements = parser.ParseProgram();
        var interpreter = new Interpreter();
        interpreter.Interpret(statements);
    }

    private object? Run(string source)
    {
        var lexer = new Lexer(source);
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        var statements = parser.ParseProgram();
        var interpreter = new Interpreter();
        interpreter.Interpret(statements);

        var resultLexer = new Lexer("result");
        var resultTokens = resultLexer.ScanTokens();
        var resultParser = new Parser(resultTokens);
        var resultExpr = resultParser.Parse();
        return interpreter.Interpret(resultExpr);
    }

    private void RunExpectingError(string source)
    {
        var lexer = new Lexer(source);
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        var statements = parser.ParseProgram();
        var interpreter = new Interpreter();
        Assert.Throws<RuntimeError>(() => interpreter.Interpret(statements));
    }

    // ── fs.createFile ───────────────────────────────────────────────────────

    [Fact]
    public void CreateFile_CreatesNewFile()
    {
        var filePath = Path.Combine(_testDir, "create_new.txt");
        Assert.False(File.Exists(filePath));

        var result = Run($"fs.createFile(\"{filePath}\"); let result = fs.exists(\"{filePath}\");");
        Assert.Equal(true, result);
        Assert.True(File.Exists(filePath));
    }

    [Fact]
    public void CreateFile_UpdatesTimestamp()
    {
        var filePath = Path.Combine(_testDir, "create_ts.txt");
        File.WriteAllText(filePath, "old");
        var pastTime = DateTime.UtcNow.AddSeconds(-60);
        File.SetLastWriteTimeUtc(filePath, pastTime);

        Execute($"fs.createFile(\"{filePath}\");");

        var newTime = File.GetLastWriteTimeUtc(filePath);
        Assert.True(newTime > pastTime.AddSeconds(30));
    }

    [Fact]
    public void CreateFile_ReturnsNull()
    {
        var filePath = Path.Combine(_testDir, "create_null.txt");
        var result = Run($"let result = fs.createFile(\"{filePath}\");");
        Assert.Null(result);
    }

    [Fact]
    public void CreateFile_NonStringThrows()
    {
        RunExpectingError("fs.createFile(42);");
    }

    [Fact]
    public void CreateFile_ExistingFileDoesNotThrow()
    {
        var filePath = Path.Combine(_testDir, "create_existing.txt");
        File.WriteAllText(filePath, "content");

        // Should not throw — just updates the timestamp
        var result = Run($"fs.createFile(\"{filePath}\"); let result = fs.exists(\"{filePath}\");");
        Assert.Equal(true, result);
    }

    [Fact]
    public void CreateFile_CreatesEmptyFile()
    {
        var filePath = Path.Combine(_testDir, "create_empty.txt");
        Execute($"fs.createFile(\"{filePath}\");");

        Assert.True(File.Exists(filePath));
        Assert.Equal(0, new FileInfo(filePath).Length);
    }

    // ── fs.symlink ───────────────────────────────────────────────────────

    [Fact]
    public void Symlink_CreatesLink()
    {
        try
        {
            var targetPath = Path.Combine(_testDir, "symlink_target.txt");
            var linkPath = Path.Combine(_testDir, "symlink_link.txt");
            File.WriteAllText(targetPath, "symlink content");

            Execute($"fs.symlink(\"{targetPath}\", \"{linkPath}\");");

            Assert.True(File.Exists(linkPath));
            var linkInfo = new FileInfo(linkPath);
            Assert.True(linkInfo.Attributes.HasFlag(FileAttributes.ReparsePoint));
            Assert.Equal("symlink content", File.ReadAllText(linkPath));
        }
        catch (UnauthorizedAccessException) { return; } // Not supported on this platform
        catch (IOException) { return; } // Skip if symlinks not supported
    }

    [Fact]
    public void Symlink_IsSymlinkReturnsTrueForLink()
    {
        try
        {
            var targetPath = Path.Combine(_testDir, "sym_target2.txt");
            var linkPath = Path.Combine(_testDir, "sym_link2.txt");
            File.WriteAllText(targetPath, "data");

            var result = Run($"fs.symlink(\"{targetPath}\", \"{linkPath}\"); let result = fs.isSymlink(\"{linkPath}\");");
            Assert.Equal(true, result);
        }
        catch (UnauthorizedAccessException) { return; }
        catch (IOException) { return; }
    }

    [Fact]
    public void Symlink_NonStringTargetThrows()
    {
        RunExpectingError($"fs.symlink(123, \"{Path.Combine(_testDir, "link.txt")}\");");
    }

    [Fact]
    public void Symlink_NonStringPathThrows()
    {
        var targetPath = Path.Combine(_testDir, "sym_target3.txt");
        File.WriteAllText(targetPath, "data");
        RunExpectingError($"fs.symlink(\"{targetPath}\", 99);");
    }

    [Fact]
    public void Symlink_ReturnsNull()
    {
        try
        {
            var targetPath = Path.Combine(_testDir, "sym_null_target.txt");
            var linkPath = Path.Combine(_testDir, "sym_null_link.txt");
            File.WriteAllText(targetPath, "data");

            var result = Run($"let result = fs.symlink(\"{targetPath}\", \"{linkPath}\");");
            Assert.Null(result);
        }
        catch (UnauthorizedAccessException) { return; }
        catch (IOException) { return; }
    }

    // ── fs.stat ──────────────────────────────────────────────────────────

    [Fact]
    public void Stat_ReturnsDict()
    {
        var filePath = Path.Combine(_testDir, "stat_file.txt");
        File.WriteAllText(filePath, "hello world");

        var result = Run($"let result = fs.stat(\"{filePath}\");");
        Assert.IsType<StashDictionary>(result);
    }

    [Fact]
    public void Stat_FileProperties()
    {
        var filePath = Path.Combine(_testDir, "stat_props.txt");
        File.WriteAllText(filePath, "hello world"); // 11 bytes

        var isFile = Run($"let result = dict.get(fs.stat(\"{filePath}\"), \"isFile\");");
        var isDir = Run($"let result = dict.get(fs.stat(\"{filePath}\"), \"isDir\");");
        var size = Run($"let result = dict.get(fs.stat(\"{filePath}\"), \"size\");");
        var name = Run($"let result = dict.get(fs.stat(\"{filePath}\"), \"name\");");

        Assert.Equal(true, isFile);
        Assert.Equal(false, isDir);
        Assert.Equal(11L, size);
        Assert.Equal("stat_props.txt", name);
    }

    [Fact]
    public void Stat_DirProperties()
    {
        var dirPath = Path.Combine(_testDir, "stat_subdir");
        Directory.CreateDirectory(dirPath);

        var isFile = Run($"let result = dict.get(fs.stat(\"{dirPath}\"), \"isFile\");");
        var isDir = Run($"let result = dict.get(fs.stat(\"{dirPath}\"), \"isDir\");");

        Assert.Equal(false, isFile);
        Assert.Equal(true, isDir);
    }

    [Fact]
    public void Stat_HasModifiedTime()
    {
        var filePath = Path.Combine(_testDir, "stat_mtime.txt");
        File.WriteAllText(filePath, "content");

        var modified = Run($"let result = dict.get(fs.stat(\"{filePath}\"), \"modified\");");
        Assert.IsType<double>(modified);
        Assert.True((double)modified! > 0);
    }

    [Fact]
    public void Stat_HasCreatedTime()
    {
        var filePath = Path.Combine(_testDir, "stat_ctime.txt");
        File.WriteAllText(filePath, "content");

        var created = Run($"let result = dict.get(fs.stat(\"{filePath}\"), \"created\");");
        Assert.IsType<double>(created);
        Assert.True((double)created! > 0);
    }

    [Fact]
    public void Stat_NonexistentPathThrows()
    {
        var missing = Path.Combine(_testDir, "does_not_exist.txt");
        RunExpectingError($"fs.stat(\"{missing}\");");
    }

    [Fact]
    public void Stat_NonStringThrows()
    {
        RunExpectingError("fs.stat(42);");
    }

    [Fact]
    public void Stat_NameMatchesFileName()
    {
        var filePath = Path.Combine(_testDir, "myspecialfile.txt");
        File.WriteAllText(filePath, "data");

        var name = Run($"let result = dict.get(fs.stat(\"{filePath}\"), \"name\");");
        Assert.Equal("myspecialfile.txt", name);
    }
}
