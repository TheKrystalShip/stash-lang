namespace Stash.Interpreting;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using Stash.Common;
using Stash.Debugging;
using Stash.Runtime;
using Stash.Runtime.Types;

/// <summary>
/// Encapsulates all mutable per-execution state for the interpreter.
/// Each concurrent execution path (parallel task, forked evaluation) gets its own context,
/// while sharing the immutable interpreter infrastructure (globals, resolver cache, capabilities).
/// </summary>
public class ExecutionContext
{
    /// <summary>The current lexical scope. Changes as the interpreter enters and exits blocks.</summary>
    public Environment Environment { get; set; }

    /// <summary>The last caught error. Used by lastError() built-in and try expressions.</summary>
    public object? LastError { get; set; }

    /// <summary>Pending standard input to pipe into the next command execution.</summary>
    public string? PendingStdin { get; set; }

    /// <summary>The file path of the script currently being executed.</summary>
    public string? CurrentFile { get; set; }

    /// <summary>The source span of the statement currently being executed.</summary>
    public SourceSpan? CurrentSpan { get; set; }

    /// <summary>Number of statements executed since the last reset.</summary>
    public long StepCount { get; set; }

    /// <summary>When true, variable lookups walk the scope chain instead of using resolver distances.</summary>
    public bool IsAdHocEval { get; set; }

    /// <summary>The runtime call stack, tracking active function invocations.</summary>
    public List<CallFrame> CallStack { get; }

    /// <summary>Set of module paths currently being imported, used to detect circular imports.</summary>
    public HashSet<string> ImportStack { get; }

    /// <summary>The output writer for io.println and io.print.</summary>
    public TextWriter Output { get; set; }

    /// <summary>The error output writer.</summary>
    public TextWriter ErrorOutput { get; set; }

    /// <summary>The input reader for io.readLine.</summary>
    public TextReader Input { get; set; }

    /// <summary>Token checked at each statement boundary to support cooperative cancellation.</summary>
    public CancellationToken CancellationToken { get; set; }

    /// <summary>Background processes launched by process.spawn(), tracked for cleanup.</summary>
    public List<(StashInstance Handle, Process OsProcess)> TrackedProcesses { get; }

    /// <summary>Cache mapping process handles to their wait results.</summary>
    public Dictionary<StashInstance, StashInstance> ProcessWaitCache { get; }

    /// <summary>Callbacks registered via process.onExit().</summary>
    public Dictionary<StashInstance, List<IStashCallable>> ProcessExitCallbacks { get; }

    /// <summary>The name of the current describe block, used to namespace test names.</summary>
    public string? CurrentDescribe { get; set; }

    /// <summary>Optional test name filter patterns.</summary>
    public string[]? TestFilter { get; set; }

    /// <summary>When true, test() records names without executing test bodies.</summary>
    public bool DiscoveryMode { get; set; }

    /// <summary>Stack of beforeEach hook lists, one per nested describe block.</summary>
    public List<List<IStashCallable>> BeforeEachHooks { get; }

    /// <summary>Stack of afterEach hook lists, one per nested describe block.</summary>
    public List<List<IStashCallable>> AfterEachHooks { get; }

    /// <summary>Stack of afterAll hook lists, one per nested describe block.</summary>
    public List<List<IStashCallable>> AfterAllHooks { get; }

    /// <summary>Creates a new execution context with default state.</summary>
    /// <param name="environment">The initial environment (typically the global scope).</param>
    public ExecutionContext(Environment environment)
    {
        Environment = environment;
        Output = GetConsoleOutOrNull();
        ErrorOutput = GetConsoleErrorOrNull();
        Input = GetConsoleInOrNull();
        CallStack = new();
        ImportStack = new();
        TrackedProcesses = new();
        ProcessWaitCache = new(ReferenceEqualityComparer.Instance);
        ProcessExitCallbacks = new(ReferenceEqualityComparer.Instance);
        BeforeEachHooks = new();
        AfterEachHooks = new();
        AfterAllHooks = new();
    }

    private static TextWriter GetConsoleOutOrNull()
    {
        try { return Console.Out; }
        catch (PlatformNotSupportedException) { return TextWriter.Null; }
    }

    private static TextWriter GetConsoleErrorOrNull()
    {
        try { return Console.Error; }
        catch (PlatformNotSupportedException) { return TextWriter.Null; }
    }

    private static TextReader GetConsoleInOrNull()
    {
        try { return Console.In; }
        catch (PlatformNotSupportedException) { return TextReader.Null; }
    }
}
