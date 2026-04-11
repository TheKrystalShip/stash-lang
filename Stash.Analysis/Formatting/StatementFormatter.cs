using System;
using Stash.Lexing;
using Stash.Parsing.AST;

namespace Stash.Analysis.Formatting;

/// <summary>
/// Contains formatting logic for all statement AST node types.
/// Each method corresponds to a <see cref="IStmtVisitor{T}"/> visitor method.
/// </summary>
internal static class StatementFormatter
{
    internal static void FormatVarDecl(VarDeclStmt stmt, FormatterContext ctx, Action<Expr> formatExpr)
    {
        ctx.EmitToken(); // let
        ctx.Space();
        ctx.EmitToken(); // name
        if (stmt.TypeHint != null)
        {
            ctx.EmitToken(); // :
            ctx.Space();
            ctx.EmitToken(); // type
        }
        if (stmt.Initializer != null)
        {
            ctx.Space();
            ctx.EmitToken(); // =
            ctx.Space();
            formatExpr(stmt.Initializer);
        }
        ctx.EmitToken(); // ;
    }

    internal static void FormatConstDecl(ConstDeclStmt stmt, FormatterContext ctx, Action<Expr> formatExpr)
    {
        ctx.EmitToken(); // const
        ctx.Space();
        ctx.EmitToken(); // name
        if (stmt.TypeHint != null)
        {
            ctx.EmitToken(); // :
            ctx.Space();
            ctx.EmitToken(); // type
        }
        ctx.Space();
        ctx.EmitToken(); // =
        ctx.Space();
        formatExpr(stmt.Initializer);
        ctx.EmitToken(); // ;
    }

    internal static void FormatFnDecl(FnDeclStmt stmt, FormatterContext ctx, Action<Stmt> formatStmt, Action<Expr> formatExpr)
    {
        if (stmt.IsAsync)
        {
            ctx.EmitToken(); // async
            ctx.Space();
        }
        ctx.EmitToken(); // fn
        ctx.Space();
        ctx.EmitToken(); // name
        ctx.EmitToken(); // (
        for (int i = 0; i < stmt.Parameters.Count; i++)
        {
            if (i > 0) { ctx.EmitToken(); ctx.Space(); } // ,
            if (stmt.HasRestParam && i == stmt.Parameters.Count - 1)
            {
                ctx.EmitToken(); // ...
            }
            ctx.EmitToken(); // param name
            if (stmt.ParameterTypes[i] != null)
            {
                ctx.EmitToken(); // :
                ctx.Space();
                ctx.EmitToken(); // type
            }
            if (stmt.DefaultValues[i] != null)
            {
                ctx.Space();
                ctx.EmitToken(); // =
                ctx.Space();
                formatExpr(stmt.DefaultValues[i]!);
            }
        }
        ctx.EmitToken(); // )
        if (stmt.ReturnType != null)
        {
            ctx.Space();
            ctx.EmitToken(); // ->
            ctx.Space();
            ctx.EmitToken(); // return type
        }
        ctx.Space();
        formatStmt(stmt.Body);
    }

    internal static void FormatBlock(BlockStmt stmt, FormatterContext ctx, Action<Stmt> formatStmt)
    {
        ctx.EmitToken(); // {

        if (ctx.SingleLineBlocks && stmt.Statements.Count == 1 && !ctx.BlockHasComments(stmt.Span.StartLine, stmt.Span.EndLine))
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
        foreach (var s in stmt.Statements)
        {
            ctx.NewLine();
            formatStmt(s);
        }
        ctx.WrapFrom(mark, Doc.Indent);
        ctx.Indent--;
        ctx.NewLine();
        ctx.EmitToken(); // }
    }

    internal static void FormatIf(IfStmt stmt, FormatterContext ctx, Action<Stmt> formatStmt, Action<Expr> formatExpr)
    {
        ctx.EmitToken(); // if
        ctx.Space();
        ctx.EmitToken(); // (
        formatExpr(stmt.Condition);
        ctx.EmitToken(); // )
        ctx.Space();
        formatStmt(stmt.ThenBranch);
        if (stmt.ElseBranch != null)
        {
            ctx.Space();
            ctx.EmitToken(); // else
            ctx.Space();
            formatStmt(stmt.ElseBranch);
        }
    }

    internal static void FormatWhile(WhileStmt stmt, FormatterContext ctx, Action<Stmt> formatStmt, Action<Expr> formatExpr)
    {
        ctx.EmitToken(); // while
        ctx.Space();
        ctx.EmitToken(); // (
        formatExpr(stmt.Condition);
        ctx.EmitToken(); // )
        ctx.Space();
        formatStmt(stmt.Body);
    }

    internal static void FormatElevate(ElevateStmt stmt, FormatterContext ctx, Action<Stmt> formatStmt, Action<Expr> formatExpr)
    {
        ctx.EmitToken(); // elevate
        if (stmt.Elevator != null)
        {
            ctx.EmitToken(); // (
            formatExpr(stmt.Elevator);
            ctx.EmitToken(); // )
        }
        ctx.Space();
        formatStmt(stmt.Body);
    }

