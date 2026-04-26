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
        byte dest = _destReg;
        _builder.AddSourceMapping(expr.Span);

        // OPT: Merge consecutive constant parts (same as interpolated strings)
        var mergedParts = MergeInterpolationParts(expr.Parts);
        int partCount = mergedParts.Count;
        byte baseReg = _scope.ReserveRegs(1 + partCount);

        for (int i = 0; i < partCount; i++)
        {
            byte partReg = (byte)(baseReg + 1 + i);
            var (originalExpr, folded) = mergedParts[i];
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

        byte flags = 0;
        if (expr.IsPassthrough) flags |= 0x01;
        if (expr.IsStrict)      flags |= 0x02;

        _builder.EmitABC(OpCode.Command, baseReg, (byte)partCount, flags);

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

    public object? VisitPipeExpr(PipeExpr expr)
    {
        byte dest = _destReg;
        _builder.AddSourceMapping(expr.Span);

        // Flatten the left-associative pipe chain into a list of CommandExprs
        var stages = FlattenPipeChain(expr);

        // Compute merged parts per stage and validate
        var allMergedParts = new List<List<(Expr? originalExpr, string? folded)>>(stages.Count);
        int totalParts = 0;
        foreach (var stage in stages)
        {
            var merged = MergeInterpolationParts(stage.Parts);
            allMergedParts.Add(merged);
            totalParts += merged.Count;
        }

        // Reserve a contiguous block for all parts across all stages
        // (use at least 1 to avoid zero-size reservation issues)
        byte partsBase = _scope.ReserveRegs(totalParts > 0 ? totalParts : 1);

        // Compile all parts into the register block
        int regOffset = 0;
        for (int si = 0; si < stages.Count; si++)
        {
            foreach (var (originalExpr, folded) in allMergedParts[si])
            {
                byte partReg = (byte)(partsBase + regOffset++);
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
        }

        // Emit PipeChain instruction followed by B companion words
        _builder.EmitABC(OpCode.PipeChain, dest, (byte)stages.Count, partsBase);
        for (int si = 0; si < stages.Count; si++)
        {
            byte flags = stages[si].IsStrict ? (byte)0x01 : (byte)0x00;
            uint companion = ((uint)allMergedParts[si].Count << 8) | flags;
            _builder.EmitRaw(companion);
        }

        // Free the parts register block
        _scope.FreeTempFrom(partsBase);

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
        return stages;
    }
}
