namespace Stash.Analysis.Rules;

using System;
using System.Collections.Generic;
using System.Linq;
using Stash.Parsing.AST;

/// <summary>
/// SA0210 — Emits a warning when a variable is declared without an initializer (<c>let x;</c>)
/// and may be used before it has been assigned on all code paths.
/// </summary>
/// <remarks>
/// This rule performs a simple, conservative analysis: it tracks variables declared without
/// initializers and checks whether any subsequent usage site can be reached before a guaranteed
/// assignment. The analysis handles the common case of if/else — if only one branch assigns
/// the variable, usage after the if/else is flagged.
///
/// <code>
/// let x;
/// if (cond) { x = 1; }
/// use(x);  // SA0210 — x may be unassigned if cond was false
/// </code>
/// </remarks>
public sealed class DefiniteAssignmentRule : IAnalysisRule
{
    public DiagnosticDescriptor Descriptor => DiagnosticDescriptors.SA0210;

    /// <summary>Empty — this is a post-walk rule invoked once after the full AST walk.</summary>
    public IReadOnlySet<Type> SubscribedNodeTypes { get; } = new HashSet<Type>();

    public void Analyze(RuleContext context)
    {
        AnalyzeBlock(context.AllStatements, new HashSet<string>(), context);
    }

    // ── Core analysis ─────────────────────────────────────────────────────────

    /// <summary>
    /// Analyzes a sequence of statements.
    /// <paramref name="definitelyAssigned"/> is the set of variables known to be assigned on
    /// all paths reaching the start of this block. The set is updated in place to reflect
    /// assignments made within the block.
    /// </summary>
    private static void AnalyzeBlock(
        IReadOnlyList<Stmt> stmts,
        HashSet<string> definitelyAssigned,
        RuleContext context)
    {
        // uninitialized: variables declared without initializer in THIS block
        // (child scopes have their own tracking)
        var uninitInBlock = new HashSet<string>();

        foreach (var stmt in stmts)
        {
            ProcessStmt(stmt, uninitInBlock, definitelyAssigned, context);
        }
    }

    private static void ProcessStmt(
        Stmt stmt,
        HashSet<string> uninitInBlock,
        HashSet<string> definitelyAssigned,
        RuleContext context)
    {
        switch (stmt)
        {
            case VarDeclStmt varDecl:
                if (varDecl.Initializer == null)
                {
                    // Declared without initializer → possibly null until assigned
                    uninitInBlock.Add(varDecl.Name.Lexeme);
                }
                else
                {
                    CheckReadsInExpr(varDecl.Initializer, uninitInBlock, context);
                    definitelyAssigned.Add(varDecl.Name.Lexeme);
                    uninitInBlock.Remove(varDecl.Name.Lexeme);
                }
                break;

            case ConstDeclStmt constDecl:
                CheckReadsInExpr(constDecl.Initializer, uninitInBlock, context);
                definitelyAssigned.Add(constDecl.Name.Lexeme);
                break;

            case ExprStmt exprStmt:
                if (exprStmt.Expression is AssignExpr assign)
                {
                    // RHS is read before LHS is written
                    CheckReadsInExpr(assign.Value, uninitInBlock, context);
                    definitelyAssigned.Add(assign.Name.Lexeme);
                    uninitInBlock.Remove(assign.Name.Lexeme);
                }
                else
                {
                    CheckReadsInExpr(exprStmt.Expression, uninitInBlock, context);
                }
                break;

            case ReturnStmt ret:
                if (ret.Value != null) CheckReadsInExpr(ret.Value, uninitInBlock, context);
                break;

            case ThrowStmt throwStmt:
                if (throwStmt.Value != null) CheckReadsInExpr(throwStmt.Value, uninitInBlock, context);
                break;

            case IfStmt ifStmt:
                CheckReadsInExpr(ifStmt.Condition, uninitInBlock, context);

                // Compute what's assigned in each branch
                var thenAssigned = new HashSet<string>(definitelyAssigned);
                var thenUninit = new HashSet<string>(uninitInBlock);
                AnalyzeBlock(GetStmts(ifStmt.ThenBranch), thenAssigned, context);

                if (ifStmt.ElseBranch != null)
                {
                    var elseAssigned = new HashSet<string>(definitelyAssigned);
                    var elseUninit = new HashSet<string>(uninitInBlock);
                    AnalyzeBlock(GetStmts(ifStmt.ElseBranch), elseAssigned, context);

                    // After if/else: a variable is definitely assigned only if both branches
                    // assigned it. Intersect the two sets.
                    foreach (var name in thenAssigned)
                    {
                        if (elseAssigned.Contains(name))
                        {
                            definitelyAssigned.Add(name);
                            uninitInBlock.Remove(name);
                        }
                    }
                }
                else
                {
                    // No else: we can't guarantee anything from the then-branch
                    // (condition may have been false)
                }
                break;

            case WhileStmt whileStmt:
                CheckReadsInExpr(whileStmt.Condition, uninitInBlock, context);
                // Don't propagate loop-body assignments — the loop may not execute
                var loopAssigned = new HashSet<string>(definitelyAssigned);
                AnalyzeBlock(whileStmt.Body.Statements, loopAssigned, context);
                break;

            case DoWhileStmt doWhile:
                // do-while executes at least once, so body assignments are visible after
                var doBodyAssigned = new HashSet<string>(definitelyAssigned);
                AnalyzeBlock(doWhile.Body.Statements, doBodyAssigned, context);
                CheckReadsInExpr(doWhile.Condition, uninitInBlock, context);
                // Propagate assignments: do-while body runs at least once
                foreach (var name in doBodyAssigned)
                {
                    definitelyAssigned.Add(name);
                    uninitInBlock.Remove(name);
                }
                break;

            case ForStmt forStmt:
                if (forStmt.Initializer != null)
                    ProcessStmt(forStmt.Initializer, uninitInBlock, definitelyAssigned, context);
                if (forStmt.Condition != null)
                    CheckReadsInExpr(forStmt.Condition, uninitInBlock, context);
                var forBodyAssigned = new HashSet<string>(definitelyAssigned);
                AnalyzeBlock(forStmt.Body.Statements, forBodyAssigned, context);
                break;

            case ForInStmt forIn:
                CheckReadsInExpr(forIn.Iterable, uninitInBlock, context);
                var forInBodyAssigned = new HashSet<string>(definitelyAssigned);
                forInBodyAssigned.Add(forIn.VariableName.Lexeme);
                AnalyzeBlock(forIn.Body.Statements, forInBodyAssigned, context);
                break;

            case TryCatchStmt tryCatch:
                var tryAssigned = new HashSet<string>(definitelyAssigned);
                AnalyzeBlock(tryCatch.TryBody.Statements, tryAssigned, context);
                foreach (var clause in tryCatch.CatchClauses)
                {
                    var catchAssigned = new HashSet<string>(definitelyAssigned);
                    AnalyzeBlock(clause.Body.Statements, catchAssigned, context);
                }
                if (tryCatch.FinallyBody != null)
                {
                    AnalyzeBlock(tryCatch.FinallyBody.Statements, definitelyAssigned, context);
                }
                break;

            case FnDeclStmt fn:
                // Function bodies have their own definiteness scope — parameters are assigned
                var fnAssigned = new HashSet<string>(fn.Parameters.Select(p => p.Lexeme));
                AnalyzeBlock(fn.Body.Statements, fnAssigned, context);
                break;

            case StructDeclStmt structDecl:
                foreach (var method in structDecl.Methods)
                {
                    var methodAssigned = new HashSet<string>(method.Parameters.Select(p => p.Lexeme));
                    AnalyzeBlock(method.Body.Statements, methodAssigned, context);
                }
                break;

            case ExtendStmt extend:
                foreach (var method in extend.Methods)
                {
                    var methodAssigned = new HashSet<string>(method.Parameters.Select(p => p.Lexeme));
                    AnalyzeBlock(method.Body.Statements, methodAssigned, context);
                }
                break;
        }
    }

