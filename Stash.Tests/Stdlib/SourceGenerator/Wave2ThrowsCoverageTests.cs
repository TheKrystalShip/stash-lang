namespace Stash.Tests.Stdlib.SourceGenerator;

using System.Collections.Generic;
using System.Linq;
using Stash.Stdlib;
using Xunit;

public class Wave2ThrowsCoverageTests
{
    // Wave 2 namespaces per spec §5.
    // Each name maps to the set of function names that intentionally throw
    // nothing (so an empty Throws on those is correct, not a coverage gap).
    private static readonly Dictionary<string, HashSet<string>> NoThrowAllowList = new()
    {
        ["str"] = new() { "upper", "lower", "reverse", "chars", "isDigit", "isAlpha", "isAlphaNum", "isUpper", "isLower", "isEmpty", "replaceAll", "capitalize", "title", "lines", "words", "slug" },
        ["arr"] = new() { "contains", "includes", "indexOf", "join", "map", "forEach", "find", "reduce", "any", "every", "flat", "flatMap", "findIndex", "count", "zip", "partition", "typed", "elementType", "groupBy" },
        ["dict"] = new() { "new", "clear", "keys", "values", "size", "pairs", "forEach", "merge", "map", "filter", "pick", "omit", "defaults", "any", "every", "find" },
        ["math"] = new() { "pow", "sqrt", "random", "sin", "cos", "tan", "asin", "acos", "atan", "atan2", "exp", "log10", "log2" },
        ["time"] = new() { "now", "millis", "date", "clock", "iso", "add", "diff", "timezone", "timezones", "seconds", "minutes", "hours", "days", "weeks", "format" },
        ["path"] = new() { "separator" },
        ["re"] = new(),
        ["crypto"] = new() { "md5", "sha1", "sha256", "sha512", "uuid", "md5Bytes", "sha1Bytes", "sha256Bytes", "sha512Bytes" },
        ["encoding"] = new(),
        ["net"] = new() { "interfaces" },
        ["env"] = new() { "set", "has", "all", "withPrefix", "remove", "cwd", "home", "hostname", "user", "os", "arch", "dirStack", "dirStackDepth", "exit" },
    };

    public static IEnumerable<object[]> Wave2Namespaces => NoThrowAllowList.Keys.Select(n => new object[] { n });

    [Theory]
    [MemberData(nameof(Wave2Namespaces))]
    public void Wave2_EveryFunctionHasThrowsOrIsAllowlisted(string namespaceName)
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
    public void Wave2_AllFunctionsTagged_CoverageCheckPasses()
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
    public void Wave2_TaggedThrows_ReferenceKnownErrorTypes()
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
