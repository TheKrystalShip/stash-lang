using System.Collections.Generic;
using Stash.Common;
using Stash.Lexing;

namespace Stash.Parsing.AST;

/// <summary>
/// An import declaration: <c>import { name1, name2 } from expr;</c>
/// </summary>
public class ImportStmt : Stmt
{
    /// <summary>
    /// Gets the list of name tokens to import from the module.
    /// </summary>
    public List<Token> Names { get; }

    /// <summary>
    /// Gets the expression that evaluates to the module file path.
    /// </summary>
    public Expr Path { get; }

    /// <summary>
    /// Gets a value indicating whether the path is a static string literal.
    /// </summary>
    public bool IsStaticPath => Path is LiteralExpr { Value: string };

    /// <summary>
    /// Gets the static string value of the path if it is a string literal; otherwise <c>null</c>.
    /// </summary>
    public string? StaticPathValue => (Path as LiteralExpr)?.Value as string;

    /// <summary>
    /// Creates a new import statement node.
    /// </summary>
    /// <param name="names">The list of identifier tokens to import.</param>
    /// <param name="path">The expression evaluating to the module file path.</param>
    /// <param name="span">The source span covering the entire import statement.</param>
    public ImportStmt(List<Token> names, Expr path, SourceSpan span) : base(span)
    {
        Names = names;
        Path = path;
    }

    /// <inheritdoc />
    public override T Accept<T>(IStmtVisitor<T> visitor) => visitor.VisitImportStmt(this);
}
