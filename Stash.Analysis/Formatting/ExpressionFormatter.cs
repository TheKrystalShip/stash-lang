using System;
using Stash.Lexing;
using Stash.Parsing.AST;

namespace Stash.Analysis.Formatting;

/// <summary>
/// Contains formatting logic for all expression AST node types.
/// Each method corresponds to an <see cref="IExprVisitor{T}"/> visitor method.
/// </summary>
internal static class ExpressionFormatter
{
    internal static void FormatLiteral(FormatterContext ctx)
    {
        ctx.EmitToken();
    }

    internal static void FormatIdentifier(FormatterContext ctx)
    {
        ctx.EmitToken();
    }

    internal static void FormatBinary(BinaryExpr expr, FormatterContext ctx, Action<Expr> formatExpr)
    {
        formatExpr(expr.Left);
        ctx.Space();
        ctx.EmitToken(); // operator
        ctx.Space();
        formatExpr(expr.Right);
    }

    internal static void FormatIs(IsExpr expr, FormatterContext ctx, Action<Expr> formatExpr)
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

    internal static void FormatUnary(UnaryExpr expr, FormatterContext ctx, Action<Expr> formatExpr)
    {
        ctx.EmitToken(); // ! or -
        formatExpr(expr.Right);
    }

    internal static void FormatGrouping(GroupingExpr expr, FormatterContext ctx, Action<Expr> formatExpr)
    {
        ctx.EmitToken(); // (
        formatExpr(expr.Expression);
        ctx.EmitToken(); // )
    }

    internal static void FormatTernary(TernaryExpr expr, FormatterContext ctx, Action<Expr> formatExpr)
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

    internal static void FormatAssign(AssignExpr expr, FormatterContext ctx, Action<Expr> formatExpr)
    {
        ctx.EmitToken(); // identifier
        ctx.Space();
        if (ctx.NextIsCompoundOperator())
        {
            ctx.EmitToken(); // compound operator (+=, -=, etc.)
            ctx.Space();
            if (expr.Value is BinaryExpr binary)
            {
                formatExpr(binary.Right);
            }
            else if (expr.Value is NullCoalesceExpr nc)
            {
                formatExpr(nc.Right);
            }
            else
            {
                throw new InvalidOperationException($"Unexpected compound assignment value type: {expr.Value.GetType().Name}");
            }
        }
        else
        {
            ctx.EmitToken(); // =
            ctx.Space();
            formatExpr(expr.Value);
        }
    }

    internal static void FormatDot(DotExpr expr, FormatterContext ctx, Action<Expr> formatExpr)
    {
        formatExpr(expr.Object);
        ctx.EmitToken(); // . or ?.
        ctx.EmitToken(); // member name
    }

