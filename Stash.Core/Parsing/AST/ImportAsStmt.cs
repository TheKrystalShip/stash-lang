using Stash.Common;
using Stash.Lexing;

namespace Stash.Parsing.AST;

/// <summary>
/// A module import with namespace alias: <c>import expr as name;</c>
/// </summary>
public class ImportAsStmt : Stmt
{
    /// <summary>
    /// Gets the expression that evaluates to the module file path.
    /// </summary>
    public Expr Path { get; }

    /// <summary>
    /// Gets the identifier token for the namespace alias.
    /// </summary>
    public Token Alias { get; }

    /// <summary>
    /// Gets a value indicating whether the path is a static string literal.
    /// </summary>
    public bool IsStaticPath => Path is LiteralExpr { Value: string };

    /// <summary>
    /// Gets the static string value of the path if it is a string literal; otherwise <c>null</c>.
    /// </summary>
    public string? StaticPathValue => (Path as LiteralExpr)?.Value as string;

    public ImportAsStmt(Expr path, Token alias, SourceSpan span) : base(span)
    {
        Path = path;
        Alias = alias;
    }

    public override T Accept<T>(IStmtVisitor<T> visitor) => visitor.VisitImportAsStmt(this);
}
