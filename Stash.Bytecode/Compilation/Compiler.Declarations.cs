using System;
using System.Collections.Generic;
using Stash.Common;
using Stash.Lexing;
using Stash.Parsing.AST;
using Stash.Runtime.Types;

namespace Stash.Bytecode;

/// <summary>
/// Declaration and basic statement visitor implementations.
/// </summary>
public sealed partial class Compiler
{
    // =========================================================================
    // Statement Visitors
    // =========================================================================

    /// <inheritdoc />
    public object? VisitExprStmt(ExprStmt stmt)
    {
        _builder.AddSourceMapping(stmt.Span);
        CompileExpr(stmt.Expression);
        _builder.Emit(OpCode.Pop);
        return null;
    }

    /// <inheritdoc />
    public object? VisitVarDeclStmt(VarDeclStmt stmt)
    {
        _builder.AddSourceMapping(stmt.Span);
        int slot = _scope.DeclareLocal(stmt.Name.Lexeme, isConst: false);

        if (stmt.Initializer != null)
        {
            CompileExpr(stmt.Initializer);
        }
        else
        {
            _builder.Emit(OpCode.Null);
        }

        _scope.MarkInitialized(slot);
        // The initializer value on the stack IS the local — no separate store needed.

        // At top-level, also seed globals so that OP_LOAD_GLOBAL (emitted because
        // the Resolver leaves top-level variables unresolved at distance=-1) can
        // find the initial value.
        if (_enclosing is null && _scope.ScopeDepth == 0)
        {
            int resolvedSlot = _scope.ResolveLocal(stmt.Name.Lexeme);
            if (resolvedSlot >= 0)
            {
                _builder.Emit(OpCode.LoadLocal, (byte)resolvedSlot);
                ushort globalSlot = _globalSlots.GetOrAllocate(stmt.Name.Lexeme);
                _builder.Emit(OpCode.StoreGlobal, globalSlot);
            }
        }

        return null;
    }

    /// <inheritdoc />
    public object? VisitConstDeclStmt(ConstDeclStmt stmt)
    {
        _builder.AddSourceMapping(stmt.Span);
        int slot = _scope.DeclareLocal(stmt.Name.Lexeme, isConst: true);
        CompileExpr(stmt.Initializer);
        _scope.MarkInitialized(slot);

        // Track literal const values for compile-time folding in EmitVariable.
        if (_enclosing is null && stmt.Initializer is LiteralExpr literal)
        {
            _globalSlots.TrackConstValue(stmt.Name.Lexeme, literal.Value);
        }

        // At top-level, also seed globals so that OP_LOAD_GLOBAL (emitted because
        // the Resolver leaves top-level variables unresolved at distance=-1) can
        // find the initial value. Use InitConstGlobal so the VM marks the name as
        // immutable and throws on any subsequent StoreGlobal to the same name.
        if (_enclosing is null)
        {
            int resolvedSlot = _scope.ResolveLocal(stmt.Name.Lexeme);
            if (resolvedSlot >= 0)
            {
                _builder.Emit(OpCode.LoadLocal, (byte)resolvedSlot);
                ushort globalSlot = _globalSlots.GetOrAllocate(stmt.Name.Lexeme);
                _builder.Emit(OpCode.InitConstGlobal, globalSlot);
            }
        }

        return null;
    }

    /// <inheritdoc />
    public object? VisitBlockStmt(BlockStmt stmt)
    {
        _scope.BeginScope();
        foreach (Stmt s in stmt.Statements)
        {
            CompileStmt(s);
        }

        EmitScopePops();
        return null;
    }

    /// <inheritdoc />
    public object? VisitFnDeclStmt(FnDeclStmt stmt)
    {
        _builder.AddSourceMapping(stmt.Span);

        // Declare the function name as a local before compiling its body (enables recursion)
        int slot = _scope.DeclareLocal(stmt.Name.Lexeme, isConst: false);
        _scope.MarkInitialized(slot);

        CompileFunction(
            stmt.Name.Lexeme,
            stmt.Parameters,
            stmt.DefaultValues,
            stmt.Body,
            stmt.IsAsync,
            stmt.HasRestParam);

