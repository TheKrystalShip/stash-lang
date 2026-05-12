namespace Stash.Tests.Stdlib.SourceGenerator;

using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Stash.Stdlib;
using Xunit;

public class Wave4ThrowsCoverageTests
{
    // Wave 4 namespaces per spec §5 (long tail).
    // `lock` is not registered as a stdlib namespace; omitted from this wave.
    //
    // `test.beforeAll`/`afterAll`/`beforeEach`/`afterEach` currently throw
    // RuntimeError without `errorType:` (pre-existing bug, tracked separately).
    // They are allow-listed here as silent until the throws are typed; once
    // typed they will move to `<exception>`-tagged status.
    //
    // `assert.*` failures throw `AssertionError` (a RuntimeError subclass that
    // is NOT a StashErrorTypes constant). Assertion-only functions are silent;
    // only those that also throw user-catchable typed errors (e.g. TypeError
    // for arg validation) carry `<exception>` tags.
    private static readonly Dictionary<string, HashSet<string>> NoThrowAllowList = new()
    {
        ["sys"] = new() { "cpuCount", "totalMemory", "freeMemory", "uptime", "loadAvg", "pid", "tempDir", "networkInterfaces" },
        ["term"] = new() { "bold", "dim", "underline", "strip", "width", "isInteractive", "zeroWidth", "colorsEnabled", "clear" },
        ["args"] = new() { "list", "count" },
        ["alias"] = new() { "list", "names", "get", "exists", "clear", "__listPretty", "__getPretty" },
        ["config"] = new(),
        ["assert"] = new() { "equal", "notEqual", "true", "false", "null", "notNull", "throws", "fail", "deepEqual" },
        ["test"] = new() { "it", "only", "skip", "describe", "beforeAll", "afterAll", "beforeEach", "afterEach", "captureOutput" },
    };

    public static IEnumerable<object[]> Wave4Namespaces => NoThrowAllowList.Keys.Select(n => new object[] { n });

    [Theory]
    [MemberData(nameof(Wave4Namespaces))]
    public void Wave4_EveryFunctionHasThrowsOrIsAllowlisted(string namespaceName)
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
    public void Wave4_AllFunctionsTagged_CoverageCheckPasses()
    {
        foreach (var (ns, allowed) in NoThrowAllowList)
        {
            var nsModel = StdlibDefinitions.Namespaces.First(n => n.Name == ns);
            int total = nsModel.Functions.Count;
            int tagged = nsModel.Functions.Count(f => f.Throws is { Length: > 0 });
            int allow = nsModel.Functions.Count(f => allowed.Contains(f.Name) && (f.Throws is null or { Length: 0 }));
            Assert.True(total == tagged + allow,
                $"{ns}: total={total} tagged={tagged} allowlisted={allow} (sum={tagged + allow}). Allow-list may contain stale names or new functions are missing throws.");
        }
    }

    [Fact]
    public void Wave4_TaggedThrows_ReferenceKnownErrorTypes()
    {
        var known = new HashSet<string>(typeof(Stash.Runtime.StashErrorTypes)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!));

        foreach (var nsName in NoThrowAllowList.Keys)
        {
            var ns = StdlibDefinitions.Namespaces.First(n => n.Name == nsName);
            foreach (var fn in ns.Functions)
            {
                if (fn.Throws is null) continue;
                foreach (var t in fn.Throws)
                {
                    Assert.True(known.Contains(t.ErrorType),
                        $"{nsName}.{fn.Name} throws unknown error type '{t.ErrorType}'. Must be a constant in StashErrorTypes.");
                }
            }
        }
    }
}
