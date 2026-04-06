using System;
using System.Collections.Generic;
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
        foreach (Expr part in expr.Parts)
        {
            CompileExpr(part);
        }

        _builder.Emit(OpCode.Interpolate, (ushort)expr.Parts.Count);
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
