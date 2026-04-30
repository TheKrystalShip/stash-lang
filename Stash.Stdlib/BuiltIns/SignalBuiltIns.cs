namespace Stash.Stdlib.BuiltIns;

using System;
using Stash.Runtime;
using Stash.Runtime.Types;
using Stash.Stdlib.Registration;
using static Stash.Stdlib.Registration.P;

/// <summary>
/// Registers the <c>signal</c> namespace built-in functions for POSIX signal handling.
/// </summary>
public static class SignalBuiltIns
{
    public static NamespaceDefinition Define()
    {
        var ns = new NamespaceBuilder("signal");

        // signal.on(signal, handler) — Registers a callback for the given POSIX signal.
        ns.Function("on", [Param("signal", "Signal"), Param("handler", "function")], (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
            SignalImpl.OnSignal(ctx, args, "signal.on"),
            returnType: "null",
            documentation: "Registers a callback function to be invoked when the specified signal is received.\nReplaces any existing handler for that signal.\n\n@param signal A Signal enum value (e.g., Signal.Term)\n@param handler A function to invoke when the signal is received");

        // signal.off(signal) — Removes a previously registered signal handler.
        ns.Function("off", [Param("signal", "Signal")], static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
            SignalImpl.OffSignal(ctx, args, "signal.off"),
            returnType: "null",
            documentation: "Removes a previously registered signal handler.\nIf no handler was registered for the signal, this is a no-op.\n\n@param signal A Signal enum value (e.g., Signal.Term)");

        return ns.Build();
    }
}
