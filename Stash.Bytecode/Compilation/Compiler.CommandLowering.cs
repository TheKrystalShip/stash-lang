using System;
using System.Collections.Generic;
using System.Text;
using Stash.Common;
using Stash.Parsing.AST;
using Stash.Runtime.Types;

namespace Stash.Bytecode;

/// <summary>
/// Compile-time lowering of <see cref="CommandExpr"/>, <see cref="PipeExpr"/>, and
/// <see cref="RedirectExpr"/> into calls to <c>process.exec</c> / <c>process.pipeline</c>.
///
/// Phase B of "Safe Shell Interpolation — Sugar Over process.exec".
/// Literal text is tokenised here (whitespace split, source-level quote grouping, glob/tilde
/// flags). Interpolation slots become atomic argv elements (runtime splats arrays).
/// No runtime CommandParser invocation for source-originated commands.
/// </summary>
public sealed partial class Compiler
{
    // =========================================================================
    // ArgSlot hierarchy — compile-time representation of a single argv entry
    // =========================================================================

    private abstract record ArgSlot;

    /// <summary>
    /// A literal text token from source. When <see cref="ShouldExpand"/> is true the token
    /// came from unquoted source and receives tilde/glob expansion at runtime via
    /// <see cref="StashLiteralArg"/>. When false it was source-quoted and is passed verbatim.
    /// </summary>
    private sealed record LiteralSlot(string Text, bool ShouldExpand) : ArgSlot;

    /// <summary>
    /// A standalone <c>${expr}</c> interpolation slot. Runtime value is passed as a single
    /// argv entry; if it is an array, <c>process.exec</c> splats it element-wise.
    /// </summary>
    private sealed record InterpAtomicSlot(Expr Expression) : ArgSlot;

    /// <summary>
    /// Literal and interpolation fragments glued together without whitespace between them,
    /// e.g. <c>--flag=${value}</c>. Compiled as a single string via the Interpolate opcode.
    /// No splatting — always produces exactly one argv entry.
    /// </summary>
    private sealed record JoinedSlot(IReadOnlyList<JoinedFragment> Fragments) : ArgSlot;

    private abstract record JoinedFragment;
    private sealed record JoinedLiteral(string Text) : JoinedFragment;
    private sealed record JoinedInterp(Expr Expression) : JoinedFragment;

    // =========================================================================
    // CommandLoweringPlan
    // =========================================================================

    private sealed record CommandLoweringPlan(ArgSlot Program, IReadOnlyList<ArgSlot> Args);

    // =========================================================================
    // RedirectEntry — an accumulated redirect from a RedirectExpr chain
    // =========================================================================

    private sealed record RedirectEntry(RedirectStream Stream, Expr Target, bool Append);

    // =========================================================================
    // AnalyzeCommandParts — walk parts → produce CommandLoweringPlan
    // =========================================================================

