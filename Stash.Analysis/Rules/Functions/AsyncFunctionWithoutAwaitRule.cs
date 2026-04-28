namespace Stash.Analysis.Rules;

using System;
using System.Collections.Generic;
using System.Linq;
using Stash.Parsing.AST;

/// <summary>
/// SA0407 — Warns when an async function declaration contains no await expressions.
/// </summary>
public sealed class AsyncFunctionWithoutAwaitRule : IAnalysisRule
{
    public DiagnosticDescriptor Descriptor => DiagnosticDescriptors.SA0407;

    public IReadOnlySet<Type> SubscribedNodeTypes { get; } = new HashSet<Type> { typeof(FnDeclStmt) };

    public void Analyze(RuleContext context)
    {
        if (context.Statement is not FnDeclStmt fn) return;
        if (!fn.IsAsync) return;

        // Check if body contains any await expression (not inside nested functions/lambdas)
        if (!ContainsAwait(fn.Body.Statements))
        {
            context.ReportDiagnostic(
                DiagnosticDescriptors.SA0407.CreateDiagnostic(fn.Name.Span, fn.Name.Lexeme));
        }
    }

    private static bool ContainsAwait(IEnumerable<Stmt> stmts)
    {
        foreach (var stmt in stmts)
        {
            if (StmtContainsAwait(stmt)) return true;
        }
        return false;
    }

    private static bool StmtContainsAwait(Stmt stmt) => stmt switch
    {
        // Stop at nested function boundaries — they have their own async context
        FnDeclStmt => false,
        ExprStmt exprStmt => ExprContainsAwait(exprStmt.Expression),
        VarDeclStmt varDecl => varDecl.Initializer != null && ExprContainsAwait(varDecl.Initializer),
        ConstDeclStmt constDecl => ExprContainsAwait(constDecl.Initializer),
        ReturnStmt ret => ret.Value != null && ExprContainsAwait(ret.Value),
        IfStmt ifStmt => ExprContainsAwait(ifStmt.Condition)
                      || StmtContainsAwait(ifStmt.ThenBranch)
                      || (ifStmt.ElseBranch != null && StmtContainsAwait(ifStmt.ElseBranch)),
        WhileStmt whileStmt => ExprContainsAwait(whileStmt.Condition)
                            || StmtContainsAwait(whileStmt.Body),
        DoWhileStmt doWhile => StmtContainsAwait(doWhile.Body)
                            || ExprContainsAwait(doWhile.Condition),
        ForStmt forStmt => (forStmt.Initializer != null && StmtContainsAwait(forStmt.Initializer))
                        || (forStmt.Condition != null && ExprContainsAwait(forStmt.Condition))
                        || (forStmt.Increment != null && ExprContainsAwait(forStmt.Increment))
                        || StmtContainsAwait(forStmt.Body),
        ForInStmt forIn => ExprContainsAwait(forIn.Iterable)
                        || StmtContainsAwait(forIn.Body),
        BlockStmt block => ContainsAwait(block.Statements),
        TryCatchStmt tryCatch => ContainsAwait(tryCatch.TryBody.Statements)
                              || tryCatch.CatchClauses.Any(cc => ContainsAwait(cc.Body.Statements))
                              || (tryCatch.FinallyBody != null && ContainsAwait(tryCatch.FinallyBody.Statements)),
        DeferStmt deferStmt => StmtContainsAwait(deferStmt.Body),
        ElevateStmt elevateStmt => ContainsAwait(elevateStmt.Body.Statements),
        LockStmt lockStmt => ContainsAwait(lockStmt.Body.Statements),
        _ => false
    };

    private static bool ExprContainsAwait(Expr expr) => expr switch
    {
        AwaitExpr => true,
        // Stop at nested lambda boundaries — they have their own async context
        LambdaExpr => false,
        BinaryExpr bin => ExprContainsAwait(bin.Left) || ExprContainsAwait(bin.Right),
        UnaryExpr unary => ExprContainsAwait(unary.Right),
        CallExpr call => ExprContainsAwait(call.Callee) || call.Arguments.Any(ExprContainsAwait),
        DotExpr dot => ExprContainsAwait(dot.Object),
        GroupingExpr group => ExprContainsAwait(group.Expression),
        AssignExpr assign => ExprContainsAwait(assign.Value),
        _ => false
    };
}
