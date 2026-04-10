namespace Stash.Analysis.Rules;

using System;
using System.Collections.Generic;
using Stash.Parsing.AST;

/// <summary>
/// SA1103 — Emits a warning when a switch expression contains duplicate case values.
/// </summary>
public sealed class NoDuplicateCaseRule : IAnalysisRule
{
    public DiagnosticDescriptor Descriptor => DiagnosticDescriptors.SA1103;

    public IReadOnlySet<Type> SubscribedNodeTypes { get; } = new HashSet<Type> { typeof(SwitchExpr) };

    public void Analyze(RuleContext context)
    {
        if (context.Expression is not SwitchExpr switchExpr)
        {
            return;
        }

        var seen = new HashSet<object?>(SwitchValueComparer.Instance);
        foreach (var arm in switchExpr.Arms)
        {
            if (arm.IsDiscard || arm.Pattern == null)
            {
                continue;
            }

            if (arm.Pattern is not LiteralExpr literal)
            {
                continue;
            }

            if (!seen.Add(literal.Value))
            {
                context.ReportDiagnostic(
                    DiagnosticDescriptors.SA1103.CreateDiagnostic(arm.Span, FormatValue(literal.Value)));
            }
        }
    }

    private static string FormatValue(object? value) => value switch
    {
        null => "null",
        string s => $"\"{s}\"",
        bool b => b ? "true" : "false",
        _ => value.ToString() ?? "null",
    };

    /// <summary>Comparer that treats <c>null</c> as a valid key for the HashSet.</summary>
    private sealed class SwitchValueComparer : IEqualityComparer<object?>
    {
        public static readonly SwitchValueComparer Instance = new();

        bool IEqualityComparer<object?>.Equals(object? x, object? y)
        {
            if (x is null && y is null) return true;
            if (x is null || y is null) return false;
            return x.Equals(y);
        }

        int IEqualityComparer<object?>.GetHashCode(object? obj) => obj?.GetHashCode() ?? 0;
    }
}
