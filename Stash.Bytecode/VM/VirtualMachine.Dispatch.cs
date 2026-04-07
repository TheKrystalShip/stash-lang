using System;
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

    private object? RunInner(int targetFrameCount = 0)
    {
        IDebugger? debugger = _debugger;
        // Per-frame last-debug-line tracking prevents re-triggering a breakpoint
        // at line N in a caller frame after returning from a callee that also ended
        // at line N (or any different line), which would otherwise happen because the
        // single lastDebugLine variable crosses frame boundaries.
        if (debugger is not null && _lastDebugLinePerFrame is null)
        {
            _lastDebugLinePerFrame = new int[DefaultFrameDepth];
            _lastDebugLinePerFrame.AsSpan().Fill(-1);
        }

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
                    int curLine = span.Value.StartLine;
                    int frameIdx = _frameCount - 1;
                    if (frameIdx >= _lastDebugLinePerFrame!.Length)
                    {
                        int oldLen = _lastDebugLinePerFrame.Length;
                        Array.Resize(ref _lastDebugLinePerFrame, _frames.Length);
                        _lastDebugLinePerFrame.AsSpan(oldLen).Fill(-1);
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

            switch ((OpCode)instruction)
            {
                // ==================== Constants & Literals ====================
                case OpCode.Const: ExecuteConst(ref frame); break;
                case OpCode.Null: Push(StashValue.Null); break;
                case OpCode.True: Push(StashValue.True); break;
                case OpCode.False: Push(StashValue.False); break;

                // ==================== Stack Manipulation ====================
                case OpCode.Pop: _sp--; break;
                case OpCode.Dup: Push(_stack[_sp - 1]); break;

                // ==================== Variable Access ====================
                case OpCode.LoadLocal: ExecuteLoadLocal(ref frame); break;
                case OpCode.StoreLocal: ExecuteStoreLocal(ref frame); break;
                case OpCode.LoadGlobal: ExecuteLoadGlobal(ref frame); break;
                case OpCode.StoreGlobal: ExecuteStoreGlobal(ref frame); break;
                case OpCode.InitConstGlobal: ExecuteInitConstGlobal(ref frame); break;
                case OpCode.LoadUpvalue: ExecuteLoadUpvalue(ref frame); break;
                case OpCode.StoreUpvalue: ExecuteStoreUpvalue(ref frame); break;

                // ==================== Arithmetic ====================
                case OpCode.Add: ExecuteAdd(ref frame); break;
                case OpCode.Subtract: ExecuteSubtract(ref frame); break;
                case OpCode.Multiply: ExecuteMultiply(ref frame); break;
                case OpCode.Divide: ExecuteDivide(ref frame); break;
                case OpCode.Modulo: ExecuteModulo(ref frame); break;
                case OpCode.Power: ExecutePower(ref frame); break;
                case OpCode.Negate: ExecuteNegate(ref frame); break;

                // ==================== Bitwise ====================
                case OpCode.BitAnd: ExecuteBitAnd(ref frame); break;
                case OpCode.BitOr: ExecuteBitOr(ref frame); break;
                case OpCode.BitXor: ExecuteBitXor(ref frame); break;
                case OpCode.BitNot: Push(RuntimeOps.BitNot(Pop(), GetCurrentSpan(ref frame))); break;
                case OpCode.ShiftLeft: ExecuteShiftLeft(ref frame); break;
                case OpCode.ShiftRight: ExecuteShiftRight(ref frame); break;

                // ==================== Comparison ====================
                case OpCode.Equal: ExecuteEqual(ref frame); break;
                case OpCode.NotEqual: ExecuteNotEqual(ref frame); break;
                case OpCode.LessThan: ExecuteLessThan(ref frame); break;
                case OpCode.LessEqual: ExecuteLessEqual(ref frame); break;
                case OpCode.GreaterThan: ExecuteGreaterThan(ref frame); break;
                case OpCode.GreaterEqual: ExecuteGreaterEqual(ref frame); break;

                // ==================== Logic ====================
                case OpCode.Not: Push(StashValue.FromBool(RuntimeOps.IsFalsy(Pop()))); break;
                case OpCode.And: ExecuteAnd(ref frame); break;
                case OpCode.Or: ExecuteOr(ref frame); break;
                case OpCode.NullCoalesce: ExecuteNullCoalesce(ref frame); break;

                // ==================== Control Flow ====================
                case OpCode.Jump: ExecuteJump(ref frame); break;
                case OpCode.JumpTrue: ExecuteJumpTrue(ref frame); break;
                case OpCode.JumpFalse: ExecuteJumpFalse(ref frame); break;
                case OpCode.Loop:
                    ExecuteLoop(ref frame);
                    if (debugger is not null && debugger.IsPauseRequested)
                    {
                        _lastDebugLinePerFrame![_frameCount - 1] = -1;
                    }

                    break;

                // ==================== Functions ====================
                case OpCode.Call: ExecuteCall(ref frame, debugger); break;
                case OpCode.ArgMark: Push(StashValue.FromObj(_argSentinel)); break;
                case OpCode.CallSpread: ExecuteCallSpread(ref frame, debugger); break;
                case OpCode.Closure: ExecuteClosure(ref frame); break;
                case OpCode.Return:
                    if (ExecuteReturn(ref frame, targetFrameCount, debugger, out object? retResult))
                    {
                        return retResult;
                    }

                    break;

                // ==================== Collections ====================
                case OpCode.Array: ExecuteArray(ref frame); break;
                case OpCode.Dict: ExecuteDict(ref frame); break;
                case OpCode.Range: ExecuteRange(ref frame); break;
                case OpCode.Spread: ExecuteSpread(ref frame); break;

                // ==================== Object Access ====================
                case OpCode.GetField: ExecuteGetField(ref frame); break;
                case OpCode.SetField: ExecuteSetField(ref frame); break;
                case OpCode.GetIndex: ExecuteGetIndex(ref frame); break;
                case OpCode.SetIndex: ExecuteSetIndex(ref frame); break;
                case OpCode.StructInit: ExecuteStructInit(ref frame); break;

                // ==================== Strings ====================
                case OpCode.Interpolate: ExecuteInterpolate(ref frame); break;

                // ==================== Type Operations ====================
                case OpCode.Is: ExecuteIs(ref frame); break;
                case OpCode.StructDecl: ExecuteStructDecl(ref frame); break;
                case OpCode.EnumDecl: ExecuteEnumDecl(ref frame); break;
                case OpCode.InterfaceDecl: ExecuteInterfaceDecl(ref frame); break;
                case OpCode.Extend: ExecuteExtend(ref frame); break;

                // ==================== Error Handling ====================
                case OpCode.Throw: ExecuteThrow(ref frame); break;
                case OpCode.TryBegin: ExecuteTryBegin(ref frame); break;
                case OpCode.TryEnd: ExecuteTryEnd(); break;

                // ==================== Switch ====================
                case OpCode.Switch: ExecuteSwitch(ref frame); break;

                // ==================== Iteration ====================
                case OpCode.Iterator: ExecuteIterator(ref frame); break;
                case OpCode.Iterate: ExecuteIterate(ref frame); break;

                // ==================== Async ====================
                case OpCode.Await: ExecuteAwait(ref frame); break;
                case OpCode.CloseUpvalue: ExecuteCloseUpvalue(ref frame); break;

                // ==================== Containment ====================
                case OpCode.In: ExecuteIn(ref frame); break;

                // ==================== Shell Commands ====================
                case OpCode.Command: ExecuteCommand(ref frame); break;
                case OpCode.Pipe: ExecutePipe(ref frame); break;
                case OpCode.Redirect: ExecuteRedirect(ref frame); break;

                // ==================== Module Import ====================
                case OpCode.Import: ExecuteImport(ref frame); break;
                case OpCode.ImportAs: ExecuteImportAs(ref frame); break;

                // ==================== Destructure ====================
                case OpCode.Destructure: ExecuteDestructure(ref frame); break;

                // ==================== Elevation ====================
                case OpCode.ElevateBegin: ExecuteElevateBegin(ref frame); break;
                case OpCode.ElevateEnd: ExecuteElevateEnd(ref frame); break;

                // ==================== Retry ====================
                case OpCode.Retry: Push(ExecuteRetry(ref frame)); break;

                // ==================== Deferred / Not-yet-implemented ====================
                case OpCode.CheckNumeric: ExecuteCheckNumeric(ref frame); break;

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

}
