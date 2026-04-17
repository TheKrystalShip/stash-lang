using System;
using System.Collections.Concurrent;
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
    private CancellationToken _ct;

    public VMContext(CancellationToken ct)
    {
        _ct = ct;
    }

    internal void SetCancellationToken(CancellationToken ct) => _ct = ct;

    // --- Execution Context ---

    public object? LastError { get; set; }
    public bool EmbeddedMode { get; set; }
    public string? CurrentFile { get; set; }

    // Lazy span: set by the VM before each built-in call so built-ins can report
    // their call-site location without an eager O(log n) binary search on the success path.
    internal SourceMap? CallSourceMap;
    internal int CallIP;
    internal SourceSpan? _currentSpan;

    public SourceSpan? CurrentSpan
    {
        get => _currentSpan ?? CallSourceMap?.GetSpan(CallIP);
        set => _currentSpan = value;
    }

    public string[]? ScriptArgs { get; set; }
    public TextWriter Output { get; set; } = TextWriter.Null;
    public TextWriter ErrorOutput { get; set; } = TextWriter.Null;
    public TextReader Input { get; set; } = TextReader.Null;
    public CancellationToken CancellationToken => _ct;
    public object? Debugger { get; set; }

    // --- Elevation Context ---
    public bool ElevationActive { get; set; }
    public string? ElevationCommand { get; set; }

    public void EmitExit(int code)
    {
        CleanupTrackedProcesses();
        CleanupTrackedWatchers();
        if (EmbeddedMode)
        {
            throw new Stash.Runtime.ExitException(code);
        }

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

    // --- Process Tracking ---

    public List<(StashInstance Handle, Process Process)> TrackedProcesses { get; } = new();
    public Dictionary<StashInstance, StashInstance> ProcessWaitCache { get; } = new();
    public Dictionary<StashInstance, List<IStashCallable>> ProcessExitCallbacks { get; } = new();
    public void CleanupTrackedProcesses()
    {
        List<(StashInstance Handle, Process Process)> snapshot;
        snapshot = new List<(StashInstance, Process)>(TrackedProcesses);
        TrackedProcesses.Clear();
        ProcessExitCallbacks.Clear();

        foreach (var (_, osProcess) in snapshot)
        {
            try
            {
                if (!osProcess.HasExited)
                {
                    osProcess.Kill(false);
                    if (!osProcess.WaitForExit(3000))
                    {
                        osProcess.Kill(true);
                    }
                }
            }
            catch { /* Process may have already exited */ }

            try { osProcess.Dispose(); }
            catch { /* Best-effort disposal */ }
        }
    }

    // --- Test Context ---

    public ITestHarness? TestHarness { get; set; }
    public string? CurrentDescribe { get; set; }
    public string[]? TestFilter { get; set; }
    public bool DiscoveryMode { get; set; }
    public List<List<IStashCallable>> BeforeEachHooks { get; } = new();
    public List<List<IStashCallable>> AfterEachHooks { get; } = new();
    public List<List<IStashCallable>> AfterAllHooks { get; } = new();

    // --- ITemplateContext ---

    object? ITemplateContext.CompileAndRenderTemplate(string template, Runtime.Types.StashDictionary data, string? basePath)
    {
        if (Globals is null)
        {
            return null;
        }

        var evaluator = new VMTemplateEvaluator(Globals);
        var renderer = new Stash.Tpl.TemplateRenderer(evaluator, basePath);
        return renderer.Render(template, data);
    }

    object? ITemplateContext.CompileTemplate(string template)
    {
        var lexer = new Stash.Tpl.TemplateLexer(template);
        var tokens = lexer.Scan();
        var parser = new Stash.Tpl.TemplateParser(tokens);
        return parser.Parse();
    }

    object? ITemplateContext.RenderCompiledTemplate(object? compiled, Runtime.Types.StashDictionary data)
    {
        if (compiled is not List<Stash.Tpl.TemplateNode> nodes)
        {
            throw new RuntimeError("'tpl.render' expects a string or compiled template as the first argument.");
        }

        if (Globals is null)
        {
            return null;
        }

        var evaluator = new VMTemplateEvaluator(Globals);
        var renderer = new Stash.Tpl.TemplateRenderer(evaluator);
        return renderer.Render(nodes, data);
    }

    // --- File Watch Context ---

    public List<(StashInstance Handle, FileSystemWatcher Watcher)> TrackedWatchers { get; } = new();
    public void CleanupTrackedWatchers()
    {
        List<(StashInstance Handle, FileSystemWatcher Watcher)> snapshot;
        snapshot = new List<(StashInstance, FileSystemWatcher)>(TrackedWatchers);
        TrackedWatchers.Clear();

        foreach (var (_, watcher) in snapshot)
        {
            try
            {
                watcher.EnableRaisingEvents = false;
                watcher.Dispose();
            }
            catch { /* Best-effort disposal */ }
        }
    }

    // --- IInterpreterContext ---

    /// <summary>
    /// Creates a child context for async/parallel execution.
    /// Shared (by reference): <see cref="Output"/>, <see cref="ErrorOutput"/>, <see cref="Input"/>, <see cref="Globals"/>, <see cref="ModuleCache"/>, <see cref="ModuleLocks"/>, <see cref="ModuleLoader"/>, <see cref="TestHarness"/>.
    /// Copied (by value): <see cref="CurrentFile"/>, <see cref="ScriptArgs"/>, <see cref="ElevationActive"/>, <see cref="ElevationCommand"/>, <see cref="EmbeddedMode"/>.
    /// Writers are upgraded to <see cref="SynchronizedTextWriter"/> on first fork to prevent interleaved output.
    /// </summary>
    public IInterpreterContext Fork(CancellationToken cancellationToken = default)
    {
        // Upgrade to synchronized writers on first fork to prevent interleaved output
        // when multiple parallel tasks write to the same underlying writer.
        if (Output is not SynchronizedTextWriter)
        {
            Output = new SynchronizedTextWriter(Output);
        }

        if (ErrorOutput is not SynchronizedTextWriter)
        {
            ErrorOutput = new SynchronizedTextWriter(ErrorOutput);
        }

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
            EmbeddedMode = EmbeddedMode,
            TestHarness = TestHarness,
            TestFilter = TestFilter,
            Globals = Globals,
            ModuleLoader = ModuleLoader,
            ModuleCache = ModuleCache,
            ModuleLocks = ModuleLocks,
        };
    }

    /// <summary>
    /// Reference to the VM's global variable store. Set by <see cref="VirtualMachine"/> so that
    /// <see cref="InvokeCallback"/> can create a child VM for <c>VMFunction</c> closures.
    /// </summary>
    internal Dictionary<string, StashValue>? Globals { get; set; }

    /// <summary>Module loading callback, propagated from the parent <see cref="VirtualMachine"/>.</summary>
    internal Func<string, string?, Chunk>? ModuleLoader { get; set; }

    /// <summary>Shared module cache, propagated from the parent <see cref="VirtualMachine"/>.</summary>
    internal ConcurrentDictionary<string, Dictionary<string, StashValue>>? ModuleCache { get; set; }

    /// <summary>Per-module locks for double-checked loading, propagated from the parent <see cref="VirtualMachine"/>.</summary>
    internal ConcurrentDictionary<string, object>? ModuleLocks { get; set; }

    /// <summary>
    /// Reference to the active <see cref="VirtualMachine"/> executing on the main thread.
    /// Used by <see cref="InvokeCallback"/> to execute user lambdas inline on the main thread.
    /// Null on contexts created for background threads.
    /// </summary>
    internal VirtualMachine? ActiveVM { get; set; }

    /// <summary>
    /// The managed thread ID of the thread that owns this VM context.
    /// Used by <see cref="InvokeCallback"/> to distinguish same-thread (inline) from
    /// background-thread (child VM) callback invocations.
    /// </summary>
    internal int MainThreadId { get; set; }

    public StashValue InvokeCallbackDirect(IStashCallable callable, System.ReadOnlySpan<StashValue> args)
    {
        if (callable is VMFunction vmFn)
        {
            if (ActiveVM != null && System.Threading.Thread.CurrentThread.ManagedThreadId == MainThreadId)
            {
                return ActiveVM.ExecuteVMFunctionInlineDirect(vmFn, args, null);
            }

            if (Globals != null)
            {
                var childVm = new VirtualMachine(Globals, CancellationToken);
                childVm.Output = Output;
                childVm.ErrorOutput = ErrorOutput;
                childVm.Input = Input;
                childVm.CurrentFile = CurrentFile;
                childVm.ScriptArgs = ScriptArgs;
                childVm.EmbeddedMode = EmbeddedMode;
                if (ModuleLoader != null) childVm.ModuleLoader = ModuleLoader;
                if (ModuleCache != null) childVm.ModuleCache = ModuleCache;
                if (ModuleLocks != null) childVm.ModuleLocks = ModuleLocks;

                childVm.InitGlobalSlots(vmFn.Chunk);
                StashValue[] argsCopy = args.ToArray();
                StashValue result = childVm.CallClosureDirect(vmFn, argsCopy);
                // Sync any globals modified by the child back to the parent VM's slot array.
                // The child writes through to the shared _globals dict; refresh the parent's slots.
                ActiveVM?.RefreshGlobalSlots();
                return result;
            }
        }
        return callable.CallDirect(Fork(), args);
    }

}
