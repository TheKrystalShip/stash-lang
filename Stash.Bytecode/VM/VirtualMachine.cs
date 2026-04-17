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

    private Func<string, string?, Chunk>? _moduleLoader;
    internal ConcurrentDictionary<string, Dictionary<string, StashValue>> ModuleCache = new(StringComparer.OrdinalIgnoreCase);
    private HashSet<string> _importStack = new(StringComparer.OrdinalIgnoreCase);
    internal ConcurrentDictionary<string, object> ModuleLocks = new(StringComparer.OrdinalIgnoreCase);

    // ── Debugger Integration ──
    private IDebugger? _debugger;
    private readonly List<DebugCallFrame> _debugCallStack = new();
    private int _debugThreadId = 1;
    private int[]? _lastDebugLinePerFrame;
    private int _loopCheckCounter;

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
            ModuleLocks = ModuleLocks
        };
    }

    // ── IVMTypeRegistrar ──
    public void RegisterTypeName<T>(string vmTypeName) where T : class
    {
        _registeredTypeNames[typeof(T)] = vmTypeName;
        _context.TypeNameResolver = ResolveRegisteredTypeName;
    }

    public void RegisterTypeCheck(string vmTypeName, Func<object, bool> predicate)
    {
        _registeredTypeChecks[vmTypeName] = predicate;
    }

    /// <summary>The global variable store. Populate before Execute for built-in namespaces.</summary>
    public Dictionary<string, StashValue> Globals => _globals;

    /// <summary>Standard output stream for built-in functions. Defaults to Console.Out.</summary>
    public TextWriter Output { get => _context.Output; set => _context.Output = value; }

    /// <summary>Standard error stream for built-in functions. Defaults to Console.Error.</summary>
    public TextWriter ErrorOutput { get => _context.ErrorOutput; set => _context.ErrorOutput = value; }

    /// <summary>Standard input stream for built-in functions. Defaults to Console.In.</summary>
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

        // Promote top-level locals to globals for next REPL input
        if (chunk.LocalNames is not null)
        {
            for (int i = 0; i < chunk.LocalNames.Length; i++)
            {
                string? name = chunk.LocalNames[i];
                if (!string.IsNullOrEmpty(name) && name[0] != '<')
                {
                    _globals[name] = _stack[i];
                }
            }
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

        // Ensure the shared stack has room for this frame's entire register window.
        int needed = baseSlot + chunk.MaxRegs;
        while (needed >= _stack.Length)
            GrowStack();
        if (needed > _sp)
            _sp = needed;
    }

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

    // ---- Upvalue Management ----

    private Upvalue CaptureUpvalue(int stackIndex)
    {
        for (int i = 0; i < _openUpvalues.Count; i++)
        {
            Upvalue existing = _openUpvalues[i];
            if (existing.StackIndex == stackIndex)
            {
                return existing;
            }
        }
        var upvalue = new Upvalue(_stack, stackIndex);
        // Insert sorted by descending StackIndex for efficient closing
        int insertIdx = 0;
        while (insertIdx < _openUpvalues.Count && _openUpvalues[insertIdx].StackIndex > stackIndex)
        {
            insertIdx++;
        }

        _openUpvalues.Insert(insertIdx, upvalue);
        return upvalue;
    }

    private void CloseUpvalues(int fromSlot)
    {
        if (_openUpvalues.Count == 0) return;
        for (int i = _openUpvalues.Count - 1; i >= 0; i--)
        {
            if (_openUpvalues[i].StackIndex >= fromSlot)
            {
                _openUpvalues[i].Close();
                _openUpvalues.RemoveAt(i);
            }
        }
    }

}
