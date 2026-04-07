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
            returnType: "array"
        );

        ns.Function("count", [], static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
            {
                return StashValue.FromInt((long)(ctx.ScriptArgs?.Length ?? 0));
            },
            returnType: "int"
        );

        ns.Function("parse", [Param("spec", "dict")], static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
            {
                return StashValue.FromObj(new ArgumentParser(ctx.ScriptArgs ?? Array.Empty<string>()).Parse(args[0].ToObject()));
            },
            returnType: "dict"
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
