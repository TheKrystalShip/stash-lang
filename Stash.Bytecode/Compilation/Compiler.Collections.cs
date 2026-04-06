using System;
using System.Collections.Generic;
using Stash.Common;
using Stash.Lexing;
using Stash.Parsing.AST;
using Stash.Runtime.Types;

namespace Stash.Bytecode;

/// <summary>
/// Collection expression visitor implementations.
/// </summary>
public sealed partial class Compiler
{
    /// <inheritdoc />
    public object? VisitArrayExpr(ArrayExpr expr)
    {
        foreach (Expr element in expr.Elements)
        {
            CompileExpr(element);
        }

        _builder.Emit(OpCode.Array, (ushort)expr.Elements.Count);
        return null;
    }

    /// <inheritdoc />
    public object? VisitIndexExpr(IndexExpr expr)
    {
        _builder.AddSourceMapping(expr.BracketSpan);
        CompileExpr(expr.Object);
        CompileExpr(expr.Index);
        _builder.Emit(OpCode.GetIndex);
        return null;
    }

    /// <inheritdoc />
    public object? VisitIndexAssignExpr(IndexAssignExpr expr)
    {
        _builder.AddSourceMapping(expr.BracketSpan);
        CompileExpr(expr.Object);
        CompileExpr(expr.Index);
        CompileExpr(expr.Value);
        _builder.Emit(OpCode.SetIndex);
        return null;
    }

    /// <inheritdoc />
    public object? VisitStructInitExpr(StructInitExpr expr)
    {
        _builder.AddSourceMapping(expr.Span);

        // Load the struct type
        if (expr.Target != null)
        {
            CompileExpr(expr.Target);
        }
        else
        {
            ushort nameIdx = _builder.AddConstant(expr.Name.Lexeme);
            _builder.Emit(OpCode.LoadGlobal, nameIdx);
        }

        // Push each field name string followed by its value
        foreach ((Token field, Expr value) in expr.FieldValues)
        {
            ushort fieldIdx = _builder.AddConstant(field.Lexeme);
            _builder.Emit(OpCode.Const, fieldIdx);
            CompileExpr(value);
        }

        _builder.Emit(OpCode.StructInit, (ushort)expr.FieldValues.Count);
        return null;
    }

    /// <inheritdoc />
    public object? VisitRangeExpr(RangeExpr expr)
    {
        CompileExpr(expr.Start);
        CompileExpr(expr.End);
        if (expr.Step != null)
        {
            CompileExpr(expr.Step);
        }
        else
        {
            _builder.Emit(OpCode.Null);  // null step = VM uses default (1)
        }

        _builder.Emit(OpCode.Range);
        return null;
    }

    /// <inheritdoc />
    public object? VisitDictLiteralExpr(DictLiteralExpr expr)
    {
        foreach ((Token? key, Expr value) in expr.Entries)
        {
            if (key != null)
            {
                ushort keyIdx = _builder.AddConstant(key.Lexeme);
                _builder.Emit(OpCode.Const, keyIdx);
            }
            else
            {
                // Spread entry: push a null marker as the "key" so count*2 stays consistent
                _builder.Emit(OpCode.Null);
            }
            CompileExpr(value);
        }
        _builder.Emit(OpCode.Dict, (ushort)expr.Entries.Count);
        return null;
    }

}
