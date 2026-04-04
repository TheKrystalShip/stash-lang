using Stash.Common;

namespace Stash.Parsing.AST;

/// <summary>
/// Specifies which output stream(s) a <see cref="RedirectExpr"/> redirects to a file.
/// </summary>
public enum RedirectStream
{
    /// <summary>Redirect standard output only.</summary>
    Stdout,

    /// <summary>Redirect standard error only.</summary>
    Stderr,

    /// <summary>Redirect both standard output and standard error.</summary>
    All
}

/// <summary>
/// Represents an output redirection expression that writes command output to a file:
/// <c>$(cmd) &gt; "file"</c>, <c>$(cmd) &gt;&gt; "file"</c>, <c>$(cmd) 2&gt; "file"</c>, <c>$(cmd) &amp;&gt; "file"</c>.
/// </summary>
public class RedirectExpr : Expr
{
    /// <summary>The command expression whose output is being redirected.</summary>
    public Expr Expression { get; }

    /// <summary>Which output stream(s) to redirect.</summary>
    public RedirectStream Stream { get; }

    /// <summary>Whether to append (<c>&gt;&gt;</c>) rather than overwrite (<c>&gt;</c>).</summary>
    public bool Append { get; }

    /// <summary>The target file path expression (must evaluate to a string).</summary>
    public Expr Target { get; }

    public RedirectExpr(Expr expression, RedirectStream stream, bool append, Expr target, SourceSpan span)
        : base(span, ExprType.Redirect)
    {
        Expression = expression;
        Stream = stream;
        Append = append;
        Target = target;
    }

    public override T Accept<T>(IExprVisitor<T> visitor)
    {
        return visitor.VisitRedirectExpr(this);
    }
}
