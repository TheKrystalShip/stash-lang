using System.Collections.Generic;

namespace Stash.Parsing.AST;

using Stash.Common;
using Stash.Lexing;

/// <summary>
/// A destructuring declaration: <c>let [a, b, c] = expr;</c> or <c>let { x, y } = expr;</c>
/// </summary>
public class DestructureStmt : Stmt
{
    public enum PatternKind { Array, Object }

    public PatternKind Kind { get; }
    public List<Token> Names { get; }
    public bool IsConst { get; }
    public Expr Initializer { get; }

    public DestructureStmt(PatternKind kind, List<Token> names, bool isConst, Expr initializer, SourceSpan span) : base(span)
    {
        Kind = kind;
        Names = names;
        IsConst = isConst;
        Initializer = initializer;
    }

    public override T Accept<T>(IStmtVisitor<T> visitor) => visitor.VisitDestructureStmt(this);
}
