namespace Stash.Analysis;

using System.Collections.Generic;
using static Stash.Analysis.SemanticTokenConstants;
using System.Linq;
using Stash.Lexing;
using Stash.Parsing.AST;
using Stash.Stdlib;
using StashSymbolKind = Stash.Analysis.SymbolKind;

public class SemanticTokenWalker : IExprVisitor<int>, IStmtVisitor<int>
{
    private readonly AnalysisResult _result;
    private readonly Dictionary<(int Line, int Col), (int Type, int Modifiers)> _classified;
    private readonly Dictionary<(int Line, int Col), SymbolInfo> _resolvedRefs;

    public IReadOnlyDictionary<(int Line, int Col), (int Type, int Modifiers)> ClassifiedTokens => _classified;

    public SemanticTokenWalker(AnalysisResult result)
    {
        _result = result;
        _classified = new Dictionary<(int Line, int Col), (int Type, int Modifiers)>(result.Tokens.Count / 3);
        _resolvedRefs = new Dictionary<(int Line, int Col), SymbolInfo>(result.Symbols.References.Count);
        foreach (var reference in result.Symbols.References)
        {
            if (reference.ResolvedSymbol != null)
            {
                _resolvedRefs[(reference.Span.StartLine, reference.Span.StartColumn)] = reference.ResolvedSymbol;
            }
        }
    }

    public void Walk(List<Stmt> statements)
    {
        foreach (var stmt in statements)
        {
            stmt.Accept(this);
        }
    }

    private void Emit(int line, int col, int type, int modifiers)
    {
        _classified[(line, col)] = (type, modifiers);
    }

    private void EmitFromToken(Token token, int type, int modifiers)
    {
        Emit(token.Span.StartLine - 1, token.Span.StartColumn - 1, type, modifiers);
    }

    private (int TokenType, int Modifiers) MapSymbolKind(SymbolInfo definition, Token token)
    {
        int tokenType = definition.Kind switch
        {
            StashSymbolKind.Function or StashSymbolKind.Method => TokenTypeFunction,
            StashSymbolKind.Variable => TokenTypeVariable,
            StashSymbolKind.Constant => TokenTypeVariable,
            StashSymbolKind.Parameter => TokenTypeParameter,
            StashSymbolKind.Struct => TokenTypeType,
            StashSymbolKind.Enum => TokenTypeType,
            StashSymbolKind.Interface => TokenTypeInterface,
            StashSymbolKind.Field => TokenTypeProperty,
            StashSymbolKind.EnumMember => TokenTypeEnumMember,
            StashSymbolKind.LoopVariable => TokenTypeVariable,
            StashSymbolKind.Namespace => TokenTypeNamespace,
            _ => TokenTypeVariable,
        };

        int modifiers = 0;
        bool isDeclaration = definition.Span.StartLine == token.Span.StartLine
            && definition.Span.StartColumn == token.Span.StartColumn;
        if (isDeclaration)
        {
            modifiers |= ModifierDeclaration;
        }

        if (definition.Kind == StashSymbolKind.Constant)
        {
            modifiers |= ModifierReadonly;
        }

        return (tokenType, modifiers);
    }

    private void ClassifyStandaloneIdentifier(Token token)
    {
        if (token.Lexeme is "self" or "attempt")
        {
            return;
        }

        if (_resolvedRefs.TryGetValue((token.Span.StartLine, token.Span.StartColumn), out var def))
        {
            var (type, modifiers) = MapSymbolKind(def, token);
            EmitFromToken(token, type, modifiers);
            return;
        }

        if (StdlibRegistry.IsBuiltInFunction(token.Lexeme))
        {
            EmitFromToken(token, TokenTypeFunction, ModifierReadonly);
            return;
        }

        if (StdlibRegistry.IsBuiltInNamespace(token.Lexeme))
        {
            EmitFromToken(token, TokenTypeNamespace, 0);
        }
    }

