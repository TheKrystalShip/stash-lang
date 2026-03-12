namespace Stash.Debugging;

using Stash.Common;
using StashEnv = Stash.Interpreting.Environment;

/// <summary>
/// Represents a single frame in the call stack during debugging.
/// </summary>
public class CallFrame
{
    /// <summary>
    /// Gets the function name for this frame.
    /// </summary>
    public string FunctionName { get; init; } = "<script>";

    /// <summary>
    /// Gets the source location where this function was called.
    /// </summary>
    public SourceSpan CallSite { get; init; } = null!;

    /// <summary>
    /// Gets the local scope for this frame.
    /// </summary>
    public StashEnv LocalScope { get; init; } = null!;
}
