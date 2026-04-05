namespace Stash.Debugging;

using System.Collections.Generic;
using Stash.Common;
using Stash.Runtime;

/// <summary>
/// Interface for debugger hooks called during interpretation.
/// When no debugger is attached, these methods are never called (null check).
/// A DAP adapter implements this interface to receive interpreter events and
/// control execution flow.
/// </summary>
public interface IDebugger
{
    // ── Required hooks ──────────────────────────────────────

    void OnBeforeExecute(SourceSpan span, IDebugScope env, int threadId);
    void OnFunctionEnter(string name, SourceSpan callSite, IDebugScope env, int threadId);
    void OnFunctionExit(string name, int threadId);
    void OnError(RuntimeError error, IReadOnlyList<CallFrame> callStack, int threadId);

    // ── Optional hooks (default no-op) ────────────────────

    bool StopOnEntry => false;
    bool IsPauseRequested => false;
    void OnSourceLoaded(string filePath) { }
    void OnExecutionComplete() { }
    void OnOutput(string category, string text) { }
    void OnThreadStarted(int threadId, string name, IDebugExecutor executor) { }
    void OnThreadExited(int threadId) { }
    bool ShouldBreakOnException(RuntimeError error) => false;
    bool ShouldBreakOnFunctionEntry(string functionName) => false;
}
