namespace Stash.Cli.AstGraph.Models;

/// <summary>
/// Holds the result of an AST graph generation run.
/// </summary>
internal sealed class AstResult
{
    /// <summary>Whether the run failed with an error.</summary>
    public bool HasErrors { get; init; }

    /// <summary>The generated DOT graph content, or <c>null</c> on error.</summary>
    public string? Dot { get; init; }

    /// <summary>Error message on failure, or <c>null</c> on success.</summary>
    public string? Error { get; init; }

    /// <summary>Process exit code (0 = success, 1 = parse error, 2 = fatal).</summary>
    public int ExitCode { get; init; }

    /// <summary>Creates a successful result containing the generated DOT graph.</summary>
    public static AstResult Success(string dot) => new() { Dot = dot };

    /// <summary>Creates a failure result from parse/lex errors.</summary>
    public static AstResult ParseError(string error) => new()
    {
        HasErrors = true,
        Error = error,
        ExitCode = 1,
    };

    /// <summary>Creates a failure result from an unexpected error.</summary>
    public static AstResult FatalError(string error) => new()
    {
        HasErrors = true,
        Error = error,
        ExitCode = 2,
    };
}
