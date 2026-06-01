namespace Stash.Analysis.Rules;

using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using Stash.Parsing.AST;

/// <summary>
/// SA0847 — Best-effort static diagnostic for direct mutations of a known-<c>readonly</c> binding.
/// </summary>
/// <remarks>
/// <para>
/// Fires on three patterns when the receiver/target is a bare identifier that resolves to a
/// <c>readonly let</c> or <c>readonly const</c> declaration in the visible scope:
/// <list type="bullet">
///   <item><b>Field assignment</b> — <c>D.x = …</c> (a <see cref="DotAssignExpr"/> whose
///     <c>Object</c> is an <see cref="IdentifierExpr"/> pointing at a readonly binding).</item>
///   <item><b>Index assignment</b> — <c>D[i] = …</c> (an <see cref="IndexAssignExpr"/> whose
///     <c>Object</c> is an <see cref="IdentifierExpr"/> pointing at a readonly binding).</item>
///   <item><b>Known in-place stdlib mutator</b> — e.g. <c>arr.push(D, …)</c>, where the
///     callee is a known-mutating namespace function and the first argument is a bare
///     identifier resolving to a readonly binding.</item>
/// </list>
/// </para>
/// <para>
/// This is <b>best-effort</b> — aliasing (<c>let a = D; a.x = …</c>) is intentionally
/// <em>not</em> diagnosed here; the runtime deep-freeze flag catches it.  Reporting on
/// aliases would require tracking initializer flow, which is beyond the scope of this rule.
/// Only direct identifiers are checked, so there are no false positives on alias bindings.
/// </para>
/// </remarks>
public sealed class ReadOnlyMutationRule : IAnalysisRule
{
    public DiagnosticDescriptor Descriptor => DiagnosticDescriptors.SA0847;

    public IReadOnlySet<Type> SubscribedNodeTypes { get; } = new HashSet<Type>
    {
        typeof(DotAssignExpr),
        typeof(IndexAssignExpr),
        typeof(CallExpr),
    };

    /// <summary>
    /// The set of qualified namespace-function names that mutate their first argument
    /// in place.  Defined as a static bounded constant so the known-mutator set has a
    /// single source of truth.  Parity with the runtime: these are exactly the stdlib
    /// functions whose implementation throws <c>ReadOnlyError</c> when the array/dict
    /// is frozen.
    /// </summary>
    private static readonly FrozenSet<string> KnownInPlaceMutators = FrozenSet.ToFrozenSet([
        // arr namespace — all mutators that throw ReadOnlyError when frozen
        "arr.push",
        "arr.pop",
        "arr.insert",
        "arr.removeAt",
        "arr.remove",
        "arr.clear",
        "arr.reverse",
        "arr.sort",
        "arr.shuffle",
        // dict namespace — all mutators that throw ReadOnlyError when frozen
        "dict.set",
        "dict.remove",
        "dict.clear",
    ]);

    public void Analyze(RuleContext context)
    {
        switch (context.Expression)
        {
            case DotAssignExpr dotAssign:
                AnalyzeDotAssign(context, dotAssign);
                break;

            case IndexAssignExpr indexAssign:
                AnalyzeIndexAssign(context, indexAssign);
                break;

            case CallExpr call:
                AnalyzeCall(context, call);
                break;
        }
    }

    // ── DotAssignExpr: D.x = … ────────────────────────────────────────────────

    private static void AnalyzeDotAssign(RuleContext context, DotAssignExpr expr)
    {
        if (expr.Object is not IdentifierExpr receiver)
            return;

        if (!IsReadonlyBinding(context, receiver.Name.Lexeme, receiver.Name.Span.StartLine, receiver.Name.Span.StartColumn))
            return;

        context.ReportDiagnostic(
            DiagnosticDescriptors.SA0847.CreateDiagnostic(expr.Span, receiver.Name.Lexeme));
    }

    // ── IndexAssignExpr: D[i] = … ─────────────────────────────────────────────

    private static void AnalyzeIndexAssign(RuleContext context, IndexAssignExpr expr)
    {
        if (expr.Object is not IdentifierExpr receiver)
            return;

        if (!IsReadonlyBinding(context, receiver.Name.Lexeme, receiver.Name.Span.StartLine, receiver.Name.Span.StartColumn))
            return;

        context.ReportDiagnostic(
            DiagnosticDescriptors.SA0847.CreateDiagnostic(expr.Span, receiver.Name.Lexeme));
    }

    // ── CallExpr: arr.push(D, …) / dict.set(D, …) / … ────────────────────────

    private static void AnalyzeCall(RuleContext context, CallExpr expr)
    {
        // Callee must be a dot expression: <ns>.<fn>
        if (expr.Callee is not DotExpr dot)
            return;

        // Namespace must be a simple identifier (e.g. `arr`, `dict`)
        if (dot.Object is not IdentifierExpr nsIdent)
            return;

        var qualifiedName = $"{nsIdent.Name.Lexeme}.{dot.Name.Lexeme}";

        if (!KnownInPlaceMutators.Contains(qualifiedName))
            return;

        // The first argument must be a bare identifier resolving to a readonly binding.
        if (expr.Arguments.Count == 0)
            return;

        if (expr.Arguments[0] is not IdentifierExpr firstArg)
            return;

        if (!IsReadonlyBinding(context, firstArg.Name.Lexeme, firstArg.Name.Span.StartLine, firstArg.Name.Span.StartColumn))
            return;

        context.ReportDiagnostic(
            DiagnosticDescriptors.SA0847.CreateDiagnostic(expr.Span, firstArg.Name.Lexeme));
    }

    // ── Helper ────────────────────────────────────────────────────────────────

    private static bool IsReadonlyBinding(RuleContext context, string name, int line, int col)
    {
        var definition = context.ScopeTree.FindDefinition(name, line, col);
        return definition is { IsReadonly: true };
    }
}
