using System;
using System.Buffers;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Stash.Common;
using Stash.Debugging;
using Stash.Runtime;
using Stash.Runtime.Types;
using DebugCallFrame = Stash.Debugging.CallFrame;

namespace Stash.Bytecode;

/// <summary>
/// Main dispatch loop, exception handler infrastructure, and execution routing.
/// </summary>
public sealed partial class VirtualMachine
{
    // ---- Exception Handler Infrastructure ----

    private struct ExceptionHandler
    {
        public int CatchIP;
        public int StackLevel;
        public int FrameIndex;
        public byte ErrorReg;  // register that receives the caught error value
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
                return RunInner<DebugOff>(0);
            }
            catch (RuntimeError ex) when (_exceptionHandlers.Count > 0)
            {
                ExceptionHandler handler = _exceptionHandlers[^1];
                _exceptionHandlers.RemoveAt(_exceptionHandlers.Count - 1);

                // Close any upvalues in the unwound stack region before restoring
                CloseUpvalues(handler.StackLevel);

                // Restore call stack and stack pointer to the handler's save point
                _frameCount = handler.FrameIndex + 1;
                _sp = handler.StackLevel;

                // Construct the StashError and store it in the designated error register
                var stashError = new StashError(ex.Message, ex.ErrorType ?? "RuntimeError", null, ex.Properties);
                _context.LastError = stashError;
                ref CallFrame handlerFrame = ref _frames[_frameCount - 1];
                _stack[handlerFrame.BaseSlot + handler.ErrorReg] = StashValue.FromObj(stashError);

                // Resume execution at the catch handler's bytecode offset
                handlerFrame.IP = handler.CatchIP;
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private object? RunInner<TDebugMode>(int targetFrameCount = 0) where TDebugMode : struct
    {
        if (typeof(TDebugMode) == typeof(DebugOn) && _lastDebugLinePerFrame is null)
        {
            _lastDebugLinePerFrame = ArrayPool<int>.Shared.Rent(DefaultFrameDepth);
            _lastDebugLinePerFrame.AsSpan(0, DefaultFrameDepth).Fill(-1);
        }

        while (true)
        {
            ref CallFrame frame = ref _frames[_frameCount - 1];

            // Fetch the 32-bit instruction and advance the instruction pointer.
            uint inst = frame.Chunk.Code[frame.IP++];

            // ── Debug hook: check for breakpoints/stepping at statement boundaries ──
            // In the DebugOff specialization, this entire block is eliminated at JIT/AOT time.
            if (typeof(TDebugMode) == typeof(DebugOn))
            {
                IDebugger debugger = _debugger!;
                SourceSpan? span = frame.Chunk.SourceMap.GetSpan(frame.IP - 1);
                if (span is not null)
                {
                    int curLine = span.Value.StartLine;
                    int frameIdx = _frameCount - 1;
                    if (frameIdx >= _lastDebugLinePerFrame!.Length)
                    {
                        int oldLen = _lastDebugLinePerFrame.Length;
                        int[] newArray = ArrayPool<int>.Shared.Rent(_frames.Length);
                        _lastDebugLinePerFrame.AsSpan(0, oldLen).CopyTo(newArray);
                        ArrayPool<int>.Shared.Return(_lastDebugLinePerFrame);
                        _lastDebugLinePerFrame = newArray;
                        _lastDebugLinePerFrame.AsSpan(oldLen, _lastDebugLinePerFrame.Length - oldLen).Fill(-1);
                    }

                    if (curLine != _lastDebugLinePerFrame[frameIdx] || debugger.IsPauseRequested)
                    {
                        _lastDebugLinePerFrame[frameIdx] = curLine;
                        _context.CurrentSpan = span;
                        IDebugScope scope = BuildFrameScope(ref frame);
                        debugger.OnBeforeExecute(span.Value, scope, _debugThreadId);
                        // Re-acquire frame ref after debugger pause
                        frame = ref _frames[_frameCount - 1];
                    }
                }
            }

            switch (Instruction.GetOp(inst))
            {
                // ==================== Loads & Constants ====================
                case OpCode.LoadK:
                {
                    byte a = Instruction.GetA(inst);
                    ushort bx = Instruction.GetBx(inst);
                    _stack[frame.BaseSlot + a] = frame.Chunk.Constants[bx];
                    break;
                }
                case OpCode.LoadNull:
                {
                    byte a = Instruction.GetA(inst);
                    _stack[frame.BaseSlot + a] = StashValue.Null;
                    break;
                }
                case OpCode.LoadBool:
                {
                    byte a = Instruction.GetA(inst);
                    byte b = Instruction.GetB(inst);
                    byte c = Instruction.GetC(inst);
                    _stack[frame.BaseSlot + a] = StashValue.FromBool(b != 0);
                    if (c != 0) frame.IP++;
                    break;
                }
                case OpCode.Move:
                {
                    byte a = Instruction.GetA(inst);
                    byte b = Instruction.GetB(inst);
                    _stack[frame.BaseSlot + a] = _stack[frame.BaseSlot + b];
                    break;
                }

                // ==================== Variable Access ====================
                case OpCode.GetGlobal: ExecuteGetGlobal(ref frame, inst); break;
                case OpCode.SetGlobal: ExecuteSetGlobal(ref frame, inst); break;
                case OpCode.InitConstGlobal: ExecuteInitConstGlobal(ref frame, inst); break;
                case OpCode.GetUpval: ExecuteGetUpval(ref frame, inst); break;
                case OpCode.SetUpval: ExecuteSetUpval(ref frame, inst); break;
                case OpCode.CloseUpval: ExecuteCloseUpval(ref frame, inst); break;
                case OpCode.CheckNumeric: ExecuteCheckNumeric(ref frame, inst); break;

                // ==================== Arithmetic ====================
                case OpCode.Add: ExecuteAdd(ref frame, inst); break;
                case OpCode.Sub: ExecuteSub(ref frame, inst); break;
                case OpCode.Mul: ExecuteMul(ref frame, inst); break;
                case OpCode.Div: ExecuteDiv(ref frame, inst); break;
                case OpCode.Mod: ExecuteMod(ref frame, inst); break;
                case OpCode.Pow: ExecutePow(ref frame, inst); break;
                case OpCode.Neg: ExecuteNeg(ref frame, inst); break;
                case OpCode.AddI: ExecuteAddI(ref frame, inst); break;

                // ==================== Bitwise ====================
                case OpCode.BAnd: ExecuteBAnd(ref frame, inst); break;
                case OpCode.BOr: ExecuteBOr(ref frame, inst); break;
                case OpCode.BXor: ExecuteBXor(ref frame, inst); break;
                case OpCode.BNot: ExecuteBNot(ref frame, inst); break;
                case OpCode.Shl: ExecuteShl(ref frame, inst); break;
                case OpCode.Shr: ExecuteShr(ref frame, inst); break;

                // ==================== Comparison ====================
                case OpCode.Eq: ExecuteEq(ref frame, inst); break;
                case OpCode.Ne: ExecuteNe(ref frame, inst); break;
                case OpCode.Lt: ExecuteLt(ref frame, inst); break;
                case OpCode.Le: ExecuteLe(ref frame, inst); break;
                case OpCode.Gt: ExecuteGt(ref frame, inst); break;
                case OpCode.Ge: ExecuteGe(ref frame, inst); break;

                // ==================== Logic ====================
                case OpCode.Not:
                {
                    // ABC: R(A) = !IsTruthy(R(B))
                    byte a = Instruction.GetA(inst);
                    byte b = Instruction.GetB(inst);
                    _stack[frame.BaseSlot + a] = StashValue.FromBool(RuntimeOps.IsFalsy(_stack[frame.BaseSlot + b]));
                    break;
                }
                case OpCode.TestSet:
                {
                    // ABC: if IsTruthy(R(B)) == C then R(A) = R(B) else skip next
                    byte a = Instruction.GetA(inst);
                    byte b = Instruction.GetB(inst);
                    byte c = Instruction.GetC(inst);
                    StashValue rb = _stack[frame.BaseSlot + b];
                    bool truthy = !RuntimeOps.IsFalsy(rb);
                    if (truthy == (c != 0))
                        _stack[frame.BaseSlot + a] = rb;
                    else
                        frame.IP++;
                    break;
                }
                case OpCode.Test:
                {
                    // ABC: if IsTruthy(R(A)) != C then skip next
                    byte a = Instruction.GetA(inst);
                    byte c = Instruction.GetC(inst);
                    bool truthy = !RuntimeOps.IsFalsy(_stack[frame.BaseSlot + a]);
                    if (truthy != (c != 0))
                        frame.IP++;
                    break;
                }
                case OpCode.In: ExecuteIn(ref frame, inst); break;

                // ==================== Control Flow ====================
                case OpCode.Jmp:
                {
                    frame.IP += Instruction.GetSBx(inst);
                    break;
                }
                case OpCode.JmpFalse:
                {
                    byte a = Instruction.GetA(inst);
                    if (RuntimeOps.IsFalsy(_stack[frame.BaseSlot + a]))
                        frame.IP += Instruction.GetSBx(inst);
                    break;
                }
                case OpCode.JmpTrue:
                {
                    byte a = Instruction.GetA(inst);
                    if (!RuntimeOps.IsFalsy(_stack[frame.BaseSlot + a]))
                        frame.IP += Instruction.GetSBx(inst);
                    break;
                }
                case OpCode.Loop:
                {
                    // AsBx: IP += sBx (negative → backward jump) + cancellation + step limit
                    if ((++_loopCheckCounter & 0xFF) == 0)
                    {
                        _ct.ThrowIfCancellationRequested();
                        if (StepLimit > 0)
                        {
                            StepCount += 256;
                            if (StepCount >= StepLimit)
                                throw new Stash.Runtime.StepLimitExceededException(StepLimit);
                        }
                    }
                    frame.IP += Instruction.GetSBx(inst);
                    if (typeof(TDebugMode) == typeof(DebugOn) && _debugger!.IsPauseRequested)
                        _lastDebugLinePerFrame![_frameCount - 1] = -1;
                    break;
                }

                // ==================== Functions ====================
                case OpCode.Call: ExecuteCall<TDebugMode>(ref frame, inst); break;
                case OpCode.CallSpread: ExecuteCallSpread<TDebugMode>(ref frame, inst); break;
                case OpCode.CallBuiltIn: ExecuteCallBuiltIn<TDebugMode>(ref frame, inst); break;
                case OpCode.Return:
                    if (ExecuteReturn<TDebugMode>(ref frame, inst, targetFrameCount, out object? retResult))
                        return retResult;
                    break;
                case OpCode.Closure: ExecuteClosure(ref frame, inst); break;

                // ==================== Iteration ====================
                case OpCode.ForPrep: ExecuteForPrep(ref frame, inst); break;
                case OpCode.ForLoop: ExecuteForLoop(ref frame, inst); break;
                case OpCode.IterPrep: ExecuteIterPrep(ref frame, inst); break;
                case OpCode.IterLoop: ExecuteIterLoop(ref frame, inst); break;
                case OpCode.ForPrepII: ExecuteForPrepII(ref frame, inst); break;
                case OpCode.ForLoopII: ExecuteForLoopII(ref frame, inst); break;

                // ==================== Tables & Fields ====================
                case OpCode.GetTable: ExecuteGetTable(ref frame, inst); break;
                case OpCode.SetTable: ExecuteSetTable(ref frame, inst); break;
                case OpCode.GetField: ExecuteGetField(ref frame, inst); break;
                case OpCode.GetFieldIC: ExecuteGetFieldIC(ref frame, inst); break;
                case OpCode.SetField: ExecuteSetField(ref frame, inst); break;
                case OpCode.Self: ExecuteSelf(ref frame, inst); break;

                // ==================== Collections ====================
                case OpCode.NewArray: ExecuteNewArray(ref frame, inst); break;
                case OpCode.NewDict: ExecuteNewDict(ref frame, inst); break;
                case OpCode.NewRange: ExecuteNewRange(ref frame, inst); break;
                case OpCode.Spread: ExecuteSpread(ref frame, inst); break;
                case OpCode.Destructure: ExecuteDestructure(ref frame, inst); break;

                // ==================== Types & Closures ====================
                case OpCode.NewStruct: ExecuteNewStruct(ref frame, inst); break;
                case OpCode.TypeOf: ExecuteTypeOf(ref frame, inst); break;
                case OpCode.Is: ExecuteIs(ref frame, inst); break;
                case OpCode.StructDecl: ExecuteStructDecl(ref frame, inst); break;
                case OpCode.EnumDecl: ExecuteEnumDecl(ref frame, inst); break;
                case OpCode.IfaceDecl: ExecuteIfaceDecl(ref frame, inst); break;
                case OpCode.Extend: ExecuteExtend(ref frame, inst); break;

                // ==================== Error Handling ====================
                case OpCode.TryBegin:
                {
                    // ABx (signed offset via EmitJump+PatchJump): push handler; decode with GetSBx
                    byte errReg = Instruction.GetA(inst);
                    int catchOffset = Instruction.GetSBx(inst);
                    _exceptionHandlers.Add(new ExceptionHandler
                    {
                        CatchIP = frame.IP + catchOffset,
                        StackLevel = _sp,
                        FrameIndex = _frameCount - 1,
                        ErrorReg = errReg,
                    });
                    break;
                }
                case OpCode.TryEnd: ExecuteTryEnd(); break;
                case OpCode.Throw: ExecuteThrow(ref frame, inst); break;
                case OpCode.TryExpr: ExecuteTryExpr(ref frame, inst); break;

                // ==================== Strings ====================
                case OpCode.Interpolate: ExecuteInterpolate(ref frame, inst); break;

                // ==================== Shell Commands ====================
                case OpCode.Command: ExecuteCommand(ref frame, inst); break;
                case OpCode.Pipe: ExecutePipe(ref frame, inst); break;
                case OpCode.Redirect: ExecuteRedirect(ref frame, inst); break;

                // ==================== Module Import ====================
                case OpCode.Import: ExecuteImport(ref frame, inst); break;
                case OpCode.ImportAs: ExecuteImportAs(ref frame, inst); break;

                // ==================== Switch ====================
                case OpCode.Switch: ExecuteSwitch(ref frame, inst); break;

                // ==================== Elevation ====================
                case OpCode.ElevateBegin: ExecuteElevateBegin(ref frame, inst); break;
                case OpCode.ElevateEnd: ExecuteElevateEnd(ref frame, inst); break;

                // ==================== Retry ====================
                case OpCode.Retry: ExecuteRetry(ref frame, inst); break;

                // ==================== Timeout ====================
                case OpCode.Timeout: ExecuteTimeout(ref frame, inst); break;

                // ==================== Async ====================
                case OpCode.Await: ExecuteAwait(ref frame, inst); break;

                default:
                    throw new RuntimeError(
                        $"Unknown opcode {Instruction.GetOp(inst)} at offset {frame.IP - 1}.",
                        GetCurrentSpan(ref frame));
            }
        }
    }

}

