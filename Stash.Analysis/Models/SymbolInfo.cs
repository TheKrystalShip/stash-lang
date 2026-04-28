namespace Stash.Analysis;

using System;
using Stash.Common;

/// <summary>
/// Classifies what kind of declaration a <see cref="SymbolInfo"/> represents,
/// driving how LSP handlers present and filter symbols.
/// </summary>
public enum SymbolKind
{
    /// <summary>A mutable variable declared with <c>let</c>.</summary>
    Variable,
    /// <summary>An immutable binding declared with <c>const</c>.</summary>
    Constant,
    /// <summary>A top-level or module-level function declared with <c>fn</c>.</summary>
    Function,
    /// <summary>A named parameter of a function or method.</summary>
    Parameter,
    /// <summary>A user-defined struct type declared with <c>struct</c>.</summary>
    Struct,
    /// <summary>A user-defined enum type declared with <c>enum</c>.</summary>
    Enum,
    /// <summary>A member value of an enum.</summary>
    EnumMember,
    /// <summary>A named field of a struct.</summary>
    Field,
    /// <summary>A method defined inside a struct body.</summary>
    Method,
    /// <summary>The iteration variable bound by a <c>for-in</c> loop, including the optional index variable.</summary>
    LoopVariable,
    /// <summary>A module alias introduced by <c>import "…" as alias</c>, or a built-in namespace (e.g. <c>http</c>, <c>fs</c>).</summary>
    Namespace,
    /// <summary>A user-defined interface type declared with <c>interface</c>.</summary>
    Interface
}

/// <summary>
/// Carries the full metadata for a single symbol declaration in a Stash document,
/// including its name, kind, source location, type, and documentation.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="SymbolInfo"/> instances are produced by <see cref="SymbolCollector"/> during AST
/// traversal and stored in <see cref="Scope"/> objects. After collection, additional passes
/// (<see cref="TypeInferenceEngine"/>, <see cref="DocCommentResolver"/>,
/// <see cref="ImportResolver"/>) may mutate <see cref="TypeHint"/> and
/// <see cref="Documentation"/> in place.
/// </para>
/// <para>
/// Consumers such as the completion, hover, go-to-definition, and rename handlers use these
/// properties to build LSP responses. The <see cref="SourceUri"/> property enables
/// cross-file navigation when a symbol originates from an imported module.
/// </para>
/// </remarks>
public class SymbolInfo
{
    /// <summary>Gets the unqualified identifier as it appears in source (e.g. <c>"greet"</c>).</summary>
    public string Name { get; }

    /// <summary>Gets the declaration category of this symbol (variable, function, struct, …).</summary>
    public SymbolKind Kind { get; }

    /// <summary>
    /// Gets the source span of the symbol's <em>name token</em> — used for rename edits
    /// and as the definition location returned by go-to-definition.
    /// </summary>
    public SourceSpan Span { get; }

    /// <summary>
    /// Gets the source span of the <em>entire declaration</em> (from keyword to closing brace),
    /// or <see langword="null"/> for built-in symbols and simple name-only declarations.
    /// Used by <see cref="ScopeTree.GetHierarchicalSymbols"/> to match a function symbol to its body scope.
    /// </summary>
    public SourceSpan? FullSpan { get; }

    /// <summary>
    /// Gets the human-readable signature string shown in completion and hover tooltips
    /// (e.g. <c>"fn greet(name: string) -&gt; string"</c>, <c>"struct Point { x: float, y: float }"</c>).
    /// </summary>
    public string? Detail { get; }

    /// <summary>
    /// Gets the name of the enclosing type for members — the struct name for fields and methods,
    /// the enum name for enum members, the function name for parameters, or <c>"&lt;lambda&gt;"</c>
    /// for lambda parameters. <see langword="null"/> for top-level symbols.
    /// </summary>
    public string? ParentName { get; }

    /// <summary>
    /// Gets or sets the inferred or explicitly annotated type of this symbol
    /// (e.g. <c>"string"</c>, <c>"int"</c>, <c>"Point"</c>).
    /// Set during collection from explicit type annotations, then potentially refined
    /// by <see cref="TypeInferenceEngine"/> for variables without annotations.
    /// </summary>
    public string? TypeHint { get; set; }

    /// <summary>
    /// Gets the URI of the file where this symbol is defined, or <see langword="null"/> when
    /// the symbol belongs to the current document. Set by <see cref="ImportResolver"/> for
    /// symbols resolved from imported modules to enable cross-file navigation.
    /// </summary>
    public Uri? SourceUri { get; }

