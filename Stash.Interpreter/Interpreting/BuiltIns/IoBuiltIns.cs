namespace Stash.Interpreting.BuiltIns;

using System;
using Stash.Interpreting.Types;

/// <summary>
/// Registers the 'io' namespace built-in functions.
/// </summary>
public static class IoBuiltIns
{
    public static void Register(Stash.Interpreting.Environment globals)
    {
        // ── io namespace ─────────────────────────────────────────────────
        var io = new StashNamespace("io");

        io.Define("println", new BuiltInFunction("io.println", 1, (interp, args) =>
        {
            string text = RuntimeValues.Stringify(args[0]);
            interp.Output.WriteLine(text);
            interp.Debugger?.OnOutput("stdout", text + "\n");
            return null;
        }));

        io.Define("print", new BuiltInFunction("io.print", 1, (interp, args) =>
        {
            string text = RuntimeValues.Stringify(args[0]);
            interp.Output.Write(text);
            interp.Debugger?.OnOutput("stdout", text);
            return null;
        }));

        io.Define("readLine", new BuiltInFunction("io.readLine", -1, (interp, args) =>
        {
            if (args.Count > 1)
            {
                throw new RuntimeError("'io.readLine' expects 0 or 1 arguments.");
            }

            if (args.Count == 1)
            {
                string prompt = RuntimeValues.Stringify(args[0]);
                interp.Output.Write(prompt);
            }
            return interp.Input.ReadLine();
        }));

        globals.Define("io", io);
    }
}
