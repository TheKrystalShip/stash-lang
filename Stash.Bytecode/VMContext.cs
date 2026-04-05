using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using Stash.Common;
using Stash.Runtime;
using Stash.Runtime.Types;

namespace Stash.Bytecode;

/// <summary>
/// Minimal <see cref="IInterpreterContext"/> adapter for the bytecode VM.
/// Provides the execution context that built-in functions expect.
/// Phase 4: I/O streams, cancellation, and basic state.
/// Phase 6+: Process tracking, test framework, template rendering.
/// </summary>
internal sealed class VMContext : IInterpreterContext
{
    private readonly CancellationToken _ct;

    public VMContext(CancellationToken ct)
    {
        _ct = ct;
    }

    // --- IExecutionContext ---

    public object? LastError { get; set; }
    public bool EmbeddedMode { get; set; }
    public string? CurrentFile { get; set; }
    public SourceSpan? CurrentSpan { get; set; }
    public string[]? ScriptArgs { get; set; }
    public TextWriter Output { get; set; } = Console.Out;
    public TextWriter ErrorOutput { get; set; } = Console.Error;
    public TextReader Input { get; set; } = Console.In;
    public CancellationToken CancellationToken => _ct;
    public object? Debugger { get; set; }

    // --- Elevation Context ---
    public bool ElevationActive { get; set; }
    public string? ElevationCommand { get; set; }

    public void EmitExit(int code)
    {
        if (EmbeddedMode)
            throw new Stash.Runtime.ExitException(code);
        System.Environment.Exit(code);
    }

    public string ExpandTilde(string path)
    {
        if (path.StartsWith('~'))
        {
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return path.Length == 1 ? home : string.Concat(home, path.AsSpan(1));
        }
        return path;
    }

    // --- IProcessContext (stubs for Phase 6) ---

    public List<(StashInstance Handle, Process Process)> TrackedProcesses { get; } = new();
    public Dictionary<StashInstance, StashInstance> ProcessWaitCache { get; } = new();
    public Dictionary<StashInstance, List<IStashCallable>> ProcessExitCallbacks { get; } = new();
    public void CleanupTrackedProcesses() { }

    // --- ITestContext (stubs for TAP framework) ---

    public ITestHarness? TestHarness { get; set; }
    public string? CurrentDescribe { get; set; }
    public string[]? TestFilter { get; set; }
    public bool DiscoveryMode { get; set; }
    public List<List<IStashCallable>> BeforeEachHooks { get; } = new();
    public List<List<IStashCallable>> AfterEachHooks { get; } = new();
    public List<List<IStashCallable>> AfterAllHooks { get; } = new();

    // --- ITemplateContext (default implementations from interface suffice) ---

    // --- IFileWatchContext (stubs for Phase 6) ---

    public List<(StashInstance Handle, FileSystemWatcher Watcher)> TrackedWatchers { get; } = new();
    public void CleanupTrackedWatchers() { }

    // --- IInterpreterContext ---

    public IInterpreterContext Fork(CancellationToken cancellationToken = default)
    {
        return new VMContext(cancellationToken)
        {
            Output = Output,
            ErrorOutput = ErrorOutput,
            Input = Input,
            CurrentFile = CurrentFile,
            ScriptArgs = ScriptArgs,
            ElevationActive = ElevationActive,
            ElevationCommand = ElevationCommand,
            Debugger = Debugger,
        };
    }
}
