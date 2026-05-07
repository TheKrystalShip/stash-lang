namespace Stash.Stdlib.BuiltIns;

using System;
using Stash.Runtime;
using Stash.Runtime.Types;
using Stash.Stdlib.Abstractions;

/// <summary>
/// Registers the <c>signal</c> namespace built-in functions for POSIX signal handling.
/// </summary>
[StashNamespace]
public static partial class SignalBuiltIns
{
    /// <summary>Registers a callback function to be invoked when the specified signal is received.
    /// Replaces any existing handler for that signal.</summary>
    /// <param name="signal">A Signal enum value (e.g., Signal.Term)</param>
    /// <param name="handler">A function to invoke when the signal is received</param>
    /// <returns>null</returns>
    [StashFn(Raw = true, ReturnType = "null")]
    private static StashValue On(IInterpreterContext ctx, ReadOnlySpan<StashValue> args)
    {
        return SignalImpl.OnSignal(ctx, args, "signal.on");
    }

    /// <summary>Removes a previously registered signal handler.
    /// If no handler was registered for the signal, this is a no-op.</summary>
    /// <param name="signal">A Signal enum value (e.g., Signal.Term)</param>
    /// <returns>null</returns>
    [StashFn(Raw = true, ReturnType = "null")]
    private static StashValue Off(IInterpreterContext ctx, ReadOnlySpan<StashValue> args)
    {
        return SignalImpl.OffSignal(ctx, args, "signal.off");
    }
}

