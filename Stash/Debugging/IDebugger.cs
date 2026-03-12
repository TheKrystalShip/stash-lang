namespace Stash.Debugging;

using System.Collections.Generic;
using Stash.Common;
using StashEnv = Stash.Interpreting.Environment;
using Stash.Interpreting;

/// <summary>
/// Interface for debugger hooks called during interpretation.
/// When no debugger is attached, these methods are never called (null check).
/// </summary>
public interface IDebugger
{
    /// <summary>
    /// Called before each statement is executed.
    /// </summary>
    void OnBeforeExecute(SourceSpan span, StashEnv env);

    /// <summary>
    /// Called when a function is entered.
    /// </summary>
    void OnFunctionEnter(string name, SourceSpan callSite, StashEnv env);

    /// <summary>
    /// Called when a function returns.
    /// </summary>
    void OnFunctionExit(string name);

    /// <summary>
    /// Called when a runtime error occurs.
    /// </summary>
    void OnError(RuntimeError error, IReadOnlyList<CallFrame> callStack);
}
