namespace Stash.Analysis.Rules;

using System;
using System.Collections.Generic;

/// <summary>
/// Represents a single, isolated semantic analysis rule.
/// </summary>
/// <remarks>
/// <para>
/// Per-node rules have a non-empty <see cref="SubscribedNodeTypes"/> and are invoked by the
/// <see cref="SemanticValidator"/> dispatcher each time a matching AST node is visited.
/// </para>
/// <para>
/// Post-walk rules have an empty <see cref="SubscribedNodeTypes"/> and are invoked once after the
/// full AST walk, receiving the complete top-level statement list via
/// <see cref="RuleContext.AllStatements"/>.
/// </para>
/// </remarks>
public interface IAnalysisRule
{
    /// <summary>The descriptor that describes this rule's code, title, severity, and category.</summary>
    DiagnosticDescriptor Descriptor { get; }

    /// <summary>
    /// The set of AST node types this rule subscribes to, or an empty set for post-walk rules.
    /// </summary>
    IReadOnlySet<Type> SubscribedNodeTypes { get; }

    /// <summary>
    /// Runs the rule against the current context. Use <see cref="RuleContext.ReportDiagnostic"/>
    /// to emit diagnostics.
    /// </summary>
    void Analyze(RuleContext context);
}
