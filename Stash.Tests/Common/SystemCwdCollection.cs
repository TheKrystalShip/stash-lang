using Xunit;

namespace Stash.Tests.Common;

/// <summary>
/// xUnit collection for tests that mutate <see cref="System.Environment.CurrentDirectory"/>
/// (a process-global). Marking the collection with <c>DisableParallelization = true</c>
/// forces every test that joins it to run serially — both with respect to other members
/// of this collection and to every other collection in the assembly.
///
/// Why this exists: <c>Environment.CurrentDirectory</c> cannot be isolated by xUnit
/// collections alone. If two tests in different (parallelizable) collections concurrently
/// cd into temp dirs and then delete them, any third test that constructs a fresh
/// <c>VirtualMachine</c> can observe a deleted process cwd and throw
/// <c>DirectoryNotFoundException</c> from <c>VMContext..ctor</c>. The failure cascades
/// across hundreds of unrelated tests.
///
/// Apply <c>[Collection("SystemCwdTests")]</c> to every test class that:
///   - assigns to <c>Environment.CurrentDirectory</c> directly, or
///   - calls <c>env.chdir</c> / <c>env.popDir</c> / <c>process.chdir</c> from a Stash
///     script under test, or
///   - uses a helper (e.g., a <c>TempDir</c> RAII type) that does either of the above.
///
/// See <c>.kanban/0-backlog/bugs/Test Suite — Parallel Execution Flake from cwd Mutation.md</c>
/// for the original investigation.
/// </summary>
[CollectionDefinition("SystemCwdTests", DisableParallelization = true)]
public class SystemCwdCollection
{
}
