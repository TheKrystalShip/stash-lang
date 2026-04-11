namespace Stash.Stdlib.BuiltIns;

using System;
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

        ns.Function("println", [Param("value")], static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
        {
            if (args.Length > 1)
            {
                throw new RuntimeError("'io.println' expects 0 or 1 arguments.");
            }

            string text = args.Length == 1 ? RuntimeValues.Stringify(args[0].ToObject()) : "";
            ctx.Output.WriteLine(text);
            ctx.NotifyOutput("stdout", text + "\n");
            return StashValue.Null;
        }, isVariadic: true);

        ns.Function("print", [Param("value")], static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
        {
            string text = RuntimeValues.Stringify(args[0].ToObject());
            ctx.Output.Write(text);
            ctx.NotifyOutput("stdout", text);
            return StashValue.Null;
        });

        ns.Function("eprintln", [Param("value")], static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
        {
            string text = RuntimeValues.Stringify(args[0].ToObject());
            ctx.ErrorOutput.WriteLine(text);
            ctx.NotifyOutput("stderr", text + "\n");
            return StashValue.Null;
        });

        ns.Function("eprint", [Param("value")], static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
        {
            string text = RuntimeValues.Stringify(args[0].ToObject());
            ctx.ErrorOutput.Write(text);
            ctx.NotifyOutput("stderr", text);
            return StashValue.Null;
        });

        ns.Function("readLine", [Param("prompt", "string")], static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
        {
            if (args.Length > 1)
            {
                throw new RuntimeError("'io.readLine' expects 0 or 1 arguments.");
            }

            if (args.Length == 1)
            {
                string prompt = RuntimeValues.Stringify(args[0].ToObject());
                ctx.Output.Write(prompt);
            }
            var result = ctx.Input.ReadLine();
            return result is null ? StashValue.Null : StashValue.FromObj(result);
        }, returnType: "string", isVariadic: true);

        ns.Function("confirm", [Param("prompt", "string"), Param("default", "bool")], static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
        {
            if (args.Length < 1 || args.Length > 2)
                throw new RuntimeError("'io.confirm' requires 1 or 2 arguments.");

            string prompt = RuntimeValues.Stringify(args[0].ToObject());

            bool? defaultValue = null;
            if (args.Length == 2)
                defaultValue = SvArgs.Bool(args, 1, "io.confirm");

            string hint = defaultValue switch
            {
                true  => "[Y/n]",
                false => "[y/N]",
                null  => "[y/N]"
            };

            ctx.Output.Write(prompt + " " + hint + " ");
            ctx.Output.Flush();
            string? response = ctx.Input.ReadLine();
            if (response == null)
                return StashValue.FromBool(defaultValue ?? false);

            response = response.Trim().ToLowerInvariant();
            if (response == "")
                return StashValue.FromBool(defaultValue ?? false);

            return StashValue.FromBool(response == "y" || response == "yes");
        },
            returnType: "bool",
            isVariadic: true,
            documentation: "Prompts the user for a yes/no confirmation.\n@param prompt The prompt text to display\n@param default Optional default value: true shows [Y/n] (Enter = yes), false shows [y/N] (Enter = no)\n@return true if the user answered yes, false otherwise");

        return ns.Build();
    }
}
