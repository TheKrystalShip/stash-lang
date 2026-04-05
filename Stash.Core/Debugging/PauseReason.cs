namespace Stash.Debugging;

/// <summary>
/// Specifies the reason the interpreter paused execution during a debug session.
/// </summary>
/// <remarks>
/// <para>
/// The <c>DebugSession</c> (DAP adapter) and <see cref="CliDebugger"/> report this
/// value to the debug client when sending a <c>stopped</c> event so that the client
/// can display the appropriate label in the call-stack and status views.
/// </para>
/// <para>
/// The string representations used in DAP events are lowercase versions of each member
/// name (e.g., <c>"breakpoint"</c>, <c>"step"</c>, <c>"exception"</c>).
/// </para>
/// </remarks>
public enum PauseReason
{
    /// <summary>Execution reached a source-level <see cref="Breakpoint"/> and the breakpoint's conditions were satisfied.</summary>
    Breakpoint,

    /// <summary>A stepping operation completed — step into, step over, or step out.</summary>
    Step,

    /// <summary>An external pause was requested by the debug client (DAP <c>pause</c> request or Ctrl+C in the CLI).</summary>
    Pause,

    /// <summary>A <see cref="Stash.Interpreting.RuntimeError"/> was raised and <see cref="IDebugger.ShouldBreakOnException"/> returned <see langword="true"/>.</summary>
    Exception,

    /// <summary>Execution paused at the very first statement because <see cref="IDebugger.StopOnEntry"/> is <see langword="true"/>.</summary>
    Entry,

    /// <summary>Execution entered a function whose name matches a configured DAP function breakpoint.</summary>
    FunctionBreakpoint,
}
