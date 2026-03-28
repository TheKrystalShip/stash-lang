namespace Stash.Analysis;

using System.Collections.Generic;
using System.Linq;
using Stash.Common;
using Stash.Parsing.AST;

/// <summary>
/// Walks the AST to collect symbol definitions (variables, functions, structs, enums,
/// imports) into a hierarchical <see cref="ScopeTree"/>.
/// </summary>
/// <remarks>
/// <para>
/// Produces <see cref="SymbolInfo"/> entries for each declaration, tracking name, kind,
/// type, source span, and documentation. The resulting scope tree is used by handlers
/// for go-to-definition, references, completion, and rename operations.
/// </para>
/// <para>
/// In addition to declarations, the collector records every identifier use as a
/// <see cref="ReferenceInfo"/>, resolving each reference to the nearest in-scope
/// <see cref="SymbolInfo"/> declared before the usage position. These references are
/// stored in the returned <see cref="ScopeTree.References"/> list and consumed by
/// <see cref="ScopeTree.FindReferences"/> and <see cref="SemanticValidator"/>.
/// </para>
/// <para>
/// Built-in symbols (functions, structs, namespaces from <see cref="BuiltInRegistry"/>)
/// are pre-populated into the global scope when <see cref="IncludeBuiltIns"/> is
/// <see langword="true"/> (the default). They are assigned span line 0 so that
/// position-based filters exclude them from user-facing declaration lists.
/// </para>
/// </remarks>
public class SymbolCollector : IStmtVisitor<object?>, IExprVisitor<object?>
{
    /// <summary>The scope currently being populated during AST traversal.</summary>
    private Scope _currentScope = null!;

    /// <summary>All identifier references accumulated during traversal.</summary>
    private readonly List<ReferenceInfo> _references = new();

    /// <summary>
    /// Holds narrowing info extracted from an <c>is</c> expression condition.  The
    /// <see cref="BlockStmt"/> target ensures only the exact then-branch block applies
    /// the narrowing, preventing accidental consumption by unrelated blocks.
    /// </summary>
    private (string Name, string TypeHint, BlockStmt Target)? _pendingNarrowing;

    /// <summary>
    /// Gets or sets whether built-in symbols from <see cref="BuiltInRegistry"/> are
    /// pre-registered into the global scope before traversal begins. Defaults to <see langword="true"/>.
    /// Set to <see langword="false"/> in tests or when built-ins are injected separately.
    /// </summary>
    public bool IncludeBuiltIns { get; set; } = true;

    /// <summary>
    /// Traverses <paramref name="statements"/> and returns a fully populated
    /// <see cref="ScopeTree"/> containing all declared symbols and recorded references.
    /// </summary>
    /// <param name="statements">The top-level AST statements of the document to analyze.</param>
    /// <returns>
    /// A <see cref="ScopeTree"/> rooted at a new global scope, with nested child scopes for
    /// each function, block, and loop body encountered during traversal.
    /// </returns>
    public ScopeTree Collect(List<Stmt> statements)
    {
        _references.Clear();
        var file = statements.Count > 0 ? statements[0].Span.File : "";
        var globalSpan = new SourceSpan(file, 1, 1, int.MaxValue, int.MaxValue);
        var globalScope = new Scope(ScopeKind.Global, null, globalSpan);
        _currentScope = globalScope;

        if (IncludeBuiltIns)
        {
            RegisterBuiltIns(file);
        }

        foreach (var stmt in statements)
        {
            stmt.Accept(this);
        }

        return new ScopeTree(globalScope, _references);
    }

