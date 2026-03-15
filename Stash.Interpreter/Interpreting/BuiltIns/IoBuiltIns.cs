namespace Stash.Interpreting.BuiltIns;

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

        globals.Define("io", io);
    }
}
