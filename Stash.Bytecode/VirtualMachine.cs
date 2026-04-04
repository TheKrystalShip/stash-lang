using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using Stash.Common;
using Stash.Runtime;
using Stash.Runtime.Types;

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

    private object?[] _stack;
    private int _sp; // stack pointer: index of next free slot

    private CallFrame[] _frames;
    private int _frameCount;

    private readonly Dictionary<string, object?> _globals;
    private readonly List<Upvalue> _openUpvalues;
    private readonly CancellationToken _ct;

    private readonly List<ExceptionHandler> _exceptionHandlers = new();
    private readonly VMContext _context;
    private readonly ExtensionRegistry _extensionRegistry = new();

    public VirtualMachine(Dictionary<string, object?>? globals = null, CancellationToken ct = default)
    {
        _stack = new object?[DefaultStackSize];
        _frames = new CallFrame[DefaultFrameDepth];
        _globals = globals ?? new Dictionary<string, object?>();
        _openUpvalues = new List<Upvalue>();
        _ct = ct;
        _context = new VMContext(ct);
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

    /// <summary>Execute a compiled chunk and return the result.</summary>
    public object? Execute(Chunk chunk)
    {
        _sp = 0;
        _frameCount = 0;
        _exceptionHandlers.Clear();
        _openUpvalues.Clear();
        PushFrame(chunk, baseSlot: 0, upvalues: null, name: chunk.Name);
        return Run();
    }

    // ---- Frame Management ----

    private void PushFrame(Chunk chunk, int baseSlot, Upvalue[]? upvalues, string? name)
    {
        if (_frameCount >= _frames.Length)
            Array.Resize(ref _frames, _frames.Length * 2);
        ref CallFrame frame = ref _frames[_frameCount++];
        frame.Chunk = chunk;
        frame.IP = 0;
        frame.BaseSlot = baseSlot;
        frame.Upvalues = upvalues;
        frame.FunctionName = name;
    }

    // ---- Stack Operations ----

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void Push(object? value)
    {
        if (_sp >= _stack.Length)
            GrowStack();
        _stack[_sp++] = value;
    }

    private void GrowStack()
    {
        Array.Resize(ref _stack, _stack.Length * 2);
        foreach (Upvalue uv in _openUpvalues)
            uv.UpdateStack(_stack);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private object? Pop() => _stack[--_sp];

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private object? Peek() => _stack[_sp - 1];

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
                return existing;
        }
        var upvalue = new Upvalue(_stack, stackIndex);
        // Insert sorted by descending StackIndex for efficient closing
        int insertIdx = 0;
        while (insertIdx < _openUpvalues.Count && _openUpvalues[insertIdx].StackIndex > stackIndex)
            insertIdx++;
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
                return RunInner();
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
                Push(new StashError(ex.Message, ex.ErrorType ?? "RuntimeError"));

                // Resume execution at the catch handler's bytecode offset
                _frames[_frameCount - 1].IP = handler.CatchIP;
            }
        }
    }

    private object? RunInner()
    {
        while (true)
        {
            ref CallFrame frame = ref _frames[_frameCount - 1];
            byte instruction = frame.Chunk.Code[frame.IP++];

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
                    Push(null);
                    break;

                case OpCode.True:
                    Push(true);
                    break;

                case OpCode.False:
                    Push(false);
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
                    string name = (string)frame.Chunk.Constants[nameIdx]!;
                    if (!_globals.TryGetValue(name, out object? value))
                        throw new RuntimeError($"Undefined variable '{name}'.", GetCurrentSpan(ref frame));
                    Push(value);
                    break;
                }

                case OpCode.StoreGlobal:
                {
                    ushort nameIdx = ReadU16(ref frame);
                    string name = (string)frame.Chunk.Constants[nameIdx]!;
                    _globals[name] = Pop();
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
                    object? b = Pop();
                    object? a = Pop();
                    if (a is long la && b is long lb)
                        Push(la + lb);
                    else
                        Push(RuntimeOps.Add(a, b, GetCurrentSpan(ref frame)));
                    break;
                }

                case OpCode.Subtract:
                {
                    object? b = Pop();
                    object? a = Pop();
                    if (a is long la && b is long lb)
                        Push(la - lb);
                    else
                        Push(RuntimeOps.Subtract(a, b, GetCurrentSpan(ref frame)));
                    break;
                }

                case OpCode.Multiply:
                {
                    object? b = Pop();
                    object? a = Pop();
                    if (a is long la && b is long lb)
                        Push(la * lb);
                    else
                        Push(RuntimeOps.Multiply(a, b, GetCurrentSpan(ref frame)));
                    break;
                }

                case OpCode.Divide:
                {
                    object? b = Pop();
                    object? a = Pop();
                    Push(RuntimeOps.Divide(a, b, GetCurrentSpan(ref frame)));
                    break;
                }

                case OpCode.Modulo:
                {
                    object? b = Pop();
                    object? a = Pop();
                    Push(RuntimeOps.Modulo(a, b, GetCurrentSpan(ref frame)));
                    break;
                }

                case OpCode.Negate:
                {
                    object? val = Pop();
                    if (val is long l)
                        Push(-l);
                    else
                        Push(RuntimeOps.Negate(val, GetCurrentSpan(ref frame)));
                    break;
                }

                // ==================== Bitwise ====================
                case OpCode.BitAnd:
                {
                    object? b = Pop(), a = Pop();
                    if (a is long la && b is long lb)
                        Push(la & lb);
                    else
                        Push(RuntimeOps.BitAnd(a, b, GetCurrentSpan(ref frame)));
                    break;
                }

                case OpCode.BitOr:
                {
                    object? b = Pop(), a = Pop();
                    if (a is long la && b is long lb)
                        Push(la | lb);
                    else
                        Push(RuntimeOps.BitOr(a, b, GetCurrentSpan(ref frame)));
                    break;
                }

                case OpCode.BitXor:
                {
                    object? b = Pop(), a = Pop();
                    Push(RuntimeOps.BitXor(a, b, GetCurrentSpan(ref frame)));
                    break;
                }

                case OpCode.BitNot:
                    Push(RuntimeOps.BitNot(Pop(), GetCurrentSpan(ref frame)));
                    break;

                case OpCode.ShiftLeft:
                {
                    object? b = Pop(), a = Pop();
                    Push(RuntimeOps.ShiftLeft(a, b, GetCurrentSpan(ref frame)));
                    break;
                }

                case OpCode.ShiftRight:
                {
                    object? b = Pop(), a = Pop();
                    Push(RuntimeOps.ShiftRight(a, b, GetCurrentSpan(ref frame)));
                    break;
                }

                // ==================== Comparison ====================
                case OpCode.Equal:
                {
                    object? b = Pop(), a = Pop();
                    Push(RuntimeOps.IsEqual(a, b));
                    break;
                }

                case OpCode.NotEqual:
                {
                    object? b = Pop(), a = Pop();
                    Push(!RuntimeOps.IsEqual(a, b));
                    break;
                }

                case OpCode.LessThan:
                {
                    object? b = Pop(), a = Pop();
                    Push(RuntimeOps.LessThan(a, b, GetCurrentSpan(ref frame)));
                    break;
                }

                case OpCode.LessEqual:
                {
                    object? b = Pop(), a = Pop();
                    Push(RuntimeOps.LessEqual(a, b, GetCurrentSpan(ref frame)));
                    break;
                }

                case OpCode.GreaterThan:
                {
                    object? b = Pop(), a = Pop();
                    Push(RuntimeOps.GreaterThan(a, b, GetCurrentSpan(ref frame)));
                    break;
                }

                case OpCode.GreaterEqual:
                {
                    object? b = Pop(), a = Pop();
                    Push(RuntimeOps.GreaterEqual(a, b, GetCurrentSpan(ref frame)));
                    break;
                }

                // ==================== Logic ====================
                case OpCode.Not:
                    Push(RuntimeOps.IsFalsy(Pop()));
                    break;

                case OpCode.And:
                {
                    // Short-circuit AND: if top is falsy, keep it and jump (skip right); else pop and eval right
                    short offset = ReadI16(ref frame);
                    if (RuntimeOps.IsFalsy(Peek()))
                        frame.IP += offset;
                    else
                        _sp--; // pop truthy left, continue to right operand
                    break;
                }

                case OpCode.Or:
                {
                    // Short-circuit OR: if top is truthy, keep it and jump (skip right); else pop and eval right
                    short offset = ReadI16(ref frame);
                    if (!RuntimeOps.IsFalsy(Peek()))
                        frame.IP += offset;
                    else
                        _sp--; // pop falsy left, continue to right operand
                    break;
                }

                case OpCode.NullCoalesce:
                {
                    // If top is non-null, keep it and jump; else pop null and eval right
                    short offset = ReadI16(ref frame);
                    if (Peek() is not null)
                        frame.IP += offset;
                    else
                        _sp--; // pop null, continue to right operand
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
                        frame.IP += offset;
                    break;
                }

                case OpCode.JumpFalse:
                {
                    short offset = ReadI16(ref frame);
                    if (RuntimeOps.IsFalsy(Pop()))
                        frame.IP += offset;
                    break;
                }

                case OpCode.Loop:
                {
                    ushort offset = ReadU16(ref frame);
                    frame.IP -= offset;
                    if (_ct.IsCancellationRequested)
                        throw new OperationCanceledException(_ct);
                    break;
                }

                // ==================== Functions ====================
                case OpCode.Call:
                {
                    byte argc = ReadByte(ref frame);
                    // Save span before potential frame array resize
                    SourceSpan? callSpan = GetCurrentSpan(ref frame);
                    object? callee = _stack[_sp - argc - 1];
                    CallValue(callee, argc, callSpan);
                    break;
                }

                case OpCode.Return:
                {
                    object? result = Pop();
                    int baseSlot = _frames[_frameCount - 1].BaseSlot;
                    CloseUpvalues(baseSlot);
                    _frameCount--;
                    if (_frameCount == 0)
                    {
                        _sp = 0;
                        return result;
                    }
                    _sp = baseSlot - 1; // discard function stack window + callee slot
                    Push(result);
                    break;
                }

                case OpCode.Closure:
                {
                    ushort chunkIdx = ReadU16(ref frame);
                    Chunk fnChunk = (Chunk)frame.Chunk.Constants[chunkIdx]!;
                    var upvalues = new Upvalue[fnChunk.Upvalues.Length];
                    for (int i = 0; i < fnChunk.Upvalues.Length; i++)
                    {
                        byte isLocal = frame.Chunk.Code[frame.IP++];
                        byte index = frame.Chunk.Code[frame.IP++];
                        if (isLocal == 1)
                            upvalues[i] = CaptureUpvalue(frame.BaseSlot + index);
                        else
                            upvalues[i] = frame.Upvalues![index];
                    }
                    Push(new VMFunction(fnChunk, upvalues));
                    break;
                }

                // ==================== Collections ====================
                case OpCode.Array:
                {
                    ushort count = ReadU16(ref frame);
                    var list = new List<object?>(count);
                    int start = _sp - count;
                    for (int i = start; i < _sp; i++)
                        list.Add(_stack[i]);
                    _sp = start;
                    Push(list);
                    break;
                }

                case OpCode.Dict:
                {
                    ushort count = ReadU16(ref frame);
                    var dict = new StashDictionary();
                    int start = _sp - (count * 2);
                    for (int i = start; i < _sp; i += 2)
                        dict.Set(_stack[i]!, _stack[i + 1]);
                    _sp = start;
                    Push(dict);
                    break;
                }

                case OpCode.Range:
                {
                    object? step = Pop();
                    object? end = Pop();
                    object? start = Pop();
                    long s = start is long ls
                        ? ls
                        : throw new RuntimeError("Range start must be an integer.", GetCurrentSpan(ref frame));
                    long e = end is long le
                        ? le
                        : throw new RuntimeError("Range end must be an integer.", GetCurrentSpan(ref frame));
                    long st = step is long lst ? lst : (s <= e ? 1L : -1L);
                    Push(new StashRange(s, e, st));
                    break;
                }

                case OpCode.Spread:
                {
                    object? iterable = Pop();
                    if (iterable is List<object?> spreadList)
                    {
                        foreach (object? item in spreadList)
                            Push(item);
                    }
                    else
                    {
                        throw new RuntimeError("Spread operator requires an array.", GetCurrentSpan(ref frame));
                    }
                    break;
                }

                // ==================== Object Access ====================
                case OpCode.GetField:
                {
                    ushort nameIdx = ReadU16(ref frame);
                    string fieldName = (string)frame.Chunk.Constants[nameIdx]!;
                    SourceSpan? span = GetCurrentSpan(ref frame);
                    object? obj = Pop();
                    Push(GetFieldValue(obj, fieldName, span));
                    break;
                }

                case OpCode.SetField:
                {
                    ushort nameIdx = ReadU16(ref frame);
                    string fieldName = (string)frame.Chunk.Constants[nameIdx]!;
                    SourceSpan? span = GetCurrentSpan(ref frame);
                    object? value = Pop();
                    object? obj = Pop();
                    SetFieldValue(obj, fieldName, value, span);
                    Push(value);
                    break;
                }

                case OpCode.GetIndex:
                {
                    SourceSpan? span = GetCurrentSpan(ref frame);
                    object? index = Pop();
                    object? obj = Pop();
                    Push(GetIndexValue(obj, index, span));
                    break;
                }

                case OpCode.SetIndex:
                {
                    SourceSpan? span = GetCurrentSpan(ref frame);
                    object? value = Pop();
                    object? index = Pop();
                    object? obj = Pop();
                    SetIndexValue(obj, index, value, span);
                    Push(value);
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
                        string fname = (string)_stack[i]!;
                        providedFields[fname] = _stack[i + 1];
                    }
                    _sp = pairStart;
                    object? structDef = Pop();
                    if (structDef is StashStruct ss)
                    {
                        // Initialize all declared fields to null, then override with provided values
                        var allFields = new Dictionary<string, object?>(ss.Fields.Count);
                        foreach (string f in ss.Fields)
                            allFields[f] = null;

                        foreach (KeyValuePair<string, object?> kvp in providedFields)
                        {
                            if (!allFields.ContainsKey(kvp.Key))
                                throw new RuntimeError($"Unknown field '{kvp.Key}' for struct '{ss.Name}'.", span);
                            allFields[kvp.Key] = kvp.Value;
                        }

                        Push(new StashInstance(ss.Name, ss, allFields));
                    }
                    else
                        throw new RuntimeError("Not a struct type.", span);
                    break;
                }

                // ==================== Strings ====================
                case OpCode.Interpolate:
                {
                    ushort count = ReadU16(ref frame);
                    string result = RuntimeOps.Interpolate(_stack, _sp, count);
                    _sp -= count;
                    Push(result);
                    break;
                }

                // ==================== Type Operations ====================
                case OpCode.Is:
                {
                    ushort typeIdx = ReadU16(ref frame);
                    string typeName = (string)frame.Chunk.Constants[typeIdx]!;
                    object? value = Pop();
                    Push(CheckIsType(value, typeName));
                    break;
                }

                case OpCode.StructDecl:
                {
                    ushort metaIdx = ReadU16(ref frame);
                    SourceSpan? span = GetCurrentSpan(ref frame);
                    var metadata = (StructMetadata)frame.Chunk.Constants[metaIdx]!;

                    // Pop method closures from stack (pushed in order, so pop in reverse)
                    var methods = new Dictionary<string, IStashCallable>(metadata.MethodNames.Length);
                    for (int i = metadata.MethodNames.Length - 1; i >= 0; i--)
                    {
                        object? methodObj = Pop();
                        if (methodObj is VMFunction vmFunc)
                            methods[metadata.MethodNames[i]] = vmFunc;
                        else
                            throw new RuntimeError($"Expected function for method '{metadata.MethodNames[i]}'.", span);
                    }

                    var fieldList = new List<string>(metadata.Fields);
                    var structDef = new StashStruct(metadata.Name, fieldList, methods);

                    // Resolve and validate interfaces
                    foreach (string ifaceName in metadata.InterfaceNames)
                    {
                        if (!_globals.TryGetValue(ifaceName, out object? resolved) || resolved is not StashInterface iface)
                            throw new RuntimeError($"'{ifaceName}' is not an interface.", span);

                        foreach (InterfaceField reqField in iface.RequiredFields)
                        {
                            if (!fieldList.Contains(reqField.Name))
                                throw new RuntimeError(
                                    $"Struct '{metadata.Name}' does not implement interface '{ifaceName}': missing field '{reqField.Name}'.",
                                    span);
                        }

                        foreach (InterfaceMethod reqMethod in iface.RequiredMethods)
                        {
                            if (!methods.ContainsKey(reqMethod.Name))
                                throw new RuntimeError(
                                    $"Struct '{metadata.Name}' does not implement interface '{ifaceName}': missing method '{reqMethod.Name}'.",
                                    span);
                        }

                        structDef.Interfaces.Add(iface);
                    }

                    Push(structDef);
                    break;
                }

                case OpCode.EnumDecl:
                {
                    ushort metaIdx = ReadU16(ref frame);
                    var metadata = (EnumMetadata)frame.Chunk.Constants[metaIdx]!;

                    var members = new List<string>(metadata.Members);
                    var enumDef = new StashEnum(metadata.Name, members);
                    Push(enumDef);
                    break;
                }

                case OpCode.InterfaceDecl:
                {
                    ushort metaIdx = ReadU16(ref frame);
                    var metadata = (InterfaceMetadata)frame.Chunk.Constants[metaIdx]!;

                    var requiredFields = new List<InterfaceField>(metadata.Fields);
                    var requiredMethods = new List<InterfaceMethod>(metadata.Methods);
                    var interfaceDef = new StashInterface(metadata.Name, requiredFields, requiredMethods);
                    Push(interfaceDef);
                    break;
                }

                case OpCode.Extend:
                {
                    ushort metaIdx = ReadU16(ref frame);
                    SourceSpan? span = GetCurrentSpan(ref frame);
                    var metadata = (ExtendMetadata)frame.Chunk.Constants[metaIdx]!;

                    // Pop method closures (pushed in order, so pop in reverse)
                    var methodFuncs = new IStashCallable[metadata.MethodNames.Length];
                    for (int i = metadata.MethodNames.Length - 1; i >= 0; i--)
                    {
                        object? methodObj = Pop();
                        if (methodObj is not VMFunction vmFunc)
                            throw new RuntimeError($"Expected function for extension method '{metadata.MethodNames[i]}'.", span);
                        methodFuncs[i] = vmFunc;
                    }

                    if (metadata.IsBuiltIn)
                    {
                        for (int i = 0; i < metadata.MethodNames.Length; i++)
                            _extensionRegistry.Register(metadata.TypeName, metadata.MethodNames[i], methodFuncs[i]);
                    }
                    else
                    {
                        if (!_globals.TryGetValue(metadata.TypeName, out object? resolved) || resolved is not StashStruct structDef)
                            throw new RuntimeError($"Cannot extend '{metadata.TypeName}': not a known type.", span);

                        for (int i = 0; i < metadata.MethodNames.Length; i++)
                        {
                            string methodName = metadata.MethodNames[i];
                            if (!structDef.OriginalMethodNames.Contains(methodName))
                                structDef.Methods[methodName] = methodFuncs[i];
                        }
                    }

                    break;
                }

                // ==================== Error Handling ====================
                case OpCode.Throw:
                {
                    object? errorVal = Pop();
                    SourceSpan? span = GetCurrentSpan(ref frame);
                    if (errorVal is StashError se)
                        throw new RuntimeError(se.Message, span, se.Type);
                    if (errorVal is string msg)
                        throw new RuntimeError(msg, span);
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
                        _exceptionHandlers.RemoveAt(_exceptionHandlers.Count - 1);
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
                    object? iterable = Pop();
                    Push(CreateIterator(iterable, span));
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
                        if (_stack[i] is StashIterator found)
                        {
                            iter = found;
                            iterSlot = i;
                            break;
                        }
                    }
                    if (iter == null)
                        throw new RuntimeError("Internal error: no active iterator.", span);

                    if (!iter.MoveNext())
                    {
                        frame.IP += exitOffset;
                    }
                    else
                    {
                        Push(iter.Current);
                        // Update index variable if present.
                        // Layout: [iter @ iterSlot][indexVar @ iterSlot+1][loopVar] = 3 locals
                        //         [iter @ iterSlot][loopVar]                         = 2 locals
                        // After Push, _sp increased by 1; forInLocals = (_sp - 1) - iterSlot
                        int forInLocals = (_sp - 1) - iterSlot;
                        if (forInLocals == 3)
                            _stack[iterSlot + 1] = (long)iter.Index;
                    }
                    break;
                }

                // ==================== Async ====================
                case OpCode.Await:
                {
                    object? future = Pop();
                    if (future is StashFuture sf)
                        Push(sf.GetResult());
                    else
                        Push(future); // non-future values pass through
                    break;
                }

                // ==================== Shell (stubs) ====================
                case OpCode.Command:
                case OpCode.Pipe:
                case OpCode.Redirect:
                {
                    int operandSize = OpCodeInfo.OperandSize((OpCode)instruction);
                    frame.IP += operandSize;
                    throw new RuntimeError(
                        "Shell command execution is not yet supported in the bytecode VM.",
                        GetCurrentSpan(ref frame));
                }

                // ==================== Deferred / Not-yet-implemented ====================
                case OpCode.Power:
                case OpCode.PreIncrement:
                case OpCode.PreDecrement:
                case OpCode.PostIncrement:
                case OpCode.PostDecrement:
                case OpCode.Import:
                case OpCode.ImportAs:
                case OpCode.Destructure:
                case OpCode.ElevateBegin:
                case OpCode.ElevateEnd:
                case OpCode.Retry:
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
                    throw new RuntimeError(
                        $"Expected at least {minRequired} arguments but got {provided}.",
                        callSpan);

                // Pad non-rest params that weren't provided (may have defaults)
                if (provided < nonRestCount)
                {
                    for (int i = provided; i < nonRestCount; i++)
                        Push(NotProvided);
                    provided = nonRestCount;
                }

                // Collect rest args into a list
                int restCount = Math.Max(0, provided - nonRestCount);
                var restList = new List<object?>(restCount);
                int restStart = _sp - restCount;
                for (int i = restStart; i < _sp; i++)
                    restList.Add(_stack[i]);
                _sp = restStart;
                Push(restList);
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
                        Push(NotProvided);
                }
            }

            int baseSlot = _sp - expected;
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
            Push(null!);                 // grow stack + increment _sp
            for (int i = _sp - 2; i >= shiftStart; i--)
                _stack[i + 1] = _stack[i];
            _stack[shiftStart] = bound.Instance; // insert self after callee

            int provided = argc + 1; // user args + self
            int expected = fnChunk.Arity;
            int minArity = fnChunk.MinArity;

            if (fnChunk.HasRestParam)
            {
                int nonRestCount = expected - 1;
                int minRequired = Math.Min(minArity, nonRestCount);
                if (provided < minRequired)
                    throw new RuntimeError($"Expected at least {minRequired - 1} arguments but got {argc}.", callSpan);

                if (provided < nonRestCount)
                {
                    for (int i = provided; i < nonRestCount; i++)
                        Push(NotProvided);
                    provided = nonRestCount;
                }

                int restCount = Math.Max(0, provided - nonRestCount);
                var restList = new List<object?>(restCount);
                int restStart = _sp - restCount;
                for (int i = restStart; i < _sp; i++)
                    restList.Add(_stack[i]);
                _sp = restStart;
                Push(restList);
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
                        Push(NotProvided);
                }
            }

            int baseSlot = _sp - fnChunk.Arity;
            PushFrame(fnChunk, baseSlot, boundFn.Upvalues, fnChunk.Name);
            return;
        }

        if (callee is VMExtensionBoundMethod extBound)
        {
            VMFunction extFn = extBound.Function;
            Chunk fnChunk = extFn.Chunk;

            // Same shift approach as VMBoundMethod — preserve callee slot for OP_RETURN.
            int shiftStart = _sp - argc;
            Push(null!);
            for (int i = _sp - 2; i >= shiftStart; i--)
                _stack[i + 1] = _stack[i];
            _stack[shiftStart] = extBound.Receiver;

            int provided = argc + 1; // user args + self
            int expected = fnChunk.Arity;
            int minArity = fnChunk.MinArity;

            if (fnChunk.HasRestParam)
            {
                int nonRestCount = expected - 1;
                int minRequired = Math.Min(minArity, nonRestCount);
                if (provided < minRequired)
                    throw new RuntimeError($"Expected at least {minRequired - 1} arguments but got {argc}.", callSpan);

                if (provided < nonRestCount)
                {
                    for (int i = provided; i < nonRestCount; i++)
                        Push(NotProvided);
                    provided = nonRestCount;
                }

                int restCount = Math.Max(0, provided - nonRestCount);
                var restList = new List<object?>(restCount);
                int restStart = _sp - restCount;
                for (int i = restStart; i < _sp; i++)
                    restList.Add(_stack[i]);
                _sp = restStart;
                Push(restList);
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
                        Push(NotProvided);
                }
            }

            int baseSlot = _sp - fnChunk.Arity;
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
                args.Add(_stack[i]);
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
            catch (Exception ex)
            {
                throw new RuntimeError($"Built-in function error: {ex.Message}", callSpan);
            }
            Push(result);
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
                return new VMBoundMethod(bound.Instance, vmFunc);
            return result;
        }

        // 2. StashDictionary: extension methods first, then key access
        if (obj is StashDictionary dict)
        {
            if (_extensionRegistry.TryGetMethod("dict", name, out IStashCallable? dictExtMethod) &&
                dictExtMethod is VMFunction dictExtFunc)
                return new VMExtensionBoundMethod(obj, dictExtFunc);
            return dict.Get(name);
        }

        // 3. StashNamespace: member access
        if (obj is StashNamespace ns)
            return ns.GetMember(name, span);

        // 4. StashStruct: static method access
        if (obj is StashStruct structDef)
        {
            if (structDef.Methods.TryGetValue(name, out IStashCallable? method))
                return method;
            throw new RuntimeError($"Struct '{structDef.Name}' has no static member '{name}'.", span);
        }

        // 5. StashEnum: member access
        if (obj is StashEnum enumDef)
        {
            StashEnumValue? enumVal = enumDef.GetMember(name);
            if (enumVal == null)
                throw new RuntimeError($"Enum '{enumDef.Name}' has no member '{name}'.", span);
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

        // 8. Built-in type .length properties
        if (obj is List<object?> list && name == "length")
            return (long)list.Count;
        if (obj is string s && name == "length")
            return (long)s.Length;

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
                return new VMExtensionBoundMethod(obj, extVmFunc);
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
                return new BuiltInBoundMethod(obj, callable);
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
                throw new RuntimeError("Array index must be an integer.", span);
            if (i < 0) i += list.Count;
            if (i < 0 || i >= list.Count)
                throw new RuntimeError($"Index {index} out of bounds for array of length {list.Count}.", span);
            return list[(int)i];
        }
        if (obj is StashDictionary dict)
            return dict.Get(index!);
        if (obj is string s)
        {
            if (index is not long idx)
                throw new RuntimeError("String index must be an integer.", span);
            if (idx < 0) idx += s.Length;
            if (idx < 0 || idx >= s.Length)
                throw new RuntimeError($"Index {index} out of bounds for string of length {s.Length}.", span);
            return s[(int)idx].ToString();
        }
        throw new RuntimeError($"Cannot index into {RuntimeValues.Stringify(obj)}.", span);
    }

    private static void SetIndexValue(object? obj, object? index, object? value, SourceSpan? span)
    {
        if (obj is List<object?> list)
        {
            if (index is not long i)
                throw new RuntimeError("Array index must be an integer.", span);
            if (i < 0) i += list.Count;
            if (i < 0 || i >= list.Count)
                throw new RuntimeError($"Index {index} out of bounds for array of length {list.Count}.", span);
            list[(int)i] = value;
            return;
        }
        if (obj is StashDictionary dict)
        {
            dict.Set(index!, value);
            return;
        }
        throw new RuntimeError($"Cannot index-assign into {RuntimeValues.Stringify(obj)}.", span);
    }

    private static bool CheckIsType(object? value, string typeName) => typeName switch
    {
        "int"      => value is long,
        "float"    => value is double,
        "string"   => value is string,
        "bool"     => value is bool,
        "array"    => value is List<object?>,
        "dict"     => value is StashDictionary,
        "null"     => value is null,
        "function" => value is VMFunction or IStashCallable,
        "range"    => value is StashRange,
        "duration" => value is StashDuration,
        "bytesize" => value is StashByteSize,
        "semver"   => value is StashSemVer,
        "ip"       => value is StashIpAddress,
        "error"    => value is StashError,
        _          => value is StashEnumValue ev ? ev.TypeName == typeName
                     : value is StashInstance inst && inst.TypeName == typeName,
    };

    private static StashIterator CreateIterator(object? iterable, SourceSpan? span)
    {
        IEnumerator<object?> enumerator = iterable switch
        {
            List<object?> list    => list.GetEnumerator(),
            StashRange range      => range.Iterate().GetEnumerator(),
            StashDictionary dict  => dict.Keys().GetEnumerator(),
            string s              => RuntimeValues.StringToChars(s).GetEnumerator(),
            _ => throw new RuntimeError($"Cannot iterate over {RuntimeValues.Stringify(iterable)}.", span),
        };
        return new StashIterator(enumerator);
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

    public StashIterator(IEnumerator<object?> inner) => _inner = inner;

    public bool MoveNext()
    {
        Index++;
        return _inner.MoveNext();
    }

    public object? Current => _inner.Current;
}
