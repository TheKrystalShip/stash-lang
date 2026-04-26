namespace Stash.Parsing.AST;

using System.Collections.Generic;
using Stash.Common;
using Stash.Lexing;

/// <summary>
/// A try/catch/finally statement: <c>try { ... } catch(e) { ... } finally { ... }</c>
/// </summary>
/// <remarks>
/// Supports these valid forms:
/// <list type="bullet">
/// <item><c>try { ... } catch (e) { ... }</c> — untyped catch-all</item>
/// <item><c>try { ... } catch (TypeError e) { ... }</c> — typed catch</item>
/// <item><c>try { ... } catch (TypeError | ValueError e) { ... } catch (e) { ... }</c> — multiple clauses</item>
/// <item><c>try { ... } finally { ... }</c> — guaranteed cleanup, errors propagate</item>
/// <item><c>try { ... } catch (e) { ... } finally { ... }</c> — both</item>
/// <item><c>try { ... }</c> — error suppression (errors swallowed silently)</item>
/// </list>
/// <see cref="CatchClauses"/> is empty when there are no catch clauses.
/// <see cref="FinallyKeyword"/> and <see cref="FinallyBody"/> are <c>null</c> when there is no finally clause.
/// </remarks>
public class TryCatchStmt : Stmt
{
    /// <summary>Gets the <c>try</c> keyword token.</summary>
    public Token TryKeyword { get; }

    /// <summary>Gets the body of the try block.</summary>
    public BlockStmt TryBody { get; }

    /// <summary>Gets the ordered list of catch clauses. Empty when there is no catch.</summary>
    public IReadOnlyList<CatchClause> CatchClauses { get; }

    /// <summary>Gets the <c>finally</c> keyword token, or <c>null</c> if there is no finally clause.</summary>
    public Token? FinallyKeyword { get; }

    /// <summary>Gets the body of the finally block, or <c>null</c> if there is no finally clause.</summary>
    public BlockStmt? FinallyBody { get; }

    /// <summary>Initializes a new instance of <see cref="TryCatchStmt"/>.</summary>
    public TryCatchStmt(
        Token tryKeyword,
        BlockStmt tryBody,
        IReadOnlyList<CatchClause> catchClauses,
        Token? finallyKeyword,
        BlockStmt? finallyBody,
        SourceSpan span) : base(span, StmtType.TryCatch)
    {
        TryKeyword = tryKeyword;
        TryBody = tryBody;
        CatchClauses = catchClauses;
        FinallyKeyword = finallyKeyword;
        FinallyBody = finallyBody;
    }

    /// <inheritdoc />
    public override T Accept<T>(IStmtVisitor<T> visitor) => visitor.VisitTryCatchStmt(this);
}
