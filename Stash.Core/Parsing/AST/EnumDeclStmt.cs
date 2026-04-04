namespace Stash.Parsing.AST;

using System.Collections.Generic;
using Stash.Common;
using Stash.Lexing;

/// <summary>
/// An enum declaration: <c>enum Name { Member1, Member2, ... }</c>
/// </summary>
/// <remarks>
/// Enums define a set of named constants accessed via dot notation (<c>Color.Red</c>). Each member is an
/// auto-incremented integer starting from 0. Enum values are compared by identity at runtime.
/// </remarks>
public class EnumDeclStmt : Stmt
{
    /// <summary>Gets the enum name token.</summary>
    public Token Name { get; }
    /// <summary>Gets the list of member name tokens.</summary>
    public List<Token> Members { get; }

    /// <summary>Initializes a new instance of <see cref="EnumDeclStmt"/>.</summary>
    /// <param name="name">The enum name token.</param>
    /// <param name="members">The list of member name tokens.</param>
    /// <param name="span">The source location of this declaration.</param>
    public EnumDeclStmt(Token name, List<Token> members, SourceSpan span) : base(span, StmtType.EnumDecl)
    {
        Name = name;
        Members = members;
    }

    /// <inheritdoc />
    public override T Accept<T>(IStmtVisitor<T> visitor) => visitor.VisitEnumDeclStmt(this);
}
