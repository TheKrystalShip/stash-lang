using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Stash.Common;
using Stash.Debugging;
using Stash.Lexing;
using Stash.Parsing;
using Stash.Resolution;
using Stash.Runtime;
using Stash.Runtime.Protocols;
using Stash.Runtime.Stdlib;
using Stash.Runtime.Types;
using DebugCallFrame = Stash.Debugging.CallFrame;

namespace Stash.Bytecode;

/// <summary>
/// Core fields, constructor, properties, stack operations, and execution entry points.
/// </summary>
public sealed partial class VirtualMachine : IVMTypeRegistrar
{
    // ── Zero-cost debug mode markers ──
    // .NET JIT/AOT fully specializes generic methods over value-type type params.
    // typeof(TDebugMode) == typeof(DebugOn) is a compile-time constant — the JIT
    // eliminates the entire dead branch, producing a DebugOff specialization with
    // zero debug code in the native instruction stream.
    internal readonly struct DebugOn { }
    internal readonly struct DebugOff { }

    private const int DefaultStackSize = 1024;
    private const int DefaultFrameDepth = 256;

    /// <summary>
    /// Sentinel value used to mark function parameters that were not provided by the caller.
    /// The compiler emits a prologue that checks for this sentinel and evaluates default expressions.
    /// </summary>
    public static readonly object NotProvided = new();

    private StashValue[] _stack;
    private int _sp; // stack pointer: index of next free slot

    private CallFrame[] _frames;
    private int _frameCount;

    private readonly Dictionary<string, StashValue> _globals;
    private readonly HashSet<string> _constGlobals = new(StringComparer.Ordinal);
    private StashValue[] _globalSlots = Array.Empty<StashValue>();
    private bool[] _constGlobalSlots = Array.Empty<bool>();
    private string[] _globalNameTable = Array.Empty<string>();
    private static readonly object _undefinedSentinel = new();
    internal static readonly StashValue UndefinedGlobal = StashValue.FromObj(_undefinedSentinel);
    private readonly List<Upvalue> _openUpvalues;
    private CancellationToken _ct;
    private readonly Dictionary<Type, string> _registeredTypeNames = new();
    private readonly Dictionary<string, Func<object, bool>> _registeredTypeChecks = new(StringComparer.Ordinal);

    private readonly List<ExceptionHandler> _exceptionHandlers = new();
    private readonly VMContext _context;
    private readonly ExtensionRegistry _extensionRegistry = new();

    /// <summary>
    /// Per-VM inline cache slot arrays, keyed by chunk identity.
    /// Each entry is a private clone of <see cref="Chunk.ICSlots"/> (the immutable template
    /// pre-filled by <see cref="ChunkBuilder"/>). Created lazily on first frame push for a
    /// given chunk so that two VMs executing the same chunk never share mutable IC state.
    /// </summary>
    private readonly Dictionary<Chunk, ICSlot[]> _vmICSlots = new(ReferenceEqualityComparer.Instance);

    private Func<string, string?, Chunk>? _moduleLoader;
    internal ConcurrentDictionary<string, Dictionary<string, StashValue>> ModuleCache = new(StringComparer.OrdinalIgnoreCase);
    private HashSet<string> _importStack = new(StringComparer.OrdinalIgnoreCase);
    internal ConcurrentDictionary<string, object> ModuleLocks = new(StringComparer.OrdinalIgnoreCase);

    private SpawnedFutureRegistry _spawnedFutures = new SpawnedFutureRegistry();

    /// <summary>
    /// Per-root-VM registry of every user-visible <see cref="StashFuture"/> spawned during
    /// a script run. Shared by reference with every child VM so background futures register
    /// into the same set.  Root VM creates the instance; child VMs receive it via the
    /// constructor path or the <see cref="SpawnedFutures"/> property setter.
    /// The CLI driver reads this after script exit to emit D1 unobserved-task warnings.
    /// </summary>
    internal SpawnedFutureRegistry SpawnedFutures
    {
        get => _spawnedFutures;
        set
        {
            _spawnedFutures = value;
            // Keep context in sync so Fork() and InvokeCallbackDirect propagate correctly.
            // _context may be null during field-initializer phase (before the constructor body
            // runs); the constructor wires SpawnedFutures into the context explicitly.
            if (_context is not null) _context.SpawnedFutures = value;
        }
    }

