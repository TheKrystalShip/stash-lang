namespace Stash.Analysis.Rules;

using System;
using System.Collections.Generic;
using Stash.Parsing.AST;

/// <summary>
/// SA0308 — Emits a warning when a variable declared without an initializer (<c>let x;</c>, which
/// defaults to <c>null</c>) is accessed via member access or method call without a preceding null
/// check or assignment on all paths.
/// </summary>
/// <remarks>
/// This rule is intentionally conservative: it only flags the most obvious case — a variable
/// declared as <c>let x;</c> (null by default) that is subsequently accessed via dot-access or
/// subscript without an intervening assignment.
///
/// <code>
/// let result;
/// result.name;  // SA0308 — result may be null
/// </code>
///
/// The rule does not perform inter-procedural analysis or full null-flow tracking. It is designed
/// to flag the straightforward usage of uninitialized variables, minimizing false positives.
/// </remarks>
public sealed class PossibleNullAccessRule : IAnalysisRule
{
    public DiagnosticDescriptor Descriptor => DiagnosticDescriptors.SA0308;

    /// <summary>Empty — this is a post-walk rule invoked once after the full AST walk.</summary>
    public IReadOnlySet<Type> SubscribedNodeTypes { get; } = new HashSet<Type>();

    public void Analyze(RuleContext context)
    {
        AnalyzeBlock(context.AllStatements, new HashSet<string>(), context);
    }

    private static void AnalyzeBlock(
        IReadOnlyList<Stmt> stmts,
        HashSet<string> maybeNull,
        RuleContext context)
    {
        foreach (var stmt in stmts)
        {
            ProcessStmt(stmt, maybeNull, context);
        }
    }

    private static void ProcessStmt(
        Stmt stmt,
        HashSet<string> maybeNull,
        RuleContext context)
    {
        switch (stmt)
        {
            case VarDeclStmt varDecl:
                if (varDecl.Initializer == null)
                {
                    // let x; → null by default
                    maybeNull.Add(varDecl.Name.Lexeme);
                }
                else
                {
                    CheckExprForNullAccess(varDecl.Initializer, maybeNull, context);
                    maybeNull.Remove(varDecl.Name.Lexeme);
                }
                break;

            case ExprStmt exprStmt:
                if (exprStmt.Expression is AssignExpr assign)
                {
                    CheckExprForNullAccess(assign.Value, maybeNull, context);
                    // After assignment, the variable is no longer definitely null
                    maybeNull.Remove(assign.Name.Lexeme);
                }
                else
                {
                    CheckExprForNullAccess(exprStmt.Expression, maybeNull, context);
                }
                break;

            case ReturnStmt ret:
                if (ret.Value != null) CheckExprForNullAccess(ret.Value, maybeNull, context);
                break;

            case ThrowStmt throwStmt:
                if (throwStmt.Value != null) CheckExprForNullAccess(throwStmt.Value, maybeNull, context);
                break;

            case IfStmt ifStmt:
                CheckExprForNullAccess(ifStmt.Condition, maybeNull, context);

                // Check for null guard: `if (x != null)` or `if (x)` → remove x from maybeNull
                // in the then-branch
                var thenMaybeNull = new HashSet<string>(maybeNull);
                ApplyNullNarrowing(ifStmt.Condition, thenMaybeNull);
                AnalyzeBlock(GetStmts(ifStmt.ThenBranch), thenMaybeNull, context);

                if (ifStmt.ElseBranch != null)
                {
                    var elseMaybeNull = new HashSet<string>(maybeNull);
                    AnalyzeBlock(GetStmts(ifStmt.ElseBranch), elseMaybeNull, context);
                }
                break;

            case WhileStmt whileStmt:
                CheckExprForNullAccess(whileStmt.Condition, maybeNull, context);
                AnalyzeBlock(whileStmt.Body.Statements, new HashSet<string>(maybeNull), context);
                break;

            case DoWhileStmt doWhile:
                AnalyzeBlock(doWhile.Body.Statements, new HashSet<string>(maybeNull), context);
                CheckExprForNullAccess(doWhile.Condition, maybeNull, context);
                break;

            case ForStmt forStmt:
                if (forStmt.Initializer != null)
                    ProcessStmt(forStmt.Initializer, maybeNull, context);
                if (forStmt.Condition != null)
                    CheckExprForNullAccess(forStmt.Condition, maybeNull, context);
                AnalyzeBlock(forStmt.Body.Statements, new HashSet<string>(maybeNull), context);
                break;

            case ForInStmt forIn:
                CheckExprForNullAccess(forIn.Iterable, maybeNull, context);
                var forInNull = new HashSet<string>(maybeNull);
                forInNull.Remove(forIn.VariableName.Lexeme);
                AnalyzeBlock(forIn.Body.Statements, forInNull, context);
                break;

            case TryCatchStmt tryCatch:
                AnalyzeBlock(tryCatch.TryBody.Statements, new HashSet<string>(maybeNull), context);
                foreach (var clause in tryCatch.CatchClauses)
                    AnalyzeBlock(clause.Body.Statements, new HashSet<string>(maybeNull), context);
                if (tryCatch.FinallyBody != null)
                    AnalyzeBlock(tryCatch.FinallyBody.Statements, new HashSet<string>(maybeNull), context);
                break;

            case FnDeclStmt fn:
                // Function bodies have their own null-state tracking (parameters are not null)
                AnalyzeBlock(fn.Body.Statements, new HashSet<string>(), context);
                break;

            case StructDeclStmt structDecl:
                foreach (var method in structDecl.Methods)
                    AnalyzeBlock(method.Body.Statements, new HashSet<string>(), context);
                break;

            case ExtendStmt extend:
                foreach (var method in extend.Methods)
                    AnalyzeBlock(method.Body.Statements, new HashSet<string>(), context);
                break;
        }
    }

