namespace Stash.Analysis.Rules;

using System;
using System.Collections.Generic;
using Stash.Lexing;
using Stash.Parsing.AST;

/// <summary>
/// SA0209 — Emits an information diagnostic when a declaration does not follow the
/// expected Stash naming conventions:
/// <list type="bullet">
///   <item>Variables and parameters: camelCase (starts with lowercase letter or underscore)</item>
///   <item>Functions: camelCase (starts with lowercase letter or underscore)</item>
///   <item>Structs, enums, interfaces: PascalCase (starts with uppercase letter)</item>
/// </list>
/// Constants are intentionally not checked — both camelCase and UPPER_SNAKE_CASE are idiomatic.
/// Single-character and underscore-only names are always allowed.
/// </summary>
public sealed class NamingConventionRule : IAnalysisRule
{
    public DiagnosticDescriptor Descriptor => DiagnosticDescriptors.SA0209;

    public IReadOnlySet<Type> SubscribedNodeTypes { get; } = new HashSet<Type>
    {
        typeof(VarDeclStmt),
        typeof(FnDeclStmt),
        typeof(StructDeclStmt),
        typeof(EnumDeclStmt),
        typeof(InterfaceDeclStmt),
    };

    public void Analyze(RuleContext context)
    {
        switch (context.Statement)
        {
            case VarDeclStmt varDecl:
                CheckCamelCase(varDecl.Name, "camelCase (variables)", context);
                break;

            case FnDeclStmt fn:
                CheckCamelCase(fn.Name, "camelCase (functions)", context);
                foreach (var param in fn.Parameters)
                    CheckCamelCase(param, "camelCase (parameters)", context);
                break;

            case StructDeclStmt structDecl:
                CheckPascalCase(structDecl.Name, "PascalCase (structs)", context);
                break;

            case EnumDeclStmt enumDecl:
                CheckPascalCase(enumDecl.Name, "PascalCase (enums)", context);
                break;

            case InterfaceDeclStmt interfaceDecl:
                CheckPascalCase(interfaceDecl.Name, "PascalCase (interfaces)", context);
                break;
        }
    }

    /// <summary>
    /// Checks that the name starts with a lowercase letter or underscore (camelCase convention).
    /// </summary>
    private static void CheckCamelCase(Token name, string convention, RuleContext context)
    {
        string lexeme = name.Lexeme;
        if (IsExempt(lexeme))
            return;

        // camelCase: must start with a lowercase letter or underscore
        if (!char.IsLower(lexeme[0]) && lexeme[0] != '_')
        {
            context.ReportDiagnostic(
                DiagnosticDescriptors.SA0209.CreateDiagnostic(name.Span, lexeme, convention));
        }
    }

    /// <summary>
    /// Checks that the name starts with an uppercase letter (PascalCase convention).
    /// </summary>
    private static void CheckPascalCase(Token name, string convention, RuleContext context)
    {
        string lexeme = name.Lexeme;
        if (IsExempt(lexeme))
            return;

        // PascalCase: must start with an uppercase letter
        if (!char.IsUpper(lexeme[0]))
        {
            context.ReportDiagnostic(
                DiagnosticDescriptors.SA0209.CreateDiagnostic(name.Span, lexeme, convention));
        }
    }

    /// <summary>
    /// Returns true for names that are always exempt from convention checks:
    /// single characters, underscore-only names, or built-in names (span starts at line 0).
    /// </summary>
    private static bool IsExempt(string name)
    {
        if (name.Length <= 1)
            return true;

        bool allUnderscores = true;
        foreach (char c in name)
        {
            if (c != '_')
            {
                allUnderscores = false;
                break;
            }
        }
        return allUnderscores;
    }
}
