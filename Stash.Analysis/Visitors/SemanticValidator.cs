namespace Stash.Analysis;

using System;
using System.Collections.Generic;
using Stash.Analysis.Rules;
using Stash.Lexing;
using Stash.Parsing.AST;
using Stash.Stdlib;

/// <summary>
/// Thin dispatcher that walks the AST, maintains nesting counters, and delegates every check to
/// the registered <see cref="IAnalysisRule"/> implementations.
/// </summary>
public class SemanticValidator : IStmtVisitor<object?>, IExprVisitor<object?>
{
    private readonly ScopeTree _scopeTree;
    private readonly List<SemanticDiagnostic> _diagnostics = new();

    private int _loopDepth;
    private int _functionDepth;
    private int _elevateDepth;
    private int _asyncDepth;
    private int _catchDepth;
    private int _lockDepth;
    private readonly Dictionary<string, int> _activeLockPaths = new();

    private static readonly IReadOnlySet<string> _builtInNames = StdlibRegistry.KnownNames;
    private static readonly IReadOnlySet<string> _validBuiltInTypes = StdlibRegistry.ValidTypes;

    private List<Stmt> _allStatements = new();

    private readonly Dictionary<Type, List<IAnalysisRule>> _rulesByNodeType = new();
    private readonly List<IAnalysisRule> _postWalkRules = new();
    private readonly UnreachableCodeRule _unreachableCodeRule;

    /// <summary>
    /// When <see langword="false"/>, <see cref="CheckUnreachableStatements"/> is a no-op because
    /// SA0104 was not included in the caller-supplied rule list.
    /// </summary>
    private readonly bool _runUnreachableCheck;

    public SemanticValidator(ScopeTree scopeTree, IReadOnlyList<IAnalysisRule>? rules = null)
    {
        _scopeTree = scopeTree;

        var allRules = rules ?? RuleRegistry.GetAllRules();

        UnreachableCodeRule? unreachableRule = null;
        foreach (var rule in allRules)
        {
            if (rule is UnreachableCodeRule ucr)
            {
                unreachableRule = ucr;
                continue;
            }

            if (rule.SubscribedNodeTypes.Count == 0)
            {
                _postWalkRules.Add(rule);
            }
            else
            {
                foreach (var nodeType in rule.SubscribedNodeTypes)
                {
                    if (!_rulesByNodeType.TryGetValue(nodeType, out var list))
                    {
                        list = new List<IAnalysisRule>();
                        _rulesByNodeType[nodeType] = list;
                    }
                    list.Add(rule);
                }
            }
        }

        // When rules == null we use the full default set, so always run the reachability check.
        // When rules is explicitly provided, only run it if UnreachableCodeRule was included.
        _unreachableCodeRule = unreachableRule ?? new UnreachableCodeRule();
        _runUnreachableCheck = unreachableRule != null || rules is null;
    }

    public List<SemanticDiagnostic> Validate(List<Stmt> statements)
    {
        _diagnostics.Clear();
        _allStatements = statements;
        _loopDepth = 0;
        _functionDepth = 0;
        _elevateDepth = 0;

        CheckUnreachableStatements(statements);

        foreach (var stmt in statements)
        {
            stmt.Accept(this);
        }

        var postContext = BuildPostContext(statements);
        foreach (var rule in _postWalkRules)
        {
            rule.Analyze(postContext);
        }

        return _diagnostics;
    }

    private void DispatchNodeRules(Stmt stmt)
    {
        if (_rulesByNodeType.TryGetValue(stmt.GetType(), out var rules))
        {
            var context = BuildContext(stmt);
            foreach (var rule in rules)
            {
                rule.Analyze(context);
            }
        }
    }

    private void DispatchNodeRules(Expr expr)
    {
        if (_rulesByNodeType.TryGetValue(expr.GetType(), out var rules))
        {
            var context = BuildExprContext(expr);
            foreach (var rule in rules)
            {
                rule.Analyze(context);
            }
        }
    }

    private void CheckUnreachableStatements(List<Stmt> statements)
    {
        if (!_runUnreachableCheck)
        {
            return;
        }

        _unreachableCodeRule.Analyze(new RuleContext
        {
            AllStatements = statements,
            ScopeTree = _scopeTree,
            BuiltInNames = _builtInNames,
            ValidBuiltInTypes = _validBuiltInTypes,
            ReportDiagnostic = _diagnostics.Add
        });
    }