    /// <summary>
    /// Registers all built-in symbols from <see cref="BuiltInRegistry"/> into the current
    /// (global) scope at source line 0 so they are always visible but excluded from
    /// user-facing declaration lists.
    /// </summary>
    /// <param name="file">The file path used to construct synthetic zero-position spans.</param>
    private void RegisterBuiltIns(string file)
    {
        var span = new SourceSpan(file, 0, 0, 0, 0);

        foreach (var s in BuiltInRegistry.Structs)
        {
            _currentScope.AddSymbol(new SymbolInfo(s.Name, SymbolKind.Struct, span, span, s.Detail));
            foreach (var f in s.Fields)
            {
                var fieldDetail = f.Type != null ? $"field of {s.Name}: {f.Type}" : $"field of {s.Name}";
                _currentScope.AddSymbol(new SymbolInfo(f.Name, SymbolKind.Field, span, detail: fieldDetail, parentName: s.Name, typeHint: f.Type));
            }
        }

        foreach (var e in BuiltInRegistry.Enums)
        {
            _currentScope.AddSymbol(new SymbolInfo(e.Name, SymbolKind.Enum, span, span, e.Detail, parentName: e.Namespace));
            foreach (var member in e.Members)
            {
                _currentScope.AddSymbol(new SymbolInfo(member, SymbolKind.EnumMember, span, detail: $"member of {e.Name}", parentName: e.Name));
            }
        }

        foreach (var fn in BuiltInRegistry.Functions)
        {
            _currentScope.AddSymbol(new SymbolInfo(fn.Name, SymbolKind.Function, span, span, fn.Detail, typeHint: fn.ReturnType, parameterNames: fn.ParamNames));
        }

        foreach (var ns in BuiltInRegistry.NamespaceNames)
        {
            _currentScope.AddSymbol(new SymbolInfo(ns, SymbolKind.Namespace, span, detail: $"namespace {ns}"));
        }
    }

    /// <summary>
    /// Records an identifier use at <paramref name="span"/> as a <see cref="ReferenceInfo"/>,
    /// resolving it to the nearest in-scope symbol declared before that position.
    /// </summary>
    /// <param name="name">The identifier name being referenced.</param>
    /// <param name="span">The source span of the identifier token.</param>
    /// <param name="kind">Whether this use is a read, write, call, or type-use reference.</param>
    private void RecordReference(string name, SourceSpan span, ReferenceKind kind)
    {
        var resolved = FindSymbolInScopeChain(name, span.StartLine, span.StartColumn);
        _references.Add(new ReferenceInfo(name, span, kind, resolved));
    }

    /// <summary>
    /// Searches the scope chain from the current scope outward for a symbol named
    /// <paramref name="name"/> that is declared at or before the given source position.
    /// Returns the innermost (most recently declared) match, or <see langword="null"/> if none.
    /// </summary>
    /// <param name="name">The identifier to resolve.</param>
    /// <param name="line">One-based line of the usage site.</param>
    /// <param name="column">One-based column of the usage site.</param>
    /// <returns>The resolved <see cref="SymbolInfo"/>, or <see langword="null"/> if undeclared.</returns>
    private SymbolInfo? FindSymbolInScopeChain(string name, int line, int column)
    {
        var scope = _currentScope;
        while (scope != null)
        {
            for (int i = scope.Symbols.Count - 1; i >= 0; i--)
            {
                var sym = scope.Symbols[i];
                if (sym.Name == name && (sym.Span.StartLine < line || (sym.Span.StartLine == line && sym.Span.StartColumn <= column)))
                {
                    return sym;
                }
            }
            scope = scope.Parent;
        }
        return null;
    }

    /// <summary>
    /// Creates a new child scope of the given <paramref name="kind"/> nested inside the current
    /// scope and makes it the active scope for subsequent symbol registrations.
    /// </summary>
    /// <param name="kind">The syntactic kind of the new scope.</param>
    /// <param name="span">The source range covered by the new scope.</param>
    private void PushScope(ScopeKind kind, SourceSpan span)
    {
        var scope = new Scope(kind, _currentScope, span);
        _currentScope = scope;
    }

    /// <summary>
    /// Restores the active scope to the parent of the current scope after all symbols in
    /// the current scope have been registered.
    /// </summary>
    private void PopScope()
    {
        _currentScope = _currentScope.Parent!;
    }

    // Statement visitors

    /// <summary>
    /// Registers a <see cref="SymbolKind.Function"/> symbol in the current scope, then opens a
    /// function scope for the parameters and body statements.
    /// </summary>
    /// <param name="stmt">The function declaration statement.</param>
    /// <returns>Always <see langword="null"/>.</returns>
    public object? VisitFnDeclStmt(FnDeclStmt stmt)
    {
        var paramParts = new List<string>();
        for (int i = 0; i < stmt.Parameters.Count; i++)
        {
            var paramName = stmt.Parameters[i].Lexeme;
            var paramType = i < stmt.ParameterTypes.Count ? stmt.ParameterTypes[i]?.Lexeme : null;
            var part = paramType != null ? $"{paramName}: {paramType}" : paramName;

            if (i < stmt.DefaultValues.Count && stmt.DefaultValues[i] != null)
            {
                part += $" = {FormatDefaultValue(stmt.DefaultValues[i]!)}";
            }

            paramParts.Add(part);
        }

