namespace Stash.Analysis;

using System.Collections.Generic;
using Stash.Lexing;
using Stash.Parsing.AST;
using Stash.Stdlib;

/// <summary>
/// Validates a parsed Stash AST against the <see cref="ScopeTree"/> produced by
/// <see cref="SymbolCollector"/>, emitting <see cref="SemanticDiagnostic"/> entries for
/// semantic errors, warnings, and informational hints.
/// </summary>
/// <remarks>
/// <para>
/// The validator implements the full <see cref="IStmtVisitor{T}"/> and
/// <see cref="IExprVisitor{T}"/> interfaces to walk every node in the tree.
/// The following diagnostic categories are produced:
/// </para>
/// <list type="bullet">
///   <item><description><b>Errors</b>: <c>break</c>/<c>continue</c> outside a loop; <c>return</c> outside a function; constant reassignment; arity mismatch on calls to known built-in functions.</description></item>
///   <item><description><b>Warnings</b>: unknown type annotations; undefined identifier references; type mismatches on variable initialization, assignment, struct field assignment, and function argument passing.</description></item>
///   <item><description><b>Information</b>: unreachable statements following a terminating statement (<c>return</c>, <c>break</c>, <c>continue</c>, <c>process.exit()</c>), rendered as faded unnecessary code.</description></item>
/// </list>
/// <para>
/// Type mismatch checks delegate to <see cref="TypeInferenceEngine.InferExpressionType"/>
/// to determine the actual type of an expression. Arity checks use
/// <see cref="SymbolInfo.ParameterNames"/>, <see cref="SymbolInfo.RequiredParameterCount"/>,
/// and <see cref="SymbolInfo.ParameterTypes"/> recorded during symbol collection.
/// </para>
/// </remarks>
public class SemanticValidator : IStmtVisitor<object?>, IExprVisitor<object?>
{
    /// <summary>The scope tree used for definition lookups and unresolved reference queries.</summary>
    private readonly ScopeTree _scopeTree;

    /// <summary>Accumulates diagnostics produced during a single <see cref="Validate"/> call.</summary>
    private readonly List<SemanticDiagnostic> _diagnostics = new();

    /// <summary>Tracks nesting depth of loop bodies to validate <c>break</c>/<c>continue</c> usage.</summary>
    private int _loopDepth;

    /// <summary>Tracks nesting depth of function bodies to validate <c>return</c> usage.</summary>
    private int _functionDepth;

    /// <summary>Tracks nesting depth of elevate blocks to detect nested elevation.</summary>
    private int _elevateDepth;

    /// <summary>Set of names that are always in scope (built-in functions, namespaces, etc.).</summary>
    private static readonly IReadOnlySet<string> _builtInNames = StdlibRegistry.KnownNames;

    /// <summary>Set of type names that are always valid (primitives and built-in struct names).</summary>
    private static readonly IReadOnlySet<string> _validBuiltInTypes = StdlibRegistry.ValidTypes;

    /// <summary>
    /// Initializes a new <see cref="SemanticValidator"/> for the given scope tree.
    /// </summary>
    /// <param name="scopeTree">The scope tree previously built by <see cref="SymbolCollector"/>.</param>
    public SemanticValidator(ScopeTree scopeTree)
    {
        _scopeTree = scopeTree;
    }

    /// <summary>
    /// Runs all semantic checks over <paramref name="statements"/> and returns the collected
    /// diagnostics. Also checks for unresolved identifier references not covered by the visitor
    /// walk (undefined variable warnings).
    /// </summary>
    /// <param name="statements">The top-level AST statements of the document to validate.</param>
    /// <returns>All <see cref="SemanticDiagnostic"/> entries produced during validation.</returns>
    public List<SemanticDiagnostic> Validate(List<Stmt> statements)
    {
        _diagnostics.Clear();
        _loopDepth = 0;
        _functionDepth = 0;
        _elevateDepth = 0;

        CheckUnreachableStatements(statements);

        var unresolved = _scopeTree.GetUnresolvedReferences(_builtInNames);
        foreach (var reference in unresolved)
        {
            _diagnostics.Add(new SemanticDiagnostic(
                $"'{reference.Name}' is not defined.",
                DiagnosticLevel.Warning,
                reference.Span));
        }

        CheckUnusedSymbols();

        return _diagnostics;
    }

    // Statement visitors

