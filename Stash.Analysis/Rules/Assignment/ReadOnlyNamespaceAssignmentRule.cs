namespace Stash.Analysis.Rules.Assignment;

using System;
using System.Collections.Generic;
using Stash.Parsing.AST;
using Stash.Stdlib;

/// <summary>
/// SA0845 — Emits an error when an assignment targets a field on a built-in namespace or a
/// user-module alias (produced by <c>import "path" as ns</c>).
/// </summary>
/// <remarks>
/// All exported symbols from a Stash module are immutable (SA0805 prohibits exporting mutable
/// <c>let</c> bindings), so any <c>DotAssignExpr</c> whose receiver resolves to a namespace —
/// whether built-in or a user-module alias — is unconditionally read-only.
/// </remarks>
public sealed class ReadOnlyNamespaceAssignmentRule : IAnalysisRule
{
    public DiagnosticDescriptor Descriptor => DiagnosticDescriptors.SA0845;

    public IReadOnlySet<Type> SubscribedNodeTypes { get; } = new HashSet<Type> { typeof(DotAssignExpr) };

    public void Analyze(RuleContext context)
    {
        if (context.Expression is not DotAssignExpr expr)
            return;

        // Only fire when the receiver is a simple identifier.
        if (expr.Object is not IdentifierExpr receiverIdent)
            return;

        string receiverName = receiverIdent.Name.Lexeme;
        string qualifiedName = $"{receiverName}.{expr.Name.Lexeme}";

        // (a) Built-in namespace receiver.
        if (StdlibRegistry.IsBuiltInNamespace(receiverName))
        {
            context.ReportDiagnostic(
                DiagnosticDescriptors.SA0845.CreateDiagnostic(expr.Span, qualifiedName));
            return;
        }

        // (b) User-module alias receiver: scan top-level statements for ImportAsStmt whose
        // alias matches the receiver name.  This is O(imports) per fired rule invocation,
        // which is acceptably cheap because assignment-to-namespace-member is extremely rare.
        foreach (var stmt in context.AllStatements)
        {
            if (stmt is ImportAsStmt importAs && importAs.Alias.Lexeme == receiverName)
            {
                context.ReportDiagnostic(
                    DiagnosticDescriptors.SA0845.CreateDiagnostic(expr.Span, qualifiedName));
                return;
            }
        }
    }
}