    private static void CheckReadsInExpr(
        Expr expr,
        HashSet<string> uninitInBlock,
        RuleContext context)
    {
        if (uninitInBlock.Count == 0) return; // fast path

        switch (expr)
        {
            case IdentifierExpr id when uninitInBlock.Contains(id.Name.Lexeme):
                context.ReportDiagnostic(
                    DiagnosticDescriptors.SA0210.CreateDiagnostic(id.Span, id.Name.Lexeme));
                // Don't report again for the same variable
                uninitInBlock.Remove(id.Name.Lexeme);
                break;
            case BinaryExpr bin:
                CheckReadsInExpr(bin.Left, uninitInBlock, context);
                CheckReadsInExpr(bin.Right, uninitInBlock, context);
                break;
            case UnaryExpr unary:
                CheckReadsInExpr(unary.Right, uninitInBlock, context);
                break;
            case CallExpr call:
                CheckReadsInExpr(call.Callee, uninitInBlock, context);
                foreach (var arg in call.Arguments)
                    CheckReadsInExpr(arg, uninitInBlock, context);
                break;
            case DotExpr dot:
                CheckReadsInExpr(dot.Object, uninitInBlock, context);
                break;
            case IndexExpr idx:
                CheckReadsInExpr(idx.Object, uninitInBlock, context);
                CheckReadsInExpr(idx.Index, uninitInBlock, context);
                break;
            case TernaryExpr ternary:
                CheckReadsInExpr(ternary.Condition, uninitInBlock, context);
                CheckReadsInExpr(ternary.ThenBranch, uninitInBlock, context);
                CheckReadsInExpr(ternary.ElseBranch, uninitInBlock, context);
                break;
            case NullCoalesceExpr nullCoalesce:
                CheckReadsInExpr(nullCoalesce.Left, uninitInBlock, context);
                CheckReadsInExpr(nullCoalesce.Right, uninitInBlock, context);
                break;
            case ArrayExpr arr:
                foreach (var el in arr.Elements)
                    CheckReadsInExpr(el, uninitInBlock, context);
                break;
            case DictLiteralExpr dict:
                foreach (var entry in dict.Entries)
                    CheckReadsInExpr(entry.Value, uninitInBlock, context);
                break;
            case SpreadExpr spread:
                CheckReadsInExpr(spread.Expression, uninitInBlock, context);
                break;
            case AssignExpr assign:
                CheckReadsInExpr(assign.Value, uninitInBlock, context);
                break;
        }
    }

    private static IReadOnlyList<Stmt> GetStmts(Stmt stmt)
        => stmt is BlockStmt block ? block.Statements : (IReadOnlyList<Stmt>)[stmt];
}
