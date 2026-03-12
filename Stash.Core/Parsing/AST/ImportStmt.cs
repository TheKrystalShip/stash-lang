using System.Collections.Generic;
using Stash.Common;
using Stash.Lexing;

namespace Stash.Parsing.AST;

/// <summary>
/// An import declaration: <c>import { name1, name2 } from "file.stash";</c>
/// </summary>
public class ImportStmt : Stmt
{
    /// <summary>
    /// Gets the list of name tokens to import from the module.
    /// </summary>
    public List<Token> Names { get; }

    /// <summary>
    /// Gets the string literal token containing the module file path.
    /// </summary>
    public Token Path { get; }

    /// <summary>
    /// Creates a new import statement node.
    /// </summary>
    /// <param name="names">The list of identifier tokens to import.</param>
    /// <param name="path">The string literal token with the module file path.</param>
    /// <param name="span">The source span covering the entire import statement.</param>
    public ImportStmt(List<Token> names, Token path, SourceSpan span) : base(span)
    {
        Names = names;
        Path = path;
    }

    /// <inheritdoc />
    public override T Accept<T>(IStmtVisitor<T> visitor) => visitor.VisitImportStmt(this);
}
