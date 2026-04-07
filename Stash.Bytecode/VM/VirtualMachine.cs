using System;
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
using Stash.Runtime.Types;
using DebugCallFrame = Stash.Debugging.CallFrame;

namespace Stash.Bytecode;

/// <summary>
/// Core fields, constructor, properties, stack operations, and execution entry points.
/// </summary>
public sealed partial class VirtualMachine
{
    private const int DefaultStackSize = 1024;
    private const int DefaultFrameDepth = 256;

    /// <summary>
    /// Sentinel value used to mark function parameters that were not provided by the caller.
    /// The compiler emits a prologue that checks for this sentinel and evaluates default expressions.
    /// </summary>
    public static readonly object NotProvided = new();

    /// <summary>Sentinel value pushed by OP_ArgMark to delimit spread call arguments.</summary>
    private static readonly object _argSentinel = new object();

    private StashValue[] _stack;
    private int _sp; // stack pointer: index of next free slot

    private CallFrame[] _frames;
    private int _frameCount;

    private readonly Dictionary<string, object?> _globals;
    private readonly HashSet<string> _constGlobals = new(StringComparer.Ordinal);
    private readonly List<Upvalue> _openUpvalues;
    private readonly CancellationToken _ct;

    private readonly List<ExceptionHandler> _exceptionHandlers = new();
    private readonly VMContext _context;
    private readonly ExtensionRegistry _extensionRegistry = new();

    private Func<string, string?, Chunk>? _moduleLoader;
    internal ConcurrentDictionary<string, Dictionary<string, object?>> ModuleCache = new(StringComparer.OrdinalIgnoreCase);
    private HashSet<string> _importStack = new(StringComparer.OrdinalIgnoreCase);
    internal ConcurrentDictionary<string, object> ModuleLocks = new(StringComparer.OrdinalIgnoreCase);

    // ── Debugger Integration ──
    private IDebugger? _debugger;
    private readonly List<DebugCallFrame> _debugCallStack = new();
    private int _debugThreadId = 1;

    public VirtualMachine(Dictionary<string, object?>? globals = null, CancellationToken ct = default)
    {
        _stack = new StashValue[DefaultStackSize];
        _frames = new CallFrame[DefaultFrameDepth];
        _globals = globals ?? new Dictionary<string, object?>();
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

    /// <summary>The global variable store. Populate before Execute for built-in namespaces.</summary>
    public Dictionary<string, object?> Globals => _globals;

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
        _debugCallStack.Clear();
        PushFrame(chunk, baseSlot: 0, upvalues: null, name: chunk.Name);

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
        _debugCallStack.Clear();
        PushFrame(chunk, baseSlot: 0, upvalues: null, name: chunk.Name);

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
                    _globals[name] = _stack[i].ToObject();
                }
            }
        }

        return result;
    }

    // ---- Frame Management ----

    private void PushFrame(Chunk chunk, int baseSlot, Upvalue[]? upvalues, string? name, Dictionary<string, object?>? moduleGlobals = null)
    {
        if (_frameCount >= _frames.Length)
        {
            Array.Resize(ref _frames, _frames.Length * 2);
        }

        ref CallFrame frame = ref _frames[_frameCount++];
        frame.Chunk = chunk;
        frame.IP = 0;
        frame.BaseSlot = baseSlot;
        frame.Upvalues = upvalues;
        frame.FunctionName = name;
        frame.ModuleGlobals = moduleGlobals;
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
        Array.Resize(ref _stack, _stack.Length * 2);
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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static byte ReadByte(ref CallFrame frame) => frame.Chunk.Code[frame.IP++];

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ushort ReadU16(ref CallFrame frame)
    {
        byte hi = frame.Chunk.Code[frame.IP++];
        byte lo = frame.Chunk.Code[frame.IP++];
        return (ushort)((hi << 8) | lo);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static short ReadI16(ref CallFrame frame) => (short)ReadU16(ref frame);

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
