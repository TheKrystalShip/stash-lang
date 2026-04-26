using System;
using Stash.Analysis.Formatting.Rules;
using Stash.Parsing.AST;

namespace Stash.Analysis.Formatting.Printers;

internal static class ControlFlowPrinter
{
    internal static void PrintIf(IfStmt stmt, FormatContext ctx, Action<Stmt> formatStmt, Action<Expr> formatExpr)
    {
        ctx.EmitToken(); // if
        ctx.Space();
        ctx.EmitToken(); // (
        formatExpr(stmt.Condition);
        ctx.EmitToken(); // )
        BraceRules.BeforeOpenBrace(ctx);
        ctx.PushScope(ScopeKind.ControlFlowBody);
        formatStmt(stmt.ThenBranch);
        ctx.PopScope();
        if (stmt.ElseBranch != null)
        {
            ctx.Space();
            ctx.EmitToken(); // else
            if (stmt.ElseBranch is IfStmt)
            {
                ctx.Space();
                formatStmt(stmt.ElseBranch);
            }
            else
            {
                BraceRules.BeforeOpenBrace(ctx);
                ctx.PushScope(ScopeKind.ControlFlowBody);
                formatStmt(stmt.ElseBranch);
                ctx.PopScope();
            }
        }
    }

    internal static void PrintWhile(WhileStmt stmt, FormatContext ctx, Action<Stmt> formatStmt, Action<Expr> formatExpr)
    {
        ctx.EmitToken(); // while
        ctx.Space();
        ctx.EmitToken(); // (
        formatExpr(stmt.Condition);
        ctx.EmitToken(); // )
        BraceRules.BeforeOpenBrace(ctx);
        ctx.PushScope(ScopeKind.ControlFlowBody);
        formatStmt(stmt.Body);
        ctx.PopScope();
    }

    internal static void PrintDoWhile(DoWhileStmt stmt, FormatContext ctx, Action<Stmt> formatStmt, Action<Expr> formatExpr)
    {
        ctx.EmitToken(); // do
        BraceRules.BeforeOpenBrace(ctx);
        ctx.PushScope(ScopeKind.ControlFlowBody);
        formatStmt(stmt.Body);
        ctx.PopScope();
        ctx.Space();
        ctx.EmitToken(); // while
        ctx.Space();
        ctx.EmitToken(); // (
        formatExpr(stmt.Condition);
        ctx.EmitToken(); // )
        ctx.EmitToken(); // ;
    }

    internal static void PrintFor(ForStmt stmt, FormatContext ctx, Action<Stmt> formatStmt, Action<Expr> formatExpr)
    {
        ctx.EmitToken(); // for
        ctx.Space();
        ctx.EmitToken(); // (
        if (stmt.Initializer is not null)
        {
            formatStmt(stmt.Initializer);
        }
        else
        {
            ctx.EmitToken(); // ;
        }
        ctx.Space();
        if (stmt.Condition is not null)
        {
            formatExpr(stmt.Condition);
        }
        ctx.EmitToken(); // ;
        if (stmt.Increment is not null)
        {
            ctx.Space();
            formatExpr(stmt.Increment);
        }
        ctx.EmitToken(); // )
        BraceRules.BeforeOpenBrace(ctx);
        ctx.PushScope(ScopeKind.ControlFlowBody);
        formatStmt(stmt.Body);
        ctx.PopScope();
    }

    internal static void PrintForIn(ForInStmt stmt, FormatContext ctx, Action<Stmt> formatStmt, Action<Expr> formatExpr)
    {
        ctx.EmitToken(); // for
        ctx.Space();
        ctx.EmitToken(); // (
        ctx.EmitToken(); // let
        ctx.Space();
        if (stmt.IndexName != null)
        {
            ctx.EmitToken(); // index variable
            ctx.EmitToken(); // ,
            ctx.Space();
        }
        ctx.EmitToken(); // loop variable
        if (stmt.TypeHint != null)
        {
            ctx.EmitToken(); // :
            ctx.Space();
            ctx.EmitToken(); // type
        }
        ctx.Space();
        ctx.EmitToken(); // in
        ctx.Space();
        formatExpr(stmt.Iterable);
        ctx.EmitToken(); // )
        BraceRules.BeforeOpenBrace(ctx);
        ctx.PushScope(ScopeKind.ControlFlowBody);
        formatStmt(stmt.Body);
        ctx.PopScope();
    }

