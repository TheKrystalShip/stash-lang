namespace Stash.Runtime.Errors;

using System.Collections.Generic;
using Stash.Common;
using Stash.Runtime;

[StashError(Properties = new[] { "exitCode", "stderr", "stdout", "command" })]
public sealed class CommandError : RuntimeError
{
    public long ExitCode { get; }
    public string? Stderr { get; }
    public string? Stdout { get; }
    public string? Command { get; }

    public CommandError(
        string message,
        long exitCode,
        string? stderr = null,
        string? stdout = null,
        string? command = null,
        SourceSpan? span = null)
        : base(message, span, StashErrorTypes.CommandError)
    {
        ExitCode = exitCode;
        Stderr = stderr;
        Stdout = stdout;
        Command = command;
    }

    protected internal override Dictionary<string, object?>? GetProperties() => new()
    {
        ["exitCode"] = ExitCode,
        ["stderr"] = Stderr,
        ["stdout"] = Stdout,
        ["command"] = Command,
    };
}
