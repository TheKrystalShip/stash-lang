namespace Stash.Cli.Modes;

using System.IO;
using Stash.Analysis.Cli;
using Stash.Runtime.Types;
using Stash.Stdlib.BuiltIns;

/// <summary>
/// Implements the <c>stash --help script.stash</c> static discovery mode (P10).
/// </summary>
/// <remarks>
/// <para>
/// This mode is invoked when the Stash CLI receives <c>--help &lt;script.stash&gt;</c> as its
/// first two arguments.  It short-circuits before the normal lex/parse/compile/execute path:
/// no script code is run.
/// </para>
/// <para>
/// The analyser looks for a top-level binding named <c>schema</c> (overridable via a
/// <c>// @cli-schema-binding: &lt;name&gt;</c> comment marker) whose initialiser is a
/// fully-literal <c>cli.schema({...})</c> call.  If found, the rendered help text is printed
/// and the process exits 0.  If not found, the generic fallback message is printed and the
/// process exits 0.
/// </para>
/// </remarks>
public static class StaticHelpMode
{
    /// <summary>
    /// The generic fallback message printed when no statically-discoverable CLI schema is found.
    /// </summary>
    public const string FallbackMessage =
        "usage: stash <script> [args...]\n\n" +
        "No statically discoverable CLI schema; run the script with --help for full usage if it supports it.";

    /// <summary>
    /// Runs the static help mode for <paramref name="scriptPath"/>.
    /// Prints rendered help (or the fallback message) to <paramref name="output"/> and returns
    /// exit code 0 in both cases.
    /// </summary>
    /// <param name="scriptPath">Path to the <c>.stash</c> script file.</param>
    /// <param name="output">The writer to print output to (usually <c>Console.Out</c>).</param>
    /// <returns>Exit code — always 0.</returns>
    public static int Run(string scriptPath, TextWriter output)
    {
        if (!File.Exists(scriptPath))
        {
            output.WriteLine(FallbackMessage);
            return 0;
        }

        string source;
        try
        {
            source = File.ReadAllText(scriptPath);
        }
        catch
        {
            output.WriteLine(FallbackMessage);
            return 0;
        }

        if (LiteralSchemaBuilder.TryBuild(source, scriptPath, out StashInstance? schema) &&
            schema is not null)
        {
            string helpText = CliBuiltIns.RenderHelp(schema, width: 80);
            output.WriteLine(helpText);
        }
        else
        {
            output.WriteLine(FallbackMessage);
        }

        return 0;
    }
}