    // ── Debugger Integration ──
    private IDebugger? _debugger;
    private readonly List<DebugCallFrame> _debugCallStack = new();

    /// <summary>
    /// Persistent global slot allocator shared across all REPL compilations.
    /// When non-null, <see cref="ShellRunner.EvaluateSource"/> uses this allocator so that
    /// all REPL chunks assign the same slot index to the same global name. This prevents
    /// cross-chunk lambdas from reading wrong global slot values when invoked in a later REPL input.
    /// </summary>
    public GlobalSlotAllocator ReplGlobalAllocator { get; } = new GlobalSlotAllocator();
    private int _debugThreadId = 1;
    private int[]? _lastDebugLinePerFrame;
    private int _loopCheckCounter;

    // Wire GlobExpandHandler once for the whole process so that process.exec can
    // apply glob expansion to unquoted StashLiteralArg tokens emitted by Phase B.
    static VirtualMachine()
    {
        Stash.Runtime.ShellExpansion.GlobExpandHandler = static (pattern, cwd) =>
            GlobExpander.Expand(pattern, cwd);
    }

    public VirtualMachine(Dictionary<string, StashValue>? globals = null, CancellationToken ct = default)
    {
        _stack = ArrayPool<StashValue>.Shared.Rent(DefaultStackSize);
        _frames = ArrayPool<CallFrame>.Shared.Rent(DefaultFrameDepth);
        _globals = globals ?? new Dictionary<string, StashValue>();
        _openUpvalues = new List<Upvalue>();
        _ct = ct;
        _context = new VMContext(ct)
        {
            Globals = _globals,
            ActiveVM = this,
            MainThreadId = System.Threading.Thread.CurrentThread.ManagedThreadId,
            ModuleCache = ModuleCache,
            ModuleLocks = ModuleLocks,
            // Wire up the VM's import stack so InvokeCallbackDirect can snapshot it
            // when creating a cross-thread child VM (background-thread branch).
            ImportStack = _importStack,
            // Wire the root's SpawnedFutureRegistry into the context so Fork() and
            // InvokeCallbackDirect's child-VM branch both inherit the same root registry.
            SpawnedFutures = SpawnedFutures,
        };
    }

    /// <summary>
    /// Gets or sets the cancellation token used by the VM's step-limit / loop-check guard.
    /// Setting this after VM creation allows per-call cancellation from the embedding host
    /// without constructing a new VM (the <c>Stash.Hosting</c> pattern).
    /// Updates both the VM's own <c>_ct</c> and <see cref="VMContext"/>'s token so that
    /// stdlib built-ins that await process I/O also see the updated token.
    /// </summary>
    public CancellationToken CancellationToken
    {
        get => _ct;
        set
        {
            _ct = value;
            _context.SetCancellationToken(value);
        }
    }

    // ── IVMTypeRegistrar ──
    public void RegisterTypeName<T>(string vmTypeName) where T : class
    {
        _registeredTypeNames[typeof(T)] = vmTypeName;
        _context.TypeNameResolver = ResolveRegisteredTypeName;
    }

    /// <summary>
    /// Test-only helper: sets <c>MainThreadId</c> to -1 so that no real thread ID ever
    /// matches, forcing <c>VMContext.InvokeCallbackDirect</c> to always take the
    /// background-thread (child-VM fork) branch regardless of which thread fires the callback.
    /// This lets regression tests exercise the cross-thread path without needing a real
    /// background-thread caller.
    /// </summary>
    internal void TestForceBackgroundBranch()
    {
        _context.MainThreadId = -1;
    }