        var detail = $"fn {stmt.Name.Lexeme}({string.Join(", ", paramParts)})";
        if (stmt.ReturnType != null)
        {
            detail += $" -> {stmt.ReturnType.Lexeme}";
        }

        var returnTypeStr = stmt.ReturnType?.Lexeme;
        var paramNames = stmt.Parameters.Select(p => p.Lexeme).ToArray();
        int requiredCount = stmt.DefaultValues.TakeWhile(d => d == null).Count();
        var paramTypes = stmt.ParameterTypes.Select(t => t?.Lexeme).ToArray();

        // Function name goes into the parent (current) scope
        _currentScope.AddSymbol(new SymbolInfo(stmt.Name.Lexeme, SymbolKind.Function, stmt.Name.Span, stmt.Span, detail, typeHint: returnTypeStr, parameterNames: paramNames, requiredParameterCount: requiredCount, parameterTypes: paramTypes));

        // Visit default value expressions for reference collection
        foreach (var defaultVal in stmt.DefaultValues)
        {
            defaultVal?.Accept(this);
        }

        // Parameters and body statements share the function scope
        PushScope(ScopeKind.Function, stmt.Body.Span);
        for (int i = 0; i < stmt.Parameters.Count; i++)
        {
            var param = stmt.Parameters[i];
            var paramType = i < stmt.ParameterTypes.Count ? stmt.ParameterTypes[i]?.Lexeme : null;
            var paramDetail = paramType != null ? $"parameter of {stmt.Name.Lexeme}: {paramType}" : $"parameter of {stmt.Name.Lexeme}";
            _currentScope.AddSymbol(new SymbolInfo(param.Lexeme, SymbolKind.Parameter, param.Span, detail: paramDetail, parentName: stmt.Name.Lexeme, typeHint: paramType, isExplicitTypeHint: paramType != null));
        }

        foreach (var s in stmt.Body.Statements)
        {
            s.Accept(this);
        }

        PopScope();
        return null;
    }

    /// <summary>
    /// Produces a human-readable representation of a parameter default-value expression
    /// for inclusion in function signature <see cref="SymbolInfo.Detail"/> strings.
    /// </summary>
    /// <param name="expr">The default-value expression.</param>
    /// <returns>A compact source-like string (e.g. <c>"null"</c>, <c>"true"</c>, <c>"42"</c>).</returns>
    private static string FormatDefaultValue(Expr expr)
    {
        return expr switch
        {
            LiteralExpr lit => lit.Value switch
            {
                null => "null",
                string s => $"\"{ s}\"",
                bool b => b ? "true" : "false",
                _ => lit.Value.ToString() ?? "null"
            },
            IdentifierExpr id => id.Name.Lexeme,
            UnaryExpr u => $"{u.Operator.Lexeme}{FormatDefaultValue(u.Right)}",
            _ => "..."
        };
    }

    /// <summary>
    /// Registers a <see cref="SymbolKind.Struct"/> symbol, its <see cref="SymbolKind.Field"/>
    /// children, and each <see cref="SymbolKind.Method"/> (with its own function scope, parameters,
    /// and implicit <c>self</c> parameter) into the current scope.
    /// </summary>
    /// <param name="stmt">The struct declaration statement.</param>
    /// <returns>Always <see langword="null"/>.</returns>
    public object? VisitStructDeclStmt(StructDeclStmt stmt)
    {
        var fieldParts = new List<string>();
        for (int i = 0; i < stmt.Fields.Count; i++)
        {
            var fieldName = stmt.Fields[i].Lexeme;
            var fieldType = i < stmt.FieldTypes.Count ? stmt.FieldTypes[i]?.Lexeme : null;
            fieldParts.Add(fieldType != null ? $"{fieldName}: {fieldType}" : fieldName);
        }

