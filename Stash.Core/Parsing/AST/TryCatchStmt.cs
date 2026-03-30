namespace Stash.Parsing.AST;

using Stash.Common;
using Stash.Lexing;

/// <summary>
/// A try/catch/finally statement: <c>try { ... } catch(e) { ... } finally { ... }</c>
/// </summary>
/// <remarks>
/// Supports four valid forms:
/// <list type="bullet">
/// <item><c>try { ... } catch(e) { ... }</c> — error handling</item>
/// <item><c>try { ... } finally { ... }</c> — guaranteed cleanup, errors propagate</item>
/// <item><c>try { ... } catch(e) { ... } finally { ... }</c> — both</item>
/// <item><c>try { ... }</c> — error suppression (errors swallowed silently)</item>
/// </list>
/// The <see cref="CatchKeyword"/>, <see cref="CatchVariable"/>, and <see cref="CatchBody"/> are
/// all <c>null</c> when there is no catch clause. The <see cref="FinallyKeyword"/> and
/// <see cref="FinallyBody"/> are <c>null</c> when there is no finally clause.
/// </remarks>
public class TryCatchStmt : Stmt
{
    /// <summary>Gets the <c>try</c> keyword token.</summary>
    public Token TryKeyword { get; }
    /// <summary>Gets the body of the try block.</summary>
    public BlockStmt TryBody { get; }
    /// <summary>Gets the <c>catch</c> keyword token, or <c>null</c> if there is no catch clause.</summary>
    public Token? CatchKeyword { get; }
    /// <summary>Gets the catch variable name token (e.g. <c>e</c> in <c>catch(e)</c>), or <c>null</c> if there is no catch clause.</summary>
    public Token? CatchVariable { get; }
    /// <summary>Gets the body of the catch block, or <c>null</c> if there is no catch clause.</summary>
    public BlockStmt? CatchBody { get; }
    /// <summary>Gets the <c>finally</c> keyword token, or <c>null</c> if there is no finally clause.</summary>
    public Token? FinallyKeyword { get; }
    /// <summary>Gets the body of the finally block, or <c>null</c> if there is no finally clause.</summary>
    public BlockStmt? FinallyBody { get; }

    /// <summary>Initializes a new instance of <see cref="TryCatchStmt"/>.</summary>
    /// <param name="tryKeyword">The <c>try</c> keyword token.</param>
    /// <param name="tryBody">The body of the try block.</param>
    /// <param name="catchKeyword">The <c>catch</c> keyword token, or <c>null</c>.</param>
    /// <param name="catchVariable">The catch variable name token, or <c>null</c>.</param>
    /// <param name="catchBody">The body of the catch block, or <c>null</c>.</param>
    /// <param name="finallyKeyword">The <c>finally</c> keyword token, or <c>null</c>.</param>
    /// <param name="finallyBody">The body of the finally block, or <c>null</c>.</param>
    /// <param name="span">The source location of this statement.</param>
    public TryCatchStmt(
        Token tryKeyword,
        BlockStmt tryBody,
        Token? catchKeyword,
        Token? catchVariable,
        BlockStmt? catchBody,
        Token? finallyKeyword,
        BlockStmt? finallyBody,
        SourceSpan span) : base(span)
    {
        TryKeyword = tryKeyword;
        TryBody = tryBody;
        CatchKeyword = catchKeyword;
        CatchVariable = catchVariable;
        CatchBody = catchBody;
        FinallyKeyword = finallyKeyword;
        FinallyBody = finallyBody;
    }

    /// <inheritdoc />
    public override T Accept<T>(IStmtVisitor<T> visitor) => visitor.VisitTryCatchStmt(this);
}
