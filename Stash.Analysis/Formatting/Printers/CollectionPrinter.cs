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
            var entry = expr.Entries[i];
            switch (entry.Kind)
            {
                case DictKeyKind.Constant:
                    ctx.EmitToken(); // identifier or string-literal key token
                    ctx.EmitToken(); // :
                    ctx.Space();
                    break;
                case DictKeyKind.Computed:
                    ctx.EmitToken(); // [
                    formatExpr(entry.KeyExpr!);
                    ctx.EmitToken(); // ]
                    ctx.EmitToken(); // :
                    ctx.Space();
                    break;
                case DictKeyKind.Spread:
                    // spread: no key tokens; the SpreadExpr formatter handles the ... and value
                    break;
            }
            formatExpr(entry.Value);
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
