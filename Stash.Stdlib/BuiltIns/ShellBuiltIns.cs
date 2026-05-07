namespace Stash.Stdlib.BuiltIns;

using System;
using Stash.Runtime;
using Stash.Stdlib.Abstractions;

/// <summary>
/// Registers the <c>shell</c> namespace built-in functions for interactive shell / REPL state.
/// </summary>
/// <remarks>
/// Only meaningful inside the interactive REPL or shell mode. Gated on
/// <see cref="StashCapabilities.Shell"/>; embedded hosts (Playground / WASM) leave it
/// disabled by default.
/// </remarks>
[StashNamespace(Capability = StashCapabilities.Shell)]
public static partial class ShellBuiltIns
{
    /// <summary>Returns the exit code of the most recently executed bare command pipeline. Defaults to 0 until any command has run.</summary>
    /// <returns>The exit code as an integer</returns>
    [StashFn(Raw = true, ReturnType = "int")]
    private static StashValue LastExitCode(IInterpreterContext ctx, ReadOnlySpan<StashValue> _args)
    {
        return StashValue.FromInt((long)ctx.GetLastExitCode());
    }
}