    internal static void PrintSwitch(SwitchStmt stmt, FormatContext ctx, Action<Stmt> formatStmt, Action<Expr> formatExpr)
    {
        ctx.EmitToken(); // switch
        ctx.Space();
        ctx.EmitToken(); // (
        formatExpr(stmt.Subject);
        ctx.EmitToken(); // )
        BraceRules.BeforeOpenBrace(ctx);
        ctx.EmitToken(); // {
        ctx.Indent++;
        int mark = ctx.Mark();
        foreach (SwitchCase @case in stmt.Cases)
        {
            ctx.NewLine();
            if (@case.IsDefault)
            {
                ctx.EmitToken(); // default
            }
            else
            {
                ctx.EmitToken(); // case
                ctx.Space();
                for (int i = 0; i < @case.Patterns.Count; i++)
                {
                    formatExpr(@case.Patterns[i]);
                    if (i < @case.Patterns.Count - 1)
                    {
                        ctx.EmitToken(); // ,
                        ctx.Space();
                    }
                }
            }
            ctx.Space();
            ctx.EmitToken(); // :
            BraceRules.BeforeOpenBrace(ctx);
            ctx.PushScope(ScopeKind.SwitchCase);
            formatStmt(@case.Body);
            ctx.PopScope();
        }
        ctx.WrapFrom(mark, Doc.Indent);
        ctx.Indent--;
        ctx.NewLine();
        ctx.EmitToken(); // }
    }

    internal static void PrintTryCatch(TryCatchStmt stmt, FormatContext ctx, Action<Stmt> formatStmt)
    {
        ctx.EmitToken(); // try
        BraceRules.BeforeOpenBrace(ctx);
        ctx.PushScope(ScopeKind.TryCatchBody);
        formatStmt(stmt.TryBody);
        ctx.PopScope();

        foreach (CatchClause clause in stmt.CatchClauses)
        {
            ctx.Space();
            ctx.EmitToken(); // catch
            ctx.Space();
            ctx.EmitToken(); // (
            // For typed catch: emit type names with | between them, then the variable
            if (clause.TypeTokens.Count > 0)
            {
                for (int i = 0; i < clause.TypeTokens.Count; i++)
                {
                    if (i > 0)
                    {
                        ctx.Space();
                        ctx.EmitToken(); // |
                        ctx.Space();
                    }
                    ctx.EmitToken(); // type name
                }
                ctx.Space();
            }
            ctx.EmitToken(); // variable
            ctx.EmitToken(); // )
            BraceRules.BeforeOpenBrace(ctx);
            ctx.PushScope(ScopeKind.TryCatchBody);
            formatStmt(clause.Body);
            ctx.PopScope();
        }

        if (stmt.FinallyBody is not null)
        {
            ctx.Space();
            ctx.EmitToken(); // finally
            BraceRules.BeforeOpenBrace(ctx);
            ctx.PushScope(ScopeKind.TryCatchBody);
            formatStmt(stmt.FinallyBody);
            ctx.PopScope();
        }
    }

    internal static void PrintElevate(ElevateStmt stmt, FormatContext ctx, Action<Stmt> formatStmt, Action<Expr> formatExpr)
    {
        ctx.EmitToken(); // elevate
        if (stmt.Elevator != null)
        {
            ctx.EmitToken(); // (
            formatExpr(stmt.Elevator);
            ctx.EmitToken(); // )
        }
        BraceRules.BeforeOpenBrace(ctx);
        ctx.PushScope(ScopeKind.ElevateBody);
        formatStmt(stmt.Body);
        ctx.PopScope();
    }

    internal static void PrintReturn(ReturnStmt stmt, FormatContext ctx, Action<Expr> formatExpr)
    {
        ctx.EmitToken(); // return
        if (stmt.Value != null)
        {
            ctx.Space();
            formatExpr(stmt.Value);
        }
        ctx.EmitToken(); // ;
    }

    internal static void PrintThrow(ThrowStmt stmt, FormatContext ctx, Action<Expr> formatExpr)
    {
        ctx.EmitToken(); // throw
        if (stmt.Value is not null)
        {
            ctx.Space();
            formatExpr(stmt.Value);
        }
        ctx.EmitToken(); // ;
    }

    internal static void PrintBreak(FormatContext ctx)
    {
        ctx.EmitToken(); // break
        ctx.EmitToken(); // ;
    }

    internal static void PrintContinue(FormatContext ctx)
    {
        ctx.EmitToken(); // continue
        ctx.EmitToken(); // ;
    }

    internal static void PrintDefer(DeferStmt stmt, FormatContext ctx, Action<Stmt> formatStmt)
    {
        ctx.EmitToken(); // defer
        if (stmt.HasAwait)
        {
            ctx.Space();
            ctx.EmitToken(); // await
        }
        if (stmt.Body is BlockStmt)
        {
            BraceRules.BeforeOpenBrace(ctx);
            ctx.PushScope(ScopeKind.ControlFlowBody);
            formatStmt(stmt.Body);
            ctx.PopScope();
        }
        else
        {
            ctx.Space();
            formatStmt(stmt.Body);
        }
    }

    internal static void PrintExprStmt(ExprStmt stmt, FormatContext ctx, Action<Expr> formatExpr)
    {
        formatExpr(stmt.Expression);
        ctx.EmitToken(); // ;
    }
}