    /// <summary>
    /// Validates the <paramref name="typeHint"/> token against the set of known built-in types
    /// and user-declared struct/enum names. Emits a <see cref="DiagnosticLevel.Warning"/> diagnostic
    /// if the type name is not recognised.
    /// </summary>
    /// <param name="typeHint">The type annotation token to validate, or <see langword="null"/> to skip.</param>
    private void ValidateTypeHint(Token? typeHint)
    {
        if (typeHint == null)
        {
            return;
        }

        var typeName = typeHint.Lexeme;

        if (_validBuiltInTypes.Contains(typeName))
        {
            return;
        }

        var definition = _scopeTree.FindDefinition(typeName, typeHint.Span.StartLine, typeHint.Span.StartColumn);
        if (definition != null && (definition.Kind == SymbolKind.Struct || definition.Kind == SymbolKind.Enum))
        {
            return;
        }

        _diagnostics.Add(new SemanticDiagnostic(
            $"Unknown type '{typeName}'.",
            DiagnosticLevel.Warning,
            typeHint.Span));
    }

    /// <summary>
    /// Validates parameter and return type annotations, then recurses into the function body
    /// checking for unreachable statements within a new function-depth context.
    /// </summary>
    /// <param name="stmt">The function declaration to validate.</param>
    /// <returns>Always <see langword="null"/>.</returns>
    public object? VisitFnDeclStmt(FnDeclStmt stmt)
    {
        foreach (var paramType in stmt.ParameterTypes)
        {
            ValidateTypeHint(paramType);
        }
        ValidateTypeHint(stmt.ReturnType);

        _functionDepth++;
        CheckUnreachableStatements(stmt.Body.Statements);
        _functionDepth--;
        return null;
    }

    /// <summary>
    /// Validates the condition expression, then recurses into the loop body within an incremented
    /// loop-depth context to allow <c>break</c>/<c>continue</c> without triggering errors.
    /// </summary>
    /// <param name="stmt">The while statement to validate.</param>
    /// <returns>Always <see langword="null"/>.</returns>
    public object? VisitWhileStmt(WhileStmt stmt)
    {
        stmt.Condition.Accept(this);
        _loopDepth++;
        stmt.Body.Accept(this);
        _loopDepth--;
        return null;
    }

    /// <inheritdoc />
    public object? VisitElevateStmt(ElevateStmt stmt)
    {
        if (_elevateDepth > 0)
        {
            _diagnostics.Add(new SemanticDiagnostic(
                "Nested 'elevate' has no effect. The outer elevation context already applies.",
                DiagnosticLevel.Warning,
                stmt.Span));
        }

        stmt.Elevator?.Accept(this);
        _elevateDepth++;
        stmt.Body.Accept(this);
        _elevateDepth--;
        return null;
    }

    /// <summary>
    /// Recurses into the do-while body (within an incremented loop-depth context), then
    /// validates the condition expression.
    /// </summary>
    /// <param name="stmt">The do-while statement to validate.</param>
    /// <returns>Always <see langword="null"/>.</returns>
    public object? VisitDoWhileStmt(DoWhileStmt stmt)
    {
        _loopDepth++;
        stmt.Body.Accept(this);
        _loopDepth--;
        stmt.Condition.Accept(this);
        return null;
    }

    public object? VisitForStmt(ForStmt stmt)
    {
        if (stmt.Initializer is not null)
        {
            stmt.Initializer.Accept(this);
        }
        if (stmt.Condition is not null)
        {
            stmt.Condition.Accept(this);
        }
        _loopDepth++;
        stmt.Body.Accept(this);
        if (stmt.Increment is not null)
        {
            stmt.Increment.Accept(this);
        }
        _loopDepth--;
        return null;
    }

    /// <summary>
    /// Validates the optional element type annotation, recurses into the iterable expression,
    /// then visits the loop body within an incremented loop-depth context.
    /// </summary>
    /// <param name="stmt">The for-in statement to validate.</param>
    /// <returns>Always <see langword="null"/>.</returns>
    public object? VisitForInStmt(ForInStmt stmt)
    {
        ValidateTypeHint(stmt.TypeHint);
        stmt.Iterable.Accept(this);
        _loopDepth++;
        stmt.Body.Accept(this);
        _loopDepth--;
        return null;
    }

    /// <summary>
    /// Emits a <see cref="DiagnosticLevel.Error"/> diagnostic if <c>break</c> appears outside
    /// any loop (i.e. <see cref="_loopDepth"/> is zero).
    /// </summary>
    /// <param name="stmt">The break statement.</param>
    /// <returns>Always <see langword="null"/>.</returns>
    public object? VisitBreakStmt(BreakStmt stmt)
    {
        if (_loopDepth == 0)
        {
            _diagnostics.Add(new SemanticDiagnostic(
                "'break' used outside of a loop.",
                DiagnosticLevel.Error,
                stmt.Span));
        }
        return null;
    }

