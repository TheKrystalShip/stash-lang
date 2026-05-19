namespace Stash.Parsing.AST;

using System.Collections.Generic;
using Stash.Common;
using Stash.Lexing;

/// <summary>
/// A block-form export statement: <c>export { name1, name2, … };</c>
/// </summary>
/// <remarks>
/// <code>
/// export { diff, VERSION };
/// export { };   // valid — Names ends up empty, same as a module with no export annotations
/// </code>
/// Each identifier in <see cref="Names"/> must resolve to a top-level <c>fn</c>, <c>const</c>,
/// <c>struct</c>, <c>enum</c>, or <c>interface</c> declaration in the same file. Validation of
/// name resolution and allowed declaration kinds is performed by the semantic validator (not the parser).
/// </remarks>
public sealed class ExportBlockStmt : Stmt
{
    /// <summary>The <c>export</c> soft-keyword token.</summary>
    public Token ExportKeyword { get; }

    /// <summary>
    /// The list of identifier tokens naming the symbols to export.
    /// May be empty (an empty export block is syntactically valid).
    /// </summary>
    public List<Token> Names { get; }

    /// <summary>
    /// Creates a new export-block node.
    /// </summary>
    /// <param name="exportKeyword">The <c>export</c> keyword token.</param>
    /// <param name="names">The list of identifier tokens to export.</param>
    /// <param name="span">The source span covering the entire export block statement.</param>
    public ExportBlockStmt(Token exportKeyword, List<Token> names, SourceSpan span)
        : base(span, StmtType.ExportBlock)
    {
        ExportKeyword = exportKeyword;
        Names = names;
    }

    /// <inheritdoc />
    public override T Accept<T>(IStmtVisitor<T> visitor) => visitor.VisitExportBlockStmt(this);
}
