using System;
using System.Collections.Generic;
using System.Linq;
using Stash.Common;
using Stash.Lexing;
using Stash.Parsing.AST;
using Stash.Runtime;
using Stash.Runtime.Types;

namespace Stash.Bytecode;

partial class Compiler
{
    // =========================================================================
    // Statement Visitors
    // =========================================================================

    public object? VisitExprStmt(ExprStmt stmt)
    {
        // OPT: Set void context for expressions used as statements where the
        // result is discarded — calls, updates (x++), and assignments (x = ...).
        if (stmt.Expression is CallExpr or UpdateExpr or AssignExpr)
            _voidContext = true;
        byte reg = CompileExpr(stmt.Expression);
        _voidContext = false;
        _scope.FreeTemp(reg);
        return null;
    }

    public object? VisitBlockStmt(BlockStmt stmt)
    {
        _scope.BeginScope();
        foreach (Stmt s in stmt.Statements)
            CompileStmt(s);
        EndScope();
        return null;
    }

    public object? VisitVarDeclStmt(VarDeclStmt stmt)
    {
        _builder.AddSourceMapping(stmt.Span);

        byte reg = _scope.DeclareLocal(stmt.Name.Lexeme);
        if (stmt.Initializer != null)
            CompileExprTo(stmt.Initializer, reg);
        else
            _builder.EmitA(OpCode.LoadNull, reg);
        _scope.MarkInitialized();

        // OPT-10: Track numeric locals
        if (stmt.Initializer != null && IsNumericExpr(stmt.Initializer))
            _scope.MarkNumeric(reg);

        // At top level, also write to global slot for cross-module access
        if (_enclosing == null && _scope.ScopeDepth == 0)
        {
            ushort gslot = _globalSlots.GetOrAllocate(stmt.Name.Lexeme);
            _builder.EmitABx(OpCode.SetGlobal, reg, gslot);
        }
        return null;
    }

    public object? VisitConstDeclStmt(ConstDeclStmt stmt)
    {
        _builder.AddSourceMapping(stmt.Span);

        // Check if this is a top-level const with a foldable initializer
        bool isTopLevelGlobal = _enclosing == null && _scope.ScopeDepth == 0;
        bool useMetadataInit = false;
        object? foldedValue = null;

        if (isTopLevelGlobal && TryEvaluateConstant(stmt.Initializer, out foldedValue))
            useMetadataInit = true;

        byte reg = _scope.DeclareLocal(stmt.Name.Lexeme, isConst: true);

        if (useMetadataInit)
        {
            // OPT: Metadata-based init — emit a LoadK into the local register so subsequent
            // same-scope reads work, but skip InitConstGlobal (the VM pre-populates the slot).
            EmitFoldedConstant(foldedValue, reg);
        }
        else
        {
            CompileExprTo(stmt.Initializer, reg);
        }
        _scope.MarkInitialized();

        // OPT-10: Track numeric locals
        if (IsNumericExpr(stmt.Initializer))
            _scope.MarkNumeric(reg);

        if (isTopLevelGlobal)
        {
            if (useMetadataInit)
            {
                // Metadata-based init: record (slot, constIndex) pair — no InitConstGlobal emitted
                _globalSlots.TrackConstValue(stmt.Name.Lexeme, foldedValue);
                ushort gslot = _globalSlots.GetOrAllocate(stmt.Name.Lexeme);
                StashValue sv = StashValue.FromObject(foldedValue);
                ushort constIdx = _builder.AddConstant(sv);
                _builder.AddConstGlobalInit(gslot, constIdx);
            }
            else
            {
                ushort gslot = _globalSlots.GetOrAllocate(stmt.Name.Lexeme);
                _builder.EmitABx(OpCode.InitConstGlobal, reg, gslot);
            }
        }
        else if (TryEvaluateConstant(stmt.Initializer, out object? constVal))
        {
            _globalSlots.TrackConstValue(stmt.Name.Lexeme, constVal);
        }
        return null;
    }

    public object? VisitFnDeclStmt(FnDeclStmt stmt)
    {
        _builder.AddSourceMapping(stmt.Span);

        Chunk fnChunk = CompileFunction(
            stmt.Parameters, stmt.Body, stmt.Name.Lexeme,
            stmt.IsAsync, stmt.HasRestParam, stmt.DefaultValues);

        ushort fnIdx = _builder.AddConstant(StashValue.FromObj(fnChunk));

