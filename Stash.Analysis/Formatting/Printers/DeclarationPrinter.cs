using System;
using Stash.Analysis.Formatting.Rules;
using Stash.Lexing;
using Stash.Parsing.AST;

namespace Stash.Analysis.Formatting.Printers;

internal static class DeclarationPrinter
{
    internal static void PrintVarDecl(VarDeclStmt stmt, FormatContext ctx, Action<Expr> formatExpr)
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

    internal static void PrintConstDecl(ConstDeclStmt stmt, FormatContext ctx, Action<Expr> formatExpr)
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

    internal static void PrintFnDecl(FnDeclStmt stmt, FormatContext ctx, Action<Stmt> formatStmt, Action<Expr> formatExpr)
    {
        if (stmt.IsAsync)
        {
            ctx.EmitToken(); // async
            ctx.Space();
        }
        ctx.EmitToken(); // fn
        ctx.Space();
        ctx.EmitToken(); // name
        ParameterRules.FormatParameterList(
            ctx,
            stmt.Parameters.Count,
            i => stmt.ParameterTypes[i] != null,
            i => stmt.DefaultValues[i],
            stmt.HasRestParam,
            formatExpr);
        if (stmt.ReturnType != null)
        {
            ctx.Space();
            ctx.EmitToken(); // ->
            ctx.Space();
            ctx.EmitToken(); // return type
        }
        BraceRules.BeforeOpenBrace(ctx);
        ctx.PushScope(ScopeKind.FunctionBody);
        BlockPrinter.Print(stmt.Body, ctx, formatStmt);
        ctx.PopScope();
    }

    internal static void PrintStructDecl(StructDeclStmt stmt, FormatContext ctx, Action<Stmt> formatStmt)
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
        BraceRules.BeforeOpenBrace(ctx);
        ctx.EmitToken(); // {
        ctx.PushScope(ScopeKind.StructBody);
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
        ctx.PopScope();
    }

    internal static void PrintEnumDecl(EnumDeclStmt stmt, FormatContext ctx)
    {
        ctx.EmitToken(); // enum
        ctx.Space();
        ctx.EmitToken(); // name
        BraceRules.BeforeOpenBrace(ctx);
        ctx.EmitToken(); // {
        ctx.PushScope(ScopeKind.EnumBody);
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
        ctx.PopScope();
    }

    internal static void PrintInterfaceDecl(InterfaceDeclStmt stmt, FormatContext ctx)
    {
        ctx.EmitToken(); // interface
        ctx.Space();
        ctx.EmitToken(); // name
        BraceRules.BeforeOpenBrace(ctx);
        ctx.EmitToken(); // {
        ctx.PushScope(ScopeKind.InterfaceBody);
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
        ctx.PopScope();
    }

    internal static void PrintExtend(ExtendStmt stmt, FormatContext ctx, Action<Stmt> formatStmt)
    {
        ctx.EmitToken(); // extend
        ctx.Space();
        ctx.EmitToken(); // type name
        BraceRules.BeforeOpenBrace(ctx);
        ctx.EmitToken(); // {
        ctx.PushScope(ScopeKind.ExtendBody);
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
        ctx.PopScope();
    }

    internal static void PrintImportAs(ImportAsStmt stmt, FormatContext ctx, Action<Expr> formatExpr)
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

    internal static void PrintImport(ImportStmt stmt, FormatContext ctx, Action<Expr> formatExpr)
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

    internal static void PrintDestructure(DestructureStmt stmt, FormatContext ctx, Action<Expr> formatExpr)
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