    internal static void FormatDotAssign(DotAssignExpr expr, FormatterContext ctx, Action<Expr> formatExpr)
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
            {
                formatExpr(binary.Right);
            }
            else if (expr.Value is NullCoalesceExpr nc)
            {
                formatExpr(nc.Right);
            }
            else
            {
                throw new InvalidOperationException($"Unexpected compound assignment value type: {expr.Value.GetType().Name}");
            }
        }
        else
        {
            ctx.EmitToken(); // =
            ctx.Space();
            formatExpr(expr.Value);
        }
    }

    internal static void FormatCall(CallExpr expr, FormatterContext ctx, Action<Expr> formatExpr)
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

    internal static void FormatIndex(IndexExpr expr, FormatterContext ctx, Action<Expr> formatExpr)
    {
        formatExpr(expr.Object);
        ctx.EmitToken(); // [
        formatExpr(expr.Index);
        ctx.EmitToken(); // ]
    }

    internal static void FormatIndexAssign(IndexAssignExpr expr, FormatterContext ctx, Action<Expr> formatExpr)
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
            {
                formatExpr(binary.Right);
            }
            else if (expr.Value is NullCoalesceExpr nc)
            {
                formatExpr(nc.Right);
            }
            else
            {
                throw new InvalidOperationException($"Unexpected compound assignment value type: {expr.Value.GetType().Name}");
            }
        }
        else
        {
            ctx.EmitToken(); // =
            ctx.Space();
            formatExpr(expr.Value);
        }
    }

    internal static void FormatArray(ArrayExpr expr, FormatterContext ctx, Action<Expr> formatExpr)
    {
        ctx.EmitToken(); // [
        if (expr.Elements.Count == 0)
        {
            ctx.EmitToken(); // ]
            return;
        }

        int groupMark = ctx.Mark();
        ctx.Indent++;
        int indentMark = ctx.Mark();
        ctx.AddDoc(Doc.SoftLine);

        for (int i = 0; i < expr.Elements.Count; i++)
        {
            formatExpr(expr.Elements[i]);

            bool isLast = i == expr.Elements.Count - 1;
            if (!isLast)
            {
                if (ctx.NextIs(TokenType.Comma)) ctx.EmitToken();
                ctx.SoftNewLine();
            }
            else
            {
                if (ctx.NextIs(TokenType.Comma))
                {
                    ctx.LastCodeToken = ctx.CurrentToken;
                    ctx.SkipToken();
                }
                if (ctx.TrailingComma == TrailingCommaStyle.All)
                {
                    ctx.AddDoc(Doc.IfBreak(Doc.Text(","), Doc.Empty));
                }
            }
        }

        ctx.FlushTriviaBeforeCurrentToken();

        ctx.WrapFrom(indentMark, Doc.Indent);
        ctx.Indent--;

        if (ctx.HasPendingWhitespace)
        {
            ctx.ResetPending();
            ctx.AddDoc(Doc.HardLine);
        }
        else
        {
            ctx.AddDoc(Doc.SoftLine);
        }

        ctx.WrapFrom(groupMark, Doc.Group);
        ctx.EmitToken(); // ]
    }

    internal static void FormatStructInit(StructInitExpr expr, FormatterContext ctx, Action<Expr> formatExpr)
    {
        if (expr.Target != null)
        {
            formatExpr(expr.Target);
        }
        else
        {
            ctx.EmitToken(); // struct name
        }

        ctx.Space();
        ctx.EmitToken(); // {

        if (expr.FieldValues.Count == 0)
        {
            ctx.EmitToken(); // }
            return;
        }

        int groupMark = ctx.Mark();
        ctx.Indent++;
        int indentMark = ctx.Mark();
        ctx.AddDoc(ctx.BracketSpacing ? Doc.Line : Doc.SoftLine);

        for (int i = 0; i < expr.FieldValues.Count; i++)
        {
            ctx.EmitToken(); // field name
            ctx.EmitToken(); // :
            ctx.Space();
            formatExpr(expr.FieldValues[i].Value);

            bool isLast = i == expr.FieldValues.Count - 1;
            if (!isLast)
            {
                if (ctx.NextIs(TokenType.Comma)) ctx.EmitToken();
                ctx.SoftNewLine();
            }
            else
            {
                if (ctx.NextIs(TokenType.Comma))
                {
                    ctx.LastCodeToken = ctx.CurrentToken;
                    ctx.SkipToken();
                }
                if (ctx.TrailingComma == TrailingCommaStyle.All)
                {
                    ctx.AddDoc(Doc.IfBreak(Doc.Text(","), Doc.Empty));
                }
            }
        }

        ctx.FlushTriviaBeforeCurrentToken();

        ctx.WrapFrom(indentMark, Doc.Indent);
        ctx.Indent--;

        if (ctx.HasPendingWhitespace)
        {
            ctx.ResetPending();
            ctx.AddDoc(Doc.HardLine);
        }
        else
        {
            ctx.AddDoc(ctx.BracketSpacing ? Doc.Line : Doc.SoftLine);
        }

        ctx.WrapFrom(groupMark, Doc.Group);
        ctx.EmitToken(); // }
    }

    internal static void FormatSwitchExpr(SwitchExpr expr, FormatterContext ctx, Action<Expr> formatExpr)
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

    internal static void FormatInterpolatedString(FormatterContext ctx)
    {
        ctx.EmitToken();
    }

    internal static void FormatCommand(FormatterContext ctx)
    {
        ctx.EmitToken();
    }

    internal static void FormatLambda(LambdaExpr expr, FormatterContext ctx, Action<Stmt> formatStmt, Action<Expr> formatExpr)
    {
        if (expr.IsAsync)
        {
            ctx.EmitToken(); // async
            ctx.Space();
        }
        ctx.EmitToken(); // (
        for (int i = 0; i < expr.Parameters.Count; i++)
        {
            if (i > 0) { ctx.EmitToken(); ctx.Space(); } // ,
            if (expr.HasRestParam && i == expr.Parameters.Count - 1)
            {
                ctx.EmitToken(); // ...
            }
            ctx.EmitToken(); // param name
            if (expr.ParameterTypes[i] != null)
            {
                ctx.EmitToken(); // :
                ctx.Space();
                ctx.EmitToken(); // type
            }
            if (expr.DefaultValues[i] != null)
            {
                ctx.Space();
                ctx.EmitToken(); // =
                ctx.Space();
                formatExpr(expr.DefaultValues[i]!);
            }
        }
        ctx.EmitToken(); // )
        ctx.Space();
        ctx.EmitToken(); // =>
        ctx.Space();
        if (expr.BlockBody != null)
        {
            formatStmt(expr.BlockBody);
        }
        else
        {
            formatExpr(expr.ExpressionBody!);
        }
    }

    internal static void FormatUpdate(UpdateExpr expr, FormatterContext ctx, Action<Expr> formatExpr)
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

    internal static void FormatTry(TryExpr expr, FormatterContext ctx, Action<Expr> formatExpr)
    {
        ctx.EmitToken(); // try
        ctx.Space();
        formatExpr(expr.Expression);
    }

    internal static void FormatAwait(AwaitExpr expr, FormatterContext ctx, Action<Expr> formatExpr)
    {
        ctx.EmitToken(); // await
        ctx.Space();
        formatExpr(expr.Expression);
    }

    internal static void FormatTimeout(TimeoutExpr expr, FormatterContext ctx, Action<Stmt> formatStmt, Action<Expr> formatExpr)
    {
        ctx.EmitToken(); // timeout
        ctx.Space();
        formatExpr(expr.Duration);
        ctx.Space();
        StatementFormatter.FormatBlock(expr.Body, ctx, formatStmt);
    }

    internal static void FormatRetry(RetryExpr expr, FormatterContext ctx, Action<Stmt> formatStmt, Action<Expr> formatExpr)
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

    internal static void FormatNullCoalesce(NullCoalesceExpr expr, FormatterContext ctx, Action<Expr> formatExpr)
    {
        formatExpr(expr.Left);
        ctx.Space();
        ctx.EmitToken(); // ??
        ctx.Space();
        formatExpr(expr.Right);
    }

    internal static void FormatPipe(PipeExpr expr, FormatterContext ctx, Action<Expr> formatExpr)
    {
        formatExpr(expr.Left);
        ctx.Space();
        ctx.EmitToken(); // |
        ctx.Space();
        formatExpr(expr.Right);
    }

    internal static void FormatRedirect(RedirectExpr expr, FormatterContext ctx, Action<Expr> formatExpr)
    {
        formatExpr(expr.Expression);
        ctx.Space();
        ctx.EmitToken(); // redirect operator
        ctx.Space();
        formatExpr(expr.Target);
    }

    internal static void FormatRange(RangeExpr expr, FormatterContext ctx, Action<Expr> formatExpr)
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

    internal static void FormatDictLiteral(DictLiteralExpr expr, FormatterContext ctx, Action<Expr> formatExpr)
    {
        ctx.EmitToken(); // {
        if (expr.Entries.Count == 0)
        {
            ctx.EmitToken(); // }
            return;
        }

        int groupMark = ctx.Mark();
        ctx.Indent++;
        int indentMark = ctx.Mark();
        ctx.AddDoc(ctx.BracketSpacing ? Doc.Line : Doc.SoftLine);

        for (int i = 0; i < expr.Entries.Count; i++)
        {
            if (expr.Entries[i].Key != null)
            {
                ctx.EmitToken(); // key
                ctx.EmitToken(); // :
                ctx.Space();
            }
            formatExpr(expr.Entries[i].Value);

            bool isLast = i == expr.Entries.Count - 1;
            if (!isLast)
            {
                if (ctx.NextIs(TokenType.Comma)) ctx.EmitToken();
                ctx.SoftNewLine();
            }
            else
            {
                if (ctx.NextIs(TokenType.Comma))
                {
                    ctx.LastCodeToken = ctx.CurrentToken;
                    ctx.SkipToken();
                }
                if (ctx.TrailingComma == TrailingCommaStyle.All)
                {
                    ctx.AddDoc(Doc.IfBreak(Doc.Text(","), Doc.Empty));
                }
            }
        }

        ctx.FlushTriviaBeforeCurrentToken();

        ctx.WrapFrom(indentMark, Doc.Indent);
        ctx.Indent--;

        if (ctx.HasPendingWhitespace)
        {
            ctx.ResetPending();
            ctx.AddDoc(Doc.HardLine);
        }
        else
        {
            ctx.AddDoc(ctx.BracketSpacing ? Doc.Line : Doc.SoftLine);
        }

        ctx.WrapFrom(groupMark, Doc.Group);
        ctx.EmitToken(); // }
    }

    internal static void FormatSpread(SpreadExpr expr, FormatterContext ctx, Action<Expr> formatExpr)
    {
        ctx.EmitToken(); // ...
        formatExpr(expr.Expression);
    }
}