        byte reg = _scope.DeclareLocal(stmt.Name.Lexeme);
        _builder.EmitABx(OpCode.Closure, reg, fnIdx);
        foreach (UpvalueDescriptor uv in fnChunk.Upvalues)
            _builder.EmitRaw((uint)(uv.IsLocal ? 1 : 0) | ((uint)uv.Index << 8));
        _scope.MarkInitialized();

        if (_enclosing == null && _scope.ScopeDepth == 0)
        {
            ushort gslot = _globalSlots.GetOrAllocate(stmt.Name.Lexeme);
            _builder.EmitABx(OpCode.SetGlobal, reg, gslot);
        }
        return null;
    }

    public object? VisitThrowStmt(ThrowStmt stmt)
    {
        _builder.AddSourceMapping(stmt.Span);
        byte reg = CompileExpr(stmt.Value);
        _builder.EmitA(OpCode.Throw, reg);
        _scope.FreeTemp(reg);
        return null;
    }

    public object? VisitStructDeclStmt(StructDeclStmt stmt)
    {
        _builder.AddSourceMapping(stmt.Span);

        var methodNames = new List<string>();
        var methodChunks = new List<Chunk>();
        foreach (FnDeclStmt method in stmt.Methods)
        {
            List<Token> methodParams = method.Parameters;
            List<Expr?>? methodDefaults = method.DefaultValues;

            // Prepend implicit 'self' if not already in parameter list
            if (!methodParams.Any(p => p.Lexeme == "self"))
            {
                methodParams = new List<Token>(methodParams.Count + 1);
                methodParams.Add(new Token(TokenType.Identifier, "self", null, method.Name.Span));
                methodParams.AddRange(method.Parameters);

                // Shift default values to account for the prepended self (which has no default)
                if (methodDefaults != null && methodDefaults.Count > 0)
                {
                    var newDefaults = new List<Expr?>(methodDefaults.Count + 1) { null };
                    newDefaults.AddRange(methodDefaults);
                    methodDefaults = newDefaults;
                }
            }

            Chunk mChunk = CompileFunction(
                methodParams, method.Body, method.Name.Lexeme,
                method.IsAsync, method.HasRestParam, methodDefaults);
            methodNames.Add(method.Name.Lexeme);
            methodChunks.Add(mChunk);
        }

        string[] fieldNames = new string[stmt.Fields.Count];
        for (int i = 0; i < stmt.Fields.Count; i++)
            fieldNames[i] = stmt.Fields[i].Lexeme;

        string[] ifaceNames = new string[stmt.Interfaces.Count];
        for (int i = 0; i < stmt.Interfaces.Count; i++)
            ifaceNames[i] = stmt.Interfaces[i].Lexeme;

        var meta = new StructMetadata(stmt.Name.Lexeme, fieldNames, methodNames.ToArray(), ifaceNames);
        ushort metaIdx = _builder.AddConstant(meta);

        byte destReg = _scope.DeclareLocal(stmt.Name.Lexeme);

        // Emit method closures into registers above destReg
        foreach (Chunk mChunk in methodChunks)
        {
            byte mReg = _scope.AllocTemp();
            ushort mIdx = _builder.AddConstant(StashValue.FromObj(mChunk));
            _builder.EmitABx(OpCode.Closure, mReg, mIdx);
            foreach (UpvalueDescriptor uv in mChunk.Upvalues)
                _builder.EmitRaw((uint)(uv.IsLocal ? 1 : 0) | ((uint)uv.Index << 8));
        }

        _builder.EmitABx(OpCode.StructDecl, destReg, metaIdx);
        _scope.FreeTempFrom((byte)(destReg + 1));
        _scope.MarkInitialized();

        if (_enclosing == null && _scope.ScopeDepth == 0)
        {
            ushort gslot = _globalSlots.GetOrAllocate(stmt.Name.Lexeme);
            _builder.EmitABx(OpCode.SetGlobal, destReg, gslot);
        }
        return null;
    }

    public object? VisitEnumDeclStmt(EnumDeclStmt stmt)
    {
        _builder.AddSourceMapping(stmt.Span);

        string[] members = new string[stmt.Members.Count];
        for (int i = 0; i < stmt.Members.Count; i++)
            members[i] = stmt.Members[i].Lexeme;

        var meta = new EnumMetadata(stmt.Name.Lexeme, members);
        ushort metaIdx = _builder.AddConstant(meta);

        byte destReg = _scope.DeclareLocal(stmt.Name.Lexeme);
        _builder.EmitABx(OpCode.EnumDecl, destReg, metaIdx);
        _scope.MarkInitialized();

