namespace Stash.Analysis.FlowAnalysis;

using System.Collections.Generic;
using Stash.Parsing.AST;

/// <summary>
/// Builds a <see cref="ControlFlowGraph"/> from a flat list of AST statements, such as a
/// function body or the top-level program.
/// </summary>
/// <remarks>
/// The builder performs a single recursive descent over the AST, creating basic blocks and
/// wiring successor/predecessor edges. It handles:
/// <list type="bullet">
/// <item>Sequential statements (added to the current block)</item>
/// <item>if/else — creates condition block, then-block, else-block, and a join block</item>
/// <item>while / do-while / for / for-in — creates header, body, and after blocks with back edges</item>
/// <item>try/catch/finally — creates try, catch, finally, and after blocks</item>
/// <item>return / throw — terminates the current block and connects it to the synthetic exit</item>
/// <item>break / continue — connects to the appropriate loop-exit or loop-header block</item>
/// </list>
/// Nested function declarations are recorded as statements in their enclosing block but their
/// bodies are not analyzed as part of the enclosing CFG; each function has its own CFG.
/// </remarks>
public sealed class CfgBuilder
{
    private readonly List<BasicBlock> _blocks = new();
    private int _nextId;

    private BasicBlock NewBlock()
    {
        var block = new BasicBlock(++_nextId);
        _blocks.Add(block);
        return block;
    }

    /// <summary>
    /// Builds a <see cref="ControlFlowGraph"/> for the given statement list.
    /// </summary>
    public ControlFlowGraph Build(List<Stmt> statements)
    {
        _blocks.Clear();
        _nextId = 0;

        var entry = NewBlock();
        var exit = NewBlock(); // synthetic exit — always last

        var outBlock = BuildSequence(entry, statements, exit,
            breakTarget: null, continueTarget: null);

        // Remaining fall-through path connects to exit
        if (outBlock != null)
        {
            outBlock.AddSuccessor(exit);
        }

        return new ControlFlowGraph(entry, exit, new List<BasicBlock>(_blocks));
    }

    // ── Core sequence builder ────────────────────────────────────────────────

    /// <summary>
    /// Processes <paramref name="stmts"/> starting from <paramref name="current"/>.
    /// Returns the block that receives control after the sequence, or <see langword="null"/>
    /// if every path in the sequence is guaranteed to terminate.
    /// </summary>
    private BasicBlock? BuildSequence(
        BasicBlock current,
        IReadOnlyList<Stmt> stmts,
        BasicBlock exit,
        BasicBlock? breakTarget,
        BasicBlock? continueTarget)
    {
        foreach (var stmt in stmts)
        {
            var cont = BuildStatement(current, stmt, exit, breakTarget, continueTarget);
            if (cont == null)
            {
                return null; // path terminated; remaining stmts unreachable
            }
            current = cont;
        }
        return current;
    }

    // ── Statement dispatcher ─────────────────────────────────────────────────

    private BasicBlock? BuildStatement(
        BasicBlock current,
        Stmt stmt,
        BasicBlock exit,
        BasicBlock? breakTarget,
        BasicBlock? continueTarget)
    {
        switch (stmt)
        {
            case ReturnStmt:
                current.Statements.Add(stmt);
                current.BranchKind = BranchKind.Return;
                current.AddSuccessor(exit);
                return null;

            case ThrowStmt:
                current.Statements.Add(stmt);
                current.BranchKind = BranchKind.Throw;
                current.AddSuccessor(exit);
                return null;

            case BreakStmt:
                current.Statements.Add(stmt);
                current.BranchKind = BranchKind.Break;
                current.AddSuccessor(breakTarget ?? exit);
                return null;

            case ContinueStmt:
                current.Statements.Add(stmt);
                current.BranchKind = BranchKind.Continue;
                current.AddSuccessor(continueTarget ?? exit);
                return null;

            case ExprStmt exprStmt when IsProcessExit(exprStmt):
                current.Statements.Add(stmt);
                current.BranchKind = BranchKind.Throw;
                current.AddSuccessor(exit);
                return null;

            case IfStmt ifStmt:
                return BuildIfStmt(current, ifStmt, exit, breakTarget, continueTarget);

            case WhileStmt whileStmt:
                return BuildWhileStmt(current, whileStmt, exit);

            case DoWhileStmt doWhileStmt:
                return BuildDoWhileStmt(current, doWhileStmt, exit);

            case ForStmt forStmt:
                return BuildForStmt(current, forStmt, exit);

            case ForInStmt forInStmt:
                return BuildForInStmt(current, forInStmt, exit);

            case TryCatchStmt tryCatchStmt:
                return BuildTryCatchStmt(current, tryCatchStmt, exit, breakTarget, continueTarget);

            case BlockStmt blockStmt:
                return BuildSequence(current, blockStmt.Statements, exit, breakTarget, continueTarget);

            default:
                // Simple statement (var decl, expr stmt, import, struct decl, fn decl, etc.)
                // Nested fn/struct/enum bodies are NOT analyzed as part of the enclosing CFG.
                current.Statements.Add(stmt);
                return current;
        }
    }

