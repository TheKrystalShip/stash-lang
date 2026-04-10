namespace Stash.Analysis.Rules;

using System;
using System.Collections.Generic;
using Stash.Parsing.AST;

/// <summary>
/// SA0501–SA0506 — Validates spread expressions in call arguments, array literals, and dict
/// literals, emitting diagnostics for type mismatches, null spreads, unnecessary spreads, and
/// empty-literal spreads.
/// </summary>
public sealed class SpreadDiagnosticsRule : IAnalysisRule
{
    public DiagnosticDescriptor Descriptor => DiagnosticDescriptors.SA0501;

    public IReadOnlySet<Type> SubscribedNodeTypes { get; } = new HashSet<Type>
    {
        typeof(CallExpr),
        typeof(ArrayExpr),
        typeof(DictLiteralExpr),
    };

    public void Analyze(RuleContext context)
    {
        switch (context.Expression)
        {
            case CallExpr call:
                AnalyzeCallExpr(call, context);
                break;

            case ArrayExpr arr:
                foreach (var el in arr.Elements)
                {
                    if (el is SpreadExpr spread)
                    {
                        CheckSpreadDiagnostics(spread, isArrayContext: true, context);
                    }
                }
                break;

            case DictLiteralExpr dict:
                foreach (var (key, value) in dict.Entries)
                {
                    if (key == null && value is SpreadExpr spread)
                    {
                        CheckSpreadDiagnostics(spread, isArrayContext: false, context);
                    }
                }
                break;
        }
    }

    private static void AnalyzeCallExpr(CallExpr expr, RuleContext context)
    {
        foreach (var arg in expr.Arguments)
        {
            if (arg is SpreadExpr spreadArg)
            {
                if (spreadArg.Expression is ArrayExpr innerArr && innerArr.Elements.Count > 0)
                {
                    context.ReportDiagnostic(DiagnosticDescriptors.SA0504.CreateUnnecessaryDiagnostic(spreadArg.Span));
                }
                else
                {
                    CheckSpreadDiagnostics(spreadArg, isArrayContext: true, context);
                }
            }
        }
    }

    private static void CheckSpreadDiagnostics(SpreadExpr spread, bool isArrayContext, RuleContext context)
    {
        var inner = spread.Expression;

        // SA0503: spreading null literal
        if (inner is LiteralExpr lit && lit.Value == null)
        {
            context.ReportDiagnostic(DiagnosticDescriptors.SA0503.CreateDiagnostic(spread.Span));
            return;
        }

        // SA0505: empty array spread
        if (inner is ArrayExpr arr && arr.Elements.Count == 0)
        {
            context.ReportDiagnostic(DiagnosticDescriptors.SA0505.CreateUnnecessaryDiagnostic(spread.Span, "array"));
            return;
        }

        // SA0505: empty dict spread (dict context only)
        if (!isArrayContext && inner is DictLiteralExpr dict && dict.Entries.Count == 0)
        {
            context.ReportDiagnostic(DiagnosticDescriptors.SA0505.CreateUnnecessaryDiagnostic(spread.Span, "dictionary"));
            return;
        }

        // Type inference check
        var line = spread.Span.StartLine;
        var col = spread.Span.StartColumn;
        var inferredType = TypeInferenceEngine.InferExpressionType(context.ScopeTree, inner, line, col);
        if (inferredType != null && inferredType != "null")
        {
            if (isArrayContext && inferredType != "array")
            {
                context.ReportDiagnostic(DiagnosticDescriptors.SA0501.CreateDiagnostic(spread.Span, inferredType));
            }
            else if (!isArrayContext && inferredType != "dict")
            {
                // Structs can also be spread into dicts
                var structDef = context.ScopeTree.FindDefinition(inferredType, line, col);
                if (structDef == null || structDef.Kind != SymbolKind.Struct)
                {
                    context.ReportDiagnostic(DiagnosticDescriptors.SA0502.CreateDiagnostic(spread.Span, inferredType));
                }
            }
        }
    }
}
