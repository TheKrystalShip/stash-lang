namespace Stash.Analysis.Rules;

using System;
using System.Collections.Generic;
using Stash.Lexing;
using Stash.Parsing.AST;

/// <summary>
/// SA1106 — Emits a warning when both sides of a comparison expression are the same identifier
/// (e.g. <c>x == x</c>), which is always trivially true or false.
/// </summary>
public sealed class NoSelfCompareRule : IAnalysisRule
{
    public DiagnosticDescriptor Descriptor => DiagnosticDescriptors.SA1106;

    public IReadOnlySet<Type> SubscribedNodeTypes { get; } = new HashSet<Type> { typeof(BinaryExpr) };

    public void Analyze(RuleContext context)
    {
        if (context.Expression is not BinaryExpr binary)
        {
            return;
        }

        if (!IsComparisonOperator(binary.Operator.Type))
        {
            return;
        }

        if (binary.Left is not IdentifierExpr lhs || binary.Right is not IdentifierExpr rhs)
        {
            return;
        }

        if (lhs.Name.Lexeme != rhs.Name.Lexeme)
        {
            return;
        }

        string result = GetAlwaysResult(binary.Operator.Type);
        context.ReportDiagnostic(
            DiagnosticDescriptors.SA1106.CreateDiagnostic(binary.Span, lhs.Name.Lexeme, result));
    }

    private static bool IsComparisonOperator(TokenType type) => type is
        TokenType.EqualEqual or
        TokenType.BangEqual or
        TokenType.Less or
        TokenType.Greater or
        TokenType.LessEqual or
        TokenType.GreaterEqual;

    private static string GetAlwaysResult(TokenType type) => type switch
    {
        TokenType.EqualEqual => "true",
        TokenType.BangEqual => "false",
        TokenType.Less => "false",
        TokenType.Greater => "false",
        TokenType.LessEqual => "true",
        TokenType.GreaterEqual => "true",
        _ => "unknown",
    };
}
