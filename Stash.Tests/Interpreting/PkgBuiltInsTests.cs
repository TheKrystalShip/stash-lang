using Stash.Lexing;
using Stash.Parsing;
using Stash.Bytecode;
using Stash.Resolution;
using Stash.Runtime.Types;

namespace Stash.Tests.Interpreting;

public class PkgBuiltInsTests
{
    private static object? Run(string source)
    {
        string full = source + "\nreturn result;";
        var lexer = new Lexer(full, "<test>");
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        var stmts = parser.ParseProgram();
        SemanticResolver.Resolve(stmts);
        var chunk = Compiler.Compile(stmts);
        var vm = new VirtualMachine(TestVM.CreateGlobals());
        return vm.Execute(chunk);
    }

    private static object? RunWithFile(string source, string currentFile)
    {
        string full = source + "\nreturn result;";
        var lexer = new Lexer(full, "<test>");
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        var stmts = parser.ParseProgram();
        SemanticResolver.Resolve(stmts);
        var chunk = Compiler.Compile(stmts);
        var vm = new VirtualMachine(TestVM.CreateGlobals());
        vm.CurrentFile = currentFile;
        return vm.Execute(chunk);
    }

    private static string MakeTempDir()
    {
        string tmpDir = Path.Combine(Path.GetTempPath(), "stash_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmpDir);
        return tmpDir;
    }

    // ── No project root (no stash.json) ───────────────────────────────────

    [Fact]
    public void Root_NoStashJson_ReturnsNull()
    {
        string tmpDir = MakeTempDir();
        try
        {
            string currentFile = Path.Combine(tmpDir, "main.stash");
            object? result = RunWithFile("let result = pkg.root();", currentFile);
            Assert.Null(result);
        }
        finally
        {
            Directory.Delete(tmpDir, true);
        }
    }

    [Fact]
    public void Version_NoStashJson_ReturnsNull()
    {
        string tmpDir = MakeTempDir();
        try
        {
            string currentFile = Path.Combine(tmpDir, "main.stash");
            object? result = RunWithFile("let result = pkg.version();", currentFile);
            Assert.Null(result);
        }
        finally
        {
            Directory.Delete(tmpDir, true);
        }
    }

    [Fact]
    public void Info_NoStashJson_ReturnsNull()
    {
        string tmpDir = MakeTempDir();
        try
        {
            string currentFile = Path.Combine(tmpDir, "main.stash");
            object? result = RunWithFile("let result = pkg.info();", currentFile);
            Assert.Null(result);
        }
        finally
        {
            Directory.Delete(tmpDir, true);
        }
    }

    [Fact]
    public void Dependencies_NoStashJson_ReturnsNull()
    {
        string tmpDir = MakeTempDir();
        try
        {
            string currentFile = Path.Combine(tmpDir, "main.stash");
            object? result = RunWithFile("let result = pkg.dependencies();", currentFile);
            Assert.Null(result);
        }
        finally
        {
            Directory.Delete(tmpDir, true);
        }
    }

    // ── With stash.json (basic project) ───────────────────────────────────

    private static readonly string _basicManifest = """
        {"name": "test-project", "version": "2.1.0", "description": "A test", "author": "Test Author", "license": "MIT", "main": "index.stash"}
        """;

    [Fact]
    public void Root_WithStashJson_ReturnsProjectRoot()
    {
        string tmpDir = MakeTempDir();
        try
        {
            File.WriteAllText(Path.Combine(tmpDir, "stash.json"), _basicManifest);
            string currentFile = Path.Combine(tmpDir, "main.stash");
            object? result = RunWithFile("let result = pkg.root();", currentFile);
            Assert.Equal(tmpDir, result);
        }
        finally
        {
            Directory.Delete(tmpDir, true);
        }
    }

    [Fact]
    public void Version_WithStashJson_ReturnsVersion()
    {
        string tmpDir = MakeTempDir();
        try
        {
            File.WriteAllText(Path.Combine(tmpDir, "stash.json"), _basicManifest);
            string currentFile = Path.Combine(tmpDir, "main.stash");
            object? result = RunWithFile("let result = pkg.version();", currentFile);
            Assert.Equal("2.1.0", result);
        }
        finally
        {
            Directory.Delete(tmpDir, true);
        }
    }

    [Fact]
    public void Info_WithStashJson_ReturnsDict()
    {
        string tmpDir = MakeTempDir();
        try
        {
            File.WriteAllText(Path.Combine(tmpDir, "stash.json"), _basicManifest);
            string currentFile = Path.Combine(tmpDir, "main.stash");
            object? result = RunWithFile("let result = pkg.info();", currentFile);
            Assert.NotNull(result);
            Assert.IsType<StashDictionary>(result);
        }
        finally
        {
            Directory.Delete(tmpDir, true);
        }
    }

    [Fact]
    public void Info_Name_ReturnsCorrectValue()
    {
        string tmpDir = MakeTempDir();
        try
        {
            File.WriteAllText(Path.Combine(tmpDir, "stash.json"), _basicManifest);
            string currentFile = Path.Combine(tmpDir, "main.stash");
            object? result = RunWithFile("""
                let info = pkg.info();
                let result = info["name"];
                """, currentFile);
            Assert.Equal("test-project", result);
        }
        finally
        {
            Directory.Delete(tmpDir, true);
        }
    }

    [Fact]
    public void Info_Version_ReturnsCorrectValue()
    {
        string tmpDir = MakeTempDir();
        try
        {
            File.WriteAllText(Path.Combine(tmpDir, "stash.json"), _basicManifest);
            string currentFile = Path.Combine(tmpDir, "main.stash");
            object? result = RunWithFile("""
                let info = pkg.info();
                let result = info["version"];
                """, currentFile);
            Assert.Equal("2.1.0", result);
        }
        finally
        {
            Directory.Delete(tmpDir, true);
        }
    }

    [Fact]
    public void Info_Description_ReturnsCorrectValue()
    {
        string tmpDir = MakeTempDir();
        try
        {
            File.WriteAllText(Path.Combine(tmpDir, "stash.json"), _basicManifest);
            string currentFile = Path.Combine(tmpDir, "main.stash");
            object? result = RunWithFile("""
                let info = pkg.info();
                let result = info["description"];
                """, currentFile);
            Assert.Equal("A test", result);
        }
        finally
        {
            Directory.Delete(tmpDir, true);
        }
    }

    // ── Nested directory (finds parent stash.json) ────────────────────────

    [Fact]
    public void Root_NestedDirectory_FindsParent()
    {
        string tmpDir = MakeTempDir();
        try
        {
            File.WriteAllText(Path.Combine(tmpDir, "stash.json"), _basicManifest);
            string srcDir = Path.Combine(tmpDir, "src");
            Directory.CreateDirectory(srcDir);
            string currentFile = Path.Combine(srcDir, "main.stash");
            object? result = RunWithFile("let result = pkg.root();", currentFile);
            Assert.Equal(tmpDir, result);
        }
        finally
        {
            Directory.Delete(tmpDir, true);
        }
    }

    // ── Dependencies from manifest ────────────────────────────────────────

    private static readonly string _depsManifest = """
        {"name": "test", "version": "1.0.0", "dependencies": {"http-utils": "^1.2.0", "logger": "~2.0.0"}}
        """;

    [Fact]
    public void Dependencies_FromManifest_ReturnsDeps()
    {
        string tmpDir = MakeTempDir();
        try
        {
            File.WriteAllText(Path.Combine(tmpDir, "stash.json"), _depsManifest);
            string currentFile = Path.Combine(tmpDir, "main.stash");
            object? result = RunWithFile("""
                let deps = pkg.dependencies();
                let result = deps["http-utils"];
                """, currentFile);
            Assert.Equal("^1.2.0", result);
        }
        finally
        {
            Directory.Delete(tmpDir, true);
        }
    }

    [Fact]
    public void Dependencies_FromManifest_ReturnsAllDeps()
    {
        string tmpDir = MakeTempDir();
        try
        {
            File.WriteAllText(Path.Combine(tmpDir, "stash.json"), _depsManifest);
            string currentFile = Path.Combine(tmpDir, "main.stash");
            object? result = RunWithFile("""
                let deps = pkg.dependencies();
                let result = deps["logger"];
                """, currentFile);
            Assert.Equal("~2.0.0", result);
        }
        finally
        {
            Directory.Delete(tmpDir, true);
        }
    }

    // ── Dependencies from lock file ───────────────────────────────────────

    private static readonly string _lockFileContent = """
        {"lockVersion": 1, "resolved": {"http-utils": {"version": "1.3.0"}, "logger": {"version": "2.0.1"}}}
        """;

    [Fact]
    public void Dependencies_FromLockFile_ReturnsResolvedVersions()
    {
        string tmpDir = MakeTempDir();
        try
        {
            File.WriteAllText(Path.Combine(tmpDir, "stash.json"), _depsManifest);
            File.WriteAllText(Path.Combine(tmpDir, "stash-lock.json"), _lockFileContent);
            string currentFile = Path.Combine(tmpDir, "main.stash");
            object? result = RunWithFile("""
                let deps = pkg.dependencies();
                let result = deps["http-utils"];
                """, currentFile);
            Assert.Equal("1.3.0", result);
        }
        finally
        {
            Directory.Delete(tmpDir, true);
        }
    }

    [Fact]
    public void Dependencies_FromLockFile_PreferredOverManifest()
    {
        string tmpDir = MakeTempDir();
        try
        {
            File.WriteAllText(Path.Combine(tmpDir, "stash.json"), _depsManifest);
            File.WriteAllText(Path.Combine(tmpDir, "stash-lock.json"), _lockFileContent);
            string currentFile = Path.Combine(tmpDir, "main.stash");
            object? result = RunWithFile("""
                let deps = pkg.dependencies();
                let result = deps["logger"];
                """, currentFile);
            Assert.Equal("2.0.1", result);
        }
        finally
        {
            Directory.Delete(tmpDir, true);
        }
    }

    // ── No dependencies ───────────────────────────────────────────────────

    [Fact]
    public void Dependencies_NoDependencies_ReturnsNull()
    {
        string tmpDir = MakeTempDir();
        try
        {
            File.WriteAllText(Path.Combine(tmpDir, "stash.json"), """{"name": "no-deps", "version": "1.0.0"}""");
            string currentFile = Path.Combine(tmpDir, "main.stash");
            object? result = RunWithFile("let result = pkg.dependencies();", currentFile);
            Assert.Null(result);
        }
        finally
        {
            Directory.Delete(tmpDir, true);
        }
    }

    // ── Minimal manifest ──────────────────────────────────────────────────

    [Fact]
    public void Info_MinimalManifest_HasNameAndVersion()
    {
        string tmpDir = MakeTempDir();
        try
        {
            File.WriteAllText(Path.Combine(tmpDir, "stash.json"), """{"name": "minimal", "version": "0.1.0"}""");
            string currentFile = Path.Combine(tmpDir, "main.stash");

            object? nameResult = RunWithFile("""
                let info = pkg.info();
                let result = info["name"];
                """, currentFile);
            Assert.Equal("minimal", nameResult);

            object? versionResult = RunWithFile("""
                let info = pkg.info();
                let result = info["version"];
                """, currentFile);
            Assert.Equal("0.1.0", versionResult);
        }
        finally
        {
            Directory.Delete(tmpDir, true);
        }
    }

    // ── Additional manifest fields ────────────────────────────────────────

    [Fact]
    public void Info_WithKeywords_ReturnsArray()
    {
        string tmpDir = Path.Combine(Path.GetTempPath(), "stash_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmpDir);
        try
        {
            File.WriteAllText(Path.Combine(tmpDir, "stash.json"),
                """{"name": "test", "version": "1.0.0", "keywords": ["cli", "utils", "tools"]}""");
            string currentFile = Path.Combine(tmpDir, "main.stash");

            var result = RunWithFile("""
                let info = pkg.info();
                let result = info["keywords"];
            """, currentFile);

            var list = Assert.IsType<List<object?>>(result);
            Assert.Equal(3, list.Count);
            Assert.Equal("cli", list[0]);
            Assert.Equal("utils", list[1]);
            Assert.Equal("tools", list[2]);
        }
        finally
        {
            Directory.Delete(tmpDir, true);
        }
    }

    [Fact]
    public void Info_WithPrivateFlag_ReturnsBool()
    {
        string tmpDir = Path.Combine(Path.GetTempPath(), "stash_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmpDir);
        try
        {
            File.WriteAllText(Path.Combine(tmpDir, "stash.json"),
                """{"name": "internal-lib", "version": "1.0.0", "private": true}""");
            string currentFile = Path.Combine(tmpDir, "main.stash");

            var result = RunWithFile("""
                let info = pkg.info();
                let result = info["private"];
            """, currentFile);

            Assert.Equal(true, result);
        }
        finally
        {
            Directory.Delete(tmpDir, true);
        }
    }

    [Fact]
    public void Info_WithDependencies_ReturnsNestedDict()
    {
        string tmpDir = Path.Combine(Path.GetTempPath(), "stash_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmpDir);
        try
        {
            File.WriteAllText(Path.Combine(tmpDir, "stash.json"),
                """{"name": "test", "version": "1.0.0", "dependencies": {"http-utils": "^1.2.0", "logger": "~2.0.0"}}""");
            string currentFile = Path.Combine(tmpDir, "main.stash");

            var result = RunWithFile("""
                let info = pkg.info();
                let deps = info["dependencies"];
                let result = deps["http-utils"];
            """, currentFile);

            Assert.Equal("^1.2.0", result);
        }
        finally
        {
            Directory.Delete(tmpDir, true);
        }
    }
}