    /// <summary>
    /// When true, the VM is running as an async child (spawned via task.run or async fn).
    /// In this mode, an <see cref="OperationCanceledException"/> is propagated directly
    /// (rather than converted to <see cref="Stash.Runtime.Errors.CancellationError"/>) so
    /// the enclosing .NET Task transitions to the Canceled state instead of Faulted.
    /// The main VM always leaves this false, preserving <c>CancellationError</c> for the
    /// host (Ctrl-C / event.loop cancellation).
    /// </summary>
    internal bool IsAsyncChild { get; set; }


    public void RegisterTypeCheck(string vmTypeName, Func<object, bool> predicate)
    {
        _registeredTypeChecks[vmTypeName] = predicate;
    }

    /// <summary>The global variable store. Populate before Execute for built-in namespaces.</summary>
    public Dictionary<string, StashValue> Globals => _globals;

    /// <summary>
    /// The interpreter context for this VM instance. Used by components outside the bytecode
    /// layer (e.g. PromptRenderer) that need to invoke callbacks or query VM state.
    /// </summary>
    public IInterpreterContext Context => _context;

    /// <summary>
    /// Returns true if the REPL global symbol table contains the given name.
    /// Used by ShellLineClassifier to determine whether a bare identifier is a declared symbol.
    /// </summary>
    public bool HasReplGlobal(string name) => _globals.ContainsKey(name);

    /// <summary>
    /// Enumerates all global variables in the VM along with their current value and
    /// whether the binding is const. Useful for completion and introspection.
    /// </summary>
    public IEnumerable<(string Name, StashValue Value, bool IsConst)> EnumerateGlobals()
    {
        foreach (var kv in _globals)
        {
            yield return (kv.Key, kv.Value, _constGlobals.Contains(kv.Key));
        }
    }

    /// <summary>
    /// Exit code of the last shell-mode passthrough command.
    /// Updated by ShellRunner after each bare-command execution.
    /// </summary>
    public int LastExitCode { get; set; }

    /// <summary>
    /// Registry of user-defined aliases for this VM session.
    /// Shell-mode components and the <c>alias</c> namespace built-ins read and write through
    /// this registry. Each VM instance owns its own independent registry.
    /// </summary>
    public AliasRegistry AliasRegistry { get; } = new();

    /// <summary>
    /// Standard output stream for built-in functions.
    /// When <see cref="EmbeddedMode"/> is <c>false</c> (CLI default) the effective default is <see cref="Console.Out"/>.
    /// When <see cref="EmbeddedMode"/> is <c>true</c> the effective default is <see cref="TextWriter.Null"/> — no Console fallthrough.
    /// </summary>
    public TextWriter Output { get => _context.Output; set => _context.Output = value; }

    /// <summary>
    /// Standard error stream for built-in functions.
    /// When <see cref="EmbeddedMode"/> is <c>false</c> (CLI default) the effective default is <see cref="Console.Error"/>.
    /// When <see cref="EmbeddedMode"/> is <c>true</c> the effective default is <see cref="TextWriter.Null"/> — no Console fallthrough.
    /// </summary>
    public TextWriter ErrorOutput { get => _context.ErrorOutput; set => _context.ErrorOutput = value; }

    /// <summary>
    /// Standard input stream for built-in functions.
    /// When <see cref="EmbeddedMode"/> is <c>false</c> (CLI default) the effective default is <see cref="Console.In"/>.
    /// When <see cref="EmbeddedMode"/> is <c>true</c> the effective default is <see cref="TextReader.Null"/> — no Console fallthrough.
    /// </summary>
    public TextReader Input { get => _context.Input; set => _context.Input = value; }

    /// <summary>Extension method registry for extend blocks on built-in types.</summary>
    internal ExtensionRegistry Extensions => _extensionRegistry;