    /// <summary>
    /// Emits a <see cref="DiagnosticLevel.Error"/> diagnostic if <c>continue</c> appears outside
    /// any loop (i.e. <see cref="_loopDepth"/> is zero).
    /// </summary>
    /// <param name="stmt">The continue statement.</param>
    /// <returns>Always <see langword="null"/>.</returns>
    public object? VisitContinueStmt(ContinueStmt stmt)
    {
        if (_loopDepth == 0)
        {
            _diagnostics.Add(new SemanticDiagnostic(
                "'continue' used outside of a loop.",
                DiagnosticLevel.Error,
                stmt.Span));
        }
        return null;
    }

    /// <summary>
    /// Emits a <see cref="DiagnosticLevel.Error"/> diagnostic if <c>return</c> appears outside
    /// any function (i.e. <see cref="_functionDepth"/> is zero), then recurses into the return value.
    /// </summary>
    /// <param name="stmt">The return statement.</param>
    /// <returns>Always <see langword="null"/>.</returns>
    public object? VisitReturnStmt(ReturnStmt stmt)
    {
        if (_functionDepth == 0)
        {
            _diagnostics.Add(new SemanticDiagnostic(
                "'return' used outside of a function.",
                DiagnosticLevel.Error,
                stmt.Span));
        }
        stmt.Value?.Accept(this);
        return null;
    }

    /// <summary>
    /// Validates the explicit type annotation (if present) and emits a warning if the inferred
    /// type of the initializer does not match. Recurses into the initializer expression.
    /// </summary>
    /// <param name="stmt">The variable declaration to validate.</param>
    /// <returns>Always <see langword="null"/>.</returns>
    public object? VisitVarDeclStmt(VarDeclStmt stmt)
    {
        ValidateTypeHint(stmt.TypeHint);
        if (stmt.TypeHint != null && stmt.Initializer != null)
        {
            var expectedType = stmt.TypeHint.Lexeme;
            var actualType = TypeInferenceEngine.InferExpressionType(_scopeTree, stmt.Initializer, stmt.Name.Span.StartLine, stmt.Name.Span.StartColumn);
            if (actualType != null && actualType != "null" && actualType != expectedType)
            {
                _diagnostics.Add(new SemanticDiagnostic(
                    $"Variable '{stmt.Name.Lexeme}' is declared as '{expectedType}' but initialized with '{actualType}'.",
                    DiagnosticLevel.Warning,
                    stmt.Initializer.Span));
            }
        }
        stmt.Initializer?.Accept(this);
        return null;
    }

    /// <summary>
    /// Validates the explicit type annotation (if present) and emits a warning if the inferred
    /// type of the initializer does not match. Recurses into the initializer expression.
    /// </summary>
    /// <param name="stmt">The constant declaration to validate.</param>
    /// <returns>Always <see langword="null"/>.</returns>
    public object? VisitConstDeclStmt(ConstDeclStmt stmt)
    {
        ValidateTypeHint(stmt.TypeHint);
        if (stmt.TypeHint != null)
        {
            var expectedType = stmt.TypeHint.Lexeme;
            var actualType = TypeInferenceEngine.InferExpressionType(_scopeTree, stmt.Initializer, stmt.Name.Span.StartLine, stmt.Name.Span.StartColumn);
            if (actualType != null && actualType != "null" && actualType != expectedType)
            {
                _diagnostics.Add(new SemanticDiagnostic(
                    $"Constant '{stmt.Name.Lexeme}' is declared as '{expectedType}' but initialized with '{actualType}'.",
                    DiagnosticLevel.Warning,
                    stmt.Initializer.Span));
            }
        }
        stmt.Initializer.Accept(this);
        return null;
    }

    /// <summary>Recurses into the initializer expression of a destructuring declaration.</summary>
    /// <param name="stmt">The destructuring declaration to validate.</param>
    /// <returns>Always <see langword="null"/>.</returns>
    public object? VisitDestructureStmt(DestructureStmt stmt)
    {
        stmt.Initializer.Accept(this);
        return null;
    }