        // Dup + StoreGlobal: needed because references use LoadGlobal (distance=-1 at top level)
        // and harmless in local scopes where LoadLocal is used instead
        _builder.Emit(OpCode.Dup);
        {
            ushort globalSlot = _globalSlots.GetOrAllocate(stmt.Name.Lexeme);
            _builder.Emit(OpCode.StoreGlobal, globalSlot);
        }

        return null;
    }

    /// <inheritdoc />
    public object? VisitThrowStmt(ThrowStmt stmt)
    {
        _builder.AddSourceMapping(stmt.Span);
        CompileExpr(stmt.Value);
        _builder.Emit(OpCode.Throw);
        return null;
    }


    // --- Deferred statement visitors ---

    /// <inheritdoc />
    public object? VisitStructDeclStmt(StructDeclStmt stmt)
    {
        _builder.AddSourceMapping(stmt.Span);

        int slot = _scope.DeclareLocal(stmt.Name.Lexeme, isConst: false);
        _scope.MarkInitialized(slot);

        // Compile each method as a closure (pushes VMFunction onto stack)
        var methodNames = new string[stmt.Methods.Count];
        for (int i = 0; i < stmt.Methods.Count; i++)
        {
            methodNames[i] = stmt.Methods[i].Name.Lexeme;

            // Prepend a synthetic 'self' parameter so the VM's arity check passes.
            // CallValue for VMBoundMethod does argc+1 (inserting self), so expected must match.
            // Only add if not already explicitly declared by the user.
            List<Token> methodParams;
            List<Expr?> methodDefaults;
            bool hasSelfParam = stmt.Methods[i].Parameters.Count > 0
                && stmt.Methods[i].Parameters[0].Lexeme == "self";

            if (hasSelfParam)
            {
                methodParams = stmt.Methods[i].Parameters;
                methodDefaults = stmt.Methods[i].DefaultValues;
            }
            else
            {
                methodParams = new List<Token>();
                methodParams.Add(new Token(TokenType.Identifier, "self", null, stmt.Methods[i].Name.Span));
                methodParams.AddRange(stmt.Methods[i].Parameters);

                methodDefaults = new List<Expr?>();
                methodDefaults.Add(null); // self has no default
                methodDefaults.AddRange(stmt.Methods[i].DefaultValues);
            }

            CompileFunction(
                stmt.Methods[i].Name.Lexeme,
                methodParams,
                methodDefaults,
                stmt.Methods[i].Body,
                stmt.Methods[i].IsAsync,
                stmt.Methods[i].HasRestParam);
        }

        string[] fields = new string[stmt.Fields.Count];
        for (int i = 0; i < stmt.Fields.Count; i++)
        {
            fields[i] = stmt.Fields[i].Lexeme;
        }

        string[] interfaceNames = new string[stmt.Interfaces.Count];
        for (int i = 0; i < stmt.Interfaces.Count; i++)
        {
            interfaceNames[i] = stmt.Interfaces[i].Lexeme;
        }

        var metadata = new StructMetadata(stmt.Name.Lexeme, fields, methodNames, interfaceNames);
        ushort metaIdx = _builder.AddConstant(metadata);
        _builder.Emit(OpCode.StructDecl, metaIdx);

        // Dup + StoreGlobal: needed because references use LoadGlobal (distance=-1 at top level)
        // and harmless in local scopes where LoadLocal is used instead
        _builder.Emit(OpCode.Dup);
        {
            ushort globalSlot = _globalSlots.GetOrAllocate(stmt.Name.Lexeme);
            _builder.Emit(OpCode.StoreGlobal, globalSlot);
        }

        return null;
    }

    /// <inheritdoc />
    public object? VisitEnumDeclStmt(EnumDeclStmt stmt)
    {
        _builder.AddSourceMapping(stmt.Span);

        int slot = _scope.DeclareLocal(stmt.Name.Lexeme, isConst: false);
        _scope.MarkInitialized(slot);

        string[] members = new string[stmt.Members.Count];
        for (int i = 0; i < stmt.Members.Count; i++)
        {
            members[i] = stmt.Members[i].Lexeme;
        }