    /// <summary>
    /// Gets the ordered parameter names for <see cref="SymbolKind.Function"/> and
    /// <see cref="SymbolKind.Method"/> symbols, used by <see cref="SemanticValidator"/>
    /// to name arguments in arity-mismatch diagnostics.
    /// <see langword="null"/> for non-callable symbols.
    /// </summary>
    public string[]? ParameterNames { get; }

    /// <summary>
    /// Gets the minimum number of arguments required when calling this function or method.
    /// Parameters with default values do not count toward the required count.
    /// <see langword="null"/> when arity information is unavailable.
    /// </summary>
    public int? RequiredParameterCount { get; }

    /// <summary>
    /// Gets the declared type annotation for each parameter, in declaration order.
    /// A <see langword="null"/> element means the parameter has no type annotation.
    /// Used by <see cref="SemanticValidator"/> to emit argument type mismatch warnings.
    /// </summary>
    public string?[]? ParameterTypes { get; }

    /// <summary>
    /// Gets whether <see cref="TypeHint"/> was written explicitly in source (e.g. <c>let x: string</c>)
    /// rather than inferred. When <see langword="true"/>, <see cref="SemanticValidator"/> enforces
    /// assignment type compatibility against it.
    /// </summary>
    public bool IsExplicitTypeHint { get; }

    /// <summary>
    /// Gets whether this callable accepts an unlimited number of arguments via a rest parameter
    /// (<c>...rest</c>). When <see langword="true"/>, the upper-bound arity check is skipped.
    /// </summary>
    public bool IsVariadic { get; }

    /// <summary>
    /// Gets whether this is an async function or method, declared with the <c>async</c> keyword.
    /// Always <see langword="false"/> for non-callable symbols.
    /// </summary>
    public bool IsAsync { get; }

    /// <summary>
    /// Gets or sets the documentation text extracted from a preceding <c>///</c> or
    /// <c>/** */</c> comment by <see cref="DocCommentResolver"/>.
    /// Shown in hover tooltips and completion item documentation.
    /// </summary>
    public string? Documentation { get; set; }

    /// <summary>
    /// Initializes a new <see cref="SymbolInfo"/> with the given metadata.
    /// </summary>
    /// <param name="name">The unqualified identifier name.</param>
    /// <param name="kind">The declaration category.</param>
    /// <param name="span">The source span of the name token.</param>
    /// <param name="fullSpan">The source span of the full declaration, or <see langword="null"/>.</param>
    /// <param name="detail">Human-readable signature string, or <see langword="null"/>.</param>
    /// <param name="parentName">The enclosing type name for members, or <see langword="null"/>.</param>
    /// <param name="typeHint">The type annotation or inferred type, or <see langword="null"/>.</param>
    /// <param name="sourceUri">The URI of the defining file for imported symbols, or <see langword="null"/>.</param>
    /// <param name="parameterNames">Ordered parameter names for callables, or <see langword="null"/>.</param>
    /// <param name="requiredParameterCount">Minimum required argument count, or <see langword="null"/>.</param>
    /// <param name="parameterTypes">Per-parameter type annotations, or <see langword="null"/>.</param>
    /// <param name="isExplicitTypeHint"><see langword="true"/> if <paramref name="typeHint"/> was written explicitly in source.</param>
    /// <param name="isVariadic"><see langword="true"/> if this callable accepts unlimited arguments via a rest parameter.</param>
    /// <param name="isAsync"><see langword="true"/> if this function or method was declared with the <c>async</c> keyword.</param>
    public SymbolInfo(string name, SymbolKind kind, SourceSpan span, SourceSpan? fullSpan = null, string? detail = null, string? parentName = null, string? typeHint = null, Uri? sourceUri = null, string[]? parameterNames = null, int? requiredParameterCount = null, string?[]? parameterTypes = null, bool isExplicitTypeHint = false, bool isVariadic = false, bool isAsync = false)
    {
        Name = name;
        Kind = kind;
        Span = span;
        FullSpan = fullSpan;
        Detail = detail;
        ParentName = parentName;
        TypeHint = typeHint;
        SourceUri = sourceUri;
        ParameterNames = parameterNames;
        RequiredParameterCount = requiredParameterCount;
        ParameterTypes = parameterTypes;
        IsExplicitTypeHint = isExplicitTypeHint;
        IsVariadic = isVariadic;
        IsAsync = isAsync;
    }
}
