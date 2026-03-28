namespace Stash.Stdlib.BuiltIns;

using Stash.Runtime;
using Stash.Stdlib.Registration;
using static Stash.Stdlib.Registration.P;

/// <summary>
/// Registers the 'io' namespace built-in functions.
/// </summary>
public static class IoBuiltIns
{
    public static NamespaceDefinition Define()
    {
        var ns = new NamespaceBuilder("io");

        ns.Function("println", [Param("value")], (ctx, args) =>
        {
            if (args.Count > 1)
            {
                throw new RuntimeError("'io.println' expects 0 or 1 arguments.");
            }

            string text = args.Count == 1 ? RuntimeValues.Stringify(args[0]) : "";
            ctx.Output.WriteLine(text);
            ctx.NotifyOutput("stdout", text + "\n");
            return null;
        }, isVariadic: true);

        ns.Function("print", [Param("value")], (ctx, args) =>
        {
            string text = RuntimeValues.Stringify(args[0]);
            ctx.Output.Write(text);
            ctx.NotifyOutput("stdout", text);
            return null;
        });

        ns.Function("eprintln", [Param("value")], (ctx, args) =>
        {
            string text = RuntimeValues.Stringify(args[0]);
            ctx.ErrorOutput.WriteLine(text);
            ctx.NotifyOutput("stderr", text + "\n");
            return null;
        });

        ns.Function("eprint", [Param("value")], (ctx, args) =>
        {
            string text = RuntimeValues.Stringify(args[0]);
            ctx.ErrorOutput.Write(text);
            ctx.NotifyOutput("stderr", text);
            return null;
        });

        ns.Function("readLine", [Param("prompt", "string")], (ctx, args) =>
        {
            if (args.Count > 1)
            {
                throw new RuntimeError("'io.readLine' expects 0 or 1 arguments.");
            }

            if (args.Count == 1)
            {
                string prompt = RuntimeValues.Stringify(args[0]);
                ctx.Output.Write(prompt);
            }
            return ctx.Input.ReadLine();
        }, returnType: "string", isVariadic: true);

        ns.Function("confirm", [Param("prompt", "string")], (ctx, args) =>
        {
            string prompt = RuntimeValues.Stringify(args[0]);
            ctx.Output.Write(prompt + " [y/N] ");
            ctx.Output.Flush();
            string? response = ctx.Input.ReadLine();
            if (response == null)
            {
                return false;
            }

            response = response.Trim().ToLowerInvariant();
            return response == "y" || response == "yes";
        }, returnType: "bool");

        return ns.Build();
    }
}
