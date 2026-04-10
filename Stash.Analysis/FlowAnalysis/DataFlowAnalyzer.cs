namespace Stash.Analysis.FlowAnalysis;

using System.Collections.Generic;
using Stash.Parsing.AST;

/// <summary>
/// Forward data flow analysis engine that propagates <see cref="DataFlowState"/>
/// through a <see cref="ControlFlowGraph"/> using a worklist algorithm.
/// </summary>
public sealed class DataFlowAnalyzer
{
    /// <summary>
    /// Runs forward null-state analysis on the given CFG.
    /// Returns a mapping from each block ID to the data flow state at the block's entry.
    /// </summary>
    public static Dictionary<int, DataFlowState> Analyze(ControlFlowGraph cfg)
    {
        var entryStates = new Dictionary<int, DataFlowState>();
        var exitStates = new Dictionary<int, DataFlowState>();

        // Initialize all blocks with empty states
        foreach (var block in cfg.Blocks)
        {
            entryStates[block.Id] = new DataFlowState();
            exitStates[block.Id] = new DataFlowState();
        }

        // Worklist (forward analysis)
        var worklist = new Queue<BasicBlock>();
        worklist.Enqueue(cfg.Entry);
        var inWorklist = new HashSet<int> { cfg.Entry.Id };

        while (worklist.Count > 0)
        {
            var block = worklist.Dequeue();
            inWorklist.Remove(block.Id);

            // Merge predecessor exit states into this block's entry
            var mergedEntry = new DataFlowState();
            foreach (var pred in block.Predecessors)
            {
                mergedEntry.MergeFrom(exitStates[pred.Id]);
            }

            // For entry block with no predecessors, keep current entry state
            if (block.Predecessors.Count > 0)
                entryStates[block.Id] = mergedEntry;

            // Transfer: apply statements in this block to compute exit state
            var state = entryStates[block.Id].Clone();
            foreach (var stmt in block.Statements)
            {
                ApplyTransfer(state, stmt);
            }

            // Check if exit state changed
            var oldExit = exitStates[block.Id];
            if (!StatesEqual(oldExit, state))
            {
                exitStates[block.Id] = state;

                // Add successors to worklist
                foreach (var succ in block.Successors)
                {
                    if (inWorklist.Add(succ.Id))
                        worklist.Enqueue(succ);
                }
            }
        }

        return entryStates;
    }

    /// <summary>
    /// Applies the effect of a statement on the data flow state.
    /// </summary>
    internal static void ApplyTransfer(DataFlowState state, Stmt stmt)
    {
        // Variable declaration with null initializer
        if (stmt is VarDeclStmt varDecl)
        {
            if (varDecl.Initializer == null)
                state.SetState(varDecl.Name.Lexeme, NullState.Unknown);
            else
                state.SetState(varDecl.Name.Lexeme, InferNullState(varDecl.Initializer, state));
        }
        else if (stmt is ConstDeclStmt constDecl)
        {
            state.SetState(constDecl.Name.Lexeme, InferNullState(constDecl.Initializer, state));
        }
        // Assignment expression statement
        else if (stmt is ExprStmt exprStmt && exprStmt.Expression is AssignExpr assign)
        {
            state.SetState(assign.Name.Lexeme, InferNullState(assign.Value, state));
        }
    }

    /// <summary>
    /// Infers the null state of an expression.
    /// </summary>
    internal static NullState InferNullState(Expr expr, DataFlowState state)
    {
        // Literal null → Null
        if (expr is LiteralExpr literal)
        {
            return literal.Value == null ? NullState.Null : NullState.NonNull;
        }

        // Variable reference → look up current state
        if (expr is IdentifierExpr id)
        {
            return state.GetState(id.Name.Lexeme);
        }

        // Null coalescing: a ?? b → may be null depending on b; conservative estimate
        if (expr is NullCoalesceExpr)
        {
            return NullState.MaybeNull;
        }

        // Function calls, operations, etc. → assume non-null (conservative)
        return NullState.NonNull;
    }

    private static bool StatesEqual(DataFlowState a, DataFlowState b)
    {
        var aStates = a.AllStates;
        var bStates = b.AllStates;

        if (aStates.Count != bStates.Count) return false;
        foreach (var kv in aStates)
        {
            if (!bStates.TryGetValue(kv.Key, out var bVal) || kv.Value != bVal)
                return false;
        }
        return true;
    }
}
