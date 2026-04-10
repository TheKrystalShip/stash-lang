namespace Stash.Analysis.Rules;

using System;
using System.Collections.Generic;
using Stash.Parsing.AST;

/// <summary>
/// SA0208 — Emits an information diagnostic when a local variable is assigned a value that
/// is immediately overwritten before the value is ever read (a "dead store").
/// </summary>
/// <remarks>
/// This rule performs a simple forward pass within each flat statement sequence. For each
/// assignment, it records the assignment site and clears it when the variable is subsequently
/// read. If a second assignment to the same variable is encountered before a read, the first
/// assignment is flagged as a dead store.
///
/// The analysis is intentionally conservative: it only tracks assignments within a single
/// sequential statement block and does not attempt cross-branch data-flow. This avoids
/// false positives in branching code while still catching the common pattern:
/// <code>
/// let x = compute();   // SA0208 – value never read
/// x = something_else();
/// use(x);
/// </code>
/// </remarks>
public sealed class DeadStoreRule : IAnalysisRule
{
    public DiagnosticDescriptor Descriptor => DiagnosticDescriptors.SA0208;

    /// <summary>Empty — this is a post-walk rule invoked once after the full AST walk.</summary>
    public IReadOnlySet<Type> SubscribedNodeTypes { get; } = new HashSet<Type>();

    public void Analyze(RuleContext context)
    {
        AnalyzeBlock(context.AllStatements, context);
    }

    private static void AnalyzeBlock(IReadOnlyList<Stmt> stmts, RuleContext context)
    {
        // pendingAssign: varName → (assign span, is-decl)
        // We only flag assignment-then-overwrite within the same sequential block.
        var pendingAssign = new Dictionary<string, (Stash.Common.SourceSpan AssignSpan, bool IsDecl)>();

        foreach (var stmt in stmts)
        {
            // Collect reads from this statement first (reads happen before the assignment in
            // the expression `x = x + 1`, but AST represents RHS first)
            CollectReads(stmt, pendingAssign);

            // Then look at what is written by this statement
            ProcessWrites(stmt, pendingAssign, context);

            // Recurse into nested bodies
            RecurseNested(stmt, context);
        }
        // End of block: any pending assigns that were never read are NOT reported here —
        // the unused-declaration rule (SA0201) handles that case.
    }

    private static void CollectReads(Stmt stmt, Dictionary<string, (Stash.Common.SourceSpan AssignSpan, bool IsDecl)> pending)
    {
        // Walk expressions in this statement and clear any pending assigns for read variables
        switch (stmt)
        {
            case VarDeclStmt varDecl when varDecl.Initializer != null:
                ClearReadsInExpr(varDecl.Initializer, pending);
                break;
            case ConstDeclStmt constDecl:
                ClearReadsInExpr(constDecl.Initializer, pending);
                break;
            case ExprStmt exprStmt:
                // For assign expr, the RHS is read before the LHS is written
                if (exprStmt.Expression is AssignExpr assign)
                {
                    ClearReadsInExpr(assign.Value, pending);
                    // The LHS target itself is not a "read" here
                }
                else
                {
                    ClearReadsInExpr(exprStmt.Expression, pending);
                }
                break;
            case ReturnStmt ret when ret.Value != null:
                ClearReadsInExpr(ret.Value, pending);
                break;
            case ThrowStmt throwStmt:
                ClearReadsInExpr(throwStmt.Value, pending);
                break;
            case IfStmt ifStmt:
                // The condition is read — clear any pending assigns referenced there
                ClearReadsInExpr(ifStmt.Condition, pending);
                // Don't descend into branches — that's handled by RecurseNested
                break;
            case WhileStmt whileStmt:
                ClearReadsInExpr(whileStmt.Condition, pending);
                break;
            case DoWhileStmt doWhile:
                ClearReadsInExpr(doWhile.Condition, pending);
                break;
            case ForStmt forStmt when forStmt.Condition != null:
                ClearReadsInExpr(forStmt.Condition, pending);
                break;
            case ForInStmt forIn:
                ClearReadsInExpr(forIn.Iterable, pending);
                break;
        }
    }

