namespace Stash.Analysis.Rules;

using System;
using System.Collections.Generic;
using Stash.Lexing;
using Stash.Parsing.AST;

/// <summary>
/// SA0303 — Emits a warning for every type annotation that refers to an unknown type name
/// (not a built-in primitive and not a user-declared struct, enum, or interface).
/// </summary>
public sealed class UnknownTypeRule : IAnalysisRule
{
    public DiagnosticDescriptor Descriptor => DiagnosticDescriptors.SA0303;

    public IReadOnlySet<Type> SubscribedNodeTypes { get; } = new HashSet<Type>
    {
        typeof(VarDeclStmt),
        typeof(ConstDeclStmt),
        typeof(FnDeclStmt),
        typeof(ForInStmt),
        typeof(StructDeclStmt),
        typeof(ExtendStmt),
        typeof(InterfaceDeclStmt),
        typeof(LambdaExpr),
    };

    public void Analyze(RuleContext context)
    {
        switch (context.Statement)
        {
            case VarDeclStmt vds:
                ValidateTypeHint(vds.TypeHint, context);
                break;

            case ConstDeclStmt cds:
                ValidateTypeHint(cds.TypeHint, context);
                break;

            case FnDeclStmt fds:
                foreach (var paramType in fds.ParameterTypes)
                {
                    ValidateTypeHint(paramType, context);
                }
                ValidateTypeHint(fds.ReturnType, context);
                break;

            case ForInStmt fis:
                ValidateTypeHint(fis.TypeHint, context);
                break;

            case StructDeclStmt sds:
                foreach (var fieldType in sds.FieldTypes)
                {
                    ValidateTypeHint(fieldType, context);
                }
                foreach (var method in sds.Methods)
                {
                    foreach (var paramType in method.ParameterTypes)
                    {
                        ValidateTypeHint(paramType, context);
                    }
                    ValidateTypeHint(method.ReturnType, context);
                }
                break;

            case ExtendStmt es:
                foreach (var method in es.Methods)
                {
                    foreach (var paramType in method.ParameterTypes)
                    {
                        ValidateTypeHint(paramType, context);
                    }
                    ValidateTypeHint(method.ReturnType, context);
                }
                break;

            case InterfaceDeclStmt ids:
                foreach (var fieldType in ids.FieldTypes)
                {
                    ValidateTypeHint(fieldType, context);
                }
                foreach (var method in ids.Methods)
                {
                    foreach (var paramType in method.ParameterTypes)
                    {
                        ValidateTypeHint(paramType, context);
                    }
                    ValidateTypeHint(method.ReturnType, context);
                }
                break;
        }

        if (context.Expression is LambdaExpr le)
        {
            foreach (var paramType in le.ParameterTypes)
            {
                if (paramType != null)
                {
                    ValidateTypeHint(paramType, context);
                }
            }
        }
    }

    private static void ValidateTypeHint(TypeHint? typeHint, RuleContext context)
    {
        if (typeHint == null)
        {
            return;
        }

        var typeName = typeHint.Name.Lexeme;

        if (context.ValidBuiltInTypes.Contains(typeName))
        {
            return;
        }

        var definition = context.ScopeTree.FindDefinition(typeName, typeHint.Name.Span.StartLine, typeHint.Name.Span.StartColumn);
        if (definition != null && (definition.Kind == SymbolKind.Struct || definition.Kind == SymbolKind.Enum || definition.Kind == SymbolKind.Interface))
        {
            return;
        }

        context.ReportDiagnostic(DiagnosticDescriptors.SA0303.CreateDiagnostic(typeHint.Span, typeName));
    }
}
