namespace Stash.Analysis.Rules;

using System;
using System.Collections.Generic;
using Stash.Parsing.AST;

/// <summary>
/// SA1302 — Emits a warning when a shell command contains an interpolated expression part
/// (a non-literal value inserted via <c>${...}</c>), which may allow command injection.
/// </summary>
/// <remarks>
/// Strict commands (<c>$!(...)</c>) are excluded because they carry explicit error handling
/// and are considered a deliberate choice by the author.
/// </remarks>
public sealed class NoUnsafeCommandInterpolationRule : IAnalysisRule
{
    public DiagnosticDescriptor Descriptor => DiagnosticDescriptors.SA1302;

    public IReadOnlySet<Type> SubscribedNodeTypes { get; } = new HashSet<Type>
    {
        typeof(CommandExpr)
    };

    public void Analyze(RuleContext context)
    {
        if (context.Expression is not CommandExpr command) return;
        if (command.IsStrict) return;

        foreach (var part in command.Parts)
        {
            if (part is LiteralExpr) continue;

            string varName = part is IdentifierExpr id ? id.Name.Lexeme : "<expression>";
            context.ReportDiagnostic(
                DiagnosticDescriptors.SA1302.CreateDiagnostic(part.Span, varName));
            return; // report once per command
        }
    }
}