    // ── Compound statement builders ──────────────────────────────────────────

    private BasicBlock? BuildIfStmt(
        BasicBlock current,
        IfStmt stmt,
        BasicBlock exit,
        BasicBlock? breakTarget,
        BasicBlock? continueTarget)
    {
        current.BranchKind = BranchKind.Conditional;
        current.BranchCondition = stmt.Condition;

        var thenEntry = NewBlock();
        var joinBlock = NewBlock();

        // Then branch
        current.AddSuccessor(thenEntry);
        var thenOut = BuildSequence(thenEntry, GetStmts(stmt.ThenBranch), exit, breakTarget, continueTarget);
        if (thenOut != null)
        {
            thenOut.AddSuccessor(joinBlock);
        }

        if (stmt.ElseBranch != null)
        {
            // Else branch
            var elseEntry = NewBlock();
            current.AddSuccessor(elseEntry);
            var elseOut = BuildSequence(elseEntry, GetStmts(stmt.ElseBranch), exit, breakTarget, continueTarget);
            if (elseOut != null)
            {
                elseOut.AddSuccessor(joinBlock);
            }
        }
        else
        {
            // No else: the false path falls through directly to join
            current.AddSuccessor(joinBlock);
        }

        // If join has no predecessors, all paths terminated — it's unreachable
        return joinBlock.Predecessors.Count > 0 ? joinBlock : null;
    }

    private BasicBlock? BuildWhileStmt(BasicBlock current, WhileStmt stmt, BasicBlock exit)
    {
        var condBlock = NewBlock();
        var bodyEntry = NewBlock();
        var afterBlock = NewBlock();

        current.AddSuccessor(condBlock);
        condBlock.BranchKind = BranchKind.Conditional;
        condBlock.BranchCondition = stmt.Condition;
        condBlock.AddSuccessor(bodyEntry);   // condition true → body
        condBlock.AddSuccessor(afterBlock);  // condition false → after loop

        var bodyOut = BuildSequence(bodyEntry, stmt.Body.Statements, exit,
            breakTarget: afterBlock, continueTarget: condBlock);
        if (bodyOut != null)
        {
            bodyOut.AddSuccessor(condBlock); // back edge
        }

        return afterBlock;
    }

    private BasicBlock? BuildDoWhileStmt(BasicBlock current, DoWhileStmt stmt, BasicBlock exit)
    {
        var bodyEntry = NewBlock();
        var condBlock = NewBlock();
        var afterBlock = NewBlock();

        current.AddSuccessor(bodyEntry);

        var bodyOut = BuildSequence(bodyEntry, stmt.Body.Statements, exit,
            breakTarget: afterBlock, continueTarget: condBlock);
        if (bodyOut != null)
        {
            bodyOut.AddSuccessor(condBlock);
        }

        condBlock.BranchKind = BranchKind.Conditional;
        condBlock.BranchCondition = stmt.Condition;
        condBlock.AddSuccessor(bodyEntry);   // condition true → re-enter body
        condBlock.AddSuccessor(afterBlock);  // condition false → after loop

        return afterBlock;
    }

