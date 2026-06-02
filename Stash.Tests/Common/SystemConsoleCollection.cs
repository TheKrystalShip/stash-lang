using Xunit;

namespace Stash.Tests.Common;

/// <summary>
/// xUnit collection for tests that mutate <see cref="Console.Out"/>, <see cref="Console.Error"/>,
/// or <see cref="Console.In"/> via <c>Console.SetOut</c> / <c>Console.SetError</c> / <c>Console.SetIn</c>.
/// These are process-global handles; concurrent mutation from parallel tests causes flaky captures
/// and "wrong writer" assertions. Serializing the entire collection prevents cross-test interference.
///
/// Apply <c>[Collection("SystemConsoleTests")]</c> to every test class that redirects any of
/// the three standard console streams.
/// </summary>
[CollectionDefinition("SystemConsoleTests", DisableParallelization = true)]
public class SystemConsoleCollection
{
}
