namespace Stash.Parsing.AST;

using System.Collections.Generic;
using Stash.Common;
using Stash.Lexing;

/// <summary>
/// A function declaration: <c>fn name(params) { body }</c>
/// </summary>
/// <remarks>
/// Functions are first-class values in Stash. A function declaration introduces a named binding in the
/// current scope. Parameters may have optional type hints (not enforced at runtime) and optional default
/// values. An optional return type hint may also be present.
/// </remarks>
public class FnDeclStmt : Stmt
{
    /// <summary>Gets the function name token.</summary>
    public Token Name { get; }
    /// <summary>Gets the list of parameter name tokens.</summary>
    public List<Token> Parameters { get; }
    /// <summary>Gets the list of optional type hint tokens for each parameter. Each entry is <c>null</c> if no type hint was provided.</summary>
    public List<Token?> ParameterTypes { get; }
    /// <summary>Gets the list of optional default value expressions for each parameter. Each entry is <c>null</c> if no default was provided.</summary>
    public List<Expr?> DefaultValues { get; }
    /// <summary>Gets the optional return type hint token. <c>null</c> if no return type was annotated.</summary>
    public Token? ReturnType { get; }
    /// <summary>Gets whether this function was declared with the <c>async</c> keyword.</summary>
    public bool IsAsync { get; }
    /// <summary>Gets the <c>async</c> keyword token, or <c>null</c> if not async.</summary>
    public Token? AsyncKeyword { get; }
    /// <summary>Gets the function body block.</summary>
    public BlockStmt Body { get; }
    /// <summary>Gets whether the last parameter is a rest parameter (<c>...name</c>).</summary>
    public bool HasRestParam { get; }
    /// <summary>The number of local variable slots needed in this function's scope, set by the Resolver.</summary>
    public int ResolvedLocalCount { get; set; }

    /// <summary>Initializes a new instance of <see cref="FnDeclStmt"/>.</summary>
    /// <param name="name">The function name token.</param>
    /// <param name="parameters">The list of parameter name tokens.</param>
    /// <param name="parameterTypes">The list of optional type hint tokens for each parameter.</param>
    /// <param name="defaultValues">The list of optional default value expressions for each parameter.</param>
    /// <param name="returnType">The optional return type hint token, or <c>null</c>.</param>
    /// <param name="body">The function body block.</param>
    /// <param name="span">The source location of this declaration.</param>
    /// <param name="isAsync">Whether this function was declared with the <c>async</c> keyword.</param>
    /// <param name="asyncKeyword">The <c>async</c> keyword token, or <c>null</c>.</param>
    public FnDeclStmt(Token name, List<Token> parameters, List<Token?> parameterTypes, List<Expr?> defaultValues, Token? returnType, BlockStmt body, SourceSpan span, bool isAsync = false, Token? asyncKeyword = null, bool hasRestParam = false) : base(span, StmtType.FnDecl)
    {
        Name = name;
        Parameters = parameters;
        ParameterTypes = parameterTypes;
        DefaultValues = defaultValues;
        ReturnType = returnType;
        Body = body;
        IsAsync = isAsync;
        AsyncKeyword = asyncKeyword;
        HasRestParam = hasRestParam;
    }

    /// <inheritdoc />
    public override T Accept<T>(IStmtVisitor<T> visitor) => visitor.VisitFnDeclStmt(this);
}
