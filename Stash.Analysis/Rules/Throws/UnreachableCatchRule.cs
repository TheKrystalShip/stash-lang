namespace Stash.Analysis.Rules.Throws;

using System;
using System.Collections.Generic;
using Stash.Common;
using Stash.Parsing.AST;
using Stash.Stdlib;
using Stash.Stdlib.Models;

/// <summary>
/// SA0169 — Emits an info diagnostic when a catch clause covers an error type that no call
/// inside the try body declares in its throws metadata (dead catch clause).
/// </summary>
/// <remarks>
/// Default-disabled. Enable via <c>enable=SA0169</c> in a <c>.stashcheck</c> file.
/// Only fires when the try body has at least one call with explicit throws metadata.
/// Functions without metadata never contribute to the union, so catch clauses for their
/// errors are never flagged (silent-fallback posture matching SA0164).
/// </remarks>
public sealed class UnreachableCatchRule : IAnalysisRule
{
    public DiagnosticDescriptor Descriptor => DiagnosticDescriptors.SA0169;

    public IReadOnlySet<Type> SubscribedNodeTypes { get; } = new HashSet<Type>
    {
        typeof(TryCatchStmt)
    };

    /// <summary>
    /// Internal hook for unit tests: when non-null, consulted BEFORE
    /// <see cref="StdlibRegistry.TryGetNamespaceFunction"/> for stdlib call resolution.
    /// </summary>
    internal static Func<string, NamespaceFunction?>? TestStdlibLookup;

    public void Analyze(RuleContext context)
    {
        if (context.Statement is not TryCatchStmt tryCatch)
            return;

        // Collect the union of all throw types declared by calls in the try body.
        var declaredUnion = new HashSet<string>(StringComparer.Ordinal);
        CollectThrowsFromBlock(tryCatch.TryBody, context.ScopeTree, declaredUnion);

        // If no call has any throws metadata, we have no information — stay silent.
        if (declaredUnion.Count == 0)
            return;

        // Walk catch clauses in order. Once we reach a catch-all, all subsequent clauses
        // are already unreachable (SA0161 covers that) — stop here.
        foreach (var clause in tryCatch.CatchClauses)
        {
            // catch (Error e) and untyped catch-all are universal — skip analysis.
            if (clause.IsCatchAll)
                break;

            // For a typed clause (possibly union: catch (A | B e)), fire SA0169 for each
            // type that is NOT present in the declared union.
            // If at least one type IS present, the clause is reachable — skip those types.
            // We only fire for types that are completely absent.
            foreach (var typeToken in clause.TypeTokens)
            {
                if (!declaredUnion.Contains(typeToken.Lexeme))
                {
                    context.ReportDiagnostic(DiagnosticDescriptors.SA0169.CreateDiagnostic(
                        clause.Span, typeToken.Lexeme));
                }
            }
        }
    }

    // ── Block / statement walkers (mirrors UncaughtDeclaredThrowRule) ─────────

    private static void CollectThrowsFromBlock(
        BlockStmt block,
        ScopeTree scopeTree,
        HashSet<string> union)
    {
        foreach (var stmt in block.Statements)
            CollectThrowsFromStmt(stmt, scopeTree, union);
    }

    private static void CollectThrowsFromStmt(
        Stmt stmt,
        ScopeTree scopeTree,
        HashSet<string> union)
    {
        switch (stmt)
        {
            case TryCatchStmt:
                // Nested try is a sealed unit — don't recurse into it.
                return;

            case ExprStmt exprStmt:
                CollectThrowsFromExpr(exprStmt.Expression, scopeTree, union);
                break;

            case VarDeclStmt varDecl when varDecl.Initializer != null:
                CollectThrowsFromExpr(varDecl.Initializer, scopeTree, union);
                break;

            case ConstDeclStmt constDecl:
                CollectThrowsFromExpr(constDecl.Initializer, scopeTree, union);
                break;

            case ReturnStmt ret when ret.Value != null:
                CollectThrowsFromExpr(ret.Value, scopeTree, union);
                break;

            case IfStmt ifStmt:
                CollectThrowsFromExpr(ifStmt.Condition, scopeTree, union);
                CollectThrowsFromBranch(ifStmt.ThenBranch, scopeTree, union);
                if (ifStmt.ElseBranch != null)
                    CollectThrowsFromBranch(ifStmt.ElseBranch, scopeTree, union);
                break;

            case BlockStmt block:
                CollectThrowsFromBlock(block, scopeTree, union);
                break;
        }
    }

