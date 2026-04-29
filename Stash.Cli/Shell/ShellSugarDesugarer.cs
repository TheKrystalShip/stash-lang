using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using Stash.Runtime;

namespace Stash.Cli.Shell;

/// <summary>
/// Implements §11.2 shell built-in desugaring: converts cd / pwd / exit / quit command lines
/// into equivalent Stash source strings for evaluation through the VM.
///
/// Only single-stage, redirect-free lines are desugared. Piped/redirected occurrences of these
/// names (e.g., <c>pwd | grep foo</c>) are NOT desugared — they fall through to normal process
/// execution. This is a deliberate simplification; §11.2 describes full piped desugaring as a
/// future TODO.
/// </summary>
internal static class ShellSugarDesugarer
{
    private static readonly HashSet<string> _sugarNames =
        new(System.StringComparer.Ordinal) { "cd", "pwd", "exit", "quit" };

    /// <summary>Returns <c>true</c> when <paramref name="program"/> is one of the four sugar names.</summary>
    public static bool IsSugarName(string program) => _sugarNames.Contains(program);

    /// <summary>
    /// If <paramref name="line"/> is a single-stage, redirect-free line whose program is a
    /// sugar name, returns a Stash source snippet to evaluate via the VM. Returns <c>null</c>
    /// when desugaring does not apply (piped / redirected cases).
    /// </summary>
    /// <exception cref="RuntimeError">
    /// Thrown with <see cref="StashErrorTypes.CommandError"/> on arity or numeric-argument
    /// violations, matching the exact wording required by §11.2.
    /// </exception>
    public static string? TryDesugar(ShellCommandLine line, IReadOnlyList<string> expandedArgs)
    {
        // TODO §11.2 (piped sugar): desugaring `pwd | grep foo` requires capturing the
        // desugared call's stdout and feeding it into the subsequent pipeline stage —
        // significant complexity. For now only desugar at the top level of a single-stage,
        // no-redirect line.
        if (line.Stages.Count != 1 || line.Redirects.Count != 0)
            return null;

        string program = line.Stages[0].Program;

        return program switch
        {
            "cd"             => DesugarCd(expandedArgs),
            "pwd"            => DesugarPwd(expandedArgs),
            "exit" or "quit" => DesugarExit(program, expandedArgs),
            _                => null,
        };
    }

    // ── Per-built-in desugaring helpers ──────────────────────────────────────

    private static string DesugarCd(IReadOnlyList<string> args)
    {
        if (args.Count > 1)
            throw new RuntimeError("cd: too many arguments", null, StashErrorTypes.CommandError);

        if (args.Count == 0)
        {
            // 0 args → go home.  Use the platform-appropriate home environment variable.
            string homeVar = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? "USERPROFILE"
                : "HOME";
            return $"process.chdir(env.get(\"{homeVar}\"));";
        }

        string arg = args[0];

        // `cd -` (or `cd "-"`, `cd \-` — all expand to the literal string "-") → pop dir.
        if (arg == "-")
            return "process.popDir(); io.println(env.cwd());";

        return $"process.chdir(\"{EscapeForStashString(arg)}\");";
    }

    private static string DesugarPwd(IReadOnlyList<string> args)
    {
        if (args.Count > 0)
            throw new RuntimeError("pwd: too many arguments", null, StashErrorTypes.CommandError);

        return "io.println(env.cwd());";
    }

    private static string DesugarExit(string program, IReadOnlyList<string> args)
    {
        if (args.Count > 1)
            throw new RuntimeError($"{program}: too many arguments", null, StashErrorTypes.CommandError);

        if (args.Count == 0)
            return "process.exit(0);";

        string arg = args[0];

        // Validate numeric argument in C# to produce the right error immediately, then
        // embed the parsed integer as a literal so `process.exit` receives a plain int.
        if (!long.TryParse(arg, out long code))
            throw new RuntimeError($"{program}: numeric argument required", null, StashErrorTypes.CommandError);

        return $"process.exit({code});";
    }

    // ── String escaping ───────────────────────────────────────────────────────

    /// <summary>
    /// Escapes a raw shell argument for embedding inside a Stash double-quoted string literal.
    /// Escapes: <c>\</c> → <c>\\</c>, <c>"</c> → <c>\"</c>, and the common C escape sequences.
    /// </summary>
    internal static string EscapeForStashString(string arg)
    {
        var sb = new StringBuilder(arg.Length + 4);
        foreach (char c in arg)
        {
            switch (c)
            {
                case '\\': sb.Append("\\\\"); break;
                case '"':  sb.Append("\\\""); break;
                case '\n': sb.Append("\\n");  break;
                case '\r': sb.Append("\\r");  break;
                case '\t': sb.Append("\\t");  break;
                default:   sb.Append(c);      break;
            }
        }
        return sb.ToString();
    }
}