    private RuleContext BuildContext(Stmt stmt) => new RuleContext
    {
        Statement = stmt,
        ScopeTree = _scopeTree,
        BuiltInNames = _builtInNames,
        ValidBuiltInTypes = _validBuiltInTypes,
        AllStatements = _allStatements,
        LoopDepth = _loopDepth,
        FunctionDepth = _functionDepth,
        ElevateDepth = _elevateDepth,
        ReportDiagnostic = _diagnostics.Add
    };

    private RuleContext BuildExprContext(Expr expr) => new RuleContext
    {
        Expression = expr,
        ScopeTree = _scopeTree,
        BuiltInNames = _builtInNames,
        ValidBuiltInTypes = _validBuiltInTypes,
        AllStatements = _allStatements,
        LoopDepth = _loopDepth,
        FunctionDepth = _functionDepth,
        ElevateDepth = _elevateDepth,
        ReportDiagnostic = _diagnostics.Add
    };

    private RuleContext BuildPostContext(List<Stmt> statements) => new RuleContext
    {
        ScopeTree = _scopeTree,
        BuiltInNames = _builtInNames,
        ValidBuiltInTypes = _validBuiltInTypes,
        AllStatements = statements,
        ReportDiagnostic = _diagnostics.Add
    };

    // Statement visitors

    public object? VisitFnDeclStmt(FnDeclStmt stmt)
    {
        DispatchNodeRules(stmt);

        _functionDepth++;
        if (stmt.IsAsync) _asyncDepth++;
        var savedStatements = _allStatements;
        _allStatements = stmt.Body.Statements;
        CheckUnreachableStatements(stmt.Body.Statements);
        foreach (var s in stmt.Body.Statements)
        {
            s.Accept(this);
        }
        _allStatements = savedStatements;
        if (stmt.IsAsync) _asyncDepth--;
        _functionDepth--;

        return null;
    }

    public object? VisitWhileStmt(WhileStmt stmt)
    {
        DispatchNodeRules(stmt);

        stmt.Condition.Accept(this);

        _loopDepth++;
        stmt.Body.Accept(this);
        _loopDepth--;

        return null;
    }

    public object? VisitElevateStmt(ElevateStmt stmt)
    {
        DispatchNodeRules(stmt);

        stmt.Elevator?.Accept(this);

        _elevateDepth++;
        stmt.Body.Accept(this);
        _elevateDepth--;

        return null;
    }

    public object? VisitDoWhileStmt(DoWhileStmt stmt)
    {
        DispatchNodeRules(stmt);

        _loopDepth++;
        stmt.Body.Accept(this);
        _loopDepth--;

        stmt.Condition.Accept(this);

        return null;
    }

    public object? VisitForStmt(ForStmt stmt)
    {
        DispatchNodeRules(stmt);

        stmt.Initializer?.Accept(this);
        stmt.Condition?.Accept(this);

        _loopDepth++;
        stmt.Body.Accept(this);
        stmt.Increment?.Accept(this);
        _loopDepth--;

        return null;
    }

    public object? VisitForInStmt(ForInStmt stmt)
    {
        DispatchNodeRules(stmt);

        stmt.Iterable.Accept(this);

        _loopDepth++;
        stmt.Body.Accept(this);
        _loopDepth--;

        return null;
    }

    public object? VisitBreakStmt(BreakStmt stmt)
    {
        DispatchNodeRules(stmt);
        return null;
    }

    public object? VisitContinueStmt(ContinueStmt stmt)
    {
        DispatchNodeRules(stmt);
        return null;
    }

    public object? VisitReturnStmt(ReturnStmt stmt)
    {
        DispatchNodeRules(stmt);
        stmt.Value?.Accept(this);
        return null;
    }

    public object? VisitVarDeclStmt(VarDeclStmt stmt)
    {
        DispatchNodeRules(stmt);
        stmt.Initializer?.Accept(this);
        return null;
    }

    public object? VisitConstDeclStmt(ConstDeclStmt stmt)
    {
        DispatchNodeRules(stmt);
        stmt.Initializer.Accept(this);
        return null;
    }

    public object? VisitDestructureStmt(DestructureStmt stmt)
    {
        stmt.Initializer.Accept(this);
        return null;
    }

    public object? VisitBlockStmt(BlockStmt stmt)
    {
        DispatchNodeRules(stmt);
        var savedStatements = _allStatements;
        _allStatements = stmt.Statements;
        CheckUnreachableStatements(stmt.Statements);
        foreach (var s in stmt.Statements)
        {
            s.Accept(this);
        }
        _allStatements = savedStatements;
        return null;
    }

