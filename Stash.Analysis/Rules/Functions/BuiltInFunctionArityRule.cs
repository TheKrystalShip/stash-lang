namespace Stash.Analysis.Rules;

using System;
using System.Collections.Generic;
using System.Linq;
using Stash.Parsing.AST;
using Stash.Stdlib;

/// <summary>
/// SA0402 — Emits an error when a call to a built-in namespace function (e.g. <c>http.get</c>)
/// is made with the wrong number of arguments.
/// </summary>
public sealed class BuiltInFunctionArityRule : IAnalysisRule
{
    public DiagnosticDescriptor Descriptor => DiagnosticDescriptors.SA0402;

    public IReadOnlySet<Type> SubscribedNodeTypes { get; } = new HashSet<Type> { typeof(CallExpr) };

    public void Analyze(RuleContext context)
    {
        if (context.Expression is not CallExpr expr)
        {
            return;
        }

        if (expr.Callee is not DotExpr dot || dot.Object is not IdentifierExpr nsId)
        {
            return;
        }

        if (!StdlibRegistry.IsBuiltInNamespace(nsId.Name.Lexeme))
        {
            return;
        }

        var qualifiedName = $"{nsId.Name.Lexeme}.{dot.Name.Lexeme}";
        if (!StdlibRegistry.TryGetNamespaceFunction(qualifiedName, out var func) || func.IsVariadic)
        {
            return;
        }

        bool hasSpread = expr.Arguments.Any(a => a is SpreadExpr);
        if (hasSpread)
        {
            int nonSpreadCount = expr.Arguments.Count(a => a is not SpreadExpr);
            if (nonSpreadCount > func.Parameters.Length)
            {
                context.ReportDiagnostic(DiagnosticDescriptors.SA0506.CreateDiagnostic(expr.Paren.Span, nonSpreadCount, qualifiedName, func.Parameters.Length));
            }
        }
        else if (expr.Arguments.Count != func.Parameters.Length)
        {
            context.ReportDiagnostic(DiagnosticDescriptors.SA0402.CreateDiagnostic(expr.Paren.Span, func.Parameters.Length, expr.Arguments.Count));
        }
    }
}
