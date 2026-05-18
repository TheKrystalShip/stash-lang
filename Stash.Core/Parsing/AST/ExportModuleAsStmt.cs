namespace Stash.Parsing.AST;

using Stash.Common;
using Stash.Lexing;

/// <summary>
/// A namespace re-export statement: <c>export path-expr as alias;</c>
/// </summary>
/// <remarks>
/// <para>
/// Combines an import and an export in a single declaration. The module at <see cref="Path"/>
/// is imported and bound under <see cref="Alias"/> in the current module's scope, and <see cref="Alias"/>
/// is additionally added to the current module's <c>ModuleExports.Names</c>.
/// </para>
/// <code>
/// export "lib/data.stash" as data;
/// export path_fn() as ns;
/// </code>
/// The alias is both usable as a local namespace binding in the same file and re-exported to
/// downstream importers. Validation of name collisions and source-module membership is performed
/// by the semantic analyzer (SA0824), not the parser.
/// </remarks>
public sealed class ExportModuleAsStmt : Stmt
{
    /// <summary>The <c>export</c> soft-keyword token.</summary>
    public Token ExportKeyword { get; }

    /// <summary>
    /// The expression that evaluates to the module file path.
    /// Any expression accepted by <c>import expr as name;</c> is accepted here.
    /// </summary>
    public Expr Path { get; }

    /// <summary>The <c>as</c> keyword token.</summary>
    public Token AsKeyword { get; }

    /// <summary>
    /// The identifier token used as both the local namespace binding and the exported name.
    /// </summary>
    public Token Alias { get; }

    /// <summary>
    /// Creates a new namespace re-export node.
    /// </summary>
    /// <param name="exportKeyword">The <c>export</c> keyword token.</param>
    /// <param name="path">The module path expression.</param>
    /// <param name="asKeyword">The <c>as</c> keyword token.</param>
    /// <param name="alias">The alias identifier token.</param>
    /// <param name="span">The source span covering the entire statement.</param>
    public ExportModuleAsStmt(Token exportKeyword, Expr path, Token asKeyword, Token alias, SourceSpan span)
        : base(span, StmtType.ExportModuleAs)
    {
        ExportKeyword = exportKeyword;
        Path = path;
        AsKeyword = asKeyword;
        Alias = alias;
    }

    /// <inheritdoc />
    public override T Accept<T>(IStmtVisitor<T> visitor) => visitor.VisitExportModuleAsStmt(this);
}