    private static void ProcessWrites(
        Stmt stmt,
        Dictionary<string, (Stash.Common.SourceSpan AssignSpan, bool IsDecl)> pending,
        RuleContext context)
    {
        switch (stmt)
        {
            case VarDeclStmt varDecl:
                // let x = expr — records a new assignment; no prior pending to flag
                // (SA0201 handles truly-unused variables)
                pending[varDecl.Name.Lexeme] = (varDecl.Span, IsDecl: true);
                break;

            case ExprStmt exprStmt when exprStmt.Expression is AssignExpr assign:
            {
                var name = assign.Name.Lexeme;
                if (pending.TryGetValue(name, out var prior) && !prior.IsDecl)
                {
                    // A prior pure assignment (not a declaration) was overwritten before being read
                    context.ReportDiagnostic(
                        DiagnosticDescriptors.SA0208.CreateDiagnostic(prior.AssignSpan, name));
                }
                pending[name] = (assign.Span, IsDecl: false);
                break;
            }
        }
    }

    private static void RecurseNested(Stmt stmt, RuleContext context)
    {
        switch (stmt)
        {
            case FnDeclStmt fn:
                AnalyzeBlock(fn.Body.Statements, context);
                break;
            case IfStmt ifStmt:
                AnalyzeBlock(GetStmts(ifStmt.ThenBranch), context);
                if (ifStmt.ElseBranch != null)
                    AnalyzeBlock(GetStmts(ifStmt.ElseBranch), context);
                break;
            case WhileStmt @while:
                AnalyzeBlock(@while.Body.Statements, context);
                break;
            case DoWhileStmt doWhile:
                AnalyzeBlock(doWhile.Body.Statements, context);
                break;
            case ForStmt @for:
                AnalyzeBlock(@for.Body.Statements, context);
                break;
            case ForInStmt forIn:
                AnalyzeBlock(forIn.Body.Statements, context);
                break;
            case TryCatchStmt tryCatch:
                AnalyzeBlock(tryCatch.TryBody.Statements, context);
                if (tryCatch.CatchBody != null)
                    AnalyzeBlock(tryCatch.CatchBody.Statements, context);
                if (tryCatch.FinallyBody != null)
                    AnalyzeBlock(tryCatch.FinallyBody.Statements, context);
                break;
            case BlockStmt block:
                AnalyzeBlock(block.Statements, context);
                break;
            case StructDeclStmt structDecl:
                foreach (var method in structDecl.Methods)
                    AnalyzeBlock(method.Body.Statements, context);
                break;
            case ExtendStmt extend:
                foreach (var method in extend.Methods)
                    AnalyzeBlock(method.Body.Statements, context);
                break;
        }
    }

    private static void ClearReadsInExpr(
        Expr expr,
        Dictionary<string, (Stash.Common.SourceSpan AssignSpan, bool IsDecl)> pending)
    {
        // Walk the expression tree and remove any variables that are read
        switch (expr)
        {
            case IdentifierExpr id:
                pending.Remove(id.Name.Lexeme);
                break;
            case BinaryExpr bin:
                ClearReadsInExpr(bin.Left, pending);
                ClearReadsInExpr(bin.Right, pending);
                break;
            case UnaryExpr unary:
                ClearReadsInExpr(unary.Right, pending);
                break;
            case CallExpr call:
                ClearReadsInExpr(call.Callee, pending);
                foreach (var arg in call.Arguments)
                    ClearReadsInExpr(arg, pending);
                break;
            case DotExpr dot:
                ClearReadsInExpr(dot.Object, pending);
                break;
            case IndexExpr idx:
                ClearReadsInExpr(idx.Object, pending);
                ClearReadsInExpr(idx.Index, pending);
                break;
            case AssignExpr assign:
                ClearReadsInExpr(assign.Value, pending);
                break;
            case TernaryExpr ternary:
                ClearReadsInExpr(ternary.Condition, pending);
                ClearReadsInExpr(ternary.ThenBranch, pending);
                ClearReadsInExpr(ternary.ElseBranch, pending);
                break;
            case ArrayExpr arr:
                foreach (var el in arr.Elements)
                    ClearReadsInExpr(el, pending);
                break;
            case DictLiteralExpr dict:
                foreach (var entry in dict.Entries)
                    ClearReadsInExpr(entry.Value, pending);
                break;
            case SpreadExpr spread:
                ClearReadsInExpr(spread.Expression, pending);
                break;
            case NullCoalesceExpr nullCoalesce:
                ClearReadsInExpr(nullCoalesce.Left, pending);
                ClearReadsInExpr(nullCoalesce.Right, pending);
                break;
            case LambdaExpr:
                // Don't descend into lambdas — they capture variables but have their own scope
                break;
        }
    }

    private static IReadOnlyList<Stmt> GetStmts(Stmt stmt)
        => stmt is BlockStmt block ? block.Statements : (IReadOnlyList<Stmt>)[stmt];
}
