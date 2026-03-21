using Stash.Common;
using Stash.Cli.PackageManager;

namespace Stash.Tests.Interpreting;

public class DependencyResolverTests
{
    private class MockPackageSource : IPackageSource
    {
        private readonly Dictionary<string, List<(SemVer version, PackageManifest manifest)>> _packages = new();

        public void AddPackage(string name, string version, Dictionary<string, string>? deps = null)
        {
            if (!_packages.TryGetValue(name, out var list))
            {
                list = new();
                _packages[name] = list;
            }
            var sv = SemVer.Parse(version);
            list.Add((sv, new PackageManifest
            {
                Name = name,
                Version = version,
                Dependencies = deps
            }));
        }

        public List<SemVer> GetAvailableVersions(string packageName)
            => _packages.TryGetValue(packageName, out var list)
                ? list.Select(p => p.version).ToList()
                : new List<SemVer>();

        public PackageManifest? GetManifest(string packageName, SemVer version)
            => _packages.TryGetValue(packageName, out var list)
                ? list.FirstOrDefault(p => p.version.Equals(version)).manifest
                : null;

        public string GetResolvedUrl(string packageName, SemVer version)
            => $"https://registry.example.com/{packageName}/{version}.tar.gz";

        public string? GetIntegrity(string packageName, SemVer version)
            => $"sha256-mock-{packageName}-{version}";
    }

    // ── Single Dependency ────────────────────────────────────────────────────

    [Fact]
    public void Resolve_SingleDep_CaretConstraint_ResolvesToLatestCompatible()
    {
        var source = new MockPackageSource();
        source.AddPackage("http-utils", "1.0.0");
        source.AddPackage("http-utils", "1.2.0");
        source.AddPackage("http-utils", "1.3.0");

        var root = new PackageManifest
        {
            Name = "root",
            Dependencies = new Dictionary<string, string> { ["http-utils"] = "^1.0.0" }
        };

        var resolver = new DependencyResolver(source);
        var result = resolver.Resolve(root);

        Assert.True(result.ContainsKey("http-utils"));
        Assert.Equal("1.3.0", result["http-utils"].Version);
    }

    [Fact]
    public void Resolve_SingleDep_ExactConstraint_ResolvesToExactVersion()
    {
        var source = new MockPackageSource();
        source.AddPackage("utils", "1.0.0");
        source.AddPackage("utils", "1.1.0");

        var root = new PackageManifest
        {
            Name = "root",
            Dependencies = new Dictionary<string, string> { ["utils"] = "1.0.0" }
        };

        var resolver = new DependencyResolver(source);
        var result = resolver.Resolve(root);

        Assert.Equal("1.0.0", result["utils"].Version);
    }

    [Fact]
    public void Resolve_SingleDep_ResolvedEntryHasCorrectFields()
    {
        var source = new MockPackageSource();
        source.AddPackage("mylib", "2.1.0");

        var root = new PackageManifest
        {
            Name = "root",
            Dependencies = new Dictionary<string, string> { ["mylib"] = "2.1.0" }
        };

        var resolver = new DependencyResolver(source);
        var result = resolver.Resolve(root);

        var entry = result["mylib"];
        Assert.Equal("2.1.0", entry.Version);
        Assert.Equal("https://registry.example.com/mylib/2.1.0.tar.gz", entry.Resolved);
        Assert.Equal("sha256-mock-mylib-2.1.0", entry.Integrity);
    }

    // ── Latest Compatible Version ────────────────────────────────────────────

    [Fact]
    public void Resolve_CaretConstraint_ExcludesMajorBump()
    {
        var source = new MockPackageSource();
        source.AddPackage("lib", "1.0.0");
        source.AddPackage("lib", "1.2.0");
        source.AddPackage("lib", "1.3.0");
        source.AddPackage("lib", "1.4.0");
        source.AddPackage("lib", "2.0.0");

        var root = new PackageManifest
        {
            Name = "root",
            Dependencies = new Dictionary<string, string> { ["lib"] = "^1.2.0" }
        };

        var resolver = new DependencyResolver(source);
        var result = resolver.Resolve(root);

        Assert.Equal("1.4.0", result["lib"].Version);
    }

