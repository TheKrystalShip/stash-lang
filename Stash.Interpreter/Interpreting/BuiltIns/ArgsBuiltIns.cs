namespace Stash.Interpreting.BuiltIns;

using System.Collections.Generic;
using Stash.Interpreting.Types;

public static class ArgsBuiltIns
{
    public static void Register(Stash.Interpreting.Environment globals)
    {
        var args = new StashNamespace("args");

        args.Define("list", new BuiltInFunction("args.list", 0, (interp, args) =>
        {
            var result = new List<object?>();
            foreach (var s in interp.ScriptArgs)
            {
                result.Add((object?)s);
            }

            return result;
        }));

        args.Define("count", new BuiltInFunction("args.count", 0, (interp, args) =>
        {
            return (long)interp.ScriptArgs.Length;
        }));

        args.Define("parse", new BuiltInFunction("args.parse", 1, (interp, args) =>
        {
            return new ArgumentParser(interp.ScriptArgs).Parse(args[0]);
        }));

        globals.Define("args", args);
    }
}
