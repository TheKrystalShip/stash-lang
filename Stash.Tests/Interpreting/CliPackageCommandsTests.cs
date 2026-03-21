using System.Text.Json;
using System.Text.Json.Nodes;
using Stash.Common;
using Stash.Cli.PackageManager;
using Stash.Cli.PackageManager.Commands;

namespace Stash.Tests.Interpreting;

[CollectionDefinition("CliTests", DisableParallelization = true)]
public class CliTestCollection { }

[Collection("CliTests")]
public class CliPackageCommandsTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _savedDir;

    public CliPackageCommandsTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "stash-cli-test-" + Path.GetRandomFileName());
        Directory.CreateDirectory(_tempDir);
        _savedDir = Directory.GetCurrentDirectory();
        Directory.SetCurrentDirectory(_tempDir);
    }

    public void Dispose()
    {
        Directory.SetCurrentDirectory(_savedDir);
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string CaptureStdOut(Action action)
    {
        var originalOut = Console.Out;
        using var sw = new StringWriter();
        Console.SetOut(sw);
        try
        {
            action();
        }
        finally
        {
            Console.SetOut(originalOut);
        }
        return sw.ToString();
    }

    private static string CaptureStdErr(Action action)
    {
        var originalErr = Console.Error;
        using var sw = new StringWriter();
        Console.SetError(sw);
        try
        {
            action();
        }
        finally
        {
            Console.SetError(originalErr);
        }
        return sw.ToString();
    }

    private void WriteSimpleManifest(string name = "test-package", string version = "1.0.0",
        Dictionary<string, string>? deps = null)
    {
        var root = new JsonObject
        {
            ["name"] = name,
            ["version"] = version,
            ["license"] = "MIT",
            ["main"] = "index.stash"
        };
        if (deps != null)
        {
            var depsObj = new JsonObject();
            foreach (var (k, v) in deps)
            {
                depsObj[k] = v;
            }

            root["dependencies"] = depsObj;
        }
        File.WriteAllText(
            Path.Combine(_tempDir, "stash.json"),
            root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n");
    }

    private void WriteLockFile(Dictionary<string, (string Version, string Resolved)> packages)
    {
        var lockFile = new LockFile();
        foreach (var (name, (version, resolved)) in packages)
        {
            lockFile.Resolved[name] = new LockFileEntry { Version = version, Resolved = resolved };
        }

        lockFile.Save(_tempDir);
    }

    // ── InitCommand ───────────────────────────────────────────────────────────

    [Fact]
    public void Init_WithYesFlag_CreatesValidManifest()
    {
        CaptureStdOut(() => InitCommand.Execute(["--yes"]));

        string manifestPath = Path.Combine(_tempDir, "stash.json");
        Assert.True(File.Exists(manifestPath));

        var manifest = PackageManifest.Load(_tempDir);
        Assert.NotNull(manifest);
        Assert.False(string.IsNullOrEmpty(manifest.Name));
        Assert.Equal("1.0.0", manifest.Version);
        Assert.Equal("MIT", manifest.License);
        Assert.Equal("index.stash", manifest.Main);
    }

    [Fact]
    public void Init_WithShortFlag_CreatesValidManifest()
    {
        CaptureStdOut(() => InitCommand.Execute(["-y"]));

        var manifest = PackageManifest.Load(_tempDir);
        Assert.NotNull(manifest);
        Assert.Equal("1.0.0", manifest.Version);
    }

    [Fact]
    public void Init_WithYesFlag_SanitizesDirectoryName()
    {
        // Dir starting with digits and containing special chars should produce a clean name
        string specialDir = Path.Combine(_tempDir, "123-My_Package!");
        Directory.CreateDirectory(specialDir);
        Directory.SetCurrentDirectory(specialDir);

        CaptureStdOut(() => InitCommand.Execute(["--yes"]));

        var manifest = PackageManifest.Load(specialDir);
        Assert.NotNull(manifest);
        // "123-My_Package!" → lower → "123-my_package!" → replace [^a-z0-9-] → "123-my-package-"
        // → strip leading non-alpha (^[^a-z]+) → "my-package-" → strip trailing hyphens → "my-package"
        Assert.Equal("my-package", manifest.Name);
    }

    // ── InstallCommand ────────────────────────────────────────────────────────

    [Fact]
    public void Install_NameAtVersion_WrapsInCaretRange()
    {
        WriteSimpleManifest();

        CaptureStdOut(() => InstallCommand.Execute(["foo@1.2.0"]));

        var manifest = PackageManifest.Load(_tempDir);
        Assert.NotNull(manifest?.Dependencies);
        Assert.True(manifest.Dependencies.ContainsKey("foo"));
        Assert.Equal("^1.2.0", manifest.Dependencies["foo"]);
    }

    [Fact]
    public void Install_NameOnly_UsesWildcard()
    {
        WriteSimpleManifest();

        CaptureStdOut(() => InstallCommand.Execute(["bar"]));

        var manifest = PackageManifest.Load(_tempDir);
        Assert.NotNull(manifest?.Dependencies);
        Assert.Equal("*", manifest.Dependencies["bar"]);
    }

    [Fact]
    public void Install_GitSpecifier_ExtractsRepoName()
    {
        WriteSimpleManifest();

        string specifier = "git:https://github.com/user/my-tool.git#v1.0.0";
        CaptureStdOut(() => InstallCommand.Execute([specifier]));

        var manifest = PackageManifest.Load(_tempDir);
        Assert.NotNull(manifest?.Dependencies);
        Assert.True(manifest.Dependencies.ContainsKey("my-tool"));
        Assert.Equal(specifier, manifest.Dependencies["my-tool"]);
    }

    // ── UninstallCommand ──────────────────────────────────────────────────────

    [Fact]
    public void Uninstall_ExistingDep_RemovesFromManifestAndDisk()
    {
        WriteSimpleManifest(deps: new Dictionary<string, string> { ["my-pkg"] = "^1.0.0" });
        WriteLockFile(new Dictionary<string, (string, string)>
        {
            ["my-pkg"] = ("1.0.0", "https://example.com/my-pkg-1.0.0.tar.gz")
        });

        string pkgDir = Path.Combine(_tempDir, "stashes", "my-pkg");
        Directory.CreateDirectory(pkgDir);
        File.WriteAllText(Path.Combine(pkgDir, "index.stash"), "// stub");

        CaptureStdOut(() => UninstallCommand.Execute(["my-pkg"]));

        var manifest = PackageManifest.Load(_tempDir);
        Assert.NotNull(manifest);
        Assert.False(manifest.Dependencies?.ContainsKey("my-pkg") ?? false);
        Assert.False(Directory.Exists(pkgDir));
    }

    [Fact]
    public void Uninstall_MissingDep_ThrowsInvalidOperation()
    {
        WriteSimpleManifest();

        Assert.Throws<InvalidOperationException>(() =>
            UninstallCommand.Execute(["nonexistent"]));
    }

    // ── ListCommand ───────────────────────────────────────────────────────────

    [Fact]
    public void List_WithDeps_ShowsTree()
    {
        WriteSimpleManifest(deps: new Dictionary<string, string> { ["alpha"] = "^1.0.0" });
        WriteLockFile(new Dictionary<string, (string, string)>
        {
            ["alpha"] = ("1.2.3", "https://example.com/alpha-1.2.3.tar.gz")
        });

        string output = CaptureStdOut(() => ListCommand.Execute([]));

        Assert.Contains("test-package@1.0.0", output);
        Assert.Contains("alpha@1.2.3", output);
        Assert.Contains("└──", output);
    }

    [Fact]
    public void List_NoDepsInstalled_ShowsEmptyMessage()
    {
        WriteSimpleManifest();

        string output = CaptureStdOut(() => ListCommand.Execute([]));

        Assert.Contains("(no dependencies installed)", output);
    }

    // ── PackCommand ───────────────────────────────────────────────────────────

    [Fact]
    public void Pack_CreatesCorrectTarball()
    {
        WriteSimpleManifest(name: "my-package", version: "2.3.1");
        File.WriteAllText(Path.Combine(_tempDir, "index.stash"), "// main");

        CaptureStdOut(() => PackCommand.Execute([]));

        Assert.True(File.Exists(Path.Combine(_tempDir, "my-package-2.3.1.tar.gz")));
    }

    [Fact]
    public void Pack_ScopedPackage_FormatsFilenameCorrectly()
    {
        WriteSimpleManifest(name: "@scope/tool", version: "1.0.0");
        File.WriteAllText(Path.Combine(_tempDir, "index.stash"), "// main");

        CaptureStdOut(() => PackCommand.Execute([]));

        Assert.True(File.Exists(Path.Combine(_tempDir, "scope-tool-1.0.0.tar.gz")));
    }

    // ── OutdatedCommand ───────────────────────────────────────────────────────

    [Fact]
    public void Outdated_WithLockedDeps_ShowsTable()
    {
        WriteSimpleManifest(deps: new Dictionary<string, string> { ["pkg-a"] = "^1.0.0" });
        WriteLockFile(new Dictionary<string, (string, string)>
        {
            ["pkg-a"] = ("1.2.0", "https://example.com/pkg-a-1.2.0.tar.gz")
        });

        string output = CaptureStdOut(() => OutdatedCommand.Execute([]));

        Assert.Contains("Package", output);
        Assert.Contains("Current", output);
        Assert.Contains("pkg-a", output);
        Assert.Contains("1.2.0", output);
        Assert.Contains("^1.0.0", output);
    }

    [Fact]
    public void Outdated_NoDepsInstalled_ShowsMessage()
    {
        WriteSimpleManifest();

        string output = CaptureStdOut(() => OutdatedCommand.Execute([]));

        Assert.Contains("No dependencies installed", output);
    }

    // ── UpdateCommand ─────────────────────────────────────────────────────────

    [Fact]
    public void Update_All_DeletesLockFile()
    {
        WriteSimpleManifest(deps: new Dictionary<string, string> { ["dep"] = "^1.0.0" });
        WriteLockFile(new Dictionary<string, (string, string)>
        {
            ["dep"] = ("1.0.0", "https://example.com/dep-1.0.0.tar.gz")
        });

        string lockPath = Path.Combine(_tempDir, "stash-lock.json");
        Assert.True(File.Exists(lockPath));

        // Capture both streams — UpdateCommand prints error to stderr when no registry is available
        CaptureStdOut(() => CaptureStdErr(() => UpdateCommand.Execute([])));

        Assert.False(File.Exists(lockPath));
    }

    // ── PackageCommands Routing ───────────────────────────────────────────────

    [Fact]
    public void Run_HelpCommand_PrintsUsage()
    {
        string output = CaptureStdOut(() => PackageCommands.Run(["help"]));

        Assert.Contains("Usage:", output);
        Assert.Contains("stash pkg", output);
    }

    [Fact]
    public void Run_NoArgs_PrintsUsage()
    {
        string output = CaptureStdOut(() => PackageCommands.Run([]));

        Assert.Contains("Usage:", output);
    }
}
