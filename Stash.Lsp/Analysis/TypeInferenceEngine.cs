namespace Stash.Lsp.Analysis;

using System.Collections.Generic;
using System.Linq;
using Stash.Common;
using Stash.Parsing.AST;

public static class TypeInferenceEngine
{
    public static void InferTypes(ScopeTree scopeTree, List<Stmt> statements)
    {
        foreach (var stmt in statements)
        {
            InferFromStatement(scopeTree, stmt);
        }
    }

    private static void InferFromStatement(ScopeTree scopeTree, Stmt stmt)
    {
        switch (stmt)
        {
            case VarDeclStmt varDecl:
                InferVariableType(scopeTree, varDecl.Name.Lexeme, varDecl.Name.Span, varDecl.Initializer);
                break;
            case ConstDeclStmt constDecl:
                InferVariableType(scopeTree, constDecl.Name.Lexeme, constDecl.Name.Span, constDecl.Initializer);
                break;
            case FnDeclStmt fnDecl:
                foreach (var s in fnDecl.Body.Statements)
                {
                    InferFromStatement(scopeTree, s);
                }

                break;
            case StructDeclStmt structDecl:
                foreach (var method in structDecl.Methods)
                {
                    foreach (var s in method.Body.Statements)
                    {
                        InferFromStatement(scopeTree, s);
                    }
                }

                break;
            case BlockStmt block:
                foreach (var s in block.Statements)
                {
                    InferFromStatement(scopeTree, s);
                }

                break;
            case IfStmt ifStmt:
                InferFromStatement(scopeTree, ifStmt.ThenBranch);
                if (ifStmt.ElseBranch != null)
                {
                    InferFromStatement(scopeTree, ifStmt.ElseBranch);
                }

                break;
            case WhileStmt whileStmt:
                foreach (var s in whileStmt.Body.Statements)
                {
                    InferFromStatement(scopeTree, s);
                }

                break;
            case ForInStmt forInStmt:
                foreach (var s in forInStmt.Body.Statements)
                {
                    InferFromStatement(scopeTree, s);
                }

                break;
            case ExprStmt:
            default:
                break;
        }
    }

    private static void InferVariableType(ScopeTree scopeTree, string name, SourceSpan nameSpan, Expr? initializer)
    {
        if (initializer == null)
        {
            return;
        }

        var symbol = scopeTree.FindDefinition(name, nameSpan.StartLine, nameSpan.StartColumn);
        if (symbol == null || symbol.TypeHint != null)
        {
            return;
        }

        var inferredType = InferExpressionType(scopeTree, initializer, nameSpan.StartLine, nameSpan.StartColumn);
        if (inferredType != null)
        {
            symbol.TypeHint = inferredType;
        }
    }

    internal static string? InferExpressionType(ScopeTree scopeTree, Expr expr, int contextLine, int contextCol)
    {
        switch (expr)
        {
            // Rule 1: Struct initialization
            case StructInitExpr structInit:
                return structInit.Name.Lexeme;

            // Rule 2: Command expression
            case CommandExpr:
                return "CommandResult";

            // Rule 3 & 4: Function call return types
            case CallExpr callExpr:
                return InferCallExprType(scopeTree, callExpr, contextLine, contextCol);

            // Rule 5: Variable-to-variable assignment
            case IdentifierExpr identExpr:
            {
                var resolved = scopeTree.FindDefinition(identExpr.Name.Lexeme, contextLine, contextCol);
                return resolved?.TypeHint;
            }

            // Rule 6: Try expression — recurse into inner expression
            case TryExpr tryExpr:
                return InferExpressionType(scopeTree, tryExpr.Expression, contextLine, contextCol);

            // Rule 7: Literal type inference
            case LiteralExpr literal:
                return literal.Value switch
                {
                    long => "int",
                    double => "float",
                    string => "string",
                    bool => "bool",
                    null => "null",
                    _ => null
                };

            // Rule 8: Array literal
            case ArrayExpr:
                return "array";

            // Rule 9: Dot access — resolve struct field type
            case DotExpr dotExpr:
            {
                var receiverType = InferExpressionType(scopeTree, dotExpr.Object, contextLine, contextCol);
                if (receiverType != null)
                {
                    var field = scopeTree.GlobalScope.Symbols.FirstOrDefault(s =>
                        s.Kind == SymbolKind.Field && s.ParentName == receiverType && s.Name == dotExpr.Name.Lexeme);
                    return field?.TypeHint;
                }
                return null;
            }

            default:
                return null;
        }
    }

    private static string? InferCallExprType(ScopeTree scopeTree, CallExpr callExpr, int contextLine, int contextCol)
    {
        // Rule 3: Simple function call — callee is a plain identifier
        if (callExpr.Callee is IdentifierExpr funcIdent)
        {
            var funcSymbol = scopeTree.FindDefinition(funcIdent.Name.Lexeme, contextLine, contextCol);
            return funcSymbol?.TypeHint;
        }

        // Rule 4: Namespace function call — callee is a dot expression (e.g. http.get)
        if (callExpr.Callee is DotExpr dotExpr &&
            dotExpr.Object is IdentifierExpr nsIdent &&
            BuiltInRegistry.IsBuiltInNamespace(nsIdent.Name.Lexeme))
        {
            var qualified = $"{nsIdent.Name.Lexeme}.{dotExpr.Name.Lexeme}";
            if (BuiltInRegistry.TryGetNamespaceFunction(qualified, out var nsFunc) && nsFunc.ReturnType != null)
            {
                return nsFunc.ReturnType;
            }
        }

        return null;
    }
}
