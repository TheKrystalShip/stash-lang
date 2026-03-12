using Stash.Common;

namespace Stash.Parsing.AST;

/// <summary>
/// Represents a pipe expression that chains the stdout of one command to the stdin of another:
/// <c>$(cmd1) | $(cmd2)</c>.
/// </summary>
public class PipeExpr : Expr
{
    /// <summary>The left-hand side expression (whose stdout is piped).</summary>
    public Expr Left { get; }

    /// <summary>The right-hand side expression (which receives stdin).</summary>
    public Expr Right { get; }

    public PipeExpr(Expr left, Expr right, SourceSpan span) : base(span)
    {
        Left = left;
        Right = right;
    }

    public override T Accept<T>(IExprVisitor<T> visitor)
    {
        return visitor.VisitPipeExpr(this);
    }
}
