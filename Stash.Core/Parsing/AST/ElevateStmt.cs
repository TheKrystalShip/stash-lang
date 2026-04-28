namespace Stash.Parsing.AST;

using Stash.Common;
using Stash.Lexing;

/// <summary>
/// A scoped privilege elevation block: <c>elevate { ... }</c> or <c>elevate("doas") { ... }</c>
/// </summary>
/// <remarks>
/// Commands (<c>$()</c> and <c>$&gt;()</c>) executed inside the block are automatically
/// prefixed with the platform elevation program (e.g., <c>sudo</c> on Unix, <c>gsudo</c>
/// on Windows). The optional <see cref="Elevator"/> expression specifies an alternative
/// elevation program; if omitted, the platform default is used.
/// </remarks>
public class ElevateStmt : Stmt
{
    /// <summary>Gets the optional elevator program expression (e.g. a string literal <c>"doas"</c>).
    /// Null when the platform default should be used.</summary>
    public Expr? Elevator { get; }
    /// <summary>Gets the block of statements executed under elevated privileges.</summary>
    public BlockStmt Body { get; }
    /// <summary>Gets the <c>elevate</c> keyword token (for semantic highlighting).</summary>
    public Token? ElevateKeyword { get; }

    /// <summary>Initializes a new instance of <see cref="ElevateStmt"/>.</summary>
    /// <param name="elevator">The optional elevator program expression, or null for platform default.</param>
    /// <param name="body">The block of statements to execute with elevated privileges.</param>
    /// <param name="span">The source location of this statement.</param>
    /// <param name="elevateKeyword">The <c>elevate</c> keyword token, or <c>null</c>.</param>
    public ElevateStmt(Expr? elevator, BlockStmt body, SourceSpan span, Token? elevateKeyword = null) : base(span, StmtType.Elevate)
    {
        Elevator = elevator;
        Body = body;
        ElevateKeyword = elevateKeyword;
    }

    /// <inheritdoc />
    public override T Accept<T>(IStmtVisitor<T> visitor) => visitor.VisitElevateStmt(this);
}
