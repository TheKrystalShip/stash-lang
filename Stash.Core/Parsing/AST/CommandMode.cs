namespace Stash.Parsing.AST;

/// <summary>
/// Specifies how a command literal handles its standard streams and result.
/// </summary>
public enum CommandMode
{
    /// <summary>Default <c>$(cmd)</c> — captures stdout/stderr into a CommandResult.</summary>
    Capture,

    /// <summary>Streaming <c>$&lt;(cmd)</c> — returns a StreamingProcess handle for line-by-line iteration. (Phase B implements runtime behavior.)</summary>
    Stream,

    /// <summary>Passthrough <c>$&gt;(cmd)</c> — inherits stdin/stdout/stderr from the parent.</summary>
    Passthrough,
}
