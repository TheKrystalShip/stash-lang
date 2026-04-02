namespace Stash.Analysis;

using System.Collections.Generic;
using Stash.Common;
using Stash.Parsing.AST;
using Stash.Runtime.Types;
using Stash.Stdlib;

/// <summary>
/// Performs a best-effort type inference pass over the <see cref="ScopeTree"/>, populating
/// <see cref="SymbolInfo.TypeHint"/> for symbols whose type was not explicitly annotated in source.
/// </summary>
/// <remarks>
/// <para>
/// Inference is driven by a fixed set of rules applied to variable and constant initializer
/// expressions:
/// </para>
/// <list type="number">
///   <item><description><b>Struct init</b> — <c>let p = Point { … }</c> infers type <c>"Point"</c>.</description></item>
///   <item><description><b>Command expression</b> — <c>let r = `cmd`</c> infers type <c>"CommandResult"</c>.</description></item>
///   <item><description><b>Simple function call</b> — looks up the callee symbol and copies its <see cref="SymbolInfo.TypeHint"/> (the return type).</description></item>
///   <item><description><b>Namespace function call</b> — resolves the qualified name (e.g. <c>http.get</c>) via <see cref="StdlibRegistry"/> and copies the registered return type.</description></item>
///   <item><description><b>Identifier assignment</b> — copies the type hint of the referenced symbol.</description></item>
///   <item><description><b>Try expression</b> — recursively infers the inner expression type.</description></item>
///   <item><description><b>Literals</b> — maps <c>long → "int"</c>, <c>double → "float"</c>, <c>string → "string"</c>, <c>bool → "bool"</c>, <c>null → "null"</c>.</description></item>
///   <item><description><b>Array literal</b> — infers <c>"array"</c>.</description></item>
///   <item><description><b>Dict literal</b> — infers <c>"dict"</c>.</description></item>
///   <item><description><b>Dot access</b> — recursively infers the receiver type, then looks up the field via <see cref="ScopeTree.FindField"/>.</description></item>
/// </list>
/// <para>
/// Inference only runs when a symbol's <see cref="SymbolInfo.TypeHint"/> is <see langword="null"/>,
/// so explicit annotations are never overwritten.
/// </para>
/// <para>
/// The inferred types are used by <see cref="SemanticValidator"/> to emit type-mismatch
/// diagnostics, and by completion and hover handlers to show richer type information.
/// </para>
/// </remarks>
public static class TypeInferenceEngine
{
    /// <summary>
    /// Runs a single type-inference pass over the top-level <paramref name="statements"/>,
    /// mutating <see cref="SymbolInfo.TypeHint"/> on any symbol whose type could be determined.
    /// Recurses into function and struct method bodies.
    /// </summary>
    /// <param name="scopeTree">The scope tree to look up symbols in during inference.</param>
    /// <param name="statements">The top-level AST statements to process.</param>
    public static void InferTypes(ScopeTree scopeTree, List<Stmt> statements)
    {
        foreach (var stmt in statements)
        {
            InferFromStatement(scopeTree, stmt);
        }
    }

    /// <summary>
    /// Dispatches a single statement to the appropriate inference handler.
    /// Handles <c>let</c>, <c>const</c>, <c>fn</c>, <c>struct</c>, block, <c>if</c>,
    /// <c>while</c>, and <c>for-in</c> statements; other statement kinds are skipped.
    /// </summary>
    /// <param name="scopeTree">The scope tree used for symbol lookups.</param>
    /// <param name="stmt">The statement to process.</param>
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
            case ForStmt forStmt:
                if (forStmt.Initializer is not null)
                {
                    InferFromStatement(scopeTree, forStmt.Initializer);
                }
                foreach (var s in forStmt.Body.Statements)
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

    /// <summary>
    /// Looks up the symbol for <paramref name="name"/> at its declaration position and,
    /// if the symbol has no existing <see cref="SymbolInfo.TypeHint"/>, infers one from
    /// the <paramref name="initializer"/> expression and assigns it.
    /// </summary>
    /// <param name="scopeTree">The scope tree used for symbol and definition lookups.</param>
    /// <param name="name">The declared variable or constant name.</param>
    /// <param name="nameSpan">The source span of the name token, used as the lookup position.</param>
    /// <param name="initializer">The initializer expression, or <see langword="null"/> for uninitialized declarations.</param>
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

    /// <summary>
    /// Infers the type of an expression using the ten inference rules described on
    /// <see cref="TypeInferenceEngine"/>. Returns <see langword="null"/> when no rule applies.
    /// </summary>
    /// <param name="scopeTree">The scope tree used for symbol and field lookups.</param>
    /// <param name="expr">The expression whose type to infer.</param>
    /// <param name="contextLine">One-based line of the usage context, used for scoped lookups.</param>
    /// <param name="contextCol">One-based column of the usage context, used for scoped lookups.</param>
    /// <returns>The inferred type name (e.g. <c>"int"</c>, <c>"Point"</c>), or <see langword="null"/>.</returns>
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
                    StashDuration => "duration",
                    StashByteSize => "bytes",
                    StashIpAddress => "ip",
                    StashSemVer => "semver",
                    _ => null
                };

            // Rule 8: Array literal
            case ArrayExpr:
                return "array";

            // Rule 10: Dict literal
            case DictLiteralExpr:
                return "dict";

            // Rule 9: Dot access — resolve struct field type
            case DotExpr dotExpr:
            {
                var receiverType = InferExpressionType(scopeTree, dotExpr.Object, contextLine, contextCol);
                if (receiverType != null)
                {
                    var field = scopeTree.FindField(receiverType, dotExpr.Name.Lexeme);
                    return field?.TypeHint;
                }
                return null;
            }

            default:
                return null;
        }
    }

    /// <summary>
    /// Infers the return type of a call expression by applying rules 3 and 4:
    /// <list type="bullet">
    ///   <item><description>Rule 3: simple identifier callee — looks up the function symbol and returns its <see cref="SymbolInfo.TypeHint"/>.</description></item>
    ///   <item><description>Rule 4: namespace dot-access callee (e.g. <c>http.get</c>) — resolves via <see cref="StdlibRegistry.TryGetNamespaceFunction"/> and returns the registered return type.</description></item>
    /// </list>
    /// </summary>
    /// <param name="scopeTree">The scope tree used for function symbol lookups.</param>
    /// <param name="callExpr">The call expression to infer the type of.</param>
    /// <param name="contextLine">One-based line of the call site.</param>
    /// <param name="contextCol">One-based column of the call site.</param>
    /// <returns>The inferred return type, or <see langword="null"/> if it cannot be determined.</returns>
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
            StdlibRegistry.IsBuiltInNamespace(nsIdent.Name.Lexeme))
        {
            var qualified = $"{nsIdent.Name.Lexeme}.{dotExpr.Name.Lexeme}";
            if (StdlibRegistry.TryGetNamespaceFunction(qualified, out var nsFunc) && nsFunc.ReturnType != null)
            {
                return nsFunc.ReturnType;
            }
        }

        return null;
    }
}
