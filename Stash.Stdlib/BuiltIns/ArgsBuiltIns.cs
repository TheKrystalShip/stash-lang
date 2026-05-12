namespace Stash.Stdlib.BuiltIns;

using System;
using System.Collections.Generic;
using Stash.Common;
using Stash.Runtime;
using Stash.Stdlib.Abstractions;

/// <summary>
/// Registers the <c>args</c> namespace built-in functions for command-line argument parsing.
/// </summary>
[StashNamespace(Capability = StashCapabilities.Process)]
public static partial class ArgsBuiltIns
{
    /// <summary>Returns the command-line arguments as an array of strings.</summary>
    /// <returns>An array of argument strings passed to the script</returns>
    [StashFn(ReturnType = "array")]
    public static StashValue List(IInterpreterContext ctx)
    {
        var result = new List<StashValue>();
        foreach (var s in ctx.ScriptArgs ?? Array.Empty<string>())
        {
            result.Add(StashValue.FromObj(s));
        }

        return StashValue.FromObj(result);
    }

    /// <summary>Returns the number of command-line arguments.</summary>
    /// <returns>The count of arguments passed to the script</returns>
    [StashFn(ReturnType = "int")]
    public static long Count(IInterpreterContext ctx)
    {
        return (long)(ctx.ScriptArgs?.Length ?? 0);
    }

    /// <summary>Parses command-line arguments according to the given spec.</summary>
    /// <param name="spec">A dict describing the expected arguments (flags, options, positional)</param>
    /// <exception cref="StashErrorTypes.ValueError">if a required option or positional argument is missing, or an unknown argument is encountered</exception>
    /// <exception cref="StashErrorTypes.ParseError">if an option value cannot be parsed as the declared type (int, float, or bool)</exception>
    /// <exception cref="StashErrorTypes.TypeError">if spec is not a dict, or a declared argument type is unrecognised</exception>
    /// <returns>A dict of parsed argument values</returns>
    [StashFn(ReturnType = "dict")]
    public static StashValue Parse(IInterpreterContext ctx, [StashParam(Type = "dict")] StashValue spec)
    {
        return new ArgumentParser(ctx.ScriptArgs ?? Array.Empty<string>()).Parse(spec.ToObject()) is { } result
            ? StashValue.FromObj(result)
            : StashValue.Null;
    }

    /// <summary>Builds an array of CLI argument strings from a spec and values dict.</summary>
    /// <param name="spec">The argument specification dict (same format as args.parse).</param>
    /// <param name="values">The values dict to serialize into CLI arguments.</param>
    /// <exception cref="StashErrorTypes.TypeError">if spec or values is not a dict, or if a value's type does not match the declared option type</exception>
    /// <returns>An array of argument strings.</returns>
    [StashFn(ReturnType = "array")]
    public static StashValue Build([StashParam(Type = "dict")] StashValue spec, [StashParam(Type = "dict")] StashValue values)
    {
        return StashValue.FromObj(ArgumentBuilder.Build(spec.ToObject(), values.ToObject()));
    }
}