    [Fact]
    public void Resolve_CaretZeroMinor_RespectsMinorBreaking()
    {
        var source = new MockPackageSource();
        source.AddPackage("alpha", "0.1.0");
        source.AddPackage("alpha", "0.2.0");
        source.AddPackage("alpha", "0.2.5");
        source.AddPackage("alpha", "0.3.0");

        var root = new PackageManifest
        {
            Name = "root",
            Dependencies = new Dictionary<string, string> { ["alpha"] = "^0.2.0" }
        };

        var resolver = new DependencyResolver(source);
        var result = resolver.Resolve(root);

        Assert.Equal("0.2.5", result["alpha"].Version);
    }

    // ── Transitive Dependencies ──────────────────────────────────────────────

    [Fact]
    public void Resolve_TransitiveDeps_AllResolved()
    {
        var source = new MockPackageSource();
        source.AddPackage("a", "1.0.0", new Dictionary<string, string> { ["b"] = "^1.0.0" });
        source.AddPackage("b", "1.0.0", new Dictionary<string, string> { ["c"] = "^1.0.0" });
        source.AddPackage("c", "1.0.0");

        var root = new PackageManifest
        {
            Name = "root",
            Dependencies = new Dictionary<string, string> { ["a"] = "^1.0.0" }
        };

        var resolver = new DependencyResolver(source);
        var result = resolver.Resolve(root);

        Assert.True(result.ContainsKey("a"));
        Assert.True(result.ContainsKey("b"));
        Assert.True(result.ContainsKey("c"));
    }

    [Fact]
    public void Resolve_TransitiveDeps_ConstraintsRespected()
    {
        var source = new MockPackageSource();
        source.AddPackage("a", "1.0.0", new Dictionary<string, string> { ["c"] = "^1.0.0" });
        source.AddPackage("c", "1.0.0");
        source.AddPackage("c", "1.5.0");
        source.AddPackage("c", "2.0.0");

        var root = new PackageManifest
        {
            Name = "root",
            Dependencies = new Dictionary<string, string> { ["a"] = "^1.0.0" }
        };

        var resolver = new DependencyResolver(source);
        var result = resolver.Resolve(root);

        Assert.Equal("1.5.0", result["c"].Version);
    }

    [Fact]
    public void Resolve_TransitiveDeps_LockEntriesHaveDependencyMaps()
    {
        var source = new MockPackageSource();
        source.AddPackage("a", "1.0.0", new Dictionary<string, string> { ["b"] = "^1.0.0" });
        source.AddPackage("b", "1.0.0");

        var root = new PackageManifest
        {
            Name = "root",
            Dependencies = new Dictionary<string, string> { ["a"] = "^1.0.0" }
        };

        var resolver = new DependencyResolver(source);
        var result = resolver.Resolve(root);

        Assert.NotNull(result["a"].Dependencies);
        Assert.True(result["a"].Dependencies!.ContainsKey("b"));
    }

    // ── Version Conflict ─────────────────────────────────────────────────────

    [Fact]
    public void Resolve_VersionConflict_ThrowsWithConflictMessage()
    {
        var source = new MockPackageSource();
        source.AddPackage("a", "1.0.0", new Dictionary<string, string> { ["c"] = "^1.0.0" });
        source.AddPackage("b", "1.0.0", new Dictionary<string, string> { ["c"] = "^2.0.0" });
        source.AddPackage("c", "1.5.0");
        source.AddPackage("c", "2.5.0");

        var root = new PackageManifest
        {
            Name = "root",
            Dependencies = new Dictionary<string, string> { ["a"] = "^1.0.0", ["b"] = "^1.0.0" }
        };

        var resolver = new DependencyResolver(source);
        var ex = Assert.Throws<InvalidOperationException>(() => resolver.Resolve(root));

        Assert.Contains("Version conflict for", ex.Message);
    }