    private void ClassifyDotMember(DotExpr dot, bool isCall)
    {
        string memberName = dot.Name.Lexeme;
        Token memberToken = dot.Name;

        if (dot.Object is IdentifierExpr objId)
        {
            string namespaceName = objId.Name.Lexeme;

            if (StdlibRegistry.IsBuiltInNamespace(namespaceName))
            {
                string qualifiedName = namespaceName + "." + memberName;

                if (StdlibRegistry.TryGetNamespaceFunction(qualifiedName, out _))
                {
                    EmitFromToken(memberToken, TokenTypeFunction, ModifierReadonly);
                    return;
                }

                if (StdlibRegistry.TryGetNamespaceConstant(qualifiedName, out _))
                {
                    EmitFromToken(memberToken, TokenTypeVariable, ModifierReadonly);
                    return;
                }

                if (StdlibRegistry.Enums.Any(e => e.Name == memberName && e.Namespace == namespaceName))
                {
                    EmitFromToken(memberToken, TokenTypeType, 0);
                    return;
                }
            }

            if (_result.NamespaceImports.TryGetValue(namespaceName, out var module))
            {
                var sym = module.Symbols.All.FirstOrDefault(s => s.Name == memberName);
                if (sym != null)
                {
                    var (type, modifiers) = MapSymbolKind(sym, memberToken);
                    EmitFromToken(memberToken, type, modifiers);
                    return;
                }
            }

            _resolvedRefs.TryGetValue((objId.Name.Span.StartLine, objId.Name.Span.StartColumn), out var parentDef);
            if (parentDef?.Kind == StashSymbolKind.Enum)
            {
                EmitFromToken(memberToken, TokenTypeEnumMember, 0);
                return;
            }
        }

        if (dot.Object is DotExpr parentDot && parentDot.Object is IdentifierExpr aliasId)
        {
            if (_result.NamespaceImports.TryGetValue(aliasId.Name.Lexeme, out var chainedModule))
            {
                var sym = chainedModule.Symbols.All.FirstOrDefault(s => s.Name == memberName && s.ParentName == parentDot.Name.Lexeme);
                if (sym != null)
                {
                    var (type, modifiers) = MapSymbolKind(sym, memberToken);
                    EmitFromToken(memberToken, type, modifiers);
                    return;
                }
            }
        }

        _resolvedRefs.TryGetValue((memberToken.Span.StartLine, memberToken.Span.StartColumn), out var def);
        if (def != null && def.Kind is StashSymbolKind.Field or StashSymbolKind.Function or StashSymbolKind.Method or StashSymbolKind.EnumMember)
        {
            var (type, modifiers) = MapSymbolKind(def, memberToken);
            EmitFromToken(memberToken, type, modifiers);
            return;
        }

        // UFCS: classify str/arr namespace methods used as method calls on values
        if (StdlibRegistry.TryGetNamespaceFunction("str." + memberName, out _) ||
            StdlibRegistry.TryGetNamespaceFunction("arr." + memberName, out _))
        {
            EmitFromToken(memberToken, TokenTypeFunction, ModifierReadonly);
            return;
        }

        if (StdlibRegistry.IsBuiltInFunction(memberName))
        {
            EmitFromToken(memberToken, TokenTypeFunction, ModifierReadonly);
            return;
        }

        if (isCall)
        {
            EmitFromToken(memberToken, TokenTypeFunction, 0);
        }
        else
        {
            EmitFromToken(memberToken, TokenTypeProperty, 0);
        }
    }

    // ── Statement Visitors ──

    public int VisitExprStmt(ExprStmt stmt)
    {
        stmt.Expression.Accept(this);
        return 0;
    }

    public int VisitVarDeclStmt(VarDeclStmt stmt)
    {
        EmitFromToken(stmt.Name, TokenTypeVariable, ModifierDeclaration);
        if (stmt.TypeHint is not null)
        {
            EmitFromToken(stmt.TypeHint, TokenTypeType, 0);
        }

        stmt.Initializer?.Accept(this);

        return 0;
    }

    public int VisitConstDeclStmt(ConstDeclStmt stmt)
    {
        EmitFromToken(stmt.Name, TokenTypeVariable, ModifierDeclaration | ModifierReadonly);
        if (stmt.TypeHint is not null)
        {
            EmitFromToken(stmt.TypeHint, TokenTypeType, 0);
        }

        stmt.Initializer.Accept(this);
        return 0;
    }

    public int VisitBlockStmt(BlockStmt stmt)
    {
        foreach (var s in stmt.Statements)
        {
            s.Accept(this);
        }
        return 0;
    }

