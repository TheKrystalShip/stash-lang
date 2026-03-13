namespace Stash.Parsing.AST;

using System.Collections.Generic;
using Stash.Common;
using Stash.Lexing;

/// <summary>
/// A function declaration: <c>fn name(params) { body }</c>
/// </summary>
public class FnDeclStmt : Stmt
{
    public Token Name { get; }
    public List<Token> Parameters { get; }
    public List<Token?> ParameterTypes { get; }
    public Token? ReturnType { get; }
    public BlockStmt Body { get; }

    public FnDeclStmt(Token name, List<Token> parameters, List<Token?> parameterTypes, Token? returnType, BlockStmt body, SourceSpan span) : base(span)
    {
        Name = name;
        Parameters = parameters;
        ParameterTypes = parameterTypes;
        ReturnType = returnType;
        Body = body;
    }

    public override T Accept<T>(IStmtVisitor<T> visitor) => visitor.VisitFnDeclStmt(this);
}