    [Fact]
    public void Resolve_VersionConflict_ErrorIncludesBothRequirers()
    {
        var source = new MockPackageSource();
        source.AddPackage("pkg-a", "1.0.0", new Dictionary<string, string> { ["shared"] = "^1.0.0" });
        source.AddPackage("pkg-b", "1.0.0", new Dictionary<string, string> { ["shared"] = "^2.0.0" });
        source.AddPackage("shared", "1.9.0");
        source.AddPackage("shared", "2.9.0");

        var root = new PackageManifest
        {
            Name = "root",
            Dependencies = new Dictionary<string, string> { ["pkg-a"] = "^1.0.0", ["pkg-b"] = "^1.0.0" }
        };

        var resolver = new DependencyResolver(source);
        var ex = Assert.Throws<InvalidOperationException>(() => resolver.Resolve(root));

        Assert.Contains("pkg-a@", ex.Message);
        Assert.Contains("pkg-b@", ex.Message);
    }

    // ── No Compatible Version ────────────────────────────────────────────────

    [Fact]
    public void Resolve_NoCompatibleVersion_ThrowsWithMessage()
    {
        var source = new MockPackageSource();
        source.AddPackage("foo", "1.0.0");
        source.AddPackage("foo", "2.0.0");

        var root = new PackageManifest
        {
            Name = "root",
            Dependencies = new Dictionary<string, string> { ["foo"] = "^3.0.0" }
        };

        var resolver = new DependencyResolver(source);
        var ex = Assert.Throws<InvalidOperationException>(() => resolver.Resolve(root));

        Assert.Contains("No version of", ex.Message);
        Assert.Contains("foo", ex.Message);
    }

    [Fact]
    public void Resolve_NoCompatibleVersion_ErrorIncludesAvailableVersions()
    {
        var source = new MockPackageSource();
        source.AddPackage("bar", "1.0.0");
        source.AddPackage("bar", "2.0.0");

        var root = new PackageManifest
        {
            Name = "root",
            Dependencies = new Dictionary<string, string> { ["bar"] = "^3.0.0" }
        };

        var resolver = new DependencyResolver(source);
        var ex = Assert.Throws<InvalidOperationException>(() => resolver.Resolve(root));

        Assert.Contains("1.0.0", ex.Message);
        Assert.Contains("2.0.0", ex.Message);
    }

    // ── Package Not Found ────────────────────────────────────────────────────

    [Fact]
    public void Resolve_PackageNotFound_Throws()
    {
        var source = new MockPackageSource();

        var root = new PackageManifest
        {
            Name = "root",
            Dependencies = new Dictionary<string, string> { ["nonexistent"] = "^1.0.0" }
        };

        var resolver = new DependencyResolver(source);
        var ex = Assert.Throws<InvalidOperationException>(() => resolver.Resolve(root));

        Assert.Contains("not found", ex.Message);
        Assert.Contains("nonexistent", ex.Message);
    }

    // ── Circular Dependencies ────────────────────────────────────────────────

    [Fact]
    public void Resolve_CircularDep_DirectCycle_Throws()
    {
        var source = new MockPackageSource();
        source.AddPackage("a", "1.0.0", new Dictionary<string, string> { ["b"] = "^1.0.0" });
        source.AddPackage("b", "1.0.0", new Dictionary<string, string> { ["a"] = "^1.0.0" });

        var root = new PackageManifest
        {
            Name = "root",
            Dependencies = new Dictionary<string, string> { ["a"] = "^1.0.0" }
        };

        var resolver = new DependencyResolver(source);
        var ex = Assert.Throws<InvalidOperationException>(() => resolver.Resolve(root));

        Assert.Contains("Circular dependency detected", ex.Message);
    }

    [Fact]
    public void Resolve_CircularDep_LongerCycle_Throws()
    {
        var source = new MockPackageSource();
        source.AddPackage("a", "1.0.0", new Dictionary<string, string> { ["b"] = "^1.0.0" });
        source.AddPackage("b", "1.0.0", new Dictionary<string, string> { ["c"] = "^1.0.0" });
        source.AddPackage("c", "1.0.0", new Dictionary<string, string> { ["a"] = "^1.0.0" });

        var root = new PackageManifest
        {
            Name = "root",
            Dependencies = new Dictionary<string, string> { ["a"] = "^1.0.0" }
        };

        var resolver = new DependencyResolver(source);
        var ex = Assert.Throws<InvalidOperationException>(() => resolver.Resolve(root));

        Assert.Contains("Circular dependency detected", ex.Message);
    }

