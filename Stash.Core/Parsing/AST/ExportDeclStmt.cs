namespace Stash.Parsing.AST;

using Stash.Common;
using Stash.Lexing;

/// <summary>
/// A declaration-site export annotation: <c>export fn/const/struct/enum/interface …</c>
/// </summary>
/// <remarks>
/// <para>
/// Wraps one inner declaration. <see cref="Inner"/> is guaranteed at construction time to be one
/// of <see cref="FnDeclStmt"/>, <see cref="ConstDeclStmt"/>, <see cref="StructDeclStmt"/>,
/// <see cref="EnumDeclStmt"/>, or <see cref="InterfaceDeclStmt"/>.
/// </para>
/// <code>
/// export fn diff(a, b) { ... }
/// export async fn fetch(url) { ... }
/// export const VERSION: str = "1.0.0";
/// export struct Point { x: int, y: int }
/// export enum Status { Ok, Err }
/// export interface Closer { fn close() }
/// </code>
/// </remarks>
public sealed class ExportDeclStmt : Stmt
{
    /// <summary>The <c>export</c> soft-keyword token.</summary>
    public Token ExportKeyword { get; }

    /// <summary>
    /// The wrapped declaration. One of <see cref="FnDeclStmt"/>, <see cref="ConstDeclStmt"/>,
    /// <see cref="StructDeclStmt"/>, <see cref="EnumDeclStmt"/>, or <see cref="InterfaceDeclStmt"/>.
    /// </summary>
    public Stmt Inner { get; }

    /// <summary>
    /// Creates a new export-declaration node.
    /// </summary>
    /// <param name="exportKeyword">The <c>export</c> keyword token.</param>
    /// <param name="inner">The wrapped declaration statement.</param>
    /// <param name="span">The source span covering the entire export declaration.</param>
    /// <exception cref="System.ArgumentException">
    /// Thrown when <paramref name="inner"/> is not a supported declaration type.
    /// </exception>
    public ExportDeclStmt(Token exportKeyword, Stmt inner, SourceSpan span)
        : base(span, StmtType.ExportDecl)
    {
        if (inner is not (FnDeclStmt or ConstDeclStmt or StructDeclStmt or EnumDeclStmt or InterfaceDeclStmt))
        {
            throw new System.ArgumentException(
                $"ExportDeclStmt.Inner must be a function, const, struct, enum, or interface declaration; got {inner.GetType().Name}.",
                nameof(inner));
        }

        ExportKeyword = exportKeyword;
        Inner = inner;
    }

    /// <inheritdoc />
    public override T Accept<T>(IStmtVisitor<T> visitor) => visitor.VisitExportDeclStmt(this);
}