        var metadata = new EnumMetadata(stmt.Name.Lexeme, members);
        ushort metaIdx = _builder.AddConstant(metadata);
        _builder.Emit(OpCode.EnumDecl, metaIdx);

        _builder.Emit(OpCode.Dup);
        {
            ushort globalSlot = _globalSlots.GetOrAllocate(stmt.Name.Lexeme);
            _builder.Emit(OpCode.StoreGlobal, globalSlot);
        }

        return null;
    }

    /// <inheritdoc />
    public object? VisitInterfaceDeclStmt(InterfaceDeclStmt stmt)
    {
        _builder.AddSourceMapping(stmt.Span);

        int slot = _scope.DeclareLocal(stmt.Name.Lexeme, isConst: false);
        _scope.MarkInitialized(slot);

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
            var paramNames = new List<string>();
            foreach (Token p in sig.Parameters)
            {
                paramNames.Add(p.Lexeme);
            }

            var paramTypes = new List<string?>();
            for (int j = 0; j < sig.Parameters.Count; j++)
            {
                string? paramType = j < sig.ParameterTypes.Count ? sig.ParameterTypes[j]?.Lexeme : null;
                paramTypes.Add(paramType);
            }

            string? returnType = sig.ReturnType?.Lexeme;
            methods[i] = new InterfaceMethod(sig.Name.Lexeme, sig.Parameters.Count, paramNames, paramTypes, returnType);
        }

        var metadata = new InterfaceMetadata(stmt.Name.Lexeme, fields, methods);
        ushort metaIdx = _builder.AddConstant(metadata);
        _builder.Emit(OpCode.InterfaceDecl, metaIdx);

        _builder.Emit(OpCode.Dup);
        {
            ushort globalSlot = _globalSlots.GetOrAllocate(stmt.Name.Lexeme);
            _builder.Emit(OpCode.StoreGlobal, globalSlot);
        }