    // ── Git Dependencies ─────────────────────────────────────────────────────

    [Fact]
    public void Resolve_GitDep_EntryHasGitUrl()
    {
        var source = new MockPackageSource();
        const string gitConstraint = "git:https://github.com/user/tool.git#v1.0.0";

        var root = new PackageManifest
        {
            Name = "root",
            Dependencies = new Dictionary<string, string> { ["tool"] = gitConstraint }
        };

        var resolver = new DependencyResolver(source);
        var result = resolver.Resolve(root);

        Assert.True(result.ContainsKey("tool"));
        var entry = result["tool"];
        Assert.Equal("", entry.Version);
        Assert.Equal(gitConstraint, entry.Resolved);
        Assert.Null(entry.Integrity);
    }

    [Fact]
    public void Resolve_GitDepMixedWithRegistry_BothResolved()
    {
        var source = new MockPackageSource();
        source.AddPackage("registry-pkg", "1.0.0");
        const string gitConstraint = "git:https://github.com/user/git-pkg.git#main";

        var root = new PackageManifest
        {
            Name = "root",
            Dependencies = new Dictionary<string, string>
            {
                ["registry-pkg"] = "^1.0.0",
                ["git-pkg"] = gitConstraint
            }
        };

        var resolver = new DependencyResolver(source);
        var result = resolver.Resolve(root);

        Assert.Equal("1.0.0", result["registry-pkg"].Version);
        Assert.Equal(gitConstraint, result["git-pkg"].Resolved);
        Assert.Equal("", result["git-pkg"].Version);
    }

    // ── No Dependencies ──────────────────────────────────────────────────────

    [Fact]
    public void Resolve_NullDependencies_ReturnsEmpty()
    {
        var source = new MockPackageSource();

        var root = new PackageManifest
        {
            Name = "root",
            Dependencies = null
        };

        var resolver = new DependencyResolver(source);
        var result = resolver.Resolve(root);

        Assert.Empty(result);
    }

    // ── Multiple Constraints on Same Package ─────────────────────────────────

    [Fact]
    public void Resolve_MultipleConstraints_PicksLatestSatisfyingAll()
    {
        var source = new MockPackageSource();
        source.AddPackage("a", "1.0.0", new Dictionary<string, string> { ["c"] = "^1.2.0" });
        source.AddPackage("b", "1.0.0", new Dictionary<string, string> { ["c"] = "^1.0.0" });
        source.AddPackage("c", "1.0.0");
        source.AddPackage("c", "1.2.0");
        source.AddPackage("c", "1.3.0");
        source.AddPackage("c", "1.4.0");
        source.AddPackage("c", "2.0.0");

        var root = new PackageManifest
        {
            Name = "root",
            Dependencies = new Dictionary<string, string> { ["a"] = "^1.0.0", ["b"] = "^1.0.0" }
        };

        var resolver = new DependencyResolver(source);
        var result = resolver.Resolve(root);

        Assert.Equal("1.4.0", result["c"].Version);
    }

    [Fact]
    public void Resolve_MultipleConstraints_TildeNarrowsRange()
    {
        var source = new MockPackageSource();
        source.AddPackage("a", "1.0.0", new Dictionary<string, string> { ["c"] = "^1.0.0" });
        source.AddPackage("b", "1.0.0", new Dictionary<string, string> { ["c"] = "~1.2.0" });
        source.AddPackage("c", "1.0.0");
        source.AddPackage("c", "1.2.0");
        source.AddPackage("c", "1.2.5");
        source.AddPackage("c", "1.3.0");
        source.AddPackage("c", "1.4.0");

        var root = new PackageManifest
        {
            Name = "root",
            Dependencies = new Dictionary<string, string> { ["a"] = "^1.0.0", ["b"] = "^1.0.0" }
        };

        var resolver = new DependencyResolver(source);
        var result = resolver.Resolve(root);

        Assert.Equal("1.2.5", result["c"].Version);
    }
}
