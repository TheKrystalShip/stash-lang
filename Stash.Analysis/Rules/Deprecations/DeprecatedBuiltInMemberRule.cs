namespace Stash.Analysis.Rules.Deprecations;

using System;
using System.Collections.Generic;
using Stash.Parsing.AST;
using Stash.Stdlib;

/// <summary>
/// Emits SA0830 when user code references a deprecated built-in namespace member
/// (function or constant). Operates on member-access expressions of the form
/// <c>namespace.member</c> where the namespace is a known built-in.
/// </summary>
/// <remarks>
/// Subscribes to both <see cref="CallExpr"/> (for function calls like <c>process.exit()</c>)
/// and <see cref="DotExpr"/> (for bare constant accesses like <c>process.SIGTERM</c>).
/// The <see cref="Stash.Analysis.SemanticValidator"/> skips visiting the callee <see cref="DotExpr"/>
/// for built-in namespace calls, so function calls must be caught via <see cref="CallExpr"/>.
/// </remarks>
public sealed class DeprecatedBuiltInMemberRule : IAnalysisRule
{
    public DiagnosticDescriptor Descriptor => DiagnosticDescriptors.SA0830;

    public IReadOnlySet<Type> SubscribedNodeTypes { get; } = new HashSet<Type>
    {
        typeof(CallExpr),
        typeof(DotExpr)
    };

    public void Analyze(RuleContext context)
    {
        switch (context.Expression)
        {
            case CallExpr call when call.Callee is DotExpr dot && dot.Object is IdentifierExpr nsId:
                CheckMember(context, nsId.Name.Lexeme, dot.Name.Lexeme, dot.Name.Span, checkFunction: true, checkConstant: false);
                break;

            case DotExpr dot when dot.Object is IdentifierExpr nsId:
                // Only handle bare accesses (not function call callees — those are handled above)
                CheckMember(context, nsId.Name.Lexeme, dot.Name.Lexeme, dot.Name.Span, checkFunction: true, checkConstant: true);
                break;
        }
    }

    private static void CheckMember(RuleContext context, string namespaceName, string memberName,
        Stash.Common.SourceSpan span, bool checkFunction, bool checkConstant)
    {
        if (!StdlibRegistry.IsBuiltInNamespace(namespaceName))
        {
            return;
        }

        var qualifiedName = $"{namespaceName}.{memberName}";

        if (checkFunction && StdlibRegistry.TryGetNamespaceFunction(qualifiedName, out var func) && func.Deprecation != null)
        {
            context.ReportDiagnostic(DiagnosticDescriptors.SA0830.CreateDeprecatedDiagnostic(span, qualifiedName, func.Deprecation.ReplacementQualifiedName));
            return;
        }

        if (checkConstant && StdlibRegistry.TryGetNamespaceConstant(qualifiedName, out var constant) && constant.Deprecation != null)
        {
            context.ReportDiagnostic(DiagnosticDescriptors.SA0830.CreateDeprecatedDiagnostic(span, qualifiedName, constant.Deprecation.ReplacementQualifiedName));
        }
    }
}
