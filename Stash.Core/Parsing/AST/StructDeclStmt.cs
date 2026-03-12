namespace Stash.Parsing.AST;

using System.Collections.Generic;
using Stash.Common;
using Stash.Lexing;

/// <summary>
/// A struct declaration: <c>struct Name { field1, field2, ... }</c>
/// </summary>
public class StructDeclStmt : Stmt
{
    public Token Name { get; }
    public List<Token> Fields { get; }

    public StructDeclStmt(Token name, List<Token> fields, SourceSpan span) : base(span)
    {
        Name = name;
        Fields = fields;
    }

    public override T Accept<T>(IStmtVisitor<T> visitor) => visitor.VisitStructDeclStmt(this);
}
