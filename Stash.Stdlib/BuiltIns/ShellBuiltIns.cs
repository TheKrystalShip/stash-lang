namespace Stash.Stdlib.BuiltIns;

using System;
using Stash.Runtime;
using Stash.Stdlib.Registration;
using static Stash.Stdlib.Registration.P;

/// <summary>
/// Registers the <c>shell</c> namespace built-in functions for interactive shell / REPL state.
/// </summary>
/// <remarks>
/// Only meaningful inside the interactive REPL or shell mode. Gated on
/// <see cref="StashCapabilities.Shell"/>; embedded hosts (Playground / WASM) leave it
/// disabled by default.
/// </remarks>
public static class ShellBuiltIns
{
    public static NamespaceDefinition Define()
    {
        var ns = new NamespaceBuilder("shell");
        ns.RequiresCapability(StashCapabilities.Shell);

        ns.Function("lastExitCode", [], static (IInterpreterContext ctx, ReadOnlySpan<StashValue> _args) =>
        {
            return StashValue.FromInt((long)ctx.GetLastExitCode());
        },
            returnType: "int",
            documentation: "Returns the exit code of the most recently executed bare command pipeline. Defaults to 0 until any command has run.\n@return The exit code as an integer");

        return ns.Build();
    }
}
