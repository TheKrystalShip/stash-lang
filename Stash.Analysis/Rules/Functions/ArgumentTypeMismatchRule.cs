namespace Stash.Analysis.Rules;

using System;
using System.Collections.Generic;
using Stash.Parsing.AST;

/// <summary>
/// SA0403 — Emits a warning when an argument passed to a user-defined function has an inferred
/// type that is incompatible with the parameter's declared type annotation.
/// </summary>
public sealed class ArgumentTypeMismatchRule : IAnalysisRule
{
    public DiagnosticDescriptor Descriptor => DiagnosticDescriptors.SA0403;

    public IReadOnlySet<Type> SubscribedNodeTypes { get; } = new HashSet<Type> { typeof(CallExpr) };

    public void Analyze(RuleContext context)
    {
        if (context.Expression is not CallExpr expr)
        {
            return;
        }

        if (expr.Callee is not IdentifierExpr id)
        {
            return;
        }

        var line = id.Span.StartLine;
        var col = id.Span.StartColumn;
        var definition = context.ScopeTree.FindDefinition(id.Name.Lexeme, line, col);
        if (definition == null || definition.Kind != SymbolKind.Function || definition.ParameterTypes == null)
        {
            return;
        }

        for (int i = 0; i < expr.Arguments.Count && i < definition.ParameterTypes.Length; i++)
        {
            if (expr.Arguments[i] is SpreadExpr)
            {
                break; // Unknown distribution after spread
            }

            var expectedType = definition.ParameterTypes[i];
            if (expectedType == null)
            {
                continue;
            }

            var argType = TypeInferenceEngine.InferExpressionType(context.ScopeTree, expr.Arguments[i], line, col);
            if (argType != null && argType != "null" && argType != expectedType)
            {
                var paramName = definition.ParameterNames != null && i < definition.ParameterNames.Length
                    ? definition.ParameterNames[i]
                    : $"argument {i + 1}";
                context.ReportDiagnostic(DiagnosticDescriptors.SA0403.CreateDiagnostic(expr.Arguments[i].Span, paramName, expectedType, argType));
            }
        }
    }
}
