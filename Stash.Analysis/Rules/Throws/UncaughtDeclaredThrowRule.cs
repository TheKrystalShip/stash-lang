namespace Stash.Analysis.Rules.Throws;

using System;
using System.Collections.Generic;
using Stash.Common;
using Stash.Parsing.AST;
using Stash.Stdlib;
using Stash.Stdlib.Models;

/// <summary>
/// SA0164 — Emits a warning when a <c>try</c> block calls a function whose declared throws
/// are not covered by any catch clause and there is no catch-all.
/// </summary>
/// <remarks>
/// Default-disabled. Enable via <c>enable=SA0164</c> in a <c>.stashcheck</c> file.
/// Only fires when the called function has explicit throws metadata (<c>@throws</c> or
/// <c>[StashFn(Throws=...)]</c>). Functions without metadata never trigger this rule.
/// </remarks>
public sealed class UncaughtDeclaredThrowRule : IAnalysisRule
{
    public DiagnosticDescriptor Descriptor => DiagnosticDescriptors.SA0164;

    public IReadOnlySet<Type> SubscribedNodeTypes { get; } = new HashSet<Type>
    {
        typeof(TryCatchStmt)
    };

    /// <summary>
    /// Internal hook for unit tests: when non-null, this delegate is consulted BEFORE
    /// <see cref="StdlibRegistry.TryGetNamespaceFunction"/> for stdlib call resolution.
    /// Set to null to restore production behaviour.
    /// </summary>
    internal static Func<string, NamespaceFunction?>? TestStdlibLookup;

    public void Analyze(RuleContext context)
    {
        if (context.Statement is not TryCatchStmt tryCatch)
            return;

        // If any catch is a catch-all (untyped or "Error" base type), suppress all diagnostics.
        foreach (var clause in tryCatch.CatchClauses)
        {
            if (clause.IsCatchAll)
                return;
        }

        // Build the set of error types covered by existing catch clauses.
        var coveredTypes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var clause in tryCatch.CatchClauses)
        {
            foreach (var typeExpression in clause.CatchTypes)
                coveredTypes.Add(typeExpression.ToCanonicalString());
        }

        // Walk the try body, collecting declared throws from every reachable call
        // (but NOT recursing into nested TryCatchStmt or TryExpr bodies).
        var declaredThrows = new List<(string FnName, string ErrorType, SourceSpan Span)>();
        CollectThrowsFromBlock(tryCatch.TryBody, context.ScopeTree, declaredThrows);