    public int VisitIfStmt(IfStmt stmt)
    {
        stmt.Condition.Accept(this);
        stmt.ThenBranch.Accept(this);
        stmt.ElseBranch?.Accept(this);

        return 0;
    }

    public int VisitWhileStmt(WhileStmt stmt)
    {
        stmt.Condition.Accept(this);
        stmt.Body.Accept(this);
        return 0;
    }

    /// <inheritdoc />
    public int VisitElevateStmt(ElevateStmt stmt)
    {
        stmt.Elevator?.Accept(this);
        stmt.Body.Accept(this);
        return 0;
    }

    public int VisitDoWhileStmt(DoWhileStmt stmt)
    {
        stmt.Body.Accept(this);
        stmt.Condition.Accept(this);
        return 0;
    }

    public int VisitForStmt(ForStmt stmt)
    {
        if (stmt.Initializer is not null)
        {
            stmt.Initializer.Accept(this);
        }
        if (stmt.Condition is not null)
        {
            stmt.Condition.Accept(this);
        }
        if (stmt.Increment is not null)
        {
            stmt.Increment.Accept(this);
        }
        stmt.Body.Accept(this);
        return 0;
    }

    public int VisitForInStmt(ForInStmt stmt)
    {
        if (stmt.IndexName is not null)
        {
            EmitFromToken(stmt.IndexName, TokenTypeVariable, ModifierDeclaration);
        }

        EmitFromToken(stmt.VariableName, TokenTypeVariable, ModifierDeclaration);
        if (stmt.TypeHint is not null)
        {
            EmitFromToken(stmt.TypeHint, TokenTypeType, 0);
        }

        stmt.Iterable.Accept(this);
        stmt.Body.Accept(this);
        return 0;
    }

    public int VisitBreakStmt(BreakStmt stmt) => 0;

    public int VisitContinueStmt(ContinueStmt stmt) => 0;

    public int VisitFnDeclStmt(FnDeclStmt stmt)
    {
        if (stmt.AsyncKeyword is Token asyncTok)
        {
            EmitFromToken(asyncTok, TokenTypeKeyword, 0);
        }
        EmitFromToken(stmt.Name, TokenTypeFunction, ModifierDeclaration);
        for (int i = 0; i < stmt.Parameters.Count; i++)
        {
            EmitFromToken(stmt.Parameters[i], TokenTypeParameter, ModifierDeclaration);
            if (stmt.ParameterTypes[i] is Token paramType)
            {
                EmitFromToken(paramType, TokenTypeType, 0);
            }

            if (stmt.DefaultValues[i] is Expr defaultVal)
            {
                defaultVal.Accept(this);
            }
        }
        if (stmt.ReturnType is Token returnType)
        {
            EmitFromToken(returnType, TokenTypeType, 0);
        }

        stmt.Body.Accept(this);
        return 0;
    }

    public int VisitReturnStmt(ReturnStmt stmt)
    {
        stmt.Value?.Accept(this);

        return 0;
    }

    public int VisitThrowStmt(ThrowStmt stmt)
    {
        EmitFromToken(stmt.Keyword, TokenTypeKeyword, 0);
        stmt.Value.Accept(this);
        return 0;
    }

    public int VisitTryCatchStmt(TryCatchStmt stmt)
    {
        EmitFromToken(stmt.TryKeyword, TokenTypeKeyword, 0);
        stmt.TryBody.Accept(this);
        if (stmt.CatchBody is not null)
        {
            if (stmt.CatchKeyword is not null)
                EmitFromToken(stmt.CatchKeyword, TokenTypeKeyword, 0);
            if (stmt.CatchVariable is not null)
                EmitFromToken(stmt.CatchVariable, TokenTypeVariable, ModifierDeclaration);
            stmt.CatchBody.Accept(this);
        }
        if (stmt.FinallyKeyword is not null)
            EmitFromToken(stmt.FinallyKeyword, TokenTypeKeyword, 0);
        stmt.FinallyBody?.Accept(this);
        return 0;
    }

    public int VisitSwitchStmt(SwitchStmt stmt)
    {
        stmt.Subject.Accept(this);
        foreach (SwitchCase @case in stmt.Cases)
        {
            foreach (Expr pattern in @case.Patterns)
            {
                pattern.Accept(this);
            }
            @case.Body.Accept(this);
        }
        return 0;
    }

