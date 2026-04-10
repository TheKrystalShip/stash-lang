namespace Stash.Analysis.Rules;

using System;
using System.Collections.Generic;
using System.Linq;
using Stash.Parsing.AST;

/// <summary>
/// SA0401 — Emits an error when a call to a user-defined function is made with the wrong number
/// of arguments (too few or too many).
/// </summary>
public sealed class UserFunctionArityRule : IAnalysisRule
{
    public DiagnosticDescriptor Descriptor => DiagnosticDescriptors.SA0401;

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
        if (definition == null || definition.Kind != SymbolKind.Function)
        {
            return;
        }

        var paramCount = definition.ParameterNames != null
            ? definition.ParameterNames.Length
            : CountParameters(definition.Detail ?? "");

        if (paramCount < 0)
        {
            return;
        }

        bool hasSpread = expr.Arguments.Any(a => a is SpreadExpr);
        int requiredCount = definition.RequiredParameterCount ?? paramCount;

        if (hasSpread)
        {
            int nonSpreadCount = expr.Arguments.Count(a => a is not SpreadExpr);
            if (!definition.IsVariadic && nonSpreadCount > paramCount)
            {
                context.ReportDiagnostic(DiagnosticDescriptors.SA0506.CreateDiagnostic(expr.Paren.Span, nonSpreadCount, id.Name.Lexeme, paramCount));
            }
        }
        else if (definition.IsVariadic)
        {
            if (expr.Arguments.Count < requiredCount)
            {
                string expected = $"{requiredCount}+";
                context.ReportDiagnostic(DiagnosticDescriptors.SA0401.CreateDiagnostic(expr.Paren.Span, expected, expr.Arguments.Count));
            }
        }
        else
        {
            if (expr.Arguments.Count < requiredCount || expr.Arguments.Count > paramCount)
            {
                string expected = requiredCount == paramCount
                    ? $"{paramCount}"
                    : $"{requiredCount} to {paramCount}";
                context.ReportDiagnostic(DiagnosticDescriptors.SA0401.CreateDiagnostic(expr.Paren.Span, expected, expr.Arguments.Count));
            }
        }
    }

    /// <summary>
    /// Parses the parameter count from a symbol detail signature string of the form
    /// <c>"fn name(a, b)"</c>. Returns <c>-1</c> if the string cannot be parsed.
    /// </summary>
    private static int CountParameters(string detail)
    {
        var openParen = detail.IndexOf('(');
        var closeParen = detail.IndexOf(')');
        if (openParen < 0 || closeParen < 0 || closeParen <= openParen)
        {
            return -1;
        }

        var inside = detail[(openParen + 1)..closeParen].Trim();
        if (string.IsNullOrEmpty(inside))
        {
            return 0;
        }

        return inside.Split(',').Length;
    }
}