        if (_enclosing == null && _scope.ScopeDepth == 0)
        {
            ushort gslot = _globalSlots.GetOrAllocate(stmt.Name.Lexeme);
            _builder.EmitABx(OpCode.SetGlobal, destReg, gslot);
        }
        return null;
    }

    public object? VisitInterfaceDeclStmt(InterfaceDeclStmt stmt)
    {
        _builder.AddSourceMapping(stmt.Span);

        var fields = new InterfaceField[stmt.Fields.Count];
        for (int i = 0; i < stmt.Fields.Count; i++)
        {
            string fieldName = stmt.Fields[i].Lexeme;
            string? typeHint = i < stmt.FieldTypes.Count ? stmt.FieldTypes[i]?.Lexeme : null;
            fields[i] = new InterfaceField(fieldName, typeHint);
        }

        var methods = new InterfaceMethod[stmt.Methods.Count];
        for (int i = 0; i < stmt.Methods.Count; i++)
        {
            InterfaceMethodSignature sig = stmt.Methods[i];
            var paramNames = sig.Parameters.Select(p => p.Lexeme).ToList();
            var paramTypes = sig.Parameters
                .Select((_, j) => j < sig.ParameterTypes.Count ? sig.ParameterTypes[j]?.Lexeme : null)
                .ToList();
            methods[i] = new InterfaceMethod(
                sig.Name.Lexeme, sig.Parameters.Count, paramNames, paramTypes, sig.ReturnType?.Lexeme);
        }

        var meta = new InterfaceMetadata(stmt.Name.Lexeme, fields, methods);
        ushort metaIdx = _builder.AddConstant(meta);

        byte destReg = _scope.DeclareLocal(stmt.Name.Lexeme);
        _builder.EmitABx(OpCode.IfaceDecl, destReg, metaIdx);
        _scope.MarkInitialized();

        if (_enclosing == null && _scope.ScopeDepth == 0)
        {
            ushort gslot = _globalSlots.GetOrAllocate(stmt.Name.Lexeme);
            _builder.EmitABx(OpCode.SetGlobal, destReg, gslot);
        }
        return null;
    }

    public object? VisitExtendStmt(ExtendStmt stmt)
    {
        _builder.AddSourceMapping(stmt.Span);

        if (_enclosing != null || _scope.ScopeDepth > 0)
        {
            byte errReg = _scope.AllocTemp();
            ushort msgIdx = _builder.AddConstant("'extend' blocks must be defined at the top level.");
            _builder.EmitABx(OpCode.LoadK, errReg, msgIdx);
            _builder.EmitA(OpCode.Throw, errReg);
            _scope.FreeTemp(errReg);
            return null;
        }

        string typeName = stmt.TypeName.Lexeme;
        bool isBuiltIn = typeName is "string" or "array" or "dict" or "int" or "float";

        var methodNames = new List<string>();
        var methodChunks = new List<Chunk>();
        foreach (FnDeclStmt method in stmt.Methods)
        {
            List<Token> methodParams = method.Parameters;
            List<Expr?>? methodDefaults = method.DefaultValues;

            // Prepend implicit 'self' if not already in parameter list
            if (!methodParams.Any(p => p.Lexeme == "self"))
            {
                methodParams = new List<Token>(methodParams.Count + 1);
                methodParams.Add(new Token(TokenType.Identifier, "self", null, method.Name.Span));
                methodParams.AddRange(method.Parameters);

                // Shift default values to account for the prepended self (which has no default)
                if (methodDefaults != null && methodDefaults.Count > 0)
                {
                    var newDefaults = new List<Expr?>(methodDefaults.Count + 1) { null };
                    newDefaults.AddRange(methodDefaults);
                    methodDefaults = newDefaults;
                }
            }

            Chunk mChunk = CompileFunction(
                methodParams, method.Body, method.Name.Lexeme,
                method.IsAsync, method.HasRestParam, methodDefaults,
                constFirstParam: true);
            methodNames.Add(method.Name.Lexeme);
            methodChunks.Add(mChunk);
        }

        var meta = new ExtendMetadata(typeName, methodNames.ToArray(), isBuiltIn);
        ushort metaIdx = _builder.AddConstant(meta);

        byte baseReg = _scope.AllocTemp();
        foreach (Chunk mChunk in methodChunks)
        {
            byte mReg = _scope.AllocTemp();
            ushort mIdx = _builder.AddConstant(StashValue.FromObj(mChunk));
            _builder.EmitABx(OpCode.Closure, mReg, mIdx);
            foreach (UpvalueDescriptor uv in mChunk.Upvalues)
                _builder.EmitRaw((uint)(uv.IsLocal ? 1 : 0) | ((uint)uv.Index << 8));
        }

