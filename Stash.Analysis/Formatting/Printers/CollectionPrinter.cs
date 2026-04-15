using System;
using Stash.Analysis.Formatting.Rules;
using Stash.Lexing;
using Stash.Parsing.AST;

namespace Stash.Analysis.Formatting.Printers;

internal static class CollectionPrinter
{
    internal static void PrintArray(ArrayExpr expr, FormatContext ctx, Action<Expr> formatExpr)
    {
        ctx.EmitToken(); // [
        CollectionRules.FormatCollection(ctx, expr.Elements.Count, i =>
        {
            formatExpr(expr.Elements[i]);
        }, new CollectionStyle(BracketSpacing: false));
    }

    internal static void PrintDictLiteral(DictLiteralExpr expr, FormatContext ctx, Action<Expr> formatExpr)
    {
        ctx.EmitToken(); // {
        CollectionRules.FormatCollection(ctx, expr.Entries.Count, i =>
        {
            if (expr.Entries[i].Key != null)
            {
                ctx.EmitToken(); // key
                ctx.EmitToken(); // :
                ctx.Space();
            }
            formatExpr(expr.Entries[i].Value);
        }, new CollectionStyle(BracketSpacing: ctx.BracketSpacing));
    }

    internal static void PrintStructInit(StructInitExpr expr, FormatContext ctx, Action<Expr> formatExpr)
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

        CollectionRules.FormatCollection(ctx, expr.FieldValues.Count, i =>
        {
            ctx.EmitToken(); // field name
            ctx.EmitToken(); // :
            ctx.Space();
            formatExpr(expr.FieldValues[i].Value);
        }, new CollectionStyle(BracketSpacing: ctx.BracketSpacing));
    }
}