        var detail = $"struct {stmt.Name.Lexeme} {{ {string.Join(", ", fieldParts)} }}";

        _currentScope.AddSymbol(new SymbolInfo(stmt.Name.Lexeme, SymbolKind.Struct, stmt.Name.Span, stmt.Span, detail));

        for (int i = 0; i < stmt.Fields.Count; i++)
        {
            var field = stmt.Fields[i];
            var fieldType = i < stmt.FieldTypes.Count ? stmt.FieldTypes[i]?.Lexeme : null;
            var fieldDetail = fieldType != null ? $"field of {stmt.Name.Lexeme}: {fieldType}" : $"field of {stmt.Name.Lexeme}";
            _currentScope.AddSymbol(new SymbolInfo(field.Lexeme, SymbolKind.Field, field.Span, detail: fieldDetail, parentName: stmt.Name.Lexeme, typeHint: fieldType));
        }

        // Emit method symbols
        foreach (var method in stmt.Methods)
        {
            var paramParts = new List<string>();
            for (int i = 0; i < method.Parameters.Count; i++)
            {
                var paramName = method.Parameters[i].Lexeme;
                var paramType = i < method.ParameterTypes.Count ? method.ParameterTypes[i]?.Lexeme : null;
                var part = paramType != null ? $"{paramName}: {paramType}" : paramName;

                if (i < method.DefaultValues.Count && method.DefaultValues[i] != null)
                {
                    part += $" = {FormatDefaultValue(method.DefaultValues[i]!)}";
                }

                paramParts.Add(part);
            }

            var methodDetail = $"fn {method.Name.Lexeme}({string.Join(", ", paramParts)})";
            if (method.ReturnType != null)
            {
                methodDetail += $" -> {method.ReturnType.Lexeme}";
            }

            var returnTypeStr = method.ReturnType?.Lexeme;
            var paramNames = method.Parameters.Select(p => p.Lexeme).ToArray();
            int requiredCount = method.DefaultValues.TakeWhile(d => d == null).Count();
            var methodParamTypes = method.ParameterTypes.Select(t => t?.Lexeme).ToArray();

            _currentScope.AddSymbol(new SymbolInfo(method.Name.Lexeme, SymbolKind.Method, method.Name.Span, method.Span, methodDetail,
                parentName: stmt.Name.Lexeme, typeHint: returnTypeStr, parameterNames: paramNames, requiredParameterCount: requiredCount, parameterTypes: methodParamTypes));

            // Visit default value expressions for reference collection
            foreach (var defaultVal in method.DefaultValues)
            {
                defaultVal?.Accept(this);
            }

            // Push scope for method body (parameters + self)
            PushScope(ScopeKind.Function, method.Body.Span);

            // Register self as implicit parameter
            _currentScope.AddSymbol(new SymbolInfo("self", SymbolKind.Parameter, method.Name.Span,
                detail: $"instance of {stmt.Name.Lexeme}", parentName: method.Name.Lexeme, typeHint: stmt.Name.Lexeme));

            for (int i = 0; i < method.Parameters.Count; i++)
            {
                var param = method.Parameters[i];
                var paramType = i < method.ParameterTypes.Count ? method.ParameterTypes[i]?.Lexeme : null;
                var paramDetail = paramType != null ? $"parameter of {method.Name.Lexeme}: {paramType}" : $"parameter of {method.Name.Lexeme}";
                _currentScope.AddSymbol(new SymbolInfo(param.Lexeme, SymbolKind.Parameter, param.Span, detail: paramDetail, parentName: method.Name.Lexeme, typeHint: paramType, isExplicitTypeHint: paramType != null));
            }

            foreach (var s in method.Body.Statements)
            {
                s.Accept(this);
            }

            PopScope();
        }

        return null;
    }

    /// <summary>
    /// Registers a <see cref="SymbolKind.Enum"/> symbol and all its
    /// <see cref="SymbolKind.EnumMember"/> children into the current scope.
    /// </summary>
    /// <param name="stmt">The enum declaration statement.</param>
    /// <returns>Always <see langword="null"/>.</returns>
    public object? VisitEnumDeclStmt(EnumDeclStmt stmt)
    {
        var memberNames = new List<string>();
        foreach (var m in stmt.Members)
        {
            memberNames.Add(m.Lexeme);
        }

