namespace Stash.Stdlib.BuiltIns;

using System.Collections.Generic;
using Stash.Interpreting;
using Stash.Runtime;
using Stash.Runtime.Types;
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
        }, returnType: "array");

        ns.Function("count", [], (ctx, args) =>
        {
            return (long)(ctx.ScriptArgs?.Length ?? 0);
        }, returnType: "int");

        ns.Function("parse", [Param("spec", "dict")], (ctx, args) =>
        {
            return new ArgumentParser(ctx.ScriptArgs ?? System.Array.Empty<string>()).Parse(args[0]);
        }, returnType: "dict");

        return ns.Build();
    }
}
