using Stash.Parsing.AST;

namespace Stash.Bytecode;

/// <summary>
/// Lock statement visitor stub — Phase 2 will implement the full compilation.
/// </summary>
public sealed partial class Compiler
{
    /// <inheritdoc />
    public object? VisitLockStmt(LockStmt stmt)
    {
        // TODO: Phase 2 — implement lock compilation
        CompileStmt(stmt.Body);
        return null;
    }
}
