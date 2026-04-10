namespace Stash.Analysis.Rules;

using System;
using System.Collections.Generic;
using Stash.Parsing.AST;
using Stash.Runtime.Types;

/// <summary>
/// SA0702–SA0709 — Validates <c>retry</c> expressions, covering: shell-command-only bodies
/// without an <c>until</c> clause, zero or single attempt counts, invalid <c>on</c> filter
/// values, invalid <c>until</c> clause expressions, <c>backoff</c> without a delay, and retry
/// bodies with no throwable operations.
/// </summary>
public sealed class RetryValidationRule : IAnalysisRule
{
    public DiagnosticDescriptor Descriptor => DiagnosticDescriptors.SA0702;

    public IReadOnlySet<Type> SubscribedNodeTypes { get; } = new HashSet<Type> { typeof(RetryExpr) };

    public void Analyze(RuleContext context)
    {
        if (context.Expression is not RetryExpr expr)
        {
            return;
        }

        // SA0702: retry body contains only non-strict shell commands (never throw without until)
        if (expr.Body.Statements.Count > 0
            && expr.UntilClause is null
            && expr.Body.Statements.TrueForAll(s => s is ExprStmt es && es.Expression is CommandExpr { IsStrict: false } or PipeExpr or RedirectExpr))
        {
            context.ReportDiagnostic(DiagnosticDescriptors.SA0702.CreateDiagnostic(expr.RetryKeyword.Span));
        }

        // SA0703 / SA0704: attempt count literals
        if (expr.MaxAttempts is LiteralExpr { Value: long attempts })
        {
            if (attempts == 0)
            {
                context.ReportDiagnostic(DiagnosticDescriptors.SA0703.CreateDiagnostic(expr.MaxAttempts.Span));
            }
            else if (attempts == 1)
            {
                context.ReportDiagnostic(DiagnosticDescriptors.SA0704.CreateDiagnostic(expr.MaxAttempts.Span));
            }
        }

        // SA0705 / SA0706: on filter validation
        if (expr.NamedOptions is not null)
        {
            foreach (var option in expr.NamedOptions)
            {
                if (option.Name.Lexeme == "on")
                {
                    if (option.Value is ArrayExpr arrayExpr)
                    {
                        foreach (var element in arrayExpr.Elements)
                        {
                            if (element is not LiteralExpr { Value: string } and not IdentifierExpr)
                            {
                                context.ReportDiagnostic(DiagnosticDescriptors.SA0705.CreateDiagnostic(element.Span));
                            }
                        }
                    }
                    else if (option.Value is not IdentifierExpr)
                    {
                        context.ReportDiagnostic(DiagnosticDescriptors.SA0706.CreateDiagnostic(option.Value.Span));
                    }
                    break;
                }
            }
        }

        // SA0707: invalid until clause
        if (expr.UntilClause is not null
            && expr.UntilClause is not LambdaExpr
            && expr.UntilClause is not IdentifierExpr
            && expr.UntilClause is not DotExpr
            && expr.UntilClause is not CallExpr)
        {
            context.ReportDiagnostic(DiagnosticDescriptors.SA0707.CreateDiagnostic(expr.UntilClause.Span));
        }

        // SA0708: backoff without delay
        if (expr.NamedOptions is not null)
        {
            (Stash.Lexing.Token Name, Expr Value)? backoffOption = null;
            bool hasNonZeroDelay = false;
            foreach (var option in expr.NamedOptions)
            {
                if (option.Name.Lexeme == "backoff")
                    backoffOption = option;
                else if (option.Name.Lexeme == "delay")
                {
                    if (option.Value is LiteralExpr lit && lit.Value is StashDuration dur)
                        hasNonZeroDelay = dur.TotalMilliseconds != 0;
                    else
                        hasNonZeroDelay = true;
                }
            }
            if (backoffOption is { } b && !hasNonZeroDelay)
            {
                context.ReportDiagnostic(DiagnosticDescriptors.SA0708.CreateDiagnostic(b.Name.Span));
            }
        }

        // SA0709: no throwable operations in retry body
        if (expr.UntilClause is null
            && expr.Body.Statements.Count > 0
            && !ContainsThrowableOperation(expr.Body.Statements))
        {
            context.ReportDiagnostic(DiagnosticDescriptors.SA0709.CreateDiagnostic(expr.RetryKeyword.Span));
        }
    }

    private static bool ContainsThrowableOperation(List<Stmt> statements)
    {
        foreach (var stmt in statements)
        {
            if (stmt is ExprStmt es && ContainsThrowableExpr(es.Expression)) return true;
            if (stmt is ThrowStmt) return true;
            if (stmt is VarDeclStmt vds && vds.Initializer is not null && ContainsThrowableExpr(vds.Initializer)) return true;
            if (stmt is ConstDeclStmt cds && ContainsThrowableExpr(cds.Initializer)) return true;
            if (stmt is IfStmt or TryCatchStmt or ForInStmt or ForStmt or WhileStmt or DoWhileStmt) return true;
            if (stmt is ReturnStmt rs && rs.Value is not null && ContainsThrowableExpr(rs.Value)) return true;
        }
        return false;
    }

    private static bool ContainsThrowableExpr(Expr expr)
    {
        return expr is CallExpr or CommandExpr or DotExpr or IndexExpr or PipeExpr or RedirectExpr;
    }
}
