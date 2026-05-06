using System;
using System.Collections.Generic;
using System.Text;
using Stash.Parsing.AST;

namespace Stash.Bytecode;

public sealed partial class Compiler
{
    public object? VisitInterpolatedStringExpr(InterpolatedStringExpr expr)
    {
        byte dest = _destReg;
        var mergedParts = MergeInterpolationParts(expr.Parts);

        if (mergedParts.Count == 1 && mergedParts[0].folded is not null)
        {
            ushort idx = _builder.AddConstant(mergedParts[0].folded!);
            _builder.EmitABx(OpCode.LoadK, dest, idx);
            return null;
        }

        // NOTE: Single-expression elision (OPT-3) was considered but rejected — skipping
        // Interpolate bypasses Stringify, causing $"{42}" to return int 42 instead of "42".

        int partCount = mergedParts.Count;
        byte baseReg = _scope.ReserveRegs(1 + partCount);

        for (int i = 0; i < partCount; i++)
        {
            var (originalExpr, folded) = mergedParts[i];
            byte partReg = (byte)(baseReg + 1 + i);
            if (folded is not null)
            {
                ushort idx = _builder.AddConstant(folded);
                _builder.EmitABx(OpCode.LoadK, partReg, idx);
            }
            else
            {
                CompileExprTo(originalExpr!, partReg);
            }
        }

        _builder.EmitABC(OpCode.Interpolate, baseReg, (byte)partCount, 0);

        if (baseReg != dest)
        {
            _builder.EmitAB(OpCode.Move, dest, baseReg);
            _scope.FreeTempFrom(baseReg);
        }
        else if (partCount > 0)
        {
            _scope.FreeTempFrom((byte)(baseReg + 1));
        }

        return null;
    }

    private List<(Expr? originalExpr, string? folded)> MergeInterpolationParts(List<Expr> parts)
    {
        var result = new List<(Expr? originalExpr, string? folded)>(parts.Count);

        for (int i = 0; i < parts.Count; i++)
        {
            string? known = TryGetCompileTimeString(parts[i]);

            if (known is null)
            {
                result.Add((parts[i], null));
                continue;
            }

            var sb = new StringBuilder(known);
            int j = i + 1;
            while (j < parts.Count)
            {
                string? nextKnown = TryGetCompileTimeString(parts[j]);
                if (nextKnown is null) break;
                sb.Append(nextKnown);
                j++;
            }

            result.Add((null, sb.ToString()));
            i = j - 1;
        }

        return result;
    }

    private string? TryGetCompileTimeString(Expr expr)
    {
        if (TryEvaluateConstant(expr, out object? value))
            return CompileTimeStringify(value);
        return null;
    }

    public object? VisitCommandExpr(CommandExpr expr)
    {
        _builder.AddSourceMapping(expr.Span);
        byte dest = _destReg;
        var plan = AnalyzeCommandParts(expr.Parts);
        EmitProcessExecCall(dest, plan.Program, plan.Args,
            expr.IsStrict, expr.IsPassthrough, expr.Mode,
            Array.Empty<RedirectEntry>());
        return null;
    }

    public object? VisitPipeExpr(PipeExpr expr)
    {
        _builder.AddSourceMapping(expr.Span);
        byte dest = _destReg;
        var stages = FlattenPipeChain(expr);
        bool isStreaming = stages[0].Mode == CommandMode.Stream;
        bool lastStrict  = stages[stages.Count - 1].IsStrict;
        EmitProcessPipelineCall(dest, stages, isStreaming, lastStrict, Array.Empty<RedirectEntry>());
        return null;
    }

    private static List<CommandExpr> FlattenPipeChain(PipeExpr root)
    {
        var stages = new List<CommandExpr>();
        Expr current = root;
        while (current is PipeExpr pipe)
        {
            if (pipe.Right is not CommandExpr rightCmd)
                throw new CompileError("Pipe stages must be command expressions.", pipe.Right.Span);
            if (rightCmd.IsPassthrough)
                throw new CompileError("Passthrough command ($>(...) or $!>(...)) cannot appear in a pipe chain. Use $(cmd) or $!(cmd) instead.", rightCmd.Span);
            stages.Insert(0, rightCmd);
            current = pipe.Left;
        }
        if (current is not CommandExpr leftCmd)
            throw new CompileError("Pipe stages must be command expressions.", current.Span);
        if (leftCmd.IsPassthrough)
            throw new CompileError("Passthrough command ($>(...) or $!>(...)) cannot appear in a pipe chain. Use $(cmd) or $!(cmd) instead.", leftCmd.Span);
        stages.Insert(0, leftCmd);

        // Mixed-mode pipe chains are nonsensical: $<(a) piped into $(b) tries to read a
        // streaming handle as a string. Either ALL stages stream, or NONE do.
        bool anyStream = false, allStream = true;
        foreach (var s in stages)
        {
            if (s.Mode == CommandMode.Stream) anyStream = true;
            else allStream = false;
        }
        if (anyStream && !allStream)
        {
            // Find the first mismatched stage for the diagnostic location.
            CommandMode first = stages[0].Mode;
            foreach (var s in stages)
            {
                if (s.Mode != first)
                    throw new CompileError(
                        "mixed streaming and non-streaming command stages cannot appear in the same pipe chain — wrap the entire chain in a single $<(...) or use $(...)",
                        s.Span);
            }
        }
        return stages;
    }
}
