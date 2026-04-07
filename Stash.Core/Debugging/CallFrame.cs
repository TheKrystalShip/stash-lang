namespace Stash.Debugging;

using System.Threading;
using Stash.Common;

/// <summary>
/// Represents a single frame in the call stack during debugging.
/// Each frame has a unique ID for stable references in DAP stack trace responses.
/// </summary>
public class CallFrame
{
    private static int _nextId;

    /// <summary>
    /// Unique identifier for this frame. Stable for the lifetime of the frame.
    /// Used as DAP frameId in stackTrace responses.
    /// </summary>
    public int Id { get; } = Interlocked.Increment(ref _nextId);

    /// <summary>
    /// Gets the function name for this frame.
    /// </summary>
    public string FunctionName { get; init; } = "<script>";

    /// <summary>
    /// Gets the source location where this function was called.
    /// </summary>
    public SourceSpan? CallSite { get; init; }

    /// <summary>
    /// Gets the local scope for this frame.
    /// </summary>
    public IDebugScope LocalScope { get; init; } = null!;

    /// <summary>
    /// Gets the source location where the function is defined (its declaration site).
    /// Null for the top-level script frame and built-in functions.
    /// Used for DAP "go to definition" from a stack frame.
    /// </summary>
    public SourceSpan? FunctionSpan { get; init; }
}