        var detail = $"enum {stmt.Name.Lexeme} {{ {string.Join(", ", memberNames)} }}";

        _currentScope.AddSymbol(new SymbolInfo(stmt.Name.Lexeme, SymbolKind.Enum, stmt.Name.Span, stmt.Span, detail));

        foreach (var member in stmt.Members)
        {
            _currentScope.AddSymbol(new SymbolInfo(member.Lexeme, SymbolKind.EnumMember, member.Span, detail: $"member of {stmt.Name.Lexeme}", parentName: stmt.Name.Lexeme));
        }

        return null;
    }

    public object? VisitInterfaceDeclStmt(InterfaceDeclStmt stmt)
    {
        _currentScope.AddSymbol(new SymbolInfo(stmt.Name.Lexeme, SymbolKind.Interface, stmt.Name.Span, stmt.Span, $"interface {stmt.Name.Lexeme}"));
        return null;
    }

    /// <summary>
    /// Registers a <see cref="SymbolKind.Variable"/> symbol for a <c>let</c> declaration,
    /// capturing an explicit type annotation when present, then recurses into the initializer.
    /// </summary>
    /// <param name="stmt">The variable declaration statement.</param>
    /// <returns>Always <see langword="null"/>.</returns>
    public object? VisitVarDeclStmt(VarDeclStmt stmt)
    {
        var typeStr = stmt.TypeHint?.Lexeme;
        var detail = typeStr != null ? $"let {stmt.Name.Lexeme}: {typeStr}" : $"let {stmt.Name.Lexeme}";
        _currentScope.AddSymbol(new SymbolInfo(stmt.Name.Lexeme, SymbolKind.Variable, stmt.Name.Span, stmt.Span, detail, typeHint: typeStr, isExplicitTypeHint: typeStr != null));
        stmt.Initializer?.Accept(this);
        return null;
    }

    /// <summary>
    /// Registers a <see cref="SymbolKind.Constant"/> symbol for a <c>const</c> declaration,
    /// capturing an explicit type annotation when present, then recurses into the initializer.
    /// </summary>
    /// <param name="stmt">The constant declaration statement.</param>
    /// <returns>Always <see langword="null"/>.</returns>
    public object? VisitConstDeclStmt(ConstDeclStmt stmt)
    {
        var typeStr = stmt.TypeHint?.Lexeme;
        var detail = typeStr != null ? $"const {stmt.Name.Lexeme}: {typeStr}" : $"const {stmt.Name.Lexeme}";
        _currentScope.AddSymbol(new SymbolInfo(stmt.Name.Lexeme, SymbolKind.Constant, stmt.Name.Span, stmt.Span, detail, typeHint: typeStr, isExplicitTypeHint: typeStr != null));
        stmt.Initializer.Accept(this);
        return null;
    }

    /// <summary>
    /// Registers one <see cref="SymbolKind.Variable"/> or <see cref="SymbolKind.Constant"/>
    /// symbol for each name in a destructuring declaration (e.g. <c>let { a, b } = expr</c>),
    /// then recurses into the initializer.
    /// </summary>
    /// <param name="stmt">The destructuring declaration statement.</param>
    /// <returns>Always <see langword="null"/>.</returns>
    public object? VisitDestructureStmt(DestructureStmt stmt)
    {
        foreach (var name in stmt.Names)
        {
            var symbolKind = stmt.IsConst ? SymbolKind.Constant : SymbolKind.Variable;
            var detail = stmt.IsConst ? $"const {name.Lexeme}" : $"let {name.Lexeme}";
            _currentScope.AddSymbol(new SymbolInfo(name.Lexeme, symbolKind, name.Span, stmt.Span, detail));
        }
        stmt.Initializer.Accept(this);
        return null;
    }

    /// <summary>
    /// Opens a <see cref="ScopeKind.Block"/> scope, visits all nested statements, then closes it.
    /// If this block is the target of a pending <c>is</c>-expression narrowing, a type
    /// narrowing entry is added to the scope so that completion and hover resolve the
    /// narrowed type without affecting symbol references.
    /// </summary>
    /// <param name="stmt">The block statement.</param>
    /// <returns>Always <see langword="null"/>.</returns>
    public object? VisitBlockStmt(BlockStmt stmt)
    {
        PushScope(ScopeKind.Block, stmt.Span);