    private BasicBlock? BuildForStmt(BasicBlock current, ForStmt stmt, BasicBlock exit)
    {
        // Emit the initializer into the current block
        if (stmt.Initializer != null)
        {
            current.Statements.Add(stmt.Initializer);
        }

        var condBlock = NewBlock();
        var bodyEntry = NewBlock();
        var incrBlock = NewBlock();
        var afterBlock = NewBlock();

        current.AddSuccessor(condBlock);
        condBlock.BranchKind = BranchKind.Conditional;
        condBlock.BranchCondition = stmt.Condition;
        condBlock.AddSuccessor(bodyEntry);  // condition true → body
        condBlock.AddSuccessor(afterBlock); // condition false → after loop

        var bodyOut = BuildSequence(bodyEntry, stmt.Body.Statements, exit,
            breakTarget: afterBlock, continueTarget: incrBlock);
        if (bodyOut != null)
        {
            bodyOut.AddSuccessor(incrBlock);
        }

        incrBlock.AddSuccessor(condBlock); // back edge (increment then re-check)

        return afterBlock;
    }

    private BasicBlock? BuildForInStmt(BasicBlock current, ForInStmt stmt, BasicBlock exit)
    {
        var headerBlock = NewBlock();
        var bodyEntry = NewBlock();
        var afterBlock = NewBlock();

        current.AddSuccessor(headerBlock);
        headerBlock.BranchKind = BranchKind.Conditional;
        headerBlock.AddSuccessor(bodyEntry);  // more elements → body
        headerBlock.AddSuccessor(afterBlock); // done → after loop

        var bodyOut = BuildSequence(bodyEntry, stmt.Body.Statements, exit,
            breakTarget: afterBlock, continueTarget: headerBlock);
        if (bodyOut != null)
        {
            bodyOut.AddSuccessor(headerBlock); // back edge
        }

        return afterBlock;
    }

    private BasicBlock? BuildTryCatchStmt(
        BasicBlock current,
        TryCatchStmt stmt,
        BasicBlock exit,
        BasicBlock? breakTarget,
        BasicBlock? continueTarget)
    {
        var afterBlock = NewBlock();

        // Try body
        var tryEntry = NewBlock();
        current.AddSuccessor(tryEntry);
        var tryOut = BuildSequence(tryEntry, stmt.TryBody.Statements, exit, breakTarget, continueTarget);

        if (stmt.CatchClauses.Count > 0)
        {
            // Catch block(s): reachable from any throw within the try block.
            // For CFG purposes, model all clauses as a single merged catch region.
            BasicBlock? lastCatchOut = null;
            foreach (var clause in stmt.CatchClauses)
            {
                var catchEntry = NewBlock();
                tryEntry.AddSuccessor(catchEntry); // represent the exception edge
                var catchOut = BuildSequence(catchEntry, clause.Body.Statements, exit, breakTarget, continueTarget);
                lastCatchOut = catchOut;
            }

            if (stmt.FinallyBody != null)
            {
                var finallyEntry = NewBlock();
                if (tryOut != null) tryOut.AddSuccessor(finallyEntry);
                if (lastCatchOut != null) lastCatchOut.AddSuccessor(finallyEntry);
                var finallyOut = BuildSequence(finallyEntry, stmt.FinallyBody.Statements, exit, breakTarget, continueTarget);
                if (finallyOut != null) finallyOut.AddSuccessor(afterBlock);
            }
            else
            {
                if (tryOut != null) tryOut.AddSuccessor(afterBlock);
                if (lastCatchOut != null) lastCatchOut.AddSuccessor(afterBlock);
            }
        }
        else if (stmt.FinallyBody != null)
        {
            // Finally only (no catch)
            var finallyEntry = NewBlock();
            if (tryOut != null) tryOut.AddSuccessor(finallyEntry);
            var finallyOut = BuildSequence(finallyEntry, stmt.FinallyBody.Statements, exit, breakTarget, continueTarget);
            if (finallyOut != null) finallyOut.AddSuccessor(afterBlock);
        }
        else
        {
            if (tryOut != null) tryOut.AddSuccessor(afterBlock);
        }

        return afterBlock;
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static bool IsProcessExit(ExprStmt stmt)
    {
        return stmt.Expression is CallExpr call &&
               call.Callee is DotExpr dot &&
               dot.Object is IdentifierExpr obj &&
               obj.Name.Lexeme == "process" &&
               dot.Name.Lexeme == "exit";
    }

    private static IReadOnlyList<Stmt> GetStmts(Stmt stmt)
    {
        return stmt is BlockStmt block ? block.Statements : (IReadOnlyList<Stmt>)[stmt];
    }
}
