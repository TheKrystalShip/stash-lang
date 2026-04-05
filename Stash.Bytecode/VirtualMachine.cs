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
/// Stack-based bytecode virtual machine for executing compiled Stash programs.
/// </summary>
public sealed class VirtualMachine
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
    internal ConcurrentDictionary<string, Dictionary<string, object?>> _moduleCache = new(StringComparer.OrdinalIgnoreCase);
    private HashSet<string> _importStack = new(StringComparer.OrdinalIgnoreCase);
    internal ConcurrentDictionary<string, object> _moduleLocks = new(StringComparer.OrdinalIgnoreCase);

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
        _context = new VMContext(ct);
        _context.Globals = _globals;
        _context.ActiveVM = this;
        _context.MainThreadId = System.Threading.Thread.CurrentThread.ManagedThreadId;
        _context.ModuleCache = _moduleCache;
        _context.ModuleLocks = _moduleLocks;
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
            _context.ModuleCache = _moduleCache;
            _context.ModuleLocks = _moduleLocks;
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

    private void PushFrame(Chunk chunk, int baseSlot, Upvalue[]? upvalues, string? name)
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

    // ---- Exception Handler Infrastructure ----

    private struct ExceptionHandler
    {
        public int CatchIP;
        public int StackLevel;
        public int FrameIndex;
    }

    // ------------------------------------------------------------------
    // Main execution loop — outer loop catches RuntimeError and routes
    // to the innermost registered exception handler if present.
    // ------------------------------------------------------------------

    private object? Run()
    {
        while (true)
        {
            try
            {
                return RunInner(0);
            }
            catch (RuntimeError ex) when (_exceptionHandlers.Count > 0)
            {
                ExceptionHandler handler = _exceptionHandlers[^1];
                _exceptionHandlers.RemoveAt(_exceptionHandlers.Count - 1);

                // Close any upvalues in the unwound stack region before restoring
                CloseUpvalues(handler.StackLevel);

                // Restore call stack and operand stack to the handler's save point
                _frameCount = handler.FrameIndex + 1;
                _sp = handler.StackLevel;

                // Push a StashError value for the catch block to consume
                var stashError = new StashError(ex.Message, ex.ErrorType ?? "RuntimeError", null, ex.Properties);
                _context.LastError = stashError;
                Push(StashValue.FromObj(stashError));

                // Resume execution at the catch handler's bytecode offset
                _frames[_frameCount - 1].IP = handler.CatchIP;
            }
        }
    }

    private object? RunDebug()
    {
        IDebugger debugger = _debugger!;

        while (true)
        {
            try
            {
                return RunInner(0);
            }
            catch (RuntimeError ex) when (_exceptionHandlers.Count > 0)
            {
                ExceptionHandler handler = _exceptionHandlers[^1];
                _exceptionHandlers.RemoveAt(_exceptionHandlers.Count - 1);
                CloseUpvalues(handler.StackLevel);
                _frameCount = handler.FrameIndex + 1;
                while (_debugCallStack.Count > handler.FrameIndex)
                {
                    _debugCallStack.RemoveAt(_debugCallStack.Count - 1);
                }

                _sp = handler.StackLevel;
                var stashError = new StashError(ex.Message, ex.ErrorType ?? "RuntimeError", null, ex.Properties);
                _context.LastError = stashError;
                Push(StashValue.FromObj(stashError));
                _frames[_frameCount - 1].IP = handler.CatchIP;
            }
            catch (RuntimeError ex)
            {
                // Uncaught error — notify debugger
                if (debugger.ShouldBreakOnException(ex))
                {
                    SourceSpan? span = (_frameCount > 0) ? GetCurrentSpan(ref _frames[_frameCount - 1]) : null;
                    if (span is not null)
                    {
                        IDebugScope scope = (_frameCount > 0)
                            ? BuildFrameScope(ref _frames[_frameCount - 1])
                            : BuildGlobalScope();
                        debugger.OnBeforeExecute(span, scope, _debugThreadId);
                    }
                }
                debugger.OnError(ex, _debugCallStack, _debugThreadId);
                throw;
            }
        }
    }

    private object? RunInner(int targetFrameCount = 0)
    {
        IDebugger? debugger = _debugger;
        // Per-frame last-debug-line tracking prevents re-triggering a breakpoint
        // at line N in a caller frame after returning from a callee that also ended
        // at line N (or any different line), which would otherwise happen because the
        // single lastDebugLine variable crosses frame boundaries.
        int[] lastDebugLinePerFrame = new int[DefaultFrameDepth];
        Array.Fill(lastDebugLinePerFrame, -1);

        while (true)
        {
            ref CallFrame frame = ref _frames[_frameCount - 1];
            byte instruction = frame.Chunk.Code[frame.IP++];

            // ── Debug hook: check for breakpoints/stepping at statement boundaries ──
            if (debugger is not null)
            {
                SourceSpan? span = frame.Chunk.SourceMap.GetSpan(frame.IP - 1);
                if (span is not null)
                {
                    int curLine = span.StartLine;
                    int frameIdx = _frameCount - 1;
                    if (frameIdx >= lastDebugLinePerFrame.Length)
                    {
                        Array.Resize(ref lastDebugLinePerFrame, _frames.Length);
                    }

                    if (curLine != lastDebugLinePerFrame[frameIdx] || debugger.IsPauseRequested)
                    {
                        lastDebugLinePerFrame[frameIdx] = curLine;
                        _context.CurrentSpan = span;
                        IDebugScope scope = BuildFrameScope(ref frame);
                        debugger.OnBeforeExecute(span, scope, _debugThreadId);
                        // Re-acquire frame ref after debugger pause
                        frame = ref _frames[_frameCount - 1];
                    }
                }
            }

            switch ((OpCode)instruction)
            {
                // ==================== Constants & Literals ====================
                case OpCode.Const:
                {
                    ushort idx = ReadU16(ref frame);
                    Push(frame.Chunk.Constants[idx]);
                    break;
                }

                case OpCode.Null:
                    Push(StashValue.Null);
                    break;

                case OpCode.True:
                    Push(StashValue.True);
                    break;

                case OpCode.False:
                    Push(StashValue.False);
                    break;

                // ==================== Stack Manipulation ====================
                case OpCode.Pop:
                    _sp--;
                    break;

                case OpCode.Dup:
                    Push(_stack[_sp - 1]);
                    break;

                // ==================== Variable Access ====================
                case OpCode.LoadLocal:
                {
                    byte slot = ReadByte(ref frame);
                    Push(_stack[frame.BaseSlot + slot]);
                    break;
                }

                case OpCode.StoreLocal:
                {
                    byte slot = ReadByte(ref frame);
                    _stack[frame.BaseSlot + slot] = Pop();
                    break;
                }

                case OpCode.LoadGlobal:
                {
                    ushort nameIdx = ReadU16(ref frame);
                    string name = (string)frame.Chunk.Constants[nameIdx].AsObj!;
                    if (!_globals.TryGetValue(name, out object? value))
                        {
                            throw new RuntimeError($"Undefined variable '{name}'.", GetCurrentSpan(ref frame));
                        }

                        Push(StashValue.FromObject(value));
                    break;
                }

                case OpCode.StoreGlobal:
                {
                    ushort nameIdx = ReadU16(ref frame);
                    string name = (string)frame.Chunk.Constants[nameIdx].AsObj!;
                    if (_constGlobals.Contains(name))
                        {
                            throw new RuntimeError("Assignment to constant variable.", GetCurrentSpan(ref frame));
                        }

                        _globals[name] = Pop().ToObject();
                    break;
                }

                case OpCode.InitConstGlobal:
                {
                    ushort nameIdx = ReadU16(ref frame);
                    string name = (string)frame.Chunk.Constants[nameIdx].AsObj!;
                    _globals[name] = Pop().ToObject();
                    _constGlobals.Add(name);
                    break;
                }

                case OpCode.LoadUpvalue:
                {
                    byte idx = ReadByte(ref frame);
                    Push(frame.Upvalues![idx].Value);
                    break;
                }

                case OpCode.StoreUpvalue:
                {
                    byte idx = ReadByte(ref frame);
                    frame.Upvalues![idx].Value = Pop();
                    break;
                }

                // ==================== Arithmetic ====================
                case OpCode.Add:
                {
                    StashValue b = Pop();
                    StashValue a = Pop();
                    if (a.IsInt && b.IsInt)
                        {
                            Push(StashValue.FromInt(a.AsInt + b.AsInt));
                        }
                        else
                        {
                            Push(RuntimeOps.Add(a, b, GetCurrentSpan(ref frame)));
                        }

                        break;
                }

                case OpCode.Subtract:
                {
                    StashValue b = Pop();
                    StashValue a = Pop();
                    if (a.IsInt && b.IsInt)
                        {
                            Push(StashValue.FromInt(a.AsInt - b.AsInt));
                        }
                        else
                        {
                            Push(RuntimeOps.Subtract(a, b, GetCurrentSpan(ref frame)));
                        }

                        break;
                }

                case OpCode.Multiply:
                {
                    StashValue b = Pop();
                    StashValue a = Pop();
                    if (a.IsInt && b.IsInt)
                        {
                            Push(StashValue.FromInt(a.AsInt * b.AsInt));
                        }
                        else
                        {
                            Push(RuntimeOps.Multiply(a, b, GetCurrentSpan(ref frame)));
                        }

                        break;
                }

                case OpCode.Divide:
                {
                    StashValue b = Pop();
                    StashValue a = Pop();
                    Push(RuntimeOps.Divide(a, b, GetCurrentSpan(ref frame)));
                    break;
                }

                case OpCode.Modulo:
                {
                    StashValue b = Pop();
                    StashValue a = Pop();
                    Push(RuntimeOps.Modulo(a, b, GetCurrentSpan(ref frame)));
                    break;
                }

                case OpCode.Negate:
                {
                    StashValue val = Pop();
                    if (val.IsInt)
                        {
                            Push(StashValue.FromInt(-val.AsInt));
                        }
                        else
                        {
                            Push(RuntimeOps.Negate(val, GetCurrentSpan(ref frame)));
                        }

                        break;
                }

                // ==================== Bitwise ====================
                case OpCode.BitAnd:
                {
                    StashValue b = Pop(), a = Pop();
                    if (a.IsInt && b.IsInt)
                        {
                            Push(StashValue.FromInt(a.AsInt & b.AsInt));
                        }
                        else
                        {
                            Push(RuntimeOps.BitAnd(a, b, GetCurrentSpan(ref frame)));
                        }

                        break;
                }

                case OpCode.BitOr:
                {
                    StashValue b = Pop(), a = Pop();
                    if (a.IsInt && b.IsInt)
                        {
                            Push(StashValue.FromInt(a.AsInt | b.AsInt));
                        }
                        else
                        {
                            Push(RuntimeOps.BitOr(a, b, GetCurrentSpan(ref frame)));
                        }

                        break;
                }

                case OpCode.BitXor:
                {
                    StashValue b = Pop(), a = Pop();
                    if (a.IsInt && b.IsInt)
                        {
                            Push(StashValue.FromInt(a.AsInt ^ b.AsInt));
                        }
                        else
                        {
                            Push(RuntimeOps.BitXor(a, b, GetCurrentSpan(ref frame)));
                        }

                        break;
                }

                case OpCode.BitNot:
                    Push(RuntimeOps.BitNot(Pop(), GetCurrentSpan(ref frame)));
                    break;

                case OpCode.ShiftLeft:
                {
                    StashValue b = Pop(), a = Pop();
                    Push(RuntimeOps.ShiftLeft(a, b, GetCurrentSpan(ref frame)));
                    break;
                }

                case OpCode.ShiftRight:
                {
                    StashValue b = Pop(), a = Pop();
                    Push(RuntimeOps.ShiftRight(a, b, GetCurrentSpan(ref frame)));
                    break;
                }

                // ==================== Comparison ====================
                case OpCode.Equal:
                {
                    StashValue b = Pop(), a = Pop();
                    Push(StashValue.FromBool(RuntimeOps.IsEqual(a, b)));
                    break;
                }

                case OpCode.NotEqual:
                {
                    StashValue b = Pop(), a = Pop();
                    Push(StashValue.FromBool(!RuntimeOps.IsEqual(a, b)));
                    break;
                }

                case OpCode.LessThan:
                {
                    StashValue b = Pop(), a = Pop();
                    Push(StashValue.FromBool(RuntimeOps.LessThan(a, b, GetCurrentSpan(ref frame))));
                    break;
                }

                case OpCode.LessEqual:
                {
                    StashValue b = Pop(), a = Pop();
                    Push(StashValue.FromBool(RuntimeOps.LessEqual(a, b, GetCurrentSpan(ref frame))));
                    break;
                }

                case OpCode.GreaterThan:
                {
                    StashValue b = Pop(), a = Pop();
                    Push(StashValue.FromBool(RuntimeOps.GreaterThan(a, b, GetCurrentSpan(ref frame))));
                    break;
                }

                case OpCode.GreaterEqual:
                {
                    StashValue b = Pop(), a = Pop();
                    Push(StashValue.FromBool(RuntimeOps.GreaterEqual(a, b, GetCurrentSpan(ref frame))));
                    break;
                }

                // ==================== Logic ====================
                case OpCode.Not:
                    Push(StashValue.FromBool(RuntimeOps.IsFalsy(Pop())));
                    break;

                case OpCode.And:
                {
                    // Short-circuit AND: if top is falsy, keep it and jump (skip right); else pop and eval right
                    short offset = ReadI16(ref frame);
                    if (RuntimeOps.IsFalsy(Peek()))
                        {
                            frame.IP += offset;
                        }
                        else
                        {
                            _sp--; // pop truthy left, continue to right operand
                        }

                        break;
                }

                case OpCode.Or:
                {
                    // Short-circuit OR: if top is truthy, keep it and jump (skip right); else pop and eval right
                    short offset = ReadI16(ref frame);
                    if (!RuntimeOps.IsFalsy(Peek()))
                        {
                            frame.IP += offset;
                        }
                        else
                        {
                            _sp--; // pop falsy left, continue to right operand
                        }

                        break;
                }

                case OpCode.NullCoalesce:
                {
                    // If top is non-null, keep it and jump; else pop null and eval right
                    short offset = ReadI16(ref frame);
                    if (!Peek().IsNull && Peek().ToObject() is not StashError)
                        {
                            frame.IP += offset;
                        }
                        else
                        {
                            _sp--; // pop null, continue to right operand
                        }

                        break;
                }

                // ==================== Control Flow ====================
                case OpCode.Jump:
                {
                    short offset = ReadI16(ref frame);
                    frame.IP += offset;
                    break;
                }

                case OpCode.JumpTrue:
                {
                    short offset = ReadI16(ref frame);
                    if (!RuntimeOps.IsFalsy(Pop()))
                        {
                            frame.IP += offset;
                        }

                        break;
                }

                case OpCode.JumpFalse:
                {
                    short offset = ReadI16(ref frame);
                    if (RuntimeOps.IsFalsy(Pop()))
                        {
                            frame.IP += offset;
                        }

                        break;
                }

                case OpCode.Loop:
                {
                    ushort offset = ReadU16(ref frame);
                    frame.IP -= offset;
                    if (_ct.IsCancellationRequested)
                        {
                            throw new OperationCanceledException(_ct);
                        }

                        if (StepLimit > 0 && ++StepCount >= StepLimit)
                        {
                            throw new Stash.Runtime.StepLimitExceededException(StepLimit);
                        }

                        if (debugger is not null && debugger.IsPauseRequested)
                        {
                            lastDebugLinePerFrame[_frameCount - 1] = -1; // Force debug check on next iteration
                        }

                        break;
                }

                // ==================== Functions ====================
                case OpCode.Call:
                {
                    byte argc = ReadByte(ref frame);
                    // Save span before potential frame array resize
                    SourceSpan? callSpan = GetCurrentSpan(ref frame);
                    object? callee = _stack[_sp - argc - 1].AsObj;  // Callees are always Obj-tagged
                    int prevFrameCount = _frameCount;
                    CallValue(callee, argc, callSpan);

                    if (StepLimit > 0 && ++StepCount >= StepLimit)
                        {
                            throw new Stash.Runtime.StepLimitExceededException(StepLimit);
                        }

                        // Debug: track function entry for VM function calls
                        if (debugger is not null && _frameCount > prevFrameCount)
                    {
                        ref CallFrame newFrame = ref _frames[_frameCount - 1];
                        IDebugScope scope = BuildFrameScope(ref newFrame);
                        string funcName = newFrame.FunctionName ?? "<anonymous>";

                        _debugCallStack.Add(new DebugCallFrame
                        {
                            FunctionName = funcName,
                            CallSite = callSpan!,
                            LocalScope = scope,
                        });

                        if (debugger.ShouldBreakOnFunctionEntry(funcName))
                            {
                                debugger.OnFunctionEnter(funcName, callSpan!, scope, _debugThreadId);
                            }
                        }
                    break;
                }

                case OpCode.ArgMark:
                {
                    Push(StashValue.FromObj(_argSentinel));
                    break;
                }

                case OpCode.CallSpread:
                {
                    // Scan backward from stack top to find ArgSentinel
                    int rawArgc = 0;
                    int sentinelIdx = -1;
                    for (int i = _sp - 1; i >= 0; i--)
                    {
                        if (ReferenceEquals(_stack[i].AsObj, _argSentinel))
                        {
                            sentinelIdx = i;
                            break;
                        }
                        rawArgc++;
                    }

                    if (sentinelIdx < 0)
                        {
                            throw new RuntimeError("Internal error: ArgMark sentinel not found.", GetCurrentSpan(ref frame));
                        }

                        // Callee is right below the sentinel
                        object? callee = _stack[sentinelIdx - 1].AsObj;
                    SourceSpan? callSpan = GetCurrentSpan(ref frame);

                    // Expand SpreadMarkers: collect all args, expanding spreads
                    var expandedArgs = new List<object?>(rawArgc);
                    for (int i = sentinelIdx + 1; i < _sp; i++)
                    {
                        object? argVal = _stack[i].ToObject();
                        if (argVal is SpreadMarker sm)
                        {
                            if (sm.Items is List<object?> spreadList)
                            {
                                expandedArgs.AddRange(spreadList);
                            }
                            else
                            {
                                throw new RuntimeError("Spread in function call requires an array.",
                                    callSpan);
                            }
                        }
                        else
                        {
                            expandedArgs.Add(argVal);
                        }
                    }

                    // Write expanded args back to stack starting at sentinelIdx
                    int expandedArgc = expandedArgs.Count;
                    // Ensure stack capacity
                    while (sentinelIdx + expandedArgc >= _stack.Length)
                    {
                        var bigger = new StashValue[_stack.Length * 2];
                        Array.Copy(_stack, bigger, _stack.Length);
                        _stack = bigger;
                    }
                    for (int i = 0; i < expandedArgc; i++)
                    {
                        _stack[sentinelIdx + i] = StashValue.FromObject(expandedArgs[i]);
                    }
                    _sp = sentinelIdx + expandedArgc;

                    int prevFrameCount = _frameCount;
                    CallValue(callee, expandedArgc, callSpan);

                    if (StepLimit > 0 && ++StepCount >= StepLimit)
                    {
                        throw new Stash.Runtime.StepLimitExceededException(StepLimit);
                    }

                    // Debug: track function entry (same as OP_CALL)
                    if (debugger is not null && _frameCount > prevFrameCount)
                    {
                        ref CallFrame newFrame = ref _frames[_frameCount - 1];
                        IDebugScope scope = BuildFrameScope(ref newFrame);
                        string funcName = newFrame.FunctionName ?? "<anonymous>";

                        _debugCallStack.Add(new DebugCallFrame
                        {
                            FunctionName = funcName,
                            CallSite = callSpan!,
                            LocalScope = scope,
                        });

                        if (debugger.ShouldBreakOnFunctionEntry(funcName))
                            {
                                debugger.OnFunctionEnter(funcName, callSpan!, scope, _debugThreadId);
                            }
                        }
                    break;
                }

                case OpCode.Return:
                {
                    StashValue result = Pop();
                    int baseSlot = _frames[_frameCount - 1].BaseSlot;

                    // Debug: track function exit
                    if (debugger is not null && _debugCallStack.Count > 0)
                    {
                        string funcName = _frames[_frameCount - 1].FunctionName ?? "<anonymous>";
                        _debugCallStack.RemoveAt(_debugCallStack.Count - 1);
                        debugger.OnFunctionExit(funcName, _debugThreadId);
                    }

                    CloseUpvalues(baseSlot);
                    _frameCount--;
                    if (_frameCount == 0)
                    {
                        _sp = 0;
                        return result.ToObject();
                    }
                    _sp = baseSlot - 1; // discard function stack window + callee slot
                    Push(result);
                    if (_frameCount <= targetFrameCount)
                        {
                            return result.ToObject();
                        }

                        break;
                }

                case OpCode.Closure:
                {
                    ushort chunkIdx = ReadU16(ref frame);
                    Chunk fnChunk = (Chunk)frame.Chunk.Constants[chunkIdx].AsObj!;
                    var upvalues = new Upvalue[fnChunk.Upvalues.Length];
                    for (int i = 0; i < fnChunk.Upvalues.Length; i++)
                    {
                        byte isLocal = frame.Chunk.Code[frame.IP++];
                        byte index = frame.Chunk.Code[frame.IP++];
                        if (isLocal == 1)
                            {
                                upvalues[i] = CaptureUpvalue(frame.BaseSlot + index);
                            }
                            else
                            {
                                upvalues[i] = frame.Upvalues![index];
                            }
                        }
                    Push(StashValue.FromObj(new VMFunction(fnChunk, upvalues)));
                    break;
                }

                // ==================== Collections ====================
                case OpCode.Array:
                {
                    ushort count = ReadU16(ref frame);
                    var list = new List<object?>(count);
                    int start = _sp - count;
                    for (int i = start; i < _sp; i++)
                    {
                        object? val = _stack[i].ToObject();
                        if (val is SpreadMarker sm)
                        {
                            if (sm.Items is List<object?> spreadItems)
                            {
                                list.AddRange(spreadItems);
                            }
                            else
                            {
                                throw new RuntimeError("Spread operator requires an array.",
                                    GetCurrentSpan(ref frame));
                            }
                        }
                        else
                        {
                            list.Add(val);
                        }
                    }
                    _sp = start;
                    Push(StashValue.FromObj(list));
                    break;
                }

                case OpCode.Dict:
                {
                    ushort count = ReadU16(ref frame);
                    var dict = new StashDictionary();
                    int start = _sp - (count * 2);
                    for (int i = start; i < _sp; i += 2)
                    {
                        object? key = _stack[i].ToObject();
                        object? val = _stack[i + 1].ToObject();
                        if (val is SpreadMarker sm)
                        {
                            if (sm.Items is StashDictionary spreadDict)
                            {
                                foreach (KeyValuePair<object, object?> kv in spreadDict.RawEntries())
                                {
                                    dict.Set(kv.Key, kv.Value);
                                }
                            }
                            else if (sm.Items is StashInstance inst)
                            {
                                foreach (KeyValuePair<string, object?> kv in inst.GetFields())
                                {
                                    dict.Set(kv.Key, kv.Value);
                                }
                            }
                            else
                            {
                                throw new RuntimeError("Cannot spread non-dict value into dict literal.",
                                    GetCurrentSpan(ref frame));
                            }
                        }
                        else
                        {
                            dict.Set(key!, val);
                        }
                    }
                    _sp = start;
                    Push(StashValue.FromObj(dict));
                    break;
                }

                case OpCode.Range:
                {
                    StashValue step = Pop();
                    StashValue end = Pop();
                    StashValue start = Pop();
                    long s = start.IsInt ? start.AsInt
                        : throw new RuntimeError("Range start must be an integer.", GetCurrentSpan(ref frame));
                    long e = end.IsInt ? end.AsInt
                        : throw new RuntimeError("Range end must be an integer.", GetCurrentSpan(ref frame));
                    long st = step.IsInt ? step.AsInt : (s <= e ? 1L : -1L);
                    if (st == 0)
                        throw new RuntimeError("'range' step cannot be zero.", GetCurrentSpan(ref frame));
                    Push(StashValue.FromObj(new StashRange(s, e, st)));
                    break;
                }

                case OpCode.Spread:
                {
                    object? iterable = Pop().ToObject();
                    Push(StashValue.FromObj(new SpreadMarker(iterable!)));
                    break;
                }

                // ==================== Object Access ====================
                case OpCode.GetField:
                {
                    ushort nameIdx = ReadU16(ref frame);
                    string fieldName = (string)frame.Chunk.Constants[nameIdx].AsObj!;
                    SourceSpan? span = GetCurrentSpan(ref frame);
                    object? obj = Pop().ToObject();
                    Push(StashValue.FromObject(GetFieldValue(obj, fieldName, span)));
                    break;
                }

                case OpCode.SetField:
                {
                    ushort nameIdx = ReadU16(ref frame);
                    string fieldName = (string)frame.Chunk.Constants[nameIdx].AsObj!;
                    SourceSpan? span = GetCurrentSpan(ref frame);
                    object? value = Pop().ToObject();
                    object? obj = Pop().ToObject();
                    SetFieldValue(obj, fieldName, value, span);
                    Push(StashValue.FromObject(value));
                    break;
                }

                case OpCode.GetIndex:
                {
                    SourceSpan? span = GetCurrentSpan(ref frame);
                    object? index = Pop().ToObject();
                    object? obj = Pop().ToObject();
                    Push(StashValue.FromObject(GetIndexValue(obj, index, span)));
                    break;
                }

                case OpCode.SetIndex:
                {
                    SourceSpan? span = GetCurrentSpan(ref frame);
                    object? value = Pop().ToObject();
                    object? index = Pop().ToObject();
                    object? obj = Pop().ToObject();
                    SetIndexValue(obj, index, value, span);
                    Push(StashValue.FromObject(value));
                    break;
                }

                case OpCode.StructInit:
                {
                    ushort fieldCount = ReadU16(ref frame);
                    SourceSpan? span = GetCurrentSpan(ref frame);
                    // Stack layout: [structDef][name0][val0][name1][val1]...
                    var providedFields = new Dictionary<string, object?>(fieldCount);
                    int pairStart = _sp - (fieldCount * 2);
                    for (int i = pairStart; i < _sp; i += 2)
                    {
                        string fname = (string)_stack[i].AsObj!;
                        if (providedFields.ContainsKey(fname))
                            throw new RuntimeError($"Duplicate field '{fname}' in struct initialization.", span);
                        providedFields[fname] = _stack[i + 1].ToObject();
                    }
                    _sp = pairStart;
                    object? structDef = Pop().ToObject();
                    if (structDef is StashStruct ss)
                    {
                        // Initialize all declared fields to null, then override with provided values
                        var allFields = new Dictionary<string, object?>(ss.Fields.Count);
                        foreach (string f in ss.Fields)
                            {
                                allFields[f] = null;
                            }

                            foreach (KeyValuePair<string, object?> kvp in providedFields)
                        {
                            if (!allFields.ContainsKey(kvp.Key))
                                {
                                    throw new RuntimeError($"Unknown field '{kvp.Key}' for struct '{ss.Name}'.", span);
                                }

                                allFields[kvp.Key] = kvp.Value;
                        }

                        Push(StashValue.FromObj(new StashInstance(ss.Name, ss, allFields)));
                    }
                    else
                        {
                            throw new RuntimeError("Not a struct type.", span);
                        }

                        break;
                }

                // ==================== Strings ====================
                case OpCode.Interpolate:
                {
                    ushort count = ReadU16(ref frame);
                    string result = RuntimeOps.Interpolate(_stack, _sp, count);
                    _sp -= count;
                    Push(StashValue.FromObj(result));
                    break;
                }

                // ==================== Type Operations ====================
                case OpCode.Is:
                {
                    ushort typeIdx = ReadU16(ref frame);
                    if (typeIdx == 0xFFFF)
                    {
                        // Dynamic type check: type expression is on the stack
                        object? typeObj = Pop().ToObject();
                        object? value = Pop().ToObject();
                        bool result = typeObj switch
                        {
                            StashStruct sd => value is StashInstance inst && inst.TypeName == sd.Name,
                            StashEnum se => value is StashEnumValue ev && ev.TypeName == se.Name,
                            StashInterface si => value is StashInstance inst2 &&
                                InstanceImplementsInterfaceName(inst2, si.Name),
                            _ => throw new RuntimeError(
                                $"Right-hand side of 'is' must be a type, got {RuntimeValues.Stringify(typeObj)}.",
                                GetCurrentSpan(ref frame)),
                        };
                        Push(StashValue.FromBool(result));
                    }
                    else
                    {
                        string typeName = (string)frame.Chunk.Constants[typeIdx].AsObj!;
                        object? value = Pop().ToObject();
                        // Check globals for a variable holding a type definition (e.g. `let t = Foo; x is t`)
                        if (_globals.TryGetValue(typeName, out object? globalType) &&
                            globalType is StashStruct or StashEnum or StashInterface)
                        {
                            bool r = globalType switch
                            {
                                StashStruct sd => value is StashInstance inst && inst.TypeName == sd.Name,
                                StashEnum se => value is StashEnumValue ev && ev.TypeName == se.Name,
                                StashInterface si => value is StashInstance inst2 &&
                                    InstanceImplementsInterfaceName(inst2, si.Name),
                                _ => false,
                            };
                            Push(StashValue.FromBool(r));
                        }
                        else
                        {
                            Push(StashValue.FromBool(CheckIsType(value, typeName)));
                        }
                    }
                    break;
                }

                case OpCode.StructDecl:
                {
                    ushort metaIdx = ReadU16(ref frame);
                    SourceSpan? span = GetCurrentSpan(ref frame);
                    var metadata = (StructMetadata)frame.Chunk.Constants[metaIdx].AsObj!;

                    // Pop method closures from stack (pushed in order, so pop in reverse)
                    var methods = new Dictionary<string, IStashCallable>(metadata.MethodNames.Length);
                    for (int i = metadata.MethodNames.Length - 1; i >= 0; i--)
                    {
                        object? methodObj = Pop().ToObject();
                        if (methodObj is VMFunction vmFunc)
                            {
                                methods[metadata.MethodNames[i]] = vmFunc;
                            }
                            else
                            {
                                throw new RuntimeError($"Expected function for method '{metadata.MethodNames[i]}'.", span);
                            }
                        }

                    var fieldList = new List<string>(metadata.Fields);
                    var structDef = new StashStruct(metadata.Name, fieldList, methods);

                    // Resolve and validate interfaces
                    foreach (string ifaceName in metadata.InterfaceNames)
                    {
                        if (!_globals.TryGetValue(ifaceName, out object? resolved) || resolved is not StashInterface iface)
                            {
                                throw new RuntimeError($"'{ifaceName}' is not an interface.", span);
                            }

                            foreach (InterfaceField reqField in iface.RequiredFields)
                        {
                            if (!fieldList.Contains(reqField.Name))
                                {
                                    throw new RuntimeError(
                                    $"Struct '{metadata.Name}' does not implement interface '{ifaceName}': missing field '{reqField.Name}'.",
                                    span);
                                }
                            }

                        foreach (InterfaceMethod reqMethod in iface.RequiredMethods)
                        {
                            if (!methods.ContainsKey(reqMethod.Name))
                                {
                                    throw new RuntimeError(
                                    $"Struct '{metadata.Name}' does not implement interface '{ifaceName}': missing method '{reqMethod.Name}'.",
                                    span);
                                }

                            // Arity check: normalize both sides to exclude 'self'
                            // Interface Arity = sig.Parameters.Count (includes 'self' if explicitly declared)
                            // Struct method Chunk.Arity always includes synthetic 'self' as first param
                            int reqUserArity = reqMethod.ParameterNames.Contains("self") ? reqMethod.Arity - 1 : reqMethod.Arity;
                            if (methods[reqMethod.Name] is VMFunction vmMethod)
                            {
                                int implUserArity = vmMethod.Chunk.Arity - 1;
                                if (implUserArity != reqUserArity)
                                    {
                                        throw new RuntimeError(
                                        $"Struct '{metadata.Name}' implements interface '{ifaceName}': method '{reqMethod.Name}' has wrong number of parameters (expected {reqUserArity}, got {implUserArity}).",
                                        span);
                                    }
                                }
                            }

                        structDef.Interfaces.Add(iface);
                    }

                    Push(StashValue.FromObj(structDef));
                    break;
                }

                case OpCode.EnumDecl:
                {
                    ushort metaIdx = ReadU16(ref frame);
                    var metadata = (EnumMetadata)frame.Chunk.Constants[metaIdx].AsObj!;

                    var members = new List<string>(metadata.Members);
                    var enumDef = new StashEnum(metadata.Name, members);
                    Push(StashValue.FromObj(enumDef));
                    break;
                }

                case OpCode.InterfaceDecl:
                {
                    ushort metaIdx = ReadU16(ref frame);
                    var metadata = (InterfaceMetadata)frame.Chunk.Constants[metaIdx].AsObj!;

                    var requiredFields = new List<InterfaceField>(metadata.Fields);
                    var requiredMethods = new List<InterfaceMethod>(metadata.Methods);
                    var interfaceDef = new StashInterface(metadata.Name, requiredFields, requiredMethods);
                    Push(StashValue.FromObj(interfaceDef));
                    break;
                }

                case OpCode.Extend:
                {
                    ushort metaIdx = ReadU16(ref frame);
                    SourceSpan? span = GetCurrentSpan(ref frame);
                    var metadata = (ExtendMetadata)frame.Chunk.Constants[metaIdx].AsObj!;

                    // Pop method closures (pushed in order, so pop in reverse)
                    var methodFuncs = new IStashCallable[metadata.MethodNames.Length];
                    for (int i = metadata.MethodNames.Length - 1; i >= 0; i--)
                    {
                        object? methodObj = Pop().ToObject();
                        if (methodObj is not VMFunction vmFunc)
                            {
                                throw new RuntimeError($"Expected function for extension method '{metadata.MethodNames[i]}'.", span);
                            }

                            methodFuncs[i] = vmFunc;
                    }

                    if (metadata.IsBuiltIn)
                    {
                        for (int i = 0; i < metadata.MethodNames.Length; i++)
                            {
                                _extensionRegistry.Register(metadata.TypeName, metadata.MethodNames[i], methodFuncs[i]);
                            }
                        }
                    else
                    {
                        if (!_globals.TryGetValue(metadata.TypeName, out object? resolved) || resolved is not StashStruct structDef)
                            {
                                throw new RuntimeError($"Cannot extend '{metadata.TypeName}': not a known type.", span);
                            }

                            for (int i = 0; i < metadata.MethodNames.Length; i++)
                        {
                            string methodName = metadata.MethodNames[i];
                            if (!structDef.OriginalMethodNames.Contains(methodName))
                                {
                                    structDef.Methods[methodName] = methodFuncs[i];
                                }
                            }
                    }

                    break;
                }

                // ==================== Error Handling ====================
                case OpCode.Throw:
                {
                    object? errorVal = Pop().ToObject();
                    SourceSpan? span = GetCurrentSpan(ref frame);
                    if (errorVal is StashError se)
                        {
                            throw new RuntimeError(se.Message, span, se.Type) { Properties = se.Properties };
                        }

                        if (errorVal is string msg)
                        {
                            throw new RuntimeError(msg, span);
                        }

                        if (errorVal is StashDictionary throwDict)
                    {
                        string errMsg = throwDict.Has("message")
                            ? throwDict.Get("message")?.ToString() ?? ""
                            : RuntimeValues.Stringify(errorVal);
                        string errType = throwDict.Has("type")
                            ? throwDict.Get("type")?.ToString() ?? "RuntimeError"
                            : "RuntimeError";
                        var props = new Dictionary<string, object?>();
                        foreach (var kv in throwDict.GetAllEntries())
                            {
                                if (kv.Key is string k)
                                {
                                    props[k] = kv.Value;
                                }
                            }

                            throw new RuntimeError(errMsg, span, errType) { Properties = props };
                    }
                    throw new RuntimeError(RuntimeValues.Stringify(errorVal), span);
                }

                case OpCode.TryBegin:
                {
                    short catchOffset = ReadI16(ref frame);
                    _exceptionHandlers.Add(new ExceptionHandler
                    {
                        CatchIP = frame.IP + catchOffset,
                        StackLevel = _sp,
                        FrameIndex = _frameCount - 1,
                    });
                    // Return back to Run() which will re-enter RunInner() — needed so the
                    // outer try-catch in Run() can intercept exceptions from this handler scope.
                    // We signal this by throwing a special marker... Actually, we just continue.
                    break;
                }

                case OpCode.TryEnd:
                {
                    if (_exceptionHandlers.Count > 0)
                        {
                            _exceptionHandlers.RemoveAt(_exceptionHandlers.Count - 1);
                        }

                        break;
                }

                // ==================== Switch ====================
                case OpCode.Switch:
                {
                    ReadU16(ref frame); // skip operand — switch is compiled to basic opcodes
                    break;
                }

                // ==================== Iteration ====================
                case OpCode.Iterator:
                {
                    SourceSpan? span = GetCurrentSpan(ref frame);
                    object? iterable = Pop().ToObject();
                    Push(StashValue.FromObj(CreateIterator(iterable, span)));
                    break;
                }

                case OpCode.Iterate:
                {
                    short exitOffset = ReadI16(ref frame);
                    SourceSpan? span = GetCurrentSpan(ref frame);

                    // Find the StashIterator in the current for-in scope by scanning backward
                    // (at most 3 slots: iterator, optional index var, loop var)
                    StashIterator? iter = null;
                    int iterSlot = -1;
                    for (int i = _sp - 1; i >= Math.Max(frame.BaseSlot, _sp - 4); i--)
                    {
                        if (_stack[i].AsObj is StashIterator found)
                        {
                            iter = found;
                            iterSlot = i;
                            break;
                        }
                    }
                    if (iter == null)
                        {
                            throw new RuntimeError("Internal error: no active iterator.", span);
                        }

                        if (!iter.MoveNext())
                    {
                        frame.IP += exitOffset;
                    }
                    else
                    {
                        Push(StashValue.FromObject(iter.Current));
                        // Update index variable if present.
                        // Layout: [iter @ iterSlot][indexVar @ iterSlot+1][loopVar] = 3 locals
                        //         [iter @ iterSlot][loopVar]                         = 2 locals
                        // After Push, _sp increased by 1; forInLocals = (_sp - 1) - iterSlot
                        int forInLocals = (_sp - 1) - iterSlot;
                        if (forInLocals == 3)
                        {
                            if (iter.Dictionary != null)
                            {
                                // Dict key-value iteration: Current = key, look up value
                                object? dictKey = iter.Current;
                                _stack[iterSlot + 1] = StashValue.FromObject(dictKey);  // key
                                _stack[_sp - 1] = StashValue.FromObject(iter.Dictionary.Get(dictKey!));  // value
                            }
                            else
                            {
                                _stack[iterSlot + 1] = StashValue.FromInt(iter.Index);
                            }
                        }
                    }
                    break;
                }

                // ==================== Async ====================
                case OpCode.Await:
                {
                    object? future = Pop().ToObject();
                    if (future is StashFuture sf)
                        {
                            Push(StashValue.FromObject(sf.GetResult()));
                        }
                        else
                        {
                            Push(StashValue.FromObject(future)); // non-future values pass through
                        }

                        break;
                }

                case OpCode.CloseUpvalue:
                {
                    // Close all open upvalues rooted at or above the given local slot.
                    // Used to freeze per-iteration loop variables before the VM loops back,
                    // ensuring each closure captures the value at the moment it was created.
                    byte localSlot = ReadByte(ref frame);
                    CloseUpvalues(frame.BaseSlot + localSlot);
                    break;
                }

                // ==================== Arithmetic (continued) ====================
                case OpCode.Power:
                {
                    SourceSpan? span = GetCurrentSpan(ref frame);
                    StashValue right = Pop();
                    StashValue left = Pop();
                    Push(RuntimeOps.Power(left, right, span));
                    break;
                }

                // ==================== Containment ====================
                case OpCode.In:
                {
                    SourceSpan? span = GetCurrentSpan(ref frame);
                    StashValue right = Pop();
                    StashValue left = Pop();
                    Push(StashValue.FromBool(RuntimeOps.Contains(left, right, span)));
                    break;
                }

                // ==================== Shell Commands ====================
                case OpCode.Command:
                {
                    ushort metaCmdIdx = ReadU16(ref frame);
                    SourceSpan? span = GetCurrentSpan(ref frame);
                    var cmdMetadata = (CommandMetadata)frame.Chunk.Constants[metaCmdIdx].AsObj!;

                    var sb = new StringBuilder();
                    int partStart = _sp - cmdMetadata.PartCount;
                    for (int i = partStart; i < _sp; i++)
                        {
                            sb.Append(RuntimeOps.Stringify(_stack[i]));
                        }

                        _sp = partStart;

                    string command = _context.ExpandTilde(sb.ToString().Trim());
                    if (string.IsNullOrEmpty(command))
                        {
                            throw new RuntimeError("Command cannot be empty.", span);
                        }

                    var (program, arguments) = CommandParser.Parse(command);

                    // Expand tilde in individual arguments (the full-command ExpandTilde above
                    // only handles a leading ~ in the program name)
                    string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                    for (int i = 0; i < arguments.Count; i++)
                    {
                        string arg = arguments[i];
                        if (arg == "~")
                            arguments[i] = home;
                        else if (arg.StartsWith("~/", StringComparison.Ordinal) || arg.StartsWith("~\\", StringComparison.Ordinal))
                            arguments[i] = Path.Combine(home, arg[2..]);
                    }

                    // Apply elevation prefix if active
                    if (_context.ElevationActive && _context.ElevationCommand != null)
                    {
                        string lowerProgram = program.ToLowerInvariant();
                        if (lowerProgram is not ("sudo" or "doas" or "gsudo" or "runas") &&
                            !string.Equals(program, _context.ElevationCommand, StringComparison.OrdinalIgnoreCase))
                        {
                            var prefixedArgs = new List<string>(arguments.Count + 1) { program };
                            prefixedArgs.AddRange(arguments);
                            arguments = prefixedArgs;
                            program = _context.ElevationCommand;
                        }
                    }

                    if (cmdMetadata.IsPassthrough)
                    {
                        var (_, _, exitCode) = ExecPassthrough(program, arguments, span);
                        if (cmdMetadata.IsStrict && exitCode != 0)
                        {
                            throw new RuntimeError(
                                $"Command failed with exit code {exitCode}: {command}",
                                span, "CommandError")
                            {
                                Properties = new Dictionary<string, object?>
                                {
                                    ["exitCode"] = (long)exitCode,
                                    ["stderr"] = "",
                                    ["stdout"] = "",
                                    ["command"] = command
                                }
                            };
                        }
                        Push(StashValue.FromObj(new StashInstance("CommandResult", new Dictionary<string, object?>
                        {
                            ["stdout"] = "",
                            ["stderr"] = "",
                            ["exitCode"] = (long)exitCode
                        }) { StringifyField = "stdout" }));
                    }
                    else
                    {
                        var (stdout, stderr, exitCode) = ExecCaptured(program, arguments, null, span);
                        if (cmdMetadata.IsStrict && exitCode != 0)
                        {
                            throw new RuntimeError(
                                $"Command failed with exit code {exitCode}: {command}",
                                span, "CommandError")
                            {
                                Properties = new Dictionary<string, object?>
                                {
                                    ["exitCode"] = (long)exitCode,
                                    ["stderr"] = stderr,
                                    ["stdout"] = stdout,
                                    ["command"] = command
                                }
                            };
                        }
                        Push(StashValue.FromObj(new StashInstance("CommandResult", new Dictionary<string, object?>
                        {
                            ["stdout"] = stdout,
                            ["stderr"] = stderr,
                            ["exitCode"] = (long)exitCode
                        }) { StringifyField = "stdout" }));
                    }
                    break;
                }

                case OpCode.Pipe:
                {
                    // Both sides have already been executed. Return the right result, which
                    // carries the final exit code per pipeline semantics.
                    // True streaming pipes are a future enhancement (Phase 7+).
                    StashValue rightResult = Pop();
                    StashValue leftResult = Pop();

                    static bool IsCommandResult(object? obj) =>
                        obj is StashDictionary d && d.Has("stdout") && d.Has("exitCode");

                    if (!IsCommandResult(leftResult.ToObject()) || !IsCommandResult(rightResult.ToObject()))
                    {
                        throw new RuntimeError("All stages in a pipe must be command expressions.", GetCurrentSpan(ref frame));
                    }

                    Push(rightResult);
                    break;
                }

                case OpCode.Redirect:
                {
                    byte flags = ReadByte(ref frame);
                    SourceSpan? span = GetCurrentSpan(ref frame);
                    object? target = Pop().ToObject();
                    object? cmdResult = Pop().ToObject();

                    string filePath = target is string fp
                        ? fp
                        : throw new RuntimeError("Redirect target must be a string.", span);

                    int stream = flags & 0x03;
                    bool append = (flags & 0x04) != 0;

                    string stdout = "", stderr = "";
                    if (cmdResult is StashInstance ri)
                    {
                        stdout = (ri.GetField("stdout", span) as string) ?? "";
                        stderr = (ri.GetField("stderr", span) as string) ?? "";
                    }

                    string content = stream switch
                    {
                        0 => stdout,
                        1 => stderr,
                        _ => stdout + stderr
                    };

                    try
                    {
                        if (append)
                            {
                                File.AppendAllText(filePath, content);
                            }
                            else
                            {
                                File.WriteAllText(filePath, content);
                            }
                        }
                    catch (Exception ex)
                    {
                        throw new RuntimeError($"Redirect failed: {ex.Message}", span);
                    }

                    var newFields = new Dictionary<string, object?>
                    {
                        ["stdout"] = (stream == 0 || stream == 2) ? "" : stdout,
                        ["stderr"] = (stream == 1 || stream == 2) ? "" : stderr,
                        ["exitCode"] = cmdResult is StashInstance ri2 ? ri2.GetField("exitCode", span) : 0L
                    };
                    Push(StashValue.FromObj(new StashInstance("CommandResult", newFields) { StringifyField = "stdout" }));
                    break;
                }

                // ==================== Module Import ====================
                case OpCode.Import:
                {
                    ushort metaImportIdx = ReadU16(ref frame);
                    SourceSpan? span = GetCurrentSpan(ref frame);
                    var importMeta = (ImportMetadata)frame.Chunk.Constants[metaImportIdx].AsObj!;

                    StashValue pathVal = Pop();
                    string modulePath = pathVal.AsObj is string mp
                        ? mp
                        : throw new RuntimeError("Module path must be a string.", span);

                    Dictionary<string, object?> moduleEnv = LoadModule(modulePath, span);

                    foreach (string importName in importMeta.Names)
                    {
                        if (moduleEnv.TryGetValue(importName, out object? importedValue))
                            {
                                Push(StashValue.FromObject(importedValue));
                            }
                            else
                            {
                                throw new RuntimeError($"Module does not export '{importName}'.", span);
                            }
                        }
                    break;
                }

                case OpCode.ImportAs:
                {
                    ushort metaImportAsIdx = ReadU16(ref frame);
                    SourceSpan? span = GetCurrentSpan(ref frame);
                    var importAsMeta = (ImportAsMetadata)frame.Chunk.Constants[metaImportAsIdx].AsObj!;

                    StashValue pathVal = Pop();
                    string modulePath = pathVal.AsObj is string mp
                        ? mp
                        : throw new RuntimeError("Module path must be a string.", span);

                    Dictionary<string, object?> moduleEnv = LoadModule(modulePath, span);

                    var ns = new StashNamespace(importAsMeta.AliasName);
                    foreach (KeyValuePair<string, object?> kvp in moduleEnv)
                    {
                        if (kvp.Value is StashNamespace)
                            {
                                continue; // skip inherited built-in namespaces
                            }

                            ns.Define(kvp.Key, kvp.Value);
                    }
                    ns.Freeze();
                    Push(StashValue.FromObj(ns));
                    break;
                }

                // ==================== Destructure ====================
                case OpCode.Destructure:
                {
                    ushort metaDestructIdx = ReadU16(ref frame);
                    SourceSpan? span = GetCurrentSpan(ref frame);
                    var destructMeta = (DestructureMetadata)frame.Chunk.Constants[metaDestructIdx].AsObj!;

                    object? initializer = Pop().ToObject();

                    if (destructMeta.Kind == "array")
                    {
                        if (initializer is not List<object?> list)
                            {
                                throw new RuntimeError("Array destructuring requires an array value.", span);
                            }

                            for (int i = 0; i < destructMeta.Names.Length; i++)
                            {
                                Push(StashValue.FromObject(i < list.Count ? list[i] : null));
                            }

                            if (destructMeta.RestName != null)
                        {
                            var rest = new List<object?>();
                            for (int i = destructMeta.Names.Length; i < list.Count; i++)
                                {
                                    rest.Add(list[i]);
                                }

                                Push(StashValue.FromObj(rest));
                        }
                    }
                    else
                    {
                        if (initializer is StashInstance destructInst)
                        {
                            var usedNames = new HashSet<string>(destructMeta.Names);
                            foreach (string dname in destructMeta.Names)
                                {
                                    Push(StashValue.FromObject(destructInst.GetField(dname, span)));
                                }

                                if (destructMeta.RestName != null)
                            {
                                var rest = new StashDictionary();
                                foreach (KeyValuePair<string, object?> kvp in destructInst.GetAllFields())
                                {
                                    if (!usedNames.Contains(kvp.Key))
                                        {
                                            rest.Set(kvp.Key, kvp.Value);
                                        }
                                    }
                                Push(StashValue.FromObj(rest));
                            }
                        }
                        else if (initializer is StashDictionary destructDict)
                        {
                            var usedNames = new HashSet<string>(destructMeta.Names);
                            foreach (string dname in destructMeta.Names)
                                {
                                    Push(StashValue.FromObject(destructDict.Has(dname) ? destructDict.Get(dname) : null));
                                }

                                if (destructMeta.RestName != null)
                            {
                                var rest = new StashDictionary();
                                var allKeys = destructDict.Keys();
                                foreach (object? k in allKeys)
                                {
                                    if (k is string ks && !usedNames.Contains(ks))
                                        {
                                            rest.Set(ks, destructDict.Get(ks));
                                        }
                                    }
                                Push(StashValue.FromObj(rest));
                            }
                        }
                        else
                        {
                            throw new RuntimeError(
                                "Object destructuring requires a struct instance or dictionary.", span);
                        }
                    }
                    break;
                }

                // ==================== Elevation ====================
                case OpCode.ElevateBegin:
                {
                    if (_context.EmbeddedMode)
                        throw new RuntimeError("Elevate blocks are not supported in embedded mode.", GetCurrentSpan(ref frame));
                    object? elevator = Pop().ToObject();
                    _context.ElevationActive = true;
                    if (elevator is string elevStr)
                        {
                            _context.ElevationCommand = elevStr;
                        }
                        else if (elevator != null)
                        {
                            _context.ElevationCommand = RuntimeOps.Stringify(StashValue.FromObject(elevator));
                        }
                        else
                        {
                            _context.ElevationCommand = OperatingSystem.IsWindows() ? "gsudo" : "sudo";
                        }

                        break;
                }

                case OpCode.ElevateEnd:
                {
                    _context.ElevationActive = false;
                    _context.ElevationCommand = null;
                    break;
                }

                // ==================== Retry ====================
                case OpCode.Retry:
                {
                    ushort metaRetryIdx = ReadU16(ref frame);
                    SourceSpan? span = GetCurrentSpan(ref frame);
                    var retryMeta = (RetryMetadata)frame.Chunk.Constants[metaRetryIdx].AsObj!;

                    // Pop closures in reverse order of emission (onRetry first, then until, then body)
                    VMFunction? onRetryVmFn = null;
                    IStashCallable? onRetryCb = null;
                    if (retryMeta.HasOnRetryClause)
                    {
                        object? obj = Pop().ToObject();
                        if (obj is VMFunction f1)
                            {
                                onRetryVmFn = f1;
                            }
                            else if (obj is IStashCallable c1)
                            {
                                onRetryCb = c1;
                            }
                        }

                    VMFunction? untilVmFn = null;
                    IStashCallable? untilCb = null;
                    if (retryMeta.HasUntilClause)
                    {
                        object? obj = Pop().ToObject();
                        if (obj is VMFunction f2)
                            {
                                untilVmFn = f2;
                            }
                            else if (obj is IStashCallable c2)
                            {
                                untilCb = c2;
                            }
                        }

                    object? bodyObj = Pop().ToObject();
                    VMFunction bodyVmFn = bodyObj as VMFunction
                        ?? throw new RuntimeError("Retry body must be a function.", span);

                    // Extract options from the stack (below body)
                    long retryDelayMs = 0;
                    if (retryMeta.OptionCount == -1)
                    {
                        object? optStruct = Pop().ToObject();
                        if (optStruct is StashInstance oi)
                        {
                            if (oi.GetFields().TryGetValue("delay", out object? dv))
                            {
                                if (dv is long dl)
                                    {
                                        retryDelayMs = dl;
                                    }
                                    else if (dv is StashDuration dd)
                                    {
                                        retryDelayMs = (long)dd.TotalMilliseconds;
                                    }
                                }
                        }
                    }
                    else if (retryMeta.OptionCount > 0)
                    {
                        int pairStart = _sp - retryMeta.OptionCount * 2;
                        for (int oi = 0; oi < retryMeta.OptionCount; oi++)
                        {
                            string optKey = (string)_stack[pairStart + oi * 2].AsObj!;
                            object? optVal = _stack[pairStart + oi * 2 + 1].ToObject();
                            if (optKey == "delay")
                            {
                                if (optVal is long dl)
                                    {
                                        retryDelayMs = dl;
                                    }
                                    else if (optVal is StashDuration dd)
                                    {
                                        retryDelayMs = (long)dd.TotalMilliseconds;
                                    }
                                }
                        }
                        _sp = pairStart;
                    }

                    object? maxAttemptsObj = Pop().ToObject();
                    if (maxAttemptsObj is not long maxAttempts)
                    {
                        throw new RuntimeError("Retry max attempts must be an integer.", span);
                    }
                    if (maxAttempts < 0)
                    {
                        throw new RuntimeError("Retry max attempts must be non-negative.", span);
                    }
                    if (maxAttempts == 0)
                    {
                        throw new RuntimeError(
                            "All 0 retry attempts exhausted — predicate not satisfied.",
                            span, "RetryExhaustedError");
                    }

                    // Execute retry loop
                    object? retryLastResult = null;
                    RuntimeError? retryLastError = null;

                    var retryErrors = new List<object?>();

                    for (long attempt = 1; attempt <= maxAttempts; attempt++)
                    {
                        bool bodyThrew = false;
                        try
                        {
                            // Build attempt context dict
                            var attemptCtx = new StashDictionary();
                            attemptCtx.Set("current", attempt);
                            attemptCtx.Set("max", maxAttempts);
                            attemptCtx.Set("remaining", maxAttempts - attempt);
                            attemptCtx.Set("errors", new List<object?>(retryErrors));
                            retryLastResult = ExecuteVMFunctionInline(bodyVmFn, new object?[] { attemptCtx }, span);
                        }
                        catch (RuntimeError rex)
                        {
                            bodyThrew = true;
                            retryLastError = rex;

                            var retryErr = new StashError(rex.Message, rex.ErrorType ?? "RuntimeError", null, rex.Properties);
                            retryErrors.Add(retryErr);

                            // Only call onRetry if this is NOT the last attempt
                            if (attempt < maxAttempts)
                            {
                                if (onRetryVmFn != null)
                                {
                                    ExecuteVMFunctionInline(onRetryVmFn,
                                        new object?[] { attempt, retryErr }, span);
                                }
                                else if (onRetryCb != null)
                                {
                                    onRetryCb.Call(_context, new List<object?> { attempt, retryErr });
                                }
                            }
                        }

                        if (!bodyThrew)
                        {
                            bool success = true;
                            if (untilVmFn != null)
                            {
                                object?[] untilArgs = untilVmFn.Chunk.Arity >= 2
                                    ? new object?[] { retryLastResult, attempt }
                                    : new object?[] { retryLastResult };
                                object? pred = ExecuteVMFunctionInline(untilVmFn, untilArgs, span);
                                if (RuntimeOps.IsFalsy(StashValue.FromObject(pred)))
                                {
                                    success = false;
                                }
                            }
                            else if (untilCb != null)
                            {
                                List<object?> untilArgs2 = untilCb is VMFunction cbFn && cbFn.Chunk.Arity >= 2
                                    ? new List<object?> { retryLastResult, attempt }
                                    : new List<object?> { retryLastResult };
                                object? pred = untilCb.Call(_context, untilArgs2);
                                if (RuntimeOps.IsFalsy(StashValue.FromObject(pred)))
                                {
                                    success = false;
                                }
                            }

                            if (success)
                            {
                                Push(StashValue.FromObject(retryLastResult));
                                goto retryDone;
                            }
                            // Until predicate failed — treat as a retry-worthy failure
                            if (attempt < maxAttempts)
                            {
                                if (onRetryVmFn != null)
                                {
                                    ExecuteVMFunctionInline(onRetryVmFn,
                                        new object?[] { attempt, new StashError("Predicate not satisfied", "RetryError") }, span);
                                }
                                else if (onRetryCb != null)
                                {
                                    onRetryCb.Call(_context, new List<object?> { attempt, new StashError("Predicate not satisfied", "RetryError") });
                                }
                            }
                            bodyThrew = true;
                        }

                        if (retryDelayMs > 0 && attempt < maxAttempts)
                            {
                                Thread.Sleep((int)retryDelayMs);
                            }

                            if (attempt == maxAttempts)
                        {
                            if (retryLastError != null)
                                {
                                    throw new RuntimeError(retryLastError.Message, span,
                                    retryLastError.ErrorType ?? "RuntimeError");
                                }

                                throw new RuntimeError(
                                $"All {maxAttempts} retry attempts exhausted — predicate not satisfied.",
                                span, "RetryExhaustedError");
                        }
                    }
                    retryDone:
                    break;
                }

                // ==================== Deferred / Not-yet-implemented ====================
                case OpCode.CheckNumeric:
                {
                    StashValue val = Peek();
                    if (!val.IsNumeric)
                    {
                        throw new RuntimeError("Operand of '++' or '--' must be a number.", GetCurrentSpan(ref frame));
                    }
                    break;
                }

                case OpCode.PreIncrement:
                case OpCode.PreDecrement:
                case OpCode.PostIncrement:
                case OpCode.PostDecrement:
                case OpCode.TryExpr:
                {
                    int operandSize = OpCodeInfo.OperandSize((OpCode)instruction);
                    frame.IP += operandSize;
                    throw new RuntimeError(
                        $"Opcode {(OpCode)instruction} is not yet implemented in the bytecode VM.",
                        GetCurrentSpan(ref frame));
                }

                default:
                    throw new RuntimeError(
                        $"Unknown opcode {instruction} at offset {frame.IP - 1}.",
                        GetCurrentSpan(ref frame));
            }
        }
    }

    // ------------------------------------------------------------------
    // Helper Methods
    // ------------------------------------------------------------------

    private void CallValue(object? callee, int argc, SourceSpan? callSpan)
    {
        if (callee is VMFunction fn)
        {
            Chunk fnChunk = fn.Chunk;
            int provided = argc;
            int expected = fnChunk.Arity;
            int minArity = fnChunk.MinArity;

            if (fnChunk.HasRestParam)
            {
                int nonRestCount = expected - 1;
                int minRequired = Math.Min(minArity, nonRestCount);

                if (provided < minRequired)
                {
                    throw new RuntimeError(
                        $"Expected at least {minRequired} arguments but got {provided}.",
                        callSpan);
                }

                // Pad non-rest params that weren't provided (may have defaults)
                if (provided < nonRestCount)
                {
                    for (int i = provided; i < nonRestCount; i++)
                    {
                        Push(StashValue.FromObj(NotProvided));
                    }

                    provided = nonRestCount;
                }

                // Collect rest args into a list
                int restCount = Math.Max(0, provided - nonRestCount);
                var restList = new List<object?>(restCount);
                int restStart = _sp - restCount;
                for (int i = restStart; i < _sp; i++)
                {
                    restList.Add(_stack[i].ToObject());
                }

                _sp = restStart;
                Push(StashValue.FromObj(restList));
                provided = expected;
            }
            else
            {
                // Arity checking for non-rest functions
                if (provided < minArity || provided > expected)
                {
                    string expectedStr = minArity == expected
                        ? $"{expected}"
                        : $"{minArity} to {expected}";
                    throw new RuntimeError(
                        $"Expected {expectedStr} arguments but got {provided}.",
                        callSpan);
                }

                // Pad missing optional args with NotProvided sentinel
                if (provided < expected)
                {
                    for (int i = provided; i < expected; i++)
                    {
                        Push(StashValue.FromObj(NotProvided));
                    }
                }
            }

            int baseSlot = _sp - expected;

            if (fnChunk.IsAsync)
            {
                Push(StashValue.FromObject(SpawnAsyncFunction(fnChunk, fn.Upvalues, baseSlot, callSpan)));
                return;
            }

            PushFrame(fnChunk, baseSlot, fn.Upvalues, fnChunk.Name);
            return;
        }

        if (callee is VMBoundMethod bound)
        {
            VMFunction boundFn = bound.Function;
            Chunk fnChunk = boundFn.Chunk;

            // Shift existing args right by 1 to make room for self after the callee slot.
            // This preserves the callee slot so OP_RETURN (_sp = baseSlot - 1) correctly
            // discards the frame + callee.
            int shiftStart = _sp - argc; // first arg position
            Push(StashValue.Null);        // grow stack + increment _sp
            for (int i = _sp - 2; i >= shiftStart; i--)
            {
                _stack[i + 1] = _stack[i];
            }

            _stack[shiftStart] = StashValue.FromObj(bound.Instance); // insert self after callee

            int provided = argc + 1; // user args + self
            int expected = fnChunk.Arity;
            int minArity = fnChunk.MinArity;

            if (fnChunk.HasRestParam)
            {
                int nonRestCount = expected - 1;
                int minRequired = Math.Min(minArity, nonRestCount);
                if (provided < minRequired)
                {
                    throw new RuntimeError($"Expected at least {minRequired - 1} arguments but got {argc}.", callSpan);
                }

                if (provided < nonRestCount)
                {
                    for (int i = provided; i < nonRestCount; i++)
                    {
                        Push(StashValue.FromObj(NotProvided));
                    }

                    provided = nonRestCount;
                }

                int restCount = Math.Max(0, provided - nonRestCount);
                var restList = new List<object?>(restCount);
                int restStart = _sp - restCount;
                for (int i = restStart; i < _sp; i++)
                {
                    restList.Add(_stack[i].ToObject());
                }

                _sp = restStart;
                Push(StashValue.FromObj(restList));
                provided = expected;
            }
            else
            {
                if (provided < minArity || provided > expected)
                {
                    string expectedStr = minArity == expected
                        ? $"{expected - 1}"
                        : $"{minArity - 1} to {expected - 1}";
                    throw new RuntimeError($"Expected {expectedStr} arguments but got {argc}.", callSpan);
                }

                if (provided < expected)
                {
                    for (int i = provided; i < expected; i++)
                    {
                        Push(StashValue.FromObj(NotProvided));
                    }
                }
            }

            int baseSlot = _sp - fnChunk.Arity;

            if (fnChunk.IsAsync)
            {
                Push(StashValue.FromObject(SpawnAsyncFunction(fnChunk, boundFn.Upvalues, baseSlot, callSpan)));
                return;
            }

            PushFrame(fnChunk, baseSlot, boundFn.Upvalues, fnChunk.Name);
            return;
        }

        if (callee is VMExtensionBoundMethod extBound)
        {
            VMFunction extFn = extBound.Function;
            Chunk fnChunk = extFn.Chunk;

            // Same shift approach as VMBoundMethod — preserve callee slot for OP_RETURN.
            int shiftStart = _sp - argc;
            Push(StashValue.Null);
            for (int i = _sp - 2; i >= shiftStart; i--)
            {
                _stack[i + 1] = _stack[i];
            }

            _stack[shiftStart] = StashValue.FromObject(extBound.Receiver);

            int provided = argc + 1; // user args + self
            int expected = fnChunk.Arity;
            int minArity = fnChunk.MinArity;

            if (fnChunk.HasRestParam)
            {
                int nonRestCount = expected - 1;
                int minRequired = Math.Min(minArity, nonRestCount);
                if (provided < minRequired)
                {
                    throw new RuntimeError($"Expected at least {minRequired - 1} arguments but got {argc}.", callSpan);
                }

                if (provided < nonRestCount)
                {
                    for (int i = provided; i < nonRestCount; i++)
                    {
                        Push(StashValue.FromObj(NotProvided));
                    }

                    provided = nonRestCount;
                }

                int restCount = Math.Max(0, provided - nonRestCount);
                var restList = new List<object?>(restCount);
                int restStart = _sp - restCount;
                for (int i = restStart; i < _sp; i++)
                {
                    restList.Add(_stack[i].ToObject());
                }

                _sp = restStart;
                Push(StashValue.FromObj(restList));
                provided = expected;
            }
            else
            {
                if (provided < minArity || provided > expected)
                {
                    string expectedStr = minArity == expected
                        ? $"{expected - 1}"
                        : $"{minArity - 1} to {expected - 1}";
                    throw new RuntimeError($"Expected {expectedStr} arguments but got {argc}.", callSpan);
                }

                if (provided < expected)
                {
                    for (int i = provided; i < expected; i++)
                    {
                        Push(StashValue.FromObj(NotProvided));
                    }
                }
            }

            int baseSlot = _sp - fnChunk.Arity;

            if (fnChunk.IsAsync)
            {
                Push(StashValue.FromObject(SpawnAsyncFunction(fnChunk, extFn.Upvalues, baseSlot, callSpan)));
                return;
            }

            PushFrame(fnChunk, baseSlot, extFn.Upvalues, fnChunk.Name);
            return;
        }

        if (callee is IStashCallable callable)
        {
            // Arity checking for IStashCallable
            if (callable.Arity != -1)
            {
                int minArity = callable.MinArity;
                if (argc < minArity || argc > callable.Arity)
                {
                    string expectedStr = minArity == callable.Arity
                        ? $"{callable.Arity}"
                        : $"{minArity} to {callable.Arity}";
                    throw new RuntimeError(
                        $"Expected {expectedStr} arguments but got {argc}.",
                        callSpan);
                }
            }

            var args = new List<object?>(argc);
            int argStart = _sp - argc;
            for (int i = argStart; i < _sp; i++)
            {
                args.Add(_stack[i].ToObject());
            }

            _sp = argStart - 1; // pop args + callee slot

            object? result;
            try
            {
                _context.CurrentSpan = callSpan;
                result = callable.Call(_context, args);
            }
            catch (RuntimeError)
            {
                throw;
            }
            catch (Stash.Tpl.TemplateException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new RuntimeError($"Built-in function error: {ex.Message}", callSpan);
            }
            Push(StashValue.FromObject(result));
            return;
        }

        throw new RuntimeError(
            $"Can only call functions. Got {RuntimeValues.Stringify(callee)}.",
            callSpan);
    }

    private object? GetFieldValue(object? obj, string name, SourceSpan? span)
    {
        // 1. StashInstance: field + method access
        if (obj is StashInstance instance)
        {
            object? result = instance.GetField(name, span);
            // Intercept StashBoundMethod wrapping VMFunction → return VMBoundMethod instead
            if (result is StashBoundMethod bound && bound.Method is VMFunction vmFunc)
            {
                return new VMBoundMethod(bound.Instance, vmFunc);
            }

            return result;
        }

        // 2. StashDictionary: extension methods first, then key access
        if (obj is StashDictionary dict)
        {
            if (_extensionRegistry.TryGetMethod("dict", name, out IStashCallable? dictExtMethod) &&
                dictExtMethod is VMFunction dictExtFunc)
            {
                return new VMExtensionBoundMethod(obj, dictExtFunc);
            }

            return dict.Get(name);
        }

        // 3. StashNamespace: member access
        if (obj is StashNamespace ns)
        {
            return ns.GetMember(name, span);
        }

        // 4. StashStruct: static method access
        if (obj is StashStruct structDef)
        {
            if (structDef.Methods.TryGetValue(name, out IStashCallable? method))
            {
                return method;
            }

            throw new RuntimeError($"Struct '{structDef.Name}' has no static member '{name}'.", span);
        }

        // 5. StashEnum: member access
        if (obj is StashEnum enumDef)
        {
            StashEnumValue? enumVal = enumDef.GetMember(name);
            if (enumVal == null)
            {
                throw new RuntimeError($"Enum '{enumDef.Name}' has no member '{name}'.", span);
            }

            return enumVal;
        }

        // 6. StashEnumValue: property access
        if (obj is StashEnumValue enumValue)
        {
            return name switch
            {
                "typeName" => enumValue.TypeName,
                "memberName" => enumValue.MemberName,
                _ => throw new RuntimeError($"Enum value has no property '{name}'.", span)
            };
        }

        // 7. StashError: property access
        if (obj is StashError error)
        {
            return name switch
            {
                "message" => error.Message,
                "type" => error.Type,
                "stack" => error.Stack is not null ? new List<object?>(error.Stack) : null,
                _ => error.Properties?.TryGetValue(name, out object? propVal) == true
                    ? propVal
                    : throw new RuntimeError($"Error has no property '{name}'.", span)
            };
        }

        // StashDuration: property access
        if (obj is StashDuration dur)
        {
            return name switch
            {
                "totalMs" => (object)dur.TotalMilliseconds,
                "totalSeconds" => (object)dur.TotalSeconds,
                "totalMinutes" => (object)dur.TotalMinutes,
                "totalHours" => (object)dur.TotalHours,
                "totalDays" => (object)dur.TotalDays,
                "milliseconds" => (object)dur.Milliseconds,
                "seconds" => (object)dur.Seconds,
                "minutes" => (object)dur.Minutes,
                "hours" => (object)dur.Hours,
                "days" => (object)dur.Days,
                _ => throw new RuntimeError($"Duration has no property '{name}'.", span)
            };
        }

        // StashByteSize: property access
        if (obj is StashByteSize bs)
        {
            return name switch
            {
                "bytes" => (object)bs.TotalBytes,
                "kb" => (object)bs.Kb,
                "mb" => (object)bs.Mb,
                "gb" => (object)bs.Gb,
                "tb" => (object)bs.Tb,
                _ => throw new RuntimeError($"ByteSize has no property '{name}'.", span)
            };
        }

        // StashSemVer: property access
        if (obj is StashSemVer sv)
        {
            return name switch
            {
                "major" => (object)sv.Major,
                "minor" => (object)sv.Minor,
                "patch" => (object)sv.Patch,
                "prerelease" => (object)(sv.Prerelease ?? ""),
                "build" => (object)(sv.BuildMetadata ?? ""),
                "isPrerelease" => (object)sv.IsPrerelease,
                _ => throw new RuntimeError($"SemVer has no property '{name}'.", span)
            };
        }

        // StashIpAddress: property access
        if (obj is StashIpAddress ip)
        {
            return name switch
            {
                "address" => (object)ip.Address.ToString(),
                "version" => (object)(long)ip.Version,
                "prefixLength" => ip.PrefixLength.HasValue ? (object)(long)ip.PrefixLength.Value : null,
                "isLoopback" => (object)ip.IsLoopback,
                "isPrivate" => (object)ip.IsPrivate,
                "isLinkLocal" => (object)ip.IsLinkLocal,
                "isIPv4" => (object)(ip.Version == 4),
                "isIPv6" => (object)(ip.Version == 6),
                _ => throw new RuntimeError($"IpAddress has no property '{name}'.", span)
            };
        }

        // 8. Built-in type .length properties
        if (obj is List<object?> list && name == "length")
        {
            return (long)list.Count;
        }

        if (obj is string s && name == "length")
        {
            return (long)s.Length;
        }

        // 9. Extension methods on built-in types
        string? extTypeName = obj switch
        {
            string => "string",
            List<object?> => "array",
            long => "int",
            double => "float",
            _ => null
        };

        if (extTypeName is not null &&
            _extensionRegistry.TryGetMethod(extTypeName, name, out IStashCallable? extMethod))
        {
            if (extMethod is VMFunction extVmFunc)
            {
                return new VMExtensionBoundMethod(obj, extVmFunc);
            }

            return new BuiltInBoundMethod(obj, extMethod);
        }

        // 10. UFCS: namespace functions as methods on strings/arrays
        string? ufcsNsName = obj switch
        {
            string => "str",
            List<object?> => "arr",
            _ => null
        };

        if (ufcsNsName is not null &&
            _globals.TryGetValue(ufcsNsName, out object? nsVal) &&
            nsVal is StashNamespace ufcsNs &&
            ufcsNs.HasMember(name))
        {
            object? member = ufcsNs.GetMember(name, span);
            if (member is IStashCallable callable)
            {
                return new BuiltInBoundMethod(obj, callable);
            }
        }

        throw new RuntimeError($"Cannot access field '{name}' on {RuntimeValues.Stringify(obj)}.", span);
    }

    private static void SetFieldValue(object? obj, string name, object? value, SourceSpan? span)
    {
        if (obj is StashInstance instance)
        {
            instance.SetField(name, value, span);
            return;
        }
        if (obj is StashDictionary dict)
        {
            dict.Set(name, value);
            return;
        }
        throw new RuntimeError($"Cannot set field '{name}' on {RuntimeValues.Stringify(obj)}.", span);
    }

    private static object? GetIndexValue(object? obj, object? index, SourceSpan? span)
    {
        if (obj is List<object?> list)
        {
            if (index is not long i)
            {
                throw new RuntimeError("Array index must be an integer.", span);
            }

            if (i < 0)
            {
                i += list.Count;
            }

            if (i < 0 || i >= list.Count)
            {
                throw new RuntimeError($"Index {index} out of bounds for array of length {list.Count}.", span);
            }

            return list[(int)i];
        }
        if (obj is StashDictionary dict)
        {
            if (index is null)
                throw new RuntimeError("Dictionary key cannot be null.", span);
            return dict.Get(index);
        }

        if (obj is string s)
        {
            if (index is not long idx)
            {
                throw new RuntimeError("String index must be an integer.", span);
            }

            if (idx < 0)
            {
                idx += s.Length;
            }

            if (idx < 0 || idx >= s.Length)
            {
                throw new RuntimeError($"Index {index} out of bounds for string of length {s.Length}.", span);
            }

            return s[(int)idx].ToString();
        }
        throw new RuntimeError($"Cannot index into {RuntimeValues.Stringify(obj)}.", span);
    }

    private static void SetIndexValue(object? obj, object? index, object? value, SourceSpan? span)
    {
        if (obj is List<object?> list)
        {
            if (index is not long i)
            {
                throw new RuntimeError("Array index must be an integer.", span);
            }

            if (i < 0)
            {
                i += list.Count;
            }

            if (i < 0 || i >= list.Count)
            {
                throw new RuntimeError($"Index {index} out of bounds for array of length {list.Count}.", span);
            }

            list[(int)i] = value;
            return;
        }
        if (obj is StashDictionary dict)
        {
            if (index is null)
                throw new RuntimeError("Dictionary key cannot be null.", span);
            dict.Set(index, value);
            return;
        }
        throw new RuntimeError($"Cannot index-assign into {RuntimeValues.Stringify(obj)}.", span);
    }

    private static bool InstanceImplementsInterfaceName(StashInstance inst, string ifaceName)
    {
        if (inst.Struct == null) return false;
        foreach (StashInterface iface in inst.Struct.Interfaces)
        {
            if (iface.Name == ifaceName) return true;
        }
        return false;
    }

    private static bool CheckIsType(object? value, string typeName) => typeName switch
    {
        "int"       => value is long,
        "float"     => value is double,
        "string"    => value is string,
        "bool"      => value is bool,
        "array"     => value is List<object?>,
        "dict"      => value is StashDictionary,
        "null"      => value is null,
        "function"  => value is VMFunction or IStashCallable,
        "range"     => value is StashRange,
        "duration"  => value is StashDuration,
        "bytes"     => value is StashByteSize,
        "semver"    => value is StashSemVer,
        "ip"        => value is StashIpAddress,
        "Error"     => value is StashError,
        "struct"    => value is StashInstance,
        "enum"      => value is StashEnumValue,
        "interface" => value is StashInterface,
        "namespace" => value is StashNamespace,
        "Future"    => value is StashFuture,
        _           => value is StashEnumValue ev ? ev.TypeName == typeName
                     : value is StashInstance inst && inst.TypeName == typeName,
    };

    private static StashIterator CreateIterator(object? iterable, SourceSpan? span)
    {
        if (iterable is StashDictionary dict)
        {
            return new StashIterator(dict.Keys().GetEnumerator(), dict);
        }

        IEnumerator<object?> enumerator = iterable switch
        {
            List<object?> list    => new List<object?>(list).GetEnumerator(),
            StashRange range      => range.Iterate().GetEnumerator(),
            string s              => RuntimeValues.StringToChars(s).GetEnumerator(),
            _ => throw new RuntimeError($"Cannot iterate over {RuntimeValues.Stringify(iterable)}.", span),
        };
        return new StashIterator(enumerator);
    }

    // ------------------------------------------------------------------
    // Execution helpers
    // ------------------------------------------------------------------

    /// <summary>
    /// Runs the dispatch loop until <see cref="_frameCount"/> drops to
    /// <paramref name="targetFrameCount"/>. Handles exceptions the same
    /// way as the outer <see cref="Run"/> loop.
    /// </summary>
    private object? RunUntilFrame(int targetFrameCount)
    {
        if (_debugger is not null)
        {
            return RunUntilFrameDebug(targetFrameCount);
        }

        while (true)
        {
            try
            {
                return RunInner(targetFrameCount);
            }
            catch (RuntimeError ex) when (_exceptionHandlers.Count > 0 && _exceptionHandlers[^1].FrameIndex >= targetFrameCount)
            {
                ExceptionHandler handler = _exceptionHandlers[^1];
                _exceptionHandlers.RemoveAt(_exceptionHandlers.Count - 1);
                CloseUpvalues(handler.StackLevel);
                _frameCount = handler.FrameIndex + 1;
                _sp = handler.StackLevel;
                var stashError = new StashError(ex.Message, ex.ErrorType ?? "RuntimeError", null, ex.Properties);
                _context.LastError = stashError;
                Push(StashValue.FromObj(stashError));
                _frames[_frameCount - 1].IP = handler.CatchIP;
            }
        }
    }

    private object? RunUntilFrameDebug(int targetFrameCount)
    {
        IDebugger debugger = _debugger!;

        while (true)
        {
            try
            {
                return RunInner(targetFrameCount);
            }
            catch (RuntimeError ex) when (_exceptionHandlers.Count > 0 && _exceptionHandlers[^1].FrameIndex >= targetFrameCount)
            {
                ExceptionHandler handler = _exceptionHandlers[^1];
                _exceptionHandlers.RemoveAt(_exceptionHandlers.Count - 1);
                CloseUpvalues(handler.StackLevel);
                _frameCount = handler.FrameIndex + 1;
                while (_debugCallStack.Count > handler.FrameIndex)
                {
                    _debugCallStack.RemoveAt(_debugCallStack.Count - 1);
                }

                _sp = handler.StackLevel;
                var stashError = new StashError(ex.Message, ex.ErrorType ?? "RuntimeError", null, ex.Properties);
                _context.LastError = stashError;
                Push(StashValue.FromObj(stashError));
                _frames[_frameCount - 1].IP = handler.CatchIP;
            }
            catch (RuntimeError ex)
            {
                if (debugger.ShouldBreakOnException(ex))
                {
                    SourceSpan? span = (_frameCount > 0) ? GetCurrentSpan(ref _frames[_frameCount - 1]) : null;
                    if (span is not null)
                    {
                        IDebugScope scope = (_frameCount > 0)
                            ? BuildFrameScope(ref _frames[_frameCount - 1])
                            : BuildGlobalScope();
                        debugger.OnBeforeExecute(span, scope, _debugThreadId);
                    }
                }
                debugger.OnError(ex, _debugCallStack, _debugThreadId);
                throw;
            }
        }
    }

    /// <summary>
    /// Spawns an async <see cref="VMFunction"/> on a background thread via a child VM and
    /// returns a <see cref="StashFuture"/>. Called when a Chunk with <c>IsAsync = true</c> is
    /// invoked. All arity checks, rest-param collection, and default-param padding have already
    /// been applied by <see cref="CallValue"/>; <paramref name="baseSlot"/> points to the first
    /// argument on the current stack.
    /// </summary>
    private StashFuture SpawnAsyncFunction(Chunk fnChunk, Upvalue[] upvalues, int baseSlot, SourceSpan? callSpan)
    {
        // Snapshot the fully-prepared arguments (rest-collected, defaults applied).
        int arity = fnChunk.Arity;
        var capturedArgs = new object?[arity];
        for (int i = 0; i < arity; i++)
        {
            capturedArgs[i] = _stack[baseSlot + i].ToObject();
        }

        _sp = baseSlot - 1; // pop callee slot + all args off the parent stack

        // Upgrade parent IO streams to thread-safe wrappers before spawning.
        if (_context.Output is not SynchronizedTextWriter)
        {
            _context.Output = new SynchronizedTextWriter(_context.Output);
        }

        if (_context.ErrorOutput is not SynchronizedTextWriter)
        {
            _context.ErrorOutput = new SynchronizedTextWriter(_context.ErrorOutput);
        }

        // Snapshot everything the child VM needs — capture before Task.Run to avoid races.
        var capturedGlobals = new Dictionary<string, object?>(_globals);
        var capturedModuleLoader = _moduleLoader;
        var capturedModuleCache = _moduleCache;
        var capturedImportStack = _importStack;
        var capturedModuleLocks = _moduleLocks;
        string? capturedFile = _context.CurrentFile;
        TextWriter capturedOutput = _context.Output;
        TextWriter capturedErrorOutput = _context.ErrorOutput;
        TextReader capturedInput = _context.Input;
        bool capturedEmbedded = EmbeddedMode;

        var cts = new CancellationTokenSource();
        var task = Task.Run(() =>
        {
            var childVM = new VirtualMachine(capturedGlobals, cts.Token)
            {
                _moduleLoader = capturedModuleLoader,
                _moduleCache = capturedModuleCache,
                _importStack = capturedImportStack,
                _moduleLocks = capturedModuleLocks,
                EmbeddedMode = capturedEmbedded,
            };
            childVM._context.CurrentFile = capturedFile;
            childVM._context.Output = capturedOutput;
            childVM._context.ErrorOutput = capturedErrorOutput;
            childVM._context.Input = capturedInput;

            // Replicate the call-frame layout: callee slot + prepared args, then run.
            childVM.Push(StashValue.FromObj(new VMFunction(fnChunk, upvalues)));
            for (int i = 0; i < arity; i++)
            {
                childVM.Push(StashValue.FromObject(capturedArgs[i]));
            }

            int childBase = childVM._sp - arity;
            childVM.PushFrame(fnChunk, childBase, upvalues, fnChunk.Name);
            return childVM.Run();
        }, cts.Token);

        return new StashFuture(task, cts);
    }

    /// <summary>
    /// Calls a <see cref="VMFunction"/> closure inline on the same VM instance and
    /// returns its result. Safe to call from within the dispatch loop (e.g. OP_RETRY)
    /// and from built-in function callbacks (e.g. arr.map, test.it).
    /// The stack pointer is saved and restored after the call so that repeated calls
    /// (such as sort comparisons) do not leave stale values on the stack.
    /// </summary>
    internal object? ExecuteVMFunctionInline(VMFunction fn, object?[] args, SourceSpan? span)
    {
        int savedSp = _sp;
        int savedFrameCount = _frameCount;
        Push(StashValue.FromObj(fn)); // callee slot
        foreach (object? arg in args)
        {
            Push(StashValue.FromObject(arg));
        }

        CallValue(fn, args.Length, span);
        try
        {
            return RunUntilFrame(savedFrameCount);
        }
        finally
        {
            // CRITICAL: always restore the VM's stack pointer (and frame count on error),
            // both for repeated calls (e.g. sort comparisons) and exception paths.
            // Without this, RuntimeErrors that propagate through here and are caught by a
            // built-in wrapper (e.g. assert.throws) leave the VM with a stale _sp,
            // corrupting any subsequent stack operations.
            if (_frameCount > savedFrameCount)
            {
                CloseUpvalues(savedSp);
                _frameCount = savedFrameCount;
            }
            _sp = savedSp;
        }
    }

    /// <summary>
    /// Calls a <see cref="VMFunction"/> closure on this VM instance with a fresh execution state.
    /// Used by <see cref="VMContext.InvokeCallback"/> to execute user lambdas from background threads
    /// (e.g. fs.watch callbacks) on a dedicated child VM backed by shared globals.
    /// </summary>
    internal object? CallClosure(VMFunction fn, System.Collections.Generic.List<object?> args)
    {
        _sp = 0;
        _frameCount = 0;
        StepCount = 0;
        _exceptionHandlers.Clear();
        _openUpvalues.Clear();
        Push(StashValue.FromObj(fn));
        foreach (object? arg in args)
        {
            Push(StashValue.FromObject(arg));
        }

        CallValue(fn, args.Count, null);
        return Run();
    }

    // ------------------------------------------------------------------
    // Process execution helpers (replicate interpreter's RunCaptured/RunPassthrough)
    // ------------------------------------------------------------------

    private static (string Stdout, string Stderr, int ExitCode) ExecCaptured(
        string program, List<string> arguments, string? stdin, SourceSpan? span)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = program,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = stdin is not null,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            foreach (string arg in arguments)
            {
                psi.ArgumentList.Add(arg);
            }

            using var process = Process.Start(psi)
                ?? throw new RuntimeError("Failed to start process.", span);

            if (stdin is not null)
            {
                process.StandardInput.Write(stdin);
                process.StandardInput.Close();
            }

            var stdoutTask = Task.Run(() => process.StandardOutput.ReadToEnd());
            var stderrTask = Task.Run(() => process.StandardError.ReadToEnd());
            Task.WaitAll(stdoutTask, stderrTask);
            process.WaitForExit();

            return (stdoutTask.Result, stderrTask.Result, process.ExitCode);
        }
        catch (RuntimeError)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new RuntimeError($"Command execution failed: {ex.Message}", span);
        }
    }

    private static (string Stdout, string Stderr, int ExitCode) ExecPassthrough(
        string program, List<string> arguments, SourceSpan? span)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = program,
                RedirectStandardOutput = false,
                RedirectStandardError = false,
                RedirectStandardInput = false,
                UseShellExecute = false,
                CreateNoWindow = false
            };
            foreach (string arg in arguments)
            {
                psi.ArgumentList.Add(arg);
            }

            using var process = Process.Start(psi)
                ?? throw new RuntimeError("Failed to start process.", span);
            process.WaitForExit();

            return ("", "", process.ExitCode);
        }
        catch (RuntimeError)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new RuntimeError($"Command execution failed: {ex.Message}", span);
        }
    }

    // ------------------------------------------------------------------
    // Debugger scope helpers
    // ------------------------------------------------------------------

    /// <summary>
    /// Builds an <see cref="IDebugScope"/> from a VM call frame for debugger inspection.
    /// Creates a live-stack scope — reads and writes go directly to the VM's value stack.
    /// </summary>
    private IDebugScope BuildFrameScope(ref CallFrame frame)
    {
        // At the top-level (no function calls on the debug call stack), top-level
        // variables are accessed via LoadGlobal/StoreGlobal and live in _globals.
        // The local stack slots are only set at declaration time and go stale
        // immediately after the first assignment through StoreGlobal.  Returning the
        // global scope directly gives the debugger accurate, up-to-date values.
        if (_debugCallStack.Count == 0)
        {
            return BuildGlobalScope();
        }

        Chunk chunk = frame.Chunk;
        IDebugScope enclosing = BuildGlobalScope();

        // If present, insert a closure scope between local and global
        if (frame.Upvalues is not null && frame.Upvalues.Length > 0)
        {
            string[]? upvalueNames = chunk.UpvalueNames;
            var closureBindings = new KeyValuePair<string, object?>[frame.Upvalues.Length];
            for (int i = 0; i < frame.Upvalues.Length; i++)
            {
                string name = upvalueNames is not null && i < upvalueNames.Length
                    ? upvalueNames[i] : $"upvalue_{i}";
                object? value = frame.Upvalues[i].Value.ToObject();
                closureBindings[i] = new KeyValuePair<string, object?>(name, value);
            }
            enclosing = new VMDebugScope(closureBindings, enclosing);
        }

        return new VMDebugScope(
            _stack, frame.BaseSlot, chunk.LocalCount,
            chunk.LocalNames, chunk.LocalIsConst, enclosing);
    }

    /// <summary>
    /// Builds an <see cref="IDebugScope"/> wrapping the global variables.
    /// </summary>
    internal IDebugScope BuildGlobalScope()
    {
        return new VMDebugScope(_globals, null);
    }

    // ------------------------------------------------------------------
    // Module loading
    // ------------------------------------------------------------------

    private Dictionary<string, object?> LoadModule(string modulePath, SourceSpan? span)
    {
        // Use the context's current file, or fall back to the span's source file
        // (populated when source is compiled with a real file path, e.g. via RunWithFile).
        string? currentFile = _context.CurrentFile;
        if (currentFile == null && span?.File is { Length: > 0 } src && !src.StartsWith('<'))
            currentFile = src;
        string resolvedPath;

        if (Path.IsPathRooted(modulePath))
        {
            resolvedPath = modulePath;
        }
        else if (currentFile != null)
        {
            string? dir = Path.GetDirectoryName(currentFile);
            resolvedPath = Path.GetFullPath(Path.Combine(dir ?? ".", modulePath));
        }
        else
        {
            resolvedPath = Path.GetFullPath(modulePath);
        }

        // Try package resolution for non-path module specifiers
        if (!File.Exists(resolvedPath) && !File.Exists(resolvedPath + ".stash"))
        {
            string? packagePath = ResolvePackagePath(modulePath, span);
            if (packagePath != null)
            {
                resolvedPath = packagePath;
            }
        }

        // Auto-append .stash extension if missing
        if (!resolvedPath.EndsWith(".stash", StringComparison.OrdinalIgnoreCase))
            resolvedPath += ".stash";

        if (_moduleCache.TryGetValue(resolvedPath, out Dictionary<string, object?>? cached))
        {
            return cached;
        }

        object moduleLock = _moduleLocks.GetOrAdd(resolvedPath, _ => new object());
        lock (moduleLock)
        {
            // Double-check after acquiring lock
            if (_moduleCache.TryGetValue(resolvedPath, out Dictionary<string, object?>? cached2))
            {
                return cached2;
            }

            if (_importStack.Contains(resolvedPath))
            {
                throw new RuntimeError($"Circular import detected: {resolvedPath}", span);
            }

            _importStack.Add(resolvedPath);
            try
            {
                Chunk moduleChunk;
                try
                {
                    if (_moduleLoader != null)
                    {
                        moduleChunk = _moduleLoader(resolvedPath, currentFile);
                    }
                    else
                    {
                        // Built-in file-based loader: compile the module from disk.
                        string moduleSource = File.ReadAllText(resolvedPath);
                        var lex = new Lexer(moduleSource, resolvedPath);
                        var tokens = lex.ScanTokens();
                        if (lex.Errors.Count > 0)
                            throw new RuntimeError($"Syntax error in module '{resolvedPath}': {lex.Errors[0]}", span);
                        var par = new Parser(tokens);
                        var stmts = par.ParseProgram();
                        if (par.Errors.Count > 0)
                            throw new RuntimeError($"Parse error in module '{resolvedPath}': {par.Errors[0]}", span);
                        SemanticResolver.Resolve(stmts);
                        moduleChunk = Compiler.Compile(stmts);
                    }
                }
                catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
                {
                    throw new RuntimeError($"Cannot find module '{modulePath}'.", span);
                }

                var moduleGlobals = new Dictionary<string, object?>(_globals);
                var moduleVM = new VirtualMachine(moduleGlobals, _ct)
                {
                    _moduleLoader = _moduleLoader,
                    _moduleCache = _moduleCache,
                    _importStack = _importStack,
                    _moduleLocks = _moduleLocks,
                };
                moduleVM._context.CurrentFile = resolvedPath;
                moduleVM._context.Output = _context.Output;
                moduleVM._context.ErrorOutput = _context.ErrorOutput;
                moduleVM._context.Input = _context.Input;
                moduleVM.Debugger = _debugger;
                moduleVM._debugThreadId = _debugThreadId;
                moduleVM.EmbeddedMode = EmbeddedMode;
                moduleVM.ScriptArgs = ScriptArgs;
                moduleVM.TestHarness = TestHarness;
                moduleVM.TestFilter = TestFilter;

                // Notify debugger of newly loaded source
                _debugger?.OnSourceLoaded(resolvedPath);

                moduleVM.Execute(moduleChunk);

                _moduleCache[resolvedPath] = moduleVM.Globals;
                return moduleVM.Globals;
            }
            finally
            {
                _importStack.Remove(resolvedPath);
            }
        }
    }

    private string? ResolvePackagePath(string modulePath, SourceSpan? span)
    {
        // Only attempt package resolution for non-path module specifiers
        if (modulePath.StartsWith(".", StringComparison.Ordinal) ||
            modulePath.StartsWith("/", StringComparison.Ordinal) ||
            modulePath.StartsWith("\\", StringComparison.Ordinal) ||
            modulePath.EndsWith(".stash", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        // Find project root by walking up to find stash.json
        string? startDir = _context.CurrentFile != null
            ? Path.GetDirectoryName(Path.GetFullPath(_context.CurrentFile))
            : null;

        if (startDir == null) return null;

        string? projectRoot = null;
        string? dir = startDir;
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir, "stash.json")))
            {
                projectRoot = dir;
                break;
            }
            string? parent = Path.GetDirectoryName(dir);
            if (parent == dir) break;
            dir = parent;
        }

        if (projectRoot == null) return null;

        string stashesDir = Path.Combine(projectRoot, "stashes");

        // Parse package name and subpath
        string packageName;
        string? subPath = null;

        if (modulePath.StartsWith("@", StringComparison.Ordinal))
        {
            // Scoped package: @scope/name or @scope/name/subpath
            int secondSlash = modulePath.IndexOf('/', modulePath.IndexOf('/') + 1);
            if (secondSlash >= 0)
            {
                packageName = modulePath[..secondSlash];
                subPath = modulePath[(secondSlash + 1)..];
            }
            else
            {
                packageName = modulePath;
            }
        }
        else if (modulePath.Contains('/'))
        {
            // Unscoped with subpath: pkg/lib/helpers
            int firstSlash = modulePath.IndexOf('/');
            packageName = modulePath[..firstSlash];
            subPath = modulePath[(firstSlash + 1)..];
        }
        else
        {
            packageName = modulePath;
        }

        string packageDir = Path.Combine(stashesDir, packageName.Replace('/', Path.DirectorySeparatorChar));
        if (!Directory.Exists(packageDir))
        {
            throw new RuntimeError(
                $"Package '{packageName}' not found. Run `stash pkg install {packageName}` to install it.",
                span);
        }

        if (subPath != null)
        {
            // Resolve subpath within package
            string subFilePath = Path.Combine(packageDir, subPath.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(subFilePath)) return subFilePath;
            if (!subFilePath.EndsWith(".stash", StringComparison.OrdinalIgnoreCase))
            {
                string withExt = subFilePath + ".stash";
                if (File.Exists(withExt)) return withExt;
            }
            throw new RuntimeError($"Module '{modulePath}' not found in package '{packageName}'.", span);
        }

        // Resolve entry point: check stash.json for "main" field
        string pkgJsonPath = Path.Combine(packageDir, "stash.json");
        if (File.Exists(pkgJsonPath))
        {
            try
            {
                string json = File.ReadAllText(pkgJsonPath);
                // Simple JSON parsing for "main" field
                int mainIdx = json.IndexOf("\"main\"", StringComparison.Ordinal);
                if (mainIdx >= 0)
                {
                    int colonIdx = json.IndexOf(':', mainIdx);
                    if (colonIdx >= 0)
                    {
                        int quoteStart = json.IndexOf('"', colonIdx + 1);
                        if (quoteStart >= 0)
                        {
                            int quoteEnd = json.IndexOf('"', quoteStart + 1);
                            if (quoteEnd > quoteStart)
                            {
                                string mainFile = json[(quoteStart + 1)..quoteEnd];
                                string mainPath = Path.Combine(packageDir, mainFile.Replace('/', Path.DirectorySeparatorChar));
                                if (File.Exists(mainPath)) return mainPath;
                            }
                        }
                    }
                }
            }
            catch { /* ignore parse errors, fall through to default */ }
        }

        // Default entry point: index.stash
        string indexPath = Path.Combine(packageDir, "index.stash");
        if (File.Exists(indexPath)) return indexPath;

        throw new RuntimeError($"Package '{packageName}' has no entry point (no index.stash or main field).", span);
    }
}

/// <summary>
/// Wraps an <see cref="IEnumerator{T}"/> with a current-element index counter.
/// Used internally by the VM to execute for-in loops.
/// </summary>
internal sealed class StashIterator
{
    private readonly IEnumerator<object?> _inner;

    public int Index { get; private set; } = -1;

    /// <summary>When non-null, this iterator is iterating over a dict's keys; the dict is stored here for value lookup.</summary>
    public StashDictionary? Dictionary { get; }

    public StashIterator(IEnumerator<object?> inner) => _inner = inner;

    public StashIterator(IEnumerator<object?> inner, StashDictionary dict)
    {
        _inner = inner;
        Dictionary = dict;
    }

    public bool MoveNext()
    {
        Index++;
        return _inner.MoveNext();
    }

    public object? Current => _inner.Current;
}

/// <summary>
/// Wraps an iterable value pushed by <see cref="OpCode.Spread"/> so that
/// <see cref="OpCode.Array"/> and <see cref="OpCode.Dict"/> can expand it
/// without changing the compile-time element count.
/// </summary>
internal sealed class SpreadMarker
{
    public readonly object Items;
    public SpreadMarker(object items) => Items = items;
}