        // Apply type narrowing only to the exact block that was the then-branch target
        if (_pendingNarrowing is { } narrowing && ReferenceEquals(stmt, narrowing.Target))
        {
            _pendingNarrowing = null;
            _currentScope.AddTypeNarrowing(narrowing.Name, narrowing.TypeHint);
        }

        foreach (var s in stmt.Statements)
        {
            s.Accept(this);
        }

        PopScope();
        return null;
    }

    /// <summary>
    /// Recurses into the condition expression and both branches of an <c>if</c> statement.
    /// When the condition is an <c>is</c> expression (e.g. <c>x is Error</c>), sets
    /// <see cref="_pendingNarrowing"/> so the then-branch scope receives a type narrowing
    /// with the narrowed type hint.
    /// </summary>
    /// <param name="stmt">The if statement.</param>
    /// <returns>Always <see langword="null"/>.</returns>
    public object? VisitIfStmt(IfStmt stmt)
    {
        stmt.Condition.Accept(this);

        // Type narrowing: if condition is `x is T` and then-branch is a block,
        // mark the block as the narrowing target so it receives the type override.
        if (stmt.ThenBranch is BlockStmt thenBlock)
        {
            var narrowing = ExtractIsNarrowing(stmt.Condition);
            if (narrowing != null)
            {
                _pendingNarrowing = (narrowing.Value.Name, narrowing.Value.TypeHint, thenBlock);
            }
        }

        stmt.ThenBranch.Accept(this);
        _pendingNarrowing = null;

        stmt.ElseBranch?.Accept(this);
        return null;
    }

    /// <summary>
    /// If the condition is an <c>is</c> expression with an identifier on the left
    /// (e.g. <c>x is Error</c>), extracts the variable name and target type name.
    /// </summary>
    private static (string Name, string TypeHint)? ExtractIsNarrowing(Expr condition)
    {
        if (condition is IsExpr isExpr && isExpr.Left is IdentifierExpr ident)
        {
            return (ident.Name.Lexeme, isExpr.TypeName.Lexeme);
        }
        return null;
    }

    /// <summary>
    /// Recurses into the condition, opens a <see cref="ScopeKind.Loop"/> scope for the body,
    /// visits all body statements, then closes the scope.
    /// </summary>
    /// <param name="stmt">The while statement.</param>
    /// <returns>Always <see langword="null"/>.</returns>
    public object? VisitWhileStmt(WhileStmt stmt)
    {
        stmt.Condition.Accept(this);
        PushScope(ScopeKind.Loop, stmt.Body.Span);
        foreach (var s in stmt.Body.Statements)
        {
            s.Accept(this);
        }

        PopScope();
        return null;
    }

    /// <summary>
    /// Opens a <see cref="ScopeKind.Loop"/> scope for the body, visits all body statements,
    /// closes the scope, then recurses into the post-body condition expression.
    /// </summary>
    /// <param name="stmt">The do-while statement.</param>
    /// <returns>Always <see langword="null"/>.</returns>
    public object? VisitDoWhileStmt(DoWhileStmt stmt)
    {
        PushScope(ScopeKind.Loop, stmt.Body.Span);
        foreach (var s in stmt.Body.Statements)
        {
            s.Accept(this);
        }
        PopScope();
        stmt.Condition.Accept(this);
        return null;
    }

    /// <summary>
    /// Recurses into the iterable expression, opens a <see cref="ScopeKind.Loop"/> scope,
    /// registers the optional index <see cref="SymbolKind.LoopVariable"/> and the iteration
    /// variable (with optional type annotation), visits all body statements, then closes the scope.
    /// </summary>
    /// <param name="stmt">The for-in statement.</param>
    /// <returns>Always <see langword="null"/>.</returns>
    public object? VisitForInStmt(ForInStmt stmt)
    {
        stmt.Iterable.Accept(this);
        PushScope(ScopeKind.Loop, stmt.Body.Span);

        // Register index variable if present (for-in with index: for (let i, item in ...))
        if (stmt.IndexName is not null)
        {
            _currentScope.AddSymbol(new SymbolInfo(stmt.IndexName.Lexeme, SymbolKind.LoopVariable, stmt.IndexName.Span, detail: "loop index", typeHint: "int"));
        }

