using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
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
        // Capture the real process cwd exactly once at construction. All subsequent
        // reads of WorkingDirectory return this per-VM value — System.Environment is
        // never consulted again after this line. DirStack is seeded from the same
        // snapshot so both views are consistent from the start.
        WorkingDirectory = System.Environment.CurrentDirectory;
        DirStack = new List<string> { WorkingDirectory };
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

    // Nullable backing fields: null means "use the mode-default".
    // When EmbeddedMode is true  → default is TextWriter.Null / TextReader.Null  (no Console leak).
    // When EmbeddedMode is false → default is Console.Out / Console.Error / Console.In  (CLI default).
    // A host that sets the property explicitly always wins, regardless of mode.
    private TextWriter? _output;
    private TextWriter? _errorOutput;
    private TextReader? _input;

    /// <summary>
    /// Standard output stream. In embedded mode the default is <see cref="TextWriter.Null"/>;
    /// in CLI mode the default falls through to <see cref="Console.Out"/>.
    /// Assign explicitly to override the mode default.
    /// </summary>
    public TextWriter Output
    {
        get => _output ?? (EmbeddedMode ? TextWriter.Null : Console.Out);
        set => _output = value;
    }

    /// <summary>
    /// Standard error stream. In embedded mode the default is <see cref="TextWriter.Null"/>;
    /// in CLI mode the default falls through to <see cref="Console.Error"/>.
    /// Assign explicitly to override the mode default.
    /// </summary>
    public TextWriter ErrorOutput
    {
        get => _errorOutput ?? (EmbeddedMode ? TextWriter.Null : Console.Error);
        set => _errorOutput = value;
    }

    /// <summary>
    /// Standard input stream. In embedded mode the default is <see cref="TextReader.Null"/>;
    /// in CLI mode the default falls through to <see cref="Console.In"/>.
    /// Assign explicitly to override the mode default.
    /// </summary>
    public TextReader Input
    {
        get => _input ?? (EmbeddedMode ? TextReader.Null : Console.In);
        set => _input = value;
    }

    public CancellationToken CancellationToken => _ct;
    public object? Debugger { get; set; }

    // --- Type Registration ---
    internal Func<object?, string>? TypeNameResolver { get; set; }
    string IBuiltInContext.ResolveRegisteredTypeName(object? value) => TypeNameResolver?.Invoke(value) ?? "unknown";

    // --- Elevation Context ---
    public bool ElevationActive { get; set; }
    public string? ElevationCommand { get; set; }

    // --- Lock Context ---
    /// <summary>Stack of active file lock handles held by this VM instance. LIFO; LockEnd pops the top.</summary>
    public Stack<FileLockHandle> ActiveLocks { get; } = new();

    // --- Per-VM virtual process state ---

    /// <summary>
    /// Per-VM working directory. Initialized from <see cref="System.Environment.CurrentDirectory"/>
    /// at construction (single read); never re-reads the real process cwd after that.
    /// Implements <see cref="IInterpreterContext.WorkingDirectory"/>.
    /// </summary>
    public string WorkingDirectory { get; set; } = string.Empty; // Set in ctor after single cwd read

    /// <summary>
    /// Per-VM environment overlay. Key present with non-null value = overridden.
    /// Key present with null value = explicitly unset (shadows real process env).
    /// Key absent = fall back to <see cref="System.Environment.GetEnvironmentVariable"/>.
    /// Never mutate <see cref="System.Environment"/> — writes go here only.
    /// </summary>
    internal Dictionary<string, string?> EnvVars { get; } = new(StringComparer.Ordinal);

    /// <inheritdoc cref="IInterpreterContext.GetEnv"/>
    public string? GetEnv(string name)
    {
        if (EnvVars.TryGetValue(name, out string? overlayValue))
            return overlayValue; // null here = explicitly unset; return null without checking real env
        return System.Environment.GetEnvironmentVariable(name);
    }

    /// <inheritdoc cref="IInterpreterContext.SetEnv"/>
    public void SetEnv(string name, string value) => EnvVars[name] = value;

    /// <inheritdoc cref="IInterpreterContext.UnsetEnv"/>
    public void UnsetEnv(string name) => EnvVars[name] = null;

    /// <inheritdoc cref="IInterpreterContext.AllEnv"/>
    public Dictionary<string, string> AllEnv()
    {
        // Start with the real process env as the base, then layer the overlay on top.
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (System.Collections.DictionaryEntry entry in System.Environment.GetEnvironmentVariables())
        {
            if (entry.Key is string k && entry.Value is string v)
                result[k] = v;
        }
        // Apply overlay: non-null values override, null values mean "explicitly unset" → remove.
        foreach (var (k, v) in EnvVars)
        {
            if (v is null)
                result.Remove(k);
            else
                result[k] = v;
        }
        return result;
    }

    /// <inheritdoc cref="IInterpreterContext.ResolveAgainstCwd"/>
    public string ResolveAgainstCwd(string path) => Path.GetFullPath(path, WorkingDirectory);

    // --- Directory Stack ---
    /// <summary>
    /// Navigation history. Last entry is the current working directory.
    /// Initialized with the cwd captured at construction (see <see cref="WorkingDirectory"/>).
    /// Capped at 256 entries; oldest is dropped when full.
    /// </summary>
    public List<string> DirStack { get; private set; }

    private bool _lockCleanupRegistered;

    // Keep references to prevent GC of signal registrations
    private PosixSignalRegistration? _sigtermReg;
    private PosixSignalRegistration? _sighupReg;

    /// <summary>
    /// Register process exit and signal handlers on the first lock acquisition.
    /// Idempotent — safe to call multiple times.
    /// </summary>
    public void EnsureLockCleanupRegistered()
    {
        if (_lockCleanupRegistered) return;
        _lockCleanupRegistered = true;

        AppDomain.CurrentDomain.ProcessExit += (_, _) => ReleaseAllLocks();
        AppDomain.CurrentDomain.UnhandledException += (_, _) => ReleaseAllLocks();
        Console.CancelKeyPress += (_, _) => ReleaseAllLocks();

        if (!OperatingSystem.IsWindows())
        {
            _sigtermReg = PosixSignalRegistration.Create(PosixSignal.SIGTERM, _ => ReleaseAllLocks());
            _sighupReg  = PosixSignalRegistration.Create(PosixSignal.SIGHUP,  _ => ReleaseAllLocks());
        }
    }

    private void ReleaseAllLocks()
    {
        while (ActiveLocks.TryPop(out FileLockHandle? handle))
        {
            try { handle?.Release(); }
            catch { /* best effort — never throw from exit handlers */ }
        }
    }

    public void EmitExit(int code)
    {
        CleanupTrackedProcesses();
        CleanupTrackedWatchers();
        // Always throw ExitException — the VM dispatch loop runs all pending defer blocks
        // and then calls System.Environment.Exit (or re-throws in embedded mode).
        throw new Stash.Runtime.ExitException(code);
    }

    public int GetLastExitCode() => ActiveVM?.LastExitCode ?? 0;

    /// <inheritdoc/>
    public AliasRegistry AliasRegistry => ActiveVM?.AliasRegistry ?? new AliasRegistry();

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
    public bool HasExclusiveTests { get; set; }
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

    // --- Logger Context ---

    public LoggerState LoggerState { get; set; } = new();

    // --- IInterpreterContext ---

    /// <summary>
    /// Creates a child context for async/parallel execution.
    /// Shared (by reference): <see cref="Output"/>, <see cref="ErrorOutput"/>, <see cref="Input"/>, <see cref="Globals"/>, <see cref="ModuleCache"/>, <see cref="ModuleLocks"/>, <see cref="ModuleLoader"/>, <see cref="TestHarness"/>.
    /// Copied (by value): <see cref="CurrentFile"/>, <see cref="ScriptArgs"/>, <see cref="ElevationActive"/>, <see cref="ElevationCommand"/>, <see cref="EmbeddedMode"/>, <see cref="WorkingDirectory"/>, <see cref="EnvVars"/> (snapshot).
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

        var child = new VMContext(cancellationToken)
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
            HasExclusiveTests = HasExclusiveTests,
            Globals = Globals,
            ModuleLoader = ModuleLoader,
            ModuleCache = ModuleCache,
            ModuleLocks = ModuleLocks,
            LoggerState = LoggerState,
            // Propagate the per-VM virtual cwd — the child inherits the parent's view,
            // not the real process cwd (the constructor already set it from real env,
            // but we override here so forked contexts share the parent's virtual state).
            WorkingDirectory = WorkingDirectory,
        };

        // Propagate env overlay snapshot — child inherits parent's overlay entries.
        // The child owns its own dict (from EnvVars property initializer) so mutations
        // don't bleed back to the parent.
        foreach (var (k, v) in EnvVars)
            child.EnvVars[k] = v;

        return child;
    }

    /// <summary>
    /// Reference to the VM's global variable store. Set by <see cref="VirtualMachine"/> so that
    /// <see cref="InvokeCallback"/> can create a child VM for <c>VMFunction</c> closures.
    /// </summary>
    internal Dictionary<string, StashValue>? Globals { get; set; }

    /// <summary>
    /// The import stack for the VM that owns this context. Set by <see cref="VirtualMachine"/>
    /// at construction and updated to the child's own independent snapshot when a child VM is
    /// spawned. Used by <see cref="InvokeCallbackDirect"/>'s background-thread branch to take
    /// a snapshot of the current in-progress import set before constructing a child VM, so
    /// the child starts with an independent copy and cannot race with the parent.
    ///
    /// <para>
    /// The module-load path (<c>VirtualMachine.Modules.cs</c>) deliberately does NOT update
    /// this field — it shares the parent's <c>_importStack</c> by reference for synchronous
    /// circular-import detection. Only cross-thread fork sites snapshot and propagate this.
    /// </para>
    /// </summary>
    internal HashSet<string>? ImportStack { get; set; }

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
    /// Used by <see cref="InvokeCallbackDirect"/> to distinguish same-thread (inline) from
    /// background-thread (queued) callback invocations.
    /// </summary>
    internal int MainThreadId { get; set; }

    // ── Per-VM callback queue (event-loop marshaling) ─────────────────────────
    //
    // Background producers (fs.watch, signal.on, future timers) call EnqueueCallback
    // rather than forking a child VM.  The VM thread dequeues and runs each callback
    // inline only while parked at a yield point (time.sleep, event.poll, event.loop) via
    // DrainCallbacks.  This gives zero-concurrency delivery → Branch-1 (shared) semantics.
    //
    // Design contract (from brief.md + callback-vm-thread-marshaling.md):
    //   – MPSC: any number of background producers, single consumer (VM thread).
    //   – Per-VM (not process-global) so engine↔engine isolation is preserved.
    //   – _isDraining prevents re-entrant drain (run-to-completion task model).
    //   – Lost-wakeup safety: drain-until-empty after each signal, never one-item-per-signal.

    private readonly ConcurrentQueue<(IStashCallable Callable, StashValue[] Args)> _callbackQueue = new();
    private readonly SemaphoreSlim _queueSignal = new(0, int.MaxValue);
    private bool _isDraining;


    /// <summary>
    /// Enqueues a callback for delivery on the VM thread at the next drain point.
    /// Safe to call from any thread.  <paramref name="args"/> must be an independent
    /// copy — callers must <c>args.ToArray()</c> before passing (spans are ref-structs
    /// and cannot be stored; array ensures the producer does not retain the original buffer).
    /// </summary>
    internal void EnqueueCallback(IStashCallable callable, StashValue[] args)
    {
        _callbackQueue.Enqueue((callable, args));
        _queueSignal.Release(); // wake any parked drain loop
    }

    /// <summary>
    /// Drain queued callbacks according to <paramref name="mode"/>.  This is the ONLY method
    /// that pops the queue and invokes callbacks — the single chokepoint.
    ///
    /// <list type="bullet">
    ///   <item><see cref="WaitMode.PollMode"/> — dequeue everything currently pending and return.</item>
    ///   <item><see cref="WaitMode.UntilMode"/> — park on <c>WaitAny([cancel, queueSignal], remaining)</c>,
    ///     drain, recompute remaining, repeat until the deadline is reached.</item>
    ///   <item><see cref="WaitMode.ForeverMode"/> — same as Until but with no deadline.</item>
    /// </list>
    ///
    /// Reentrancy: if <c>_isDraining</c> is already set (a queued callback reached a yield point
    /// internally), the method returns immediately without draining (run-to-completion model).
    /// </summary>
    public void DrainCallbacks(WaitMode mode)
    {
        // Reentrancy guard — a callback calling time.sleep/event.poll must not re-pump.
        if (_isDraining) return;

        _isDraining = true;
        try
        {
            switch (mode)
            {
                case WaitMode.PollMode:
                    DrainAll();
                    break;

                case WaitMode.UntilMode until:
                    DrainUntil(until.Deadline);
                    break;

                case WaitMode.ForeverMode:
                    DrainForever();
                    break;
            }
        }
        finally
        {
            _isDraining = false;
        }
    }

    private void DrainAll()
    {
        while (_callbackQueue.TryDequeue(out var item))
            InvokeQueuedCallback(item.Callable, item.Args);
    }

    private void DrainUntil(DateTimeOffset deadline)
    {
        var cancelHandle = _ct.CanBeCanceled ? _ct.WaitHandle : null;

        while (true)
        {
            // Drain whatever is currently queued.
            DrainAll();

            // Check if time is up.
            var remaining = deadline - DateTimeOffset.UtcNow;
            if (remaining <= TimeSpan.Zero) return;

            // Park until the queue has something OR the deadline passes OR cancellation.
            int remainingMs = (int)Math.Min(remaining.TotalMilliseconds, int.MaxValue);
            if (cancelHandle is not null)
            {
                // WaitAny: [cancel, queueSignal] — whichever fires first wakes us.
                int index = WaitHandle.WaitAny(new[] { cancelHandle, _queueSignal.AvailableWaitHandle }, remainingMs);
                if (index == 0) // cancellation fired
                    _ct.ThrowIfCancellationRequested();
                // If index == WaitHandle.WaitTimeout (258), the duration expired; drain once
                // more in case a late enqueue raced the timeout, then return.
                if (index == WaitHandle.WaitTimeout)
                {
                    DrainAll();
                    return;
                }
                // index == 1: queue signal fired — consume the semaphore permit so the
                // AvailableWaitHandle is properly reset for the next wait iteration.
                // Non-blocking TryWait with 0 timeout consumes it without blocking.
                _queueSignal.Wait(0);
            }
            else
            {
                // No cancellation token — just wait on queue signal with a timeout.
                bool signaled = _queueSignal.Wait(remainingMs);
                if (!signaled)
                {
                    // Timeout: drain any late arrivals and return.
                    DrainAll();
                    return;
                }
            }
            // Loop: recompute remaining and drain again.
        }
    }

    private void DrainForever()
    {
        var cancelHandle = _ct.CanBeCanceled ? _ct.WaitHandle : null;

        while (true)
        {
            DrainAll();

            if (cancelHandle is not null)
            {
                int index = WaitHandle.WaitAny(new[] { cancelHandle, _queueSignal.AvailableWaitHandle });
                if (index == 0)
                    _ct.ThrowIfCancellationRequested();
                _queueSignal.Wait(0);
            }
            else
            {
                _queueSignal.Wait();
            }
        }
    }

    private void InvokeQueuedCallback(IStashCallable callable, StashValue[] args)
    {
        // Deliver via the inline same-thread path (Branch-1 semantics).
        // ActiveVM must be set — we are on the VM thread, inside a yield point.
        if (ActiveVM is null) return;

        try
        {
            if (callable is VMFunction vmFn)
                ActiveVM.ExecuteVMFunctionInlineDirect(vmFn, args, null);
            else
                callable.CallDirect(this, args);
        }
        catch
        {
            // Log-and-swallow: a buggy handler must not break subsequent callbacks
            // or the drain loop.  Matches SignalImpl.Dispatch and FsBuiltIns.InvokeCallback.
        }
    }

    public StashValue InvokeCallbackDirect(IStashCallable callable, System.ReadOnlySpan<StashValue> args)
    {
        if (callable is VMFunction vmFn)
        {
            if (ActiveVM != null && System.Threading.Thread.CurrentThread.ManagedThreadId == MainThreadId)
            {
                // Branch 1 — same thread, queue-owning root: execute inline, shared semantics.
                return ActiveVM.ExecuteVMFunctionInlineDirect(vmFn, args, null);
            }

            if (ActiveVM != null)
            {
                // Branch 2 — background thread on the queue-owning root: ENQUEUE for delivery
                // at the next drain point (fs.watch / signal.on path; serial-shared callback
                // marshaling). We do NOT fork a child VM here — the queue delivers on the VM
                // thread (zero concurrency) so shared semantics are safe without cloning.
                // args must be copied because ReadOnlySpan<StashValue> is a ref struct and
                // cannot be stored; the array is owned by the queue entry.
                EnqueueCallback(callable, args.ToArray());
                return StashValue.Null; // return value is discarded by both fs.watch and signal.on
            }

            // Branch 3 — forked child (ActiveVM == null): execute via a child VM fork.
            // This path is taken by task.run / task.parMap / task.timeout / process.exec exit
            // callbacks / TCP accept — all of which run on a forked child context where ActiveVM
            // is null. These use the parallel-isolated async contract: real thread, cloned state,
            // communicate via the awaited Future. The locked design Q3 asymmetry:
            //   async = parallel-isolated (this branch)
            //   event callback = serial-shared (Branch 2 above)
            if (Globals != null)
            {
                // Cross-thread path: apply freeze-or-clone to globals so the child gets a
                // private copy of any non-frozen mutable values (no cross-thread data races).
                var childGlobals = IsolationHelpers.BuildChildGlobals(Globals);
                // Snapshot upvalues so the child gets private copies of every reference-typed
                // captured local.  Without this, the child's frame would share the parent's
                // live Upvalue objects, letting a background-thread write race on a captured
                // dict / array / struct.  Mirrors what SpawnAsyncFunction does for async forks.
                var isolatedUpvalues = IsolationHelpers.SnapshotUpvalues(vmFn.Upvalues);
                // Ensure ModuleGlobals on the isolated VMFunction points at the child-local
                // globals copy when the callback was defined in the main module (i.e. when
                // vmFn.ModuleGlobals is the parent's live _globals or null).  For callbacks
                // imported from a separate module, keep the original module dict — same logic
                // as SpawnAsyncFunction's capturedModuleGlobals computation.
                var isolatedModuleGlobals = (vmFn.ModuleGlobals is null || ReferenceEquals(vmFn.ModuleGlobals, Globals))
                    ? childGlobals
                    : vmFn.ModuleGlobals;
                var isolatedFn = new VMFunction(vmFn.Chunk, isolatedUpvalues) { ModuleGlobals = isolatedModuleGlobals };
                var childVm = new VirtualMachine(childGlobals, CancellationToken);
                childVm.Output = Output;
                childVm.ErrorOutput = ErrorOutput;
                childVm.Input = Input;
                childVm.CurrentFile = CurrentFile;
                childVm.ScriptArgs = ScriptArgs;
                childVm.EmbeddedMode = EmbeddedMode;
                if (ModuleLoader != null) childVm.ModuleLoader = ModuleLoader;
                if (ModuleCache != null) childVm.ModuleCache = ModuleCache;
                if (ModuleLocks != null) childVm.ModuleLocks = ModuleLocks;
                // Pass a snapshot of the parent's import stack so the child starts with an
                // independent copy of any in-progress imports, not the parent's live set.
                // This prevents spurious "circular import" errors from the parent's in-flight
                // imports racing with the child's import calls on a background thread.
                //
                // MUST use IsolationHelpers.SnapshotImportStack (explicit foreach with version-
                // checked enumerator) and NOT pass the live reference directly.  Passing the live
                // ref enumerates the parent's HashSet on the background thread while Modules.cs
                // may be calling _importStack.Add/Remove on the parent's main thread, causing a
                // silent torn-snapshot or InvalidOperationException (swallowed by the callback's
                // try/catch → silent callback loss).  The bounded-retry snapshot mirrors the
                // SnapshotEntries guard added for globals in commit 224c52e3.
                if (ImportStack != null)
                    childVm.InitImportStack(IsolationHelpers.SnapshotImportStack(ImportStack));

                childVm.InitGlobalSlots(vmFn.Chunk);
                StashValue[] argsCopy = args.ToArray();
                return childVm.CallClosureDirect(isolatedFn, argsCopy);
                // Note: we no longer call ActiveVM?.RefreshGlobalSlots() because the child
                // now has its own isolated globals dict — writes are call-local, not shared.
            }
        }

        // Non-VMFunction callables (native delegates, etc.): fall through to fork path.
        return callable.CallDirect(Fork(), args);
    }

}
