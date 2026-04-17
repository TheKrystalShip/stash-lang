namespace Stash.Runtime;

using System.IO;
using System.Threading;
using Stash.Common;

/// <summary>
/// Core execution state: I/O streams, cancellation, state access, and forking.
/// This is the base context that most built-in functions need.
/// </summary>
public interface IExecutionContext : IBuiltInContext
{
    // --- State access ---
    object? LastError { get; set; }
    bool EmbeddedMode { get; }
    string? CurrentFile { get; }
    SourceSpan? CurrentSpan { get; }
    string[]? ScriptArgs { get; }

    // --- I/O streams ---
    new TextWriter Output { get; set; }
    new TextWriter ErrorOutput { get; set; }
    new TextReader Input { get; set; }

    // --- Cancellation ---
    new CancellationToken CancellationToken { get; }

    // --- Debug support ---
    object? Debugger { get; }

    // --- Tilde expansion (static in Interpreter, but expose as instance for interface) ---
    string ExpandTilde(string path);

    // --- Output notification (for debugger integration) ---
    void NotifyOutput(string category, string text) { }

    /// <summary>
    /// Terminates the process with the given exit code.
    /// </summary>
    /// <remarks>
    /// The default implementation calls <see cref="System.Environment.Exit(int)"/> directly.
    /// Implementers that support <see cref="EmbeddedMode"/> must override this to throw
    /// an appropriate exception instead of terminating the host process.
    /// </remarks>
    void EmitExit(int code) { System.Environment.Exit(code); }
}