    /// <summary>
    /// Module loading callback. Receives (modulePath, currentFilePath) and returns a compiled Chunk.
    /// Must be set by the host before executing scripts that use import statements.
    /// </summary>
    public Func<string, string?, Chunk>? ModuleLoader
    {
        get => _moduleLoader;
        set
        {
            _moduleLoader = value;
            _context.ModuleLoader = value;
            _context.ModuleCache = ModuleCache;
            _context.ModuleLocks = ModuleLocks;
        }
    }

    /// <summary>Debugger hook interface. Set before Execute to enable debugging.</summary>
    public IDebugger? Debugger
    {
        get => _debugger;
        set
        {
            _debugger = value;
            _context.Debugger = value;
        }
    }

    /// <summary>
    /// When true, sys.exit() throws ExitException instead of terminating the process.
    /// Must be set by the host (e.g., StashEngine) for embedded scenarios.
    /// </summary>
    public bool EmbeddedMode
    {
        get => _context.EmbeddedMode;
        set => _context.EmbeddedMode = value;
    }

    /// <summary>Thread ID for debug hooks. Defaults to 1 (main thread).</summary>
    public int DebugThreadId
    {
        get => _debugThreadId;
        set => _debugThreadId = value;
    }

    /// <summary>Maximum number of operations before throwing StepLimitExceededException. 0 = unlimited.</summary>
    public long StepLimit { get; set; }

    /// <summary>Number of operations executed since the last Execute call.</summary>
    public long StepCount { get; private set; }

    /// <summary>Gets or sets the current file path, forwarded to VMContext for module resolution.</summary>
    public string? CurrentFile
    {
        get => _context.CurrentFile;
        set => _context.CurrentFile = value;
    }

    /// <summary>Script arguments accessible via the args namespace.</summary>
    public string[]? ScriptArgs
    {
        get => _context.ScriptArgs;
        set => _context.ScriptArgs = value;
    }

    /// <summary>TAP test harness for test-mode execution.</summary>
    public Stash.Runtime.ITestHarness? TestHarness
    {
        get => _context.TestHarness;
        set => _context.TestHarness = value;
    }

    /// <summary>Filter patterns for selective test execution.</summary>
    public string[]? TestFilter
    {
        get => _context.TestFilter;
        set => _context.TestFilter = value;
    }

    /// <summary>When true, the TAP framework runs in discovery mode (collect test names, don't execute).</summary>
    public bool DiscoveryMode
    {
        get => _context.DiscoveryMode;
        set => _context.DiscoveryMode = value;
    }

    /// <summary>Kills and disposes all processes spawned by the script.</summary>
    public void CleanupTrackedProcesses() => _context.CleanupTrackedProcesses();

    /// <summary>Disposes all file system watchers created by the script.</summary>
    public void CleanupTrackedWatchers() => _context.CleanupTrackedWatchers();

    /// <summary>Current call stack depth (for stepping).</summary>
    internal int FrameCount => _frameCount;

    /// <summary>The debug call stack — populated only when a debugger is attached.</summary>
    internal IReadOnlyList<DebugCallFrame> DebugCallStack => _debugCallStack;

    /// <summary>Execute a compiled chunk and return the result.</summary>
    public object? Execute(Chunk chunk)
    {
        _sp = 0;
        _frameCount = 0;
        StepCount = 0;
        _exceptionHandlers.Clear();
        _openUpvalues.Clear();
        if (_debugger is not null)
            _debugCallStack.Clear();
        PushFrame(chunk, baseSlot: 0, upvalues: null, name: chunk.Name);
        InitGlobalSlots(chunk);
        ValidateStdlibManifest(chunk);

        if (_debugger is not null)
        {
            return RunDebug();
        }

        return Run();
    }

    /// <summary>
    /// Executes a chunk in REPL mode. After execution, top-level local variables
    /// are promoted to globals so they persist across subsequent REPL inputs.
    /// </summary>
    public object? ExecuteRepl(Chunk chunk)
    {
        _sp = 0;
        _frameCount = 0;
        StepCount = 0;
        _exceptionHandlers.Clear();
        _openUpvalues.Clear();
        if (_debugger is not null)
            _debugCallStack.Clear();
        PushFrame(chunk, baseSlot: 0, upvalues: null, name: chunk.Name);
        InitGlobalSlots(chunk);
        ValidateStdlibManifest(chunk);

