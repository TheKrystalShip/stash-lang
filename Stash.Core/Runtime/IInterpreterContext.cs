namespace Stash.Runtime;

using System.Threading;

/// <summary>
/// Full interpreter context combining all sub-interfaces.
/// The concrete Interpreter class implements this interface.
/// Individual built-in namespaces depend on narrower sub-interfaces:
/// <list type="bullet">
///   <item><see cref="IExecutionContext"/> — core state, I/O, cancellation (most builtins)</item>
///   <item><see cref="IProcessContext"/> — process tracking (ProcessBuiltIns)</item>
///   <item><see cref="ITestContext"/> — test framework hooks (TestBuiltIns, AssertBuiltins)</item>
///   <item><see cref="ITemplateContext"/> — template rendering (TplBuiltIns)</item>
///   <item><see cref="IFileWatchContext"/> — file watcher tracking (FsBuiltIns)</item>
/// </list>
/// </summary>
public interface IInterpreterContext : IExecutionContext, IProcessContext, ITestContext, ITemplateContext, IFileWatchContext
{
    // --- Parallel execution ---
    IInterpreterContext Fork(CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a thread-safe child context with an environment snapshot for parallel execution.
    /// The default falls back to Fork(); Interpreter overrides this with a proper snapshot.
    /// </summary>
    IInterpreterContext ForkParallel(CancellationToken cancellationToken = default) => Fork(cancellationToken);
}
