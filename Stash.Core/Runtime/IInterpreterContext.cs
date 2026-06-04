namespace Stash.Runtime;

using System;
using System.Collections.Generic;
using System.IO;
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
///   <item><see cref="ILoggerContext"/> — log namespace configuration (LogBuiltIns)</item>
/// </list>
/// </summary>
public interface IInterpreterContext : IExecutionContext, IProcessContext, ITestContext, ITemplateContext, IFileWatchContext, ILoggerContext
{
    // --- Per-VM virtual process state ---

    /// <summary>
    /// The per-VM working directory. Initialized once from
    /// <see cref="System.Environment.CurrentDirectory"/> at VM construction and then
    /// purely per-VM — never re-reads the real process cwd.
    /// All stdlib path-resolving sinks must use <see cref="ResolveAgainstCwd"/> instead of
    /// the raw <c>System.Environment.CurrentDirectory</c> / <c>Directory.GetCurrentDirectory()</c>.
    /// </summary>
    string WorkingDirectory { get => System.Environment.CurrentDirectory; set { } }

    /// <summary>
    /// Reads an environment variable from the per-VM overlay.
    /// Overlay semantics: a null/absent entry falls back to the real process env
    /// (<see cref="System.Environment.GetEnvironmentVariable"/>).
    /// An explicitly unset entry (written by <see cref="UnsetEnv"/>) stores null in the
    /// overlay and short-circuits to null without consulting the real env.
    /// </summary>
    string? GetEnv(string name) => System.Environment.GetEnvironmentVariable(name);

    /// <summary>
    /// Writes an environment variable to the per-VM overlay ONLY.
    /// The real <see cref="System.Environment"/> is never mutated.
    /// </summary>
    void SetEnv(string name, string value) { }

    /// <summary>
    /// Marks an environment variable as explicitly unset in the per-VM overlay,
    /// shadowing any value the real process env might have for it.
    /// The real <see cref="System.Environment"/> is never mutated.
    /// </summary>
    void UnsetEnv(string name) { }

    /// <summary>
    /// Returns the merged view of the per-VM overlay over the real process env.
    /// Explicitly-unset keys (written by <see cref="UnsetEnv"/>) are excluded.
    /// </summary>
    Dictionary<string, string> AllEnv()
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (System.Collections.DictionaryEntry entry in System.Environment.GetEnvironmentVariables())
        {
            if (entry.Key is string k && entry.Value is string v)
                result[k] = v;
        }
        return result;
    }

    /// <summary>
    /// Resolves <paramref name="path"/> against <see cref="WorkingDirectory"/>,
    /// producing a normalized absolute path without consulting the real process cwd.
    /// Equivalent to <c>Path.GetFullPath(path, WorkingDirectory)</c>.
    /// </summary>
    string ResolveAgainstCwd(string path) => Path.GetFullPath(path, WorkingDirectory);

    // --- Callback queue drain ---

    /// <summary>
    /// Drains queued background callbacks according to <paramref name="mode"/>.
    /// <list type="bullet">
    ///   <item><see cref="WaitMode.PollMode"/> — pops everything currently queued and returns.</item>
    ///   <item><see cref="WaitMode.UntilMode"/> — parks on the queue signal until the deadline, draining on each wake.</item>
    ///   <item><see cref="WaitMode.ForeverMode"/> — parks and drains until cancellation.</item>
    /// </list>
    /// The default implementation is a no-op (no queue in non-VM contexts).
    /// <c>VMContext</c> overrides this with the real MPSC queue and reentrancy guard.
    /// </summary>
    void DrainCallbacks(WaitMode mode) { }

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
    /// so that the bytecode VM can dispatch correctly.
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
    new StashValue InvokeCallbackDirect(IStashCallable callable, ReadOnlySpan<StashValue> args) =>
        callable.CallDirect(Fork(), args);
}
