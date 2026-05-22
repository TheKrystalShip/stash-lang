namespace Stash.Tests.Interpreting;

public class ArgsNamespaceTests : StashTestBase
{
    private const string SkipReason = "args namespace removed in cli-arg-parsing; migrated by follow-up spec";

    // =========================================================================
    // args.list()
    // =========================================================================

    [Fact(Skip = SkipReason)]
    public void List_WithNoArgs_ReturnsEmptyArray()
    {
        var source = """
            let result = len(args.list());
            """;
        Assert.Equal(0L, RunWithArgs(source, []));
    }

    [Fact(Skip = SkipReason)]
    public void List_WithMultipleArgs_ReturnsAllArgs()
    {
        var source = """
            let result = len(args.list());
            """;
        Assert.Equal(2L, RunWithArgs(source, ["hello", "world"]));
    }

    [Fact(Skip = SkipReason)]
    public void List_ReturnsCorrectValues()
    {
        var source = """
            let a = args.list();
            let result = a[0];
            """;
        Assert.Equal("hello", RunWithArgs(source, ["hello", "world"]));
    }

    [Fact(Skip = SkipReason)]
    public void List_ReturnsSecondArg()
    {
        var source = """
            let a = args.list();
            let result = a[1];
            """;
        Assert.Equal("world", RunWithArgs(source, ["hello", "world"]));
    }

    [Fact(Skip = SkipReason)]
    public void List_ReturnsCopy_MutationDoesNotAffectOriginal()
    {
        var source = """
            let a = args.list();
            arr.push(a, "extra");
            let b = args.list();
            let result = len(b);
            """;
        Assert.Equal(2L, RunWithArgs(source, ["hello", "world"]));
    }

    // =========================================================================
    // args.count()
    // =========================================================================

    [Fact(Skip = SkipReason)]
    public void Count_WithNoArgs_ReturnsZero()
    {
        var source = """
            let result = args.count();
            """;
        Assert.Equal(0L, RunWithArgs(source, []));
    }

    [Fact(Skip = SkipReason)]
    public void Count_WithArgs_ReturnsCorrectCount()
    {
        var source = """
            let result = args.count();
            """;
        Assert.Equal(3L, RunWithArgs(source, ["a", "b", "c"]));
    }

    // =========================================================================
    // args.parse()
    // =========================================================================

    [Fact(Skip = SkipReason)]
    public void Parse_BasicFlag_SetsTrue()
    {
        var source = """
            let parsed = args.parse({
                flags: { verbose: { description: "Verbose mode" } }
            });
            let result = parsed.verbose;
            """;
        Assert.Equal(true, RunWithArgs(source, ["--verbose"]));
    }

    [Fact(Skip = SkipReason)]
    public void Parse_BasicOption_SetsValue()
    {
        var source = """
            let parsed = args.parse({
                options: { port: { type: "int", description: "Port" } }
            });
            let result = parsed.port;
            """;
        Assert.Equal(8080L, RunWithArgs(source, ["--port", "8080"]));
    }

    // =========================================================================
    // Namespace accessibility
    // =========================================================================

    [Fact(Skip = SkipReason)]
    public void Namespace_IsAccessible()
    {
        var source = """
            let result = typeof(args);
            """;
        Assert.Equal("namespace", RunWithArgs(source, []));
    }
}
