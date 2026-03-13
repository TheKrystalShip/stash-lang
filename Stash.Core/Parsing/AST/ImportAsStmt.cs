using Stash.Common;
using Stash.Lexing;

namespace Stash.Parsing.AST;

/// <summary>
/// A module import with namespace alias: <c>import "file.stash" as name;</c>
/// </summary>
public class ImportAsStmt : Stmt
{
    /// <summary>
    /// Gets the string literal token containing the module file path.
    /// </summary>
    public Token Path { get; }

    /// <summary>
    /// Gets the identifier token for the namespace alias.
    /// </summary>
    public Token Alias { get; }

    public ImportAsStmt(Token path, Token alias, SourceSpan span) : base(span)
    {
        Path = path;
        Alias = alias;
    }

    public override T Accept<T>(IStmtVisitor<T> visitor) => visitor.VisitImportAsStmt(this);
}
