namespace Stash.Runtime;

using System;
using System.IO;
using System.Threading;

/// <summary>
/// Minimal context for built-in function implementations.
/// External stdlib authors should program against this interface
/// rather than the full IInterpreterContext.
/// </summary>
public interface IBuiltInContext
{
    TextWriter Output { get; }
    TextWriter ErrorOutput { get; }
    TextReader Input { get; }
    CancellationToken CancellationToken { get; }

    /// <summary>
    /// Invoke a user-provided callback (e.g. a comparator, predicate, or mapper).
    /// </summary>
    StashValue InvokeCallbackDirect(IStashCallable callable, ReadOnlySpan<StashValue> args);

    /// <summary>
    /// Resolves a CLR object to its VM type name using the registered type registry.
    /// Returns "unknown" if the type is not registered.
    /// </summary>
    string ResolveRegisteredTypeName(object? value) => "unknown";
}
