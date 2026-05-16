using System;
using Stash.Analysis.Formatting.Printers;
using Stash.Parsing.AST;

namespace Stash.Analysis.Formatting.Rules;

internal static class ParameterRules
{
    internal static void FormatParameterList(
        FormatContext ctx,
        int paramCount,
        Func<int, TypeExpression?> getType,
        Func<int, Expr?> getDefault,
        bool hasRestParam,
        Action<Expr> formatExpr)
    {
        ctx.EmitToken(); // (
        for (int i = 0; i < paramCount; i++)
        {
            if (i > 0) { ctx.EmitToken(); ctx.Space(); } // ,
            if (hasRestParam && i == paramCount - 1)
            {
                ctx.EmitToken(); // ...
            }
            ctx.EmitToken(); // param name
            if (getType(i) is { } paramType)
            {
                ctx.EmitToken(); // :
                ctx.Space();
                ControlFlowPrinter.EmitTypeExpressionTokens(ctx, paramType);
            }
            var defaultVal = getDefault(i);
            if (defaultVal != null)
            {
                ctx.Space();
                ctx.EmitToken(); // =
                ctx.Space();
                formatExpr(defaultVal);
            }
        }
        ctx.EmitToken(); // )
    }
}