    public object? VisitIfStmt(IfStmt stmt)
    {
        DispatchNodeRules(stmt);

        stmt.Condition.Accept(this);
        stmt.ThenBranch.Accept(this);
        stmt.ElseBranch?.Accept(this);

        return null;
    }

    public object? VisitStructDeclStmt(StructDeclStmt stmt)
    {
        DispatchNodeRules(stmt);

        _functionDepth++;
        foreach (var method in stmt.Methods)
        {
            DispatchNodeRules(method);
            var savedStatements = _allStatements;
            _allStatements = method.Body.Statements;
            CheckUnreachableStatements(method.Body.Statements);
            foreach (var s in method.Body.Statements)
            {
                s.Accept(this);
            }
            _allStatements = savedStatements;
        }
        _functionDepth--;

        return null;
    }

    public object? VisitExtendStmt(ExtendStmt stmt)
    {
        DispatchNodeRules(stmt);

        _functionDepth++;
        foreach (var method in stmt.Methods)
        {
            DispatchNodeRules(method);
            var savedStatements = _allStatements;
            _allStatements = method.Body.Statements;
            CheckUnreachableStatements(method.Body.Statements);
            foreach (var s in method.Body.Statements)
            {
                s.Accept(this);
            }
            _allStatements = savedStatements;
        }
        _functionDepth--;

        return null;
    }

    public object? VisitEnumDeclStmt(EnumDeclStmt stmt)
    {
        DispatchNodeRules(stmt);
        return null;
    }

    public object? VisitInterfaceDeclStmt(InterfaceDeclStmt stmt)
    {
        DispatchNodeRules(stmt);
        return null;
    }

    public object? VisitImportStmt(ImportStmt stmt) => null;

    public object? VisitImportAsStmt(ImportAsStmt stmt) => null;

    public object? VisitThrowStmt(ThrowStmt stmt)
    {
        if (stmt.Value is null)
        {
            // Bare rethrow
            if (_catchDepth == 0)
                _diagnostics.Add(DiagnosticDescriptors.SA0160.CreateDiagnostic(stmt.Keyword.Span));
        }
        else
        {
            stmt.Value.Accept(this);
        }
        return null;
    }

    public object? VisitTryCatchStmt(TryCatchStmt stmt)
    {
        DispatchNodeRules(stmt);

        stmt.TryBody.Accept(this);

        // Validate catch clause ordering: catch-all must be last; no duplicates
        bool seenCatchAll = false;
        for (int i = 0; i < stmt.CatchClauses.Count; i++)
        {
            CatchClause clause = stmt.CatchClauses[i];
            if (seenCatchAll)
            {
                // This clause is unreachable (after a catch-all)
                if (clause.IsCatchAll)
                    _diagnostics.Add(DiagnosticDescriptors.SA0162.CreateDiagnostic(clause.Keyword.Span));
                else
                    _diagnostics.Add(DiagnosticDescriptors.SA0161.CreateDiagnostic(clause.Keyword.Span));
            }
            if (clause.IsCatchAll)
                seenCatchAll = true;

            // SA0163: warn on explicit "RuntimeError" catch type (not for untyped catch-all)
            foreach (Token typeToken in clause.TypeTokens)
            {
                if (typeToken.Lexeme == "RuntimeError")
                    _diagnostics.Add(DiagnosticDescriptors.SA0163.CreateDiagnostic(typeToken.Span));
            }

            _catchDepth++;
            clause.Body.Accept(this);
            _catchDepth--;
        }

        stmt.FinallyBody?.Accept(this);
        return null;
    }

    public object? VisitSwitchStmt(SwitchStmt stmt)
    {
        DispatchNodeRules(stmt);
        stmt.Subject.Accept(this);
        foreach (SwitchCase @case in stmt.Cases)
        {
            foreach (Expr pattern in @case.Patterns)
            {
                pattern.Accept(this);
            }
            @case.Body.Accept(this);
        }
        return null;
    }

    public object? VisitDeferStmt(DeferStmt stmt)
    {
        // SA0150: defer inside a loop accumulates deferred calls
        if (_loopDepth > 0)
        {
            _diagnostics.Add(DiagnosticDescriptors.SA0150.CreateDiagnostic(stmt.DeferKeyword.Span));
        }

        // SA0152: empty defer block has no effect
        if (stmt.Body is BlockStmt block && block.Statements.Count == 0)
        {
            _diagnostics.Add(DiagnosticDescriptors.SA0152.CreateDiagnostic(stmt.Span));
        }

