using System;
using Stash.Analysis.Formatting.Rules;
using Stash.Lexing;
using Stash.Parsing.AST;

namespace Stash.Analysis.Formatting.Printers;

internal static class ExpressionPrinter
{
    internal static void PrintLiteral(FormatContext ctx) => ctx.EmitToken();

    internal static void PrintIdentifier(FormatContext ctx) => ctx.EmitToken();

    internal static void PrintBinary(BinaryExpr expr, FormatContext ctx, Action<Expr> formatExpr)
    {
        formatExpr(expr.Left);
        ctx.Space();
        ctx.EmitToken(); // operator
        ctx.Space();
        formatExpr(expr.Right);
    }

    internal static void PrintIs(IsExpr expr, FormatContext ctx, Action<Expr> formatExpr)
    {
        formatExpr(expr.Left);
        ctx.Space();
        ctx.EmitToken(); // is
        ctx.Space();
        if (expr.TypeName != null)
        {
            ctx.EmitToken(); // type name
        }
        else
        {
            formatExpr(expr.TypeExpr!);
        }
    }

    internal static void PrintUnary(UnaryExpr expr, FormatContext ctx, Action<Expr> formatExpr)
    {
        ctx.EmitToken(); // ! or -
        formatExpr(expr.Right);
    }

    internal static void PrintGrouping(GroupingExpr expr, FormatContext ctx, Action<Expr> formatExpr)
    {
        ctx.EmitToken(); // (
        formatExpr(expr.Expression);
        ctx.EmitToken(); // )
    }

    internal static void PrintTernary(TernaryExpr expr, FormatContext ctx, Action<Expr> formatExpr)
    {
        formatExpr(expr.Condition);
        ctx.Space();
        ctx.EmitToken(); // ?
        ctx.Space();
        formatExpr(expr.ThenBranch);
        ctx.Space();
        ctx.EmitToken(); // :
        ctx.Space();
        formatExpr(expr.ElseBranch);
    }

    internal static void PrintAssign(AssignExpr expr, FormatContext ctx, Action<Expr> formatExpr)
    {
        ctx.EmitToken(); // identifier
        ctx.Space();
        if (ctx.NextIsCompoundOperator())
        {
            ctx.EmitToken(); // compound operator
            ctx.Space();
            if (expr.Value is BinaryExpr binary)
                formatExpr(binary.Right);
            else if (expr.Value is NullCoalesceExpr nc)
                formatExpr(nc.Right);
            else
                throw new InvalidOperationException($"Unexpected compound assignment value type: {expr.Value.GetType().Name}");
        }
        else
        {
            ctx.EmitToken(); // =
            ctx.Space();
            formatExpr(expr.Value);
        }
    }

    internal static void PrintDot(DotExpr expr, FormatContext ctx, Action<Expr> formatExpr)
    {
        formatExpr(expr.Object);
        ctx.EmitToken(); // . or ?.
        ctx.EmitToken(); // member name
    }

    internal static void PrintDotAssign(DotAssignExpr expr, FormatContext ctx, Action<Expr> formatExpr)
    {
        formatExpr(expr.Object);
        ctx.EmitToken(); // .
        ctx.EmitToken(); // field name
        ctx.Space();
        if (ctx.NextIsCompoundOperator())
        {
            ctx.EmitToken(); // compound operator
            ctx.Space();
            if (expr.Value is BinaryExpr binary)
                formatExpr(binary.Right);
            else if (expr.Value is NullCoalesceExpr nc)
                formatExpr(nc.Right);
            else
                throw new InvalidOperationException($"Unexpected compound assignment value type: {expr.Value.GetType().Name}");
        }
        else
        {
            ctx.EmitToken(); // =
            ctx.Space();
            formatExpr(expr.Value);
        }
    }

    internal static void PrintCall(CallExpr expr, FormatContext ctx, Action<Expr> formatExpr)
    {
        formatExpr(expr.Callee);
        ctx.EmitToken(); // (
        for (int i = 0; i < expr.Arguments.Count; i++)
        {
            if (i > 0) { ctx.EmitToken(); ctx.Space(); } // ,
            formatExpr(expr.Arguments[i]);
        }
        ctx.EmitToken(); // )
    }