        _builder.EmitABx(OpCode.Extend, baseReg, metaIdx);
        _scope.FreeTempFrom(baseReg);
        return null;
    }

    public object? VisitImportStmt(ImportStmt stmt)
    {
        _builder.AddSourceMapping(stmt.Span);

        // Compile path into a temp register
        byte pathReg = CompileExpr(stmt.Path);

        string[] names = new string[stmt.Names.Count];
        for (int i = 0; i < stmt.Names.Count; i++)
            names[i] = stmt.Names[i].Lexeme;

        var metadata = new ImportMetadata(names);
        ushort metaIdx = _builder.AddConstant(metadata);

        // Import R(pathReg), K(metaIdx) — VM reads path from R(A), writes N results to R(A+1)..R(A+N)
        _builder.EmitABx(OpCode.Import, pathReg, metaIdx);

        // Declare each imported name as a local (allocated consecutively after pathReg)
        for (int i = 0; i < stmt.Names.Count; i++)
        {
            byte nameReg = _scope.DeclareLocal(stmt.Names[i].Lexeme);
            _scope.MarkInitialized();

            if (_enclosing == null && _scope.ScopeDepth == 0)
            {
                ushort gslot = _globalSlots.GetOrAllocate(stmt.Names[i].Lexeme);
                _builder.EmitABx(OpCode.SetGlobal, nameReg, gslot);
            }
        }
        return null;
    }

    public object? VisitImportAsStmt(ImportAsStmt stmt)
    {
        _builder.AddSourceMapping(stmt.Span);

        byte pathReg = CompileExpr(stmt.Path);

        var metadata = new ImportAsMetadata(stmt.Alias.Lexeme);
        ushort metaIdx = _builder.AddConstant(metadata);

        // ImportAs R(pathReg), K(metaIdx) — VM reads path from R(A), writes result to R(A+1)
        _builder.EmitABx(OpCode.ImportAs, pathReg, metaIdx);

        byte aliasReg = _scope.DeclareLocal(stmt.Alias.Lexeme);
        _scope.MarkInitialized();

        if (_enclosing == null && _scope.ScopeDepth == 0)
        {
            ushort gslot = _globalSlots.GetOrAllocate(stmt.Alias.Lexeme);
            _builder.EmitABx(OpCode.SetGlobal, aliasReg, gslot);
        }
        return null;
    }

    public object? VisitDestructureStmt(DestructureStmt stmt)
    {
        _builder.AddSourceMapping(stmt.Span);

        // Compile initializer into a temp register
        byte initReg = CompileExpr(stmt.Initializer);

        string kind = stmt.Kind == DestructureStmt.PatternKind.Array ? "array" : "object";
        string[] names = new string[stmt.Names.Count];
        for (int i = 0; i < stmt.Names.Count; i++)
            names[i] = stmt.Names[i].Lexeme;

        var metadata = new DestructureMetadata(kind, names, stmt.RestName?.Lexeme, stmt.IsConst);
        ushort metaIdx = _builder.AddConstant(metadata);

        // Destructure R(initReg), K(metaIdx) — VM reads from R(A), writes N results to R(A+1)..
        _builder.EmitABx(OpCode.Destructure, initReg, metaIdx);

        // Declare each destructured name as a local
        for (int i = 0; i < stmt.Names.Count; i++)
        {
            byte nameReg = _scope.DeclareLocal(stmt.Names[i].Lexeme, isConst: stmt.IsConst);
            _scope.MarkInitialized();

            if (_enclosing == null && _scope.ScopeDepth == 0)
            {
                ushort gslot = _globalSlots.GetOrAllocate(stmt.Names[i].Lexeme);
                _builder.EmitABx(
                    stmt.IsConst ? OpCode.InitConstGlobal : OpCode.SetGlobal,
                    nameReg, gslot);
            }
        }

        if (stmt.RestName != null)
        {
            byte restReg = _scope.DeclareLocal(stmt.RestName.Lexeme, isConst: stmt.IsConst);
            _scope.MarkInitialized();

            if (_enclosing == null && _scope.ScopeDepth == 0)
            {
                ushort gslot = _globalSlots.GetOrAllocate(stmt.RestName.Lexeme);
                _builder.EmitABx(
                    stmt.IsConst ? OpCode.InitConstGlobal : OpCode.SetGlobal,
                    restReg, gslot);
            }
        }
        return null;
    }
}
