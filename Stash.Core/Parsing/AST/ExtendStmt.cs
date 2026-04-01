namespace Stash.Parsing.AST;

using System.Collections.Generic;
using Stash.Common;
using Stash.Lexing;

/// <summary>
/// A type extension block: <c>extend TypeName { fn method() { ... } }</c>
/// </summary>
/// <remarks>
/// Extension blocks add methods to existing types — both built-in types (<c>string</c>, <c>array</c>,
/// <c>dict</c>, <c>int</c>, <c>float</c>) and user-defined structs. Extension methods receive an
/// implicit <c>self</c> parameter bound to the receiver value at call time. Only method declarations
/// (<c>fn</c> and <c>async fn</c>) are permitted inside the body — no fields, constants, or nested types.
/// </remarks>
public class ExtendStmt : Stmt
{
    /// <summary>Gets the <c>extend</c> keyword token.</summary>
    public Token ExtendKeyword { get; }

    /// <summary>Gets the token identifying the type being extended.</summary>
    public Token TypeName { get; }

    /// <summary>Gets the list of method declarations defined inside the extend body.</summary>
    public List<FnDeclStmt> Methods { get; }

    /// <summary>Initializes a new instance of <see cref="ExtendStmt"/>.</summary>
    /// <param name="extendKeyword">The <c>extend</c> keyword token.</param>
    /// <param name="typeName">The token identifying the type being extended.</param>
    /// <param name="methods">The list of method declarations defined inside the extend body.</param>
    /// <param name="span">The source location of this declaration.</param>
    public ExtendStmt(Token extendKeyword, Token typeName, List<FnDeclStmt> methods, SourceSpan span) : base(span)
    {
        ExtendKeyword = extendKeyword;
        TypeName = typeName;
        Methods = methods;
    }

    /// <inheritdoc />
    public override T Accept<T>(IStmtVisitor<T> visitor) => visitor.VisitExtendStmt(this);
}
