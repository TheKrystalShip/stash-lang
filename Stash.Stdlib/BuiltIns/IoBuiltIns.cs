namespace Stash.Stdlib.BuiltIns;

using System;
using System.Text;
using Stash.Runtime;
using Stash.Runtime.Types;
using Stash.Stdlib.Abstractions;

/// <summary>
/// Registers the 'io' namespace built-in functions.
/// </summary>
[StashNamespace]
public static partial class IoBuiltIns
{
    /// <summary>Prints a value followed by a newline to standard output.</summary>
    /// <param name="value">The value to print</param>
    [StashFn(Raw = true, ReturnType = "null")]
    private static StashValue Println(IInterpreterContext ctx, ReadOnlySpan<StashValue> args)
    {
        if (args.Length > 1)
            throw new RuntimeError("'io.println' expects 0 or 1 arguments.");
        string text = args.Length == 1 ? RuntimeValues.Stringify(args[0].ToObject()) : "";
        ctx.Output.WriteLine(text);
        ctx.NotifyOutput("stdout", text + "\n");
        return StashValue.Null;
    }

    /// <summary>Prints a value to standard output without a trailing newline.</summary>
    /// <param name="value">The value to print</param>
    [StashFn(Raw = true, ReturnType = "null")]
    private static StashValue Print(IInterpreterContext ctx, ReadOnlySpan<StashValue> args)
    {
        string text = RuntimeValues.Stringify(args[0].ToObject());
        ctx.Output.Write(text);
        ctx.NotifyOutput("stdout", text);
        return StashValue.Null;
    }

    /// <summary>Prints a value followed by a newline to standard error.</summary>
    /// <param name="value">The value to print</param>
    [StashFn(Raw = true, ReturnType = "null")]
    private static StashValue Eprintln(IInterpreterContext ctx, ReadOnlySpan<StashValue> args)
    {
        string text = RuntimeValues.Stringify(args[0].ToObject());
        ctx.ErrorOutput.WriteLine(text);
        ctx.NotifyOutput("stderr", text + "\n");
        return StashValue.Null;
    }

    /// <summary>Prints a value to standard error without a trailing newline.</summary>
    /// <param name="value">The value to print</param>
    [StashFn(Raw = true, ReturnType = "null")]
    private static StashValue Eprint(IInterpreterContext ctx, ReadOnlySpan<StashValue> args)
    {
        string text = RuntimeValues.Stringify(args[0].ToObject());
        ctx.ErrorOutput.Write(text);
        ctx.NotifyOutput("stderr", text);
        return StashValue.Null;
    }

    /// <summary>Displays a prompt and reads a line of input from the user.</summary>
    /// <param name="prompt">Optional prompt text to display before reading input</param>
    /// <returns>The line of text entered by the user, or null on end of input</returns>
    [StashFn(Raw = true, ReturnType = "string")]
    private static StashValue ReadLine(IInterpreterContext ctx, ReadOnlySpan<StashValue> args)
    {
        if (args.Length > 1)
            throw new RuntimeError("'io.readLine' expects 0 or 1 arguments.");
        if (args.Length == 1)
        {
            string prompt = RuntimeValues.Stringify(args[0].ToObject());
            ctx.Output.Write(prompt);
        }
        var result = ctx.Input.ReadLine();
        return result is null ? StashValue.Null : StashValue.FromObj(result);
    }

    /// <summary>Prompts the user for a yes/no confirmation.</summary>
    /// <param name="prompt">The prompt text to display</param>
    /// <param name="default">Optional default value: true shows [Y/n] (Enter = yes), false shows [y/N] (Enter = no)</param>
    /// <returns>true if the user answered yes, false otherwise</returns>
    [StashFn(Raw = true, ReturnType = "bool")]
    private static StashValue Confirm(IInterpreterContext ctx, ReadOnlySpan<StashValue> args)
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
    }

    /// <summary>Reads a password from stdin without echoing typed characters. Returns a secret.</summary>
    /// <param name="prompt">Optional prompt text to display before reading</param>
    /// <returns>The entered password wrapped in a secret value</returns>
    [StashFn(Raw = true, ReturnType = "secret")]
    private static StashValue ReadPassword(IInterpreterContext ctx, ReadOnlySpan<StashValue> args)
    {
        if (args.Length > 1)
            throw new RuntimeError("'io.readPassword' expects 0 or 1 arguments.");
        if (args.Length == 1 && !args[0].IsNull)
        {
            string prompt = SvArgs.String(args, 0, "io.readPassword");
            ctx.Output.Write(prompt);
            ctx.NotifyOutput("stdout", prompt);
        }
        string collected;
        if (Console.IsInputRedirected || !object.ReferenceEquals(ctx.Input, Console.In))
        {
            string? line = ctx.Input.ReadLine();
            if (line is null)
                throw new RuntimeError("io.readPassword: input cancelled.", errorType: StashErrorTypes.IOError);
            collected = line;
        }
        else
        {
            collected = ReadPasswordInteractive(ctx);
        }
        return StashValue.FromObj(new StashSecret(StashValue.FromObj(collected)));
    }

    private static string ReadPasswordInteractive(IInterpreterContext ctx)
    {
        var sb = new StringBuilder();
        while (true)
        {
            ConsoleKeyInfo key;
            try
            {
                key = Console.ReadKey(intercept: true);
            }
            catch (InvalidOperationException)
            {
                string? line = ctx.Input.ReadLine();
                if (line is null)
                    throw new RuntimeError("io.readPassword: input cancelled.", errorType: StashErrorTypes.IOError);
                return line;
            }

            if (key.Key == ConsoleKey.Enter)
                break;

            if (key.Key == ConsoleKey.Backspace)
            {
                if (sb.Length > 0)
                    sb.Length--;
                continue;
            }

            if (key.Key == ConsoleKey.C && (key.Modifiers & ConsoleModifiers.Control) != 0)
                throw new RuntimeError("io.readPassword: input cancelled.", errorType: StashErrorTypes.IOError);

            if (key.KeyChar != '\0' && !char.IsControl(key.KeyChar))
                sb.Append(key.KeyChar);
        }

        ctx.Output.WriteLine();
        ctx.NotifyOutput("stdout", "\n");
        return sb.ToString();
    }
}
