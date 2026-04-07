namespace Stash.Runtime;

using System;
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

    /// <summary>
    /// Invokes a callable (e.g. a user lambda) with the given arguments in a child execution context.
    /// Built-in namespaces (e.g. fs.watch) use this instead of <c>callable.Call(Fork(), args)</c>
    /// so that both the tree-walk interpreter and the bytecode VM can dispatch correctly.
    /// The default implementation forks the context and calls <see cref="IStashCallable.Call"/>.
    /// The bytecode VM overrides this to execute <c>VMFunction</c> closures on a child VM instance.
    /// </summary>
    object? InvokeCallback(IStashCallable callable, System.Collections.Generic.List<object?> args)
    {
        StashValue[] svArgs = new StashValue[args.Count];
        for (int i = 0; i < args.Count; i++)
            svArgs[i] = StashValue.FromObject(args[i]);
        return InvokeCallbackDirect(callable, svArgs).ToObject();
    }

    /// <summary>
    /// StashValue-native callback invocation. Eliminates List&lt;object?&gt; allocation
    /// and boxing/unboxing. Built-ins should prefer this over InvokeCallback.
    /// Default implementation bridges to the legacy InvokeCallback path.
    /// </summary>
    StashValue InvokeCallbackDirect(IStashCallable callable, ReadOnlySpan<StashValue> args) =>
        callable.CallDirect(Fork(), args);
}