    internal static void PrintIndex(IndexExpr expr, FormatContext ctx, Action<Expr> formatExpr)
    {
        formatExpr(expr.Object);
        ctx.EmitToken(); // [
        formatExpr(expr.Index);
        ctx.EmitToken(); // ]
    }

    internal static void PrintIndexAssign(IndexAssignExpr expr, FormatContext ctx, Action<Expr> formatExpr)
    {
        formatExpr(expr.Object);
        ctx.EmitToken(); // [
        formatExpr(expr.Index);
        ctx.EmitToken(); // ]
        ctx.Space();
        if (ctx.NextIsCompoundOperator())
        {
            ctx.EmitToken(); // compound operator
            ctx.Space();
            if (expr.Value is BinaryExpr binary)
                formatExpr(binary.Right);
            else if (expr.Value is NullCoalesceExpr nc)
                formatExpr(nc.Right);
            else
                throw new InvalidOperationException($"Unexpected compound assignment value type: {expr.Value.GetType().Name}");
        }
        else
        {
            ctx.EmitToken(); // =
            ctx.Space();
            formatExpr(expr.Value);
        }
    }

    internal static void PrintSwitchExpr(SwitchExpr expr, FormatContext ctx, Action<Expr> formatExpr)
    {
        formatExpr(expr.Subject);
        ctx.Space();
        ctx.EmitToken(); // switch
        ctx.Space();
        ctx.EmitToken(); // {
        ctx.Indent++;
        int mark = ctx.Mark();
        for (int i = 0; i < expr.Arms.Count; i++)
        {
            ctx.NewLine();
            var arm = expr.Arms[i];
            if (arm.IsDiscard)
            {
                ctx.EmitToken(); // _
            }
            else
            {
                formatExpr(arm.Pattern!);
            }

            ctx.Space();
            ctx.EmitToken(); // =>
            ctx.Space();
            formatExpr(arm.Body);
            if (ctx.NextIs(TokenType.Comma))
            {
                ctx.EmitToken(); // ,
            }
        }
        ctx.WrapFrom(mark, Doc.Indent);
        ctx.Indent--;
        ctx.NewLine();
        ctx.EmitToken(); // }
    }

    internal static void PrintInterpolatedString(FormatContext ctx) => ctx.EmitToken();

    internal static void PrintCommand(FormatContext ctx) => ctx.EmitToken();

    internal static void PrintLambda(LambdaExpr expr, FormatContext ctx, Action<Stmt> formatStmt, Action<Expr> formatExpr)
    {
        if (expr.IsAsync)
        {
            ctx.EmitToken(); // async
            ctx.Space();
        }
        ParameterRules.FormatParameterList(
            ctx,
            expr.Parameters.Count,
            i => expr.ParameterTypes[i] != null,
            i => expr.DefaultValues[i],
            expr.HasRestParam,
            formatExpr);
        ctx.Space();
        ctx.EmitToken(); // =>
        ctx.Space();
        if (expr.BlockBody != null)
        {
            ctx.PushScope(ScopeKind.LambdaBody);
            formatStmt(expr.BlockBody);
            ctx.PopScope();
        }
        else
        {
            formatExpr(expr.ExpressionBody!);
        }
    }

    internal static void PrintUpdate(UpdateExpr expr, FormatContext ctx, Action<Expr> formatExpr)
    {
        if (expr.IsPrefix)
        {
            ctx.EmitToken(); // ++ or --
            formatExpr(expr.Operand);
        }
        else
        {
            formatExpr(expr.Operand);
            ctx.EmitToken(); // ++ or --
        }
    }

    internal static void PrintTry(TryExpr expr, FormatContext ctx, Action<Expr> formatExpr)
    {
        ctx.EmitToken(); // try
        ctx.Space();
        formatExpr(expr.Expression);
    }

    internal static void PrintAwait(AwaitExpr expr, FormatContext ctx, Action<Expr> formatExpr)
    {
        ctx.EmitToken(); // await
        ctx.Space();
        formatExpr(expr.Expression);
    }