    public int VisitStructDeclStmt(StructDeclStmt stmt)
    {
        EmitFromToken(stmt.Name, TokenTypeType, ModifierDeclaration);
        for (int i = 0; i < stmt.Fields.Count; i++)
        {
            EmitFromToken(stmt.Fields[i], TokenTypeProperty, ModifierDeclaration);
            if (stmt.FieldTypes[i] is Token fieldType)
            {
                EmitFromToken(fieldType, TokenTypeType, 0);
            }
        }
        foreach (var method in stmt.Methods)
        {
            method.Accept(this);
        }
        foreach (var iface in stmt.Interfaces)
        {
            EmitFromToken(iface, TokenTypeInterface, 0);
        }
        return 0;
    }

    public int VisitExtendStmt(ExtendStmt stmt)
    {
        EmitFromToken(stmt.ExtendKeyword, TokenTypeKeyword, 0);
        EmitFromToken(stmt.TypeName, TokenTypeType, 0);
        foreach (var method in stmt.Methods)
        {
            method.Accept(this);
        }
        return 0;
    }

    public int VisitEnumDeclStmt(EnumDeclStmt stmt)
    {
        EmitFromToken(stmt.Name, TokenTypeType, ModifierDeclaration);
        foreach (var member in stmt.Members)
        {
            EmitFromToken(member, TokenTypeEnumMember, ModifierDeclaration);
        }
        return 0;
    }

    public int VisitInterfaceDeclStmt(InterfaceDeclStmt stmt)
    {
        EmitFromToken(stmt.Name, TokenTypeInterface, ModifierDeclaration);
        for (int i = 0; i < stmt.Fields.Count; i++)
        {
            EmitFromToken(stmt.Fields[i], TokenTypeProperty, ModifierDeclaration);
            if (i < stmt.FieldTypes.Count && stmt.FieldTypes[i] is Token fieldType)
            {
                EmitFromToken(fieldType, TokenTypeType, 0);
            }
        }
        foreach (var method in stmt.Methods)
        {
            EmitFromToken(method.Name, TokenTypeFunction, ModifierDeclaration);
            for (int i = 0; i < method.Parameters.Count; i++)
            {
                EmitFromToken(method.Parameters[i], TokenTypeParameter, ModifierDeclaration);
                if (i < method.ParameterTypes.Count && method.ParameterTypes[i] is Token paramType)
                {
                    EmitFromToken(paramType, TokenTypeType, 0);
                }
            }
            if (method.ReturnType is Token returnType)
            {
                EmitFromToken(returnType, TokenTypeType, 0);
            }
        }
        return 0;
    }

    public int VisitImportStmt(ImportStmt stmt)
    {
        foreach (var name in stmt.Names)
        {
            var def = _result.Symbols.FindDefinition(name.Lexeme, name.Span.StartLine, name.Span.StartColumn);
            if (def != null)
            {
                var (type, modifiers) = MapSymbolKind(def, name);
                EmitFromToken(name, type, modifiers);
            }
            else
            {
                EmitFromToken(name, TokenTypeVariable, ModifierDeclaration);
            }
        }
        stmt.Path.Accept(this);
        return 0;
    }

    public int VisitImportAsStmt(ImportAsStmt stmt)
    {
        stmt.Path.Accept(this);
        EmitFromToken(stmt.Alias, TokenTypeNamespace, ModifierDeclaration);
        return 0;
    }

    public int VisitDestructureStmt(DestructureStmt stmt)
    {
        foreach (var name in stmt.Names)
        {
            int modifiers = ModifierDeclaration | (stmt.IsConst ? ModifierReadonly : 0);
            EmitFromToken(name, TokenTypeVariable, modifiers);
        }
        if (stmt.RestName is Token restName)
        {
            int modifiers = ModifierDeclaration | (stmt.IsConst ? ModifierReadonly : 0);
            EmitFromToken(restName, TokenTypeVariable, modifiers);
        }
        stmt.Initializer.Accept(this);
        return 0;
    }

    // ── Expression Visitors ──

    public int VisitLiteralExpr(LiteralExpr expr) => 0;

    public int VisitIdentifierExpr(IdentifierExpr expr)
    {
        ClassifyStandaloneIdentifier(expr.Name);
        return 0;
    }