        return null;
    }

    /// <inheritdoc />
    public object? VisitExtendStmt(ExtendStmt stmt)
    {
        _builder.AddSourceMapping(stmt.Span);

        if (!(_enclosing is null && _scope.ScopeDepth == 0))
        {
            ushort msgIdx = _builder.AddConstant("'extend' blocks must be declared at the top level.");
            _builder.Emit(OpCode.Const, msgIdx);
            _builder.Emit(OpCode.Throw);
            return null;
        }

        string typeName = stmt.TypeName.Lexeme;
        bool isBuiltIn = typeName is "string" or "array" or "dict" or "int" or "float";

        var methodNames = new string[stmt.Methods.Count];
        for (int i = 0; i < stmt.Methods.Count; i++)
        {
            methodNames[i] = stmt.Methods[i].Name.Lexeme;

            List<Token> methodParams;
            List<Expr?> methodDefaults;
            bool hasSelfParam = stmt.Methods[i].Parameters.Count > 0
                && stmt.Methods[i].Parameters[0].Lexeme == "self";

            if (!hasSelfParam)
            {
                methodParams = new List<Token>();
                methodParams.Add(new Token(TokenType.Identifier, "self", null, stmt.Methods[i].Name.Span));
                methodParams.AddRange(stmt.Methods[i].Parameters);

                methodDefaults = new List<Expr?>();
                methodDefaults.Add(null); // self has no default
                methodDefaults.AddRange(stmt.Methods[i].DefaultValues);
            }
            else
            {
                methodParams = stmt.Methods[i].Parameters;
                methodDefaults = stmt.Methods[i].DefaultValues;
            }

            CompileFunction(
                stmt.Methods[i].Name.Lexeme,
                methodParams,
                methodDefaults,
                stmt.Methods[i].Body,
                stmt.Methods[i].IsAsync,
                stmt.Methods[i].HasRestParam,
                firstParamIsConst: !hasSelfParam);
        }

        var metadata = new ExtendMetadata(typeName, methodNames, isBuiltIn);
        ushort metaIdx = _builder.AddConstant(metadata);
        _builder.Emit(OpCode.Extend, metaIdx);

        return null;
    }

    /// <inheritdoc />
    public object? VisitImportStmt(ImportStmt stmt)
    {
        _builder.AddSourceMapping(stmt.Span);

        // Compile path expression (pushes module path string onto stack)
        CompileExpr(stmt.Path);

        string[] names = new string[stmt.Names.Count];
        for (int i = 0; i < stmt.Names.Count; i++)
        {
            names[i] = stmt.Names[i].Lexeme;
        }

        var metadata = new ImportMetadata(names);
        ushort metaIdx = _builder.AddConstant(metadata);
        _builder.Emit(OpCode.Import, metaIdx);
        // VM pops path, loads module, pushes N values onto stack (one per name)

        // Declare each imported name as a local
        foreach (Token name in stmt.Names)
        {
            int slot = _scope.DeclareLocal(name.Lexeme, isConst: false);
            _scope.MarkInitialized(slot);
            if (_enclosing is null && _scope.ScopeDepth == 0)
            {
                _builder.Emit(OpCode.LoadLocal, (byte)slot);
                ushort globalSlot = _globalSlots.GetOrAllocate(name.Lexeme);
                _builder.Emit(OpCode.StoreGlobal, globalSlot);
            }
        }

        return null;
    }

    /// <inheritdoc />
    public object? VisitImportAsStmt(ImportAsStmt stmt)
    {
        _builder.AddSourceMapping(stmt.Span);

        CompileExpr(stmt.Path);

        var metadata = new ImportAsMetadata(stmt.Alias.Lexeme);
        ushort metaIdx = _builder.AddConstant(metadata);
        _builder.Emit(OpCode.ImportAs, metaIdx);
        // VM pops path, loads module, wraps as StashNamespace, pushes 1 value

        int slot = _scope.DeclareLocal(stmt.Alias.Lexeme, isConst: false);
        _scope.MarkInitialized(slot);
        if (_enclosing is null && _scope.ScopeDepth == 0)
        {
            _builder.Emit(OpCode.LoadLocal, (byte)slot);
            ushort globalSlot = _globalSlots.GetOrAllocate(stmt.Alias.Lexeme);
            _builder.Emit(OpCode.StoreGlobal, globalSlot);
        }

        return null;
    }

    /// <inheritdoc />
    public object? VisitDestructureStmt(DestructureStmt stmt)
    {
        _builder.AddSourceMapping(stmt.Span);

        // Compile the initializer expression (pushes array/dict onto stack)
        CompileExpr(stmt.Initializer);

        string kind = stmt.Kind == DestructureStmt.PatternKind.Array ? "array" : "object";
        string[] names = new string[stmt.Names.Count];
        for (int i = 0; i < stmt.Names.Count; i++)
        {
            names[i] = stmt.Names[i].Lexeme;
        }

        var metadata = new DestructureMetadata(kind, names, stmt.RestName?.Lexeme, stmt.IsConst);
        ushort metaIdx = _builder.AddConstant(metadata);
        _builder.Emit(OpCode.Destructure, metaIdx);
        // VM pops initializer, pushes N values (one per name + optional rest)

        // Declare each name as a local
        foreach (Token name in stmt.Names)
        {
            int slot = _scope.DeclareLocal(name.Lexeme, isConst: stmt.IsConst);
            _scope.MarkInitialized(slot);

            if (_enclosing is null && _scope.ScopeDepth == 0)
            {
                _builder.Emit(OpCode.LoadLocal, (byte)slot);
                ushort globalSlot = _globalSlots.GetOrAllocate(name.Lexeme);
                _builder.Emit(stmt.IsConst ? OpCode.InitConstGlobal : OpCode.StoreGlobal, globalSlot);
            }
        }

        // Declare rest name if present
        if (stmt.RestName != null)
        {
            int restSlot = _scope.DeclareLocal(stmt.RestName.Lexeme, isConst: stmt.IsConst);
            _scope.MarkInitialized(restSlot);

            if (_enclosing is null && _scope.ScopeDepth == 0)
            {
                _builder.Emit(OpCode.LoadLocal, (byte)restSlot);
                ushort globalSlot = _globalSlots.GetOrAllocate(stmt.RestName.Lexeme);
                _builder.Emit(stmt.IsConst ? OpCode.InitConstGlobal : OpCode.StoreGlobal, globalSlot);
            }
        }

        return null;
    }

}
