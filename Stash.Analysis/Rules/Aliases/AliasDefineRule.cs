namespace Stash.Analysis.Rules.Aliases;

using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Stash.Parsing.AST;

/// <summary>
/// Emits SA0850 when <c>alias.define</c> is called with a name that is not a valid
/// identifier, and SA0851 when an <c>AliasOptions</c> struct literal provides an
/// empty string for the <c>confirm</c> field.
/// </summary>
/// <remarks>
/// Both diagnostics are detected statically from string literal arguments only;
/// non-literal names and non-literal confirm values are left for runtime validation.
/// Subscribes to <see cref="CallExpr"/> nodes only.
/// </remarks>
public sealed class AliasDefineRule : IAnalysisRule
{
    // Valid alias name: starts with a letter or underscore, followed by letters/digits/underscores.
    private static readonly Regex _validName = new(
        @"^[A-Za-z_][A-Za-z0-9_]*$",
        RegexOptions.Compiled,
        TimeSpan.FromMilliseconds(50));

    public DiagnosticDescriptor Descriptor => DiagnosticDescriptors.SA0850;

    public IReadOnlySet<Type> SubscribedNodeTypes { get; } = new HashSet<Type>
    {
        typeof(CallExpr)
    };

    public void Analyze(RuleContext context)
    {
        if (context.Expression is not CallExpr call)
            return;

        // Match: alias.define(...)
        if (call.Callee is not DotExpr dot ||
            dot.Object is not IdentifierExpr nsId ||
            nsId.Name.Lexeme != "alias" ||
            dot.Name.Lexeme != "define")
        {
            return;
        }

        // SA0850 — invalid alias name (first arg, if it is a string literal)
        if (call.Arguments.Count >= 1 &&
            call.Arguments[0] is LiteralExpr nameLiteral &&
            nameLiteral.Value is string nameStr)
        {
            if (!_validName.IsMatch(nameStr))
            {
                context.ReportDiagnostic(
                    DiagnosticDescriptors.SA0850.CreateDiagnostic(nameLiteral.Span, nameStr));
            }
        }

        // SA0851 — empty confirm prompt (third arg, if it is an AliasOptions struct literal)
        if (call.Arguments.Count >= 3 &&
            call.Arguments[2] is StructInitExpr structInit &&
            structInit.Name.Lexeme == "AliasOptions")
        {
            foreach (var (field, value) in structInit.FieldValues)
            {
                if (field.Lexeme == "confirm" &&
                    value is LiteralExpr confirmLiteral &&
                    confirmLiteral.Value is string confirmStr &&
                    confirmStr.Length == 0)
                {
                    context.ReportDiagnostic(
                        DiagnosticDescriptors.SA0851.CreateDiagnostic(confirmLiteral.Span));
                    break;
                }
            }
        }
    }
}
