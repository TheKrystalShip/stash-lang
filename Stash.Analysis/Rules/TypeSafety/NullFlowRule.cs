namespace Stash.Analysis.Rules;

using System;
using System.Collections.Generic;
using Stash.Analysis.FlowAnalysis;
using Stash.Parsing.AST;

/// <summary>
/// SA0309 — Emits a warning when a variable is definitely null or possibly null (based on
/// control-flow-aware data flow analysis) and is subsequently accessed via dot access or
/// called as a function without a preceding null guard.
/// </summary>
/// <remarks>
/// Unlike SA0308, which uses a simple intra-procedural scan, this rule uses a worklist-based
/// forward data flow analysis to track null states across all code paths — including branches
/// and assignments. It detects cases where a variable is assigned <c>null</c> on one branch
/// while remaining non-null on another, resulting in a <em>MaybeNull</em> state after the
/// join point.
///
/// <code>
/// let x = "ok";
/// if (cond) { x = null; }
/// x.member;   // SA0309 — x may be null after the branch
/// </code>
/// </remarks>
public sealed class NullFlowRule : IAnalysisRule
{
    public DiagnosticDescriptor Descriptor => DiagnosticDescriptors.SA0309;

    /// <summary>Empty — this is a post-walk rule invoked once after the full AST walk.</summary>
    public IReadOnlySet<Type> SubscribedNodeTypes { get; } = new HashSet<Type>();

    public void Analyze(RuleContext context)
    {
        var builder = new CfgBuilder();
        var cfg = builder.Build(context.AllStatements);

        var entryStates = DataFlowAnalyzer.Analyze(cfg);

        foreach (var block in cfg.Blocks)
        {
            if (!entryStates.TryGetValue(block.Id, out var blockEntry)) continue;
            var state = blockEntry.Clone();

            foreach (var stmt in block.Statements)
            {
                CheckStmtForNullAccess(stmt, state, context);
                DataFlowAnalyzer.ApplyTransfer(state, stmt);
            }
        }
    }

    // ── Null access checking ─────────────────────────────────────────────────

    private static void CheckStmtForNullAccess(Stmt stmt, DataFlowState state, RuleContext context)
    {
        switch (stmt)
        {
            case ExprStmt exprStmt:
                if (exprStmt.Expression is AssignExpr assign)
                    // Only scan the value being assigned, not the target variable itself
                    ScanExprForNullAccess(assign.Value, state, context);
                else
                    ScanExprForNullAccess(exprStmt.Expression, state, context);
                break;

            case VarDeclStmt varDecl when varDecl.Initializer != null:
                ScanExprForNullAccess(varDecl.Initializer, state, context);
                break;

            case ConstDeclStmt constDecl:
                ScanExprForNullAccess(constDecl.Initializer, state, context);
                break;

            case ReturnStmt ret when ret.Value != null:
                ScanExprForNullAccess(ret.Value, state, context);
                break;

            case ThrowStmt throwStmt:
                ScanExprForNullAccess(throwStmt.Value, state, context);
                break;
        }
    }

    /// <summary>
    /// Walks an expression tree looking for dot access or calls on null/maybe-null variables.
    /// </summary>
    private static void ScanExprForNullAccess(Expr expr, DataFlowState state, RuleContext context)
    {
        switch (expr)
        {
            case DotExpr dot:
                if (!dot.IsOptional && dot.Object is IdentifierExpr dotObj)
                {
                    var ns = state.GetState(dotObj.Name.Lexeme);
                    if (ns is NullState.Null or NullState.MaybeNull)
                    {
                        context.ReportDiagnostic(
                            DiagnosticDescriptors.SA0309.CreateDiagnostic(dot.Object.Span, dotObj.Name.Lexeme));
                        return; // Avoid cascading reports for the same variable
                    }
                }
                ScanExprForNullAccess(dot.Object, state, context);
                break;

            case CallExpr call:
                // Calling a definitely-null or maybe-null variable directly
                if (!call.IsOptional && call.Callee is IdentifierExpr calleeId)
                {
                    var ns = state.GetState(calleeId.Name.Lexeme);
                    if (ns is NullState.Null or NullState.MaybeNull)
                    {
                        context.ReportDiagnostic(
                            DiagnosticDescriptors.SA0309.CreateDiagnostic(call.Callee.Span, calleeId.Name.Lexeme));
                    }
                }
                foreach (var arg in call.Arguments)
                    ScanExprForNullAccess(arg, state, context);
                break;

            case BinaryExpr bin:
                ScanExprForNullAccess(bin.Left, state, context);
                ScanExprForNullAccess(bin.Right, state, context);
                break;

            case NullCoalesceExpr nullCoalesce:
                ScanExprForNullAccess(nullCoalesce.Left, state, context);
                ScanExprForNullAccess(nullCoalesce.Right, state, context);
                break;

            case TernaryExpr ternary:
                ScanExprForNullAccess(ternary.Condition, state, context);
                ScanExprForNullAccess(ternary.ThenBranch, state, context);
                ScanExprForNullAccess(ternary.ElseBranch, state, context);
                break;

            case UnaryExpr unary:
                ScanExprForNullAccess(unary.Right, state, context);
                break;
        }
    }
}
