using System;
using Stash.Analysis.Formatting.Rules;
using Stash.Parsing.AST;

namespace Stash.Analysis.Formatting.Printers;

internal static class BlockPrinter
{
    internal static void Print(BlockStmt stmt, FormatContext ctx, Action<Stmt> formatStmt)
    {
        ctx.EmitToken(); // {

        if (BraceRules.AllowSingleLine(stmt, ctx.CurrentScope, ctx.Config)
            && !ctx.BlockHasComments(stmt.Span.StartLine, stmt.Span.EndLine))
        {
            int outerMark = ctx.Mark();
            int innerMark = ctx.Mark();
            ctx.AddDoc(Doc.Line);
            formatStmt(stmt.Statements[0]);
            ctx.WrapFrom(innerMark, Doc.Indent);
            ctx.AddDoc(Doc.Line);
            ctx.WrapFrom(outerMark, Doc.Group);
            ctx.EmitToken(); // }
            return;
        }

        ctx.Indent++;
        int mark = ctx.Mark();
        for (int i = 0; i < stmt.Statements.Count; i++)
        {
            if (i == 0)
            {
                ctx.NewLine();
            }
            else
            {
                int blanks = ctx.BlankLinesBetween(stmt.Statements[i - 1], stmt.Statements[i]);
                if (blanks > 0) ctx.BlankLine(); else ctx.NewLine();
            }
            formatStmt(stmt.Statements[i]);
        }
        ctx.WrapFrom(mark, Doc.Indent);
        ctx.Indent--;
        ctx.NewLine();
        ctx.EmitToken(); // }
    }
}
