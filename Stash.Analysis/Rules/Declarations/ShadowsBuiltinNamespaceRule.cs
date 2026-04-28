namespace Stash.Analysis.Rules;

using System;
using System.Collections.Generic;
using Stash.Common;
using Stash.Parsing.AST;

/// <summary>
/// SA0212 — Warns when a declaration uses a name that exactly matches a built-in namespace,
/// making all functions in that namespace inaccessible in the current scope.
/// </summary>
public sealed class ShadowsBuiltinNamespaceRule : IAnalysisRule
{
    public DiagnosticDescriptor Descriptor => DiagnosticDescriptors.SA0212;

    public IReadOnlySet<Type> SubscribedNodeTypes { get; } = new HashSet<Type>
    {
        typeof(VarDeclStmt),
        typeof(ConstDeclStmt),
        typeof(FnDeclStmt),
    };

    public void Analyze(RuleContext context)
    {
        if (context.Statement is VarDeclStmt varDecl)
        {
            Check(varDecl.Name.Lexeme, varDecl.Span, context);
        }
        else if (context.Statement is ConstDeclStmt constDecl)
        {
            Check(constDecl.Name.Lexeme, constDecl.Span, context);
        }
        else if (context.Statement is FnDeclStmt fn)
        {
            Check(fn.Name.Lexeme, fn.Name.Span, context);
            foreach (var param in fn.Parameters)
            {
                Check(param.Lexeme, param.Span, context);
            }
        }
    }

    private static void Check(string name, SourceSpan span, RuleContext context)
    {
        if (context.BuiltInNames.Contains(name))
        {
            context.ReportDiagnostic(
                DiagnosticDescriptors.SA0212.CreateDiagnostic(span, name));
        }
    }
}
