namespace Stash.Tests.Stdlib;

using Stash.Runtime.Errors;
using Stash.Tests.Interpreting;
using Xunit;

/// <summary>
/// Negative-space tests proving that the args namespace was removed.
/// Each test asserts that calling args.list(), args.count(), args.parse(), or args.build()
/// raises a RuntimeError (the "Undefined namespace" or "Unknown function" runtime error).
/// </summary>
public class ArgsRemovalTests : StashTestBase
{
    [Fact]
    public void ArgsList_RaisesRuntimeError()
    {
        RunExpectingError("""
            args.list();
            let result = 0;
        """);
    }

    [Fact]
    public void ArgsCount_RaisesRuntimeError()
    {
        RunExpectingError("""
            args.count();
            let result = 0;
        """);
    }

    [Fact]
    public void ArgsParse_RaisesRuntimeError()
    {
        RunExpectingError("""
            args.parse({});
            let result = 0;
        """);
    }

    [Fact]
    public void ArgsBuild_RaisesRuntimeError()
    {
        RunExpectingError("""
            args.build({}, {});
            let result = 0;
        """);
    }
}