    // ── Null narrowing ──────────────────────────────────────────────────────

    /// <summary>
    /// Removes variables from <paramref name="maybeNull"/> based on null-guard conditions.
    /// Handles: <c>x != null</c>, <c>x !== null</c>, <c>x</c> (truthiness).
    /// </summary>
    private static void ApplyNullNarrowing(Expr condition, HashSet<string> maybeNull)
    {
        if (condition is IdentifierExpr id)
        {
            // `if (x)` — x is truthy, so not null
            maybeNull.Remove(id.Name.Lexeme);
        }
        else if (condition is BinaryExpr bin)
        {
            var op = bin.Operator.Type;
            if (op is Stash.Lexing.TokenType.BangEqual)
            {
                // `x != null`
                if (bin.Left is IdentifierExpr left && bin.Right is LiteralExpr { Value: null })
                    maybeNull.Remove(left.Name.Lexeme);
                else if (bin.Right is IdentifierExpr right && bin.Left is LiteralExpr { Value: null })
                    maybeNull.Remove(right.Name.Lexeme);
            }
        }
    }

    // ── Expression traversal ────────────────────────────────────────────────

    private static void CheckExprForNullAccess(
        Expr expr,
        HashSet<string> maybeNull,
        RuleContext context)
    {
        if (maybeNull.Count == 0) return;

        switch (expr)
        {
            case DotExpr dot when dot.Object is IdentifierExpr id && maybeNull.Contains(id.Name.Lexeme):
            {
                // `maybeNullVar.member` → SA0308
                context.ReportDiagnostic(
                    DiagnosticDescriptors.SA0308.CreateDiagnostic(dot.Object.Span, id.Name.Lexeme));
                // Remove to avoid repeated reports on the same variable
                maybeNull.Remove(id.Name.Lexeme);
                break;
            }
            case IndexExpr idx when idx.Object is IdentifierExpr id && maybeNull.Contains(id.Name.Lexeme):
            {
                context.ReportDiagnostic(
                    DiagnosticDescriptors.SA0308.CreateDiagnostic(idx.Object.Span, id.Name.Lexeme));
                maybeNull.Remove(id.Name.Lexeme);
                break;
            }
            case CallExpr call when call.Callee is DotExpr dot2 &&
                                     dot2.Object is IdentifierExpr idCall &&
                                     maybeNull.Contains(idCall.Name.Lexeme):
            {
                context.ReportDiagnostic(
                    DiagnosticDescriptors.SA0308.CreateDiagnostic(dot2.Object.Span, idCall.Name.Lexeme));
                maybeNull.Remove(idCall.Name.Lexeme);
                break;
            }
            default:
                // Recurse into sub-expressions
                WalkExpr(expr, maybeNull, context);
                break;
        }
    }

    private static void WalkExpr(Expr expr, HashSet<string> maybeNull, RuleContext context)
    {
        switch (expr)
        {
            case BinaryExpr bin:
                CheckExprForNullAccess(bin.Left, maybeNull, context);
                CheckExprForNullAccess(bin.Right, maybeNull, context);
                break;
            case UnaryExpr unary:
                CheckExprForNullAccess(unary.Right, maybeNull, context);
                break;
            case CallExpr call:
                CheckExprForNullAccess(call.Callee, maybeNull, context);
                foreach (var arg in call.Arguments)
                    CheckExprForNullAccess(arg, maybeNull, context);
                break;
            case DotExpr dot:
                CheckExprForNullAccess(dot.Object, maybeNull, context);
                break;
            case IndexExpr idx:
                CheckExprForNullAccess(idx.Object, maybeNull, context);
                CheckExprForNullAccess(idx.Index, maybeNull, context);
                break;
            case TernaryExpr ternary:
                CheckExprForNullAccess(ternary.Condition, maybeNull, context);
                CheckExprForNullAccess(ternary.ThenBranch, maybeNull, context);
                CheckExprForNullAccess(ternary.ElseBranch, maybeNull, context);
                break;
            case NullCoalesceExpr nullCoalesce:
                CheckExprForNullAccess(nullCoalesce.Left, maybeNull, context);
                CheckExprForNullAccess(nullCoalesce.Right, maybeNull, context);
                break;
            case AssignExpr assign:
                CheckExprForNullAccess(assign.Value, maybeNull, context);
                break;
            case ArrayExpr arr:
                foreach (var el in arr.Elements)
                    CheckExprForNullAccess(el, maybeNull, context);
                break;
        }
    }

    private static IReadOnlyList<Stmt> GetStmts(Stmt stmt)
        => stmt is BlockStmt block ? block.Statements : (IReadOnlyList<Stmt>)[stmt];
}
