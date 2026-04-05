namespace Stash.Stdlib.BuiltIns;

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

        ns.Function("list", [], (ctx, args) =>
            {
                var result = new List<object?>();
                foreach (var s in ctx.ScriptArgs ?? System.Array.Empty<string>())
                {
                    result.Add((object?)s);
                }

                return result;
            },
            returnType: "array"
        );

        ns.Function("count", [], (ctx, args) =>
            {
                return (long)(ctx.ScriptArgs?.Length ?? 0);
            },
            returnType: "int"
        );

        ns.Function("parse", [Param("spec", "dict")], (ctx, args) =>
            {
                return new ArgumentParser(ctx.ScriptArgs ?? System.Array.Empty<string>()).Parse(args[0]);
            },
            returnType: "dict"
        );

        ns.Function("build", [Param("spec", "dict"), Param("values", "dict")], (ctx, args) =>
            {
                return ArgumentBuilder.Build(args[0], args[1]);
            },
            returnType: "array",
            documentation: "Builds an array of CLI argument strings from a spec and values dict.\n@param spec The argument specification dict (same format as args.parse).\n@param values The values dict to serialize into CLI arguments.\n@return An array of argument strings."
        );

        return ns.Build();
    }
}