        // SA0153: defer await outside an async function
        if (stmt.HasAwait && _asyncDepth == 0)
        {
            _diagnostics.Add(DiagnosticDescriptors.SA0153.CreateDiagnostic(stmt.DeferKeyword.Span));
        }

        stmt.Body.Accept(this);
        return null;
    }

    public object? VisitLockStmt(LockStmt stmt)
    {
        DispatchNodeRules(stmt);

        // SA0810: lock path is a non-string, non-null literal
        if (stmt.Path is LiteralExpr pathLit && pathLit.Value is not string && pathLit.Value is not null)
            _diagnostics.Add(DiagnosticDescriptors.SA0810.CreateDiagnostic(stmt.Path.Span));

        // SA0811: empty lock body
        if (stmt.Body.Statements.Count == 0)
            _diagnostics.Add(DiagnosticDescriptors.SA0811.CreateDiagnostic(stmt.Body.Span));

        // SA0812: nested lock on same literal path
        string? literalPath = stmt.Path is LiteralExpr strLit && strLit.Value is string s ? s : null;
        if (literalPath != null && _activeLockPaths.TryGetValue(literalPath, out int pathCount) && pathCount > 0)
            _diagnostics.Add(DiagnosticDescriptors.SA0812.CreateDiagnostic(stmt.LockKeyword.Span, literalPath));

        stmt.Path.Accept(this);
        stmt.WaitOption?.Accept(this);
        stmt.StaleOption?.Accept(this);

        if (literalPath != null)
            _activeLockPaths[literalPath] = _activeLockPaths.GetValueOrDefault(literalPath) + 1;

        _lockDepth++;
        stmt.Body.Accept(this);
        _lockDepth--;

        if (literalPath != null)
        {
            int remaining = _activeLockPaths[literalPath] - 1;
            if (remaining == 0)
                _activeLockPaths.Remove(literalPath);
            else
                _activeLockPaths[literalPath] = remaining;
        }

        return null;
    }

    public object? VisitExprStmt(ExprStmt stmt)
    {
        DispatchNodeRules(stmt);
        stmt.Expression.Accept(this);
        return null;
    }

    // Expression visitors

    public object? VisitAssignExpr(AssignExpr expr)
    {
        DispatchNodeRules(expr);
        expr.Value.Accept(this);
        return null;
    }

    public object? VisitCallExpr(CallExpr expr)
    {
        DispatchNodeRules(expr);

        // Visit callee only when not handled by arity/type rules
        if (expr.Callee is not IdentifierExpr
            && !(expr.Callee is DotExpr dottedCallee
                && dottedCallee.Object is IdentifierExpr nsId
                && StdlibRegistry.IsBuiltInNamespace(nsId.Name.Lexeme)))
        {
            expr.Callee.Accept(this);
        }

        foreach (var arg in expr.Arguments)
        {
            arg.Accept(this);
        }

        return null;
    }

    public object? VisitLiteralExpr(LiteralExpr expr) => null;

    public object? VisitIdentifierExpr(IdentifierExpr expr) => null;

    public object? VisitUnaryExpr(UnaryExpr expr)
    {
        expr.Right.Accept(this);
        return null;
    }

    public object? VisitUpdateExpr(UpdateExpr expr)
    {
        expr.Operand.Accept(this);
        return null;
    }

    public object? VisitBinaryExpr(BinaryExpr expr)
    {
        DispatchNodeRules(expr);
        expr.Left.Accept(this);
        expr.Right.Accept(this);
        return null;
    }

    public object? VisitIsExpr(IsExpr expr)
    {
        expr.Left.Accept(this);
        expr.TypeExpr?.Accept(this);
        return null;
    }

    public object? VisitGroupingExpr(GroupingExpr expr)
    {
        expr.Expression.Accept(this);
        return null;
    }

    public object? VisitTernaryExpr(TernaryExpr expr)
    {
        DispatchNodeRules(expr);
        expr.Condition.Accept(this);
        expr.ThenBranch.Accept(this);
        expr.ElseBranch.Accept(this);
        return null;
    }

    public object? VisitSwitchExpr(SwitchExpr expr)
    {
        DispatchNodeRules(expr);
        expr.Subject.Accept(this);
        foreach (var arm in expr.Arms)
        {
            arm.Pattern?.Accept(this);
            arm.Body.Accept(this);
        }
        return null;
    }

    public object? VisitArrayExpr(ArrayExpr expr)
    {
        DispatchNodeRules(expr);
        foreach (var el in expr.Elements)
        {
            el.Accept(this);
        }
        return null;
    }

