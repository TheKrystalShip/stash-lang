namespace Stash.Analysis.Rules;

using System;
using System.Collections.Generic;
using System.Linq;
using Stash.Parsing.AST;

/// <summary>
/// SA0310 — Warns when a switch expression over an enum type does not cover all variants
/// and has no discard (<c>_</c>) arm.
/// </summary>
public sealed class ExhaustiveMatchRule : IAnalysisRule
{
    public DiagnosticDescriptor Descriptor => DiagnosticDescriptors.SA0310;

    public IReadOnlySet<Type> SubscribedNodeTypes { get; } = new HashSet<Type> { typeof(SwitchExpr) };

    public void Analyze(RuleContext context)
    {
        if (context.Expression is not SwitchExpr switchExpr)
        {
            return;
        }

        // If any arm is a discard (_), the match is exhaustive by definition
        foreach (var arm in switchExpr.Arms)
        {
            if (arm.IsDiscard)
            {
                return;
            }
        }

        // Determine the enum type name from the switch arms' patterns
        // Look at patterns: they should be DotExpr like Status.Active
        string? enumName = null;
        foreach (var arm in switchExpr.Arms)
        {
            if (arm.Pattern is DotExpr dot && dot.Object is IdentifierExpr id)
            {
                enumName = id.Name.Lexeme;
                break;
            }
        }

        if (enumName == null)
        {
            return; // Can't determine enum type — not an enum switch
        }

        // Verify this is actually a known enum
        var allSymbols = context.ScopeTree.All;
        bool isEnum = false;
        foreach (var sym in allSymbols)
        {
            if (sym.Name == enumName && sym.Kind == SymbolKind.Enum)
            {
                isEnum = true;
                break;
            }
        }

        if (!isEnum)
        {
            return; // Not switching over a known enum
        }

        // Collect all enum members
        var allMembers = new HashSet<string>();
        foreach (var sym in allSymbols)
        {
            if (sym.Kind == SymbolKind.EnumMember && sym.ParentName == enumName)
            {
                allMembers.Add(sym.Name);
            }
        }

        if (allMembers.Count == 0)
        {
            return; // Empty enum — nothing to check
        }

        // Collect covered members from switch arms
        foreach (var arm in switchExpr.Arms)
        {
            if (arm.Pattern is DotExpr dot && dot.Object is IdentifierExpr id && id.Name.Lexeme == enumName)
            {
                allMembers.Remove(dot.Name.Lexeme);
            }
        }

        // If there are uncovered members, report
        if (allMembers.Count > 0)
        {
            var missing = string.Join(", ", allMembers.OrderBy(m => m).Select(m => $"{enumName}.{m}"));
            context.ReportDiagnostic(
                DiagnosticDescriptors.SA0310.CreateDiagnostic(switchExpr.Span, enumName, missing));
        }
    }
}