    internal static void FormatDoWhile(DoWhileStmt stmt, FormatterContext ctx, Action<Stmt> formatStmt, Action<Expr> formatExpr)
    {
        ctx.EmitToken(); // do
        ctx.Space();
        formatStmt(stmt.Body);
        ctx.Space();
        ctx.EmitToken(); // while
        ctx.Space();
        ctx.EmitToken(); // (
        formatExpr(stmt.Condition);
        ctx.EmitToken(); // )
        ctx.EmitToken(); // ;
    }

    internal static void FormatFor(ForStmt stmt, FormatterContext ctx, Action<Stmt> formatStmt, Action<Expr> formatExpr)
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
        ctx.Space();
        formatStmt(stmt.Body);
    }

    internal static void FormatForIn(ForInStmt stmt, FormatterContext ctx, Action<Stmt> formatStmt, Action<Expr> formatExpr)
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
        ctx.Space();
        formatStmt(stmt.Body);
    }

    internal static void FormatReturn(ReturnStmt stmt, FormatterContext ctx, Action<Expr> formatExpr)
    {
        ctx.EmitToken(); // return
        if (stmt.Value != null)
        {
            ctx.Space();
            formatExpr(stmt.Value);
        }
        ctx.EmitToken(); // ;
    }

    internal static void FormatThrow(ThrowStmt stmt, FormatterContext ctx, Action<Expr> formatExpr)
    {
        ctx.EmitToken(); // throw
        ctx.Space();
        formatExpr(stmt.Value);
        ctx.EmitToken(); // ;
    }

    internal static void FormatTryCatch(TryCatchStmt stmt, FormatterContext ctx, Action<Stmt> formatStmt)
    {
        ctx.EmitToken(); // try
        ctx.Space();
        formatStmt(stmt.TryBody);
        if (stmt.CatchBody is not null)
        {
            ctx.Space();
            ctx.EmitToken(); // catch
            ctx.Space();
            ctx.EmitToken(); // (
            ctx.EmitToken(); // variable
            ctx.EmitToken(); // )
            ctx.Space();
            formatStmt(stmt.CatchBody);
        }
        if (stmt.FinallyBody is not null)
        {
            ctx.Space();
            ctx.EmitToken(); // finally
            ctx.Space();
            formatStmt(stmt.FinallyBody);
        }
    }

    internal static void FormatSwitch(SwitchStmt stmt, FormatterContext ctx, Action<Stmt> formatStmt, Action<Expr> formatExpr)
    {
        ctx.EmitToken(); // switch
        ctx.Space();
        ctx.EmitToken(); // (
        formatExpr(stmt.Subject);
        ctx.EmitToken(); // )
        ctx.Space();
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
            ctx.Space();
            formatStmt(@case.Body);
        }
        ctx.WrapFrom(mark, Doc.Indent);
        ctx.Indent--;
        ctx.NewLine();
        ctx.EmitToken(); // }
    }

    internal static void FormatBreak(FormatterContext ctx)
    {
        ctx.EmitToken(); // break
        ctx.EmitToken(); // ;
    }

    internal static void FormatContinue(FormatterContext ctx)
    {
        ctx.EmitToken(); // continue
        ctx.EmitToken(); // ;
    }

    internal static void FormatExprStmt(ExprStmt stmt, FormatterContext ctx, Action<Expr> formatExpr)
    {
        formatExpr(stmt.Expression);
        ctx.EmitToken(); // ;
    }

    internal static void FormatExtend(ExtendStmt stmt, FormatterContext ctx, Action<Stmt> formatStmt)
    {
        ctx.EmitToken(); // extend
        ctx.Space();
        ctx.EmitToken(); // type name
        ctx.Space();
        ctx.EmitToken(); // {
        ctx.Indent++;
        int mark = ctx.Mark();
        for (int i = 0; i < stmt.Methods.Count; i++)
        {
            if (i > 0)
            {
                ctx.BlankLine();
            }
            else
            {
                ctx.NewLine();
            }
            formatStmt(stmt.Methods[i]);
        }
        ctx.WrapFrom(mark, Doc.Indent);
        ctx.Indent--;
        ctx.NewLine();
        ctx.EmitToken(); // }
    }

    internal static void FormatStructDecl(StructDeclStmt stmt, FormatterContext ctx, Action<Stmt> formatStmt)
    {
        ctx.EmitToken(); // struct
        ctx.Space();
        ctx.EmitToken(); // name
        if (stmt.Interfaces.Count > 0)
        {
            ctx.Space();
            ctx.EmitToken(); // :
            ctx.Space();
            for (int i = 0; i < stmt.Interfaces.Count; i++)
            {
                if (i > 0)
                {
                    ctx.EmitToken(); // ,
                    ctx.Space();
                }
                ctx.EmitToken(); // interface name
            }
        }
        ctx.Space();
        ctx.EmitToken(); // {
        ctx.Indent++;
        int mark = ctx.Mark();
        for (int i = 0; i < stmt.Fields.Count; i++)
        {
            ctx.NewLine();
            ctx.EmitToken(); // field name
            if (stmt.FieldTypes[i] != null)
            {
                ctx.EmitToken(); // :
                ctx.Space();
                ctx.EmitToken(); // type
            }
            if (ctx.NextIs(TokenType.Comma))
            {
                ctx.EmitToken(); // ,
            }
        }
        for (int i = 0; i < stmt.Methods.Count; i++)
        {
            if (i > 0 || stmt.Fields.Count > 0)
            {
                ctx.BlankLine();
            }
            else
            {
                ctx.NewLine();
            }
            formatStmt(stmt.Methods[i]);
        }
        ctx.WrapFrom(mark, Doc.Indent);
        ctx.Indent--;
        ctx.NewLine();
        ctx.EmitToken(); // }
    }

    internal static void FormatEnumDecl(EnumDeclStmt stmt, FormatterContext ctx)
    {
        ctx.EmitToken(); // enum
        ctx.Space();
        ctx.EmitToken(); // name
        ctx.Space();
        ctx.EmitToken(); // {
        ctx.Indent++;
        int mark = ctx.Mark();
        for (int i = 0; i < stmt.Members.Count; i++)
        {
            ctx.NewLine();
            ctx.EmitToken(); // member name
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

    internal static void FormatInterfaceDecl(InterfaceDeclStmt stmt, FormatterContext ctx)
    {
        ctx.EmitToken(); // interface
        ctx.Space();
        ctx.EmitToken(); // name
        ctx.Space();
        ctx.EmitToken(); // {
        ctx.Indent++;
        int mark = ctx.Mark();

        int totalMembers = stmt.Fields.Count + stmt.Methods.Count;
        int methodIndex = 0;
        for (int i = 0; i < totalMembers; i++)
        {
            ctx.NewLine();
            ctx.EmitToken(); // member name

            if (ctx.NextIs(TokenType.LeftParen))
            {
                var method = stmt.Methods[methodIndex++];
                ctx.EmitToken(); // (
                for (int p = 0; p < method.Parameters.Count; p++)
                {
                    if (p > 0)
                    {
                        ctx.EmitToken(); // ,
                        ctx.Space();
                    }
                    ctx.EmitToken(); // param name
                    if (ctx.NextIs(TokenType.Colon))
                    {
                        ctx.EmitToken(); // :
                        ctx.Space();
                        ctx.EmitToken(); // type
                    }
                }
                ctx.EmitToken(); // )

                if (ctx.NextIs(TokenType.Arrow))
                {
                    ctx.Space();
                    ctx.EmitToken(); // ->
                    ctx.Space();
                    ctx.EmitToken(); // return type
                }
            }
            else if (ctx.NextIs(TokenType.Colon))
            {
                ctx.EmitToken(); // :
                ctx.Space();
                ctx.EmitToken(); // type
            }

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

    internal static void FormatImportAs(ImportAsStmt stmt, FormatterContext ctx, Action<Expr> formatExpr)
    {
        ctx.EmitToken(); // import
        ctx.Space();
        formatExpr(stmt.Path);
        ctx.Space();
        ctx.EmitToken(); // as
        ctx.Space();
        ctx.EmitToken(); // alias
        ctx.EmitToken(); // ;
    }

    internal static void FormatImport(ImportStmt stmt, FormatterContext ctx, Action<Expr> formatExpr)
    {
        ctx.EmitToken(); // import
        ctx.Space();
        ctx.EmitToken(); // {
        ctx.Space();
        for (int i = 0; i < stmt.Names.Count; i++)
        {
            if (i > 0) { ctx.EmitToken(); ctx.Space(); } // ,
            ctx.EmitToken(); // name
        }
        ctx.Space();
        ctx.EmitToken(); // }
        ctx.Space();
        ctx.EmitToken(); // from
        ctx.Space();
        formatExpr(stmt.Path);
        ctx.EmitToken(); // ;
    }

    internal static void FormatDestructure(DestructureStmt stmt, FormatterContext ctx, Action<Expr> formatExpr)
    {
        ctx.EmitToken(); // let or const
        ctx.Space();
        ctx.EmitToken(); // [ or {
        for (int i = 0; i < stmt.Names.Count; i++)
        {
            if (i > 0) { ctx.EmitToken(); ctx.Space(); } // ,
            ctx.EmitToken(); // name
        }
        if (stmt.RestName != null)
        {
            if (stmt.Names.Count > 0) { ctx.EmitToken(); ctx.Space(); } // ,
            ctx.EmitToken(); // ...
            ctx.EmitToken(); // rest name
        }
        ctx.EmitToken(); // ] or }
        ctx.Space();
        ctx.EmitToken(); // =
        ctx.Space();
        formatExpr(stmt.Initializer);
        ctx.EmitToken(); // ;
    }
}
