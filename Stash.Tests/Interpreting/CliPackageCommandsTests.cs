using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using Stash.Cli.PackageManager;
using Stash.Cli.PackageManager.Commands;
using Stash.Common;

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
        // Specifier parsing only — the install side-effect is no longer tested
        // here because manifest writes are deferred until after a successful install
        // (atomicity fix). See InstallAtomicityTests for end-to-end install behavior.
        var (name, constraint) = InstallCommand.ParseSpecifier("foo@1.2.0");
        Assert.Equal("foo", name);
        Assert.Equal("^1.2.0", constraint);
    }

    [Fact]
    public void Install_NameOnly_UsesWildcard()
    {
        var (name, constraint) = InstallCommand.ParseSpecifier("bar");
        Assert.Equal("bar", name);
        Assert.Equal("*", constraint);
    }

    [Fact]
    public void Install_GitSpecifier_ExtractsRepoName()
    {
        string specifier = "git:https://github.com/user/my-tool.git#v1.0.0";
        var (name, constraint) = InstallCommand.ParseSpecifier(specifier);
        Assert.Equal("my-tool", name);
        Assert.Equal(specifier, constraint);
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

    private sealed class StubVersionLookup : IVersionLookup
    {
        private readonly Dictionary<string, (List<SemVer> Versions, SemVer? Latest)> _data;

        public StubVersionLookup(Dictionary<string, (List<SemVer> Versions, SemVer? Latest)> data)
        {
            _data = data;
        }

        public (List<SemVer> Versions, SemVer? Latest) GetVersionsAndLatest(string packageName)
        {
            if (_data.TryGetValue(packageName, out var v)) return v;
            return (new List<SemVer>(), null);
        }
    }

    [Fact]
    public void Outdated_WithLockedDeps_ShowsTable()
    {
        WriteSimpleManifest(deps: new Dictionary<string, string> { ["pkg-a"] = "^1.0.0" });
        WriteLockFile(new Dictionary<string, (string, string)>
        {
            ["pkg-a"] = ("1.2.0", "https://example.com/pkg-a-1.2.0.tar.gz")
        });

        var manifest = PackageManifest.Load(_tempDir)!;
        var lockFile = LockFile.Load(_tempDir);
        var lookup = new StubVersionLookup(new()
        {
            ["pkg-a"] = (new List<SemVer> { SemVer.Parse("1.2.0")!, SemVer.Parse("1.3.0")! }, SemVer.Parse("1.3.0"))
        });

        string output = CaptureStdOut(() => OutdatedCommand.Run(manifest, lockFile, lookup));

        Assert.Contains("Package", output);
        Assert.Contains("Current", output);
        Assert.Contains("pkg-a", output);
        Assert.Contains("1.2.0", output);
        Assert.Contains("1.3.0", output);
    }

    [Fact]
    public void Outdated_NoDepsInstalled_ShowsMessage()
    {
        WriteSimpleManifest();

        string output = CaptureStdOut(() => OutdatedCommand.Execute([]));

        Assert.Contains("No dependencies declared", output);
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

    // ── InfoCommand ───────────────────────────────────────────────────────────

    [Fact]
    public void Info_PackageWithVersionDeprecation_RendersDeprecatedSuffixAndMessage()
    {
        // Uses InfoCommand.Render(json) directly — no registry server needed.
        string json = """
            {
                "name": "my-pkg",
                "latest": "1.1.0",
                "versions": {
                    "1.0.0": {
                        "publishedAt": "2026-01-01",
                        "deprecated": true,
                        "deprecationMessage": "use 1.1.0 instead"
                    },
                    "1.1.0": {
                        "publishedAt": "2026-02-01"
                    }
                }
            }
            """;

        string output = CaptureStdOut(() => InfoCommand.Render(json));

        Assert.Contains("(deprecated)", output);
        Assert.Contains("deprecated: use 1.1.0 instead", output);
        // Non-deprecated version should not have the suffix.
        Assert.DoesNotContain("1.1.0" + "  (deprecated)", output);
    }

    [Fact]
    public void Info_PackageWithPackageLevelDeprecation_RendersTopLevelDeprecation()
    {
        string json = """
            {
                "name": "old-pkg",
                "latest": "1.0.0",
                "deprecated": true,
                "deprecationMessage": "this package is unmaintained",
                "deprecationAlternative": "new-pkg",
                "versions": {
                    "1.0.0": { "publishedAt": "2025-01-01" }
                }
            }
            """;

        string output = CaptureStdOut(() => InfoCommand.Render(json));

        Assert.Contains("DEPRECATED: this package is unmaintained", output);
        Assert.Contains("Suggested alternative: new-pkg", output);
    }

    [Fact]
    public void Info_NonDeprecatedPackage_NoDeprecationOutput()
    {
        string json = """
            {
                "name": "good-pkg",
                "latest": "2.0.0",
                "versions": {
                    "2.0.0": { "publishedAt": "2026-03-01" }
                }
            }
            """;

        string output = CaptureStdOut(() => InfoCommand.Render(json));

        Assert.DoesNotContain("(deprecated)", output);
        Assert.DoesNotContain("DEPRECATED", output);
        Assert.DoesNotContain("Suggested alternative", output);
    }
}