        // Emit one diagnostic per unique (fn, type) pair that isn't covered.
        var reported = new HashSet<(string, string)>();
        foreach (var (fnName, errorType, span) in declaredThrows)
        {
            if (!coveredTypes.Contains(errorType) && reported.Add((fnName, errorType)))
            {
                context.ReportDiagnostic(DiagnosticDescriptors.SA0164.CreateDiagnostic(
                    span, fnName, errorType));
            }
        }
    }

    // ── Block / statement walkers ─────────────────────────────────────────────────

    private static void CollectThrowsFromBlock(
        BlockStmt block,
        ScopeTree scopeTree,
        List<(string, string, SourceSpan)> results)
    {
        foreach (var stmt in block.Statements)
            CollectThrowsFromStmt(stmt, scopeTree, results);
    }

    private static void CollectThrowsFromStmt(
        Stmt stmt,
        ScopeTree scopeTree,
        List<(string, string, SourceSpan)> results)
    {
        switch (stmt)
        {
            case TryCatchStmt:
                // Nested try is a sealed unit — inner coverage is its own responsibility.
                return;

            case ExprStmt exprStmt:
                CollectThrowsFromExpr(exprStmt.Expression, scopeTree, results);
                break;

            case VarDeclStmt varDecl when varDecl.Initializer != null:
                CollectThrowsFromExpr(varDecl.Initializer, scopeTree, results);
                break;

            case ConstDeclStmt constDecl:
                CollectThrowsFromExpr(constDecl.Initializer, scopeTree, results);
                break;

            case ReturnStmt ret when ret.Value != null:
                CollectThrowsFromExpr(ret.Value, scopeTree, results);
                break;

            case IfStmt ifStmt:
                CollectThrowsFromExpr(ifStmt.Condition, scopeTree, results);
                CollectThrowsFromBranch(ifStmt.ThenBranch, scopeTree, results);
                if (ifStmt.ElseBranch != null)
                    CollectThrowsFromBranch(ifStmt.ElseBranch, scopeTree, results);
                break;

            case BlockStmt block:
                CollectThrowsFromBlock(block, scopeTree, results);
                break;

            case WhileStmt whileStmt:
                CollectThrowsFromExpr(whileStmt.Condition, scopeTree, results);
                CollectThrowsFromBlock(whileStmt.Body, scopeTree, results);
                break;

            case DoWhileStmt doWhile:
                CollectThrowsFromBlock(doWhile.Body, scopeTree, results);
                CollectThrowsFromExpr(doWhile.Condition, scopeTree, results);
                break;

            case ForStmt forStmt:
                if (forStmt.Initializer != null)
                    CollectThrowsFromStmt(forStmt.Initializer, scopeTree, results);
                if (forStmt.Condition != null)
                    CollectThrowsFromExpr(forStmt.Condition, scopeTree, results);
                if (forStmt.Increment != null)
                    CollectThrowsFromExpr(forStmt.Increment, scopeTree, results);
                CollectThrowsFromBlock(forStmt.Body, scopeTree, results);
                break;

            case ForInStmt forIn:
                CollectThrowsFromExpr(forIn.Iterable, scopeTree, results);
                CollectThrowsFromBlock(forIn.Body, scopeTree, results);
                break;

            case SwitchStmt switchStmt:
                CollectThrowsFromExpr(switchStmt.Subject, scopeTree, results);
                foreach (var c in switchStmt.Cases)
                {
                    foreach (var pattern in c.Patterns)
                        CollectThrowsFromExpr(pattern, scopeTree, results);
                    CollectThrowsFromStmt(c.Body, scopeTree, results);
                }
                break;

            case ThrowStmt throwStmt when throwStmt.Value != null:
                CollectThrowsFromExpr(throwStmt.Value, scopeTree, results);
                break;

            case LockStmt lockStmt:
                CollectThrowsFromBlock(lockStmt.Body, scopeTree, results);
                break;

            case DeferStmt deferStmt:
                CollectThrowsFromStmt(deferStmt.Body, scopeTree, results);
                break;
        }
    }

    private static void CollectThrowsFromBranch(
        Stmt branch,
        ScopeTree scopeTree,
        List<(string, string, SourceSpan)> results)
    {
        if (branch is BlockStmt block)
            CollectThrowsFromBlock(block, scopeTree, results);
        else
            CollectThrowsFromStmt(branch, scopeTree, results);
    }

    // ── Expression walker ─────────────────────────────────────────────────────────

    private static void CollectThrowsFromExpr(
        Expr expr,
        ScopeTree scopeTree,
        List<(string, string, SourceSpan)> results)
    {
        // TryExpr (try expr ?? default) is a universal catch-all — never recurse into it.
        if (expr is TryExpr)
            return;

        if (expr is CallExpr call)
        {
            ResolveCallThrows(call, scopeTree, results);

            // Also recurse into call arguments — they may themselves be calls.
            foreach (var arg in call.Arguments)
                CollectThrowsFromExpr(arg, scopeTree, results);
        }
        else
        {
            RecurseSubExprs(expr, scopeTree, results);
        }
    }

    private static void ResolveCallThrows(
        CallExpr call,
        ScopeTree scopeTree,
        List<(string, string, SourceSpan)> results)
    {
        // Stdlib call: ns.fn(…)
        if (call.Callee is DotExpr dot &&
            dot.Object is IdentifierExpr nsId &&
            StdlibRegistry.IsBuiltInNamespace(nsId.Name.Lexeme))
        {
            var qualName = $"{nsId.Name.Lexeme}.{dot.Name.Lexeme}";

            // Check test hook first (allows tests to inject throws metadata without needing
            // real stdlib functions to be tagged — Wave 1 tagging is Phase 4).
            NamespaceFunction? nsFn = TestStdlibLookup?.Invoke(qualName);

            if (nsFn == null)
            {
                if (StdlibRegistry.TryGetNamespaceFunction(qualName, out var registryFn))
                    nsFn = registryFn;
            }

            if (nsFn?.Throws != null)
            {
                foreach (var t in nsFn.Throws)
                    results.Add((qualName, t.ErrorType, call.Span));
            }

            return;
        }

        // User function call: fn(…)
        if (call.Callee is IdentifierExpr fnId)
        {
            var def = scopeTree.FindDefinition(
                fnId.Name.Lexeme, fnId.Span.StartLine, fnId.Span.StartColumn);
            if (def?.Throws != null)
            {
                foreach (var t in def.Throws)
                    results.Add((fnId.Name.Lexeme, t.ErrorType, call.Span));
            }
        }
    }

    private static void RecurseSubExprs(
        Expr expr,
        ScopeTree scopeTree,
        List<(string, string, SourceSpan)> results)
    {
        switch (expr)
        {
            case BinaryExpr bin:
                CollectThrowsFromExpr(bin.Left, scopeTree, results);
                CollectThrowsFromExpr(bin.Right, scopeTree, results);
                break;

            case UnaryExpr un:
                CollectThrowsFromExpr(un.Right, scopeTree, results);
                break;

            case GroupingExpr group:
                CollectThrowsFromExpr(group.Expression, scopeTree, results);
                break;

            case TernaryExpr tern:
                CollectThrowsFromExpr(tern.Condition, scopeTree, results);
                CollectThrowsFromExpr(tern.ThenBranch, scopeTree, results);
                CollectThrowsFromExpr(tern.ElseBranch, scopeTree, results);
                break;

            case DotExpr dotExpr:
                CollectThrowsFromExpr(dotExpr.Object, scopeTree, results);
                break;

            case NullCoalesceExpr coalesce:
                CollectThrowsFromExpr(coalesce.Left, scopeTree, results);
                CollectThrowsFromExpr(coalesce.Right, scopeTree, results);
                break;

            case ArrayExpr arr:
                foreach (var elem in arr.Elements)
                    CollectThrowsFromExpr(elem, scopeTree, results);
                break;

            case InterpolatedStringExpr interp:
                foreach (var part in interp.Parts)
                    CollectThrowsFromExpr(part, scopeTree, results);
                break;
        }
    }
}
