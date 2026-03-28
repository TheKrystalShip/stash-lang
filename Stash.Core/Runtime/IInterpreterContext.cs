namespace Stash.Runtime;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using Stash.Common;
using Stash.Runtime.Types;

/// <summary>
/// Abstraction over the interpreter that built-in functions use for state access.
/// The concrete Interpreter class implements this interface.
/// </summary>
public interface IInterpreterContext
{
    // --- State access ---
    object? LastError { get; set; }
    bool EmbeddedMode { get; }
    string? CurrentFile { get; }
    SourceSpan? CurrentSpan { get; }
    string[]? ScriptArgs { get; }

    // --- I/O streams ---
    TextWriter Output { get; set; }
    TextWriter ErrorOutput { get; set; }
    TextReader Input { get; set; }

    // --- Cancellation ---
    CancellationToken CancellationToken { get; }

    // --- Process management ---
    List<(StashInstance Handle, Process Process)> TrackedProcesses { get; }
    Dictionary<StashInstance, StashInstance> ProcessWaitCache { get; }
    Dictionary<StashInstance, List<IStashCallable>> ProcessExitCallbacks { get; }
    void CleanupTrackedProcesses();

    // --- Parallel execution ---
    IInterpreterContext Fork(CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a thread-safe child context with an environment snapshot for parallel execution.
    /// The default falls back to Fork(); Interpreter overrides this with a proper snapshot.
    /// </summary>
    IInterpreterContext ForkParallel(CancellationToken cancellationToken = default) => Fork(cancellationToken);

    // --- Debug support ---
    object? Debugger { get; }

    // --- Test framework ---
    object? TestHarness { get; set; }
    string? CurrentDescribe { get; set; }
    string[]? TestFilter { get; set; }
    bool DiscoveryMode { get; set; }
    List<List<IStashCallable>> BeforeEachHooks { get; }
    List<List<IStashCallable>> AfterEachHooks { get; }
    List<List<IStashCallable>> AfterAllHooks { get; }

    // --- Tilde expansion (static in Interpreter, but expose as instance for interface) ---
    string ExpandTilde(string path);

    // --- Output notification (for debugger integration) ---
    void NotifyOutput(string category, string text) { }

    // --- Process exit (embeddedMode throws, otherwise calls Environment.Exit) ---
    void EmitExit(int code) { System.Environment.Exit(code); }

    // --- Template rendering (implementation in Interpreter) ---
    object? CompileAndRenderTemplate(string template, StashDictionary data, string? basePath = null) { return null; }
    object? CompileTemplate(string template) { return null; }
    object? RenderCompiledTemplate(object? compiled, StashDictionary data) { return null; }
}