    private static void CollectThrowsFromBranch(
        Stmt branch,
        ScopeTree scopeTree,
        HashSet<string> union)
    {
        if (branch is BlockStmt block)
            CollectThrowsFromBlock(block, scopeTree, union);
        else
            CollectThrowsFromStmt(branch, scopeTree, union);
    }

    // ── Expression walker ─────────────────────────────────────────────────────

    private static void CollectThrowsFromExpr(
        Expr expr,
        ScopeTree scopeTree,
        HashSet<string> union)
    {
        // TryExpr (try expr ?? default) is a universal catch-all — never recurse into it.
        if (expr is TryExpr)
            return;

        if (expr is CallExpr call)
        {
            ResolveCallThrows(call, scopeTree, union);

            foreach (var arg in call.Arguments)
                CollectThrowsFromExpr(arg, scopeTree, union);
        }
        else
        {
            RecurseSubExprs(expr, scopeTree, union);
        }
    }

    private static void ResolveCallThrows(
        CallExpr call,
        ScopeTree scopeTree,
        HashSet<string> union)
    {
        // Stdlib call: ns.fn(…)
        if (call.Callee is DotExpr dot &&
            dot.Object is IdentifierExpr nsId &&
            StdlibRegistry.IsBuiltInNamespace(nsId.Name.Lexeme))
        {
            var qualName = $"{nsId.Name.Lexeme}.{dot.Name.Lexeme}";

            NamespaceFunction? nsFn = TestStdlibLookup?.Invoke(qualName);
            if (nsFn == null)
            {
                if (StdlibRegistry.TryGetNamespaceFunction(qualName, out var registryFn))
                    nsFn = registryFn;
            }

            if (nsFn?.Throws != null)
            {
                foreach (var t in nsFn.Throws)
                    union.Add(t.ErrorType);
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
                    union.Add(t.ErrorType);
            }
        }
    }

    private static void RecurseSubExprs(
        Expr expr,
        ScopeTree scopeTree,
        HashSet<string> union)
    {
        switch (expr)
        {
            case BinaryExpr bin:
                CollectThrowsFromExpr(bin.Left, scopeTree, union);
                CollectThrowsFromExpr(bin.Right, scopeTree, union);
                break;

            case UnaryExpr un:
                CollectThrowsFromExpr(un.Right, scopeTree, union);
                break;

            case GroupingExpr group:
                CollectThrowsFromExpr(group.Expression, scopeTree, union);
                break;

            case TernaryExpr tern:
                CollectThrowsFromExpr(tern.Condition, scopeTree, union);
                CollectThrowsFromExpr(tern.ThenBranch, scopeTree, union);
                CollectThrowsFromExpr(tern.ElseBranch, scopeTree, union);
                break;

            case DotExpr dotExpr:
                CollectThrowsFromExpr(dotExpr.Object, scopeTree, union);
                break;

            case NullCoalesceExpr coalesce:
                CollectThrowsFromExpr(coalesce.Left, scopeTree, union);
                CollectThrowsFromExpr(coalesce.Right, scopeTree, union);
                break;

            case ArrayExpr arr:
                foreach (var elem in arr.Elements)
                    CollectThrowsFromExpr(elem, scopeTree, union);
                break;

            case InterpolatedStringExpr interp:
                foreach (var part in interp.Parts)
                    CollectThrowsFromExpr(part, scopeTree, union);
                break;
        }
    }
}
