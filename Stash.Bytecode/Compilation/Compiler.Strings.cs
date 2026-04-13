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

        int partCount = expr.Parts.Count;
        byte baseReg = _scope.ReserveRegs(1 + partCount);

        for (int i = 0; i < partCount; i++)
            CompileExprTo(expr.Parts[i], (byte)(baseReg + 1 + i));

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
        byte leftReg = CompileExpr(expr.Left);
        byte rightReg = CompileExpr(expr.Right);
        _builder.EmitABC(OpCode.Pipe, dest, leftReg, rightReg);
        _scope.FreeTemp(rightReg);
        _scope.FreeTemp(leftReg);
        return null;
    }
}
