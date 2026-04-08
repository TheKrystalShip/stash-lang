using System;
using System.Collections.Generic;
using System.Text;
using Stash.Common;
using Stash.Lexing;
using Stash.Parsing.AST;
using Stash.Runtime.Types;

namespace Stash.Bytecode;

/// <summary>
/// String interpolation and command expression visitor implementations.
/// </summary>
public sealed partial class Compiler
{
    /// <inheritdoc />
    public object? VisitInterpolatedStringExpr(InterpolatedStringExpr expr)
    {
        // Try to fold adjacent compile-time-known parts.
        // A part is "known" if it is a string LiteralExpr, or an IdentifierExpr
        // referencing a const global whose tracked value is a string.
        List<Expr> parts = expr.Parts;

        // Fast path: try full fold — if every part is known, emit a single Const.
        // Otherwise, merge adjacent known parts to reduce the interpolate count.
        var mergedParts = MergeInterpolationParts(parts);

        if (mergedParts.Count == 1 && mergedParts[0].folded is not null)
        {
            // Fully folded to a single string constant
            ushort idx = _builder.AddConstant(mergedParts[0].folded!);
            _builder.Emit(OpCode.Const, idx);
            return null;
        }

        foreach (var (originalExpr, folded) in mergedParts)
        {
            if (folded is not null)
            {
                // Merged compile-time-known segment
                ushort idx = _builder.AddConstant(folded);
                _builder.Emit(OpCode.Const, idx);
            }
            else
            {
                // Runtime expression — compile normally
                CompileExpr(originalExpr!);
            }
        }

        _builder.Emit(OpCode.Interpolate, (ushort)mergedParts.Count);
        return null;
    }

    /// <summary>
    /// Merges adjacent compile-time-known parts of an interpolated string.
    /// Returns a list where each element is either a folded string constant
    /// or a reference to the original expression that must be compiled at runtime.
    /// </summary>
    private List<(Expr? originalExpr, string? folded)> MergeInterpolationParts(List<Expr> parts)
    {
        var result = new List<(Expr? originalExpr, string? folded)>(parts.Count);

        for (int i = 0; i < parts.Count; i++)
        {
            string? known = TryGetCompileTimeString(parts[i]);

            if (known is null)
            {
                // Runtime part — emit as-is
                result.Add((parts[i], null));
                continue;
            }

            // Start merging adjacent known parts
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
            i = j - 1; // advance past merged parts (for-loop will i++)
        }

        return result;
    }

    /// <summary>
    /// Returns the compile-time string value of an expression if it can be determined,
    /// or null if the expression must be evaluated at runtime.
    /// </summary>
    private string? TryGetCompileTimeString(Expr expr)
    {
        if (TryEvaluateConstant(expr, out object? value))
            return CompileTimeStringify(value);

        return null;
    }

    /// <inheritdoc />
    public object? VisitCommandExpr(CommandExpr expr)
    {
        _builder.AddSourceMapping(expr.Span);
        foreach (Expr part in expr.Parts)
        {
            CompileExpr(part);
        }

        var metadata = new CommandMetadata(expr.Parts.Count, expr.IsPassthrough, expr.IsStrict);
        ushort metaIdx = _builder.AddConstant(metadata);
        _builder.Emit(OpCode.Command, metaIdx);
        return null;
    }

    /// <inheritdoc />
    public object? VisitPipeExpr(PipeExpr expr)
    {
        _builder.AddSourceMapping(expr.Span);
        CompileExpr(expr.Left);
        CompileExpr(expr.Right);
        _builder.Emit(OpCode.Pipe);
        return null;
    }

}