    public int VisitUnaryExpr(UnaryExpr expr)
    {
        expr.Right.Accept(this);
        return 0;
    }

    public int VisitUpdateExpr(UpdateExpr expr)
    {
        expr.Operand.Accept(this);
        return 0;
    }

    public int VisitBinaryExpr(BinaryExpr expr)
    {
        expr.Left.Accept(this);
        expr.Right.Accept(this);
        return 0;
    }

    public int VisitIsExpr(IsExpr expr)
    {
        expr.Left.Accept(this);
        if (expr.TypeName != null)
        {
            if (expr.TypeName.Type == TokenType.Identifier &&
                _resolvedRefs.TryGetValue((expr.TypeName.Span.StartLine, expr.TypeName.Span.StartColumn), out var def))
            {
                var (type, modifiers) = MapSymbolKind(def, expr.TypeName);
                EmitFromToken(expr.TypeName, type, modifiers);
            }
            else
            {
                EmitFromToken(expr.TypeName, TokenTypeType, 0);
            }
        }
        else
        {
            expr.TypeExpr!.Accept(this);
        }
        return 0;
    }

    public int VisitGroupingExpr(GroupingExpr expr)
    {
        expr.Expression.Accept(this);
        return 0;
    }

    public int VisitTernaryExpr(TernaryExpr expr)
    {
        expr.Condition.Accept(this);
        expr.ThenBranch.Accept(this);
        expr.ElseBranch.Accept(this);
        return 0;
    }

    public int VisitAssignExpr(AssignExpr expr)
    {
        ClassifyStandaloneIdentifier(expr.Name);
        expr.Value.Accept(this);
        return 0;
    }

    public int VisitCallExpr(CallExpr expr)
    {
        if (expr.Callee is DotExpr dot)
        {
            dot.Object.Accept(this);
            ClassifyDotMember(dot, isCall: true);
        }
        else
        {
            expr.Callee.Accept(this);
        }
        foreach (var arg in expr.Arguments)
        {
            arg.Accept(this);
        }
        return 0;
    }

    public int VisitArrayExpr(ArrayExpr expr)
    {
        foreach (var element in expr.Elements)
        {
            element.Accept(this);
        }
        return 0;
    }

    public int VisitIndexExpr(IndexExpr expr)
    {
        expr.Object.Accept(this);
        expr.Index.Accept(this);
        return 0;
    }

    public int VisitIndexAssignExpr(IndexAssignExpr expr)
    {
        expr.Object.Accept(this);
        expr.Index.Accept(this);
        expr.Value.Accept(this);
        return 0;
    }

    public int VisitStructInitExpr(StructInitExpr expr)
    {
        expr.Target?.Accept(this);

        EmitFromToken(expr.Name, TokenTypeType, 0);
        foreach (var (field, value) in expr.FieldValues)
        {
            EmitFromToken(field, TokenTypeProperty, 0);
            value.Accept(this);
        }
        return 0;
    }

    public int VisitDotExpr(DotExpr expr)
    {
        expr.Object.Accept(this);
        ClassifyDotMember(expr, isCall: false);
        return 0;
    }

    public int VisitDotAssignExpr(DotAssignExpr expr)
    {
        expr.Object.Accept(this);
        EmitFromToken(expr.Name, TokenTypeProperty, 0);
        expr.Value.Accept(this);
        return 0;
    }

    public int VisitInterpolatedStringExpr(InterpolatedStringExpr expr)
    {
        foreach (var part in expr.Parts)
        {
            if (part is not LiteralExpr)
            {
                part.Accept(this);
            }
        }
        return 0;
    }

    public int VisitCommandExpr(CommandExpr expr)
    {
        foreach (var part in expr.Parts)
        {
            if (part is not LiteralExpr)
            {
                part.Accept(this);
            }
        }
        return 0;
    }

    public int VisitPipeExpr(PipeExpr expr)
    {
        expr.Left.Accept(this);
        expr.Right.Accept(this);
        return 0;
    }

    public int VisitTryExpr(TryExpr expr)
    {
        expr.Expression.Accept(this);
        return 0;
    }

    public int VisitAwaitExpr(AwaitExpr expr)
    {
        EmitFromToken(expr.Keyword, TokenTypeKeyword, 0);
        expr.Expression.Accept(this);
        return 0;
    }