    internal static void PrintTimeout(TimeoutExpr expr, FormatContext ctx, Action<Stmt> formatStmt, Action<Expr> formatExpr)
    {
        ctx.EmitToken(); // timeout
        ctx.Space();
        formatExpr(expr.Duration);
        BraceRules.BeforeOpenBrace(ctx);
        BlockPrinter.Print(expr.Body, ctx, formatStmt);
    }

    internal static void PrintRetry(RetryExpr expr, FormatContext ctx, Action<Stmt> formatStmt, Action<Expr> formatExpr)
    {
        ctx.EmitToken(); // retry
        ctx.Space();
        ctx.EmitToken(); // (
        formatExpr(expr.MaxAttempts);

        if (expr.NamedOptions is not null)
        {
            foreach (var (_, value) in expr.NamedOptions)
            {
                ctx.EmitToken(); // ,
                ctx.Space();
                ctx.EmitToken(); // option name identifier
                ctx.EmitToken(); // :
                ctx.Space();
                formatExpr(value);
            }
        }
        else if (expr.OptionsExpr is not null)
        {
            ctx.EmitToken(); // ,
            ctx.Space();
            formatExpr(expr.OptionsExpr);
        }

        ctx.EmitToken(); // )

        if (expr.OnRetryClause is not null)
        {
            ctx.Space();
            ctx.EmitToken(); // onRetry
            if (expr.OnRetryClause.IsReference)
            {
                ctx.Space();
                formatExpr(expr.OnRetryClause.Reference!);
            }
            else
            {
                ctx.Space();
                ctx.EmitToken(); // (
                if (expr.OnRetryClause.ParamAttempt is not null)
                {
                    ctx.EmitToken(); // attempt param identifier
                    if (expr.OnRetryClause.ParamAttemptTypeHint is not null)
                    {
                        ctx.EmitToken(); // :
                        ctx.Space();
                        ctx.EmitToken(); // type hint identifier
                    }
                }
                if (expr.OnRetryClause.ParamError is not null)
                {
                    ctx.EmitToken(); // ,
                    ctx.Space();
                    ctx.EmitToken(); // error param identifier
                    if (expr.OnRetryClause.ParamErrorTypeHint is not null)
                    {
                        ctx.EmitToken(); // :
                        ctx.Space();
                        ctx.EmitToken(); // type hint identifier
                    }
                }
                ctx.EmitToken(); // )
                ctx.Space();
                formatStmt(expr.OnRetryClause.Body!);
            }
        }

        if (expr.UntilClause is not null)
        {
            ctx.Space();
            ctx.EmitToken(); // until
            ctx.Space();
            formatExpr(expr.UntilClause);
        }

        ctx.Space();
        formatStmt(expr.Body);
    }

    internal static void PrintNullCoalesce(NullCoalesceExpr expr, FormatContext ctx, Action<Expr> formatExpr)
    {
        formatExpr(expr.Left);
        ctx.Space();
        ctx.EmitToken(); // ??
        ctx.Space();
        formatExpr(expr.Right);
    }

    internal static void PrintPipe(PipeExpr expr, FormatContext ctx, Action<Expr> formatExpr)
    {
        formatExpr(expr.Left);
        ctx.Space();
        ctx.EmitToken(); // |
        ctx.Space();
        formatExpr(expr.Right);
    }

    internal static void PrintRedirect(RedirectExpr expr, FormatContext ctx, Action<Expr> formatExpr)
    {
        formatExpr(expr.Expression);
        ctx.Space();
        ctx.EmitToken(); // redirect operator
        ctx.Space();
        formatExpr(expr.Target);
    }

    internal static void PrintRange(RangeExpr expr, FormatContext ctx, Action<Expr> formatExpr)
    {
        formatExpr(expr.Start);
        ctx.EmitToken(); // ..
        formatExpr(expr.End);
        if (expr.Step != null)
        {
            ctx.EmitToken(); // ..
            formatExpr(expr.Step);
        }
    }

    internal static void PrintSpread(SpreadExpr expr, FormatContext ctx, Action<Expr> formatExpr)
    {
        ctx.EmitToken(); // ...
        formatExpr(expr.Expression);
    }
}
