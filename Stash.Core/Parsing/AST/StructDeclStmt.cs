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
    public List<Token?> FieldTypes { get; }
    public List<FnDeclStmt> Methods { get; }

    public StructDeclStmt(Token name, List<Token> fields, List<Token?> fieldTypes, List<FnDeclStmt> methods, SourceSpan span) : base(span)
    {
        Name = name;
        Fields = fields;
        FieldTypes = fieldTypes;
        Methods = methods;
    }

    public override T Accept<T>(IStmtVisitor<T> visitor) => visitor.VisitStructDeclStmt(this);
}