    public int VisitRetryExpr(RetryExpr expr)
    {
        EmitFromToken(expr.RetryKeyword, TokenTypeKeyword, 0);
        expr.MaxAttempts.Accept(this);
        expr.OptionsExpr?.Accept(this);
        if (expr.NamedOptions is not null)
            foreach (var (_, value) in expr.NamedOptions)
                value.Accept(this);

        if (expr.OnRetryClause is not null)
        {
            EmitFromToken(expr.OnRetryClause.OnRetryKeyword, TokenTypeKeyword, 0);
            if (expr.OnRetryClause.IsReference && expr.OnRetryClause.Reference is not null)
            {
                expr.OnRetryClause.Reference.Accept(this);
            }
            else if (expr.OnRetryClause.Body is not null)
            {
                if (expr.OnRetryClause.ParamAttempt is not null)
                    EmitFromToken(expr.OnRetryClause.ParamAttempt, TokenTypeVariable, ModifierDeclaration);
                if (expr.OnRetryClause.ParamAttemptTypeHint is not null)
                    EmitFromToken(expr.OnRetryClause.ParamAttemptTypeHint, TokenTypeType, 0);
                if (expr.OnRetryClause.ParamError is not null)
                    EmitFromToken(expr.OnRetryClause.ParamError, TokenTypeVariable, ModifierDeclaration);
                if (expr.OnRetryClause.ParamErrorTypeHint is not null)
                    EmitFromToken(expr.OnRetryClause.ParamErrorTypeHint, TokenTypeType, 0);
                expr.OnRetryClause.Body.Accept(this);
            }
        }

        if (expr.UntilKeyword is not null)
            EmitFromToken(expr.UntilKeyword, TokenTypeKeyword, 0);
        expr.UntilClause?.Accept(this);

        expr.Body.Accept(this);
        return 0;
    }

    public int VisitTimeoutExpr(TimeoutExpr expr)
    {
        EmitFromToken(expr.TimeoutKeyword, TokenTypeKeyword, 0);
        expr.Duration.Accept(this);
        foreach (var stmt in expr.Body.Statements)
            stmt.Accept(this);
        return 0;
    }

    public int VisitNullCoalesceExpr(NullCoalesceExpr expr)
    {
        expr.Left.Accept(this);
        expr.Right.Accept(this);
        return 0;
    }

    public int VisitSwitchExpr(SwitchExpr expr)
    {
        expr.Subject.Accept(this);
        foreach (var arm in expr.Arms)
        {
            arm.Pattern?.Accept(this);

            arm.Body.Accept(this);
        }
        return 0;
    }

    public int VisitLambdaExpr(LambdaExpr expr)
    {
        if (expr.AsyncKeyword is Token asyncTok)
        {
            EmitFromToken(asyncTok, TokenTypeKeyword, 0);
        }
        for (int i = 0; i < expr.Parameters.Count; i++)
        {
            EmitFromToken(expr.Parameters[i], TokenTypeParameter, ModifierDeclaration);
            if (expr.ParameterTypes[i] is Token paramType)
            {
                EmitFromToken(paramType, TokenTypeType, 0);
            }

            if (expr.DefaultValues[i] is Expr defaultVal)
            {
                defaultVal.Accept(this);
            }
        }
        expr.ExpressionBody?.Accept(this);

        expr.BlockBody?.Accept(this);

        return 0;
    }

    public int VisitRedirectExpr(RedirectExpr expr)
    {
        expr.Expression.Accept(this);
        expr.Target.Accept(this);
        return 0;
    }

    public int VisitRangeExpr(RangeExpr expr)
    {
        expr.Start.Accept(this);
        expr.End.Accept(this);
        expr.Step?.Accept(this);

        return 0;
    }

    public int VisitDictLiteralExpr(DictLiteralExpr expr)
    {
        foreach (var (key, value) in expr.Entries)
        {
            if (key is not null)
            {
                EmitFromToken(key, TokenTypeProperty, 0);
            }
            value.Accept(this);
        }
        return 0;
    }

    /// <inheritdoc />
    public int VisitSpreadExpr(SpreadExpr expr)
    {
        EmitFromToken(expr.Operator, TokenTypeOperator, 0);
        expr.Expression.Accept(this);
        return 0;
    }
}
