namespace Stash.Analysis.Rules;

using System;
using System.Collections.Generic;
using Stash.Common;
using Stash.Parsing.AST;

/// <summary>
/// SA1301 — Emits a warning when a variable or constant with a sensitive name (password, secret,
/// token, etc.) is initialized with a hardcoded non-empty string literal.
/// </summary>
public sealed class NoHardcodedCredentialsRule : IAnalysisRule
{
    public DiagnosticDescriptor Descriptor => DiagnosticDescriptors.SA1301;

    public IReadOnlySet<Type> SubscribedNodeTypes { get; } = new HashSet<Type>
    {
        typeof(VarDeclStmt),
        typeof(ConstDeclStmt)
    };

    private static readonly HashSet<string> SensitiveNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "password", "passwd", "secret", "token", "apikey", "api_key",
        "accesskey", "access_key", "privatekey", "private_key",
        "credential", "auth_token", "authtoken", "secretkey", "secret_key"
    };

    public void Analyze(RuleContext context)
    {
        string? name = null;
        Expr? initializer = null;
        SourceSpan span = default;

        if (context.Statement is VarDeclStmt varDecl)
        {
            name = varDecl.Name.Lexeme;
            initializer = varDecl.Initializer;
            span = varDecl.Span;
        }
        else if (context.Statement is ConstDeclStmt constDecl)
        {
            name = constDecl.Name.Lexeme;
            initializer = constDecl.Initializer;
            span = constDecl.Span;
        }

        if (name == null || initializer == null) return;
        if (!SensitiveNames.Contains(name)) return;

        if (initializer is LiteralExpr literal &&
            literal.Value is string strVal &&
            !string.IsNullOrEmpty(strVal))
        {
            context.ReportDiagnostic(
                DiagnosticDescriptors.SA1301.CreateDiagnostic(span, name));
        }
    }
}
