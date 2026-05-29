namespace Stash.Tests.Stdlib.SourceGenerator;

using System.Collections.Generic;
using System.Linq;
using Stash.Stdlib;
using Xunit;

public class Wave1ThrowsCoverageTests
{
    // Wave 1 namespaces per spec §5.
    // Each name maps to the set of function names that intentionally throw
    // nothing (so an empty Throws on those is correct, not a coverage gap).
    private static readonly Dictionary<string, HashSet<string>> NoThrowAllowList = new()
    {
        ["fs"] = new() { "exists", "dirExists", "pathExists", "isFile", "isDir", "isSymlink", "tempFile", "tempDir", "readable", "writable", "executable" },
        ["io"] = new() { "println", "print", "eprintln", "eprint", "readLine", "pathSeparator", "newLine" },
        ["conv"] = new() { "toStr", "toBool" },
        ["json"] = new(), // every json fn throws
        ["http"] = new(), // every http fn throws
        ["process"] = new() { "list", "find", "exists", "dirStack", "dirStackDepth", "lastExitCode", "historyList", "historyClear" },
    };

    public static IEnumerable<object[]> Wave1Namespaces => NoThrowAllowList.Keys.Select(n => new object[] { n });

    [Theory]
    [MemberData(nameof(Wave1Namespaces))]
    public void Wave1_EveryFunctionHasThrowsOrIsAllowlisted(string namespaceName)
    {
        var ns = StdlibDefinitions.Namespaces.FirstOrDefault(n => n.Name == namespaceName);
        Assert.NotNull(ns);

        var allowed = NoThrowAllowList[namespaceName];
        var gaps = new List<string>();
        foreach (var fn in ns.Functions)
        {
            var hasThrows = fn.Throws is { Length: > 0 };
            var isAllowed = allowed.Contains(fn.Name);
            if (!hasThrows && !isAllowed)
                gaps.Add(fn.Name);
        }

        Assert.True(gaps.Count == 0,
            $"{namespaceName}: {gaps.Count} function(s) lack throws metadata and are not in the no-throw allow-list: {string.Join(", ", gaps)}. " +
            $"Either add <exception> tags or update NoThrowAllowList in this test.");
    }

    [Fact]
    public void Wave1_AllFunctionsTagged_CoverageCheckPasses()
    {
        // Aggregate sanity check.
        var totals = new List<(string ns, int total, int tagged, int allowlisted)>();
        foreach (var (ns, allowed) in NoThrowAllowList)
        {
            var nsModel = StdlibDefinitions.Namespaces.First(n => n.Name == ns);
            int total = nsModel.Functions.Count;
            int tagged = nsModel.Functions.Count(f => f.Throws is { Length: > 0 });
            int allow = nsModel.Functions.Count(f => allowed.Contains(f.Name) && (f.Throws is null or { Length: 0 }));
            totals.Add((ns, total, tagged, allow));
            Assert.Equal(total, tagged + allow);
        }
    }

    [Fact]
    public void Wave1_TaggedThrows_ReferenceKnownErrorTypes()
    {
        var known = new HashSet<string>(Stash.Runtime.Errors.BuiltInErrorRegistry.Metadata.Keys);

        foreach (var nsName in NoThrowAllowList.Keys)
        {
            var ns = StdlibDefinitions.Namespaces.First(n => n.Name == nsName);
            foreach (var fn in ns.Functions)
            {
                if (fn.Throws is null) continue;
                foreach (var t in fn.Throws)
                {
                    Assert.True(known.Contains(t.ErrorType),
                        $"{nsName}.{fn.Name} throws unknown error type '{t.ErrorType}'. Must be a built-in error type in BuiltInErrorRegistry.");
                }
            }
        }
    }
}
