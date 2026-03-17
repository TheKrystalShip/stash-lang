using System.Collections.Generic;

namespace Stash.Parsing.AST;

using Stash.Common;
using Stash.Lexing;

/// <summary>
/// A destructuring declaration: <c>let [a, b, c] = expr;</c> or <c>let { x, y } = expr;</c>
/// </summary>
/// <remarks>
/// Supports two patterns: array destructuring (<c>let [a, b] = expr</c>) and object destructuring
/// (<c>let { x, y } = expr</c>). The <see cref="IsConst"/> flag indicates whether the bindings are
/// constants (<c>const</c>) or variables (<c>let</c>). Array destructuring unpacks elements by position;
/// object destructuring unpacks by key name.
/// </remarks>
public class DestructureStmt : Stmt
{
    /// <summary>Specifies the kind of destructuring pattern.</summary>
    public enum PatternKind
    {
        /// <summary>Array destructuring: <c>let [a, b, c] = expr;</c></summary>
        Array,
        /// <summary>Object/dictionary destructuring: <c>let { x, y } = expr;</c></summary>
        Object
    }

    /// <summary>Gets the destructuring pattern kind: <see cref="PatternKind.Array"/> for <c>[a, b]</c> or <see cref="PatternKind.Object"/> for <c>{ x, y }</c>.</summary>
    public PatternKind Kind { get; }
    /// <summary>Gets the list of identifier tokens for the bindings being declared.</summary>
    public List<Token> Names { get; }
    /// <summary>Gets whether the bindings are constants (<c>const</c>) or mutable variables (<c>let</c>).</summary>
    public bool IsConst { get; }
    /// <summary>Gets the expression producing the value to destructure.</summary>
    public Expr Initializer { get; }

    /// <summary>Initializes a new instance of <see cref="DestructureStmt"/>.</summary>
    /// <param name="kind">The destructuring pattern kind.</param>
    /// <param name="names">The list of identifier tokens for the bindings being declared.</param>
    /// <param name="isConst">Whether the bindings are constants or mutable variables.</param>
    /// <param name="initializer">The expression producing the value to destructure.</param>
    /// <param name="span">The source location of this statement.</param>
    public DestructureStmt(PatternKind kind, List<Token> names, bool isConst, Expr initializer, SourceSpan span) : base(span)
    {
        Kind = kind;
        Names = names;
        IsConst = isConst;
        Initializer = initializer;
    }

    /// <inheritdoc />
    public override T Accept<T>(IStmtVisitor<T> visitor) => visitor.VisitDestructureStmt(this);
}
