namespace Stash.Interpreting.BuiltIns;

using System;
using System.Collections.Generic;

/// <summary>
/// Registers the 'io' namespace built-in functions.
/// </summary>
public static class IoBuiltIns
{
    public static void Register(Stash.Interpreting.Environment globals)
    {
        // ── io namespace ─────────────────────────────────────────────────
        var io = new StashNamespace("io");

        io.Define("println", new BuiltInFunction("io.println", 1, (_, args) =>
        {
            Console.WriteLine(RuntimeValues.Stringify(args[0]));
            return null;
        }));

        io.Define("print", new BuiltInFunction("io.print", 1, (_, args) =>
        {
            Console.Write(RuntimeValues.Stringify(args[0]));
            return null;
        }));

        globals.Define("io", io);
    }
}
