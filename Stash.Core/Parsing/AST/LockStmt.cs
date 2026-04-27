namespace Stash.Parsing.AST;

using Stash.Common;
using Stash.Lexing;

/// <summary>
/// Represents a <c>lock</c> statement that acquires a file-based mutex before executing its body.
/// </summary>
/// <remarks>
/// <code>
/// lock "/var/run/deploy.lock" { ... }
/// lock "/var/run/job.lock" (wait: 30s) { ... }
/// lock "/var/run/nightly.lock" (stale: 1h) { ... }
/// lock "/var/run/sync.lock" (wait: 10s, stale: 2h) { ... }
/// </code>
/// </remarks>
public sealed class LockStmt : Stmt
{
    /// <summary>The <c>lock</c> keyword token.</summary>
    public Token LockKeyword { get; }

    /// <summary>Expression that evaluates to the lock file path.</summary>
    public Expr Path { get; }

    /// <summary>Optional <c>wait</c> duration — how long to wait for the lock before throwing <c>LockError</c>. <see langword="null"/> means wait forever.</summary>
    public Expr? WaitOption { get; }

    /// <summary>Optional <c>stale</c> duration — steal the lock if the lock file is older than this. <see langword="null"/> means no stale detection.</summary>
    public Expr? StaleOption { get; }

    /// <summary>The body to execute while holding the lock.</summary>
    public BlockStmt Body { get; }

    public LockStmt(Token lockKeyword, Expr path, Expr? waitOption, Expr? staleOption, BlockStmt body, SourceSpan span)
        : base(span, StmtType.Lock)
    {
        LockKeyword = lockKeyword;
        Path = path;
        WaitOption = waitOption;
        StaleOption = staleOption;
        Body = body;
    }

    /// <inheritdoc />
    public override T Accept<T>(IStmtVisitor<T> visitor) => visitor.VisitLockStmt(this);
}
