using System;
using System.Collections.Generic;
using Stash.Common;
using Stash.Lexing;
using Stash.Parsing.AST;
using Stash.Runtime;
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
        byte dest = _destReg;
        int count = expr.Elements.Count;

        if (count == 0)
        {
            _builder.EmitABC(OpCode.NewArray, dest, 0, 0);
            return null;
        }

        // Reserve a contiguous window: [base, elem0, elem1, ..., elemN-1]
        // NewArray spec: R(A) = new array with B elements from R(A+1)..R(A+B).
        byte baseReg = _scope.ReserveRegs(1 + count);
        for (int i = 0; i < count; i++)
            CompileExprTo(expr.Elements[i], (byte)(baseReg + 1 + i));
        _builder.EmitABC(OpCode.NewArray, baseReg, (byte)count, 0);
        if (baseReg != dest)
            _builder.EmitAB(OpCode.Move, dest, baseReg);
        _scope.FreeTempFrom(baseReg);
        return null;
    }

    /// <inheritdoc />
    public object? VisitIndexExpr(IndexExpr expr)
    {
        byte dest = _destReg;
        _builder.AddSourceMapping(expr.BracketSpan);
        byte objReg = CompileExpr(expr.Object);
        byte indexReg = CompileExpr(expr.Index);
        _builder.EmitABC(OpCode.GetTable, dest, objReg, indexReg);
        _scope.FreeTemp(indexReg);
        _scope.FreeTemp(objReg);
        return null;
    }

    /// <inheritdoc />
    public object? VisitIndexAssignExpr(IndexAssignExpr expr)
    {
        byte dest = _destReg;
        _builder.AddSourceMapping(expr.BracketSpan);
        byte objReg = CompileExpr(expr.Object);
        byte indexReg = CompileExpr(expr.Index);
        // Value compiles into dest — the assignment expression result is the stored value.
        CompileExprTo(expr.Value, dest);
        _builder.EmitABC(OpCode.SetTable, objReg, indexReg, dest);
        _scope.FreeTemp(indexReg);
        _scope.FreeTemp(objReg);
        return null;
    }

    /// <inheritdoc />
    public object? VisitRangeExpr(RangeExpr expr)
    {
        byte dest = _destReg;

        byte startReg = CompileExpr(expr.Start);
        byte endReg = CompileExpr(expr.End);

        // Reserve [base, step]: base is the result register, base+1 holds the step.
        // NewRange spec: R(A) = range(R(B), R(C)); step is read from R(A+1).
        byte baseReg = _scope.ReserveRegs(2);
        if (expr.Step != null)
            CompileExprTo(expr.Step, (byte)(baseReg + 1));
        else
            _builder.EmitA(OpCode.LoadNull, (byte)(baseReg + 1));

        _builder.EmitABC(OpCode.NewRange, baseReg, startReg, endReg);
        if (baseReg != dest)
            _builder.EmitAB(OpCode.Move, dest, baseReg);
        _scope.FreeTempFrom(baseReg);
        _scope.FreeTemp(endReg);
        _scope.FreeTemp(startReg);
        return null;
    }

    /// <inheritdoc />
    public object? VisitDictLiteralExpr(DictLiteralExpr expr)
    {
        byte dest = _destReg;
        int count = expr.Entries.Count;

        if (count == 0)
        {
            _builder.EmitABC(OpCode.NewDict, dest, 0, 0);
            return null;
        }

        // Reserve [base, k0, v0, k1, v1, ...].
        // NewDict spec: R(A) = new dict with B key-value pairs from R(A+1)..R(A+2*B).
        byte baseReg = _scope.ReserveRegs(1 + 2 * count);
        for (int i = 0; i < count; i++)
        {
            (Token? key, Expr value) = expr.Entries[i];
            byte keySlot = (byte)(baseReg + 1 + 2 * i);
            byte valSlot = (byte)(baseReg + 2 + 2 * i);

            if (key != null)
            {
                ushort keyIdx = _builder.AddConstant(key.Lexeme);
                _builder.EmitABx(OpCode.LoadK, keySlot, keyIdx);
            }
            else
            {
                // Spread entry: null key signals to the VM that the value is a spread source.
                _builder.EmitA(OpCode.LoadNull, keySlot);
            }

            CompileExprTo(value, valSlot);
        }

        _builder.EmitABC(OpCode.NewDict, baseReg, (byte)count, 0);
        if (baseReg != dest)
            _builder.EmitAB(OpCode.Move, dest, baseReg);
        _scope.FreeTempFrom(baseReg);
        return null;
    }

    /// <inheritdoc />
    public object? VisitStructInitExpr(StructInitExpr expr)
    {
        byte dest = _destReg;
        _builder.AddSourceMapping(expr.Span);

        int fieldCount = expr.FieldValues.Count;
        bool hasDynamicType = expr.Target != null;

        // Check if the struct name resolves to a local variable (e.g., struct declared inside a function body).
        int localTypeReg = -1;
        if (!hasDynamicType)
        {
            localTypeReg = _scope.ResolveLocal(expr.Name.Lexeme);
            if (localTypeReg >= 0)
                hasDynamicType = true;
        }

        // Build field name list for the metadata constant.
        string[] fieldNames = new string[fieldCount];
        for (int i = 0; i < fieldCount; i++)
            fieldNames[i] = expr.FieldValues[i].Field.Lexeme;

        // When HasTypeReg is false, the VM looks up TypeName from globals.
        // When HasTypeReg is true, the VM reads the struct type from R(A+1).
        string typeName = hasDynamicType ? "" : expr.Name.Lexeme;
        var meta = new StructInitMetadata(typeName, hasDynamicType, fieldNames);
        ushort metaIdx = _builder.AddConstant(meta);

        // Window layout:
        //   !hasDynamicType: [base, field0, ..., fieldN-1]        — 1+N slots
        //    hasDynamicType: [base, typeRef, field0, ..., fieldN-1] — 2+N slots
        int windowSize = 1 + (hasDynamicType ? 1 : 0) + fieldCount;
        byte baseReg = _scope.ReserveRegs(windowSize);

        if (hasDynamicType)
        {
            if (localTypeReg >= 0)
                _builder.EmitAB(OpCode.Move, (byte)(baseReg + 1), (byte)localTypeReg);
            else
                CompileExprTo(expr.Target!, (byte)(baseReg + 1));
        }

        int fieldOffset = hasDynamicType ? 2 : 1;
        for (int i = 0; i < fieldCount; i++)
            CompileExprTo(expr.FieldValues[i].Value, (byte)(baseReg + fieldOffset + i));

        // NewStruct ABC: R(A) = new instance of struct K(B) with C field values from R(A+1).
        _builder.EmitABC(OpCode.NewStruct, baseReg, (byte)metaIdx, (byte)fieldCount);
        if (baseReg != dest)
            _builder.EmitAB(OpCode.Move, dest, baseReg);
        _scope.FreeTempFrom(baseReg);
        return null;
    }
}