        object? result;
        if (_debugger is not null)
        {
            result = RunDebug();
        }
        else
        {
            result = Run();
        }

        return result;
    }

    /// <summary>
    /// Initializes the slot-based global variable array from the compiled chunk's name table
    /// and any pre-populated globals (built-in namespaces, imported values, etc.).
    /// </summary>
    internal void InitGlobalSlots(Chunk chunk)
    {
        string[]? nameTable = chunk.GlobalNameTable;
        if (nameTable == null) return;

        int slotCount = chunk.GlobalSlotCount;
        if (_globalSlots.Length < slotCount)
        {
            _globalSlots = new StashValue[slotCount];
            _constGlobalSlots = new bool[slotCount];
        }

        _globalNameTable = nameTable;

        // Populate slots from the existing _globals dictionary (built-in namespaces, etc.)
        for (int i = 0; i < slotCount; i++)
        {
            string name = nameTable[i];
            if (_globals.TryGetValue(name, out StashValue value))
            {
                _globalSlots[i] = value;
                if (_constGlobals.Contains(name))
                    _constGlobalSlots[i] = true;
            }
            else
            {
                _globalSlots[i] = UndefinedGlobal;
                _constGlobalSlots[i] = false;
            }
        }

        // OPT: Metadata-based const global initialization — pre-populate slots
        // from the constant pool without executing any bytecode instructions.
        if (chunk.ConstGlobalInits is { } inits)
        {
            for (int i = 0; i < inits.Length; i++)
            {
                var (slot, constIdx) = inits[i];
                StashValue val = chunk.Constants[constIdx];
                _globalSlots[slot] = val;
                _constGlobalSlots[slot] = true;
                string name = nameTable[slot];
                _globals[name] = val;
                _constGlobals.Add(name);
            }
        }
    }

    /// <summary>
    /// Re-reads globals from the shared <c>_globals</c> dictionary into <c>_globalSlots</c>.
    /// Called after off-thread callbacks (fs.watch, signal handlers) that may have modified
    /// globals via a child VM's write-through path.
    /// </summary>
    internal void RefreshGlobalSlots()
    {
        for (int i = 0; i < _globalNameTable.Length && i < _globalSlots.Length; i++)
        {
            if (_globals.TryGetValue(_globalNameTable[i], out StashValue value))
            {
                _globalSlots[i] = value;
            }
        }
    }

    /// <summary>
    /// Initialises this VM's <c>_importStack</c> from a snapshot produced at a cross-thread
    /// fork site. Called by <see cref="VMContext.InvokeCallbackDirect"/> (background-thread
    /// branch) so that a child VM starts with an independent copy of the parent's in-progress
    /// import set, not a reference to the parent's live set.
    ///
    /// <para>
    /// This is NOT called by the module-load path (<c>VirtualMachine.Modules.cs</c>), which
    /// deliberately keeps sharing the parent's <c>_importStack</c> by reference for synchronous
    /// circular-import detection.
    /// </para>
    /// </summary>
    /// <param name="snapshot">A pre-copied snapshot of the parent's import stack.</param>
    internal void InitImportStack(HashSet<string> snapshot)
    {
        _importStack = new HashSet<string>(snapshot, StringComparer.OrdinalIgnoreCase);
        // Keep the context's import-stack reference in sync so that InvokeCallbackDirect
        // on this child VM can also take a correct snapshot for any further nesting.
        _context.ImportStack = _importStack;
    }

