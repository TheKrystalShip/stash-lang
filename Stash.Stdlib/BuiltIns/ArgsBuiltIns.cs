namespace Stash.Stdlib.BuiltIns;

using System;
using System.Collections.Generic;
using Stash.Common;
using Stash.Runtime;
using Stash.Stdlib.Registration;
using static Stash.Stdlib.Registration.P;

public static class ArgsBuiltIns
{
    public static NamespaceDefinition Define()
    {
        var ns = new NamespaceBuilder("args");
        ns.RequiresCapability(StashCapabilities.Process);

        ns.Function("list", [], static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
            {
                var result = new List<StashValue>();
                foreach (var s in ctx.ScriptArgs ?? Array.Empty<string>())
                {
                    result.Add(StashValue.FromObj(s));
                }

                return StashValue.FromObj(result);
            },
            returnType: "array",
            documentation: "Returns the command-line arguments as an array of strings.\n@return An array of argument strings passed to the script"
        );

        ns.Function("count", [], static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
            {
                return StashValue.FromInt((long)(ctx.ScriptArgs?.Length ?? 0));
            },
            returnType: "int",
            documentation: "Returns the number of command-line arguments.\n@return The count of arguments passed to the script"
        );

        ns.Function("parse", [Param("spec", "dict")], static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
            {
                return new ArgumentParser(ctx.ScriptArgs ?? Array.Empty<string>()).Parse(args[0].ToObject()) is { } result
                    ? StashValue.FromObj(result)
                    : StashValue.Null;
            },
            returnType: "dict",
            documentation: "Parses command-line arguments according to the given spec.\n@param spec A dict describing the expected arguments (flags, options, positional)\n@return A dict of parsed argument values"
        );

        ns.Function("build", [Param("spec", "dict"), Param("values", "dict")], static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
            {
                return StashValue.FromObj(ArgumentBuilder.Build(args[0].ToObject(), args[1].ToObject()));
            },
            returnType: "array",
            documentation: "Builds an array of CLI argument strings from a spec and values dict.\n@param spec The argument specification dict (same format as args.parse).\n@param values The values dict to serialize into CLI arguments.\n@return An array of argument strings."
        );

        return ns.Build();
    }
}
