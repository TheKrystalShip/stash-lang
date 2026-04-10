namespace Stash.Analysis.Rules;

using System;
using System.Collections.Generic;

/// <summary>
/// SA0202 — Post-walk rule that emits a warning for every identifier reference that could not
/// be resolved to a declaration in the scope tree or the built-in registry.
/// </summary>
public sealed class UndefinedIdentifierRule : IAnalysisRule
{
    public DiagnosticDescriptor Descriptor => DiagnosticDescriptors.SA0202;

    /// <summary>Empty — this is a post-walk rule invoked once after the full AST walk.</summary>
    public IReadOnlySet<Type> SubscribedNodeTypes { get; } = new HashSet<Type>();

    public void Analyze(RuleContext context)
    {
        var unresolved = context.ScopeTree.GetUnresolvedReferences(context.BuiltInNames);
        foreach (var reference in unresolved)
        {
            context.ReportDiagnostic(DiagnosticDescriptors.SA0202.CreateDiagnostic(reference.Span, reference.Name));
        }
    }
}
