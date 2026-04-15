using System;

namespace Stash.Analysis.Formatting.Rules;

internal static class CollectionRules
{
    internal static void FormatCollection(
        FormatContext ctx,
        int elementCount,
        Action<int> formatElement,
        CollectionStyle style)
    {
        if (elementCount == 0)
        {
            ctx.EmitToken(); // closing bracket
            return;
        }

        int groupMark = ctx.Mark();
        ctx.Indent++;
        int indentMark = ctx.Mark();
        ctx.AddDoc(style.BracketSpacing ? Doc.Line : Doc.SoftLine);

        for (int i = 0; i < elementCount; i++)
        {
            formatElement(i);

            bool isLast = i == elementCount - 1;
            if (!isLast)
            {
                if (ctx.NextIs(Stash.Lexing.TokenType.Comma)) ctx.EmitToken();
                ctx.SoftNewLine();
            }
            else
            {
                if (ctx.NextIs(Stash.Lexing.TokenType.Comma))
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
            ctx.AddDoc(style.BracketSpacing ? Doc.Line : Doc.SoftLine);
        }

        ctx.WrapFrom(groupMark, Doc.Group);
        ctx.EmitToken(); // closing bracket
    }
}

internal record CollectionStyle(
    bool BracketSpacing
);