    public object? VisitDictLiteralExpr(DictLiteralExpr expr)
    {
        DispatchNodeRules(expr);
        foreach (var (_, value) in expr.Entries)
        {
            value.Accept(this);
        }
        return null;
    }

    public object? VisitIndexExpr(IndexExpr expr)
    {
        expr.Object.Accept(this);
        expr.Index.Accept(this);
        return null;
    }

    public object? VisitIndexAssignExpr(IndexAssignExpr expr)
    {
        expr.Object.Accept(this);
        expr.Index.Accept(this);
        expr.Value.Accept(this);
        return null;
    }

    public object? VisitStructInitExpr(StructInitExpr expr)
    {
        foreach (var (_, value) in expr.FieldValues)
        {
            value.Accept(this);
        }
        return null;
    }

    public object? VisitDotExpr(DotExpr expr)
    {
        expr.Object.Accept(this);
        return null;
    }

    public object? VisitDotAssignExpr(DotAssignExpr expr)
    {
        expr.Object.Accept(this);
        expr.Value.Accept(this);
        DispatchNodeRules(expr);
        return null;
    }

    public object? VisitInterpolatedStringExpr(InterpolatedStringExpr expr)
    {
        foreach (var part in expr.Parts)
        {
            part.Accept(this);
        }
        return null;
    }

    public object? VisitCommandExpr(CommandExpr expr)
    {
        DispatchNodeRules(expr);
        foreach (var part in expr.Parts)
        {
            part.Accept(this);
        }
        return null;
    }

    public object? VisitPipeExpr(PipeExpr expr)
    {
        // Flatten the left-associative PipeExpr chain into an ordered stage list
        var stages = new List<Expr>();
        Expr current = expr;
        while (current is PipeExpr pipe)
        {
            stages.Insert(0, pipe.Right);
            current = pipe.Left;
        }
        stages.Insert(0, current); // leftmost non-pipe node

        // Validate and visit each stage
        foreach (var stage in stages)
        {
            if (stage is CommandExpr { IsPassthrough: true } cmd)
                _diagnostics.Add(DiagnosticDescriptors.SA0710.CreateDiagnostic(cmd.Span));

            stage.Accept(this);
        }

        return null;
    }

    public object? VisitRedirectExpr(RedirectExpr expr)
    {
        expr.Expression.Accept(this);
        expr.Target.Accept(this);
        return null;
    }

    public object? VisitTryExpr(TryExpr expr)
    {
        expr.Expression.Accept(this);
        return null;
    }

    public object? VisitAwaitExpr(AwaitExpr expr)
    {
        expr.Expression.Accept(this);
        return null;
    }

    public object? VisitRetryExpr(RetryExpr expr)
    {
        expr.MaxAttempts.Accept(this);
        expr.OptionsExpr?.Accept(this);
        if (expr.NamedOptions is not null)
        {
            foreach (var (_, value) in expr.NamedOptions)
            {
                value.Accept(this);
            }
        }
        expr.UntilClause?.Accept(this);
        if (expr.OnRetryClause is { IsReference: true, Reference: not null })
        {
            expr.OnRetryClause.Reference.Accept(this);
        }
        else if (expr.OnRetryClause is { Body: not null } onRetry)
        {
            onRetry.Body.Accept(this);
        }
        expr.Body.Accept(this);

        DispatchNodeRules(expr);

        return null;
    }

    public object? VisitTimeoutExpr(TimeoutExpr expr)
    {
        expr.Duration.Accept(this);
        expr.Body.Accept(this);
        return null;
    }

    public object? VisitNullCoalesceExpr(NullCoalesceExpr expr)
    {
        expr.Left.Accept(this);
        expr.Right.Accept(this);
        return null;
    }

    public object? VisitRangeExpr(RangeExpr expr)
    {
        expr.Start.Accept(this);
        expr.End.Accept(this);
        expr.Step?.Accept(this);
        return null;
    }

    public object? VisitLambdaExpr(LambdaExpr expr)
    {
        DispatchNodeRules(expr);

        _functionDepth++;
        if (expr.IsAsync) _asyncDepth++;

        if (expr.ExpressionBody != null)
        {
            expr.ExpressionBody.Accept(this);
        }
        else if (expr.BlockBody != null)
        {
            expr.BlockBody.Accept(this);
        }

        if (expr.IsAsync) _asyncDepth--;
        _functionDepth--;

        return null;
    }

    public object? VisitSpreadExpr(SpreadExpr expr)
    {
        expr.Expression.Accept(this);
        return null;
    }
}