        var typeStr = stmt.TypeHint?.Lexeme;
        var detail = typeStr != null ? $"loop variable: {typeStr}" : "loop variable";
        _currentScope.AddSymbol(new SymbolInfo(stmt.VariableName.Lexeme, SymbolKind.LoopVariable, stmt.VariableName.Span, detail: detail, typeHint: typeStr, isExplicitTypeHint: typeStr != null));
        foreach (var s in stmt.Body.Statements)
        {
            s.Accept(this);
        }

        PopScope();
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

    /// <summary>Recurses into the optional return value expression.</summary>
    /// <param name="stmt">The return statement.</param>
    /// <returns>Always <see langword="null"/>.</returns>
    public object? VisitReturnStmt(ReturnStmt stmt)
    {
        stmt.Value?.Accept(this);
        return null;
    }

    /// <summary>No-op — <c>throw</c> introduces no symbols; the value is walked for nested declarations.</summary>
    /// <returns>Always <see langword="null"/>.</returns>
    public object? VisitThrowStmt(ThrowStmt stmt)
    {
        stmt.Value.Accept(this);
        return null;
    }

    /// <summary>No-op — <c>break</c> introduces no symbols.</summary>
    /// <returns>Always <see langword="null"/>.</returns>
    public object? VisitBreakStmt(BreakStmt stmt) => null;

    /// <summary>No-op — <c>continue</c> introduces no symbols.</summary>
    /// <returns>Always <see langword="null"/>.</returns>
    public object? VisitContinueStmt(ContinueStmt stmt) => null;

    /// <summary>
    /// Registers a placeholder <see cref="SymbolKind.Variable"/> symbol for each imported name.
    /// These placeholder symbols are later replaced by fully-resolved <see cref="SymbolInfo"/>
    /// instances from <see cref="ImportResolver"/> during the <see cref="AnalysisEngine"/> pass.
    /// </summary>
    /// <param name="stmt">The selective import statement (<c>import { … } from "…"</c>).</param>
    /// <returns>Always <see langword="null"/>.</returns>
    public object? VisitImportStmt(ImportStmt stmt)
    {
        foreach (var name in stmt.Names)
        {
            _currentScope.AddSymbol(new SymbolInfo(name.Lexeme, SymbolKind.Variable, name.Span, detail: $"imported from {stmt.Path.Lexeme}"));
        }
        return null;
    }

    /// <summary>
    /// Registers a <see cref="SymbolKind.Namespace"/> symbol for a namespace import alias
    /// (<c>import "…" as alias</c>), enabling dot-completion on the alias.
    /// </summary>
    /// <param name="stmt">The namespace import statement.</param>
    /// <returns>Always <see langword="null"/>.</returns>
    public object? VisitImportAsStmt(ImportAsStmt stmt)
    {
        _currentScope.AddSymbol(new SymbolInfo(stmt.Alias.Lexeme, SymbolKind.Namespace, stmt.Alias.Span, detail: $"namespace from {stmt.Path.Lexeme}"));
        return null;
    }

    // Expression visitors — just recurse, we don't collect declarations from expressions

    /// <summary>No-op — literals introduce no symbols or references.</summary>
    /// <returns>Always <see langword="null"/>.</returns>
    public object? VisitLiteralExpr(LiteralExpr expr) => null;

    /// <summary>
    /// Records a <see cref="ReferenceKind.Read"/> reference for the identifier.
    /// </summary>
    /// <param name="expr">The identifier expression.</param>
    /// <returns>Always <see langword="null"/>.</returns>
    public object? VisitIdentifierExpr(IdentifierExpr expr)
    {
        RecordReference(expr.Name.Lexeme, expr.Span, ReferenceKind.Read);
        return null;
    }

    /// <summary>Recurses into the operand of a unary expression.</summary>
    /// <returns>Always <see langword="null"/>.</returns>
    public object? VisitUnaryExpr(UnaryExpr expr)
    {
        expr.Right.Accept(this);
        return null;
    }

