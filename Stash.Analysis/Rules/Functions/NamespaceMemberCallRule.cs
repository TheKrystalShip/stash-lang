namespace Stash.Analysis.Rules.Functions;

using System;
using System.Collections.Generic;
using Stash.Parsing.AST;
using Stash.Stdlib;
using Stash.Stdlib.Registration;

/// <summary>
/// SA0846 — Emits an error when a namespace data member is called like a function.
/// <para>
/// <c>cli.argc()</c> is invalid when <c>argc</c> is registered as a <see cref="DeclarationKind.DataMember"/>
/// rather than a <see cref="DeclarationKind.Function"/>.  Drop the parentheses to read
/// the value: <c>let n = cli.argc;</c>.
/// </para>
/// </summary>
public sealed class NamespaceMemberCallRule : IAnalysisRule
{
    public DiagnosticDescriptor Descriptor => DiagnosticDescriptors.SA0846;

    public IReadOnlySet<Type> SubscribedNodeTypes { get; } = new HashSet<Type> { typeof(CallExpr) };

    public void Analyze(RuleContext context)
    {
        if (context.Expression is not CallExpr expr)
            return;

        // Only fire for calls whose callee is a simple DotExpr with a known built-in namespace receiver.
        if (expr.Callee is not DotExpr dot || dot.Object is not IdentifierExpr nsId)
            return;

        if (!StdlibRegistry.IsBuiltInNamespace(nsId.Name.Lexeme))
            return;

        var qualifiedName = $"{nsId.Name.Lexeme}.{dot.Name.Lexeme}";

        if (!StdlibRegistry.TryGetDeclarationKind(qualifiedName, out var kind))
            return;

        if (kind != DeclarationKind.DataMember)
            return;

        // Emit the diagnostic at the call-site parenthesis span, consistent with arity-rule precedent.
        context.ReportDiagnostic(
            DiagnosticDescriptors.SA0846.CreateDiagnostic(expr.Paren.Span, qualifiedName));
    }
}