    private CommandLoweringPlan AnalyzeCommandParts(IReadOnlyList<Expr> parts)
    {
        var currentJoined = new List<JoinedFragment>();
        bool currentJoinedWasQuoted = false; // valid when currentJoined has exactly one JoinedLiteral
        bool currentJoinedHasSpread = false; // true when the pending slot contains an explicit SpreadExpr
        SourceSpan currentJoinedSpreadSpan = default;
        var output = new List<ArgSlot>();

        foreach (var part in parts)
        {
            if (part is LiteralExpr litExpr && litExpr.Value is string s)
            {
                if (string.IsNullOrEmpty(s))
                    continue;

                bool startsWS = char.IsWhiteSpace(s[0]);
                bool endsWS = char.IsWhiteSpace(s[^1]);

                // If literal starts with whitespace and we have an in-progress joined slot,
                // flush it now (the whitespace is a slot boundary).
                if (startsWS && currentJoined.Count > 0)
                    FlushJoined(output, currentJoined, ref currentJoinedWasQuoted, ref currentJoinedHasSpread);

                var tokens = TokenizeCommandText(s);

                for (int i = 0; i < tokens.Count; i++)
                {
                    var (tok, wasQuoted) = tokens[i];
                    bool isLast = (i == tokens.Count - 1);

                    if (currentJoined.Count > 0)
                    {
                        // Append to the in-progress joined slot.
                        // If the pending slot contains an explicit spread, any additional
                        // fragment would produce a glued slot — reject.
                        if (currentJoinedHasSpread)
                            throw new CompileError(
                                "Explicit spread '${...}' is not allowed in a glued slot; spread must be a standalone interpolation.",
                                currentJoinedSpreadSpan);

                        currentJoined.Add(new JoinedLiteral(tok));
                        if (!isLast || endsWS)
                            FlushJoined(output, currentJoined, ref currentJoinedWasQuoted, ref currentJoinedHasSpread);
                        // else: last token, no trailing WS → leave in currentJoined for next part
                    }
                    else
                    {
                        if (isLast && !endsWS)
                        {
                            // This token may be glued to the next part (interpolation or literal).
                            currentJoined.Add(new JoinedLiteral(tok));
                            currentJoinedWasQuoted = wasQuoted;
                        }
                        else
                        {
                            // Complete standalone slot.
                            output.Add(new LiteralSlot(tok, !wasQuoted));
                        }
                    }
                }
            }
            else
            {
                if (part is SpreadExpr spreadPart)
                {
                    // Explicit spread: must be a standalone slot (not glued with adjacent content).
                    if (currentJoined.Count > 0)
                        throw new CompileError(
                            "Explicit spread '${...}' is not allowed in a glued slot; spread must be a standalone interpolation.",
                            spreadPart.Span);

                    currentJoined.Add(new JoinedInterp(spreadPart.Expression));
                    currentJoinedHasSpread = true;
                    currentJoinedSpreadSpan = spreadPart.Span;
                }
                else
                {
                    // Regular interpolation. If a spread is already pending, appending another
                    // fragment would produce a glued spread slot — reject.
                    if (currentJoinedHasSpread)
                        throw new CompileError(
                            "Explicit spread '${...}' is not allowed in a glued slot; spread must be a standalone interpolation.",
                            currentJoinedSpreadSpan);

                    currentJoined.Add(new JoinedInterp(part));
                }
                // Don't flush — wait for next literal or end-of-parts.
            }
        }

        // Flush any remaining joined fragments at end of parts.
        if (currentJoined.Count > 0)
            FlushJoined(output, currentJoined, ref currentJoinedWasQuoted, ref currentJoinedHasSpread);

        if (output.Count == 0)
            throw new CompileError("Command expression cannot be empty.", default);

        // First slot is the program name. Force ShouldExpand=false — no glob/tilde on program.
        ArgSlot program = output[0];
        if (program is LiteralSlot litProg)
            program = litProg with { ShouldExpand = false };

        var args = output.Count > 1
            ? (IReadOnlyList<ArgSlot>)output.GetRange(1, output.Count - 1)
            : Array.Empty<ArgSlot>();

        return new CommandLoweringPlan(program, args);
    }

    private static void FlushJoined(
        List<ArgSlot> output,
        List<JoinedFragment> currentJoined,
        ref bool wasQuoted,
        ref bool hasSpread)
    {
        if (currentJoined.Count == 0) return;

        ArgSlot slot;
        if (currentJoined.Count == 1)
        {
            slot = currentJoined[0] switch
            {
                JoinedLiteral jl => new LiteralSlot(jl.Text, !wasQuoted),
                JoinedInterp ji  => new InterpAtomicSlot(ji.Expression),
                _                => throw new InvalidOperationException("Unknown JoinedFragment type.")
            };
        }
        else
        {
            slot = new JoinedSlot(new List<JoinedFragment>(currentJoined));
        }

        output.Add(slot);
        currentJoined.Clear();
        wasQuoted = false;
        hasSpread = false;
    }

    // =========================================================================
    // TokenizeCommandText — quote-aware whitespace splitter
    // Returns list of (token, wasQuoted) pairs for the non-empty tokens in s.
    // =========================================================================