    /// <summary>
    /// Records a <see cref="ReferenceKind.Write"/> reference when the operand is a plain identifier
    /// (<c>x++</c> / <c>x--</c>), otherwise recurses normally.
    /// </summary>
    /// <returns>Always <see langword="null"/>.</returns>
    public object? VisitUpdateExpr(UpdateExpr expr)
    {
        if (expr.Operand is IdentifierExpr id)
        {
            RecordReference(id.Name.Lexeme, id.Span, ReferenceKind.Write);
        }
        else
        {
            expr.Operand.Accept(this);
        }
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

    /// <summary>
    /// Records a <see cref="ReferenceKind.Write"/> reference for the assignment target and
    /// recurses into the right-hand side value.
    /// </summary>
    /// <returns>Always <see langword="null"/>.</returns>
    public object? VisitAssignExpr(AssignExpr expr)
    {
        RecordReference(expr.Name.Lexeme, expr.Name.Span, ReferenceKind.Write);
        expr.Value.Accept(this);
        return null;
    }

    /// <summary>
    /// Records a <see cref="ReferenceKind.Call"/> reference when the callee is a plain identifier,
    /// otherwise recurses into the callee expression, then recurses into all arguments.
    /// </summary>
    /// <returns>Always <see langword="null"/>.</returns>
    public object? VisitCallExpr(CallExpr expr)
    {
        if (expr.Callee is IdentifierExpr id)
        {
            RecordReference(id.Name.Lexeme, id.Span, ReferenceKind.Call);
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

    /// <summary>Recurses into all elements of an array literal.</summary>
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

    /// <summary>
    /// Records a <see cref="ReferenceKind.TypeUse"/> reference for the struct name and
    /// recurses into all field-value expressions of the struct initializer.
    /// </summary>
    /// <returns>Always <see langword="null"/>.</returns>
    public object? VisitStructInitExpr(StructInitExpr expr)
    {
        RecordReference(expr.Name.Lexeme, expr.Name.Span, ReferenceKind.TypeUse);
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

    /// <summary>Recurses into the object and the right-hand side value of a dot-assignment expression.</summary>
    /// <returns>Always <see langword="null"/>.</returns>
    public object? VisitDotAssignExpr(DotAssignExpr expr)
    {
        expr.Object.Accept(this);
        expr.Value.Accept(this);
        return null;
    }

    /// <summary>Recurses into each interpolation part of an interpolated string expression.</summary>
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

    /// <summary>Recurses into both sides of a pipe expression (<c>left | right</c>).</summary>
    /// <returns>Always <see langword="null"/>.</returns>
    public object? VisitPipeExpr(PipeExpr expr)
    {
        expr.Left.Accept(this);
        expr.Right.Accept(this);
        return null;
    }

    /// <summary>Recurses into the source expression and the redirect target of a redirect expression.</summary>
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

    /// <summary>Recurses into both sides of a null-coalescing expression (<c>left ?? right</c>).</summary>
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
    /// Opens a <see cref="ScopeKind.Function"/> scope for the lambda, registers each
    /// parameter (with optional type annotation) and recurses into default values and the body
    /// (either an expression body or a block body).
    /// </summary>
    /// <returns>Always <see langword="null"/>.</returns>
    public object? VisitLambdaExpr(LambdaExpr expr)
    {
        var bodySpan = expr.ExpressionBody?.Span ?? expr.BlockBody!.Span;
        PushScope(ScopeKind.Function, bodySpan);

        for (int i = 0; i < expr.Parameters.Count; i++)
        {
            var param = expr.Parameters[i];
            var paramType = i < expr.ParameterTypes.Count ? expr.ParameterTypes[i]?.Lexeme : null;
            var paramDetail = paramType != null ? $"parameter: {paramType}" : "parameter";
            _currentScope.AddSymbol(new SymbolInfo(param.Lexeme, SymbolKind.Parameter, param.Span, detail: paramDetail, parentName: "<lambda>", typeHint: paramType));
        }

        foreach (var defaultVal in expr.DefaultValues)
        {
            defaultVal?.Accept(this);
        }

        if (expr.ExpressionBody != null)
        {
            expr.ExpressionBody.Accept(this);
        }
        else if (expr.BlockBody != null)
        {
            foreach (var s in expr.BlockBody.Statements)
            {
                s.Accept(this);
            }
        }

        PopScope();
        return null;
    }
}