    /// <summary>Delegates to <see cref="CheckUnreachableStatements"/> for the block's statement list.</summary>
    /// <param name="stmt">The block statement to validate.</param>
    /// <returns>Always <see langword="null"/>.</returns>
    public object? VisitBlockStmt(BlockStmt stmt)
    {
        CheckUnreachableStatements(stmt.Statements);
        return null;
    }

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="stmt"/> unconditionally terminates
    /// control flow, making any following statement unreachable. Handles <c>return</c>,
    /// <c>break</c>, <c>continue</c>, and <c>process.exit(…)</c> calls.
    /// </summary>
    /// <param name="stmt">The statement to check.</param>
    /// <returns><see langword="true"/> if no statement after this one can be reached.</returns>
    private static bool IsTerminatingStatement(Stmt stmt)
    {
        if (stmt is ReturnStmt || stmt is BreakStmt || stmt is ContinueStmt || stmt is ThrowStmt)
        {
            return true;
        }

        // process.exit(...) call
        if (stmt is ExprStmt exprStmt && exprStmt.Expression is CallExpr call &&
            call.Callee is DotExpr dot &&
            dot.Object is IdentifierExpr obj && obj.Name.Lexeme == "process" &&
            dot.Name.Lexeme == "exit")
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// Iterates over <paramref name="statements"/> and emits a
    /// <see cref="DiagnosticLevel.Information"/> / <see cref="SemanticDiagnostic.IsUnnecessary"/>
    /// diagnostic for every statement that follows a terminating statement.
    /// Still visits unreachable statements so that nested semantic errors are not silently swallowed.
    /// </summary>
    /// <param name="statements">The ordered list of statements to scan.</param>
    private void CheckUnreachableStatements(List<Stmt> statements)
    {
        bool reachable = true;
        Stmt? terminatingStmt = null;

        foreach (var stmt in statements)
        {
            if (!reachable)
            {
                _diagnostics.Add(new SemanticDiagnostic(
                    "Unreachable code detected.",
                    DiagnosticLevel.Information,
                    stmt.Span,
                    isUnnecessary: true));
                // Still visit for other diagnostics (e.g., nested errors)
                stmt.Accept(this);
                continue;
            }

            stmt.Accept(this);

            if (IsTerminatingStatement(stmt))
            {
                reachable = false;
                terminatingStmt = stmt;
            }
        }
    }

    private void CheckUnusedSymbols()
    {
        var globalSymbols = new HashSet<SymbolInfo>(_scopeTree.GlobalScope.Symbols);

        foreach (var symbol in _scopeTree.All)
        {
            // Skip built-ins (injected at line 0)
            if (symbol.Span.StartLine == 0)
            {
                continue;
            }

            // Skip intentionally unused names (convention)
            if (symbol.Name == "_")
            {
                continue;
            }

            bool isTopLevel = globalSymbols.Contains(symbol);

            // Top-level functions, structs, and enums are auto-exported (public API), so only
            // flag them if they're imported. Variables and constants at file scope are still
            // checked — they're typically not imported by name and unused ones are noise.
            if (isTopLevel && symbol.SourceUri == null
                && symbol.Kind is SymbolKind.Function or SymbolKind.Struct or SymbolKind.Enum)
            {
                continue;
            }

            // Only check variables, constants, loop variables, and imported namespaces
            // Parameters are excluded — they are commonly unused in callbacks and interface implementations
            if (symbol.Kind is not (SymbolKind.Variable or SymbolKind.Constant
                or SymbolKind.LoopVariable or SymbolKind.Namespace
                or SymbolKind.Function or SymbolKind.Struct or SymbolKind.Enum))
            {
                continue;
            }

            // For non-imported top-level symbols, we already skipped above.
            // For non-top-level Function/Struct/Enum, skip — these are rare and usually intentional.
            if (!isTopLevel && symbol.Kind is SymbolKind.Function or SymbolKind.Struct or SymbolKind.Enum)
            {
                continue;
            }

            // Check whether any reference in the tree resolves to this symbol.
            // We scan References directly rather than using FindReferences() because
            // FindReferences relies on position-based scope lookup which fails for:
            //  - Loop variables: token is in the FOR header but scope span is the body
            //  - Namespace imports: SymbolInfo is replaced after reference recording
            bool isUsed = false;
            foreach (var r in _scopeTree.References)
            {
                if (r.ResolvedSymbol == symbol
                    || (symbol.Kind == SymbolKind.Namespace && r.Name == symbol.Name && r.ResolvedSymbol != null))
                {
                    isUsed = true;
                    break;
                }
            }
            if (isUsed)
            {
                continue;
            }

            string label = symbol.Kind switch
            {
                SymbolKind.LoopVariable => "Loop variable",
                SymbolKind.Constant => "Constant",
                SymbolKind.Namespace => "Import",
                _ when symbol.SourceUri != null => "Import",
                _ => "Variable"
            };

            _diagnostics.Add(new SemanticDiagnostic(
                $"{label} '{symbol.Name}' is declared but never used.",
                DiagnosticLevel.Information,
                symbol.Span,
                isUnnecessary: true));
        }
    }

    /// <summary>Recurses into the condition and both branches of an if statement.</summary>
    /// <param name="stmt">The if statement.</param>
    /// <returns>Always <see langword="null"/>.</returns>
    public object? VisitIfStmt(IfStmt stmt)
    {
        stmt.Condition.Accept(this);
        stmt.ThenBranch.Accept(this);
        stmt.ElseBranch?.Accept(this);
        return null;
    }

    /// <summary>Recurses into the expression of an expression statement.</summary>
    /// <param name="stmt">The expression statement.</param>
    /// <returns>Always <see langword="null"/>.</returns>
    public object? VisitExprStmt(ExprStmt stmt)
    {
        stmt.Expression.Accept(this);
        return null;
    }

    /// <summary>
    /// Validates all field type annotations and method signatures/bodies of a struct declaration.
    /// Method bodies are validated within an incremented function-depth context.
    /// </summary>
    /// <param name="stmt">The struct declaration to validate.</param>
    /// <returns>Always <see langword="null"/>.</returns>
    public object? VisitStructDeclStmt(StructDeclStmt stmt)
    {
        foreach (var fieldType in stmt.FieldTypes)
        {
            ValidateTypeHint(fieldType);
        }

        // Validate method bodies
        foreach (var method in stmt.Methods)
        {
            foreach (var paramType in method.ParameterTypes)
            {
                ValidateTypeHint(paramType);
            }
            ValidateTypeHint(method.ReturnType);

            _functionDepth++;
            CheckUnreachableStatements(method.Body.Statements);
            _functionDepth--;
        }

        return null;
    }
    /// <summary>No-op — enum declarations introduce no semantic constraints to validate.</summary>
    /// <returns>Always <see langword="null"/>.</returns>
    public object? VisitEnumDeclStmt(EnumDeclStmt stmt) => null;

    /// <summary>Validates type hints in interface field and method signatures.</summary>
    /// <returns>Always <see langword="null"/>.</returns>
    public object? VisitInterfaceDeclStmt(InterfaceDeclStmt stmt)
    {
        foreach (var fieldType in stmt.FieldTypes)
        {
            ValidateTypeHint(fieldType);
        }
        foreach (var method in stmt.Methods)
        {
            foreach (var paramType in method.ParameterTypes)
            {
                ValidateTypeHint(paramType);
            }
            ValidateTypeHint(method.ReturnType);
        }
        return null;
    }

    /// <summary>No-op — import statements are validated separately by <see cref="ImportResolver"/>.</summary>
    /// <returns>Always <see langword="null"/>.</returns>
    public object? VisitImportStmt(ImportStmt stmt) => null;

    /// <summary>No-op — import-as statements are validated separately by <see cref="ImportResolver"/>.</summary>
    /// <returns>Always <see langword="null"/>.</returns>
    public object? VisitImportAsStmt(ImportAsStmt stmt) => null;

    /// <inheritdoc />
    public object? VisitThrowStmt(ThrowStmt stmt)
    {
        stmt.Value.Accept(this);
        return null;
    }

    /// <inheritdoc />
    public object? VisitTryCatchStmt(TryCatchStmt stmt)
    {
        stmt.TryBody.Accept(this);
        stmt.CatchBody?.Accept(this);
        stmt.FinallyBody?.Accept(this);
        return null;
    }

    // Expression visitors

    /// <summary>
    /// Validates a variable assignment: emits an error if the target is a constant, and
    /// emits a warning if the assigned value's inferred type mismatches the target's explicit
    /// type annotation. Then recurses into the right-hand side value.
    /// </summary>
    /// <param name="expr">The assignment expression to validate.</param>
    /// <returns>Always <see langword="null"/>.</returns>
    public object? VisitAssignExpr(AssignExpr expr)
    {
        var line = expr.Name.Span.StartLine;
        var col = expr.Name.Span.StartColumn;
        var definition = _scopeTree.FindDefinition(expr.Name.Lexeme, line, col);
        if (definition != null && definition.Kind == SymbolKind.Constant)
        {
            _diagnostics.Add(new SemanticDiagnostic(
                $"Cannot reassign constant '{expr.Name.Lexeme}'.",
                DiagnosticLevel.Error,
                expr.Name.Span));
        }
        if (definition != null && definition.IsExplicitTypeHint && definition.TypeHint != null)
        {
            var valueType = TypeInferenceEngine.InferExpressionType(_scopeTree, expr.Value, line, col);
            if (valueType != null && valueType != "null" && valueType != definition.TypeHint)
            {
                _diagnostics.Add(new SemanticDiagnostic(
                    $"Cannot assign value of type '{valueType}' to variable '{expr.Name.Lexeme}' of type '{definition.TypeHint}'.",
                    DiagnosticLevel.Warning,
                    expr.Name.Span));
            }
        }
        expr.Value.Accept(this);
        return null;
    }

    /// <summary>
    /// Validates a function call: checks arity (too-few / too-many arguments) and per-argument
    /// type compatibility against the callee's parameter types. For built-in namespace calls
    /// (e.g. <c>http.get</c>), validates arity against <see cref="StdlibRegistry"/>.
    /// Recurses into all argument expressions.
    /// </summary>
    /// <param name="expr">The call expression to validate.</param>
    /// <returns>Always <see langword="null"/>.</returns>
    public object? VisitCallExpr(CallExpr expr)
    {
        if (expr.Callee is IdentifierExpr id)
        {
            var line = id.Span.StartLine;
            var col = id.Span.StartColumn;
            var definition = _scopeTree.FindDefinition(id.Name.Lexeme, line, col);
            if (definition != null && definition.Kind == SymbolKind.Function)
            {
                var paramCount = definition.ParameterNames != null
                    ? definition.ParameterNames.Length
                    : CountParameters(definition.Detail ?? "");
                if (paramCount >= 0)
                {
                    int requiredCount = definition.RequiredParameterCount ?? paramCount;
                    if (expr.Arguments.Count < requiredCount || expr.Arguments.Count > paramCount)
                    {
                        string expected = requiredCount == paramCount
                            ? $"{paramCount}"
                            : $"{requiredCount} to {paramCount}";
                        _diagnostics.Add(new SemanticDiagnostic(
                            $"Expected {expected} arguments but got {expr.Arguments.Count}.",
                            DiagnosticLevel.Error,
                            expr.Paren.Span));
                    }
                }

                // Type-check arguments against parameter type hints
                if (definition.ParameterTypes != null)
                {
                    for (int i = 0; i < expr.Arguments.Count && i < definition.ParameterTypes.Length; i++)
                    {
                        var expectedType = definition.ParameterTypes[i];
                        if (expectedType == null)
                        {
                            continue;
                        }

                        var argType = TypeInferenceEngine.InferExpressionType(_scopeTree, expr.Arguments[i], line, col);
                        if (argType != null && argType != "null" && argType != expectedType)
                        {
                            var paramName = definition.ParameterNames != null && i < definition.ParameterNames.Length
                                ? definition.ParameterNames[i]
                                : $"argument {i + 1}";
                            _diagnostics.Add(new SemanticDiagnostic(
                                $"Argument '{paramName}' expects type '{expectedType}' but got '{argType}'.",
                                DiagnosticLevel.Warning,
                                expr.Arguments[i].Span));
                        }
                    }
                }
            }
        }
        else if (expr.Callee is DotExpr dot && dot.Object is IdentifierExpr nsId &&
                 StdlibRegistry.IsBuiltInNamespace(nsId.Name.Lexeme))
        {
            var qualifiedName = $"{nsId.Name.Lexeme}.{dot.Name.Lexeme}";
            if (StdlibRegistry.TryGetNamespaceFunction(qualifiedName, out var func) &&
                !func.IsVariadic &&
                expr.Arguments.Count != func.Parameters.Length)
            {
                _diagnostics.Add(new SemanticDiagnostic(
                    $"Expected {func.Parameters.Length} arguments but got {expr.Arguments.Count}.",
                    DiagnosticLevel.Error,
                    expr.Paren.Span));
            }
        }
        else
        {
            expr.Callee.Accept(this);
        }

        foreach (var arg in expr.Arguments)
        {
            arg.Accept(this);
        }
        return null;
    }

    /// <summary>No-op — literals have no semantic constraints to validate.</summary>
    /// <returns>Always <see langword="null"/>.</returns>
    public object? VisitLiteralExpr(LiteralExpr expr) => null;

    /// <summary>No-op — identifier resolution is handled by the unresolved-references pass in <see cref="Validate"/>.</summary>
    /// <returns>Always <see langword="null"/>.</returns>
    public object? VisitIdentifierExpr(IdentifierExpr expr) => null;

    /// <summary>Recurses into the operand of a unary expression.</summary>
    /// <returns>Always <see langword="null"/>.</returns>
    public object? VisitUnaryExpr(UnaryExpr expr)
    {
        expr.Right.Accept(this);
        return null;
    }

    /// <summary>Recurses into the operand of an update expression (<c>++</c>/<c>--</c>).</summary>
    /// <returns>Always <see langword="null"/>.</returns>
    public object? VisitUpdateExpr(UpdateExpr expr)
    {
        expr.Operand.Accept(this);
        return null;
    }

    /// <summary>Recurses into both operands of a binary expression.</summary>
    /// <returns>Always <see langword="null"/>.</returns>
    public object? VisitBinaryExpr(BinaryExpr expr)
    {
        expr.Left.Accept(this);
        expr.Right.Accept(this);
        return null;
    }

    public object? VisitIsExpr(IsExpr expr)
    {
        expr.Left.Accept(this);
        if (expr.TypeExpr != null)
        {
            expr.TypeExpr.Accept(this);
        }
        return null;
    }

    /// <summary>Recurses into the inner expression of a grouping.</summary>
    /// <returns>Always <see langword="null"/>.</returns>
    public object? VisitGroupingExpr(GroupingExpr expr)
    {
        expr.Expression.Accept(this);
        return null;
    }

    /// <summary>Recurses into the condition and both branches of a ternary expression.</summary>
    /// <returns>Always <see langword="null"/>.</returns>
    public object? VisitTernaryExpr(TernaryExpr expr)
    {
        expr.Condition.Accept(this);
        expr.ThenBranch.Accept(this);
        expr.ElseBranch.Accept(this);
        return null;
    }

    /// <summary>Recurses into the subject and all arm patterns and bodies of a switch expression.</summary>
    /// <returns>Always <see langword="null"/>.</returns>
    public object? VisitSwitchExpr(SwitchExpr expr)
    {
        expr.Subject.Accept(this);
        foreach (var arm in expr.Arms)
        {
            arm.Pattern?.Accept(this);
            arm.Body.Accept(this);
        }
        return null;
    }

    /// <summary>Recurses into all element expressions of an array literal.</summary>
    /// <returns>Always <see langword="null"/>.</returns>
    public object? VisitArrayExpr(ArrayExpr expr)
    {
        foreach (var el in expr.Elements)
        {
            el.Accept(this);
        }

        return null;
    }

    /// <summary>Recurses into the value expression of each entry in a dict literal.</summary>
    /// <returns>Always <see langword="null"/>.</returns>
    public object? VisitDictLiteralExpr(DictLiteralExpr expr)
    {
        foreach (var (_, value) in expr.Entries)
        {
            value.Accept(this);
        }

        return null;
    }

    /// <summary>Recurses into the object and index sub-expressions of an index expression.</summary>
    /// <returns>Always <see langword="null"/>.</returns>
    public object? VisitIndexExpr(IndexExpr expr)
    {
        expr.Object.Accept(this);
        expr.Index.Accept(this);
        return null;
    }

    /// <summary>Recurses into the object, index, and assigned value of an index-assignment expression.</summary>
    /// <returns>Always <see langword="null"/>.</returns>
    public object? VisitIndexAssignExpr(IndexAssignExpr expr)
    {
        expr.Object.Accept(this);
        expr.Index.Accept(this);
        expr.Value.Accept(this);
        return null;
    }

    /// <summary>Recurses into all field-value expressions of a struct initializer.</summary>
    /// <returns>Always <see langword="null"/>.</returns>
    public object? VisitStructInitExpr(StructInitExpr expr)
    {
        foreach (var (_, value) in expr.FieldValues)
        {
            value.Accept(this);
        }

        return null;
    }

    /// <summary>Recurses into the object sub-expression of a dot-access expression.</summary>
    /// <returns>Always <see langword="null"/>.</returns>
    public object? VisitDotExpr(DotExpr expr)
    {
        expr.Object.Accept(this);
        return null;
    }

    /// <summary>
    /// Recurses into the object and assigned value, then checks that the value's inferred type
    /// is compatible with the field's declared type. Emits a warning on mismatch.
    /// </summary>
    /// <param name="expr">The dot-assignment expression to validate.</param>
    /// <returns>Always <see langword="null"/>.</returns>
    public object? VisitDotAssignExpr(DotAssignExpr expr)
    {
        expr.Object.Accept(this);
        expr.Value.Accept(this);

        // Check field type compatibility for struct field assignments
        var line = expr.Name.Span.StartLine;
        var col = expr.Name.Span.StartColumn;
        var receiverType = TypeInferenceEngine.InferExpressionType(_scopeTree, expr.Object, line, col);
        if (receiverType != null)
        {
            var field = _scopeTree.FindField(receiverType, expr.Name.Lexeme);
            if (field?.TypeHint != null)
            {
                var valueType = TypeInferenceEngine.InferExpressionType(_scopeTree, expr.Value, line, col);
                if (valueType != null && valueType != "null" && valueType != field.TypeHint)
                {
                    _diagnostics.Add(new SemanticDiagnostic(
                        $"Cannot assign value of type '{valueType}' to field '{expr.Name.Lexeme}' of type '{field.TypeHint}'.",
                        DiagnosticLevel.Warning,
                        expr.Name.Span));
                }
            }
        }

        return null;
    }

    /// <summary>Recurses into each interpolated part of an interpolated string expression.</summary>
    /// <returns>Always <see langword="null"/>.</returns>
    public object? VisitInterpolatedStringExpr(InterpolatedStringExpr expr)
    {
        foreach (var part in expr.Parts)
        {
            part.Accept(this);
        }

        return null;
    }

    /// <summary>Recurses into each interpolated part of a shell command expression.</summary>
    /// <returns>Always <see langword="null"/>.</returns>
    public object? VisitCommandExpr(CommandExpr expr)
    {
        foreach (var part in expr.Parts)
        {
            part.Accept(this);
        }
        return null;
    }

    /// <summary>Recurses into both sides of a pipe expression.</summary>
    /// <returns>Always <see langword="null"/>.</returns>
    public object? VisitPipeExpr(PipeExpr expr)
    {
        expr.Left.Accept(this);
        expr.Right.Accept(this);
        return null;
    }

    /// <summary>Recurses into the source expression and redirect target of a redirect expression.</summary>
    /// <returns>Always <see langword="null"/>.</returns>
    public object? VisitRedirectExpr(RedirectExpr expr)
    {
        expr.Expression.Accept(this);
        expr.Target.Accept(this);
        return null;
    }

    /// <summary>Recurses into the inner expression of a <c>try</c> expression.</summary>
    /// <returns>Always <see langword="null"/>.</returns>
    public object? VisitTryExpr(TryExpr expr)
    {
        expr.Expression.Accept(this);
        return null;
    }

    /// <inheritdoc />
    public object? VisitAwaitExpr(AwaitExpr expr)
    {
        expr.Expression.Accept(this);
        return null;
    }

    /// <summary>Recurses into both sides of a null-coalescing expression (<c>??</c>).</summary>
    /// <returns>Always <see langword="null"/>.</returns>
    public object? VisitNullCoalesceExpr(NullCoalesceExpr expr)
    {
        expr.Left.Accept(this);
        expr.Right.Accept(this);
        return null;
    }

    /// <summary>Recurses into the start, end, and optional step of a range expression.</summary>
    /// <returns>Always <see langword="null"/>.</returns>
    public object? VisitRangeExpr(RangeExpr expr)
    {
        expr.Start.Accept(this);
        expr.End.Accept(this);
        expr.Step?.Accept(this);
        return null;
    }

    /// <summary>
    /// Validates each parameter type annotation, then recurses into the lambda body
    /// (expression or block) within an incremented function-depth context.
    /// </summary>
    /// <param name="expr">The lambda expression to validate.</param>
    /// <returns>Always <see langword="null"/>.</returns>
    public object? VisitLambdaExpr(LambdaExpr expr)
    {
        foreach (var paramType in expr.ParameterTypes)
        {
            if (paramType != null)
            {
                ValidateTypeHint(paramType);
            }
        }

        _functionDepth++;

        if (expr.ExpressionBody != null)
        {
            expr.ExpressionBody.Accept(this);
        }
        else if (expr.BlockBody != null)
        {
            expr.BlockBody.Accept(this);
        }

        _functionDepth--;
        return null;
    }

    // Helper to count parameters from detail string "fn name(a, b)" or "fn name()"

    /// <summary>
    /// Parses the parameter count from a <see cref="SymbolInfo.Detail"/> signature string
    /// of the form <c>"fn name(a, b)"</c>. Returns <c>-1</c> if the string cannot be parsed.
    /// Used as a fallback when <see cref="SymbolInfo.ParameterNames"/> is unavailable.
    /// </summary>
    /// <param name="detail">The signature detail string to parse.</param>
    /// <returns>The number of comma-separated parameters, or <c>-1</c> on parse failure.</returns>
    private static int CountParameters(string detail)
    {
        var openParen = detail.IndexOf('(');
        var closeParen = detail.IndexOf(')');
        if (openParen < 0 || closeParen < 0 || closeParen <= openParen)
        {
            return -1;
        }

        var inside = detail[(openParen + 1)..closeParen].Trim();
        if (string.IsNullOrEmpty(inside))
        {
            return 0;
        }

        return inside.Split(',').Length;
    }
}