    private static List<(string Token, bool WasQuoted)> TokenizeCommandText(string s)
    {
        var result = new List<(string, bool)>();
        var buf = new StringBuilder();
        bool inSingle = false;
        bool inDouble = false;
        bool tokenHasQuote = false;

        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];

            if (inSingle)
            {
                if (c == '\'')
                {
                    inSingle = false;
                    tokenHasQuote = true;
                }
                else
                {
                    buf.Append(c);
                }
                continue;
            }

            if (inDouble)
            {
                if (c == '"')
                {
                    inDouble = false;
                    tokenHasQuote = true;
                }
                else if (c == '\\' && i + 1 < s.Length)
                {
                    // Preserve escape sequences verbatim (same as CommandParser.ParseWithQuotedFlags).
                    // The target program receives the literal backslash sequence.
                    buf.Append('\\');
                    buf.Append(s[i + 1]);
                    i++;
                }
                else
                {
                    buf.Append(c);
                }
                continue;
            }

            // Unquoted.
            if (c == '\'')
            {
                inSingle = true;
                tokenHasQuote = true;
            }
            else if (c == '"')
            {
                inDouble = true;
                tokenHasQuote = true;
            }
            else if (char.IsWhiteSpace(c))
            {
                if (buf.Length > 0)
                {
                    result.Add((buf.ToString(), tokenHasQuote));
                    buf.Clear();
                    tokenHasQuote = false;
                }
            }
            else
            {
                buf.Append(c);
            }
        }

        if (buf.Length > 0)
            result.Add((buf.ToString(), tokenHasQuote));

        return result;
    }

    // =========================================================================
    // Emit helpers
    // =========================================================================

    /// <summary>Compile a single ArgSlot into <paramref name="destReg"/>.</summary>
    private void EmitArgSlot(ArgSlot slot, byte destReg)
    {
        switch (slot)
        {
            case LiteralSlot(string text, true):
            {
                // Unquoted literal — store as StashLiteralArg so process.exec applies tilde+glob.
                ushort idx = _builder.AddConstant(new StashLiteralArg(text, shouldExpand: true));
                _builder.EmitABx(OpCode.LoadK, destReg, idx);
                break;
            }
            case LiteralSlot(string text, false):
            {
                // Quoted literal — pass as plain string, verbatim.
                ushort idx = _builder.AddConstant(text);
                _builder.EmitABx(OpCode.LoadK, destReg, idx);
                break;
            }
            case InterpAtomicSlot(Expr expr):
            {
                CompileExprTo(expr, destReg);
                break;
            }
            case JoinedSlot joinedSlot:
            {
                EmitJoinedSlot(joinedSlot.Fragments, destReg);
                break;
            }
        }
    }

    /// <summary>
    /// Emit an Interpolate opcode sequence that concatenates the fragments into a string,
    /// storing the result in <paramref name="destReg"/>.
    /// </summary>
    private void EmitJoinedSlot(IReadOnlyList<JoinedFragment> fragments, byte destReg)
    {
        int count = fragments.Count;
        byte baseReg = _scope.ReserveRegs(1 + count);

        for (int i = 0; i < count; i++)
        {
            byte partReg = (byte)(baseReg + 1 + i);
            switch (fragments[i])
            {
                case JoinedLiteral(string text):
                {
                    ushort idx = _builder.AddConstant(text);
                    _builder.EmitABx(OpCode.LoadK, partReg, idx);
                    break;
                }
                case JoinedInterp(Expr expr):
                {
                    CompileExprTo(expr, partReg);
                    break;
                }
            }
        }

        _builder.EmitABC(OpCode.Interpolate, baseReg, (byte)count, 0);

        if (baseReg != destReg)
        {
            _builder.EmitAB(OpCode.Move, destReg, baseReg);
            _scope.FreeTempFrom(baseReg);
        }
        else if (count > 0)
        {
            _scope.FreeTempFrom((byte)(baseReg + 1));
        }
    }

    /// <summary>
    /// Emit a NewArray instruction whose elements are the compiled <paramref name="argSlots"/>,
    /// storing the resulting array in <paramref name="destReg"/>.
    /// </summary>
    private void EmitArgsArray(IReadOnlyList<ArgSlot> argSlots, byte destReg)
    {
        int count = argSlots.Count;

        if (count == 0)
        {
            _builder.EmitABC(OpCode.NewArray, destReg, 0, 0);
            return;
        }

        byte baseReg = _scope.ReserveRegs(1 + count);
        for (int i = 0; i < count; i++)
            EmitArgSlot(argSlots[i], (byte)(baseReg + 1 + i));

        _builder.EmitABC(OpCode.NewArray, baseReg, (byte)count, 0);

        if (baseReg != destReg)
        {
            _builder.EmitAB(OpCode.Move, destReg, baseReg);
            _scope.FreeTempFrom(baseReg);
        }
        else if (count > 0)
        {
            _scope.FreeTempFrom((byte)(baseReg + 1));
        }
    }

    /// <summary>
    /// Emit the ExecOptions dict (or null for default opts) into <paramref name="destReg"/>.
    /// </summary>
    private void EmitOptsDict(
        bool isStrict,
        bool isPassthrough,
        CommandMode mode,
        IReadOnlyList<RedirectEntry> redirects,
        byte destReg)
    {
        bool hasMode     = isPassthrough || mode == CommandMode.Stream;
        bool hasStrict   = isStrict;
        bool hasRedirect = redirects.Count > 0;
        int entryCount   = (hasMode ? 1 : 0) + (hasStrict ? 1 : 0) + (hasRedirect ? 1 : 0);

        if (entryCount == 0)
        {
            _builder.EmitA(OpCode.LoadNull, destReg);
            return;
        }

        // NewDict spec: R(A) = new dict with B k-v pairs from R(A+1)..R(A+2*B).
        byte baseReg = _scope.ReserveRegs(1 + 2 * entryCount);
        int ei = 0;

        if (hasMode)
        {
            string modeStr = isPassthrough ? "Passthrough" : "Stream";
            ushort keyIdx = _builder.AddConstant("mode");
            _builder.EmitABx(OpCode.LoadK, (byte)(baseReg + 1 + 2 * ei), keyIdx);
            ushort valIdx = _builder.AddConstant(modeStr);
            _builder.EmitABx(OpCode.LoadK, (byte)(baseReg + 2 + 2 * ei), valIdx);
            ei++;
        }

        if (hasStrict)
        {
            ushort keyIdx = _builder.AddConstant("strict");
            _builder.EmitABx(OpCode.LoadK, (byte)(baseReg + 1 + 2 * ei), keyIdx);
            _builder.EmitABC(OpCode.LoadBool, (byte)(baseReg + 2 + 2 * ei), 1, 0);
            ei++;
        }

        if (hasRedirect)
        {
            ushort keyIdx = _builder.AddConstant("redirect");
            _builder.EmitABx(OpCode.LoadK, (byte)(baseReg + 1 + 2 * ei), keyIdx);
            EmitRedirectValue(redirects, (byte)(baseReg + 2 + 2 * ei));
            ei++;
        }

        _builder.EmitABC(OpCode.NewDict, baseReg, (byte)entryCount, 0);

        if (baseReg != destReg)
        {
            _builder.EmitAB(OpCode.Move, destReg, baseReg);
            _scope.FreeTempFrom(baseReg);
        }
        else if (entryCount > 0)
        {
            _scope.FreeTempFrom((byte)(baseReg + 1));
        }
    }

    private void EmitRedirectValue(IReadOnlyList<RedirectEntry> redirects, byte destReg)
    {
        if (redirects.Count == 1)
        {
            EmitSingleRedirectDict(redirects[0], destReg);
        }
        else
        {
            // Multiple redirects: emit array of single-redirect dicts.
            byte baseReg = _scope.ReserveRegs(1 + redirects.Count);
            for (int i = 0; i < redirects.Count; i++)
                EmitSingleRedirectDict(redirects[i], (byte)(baseReg + 1 + i));
            _builder.EmitABC(OpCode.NewArray, baseReg, (byte)redirects.Count, 0);
            if (baseReg != destReg)
            {
                _builder.EmitAB(OpCode.Move, destReg, baseReg);
                _scope.FreeTempFrom(baseReg);
            }
            else if (redirects.Count > 0)
            {
                _scope.FreeTempFrom((byte)(baseReg + 1));
            }
        }
    }

    private void EmitSingleRedirectDict(RedirectEntry redirect, byte destReg)
    {
        // { stream: "stdout"|"stderr"|"all", target: expr, append: bool }
        // 3 key-value pairs → 6 element registers + 1 base = 7 total.
        byte baseReg = _scope.ReserveRegs(1 + 6);

        string streamStr = redirect.Stream switch
        {
            RedirectStream.Stderr => "stderr",
            RedirectStream.All    => "all",
            _                     => "stdout"
        };

        ushort k0 = _builder.AddConstant("stream");
        _builder.EmitABx(OpCode.LoadK, (byte)(baseReg + 1), k0);
        ushort v0 = _builder.AddConstant(streamStr);
        _builder.EmitABx(OpCode.LoadK, (byte)(baseReg + 2), v0);

        ushort k1 = _builder.AddConstant("target");
        _builder.EmitABx(OpCode.LoadK, (byte)(baseReg + 3), k1);
        CompileExprTo(redirect.Target, (byte)(baseReg + 4));

        ushort k2 = _builder.AddConstant("append");
        _builder.EmitABx(OpCode.LoadK, (byte)(baseReg + 5), k2);
        _builder.EmitABC(OpCode.LoadBool, (byte)(baseReg + 6), redirect.Append ? (byte)1 : (byte)0, 0);

        _builder.EmitABC(OpCode.NewDict, baseReg, 3, 0);

        if (baseReg != destReg)
        {
            _builder.EmitAB(OpCode.Move, destReg, baseReg);
            _scope.FreeTempFrom(baseReg);
        }
        else
        {
            _scope.FreeTempFrom((byte)(baseReg + 1));
        }
    }

    // =========================================================================
    // process.exec(...) call emission
    // =========================================================================

    /// <summary>
    /// Lowers a single command expression into a <c>process.exec(program, args, opts?)</c>
    /// CallBuiltIn sequence, result in <paramref name="destReg"/>.
    /// </summary>
    private void EmitProcessExecCall(
        byte destReg,
        ArgSlot programSlot,
        IReadOnlyList<ArgSlot> argSlots,
        bool isStrict,
        bool isPassthrough,
        CommandMode mode,
        IReadOnlyList<RedirectEntry> redirects)
    {
        bool needsOpts = isStrict || isPassthrough || mode == CommandMode.Stream || redirects.Count > 0;
        int argc = needsOpts ? 3 : 2;

        byte calleeReg = ReserveCallWindow(destReg, argc);

        // Get the process namespace into nsReg (above the call window).
        byte nsReg = _scope.AllocTemp();
        ushort processSlot = _globalSlots.GetOrAllocate("process");
        _builder.EmitABx(OpCode.GetGlobal, nsReg, processSlot);

        // arg 0: program
        EmitArgSlot(programSlot, (byte)(calleeReg + 1));

        // arg 1: args array
        EmitArgsArray(argSlots, (byte)(calleeReg + 2));

        // arg 2: opts dict (if needed)
        if (needsOpts)
            EmitOptsDict(isStrict, isPassthrough, mode, redirects, (byte)(calleeReg + 3));

        // Emit CallBuiltIn + companion IC slot.
        ushort nameIdx = _builder.AddConstant("exec");
        ushort icSlot  = _builder.AllocateICSlot(nameIdx);
        _builder.EmitABC(OpCode.CallBuiltIn, calleeReg, nsReg, (byte)argc);
        _builder.EmitRaw((uint)icSlot);

        _scope.FreeTemp(nsReg);

        if (calleeReg != destReg)
        {
            _builder.EmitAB(OpCode.Move, destReg, calleeReg);
            _scope.FreeTempFrom(calleeReg);
        }
        else if (argc > 0)
        {
            _scope.FreeTempFrom((byte)(calleeReg + 1));
        }
    }

    // =========================================================================
    // process.pipeline(...) call emission
    // =========================================================================

    /// <summary>
    /// Lowers a pipe chain into a <c>process.pipeline(stages, opts?)</c> CallBuiltIn sequence.
    /// </summary>
    private void EmitProcessPipelineCall(
        byte destReg,
        IReadOnlyList<CommandExpr> stages,
        bool isStreaming,
        bool isStrict,
        IReadOnlyList<RedirectEntry> redirects)
    {
        // process.pipeline(stages, opts) — always 2 args.
        const int argc = 2;
        byte calleeReg = ReserveCallWindow(destReg, argc);

        byte nsReg = _scope.AllocTemp();
        ushort processSlot = _globalSlots.GetOrAllocate("process");
        _builder.EmitABx(OpCode.GetGlobal, nsReg, processSlot);

        // arg 0: stages array
        EmitStagesArray(stages, (byte)(calleeReg + 1));

        // arg 1: opts dict
        CommandMode pipelineMode = isStreaming ? CommandMode.Stream : CommandMode.Capture;
        EmitOptsDict(isStrict, isPassthrough: false, pipelineMode, redirects, (byte)(calleeReg + 2));

        ushort nameIdx = _builder.AddConstant("pipeline");
        ushort icSlot  = _builder.AllocateICSlot(nameIdx);
        _builder.EmitABC(OpCode.CallBuiltIn, calleeReg, nsReg, argc);
        _builder.EmitRaw((uint)icSlot);

        _scope.FreeTemp(nsReg);

        if (calleeReg != destReg)
        {
            _builder.EmitAB(OpCode.Move, destReg, calleeReg);
            _scope.FreeTempFrom(calleeReg);
        }
        else if (argc > 0)
        {
            _scope.FreeTempFrom((byte)(calleeReg + 1));
        }
    }

    private void EmitStagesArray(IReadOnlyList<CommandExpr> stages, byte destReg)
    {
        int count = stages.Count;
        byte baseReg = _scope.ReserveRegs(1 + count);

        for (int si = 0; si < count; si++)
        {
            var plan = AnalyzeCommandParts(stages[si].Parts);
            EmitPipelineStageDict(plan, (byte)(baseReg + 1 + si));
        }

        _builder.EmitABC(OpCode.NewArray, baseReg, (byte)count, 0);

        if (baseReg != destReg)
        {
            _builder.EmitAB(OpCode.Move, destReg, baseReg);
            _scope.FreeTempFrom(baseReg);
        }
        else if (count > 0)
        {
            _scope.FreeTempFrom((byte)(baseReg + 1));
        }
    }

    private void EmitPipelineStageDict(CommandLoweringPlan plan, byte destReg)
    {
        // { program: <slot>, args: <array> } — 2 key-value pairs → 4 element regs + base = 5.
        byte baseReg = _scope.ReserveRegs(1 + 4);

        ushort k0 = _builder.AddConstant("program");
        _builder.EmitABx(OpCode.LoadK, (byte)(baseReg + 1), k0);
        EmitArgSlot(plan.Program, (byte)(baseReg + 2));

        ushort k1 = _builder.AddConstant("args");
        _builder.EmitABx(OpCode.LoadK, (byte)(baseReg + 3), k1);
        EmitArgsArray(plan.Args, (byte)(baseReg + 4));

        _builder.EmitABC(OpCode.NewDict, baseReg, 2, 0);

        if (baseReg != destReg)
        {
            _builder.EmitAB(OpCode.Move, destReg, baseReg);
            _scope.FreeTempFrom(baseReg);
        }
        else
        {
            _scope.FreeTempFrom((byte)(baseReg + 1));
        }
    }
}