    private void ValidateStdlibManifest(Chunk chunk)
    {
        if (chunk.StdlibManifest is not { } manifest)
            return;

        foreach (string ns in manifest.RequiredNamespaces)
        {
            if (!_globals.ContainsKey(ns))
                throw new RuntimeError(
                    $"Bytecode requires namespace '{ns}' but it is not available. " +
                    "Ensure the VM is configured with the required stdlib provider.");
        }

        foreach (string global in manifest.RequiredGlobals)
        {
            if (!_globals.ContainsKey(global))
                throw new RuntimeError(
                    $"Bytecode requires global '{global}' but it is not available.");
        }
    }

    // ---- Frame Management ----

    private void PushFrame(Chunk chunk, int baseSlot, Upvalue[]? upvalues, string? name, Dictionary<string, StashValue>? moduleGlobals = null)
    {
        if (_frameCount >= _frames.Length)
        {
            int newSize = _frames.Length * 2;
            CallFrame[] newFrames = ArrayPool<CallFrame>.Shared.Rent(newSize);
            _frames.AsSpan(0, _frameCount).CopyTo(newFrames);

            if (_frames.Length > 0)
                ArrayPool<CallFrame>.Shared.Return(_frames, clearArray: true);

            _frames = newFrames;
        }

        ref CallFrame frame = ref _frames[_frameCount++];
        frame.Chunk = chunk;
        frame.IP = 0;
        frame.BaseSlot = baseSlot;
        frame.Upvalues = upvalues;
        frame.FunctionName = name;
        frame.ModuleGlobals = moduleGlobals;
        frame.Defers = null;
        frame.ActiveIterators = null;
        frame.ICSlots = ResolveICSlots(chunk);

        // Ensure the shared stack has room for this frame's entire register window.
        int needed = baseSlot + chunk.MaxRegs;
        while (needed >= _stack.Length)
            GrowStack();
        if (needed > _sp)
            _sp = needed;
    }

    // ---- Per-VM IC Slot Management ----

    /// <summary>
    /// Returns this VM's private <see cref="ICSlot"/> array for <paramref name="chunk"/>,
    /// cloning the chunk's immutable template on first access so that no two VM instances
    /// ever write to the same IC array.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private ICSlot[]? ResolveICSlots(Chunk chunk)
    {
        ICSlot[]? template = chunk.ICSlots;
        if (template is null || template.Length == 0)
            return null;

        if (!_vmICSlots.TryGetValue(chunk, out ICSlot[]? slots))
        {
            // Clone preserves ConstantIndex (pre-filled by ChunkBuilder) but gives
            // fresh zero State / null Guard so this VM's IC starts uninitialized.
            slots = (ICSlot[])template.Clone();
            _vmICSlots[chunk] = slots;
        }
        return slots;
    }

    /// <summary>
    /// Returns this VM's private IC slot array for the given chunk, or null if the chunk
    /// has no IC slots or has not yet been executed by this VM.
    /// Used only by unit tests to observe per-VM IC state.
    /// </summary>
    internal ICSlot[]? GetICSlotsForChunk(Chunk chunk)
        => _vmICSlots.TryGetValue(chunk, out ICSlot[]? slots) ? slots : null;

    // ---- Stack Operations ----

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void Push(StashValue value)
    {
        if (_sp >= _stack.Length)
        {
            GrowStack();
        }

        _stack[_sp++] = value;
    }

    private void GrowStack()
    {
        int newSize = _stack.Length * 2;
        StashValue[] newStack = ArrayPool<StashValue>.Shared.Rent(newSize);
        _stack.AsSpan(0, _sp).CopyTo(newStack);

        if (_stack.Length > 0)
            ArrayPool<StashValue>.Shared.Return(_stack, clearArray: true);

        _stack = newStack;
        foreach (Upvalue uv in _openUpvalues)
        {
            uv.UpdateStack(_stack);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private StashValue Pop() => _stack[--_sp];

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private ref StashValue Peek() => ref _stack[_sp - 1];

    // ---- Instruction Helpers ----

    private SourceSpan? GetCurrentSpan(ref CallFrame frame) =>
        frame.Chunk.SourceMap.GetSpan(frame.IP > 0 ? frame.IP - 1 : 0);

}
