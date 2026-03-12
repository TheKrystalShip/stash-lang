namespace Stash.Parsing.AST;

using System.Collections.Generic;
using Stash.Common;
using Stash.Lexing;

/// <summary>
/// An enum declaration: <c>enum Name { Member1, Member2, ... }</c>
/// </summary>
public class EnumDeclStmt : Stmt
{
    public Token Name { get; }
    public List<Token> Members { get; }

    public EnumDeclStmt(Token name, List<Token> members, SourceSpan span) : base(span)
    {
        Name = name;
        Members = members;
    }

    public override T Accept<T>(IStmtVisitor<T> visitor) => visitor.VisitEnumDeclStmt(this);
}
