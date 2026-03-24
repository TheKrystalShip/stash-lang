namespace Stash.Debugging;

using System.Collections.Generic;
using Stash.Common;
using StashEnv = Stash.Interpreting.Environment;
using Stash.Interpreting;

/// <summary>
/// Interface for debugger hooks called during interpretation.
/// When no debugger is attached, these methods are never called (null check).
/// A DAP adapter implements this interface to receive interpreter events and
/// control execution flow.
/// </summary>
public interface IDebugger
{
    // ── Existing hooks (required) ──────────────────────────────────────

    /// <summary>
    /// Called before each statement is executed. Implementations can block
    /// here to pause execution (e.g., on breakpoint hit or step completion).
    /// </summary>
    void OnBeforeExecute(SourceSpan span, StashEnv env, int threadId);

    /// <summary>
    /// Called when a function is entered.
    /// </summary>
    void OnFunctionEnter(string name, SourceSpan callSite, StashEnv env, int threadId);

    /// <summary>
    /// Called when a function returns.
    /// </summary>
    void OnFunctionExit(string name, int threadId);

    /// <summary>
    /// Called when a runtime error occurs.
    /// </summary>
    void OnError(RuntimeError error, IReadOnlyList<CallFrame> callStack, int threadId);

    // ── New hooks (optional, default no-op) ────────────────────────────

    /// <summary>
    /// Whether execution should stop at the very first statement (DAP "stopOnEntry").
    /// </summary>
    bool StopOnEntry => false;

    /// <summary>
    /// Whether an external pause has been requested (e.g., DAP pause request).
    /// The interpreter checks this in the execution loop. When true, the interpreter
    /// will call <see cref="OnBeforeExecute"/> which should block until resumed.
    /// </summary>
    bool IsPauseRequested => false;

    /// <summary>
    /// Called when the interpreter loads and begins executing a source file
    /// (main script or imported module). Used for DAP "loadedSources" tracking.
    /// </summary>
    void OnSourceLoaded(string filePath) { }

    /// <summary>
    /// Called when the interpreter finishes executing (normally or due to error).
    /// Used to send DAP "terminated" and "exited" events.
    /// </summary>
    void OnExecutionComplete() { }

    /// <summary>
    /// Called when the interpreter produces output (e.g., io.println).
    /// Used to send DAP "output" events.
    /// </summary>
    /// <param name="category">Output category: "stdout", "stderr", or "console".</param>
    /// <param name="text">The output text.</param>
    void OnOutput(string category, string text) { }

    /// <summary>
    /// Called when a new logical thread starts (e.g., task.run() spawns a child interpreter).
    /// The DAP adapter uses this to send thread-started events to the client.
    /// </summary>
    void OnThreadStarted(int threadId, string name, Interpreter interpreter) { }

    /// <summary>
    /// Called when a logical thread exits (e.g., a task completes).
    /// The DAP adapter uses this to send thread-exited events to the client.
    /// </summary>
    void OnThreadExited(int threadId) { }

    /// <summary>
    /// Determines whether the debugger should break on the given runtime error.
    /// Called before <see cref="OnError"/>. If this returns true, the interpreter
    /// will pause execution via <see cref="OnBeforeExecute"/> before propagating the error.
    /// Used for DAP exception breakpoint configuration ("all", "uncaught", "never").
    /// </summary>
    bool ShouldBreakOnException(RuntimeError error) => false;

    /// <summary>
    /// Determines whether the debugger should break on entry to the named function.
    /// Called during <see cref="OnFunctionEnter"/>. Used for DAP function breakpoints.
    /// </summary>
    bool ShouldBreakOnFunctionEntry(string functionName) => false;
}
