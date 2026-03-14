namespace Stash.Debugging;

/// <summary>
/// Describes why the interpreter paused execution.
/// </summary>
public enum PauseReason
{
    /// <summary>A breakpoint was hit.</summary>
    Breakpoint,

    /// <summary>A stepping operation completed (step into, step over, step out).</summary>
    Step,

    /// <summary>An external pause was requested (e.g., DAP pause request).</summary>
    Pause,

    /// <summary>An exception/runtime error occurred.</summary>
    Exception,

    /// <summary>Execution stopped at the first statement (stopOnEntry).</summary>
    Entry,

    /// <summary>A function breakpoint was hit.</summary>
    FunctionBreakpoint,
}
