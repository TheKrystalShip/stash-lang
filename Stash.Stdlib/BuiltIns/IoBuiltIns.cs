namespace Stash.Stdlib.BuiltIns;

using System;
using System.Text;
using Stash.Runtime;
using Stash.Runtime.Types;
using Stash.Stdlib.Abstractions;
using Stash.Runtime.Errors;

/// <summary>
/// Registers the 'io' namespace built-in functions.
/// </summary>
[StashNamespace]
public static partial class IoBuiltIns
{
    /// <summary>Prints a value followed by a newline to standard output.</summary>
    /// <param name="rest">The optional value to print</param>
    [StashFn(ReturnType = "null")]
    private static void Println(IInterpreterContext ctx, params StashValue[] rest)
    {
        if (rest.Length > 1)
            throw new RuntimeError("'io.println' expects 0 or 1 arguments.");
        string text = rest.Length == 1 ? RuntimeValues.Stringify(rest[0].ToObject()) : "";
        ctx.Output.WriteLine(text);
        ctx.NotifyOutput("stdout", text + "\n");
    }

    /// <summary>Prints a value to standard output without a trailing newline.</summary>
    /// <param name="value">The value to print</param>
    [StashFn(ReturnType = "null")]
    private static void Print(IInterpreterContext ctx, StashValue value)
    {
        string text = RuntimeValues.Stringify(value.ToObject());
        ctx.Output.Write(text);
        ctx.NotifyOutput("stdout", text);
    }

    /// <summary>Prints a value followed by a newline to standard error.</summary>
    /// <param name="value">The value to print</param>
    [StashFn(ReturnType = "null")]
    private static void Eprintln(IInterpreterContext ctx, StashValue value)
    {
        string text = RuntimeValues.Stringify(value.ToObject());
        ctx.ErrorOutput.WriteLine(text);
        ctx.NotifyOutput("stderr", text + "\n");
    }

    /// <summary>Prints a value to standard error without a trailing newline.</summary>
    /// <param name="value">The value to print</param>
    [StashFn(ReturnType = "null")]
    private static void Eprint(IInterpreterContext ctx, StashValue value)
    {
        string text = RuntimeValues.Stringify(value.ToObject());
        ctx.ErrorOutput.Write(text);
        ctx.NotifyOutput("stderr", text);
    }

    /// <summary>Displays a prompt and reads a line of input from the user.</summary>
    /// <param name="rest">Optional prompt text to display before reading input</param>
    /// <returns>The line of text entered by the user, or null on end of input</returns>
    [StashFn(ReturnType = "string")]
    private static StashValue ReadLine(IInterpreterContext ctx, params StashValue[] rest)
    {
        if (rest.Length > 1)
            throw new RuntimeError("'io.readLine' expects 0 or 1 arguments.");
        if (rest.Length == 1)
        {
            string prompt = RuntimeValues.Stringify(rest[0].ToObject());
            ctx.Output.Write(prompt);
        }
        var result = ctx.Input.ReadLine();
        return result is null ? StashValue.Null : StashValue.FromObj(result);
    }

    /// <summary>Prompts the user for a yes/no confirmation.</summary>
    /// <param name="prompt">The prompt text to display</param>
    /// <param name="rest">Optional default value: true shows [Y/n] (Enter = yes), false shows [y/N] (Enter = no)</param>
    /// <exception cref="TypeError">if the optional default argument is not a bool</exception>
    /// <returns>true if the user answered yes, false otherwise</returns>
    [StashFn(ReturnType = "bool")]
    private static bool Confirm(IInterpreterContext ctx, StashValue prompt, params StashValue[] rest)
    {
        if (rest.Length > 1)
            throw new RuntimeError("'io.confirm' requires 1 or 2 arguments.");
        string promptText = RuntimeValues.Stringify(prompt.ToObject());
        bool? defaultValue = null;
        if (rest.Length == 1)
            defaultValue = SvArgs.Bool(new[] { rest[0] }, 0, "io.confirm");
        string hint = defaultValue switch
        {
            true  => "[Y/n]",
            false => "[y/N]",
            null  => "[y/N]"
        };
        ctx.Output.Write(promptText + " " + hint + " ");
        ctx.Output.Flush();
        string? response = ctx.Input.ReadLine();
        if (response == null)
            return defaultValue ?? false;
        response = response.Trim().ToLowerInvariant();
        if (response == "")
            return defaultValue ?? false;
        return response == "y" || response == "yes";
    }

    /// <summary>Reads a password from stdin without echoing typed characters. Returns a secret.</summary>
    /// <param name="rest">Optional prompt text to display before reading</param>
    /// <exception cref="IOError">if input is closed or cancelled (Ctrl-C)</exception>
    /// <exception cref="TypeError">if the optional prompt argument is not a string</exception>
    /// <returns>The entered password wrapped in a secret value</returns>
    [StashFn(ReturnType = "secret")]
    private static StashValue ReadPassword(IInterpreterContext ctx, params StashValue[] rest)
    {
        if (rest.Length > 1)
            throw new RuntimeError("'io.readPassword' expects 0 or 1 arguments.");
        if (rest.Length == 1 && !rest[0].IsNull)
        {
            string prompt = SvArgs.String(new[] { rest[0] }, 0, "io.readPassword");
            ctx.Output.Write(prompt);
            ctx.NotifyOutput("stdout", prompt);
        }
        string collected;
        if (Console.IsInputRedirected || !object.ReferenceEquals(ctx.Input, Console.In))
        {
            string? line = ctx.Input.ReadLine();
            if (line is null)
                throw new IOError("io.readPassword: input cancelled.");
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
                    throw new IOError("io.readPassword: input cancelled.");
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
                throw new IOError("io.readPassword: input cancelled.");

            if (key.KeyChar != '\0' && !char.IsControl(key.KeyChar))
                sb.Append(key.KeyChar);
        }

        ctx.Output.WriteLine();
        ctx.NotifyOutput("stdout", "\n");
        return sb.ToString();
    }
}
