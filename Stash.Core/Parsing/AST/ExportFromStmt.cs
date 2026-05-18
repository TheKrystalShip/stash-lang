namespace Stash.Parsing.AST;

using System.Collections.Generic;
using Stash.Common;
using Stash.Lexing;

/// <summary>
/// A selective named re-export statement: <c>export { name1, name2 } from path-expr;</c>
/// </summary>
/// <remarks>
/// <para>
/// Combines an import and an export in a single declaration. The names listed in <see cref="Names"/>
/// are imported from the module at <see cref="Path"/> and bound as locals in the current module's scope.
/// Each name is additionally added to the current module's <c>ModuleExports.Names</c>.
/// </para>
/// <code>
/// export { Color, Size, Direction } from "lib/types.stash";
/// export { encode, decode } from path_fn();
/// export { } from "lib/x.stash";   // SA0812 — empty list (caught by analyzer in Phase 2C)
/// </code>
/// Validation of source-module membership (SA0809), empty list (SA0812), and other semantic rules
/// is performed by the semantic analyzer, not the parser. The parser produces this node for all
/// syntactically valid <c>export { … } from expr;</c> forms, including an empty name list.
/// </para>
/// </remarks>
public sealed class ExportFromStmt : Stmt
{
    /// <summary>The <c>export</c> soft-keyword token.</summary>
    public Token ExportKeyword { get; }

    /// <summary>
    /// The list of identifier tokens naming the symbols to re-export.
    /// May be empty; SA0812 is raised by the analyzer (Phase 2C) when empty.
    /// </summary>
    public List<Token> Names { get; }

    /// <summary>The contextual <c>from</c> identifier token.</summary>
    public Token FromKeyword { get; }

    /// <summary>
    /// The expression that evaluates to the source module file path.
    /// Any expression accepted by <c>import { … } from expr;</c> is accepted here.
    /// </summary>
    public Expr Path { get; }

    /// <summary>
    /// Creates a new selective re-export node.
    /// </summary>
    /// <param name="exportKeyword">The <c>export</c> keyword token.</param>
    /// <param name="names">The list of identifier tokens to re-export.</param>
    /// <param name="fromKeyword">The contextual <c>from</c> identifier token.</param>
    /// <param name="path">The source module path expression.</param>
    /// <param name="span">The source span covering the entire statement.</param>
    public ExportFromStmt(Token exportKeyword, List<Token> names, Token fromKeyword, Expr path, SourceSpan span)
        : base(span, StmtType.ExportFrom)
    {
        ExportKeyword = exportKeyword;
        Names = names;
        FromKeyword = fromKeyword;
        Path = path;
    }

    /// <inheritdoc />
    public override T Accept<T>(IStmtVisitor<T> visitor) => visitor.VisitExportFromStmt(this);
}
