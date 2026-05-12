namespace Stash.Tests.Stdlib.SourceGenerator;

using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Stash.Stdlib;
using Xunit;

public class Wave3ThrowsCoverageTests
{
    // Wave 3 namespaces per spec §5.
    private static readonly Dictionary<string, HashSet<string>> NoThrowAllowList = new()
    {
        ["buf"] = new() { "toHex", "toBase64", "len", "equals", "slice", "reverse" },
        ["xml"] = new() { "valid" },
        ["ini"] = new() { "stringify" },
        ["yaml"] = new() { "valid" },
        ["csv"] = new(),
        ["archive"] = new(),
        ["tpl"] = new(),
        ["task"] = new() { "run", "all", "resolve", "delay" },
    };

    public static IEnumerable<object[]> Wave3Namespaces => NoThrowAllowList.Keys.Select(n => new object[] { n });

    [Theory]
    [MemberData(nameof(Wave3Namespaces))]
    public void Wave3_EveryFunctionHasThrowsOrIsAllowlisted(string namespaceName)
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
    public void Wave3_AllFunctionsTagged_CoverageCheckPasses()
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
    public void Wave3_TaggedThrows_ReferenceKnownErrorTypes()
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
